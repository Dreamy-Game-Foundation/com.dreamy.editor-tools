using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;

namespace Dreamy.EditorTools.Package
{
    internal sealed class PackageOperationJournal
    {
        public bool Pending;
        public string StartedAt;
        public string ManifestBefore;
        public string LockBefore;
        public string ManifestAfter;
        public string LockAfter;
        public string Removed;
        public List<PackageChange> Changes = new();
        public string Message;
    }

    [InitializeOnLoad]
    internal static class PackageOperations
    {
        private const string JournalPath = "Library/DreamyPackageManager/operation.json";
        private static Request mutation;
        private static ListRequest listing;
        private static PackageOperationJournal journal;
        private static double waitUntil;
        internal static bool Busy => mutation != null || listing != null || journal?.Pending == true;
        internal static string Status { get; private set; } = "Ready.";
        internal static bool CanRestore => !Busy && journal?.ManifestAfter != null &&
            File.ReadAllText("Packages/manifest.json") == journal.ManifestAfter && ReadLock() == journal.LockAfter;
        internal static event Action Changed;

        static PackageOperations()
        {
            try
            {
                if (File.Exists(JournalPath)) journal = JsonConvert.DeserializeObject<PackageOperationJournal>(File.ReadAllText(JournalPath));
                if (journal?.Pending == true)
                {
                    Status = "Reconciling package operation after reload; the request will not be repeated.";
                    waitUntil = EditorApplication.timeSinceStartup + 2;
                }
                else if (journal != null) Status = journal.Message;
            }
            catch (Exception) { Status = "Operation history unreadable; inspect manifest/lock before changing packages."; }
            EditorApplication.update += Poll;
        }

        internal static void Apply(PackageChangePlan plan)
        {
            if (!plan.CanApply) throw new InvalidOperationException("The package plan is blocked.");
            if (File.ReadAllText("Packages/manifest.json") != plan.ManifestBefore || ReadLock() != plan.LockBefore)
                throw new InvalidOperationException("Manifest/lock changed after preview. Rebuild the plan.");
            Start(plan.Changes, null, () => Client.AddAndRemove(plan.Changes.Select(c => c.Source == "git" ? c.Value : c.Id + "@" + c.Value).ToArray(), Array.Empty<string>(), false));
        }

        internal static void Add(string value) => Start(new List<PackageChange>(), null, () => Client.Add(value));
        internal static void Remove(string id)
        {
            var dependents = PackageChangePlanner.Dependents(id, InstalledPackage.Read());
            if (dependents.Length > 0) throw new InvalidOperationException("Required by: " + string.Join(", ", dependents));
            Start(new List<PackageChange>(), id, () => Client.Remove(id));
        }
        internal static void Resolve() => Start(new List<PackageChange>(), null, () => { Client.Resolve(); return null; });

        private static void Start(List<PackageChange> changes, string removed, Func<Request> begin)
        {
            if (Busy || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Wait for the package operation/compilation to finish and exit Play Mode.");
            journal = new PackageOperationJournal
            {
                Pending = true, StartedAt = DateTime.UtcNow.ToString("O"), Changes = changes, Removed = removed,
                ManifestBefore = File.ReadAllText("Packages/manifest.json"), LockBefore = ReadLock(), Message = "Applying package operation..."
            };
            Save();
            Status = journal.Message;
            waitUntil = EditorApplication.timeSinceStartup + 2;
            try { mutation = begin(); }
            catch (Exception e) { Finish("Package operation could not start: " + e.Message, false); }
            Changed?.Invoke();
        }

        private static void Poll()
        {
            if (journal?.Pending != true) return;
            if (EditorApplication.isCompiling || EditorApplication.isUpdating || EditorApplication.timeSinceStartup < waitUntil) return;
            try
            {
                if (mutation != null)
                {
                    if (!mutation.IsCompleted) return;
                    if (mutation.Status != StatusCode.Success)
                    { Finish("UPM failed: " + (mutation.Error?.message ?? "Unknown error") + ". Inspect manifest/lock; changes may have been applied.", false); return; }
                    mutation = null;
                }
                if (listing == null) { listing = Client.List(true, true); return; }
                if (!listing.IsCompleted) return;
                if (listing.Status != StatusCode.Success)
                { Finish("Cannot verify resolved packages: " + listing.Error?.message, false); return; }
                var actual = listing.Result.ToDictionary(p => p.name);
                var errors = new List<string>();
                foreach (var change in journal.Changes)
                {
                    if (!actual.TryGetValue(change.Id, out var p) || p.version != change.Version ||
                        (change.Source == "git" && (p.source != PackageSource.Git || !string.Equals(p.git?.hash, change.Revision, StringComparison.OrdinalIgnoreCase))))
                        errors.Add(change.Id + ": resolved version/source/revision differs from preview");
                }
                if (journal.Removed != null && actual.ContainsKey(journal.Removed)) errors.Add(journal.Removed + " is still installed");
                Finish(errors.Count == 0 ? "UPM completed and resolved packages verified. Check Console for compilation results." :
                    "Verification failed: " + string.Join("; ", errors) + ". The operation was not repeated.", errors.Count == 0);
            }
            catch (Exception e) { Finish("Package verification failed: " + e.Message, false); }
        }

        private static void Finish(string message, bool success)
        {
            mutation = null; listing = null;
            journal.Pending = false;
            journal.Message = Status = message;
            var current = File.ReadAllText("Packages/manifest.json");
            // Only offer restore when the manifest matches the recorded intent exactly. Generic Add has unknown intent.
            if ((journal.Changes.Count > 0 || journal.Removed != null) && PackageChangePlanner.ManifestMatchesIntent(journal.ManifestBefore, current, journal.Changes, journal.Removed))
            { journal.ManifestAfter = current; journal.LockAfter = ReadLock(); }
            Save();
            Changed?.Invoke();
        }

        internal static void Restore()
        {
            if (!CanRestore) throw new InvalidOperationException("Cannot restore: manifest/lock changed since the operation, or no recoverable snapshot exists.");
            var manifest = journal.ManifestBefore;
            var lockFile = journal.LockBefore;
            // Persist before resolving, which may reload this assembly.
            journal.Pending = false;
            journal.ManifestAfter = null; journal.LockAfter = null;
            journal.Message = "Restored manifest/lock snapshot; resolving packages.";
            Save();
            File.WriteAllText("Packages/manifest.json", manifest);
            if (lockFile == null) { if (File.Exists("Packages/packages-lock.json")) File.Delete("Packages/packages-lock.json"); }
            else File.WriteAllText("Packages/packages-lock.json", lockFile);
            Resolve();
        }

        private static string ReadLock() => File.Exists("Packages/packages-lock.json") ? File.ReadAllText("Packages/packages-lock.json") : null;
        private static void Save()
        {
            Directory.CreateDirectory(PackageCatalog.CachePath);
            var temp = JournalPath + ".tmp";
            File.WriteAllText(temp, JsonConvert.SerializeObject(journal, Formatting.Indented));
            File.Copy(temp, JournalPath, true);
            File.Delete(temp);
        }
    }
}
