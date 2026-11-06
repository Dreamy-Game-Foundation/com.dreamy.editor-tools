using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace Dreamy.EditorTools.Package
{
    internal sealed class PackageChange
    {
        public string Id;
        public string Version;
        public string Revision;
        public string Value;
        public string Previous;
        public string Reason;
        public string Source;
    }

    internal sealed class PackageChangePlan
    {
        public string ManifestBefore;
        public string LockBefore;
        public List<PackageChange> Changes = new();
        public List<string> Errors = new();
        public bool CanApply => Changes.Count > 0 && Errors.Count == 0;
        public string Summary => string.Join("\n", Changes.Select(c => $"{c.Id}: {c.Previous ?? "not direct"} → {c.Value}\n  {c.Reason}")) +
                                 (Errors.Count == 0 ? "" : "\n\nBlocked:\n" + string.Join("\n", Errors));
    }

    internal static class PackageChangePlanner
    {
        internal static async Task<PackageChangePlan> Build(CatalogEntry root, PackageRelease target,
            IReadOnlyList<CatalogEntry> catalog, Dictionary<string, InstalledPackage> installed,
            Func<CatalogEntry, string, Task<PackageRelease>> findRelease, string unityVersion, bool allowSourceChange = false)
        {
            var plan = new PackageChangePlan
            {
                ManifestBefore = File.ReadAllText("Packages/manifest.json"),
                LockBefore = File.Exists("Packages/packages-lock.json") ? File.ReadAllText("Packages/packages-lock.json") : null
            };
            var selected = new Dictionary<string, PackageRelease>();
            var entries = catalog.ToDictionary(p => p.Id);
            var visiting = new HashSet<string>();
            try { await Visit(root, target, "Selected package", true); }
            catch (Exception e) { plan.Errors.Add(e.Message); }
            // All installed dependents must accept the target; do not silently break a different feature.
            foreach (var change in plan.Changes)
                foreach (var owner in installed.Values.Where(p => !selected.ContainsKey(p.Id)))
                    if (owner.Dependencies.TryGetValue(change.Id, out var minimum) && PackageVersion.TryParse(minimum, out _) &&
                        !PackageVersion.Meets(change.Version, minimum))
                        plan.Errors.Add($"{owner.Id} requires {change.Id} >= {minimum}.");
            return plan;

            async Task Visit(CatalogEntry entry, PackageRelease release, string reason, bool isRoot)
            {
                if (!release.Verified || release.Dependencies == null) throw new InvalidOperationException($"{entry.Id}: release metadata is not verified.");
                if (!release.EditorCompatible || !PackageVersion.UnityCompatible(release.Unity, unityVersion)) throw new InvalidOperationException($"{entry.Id} requires Unity {release.Unity}.");
                if (!visiting.Add(entry.Id)) throw new InvalidOperationException($"Dependency cycle at {entry.Id}.");
                if (selected.TryGetValue(entry.Id, out var previous))
                {
                    visiting.Remove(entry.Id);
                    if (previous.Version != release.Version) throw new InvalidOperationException($"Conflicting selections for {entry.Id}; resolve dependencies first.");
                    return;
                }
                installed.TryGetValue(entry.Id, out var current);
                if (current != null)
                {
                    if (current.IsLocal && !(isRoot && allowSourceChange)) throw new InvalidOperationException($"{entry.Id} is local/embedded. Update its development checkout separately or explicitly switch source.");
                    if (entry.Source == "git" && current.Source == "Git" && !string.IsNullOrEmpty(current.ManifestValue) &&
                        !SameGitSource(current.ManifestValue, entry) && !(isRoot && allowSourceChange))
                        throw new InvalidOperationException($"{entry.Id} uses a different Git repository/path. Use Switch Source explicitly.");
                    if ((entry.Source == "git" && current.Source == "Registry" || entry.Source == "registry" && current.Source == "Git") && !(isRoot && allowSourceChange))
                        throw new InvalidOperationException($"{entry.Id}: source differs from catalog. Use Switch Source explicitly.");
                }
                selected.Add(entry.Id, release);
                foreach (var dependency in release.Dependencies)
                {
                    if (selected.TryGetValue(dependency.Key, out var chosen))
                    {
                        if (visiting.Contains(dependency.Key)) throw new InvalidOperationException($"Dependency cycle at {dependency.Key}.");
                        if (!PackageVersion.Meets(chosen.Version, dependency.Value)) throw new InvalidOperationException($"{entry.Id} needs {dependency.Key} >= {dependency.Value}; selected {chosen.Version}.");
                        continue;
                    }
                    if (installed.TryGetValue(dependency.Key, out var available) && PackageVersion.Meets(available.Version, dependency.Value)) continue;
                    if (!PackageVersion.TryParse(dependency.Value, out _))
                        throw new InvalidOperationException($"{entry.Id}: unsupported dependency constraint {dependency.Key} {dependency.Value}; configure it in Project Packages first.");
                    if (entries.TryGetValue(dependency.Key, out var mapped))
                        await Visit(mapped, await findRelease(mapped, dependency.Value), $"Required by {entry.Id} (>= {dependency.Value})", false);
                    else if (dependency.Key.StartsWith("com.unity.", StringComparison.Ordinal))
                    {
                        if (available != null && available.IsLocal) throw new InvalidOperationException($"{dependency.Key} is local and below required version.");
                        var registryEntry = new CatalogEntry { Id = dependency.Key, Source = "registry" };
                        var registryRelease = await findRelease(registryEntry, dependency.Value);
                        await Visit(registryEntry, registryRelease, $"Required by {entry.Id}", false);
                    }
                    else throw new InvalidOperationException($"No source mapping for {dependency.Key} >= {dependency.Value}. Add a catalog entry or install it first.");
                }
                plan.Changes.Add(new PackageChange
                {
                    Id = entry.Id, Version = release.Version, Revision = release.Revision, Source = entry.Source,
                    Value = entry.Source == "git" ? entry.GitUrl(release.Revision) : release.Version,
                    Previous = current?.ManifestValue, Reason = reason + (current != null && !current.Direct ? "; becomes a direct dependency" : "") +
                        (current != null && PackageVersion.TryParse(current.Version, out var before) && PackageVersion.TryParse(release.Version, out var after) && after.CompareTo(before) < 0 ? "; DOWNGRADE from " + current.Version : "")
                });
                visiting.Remove(entry.Id);
            }
        }

        internal static bool SameGitSource(string value, CatalogEntry entry)
        {
            var noRevision = value.Split('#')[0];
            var expected = entry.GitUrl("revision").Split('#')[0];
            return string.Equals(noRevision, expected, StringComparison.OrdinalIgnoreCase);
        }

        internal static string[] Dependents(string id, Dictionary<string, InstalledPackage> installed) =>
            installed.Values.Where(p => p.Id != id && p.Dependencies.ContainsKey(id)).Select(p => p.Id).OrderBy(s => s).ToArray();

        internal static bool ManifestMatchesIntent(string before, string current, IEnumerable<PackageChange> changes, string removed)
        {
            var expected = JObject.Parse(before);
            var deps = expected["dependencies"] as JObject;
            if (deps == null) return false;
            foreach (var change in changes) deps[change.Id] = change.Value;
            if (removed != null) deps.Remove(removed);
            return JToken.DeepEquals(expected, JObject.Parse(current));
        }
    }
}
