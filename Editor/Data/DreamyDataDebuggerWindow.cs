using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Dreamy.EditorTools
{
    public sealed class DreamyDataDebuggerWindow : EditorWindow
    {
        private const string WindowTitle = "Dreamy Data Debugger";
        private const string SaveDirectoryName = "DreamySaves";
        private const string DataConfigFolderMarker = "/Resources/DataConfig/";
        private const string DefaultDataConfigFolder = "Assets/_Project/Resources/DataConfig";

        private const float SidebarWidth = 290f;
        private const float MinCellWidth = 110f;
        private const float RowHeight = 24f;

        private readonly List<string> paths = new();
        private readonly List<TableCandidate> tableCandidates = new();
        private readonly List<string> tableColumns = new();

        private DataTab selectedTab;
        private JsonViewMode viewMode;

        private Vector2 sidebarScroll;
        private Vector2 rawScroll;
        private Vector2 tableScroll;

        private string selectedPath;
        private string searchText = string.Empty;
        private string content = string.Empty;
        private string loadedContent = string.Empty;
        private string loadedRawContent = string.Empty;
        private string status = string.Empty;

        private bool isDirty;
        private bool autoBackupBeforeWrite = true;
        private bool showInfo = true;

        private MessageType statusType = MessageType.None;
        private GUIStyle rawTextAreaStyle;

        private JToken workingToken;
        private ReorderableList tableList;
        private List<JToken> tableRows = new();

        private int selectedTableIndex;
        private int selectedColumnIndex;
        private string newColumnName = string.Empty;

        [MenuItem("Tools/Dreamy/Data Debugger")]
        public static void Open()
        {
            DreamyDataDebuggerWindow window = GetWindow<DreamyDataDebuggerWindow>();
            window.titleContent = new GUIContent(WindowTitle);
            window.minSize = new Vector2(1100f, 620f);
            window.Show();
        }

        private void OnEnable()
        {
            Refresh(false);
        }

        private void OnGUI()
        {
            InitStyles();
            HandleShortcuts();

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
            if (rawTextAreaStyle != null)
                return;

            rawTextAreaStyle = new GUIStyle(EditorStyles.textArea)
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
                GUILayout.Width(220f));

            if (nextTab != selectedTab)
            {
                if (ConfirmDiscardIfDirty())
                {
                    selectedTab = nextTab;
                    ClearSelection();
                    Refresh(false);
                }
            }

            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(75f)))
            {
                if (ConfirmDiscardIfDirty())
                {
                    Refresh(true);
                }
            }

            if (selectedTab == DataTab.DataConfig)
            {
                if (GUILayout.Button("Open Config Folder", EditorStyles.toolbarButton, GUILayout.Width(130f)))
                {
                    OpenDataConfigFolder();
                }
            }
            else
            {
                if (GUILayout.Button("Open Save Folder", EditorStyles.toolbarButton, GUILayout.Width(120f)))
                {
                    OpenSaveFolder();
                }

                GUI.enabled = paths.Count > 0;
                if (GUILayout.Button("Delete All Saves", EditorStyles.toolbarButton, GUILayout.Width(120f)))
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
                GUILayout.Width(100f));

            showInfo = GUILayout.Toggle(
                showInfo,
                "Info",
                EditorStyles.toolbarButton,
                GUILayout.Width(50f));

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

            List<string> visiblePaths = paths.Where(MatchesSearch).ToList();

            EditorGUILayout.LabelField($"{visiblePaths.Count} / {paths.Count} file(s)", EditorStyles.miniLabel);

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
            DrawMainActions();

            if (showInfo)
            {
                DrawInfoBox();
            }

            EditorGUILayout.Space(4f);

            DrawViewModeToolbar();

            EditorGUILayout.Space(4f);

            if (viewMode == JsonViewMode.Raw)
            {
                DrawRawView();
            }
            else
            {
                DrawTableView();
            }

            EditorGUILayout.EndVertical();
        }

        private void DrawEmptyState()
        {
            string message = selectedTab == DataTab.DataConfig
                ? "Select a JSON config from Resources/DataConfig."
                : "Select a save file. Only Payload will be displayed.";

            EditorGUILayout.HelpBox(message, MessageType.Info);

            if (selectedTab == DataTab.DataConfig)
            {
                EditorGUILayout.LabelField("Expected folder:", EditorStyles.boldLabel);
                EditorGUILayout.SelectableLabel("Assets/.../Resources/DataConfig/*.json", GUILayout.Height(18f));

                if (GUILayout.Button("Create Default DataConfig Folder", GUILayout.Width(230f)))
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

                if (GUILayout.Button("Create/Open Save Folder", GUILayout.Width(190f)))
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

        private void DrawMainActions()
        {
            EditorGUILayout.BeginHorizontal();

            GUI.enabled = isDirty;
            if (GUILayout.Button(selectedTab == DataTab.Datasave ? "Save Payload" : "Save", GUILayout.Width(100f)))
            {
                SaveCurrentJson();
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

            if (GUILayout.Button("Copy Content", GUILayout.Width(105f)))
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

        private void DrawViewModeToolbar()
        {
            EditorGUILayout.BeginHorizontal();

            JsonViewMode nextMode = (JsonViewMode)GUILayout.Toolbar(
                (int)viewMode,
                new[] { "Raw JSON", "Table" },
                GUILayout.Width(180f));

            if (nextMode != viewMode)
            {
                if (nextMode == JsonViewMode.Table)
                {
                    if (EnsureWorkingTokenFromContent())
                    {
                        viewMode = nextMode;
                        InvalidateTable();
                    }
                }
                else
                {
                    SyncContentFromWorkingToken();
                    viewMode = nextMode;
                }
            }

            GUILayout.FlexibleSpace();

            if (viewMode == JsonViewMode.Table)
            {
                if (GUILayout.Button("Reload Table", GUILayout.Width(95f)))
                {
                    if (EnsureWorkingTokenFromContent())
                    {
                        InvalidateTable();
                    }
                }
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawRawView()
        {
            rawScroll = EditorGUILayout.BeginScrollView(rawScroll);

            string nextContent = EditorGUILayout.TextArea(
                content,
                rawTextAreaStyle,
                GUILayout.ExpandHeight(true));

            if (!string.Equals(nextContent, content, StringComparison.Ordinal))
            {
                content = nextContent;
                isDirty = !string.Equals(content, loadedContent, StringComparison.Ordinal);
                workingToken = null;
                InvalidateTable();
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawTableView()
        {
            if (!EnsureWorkingTokenFromContent())
            {
                EditorGUILayout.HelpBox("Cannot show table because JSON is invalid.", MessageType.Error);
                return;
            }

            RebuildTableCandidates();

            if (tableCandidates.Count == 0)
            {
                DrawNoArrayTableState();
                return;
            }

            selectedTableIndex = Mathf.Clamp(selectedTableIndex, 0, tableCandidates.Count - 1);

            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

            string[] labels = tableCandidates
                .Select(candidate => $"{candidate.DisplayName}  ({candidate.Count})")
                .ToArray();

            int nextTableIndex = EditorGUILayout.Popup("Array", selectedTableIndex, labels);

            if (nextTableIndex != selectedTableIndex)
            {
                selectedTableIndex = nextTableIndex;
                InvalidateTable();
            }

            if (GUILayout.Button("Copy Array JSON", GUILayout.Width(115f)))
            {
                CopyCurrentArrayJson();
            }

            if (GUILayout.Button("Paste Array JSON", GUILayout.Width(120f)))
            {
                PasteCurrentArrayJson();
            }

            EditorGUILayout.EndHorizontal();

            EnsureTableList();

            DrawTableRowToolbar();
            DrawColumnToolbar();

            tableScroll = EditorGUILayout.BeginScrollView(tableScroll, true, true);

            float tableWidth = Mathf.Max(
                position.width - SidebarWidth - 60f,
                80f + tableColumns.Count * MinCellWidth);

            Rect rect = GUILayoutUtility.GetRect(
                tableWidth,
                tableList.GetHeight(),
                GUILayout.ExpandWidth(false));

            tableList.DoList(rect);

            EditorGUILayout.EndScrollView();
        }

        private void DrawNoArrayTableState()
        {
            EditorGUILayout.HelpBox(
                "No JSON array found. Table View works with JSON arrays, for example: { \"items\": [ ... ] }",
                MessageType.Info);

            if (workingToken is JObject obj)
            {
                EditorGUILayout.BeginHorizontal();

                newColumnName = EditorGUILayout.TextField("New array name", newColumnName);

                if (GUILayout.Button("Create Array", GUILayout.Width(110f)))
                {
                    string arrayName = string.IsNullOrWhiteSpace(newColumnName)
                        ? "items"
                        : newColumnName.Trim();

                    if (obj[arrayName] != null)
                    {
                        SetStatus($"Array/property already exists: {arrayName}", MessageType.Warning);
                    }
                    else
                    {
                        obj[arrayName] = new JArray();
                        newColumnName = string.Empty;
                        SyncContentFromWorkingToken();
                        InvalidateTable();
                        SetStatus($"Created array: {arrayName}", MessageType.Info);
                    }
                }

                EditorGUILayout.EndHorizontal();
            }
            else if (workingToken == null || workingToken.Type == JTokenType.Null)
            {
                if (GUILayout.Button("Create Root Array", GUILayout.Width(140f)))
                {
                    workingToken = new JArray();
                    SyncContentFromWorkingToken();
                    InvalidateTable();
                }
            }
        }

        private void DrawTableRowToolbar()
        {
            EditorGUILayout.BeginHorizontal();

            GUI.enabled = HasSelectedRow();

            if (GUILayout.Button("Insert", GUILayout.Width(65f)))
            {
                InsertRowAfterSelection();
            }

            if (GUILayout.Button("Duplicate", GUILayout.Width(80f)))
            {
                DuplicateSelectedRow();
            }

            if (GUILayout.Button("Delete Row", GUILayout.Width(85f)))
            {
                DeleteSelectedRow();
            }

            if (GUILayout.Button("Copy Row", GUILayout.Width(80f)))
            {
                CopySelectedRow();
            }

            if (GUILayout.Button("Move Up", GUILayout.Width(75f)))
            {
                MoveSelectedRow(-1);
            }

            if (GUILayout.Button("Move Down", GUILayout.Width(90f)))
            {
                MoveSelectedRow(1);
            }

            GUI.enabled = true;

            if (GUILayout.Button("Add Row", GUILayout.Width(75f)))
            {
                AddRow();
            }

            if (GUILayout.Button("Paste Row", GUILayout.Width(82f)))
            {
                PasteRowsFromClipboard();
            }

            GUILayout.FlexibleSpace();

            EditorGUILayout.LabelField("Tip: drag rows by the handle on the left.", EditorStyles.miniLabel, GUILayout.Width(250f));

            EditorGUILayout.EndHorizontal();
        }

        private void DrawColumnToolbar()
        {
            bool objectTable = IsObjectTable();

            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

            GUI.enabled = objectTable;

            newColumnName = EditorGUILayout.TextField("Column", newColumnName, GUILayout.MinWidth(180f));

            if (GUILayout.Button("Add Column", GUILayout.Width(95f)))
            {
                AddColumn(newColumnName);
            }

            GUI.enabled = objectTable && tableColumns.Count > 0;

            selectedColumnIndex = Mathf.Clamp(selectedColumnIndex, 0, Mathf.Max(0, tableColumns.Count - 1));

            if (tableColumns.Count > 0)
            {
                selectedColumnIndex = EditorGUILayout.Popup(
                    selectedColumnIndex,
                    tableColumns.ToArray(),
                    GUILayout.Width(160f));
            }

            if (GUILayout.Button("Remove Column", GUILayout.Width(120f)))
            {
                RemoveSelectedColumn();
            }

            GUI.enabled = true;

            GUILayout.FlexibleSpace();

            EditorGUILayout.LabelField(
                $"Rows: {tableRows.Count} | Columns: {tableColumns.Count}",
                EditorStyles.miniLabel,
                GUILayout.Width(160f));

            EditorGUILayout.EndHorizontal();
        }

        private void DrawInfoBox()
        {
            string absolutePath = ToAbsolutePath(selectedPath);
            FileInfo info = new FileInfo(absolutePath);

            if (!info.Exists)
            {
                EditorGUILayout.HelpBox("File does not exist.", MessageType.Warning);
                return;
            }

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            EditorGUILayout.LabelField(
                selectedTab == DataTab.Datasave ? "Source" : "Source",
                selectedTab == DataTab.Datasave ? "Datasave Payload" : "DataConfig JSON");

            EditorGUILayout.LabelField("Size", FormatBytes(info.Length));
            EditorGUILayout.LabelField("Last Write", info.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"));

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
                    paths.AddRange(Directory.GetFiles(directory).Where(IsVisibleSaveFile));
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
            rawScroll = Vector2.zero;
            tableScroll = Vector2.zero;

            try
            {
                loadedRawContent = File.ReadAllText(ToAbsolutePath(path));

                if (selectedTab == DataTab.Datasave)
                {
                    if (!TryExtractPayloadToken(loadedRawContent, out JToken payloadToken, out string payloadError))
                    {
                        content = string.Empty;
                        loadedContent = content;
                        workingToken = null;
                        isDirty = false;
                        SetStatus(payloadError, MessageType.Warning);
                        return;
                    }

                    content = payloadToken.ToString(Formatting.Indented);
                }
                else
                {
                    JToken json = JToken.Parse(loadedRawContent);
                    content = json.ToString(Formatting.Indented);
                }

                loadedContent = content;
                workingToken = JToken.Parse(content);
                isDirty = false;
                InvalidateTable();

                ValidateCurrentJson(false);
            }
            catch (JsonException exception)
            {
                content = selectedTab == DataTab.Datasave ? string.Empty : loadedRawContent;
                loadedContent = content;
                workingToken = null;
                isDirty = false;

                SetStatus(
                    selectedTab == DataTab.Datasave
                        ? "Cannot read datasave payload: " + exception.Message
                        : "Invalid JSON: " + exception.Message,
                    MessageType.Warning);
            }
            catch (Exception exception)
            {
                loadedRawContent = string.Empty;
                loadedContent = string.Empty;
                content = string.Empty;
                workingToken = null;
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

                string textToWrite = BuildFileContentForWrite(token);
                File.WriteAllText(ToAbsolutePath(selectedPath), textToWrite);

                if (selectedTab == DataTab.DataConfig)
                {
                    AssetDatabase.ImportAsset(selectedPath);
                    AssetDatabase.Refresh();
                }

                Select(selectedPath);
                SetStatus(selectedTab == DataTab.Datasave ? "Payload saved successfully." : "Saved successfully.", MessageType.Info);
            }
            catch (Exception exception)
            {
                SetStatus("Save failed: " + exception.Message, MessageType.Error);
            }
        }

        private string BuildFileContentForWrite(JToken payloadOrConfigToken)
        {
            if (selectedTab == DataTab.DataConfig)
            {
                return payloadOrConfigToken.ToString(Formatting.Indented);
            }

            try
            {
                JToken original = JToken.Parse(loadedRawContent);

                if (original is JObject envelope && envelope["Payload"] != null)
                {
                    bool originalPayloadWasString = envelope["Payload"].Type == JTokenType.String;

                    envelope["Payload"] = originalPayloadWasString
                        ? payloadOrConfigToken.ToString(Formatting.None)
                        : payloadOrConfigToken.DeepClone();

                    return envelope.ToString(Formatting.Indented);
                }
            }
            catch
            {
                // If the original save is not a normal envelope, fall back to writing payload directly.
            }

            return payloadOrConfigToken.ToString(Formatting.Indented);
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
            workingToken = JToken.Parse(content);
            isDirty = false;
            InvalidateTable();

            SetStatus("Reverted unsaved changes.", MessageType.Info);
        }

        private void PrettyPrintCurrent()
        {
            if (!TryParseContent(out JToken token, out string error))
            {
                SetStatus("Cannot pretty print. Invalid JSON: " + error, MessageType.Error);
                return;
            }

            content = token.ToString(Formatting.Indented);
            workingToken = token;
            isDirty = !string.Equals(content, loadedContent, StringComparison.Ordinal);
            InvalidateTable();

            SetStatus("Pretty printed JSON.", MessageType.Info);
        }

        private void MinifyCurrent()
        {
            if (!TryParseContent(out JToken token, out string error))
            {
                SetStatus("Cannot minify. Invalid JSON: " + error, MessageType.Error);
                return;
            }

            content = token.ToString(Formatting.None);
            workingToken = token;
            isDirty = !string.Equals(content, loadedContent, StringComparison.Ordinal);
            InvalidateTable();

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

            List<string> warnings = new List<string>();
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

        private bool TryExtractPayloadToken(string rawSave, out JToken payloadToken, out string error)
        {
            payloadToken = null;
            error = null;

            try
            {
                JToken saveToken = JToken.Parse(rawSave);

                if (saveToken is JObject envelope && envelope["Payload"] != null)
                {
                    JToken payload = envelope["Payload"];

                    if (payload.Type == JTokenType.String)
                    {
                        string payloadText = payload.Value<string>();

                        if (string.IsNullOrWhiteSpace(payloadText))
                        {
                            payloadToken = new JObject();
                            return true;
                        }

                        try
                        {
                            payloadToken = JToken.Parse(payloadText);
                            return true;
                        }
                        catch
                        {
                            payloadToken = new JValue(payloadText);
                            return true;
                        }
                    }

                    payloadToken = payload.DeepClone();
                    return true;
                }

                payloadToken = saveToken.DeepClone();
                return true;
            }
            catch (Exception exception)
            {
                error = "Cannot extract Payload from save file: " + exception.Message;
                return false;
            }
        }

        private bool EnsureWorkingTokenFromContent()
        {
            if (workingToken != null)
                return true;

            try
            {
                workingToken = JToken.Parse(content);
                return true;
            }
            catch (Exception exception)
            {
                SetStatus("Invalid JSON: " + exception.Message, MessageType.Error);
                return false;
            }
        }

        private void SyncContentFromWorkingToken()
        {
            if (workingToken == null)
                return;

            content = workingToken.ToString(Formatting.Indented);
            isDirty = !string.Equals(content, loadedContent, StringComparison.Ordinal);
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

        private void RebuildTableCandidates()
        {
            tableCandidates.Clear();

            if (workingToken == null)
                return;

            CollectArrayCandidates(workingToken, string.Empty, "$");

            if (selectedTableIndex >= tableCandidates.Count)
            {
                selectedTableIndex = 0;
                InvalidateTable();
            }
        }

        private void CollectArrayCandidates(JToken token, string pointer, string displayPath)
        {
            if (token is JArray array)
            {
                tableCandidates.Add(new TableCandidate(pointer, displayPath, array.Count));
            }

            if (token is JObject obj)
            {
                foreach (JProperty property in obj.Properties())
                {
                    string childPointer = pointer + "/" + EscapePointer(property.Name);
                    string childDisplay = displayPath == "$"
                        ? "$." + property.Name
                        : displayPath + "." + property.Name;

                    CollectArrayCandidates(property.Value, childPointer, childDisplay);
                }
            }
            else if (token is JArray childArray)
            {
                for (int i = 0; i < childArray.Count; i++)
                {
                    string childPointer = pointer + "/" + i;
                    string childDisplay = displayPath + "[" + i + "]";

                    CollectArrayCandidates(childArray[i], childPointer, childDisplay);
                }
            }
        }

        private void EnsureTableList()
        {
            if (tableList != null)
                return;

            JArray array = GetSelectedTableArray();

            tableRows = array != null
                ? array.Select(x => x).ToList()
                : new List<JToken>();

            RebuildTableColumns();

            tableList = new ReorderableList(tableRows, typeof(JToken), true, true, true, true)
            {
                elementHeight = RowHeight,
                drawHeaderCallback = DrawTableHeader,
                drawElementCallback = DrawTableRow,
                onAddCallback = _ => AddRow(),
                onRemoveCallback = _ => DeleteSelectedRow(),
                onReorderCallback = _ =>
                {
                    ApplyTableRowsToWorkingToken();
                    SyncContentFromWorkingToken();
                    SetStatus("Rows reordered.", MessageType.Info);
                },
                onSelectCallback = list => Repaint()
            };
        }

        private void DrawTableHeader(Rect rect)
        {
            Rect indexRect = new Rect(rect.x, rect.y, 46f, rect.height);
            EditorGUI.LabelField(indexRect, "#", EditorStyles.boldLabel);

            if (tableColumns.Count == 0)
            {
                EditorGUI.LabelField(
                    new Rect(rect.x + 50f, rect.y, rect.width - 50f, rect.height),
                    "No columns",
                    EditorStyles.boldLabel);
                return;
            }

            float x = rect.x + 50f;
            float width = Mathf.Max(MinCellWidth, (rect.width - 55f) / tableColumns.Count);

            for (int i = 0; i < tableColumns.Count; i++)
            {
                Rect cell = new Rect(x, rect.y, width - 4f, rect.height);
                EditorGUI.LabelField(cell, tableColumns[i], EditorStyles.boldLabel);
                x += width;
            }
        }

        private void DrawTableRow(Rect rect, int index, bool isActive, bool isFocused)
        {
            if (index < 0 || index >= tableRows.Count)
                return;

            Event evt = Event.current;

            if (evt.type == EventType.ContextClick && rect.Contains(evt.mousePosition))
            {
                tableList.index = index;
                ShowRowContextMenu(index);
                evt.Use();
            }

            Rect indexRect = new Rect(rect.x, rect.y + 2f, 44f, rect.height - 4f);
            EditorGUI.LabelField(indexRect, index.ToString());

            if (tableColumns.Count == 0)
                return;

            float x = rect.x + 50f;
            float width = Mathf.Max(MinCellWidth, (rect.width - 55f) / tableColumns.Count);

            for (int col = 0; col < tableColumns.Count; col++)
            {
                string column = tableColumns[col];
                Rect cell = new Rect(x, rect.y + 2f, width - 4f, rect.height - 4f);

                DrawCell(cell, index, column);

                x += width;
            }
        }

        private void DrawCell(Rect rect, int rowIndex, string column)
        {
            JToken row = tableRows[rowIndex];

            if (IsPrimitiveTable())
            {
                string oldValue = TokenToCellString(row);
                string newValue = EditorGUI.DelayedTextField(rect, oldValue);

                if (!string.Equals(oldValue, newValue, StringComparison.Ordinal))
                {
                    tableRows[rowIndex] = ParseCellValue(newValue, row);
                    ApplyTableRowsToWorkingToken();
                    SyncContentFromWorkingToken();
                }

                return;
            }

            if (row is not JObject obj)
            {
                string oldValue = TokenToCellString(row);
                string newValue = EditorGUI.DelayedTextField(rect, oldValue);

                if (!string.Equals(oldValue, newValue, StringComparison.Ordinal))
                {
                    tableRows[rowIndex] = ParseCellValue(newValue, row);
                    ApplyTableRowsToWorkingToken();
                    SyncContentFromWorkingToken();
                }

                return;
            }

            JToken current = obj[column];
            JToken template = current ?? GetColumnTemplateToken(column);

            string currentText = TokenToCellString(current);
            string nextText = EditorGUI.DelayedTextField(rect, currentText);

            if (!string.Equals(currentText, nextText, StringComparison.Ordinal))
            {
                obj[column] = ParseCellValue(nextText, template);
                SyncContentFromWorkingToken();
            }
        }

        private void RebuildTableColumns()
        {
            tableColumns.Clear();

            if (IsPrimitiveTable())
            {
                tableColumns.Add("value");
                return;
            }

            HashSet<string> names = new HashSet<string>();

            foreach (JObject obj in tableRows.OfType<JObject>())
            {
                foreach (JProperty property in obj.Properties())
                {
                    names.Add(property.Name);
                }
            }

            if (names.Contains("id"))
            {
                tableColumns.Add("id");
                names.Remove("id");
            }

            if (names.Contains("Id"))
            {
                tableColumns.Add("Id");
                names.Remove("Id");
            }

            tableColumns.AddRange(names.OrderBy(x => x, StringComparer.OrdinalIgnoreCase));
        }

        private bool IsPrimitiveTable()
        {
            if (tableRows.Count == 0)
                return false;

            return tableRows.All(row => row is JValue);
        }

        private bool IsObjectTable()
        {
            return tableRows.Count == 0 || tableRows.All(row => row is JObject);
        }

        private bool HasSelectedRow()
        {
            return tableList != null && tableList.index >= 0 && tableList.index < tableRows.Count;
        }

        private void AddRow()
        {
            EnsureTableList();

            JToken row = CreateNewRow();
            tableRows.Add(row);

            tableList.index = tableRows.Count - 1;

            ApplyTableRowsToWorkingToken();
            SyncContentFromWorkingToken();
            InvalidateTable();

            SetStatus("Row added.", MessageType.Info);
        }

        private void InsertRowAfterSelection()
        {
            EnsureTableList();

            int index = Mathf.Clamp(tableList.index, -1, tableRows.Count - 1);
            int insertIndex = index < 0 ? tableRows.Count : index + 1;

            tableRows.Insert(insertIndex, CreateNewRow());
            tableList.index = insertIndex;

            ApplyTableRowsToWorkingToken();
            SyncContentFromWorkingToken();
            InvalidateTable();

            SetStatus("Row inserted.", MessageType.Info);
        }

        private void DeleteSelectedRow()
        {
            if (!HasSelectedRow())
                return;

            int index = tableList.index;

            bool confirm = EditorUtility.DisplayDialog(
                WindowTitle,
                $"Delete row {index}?",
                "Delete",
                "Cancel");

            if (!confirm)
                return;

            tableRows.RemoveAt(index);
            tableList.index = Mathf.Clamp(index, 0, tableRows.Count - 1);

            ApplyTableRowsToWorkingToken();
            SyncContentFromWorkingToken();
            InvalidateTable();

            SetStatus("Row deleted.", MessageType.Warning);
        }

        private void DuplicateSelectedRow()
        {
            if (!HasSelectedRow())
                return;

            int index = tableList.index;
            JToken clone = tableRows[index].DeepClone();

            tableRows.Insert(index + 1, clone);
            tableList.index = index + 1;

            ApplyTableRowsToWorkingToken();
            SyncContentFromWorkingToken();
            InvalidateTable();

            SetStatus("Row duplicated.", MessageType.Info);
        }

        private void CopySelectedRow()
        {
            if (!HasSelectedRow())
                return;

            EditorGUIUtility.systemCopyBuffer = tableRows[tableList.index].ToString(Formatting.Indented);
            SetStatus("Copied row JSON to clipboard.", MessageType.Info);
        }

        private void PasteRowsFromClipboard()
        {
            EnsureTableList();

            string clipboard = EditorGUIUtility.systemCopyBuffer;

            if (string.IsNullOrWhiteSpace(clipboard))
            {
                SetStatus("Clipboard is empty.", MessageType.Warning);
                return;
            }

            List<JToken> rows = ParseRowsFromClipboard(clipboard);

            if (rows.Count == 0)
            {
                SetStatus("Clipboard does not contain valid JSON row data.", MessageType.Warning);
                return;
            }

            int insertIndex = HasSelectedRow()
                ? tableList.index + 1
                : tableRows.Count;

            tableRows.InsertRange(insertIndex, rows.Select(row => row.DeepClone()));
            tableList.index = insertIndex;

            ApplyTableRowsToWorkingToken();
            SyncContentFromWorkingToken();
            InvalidateTable();

            SetStatus($"Pasted {rows.Count} row(s).", MessageType.Info);
        }

        private List<JToken> ParseRowsFromClipboard(string clipboard)
        {
            List<JToken> rows = new List<JToken>();

            try
            {
                JToken token = JToken.Parse(clipboard);

                if (token is JArray array)
                {
                    rows.AddRange(array.Select(x => x.DeepClone()));
                }
                else
                {
                    rows.Add(token.DeepClone());
                }

                return rows;
            }
            catch
            {
                // Try TSV from spreadsheet.
            }

            string[] lines = clipboard
                .Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);

            foreach (string line in lines)
            {
                string[] cells = line.Split('\t');

                if (cells.Length == 0)
                    continue;

                if (IsPrimitiveTable() || tableColumns.Count == 1 && tableColumns[0] == "value")
                {
                    rows.Add(new JValue(cells[0]));
                    continue;
                }

                JObject obj = new JObject();

                for (int i = 0; i < tableColumns.Count && i < cells.Length; i++)
                {
                    string column = tableColumns[i];
                    JToken template = GetColumnTemplateToken(column);
                    obj[column] = ParseCellValue(cells[i], template);
                }

                rows.Add(obj);
            }

            return rows;
        }

        private void MoveSelectedRow(int direction)
        {
            if (!HasSelectedRow())
                return;

            int index = tableList.index;
            int nextIndex = index + direction;

            if (nextIndex < 0 || nextIndex >= tableRows.Count)
                return;

            (tableRows[index], tableRows[nextIndex]) = (tableRows[nextIndex], tableRows[index]);
            tableList.index = nextIndex;

            ApplyTableRowsToWorkingToken();
            SyncContentFromWorkingToken();
            InvalidateTable();

            SetStatus("Row moved.", MessageType.Info);
        }

        private void AddColumn(string columnName)
        {
            if (string.IsNullOrWhiteSpace(columnName))
            {
                SetStatus("Column name is empty.", MessageType.Warning);
                return;
            }

            columnName = columnName.Trim();

            if (tableColumns.Contains(columnName))
            {
                SetStatus($"Column already exists: {columnName}", MessageType.Warning);
                return;
            }

            foreach (JObject obj in tableRows.OfType<JObject>())
            {
                obj[columnName] = string.Empty;
            }

            if (tableRows.Count == 0)
            {
                tableColumns.Add(columnName);
            }

            newColumnName = string.Empty;

            SyncContentFromWorkingToken();
            InvalidateTable();

            SetStatus($"Column added: {columnName}", MessageType.Info);
        }

        private void RemoveSelectedColumn()
        {
            if (tableColumns.Count == 0)
                return;

            string column = tableColumns[Mathf.Clamp(selectedColumnIndex, 0, tableColumns.Count - 1)];

            bool confirm = EditorUtility.DisplayDialog(
                WindowTitle,
                $"Remove column '{column}' from all rows?",
                "Remove",
                "Cancel");

            if (!confirm)
                return;

            foreach (JObject obj in tableRows.OfType<JObject>())
            {
                obj.Remove(column);
            }

            SyncContentFromWorkingToken();
            InvalidateTable();

            SetStatus($"Column removed: {column}", MessageType.Warning);
        }

        private JToken CreateNewRow()
        {
            if (IsPrimitiveTable())
            {
                return new JValue(string.Empty);
            }

            JObject obj = new JObject();

            foreach (string column in tableColumns)
            {
                JToken template = GetColumnTemplateToken(column);
                obj[column] = CreateDefaultValueFromTemplate(template);
            }

            if (tableColumns.Count == 0)
            {
                obj["id"] = string.Empty;
            }

            return obj;
        }

        private JToken CreateDefaultValueFromTemplate(JToken template)
        {
            if (template == null)
                return string.Empty;

            return template.Type switch
            {
                JTokenType.Integer => new JValue(0),
                JTokenType.Float => new JValue(0f),
                JTokenType.Boolean => new JValue(false),
                JTokenType.Array => new JArray(),
                JTokenType.Object => new JObject(),
                JTokenType.Null => JValue.CreateNull(),
                _ => new JValue(string.Empty)
            };
        }

        private JToken GetColumnTemplateToken(string column)
        {
            foreach (JObject obj in tableRows.OfType<JObject>())
            {
                JToken token = obj[column];

                if (token != null && token.Type != JTokenType.Null)
                {
                    return token;
                }
            }

            return null;
        }

        private string TokenToCellString(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return string.Empty;

            if (token is JValue value)
            {
                return value.Value?.ToString() ?? string.Empty;
            }

            return token.ToString(Formatting.None);
        }

        private JToken ParseCellValue(string text, JToken template)
        {
            if (template == null)
            {
                return new JValue(text ?? string.Empty);
            }

            if (string.Equals(text, "null", StringComparison.OrdinalIgnoreCase))
            {
                return JValue.CreateNull();
            }

            try
            {
                switch (template.Type)
                {
                    case JTokenType.Integer:
                        if (long.TryParse(text, out long longValue))
                            return new JValue(longValue);
                        return new JValue(0);

                    case JTokenType.Float:
                        if (double.TryParse(text, out double doubleValue))
                            return new JValue(doubleValue);
                        return new JValue(0d);

                    case JTokenType.Boolean:
                        if (bool.TryParse(text, out bool boolValue))
                            return new JValue(boolValue);
                        return new JValue(false);

                    case JTokenType.Array:
                    case JTokenType.Object:
                        return JToken.Parse(text);

                    default:
                        return new JValue(text ?? string.Empty);
                }
            }
            catch
            {
                return new JValue(text ?? string.Empty);
            }
        }

        private void ApplyTableRowsToWorkingToken()
        {
            JArray newArray = new JArray(tableRows.Select(row => row.DeepClone()));
            ReplaceTokenAtPointer(workingToken, tableCandidates[selectedTableIndex].Pointer, newArray);

            tableRows.Clear();
            tableRows.AddRange(newArray.Select(x => x));
        }

        private JArray GetSelectedTableArray()
        {
            if (tableCandidates.Count == 0)
                return null;

            string pointer = tableCandidates[selectedTableIndex].Pointer;
            return ResolvePointer(workingToken, pointer) as JArray;
        }

        private void CopyCurrentArrayJson()
        {
            JArray array = GetSelectedTableArray();

            if (array == null)
                return;

            EditorGUIUtility.systemCopyBuffer = array.ToString(Formatting.Indented);
            SetStatus("Copied array JSON to clipboard.", MessageType.Info);
        }

        private void PasteCurrentArrayJson()
        {
            string clipboard = EditorGUIUtility.systemCopyBuffer;

            if (string.IsNullOrWhiteSpace(clipboard))
            {
                SetStatus("Clipboard is empty.", MessageType.Warning);
                return;
            }

            try
            {
                JToken token = JToken.Parse(clipboard);

                if (token is not JArray array)
                {
                    SetStatus("Clipboard JSON is not an array.", MessageType.Warning);
                    return;
                }

                bool confirm = EditorUtility.DisplayDialog(
                    WindowTitle,
                    "Replace current table array with clipboard array?",
                    "Replace",
                    "Cancel");

                if (!confirm)
                    return;

                ReplaceTokenAtPointer(workingToken, tableCandidates[selectedTableIndex].Pointer, array.DeepClone());

                SyncContentFromWorkingToken();
                InvalidateTable();

                SetStatus("Array replaced from clipboard.", MessageType.Info);
            }
            catch (Exception exception)
            {
                SetStatus("Paste array failed: " + exception.Message, MessageType.Error);
            }
        }

        private void ShowRowContextMenu(int index)
        {
            GenericMenu menu = new GenericMenu();

            menu.AddItem(new GUIContent("Copy Row"), false, CopySelectedRow);
            menu.AddItem(new GUIContent("Paste Row After"), false, PasteRowsFromClipboard);
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Duplicate Row"), false, DuplicateSelectedRow);
            menu.AddItem(new GUIContent("Insert Row After"), false, InsertRowAfterSelection);
            menu.AddItem(new GUIContent("Delete Row"), false, DeleteSelectedRow);
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Move Up"), false, () => MoveSelectedRow(-1));
            menu.AddItem(new GUIContent("Move Down"), false, () => MoveSelectedRow(1));

            menu.ShowAsContext();
        }

        private void InvalidateTable()
        {
            tableList = null;
            tableRows = new List<JToken>();
            tableColumns.Clear();
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
            workingToken = null;
            isDirty = false;
            rawScroll = Vector2.zero;
            tableScroll = Vector2.zero;
            selectedTableIndex = 0;
            selectedColumnIndex = 0;
            InvalidateTable();
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

        private void HandleShortcuts()
        {
            Event evt = Event.current;

            if (evt == null || evt.type != EventType.KeyDown)
                return;

            bool command = evt.control || evt.command;

            if (command && evt.keyCode == KeyCode.S)
            {
                if (isDirty)
                {
                    SaveCurrentJson();
                    evt.Use();
                }
            }

            if (viewMode != JsonViewMode.Table)
                return;

            if (command && evt.keyCode == KeyCode.D)
            {
                DuplicateSelectedRow();
                evt.Use();
            }

            if (evt.keyCode == KeyCode.Delete)
            {
                DeleteSelectedRow();
                evt.Use();
            }
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

        private static string EscapePointer(string value)
        {
            return value.Replace("~", "~0").Replace("/", "~1");
        }

        private static string UnescapePointer(string value)
        {
            return value.Replace("~1", "/").Replace("~0", "~");
        }

        private static JToken ResolvePointer(JToken root, string pointer)
        {
            if (root == null)
                return null;

            if (string.IsNullOrEmpty(pointer))
                return root;

            string[] parts = pointer.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            JToken current = root;

            foreach (string rawPart in parts)
            {
                string part = UnescapePointer(rawPart);

                if (current is JObject obj)
                {
                    current = obj[part];
                }
                else if (current is JArray array && int.TryParse(part, out int index))
                {
                    current = index >= 0 && index < array.Count ? array[index] : null;
                }
                else
                {
                    return null;
                }

                if (current == null)
                    return null;
            }

            return current;
        }

        private static void ReplaceTokenAtPointer(JToken root, string pointer, JToken replacement)
        {
            if (root == null)
                return;

            if (string.IsNullOrEmpty(pointer))
            {
                root.Replace(replacement);
                return;
            }

            string[] parts = pointer.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 0)
                return;

            JToken parent = root;

            for (int i = 0; i < parts.Length - 1; i++)
            {
                string part = UnescapePointer(parts[i]);

                if (parent is JObject obj)
                {
                    parent = obj[part];
                }
                else if (parent is JArray array && int.TryParse(part, out int index))
                {
                    parent = array[index];
                }
            }

            string last = UnescapePointer(parts[^1]);

            if (parent is JObject parentObj)
            {
                parentObj[last] = replacement;
            }
            else if (parent is JArray parentArray && int.TryParse(last, out int index))
            {
                parentArray[index] = replacement;
            }
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

        private enum JsonViewMode
        {
            Raw,
            Table
        }

        private readonly struct TableCandidate
        {
            public readonly string Pointer;
            public readonly string DisplayName;
            public readonly int Count;

            public TableCandidate(string pointer, string displayName, int count)
            {
                Pointer = pointer;
                DisplayName = displayName;
                Count = count;
            }
        }
    }
}