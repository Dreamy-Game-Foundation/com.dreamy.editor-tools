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
        private const string DataConfigFolderMarker = "/Resources/DataConfig/";
        private const string DefaultDataConfigFolder = "Assets/_Project/Resources/DataConfig";

        private const float SidebarWidth = 280f;
        private const float ToolbarButtonWidth = 78f;

        private readonly List<string> paths = new();

        private DataTab selectedTab;
        private Vector2 sidebarScroll;
        private Vector2 contentScroll;

        private string selectedPath;
        private string searchText = string.Empty;
        private string content = string.Empty;
        private string loadedContent = string.Empty;
        private string loadedRawContent = string.Empty;
        private string status = string.Empty;

        private bool isDirty;
        private bool autoBackupBeforeWrite = true;
        private bool showMetadata = true;

        private MessageType statusType = MessageType.None;
        private GUIStyle textAreaStyle;

        [MenuItem("Tools/Dreamy/Data Debugger")]
        public static void Open()
        {
            DreamyDataDebuggerWindow window = GetWindow<DreamyDataDebuggerWindow>();
            window.titleContent = new GUIContent(WindowTitle);
            window.minSize = new Vector2(920f, 520f);
            window.Show();
        }

        private void OnEnable()
        {
            Refresh(false);
        }

        private void OnGUI()
        {
            InitStyles();

            DrawToolbar();

            EditorGUILayout.BeginHorizontal();
            DrawSidebar();
            DrawContent();
            EditorGUILayout.EndHorizontal();

            if (!string.IsNullOrWhiteSpace(status))
            {
                EditorGUILayout.HelpBox(status, statusType);
            }
        }

        private void InitStyles()
        {
            if (textAreaStyle != null)
                return;

            textAreaStyle = new GUIStyle(EditorStyles.textArea)
            {
                wordWrap = false,
                richText = false
            };
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

            DataTab nextTab = (DataTab)GUILayout.Toolbar(
                (int)selectedTab,
                new[] { "Data Config", "Datasave" },
                EditorStyles.toolbarButton,
                GUILayout.Width(210f));

            if (nextTab != selectedTab)
            {
                if (ConfirmDiscardIfDirty())
                {
                    selectedTab = nextTab;
                    ClearSelection();
                    Refresh(false);
                }
            }

            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(ToolbarButtonWidth)))
            {
                if (ConfirmDiscardIfDirty())
                {
                    Refresh(true);
                }
            }

            if (selectedTab == DataTab.DataConfig)
            {
                if (GUILayout.Button("Open Config Folder", EditorStyles.toolbarButton, GUILayout.Width(128f)))
                {
                    OpenDataConfigFolder();
                }
            }
            else
            {
                if (GUILayout.Button("Open Save Folder", EditorStyles.toolbarButton, GUILayout.Width(118f)))
                {
                    OpenSaveFolder();
                }

                GUI.enabled = paths.Count > 0;
                if (GUILayout.Button("Delete All Saves", EditorStyles.toolbarButton, GUILayout.Width(118f)))
                {
                    DeleteAllVisibleSaves();
                }

                GUI.enabled = true;
            }

            GUILayout.FlexibleSpace();

            autoBackupBeforeWrite = GUILayout.Toggle(
                autoBackupBeforeWrite,
                "Auto Backup",
                EditorStyles.toolbarButton,
                GUILayout.Width(95f));

            showMetadata = GUILayout.Toggle(
                showMetadata,
                "Info",
                EditorStyles.toolbarButton,
                GUILayout.Width(48f));

            EditorGUILayout.EndHorizontal();
        }

        private void DrawSidebar()
        {
            EditorGUILayout.BeginVertical(GUILayout.Width(SidebarWidth));

            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            searchText = EditorGUILayout.TextField(searchText, EditorStyles.toolbarSearchField);
            if (!string.IsNullOrEmpty(searchText) &&
                GUILayout.Button("x", EditorStyles.toolbarButton, GUILayout.Width(22f)))
            {
                searchText = string.Empty;
                GUI.FocusControl(null);
            }

            EditorGUILayout.EndHorizontal();

            IEnumerable<string> visiblePaths = paths.Where(MatchesSearch).ToList();

            EditorGUILayout.LabelField(
                $"{visiblePaths.Count()} / {paths.Count} file(s)",
                EditorStyles.miniLabel);

            sidebarScroll = EditorGUILayout.BeginScrollView(sidebarScroll);

            foreach (string path in visiblePaths)
            {
                bool selected = string.Equals(path, selectedPath, StringComparison.Ordinal);
                GUIStyle style = selected ? EditorStyles.miniButton : EditorStyles.label;

                if (GUILayout.Button(new GUIContent(GetDisplayName(path), path), style, GUILayout.Height(22f)))
                {
                    if (ConfirmDiscardIfDirty())
                    {
                        Select(path);
                    }
                }
            }

            if (paths.Count == 0)
            {
                EditorGUILayout.HelpBox(
                    selectedTab == DataTab.DataConfig
                        ? "No JSON found in any Resources/DataConfig folder."
                        : "No save file found.",
                    MessageType.Info);
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawContent()
        {
            EditorGUILayout.BeginVertical(GUILayout.ExpandWidth(true));

            if (string.IsNullOrEmpty(selectedPath))
            {
                DrawEmptyState();
                EditorGUILayout.EndVertical();
                return;
            }

            DrawSelectedHeader();
            DrawActionButtons();

            if (showMetadata)
            {
                DrawMetadata();
            }

            EditorGUILayout.Space(4f);

            contentScroll = EditorGUILayout.BeginScrollView(contentScroll);

            string nextContent = EditorGUILayout.TextArea(
                content,
                textAreaStyle,
                GUILayout.ExpandHeight(true));

            if (!string.Equals(nextContent, content, StringComparison.Ordinal))
            {
                content = nextContent;
                isDirty = !string.Equals(content, loadedContent, StringComparison.Ordinal);
            }

            EditorGUILayout.EndScrollView();
            EditorGUILayout.EndVertical();
        }

        private void DrawEmptyState()
        {
            string message = selectedTab == DataTab.DataConfig
                ? "Select a JSON config from Resources/DataConfig."
                : "Select a save file.";

            EditorGUILayout.HelpBox(message, MessageType.Info);

            if (selectedTab == DataTab.DataConfig)
            {
                EditorGUILayout.LabelField("Expected folder:", EditorStyles.boldLabel);
                EditorGUILayout.SelectableLabel("Assets/.../Resources/DataConfig/*.json", GUILayout.Height(18f));

                if (GUILayout.Button("Create Default DataConfig Folder", GUILayout.Width(220f)))
                {
                    Directory.CreateDirectory(ToAbsolutePath(DefaultDataConfigFolder));
                    AssetDatabase.Refresh();
                    EditorUtility.RevealInFinder(ToAbsolutePath(DefaultDataConfigFolder));
                    Refresh(false);
                }
            }
            else
            {
                EditorGUILayout.LabelField("Save folder:", EditorStyles.boldLabel);
                EditorGUILayout.SelectableLabel(GetSaveDirectory(), GUILayout.Height(18f));

                if (GUILayout.Button("Create/Open Save Folder", GUILayout.Width(180f)))
                {
                    OpenSaveFolder();
                    Refresh(false);
                }
            }
        }

        private void DrawSelectedHeader()
        {
            EditorGUILayout.BeginHorizontal();

            string title = isDirty ? $"{GetDisplayName(selectedPath)}  *" : GetDisplayName(selectedPath);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);

            GUILayout.FlexibleSpace();

            if (GUILayout.Button("Reveal", GUILayout.Width(64f)))
            {
                RevealSelectedFile();
            }

            if (GUILayout.Button("Copy Path", GUILayout.Width(82f)))
            {
                EditorGUIUtility.systemCopyBuffer = selectedPath;
                SetStatus("Copied path to clipboard.", MessageType.Info);
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.SelectableLabel(selectedPath, EditorStyles.miniLabel, GUILayout.Height(18f));
        }

        private void DrawActionButtons()
        {
            EditorGUILayout.BeginHorizontal();

            GUI.enabled = isDirty;
            if (GUILayout.Button("Save", GUILayout.Width(70f)))
            {
                SaveCurrentJson();
            }

            if (GUILayout.Button("Save Raw", GUILayout.Width(78f)))
            {
                SaveCurrentRaw();
            }

            if (GUILayout.Button("Revert", GUILayout.Width(70f)))
            {
                RevertCurrent();
            }

            GUI.enabled = true;

            if (GUILayout.Button("Validate", GUILayout.Width(76f)))
            {
                ValidateCurrentJson(true);
            }

            if (GUILayout.Button("Pretty", GUILayout.Width(70f)))
            {
                PrettyPrintCurrent();
            }

            if (GUILayout.Button("Minify", GUILayout.Width(70f)))
            {
                MinifyCurrent();
            }

            if (GUILayout.Button("Backup", GUILayout.Width(72f)))
            {
                CreateBackup(true);
            }

            if (GUILayout.Button("Copy Content", GUILayout.Width(102f)))
            {
                EditorGUIUtility.systemCopyBuffer = content;
                SetStatus("Copied content to clipboard.", MessageType.Info);
            }

            GUILayout.FlexibleSpace();

            GUI.backgroundColor = new Color(1f, 0.62f, 0.62f);
            if (GUILayout.Button("Delete", GUILayout.Width(70f)))
            {
                DeleteSelectedFile();
            }

            GUI.backgroundColor = Color.white;

            EditorGUILayout.EndHorizontal();
        }

        private void DrawMetadata()
        {
            string absolutePath = ToAbsolutePath(selectedPath);
            FileInfo info = new FileInfo(absolutePath);

            if (!info.Exists)
            {
                EditorGUILayout.HelpBox("File does not exist.", MessageType.Warning);
                return;
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField("Source", selectedTab.ToString());
            EditorGUILayout.LabelField("Size", FormatBytes(info.Length));
            EditorGUILayout.LabelField("Last Write", info.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"));
            EditorGUILayout.LabelField("Absolute Path", absolutePath);

            if (TryParseContent(out JToken token, out _))
            {
                EditorGUILayout.LabelField("JSON Summary", BuildJsonSummary(token));
            }

            EditorGUILayout.EndVertical();
        }

        private void Refresh(bool reloadSelected)
        {
            paths.Clear();

            if (selectedTab == DataTab.DataConfig)
            {
                foreach (string guid in AssetDatabase.FindAssets("t:TextAsset"))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);

                    if (IsDataConfigJsonPath(path))
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
                        .Where(IsVisibleSaveFile));
                }
            }

            paths.Sort(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrEmpty(selectedPath) && !paths.Contains(selectedPath))
            {
                ClearSelection();
            }
            else if (reloadSelected && !string.IsNullOrEmpty(selectedPath))
            {
                Select(selectedPath);
            }

            SetStatus($"Found {paths.Count} file(s).", MessageType.None);
            Repaint();
        }

        private void Select(string path)
        {
            selectedPath = path;
            contentScroll = Vector2.zero;

            try
            {
                loadedRawContent = File.ReadAllText(ToAbsolutePath(path));

                JToken json = JToken.Parse(loadedRawContent);
                content = selectedTab == DataTab.Datasave
                    ? FormatSaveEnvelope(json)
                    : json.ToString(Formatting.Indented);

                loadedContent = content;
                isDirty = false;

                ValidateCurrentJson(false);
            }
            catch (JsonException exception)
            {
                loadedContent = loadedRawContent;
                content = loadedRawContent;
                isDirty = false;

                SetStatus(
                    selectedTab == DataTab.Datasave
                        ? "Save is encoded or invalid JSON: " + exception.Message
                        : "Invalid JSON: " + exception.Message,
                    MessageType.Warning);
            }
            catch (Exception exception)
            {
                loadedRawContent = string.Empty;
                loadedContent = string.Empty;
                content = string.Empty;
                isDirty = false;

                SetStatus(exception.Message, MessageType.Error);
            }
        }

        private void SaveCurrentJson()
        {
            if (string.IsNullOrEmpty(selectedPath))
                return;

            if (!TryParseContent(out JToken token, out string error))
            {
                SetStatus("Cannot save. Invalid JSON: " + error, MessageType.Error);
                EditorUtility.DisplayDialog(WindowTitle, "Cannot save because JSON is invalid:\n\n" + error, "OK");
                return;
            }

            try
            {
                if (autoBackupBeforeWrite)
                {
                    CreateBackup(false);
                }

                string textToWrite = NormalizeJsonForWrite(token);
                File.WriteAllText(ToAbsolutePath(selectedPath), textToWrite);

                AfterFileWritten();

                Select(selectedPath);
                SetStatus("Saved successfully.", MessageType.Info);
            }
            catch (Exception exception)
            {
                SetStatus("Save failed: " + exception.Message, MessageType.Error);
            }
        }

        private void SaveCurrentRaw()
        {
            if (string.IsNullOrEmpty(selectedPath) || !isDirty)
                return;

            bool confirm = EditorUtility.DisplayDialog(
                WindowTitle,
                "Save raw text directly?\n\nThis can break JSON/save format if the content is invalid.",
                "Save Raw",
                "Cancel");

            if (!confirm)
                return;

            try
            {
                if (autoBackupBeforeWrite)
                {
                    CreateBackup(false);
                }

                File.WriteAllText(ToAbsolutePath(selectedPath), content);

                AfterFileWritten();

                Select(selectedPath);
                SetStatus("Raw content saved.", MessageType.Warning);
            }
            catch (Exception exception)
            {
                SetStatus("Raw save failed: " + exception.Message, MessageType.Error);
            }
        }

        private void RevertCurrent()
        {
            if (!isDirty)
                return;

            bool confirm = EditorUtility.DisplayDialog(
                WindowTitle,
                "Revert unsaved changes?",
                "Revert",
                "Cancel");

            if (!confirm)
                return;

            content = loadedContent;
            isDirty = false;
            SetStatus("Reverted unsaved changes.", MessageType.Info);
        }

        private void PrettyPrintCurrent()
        {
            if (!TryParseContent(out JToken token, out string error))
            {
                SetStatus("Cannot pretty print. Invalid JSON: " + error, MessageType.Error);
                return;
            }

            content = selectedTab == DataTab.Datasave
                ? FormatSaveEnvelope(token)
                : token.ToString(Formatting.Indented);

            isDirty = !string.Equals(content, loadedContent, StringComparison.Ordinal);
            SetStatus("Pretty printed JSON.", MessageType.Info);
        }

        private void MinifyCurrent()
        {
            if (!TryParseContent(out JToken token, out string error))
            {
                SetStatus("Cannot minify. Invalid JSON: " + error, MessageType.Error);
                return;
            }

            content = NormalizeJsonForWrite(token, Formatting.None);
            isDirty = !string.Equals(content, loadedContent, StringComparison.Ordinal);
            SetStatus("Minified JSON.", MessageType.Info);
        }

        private bool ValidateCurrentJson(bool showDialog)
        {
            if (!TryParseContent(out JToken token, out string error))
            {
                SetStatus("Invalid JSON: " + error, MessageType.Error);

                if (showDialog)
                {
                    EditorUtility.DisplayDialog(WindowTitle, "Invalid JSON:\n\n" + error, "OK");
                }

                return false;
            }

            List<string> warnings = new();
            CollectDuplicateIdWarnings(token, "$", warnings);

            string summary = BuildJsonSummary(token);

            if (warnings.Count == 0)
            {
                SetStatus("JSON is valid. " + summary, MessageType.Info);

                if (showDialog)
                {
                    EditorUtility.DisplayDialog(WindowTitle, "JSON is valid.\n\n" + summary, "OK");
                }
            }
            else
            {
                string warningText = string.Join("\n", warnings.Take(20));
                SetStatus($"JSON is valid with {warnings.Count} warning(s). {summary}", MessageType.Warning);

                if (showDialog)
                {
                    EditorUtility.DisplayDialog(
                        WindowTitle,
                        $"JSON is valid, but has {warnings.Count} warning(s):\n\n{warningText}",
                        "OK");
                }
            }

            return true;
        }

        private void DeleteSelectedFile()
        {
            if (string.IsNullOrEmpty(selectedPath))
                return;

            bool confirm = EditorUtility.DisplayDialog(
                WindowTitle,
                $"Delete '{GetDisplayName(selectedPath)}'?\n\nA backup will be created first if Auto Backup is enabled.",
                "Delete",
                "Cancel");

            if (!confirm)
                return;

            try
            {
                if (autoBackupBeforeWrite)
                {
                    CreateBackup(false);
                }

                if (selectedTab == DataTab.DataConfig)
                {
                    AssetDatabase.DeleteAsset(selectedPath);
                    AssetDatabase.Refresh();
                }
                else
                {
                    File.Delete(ToAbsolutePath(selectedPath));
                }

                ClearSelection();
                Refresh(false);
                SetStatus("File deleted.", MessageType.Warning);
            }
            catch (Exception exception)
            {
                SetStatus("Delete failed: " + exception.Message, MessageType.Error);
            }
        }

        private void DeleteAllVisibleSaves()
        {
            if (selectedTab != DataTab.Datasave)
                return;

            bool confirm = EditorUtility.DisplayDialog(
                WindowTitle,
                $"Delete all visible save files?\n\nCount: {paths.Count}\nBackup files are not included.",
                "Delete All",
                "Cancel");

            if (!confirm)
                return;

            try
            {
                foreach (string path in paths.ToList())
                {
                    if (autoBackupBeforeWrite && File.Exists(path))
                    {
                        string backupPath = BuildBackupPath(path);
                        File.Copy(path, backupPath, true);
                    }

                    File.Delete(path);
                }

                ClearSelection();
                Refresh(false);
                SetStatus("All visible save files deleted.", MessageType.Warning);
            }
            catch (Exception exception)
            {
                SetStatus("Delete all saves failed: " + exception.Message, MessageType.Error);
            }
        }

        private void CreateBackup(bool showStatus)
        {
            if (string.IsNullOrEmpty(selectedPath))
                return;

            string sourcePath = ToAbsolutePath(selectedPath);

            if (!File.Exists(sourcePath))
            {
                if (showStatus)
                {
                    SetStatus("Cannot backup. File does not exist.", MessageType.Warning);
                }

                return;
            }

            try
            {
                string backupPath = BuildBackupPath(sourcePath);
                File.Copy(sourcePath, backupPath, true);

                if (selectedTab == DataTab.DataConfig)
                {
                    AssetDatabase.Refresh();
                }

                if (showStatus)
                {
                    SetStatus("Backup created: " + backupPath, MessageType.Info);
                }
            }
            catch (Exception exception)
            {
                if (showStatus)
                {
                    SetStatus("Backup failed: " + exception.Message, MessageType.Error);
                }
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

        private string NormalizeJsonForWrite(JToken token, Formatting formatting = Formatting.Indented)
        {
            if (selectedTab != DataTab.Datasave)
            {
                return token.ToString(formatting);
            }

            if (token is not JObject envelope || envelope["Payload"] == null)
            {
                return token.ToString(formatting);
            }

            bool originalPayloadWasString = TryOriginalPayloadWasString();

            if (originalPayloadWasString && envelope["Payload"].Type != JTokenType.String)
            {
                envelope["Payload"] = envelope["Payload"].ToString(Formatting.None);
            }

            return envelope.ToString(formatting);
        }

        private bool TryOriginalPayloadWasString()
        {
            try
            {
                JToken original = JToken.Parse(loadedRawContent);

                return original is JObject originalEnvelope &&
                       originalEnvelope["Payload"] != null &&
                       originalEnvelope["Payload"].Type == JTokenType.String;
            }
            catch
            {
                return false;
            }
        }

        private bool TryParseContent(out JToken token, out string error)
        {
            try
            {
                token = JToken.Parse(content);
                error = null;
                return true;
            }
            catch (Exception exception)
            {
                token = null;
                error = exception.Message;
                return false;
            }
        }

        private void CollectDuplicateIdWarnings(JToken token, string path, List<string> warnings)
        {
            if (token is JObject obj)
            {
                foreach (JProperty property in obj.Properties())
                {
                    CollectDuplicateIdWarnings(property.Value, $"{path}.{property.Name}", warnings);
                }

                return;
            }

            if (token is not JArray array)
                return;

            List<JObject> objects = array.OfType<JObject>().ToList();

            if (objects.Count > 0)
            {
                var duplicateIds = objects
                    .Select((item, index) => new
                    {
                        Index = index,
                        Id = item["id"]?.Value<string>() ?? item["Id"]?.Value<string>()
                    })
                    .Where(x => !string.IsNullOrWhiteSpace(x.Id))
                    .GroupBy(x => x.Id)
                    .Where(group => group.Count() > 1)
                    .ToList();

                foreach (var duplicate in duplicateIds)
                {
                    string indexes = string.Join(", ", duplicate.Select(x => x.Index));
                    warnings.Add($"{path}: duplicate id '{duplicate.Key}' at indexes [{indexes}]");
                }
            }

            for (int i = 0; i < array.Count; i++)
            {
                CollectDuplicateIdWarnings(array[i], $"{path}[{i}]", warnings);
            }
        }

        private string BuildJsonSummary(JToken token)
        {
            if (token is JObject obj)
            {
                int propertyCount = obj.Properties().Count();
                int arrayCount = obj.DescendantsAndSelf().OfType<JArray>().Count();
                int objectCount = obj.DescendantsAndSelf().OfType<JObject>().Count();

                return $"Root object: {propertyCount} field(s), {objectCount} object(s), {arrayCount} array(s).";
            }

            if (token is JArray array)
            {
                return $"Root array: {array.Count} item(s).";
            }

            return $"Root token: {token.Type}.";
        }

        private void AfterFileWritten()
        {
            if (selectedTab == DataTab.DataConfig)
            {
                AssetDatabase.ImportAsset(selectedPath);
                AssetDatabase.Refresh();
            }
        }

        private void OpenSaveFolder()
        {
            string directory = GetSaveDirectory();
            Directory.CreateDirectory(directory);
            EditorUtility.RevealInFinder(directory);
        }

        private void OpenDataConfigFolder()
        {
            string targetPath = null;

            if (!string.IsNullOrEmpty(selectedPath))
            {
                targetPath = Path.GetDirectoryName(ToAbsolutePath(selectedPath));
            }
            else if (paths.Count > 0)
            {
                targetPath = Path.GetDirectoryName(ToAbsolutePath(paths[0]));
            }
            else
            {
                Directory.CreateDirectory(ToAbsolutePath(DefaultDataConfigFolder));
                AssetDatabase.Refresh();
                targetPath = ToAbsolutePath(DefaultDataConfigFolder);
            }

            EditorUtility.RevealInFinder(targetPath);
        }

        private void RevealSelectedFile()
        {
            if (string.IsNullOrEmpty(selectedPath))
                return;

            EditorUtility.RevealInFinder(ToAbsolutePath(selectedPath));
        }

        private bool ConfirmDiscardIfDirty()
        {
            if (!isDirty)
                return true;

            return EditorUtility.DisplayDialog(
                WindowTitle,
                "Current file has unsaved changes. Discard changes?",
                "Discard",
                "Cancel");
        }

        private void ClearSelection()
        {
            selectedPath = null;
            content = string.Empty;
            loadedContent = string.Empty;
            loadedRawContent = string.Empty;
            isDirty = false;
            contentScroll = Vector2.zero;
        }

        private bool MatchesSearch(string path)
        {
            if (string.IsNullOrWhiteSpace(searchText))
                return true;

            return path.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   GetDisplayName(path).IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private string GetDisplayName(string path)
        {
            if (selectedTab == DataTab.DataConfig)
            {
                string normalized = NormalizePath(path);
                int index = normalized.IndexOf(DataConfigFolderMarker, StringComparison.OrdinalIgnoreCase);

                if (index >= 0)
                {
                    return normalized.Substring(index + DataConfigFolderMarker.Length);
                }
            }

            return Path.GetFileName(path);
        }

        private static bool IsDataConfigJsonPath(string path)
        {
            string normalized = NormalizePath(path);

            return normalized.EndsWith(".json", StringComparison.OrdinalIgnoreCase) &&
                   normalized.IndexOf(DataConfigFolderMarker, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static bool IsVisibleSaveFile(string path)
        {
            if (path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                return false;

            if (path.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
                return false;

            if (path.Contains(".bak-", StringComparison.OrdinalIgnoreCase))
                return false;

            return File.Exists(path);
        }

        private static string GetSaveDirectory()
        {
            return Path.Combine(Application.persistentDataPath, SaveDirectoryName);
        }

        private static string ToAbsolutePath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            if (Path.IsPathRooted(path))
                return path;

            return Path.GetFullPath(path);
        }

        private static string NormalizePath(string path)
        {
            return path.Replace('\\', '/');
        }

        private static string BuildBackupPath(string path)
        {
            string timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            return $"{path}.bak-{timestamp}";
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024)
                return $"{bytes} B";

            double kb = bytes / 1024d;

            if (kb < 1024)
                return $"{kb:0.##} KB";

            double mb = kb / 1024d;

            return $"{mb:0.##} MB";
        }

        private void SetStatus(string message, MessageType type)
        {
            status = message;
            statusType = type;
            Repaint();
        }

        private enum DataTab
        {
            DataConfig,
            Datasave
        }
    }
}