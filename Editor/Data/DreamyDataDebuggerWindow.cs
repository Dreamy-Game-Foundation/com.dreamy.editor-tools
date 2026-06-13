using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Dreamy.EditorTools
{
    public sealed class DreamyDataDebuggerWindow : EditorWindow
    {
        private const string WindowTitle = "Dreamy Data Debugger";
        private const string SaveDirectoryName = "DreamySaves";
        private const float SidebarWidth = 260f;

        private readonly List<string> paths = new();
        private DataTab selectedTab;
        private Vector2 sidebarScroll;
        private Vector2 contentScroll;
        private string selectedPath;
        private string searchText = string.Empty;
        private string content = string.Empty;
        private string status = string.Empty;

        [MenuItem("Tools/Dreamy/Data Debugger")]
        public static void Open()
        {
            DreamyDataDebuggerWindow window = GetWindow<DreamyDataDebuggerWindow>();
            window.titleContent = new GUIContent(WindowTitle);
            window.minSize = new Vector2(760f, 420f);
            window.Show();
        }

        private void OnEnable()
        {
            Refresh();
        }

        private void OnGUI()
        {
            DrawToolbar();
            EditorGUILayout.BeginHorizontal();
            DrawSidebar();
            DrawContent();
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.HelpBox(status, MessageType.None);
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            DataTab nextTab = (DataTab)GUILayout.Toolbar(
                (int)selectedTab,
                new[] { "Data Config", "Datasave" },
                EditorStyles.toolbarButton,
                GUILayout.Width(190f));
            if (nextTab != selectedTab)
            {
                selectedTab = nextTab;
                selectedPath = null;
                content = string.Empty;
                Refresh();
            }

            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton))
            {
                Refresh();
            }

            if (selectedTab == DataTab.Datasave &&
                GUILayout.Button("Open Folder", EditorStyles.toolbarButton))
            {
                string directory = GetSaveDirectory();
                Directory.CreateDirectory(directory);
                EditorUtility.RevealInFinder(directory);
            }

            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();
        }

        private void DrawSidebar()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(SidebarWidth));
            searchText = EditorGUILayout.TextField(searchText, EditorStyles.toolbarSearchField);
            sidebarScroll = EditorGUILayout.BeginScrollView(sidebarScroll);
            foreach (string path in paths.Where(MatchesSearch))
            {
                if (GUILayout.Button(
                    new GUIContent(Path.GetFileName(path), path),
                    string.Equals(path, selectedPath, StringComparison.Ordinal)
                        ? EditorStyles.miniButton
                        : EditorStyles.label))
                {
                    Select(path);
                }
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawContent()
        {
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));
            if (string.IsNullOrEmpty(selectedPath))
            {
                EditorGUILayout.HelpBox("Select a data file.", MessageType.Info);
                EditorGUILayout.EndVertical();
                return;
            }

            EditorGUILayout.LabelField(selectedPath, EditorStyles.miniLabel);
            if (selectedTab == DataTab.Datasave)
            {
                if (GUILayout.Button("Delete Save File", GUILayout.Width(130f)))
                {
                    DeleteSelectedSave();
                }
            }

            contentScroll = EditorGUILayout.BeginScrollView(contentScroll);
            EditorGUILayout.TextArea(content, GUILayout.ExpandHeight(true));
            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void Refresh()
        {
            paths.Clear();
            if (selectedTab == DataTab.DataConfig)
            {
                foreach (string guid in AssetDatabase.FindAssets("t:TextAsset"))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                    {
                        paths.Add(path);
                    }
                }
            }
            else
            {
                string directory = GetSaveDirectory();
                if (Directory.Exists(directory))
                {
                    paths.AddRange(Directory.GetFiles(directory)
                        .Where(path => !path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) &&
                                       !path.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)));
                }
            }

            paths.Sort(StringComparer.OrdinalIgnoreCase);
            status = $"Found {paths.Count} file(s).";
            Repaint();
        }

        private void Select(string path)
        {
            selectedPath = path;
            try
            {
                string raw = File.ReadAllText(path);
                JToken json = JToken.Parse(raw);
                content = selectedTab == DataTab.Datasave
                    ? FormatSaveEnvelope(json)
                    : json.ToString(Formatting.Indented);
                status = "JSON is valid.";
            }
            catch (JsonException exception)
            {
                content = File.ReadAllText(path);
                status = selectedTab == DataTab.Datasave
                    ? "Save is encoded or invalid JSON: " + exception.Message
                    : "Invalid JSON: " + exception.Message;
            }
            catch (Exception exception)
            {
                content = string.Empty;
                status = exception.Message;
            }
        }

        private static string FormatSaveEnvelope(JToken json)
        {
            if (json is not JObject envelope || envelope["Payload"] == null)
            {
                return json.ToString(Formatting.Indented);
            }

            string payload = envelope.Value<string>("Payload");
            if (!string.IsNullOrWhiteSpace(payload))
            {
                try
                {
                    envelope["Payload"] = JToken.Parse(payload);
                }
                catch (JsonException)
                {
                    // Keep non-JSON payloads visible as their original string.
                }
            }

            return envelope.ToString(Formatting.Indented);
        }

        private void DeleteSelectedSave()
        {
            if (!EditorUtility.DisplayDialog(
                    WindowTitle,
                    $"Delete '{Path.GetFileName(selectedPath)}'?",
                    "Delete",
                    "Cancel"))
            {
                return;
            }

            File.Delete(selectedPath);
            selectedPath = null;
            content = string.Empty;
            Refresh();
        }

        private bool MatchesSearch(string path)
        {
            return string.IsNullOrWhiteSpace(searchText) ||
                   path.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string GetSaveDirectory()
        {
            return Path.Combine(Application.persistentDataPath, SaveDirectoryName);
        }

        private enum DataTab
        {
            DataConfig,
            Datasave
        }
    }
}
