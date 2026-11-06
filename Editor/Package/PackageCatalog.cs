using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using UnityEditor.PackageManager;

namespace Dreamy.EditorTools.Package
{
    [Serializable]
    internal sealed class CatalogEntry
    {
        public string Id;
        public string DisplayName;
        public string Category = "Foundation";
        public string Source = "git";
        public string Repository;
        public string PackagePath = "";
        public string Documentation;
        public string Description;

        public string GitUrl(string revision) => Repository.TrimEnd('/') + ".git" +
            (string.IsNullOrEmpty(PackagePath) ? "" : "?path=/" + PackagePath.Trim('/')) + "#" + revision;
    }

    [Serializable]
    internal sealed class CatalogDocument
    {
        public int SchemaVersion = 1;
        public List<CatalogEntry> Packages = new();
    }

    internal static class PackageCatalog
    {
        internal const string ProjectPath = "ProjectSettings/DreamyPackageCatalog.json";
        internal const string CachePath = "Library/DreamyPackageManager";

        internal static CatalogDocument Parse(string json)
        {
            var root = Newtonsoft.Json.Linq.JObject.Parse(json);
            if (root.GetValue("schemaVersion", StringComparison.OrdinalIgnoreCase)?.ToObject<int>() != 1 ||
                root.GetValue("packages", StringComparison.OrdinalIgnoreCase) is not Newtonsoft.Json.Linq.JArray)
                throw new InvalidDataException("Catalog must contain schemaVersion: 1 and packages array.");
            var doc = JsonConvert.DeserializeObject<CatalogDocument>(json);
            if (doc == null || doc.SchemaVersion != 1 || doc.Packages == null)
                throw new InvalidDataException("Catalog must contain schemaVersion: 1 and packages.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var p in doc.Packages)
            {
                if (p == null || !Regex.IsMatch(p.Id ?? "", @"^[a-z0-9]+(\.[a-z0-9][a-z0-9-]*)+$") || !ids.Add(p.Id))
                    throw new InvalidDataException("Catalog has an invalid or duplicate package ID.");
                if (p.Source != "git" && p.Source != "registry")
                    throw new InvalidDataException($"{p.Id}: source must be git or registry.");
                if (p.Source == "git")
                {
                    if (!Uri.TryCreate(p.Repository, UriKind.Absolute, out var uri) || uri.Scheme != "https" ||
                        uri.Host != "github.com" || uri.Segments.Length != 3 || !string.IsNullOrEmpty(uri.Query) ||
                        !string.IsNullOrEmpty(uri.Fragment) || !string.IsNullOrEmpty(uri.UserInfo))
                        throw new InvalidDataException($"{p.Id}: use https://github.com/owner/repository (no .git, query, credentials).");
                    p.Repository = p.Repository.TrimEnd('/');
                    if (p.Repository.EndsWith(".git", StringComparison.Ordinal)) p.Repository = p.Repository[..^4];
                    if ((p.PackagePath ?? "").Contains("..") || (p.PackagePath ?? "").Any(c => !(char.IsLetterOrDigit(c) || c == '/' || c == '-' || c == '_' || c == '.')))
                        throw new InvalidDataException($"{p.Id}: invalid packagePath.");
                }
                p.DisplayName = string.IsNullOrWhiteSpace(p.DisplayName) ? p.Id : p.DisplayName;
            }
            return doc;
        }

        internal static List<CatalogEntry> Merge(params CatalogDocument[] documents)
        {
            var entries = new Dictionary<string, CatalogEntry>(StringComparer.Ordinal);
            foreach (var doc in documents.Where(d => d != null))
                foreach (var entry in doc.Packages) entries[entry.Id] = entry;
            return entries.Values.OrderBy(p => p.Category).ThenBy(p => p.DisplayName).ToList();
        }

        internal static List<CatalogEntry> Load()
        {
            var info = PackageInfo.FindForAssembly(typeof(PackageCatalog).Assembly);
            if (info == null) throw new InvalidOperationException("Cannot locate Dreamy Editor Tools package.");
            var bundled = Parse(File.ReadAllText(Path.Combine(info.resolvedPath, "Editor/Package/DreamyPackageCatalog.json")));
            var remotePath = Path.Combine(CachePath, "remote-catalog.json");
            var remote = File.Exists(remotePath) ? Parse(File.ReadAllText(remotePath)) : null;
            var project = File.Exists(ProjectPath) ? Parse(File.ReadAllText(ProjectPath)) : null;
            return Merge(bundled, remote, project);
        }

        internal static void AddProjectEntry(CatalogEntry entry)
        {
            var doc = File.Exists(ProjectPath) ? Parse(File.ReadAllText(ProjectPath)) : new CatalogDocument();
            doc.Packages.RemoveAll(p => p.Id == entry.Id);
            doc.Packages.Add(entry);
            var json = JsonConvert.SerializeObject(doc, Formatting.Indented);
            Parse(json);
            Directory.CreateDirectory(Path.GetDirectoryName(ProjectPath));
            File.WriteAllText(ProjectPath, json);
        }
    }

    internal sealed class InstalledPackage
    {
        public string Id;
        public string Version;
        public string Source;
        public string ManifestValue;
        public string ResolvedPath;
        public string Revision;
        public bool Direct;
        public Dictionary<string, string> Dependencies = new();
        public bool IsLocal => Source == "Local" || Source == "Embedded" || Source == "LocalTarball";

        internal static Dictionary<string, InstalledPackage> Read()
        {
            var manifest = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText("Packages/manifest.json"))["dependencies"];
            return PackageInfo.GetAllRegisteredPackages().ToDictionary(p => p.name, p => new InstalledPackage
            {
                Id = p.name, Version = p.version, Source = p.source.ToString(), Direct = p.isDirectDependency,
                ManifestValue = (string)manifest?[p.name], ResolvedPath = p.resolvedPath,
                Revision = p.git?.hash,
                Dependencies = p.dependencies.ToDictionary(d => d.name, d => d.version)
            });
        }
    }
}
