
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
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
        private const string DataConfigMarker = "/Resources/DataConfig/";
        private const string DefaultDataConfigFolder = "Assets/_Project/Resources/DataConfig";

        private const float ToolbarHeight = 48f;
        private const float StatusHeight = 22f;
        private const float SidebarWidthMin = 190f;
        private const float SidebarWidthMax = 420f;
        private const float SplitterWidth = 4f;
        private const float RowHeight = 28f;
        private const float RowNumberWidth = 50f;
        private const float ActionWidth = 106f;
        private const float CellMinWidth = 96f;
        private const float CellDefaultWidth = 150f;
        private const float CellMaxWidth = 420f;

        private enum SourceMode
        {
            DataConfig,
            Datasave
        }

        private enum ViewMode
        {
            Visual,
            Text
        }

        private sealed class FileEntry
        {
            public string FullPath;
            public string AssetPath;
            public string RelativePath;
            public string DisplayName;
            public bool Dirty;
        }

        private float sidebarWidth = 270f;
        private SourceMode sourceMode = SourceMode.DataConfig;
        private ViewMode viewMode = ViewMode.Visual;

        private readonly List<FileEntry> files = new List<FileEntry>();
        private int selectedFileIndex = -1;
        private string fileSearch = "";
        private Vector2 sidebarScroll;

        private sealed class InspectionFrame
        {
            public JToken Token;
            public JValue EncodedSource;
        }

        private const int InlinePageSize = 12;
        private readonly HashSet<JToken> expandedValues = new HashSet<JToken>();
        private readonly Dictionary<JArray, string> inlinePaths = new Dictionary<JArray, string>();
        private readonly Dictionary<JArray, int> inlinePages = new Dictionary<JArray, int>();
        private readonly Dictionary<string, float[]> tableRowOffsets = new Dictionary<string, float[]>();
        private readonly List<InspectionFrame> inspection = new List<InspectionFrame>();
        private readonly Dictionary<string, JArray> drawnArrays = new Dictionary<string, JArray>();
        private readonly Dictionary<JArray, List<string>> cachedColumns = new Dictionary<JArray, List<string>>();
        private readonly Dictionary<JArray, List<int>> cachedRows = new Dictionary<JArray, List<int>>();
        private readonly Dictionary<JArray, string> cachedFilters = new Dictionary<JArray, string>();
        private JToken rootToken;
        private string editText = "";
        private string originalRawFile = "";
        private bool originalPayloadWasString;
        private bool isDirty;
        private bool parseError;
        private string parseErrorMessage = "";
        private bool autoBackup = true;

        private readonly HashSet<string> collapsed = new HashSet<string>();
        private readonly Dictionary<string, Vector2> tableScroll = new Dictionary<string, Vector2>();
        private readonly Dictionary<string, string> tableFilter = new Dictionary<string, string>();
        private readonly Dictionary<string, float> columnWidths = new Dictionary<string, float>();
        private string resizingColumnKey = "";

        private string selectedTablePath = "";
        private int selectedRowIndex = -1;
        private string selectedColumnKey = "";
        private int selectedCellRow = -1;

        private JToken copiedRow;
        private string copiedRowText = "";

        private string draggingTablePath = "";
        private int draggingRowIndex = -1;
        private int dragTargetVisualIndex = -1;
        private bool isDraggingRow;
        private Vector2 dragStartMouse;
        private int pendingMoveFrom = -1;
        private int pendingMoveTarget = -1;
        private string pendingMoveTable = "";

        private Vector2 visualScroll;
        private Vector2 textScroll;
        private string status = "";
        private MessageType statusType = MessageType.None;

        private GUIStyle textStyle;
        private GUIStyle cellStyle;
        private GUIStyle headerStyle;
        private GUIStyle rowNumberStyle;
        private GUIStyle smallButtonStyle;

        [MenuItem("Tools/Dreamy/Data Debugger")]
        public static void Open()
        {
            DreamyDataDebuggerWindow window = GetWindow<DreamyDataDebuggerWindow>();
            window.titleContent = new GUIContent(WindowTitle);
            window.minSize = new Vector2(980f, 560f);
            window.Show();
        }

        private void OnEnable()
        {
            RefreshFiles(false);
        }

        private void OnGUI()
        {
            InitStyles();
            HandleKeyboard();
            HandleColumnResizeDrag();
            DrawToolbar();

            float bodyY = ToolbarHeight;
            float bodyH = position.height - ToolbarHeight - StatusHeight;

            DrawSidebar(new Rect(0, bodyY, sidebarWidth, bodyH));
            DrawSplitter(new Rect(sidebarWidth, bodyY, SplitterWidth, bodyH));
            DrawContent(new Rect(sidebarWidth + SplitterWidth, bodyY, position.width - sidebarWidth - SplitterWidth, bodyH));
            DrawStatus(new Rect(0, position.height - StatusHeight, position.width, StatusHeight));
        }


        private void HandleKeyboard()
        {
            Event e = Event.current;
            if (e == null || e.type != EventType.KeyDown)
                return;

            bool command = e.control || e.command;

            if (command && e.keyCode == KeyCode.S)
            {
                RequestSave();
                e.Use();
                return;
            }

            if (e.keyCode == KeyCode.Escape)
            {
                selectedTablePath = "";
                selectedRowIndex = -1;
                selectedColumnKey = "";
                selectedCellRow = -1;
                GUI.FocusControl(null);
                Repaint();
                e.Use();
                return;
            }

            // Text controls own copy/paste, delete and cursor movement while editing.
            if (EditorGUIUtility.editingTextField || viewMode == ViewMode.Text)
                return;

            JArray selectedArray = GetSelectedArray();
            if (selectedArray == null || selectedRowIndex < 0 || selectedRowIndex >= selectedArray.Count)
                return;

            if (command && e.keyCode == KeyCode.C)
            {
                CopyRow(selectedArray, selectedRowIndex);
                e.Use();
                return;
            }

            if (command && e.keyCode == KeyCode.V)
            {
                PasteRow(selectedArray, selectedTablePath, selectedRowIndex + 1);
                e.Use();
                return;
            }

            if (command && e.keyCode == KeyCode.D)
            {
                DuplicateRow(selectedArray, selectedTablePath, selectedRowIndex);
                e.Use();
                return;
            }

            if (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace)
            {
                DeleteRow(selectedArray, selectedRowIndex);
                e.Use();
                return;
            }

            if (command && e.keyCode == KeyCode.UpArrow)
            {
                MoveRowTo(selectedArray, selectedRowIndex, selectedRowIndex - 1);
                e.Use();
                return;
            }

            if (command && e.keyCode == KeyCode.DownArrow)
            {
                MoveRowTo(selectedArray, selectedRowIndex, selectedRowIndex + 2);
                e.Use();
                return;
            }

            if (!command && e.keyCode == KeyCode.UpArrow)
            {
                selectedRowIndex = Mathf.Clamp(selectedRowIndex - 1, 0, selectedArray.Count - 1);
                Repaint();
                e.Use();
                return;
            }

            if (!command && e.keyCode == KeyCode.DownArrow)
            {
                selectedRowIndex = Mathf.Clamp(selectedRowIndex + 1, 0, selectedArray.Count - 1);
                Repaint();
                e.Use();
            }
        }

        private JArray GetSelectedArray()
        {
            if (rootToken == null || string.IsNullOrEmpty(selectedTablePath))
                return null;

            return FindArrayByPath(rootToken, selectedTablePath);
        }

        private JArray FindArrayByPath(JToken root, string path)
        {
            if (root == null || string.IsNullOrEmpty(path))
                return null;

            if (drawnArrays.TryGetValue(path, out JArray array)) return array;
            return path == "root" ? root as JArray : null;
        }


        private void InitStyles()
        {
            if (textStyle != null) return;

            Font mono = GetMonoFont();

            textStyle = new GUIStyle(EditorStyles.textArea)
            {
                font = mono,
                fontSize = 13,
                wordWrap = false
            };

            cellStyle = new GUIStyle(EditorStyles.textField)
            {
                font = mono,
                fontSize = 12,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(4, 4, 2, 2)
            };

            headerStyle = new GUIStyle(EditorStyles.label)
            {
                font = mono,
                fontSize = 12,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(5, 4, 0, 0)
            };

            rowNumberStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            {
                alignment = TextAnchor.MiddleCenter
            };

            smallButtonStyle = new GUIStyle(EditorStyles.miniButton)
            {
                fontSize = 11,
                padding = new RectOffset(2, 2, 2, 2)
            };
        }

        private static Font GetMonoFont()
        {
            HashSet<string> installed = new HashSet<string>(Font.GetOSInstalledFontNames(), StringComparer.OrdinalIgnoreCase);
            string[] names = { "Consolas", "Courier New", "Courier", "Lucida Console", "Monaco", "Menlo" };
            foreach (string name in names)
            {
                if (installed.Contains(name))
                    return Font.CreateDynamicFontFromOSFont(name, 13);
            }

            return EditorStyles.textArea.font;
        }

        private void HandleColumnResizeDrag()
        {
            if (string.IsNullOrEmpty(resizingColumnKey))
                return;

            Event e = Event.current;
            if (e == null)
                return;

            if (e.type == EventType.MouseDrag)
            {
                float current = columnWidths.TryGetValue(resizingColumnKey, out float width) ? width : CellDefaultWidth;
                columnWidths[resizingColumnKey] = Mathf.Clamp(current + e.delta.x, CellMinWidth, CellMaxWidth);
                e.Use();
                Repaint();
            }
            else if (e.type == EventType.MouseUp || e.rawType == EventType.MouseUp)
            {
                resizingColumnKey = "";
                e.Use();
                Repaint();
            }
        }

        private void DrawToolbar()
        {
            GUILayout.BeginArea(new Rect(0, 0, position.width, ToolbarHeight));
            GUILayout.BeginHorizontal(EditorStyles.toolbar);

            SourceMode nextMode = (SourceMode)GUILayout.Toolbar(
                (int)sourceMode,
                new[] { "Data Config", "Datasave" },
                EditorStyles.toolbarButton,
                GUILayout.Width(220f));

            if (nextMode != sourceMode && ConfirmLeaveDirtyFile())
            {
                sourceMode = nextMode;
                ClearCurrentFile();
                RefreshFiles(false);
            }

            if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(72f)))
            {
                if (ConfirmLeaveDirtyFile())
                    RefreshFiles(true);
            }

            using (new EditorGUI.DisabledScope(!HasSelectedFile()))
            {
                if (GUILayout.Button("Save", EditorStyles.toolbarButton, GUILayout.Width(56f)))
                    RequestSave();

                if (GUILayout.Button("Reload", EditorStyles.toolbarButton, GUILayout.Width(64f)))
                    ReloadCurrent();

                if (GUILayout.Button(viewMode == ViewMode.Visual ? "Text" : "Visual", EditorStyles.toolbarButton, GUILayout.Width(62f)))
                    SwitchView();

                if (GUILayout.Button("Format", EditorStyles.toolbarButton, GUILayout.Width(64f)))
                    FormatJson();

                if (GUILayout.Button("Validate", EditorStyles.toolbarButton, GUILayout.Width(72f)))
                    ValidateCurrent();

                if (sourceMode == SourceMode.Datasave && GUILayout.Button(new GUIContent("Reset Save", "Delete this test save and its backups without creating a backup. Stop Play Mode first."), EditorStyles.toolbarButton, GUILayout.Width(82f)))
                    DeleteSave(CurrentFile);

                if (GUILayout.Button("Reveal", EditorStyles.toolbarButton, GUILayout.Width(62f)))
                    RevealCurrent();
            }

            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal(EditorStyles.toolbar);
            if (sourceMode == SourceMode.DataConfig)
            {
                if (GUILayout.Button("New Config", EditorStyles.toolbarButton, GUILayout.Width(90f)))
                    CreateNewConfig();

                if (GUILayout.Button("Open Folder", EditorStyles.toolbarButton, GUILayout.Width(92f)))
                    OpenDataConfigFolder();
            }
            else
            {
                if (GUILayout.Button("Open Saves", EditorStyles.toolbarButton, GUILayout.Width(86f)))
                    OpenSaveFolder();

                if (GUILayout.Button("Reset Saves", EditorStyles.toolbarButton, GUILayout.Width(94f)))
                    DeleteAllSaves();
            }

            GUILayout.FlexibleSpace();
            autoBackup = GUILayout.Toggle(autoBackup, new GUIContent("Backup on Save", "Create an Editor backup before saving edits. Reset Save never creates backups. Runtime backup is independent."), EditorStyles.toolbarButton, GUILayout.Width(116f));

            if (isDirty)
                GUILayout.Label("Unsaved", EditorStyles.toolbarButton, GUILayout.Width(70f));

            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        private void DrawSidebar(Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(0.17f, 0.17f, 0.18f));

            Rect searchRect = new Rect(rect.x + 5, rect.y + 5, rect.width - 10, 20);
            fileSearch = EditorGUI.TextField(searchRect, fileSearch, EditorStyles.toolbarSearchField);

            Rect listRect = new Rect(rect.x, rect.y + 30, rect.width, rect.height - 30);
            GUILayout.BeginArea(listRect);
            sidebarScroll = GUILayout.BeginScrollView(sidebarScroll);

            for (int i = 0; i < files.Count; i++)
            {
                FileEntry file = files[i];
                if (!MatchesFileSearch(file)) continue;

                Rect itemRect = GUILayoutUtility.GetRect(listRect.width, 26f, GUILayout.ExpandWidth(true));
                bool selected = i == selectedFileIndex;

                if (selected)
                    EditorGUI.DrawRect(itemRect, new Color(0.23f, 0.43f, 0.76f, 0.95f));
                else if (itemRect.Contains(Event.current.mousePosition))
                    EditorGUI.DrawRect(itemRect, new Color(0.30f, 0.30f, 0.32f, 0.55f));

                string label = (file.Dirty ? "* " : "  ") + file.DisplayName;
                GUI.Label(new Rect(itemRect.x + 4, itemRect.y + 4, itemRect.width - 8, 18), label, selected ? EditorStyles.whiteLabel : EditorStyles.label);

                if (Event.current.type == EventType.MouseDown && itemRect.Contains(Event.current.mousePosition))
                {
                    if (Event.current.button == 0)
                        SelectFile(i);
                    else if (Event.current.button == 1)
                        ShowFileContextMenu(i);

                    Event.current.Use();
                }
            }

            if (files.Count == 0)
            {
                GUILayout.Space(12);
                GUILayout.Label(sourceMode == SourceMode.DataConfig ? "No config JSON found." : "No save file found.", EditorStyles.centeredGreyMiniLabel);
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawSplitter(Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(0.08f, 0.08f, 0.08f));
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.ResizeHorizontal);

            Event evt = Event.current;
            if (evt.type == EventType.MouseDrag && rect.Contains(evt.mousePosition))
            {
                sidebarWidth = Mathf.Clamp(sidebarWidth + evt.delta.x, SidebarWidthMin, SidebarWidthMax);
                evt.Use();
                Repaint();
            }
        }

        private void DrawContent(Rect rect)
        {
            EditorGUI.DrawRect(rect, new Color(0.14f, 0.14f, 0.15f));

            if (!HasSelectedFile())
            {
                GUI.Label(rect, sourceMode == SourceMode.DataConfig
                        ? "Select a JSON config from Resources/DataConfig."
                        : "Select a save file. Only Payload will be displayed.",
                    new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 14 });
                return;
            }

            Rect titleRect = new Rect(rect.x, rect.y, rect.width, 24f);
            EditorGUI.DrawRect(titleRect, new Color(0.15f, 0.15f, 0.19f));
            GUI.Label(new Rect(titleRect.x + 8, titleRect.y + 4, titleRect.width - 16, 18), CurrentFile.RelativePath, EditorStyles.miniLabel);

            Rect body = new Rect(rect.x, rect.y + 25, rect.width, rect.height - 25);

            if (viewMode == ViewMode.Text)
                DrawTextView(body);
            else
                DrawVisualView(body);
        }

        private void DrawTextView(Rect rect)
        {
            GUILayout.BeginArea(rect);
            textScroll = GUILayout.BeginScrollView(textScroll);

            EditorGUI.BeginChangeCheck();
            editText = GUILayout.TextArea(editText, textStyle, GUILayout.ExpandHeight(true), GUILayout.ExpandWidth(true));
            if (EditorGUI.EndChangeCheck())
                MarkDirty();

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private void DrawVisualView(Rect rect)
        {
            GUILayout.BeginArea(rect);
            visualScroll = GUILayout.BeginScrollView(visualScroll);

            if (parseError)
            {
                EditorGUILayout.HelpBox("JSON parse error: " + parseErrorMessage, MessageType.Error);
                if (GUILayout.Button("Switch To Text View", GUILayout.Width(160f)))
                    viewMode = ViewMode.Text;

                GUILayout.EndScrollView();
                GUILayout.EndArea();
                return;
            }

            if (rootToken == null)
            {
                GUILayout.EndScrollView();
                GUILayout.EndArea();
                return;
            }

            JToken displayed = rootToken;
            if (displayed is JObject obj)
            {
                DrawObjectSection(obj, "root" + displayed.Path, "root");
            }
            else if (displayed is JArray arr)
            {
                DrawArraySection(arr, "root" + displayed.Path, "root", true);
            }
            else
            {
                DrawRootScalar();
            }

            GUILayout.Space(24);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        private JToken ExpandedContent(JToken token)
        {
            if (token == null || !expandedValues.Contains(token)) return null;
            if (token is JContainer) return token;
            return inspection.FirstOrDefault(frame => ReferenceEquals(frame.EncodedSource, token))?.Token;
        }

        private void ToggleInline(JToken token)
        {
            if (!expandedValues.Remove(token))
            {
                if (token.Type == JTokenType.String && !inspection.Any(frame => ReferenceEquals(frame.EncodedSource, token)))
                {
                    try
                    {
                        JToken decoded = JToken.Parse(token.Value<string>());
                        if (!(decoded is JContainer)) return;
                        inspection.Add(new InspectionFrame { Token = decoded, EncodedSource = (JValue)token });
                    }
                    catch (JsonException ex) { SetStatus("Invalid embedded JSON: " + ex.Message, MessageType.Error); return; }
                }
                expandedValues.Add(token);
            }
            selectedTablePath = "";
            selectedRowIndex = -1;
            GUI.FocusControl(null);
            Repaint();
            GUIUtility.ExitGUI(); // Recalculate variable row heights on the next layout pass.
        }

        private int InlinePageStart(JArray array)
        {
            int page = inlinePages.TryGetValue(array, out int saved) ? saved : 0;
            page = Math.Max(0, Math.Min(page, Math.Max(0, (array.Count - 1) / InlinePageSize)));
            inlinePages[array] = page;
            return page * InlinePageSize;
        }

        private float ExpandedHeight(JToken source, int depth = 0)
        {
            JToken content = ExpandedContent(source);
            return content == null ? 0 : InlineHeight(content, depth) + 8;
        }

        private float InlineHeight(JToken content, int depth)
        {
            if (depth >= 10) return RowHeight;
            float height = RowHeight; // Title and list actions.
            if (content is JObject obj)
            {
                foreach (JProperty property in obj.Properties())
                    height += RowHeight + ExpandedHeight(property.Value, depth + 1);
            }
            else if (content is JArray array)
            {
                bool objects = IsObjectTable(array);
                List<string> columns = objects ? GetColumns(array) : new List<string> { "value" };
                if (array.Count == 0) return height + RowHeight;
                height += RowHeight; // Column headers.
                int start = InlinePageStart(array);
                for (int i = start; i < Math.Min(array.Count, start + InlinePageSize); i++)
                {
                    height += RowHeight;
                    if (objects && array[i] is JObject row)
                        foreach (string column in columns) height += (columns.Count > 4 ? RowHeight : 0) + ExpandedHeight(row[column], depth + 1);
                    else height += ExpandedHeight(array[i], depth + 1);
                }
            }
            return height;
        }

        private float TableRowHeight(JToken row, bool objectTable)
        {
            float height = RowHeight + 1;
            if (objectTable && row is JObject obj)
                foreach (JProperty property in obj.Properties()) height += ExpandedHeight(property.Value);
            else height += ExpandedHeight(row);
            return height;
        }

        private float DrawExpanded(Rect area, JToken source, string label, int depth = 0)
        {
            JToken content = ExpandedContent(source);
            if (content == null) return 0;
            float height = InlineHeight(content, depth);
            Rect panel = new Rect(area.x + 12, area.y + 4, Math.Max(120, area.width - 20), height);
            EditorGUI.DrawRect(panel, new Color(0.12f, 0.15f, 0.20f));
            EditorGUI.DrawRect(new Rect(panel.x, panel.y, 3, panel.height), new Color(0.30f, 0.60f, 0.85f));
            if (depth >= 10)
            {
                GUI.Label(new Rect(panel.x + 8, panel.y + 4, panel.width - 16, 20), "Maximum inline depth. Use Edit JSON for deeper data.", EditorStyles.miniLabel);
                return height + 8;
            }
            Rect title = new Rect(panel.x + 8, panel.y + 3, panel.width - 16, RowHeight - 6);
            GUI.Label(new Rect(title.x, title.y, Math.Max(0, title.width - (content is JArray ? 210 : 32)), title.height), new GUIContent(label + (content is JArray list ? " · " + list.Count + " items" : " · object"), label), EditorStyles.boldLabel);
            if (GUI.Button(new Rect(title.xMax - 24, title.y, 24, title.height), new GUIContent("▾", "Collapse this section"), smallButtonStyle)) ToggleInline(source);
            float y = panel.y + RowHeight;
            if (content is JObject obj)
            {
                foreach (JProperty property in obj.Properties().ToList())
                {
                    float keyWidth = Math.Min(180, panel.width * 0.3f);
                    GUI.Label(new Rect(panel.x + 10, y + 4, keyWidth - 12, 20), property.Name, EditorStyles.miniLabel);
                    DrawValueField(property.Value, new Rect(panel.x + keyWidth, y + 2, panel.width - keyWidth - 8, RowHeight - 4), value => { property.Value = value; OnTokenChanged(); });
                    y += RowHeight;
                    y += DrawExpanded(new Rect(panel.x + 8, y, panel.width - 16, 0), property.Value, property.Name, depth + 1);
                }
            }
            else if (content is JArray array)
            {
                if (!inlinePaths.TryGetValue(array, out string inlinePath))
                {
                    inlinePath = "inline:" + inlinePaths.Count;
                    inlinePaths[array] = inlinePath;
                }
                drawnArrays[inlinePath] = array;
                int start = InlinePageStart(array);
                int page = start / InlinePageSize;
                int pages = Math.Max(1, (array.Count + InlinePageSize - 1) / InlinePageSize);
                GUI.Label(new Rect(title.xMax - 202, title.y + 3, 50, 18), (page + 1) + " / " + pages, EditorStyles.miniLabel);
                using (new EditorGUI.DisabledScope(page == 0))
                    if (GUI.Button(new Rect(title.xMax - 148, title.y, 26, title.height), "<", smallButtonStyle)) { inlinePages[array] = page - 1; GUIUtility.ExitGUI(); }
                using (new EditorGUI.DisabledScope(page + 1 >= pages))
                    if (GUI.Button(new Rect(title.xMax - 118, title.y, 26, title.height), ">", smallButtonStyle)) { inlinePages[array] = page + 1; GUIUtility.ExitGUI(); }
                if (GUI.Button(new Rect(title.xMax - 88, title.y, 60, title.height), "+ Row", smallButtonStyle))
                {
                    AddRow(array, inlinePath);
                    inlinePages[array] = (array.Count - 1) / InlinePageSize;
                    GUIUtility.ExitGUI();
                }
                if (array.Count == 0)
                {
                    GUI.Label(new Rect(panel.x + 10, y + 4, panel.width - 20, 20), "Empty list — use + Row to add an item.", EditorStyles.miniLabel);
                    return height + 8;
                }
                bool objects = IsObjectTable(array);
                List<string> columns = objects ? GetColumns(array) : new List<string> { "value" };
                // Wide items use field/value rows instead of squeezing many columns into the panel.
                bool verticalFields = columns.Count > 4;
                float cellWidth = Math.Max(24, (panel.width - 82) / Math.Max(1, columns.Count));
                GUI.Label(new Rect(panel.x + 8, y + 4, 32, 20), "#", EditorStyles.miniLabel);
                if (verticalFields)
                    GUI.Label(new Rect(panel.x + 42, y + 4, panel.width - 50, 20), "Item fields", headerStyle);
                else
                    for (int c = 0; c < columns.Count; c++)
                        GUI.Label(new Rect(panel.x + 42 + c * cellWidth, y + 4, cellWidth - 4, 20), new GUIContent(columns[c], columns[c]), headerStyle);
                y += RowHeight;
                for (int i = start; i < Math.Min(array.Count, start + InlinePageSize); i++)
                {
                    int index = i;
                    JToken row = array[index];
                    EditorGUI.DrawRect(new Rect(panel.x + 6, y, panel.width - 12, RowHeight), selectedTablePath == inlinePath && selectedRowIndex == index ? new Color(0.23f, 0.43f, 0.76f) :
                        (index % 2 == 0 ? new Color(0.16f, 0.19f, 0.24f) : new Color(0.19f, 0.22f, 0.27f)));
                    GUI.Label(new Rect(panel.x + 8, y + 4, 32, 20), (index + 1).ToString(), EditorStyles.miniLabel);
                    if (!verticalFields || !(row is JObject))
                    for (int c = 0; c < columns.Count; c++)
                    {
                        string key = columns[c];
                        JToken value = objects && row is JObject ? GetCell(row, key) : row;
                        DrawValueField(value, new Rect(panel.x + 42 + c * cellWidth, y + 2, row is JObject ? cellWidth - 4 : panel.width - 82, RowHeight - 4), edited =>
                        {
                            if (objects && array[index] is JObject target) target[key] = edited;
                            else array[index] = edited;
                            OnTokenChanged();
                        });
                        if (!(row is JObject)) break;
                    }
                    if (GUI.Button(new Rect(panel.xMax - 32, y + 2, 24, RowHeight - 4), new GUIContent("×", "Delete this list item"), smallButtonStyle))
                    {
                        DeleteRow(array, index);
                        GUIUtility.ExitGUI();
                    }
                    HandleRowContext(array, inlinePath, new Rect(panel.x + 6, y, panel.width - 12, RowHeight), index);
                    y += RowHeight;
                    if (objects && row is JObject rowObject)
                        foreach (string key in columns)
                        {
                            if (verticalFields)
                            {
                                float keyWidth = Math.Min(180, panel.width * 0.3f);
                                GUI.Label(new Rect(panel.x + 16, y + 4, keyWidth - 16, 20), new GUIContent(key, key), EditorStyles.miniLabel);
                                DrawValueField(rowObject[key], new Rect(panel.x + keyWidth, y + 2, panel.width - keyWidth - 8, RowHeight - 4), edited => { rowObject[key] = edited; OnTokenChanged(); });
                                y += RowHeight;
                            }
                            y += DrawExpanded(new Rect(panel.x + 8, y, panel.width - 16, 0), rowObject[key], key, depth + 1);
                        }
                    else y += DrawExpanded(new Rect(panel.x + 8, y, panel.width - 16, 0), row, "Item " + (index + 1), depth + 1);
                }
            }
            return height + 8;
        }

        private void DrawRootScalar()
        {
            Rect r = GUILayoutUtility.GetRect(0, RowHeight, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(r, new Color(0.18f, 0.18f, 0.22f));
            GUI.Label(new Rect(r.x + 8, r.y + 4, 80, 18), "value");
            DrawValueField(rootToken, new Rect(r.x + 92, r.y + 2, r.width - 100, RowHeight - 4), delegate(JToken token)
            {
                rootToken = token;
                OnTokenChanged();
            });
            float expandedHeight = ExpandedHeight(rootToken);
            if (expandedHeight > 0)
                DrawExpanded(GUILayoutUtility.GetRect(0, expandedHeight, GUILayout.ExpandWidth(true)), rootToken, "value");
        }

        private void DrawObject(JObject obj, string path)
        {
            foreach (JProperty prop in obj.Properties().ToList())
            {
                string childPath = path + "[" + JsonConvert.ToString(prop.Name) + "]";

                if (prop.Value is JArray arr)
                    DrawArraySection(arr, childPath, prop.Name, false);
                else if (prop.Value is JObject childObj)
                    DrawObjectSection(childObj, childPath, prop.Name);
                else
                    DrawPropertyRow(prop);
            }
        }

        private void DrawObjectSection(JObject obj, string path, string label)
        {
            bool isCollapsed = collapsed.Contains(path);
            Rect h = GUILayoutUtility.GetRect(0, 28, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(h, new Color(0.19f, 0.19f, 0.23f));
            if (GUI.Button(new Rect(h.x + 4, h.y + 5, 20, 18), isCollapsed ? ">" : "v", smallButtonStyle))
                ToggleCollapse(path);

            GUI.Label(new Rect(h.x + 30, h.y + 5, 260, 18), label + "  object", EditorStyles.boldLabel);

            if (GUI.Button(new Rect(h.xMax - 82, h.y + 5, 76, 18), "Add Field", smallButtonStyle))
            {
                StringInputWindow.Open("Add Field", "Field name", "", delegate(string field)
                {
                    field = (field ?? "").Trim();
                    if (field.Length > 0 && obj.Property(field) == null)
                    {
                        obj[field] = "";
                        OnTokenChanged();
                    }
                });
            }

            if (!isCollapsed)
                DrawObject(obj, path);
        }

        private void DrawPropertyRow(JProperty prop)
        {
            Rect r = GUILayoutUtility.GetRect(0, RowHeight, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(r, new Color(0.17f, 0.17f, 0.21f));
            GUI.Label(new Rect(r.x + 8, r.y + 4, 220, 18), prop.Name);
            DrawValueField(prop.Value, new Rect(r.x + 236, r.y + 2, r.width - 246, RowHeight - 4), delegate(JToken token)
            {
                prop.Value = token ?? JValue.CreateNull();
                OnTokenChanged();
            });
            float expandedHeight = ExpandedHeight(prop.Value);
            if (expandedHeight > 0)
            {
                Rect details = GUILayoutUtility.GetRect(0, expandedHeight, GUILayout.ExpandWidth(true));
                DrawExpanded(details, prop.Value, prop.Name);
            }
        }

        private void DrawArraySection(JArray arr, string path, string label, bool rootArray)
        {
            bool isCollapsed = !rootArray && collapsed.Contains(path);

            Rect h = GUILayoutUtility.GetRect(0, 30, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(h, new Color(0.19f, 0.19f, 0.23f));

            float x = h.x + 6;
            if (!rootArray)
            {
                if (GUI.Button(new Rect(x, h.y + 6, 20, 18), isCollapsed ? ">" : "v", smallButtonStyle))
                    ToggleCollapse(path);
                x += 26;
            }

            GUI.Label(new Rect(x, h.y + 6, 260, 18), label + "  rows: " + arr.Count, EditorStyles.boldLabel);

            float bx = h.xMax - 454;
            if (GUI.Button(new Rect(bx, h.y + 5, 68, 20), "Add Row", smallButtonStyle))
                AddRow(arr, path);
            bx += 72;

            if (GUI.Button(new Rect(bx, h.y + 5, 74, 20), "Add Field", smallButtonStyle))
                AskAddColumn(arr);
            bx += 78;

            if (GUI.Button(new Rect(bx, h.y + 5, 76, 20), "Paste Row", smallButtonStyle))
                PasteRow(arr, path, arr.Count);
            bx += 80;

            if (GUI.Button(new Rect(bx, h.y + 5, 76, 20), "Copy TSV", smallButtonStyle))
                CopyTsv(arr, GetColumns(arr), GetVisibleRows(arr, path));
            bx += 80;

            if (GUI.Button(new Rect(bx, h.y + 5, 66, 20), "CSV", smallButtonStyle))
                ShowCsvMenu(arr, path);
            bx += 70;

            if (GUI.Button(new Rect(bx, h.y + 5, 66, 20), "Sort", smallButtonStyle))
                ShowSortMenu(arr, path);

            if (!isCollapsed)
                DrawTable(arr, path);
        }

        private void DrawTable(JArray arr, string path)
        {
            drawnArrays[path] = arr;
            bool objectTable = IsObjectTable(arr);
            List<string> columns = objectTable ? GetColumns(arr) : new List<string> { "value" };
            List<int> visibleRows = GetVisibleRows(arr, path);

            DrawTableToolbar(arr, path, columns, visibleRows, objectTable);

            if (arr.Count == 0)
            {
                DrawEmptyTable(arr, path);
                return;
            }

            if (visibleRows.Count == 0)
            {
                EditorGUILayout.HelpBox("No row matches current filter.", MessageType.Info);
                return;
            }

            float[] widths = BuildColumnWidths(arr, path, columns, objectTable);
            float tableWidth = RowNumberWidth + ActionWidth + widths.Sum() + columns.Count * 2 + 8;

            if (!tableScroll.ContainsKey(path))
                tableScroll[path] = Vector2.zero;

            Vector2 sv = tableScroll[path];

            Rect header = GUILayoutUtility.GetRect(0, RowHeight, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(header, new Color(0.18f, 0.20f, 0.26f));

            GUI.BeginGroup(header);
            DrawTableHeader(-sv.x, header.width, arr, path, columns, widths);
            GUI.EndGroup();

            float[] offsets = new float[visibleRows.Count + 1];
            for (int i = 0; i < visibleRows.Count; i++) offsets[i + 1] = offsets[i] + TableRowHeight(arr[visibleRows[i]], objectTable);
            tableRowOffsets[path] = offsets;
            float bodyHeight = Mathf.Min(offsets[visibleRows.Count] + 4, 720f);
            Rect body = GUILayoutUtility.GetRect(0, Mathf.Max(44, bodyHeight), GUILayout.ExpandWidth(true));
            Rect inner = new Rect(0, 0, Mathf.Max(tableWidth, body.width), offsets[visibleRows.Count] + 4);

            sv = GUI.BeginScrollView(body, sv, inner, tableWidth > body.width, inner.height > body.height);

            int deleteRow = -1;
            int duplicateRow = -1;
            int convertRow = -1;

            // Only draw rows intersecting the viewport, with one row of overscan.
            int firstVisible = 0;
            while (firstVisible < visibleRows.Count && offsets[firstVisible + 1] < sv.y) firstVisible++;
            int lastVisible = firstVisible;
            while (lastVisible < visibleRows.Count && offsets[lastVisible] <= sv.y + body.height) lastVisible++;
            for (int visualIndex = firstVisible; visualIndex < lastVisible; visualIndex++)
            {
                int rowIndex = visibleRows[visualIndex];
                Rect rowRect = new Rect(0, offsets[visualIndex], inner.width, RowHeight);
                bool selected = selectedTablePath == path && selectedRowIndex == rowIndex;

                EditorGUI.DrawRect(rowRect, selected ? new Color(0.23f, 0.43f, 0.76f, 0.95f) :
                    (rowIndex % 2 == 0 ? new Color(0.155f, 0.155f, 0.19f) : new Color(0.18f, 0.18f, 0.22f)));

                DrawRowActions(arr, path, rowRect, rowIndex, visualIndex, visibleRows, ref deleteRow, ref duplicateRow);

                float x = RowNumberWidth + ActionWidth;
                JToken rowToken = arr[rowIndex];
                JObject rowObject = rowToken as JObject;

                if (objectTable)
                {
                    if (rowObject == null)
                    {
                        Rect valueRect = new Rect(x + 1, rowRect.y + 2, Mathf.Max(260, rowRect.width - x - 96), RowHeight - 4);
                        DrawValueField(rowToken, valueRect, delegate(JToken token)
                        {
                            arr[rowIndex] = token ?? JValue.CreateNull();
                            SelectRow(path, rowIndex);
                            OnTokenChanged();
                        });

                        if (GUI.Button(new Rect(valueRect.xMax + 4, rowRect.y + 3, 84, RowHeight - 6), "To Object", smallButtonStyle))
                            convertRow = rowIndex;
                    }
                    else
                    {
                        for (int c = 0; c < columns.Count; c++)
                        {
                            string key = columns[c];
                            Rect cell = new Rect(x + 1, rowRect.y + 2, widths[c] - 2, RowHeight - 4);
                            JToken current = GetCell(rowToken, key);

                            DrawValueField(current, cell, delegate(JToken token)
                            {
                                SetCellNoNotify(arr, rowIndex, key, token ?? JValue.CreateNull());
                                selectedCellRow = rowIndex;
                                selectedColumnKey = key;
                                SelectRow(path, rowIndex);
                                OnTokenChanged();
                            });

                            HandleCellContext(arr, rowIndex, key, current, cell);
                            x += widths[c] + 2;
                        }
                    }
                }
                else
                {
                    Rect cell = new Rect(x + 1, rowRect.y + 2, widths[0] - 2, RowHeight - 4);
                    DrawValueField(rowToken, cell, delegate(JToken token)
                    {
                        arr[rowIndex] = token ?? JValue.CreateNull();
                        SelectRow(path, rowIndex);
                        OnTokenChanged();
                    });

                    HandleCellContext(arr, rowIndex, "value", rowToken, cell);
                }

                HandleRowContext(arr, path, rowRect, rowIndex);
                float detailsY = rowRect.yMax + 1;
                if (objectTable && rowObject != null)
                    foreach (JProperty property in rowObject.Properties().ToList())
                        detailsY += DrawExpanded(new Rect(0, detailsY, inner.width, 0), property.Value, property.Name + " · row " + (rowIndex + 1));
                else DrawExpanded(new Rect(0, detailsY, inner.width, 0), rowToken, "Item " + (rowIndex + 1));
            }

            DrawDropMarker(path, inner.width, visibleRows.Count);
            HandleDragMouseUp(arr, path, visibleRows);

            GUI.EndScrollView();
            tableScroll[path] = sv;

            ApplyPendingRowChanges(arr, path, deleteRow, duplicateRow, convertRow, columns);
        }

        private void DrawTableToolbar(JArray arr, string path, List<string> columns, List<int> visibleRows, bool objectTable)
        {
            Rect r = GUILayoutUtility.GetRect(0, 28, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(r, new Color(0.13f, 0.14f, 0.18f));

            float x = r.x + 8;
            GUI.Label(new Rect(x, r.y + 5, 110, 18), "Rows: " + visibleRows.Count + "/" + arr.Count, EditorStyles.miniLabel);
            x += 110;

            GUI.Label(new Rect(x, r.y + 5, 34, 18), "Find", EditorStyles.miniLabel);
            x += 34;

            string oldFilter = tableFilter.TryGetValue(path, out string f) ? f : "";
            string newFilter = EditorGUI.TextField(new Rect(x, r.y + 4, 190, 19), oldFilter, EditorStyles.toolbarSearchField);
            if (newFilter != oldFilter)
                tableFilter[path] = newFilter;
            x += 196;

            if (!string.IsNullOrEmpty(oldFilter) && GUI.Button(new Rect(x, r.y + 4, 48, 19), "Clear", smallButtonStyle))
                tableFilter[path] = "";
            x += 54;

            if (GUI.Button(new Rect(x, r.y + 4, 70, 19), "Add Row", smallButtonStyle))
                AddRow(arr, path);
            x += 74;

            using (new EditorGUI.DisabledScope(!objectTable))
            {
                if (GUI.Button(new Rect(x, r.y + 4, 74, 19), "Add Field", smallButtonStyle))
                    AskAddColumn(arr);
            }
            x += 78;

            if (GUI.Button(new Rect(x, r.y + 4, 78, 19), "Paste Row", smallButtonStyle))
                PasteRow(arr, path, arr.Count);
            x += 82;

            using (new EditorGUI.DisabledScope(selectedTablePath != path || selectedRowIndex < 0))
            {
                if (GUI.Button(new Rect(x, r.y + 4, 52, 19), "Top", smallButtonStyle))
                    MoveSelectedRowTo(arr, 0);
                x += 56;

                if (GUI.Button(new Rect(x, r.y + 4, 66, 19), "Bottom", smallButtonStyle))
                    MoveSelectedRowTo(arr, arr.Count);
            }
        }

        private void DrawEmptyTable(JArray arr, string path)
        {
            Rect r = GUILayoutUtility.GetRect(0, 70, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(r, new Color(0.12f, 0.12f, 0.15f));
            GUI.Label(new Rect(r.x + 8, r.y + 8, r.width - 16, 18), "Empty table. Add a row or field to start editing.", EditorStyles.miniLabel);

            if (GUI.Button(new Rect(r.x + 8, r.y + 36, 80, 22), "Add Row", smallButtonStyle))
                AddRow(arr, path);

            if (GUI.Button(new Rect(r.x + 96, r.y + 36, 84, 22), "Add Field", smallButtonStyle))
                AskAddColumn(arr);

            if (GUI.Button(new Rect(r.x + 188, r.y + 36, 86, 22), "Paste Row", smallButtonStyle))
                PasteRow(arr, path, 0);
        }

        private void DrawTableHeader(float x, float visibleWidth, JArray arr, string path, List<string> columns, float[] widths)
        {
            GUI.Label(new Rect(x, 0, RowNumberWidth, RowHeight), "#", rowNumberStyle);
            x += RowNumberWidth;

            GUI.Label(new Rect(x, 0, ActionWidth, RowHeight), "Actions", rowNumberStyle);
            x += ActionWidth;

            for (int i = 0; i < columns.Count; i++)
            {
                string key = columns[i];
                float width = widths[i];
                Rect cell = new Rect(x, 0, width, RowHeight);

                if (cell.xMax >= 0 && cell.x <= visibleWidth)
                {
                    GUI.Label(new Rect(cell.x + 4, cell.y + 2, cell.width - 14, cell.height - 4), key, headerStyle);

                    Rect resizeRect = new Rect(cell.xMax - 5, cell.y, 10, cell.height);
                    EditorGUIUtility.AddCursorRect(resizeRect, MouseCursor.ResizeHorizontal);
                    EditorGUI.DrawRect(new Rect(cell.xMax - 1, cell.y + 4, 1, cell.height - 8), new Color(0.36f, 0.42f, 0.58f, 0.9f));
                    if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && resizeRect.Contains(Event.current.mousePosition))
                    {
                        resizingColumnKey = path + "." + key;
                        Event.current.Use();
                    }

                    if (Event.current.type == EventType.ContextClick && cell.Contains(Event.current.mousePosition))
                    {
                        ShowColumnMenu(arr, key);
                        Event.current.Use();
                    }
                }

                x += width + 2;
            }
        }

        private void DrawRowActions(JArray arr, string path, Rect rowRect, int rowIndex, int visualIndex, List<int> visibleRows, ref int deleteRow, ref int duplicateRow)
        {
            if (GUI.Button(new Rect(rowRect.x + 1, rowRect.y + 2, RowNumberWidth - 2, RowHeight - 4), (rowIndex + 1).ToString(), rowNumberStyle))
                SelectRow(path, rowIndex);

            float x = RowNumberWidth;

            Rect dragRect = new Rect(x + 2, rowRect.y + 2, 20, RowHeight - 4);
            GUI.Label(dragRect, "=", smallButtonStyle);
            HandleRowDrag(arr, path, visibleRows, visualIndex, rowIndex, dragRect);

            if (GUI.Button(new Rect(x + 26, rowRect.y + 2, 28, RowHeight - 4), "X", smallButtonStyle))
                deleteRow = rowIndex;

            if (GUI.Button(new Rect(x + 58, rowRect.y + 2, 30, RowHeight - 4), "D", smallButtonStyle))
                duplicateRow = rowIndex;
        }

        private void DrawValueField(JToken token, Rect rect, Action<JToken> onChange)
        {
            if (token == null)
                token = JValue.CreateNull();

            if (token.Type == JTokenType.Boolean)
            {
                bool current = token.Value<bool>();
                bool next = EditorGUI.Toggle(new Rect(rect.x + 4, rect.y + 3, 18, rect.height - 6), current);
                GUI.Label(new Rect(rect.x + 28, rect.y + 3, rect.width - 30, rect.height - 6), current ? "true" : "false", EditorStyles.miniLabel);
                if (next != current)
                    onChange(new JValue(next));
                return;
            }

            if (token.Type == JTokenType.Object || token.Type == JTokenType.Array)
            {
                Rect preview = new Rect(rect.x, rect.y, Mathf.Max(0, rect.width - 88), rect.height);
                Rect edit = new Rect(rect.xMax - 42, rect.y + 1, 42, rect.height - 2);
                EditorGUI.DrawRect(preview, new Color(0.20f, 0.23f, 0.31f));
                if (GUI.Button(preview, new GUIContent(PreviewToken(token, 80), "Expand/collapse below this row"), cellStyle))
                    ToggleInline(token);

                if (GUI.Button(new Rect(rect.xMax - 86, rect.y + 1, 42, rect.height - 2), new GUIContent(expandedValues.Contains(token) ? "▾" : "▸", "Expand/collapse below this row"), smallButtonStyle))
                    ToggleInline(token);

                if (GUI.Button(edit, "Edit", smallButtonStyle))
                {
                    JsonTokenEditWindow.Open(token, delegate(JToken edited)
                    {
                        onChange(edited);
                    });
                }

                return;
            }

            if (token.Type == JTokenType.String)
            {
                string encoded = token.Value<string>()?.Trim();
                if (!string.IsNullOrEmpty(encoded) && (encoded.StartsWith("{") || encoded.StartsWith("[")))
                {
                    Rect view = new Rect(rect.xMax - 42, rect.y, 42, rect.height);
                    if (GUI.Button(view, new GUIContent(expandedValues.Contains(token) ? "▾" : "▸", "Expand/collapse below this row"), smallButtonStyle))
                        ToggleInline(token);
                    rect.width = Mathf.Max(0, rect.width - 46);
                }
            }

            string oldValue = TokenToText(token);
            EditorGUI.BeginChangeCheck();
            string newValue = EditorGUI.DelayedTextField(rect, oldValue, cellStyle);
            if (EditorGUI.EndChangeCheck())
                onChange(ParseCell(newValue, token));
        }

        private void HandleCellContext(JArray arr, int rowIndex, string key, JToken value, Rect cell)
        {
            if (Event.current.type == EventType.ContextClick && cell.Contains(Event.current.mousePosition))
            {
                selectedCellRow = rowIndex;
                selectedColumnKey = key;
                ShowCellMenu(arr, rowIndex, key, value);
                Event.current.Use();
            }
        }

        private void HandleRowContext(JArray arr, string path, Rect rowRect, int rowIndex)
        {
            if (Event.current.type == EventType.MouseDown && Event.current.button == 0 && rowRect.Contains(Event.current.mousePosition))
            {
                SelectRow(path, rowIndex);
                Repaint();
                // Do not consume the click here. Cells still need the same mouse event to focus/edit.
            }

            if (Event.current.type == EventType.ContextClick && rowRect.Contains(Event.current.mousePosition))
            {
                SelectRow(path, rowIndex);
                ShowRowMenu(arr, path, rowIndex);
                Event.current.Use();
            }
        }

        private void ShowRowMenu(JArray arr, string path, int rowIndex)
        {
            GenericMenu menu = new GenericMenu();

            menu.AddItem(new GUIContent("Copy Row"), false, delegate { CopyRow(arr, rowIndex); });
            menu.AddItem(new GUIContent("Paste Above"), false, delegate { PasteRow(arr, path, rowIndex); });
            menu.AddItem(new GUIContent("Paste Below"), false, delegate { PasteRow(arr, path, rowIndex + 1); });
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Duplicate Row"), false, delegate { DuplicateRow(arr, path, rowIndex); });
            menu.AddItem(new GUIContent("Insert Empty Above"), false, delegate { InsertEmptyRow(arr, path, rowIndex); });
            menu.AddItem(new GUIContent("Insert Empty Below"), false, delegate { InsertEmptyRow(arr, path, rowIndex + 1); });
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Move Up"), rowIndex > 0, delegate { MoveRowTo(arr, rowIndex, rowIndex - 1); });
            menu.AddItem(new GUIContent("Move Down"), rowIndex < arr.Count - 1, delegate { MoveRowTo(arr, rowIndex, rowIndex + 2); });
            menu.AddItem(new GUIContent("Move To Top"), false, delegate { MoveRowTo(arr, rowIndex, 0); });
            menu.AddItem(new GUIContent("Move To Bottom"), false, delegate { MoveRowTo(arr, rowIndex, arr.Count); });
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Copy Row JSON"), false, delegate
            {
                EditorGUIUtility.systemCopyBuffer = arr[rowIndex].ToString(Formatting.Indented);
                SetStatus("Row JSON copied.", MessageType.Info);
            });
            menu.AddItem(new GUIContent("Delete Row"), false, delegate
            {
                if (EditorUtility.DisplayDialog(WindowTitle, "Delete this row?", "Delete", "Cancel"))
                    DeleteRow(arr, rowIndex);
            });

            menu.ShowAsContext();
        }

        private void ShowCellMenu(JArray arr, int rowIndex, string key, JToken value)
        {
            GenericMenu menu = new GenericMenu();

            menu.AddItem(new GUIContent("Copy Cell Text"), false, delegate
            {
                EditorGUIUtility.systemCopyBuffer = TokenToText(value);
                SetStatus("Cell copied.", MessageType.Info);
            });

            menu.AddItem(new GUIContent("Copy Cell JSON"), false, delegate
            {
                EditorGUIUtility.systemCopyBuffer = value == null ? "null" : value.ToString(Formatting.None);
                SetStatus("Cell JSON copied.", MessageType.Info);
            });

            menu.AddItem(new GUIContent("Paste Cell"), false, delegate
            {
                SetCell(arr, rowIndex, key, ParseCell(EditorGUIUtility.systemCopyBuffer, value));
            });

            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Edit Cell As JSON"), false, delegate
            {
                JsonTokenEditWindow.Open(value ?? JValue.CreateNull(), delegate(JToken edited)
                {
                    SetCell(arr, rowIndex, key, edited);
                });
            });

            menu.AddItem(new GUIContent("Set Null"), false, delegate { SetCell(arr, rowIndex, key, JValue.CreateNull()); });
            menu.AddItem(new GUIContent("Set True"), false, delegate { SetCell(arr, rowIndex, key, new JValue(true)); });
            menu.AddItem(new GUIContent("Set False"), false, delegate { SetCell(arr, rowIndex, key, new JValue(false)); });

            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Fill Down"), false, delegate { FillDown(arr, rowIndex, key, value); });
            menu.AddItem(new GUIContent("Fill Empty In Column"), false, delegate { FillEmpty(arr, key, value); });

            menu.ShowAsContext();
        }

        private void ShowColumnMenu(JArray arr, string key)
        {
            GenericMenu menu = new GenericMenu();

            menu.AddItem(new GUIContent("Copy Column Text"), false, delegate { CopyColumnText(arr, key); });
            menu.AddItem(new GUIContent("Copy Column JSON"), false, delegate { CopyColumnJson(arr, key); });
            menu.AddItem(new GUIContent("Copy Column Name"), false, delegate { EditorGUIUtility.systemCopyBuffer = key; });
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Sort Ascending"), false, delegate { SortByColumn(arr, key, true); });
            menu.AddItem(new GUIContent("Sort Descending"), false, delegate { SortByColumn(arr, key, false); });
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Rename Column"), false, delegate
            {
                StringInputWindow.Open("Rename Column", "New field name", key, delegate(string newName) { RenameColumn(arr, key, newName); });
            });
            menu.AddItem(new GUIContent("Set All Values"), false, delegate
            {
                StringInputWindow.Open("Set All Values", "Value", "", delegate(string value) { SetAll(arr, key, value); });
            });
            menu.AddItem(new GUIContent("Fill Empty Values"), false, delegate
            {
                StringInputWindow.Open("Fill Empty", "Value", "", delegate(string value) { FillEmpty(arr, key, ParseCell(value, FindSample(arr, key))); });
            });
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Delete Column"), false, delegate
            {
                if (EditorUtility.DisplayDialog(WindowTitle, "Delete column " + key + "?", "Delete", "Cancel"))
                    DeleteColumn(arr, key);
            });

            menu.ShowAsContext();
        }

        private void ShowCsvMenu(JArray arr, string path)
        {
            GenericMenu menu = new GenericMenu();
            menu.AddItem(new GUIContent("Copy TSV"), false, delegate { CopyTsv(arr, GetColumns(arr), GetVisibleRows(arr, path)); });
            menu.AddItem(new GUIContent("Export CSV"), false, delegate { ExportCsv(arr, GetColumns(arr), GetVisibleRows(arr, path)); });
            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Paste TSV Append"), false, delegate { PasteTsv(arr, false); });
            menu.AddItem(new GUIContent("Paste TSV Replace"), false, delegate { PasteTsv(arr, true); });
            menu.ShowAsContext();
        }

        private void ShowSortMenu(JArray arr, string path)
        {
            List<string> cols = GetColumns(arr);
            GenericMenu menu = new GenericMenu();

            foreach (string col in cols)
            {
                string key = col;
                menu.AddItem(new GUIContent(key + " Ascending"), false, delegate { SortByColumn(arr, key, true); });
                menu.AddItem(new GUIContent(key + " Descending"), false, delegate { SortByColumn(arr, key, false); });
            }

            if (cols.Count == 0)
                menu.AddDisabledItem(new GUIContent("No columns"));

            menu.ShowAsContext();
        }

        private void AddRow(JArray arr, string path)
        {
            JToken row = CreateEmptyRowLike(arr);
            arr.Add(row);
            SelectRow(path, arr.Count - 1);
            OnTokenChanged();
            GUIUtility.ExitGUI();
        }

        private void AskAddColumn(JArray arr)
        {
            StringInputWindow.Open("Add Field", "Field name", "id", delegate(string name)
            {
                AddColumn(arr, name);
            });
        }

        private void AddColumn(JArray arr, string key)
        {
            key = (key ?? "").Trim();
            if (key.Length == 0) return;

            if (arr.Count == 0)
            {
                JObject obj = new JObject();
                obj[key] = "";
                arr.Add(obj);
            }
            else
            {
                for (int i = 0; i < arr.Count; i++)
                {
                    JObject obj = arr[i] as JObject;
                    if (obj == null)
                    {
                        obj = new JObject();
                        obj["value"] = arr[i] == null ? JValue.CreateNull() : arr[i].DeepClone();
                        arr[i] = obj;
                    }

                    if (obj.Property(key) == null)
                        obj[key] = "";
                }
            }

            OnTokenChanged();
        }

        private void InsertEmptyRow(JArray arr, string path, int index)
        {
            index = Mathf.Clamp(index, 0, arr.Count);
            arr.Insert(index, CreateEmptyRowLike(arr));
            SelectRow(path, index);
            OnTokenChanged();
        }

        private void DuplicateRow(JArray arr, string path, int index)
        {
            if (index < 0 || index >= arr.Count) return;
            arr.Insert(index + 1, arr[index].DeepClone());
            SelectRow(path, index + 1);
            OnTokenChanged();
        }

        private void DeleteRow(JArray arr, int index)
        {
            if (index < 0 || index >= arr.Count) return;
            arr.RemoveAt(index);
            selectedRowIndex = Mathf.Clamp(index, 0, arr.Count - 1);
            OnTokenChanged();
        }

        private void CopyRow(JArray arr, int index)
        {
            if (index < 0 || index >= arr.Count) return;
            copiedRow = arr[index].DeepClone();
            copiedRowText = copiedRow.ToString(Formatting.None);
            EditorGUIUtility.systemCopyBuffer = copiedRow.ToString(Formatting.Indented);
            SetStatus("Row copied.", MessageType.Info);
        }

        private void PasteRow(JArray arr, string path, int index)
        {
            TryReadClipboardRow();
            if (copiedRow == null)
            {
                SetStatus("Clipboard does not contain JSON row data.", MessageType.Warning);
                return;
            }

            index = Mathf.Clamp(index, 0, arr.Count);

            if (copiedRow is JArray many)
            {
                foreach (JToken item in many)
                {
                    arr.Insert(index, item.DeepClone());
                    index++;
                }
                SelectRow(path, index - 1);
            }
            else
            {
                arr.Insert(index, copiedRow.DeepClone());
                SelectRow(path, index);
            }

            OnTokenChanged();
            GUIUtility.ExitGUI();
        }

        private void TryReadClipboardRow()
        {
            string text = EditorGUIUtility.systemCopyBuffer;
            if (string.IsNullOrWhiteSpace(text) || text == copiedRowText) return;

            try
            {
                copiedRow = JToken.Parse(text);
                copiedRowText = copiedRow.ToString(Formatting.None);
            }
            catch
            {
                // Keep current clipboard token.
            }
        }

        private void MoveSelectedRowTo(JArray arr, int target)
        {
            if (selectedRowIndex < 0) return;
            MoveRowTo(arr, selectedRowIndex, target);
        }

        private void MoveRowTo(JArray arr, int from, int target)
        {
            if (from < 0 || from >= arr.Count) return;
            target = Mathf.Clamp(target, 0, arr.Count);

            int insert = target;
            if (insert > from) insert--;
            if (insert == from) return;

            JToken item = arr[from];
            arr.RemoveAt(from);
            insert = Mathf.Clamp(insert, 0, arr.Count);
            arr.Insert(insert, item);
            selectedRowIndex = insert;
            OnTokenChanged();
        }

        private void SelectRow(string path, int row)
        {
            selectedTablePath = path;
            selectedRowIndex = row;
        }

        private void SetCell(JArray arr, int row, string key, JToken value)
        {
            if (row < 0 || row >= arr.Count) return;

            if (key == "row_json" || (key == "value" && !(arr[row] is JObject)))
            {
                arr[row] = value ?? JValue.CreateNull();
            }
            else
            {
                JObject obj = arr[row] as JObject;
                if (obj == null)
                {
                    obj = new JObject();
                    arr[row] = obj;
                }
                obj[key] = value ?? JValue.CreateNull();
            }

            OnTokenChanged();
        }

        private void FillDown(JArray arr, int startRow, string key, JToken value)
        {
            for (int i = startRow + 1; i < arr.Count; i++)
                SetCellNoNotify(arr, i, key, value == null ? JValue.CreateNull() : value.DeepClone());

            OnTokenChanged();
        }

        private void FillEmpty(JArray arr, string key, JToken value)
        {
            for (int i = 0; i < arr.Count; i++)
            {
                JToken current = GetCell(arr[i], key);
                if (IsEmpty(current))
                    SetCellNoNotify(arr, i, key, value == null ? JValue.CreateNull() : value.DeepClone());
            }

            OnTokenChanged();
        }

        private void SetCellNoNotify(JArray arr, int row, string key, JToken value)
        {
            if (key == "row_json" || (key == "value" && !(arr[row] is JObject)))
            {
                arr[row] = value ?? JValue.CreateNull();
                return;
            }

            JObject obj = arr[row] as JObject;
            if (obj == null)
            {
                obj = new JObject();
                arr[row] = obj;
            }
            obj[key] = value;
        }

        private void RenameColumn(JArray arr, string oldKey, string newKey)
        {
            newKey = (newKey ?? "").Trim();
            if (newKey.Length == 0 || newKey == oldKey) return;

            foreach (JObject obj in arr.OfType<JObject>())
            {
                JProperty prop = obj.Property(oldKey);
                if (prop == null) continue;

                JToken value = prop.Value.DeepClone();
                prop.Remove();
                obj[newKey] = value;
            }

            OnTokenChanged();
        }

        private void DeleteColumn(JArray arr, string key)
        {
            foreach (JObject obj in arr.OfType<JObject>())
                obj.Remove(key);

            OnTokenChanged();
        }

        private void SetAll(JArray arr, string key, string raw)
        {
            JToken sample = FindSample(arr, key);
            JToken value = ParseCell(raw, sample);
            for (int i = 0; i < arr.Count; i++)
                SetCellNoNotify(arr, i, key, value.DeepClone());

            OnTokenChanged();
        }

        private void SortByColumn(JArray arr, string key, bool asc)
        {
            List<JToken> rows = arr.Select(x => x.DeepClone()).ToList();
            rows.Sort((a, b) => CompareCell(GetCell(a, key), GetCell(b, key)));
            if (!asc) rows.Reverse();

            arr.Clear();
            foreach (JToken row in rows)
                arr.Add(row);

            OnTokenChanged();
        }

        private int CompareCell(JToken a, JToken b)
        {
            bool ea = IsEmpty(a);
            bool eb = IsEmpty(b);
            if (ea || eb)
            {
                if (ea && eb) return 0;
                return ea ? 1 : -1;
            }

            if (TryDouble(a, out double da) && TryDouble(b, out double db))
                return da.CompareTo(db);

            return string.Compare(TokenToText(a), TokenToText(b), StringComparison.OrdinalIgnoreCase);
        }

        private void CopyColumnText(JArray arr, string key)
        {
            EditorGUIUtility.systemCopyBuffer = string.Join("\n", arr.Select(row => TokenToText(GetCell(row, key))));
            SetStatus("Column copied.", MessageType.Info);
        }

        private void CopyColumnJson(JArray arr, string key)
        {
            JArray values = new JArray();
            foreach (JToken row in arr)
                values.Add(GetCell(row, key).DeepClone());

            EditorGUIUtility.systemCopyBuffer = values.ToString(Formatting.Indented);
            SetStatus("Column JSON copied.", MessageType.Info);
        }

        private void ExportCsv(JArray arr, List<string> columns, List<int> rows)
        {
            string defaultName = HasSelectedFile() ? Path.GetFileNameWithoutExtension(CurrentFile.DisplayName) + ".csv" : "table.csv";
            string savePath = EditorUtility.SaveFilePanel("Export CSV", Application.dataPath, defaultName, "csv");
            if (string.IsNullOrEmpty(savePath))
                return;

            try
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine(string.Join(",", columns.Select(EscapeCsv)));

                foreach (int rowIndex in rows)
                {
                    List<string> cells = new List<string>();
                    foreach (string col in columns)
                        cells.Add(EscapeCsv(TokenToText(GetCell(arr[rowIndex], col))));
                    sb.AppendLine(string.Join(",", cells));
                }

                File.WriteAllText(savePath, sb.ToString(), new UTF8Encoding(true));
                SetStatus("CSV exported: " + Path.GetFileName(savePath), MessageType.Info);
            }
            catch (Exception ex)
            {
                SetStatus("CSV export failed: " + ex.Message, MessageType.Error);
            }
        }

        private static string EscapeCsv(string value)
        {
            value = value ?? "";
            bool quote = value.IndexOfAny(new[] { ',', '"', '\n', '\r' }) >= 0;
            value = value.Replace("\"", "\"\"");
            return quote ? "\"" + value + "\"" : value;
        }

        private void CopyTsv(JArray arr, List<string> columns, List<int> rows)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine(string.Join("\t", columns));

            foreach (int rowIndex in rows)
            {
                List<string> cells = new List<string>();
                foreach (string col in columns)
                    cells.Add(EscapeTsv(TokenToText(GetCell(arr[rowIndex], col))));
                sb.AppendLine(string.Join("\t", cells));
            }

            EditorGUIUtility.systemCopyBuffer = sb.ToString();
            SetStatus("TSV copied.", MessageType.Info);
        }

        private void PasteTsv(JArray arr, bool replace)
        {
            string tsv = EditorGUIUtility.systemCopyBuffer;
            if (string.IsNullOrWhiteSpace(tsv))
            {
                SetStatus("Clipboard is empty.", MessageType.Warning);
                return;
            }

            List<JObject> rows = ParseTsv(tsv, arr);
            if (rows.Count == 0)
            {
                SetStatus("Clipboard does not contain table rows.", MessageType.Warning);
                return;
            }

            if (replace)
                arr.Clear();

            foreach (JObject row in rows)
                arr.Add(row);

            OnTokenChanged();
        }

        private List<JObject> ParseTsv(string tsv, JArray sampleArray)
        {
            List<JObject> result = new List<JObject>();
            string[] lines = tsv.Replace("\r\n", "\n").Replace('\r', '\n').Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0) return result;

            string[] headers = lines[0].Split('\t').Select(h => h.Trim()).ToArray();

            for (int i = 1; i < lines.Length; i++)
            {
                string[] cells = lines[i].Split('\t');
                JObject row = new JObject();
                for (int c = 0; c < headers.Length && c < cells.Length; c++)
                {
                    string key = headers[c];
                    if (string.IsNullOrWhiteSpace(key)) continue;
                    row[key] = ParseCell(UnescapeTsv(cells[c]), FindSample(sampleArray, key));
                }
                result.Add(row);
            }

            return result;
        }

        private void HandleRowDrag(JArray arr, string path, List<int> visibleRows, int visualIndex, int realIndex, Rect dragRect)
        {
            EditorGUIUtility.AddCursorRect(dragRect, MouseCursor.Pan);
            Event e = Event.current;

            if (e.type == EventType.MouseDown && e.button == 0 && dragRect.Contains(e.mousePosition))
            {
                draggingTablePath = path;
                draggingRowIndex = realIndex;
                dragTargetVisualIndex = visualIndex;
                dragStartMouse = e.mousePosition;
                isDraggingRow = false;
                e.Use();
                return;
            }

            if (draggingTablePath != path || draggingRowIndex != realIndex) return;

            if (e.type == EventType.MouseDrag)
            {
                if (!isDraggingRow && Vector2.Distance(dragStartMouse, e.mousePosition) >= 4f)
                    isDraggingRow = true;

                if (isDraggingRow)
                {
                    dragTargetVisualIndex = visibleRows.Count;
                    if (tableRowOffsets.TryGetValue(path, out float[] offsets))
                        for (int i = 0; i < visibleRows.Count; i++)
                            if (e.mousePosition.y < (offsets[i] + offsets[i + 1]) * 0.5f) { dragTargetVisualIndex = i; break; }
                    e.Use();
                    Repaint();
                }
            }
        }

        private void DrawDropMarker(string path, float width, int visibleCount)
        {
            if (draggingTablePath != path || !isDraggingRow) return;

            float y = tableRowOffsets.TryGetValue(path, out float[] offsets)
                ? offsets[Mathf.Clamp(dragTargetVisualIndex, 0, visibleCount)] : 0;
            EditorGUI.DrawRect(new Rect(0, y - 2, width, 3), new Color(0.45f, 0.72f, 1f));
        }

        private void HandleDragMouseUp(JArray arr, string path, List<int> visibleRows)
        {
            Event e = Event.current;
            if (draggingTablePath != path || e.type != EventType.MouseUp) return;

            if (isDraggingRow && draggingRowIndex >= 0 && draggingRowIndex < arr.Count)
            {
                int visualTarget = Mathf.Clamp(dragTargetVisualIndex, 0, visibleRows.Count);
                int target = visualTarget >= visibleRows.Count ? arr.Count : visibleRows[visualTarget];

                pendingMoveTable = path;
                pendingMoveFrom = draggingRowIndex;
                pendingMoveTarget = target;
            }

            draggingTablePath = "";
            draggingRowIndex = -1;
            dragTargetVisualIndex = -1;
            isDraggingRow = false;
            e.Use();
        }

        private void ApplyPendingRowChanges(JArray arr, string path, int deleteRow, int duplicateRow, int convertRow, List<string> columns)
        {
            if (pendingMoveTable == path && pendingMoveFrom >= 0 && pendingMoveTarget >= 0)
            {
                MoveRowTo(arr, pendingMoveFrom, pendingMoveTarget);
                pendingMoveTable = "";
                pendingMoveFrom = -1;
                pendingMoveTarget = -1;
                GUIUtility.ExitGUI();
            }

            if (convertRow >= 0 && convertRow < arr.Count)
            {
                JObject obj = new JObject();
                foreach (string col in columns)
                    obj[col] = "";
                obj["value"] = arr[convertRow] == null ? JValue.CreateNull() : arr[convertRow].DeepClone();
                arr[convertRow] = obj;
                OnTokenChanged();
                GUIUtility.ExitGUI();
            }

            if (duplicateRow >= 0)
            {
                DuplicateRow(arr, path, duplicateRow);
                GUIUtility.ExitGUI();
            }

            if (deleteRow >= 0)
            {
                DeleteRow(arr, deleteRow);
                GUIUtility.ExitGUI();
            }
        }

        private List<string> GetColumns(JArray arr)
        {
            if (!IsObjectTable(arr))
                return new List<string> { "value" };

            if (cachedColumns.TryGetValue(arr, out List<string> cached)) return cached;
            List<string> result = new List<string>();
            HashSet<string> set = new HashSet<string>();

            string[] preferred = { "id", "Id", "ID", "key", "name", "Name", "order", "sortOrder", "index", "level", "type", "price", "amount", "value" };
            foreach (string key in preferred)
            {
                if (arr.OfType<JObject>().Any(o => o.Property(key) != null) && set.Add(key))
                    result.Add(key);
            }

            foreach (JObject obj in arr.OfType<JObject>())
            {
                foreach (JProperty prop in obj.Properties())
                {
                    if (set.Add(prop.Name))
                        result.Add(prop.Name);
                }
            }

            if (arr.Count > 0 && result.Count == 0)
                result.Add("row_json");

            cachedColumns[arr] = result;
            return result;
        }

        private bool IsObjectTable(JArray arr)
        {
            return arr.Count == 0 || arr.Any(x => x is JObject);
        }

        private List<int> GetVisibleRows(JArray arr, string path)
        {
            string filter = tableFilter.TryGetValue(path, out string f) ? f : "";
            if (cachedFilters.TryGetValue(arr, out string previous) && previous == filter && cachedRows.TryGetValue(arr, out List<int> cached))
                return cached;
            List<int> rows = new List<int>();

            for (int i = 0; i < arr.Count; i++)
            {
                if (string.IsNullOrWhiteSpace(filter) || arr[i].ToString(Formatting.None).IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0)
                    rows.Add(i);
            }

            cachedRows[arr] = rows;
            cachedFilters[arr] = filter;
            return rows;
        }

        private float[] BuildColumnWidths(JArray arr, string path, List<string> columns, bool objectTable)
        {
            float[] widths = new float[columns.Count];

            for (int i = 0; i < columns.Count; i++)
            {
                string key = columns[i];
                string cacheKey = path + "." + key;

                if (columnWidths.TryGetValue(cacheKey, out float cached))
                {
                    widths[i] = cached;
                    continue;
                }

                float width = Mathf.Clamp(key.Length * 9f + 60f, CellMinWidth, CellMaxWidth);

                int sampleCount = Mathf.Min(arr.Count, 80);
                for (int r = 0; r < sampleCount; r++)
                {
                    JToken sample = objectTable ? GetCell(arr[r], key) : arr[r];
                    string value = sample is JContainer ? PreviewToken(sample, 80) : TokenToText(sample);
                    width = Mathf.Max(width, Mathf.Clamp(value.Length * 7f + 70f, CellMinWidth, CellMaxWidth));
                }

                if (IsCompactKey(key))
                    width = Mathf.Min(width, 140f);

                columnWidths[cacheKey] = width;
                widths[i] = width;
            }

            return widths;
        }

        private bool IsCompactKey(string key)
        {
            key = (key ?? "").ToLowerInvariant();
            return key == "id" || key == "index" || key == "level" || key == "order" ||
                   key.Contains("price") || key.Contains("amount") || key.Contains("count") ||
                   key.Contains("rate") || key.Contains("value") || key.Contains("enabled");
        }

        private JToken GetCell(JToken row, string key)
        {
            if (row is JObject obj && obj.TryGetValue(key, out JToken value))
                return value ?? JValue.CreateNull();

            if (key == "value" || key == "row_json")
                return row ?? JValue.CreateNull();

            return JValue.CreateNull();
        }

        private JToken FindSample(JArray arr, string key)
        {
            foreach (JToken row in arr)
            {
                JToken value = GetCell(row, key);
                if (value != null && value.Type != JTokenType.Null)
                    return value;
            }

            return null;
        }

        private JToken CreateEmptyRowLike(JArray arr)
        {
            if (arr == null || arr.Count == 0)
                return new JObject { ["id"] = "" };

            JToken sample = arr.OfType<JObject>().FirstOrDefault() ?? arr[0];

            if (sample is JObject obj)
            {
                JObject newObj = new JObject();
                foreach (JProperty prop in obj.Properties())
                    newObj[prop.Name] = DefaultValue(prop.Value);

                if (!newObj.HasValues)
                    newObj["id"] = "";

                return newObj;
            }

            return DefaultValue(sample);
        }

        private JToken DefaultValue(JToken sample)
        {
            if (sample == null) return JValue.CreateNull();

            switch (sample.Type)
            {
                case JTokenType.Integer: return new JValue(0);
                case JTokenType.Float: return new JValue(0f);
                case JTokenType.Boolean: return new JValue(false);
                case JTokenType.Array: return new JArray();
                case JTokenType.Object: return new JObject();
                default: return new JValue("");
            }
        }

        private string TokenToText(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return "";
            if (token.Type == JTokenType.String)
                return token.Value<string>() ?? "";
            return token.ToString(Formatting.None);
        }

        private string PreviewToken(JToken token, int max)
        {
            if (token is JArray array) return "List [" + array.Count + "]";
            if (token is JObject obj) return "Object { " + string.Join(", ", obj.Properties().Take(4).Select(p => p.Name)) + (obj.Count > 4 ? ", …" : "") + " }";
            string text = token == null ? "null" : token.ToString(Formatting.None).Replace("\n", " ").Replace("\r", "");
            return text.Length <= max ? text : text.Substring(0, max - 3) + "...";
        }

        private JToken ParseCell(string text, JToken sample)
        {
            text = text ?? "";
            string trim = text.Trim();

            if (sample != null)
            {
                switch (sample.Type)
                {
                    case JTokenType.String: return new JValue(text);
                    case JTokenType.Integer:
                        if (long.TryParse(trim, NumberStyles.Integer, CultureInfo.InvariantCulture, out long i)) return new JValue(i);
                        return new JValue(text);
                    case JTokenType.Float:
                        if (double.TryParse(trim, NumberStyles.Float, CultureInfo.InvariantCulture, out double f)) return new JValue(f);
                        return new JValue(text);
                    case JTokenType.Boolean:
                        if (bool.TryParse(trim, out bool b)) return new JValue(b);
                        return new JValue(text);
                }
            }

            if (trim.Equals("null", StringComparison.OrdinalIgnoreCase)) return JValue.CreateNull();
            if (trim.Equals("true", StringComparison.OrdinalIgnoreCase)) return new JValue(true);
            if (trim.Equals("false", StringComparison.OrdinalIgnoreCase)) return new JValue(false);
            if (long.TryParse(trim, NumberStyles.Integer, CultureInfo.InvariantCulture, out long li)) return new JValue(li);
            if (double.TryParse(trim, NumberStyles.Float, CultureInfo.InvariantCulture, out double df)) return new JValue(df);

            if ((trim.StartsWith("{") && trim.EndsWith("}")) || (trim.StartsWith("[") && trim.EndsWith("]")))
            {
                try { return JToken.Parse(trim); }
                catch { }
            }

            return new JValue(text);
        }

        private bool IsEmpty(JToken token)
        {
            return token == null || token.Type == JTokenType.Null ||
                   (token.Type == JTokenType.String && string.IsNullOrWhiteSpace(token.Value<string>()));
        }

        private bool TryDouble(JToken token, out double value)
        {
            value = 0;
            if (token == null) return false;
            if (token.Type == JTokenType.Integer || token.Type == JTokenType.Float)
            {
                value = token.Value<double>();
                return true;
            }
            return double.TryParse(TokenToText(token), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        private string EscapeTsv(string value)
        {
            return (value ?? "").Replace("\t", "\\t").Replace("\n", "\\n").Replace("\r", "\\r");
        }

        private string UnescapeTsv(string value)
        {
            return (value ?? "").Replace("\\t", "\t").Replace("\\n", "\n").Replace("\\r", "\r");
        }

        private bool MatchesFileSearch(FileEntry file)
        {
            if (string.IsNullOrWhiteSpace(fileSearch)) return true;
            return file.DisplayName.IndexOf(fileSearch, StringComparison.OrdinalIgnoreCase) >= 0 ||
                   file.RelativePath.IndexOf(fileSearch, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private void RefreshFiles(bool reloadSelected)
        {
            string oldPath = HasSelectedFile() ? CurrentFile.FullPath : "";
            files.Clear();

            if (sourceMode == SourceMode.DataConfig)
            {
                foreach (string guid in AssetDatabase.FindAssets("t:TextAsset"))
                {
                    string assetPath = NormalizePath(AssetDatabase.GUIDToAssetPath(guid));
                    if (!assetPath.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
                    if (assetPath.IndexOf(DataConfigMarker, StringComparison.OrdinalIgnoreCase) < 0) continue;

                    files.Add(new FileEntry
                    {
                        AssetPath = assetPath,
                        FullPath = Path.GetFullPath(assetPath),
                        RelativePath = assetPath.Substring(assetPath.IndexOf(DataConfigMarker, StringComparison.OrdinalIgnoreCase) + DataConfigMarker.Length),
                        DisplayName = Path.GetFileNameWithoutExtension(assetPath)
                    });
                }
            }
            else
            {
                string dir = GetSaveDirectory();
                if (Directory.Exists(dir))
                {
                    // Include backup-only saves left by earlier deletions so they can be reset too.
                    foreach (string path in Directory.GetFiles(dir).Select(GetPrimarySavePath).Distinct(StringComparer.Ordinal))
                    {
                        files.Add(new FileEntry
                        {
                            AssetPath = "",
                            FullPath = path,
                            RelativePath = Path.GetFileName(path),
                            DisplayName = Path.GetFileName(path) + (File.Exists(path) ? "" : " (backup only)")
                        });
                    }
                }
            }

            files.Sort((a, b) => string.Compare(a.RelativePath, b.RelativePath, StringComparison.OrdinalIgnoreCase));

            selectedFileIndex = files.FindIndex(f => string.Equals(f.FullPath, oldPath, StringComparison.OrdinalIgnoreCase));
            if (selectedFileIndex >= 0 && reloadSelected)
                LoadFile(files[selectedFileIndex]);
            else if (selectedFileIndex < 0)
                ClearCurrentFile();

            SetStatus("Found " + files.Count + " file(s).", MessageType.Info);
        }

        private void SelectFile(int index)
        {
            if (index < 0 || index >= files.Count) return;
            if (index == selectedFileIndex) return;
            if (!ConfirmLeaveDirtyFile()) return;

            selectedFileIndex = index;
            LoadFile(files[index]);
        }

        private void LoadFile(FileEntry file)
        {
            ClearVisualState();
            rootToken = null;
            editText = "";
            originalRawFile = "";
            originalPayloadWasString = false;
            isDirty = false;
            hasUnsavedChanges = false;
            file.Dirty = false;

            try
            {
                if (sourceMode == SourceMode.Datasave && !File.Exists(file.FullPath))
                {
                    parseError = true;
                    parseErrorMessage = "Only backup/temp files remain for this save. Use Reset Save to remove them before testing.";
                    SetStatus(parseErrorMessage, MessageType.Warning);
                    return;
                }
                originalRawFile = File.ReadAllText(file.FullPath, Encoding.UTF8);
                JToken token;

                if (sourceMode == SourceMode.Datasave)
                {
                    if (!TryReadPayload(originalRawFile, out token, out string error))
                    {
                        parseError = true;
                        parseErrorMessage = error;
                        editText = originalRawFile;
                        viewMode = ViewMode.Text;
                        SetStatus(error, MessageType.Error);
                        return;
                    }
                }
                else
                {
                    token = JToken.Parse(originalRawFile);
                }

                rootToken = token;
                editText = token.ToString(Formatting.Indented);
                parseError = false;
                parseErrorMessage = "";
                isDirty = false;
                file.Dirty = false;
                viewMode = ViewMode.Visual;
                SetStatus("Loaded: " + file.RelativePath, MessageType.Info);
            }
            catch (Exception ex)
            {
                rootToken = null;
                editText = originalRawFile;
                parseError = true;
                parseErrorMessage = ex.Message;
                viewMode = ViewMode.Text;
                SetStatus("Load failed: " + ex.Message, MessageType.Error);
            }
        }

        private bool TryReadPayload(string raw, out JToken payload, out string error)
        {
            payload = null;
            error = "";
            originalPayloadWasString = false;

            try
            {
                JToken saveToken = JToken.Parse(raw);
                if (saveToken is JObject envelope && envelope["Payload"] != null)
                {
                    JToken payloadToken = envelope["Payload"];
                    if (payloadToken.Type == JTokenType.String)
                    {
                        originalPayloadWasString = true;
                        string text = payloadToken.Value<string>();
                        if (string.IsNullOrWhiteSpace(text))
                        {
                            payload = new JObject();
                            return true;
                        }

                        try { payload = JToken.Parse(text); }
                        catch { payload = new JValue(text); }
                        return true;
                    }

                    payload = payloadToken.DeepClone();
                    return true;
                }

                payload = saveToken.DeepClone();
                return true;
            }
            catch (Exception ex)
            {
                error = "Cannot read save Payload: " + ex.Message;
                return false;
            }
        }

        private void RequestSave()
        {
            if (!HasSelectedFile()) return;
            FileEntry target = CurrentFile;
            // Let delayed fields commit during this GUI event before writing the file.
            GUI.FocusControl(null);
            Repaint();
            EditorApplication.delayCall += () =>
            {
                if (this != null && CurrentFile == target) SaveCurrent();
            };
        }

        private void SaveCurrent()
        {
            if (!HasSelectedFile()) return;
            JToken token;
            if (viewMode == ViewMode.Text)
            {
                try { token = JToken.Parse(editText); }
                catch (Exception ex)
                {
                    SetStatus("Invalid JSON: " + ex.Message, MessageType.Error);
                    return;
                }
            }
            else
            {
                token = rootToken;
            }

            if (token == null) return;

            try
            {
                if (!File.Exists(CurrentFile.FullPath) || File.ReadAllText(CurrentFile.FullPath, Encoding.UTF8) != originalRawFile)
                {
                    SetStatus("File changed or was deleted outside this window. Copy your JSON before reloading; save was cancelled.", MessageType.Warning);
                    return;
                }
                if (autoBackup) Backup(CurrentFile.FullPath);

                string output = sourceMode == SourceMode.DataConfig ? token.ToString(Formatting.Indented) : BuildSaveOutput(token);
                File.WriteAllText(CurrentFile.FullPath, output, Encoding.UTF8);

                originalRawFile = output;
                rootToken = token;
                if (viewMode == ViewMode.Text) ClearVisualState();
                editText = token.ToString(Formatting.Indented);
                isDirty = false;
                hasUnsavedChanges = false;
                parseError = false;
                parseErrorMessage = "";
                CurrentFile.Dirty = false;

                if (sourceMode == SourceMode.DataConfig)
                {
                    AssetDatabase.ImportAsset(CurrentFile.AssetPath);
                }

                SetStatus(sourceMode == SourceMode.Datasave ? "Payload saved." : "Config saved.", MessageType.Info);
            }
            catch (Exception ex)
            {
                SetStatus("Save failed: " + ex.Message, MessageType.Error);
            }
        }

        private string BuildSaveOutput(JToken payload)
        {
            try
            {
                JToken original = JToken.Parse(originalRawFile);
                if (original is JObject envelope && envelope["Payload"] != null)
                {
                    envelope["Payload"] = originalPayloadWasString ? new JValue(payload.ToString(Formatting.None)) : payload.DeepClone();
                    return envelope.ToString(Formatting.Indented);
                }
            }
            catch { }

            return payload.ToString(Formatting.Indented);
        }

        private void SwitchView()
        {
            if (viewMode == ViewMode.Visual)
            {
                if (rootToken != null)
                    editText = rootToken.ToString(Formatting.Indented);
                viewMode = ViewMode.Text;
                return;
            }

            try
            {
                rootToken = JToken.Parse(editText);
                ClearVisualState();
                parseError = false;
                parseErrorMessage = "";
                viewMode = ViewMode.Visual;
            }
            catch (Exception ex)
            {
                parseError = true;
                parseErrorMessage = ex.Message;
                viewMode = ViewMode.Visual;
            }
        }

        private void ValidateCurrent()
        {
            JToken token = null;

            if (viewMode == ViewMode.Visual && rootToken != null)
            {
                token = rootToken;
            }
            else
            {
                try
                {
                    token = JToken.Parse(editText);
                }
                catch (Exception ex)
                {
                    SetStatus("Invalid JSON: " + ex.Message, MessageType.Error);
                    return;
                }
            }

            List<string> warnings = new List<string>();
            CollectValidationWarnings(token, "$", warnings);
            if (warnings.Count == 0)
            {
                SetStatus("JSON valid. No table warning found.", MessageType.Info);
                EditorUtility.DisplayDialog(WindowTitle, "JSON is valid.", "OK");
            }
            else
            {
                string msg = string.Join("\n", warnings.Take(30).ToArray());
                SetStatus("JSON valid with " + warnings.Count + " warning(s).", MessageType.Warning);
                EditorUtility.DisplayDialog(WindowTitle, "JSON is valid but has warning(s):\n\n" + msg, "OK");
            }
        }

        private void CollectValidationWarnings(JToken token, string path, List<string> warnings)
        {
            if (token is JObject obj)
            {
                foreach (JProperty prop in obj.Properties())
                    CollectValidationWarnings(prop.Value, path + "." + prop.Name, warnings);
                return;
            }

            if (!(token is JArray arr))
                return;

            List<JObject> objects = arr.OfType<JObject>().ToList();
            if (objects.Count > 0)
            {
                var duplicateIds = objects
                    .Select((row, index) => new { Index = index, Id = (row["id"] ?? row["Id"] ?? row["ID"])?.Value<string>() })
                    .Where(x => !string.IsNullOrWhiteSpace(x.Id))
                    .GroupBy(x => x.Id)
                    .Where(group => group.Count() > 1);

                foreach (var group in duplicateIds)
                    warnings.Add(path + ": duplicate id '" + group.Key + "' at rows " + string.Join(", ", group.Select(x => (x.Index + 1).ToString()).ToArray()));
            }

            for (int i = 0; i < arr.Count; i++)
                CollectValidationWarnings(arr[i], path + "[" + i + "]", warnings);
        }

        private void ReloadCurrent()
        {
            if (!HasSelectedFile()) return;
            if (isDirty && !EditorUtility.DisplayDialog(WindowTitle, "Discard unsaved changes?", "Reload", "Cancel")) return;
            LoadFile(CurrentFile);
        }

        private void FormatJson()
        {
            try
            {
                JToken token = JToken.Parse(viewMode == ViewMode.Text ? editText : rootToken.ToString(Formatting.None));
                editText = token.ToString(Formatting.Indented);
                rootToken = token;
                ClearVisualState();
                MarkDirty();
                SetStatus("Formatted.", MessageType.Info);
            }
            catch (Exception ex)
            {
                SetStatus("Format failed: " + ex.Message, MessageType.Error);
            }
        }

        private bool ConfirmLeaveDirtyFile()
        {
            if (!isDirty) return true;

            int result = EditorUtility.DisplayDialogComplex(WindowTitle, "Current file has unsaved changes.", "Save", "Discard", "Cancel");
            if (result == 0)
            {
                SaveCurrent();
                return !isDirty;
            }

            if (result == 1)
            {
                isDirty = false;
                hasUnsavedChanges = false;
                if (HasSelectedFile()) CurrentFile.Dirty = false;
                return true;
            }

            return false;
        }

        private void MarkDirty()
        {
            isDirty = true;
            hasUnsavedChanges = true;
            saveChangesMessage = "Save changes to the selected Dreamy data file?";
            if (HasSelectedFile()) CurrentFile.Dirty = true;
        }

        private void OnTokenChanged()
        {
            // Write decoded JSON back as a string, preserving its original storage type.
            for (int i = inspection.Count - 1; i >= 0; i--)
                if (inspection[i].EncodedSource != null)
                    inspection[i].EncodedSource.Value = inspection[i].Token.ToString(Formatting.None);
            cachedColumns.Clear();
            cachedRows.Clear();
            cachedFilters.Clear();
            MarkDirty();
            SetStatus("", MessageType.None);
        }

        private void ClearCurrentFile()
        {
            selectedFileIndex = -1;
            rootToken = null;
            editText = "";
            originalRawFile = "";
            isDirty = false;
            hasUnsavedChanges = false;
            parseError = false;
            ClearVisualState();
        }

        private void ClearVisualState()
        {
            expandedValues.Clear();
            inlinePages.Clear();
            inlinePaths.Clear();
            tableRowOffsets.Clear();
            inspection.Clear();
            drawnArrays.Clear();
            cachedColumns.Clear();
            cachedRows.Clear();
            cachedFilters.Clear();
            collapsed.Clear();
            tableScroll.Clear();
            tableFilter.Clear();
            columnWidths.Clear();
            selectedTablePath = "";
            selectedRowIndex = -1;
            selectedColumnKey = "";
            selectedCellRow = -1;
            visualScroll = Vector2.zero;
            textScroll = Vector2.zero;
        }

        private void ToggleCollapse(string path)
        {
            if (collapsed.Contains(path)) collapsed.Remove(path);
            else collapsed.Add(path);
        }

        private bool HasSelectedFile()
        {
            return selectedFileIndex >= 0 && selectedFileIndex < files.Count;
        }

        private FileEntry CurrentFile
        {
            get { return HasSelectedFile() ? files[selectedFileIndex] : null; }
        }

        private void CreateNewConfig()
        {
            if (!ConfirmLeaveDirtyFile()) return;
            string folder = Path.GetFullPath(DefaultDataConfigFolder);
            Directory.CreateDirectory(folder);

            string name = "new_config.json";
            string path = Path.Combine(folder, name);
            int index = 1;
            while (File.Exists(path))
            {
                name = "new_config_" + index + ".json";
                path = Path.Combine(folder, name);
                index++;
            }

            File.WriteAllText(path, "{\n  \"items\": []\n}\n", Encoding.UTF8);
            AssetDatabase.Refresh();
            RefreshFiles(false);
            SetStatus("Created " + name, MessageType.Info);
        }

        private void OpenDataConfigFolder()
        {
            string folder = Path.GetFullPath(DefaultDataConfigFolder);
            Directory.CreateDirectory(folder);
            AssetDatabase.Refresh();
            EditorUtility.RevealInFinder(folder);
        }

        private void OpenSaveFolder()
        {
            string folder = GetSaveDirectory();
            Directory.CreateDirectory(folder);
            EditorUtility.RevealInFinder(folder);
        }

        public override void SaveChanges()
        {
            SaveCurrent();
            if (!isDirty) base.SaveChanges();
        }

        public override void DiscardChanges()
        {
            isDirty = false;
            if (HasSelectedFile()) CurrentFile.Dirty = false;
            base.DiscardChanges();
        }

        private bool CanResetTestSaves()
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode) return true;
            SetStatus("Stop Play Mode before resetting saves. The running game holds loaded data and can save it again.", MessageType.Warning);
            EditorUtility.DisplayDialog(WindowTitle, "Stop Play Mode before resetting saves. Then reset and enter Play Mode again to test fresh data.", "OK");
            return false;
        }

        private void DeleteSave(FileEntry file)
        {
            if (sourceMode != SourceMode.Datasave || file == null || !CanResetTestSaves()) return;
            string message = "Reset test save '" + file.RelativePath + "'?\n\nThis deletes its main file, runtime backup (.bak), temporary file (.tmp), and Editor backups (.bak-*). No new backup is created. Other saves and production backup behavior are unchanged.";
            if (file == CurrentFile && isDirty) message += "\nUnsaved edits will be discarded.";
            if (!EditorUtility.DisplayDialog(WindowTitle, message, "Reset Save", "Cancel")) return;
            try
            {
                int count = DeleteSaveFilesForTesting(file.FullPath);
                if (file == CurrentFile) ClearCurrentFile();
                RefreshFiles(false);
                SetStatus("Reset: " + file.RelativePath + " (" + count + " file(s) removed, no backup created).", MessageType.Info);
            }
            catch (Exception ex) { SetStatus("Reset failed: " + ex.Message, MessageType.Error); }
        }

        private void DeleteAllSaves()
        {
            if (sourceMode != SourceMode.Datasave || !CanResetTestSaves()) return;
            if (!EditorUtility.DisplayDialog(WindowTitle, "Reset all listed test saves and their backups? No new backup is created. Unsaved edits will be discarded.", "Reset Saves", "Cancel")) return;
            int removed = 0;
            List<string> failures = new List<string>();
            foreach (FileEntry file in files.ToList())
            {
                try { removed += DeleteSaveFilesForTesting(file.FullPath); }
                catch (Exception ex) { failures.Add(file.RelativePath + ": " + ex.Message); }
            }
            ClearCurrentFile();
            RefreshFiles(false);
            SetStatus(failures.Count == 0 ? "Reset saves: " + removed + " file(s) removed, no backup created."
                : "Reset incomplete: " + string.Join("; ", failures), failures.Count == 0 ? MessageType.Info : MessageType.Error);
        }

        private static string GetPrimarySavePath(string path)
        {
            string name = Path.GetFileName(path);
            if (name.EndsWith(".bak", StringComparison.Ordinal) || name.EndsWith(".tmp", StringComparison.Ordinal))
                return path.Substring(0, path.Length - 4);
            int marker = name.IndexOf(".bak-", StringComparison.Ordinal);
            return marker < 0 ? path : Path.Combine(Path.GetDirectoryName(path), name.Substring(0, marker));
        }

        private static int DeleteSaveFilesForTesting(string path)
        {
            string directory = Path.GetDirectoryName(path);
            if (!Directory.Exists(directory)) return 0;
            string name = Path.GetFileName(path);
            // Delete recovery sources first; leave the primary intact if cleanup fails.
            List<string> companions = Directory.GetFiles(directory)
                .Where(candidate =>
                {
                    string candidateName = Path.GetFileName(candidate);
                    return candidateName == name + ".bak" || candidateName == name + ".tmp" ||
                           candidateName.StartsWith(name + ".bak-", StringComparison.Ordinal);
                }).ToList();
            int removed = 0;
            foreach (string companion in companions) { File.Delete(companion); removed++; }
            if (File.Exists(path)) { File.Delete(path); removed++; }
            return removed;
        }

        private void Backup(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
            File.Copy(path, path + ".bak-" + DateTime.Now.ToString("yyyyMMdd-HHmmss-fffffff"), false);
        }

        private void RevealCurrent()
        {
            if (HasSelectedFile()) EditorUtility.RevealInFinder(CurrentFile.FullPath);
        }

        private void ShowFileContextMenu(int index)
        {
            if (index < 0 || index >= files.Count) return;
            FileEntry file = files[index];
            GenericMenu menu = new GenericMenu();

            menu.AddItem(new GUIContent("Open"), false, delegate { SelectFile(index); });
            menu.AddItem(new GUIContent("Reveal"), false, delegate { EditorUtility.RevealInFinder(file.FullPath); });
            menu.AddItem(new GUIContent("Copy Path"), false, delegate { EditorGUIUtility.systemCopyBuffer = file.RelativePath; });
            if (sourceMode == SourceMode.Datasave)
                menu.AddItem(new GUIContent("Reset Save (no backup)"), false, delegate { DeleteSave(file); });
            menu.ShowAsContext();
        }

        private void DrawStatus(Rect rect)
        {
            Color bg = statusType == MessageType.Error ? new Color(0.45f, 0.08f, 0.08f) :
                statusType == MessageType.Warning ? new Color(0.38f, 0.30f, 0.06f) :
                statusType == MessageType.Info ? new Color(0.08f, 0.28f, 0.45f) :
                new Color(0.17f, 0.17f, 0.17f);

            EditorGUI.DrawRect(rect, bg);
            GUI.Label(new Rect(rect.x + 8, rect.y + 3, rect.width - 16, rect.height - 6), status, EditorStyles.miniLabel);
        }

        private void SetStatus(string msg, MessageType type)
        {
            status = msg ?? "";
            statusType = type;
            Repaint();
        }

        private static string GetSaveDirectory()
        {
            return Path.Combine(Application.persistentDataPath, SaveDirectoryName);
        }

        private static string NormalizePath(string path)
        {
            return path.Replace('\\', '/');
        }

        private sealed class StringInputWindow : EditorWindow
        {
            private string label;
            private string value;
            private Action<string> onApply;

            public static void Open(string title, string labelText, string initial, Action<string> apply)
            {
                StringInputWindow w = CreateInstance<StringInputWindow>();
                w.titleContent = new GUIContent(title);
                w.label = labelText;
                w.value = initial ?? "";
                w.onApply = apply;
                w.minSize = new Vector2(340, 92);
                w.ShowUtility();
                w.Focus();
            }

            private void OnGUI()
            {
                GUILayout.Space(8);
                GUILayout.Label(label, EditorStyles.boldLabel);
                GUI.SetNextControlName("input");
                value = EditorGUILayout.TextField(value);

                GUILayout.FlexibleSpace();
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Cancel", GUILayout.Width(82))) Close();
                if (GUILayout.Button("Apply", GUILayout.Width(82)))
                {
                    onApply?.Invoke(value);
                    Close();
                }

                GUILayout.EndHorizontal();

                if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return)
                {
                    onApply?.Invoke(value);
                    Close();
                    Event.current.Use();
                }

                EditorGUI.FocusTextInControl("input");
            }
        }

        private sealed class JsonTokenEditWindow : EditorWindow
        {
            private string text;
            private Vector2 scroll;
            private Action<JToken> onApply;
            private string error;
            private GUIStyle style;

            public static void Open(JToken token, Action<JToken> apply)
            {
                JsonTokenEditWindow w = CreateInstance<JsonTokenEditWindow>();
                w.titleContent = new GUIContent("Edit JSON Value");
                w.text = (token ?? JValue.CreateNull()).ToString(Formatting.Indented);
                w.onApply = apply;
                w.minSize = new Vector2(540, 380);
                w.ShowUtility();
                w.Focus();
            }

            private void OnGUI()
            {
                if (style == null)
                    style = new GUIStyle(EditorStyles.textArea) { font = GetMonoFont(), fontSize = 12, wordWrap = false };

                scroll = EditorGUILayout.BeginScrollView(scroll);
                text = EditorGUILayout.TextArea(text, style, GUILayout.ExpandHeight(true));
                EditorGUILayout.EndScrollView();

                if (!string.IsNullOrEmpty(error))
                    EditorGUILayout.HelpBox(error, MessageType.Error);

                GUILayout.BeginHorizontal();

                if (GUILayout.Button("Format", GUILayout.Width(90)))
                {
                    try
                    {
                        text = JToken.Parse(text).ToString(Formatting.Indented);
                        error = "";
                    }
                    catch (Exception ex)
                    {
                        error = ex.Message;
                    }
                }

                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Cancel", GUILayout.Width(90))) Close();
                if (GUILayout.Button("Apply", GUILayout.Width(90)))
                {
                    try
                    {
                        onApply?.Invoke(JToken.Parse(text));
                        Close();
                    }
                    catch (Exception ex)
                    {
                        error = ex.Message;
                    }
                }

                GUILayout.EndHorizontal();
            }
        }
    }
}
