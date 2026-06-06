using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;

namespace Dreamy.EditorTools.Package
{
    public sealed class DreamyPackageToolsWindow : EditorWindow
    {
        private const string WindowTitle = "Dreamy Packages";
        private const string ManifestPath = "Packages/manifest.json";
        private const string LockPath = "Packages/packages-lock.json";

        private readonly List<PackageEntry> packages = new();
        private Vector2 scroll;
        private string searchText = string.Empty;
        private string packageInput = string.Empty;
        private AddRequest addRequest;
        private RemoveRequest removeRequest;
        private string statusMessage = string.Empty;

        [MenuItem("Tools/Dreamy/Package/Package Manager")]
        public static void Open()
        {
            DreamyPackageToolsWindow window =
                GetWindow<DreamyPackageToolsWindow>();
            window.titleContent = new GUIContent(WindowTitle);
            window.minSize = new Vector2(700f, 420f);
            window.Show();
        }

        private void OnEnable()
        {
            RefreshManifest();
        }

        private void OnInspectorUpdate()
        {
            if (CheckRequest(addRequest, "Package added.") ||
                CheckRequest(removeRequest, "Package removed."))
            {
                addRequest = null;
                removeRequest = null;
                RefreshManifest();
                Repaint();
            }
        }

        private void OnGUI()
        {
            DrawToolbar();
            DrawAddPackage();

            scroll = EditorGUILayout.BeginScrollView(scroll);
            foreach (PackageEntry package in packages)
            {
                if (MatchesSearch(package))
                {
                    DrawPackageRow(package);
                }
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.HelpBox(statusMessage, MessageType.None);
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            searchText = GUILayout.TextField(
                searchText,
                EditorStyles.toolbarSearchField,
                GUILayout.MinWidth(180f));

            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton))
            {
                RefreshManifest();
            }

            using (new EditorGUI.DisabledScope(IsBusy))
            {
                if (GUILayout.Button("Resolve", EditorStyles.toolbarButton))
                {
                    Client.Resolve();
                    statusMessage = "Package resolve requested.";
                }
            }

            if (GUILayout.Button("manifest.json", EditorStyles.toolbarButton))
            {
                OpenFile(ManifestPath);
            }

            if (GUILayout.Button("packages-lock.json", EditorStyles.toolbarButton))
            {
                OpenFile(LockPath);
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawAddPackage()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            EditorGUILayout.LabelField(
                "Package ID / Git URL",
                GUILayout.Width(120f));
            packageInput = EditorGUILayout.TextField(packageInput);

            using (new EditorGUI.DisabledScope(
                IsBusy || string.IsNullOrWhiteSpace(packageInput)))
            {
                if (GUILayout.Button("Add", GUILayout.Width(70f)))
                {
                    addRequest = Client.Add(packageInput.Trim());
                    statusMessage = $"Adding {packageInput.Trim()}...";
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawPackageRow(PackageEntry package)
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);
            EditorGUILayout.LabelField(
                package.Name,
                EditorStyles.boldLabel,
                GUILayout.Width(210f));
            EditorGUILayout.SelectableLabel(
                package.Version,
                GUILayout.Height(EditorGUIUtility.singleLineHeight));

            using (new EditorGUI.DisabledScope(IsBusy))
            {
                if (GUILayout.Button("Remove", GUILayout.Width(70f)) &&
                    EditorUtility.DisplayDialog(
                        "Remove Package",
                        $"Remove {package.Name}?",
                        "Remove",
                        "Cancel"))
                {
                    removeRequest = Client.Remove(package.Name);
                    statusMessage = $"Removing {package.Name}...";
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private bool CheckRequest(Request request, string successMessage)
        {
            if (request == null || !request.IsCompleted)
            {
                return false;
            }

            statusMessage = request.Status == StatusCode.Success
                ? successMessage
                : request.Error?.message ?? "Package operation failed.";
            return true;
        }

        private bool MatchesSearch(PackageEntry package)
        {
            return string.IsNullOrWhiteSpace(searchText) ||
                   package.Name.IndexOf(
                       searchText,
                       StringComparison.OrdinalIgnoreCase) >= 0 ||
                   package.Version.IndexOf(
                       searchText,
                       StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void RefreshManifest()
        {
            packages.Clear();
            try
            {
                JObject root = JObject.Parse(File.ReadAllText(ManifestPath));
                JObject dependencies = root["dependencies"] as JObject;
                if (dependencies != null)
                {
                    foreach (JProperty property in dependencies.Properties())
                    {
                        packages.Add(new PackageEntry(
                            property.Name,
                            property.Value.Value<string>()));
                    }
                }

                packages.Sort((left, right) =>
                    string.Compare(
                        left.Name,
                        right.Name,
                        StringComparison.OrdinalIgnoreCase));
                statusMessage = $"{packages.Count} direct dependencies.";
            }
            catch (Exception exception)
            {
                statusMessage = exception.Message;
            }
        }

        private static void OpenFile(string path)
        {
            UnityEditorInternal.InternalEditorUtility
                .OpenFileAtLineExternal(path, 1);
        }

        private bool IsBusy =>
            addRequest != null ||
            removeRequest != null;

        private sealed class PackageEntry
        {
            public PackageEntry(string name, string version)
            {
                Name = name;
                Version = version;
            }

            public string Name { get; }

            public string Version { get; }
        }
    }
}
