using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Dreamy.EditorTools.Package
{
    public sealed class DreamyPackageToolsWindow : EditorWindow
    {
        [SerializeField] private int tab;
        [SerializeField] private int filter;
        [SerializeField] private string search = "";
        [SerializeField] private bool previews;
        [SerializeField] private string selectedId;
        private List<CatalogEntry> catalog = new();
        private Dictionary<string, InstalledPackage> installed = new();
        private List<InstalledPackage> unresolved = new();
        private PackageReleases releases;
        private CancellationTokenSource lifetime;
        private Vector2 scroll, detailScroll;
        private string packageInput = "";
        private string message = "";
        private string selectedTag;
        private bool working;
        private bool config;
        private string remoteUrl = "";
        private string newId = "com.dreamy.";
        private string newRepository = "https://github.com/Dreamy-Game-Foundation/";
        private string newPath = "";
        private string newCategory = "Foundation";
        private string newSource = "git";
        private string PreferenceKey => "Dreamy.PackageCatalog." + Application.dataPath;
        private bool MutationsBlocked => working || PackageOperations.Busy || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode;

        [MenuItem("Tools/Dreamy/Package/Package Manager")]
        public static void Open()
        {
            var window = GetWindow<DreamyPackageToolsWindow>();
            window.titleContent = new GUIContent("Dreamy Packages");
            window.minSize = new Vector2(840, 540);
            window.Show();
        }

        private void OnEnable()
        {
            lifetime = new CancellationTokenSource();
            releases = new PackageReleases();
            remoteUrl = EditorPrefs.GetString(PreferenceKey, "");
            PackageOperations.Changed += RefreshInstalled;
            ReloadCatalog();
            RefreshInstalled();
        }
        private void OnDisable()
        {
            PackageOperations.Changed -= RefreshInstalled;
            lifetime?.Cancel();
            releases?.Dispose();
        }
        private void OnInspectorUpdate() => Repaint();
        private void RefreshInstalled()
        {
            try
            {
                installed = InstalledPackage.Read();
                var direct = JObject.Parse(File.ReadAllText("Packages/manifest.json"))["dependencies"] as JObject;
                unresolved = direct?.Properties().Where(p => !installed.ContainsKey(p.Name)).Select(p => new InstalledPackage
                { Id = p.Name, Version = "Unresolved", Source = "Unresolved", ManifestValue = (string)p.Value, Direct = true }).ToList() ?? new List<InstalledPackage>();
            }
            catch (Exception e) { message = e.Message; }
            Repaint();
        }
        private void ReloadCatalog()
        {
            try
            {
                catalog = PackageCatalog.Load();
                releases.LoadCache(catalog);
                message = $"{catalog.Count} catalog entries. Check Updates to query release tags.";
            }
            catch (Exception e) { message = "Catalog error: " + e.Message + " Fix the project catalog and reload."; }
        }

        private void OnGUI()
        {
            tab = GUILayout.Toolbar(tab, new[] { "Dreamy Catalog", "Project Packages" });
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                search = GUILayout.TextField(search, EditorStyles.toolbarSearchField, GUILayout.MinWidth(120));
                if (GUILayout.Button("Refresh Installed", EditorStyles.toolbarButton)) RefreshInstalled();
                using (new EditorGUI.DisabledScope(working || PackageOperations.Busy))
                    if (GUILayout.Button(working ? "Checking…" : "Check Updates", EditorStyles.toolbarButton)) Run(CheckAll);
                config = GUILayout.Toggle(config, "Catalog Settings", EditorStyles.toolbarButton);
                if (GUILayout.Button("Export", EditorStyles.toolbarButton)) Export();
            }
            if (config) DrawSettings();
            if (tab == 0) DrawCatalog(); else DrawProject();
            if (!string.IsNullOrEmpty(message)) EditorGUILayout.HelpBox(message, MessageType.None);
            EditorGUILayout.HelpBox(PackageOperations.Status ?? "Ready", MessageType.None);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(MutationsBlocked))
                {
                    if (GUILayout.Button("Resolve")) Guard(PackageOperations.Resolve);
                    using (new EditorGUI.DisabledScope(!PackageOperations.CanRestore))
                        if (GUILayout.Button("Restore Last Package Change") && EditorUtility.DisplayDialog("Restore Packages", "Restore the manifest/lock snapshot from the last package change and resolve? This does not restore other project files.", "Restore", "Cancel")) Guard(PackageOperations.Restore);
                }
                if (GUILayout.Button("manifest.json")) OpenFile("Packages/manifest.json");
                if (GUILayout.Button("packages-lock.json")) OpenFile("Packages/packages-lock.json");
            }
        }

        private void DrawCatalog()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                filter = EditorGUILayout.Popup(filter, new[] { "All", "Installed", "Not installed", "Updates", "Issues" }, GUILayout.Width(160));
                previews = EditorGUILayout.ToggleLeft("Include prerelease", previews, GUILayout.Width(160));
                GUILayout.FlexibleSpace();
                GUILayout.Label($"{installed.Values.Count(p => p.Id.StartsWith("com.dreamy.", StringComparison.Ordinal))} Dreamy installed", EditorStyles.miniLabel);
            }
            using (var view = new EditorGUILayout.ScrollViewScope(scroll, GUILayout.MinHeight(120), GUILayout.ExpandHeight(true)))
            {
                scroll = view.scrollPosition;
                foreach (var entry in VisibleEntries()) DrawRow(entry);
            }
            var selected = VisibleEntries(false).FirstOrDefault(p => p.Id == selectedId);
            if (selected != null) DrawDetails(selected);
        }

        private IEnumerable<CatalogEntry> VisibleEntries(bool filtered = true)
        {
            var known = new HashSet<string>(catalog.Select(p => p.Id));
            var all = catalog.Concat(installed.Values.Where(p => p.Id.StartsWith("com.dreamy.", StringComparison.Ordinal) && !known.Contains(p.Id))
                .Select(p => new CatalogEntry { Id = p.Id, DisplayName = p.Id, Category = "Uncatalogued", Source = "unknown" }));
            foreach (var entry in all)
            {
                if (!filtered) { yield return entry; continue; }
                installed.TryGetValue(entry.Id, out var current);
                releases.Indices.TryGetValue(entry.Id, out var index);
                if (!string.IsNullOrWhiteSpace(search) && (entry.Id + " " + entry.DisplayName + " " + entry.Category).IndexOf(search, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (filter == 1 && current == null || filter == 2 && current != null || filter == 3 && !UpdateAvailable(current, index?.Latest(previews)) ||
                    filter == 4 && !(entry.Source == "unknown" || index == null || !string.IsNullOrEmpty(index.Error) || index.Releases.Any(r => r.InvalidReason != null) || Status(current, index).Contains("Incompatible"))) continue;
                yield return entry;
            }
        }

        private void DrawRow(CatalogEntry entry)
        {
            installed.TryGetValue(entry.Id, out var current);
            releases.Indices.TryGetValue(entry.Id, out var index);
            var latest = index?.Latest(previews);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Toggle(selectedId == entry.Id, entry.DisplayName, EditorStyles.boldLabel, GUILayout.MinWidth(190)))
                    {
                        if (selectedId != entry.Id) selectedTag = null;
                        selectedId = entry.Id;
                    }
                    GUILayout.Label("Installed: " + (current?.Version ?? "—"), GUILayout.Width(135));
                    GUILayout.Label("Latest: " + (latest?.Version ?? "Unknown"), GUILayout.Width(130));
                    using (new EditorGUI.DisabledScope(MutationsBlocked || entry.Source == "unknown"))
                    {
                        if (current?.IsLocal == true)
                        {
                            if (GUILayout.Button("Open Folder", GUILayout.Width(105))) EditorUtility.RevealInFinder(current.ResolvedPath);
                        }
                        else if (latest != null && (current == null || UpdateAvailable(current, latest)))
                        {
                            using (new EditorGUI.DisabledScope((!latest.EditorCompatible || !PackageVersion.UnityCompatible(latest.Unity, Application.unityVersion)) || !string.IsNullOrEmpty(index.Error)))
                                if (GUILayout.Button(current == null ? "Install" : "Update", GUILayout.Width(105))) Run(() => Prepare(entry, latest, false));
                        }
                        else if (GUILayout.Button("Check", GUILayout.Width(105))) Run(() => CheckOne(entry));
                    }
                    if (GUILayout.Button("Details", GUILayout.Width(65))) { if (selectedId != entry.Id) selectedTag = null; selectedId = entry.Id; }
                }
                GUILayout.Label(entry.Id + "  ·  " + entry.Category + "  ·  " + (current == null ? "Not installed" : current.Source + (current.Direct ? " / Direct" : " / Transitive")) + "  ·  " + Status(current, index), EditorStyles.wordWrappedMiniLabel);
            }
        }

        private string Status(InstalledPackage current, PackageReleaseIndex index)
        {
            if (index == null) return current?.IsLocal == true ? "Local development · release unknown" : "Not checked";
            string suffix = index.FromCache ? " · cached " + index.CheckedAt : " · checked " + index.CheckedAt;
            if (!string.IsNullOrEmpty(index.Error)) return "Error: " + index.Error + suffix;
            var latest = index.Latest(previews);
            if (latest == null) return "No verified release" + suffix;
            if (current?.IsLocal == true) return "Local development" + suffix;
            if ((!latest.EditorCompatible || !PackageVersion.UnityCompatible(latest.Unity, Application.unityVersion))) return (string.IsNullOrEmpty(latest.Unity) ? "Incompatible with this Editor" : "Incompatible Unity (requires " + latest.Unity + ")") + suffix;
            if (current == null) return "Available" + suffix;
            if (!PackageVersion.TryParse(current.Version, out _)) return "Unknown installed version" + suffix;
            return (UpdateAvailable(current, latest) ? "Update available" : PackageVersion.Meets(latest.Version, current.Version) ? "Up to date" : "Installed newer than release") + suffix;
        }
        private static bool UpdateAvailable(InstalledPackage current, PackageRelease latest) => current != null && latest != null &&
            PackageVersion.TryParse(current.Version, out var a) && PackageVersion.TryParse(latest.Version, out var b) && b.CompareTo(a) > 0;

        private void DrawDetails(CatalogEntry entry)
        {
            using var box = new EditorGUILayout.VerticalScope(EditorStyles.helpBox);
            using var view = new EditorGUILayout.ScrollViewScope(detailScroll, GUILayout.Height(205));
            detailScroll = view.scrollPosition;
            GUILayout.Label(entry.DisplayName, EditorStyles.boldLabel);
            if (!string.IsNullOrEmpty(entry.Description)) GUILayout.Label(entry.Description, EditorStyles.wordWrappedMiniLabel);
            installed.TryGetValue(entry.Id, out var current);
            if (current != null)
            {
                EditorGUILayout.SelectableLabel("Source: " + (current.ManifestValue ?? current.Source) + "\nResolved revision: " + (current.Revision ?? "—"), GUILayout.Height(35));
                var dependents = PackageChangePlanner.Dependents(entry.Id, installed);
                GUILayout.Label("Required by: " + (dependents.Length == 0 ? "None" : string.Join(", ", dependents)), EditorStyles.wordWrappedMiniLabel);
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (!string.IsNullOrEmpty(entry.Repository) && GUILayout.Button("Repository")) Application.OpenURL(entry.Repository);
                if (!string.IsNullOrEmpty(entry.Repository) && GUILayout.Button("Releases / Tags")) Application.OpenURL(entry.Repository + "/tags");
                if (!string.IsNullOrEmpty(entry.Repository) && GUILayout.Button("Changelog")) Application.OpenURL(entry.Repository + "/blob/HEAD/CHANGELOG.md");
                if (!string.IsNullOrEmpty(entry.Documentation) && GUILayout.Button("Documentation")) OpenHttps(entry.Documentation);
                using (new EditorGUI.DisabledScope(working || PackageOperations.Busy || entry.Source == "unknown"))
                    if (GUILayout.Button("Check / Retry")) Run(() => CheckOne(entry));
            }
            if (!releases.Indices.TryGetValue(entry.Id, out var index) || index.Releases.Count == 0) return;
            var available = index.Releases.Where(r => PackageVersion.TryParse(r.Version, out var v) && (previews || !v.IsPreview)).ToList();
            if (available.Count == 0) return;
            int choice = Math.Max(0, available.FindIndex(r => r.Tag == selectedTag));
            choice = EditorGUILayout.Popup("Version", choice, available.Select(r => r.Version + (r.InvalidReason != null ? " (invalid tag)" : "")).ToArray());
            var target = available[choice]; selectedTag = target.Tag;
            GUILayout.Label("Latest stable: " + (index.Latest(false)?.Version ?? "Unknown") + "  ·  Selected version compatibility: " +
                (target.Verified && target.EditorCompatible && PackageVersion.UnityCompatible(target.Unity, Application.unityVersion) ? "Unity metadata compatible; dependency preview required" : "Unknown / incompatible"), EditorStyles.wordWrappedMiniLabel);
            if (target.Verified) GUILayout.Label("Minimum Unity: " + (target.Unity ?? "Not declared") + "  ·  Dependencies: " + string.Join(", ", target.Dependencies.Select(d => d.Key + " " + d.Value)), EditorStyles.wordWrappedMiniLabel);
            if (target.InvalidReason != null) EditorGUILayout.HelpBox(target.InvalidReason, MessageType.Warning);
            using (new EditorGUILayout.HorizontalScope())
            using (new EditorGUI.DisabledScope(MutationsBlocked || !string.IsNullOrEmpty(index.Error) || target.InvalidReason != null))
            {
                using (new EditorGUI.DisabledScope(current?.IsLocal == true))
                    if (GUILayout.Button(current == null ? "Preview Install" : "Preview Selected Version")) Run(() => Prepare(entry, target, false));
                if (current != null && GUILayout.Button("Switch Source to Catalog…")) Run(() => Prepare(entry, target, true));
            }
        }

        private void DrawProject()
        {
            using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
            {
                GUILayout.Label("Package ID / Git URL", GUILayout.Width(140));
                packageInput = EditorGUILayout.TextField(packageInput);
                using (new EditorGUI.DisabledScope(MutationsBlocked || string.IsNullOrWhiteSpace(packageInput)))
                    if (GUILayout.Button("Add", GUILayout.Width(65))) Guard(() => PackageOperations.Add(packageInput.Trim()));
            }
            using var view = new EditorGUILayout.ScrollViewScope(scroll);
            scroll = view.scrollPosition;
            foreach (var p in installed.Values.Concat(unresolved).OrderBy(p => p.Id))
            {
                if ((p.Id + " " + p.Version).IndexOf(search ?? "", StringComparison.OrdinalIgnoreCase) < 0) continue;
                using (new EditorGUILayout.HorizontalScope(EditorStyles.helpBox))
                {
                    GUILayout.Label(p.Id, GUILayout.MinWidth(240));
                    GUILayout.Label(p.Version + " · " + p.Source + (p.Direct ? " / Direct" : " / Transitive"), GUILayout.MinWidth(200));
                    if (p.Source == "Unresolved") GUILayout.Label(new GUIContent("Manifest entry", p.ManifestValue), EditorStyles.miniLabel, GUILayout.Width(85));
                    using (new EditorGUI.DisabledScope(MutationsBlocked || !p.Direct || PackageChangePlanner.Dependents(p.Id, installed).Length > 0))
                        if (GUILayout.Button("Remove", GUILayout.Width(70)) && EditorUtility.DisplayDialog("Remove Package", "Remove " + p.Id + "?", "Remove", "Cancel")) Guard(() => PackageOperations.Remove(p.Id));
                }
            }
        }

        private void DrawSettings()
        {
            using var disabled = new EditorGUI.DisabledScope(working || PackageOperations.Busy);
            using var box = new EditorGUILayout.VerticalScope(EditorStyles.helpBox);
            GUILayout.Label("Extensible catalog", EditorStyles.boldLabel);
            GUILayout.Label("Bundled → remote → project overrides. New entries do not require C# changes.", EditorStyles.miniLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                remoteUrl = EditorGUILayout.TextField("Remote catalog URL", remoteUrl);
                using (new EditorGUI.DisabledScope(working || PackageOperations.Busy || string.IsNullOrWhiteSpace(remoteUrl)))
                    if (GUILayout.Button("Fetch Catalog", GUILayout.Width(110))) Run(FetchCatalog);
                if (GUILayout.Button("Clear Remote", GUILayout.Width(100))) Guard(() =>
                {
                    var path = Path.Combine(PackageCatalog.CachePath, "remote-catalog.json");
                    if (File.Exists(path)) File.Delete(path);
                    remoteUrl = ""; EditorPrefs.DeleteKey(PreferenceKey); ReloadCatalog();
                });
            }
            newId = EditorGUILayout.TextField("Package ID", newId);
            newSource = EditorGUILayout.Popup("Source", newSource == "git" ? 0 : 1, new[] { "GitHub Git", "Configured UPM registry" }) == 0 ? "git" : "registry";
            if (newSource == "git")
            {
                newRepository = EditorGUILayout.TextField("Repository URL", newRepository);
                newPath = EditorGUILayout.TextField("UPM subfolder (optional)", newPath);
            }
            newCategory = EditorGUILayout.TextField("Category", newCategory);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(working || PackageOperations.Busy))
                    if (GUILayout.Button("Add / Override Project Entry")) Guard(() =>
                    {
                        PackageCatalog.AddProjectEntry(new CatalogEntry { Id = newId.Trim(), DisplayName = newId.Trim(), Category = newCategory, Repository = newRepository.Trim(), PackagePath = newPath.Trim(), Source = newSource });
                        ReloadCatalog();
                    });
                if (GUILayout.Button("Open Project Catalog")) Guard(() =>
                {
                    if (!File.Exists(PackageCatalog.ProjectPath)) File.WriteAllText(PackageCatalog.ProjectPath, JsonConvert.SerializeObject(new CatalogDocument(), Formatting.Indented));
                    OpenFile(PackageCatalog.ProjectPath);
                });
                if (GUILayout.Button("Reload Catalog")) ReloadCatalog();
            }
        }

        private async Task FetchCatalog()
        {
            var json = await releases.Get(remoteUrl, lifetime.Token);
            PackageCatalog.Parse(json);
            lifetime.Token.ThrowIfCancellationRequested();
            Directory.CreateDirectory(PackageCatalog.CachePath);
            File.WriteAllText(Path.Combine(PackageCatalog.CachePath, "remote-catalog.json"), json);
            EditorPrefs.SetString(PreferenceKey, remoteUrl);
            ReloadCatalog();
        }
        private async Task CheckOne(CatalogEntry entry)
        {
            message = "Checking " + entry.Id + "…";
            var result = await releases.Check(entry, true);
            message = string.IsNullOrEmpty(result.Error) ? "Checked " + entry.Id : entry.Id + ": " + result.Error;
        }
        private async Task CheckAll()
        {
            int completed = 0;
            await Task.WhenAll(catalog.Select(async entry =>
            {
                await releases.Check(entry, true);
                completed++;
                message = $"Checked {completed}/{catalog.Count} package sources…";
                Repaint();
            }));
            message = $"Checked {catalog.Count} catalog entries. {releases.Indices.Values.Count(i => !string.IsNullOrEmpty(i.Error))} source errors; use Details → Retry.";
        }
        private async Task Prepare(CatalogEntry entry, PackageRelease target, bool switchSource)
        {
            await releases.Verify(entry, target);
            var plan = await PackageChangePlanner.Build(entry, target, catalog, InstalledPackage.Read(),
                (p, minimum) => releases.Find(p, minimum, previews), Application.unityVersion, switchSource);
            lifetime.Token.ThrowIfCancellationRequested();
            PackagePlanWindow.ShowPlan(plan);
        }
        private async void Run(Func<Task> action)
        {
            if (working) return;
            working = true;
            try { await action(); }
            catch (OperationCanceledException) { if (this) message = "Metadata lookup cancelled."; }
            catch (Exception e) { if (this) message = e.Message; }
            finally { working = false; if (this) Repaint(); }
        }
        private void Guard(Action action)
        {
            try { action(); } catch (Exception e) { message = e.Message; }
        }
        private void Export() => Guard(() =>
        {
            var path = EditorUtility.SaveFilePanel("Export Dreamy Package Report", "", "dreamy-packages.json", "json");
            if (string.IsNullOrEmpty(path)) return;
            File.WriteAllText(path, JsonConvert.SerializeObject(new { GeneratedAt = DateTime.UtcNow.ToString("O"), Unity = Application.unityVersion,
                Catalog = catalog, Installed = installed.Values, Releases = releases.Indices.Values }, Formatting.Indented));
            message = "Report saved: " + path;
        });
        private static void OpenHttps(string url)
        {
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https" && string.IsNullOrEmpty(uri.UserInfo)) Application.OpenURL(url);
        }
        private static void OpenFile(string path) => UnityEditorInternal.InternalEditorUtility.OpenFileAtLineExternal(path, 1);
    }

    internal sealed class PackagePlanWindow : EditorWindow
    {
        private PackageChangePlan plan;
        private Vector2 scroll;
        private string error;
        internal static void ShowPlan(PackageChangePlan value)
        {
            var window = CreateInstance<PackagePlanWindow>();
            window.plan = value;
            window.titleContent = new GUIContent("Package Change Preview");
            window.minSize = new Vector2(640, 350);
            window.ShowUtility();
        }
        private void OnGUI()
        {
            if (plan == null) { EditorGUILayout.HelpBox("Preview expired after reload. Build a new preview.", MessageType.Info); return; }
            EditorGUILayout.HelpBox("Review the package sources, pinned revisions and dependency changes. A manifest/lock snapshot will be saved before Apply.", MessageType.Info);
            using (var view = new EditorGUILayout.ScrollViewScope(scroll))
            {
                scroll = view.scrollPosition;
                EditorGUILayout.TextArea(plan.Summary, GUILayout.ExpandHeight(true));
            }
            if (error != null) EditorGUILayout.HelpBox(error, MessageType.Error);
            using (new EditorGUI.DisabledScope(!plan.CanApply || PackageOperations.Busy || EditorApplication.isCompiling || EditorApplication.isPlayingOrWillChangePlaymode))
                if (GUILayout.Button("Apply Package Changes"))
                {
                    try { PackageOperations.Apply(plan); Close(); }
                    catch (Exception e) { error = e.Message; }
                }
        }
    }
}
