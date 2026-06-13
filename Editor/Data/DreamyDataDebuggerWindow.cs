using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace Dreamy.EditorTools {
    public class DreamyDataDebuggerWindow : EditorWindow {
        // ─── Layout constants ─────────────────────────────────────
        private float _sidebarWidth = 250f;
        private const float SidebarMinW = 180f;
        private const float SidebarMaxW = 380f;
        private const float ToolbarHeight = 30f;
        private const float StatusBarHeight = 22f;
        private const float SplitterWidth = 4f;
        private const float CellH = 30f;
        private const float RowNumW = 52f;
        private const float ActionsColW = 74f; // ≡ + ✕ + ⧉ + drag visual room
        private const float PropLabelW = 220f;
        private const float MaxTableH = 720f;
        private const float ResizeHandleW = 6f;
        private const float MinDataColW = 86f;
        private const float MaxDataColW = 560f;
        private const string SaveDirectoryName = "DreamySaves";
        private const string DataConfigFolderMarker = "/Resources/DataConfig/";
        private const string DefaultDataConfigFolder = "Assets/_Project/Resources/DataConfig";

        private enum DataSourceMode {
            DataConfig,
            Datasave
        }

        private DataSourceMode _sourceMode = DataSourceMode.DataConfig;
        private string _loadedRawFile = "";
        private bool _savePayloadWasString;

        // ─── View mode ────────────────────────────────────────────
        private enum ViewMode {
            Visual,
            Text
        }

        private ViewMode _viewMode = ViewMode.Visual;

        private enum CsvImportMode {
            Replace,
            Append,
            UpdateByKey
        }

        private enum SortValueMode {
            Auto,
            Number,
            Text,
            Boolean,
            Date
        }

        private class SortRule {
            public string Key = "";
            public bool Ascending = true;
            public SortValueMode Mode = SortValueMode.Auto;
            public bool EmptyLast = true;

            public SortRule Clone() {
                return new SortRule { Key = Key, Ascending = Ascending, Mode = Mode, EmptyLast = EmptyLast };
            }
        }

        // ─── File list ────────────────────────────────────────────
        private List<JsonFileEntry> _files = new();
        private int _selectedIndex = -1;
        private string _searchFilter = "";
        private Vector2 _sidebarScroll;

        // ─── Edit state ───────────────────────────────────────────
        private string _editText = "";
        private bool _isDirty;

        // ─── Visual mode state ────────────────────────────────────
        private JToken _rootToken;
        private bool _parseError;
        private string _parseErrorMsg;
        private HashSet<string> _collapsed = new();
        private Dictionary<string, float> _columnWidths = new();
        private Dictionary<string, Vector2> _tableScrolls = new();
        private Dictionary<string, string> _tableFilters = new();
        private Dictionary<string, List<SortRule>> _lastSortRules = new();
        private Dictionary<string, string> _lastSortLabels = new();
        private Vector2 _visualScroll;
        private bool _showTypeBadges = true;

        // ─── Table selection & clipboard ──────────────────────────
        private string _selTablePath = "";
        private int _selRowIndex = -1;
        private string _selCellKey = "";
        private int _selCellRowIndex = -1;
        private JObject _clipboardRow;
        private string _clipboardJson = "";

        // ─── Row drag & drop ──────────────────────────────────────
        private string _dragTablePath = "";
        private int _dragRowIndex = -1;
        private int _dragDropVisualIndex = -1;
        private bool _isDraggingRow;
        private Vector2 _dragMouseStart;
        private string _dragLabel = "";
        // IMGUI-safe deferred row move. Do not mutate JArray inside BeginScrollView.
        private string _pendingMoveTablePath = "";
        private int _pendingMoveFromIndex = -1;
        private int _pendingMoveTargetIndex = -1;
        private const float DragHandleW = 18f;
        private const float DragStartDistance = 4f;

        // ─── Column resize drag ───────────────────────────────────
        private string _resizingColKey = "";

        // ─── Text mode ────────────────────────────────────────────
        private Vector2 _textScroll;
        private GUIStyle _monoStyle;

        // ─── Status bar ───────────────────────────────────────────
        private string _statusMsg = "";
        private MessageType _statusType = MessageType.None;

        // ─── Styles ───────────────────────────────────────────────
        private GUIStyle _sidebarItem;
        private GUIStyle _sidebarSelected;
        private GUIStyle _cellField;
        private GUIStyle _headerCell;
        private GUIStyle _rowNumStyle;
        private GUIStyle _actionBtn;
        private bool _stylesInit;

        // ─── Colors ───────────────────────────────────────────────
        private static readonly Color ClrSelected = new(0.22f, 0.44f, 0.80f, 0.90f);
        private static readonly Color ClrSep = new(0.11f, 0.11f, 0.11f, 1f);
        private static readonly Color ClrHeaderBg = new(0.18f, 0.20f, 0.30f, 1f);
        private static readonly Color ClrResizeBar = new(0.45f, 0.55f, 0.80f, 0.80f);
        private static readonly Color ClrRowEven = new(0.155f, 0.155f, 0.20f, 1f);
        private static readonly Color ClrRowOdd = new(0.175f, 0.18f, 0.225f, 1f);
        private static readonly Color ClrSectionBg = new(0.19f, 0.19f, 0.24f, 1f);
        private static readonly Color ClrPropRow = new(0.15f, 0.15f, 0.19f, 1f);
        private static readonly Color ClrChip = new(0.22f, 0.26f, 0.34f, 1f);
        private static readonly Color ClrNested = new(0.25f, 0.30f, 0.42f, 1f);
        private static readonly Color ClrDirty = new(1f, 0.85f, 0.3f, 1f);
        private static readonly Color ClrGood = new(0.45f, 0.95f, 0.5f, 1f);
        private static readonly Color ClrErr = new(1f, 0.38f, 0.38f, 1f);

        // ─────────────────────────────────────────────────────────
        [MenuItem("Tools/Dreamy/Data Debugger")]
        public static void ShowWindow() {
            var w = GetWindow<DreamyDataDebuggerWindow>("Dreamy Data Debugger");
            w.minSize = new Vector2(1040, 620);
            w.Show();
        }

        private void OnEnable() => RefreshFileList();

        // ─── Style init ───────────────────────────────────────────
        private void InitStyles() {
            if (_stylesInit) return;
            _stylesInit = true;

            _sidebarItem = new GUIStyle(EditorStyles.label)
            {
                padding = new RectOffset(10, 4, 4, 4), fontSize = 12, richText = true,
            };
            _sidebarSelected = new GUIStyle(_sidebarItem);
            _sidebarSelected.normal.textColor = Color.white;

            var mono = GetMonoFont();
            _monoStyle = new GUIStyle(EditorStyles.textArea) { font = mono, fontSize = 13, wordWrap = false };

            _cellField = new GUIStyle(EditorStyles.textField)
            {
                font = mono, fontSize = 12,
                padding = new RectOffset(4, 4, 3, 3),
                alignment = TextAnchor.MiddleLeft,
            };

            _headerCell = new GUIStyle(EditorStyles.label)
            {
                font = mono, fontSize = 12, fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                padding = new RectOffset(6, 4, 0, 0),
            };
            _headerCell.normal.textColor = new Color(0.82f, 0.88f, 1f);

            _rowNumStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            {
                fontSize = 10, alignment = TextAnchor.MiddleCenter,
            };

            _actionBtn = new GUIStyle(EditorStyles.miniButton)
            {
                padding = new RectOffset(2, 2, 2, 2), fontSize = 11,
            };
        }

        private static Font GetMonoFont() {
            var installed = new HashSet<string>(Font.GetOSInstalledFontNames(), StringComparer.OrdinalIgnoreCase);
            foreach (var n in new[] { "Consolas", "Courier New", "Courier", "Lucida Console", "Monaco", "Menlo" })
                if (installed.Contains(n))
                    return Font.CreateDynamicFontFromOSFont(n, 13);
            return EditorStyles.textArea.font;
        }

        // ═══════════════════════════════════════════════════════════
        //  OnGUI
        // ═══════════════════════════════════════════════════════════
        private void OnGUI() {
            HandleKeyboard();
            HandleColumnResizeDrag();
            InitStyles();
            DrawToolbar();

            float bodyY = ToolbarHeight;
            float bodyH = position.height - ToolbarHeight - StatusBarHeight;

            DrawSidebar(new Rect(0, bodyY, _sidebarWidth, bodyH));
            DrawSidebarSplitter(new Rect(_sidebarWidth, bodyY, SplitterWidth, bodyH));
            DrawContent(new Rect(_sidebarWidth + SplitterWidth, bodyY,
                position.width - _sidebarWidth - SplitterWidth, bodyH));
            DrawStatusBar(new Rect(0, position.height - StatusBarHeight, position.width, StatusBarHeight));
        }

        // ─── Keyboard shortcuts ────────────────────────────────────
        private void HandleKeyboard() {
            var e = Event.current;
            if (e.type != EventType.KeyDown) return;
            bool ctrl = e.control || e.command;

            if (ctrl && e.keyCode == KeyCode.S) {
                SaveCurrent();
                e.Use();
                return;
            }

            if (!ctrl && e.keyCode == KeyCode.Escape) {
                ClearSelection();
                e.Use();
                return;
            }

            if (_selRowIndex < 0 || string.IsNullOrEmpty(_selTablePath)) return;

            if (ctrl && e.keyCode == KeyCode.C) {
                CopySelectedRow();
                e.Use();
            }
            else if (ctrl && e.keyCode == KeyCode.V) {
                PasteRowBelow();
                e.Use();
            }
            else if (ctrl && e.keyCode == KeyCode.D) {
                DuplicateSelectedRow();
                e.Use();
            }
            else if (e.keyCode == KeyCode.Delete) {
                DeleteSelectedRow();
                e.Use();
            }
            else if (ctrl && e.keyCode == KeyCode.UpArrow) {
                MoveSelectedRow(-1);
                e.Use();
            }
            else if (ctrl && e.keyCode == KeyCode.DownArrow) {
                MoveSelectedRow(1);
                e.Use();
            }
            else if (!ctrl && e.keyCode == KeyCode.UpArrow) {
                ShiftSelection(-1);
                e.Use();
            }
            else if (!ctrl && e.keyCode == KeyCode.DownArrow) {
                ShiftSelection(1);
                e.Use();
            }
        }

        // ─── Column resize drag (handled before any groups) ────────
        private void HandleColumnResizeDrag() {
            if (string.IsNullOrEmpty(_resizingColKey)) return;
            var e = Event.current;
            if (e.type == EventType.MouseDrag) {
                if (_columnWidths.ContainsKey(_resizingColKey))
                    _columnWidths[_resizingColKey] = Mathf.Max(40f, _columnWidths[_resizingColKey] + e.delta.x);
                Repaint();
                e.Use();
            }
            else if (e.type == EventType.MouseUp) {
                _resizingColKey = "";
                Repaint();
            }
        }

        // ═══════════════════════════════════════════════════════════
        //  Toolbar
        // ═══════════════════════════════════════════════════════════
        private void DrawToolbar() {
            EditorGUI.DrawRect(new Rect(0, 0, position.width, ToolbarHeight), new Color(0.2f, 0.2f, 0.2f));
            GUILayout.BeginArea(new Rect(0, 0, position.width, ToolbarHeight));
            GUILayout.BeginHorizontal(EditorStyles.toolbar);

            DataSourceMode nextSource = (DataSourceMode)GUILayout.Toolbar(
                (int)_sourceMode,
                new[] { "DataConfig", "Datasave" },
                EditorStyles.toolbarButton,
                GUILayout.Width(190));

            if (nextSource != _sourceMode) {
                if (ConfirmSwitchSource()) {
                    _sourceMode = nextSource;
                    ClearCurrentFileState();
                    RefreshFileList();
                }
            }

            if (GUILayout.Button("↺", EditorStyles.toolbarButton, GUILayout.Width(28))) RefreshFileList();

            bool hasSel = _selectedIndex >= 0 && _selectedIndex < _files.Count;

            using (new EditorGUI.DisabledScope(!_isDirty || !hasSel))
                if (GUILayout.Button(_sourceMode == DataSourceMode.Datasave ? "💾 Save Payload" : "💾 Save", EditorStyles.toolbarButton, GUILayout.Width(_sourceMode == DataSourceMode.Datasave ? 104 : 62)))
                    SaveCurrent();

            using (new EditorGUI.DisabledScope(!hasSel)) {
                if (GUILayout.Button("⟲ Reload", EditorStyles.toolbarButton, GUILayout.Width(68))) ReloadCurrent();
                if (_viewMode == ViewMode.Text &&
                    GUILayout.Button("{ } Format", EditorStyles.toolbarButton, GUILayout.Width(76))) FormatJson();
                if (GUILayout.Button("📋 Name", EditorStyles.toolbarButton, GUILayout.Width(72))) CopyCurrentConfigName();
                if (GUILayout.Button("📋 Path", EditorStyles.toolbarButton, GUILayout.Width(68))) CopyCurrentConfigPath();
                if (GUILayout.Button("Folder", EditorStyles.toolbarButton, GUILayout.Width(58))) RevealCurrentFile();
            }

            GUILayout.Space(6);

            if (_sourceMode == DataSourceMode.DataConfig) {
                if (GUILayout.Button("+ New Config", EditorStyles.toolbarButton, GUILayout.Width(92)))
                    CreateNewDataConfigFile();
                if (GUILayout.Button("📋 Remote JSON", EditorStyles.toolbarButton, GUILayout.Width(108)))
                    CopyRemoteConfigJsonTemplate();
            }
            else {
                if (GUILayout.Button("🗑 Delete All Saves", EditorStyles.toolbarButton, GUILayout.Width(122)))
                    DeleteAllSaveFiles();
            }

            GUILayout.Space(10);

            using (new EditorGUI.DisabledScope(!hasSel)) {
                string lbl = _viewMode == ViewMode.Visual ? "◧ Text" : "⊞ Visual";
                ViewMode next = _viewMode == ViewMode.Visual ? ViewMode.Text : ViewMode.Visual;
                if (GUILayout.Button(lbl, EditorStyles.toolbarButton, GUILayout.Width(72))) SwitchViewMode(next);
            }

            GUILayout.Space(8);
            _showTypeBadges = GUILayout.Toggle(_showTypeBadges, "Types", EditorStyles.toolbarButton, GUILayout.Width(54));

            GUILayout.FlexibleSpace();

            var hintS = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(0.4f, 0.4f, 0.4f) } };
            GUILayout.Label(_sourceMode == DataSourceMode.Datasave
                ? "Datasave: only Payload is shown/edited | Ctrl+S  C/V/D  Del  Ctrl+↑↓  drag ≡"
                : "DataConfig: */Resources/DataConfig/*.json | Ctrl+S  C/V/D  Del  Ctrl+↑↓  drag ≡",
                hintS);

            if (_isDirty && hasSel) {
                var prev = GUI.color;
                GUI.color = ClrDirty;
                GUILayout.Label("● unsaved", EditorStyles.toolbarButton);
                GUI.color = prev;
            }

            GUILayout.Space(4);
            GUILayout.EndHorizontal();
            GUILayout.EndArea();
        }

        // ═══════════════════════════════════════════════════════════
        //  Sidebar
        // ═══════════════════════════════════════════════════════════
        private void DrawSidebar(Rect rect) {
            EditorGUI.DrawRect(rect, new Color(0.17f, 0.17f, 0.17f));
            _searchFilter = EditorGUI.TextField(
                new Rect(rect.x + 4, rect.y + 4, rect.width - 8, 20),
                _searchFilter, EditorStyles.toolbarSearchField);

            var listR = new Rect(rect.x, rect.y + 28, rect.width, rect.height - 28);
            GUILayout.BeginArea(listR);
            _sidebarScroll = GUILayout.BeginScrollView(_sidebarScroll, false, false);

            for (int i = 0; i < _files.Count; i++) {
                var f = _files[i];
                if (!string.IsNullOrEmpty(_searchFilter) &&
                    !f.DisplayName.Contains(_searchFilter, StringComparison.OrdinalIgnoreCase)) continue;

                bool sel = i == _selectedIndex;
                var ir = GUILayoutUtility.GetRect(listR.width, 28, GUILayout.ExpandWidth(true));
                if (sel) EditorGUI.DrawRect(ir, ClrSelected);
                else if (ir.Contains(Event.current.mousePosition)) {
                    EditorGUI.DrawRect(ir, new Color(0.3f, 0.3f, 0.3f, 0.5f));
                    Repaint();
                }

                EditorGUI.LabelField(ir, (f.IsDirty ? "● " : "    ") + f.DisplayName,
                    sel ? _sidebarSelected : _sidebarItem);

                if (Event.current.type == EventType.ContextClick && ir.Contains(Event.current.mousePosition)) {
                    ShowFileContextMenu(i);
                    Event.current.Use();
                }
                else if (Event.current.type == EventType.MouseDown && ir.Contains(Event.current.mousePosition)) {
                    if (Event.current.button == 0) {
                        SelectFile(i);
                        if (Event.current.clickCount == 2) RevealCurrentFile();
                    }
                    Event.current.Use();
                }
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }


        // ─── Resizable sidebar splitter ───────────────────────────
        private void DrawSidebarSplitter(Rect rect) {
            EditorGUI.DrawRect(rect, ClrSep);
            EditorGUIUtility.AddCursorRect(rect, MouseCursor.ResizeHorizontal);

            var e = Event.current;
            if (e.type == EventType.MouseDrag && rect.Contains(e.mousePosition)) {
                _sidebarWidth = Mathf.Clamp(_sidebarWidth + e.delta.x, SidebarMinW, SidebarMaxW);
                Repaint();
                e.Use();
            }
        }

        // ═══════════════════════════════════════════════════════════
        //  Content area
        // ═══════════════════════════════════════════════════════════
        private void DrawContent(Rect rect) {
            if (_selectedIndex < 0 || _selectedIndex >= _files.Count) {
                EditorGUI.DrawRect(rect, new Color(0.14f, 0.14f, 0.14f));
                GUI.Label(rect, "← Select a config file",
                    new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 14 });
                return;
            }

            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 22), new Color(0.15f, 0.15f, 0.2f));
            GUI.Label(new Rect(rect.x, rect.y, rect.width, 22),
                "📄  " + _files[_selectedIndex].RelativePath,
                new GUIStyle(EditorStyles.miniLabel)
                {
                    padding = new RectOffset(8, 8, 4, 4),
                    normal = { textColor = new Color(0.65f, 0.7f, 0.8f) }
                });
            EditorGUI.DrawRect(new Rect(rect.x, rect.y + 22, rect.width, 1), ClrSep);

            var bodyR = new Rect(rect.x, rect.y + 23, rect.width, rect.height - 23);
            if (_viewMode == ViewMode.Visual) DrawVisualMode(bodyR);
            else DrawTextMode(bodyR);
        }

        // ═══════════════════════════════════════════════════════════
        //  VISUAL MODE
        // ═══════════════════════════════════════════════════════════
        private void DrawVisualMode(Rect rect) {
            if (_parseError) {
                EditorGUI.DrawRect(rect, new Color(0.18f, 0.1f, 0.1f));
                EditorGUI.HelpBox(new Rect(rect.x + 12, rect.y + 12, rect.width - 24, 56),
                    "JSON parse error: " + _parseErrorMsg, MessageType.Error);
                if (GUI.Button(new Rect(rect.x + 12, rect.y + 76, 180, 26), "Switch to Text Mode to fix"))
                    SwitchViewMode(ViewMode.Text);
                return;
            }

            if (_rootToken == null) return;

            GUILayout.BeginArea(rect);
            _visualScroll = GUILayout.BeginScrollView(_visualScroll, false, true);

            if (_rootToken is JObject rootObj) DrawJObjectProperties(rootObj, "root");
            else if (_rootToken is JArray rootArr) {
                if (rootArr.Count > 0 && rootArr[0] is JObject) DrawObjectTable(rootArr, "root");
                else DrawPrimitiveArraySection(rootArr, "root", "root");
            }

            GUILayout.Space(20);
            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        // ── JObject property grid ─────────────────────────────────
        private void DrawJObjectProperties(JObject obj, string path) {
            foreach (var prop in obj.Properties().ToList()) {
                string pp = path + "." + prop.Name;
                var val = prop.Value;
                if (val is JArray arr && arr.Count > 0 && arr[0] is JObject) DrawArraySection(arr, pp, prop.Name);
                else if (val is JArray primArr) DrawPrimitiveArraySection(primArr, pp, prop.Name);
                else if (val is JObject nested) DrawNestedObjectSection(nested, pp, prop.Name);
                else DrawPrimitiveRow(prop, pp);
            }
        }

        // ── Primitive key-value row ────────────────────────────────
        private void DrawPrimitiveRow(JProperty prop, string path) {
            var rowR = GUILayoutUtility.GetRect(GUIContent.none, GUIStyle.none,
                GUILayout.Height(CellH), GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rowR, ClrPropRow);
            EditorGUI.DrawRect(new Rect(rowR.x, rowR.yMax - 1, rowR.width, 1), ClrSep);
            float x = rowR.x + 8, y = rowR.y + 1, h = CellH - 2;
            GUI.Label(new Rect(x, y, PropLabelW, h), prop.Name, EditorStyles.label);
            DrawTokenValueField(prop.Value,
                new Rect(x + PropLabelW + 4, y, rowR.width - PropLabelW - 20, h),
                tok => {
                    prop.Value = tok;
                    OnTokenChanged();
                });
        }

        // ── Collapsible array-of-objects section ──────────────────
        private void DrawArraySection(JArray arr, string path, string label) {
            bool collapsed = _collapsed.Contains(path);
            var hdrR = GUILayoutUtility.GetRect(0, 28, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(hdrR, ClrSectionBg);
            EditorGUI.DrawRect(new Rect(hdrR.x, hdrR.yMax, hdrR.width, 1), ClrSep);

            float bx = hdrR.x + 4, by = hdrR.y + 5;

            if (GUI.Button(new Rect(bx, by, 18, 18), collapsed ? "▶" : "▼", EditorStyles.miniButton)) {
                if (collapsed) _collapsed.Remove(path);
                else _collapsed.Add(path);
            }

            bx += 22;

            var lblS = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = new Color(0.8f, 0.9f, 1f) } };
            GUI.Label(new Rect(bx, by, 280, 18), $"{label}   [{arr.Count} rows]", lblS);

            // Right-side buttons (right-aligned)
            float rx = hdrR.xMax - 4;

            // Auto-fit columns
            rx -= 58;
            if (GUI.Button(new Rect(rx, by, 54, 18), "↔ Fit", EditorStyles.miniButton)) {
                var toRemove = _columnWidths.Keys.Where(k => k.StartsWith(path + ".")).ToList();
                foreach (var k in toRemove) _columnWidths.Remove(k);
                _collapsed.Remove(path);
                Repaint();
            }

            rx -= 4;

            // Add column / field
            rx -= 64;
            if (GUI.Button(new Rect(rx, by, 60, 18), "+ Field", EditorStyles.miniButton)) {
                StringInputWindow.Open("Add Field", "Field name:", "", name => AddColumnToRows(arr, name));
            }

            rx -= 4;

            // Paste row
            rx -= 62;
            using (new EditorGUI.DisabledScope(_clipboardRow == null)) {
                if (GUI.Button(new Rect(rx, by, 58, 18), "⧉ Paste", EditorStyles.miniButton)) {
                    arr.Add((JObject)_clipboardRow.DeepClone());
                    _selTablePath = path;
                    _selRowIndex = arr.Count - 1;
                    _collapsed.Remove(path);
                    OnTokenChanged();
                }
            }

            rx -= 4;

            // Add row
            rx -= 54;
            if (GUI.Button(new Rect(rx, by, 50, 18), "+ Add", EditorStyles.miniButton)) {
                AddCloneRow(arr);
                _selTablePath = path;
                _selRowIndex = arr.Count - 1;
                _collapsed.Remove(path);
                OnTokenChanged();
            }

            if (!collapsed) {
                DrawObjectTable(arr, path);
                GUILayout.Space(4);
            }
        }


        private List<int> BuildVisibleRowIndices(JArray arr, string filter) {
            var result = new List<int>();
            bool useFilter = !string.IsNullOrWhiteSpace(filter);
            string needle = useFilter ? filter.Trim() : "";

            for (int i = 0; i < arr.Count; i++) {
                if (!useFilter) {
                    result.Add(i);
                    continue;
                }

                string flat = arr[i].ToString(Formatting.None);
                if (flat.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                    result.Add(i);
            }

            return result;
        }

        private void DrawTableMiniToolbar(string path, JArray arr, List<int> visibleRowIndices, List<string> keys, float contentW, ref string filter) {
            var r = GUILayoutUtility.GetRect(0, 28, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(r, new Color(0.13f, 0.14f, 0.18f));
            EditorGUI.DrawRect(new Rect(r.x, r.yMax - 1, r.width, 1), ClrSep);

            float x = r.x + 8;
            GUI.Label(new Rect(x, r.y + 5, 128, 18), $"Rows: {visibleRowIndices.Count}/{arr.Count}", EditorStyles.miniLabel);
            x += 122;

            GUI.Label(new Rect(x, r.y + 5, 36, 18), "Find", EditorStyles.miniLabel);
            x += 34;

            EditorGUI.BeginChangeCheck();
            string next = EditorGUI.TextField(new Rect(x, r.y + 4, 210, 19), filter ?? "", EditorStyles.toolbarSearchField);
            if (EditorGUI.EndChangeCheck()) filter = next;
            x += 216;

            if (!string.IsNullOrEmpty(filter) && GUI.Button(new Rect(x, r.y + 4, 48, 19), "Clear", EditorStyles.miniButton)) {
                filter = "";
                GUI.FocusControl(null);
            }
            x += 54;

            using (new EditorGUI.DisabledScope(_selTablePath != path || _selRowIndex < 0)) {
                if (GUI.Button(new Rect(x, r.y + 4, 70, 19), "Move To", EditorStyles.miniButton)) {
                    int rowNow = Mathf.Max(1, _selRowIndex + 1);
                    StringInputWindow.Open("Move Row", "Move selected row to row number (1-based):", rowNow.ToString(),
                        s => MoveSelectedRowToSpecificOrder(s));
                }
                x += 74;

                if (GUI.Button(new Rect(x, r.y + 4, 48, 19), "Top", EditorStyles.miniButton)) {
                    MoveSelectedRowToIndex(0);
                }
                x += 52;

                if (GUI.Button(new Rect(x, r.y + 4, 58, 19), "Bottom", EditorStyles.miniButton)) {
                    MoveSelectedRowToIndex(arr.Count);
                }
                x += 62;
            }

            if (GUI.Button(new Rect(x, r.y + 4, 80, 19), "Copy Keys", EditorStyles.miniButton)) {
                EditorGUIUtility.systemCopyBuffer = string.Join(",", keys);
                SetStatus("📋 Column keys copied.", MessageType.Info);
            }
            x += 84;

            if (GUI.Button(new Rect(x, r.y + 4, 48, 19), "CSV", EditorStyles.miniButton)) {
                ShowCsvMenu(path, arr, visibleRowIndices, keys);
            }
            x += 52;

            if (GUI.Button(new Rect(x, r.y + 4, 54, 19), "Sort", EditorStyles.miniButton)) {
                ShowSortMenu(path, arr, visibleRowIndices, keys);
            }
            x += 58;

            if (_lastSortLabels.TryGetValue(path, out string sortLabel) && !string.IsNullOrEmpty(sortLabel)) {
                var sortStyle = new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(0.65f, 0.78f, 1f) } };
                GUI.Label(new Rect(x, r.y + 5, Mathf.Max(60, r.xMax - x - 236), 18), "↕ " + TruncateMiddle(sortLabel, 56), sortStyle);
            }

            var wS = new GUIStyle(EditorStyles.miniLabel) { alignment = TextAnchor.MiddleRight };
            GUI.Label(new Rect(r.xMax - 230, r.y + 5, 220, 18), $"Table width: {Mathf.RoundToInt(contentW)} px", wS);
        }

        // ═══════════════════════════════════════════════════════════
        //  EXCEL-LIKE TABLE
        // ═══════════════════════════════════════════════════════════
        private void DrawObjectTable(JArray arr, string path) {
            if (arr.Count == 0) {
                GUILayout.Label("  (empty)", EditorStyles.centeredGreyMiniLabel, GUILayout.Height(24));
                return;
            }

            // Collect column keys (union across all rows)
            var keys = new List<string>();
            var keySet = new HashSet<string>();
            foreach (JToken tok in arr) {
                if (tok is not JObject r) continue;
                foreach (var k in r.Properties().Select(p => p.Name))
                    if (keySet.Add(k))
                        keys.Add(k);
            }

            float[] colW = ComputeColumnWidths(path, keys, arr);
            float contentW = RowNumW + ActionsColW + colW.Sum() + keys.Count * 2 + 4;

            string filter = _tableFilters.TryGetValue(path, out var f) ? f : "";
            var visibleRows = BuildVisibleRowIndices(arr, filter);
            DrawTableMiniToolbar(path, arr, visibleRows, keys, contentW, ref filter);
            _tableFilters[path] = filter;

            if (!_tableScrolls.ContainsKey(path)) _tableScrolls[path] = Vector2.zero;
            var sv = _tableScrolls[path];

            // ── Frozen header (allocated in outer layout, drawn via GUI.BeginGroup) ──
            var hdrR = GUILayoutUtility.GetRect(0, CellH + 2, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(hdrR, ClrHeaderBg);
            EditorGUI.DrawRect(new Rect(hdrR.x, hdrR.yMax, hdrR.width, 1), ClrSep);

            // Draw header content offset by current horizontal scroll
            GUI.BeginGroup(hdrR);
            DrawFrozenHeader(sv.x, hdrR.width, keys, colW, path, arr);
            GUI.EndGroup();

            // ── Scrollable body ────────────────────────────────────
            float rowsH = visibleRows.Count * (CellH + 1);
            float bodyH = Mathf.Min(rowsH + 4, MaxTableH);
            bool needV = rowsH + 4 > bodyH;

            var bodyR = GUILayoutUtility.GetRect(0, bodyH, GUILayout.ExpandWidth(true));
            var innerR = new Rect(0, 0, Mathf.Max(contentW, bodyR.width), rowsH + 4);

            sv = GUI.BeginScrollView(bodyR, sv, innerR,
                alwaysShowHorizontal: contentW > bodyR.width,
                alwaysShowVertical: needV);

            int removeAt = -1;
            int duplicateAt = -1;

            for (int vis = 0; vis < visibleRows.Count; vis++) {
                int ri = visibleRows[vis];
                if (arr[ri] is not JObject row) continue;
                float ry = vis * (CellH + 1);
                bool isSel = _selTablePath == path && _selRowIndex == ri;

                Color bg = isSel ? ClrSelected : (ri % 2 == 0 ? ClrRowEven : ClrRowOdd);
                EditorGUI.DrawRect(new Rect(0, ry, innerR.width, CellH), bg);
                EditorGUI.DrawRect(new Rect(0, ry + CellH, innerR.width, 1), ClrSep);

                float rx = 0;

                // ─ Row number / select ─────────────────────────────
                if (GUI.Button(new Rect(rx + 1, ry + 2, RowNumW - 2, CellH - 4),
                        (ri + 1).ToString(), _rowNumStyle)) {
                    _selTablePath = path;
                    _selRowIndex = ri;
                    GUI.FocusControl(null);
                }

                rx += RowNumW;

                // ─ Drag / delete / duplicate actions ────────────────
                var dragR = new Rect(rx + 1, ry + 2, DragHandleW, CellH - 4);
                GUI.Label(dragR, "≡", _actionBtn);
                HandleRowDrag(arr, path, visibleRows, vis, ri, dragR);

                var prevC = GUI.color;
                GUI.color = new Color(1f, 0.5f, 0.5f);
                if (GUI.Button(new Rect(rx + 25, ry + 2, 22, CellH - 4), "✕", _actionBtn)) removeAt = ri;
                GUI.color = prevC;

                if (GUI.Button(new Rect(rx + 50, ry + 2, 22, CellH - 4), "⧉", _actionBtn)) duplicateAt = ri;
                rx += ActionsColW;

                // ─ Data cells ──────────────────────────────────────
                for (int ci = 0; ci < keys.Count; ci++) {
                    string key = keys[ci];
                    var cell = new Rect(rx + 1, ry + 1, colW[ci] - 2, CellH - 2);
                    JToken cur = row.TryGetValue(key, out var tv) ? tv : JValue.CreateNull();
                    int cRi = ri;
                    string cKey = key;
                    DrawTokenValueField(cur, cell, newTok => {
                        ((JObject)arr[cRi])[cKey] = newTok;
                        _selCellRowIndex = cRi;
                        _selCellKey = cKey;
                        OnTokenChanged();
                    });

                    if (Event.current.type == EventType.ContextClick && cell.Contains(Event.current.mousePosition)) {
                        _selTablePath = path;
                        _selRowIndex = ri;
                        _selCellRowIndex = ri;
                        _selCellKey = key;
                        ShowCellContextMenu(arr, visibleRows, ri, key, cur);
                        Event.current.Use();
                    }

                    rx += colW[ci] + 2;
                }

                // ─ Right-click context menu ─────────────────────────
                if (Event.current.type == EventType.ContextClick &&
                    new Rect(0, ry, innerR.width, CellH).Contains(Event.current.mousePosition)) {
                    _selTablePath = path;
                    _selRowIndex = ri;
                    ShowRowContextMenu(arr, ri, path);
                    Event.current.Use();
                }

                // ─ Left-click on row num area to select ────────────
                if (Event.current.type == EventType.MouseDown && Event.current.button == 0 &&
                    new Rect(0, ry, RowNumW, CellH).Contains(Event.current.mousePosition)) {
                    _selTablePath = path;
                    _selRowIndex = ri;
                    Repaint();
                }
            }

            DrawRowDropMarker(path, innerR.width, visibleRows.Count);
            HandleRowDragDropMouseUp(arr, path, visibleRows);

            GUI.EndScrollView();
            _tableScrolls[path] = sv;

            // Deferred mutations (after EndScrollView to avoid IMGUI layout errors/crashes)
            if (_pendingMoveTablePath == path && _pendingMoveFromIndex >= 0 && _pendingMoveTargetIndex >= 0) {
                MoveRowToIndex(arr, _pendingMoveFromIndex, _pendingMoveTargetIndex);
                _pendingMoveTablePath = "";
                _pendingMoveFromIndex = -1;
                _pendingMoveTargetIndex = -1;
                GUIUtility.ExitGUI();
            }

            if (duplicateAt >= 0) {
                var clone = (JObject)((JObject)arr[duplicateAt]).DeepClone();
                arr.Insert(duplicateAt + 1, clone);
                _selTablePath = path;
                _selRowIndex = duplicateAt + 1;
                OnTokenChanged();
                GUIUtility.ExitGUI();
            }

            if (removeAt >= 0) {
                arr.RemoveAt(removeAt);
                if (_selTablePath == path && _selRowIndex >= arr.Count) _selRowIndex = arr.Count - 1;
                OnTokenChanged();
                GUIUtility.ExitGUI();
            }
        }

        // ── Frozen header row ──────────────────────────────────────
        private void DrawFrozenHeader(float hScrollX, float visibleW, List<string> keys, float[] colW, string path, JArray arr) {
            float x = -hScrollX;

            // Row# column (always visible, not scrolled)
            EditorGUI.DrawRect(new Rect(0, 0, RowNumW, CellH), ClrHeaderBg);
            GUI.Label(new Rect(0, 0, RowNumW, CellH), "#", _rowNumStyle);

            // Actions column (always visible)
            EditorGUI.DrawRect(new Rect(RowNumW, 0, ActionsColW, CellH), ClrHeaderBg);

            x = RowNumW + ActionsColW - hScrollX;

            for (int ci = 0; ci < keys.Count; ci++) {
                float cw = colW[ci];
                float cellX = x;

                // Cull off-screen
                if (cellX + cw < RowNumW + ActionsColW || cellX > visibleW) {
                    x += cw + 2;
                    continue;
                }

                var cellR = new Rect(cellX, 0, cw, CellH);
                EditorGUI.DrawRect(cellR, ClrHeaderBg);
                EditorGUI.DrawRect(new Rect(cellX, 0, 1, CellH), ClrSep);
                GUI.Label(new Rect(cellX + 4, 1, cw - 10, CellH - 2), keys[ci], _headerCell);

                // Resize handle
                string ck = path + "." + keys[ci];
                var handleR = new Rect(cellX + cw - ResizeHandleW * 0.5f, 0, ResizeHandleW, CellH);
                bool hot = _resizingColKey == ck || handleR.Contains(Event.current.mousePosition);

                EditorGUI.DrawRect(new Rect(cellX + cw - 2, 3, 2, CellH - 6),
                    hot ? ClrResizeBar : new Color(0.35f, 0.4f, 0.55f, 0.5f));
                EditorGUIUtility.AddCursorRect(handleR, MouseCursor.ResizeHorizontal);

                if (Event.current.type == EventType.MouseDown && handleR.Contains(Event.current.mousePosition)) {
                    _resizingColKey = ck;
                    Event.current.Use();
                }

                if (Event.current.type == EventType.ContextClick && cellR.Contains(Event.current.mousePosition)) {
                    ShowColumnContextMenu(arr, keys[ci], path);
                    Event.current.Use();
                }

                x += cw + 2;
            }
        }

        // ── Row context menu ───────────────────────────────────────
        private void ShowRowContextMenu(JArray arr, int ri, string path) {
            var menu = new GenericMenu();

            menu.AddItem(new GUIContent("Copy Row\tCtrl+C"), false, () => {
                _selTablePath = path;
                _selRowIndex = ri;
                CopySelectedRow();
            });

            bool hasPaste = _clipboardRow != null;
            if (hasPaste) {
                menu.AddItem(new GUIContent("Paste Above"), false, () => {
                    arr.Insert(ri, (JObject)_clipboardRow.DeepClone());
                    _selTablePath = path;
                    _selRowIndex = ri;
                    OnTokenChanged();
                });
                menu.AddItem(new GUIContent("Paste Below\tCtrl+V"), false, () => {
                    arr.Insert(ri + 1, (JObject)_clipboardRow.DeepClone());
                    _selTablePath = path;
                    _selRowIndex = ri + 1;
                    OnTokenChanged();
                });
            }
            else {
                menu.AddDisabledItem(new GUIContent("Paste Above"));
                menu.AddDisabledItem(new GUIContent("Paste Below\tCtrl+V"));
            }

            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Duplicate Row\tCtrl+D"), false, () => {
                var clone = (JObject)((JObject)arr[ri]).DeepClone();
                arr.Insert(ri + 1, clone);
                _selTablePath = path;
                _selRowIndex = ri + 1;
                OnTokenChanged();
            });

            menu.AddItem(new GUIContent("Insert Empty Above"), false, () => {
                arr.Insert(ri, CreateEmptyRowLike(arr, ri));
                _selTablePath = path;
                _selRowIndex = ri;
                OnTokenChanged();
            });

            menu.AddItem(new GUIContent("Insert Empty Below"), false, () => {
                arr.Insert(ri + 1, CreateEmptyRowLike(arr, ri));
                _selTablePath = path;
                _selRowIndex = ri + 1;
                OnTokenChanged();
            });

            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Move Row To..."), false, () => {
                _selTablePath = path;
                _selRowIndex = ri;
                StringInputWindow.Open("Move Row", "Move to row number (1-based):", (ri + 1).ToString(),
                    s => MoveSelectedRowToSpecificOrder(s));
            });

            menu.AddItem(new GUIContent("Move To Top"), false, () => {
                _selTablePath = path;
                _selRowIndex = ri;
                MoveSelectedRowToIndex(0);
            });

            menu.AddItem(new GUIContent("Move To Bottom"), false, () => {
                _selTablePath = path;
                _selRowIndex = ri;
                MoveSelectedRowToIndex(arr.Count);
            });

            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Copy/Row JSON Pretty"), false, () => {
                EditorGUIUtility.systemCopyBuffer = arr[ri].ToString(Formatting.Indented);
                SetStatus($"📋 Row {ri + 1} copied as pretty JSON.", MessageType.Info);
            });

            menu.AddItem(new GUIContent("Copy/Row Display Name"), false, () => {
                EditorGUIUtility.systemCopyBuffer = GetRowDisplayName(arr[ri], ri);
                SetStatus("📋 Row display name copied.", MessageType.Info);
            });

            menu.AddItem(new GUIContent("Copy/Current Config Name"), false, CopyCurrentConfigName);
            menu.AddItem(new GUIContent("Copy/Current Config Path"), false, CopyCurrentConfigPath);

            menu.AddSeparator("");
            if (ri > 0)
                menu.AddItem(new GUIContent("Move Up\tCtrl+↑"), false, () => {
                    _selTablePath = path;
                    _selRowIndex = ri;
                    MoveSelectedRow(-1);
                });
            else
                menu.AddDisabledItem(new GUIContent("Move Up\tCtrl+↑"));

            if (ri < arr.Count - 1)
                menu.AddItem(new GUIContent("Move Down\tCtrl+↓"), false, () => {
                    _selTablePath = path;
                    _selRowIndex = ri;
                    MoveSelectedRow(1);
                });
            else
                menu.AddDisabledItem(new GUIContent("Move Down\tCtrl+↓"));

            menu.AddSeparator("");
            menu.AddItem(new GUIContent("Delete Row\tDel"), false, () => {
                _selTablePath = path;
                _selRowIndex = ri;
                DeleteSelectedRow();
            });

            menu.ShowAsContext();
        }


        // ── Column context menu ────────────────────────────────────
        private void ShowColumnContextMenu(JArray arr, string key, string path) {
            var menu = new GenericMenu();

            menu.AddItem(new GUIContent($"Copy Column JSON/{key}"), false, () => {
                var values = new JArray();
                foreach (var tok in arr)
                    values.Add(tok is JObject row && row.TryGetValue(key, out var v) ? v.DeepClone() : JValue.CreateNull());
                EditorGUIUtility.systemCopyBuffer = values.ToString(Formatting.None);
                SetStatus($"📋 Column '{key}' copied as JSON array.", MessageType.Info);
            });

            menu.AddItem(new GUIContent($"Copy Column Text Lines/{key}"), false, () => {
                var lines = arr.Select(tok =>
                    tok is JObject row && row.TryGetValue(key, out var v) ? ScalarToDisplayString(v) : "");
                EditorGUIUtility.systemCopyBuffer = string.Join("\n", lines);
                SetStatus($"📋 Column '{key}' copied as text lines.", MessageType.Info);
            });

            menu.AddItem(new GUIContent($"Copy Column Key Name/{key}"), false, () => {
                EditorGUIUtility.systemCopyBuffer = key;
                SetStatus($"📋 Key '{key}' copied.", MessageType.Info);
            });

            menu.AddSeparator("");

            menu.AddItem(new GUIContent($"Sort/{key}/Auto Ascending"), false, () => SortRowsByColumn(path, arr, key, true, SortValueMode.Auto));
            menu.AddItem(new GUIContent($"Sort/{key}/Auto Descending"), false, () => SortRowsByColumn(path, arr, key, false, SortValueMode.Auto));
            menu.AddItem(new GUIContent($"Sort/{key}/Number Ascending"), false, () => SortRowsByColumn(path, arr, key, true, SortValueMode.Number));
            menu.AddItem(new GUIContent($"Sort/{key}/Number Descending"), false, () => SortRowsByColumn(path, arr, key, false, SortValueMode.Number));
            menu.AddItem(new GUIContent($"Sort/{key}/Text A → Z"), false, () => SortRowsByColumn(path, arr, key, true, SortValueMode.Text));
            menu.AddItem(new GUIContent($"Sort/{key}/Text Z → A"), false, () => SortRowsByColumn(path, arr, key, false, SortValueMode.Text));
            menu.AddItem(new GUIContent($"Sort/{key}/Date Old → New"), false, () => SortRowsByColumn(path, arr, key, true, SortValueMode.Date));
            menu.AddItem(new GUIContent($"Sort/{key}/Date New → Old"), false, () => SortRowsByColumn(path, arr, key, false, SortValueMode.Date));

            menu.AddSeparator("");

            menu.AddItem(new GUIContent($"Rename Column/{key}"), false, () => {
                StringInputWindow.Open("Rename Field", "New field name:", key, newName => RenameColumn(arr, key, newName));
            });

            menu.AddItem(new GUIContent($"Move Column Left/{key}"), false, () => MoveColumn(arr, key, -1));
            menu.AddItem(new GUIContent($"Move Column Right/{key}"), false, () => MoveColumn(arr, key, 1));

            menu.AddSeparator("");

            menu.AddItem(new GUIContent($"Set All Values.../{key}"), false, () => {
                StringInputWindow.Open("Set Column Values", $"Set all values for '{key}' to:", "",
                    value => SetColumnAllValues(arr, key, value));
            });

            menu.AddItem(new GUIContent($"Fill Empty Values.../{key}"), false, () => {
                StringInputWindow.Open("Fill Empty Values", $"Fill empty/null values in '{key}' with:", "",
                    value => FillEmptyColumnValues(arr, key, value));
            });

            menu.AddSeparator("");

            menu.AddItem(new GUIContent($"Delete Column/{key}"), false, () => {
                if (!EditorUtility.DisplayDialog("Delete Column",
                        $"Remove field '{key}' from all rows in this table?", "Delete", "Cancel")) return;

                foreach (var tok in arr)
                    if (tok is JObject row)
                        row.Remove(key);

                OnTokenChanged();
            });

            menu.AddSeparator("");
            menu.AddDisabledItem(new GUIContent("Tip: drag ≡ handle to reorder rows"));
            menu.ShowAsContext();
        }

        private void AddColumnToRows(JArray arr, string columnName) {
            columnName = (columnName ?? "").Trim();
            if (string.IsNullOrEmpty(columnName)) return;

            bool exists = arr.OfType<JObject>().Any(o => o.Property(columnName) != null);
            if (exists && !EditorUtility.DisplayDialog("Column Exists",
                    $"Field '{columnName}' already exists in at least one row. Add missing cells only?", "Add Missing", "Cancel")) return;

            foreach (var tok in arr)
                if (tok is JObject row && row.Property(columnName) == null)
                    row[columnName] = "";

            OnTokenChanged();
        }

        // ── CSV import / export ─────────────────────────────────
        private void ShowCsvMenu(string path, JArray arr, List<int> visibleRowIndices, List<string> keys) {
            var menu = new GenericMenu();
            var visible = visibleRowIndices ?? Enumerable.Range(0, arr.Count).ToList();
            var all = Enumerable.Range(0, arr.Count).ToList();
            bool hasVisibleFilter = visible.Count != arr.Count;

            menu.AddItem(new GUIContent("Export CSV/All Rows..."), false,
                () => ExportTableToCsvFile(path, arr, all, keys, false));

            if (hasVisibleFilter)
                menu.AddItem(new GUIContent("Export CSV/Visible Filtered Rows..."), false,
                    () => ExportTableToCsvFile(path, arr, visible, keys, true));
            else
                menu.AddDisabledItem(new GUIContent("Export CSV/Visible Filtered Rows..."));

            menu.AddItem(new GUIContent("Copy CSV To Clipboard/All Rows"), false,
                () => CopyTableCsvToClipboard(arr, all, keys, false));

            if (hasVisibleFilter)
                menu.AddItem(new GUIContent("Copy CSV To Clipboard/Visible Filtered Rows"), false,
                    () => CopyTableCsvToClipboard(arr, visible, keys, true));
            else
                menu.AddDisabledItem(new GUIContent("Copy CSV To Clipboard/Visible Filtered Rows"));

            menu.AddSeparator("");

            menu.AddItem(new GUIContent("Import CSV From File/Replace Table..."), false,
                () => ImportCsvFileToTable(arr, path, CsvImportMode.Replace));
            menu.AddItem(new GUIContent("Import CSV From File/Append Rows..."), false,
                () => ImportCsvFileToTable(arr, path, CsvImportMode.Append));
            menu.AddItem(new GUIContent("Import CSV From File/Update By Key..."), false,
                () => AskCsvUpdateKeyAndImport(arr, path));

            menu.AddSeparator("");

            menu.AddItem(new GUIContent("Paste CSV From Clipboard/Replace Table"), false,
                () => ImportCsvTextToTable(arr, EditorGUIUtility.systemCopyBuffer, CsvImportMode.Replace, null, "Clipboard"));
            menu.AddItem(new GUIContent("Paste CSV From Clipboard/Append Rows"), false,
                () => ImportCsvTextToTable(arr, EditorGUIUtility.systemCopyBuffer, CsvImportMode.Append, null, "Clipboard"));
            menu.AddItem(new GUIContent("Paste CSV From Clipboard/Update By Key..."), false,
                () => AskCsvUpdateKeyAndImportFromClipboard(arr));

            menu.AddSeparator("");
            menu.AddDisabledItem(new GUIContent("CSV supports Excel / Google Sheets."));
            menu.AddDisabledItem(new GUIContent("Nested object/array cells are stored as compact JSON."));
            menu.ShowAsContext();
        }

        private void ExportTableToCsvFile(string path, JArray arr, List<int> rowIndices, List<string> keys, bool visibleOnly) {
            string configName = CurrentFile?.DisplayName ?? "config";
            string suffix = visibleOnly ? "visible" : "all";
            string defaultName = SanitizeFileName($"{configName}_{PathToSafeName(path)}_{suffix}.csv");
            string savePath = EditorUtility.SaveFilePanel("Export table to CSV", Application.dataPath, defaultName, "csv");
            if (string.IsNullOrEmpty(savePath)) return;

            try {
                string csv = BuildCsv(arr, rowIndices, keys);
                File.WriteAllText(savePath, csv, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
                SetStatus($"✅ Exported CSV: {Path.GetFileName(savePath)} ({rowIndices.Count} row(s)).", MessageType.Info);
                EditorUtility.RevealInFinder(savePath);
            }
            catch (Exception e) {
                SetStatus($"❌ CSV export failed: {e.Message}", MessageType.Error);
            }
        }

        private void CopyTableCsvToClipboard(JArray arr, List<int> rowIndices, List<string> keys, bool visibleOnly) {
            try {
                EditorGUIUtility.systemCopyBuffer = BuildCsv(arr, rowIndices, keys);
                SetStatus(visibleOnly
                    ? $"📋 Visible CSV copied ({rowIndices.Count} row(s))."
                    : $"📋 CSV copied ({rowIndices.Count} row(s)).", MessageType.Info);
            }
            catch (Exception e) {
                SetStatus($"❌ CSV copy failed: {e.Message}", MessageType.Error);
            }
        }

        private void ImportCsvFileToTable(JArray arr, string path, CsvImportMode mode, string updateKey = null) {
            string filePath = EditorUtility.OpenFilePanel("Import CSV", Application.dataPath, "csv");
            if (string.IsNullOrEmpty(filePath)) return;

            try {
                string csv = ReadTextBestEffort(filePath);
                ImportCsvTextToTable(arr, csv, mode, updateKey, Path.GetFileName(filePath));
            }
            catch (Exception e) {
                SetStatus($"❌ CSV import failed: {e.Message}", MessageType.Error);
            }
        }

        private void AskCsvUpdateKeyAndImport(JArray arr, string path) {
            string guess = GuessBestKeyName(arr);
            StringInputWindow.Open("CSV Update By Key", "Key column name, e.g. id / name / productId:", guess,
                key => ImportCsvFileToTable(arr, path, CsvImportMode.UpdateByKey, key));
        }

        private void AskCsvUpdateKeyAndImportFromClipboard(JArray arr) {
            string guess = GuessBestKeyName(arr);
            StringInputWindow.Open("CSV Update By Key", "Key column name, e.g. id / name / productId:", guess,
                key => ImportCsvTextToTable(arr, EditorGUIUtility.systemCopyBuffer, CsvImportMode.UpdateByKey, key, "Clipboard"));
        }

        private void ImportCsvTextToTable(JArray arr, string csvText, CsvImportMode mode, string updateKey, string sourceName) {
            if (string.IsNullOrWhiteSpace(csvText)) {
                SetStatus("❌ CSV is empty.", MessageType.Error);
                return;
            }

            CsvParseResult parsed;
            try { parsed = ParseCsvSmart(csvText); }
            catch (Exception e) {
                SetStatus($"❌ CSV parse failed: {e.Message}", MessageType.Error);
                return;
            }

            var table = parsed.Rows;
            if (table.Count == 0 || table[0].Count == 0) {
                SetStatus("❌ CSV has no header row.", MessageType.Error);
                return;
            }

            var headerMap = BuildCsvHeaderMap(table[0]);
            if (headerMap.Count == 0) {
                SetStatus("❌ CSV header row is empty.", MessageType.Error);
                return;
            }

            var headers = headerMap.Select(h => h.Name).ToList();
            var samples = BuildColumnSamples(arr, headers);
            var importedRows = new List<JObject>();

            for (int r = 1; r < table.Count; r++) {
                var cells = table[r];
                if (cells == null || cells.All(string.IsNullOrWhiteSpace)) continue;

                var obj = new JObject();
                foreach (var header in headerMap) {
                    string cell = header.SourceIndex < cells.Count ? cells[header.SourceIndex] : "";
                    samples.TryGetValue(header.Name, out var sample);
                    obj[header.Name] = CsvCellToToken(cell, sample);
                }
                importedRows.Add(obj);
            }

            if (importedRows.Count == 0) {
                SetStatus($"❌ CSV has no data rows. Detected delimiter: {DelimiterLabel(parsed.Delimiter)}.", MessageType.Error);
                return;
            }

            if (mode == CsvImportMode.Replace) {
                if (!EditorUtility.DisplayDialog("Replace Table From CSV",
                        $"Replace this table with {importedRows.Count} row(s) and {headers.Count} column(s) from {sourceName}?\n\nDetected delimiter: {DelimiterLabel(parsed.Delimiter)}", "Replace", "Cancel")) return;

                arr.Clear();
                foreach (var row in importedRows) arr.Add(row);
                _selTablePath = "";
                _selRowIndex = -1;
                _columnWidths.Clear();
                OnTokenChanged();
                SetStatus($"✅ CSV replaced table: {importedRows.Count} row(s), {headers.Count} column(s), delimiter {DelimiterLabel(parsed.Delimiter)}.", MessageType.Info);
                GUIUtility.ExitGUI();
                return;
            }

            if (mode == CsvImportMode.Append) {
                foreach (var row in importedRows) arr.Add(row);
                _selRowIndex = arr.Count - 1;
                _columnWidths.Clear();
                OnTokenChanged();
                SetStatus($"✅ CSV appended {importedRows.Count} row(s), {headers.Count} column(s), delimiter {DelimiterLabel(parsed.Delimiter)}.", MessageType.Info);
                GUIUtility.ExitGUI();
                return;
            }

            updateKey = (updateKey ?? "").Trim();
            if (string.IsNullOrEmpty(updateKey)) {
                SetStatus("❌ Update key is empty.", MessageType.Error);
                return;
            }
            if (!headers.Contains(updateKey)) {
                SetStatus($"❌ CSV does not contain key column '{updateKey}'.", MessageType.Error);
                return;
            }

            int updated = 0, added = 0, skipped = 0;
            var existingByKey = new Dictionary<string, JObject>(StringComparer.OrdinalIgnoreCase);
            foreach (var row in arr.OfType<JObject>()) {
                if (!row.TryGetValue(updateKey, out var v)) continue;
                string k = ScalarToDisplayString(v);
                if (!string.IsNullOrWhiteSpace(k) && !existingByKey.ContainsKey(k))
                    existingByKey.Add(k, row);
            }

            foreach (var newRow in importedRows) {
                string keyValue = newRow.TryGetValue(updateKey, out var keyTok) ? ScalarToDisplayString(keyTok) : "";
                if (string.IsNullOrWhiteSpace(keyValue)) { skipped++; continue; }

                if (existingByKey.TryGetValue(keyValue, out var oldRow)) {
                    foreach (var prop in newRow.Properties())
                        oldRow[prop.Name] = prop.Value.DeepClone();
                    updated++;
                }
                else {
                    arr.Add(newRow);
                    existingByKey[keyValue] = newRow;
                    added++;
                }
            }

            _columnWidths.Clear();
            OnTokenChanged();
            SetStatus($"✅ CSV update by '{updateKey}': {updated} updated, {added} added, {skipped} skipped. Delimiter {DelimiterLabel(parsed.Delimiter)}.", MessageType.Info);
            GUIUtility.ExitGUI();
        }

        private static string BuildCsv(JArray arr, List<int> rowIndices, List<string> keys) {
            if (arr == null) return "";
            keys = keys != null && keys.Count > 0 ? keys : CollectObjectKeys(arr);
            rowIndices ??= Enumerable.Range(0, arr.Count).ToList();

            var sb = new StringBuilder();
            sb.AppendLine(string.Join(",", keys.Select(CsvEscape)));

            foreach (int idx in rowIndices) {
                if (idx < 0 || idx >= arr.Count) continue;
                JObject row = arr[idx] as JObject;
                var cells = new List<string>();
                foreach (string key in keys) {
                    JToken v = row != null && row.TryGetValue(key, out var found) ? found : null;
                    cells.Add(CsvEscape(TokenToCsvCell(v)));
                }
                sb.AppendLine(string.Join(",", cells));
            }

            return sb.ToString();
        }

        private static List<string> CollectObjectKeys(JArray arr) {
            var keys = new List<string>();
            var set = new HashSet<string>();
            foreach (var row in arr.OfType<JObject>()) {
                foreach (var p in row.Properties())
                    if (set.Add(p.Name)) keys.Add(p.Name);
            }
            return keys;
        }

        private static string TokenToCsvCell(JToken token) {
            if (token == null || token.Type == JTokenType.Null) return "";
            if (token.Type == JTokenType.String) return token.Value<string>() ?? "";
            if (token.Type == JTokenType.Object || token.Type == JTokenType.Array)
                return token.ToString(Formatting.None);
            return token.ToString(Formatting.None);
        }

        private static string CsvEscape(string value) {
            value ??= "";
            bool mustQuote = value.Contains(',') || value.Contains('"') || value.Contains('\n') || value.Contains('\r');
            if (!mustQuote) return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }

        private class CsvParseResult {
            public List<List<string>> Rows = new();
            public char Delimiter = ',';
        }

        private struct CsvHeaderInfo {
            public int SourceIndex;
            public string Name;
        }

        private static CsvParseResult ParseCsvSmart(string text) {
            text = NormalizeCsvText(text);
            var explicitDelimiter = TryReadSepDirective(ref text);
            if (explicitDelimiter.HasValue) {
                return new CsvParseResult {
                    Rows = ParseDelimitedText(text, explicitDelimiter.Value),
                    Delimiter = explicitDelimiter.Value
                };
            }

            char bestDelimiter = DetectBestDelimiter(text);
            return new CsvParseResult {
                Rows = ParseDelimitedText(text, bestDelimiter),
                Delimiter = bestDelimiter
            };
        }

        // Backward-compatible wrapper for old calls.
        private static List<List<string>> ParseCsv(string text) => ParseCsvSmart(text).Rows;

        private static string NormalizeCsvText(string text) {
            if (string.IsNullOrEmpty(text)) return "";
            text = text.Replace("\uFEFF", "");
            // Excel/Google Sheets clipboard sometimes uses CR-only or mixed newlines.
            text = text.Replace("\r\n", "\n").Replace('\r', '\n');
            return text;
        }

        private static char? TryReadSepDirective(ref string text) {
            if (string.IsNullOrEmpty(text)) return null;
            int firstLineEnd = text.IndexOf('\n');
            string firstLine = firstLineEnd >= 0 ? text.Substring(0, firstLineEnd) : text;
            firstLine = firstLine.Trim();

            if (!firstLine.StartsWith("sep=", StringComparison.OrdinalIgnoreCase)) return null;

            string sep = firstLine.Substring(4);
            char delimiter = sep.Equals("\\t", StringComparison.OrdinalIgnoreCase) || sep.Equals("tab", StringComparison.OrdinalIgnoreCase)
                ? '\t'
                : sep.Length > 0 ? sep[0] : ',';

            text = firstLineEnd >= 0 ? text.Substring(firstLineEnd + 1) : "";
            return delimiter;
        }

        private static char DetectBestDelimiter(string text) {
            char[] candidates = { ',', ';', '\t', '|' };
            char best = ',';
            int bestScore = int.MinValue;

            foreach (char delimiter in candidates) {
                var rows = ParseDelimitedText(text, delimiter, maxRows: 20)
                    .Where(r => r.Any(c => !string.IsNullOrWhiteSpace(c)))
                    .ToList();

                if (rows.Count == 0) continue;

                var counts = rows.Select(r => r.Count).ToList();
                int maxCols = counts.Max();
                int rowsWithMultipleCols = counts.Count(c => c > 1);
                int commonCols = counts
                    .GroupBy(c => c)
                    .OrderByDescending(g => g.Count())
                    .ThenByDescending(g => g.Key)
                    .First().Key;
                int consistentRows = counts.Count(c => c == commonCols);

                // Prefer delimiters that produce multiple consistent columns.
                int score = rowsWithMultipleCols * 1000 + consistentRows * 100 + maxCols * 10;

                // Slightly prefer TAB for clipboard TSV when it clearly creates columns.
                if (delimiter == '\t' && rowsWithMultipleCols > 0) score += 25;

                if (score > bestScore) {
                    bestScore = score;
                    best = delimiter;
                }
            }

            return best;
        }

        private static List<List<string>> ParseDelimitedText(string text, char delimiter, int maxRows = int.MaxValue) {
            var rows = new List<List<string>>();
            var row = new List<string>();
            var cell = new StringBuilder();
            bool inQuotes = false;
            bool atCellStart = true;

            text = NormalizeCsvText(text);

            for (int i = 0; i < text.Length; i++) {
                char ch = text[i];

                if (inQuotes) {
                    if (ch == '"') {
                        if (i + 1 < text.Length && text[i + 1] == '"') {
                            cell.Append('"');
                            i++;
                        }
                        else {
                            inQuotes = false;
                        }
                    }
                    else {
                        cell.Append(ch);
                    }
                    continue;
                }

                if (ch == '"' && atCellStart) {
                    inQuotes = true;
                    atCellStart = false;
                }
                else if (ch == delimiter) {
                    row.Add(cell.ToString());
                    cell.Length = 0;
                    atCellStart = true;
                }
                else if (ch == '\n') {
                    row.Add(cell.ToString());
                    cell.Length = 0;
                    rows.Add(row);
                    if (rows.Count >= maxRows) return rows;
                    row = new List<string>();
                    atCellStart = true;
                }
                else {
                    cell.Append(ch);
                    atCellStart = false;
                }
            }

            row.Add(cell.ToString());
            if (row.Count > 1 || row.Any(v => !string.IsNullOrEmpty(v))) rows.Add(row);
            return rows;
        }

        private static List<CsvHeaderInfo> BuildCsvHeaderMap(List<string> rawHeaders) {
            var result = new List<CsvHeaderInfo>();
            var used = new Dictionary<string, int>(StringComparer.Ordinal);

            for (int i = 0; i < rawHeaders.Count; i++) {
                string header = NormalizeCsvHeader(rawHeaders[i]);
                if (string.IsNullOrEmpty(header)) continue;

                string unique = header;
                if (used.TryGetValue(header, out int count)) {
                    count++;
                    used[header] = count;
                    unique = $"{header}_{count}";
                }
                else {
                    used[header] = 1;
                }

                result.Add(new CsvHeaderInfo { SourceIndex = i, Name = unique });
            }

            return result;
        }

        private static string NormalizeCsvHeader(string header) {
            header = (header ?? "").Trim().TrimStart('\ufeff');
            if (header.StartsWith("\"") && header.EndsWith("\"") && header.Length >= 2)
                header = header.Substring(1, header.Length - 2).Replace("\"\"", "\"");
            return header.Trim();
        }

        private static string DelimiterLabel(char delimiter) {
            return delimiter switch
            {
                ',' => "comma (,)",
                ';' => "semicolon (;)",
                '\t' => "tab",
                '|' => "pipe (|)",
                _ => delimiter.ToString()
            };
        }

        private static string ReadTextBestEffort(string filePath) {
            byte[] bytes = File.ReadAllBytes(filePath);

            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                return Encoding.UTF8.GetString(bytes, 3, bytes.Length - 3);

            if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
                return Encoding.Unicode.GetString(bytes, 2, bytes.Length - 2);

            if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
                return Encoding.BigEndianUnicode.GetString(bytes, 2, bytes.Length - 2);

            try {
                return new UTF8Encoding(false, true).GetString(bytes);
            }
            catch {
                return Encoding.Default.GetString(bytes);
            }
        }

        private static Dictionary<string, JToken> BuildColumnSamples(JArray arr, List<string> headers) {
            var result = new Dictionary<string, JToken>(StringComparer.Ordinal);
            foreach (string h in headers) {
                foreach (var row in arr.OfType<JObject>()) {
                    if (row.TryGetValue(h, out var sample) && sample != null && sample.Type != JTokenType.Null) {
                        result[h] = sample;
                        break;
                    }
                }
            }
            return result;
        }

        private static JToken CsvCellToToken(string cell, JToken sample) {
            cell ??= "";
            string trim = cell.Trim();

            if ((trim.StartsWith("{") && trim.EndsWith("}")) || (trim.StartsWith("[") && trim.EndsWith("]"))) {
                try { return JToken.Parse(trim); }
                catch { /* keep as scalar */ }
            }

            if (sample != null) return SmartParseScalar(cell, sample);
            return SmartParseScalar(cell, null);
        }

        private static string GuessBestKeyName(JArray arr) {
            var keys = CollectObjectKeys(arr);
            foreach (string k in new[] { "id", "ID", "Id", "key", "Key", "name", "Name", "configName", "ConfigName", "productId", "ProductId" })
                if (keys.Contains(k)) return k;
            return keys.FirstOrDefault() ?? "id";
        }

        private static string PathToSafeName(string path) {
            return (path ?? "table").Replace("root.", "").Replace('.', '_').Replace('/', '_').Replace('\\', '_');
        }

        private static string SanitizeFileName(string name) {
            foreach (char c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '_');
            return string.IsNullOrWhiteSpace(name) ? "export.csv" : name;
        }

        // ── Visual editing extras ─────────────────────────────────
        private void HandleRowDrag(JArray arr, string path, List<int> visibleRows, int visualIndex, int realIndex, Rect dragR) {
            EditorGUIUtility.AddCursorRect(dragR, MouseCursor.Pan);

            var e = Event.current;
            if (e.type == EventType.MouseDown && e.button == 0 && dragR.Contains(e.mousePosition)) {
                _dragTablePath = path;
                _dragRowIndex = realIndex;
                _dragDropVisualIndex = visualIndex;
                _dragMouseStart = e.mousePosition;
                _dragLabel = GetRowDisplayName(arr[realIndex], realIndex);
                _isDraggingRow = false;
                GUI.FocusControl(null);
                e.Use();
                return;
            }

            if (_dragTablePath != path || _dragRowIndex != realIndex) return;

            if (e.type == EventType.MouseDrag) {
                if (!_isDraggingRow && Vector2.Distance(_dragMouseStart, e.mousePosition) >= DragStartDistance)
                    _isDraggingRow = true;

                if (_isDraggingRow) {
                    _dragDropVisualIndex = Mathf.Clamp(Mathf.RoundToInt(e.mousePosition.y / (CellH + 1)), 0, visibleRows.Count);
                    Repaint();
                    e.Use();
                }
            }
        }

        private void DrawRowDropMarker(string path, float width, int visibleRowCount) {
            if (_dragTablePath != path || !_isDraggingRow || _dragDropVisualIndex < 0) return;

            float y = Mathf.Clamp(_dragDropVisualIndex, 0, visibleRowCount) * (CellH + 1);
            EditorGUI.DrawRect(new Rect(0, y - 2, width, 3), new Color(0.45f, 0.72f, 1f, 1f));
            GUI.Label(new Rect(8, Mathf.Max(0, y - 22), Mathf.Min(width - 16, 360), 20),
                $"Drop: {_dragLabel}", new GUIStyle(EditorStyles.miniLabel) { normal = { textColor = new Color(0.75f, 0.9f, 1f) } });
        }

        private void HandleRowDragDropMouseUp(JArray arr, string path, List<int> visibleRows) {
            var e = Event.current;
            if (_dragTablePath != path || e.type != EventType.MouseUp) return;

            if (_isDraggingRow && _dragRowIndex >= 0 && _dragRowIndex < arr.Count) {
                int insertVisual = Mathf.Clamp(_dragDropVisualIndex, 0, visibleRows.Count);
                int targetIndex = insertVisual >= visibleRows.Count ? arr.Count : visibleRows[insertVisual];

                // IMPORTANT: only queue the move here.
                // Applying arr.RemoveAt/Insert before GUI.EndScrollView can break Unity IMGUI layout.
                _pendingMoveTablePath = path;
                _pendingMoveFromIndex = _dragRowIndex;
                _pendingMoveTargetIndex = targetIndex;
            }

            _dragTablePath = "";
            _dragRowIndex = -1;
            _dragDropVisualIndex = -1;
            _isDraggingRow = false;
            _dragLabel = "";
            e.Use();
        }

        private void MoveRowToIndex(JArray arr, int fromIndex, int targetIndex) {
            if (arr == null || fromIndex < 0 || fromIndex >= arr.Count) return;

            targetIndex = Mathf.Clamp(targetIndex, 0, arr.Count);
            int insertIndex = targetIndex;
            if (insertIndex > fromIndex) insertIndex--;

            if (insertIndex == fromIndex) return;

            var token = arr[fromIndex];
            arr.RemoveAt(fromIndex);
            insertIndex = Mathf.Clamp(insertIndex, 0, arr.Count);
            arr.Insert(insertIndex, token);

            _selRowIndex = insertIndex;
            OnTokenChanged();
            SetStatus($"↕ Moved row {fromIndex + 1} to {insertIndex + 1}.", MessageType.Info);
        }

        private void MoveSelectedRowToSpecificOrder(string input) {
            if (!int.TryParse((input ?? "").Trim(), out int oneBased)) {
                SetStatus("❌ Invalid row number.", MessageType.Error);
                return;
            }

            MoveSelectedRowToIndex(oneBased - 1);
        }

        private void MoveSelectedRowToIndex(int targetZeroBased) {
            var arr = GetSelectedArray();
            if (arr == null || _selRowIndex < 0) return;
            MoveRowToIndex(arr, _selRowIndex, Mathf.Clamp(targetZeroBased, 0, arr.Count));
        }

        private static JObject CreateEmptyRowLike(JArray arr, int sampleIndex) {
            if (arr == null || arr.Count == 0) return new JObject();
            sampleIndex = Mathf.Clamp(sampleIndex, 0, arr.Count - 1);
            if (arr[sampleIndex] is not JObject sample) return new JObject();

            var obj = new JObject();
            foreach (var p in sample.Properties())
                obj[p.Name] = CreateDefaultValueForToken(p.Value);
            return obj;
        }

        private static string GetRowDisplayName(JToken rowToken, int index) {
            if (rowToken is JObject row) {
                foreach (var key in new[] { "id", "ID", "Id", "name", "Name", "key", "Key", "configName", "ConfigName", "productId", "ProductId" }) {
                    if (row.TryGetValue(key, out var v) && v != null && v.Type != JTokenType.Null) {
                        string s = ScalarToDisplayString(v);
                        if (!string.IsNullOrWhiteSpace(s))
                            return $"#{index + 1}  {key}: {s}";
                    }
                }
            }

            return $"Row {index + 1}";
        }

        private void ShowCellContextMenu(JArray arr, List<int> visibleRows, int ri, string key, JToken value) {
            var menu = new GenericMenu();

            menu.AddItem(new GUIContent("Copy Cell/JSON"), false, () => {
                EditorGUIUtility.systemCopyBuffer = value?.ToString(Formatting.None) ?? "null";
                SetStatus("📋 Cell JSON copied.", MessageType.Info);
            });

            menu.AddItem(new GUIContent("Copy Cell/Raw Text"), false, () => {
                EditorGUIUtility.systemCopyBuffer = ScalarToDisplayString(value);
                SetStatus("📋 Cell text copied.", MessageType.Info);
            });

            menu.AddItem(new GUIContent("Paste Cell"), false, () => {
                if (arr[ri] is JObject row) {
                    row[key] = SmartParseScalar(EditorGUIUtility.systemCopyBuffer, value);
                    OnTokenChanged();
                }
            });

            menu.AddSeparator("");

            menu.AddItem(new GUIContent("Edit Cell As JSON..."), false, () => {
                JsonTokenEditWindow.Open(value ?? JValue.CreateNull(), edited => {
                    if (arr[ri] is JObject row) {
                        row[key] = edited;
                        OnTokenChanged();
                    }
                });
            });

            menu.AddItem(new GUIContent("Set Cell/null"), false, () => {
                if (arr[ri] is JObject row) { row[key] = JValue.CreateNull(); OnTokenChanged(); }
            });
            menu.AddItem(new GUIContent("Set Cell/true"), false, () => {
                if (arr[ri] is JObject row) { row[key] = true; OnTokenChanged(); }
            });
            menu.AddItem(new GUIContent("Set Cell/false"), false, () => {
                if (arr[ri] is JObject row) { row[key] = false; OnTokenChanged(); }
            });

            menu.AddSeparator("");

            menu.AddItem(new GUIContent("Fill Down From This Cell"), false, () => {
                bool start = false;
                foreach (int idx in visibleRows) {
                    if (idx == ri) { start = true; continue; }
                    if (!start) continue;
                    if (arr[idx] is JObject row) row[key] = value?.DeepClone() ?? JValue.CreateNull();
                }
                OnTokenChanged();
            });

            menu.AddItem(new GUIContent("Fill Empty In This Column"), false, () => {
                foreach (int idx in visibleRows) {
                    if (arr[idx] is not JObject row) continue;
                    if (!row.TryGetValue(key, out var cur) || cur == null || cur.Type == JTokenType.Null ||
                        (cur.Type == JTokenType.String && string.IsNullOrWhiteSpace(cur.Value<string>())))
                        row[key] = value?.DeepClone() ?? JValue.CreateNull();
                }
                OnTokenChanged();
            });

            menu.AddItem(new GUIContent("Copy/Current Config Name"), false, CopyCurrentConfigName);
            menu.ShowAsContext();
        }

        private void ShowSortMenu(string path, JArray arr, List<int> visibleRowIndices, List<string> keys) {
            var menu = new GenericMenu();

            if (keys == null || keys.Count == 0) {
                menu.AddDisabledItem(new GUIContent("No fields found"));
                menu.ShowAsContext();
                return;
            }

            // Common game-config fields appear first for faster access.
            string[] preferred = { "id", "order", "sortOrder", "index", "level", "name", "type", "price", "value", "amount", "unlockLevel", "productId" };
            var quickKeys = preferred.Where(k => keys.Contains(k)).Concat(keys.Where(k => !preferred.Contains(k))).ToList();

            foreach (string k in quickKeys) {
                string key = k;
                menu.AddItem(new GUIContent($"Quick Sort All Rows/{key}/Auto Ascending"), false,
                    () => SortRowsByColumn(path, arr, key, true, SortValueMode.Auto));
                menu.AddItem(new GUIContent($"Quick Sort All Rows/{key}/Auto Descending"), false,
                    () => SortRowsByColumn(path, arr, key, false, SortValueMode.Auto));
            }

            menu.AddSeparator("");

            bool hasFilteredRows = visibleRowIndices != null && visibleRowIndices.Count > 0 && visibleRowIndices.Count < arr.Count;
            if (hasFilteredRows) {
                foreach (string k in quickKeys) {
                    string key = k;
                    menu.AddItem(new GUIContent($"Sort Visible Filtered Rows Only/{key}/Auto Ascending"), false,
                        () => SortRowsByColumn(path, arr, key, true, SortValueMode.Auto, visibleRowIndices));
                    menu.AddItem(new GUIContent($"Sort Visible Filtered Rows Only/{key}/Auto Descending"), false,
                        () => SortRowsByColumn(path, arr, key, false, SortValueMode.Auto, visibleRowIndices));
                }
            }
            else {
                menu.AddDisabledItem(new GUIContent("Sort Visible Filtered Rows Only/No filter is active"));
            }

            menu.AddSeparator("");

            menu.AddItem(new GUIContent("Advanced Multi-field Sort..."), false, () => {
                var initialRules = _lastSortRules.TryGetValue(path, out var saved) && saved.Count > 0
                    ? saved.Select(r => r.Clone()).ToList()
                    : new List<SortRule> { new SortRule { Key = keys[0], Ascending = true, Mode = SortValueMode.Auto, EmptyLast = true } };

                MultiFieldSortWindow.Open(keys, initialRules, hasFilteredRows,
                    rules => SortRowsByFields(path, arr, rules, null),
                    rules => SortRowsByFields(path, arr, rules, visibleRowIndices));
            });

            menu.AddItem(new GUIContent("Sort By Expression/All Rows..."), false, () => {
                string initial = _lastSortRules.TryGetValue(path, out var saved) && saved.Count > 0
                    ? BuildSortExpression(saved)
                    : GuessSortExpression(keys);
                StringInputWindow.Open("Sort By Fields", "Example: id asc, level desc, price number asc", initial,
                    expr => ApplySortExpression(path, arr, visibleRowIndices, expr, false));
            });

            if (hasFilteredRows) {
                menu.AddItem(new GUIContent("Sort By Expression/Visible Filtered Rows Only..."), false, () => {
                    string initial = _lastSortRules.TryGetValue(path, out var saved) && saved.Count > 0
                        ? BuildSortExpression(saved)
                        : GuessSortExpression(keys);
                    StringInputWindow.Open("Sort Visible Rows", "Example: rarity asc, level number desc", initial,
                        expr => ApplySortExpression(path, arr, visibleRowIndices, expr, true));
                });
            }
            else {
                menu.AddDisabledItem(new GUIContent("Sort By Expression/Visible Filtered Rows Only..."));
            }

            menu.AddSeparator("");

            if (_lastSortRules.TryGetValue(path, out var lastRules) && lastRules.Count > 0) {
                menu.AddItem(new GUIContent("Re-apply Last Sort/All Rows"), false,
                    () => SortRowsByFields(path, arr, lastRules.Select(r => r.Clone()).ToList(), null));
                if (hasFilteredRows)
                    menu.AddItem(new GUIContent("Re-apply Last Sort/Visible Filtered Rows Only"), false,
                        () => SortRowsByFields(path, arr, lastRules.Select(r => r.Clone()).ToList(), visibleRowIndices));
                else
                    menu.AddDisabledItem(new GUIContent("Re-apply Last Sort/Visible Filtered Rows Only"));

                menu.AddItem(new GUIContent("Copy Last Sort Expression"), false, () => {
                    EditorGUIUtility.systemCopyBuffer = BuildSortExpression(lastRules);
                    SetStatus("📋 Sort expression copied.", MessageType.Info);
                });
            }
            else {
                menu.AddDisabledItem(new GUIContent("Re-apply Last Sort/No last sort"));
                menu.AddDisabledItem(new GUIContent("Copy Last Sort Expression"));
            }

            menu.AddSeparator("");
            menu.AddDisabledItem(new GUIContent("Expression syntax: field asc, level number desc, name text asc"));
            menu.AddDisabledItem(new GUIContent("Tip: empty/null values stay at the bottom by default."));
            menu.ShowAsContext();
        }

        private void ApplySortExpression(string path, JArray arr, List<int> visibleRowIndices, string expression, bool visibleOnly) {
            var rules = ParseSortExpression(expression);
            if (rules.Count == 0) {
                SetStatus("❌ Sort expression is empty or invalid.", MessageType.Error);
                return;
            }

            SortRowsByFields(path, arr, rules, visibleOnly ? visibleRowIndices : null);
        }

        private List<SortRule> ParseSortExpression(string expression) {
            var rules = new List<SortRule>();
            if (string.IsNullOrWhiteSpace(expression)) return rules;

            foreach (string rawPart in expression.Split(',')) {
                string part = (rawPart ?? "").Trim();
                if (string.IsNullOrEmpty(part)) continue;

                bool ascending = true;
                if (part.StartsWith("-")) { ascending = false; part = part.Substring(1).Trim(); }
                else if (part.StartsWith("+")) { ascending = true; part = part.Substring(1).Trim(); }

                var tokens = part.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                if (tokens.Count == 0) continue;

                string key = tokens[0].Trim();
                var mode = SortValueMode.Auto;
                bool emptyLast = true;

                for (int i = 1; i < tokens.Count; i++) {
                    string t = tokens[i].Trim().ToLowerInvariant();
                    if (t == "asc" || t == "ascending" || t == "a-z" || t == "az") ascending = true;
                    else if (t == "desc" || t == "descending" || t == "z-a" || t == "za") ascending = false;
                    else if (t == "number" || t == "numeric" || t == "num") mode = SortValueMode.Number;
                    else if (t == "text" || t == "string" || t == "str") mode = SortValueMode.Text;
                    else if (t == "bool" || t == "boolean") mode = SortValueMode.Boolean;
                    else if (t == "date" || t == "datetime" || t == "time") mode = SortValueMode.Date;
                    else if (t == "emptyfirst" || t == "nullfirst") emptyLast = false;
                    else if (t == "emptylast" || t == "nulllast") emptyLast = true;
                }

                rules.Add(new SortRule { Key = key, Ascending = ascending, Mode = mode, EmptyLast = emptyLast });
            }

            return rules;
        }

        private void SortRowsByColumn(string path, JArray arr, string key, bool ascending, SortValueMode mode, List<int> affectedIndices = null) {
            SortRowsByFields(path, arr,
                new List<SortRule> { new SortRule { Key = key, Ascending = ascending, Mode = mode, EmptyLast = true } },
                affectedIndices);
        }

        private void SortRowsByFields(string path, JArray arr, List<SortRule> rules, List<int> affectedIndices) {
            if (arr == null || rules == null || rules.Count == 0) return;
            rules = rules.Where(r => r != null && !string.IsNullOrWhiteSpace(r.Key)).Select(r => r.Clone()).ToList();
            if (rules.Count == 0) return;

            bool visibleOnly = affectedIndices != null && affectedIndices.Count > 0 && affectedIndices.Count < arr.Count;

            if (visibleOnly) {
                var cleanIndices = affectedIndices.Where(i => i >= 0 && i < arr.Count).Distinct().OrderBy(i => i).ToList();
                var sorted = cleanIndices.Select(i => arr[i].DeepClone()).ToList();
                sorted.Sort((a, b) => CompareRowsByRules(a, b, rules));

                for (int i = 0; i < cleanIndices.Count; i++)
                    arr[cleanIndices[i]] = sorted[i];
            }
            else {
                var sorted = arr.Select(t => t.DeepClone()).ToList();
                sorted.Sort((a, b) => CompareRowsByRules(a, b, rules));
                arr.Clear();
                foreach (var tok in sorted) arr.Add(tok);
            }

            _lastSortRules[path] = rules.Select(r => r.Clone()).ToList();
            _lastSortLabels[path] = BuildSortLabel(rules) + (visibleOnly ? " (visible only)" : "");
            OnTokenChanged();
            SetStatus($"↕ Sorted {(visibleOnly ? "visible rows" : "all rows")} by {BuildSortLabel(rules)}.", MessageType.Info);
        }

        private static int CompareRowsByRules(JToken a, JToken b, List<SortRule> rules) {
            foreach (var rule in rules) {
                int cmp = CompareSortValues(GetColumnValue(a, rule.Key), GetColumnValue(b, rule.Key), rule.Mode, rule.EmptyLast, rule.Ascending);
                if (cmp != 0) return cmp;
            }
            return 0;
        }

        private static JToken GetColumnValue(JToken row, string key) {
            return row is JObject obj && obj.TryGetValue(key, out var v) ? v : JValue.CreateNull();
        }

        private static int CompareSortValues(JToken a, JToken b, SortValueMode mode, bool emptyLast, bool ascending) {
            bool ea = IsSortEmpty(a);
            bool eb = IsSortEmpty(b);
            if (ea || eb) {
                if (ea && eb) return 0;
                int emptyCmp = ea ? (emptyLast ? 1 : -1) : (emptyLast ? -1 : 1);
                return emptyCmp;
            }

            int cmp;
            switch (mode) {
                case SortValueMode.Number:
                    cmp = CompareAsNumber(a, b);
                    break;
                case SortValueMode.Boolean:
                    cmp = CompareAsBoolean(a, b);
                    break;
                case SortValueMode.Date:
                    cmp = CompareAsDate(a, b);
                    break;
                case SortValueMode.Text:
                    cmp = CompareAsText(a, b);
                    break;
                default:
                    cmp = CompareAuto(a, b);
                    break;
            }

            return ascending ? cmp : -cmp;
        }

        private static bool IsSortEmpty(JToken tok) {
            if (tok == null) return true;
            if (tok.Type == JTokenType.Null || tok.Type == JTokenType.Undefined) return true;
            return tok.Type == JTokenType.String && string.IsNullOrWhiteSpace(tok.Value<string>());
        }

        private static int CompareAuto(JToken a, JToken b) {
            if (TryTokenToDouble(a, out double da) && TryTokenToDouble(b, out double db)) return da.CompareTo(db);
            if (TryTokenToBool(a, out bool ba) && TryTokenToBool(b, out bool bb)) return ba.CompareTo(bb);
            if (TryTokenToDateTime(a, out DateTime ta) && TryTokenToDateTime(b, out DateTime tb)) return ta.CompareTo(tb);
            return CompareAsText(a, b);
        }

        private static int CompareAsNumber(JToken a, JToken b) {
            bool okA = TryTokenToDouble(a, out double da);
            bool okB = TryTokenToDouble(b, out double db);
            if (okA && okB) return da.CompareTo(db);
            if (okA != okB) return okA ? -1 : 1;
            return CompareAsText(a, b);
        }

        private static int CompareAsBoolean(JToken a, JToken b) {
            bool okA = TryTokenToBool(a, out bool ba);
            bool okB = TryTokenToBool(b, out bool bb);
            if (okA && okB) return ba.CompareTo(bb);
            if (okA != okB) return okA ? -1 : 1;
            return CompareAsText(a, b);
        }

        private static int CompareAsDate(JToken a, JToken b) {
            bool okA = TryTokenToDateTime(a, out DateTime da);
            bool okB = TryTokenToDateTime(b, out DateTime db);
            if (okA && okB) return da.CompareTo(db);
            if (okA != okB) return okA ? -1 : 1;
            return CompareAsText(a, b);
        }

        private static int CompareAsText(JToken a, JToken b) {
            return string.Compare(ScalarToDisplayString(a), ScalarToDisplayString(b), StringComparison.OrdinalIgnoreCase);
        }

        private static bool TryTokenToDouble(JToken tok, out double value) {
            value = 0;
            if (tok == null) return false;
            if (tok.Type == JTokenType.Integer || tok.Type == JTokenType.Float) {
                try { value = tok.Value<double>(); return true; }
                catch { return false; }
            }
            return double.TryParse(ScalarToDisplayString(tok), System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out value);
        }

        private static bool TryTokenToBool(JToken tok, out bool value) {
            value = false;
            if (tok == null) return false;
            if (tok.Type == JTokenType.Boolean) { value = tok.Value<bool>(); return true; }
            string s = ScalarToDisplayString(tok).Trim();
            if (bool.TryParse(s, out value)) return true;
            if (s == "0") { value = false; return true; }
            if (s == "1") { value = true; return true; }
            return false;
        }

        private static bool TryTokenToDateTime(JToken tok, out DateTime value) {
            value = default(DateTime);
            if (tok == null) return false;
            return DateTime.TryParse(ScalarToDisplayString(tok), System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.AssumeLocal, out value);
        }

        private static string BuildSortLabel(List<SortRule> rules) {
            if (rules == null || rules.Count == 0) return "";
            return string.Join(", ", rules.Select(r => $"{r.Key} {(r.Ascending ? "ASC" : "DESC")} [{r.Mode}]"));
        }

        private static string BuildSortExpression(List<SortRule> rules) {
            if (rules == null || rules.Count == 0) return "";
            return string.Join(", ", rules.Select(r =>
                $"{r.Key} {r.Mode.ToString().ToLowerInvariant()} {(r.Ascending ? "asc" : "desc")} {(r.EmptyLast ? "emptylast" : "emptyfirst")}"));
        }

        private static string GuessSortExpression(List<string> keys) {
            if (keys == null || keys.Count == 0) return "";
            foreach (string k in new[] { "order", "sortOrder", "id", "index", "level", "name" })
                if (keys.Contains(k)) return k + " asc";
            return keys[0] + " asc";
        }

        private static string TruncateMiddle(string s, int maxLen) {
            if (string.IsNullOrEmpty(s) || s.Length <= maxLen) return s;
            int left = Mathf.Max(1, maxLen / 2 - 1);
            int right = Mathf.Max(1, maxLen - left - 1);
            return s.Substring(0, left) + "…" + s.Substring(s.Length - right);
        }

        private void RenameColumn(JArray arr, string oldKey, string newKey) {
            newKey = (newKey ?? "").Trim();
            if (string.IsNullOrEmpty(newKey) || newKey == oldKey) return;

            bool conflict = arr.OfType<JObject>().Any(o => o.Property(newKey) != null);
            if (conflict && !EditorUtility.DisplayDialog("Rename Field",
                    $"Field '{newKey}' already exists in some rows. Replace/merge values?", "Rename", "Cancel")) return;

            foreach (var row in arr.OfType<JObject>()) {
                var prop = row.Property(oldKey);
                if (prop == null) continue;
                JToken value = prop.Value.DeepClone();
                prop.Remove();
                row[newKey] = value;
            }

            var keysToMove = _columnWidths.Keys.Where(k => k.EndsWith("." + oldKey, StringComparison.Ordinal)).ToList();
            foreach (var k in keysToMove) {
                float w = _columnWidths[k];
                _columnWidths.Remove(k);
                _columnWidths[k.Substring(0, k.Length - oldKey.Length) + newKey] = w;
            }

            OnTokenChanged();
            SetStatus($"Renamed '{oldKey}' to '{newKey}'.", MessageType.Info);
        }

        private void MoveColumn(JArray arr, string key, int delta) {
            bool changed = false;
            foreach (var row in arr.OfType<JObject>()) {
                var props = row.Properties()
                    .Select(p => new JProperty(p.Name, p.Value.DeepClone()))
                    .ToList();
                int from = props.FindIndex(p => p.Name == key);
                if (from < 0) continue;
                int to = Mathf.Clamp(from + delta, 0, props.Count - 1);
                if (to == from) continue;

                (props[from], props[to]) = (props[to], props[from]);
                row.RemoveAll();
                foreach (var p in props) row.Add(p);
                changed = true;
            }

            if (changed) OnTokenChanged();
        }

        private void SetColumnAllValues(JArray arr, string key, string valueText) {
            JToken sample = arr.OfType<JObject>().Select(o => o.TryGetValue(key, out var v) ? v : null).FirstOrDefault(v => v != null);
            JToken parsed = SmartParseScalar(valueText, sample);
            foreach (var row in arr.OfType<JObject>())
                row[key] = parsed.DeepClone();

            OnTokenChanged();
            SetStatus($"Set all '{key}' values.", MessageType.Info);
        }

        private void FillEmptyColumnValues(JArray arr, string key, string valueText) {
            JToken sample = arr.OfType<JObject>().Select(o => o.TryGetValue(key, out var v) ? v : null).FirstOrDefault(v => v != null);
            JToken parsed = SmartParseScalar(valueText, sample);

            int count = 0;
            foreach (var row in arr.OfType<JObject>()) {
                if (!row.TryGetValue(key, out var cur) || cur == null || cur.Type == JTokenType.Null ||
                    (cur.Type == JTokenType.String && string.IsNullOrWhiteSpace(cur.Value<string>()))) {
                    row[key] = parsed.DeepClone();
                    count++;
                }
            }

            OnTokenChanged();
            SetStatus($"Filled {count} empty '{key}' value(s).", MessageType.Info);
        }

        // ── Table row operations ───────────────────────────────────
        private JArray GetSelectedArray() {
            if (string.IsNullOrEmpty(_selTablePath) || _selRowIndex < 0 || _rootToken == null) return null;
            return FindArrayByPath(_rootToken, _selTablePath);
        }

        private JArray FindArrayByPath(JToken root, string path) {
            if (path == "root") return root as JArray;
            var parts = path.Split('.');
            JToken cur = root;
            foreach (var part in parts.Skip(1)) {
                if (cur is JObject obj && obj.TryGetValue(part, out cur)) continue;
                return null;
            }

            return cur as JArray;
        }

        private void CopySelectedRow() {
            var arr = GetSelectedArray();
            if (arr == null || _selRowIndex >= arr.Count) return;
            if (arr[_selRowIndex] is JObject row) {
                _clipboardRow = (JObject)row.DeepClone();
                _clipboardJson = _clipboardRow.ToString(Formatting.None);
                EditorGUIUtility.systemCopyBuffer = _clipboardJson;
                SetStatus($"✂ Row {_selRowIndex + 1} copied.", MessageType.Info);
            }
        }

        private void PasteRowBelow() {
            TryRefreshClipboardFromSystem();
            var arr = GetSelectedArray();
            if (arr == null || _clipboardRow == null) return;
            int at = _selRowIndex < 0 ? arr.Count : _selRowIndex + 1;
            arr.Insert(at, (JObject)_clipboardRow.DeepClone());
            _selRowIndex = at;
            OnTokenChanged();
        }

        private void DuplicateSelectedRow() {
            var arr = GetSelectedArray();
            if (arr == null || _selRowIndex < 0 || _selRowIndex >= arr.Count) return;
            if (arr[_selRowIndex] is JObject row) {
                arr.Insert(_selRowIndex + 1, (JObject)row.DeepClone());
                _selRowIndex++;
                OnTokenChanged();
            }
        }

        private void DeleteSelectedRow() {
            var arr = GetSelectedArray();
            if (arr == null || _selRowIndex < 0 || _selRowIndex >= arr.Count) return;
            arr.RemoveAt(_selRowIndex);
            if (_selRowIndex >= arr.Count) _selRowIndex = arr.Count - 1;
            OnTokenChanged();
        }

        private void MoveSelectedRow(int delta) {
            var arr = GetSelectedArray();
            if (arr == null || _selRowIndex < 0) return;
            int to = _selRowIndex + delta;
            if (to < 0 || to >= arr.Count) return;
            (arr[_selRowIndex], arr[to]) = (arr[to], arr[_selRowIndex]);
            _selRowIndex = to;
            OnTokenChanged();
        }

        private void ShiftSelection(int delta) {
            var arr = GetSelectedArray();
            if (arr == null) return;
            int next = Mathf.Clamp(_selRowIndex + delta, 0, arr.Count - 1);
            _selRowIndex = next;
            Repaint();
        }

        private void ClearSelection() {
            _selTablePath = "";
            _selRowIndex = -1;
            _selCellKey = "";
            _selCellRowIndex = -1;
            Repaint();
        }

        private void TryRefreshClipboardFromSystem() {
            string sys = EditorGUIUtility.systemCopyBuffer;
            if (string.IsNullOrWhiteSpace(sys) || sys == _clipboardJson) return;
            try {
                if (JToken.Parse(sys) is JObject obj) {
                    _clipboardRow = obj;
                    _clipboardJson = sys;
                }
            }
            catch {
                /* not a JSON object */
            }
        }

        // ── Primitive array section ────────────────────────────────
        private void DrawPrimitiveArraySection(JArray arr, string path, string label) {
            bool collapsed = _collapsed.Contains(path);
            var hdrR = GUILayoutUtility.GetRect(0, 26, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(hdrR, ClrSectionBg);

            float bx = hdrR.x + 4, by = hdrR.y + 4;
            if (GUI.Button(new Rect(bx, by, 18, 18), collapsed ? "▶" : "▼", EditorStyles.miniButton)) {
                if (collapsed) _collapsed.Remove(path);
                else _collapsed.Add(path);
            }

            var lblS = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = new Color(0.8f, 0.9f, 1f) } };
            GUI.Label(new Rect(bx + 22, by, 300, 18), $"{label}   [{arr.Count}]", lblS);

            if (GUI.Button(new Rect(hdrR.xMax - 120, by, 56, 18), "📋 Copy", EditorStyles.miniButton))
                EditorGUIUtility.systemCopyBuffer = arr.ToString(Formatting.None);

            if (GUI.Button(new Rect(hdrR.xMax - 60, by, 52, 18), "+ Add", EditorStyles.miniButton)) {
                JToken v = arr.Count > 0 ? CreateDefaultValueForToken(arr[0]) : JValue.CreateNull();
                arr.Add(v);
                _collapsed.Remove(path);
                OnTokenChanged();
            }

            if (!collapsed) {
                bool isShort = arr.Count <= 24 &&
                               arr.All(t => t.Type is JTokenType.Integer or JTokenType.Float or JTokenType.String);
                if (isShort) DrawPrimitiveArrayChips(arr, path);
                else DrawPrimitiveArrayRows(arr, path);
            }
        }

        // Horizontal chip-style for short arrays
        private void DrawPrimitiveArrayChips(JArray arr, string path) {
            string key = path + "__chips";
            var sv = _tableScrolls.TryGetValue(key, out var v) ? v : Vector2.zero;
            var bgR = GUILayoutUtility.GetRect(0, CellH + 6, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(bgR, new Color(0.14f, 0.14f, 0.18f));

            GUILayout.BeginArea(bgR);
            sv = GUILayout.BeginScrollView(sv, false, false, GUILayout.Height(bgR.height));
            GUILayout.BeginHorizontal();
            GUILayout.Space(4);

            int removeIdx = -1;
            for (int i = 0; i < arr.Count; i++) {
                int cap = i;
                var chipR = GUILayoutUtility.GetRect(76, CellH);
                EditorGUI.DrawRect(chipR, ClrChip);
                EditorGUI.DrawRect(new Rect(chipR.xMax, chipR.y, 2, chipR.height), ClrSep);

                // Index
                GUI.Label(new Rect(chipR.x + 2, chipR.y, 20, chipR.height),
                    $"[{i}]", EditorStyles.centeredGreyMiniLabel);

                // Value field
                EditorGUI.BeginChangeCheck();
                string ns = EditorGUI.TextField(
                    new Rect(chipR.x + 22, chipR.y + 1, 38, chipR.height - 2),
                    arr[i].ToString(), _cellField);
                if (EditorGUI.EndChangeCheck()) {
                    arr[cap] = TryConvertToOriginalType(arr[i], ns);
                    OnTokenChanged();
                }

                // Remove
                var prevC = GUI.color;
                GUI.color = new Color(1f, 0.5f, 0.5f);
                if (GUI.Button(new Rect(chipR.xMax - 15, chipR.y + 1, 13, chipR.height - 2),
                        "×", EditorStyles.miniLabel)) removeIdx = i;
                GUI.color = prevC;

                GUILayout.Space(3);
            }

            GUILayout.EndHorizontal();
            GUILayout.EndScrollView();
            GUILayout.EndArea();
            _tableScrolls[key] = sv;

            if (removeIdx >= 0) {
                arr.RemoveAt(removeIdx);
                OnTokenChanged();
                GUIUtility.ExitGUI();
            }
        }

        // Vertical rows for long arrays
        private void DrawPrimitiveArrayRows(JArray arr, string path) {
            string key = path + "__rows";
            var sv = _tableScrolls.TryGetValue(key, out var v) ? v : Vector2.zero;
            float rowsH = arr.Count * (CellH + 1);
            float bodyH = Mathf.Min(rowsH + 4, 200f);
            var bodyR = GUILayoutUtility.GetRect(0, bodyH, GUILayout.ExpandWidth(true));
            var inner = new Rect(0, 0, bodyR.width, rowsH + 4);

            sv = GUI.BeginScrollView(bodyR, sv, inner, false, rowsH + 4 > bodyH);
            int removeIdx = -1;
            for (int i = 0; i < arr.Count; i++) {
                float ry = i * (CellH + 1);
                EditorGUI.DrawRect(new Rect(0, ry, inner.width, CellH), i % 2 == 0 ? ClrRowEven : ClrRowOdd);
                GUI.Label(new Rect(4, ry + 2, RowNumW, CellH - 4), $"[{i}]", _rowNumStyle);
                int cap = i;
                DrawTokenValueField(arr[i],
                    new Rect(RowNumW + 4, ry + 2, inner.width - RowNumW - 34, CellH - 4),
                    tok => {
                        arr[cap] = tok;
                        OnTokenChanged();
                    });
                var prevC = GUI.color;
                GUI.color = new Color(1f, 0.5f, 0.5f);
                if (GUI.Button(new Rect(inner.width - 28, ry + 2, 24, CellH - 4), "✕", _actionBtn)) removeIdx = i;
                GUI.color = prevC;
            }

            GUI.EndScrollView();
            _tableScrolls[key] = sv;
            if (removeIdx >= 0) {
                arr.RemoveAt(removeIdx);
                OnTokenChanged();
                GUIUtility.ExitGUI();
            }
        }

        // ── Nested object section ──────────────────────────────────
        private void DrawNestedObjectSection(JObject obj, string path, string label) {
            bool collapsed = _collapsed.Contains(path);
            var hdrR = GUILayoutUtility.GetRect(0, 26, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(hdrR, ClrSectionBg);
            float bx = hdrR.x + 4, by = hdrR.y + 4;
            if (GUI.Button(new Rect(bx, by, 18, 18), collapsed ? "▶" : "▼", EditorStyles.miniButton)) {
                if (collapsed) _collapsed.Remove(path);
                else _collapsed.Add(path);
            }

            var lblS = new GUIStyle(EditorStyles.boldLabel) { normal = { textColor = new Color(0.8f, 0.9f, 1f) } };
            GUI.Label(new Rect(bx + 22, by, 280, 18), $"{label}   {{object}}", lblS);
            if (!collapsed) DrawJObjectProperties(obj, path);
        }

        // ── Token value field ──────────────────────────────────────
        private void DrawTokenValueField(JToken tok, Rect rect, Action<JToken> onChange) {
            if (tok == null) tok = JValue.CreateNull();

            switch (tok.Type) {
                case JTokenType.Boolean:
                {
                    bool cur = tok.Value<bool>();
                    var toggleR = new Rect(rect.x + 4, rect.y, Mathf.Min(rect.height, 22), rect.height);
                    bool next = EditorGUI.Toggle(toggleR, cur);
                    GUI.Label(new Rect(rect.x + 28, rect.y + 1, rect.width - 28, rect.height - 2), cur ? "true" : "false", EditorStyles.miniLabel);
                    if (next != cur) onChange(new JValue(next));
                    break;
                }

                case JTokenType.Object:
                case JTokenType.Array:
                {
                    DrawNestedTokenCell(tok, rect, onChange);
                    break;
                }

                case JTokenType.Null:
                {
                    Rect inputR = DrawTypeBadgeIfNeeded(tok, rect);
                    EditorGUI.BeginChangeCheck();
                    string next = EditorGUI.TextField(inputR, "null", _cellField);
                    if (EditorGUI.EndChangeCheck())
                        onChange(SmartParseScalar(next, tok));
                    break;
                }

                default:
                {
                    Rect inputR = DrawTypeBadgeIfNeeded(tok, rect);
                    EditorGUI.BeginChangeCheck();
                    string next = EditorGUI.TextField(inputR, ScalarToDisplayString(tok), _cellField);
                    if (EditorGUI.EndChangeCheck()) onChange(SmartParseScalar(next, tok));
                    break;
                }
            }
        }

        private Rect DrawTypeBadgeIfNeeded(JToken tok, Rect rect) {
            if (!_showTypeBadges || rect.width < 120) return rect;

            const float badgeW = 44f;
            var inputR = new Rect(rect.x, rect.y, rect.width - badgeW - 3, rect.height);
            var badgeR = new Rect(inputR.xMax + 3, rect.y + 2, badgeW, rect.height - 4);

            EditorGUI.DrawRect(badgeR, new Color(0.12f, 0.13f, 0.17f, 0.9f));
            var s = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 9
            };
            GUI.Label(badgeR, TokenTypeShort(tok), s);
            return inputR;
        }

        private static string TokenTypeShort(JToken tok) => tok?.Type switch
        {
            JTokenType.Integer => "int",
            JTokenType.Float => "float",
            JTokenType.Boolean => "bool",
            JTokenType.String => "str",
            JTokenType.Null => "null",
            JTokenType.Array => "array",
            JTokenType.Object => "obj",
            _ => "val"
        };

        private void DrawNestedTokenCell(JToken tok, Rect rect, Action<JToken> onChange) {
            var previewR = new Rect(rect.x, rect.y, Mathf.Max(20, rect.width - 46), rect.height);
            var btnR = new Rect(rect.xMax - 42, rect.y + 1, 42, rect.height - 2);

            EditorGUI.DrawRect(previewR, ClrNested);
            string icon = tok.Type == JTokenType.Array ? "[]" : "{}";
            string preview = CompactTokenPreview(tok, 90);
            GUI.Label(new Rect(previewR.x + 5, previewR.y + 1, previewR.width - 8, previewR.height - 2),
                $"{icon} {preview}", EditorStyles.miniLabel);

            if (GUI.Button(btnR, "Edit", EditorStyles.miniButton)) {
                JsonTokenEditWindow.Open(tok, edited => {
                    onChange(edited);
                    Repaint();
                });
            }
        }

        private static string ScalarToDisplayString(JToken tok) {
            if (tok == null || tok.Type == JTokenType.Null) return "null";
            return tok.Type == JTokenType.String ? tok.Value<string>() : tok.ToString(Formatting.None);
        }

        private static string CompactTokenPreview(JToken tok, int maxLen = 120) {
            string s = tok == null ? "null" : tok.ToString(Formatting.None);
            s = s.Replace("\r", "").Replace("\n", " ");
            return s.Length > maxLen ? s.Substring(0, maxLen - 1) + "…" : s;
        }

        // ═══════════════════════════════════════════════════════════
        //  TEXT MODE
        // ═══════════════════════════════════════════════════════════
        private void DrawTextMode(Rect rect) {
            GUILayout.BeginArea(rect);
            _textScroll = GUILayout.BeginScrollView(_textScroll);
            EditorGUI.BeginChangeCheck();
            _editText = GUILayout.TextArea(_editText, _monoStyle,
                GUILayout.ExpandHeight(true), GUILayout.ExpandWidth(true));
            if (EditorGUI.EndChangeCheck()) {
                _isDirty = true;
                _files[_selectedIndex].IsDirty = true;
                SetStatus("", MessageType.None);
            }

            GUILayout.EndScrollView();
            GUILayout.EndArea();
        }

        // ═══════════════════════════════════════════════════════════
        //  Status bar
        // ═══════════════════════════════════════════════════════════
        private void DrawStatusBar(Rect rect) {
            Color bg = _statusType switch
            {
                MessageType.Error => new Color(0.45f, 0.08f, 0.08f),
                MessageType.Warning => new Color(0.40f, 0.33f, 0.04f),
                MessageType.Info => new Color(0.08f, 0.28f, 0.45f),
                _ => new Color(0.17f, 0.17f, 0.17f),
            };
            Color fg = _statusType switch
            {
                MessageType.Error => ClrErr,
                MessageType.Warning => ClrDirty,
                MessageType.Info => ClrGood,
                _ => new Color(0.55f, 0.55f, 0.55f),
            };
            EditorGUI.DrawRect(rect, bg);
            GUI.Label(rect, _statusMsg, new GUIStyle(EditorStyles.miniLabel)
            {
                padding = new RectOffset(8, 8, 4, 4), normal = { textColor = fg }
            });
        }

        // ═══════════════════════════════════════════════════════════
        //  Column width helpers
        // ═══════════════════════════════════════════════════════════
        private float[] ComputeColumnWidths(string path, List<string> keys, JArray arr) {
            var widths = new float[keys.Count];
            for (int ci = 0; ci < keys.Count; ci++) {
                string key = keys[ci];
                string ck = path + "." + key;
                if (_columnWidths.TryGetValue(ck, out float cached)) {
                    widths[ci] = cached;
                    continue;
                }

                float maxW = GetPreferredColumnBaseWidth(key);
                int sample = Mathf.Min(arr.Count, 160);

                for (int ri = 0; ri < sample; ri++) {
                    if (arr[ri] is not JObject row || !row.TryGetValue(key, out var tv)) continue;
                    maxW = Mathf.Max(maxW, EstimateTokenDisplayWidth(key, tv));
                }

                maxW = Mathf.Clamp(maxW, GetColumnMinWidth(key), GetColumnMaxWidth(key));
                _columnWidths[ck] = maxW;
                widths[ci] = maxW;
            }

            return widths;
        }

        private static float GetPreferredColumnBaseWidth(string key) {
            string k = (key ?? "").ToLowerInvariant();

            if (IsCompactKey(k)) return 92f;
            if (k.Contains("name") || k.Contains("title")) return 190f;
            if (k.Contains("description") || k.Contains("desc") || k.Contains("productid") || k.Contains("address")) return 280f;
            if (k.Contains("sprite") || k.Contains("icon") || k.Contains("prefab") || k.Contains("asset") || k.Contains("path")) return 240f;
            if (k.Contains("json") || k.Contains("data") || k.Contains("config")) return 260f;

            return Mathf.Max((key?.Length ?? 0) * 9f + 36f, MinDataColW);
        }

        private static float GetColumnMinWidth(string key) {
            string k = (key ?? "").ToLowerInvariant();
            if (IsCompactKey(k)) return 74f;
            if (k.StartsWith("is") || k.StartsWith("can") || k.StartsWith("has") || k.Contains("enabled")) return 96f;
            return MinDataColW;
        }

        private static float GetColumnMaxWidth(string key) {
            string k = (key ?? "").ToLowerInvariant();
            if (IsCompactKey(k)) return 130f;
            if (k.Contains("description") || k.Contains("desc")) return 560f;
            if (k.Contains("productid") || k.Contains("path") || k.Contains("json") || k.Contains("config")) return 520f;
            if (k.Contains("name") || k.Contains("title")) return 360f;
            return MaxDataColW;
        }

        private static bool IsCompactKey(string k) {
            return k == "id" || k == "idx" || k == "index" || k == "order" || k == "sortorder" ||
                   k == "tier" || k == "level" || k == "lv" || k.EndsWith("id") && k.Length <= 8 ||
                   k.Contains("cost") || k.Contains("price") || k.Contains("count") || k.Contains("amount") ||
                   k.Contains("value") || k.Contains("rate") || k.Contains("multiplier") || k.Contains("duration") ||
                   k.Contains("cooldown");
        }

        private static float EstimateTokenDisplayWidth(string key, JToken token) {
            if (token == null || token.Type == JTokenType.Null) return 78f;

            switch (token.Type) {
                case JTokenType.Boolean:
                    return 104f;
                case JTokenType.Integer:
                case JTokenType.Float:
                    return Mathf.Clamp(token.ToString(Formatting.None).Length * 8.5f + 42f, GetColumnMinWidth(key), 160f);
                case JTokenType.Array:
                    return Mathf.Clamp(150f + Math.Min(token.ToString(Formatting.None).Length, 120) * 2.0f, 180f, GetColumnMaxWidth(key));
                case JTokenType.Object:
                    return Mathf.Clamp(160f + Math.Min(token.ToString(Formatting.None).Length, 120) * 2.0f, 200f, GetColumnMaxWidth(key));
                default:
                    string s = ScalarToDisplayString(token);
                    if (string.IsNullOrEmpty(s)) return 88f;
                    // Pixel-ish approximation that avoids overly huge columns for long text.
                    float w = s.Length <= 18 ? s.Length * 9.0f + 50f :
                              s.Length <= 48 ? s.Length * 7.5f + 70f :
                              s.Length <= 96 ? s.Length * 5.5f + 120f :
                              520f;
                    return Mathf.Clamp(w, GetColumnMinWidth(key), GetColumnMaxWidth(key));
            }
        }

        // ═══════════════════════════════════════════════════════════
        //  Token helpers
        // ═══════════════════════════════════════════════════════════
        private static JToken TryConvertToOriginalType(JToken original, string s) => SmartParseScalar(s, original);

        private static JToken SmartParseScalar(string s, JToken original = null) {
            s ??= "";

            if (original != null) {
                switch (original.Type) {
                    case JTokenType.String:
                        return new JValue(s);
                    case JTokenType.Integer:
                        return long.TryParse(s, out long lv) ? new JValue(lv) : new JValue(s);
                    case JTokenType.Float:
                        return double.TryParse(s,
                            System.Globalization.NumberStyles.Float,
                            System.Globalization.CultureInfo.InvariantCulture, out double dv)
                            ? new JValue(dv)
                            : new JValue(s);
                    case JTokenType.Boolean:
                        return bool.TryParse(s, out bool bv) ? new JValue(bv) : new JValue(s);
                }
            }

            string trim = s.Trim();
            if (trim.Equals("null", StringComparison.OrdinalIgnoreCase)) return JValue.CreateNull();
            if (trim.Equals("true", StringComparison.OrdinalIgnoreCase)) return new JValue(true);
            if (trim.Equals("false", StringComparison.OrdinalIgnoreCase)) return new JValue(false);
            if (long.TryParse(trim, out long il)) return new JValue(il);
            if (double.TryParse(trim,
                    System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double fl))
                return new JValue(fl);

            if ((trim.StartsWith("{") && trim.EndsWith("}")) || (trim.StartsWith("[") && trim.EndsWith("]"))) {
                try { return JToken.Parse(trim); }
                catch { /* keep as string */ }
            }

            return new JValue(s);
        }

        private static JToken CreateDefaultValueForToken(JToken sample) {
            if (sample == null) return JValue.CreateNull();
            return sample.Type switch
            {
                JTokenType.Integer => new JValue(0L),
                JTokenType.Float => new JValue(0.0),
                JTokenType.Boolean => new JValue(false),
                JTokenType.String => new JValue(""),
                JTokenType.Null => JValue.CreateNull(),
                JTokenType.Object => new JObject(((JObject)sample).Properties()
                    .Select(p => new JProperty(p.Name, CreateDefaultValueForToken(p.Value)))),
                JTokenType.Array => new JArray(),
                _ => new JValue("")
            };
        }

        private static void AddCloneRow(JArray arr) {
            if (arr.Count == 0) {
                arr.Add(new JObject());
                return;
            }

            var clone = arr[arr.Count - 1] is JObject last ? (JObject)last.DeepClone() : new JObject();
            foreach (var p in clone.Properties().ToList())
                p.Value = CreateDefaultValueForToken(p.Value);

            arr.Add(clone);
        }

        private void OnTokenChanged() {
            _isDirty = true;
            if (_selectedIndex >= 0 && _selectedIndex < _files.Count) _files[_selectedIndex].IsDirty = true;
            if (_rootToken != null) _editText = _rootToken.ToString(Formatting.Indented);
            SetStatus("", MessageType.None);
        }

        // ═══════════════════════════════════════════════════════════
        //  View mode switching
        // ═══════════════════════════════════════════════════════════
        private void SwitchViewMode(ViewMode next) {
            if (next == ViewMode.Visual) {
                if (TryParseJson(_editText, out JToken tok, out string err)) {
                    _rootToken = tok;
                    _parseError = false;
                    _viewMode = ViewMode.Visual;
                    _visualScroll = Vector2.zero;
                    _tableScrolls.Clear();
                    _tableFilters.Clear();
                    _lastSortRules.Clear();
                    _lastSortLabels.Clear();
                    _columnWidths.Clear();
                }
                else {
                    _rootToken = null;
                    _parseError = true;
                    _parseErrorMsg = err;
                    _viewMode = ViewMode.Visual;
                }
            }
            else {
                if (_rootToken != null) _editText = _rootToken.ToString(Formatting.Indented);
                _viewMode = ViewMode.Text;
            }
        }

        // ═══════════════════════════════════════════════════════════
        //  File / config copy helpers
        // ═══════════════════════════════════════════════════════════
        private JsonFileEntry CurrentFile =>
            _selectedIndex >= 0 && _selectedIndex < _files.Count ? _files[_selectedIndex] : null;

        private void CopyCurrentConfigName() {
            var file = CurrentFile;
            if (file == null) return;
            EditorGUIUtility.systemCopyBuffer = file.DisplayName;
            SetStatus($"📋 Config name copied: {file.DisplayName}", MessageType.Info);
        }

        private void CopyCurrentConfigPath() {
            var file = CurrentFile;
            if (file == null) return;
            EditorGUIUtility.systemCopyBuffer = file.RelativePath;
            SetStatus($"📋 Config path copied: {file.RelativePath}", MessageType.Info);
        }

        private void CopyCurrentConfigFullPath() {
            var file = CurrentFile;
            if (file == null) return;
            EditorGUIUtility.systemCopyBuffer = file.FullPath;
            SetStatus("📋 Full path copied.", MessageType.Info);
        }

        private void CopyCurrentConfigJson(bool pretty) {
            var file = CurrentFile;
            if (file == null) return;

            try {
                var tok = _rootToken ?? JToken.Parse(_editText);
                EditorGUIUtility.systemCopyBuffer = tok.ToString(pretty ? Formatting.Indented : Formatting.None);
                SetStatus(pretty ? "📋 Pretty JSON copied." : "📋 Minified JSON copied.", MessageType.Info);
            }
            catch (Exception e) {
                SetStatus($"❌ Cannot copy JSON: {e.Message}", MessageType.Error);
            }
        }

        private void RevealCurrentFile() {
            var file = CurrentFile;
            if (file == null || !File.Exists(file.FullPath)) return;
            EditorUtility.RevealInFinder(file.FullPath);
        }

        private void PingCurrentFile() {
            var file = CurrentFile;
            if (file == null) return;
            string assetPath = file.Source == DataSourceMode.DataConfig ? file.RelativePath : null;
            if (string.IsNullOrEmpty(assetPath)) return;
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(assetPath);
            if (asset != null) {
                EditorGUIUtility.PingObject(asset);
                Selection.activeObject = asset;
            }
        }

        private void ShowFileContextMenu(int index) {
            if (index < 0 || index >= _files.Count) return;

            var f = _files[index];
            var menu = new GenericMenu();

            menu.AddItem(new GUIContent("Open"), false, () => SelectFile(index));
            menu.AddItem(new GUIContent("Reveal In Folder"), false, () => {
                SelectFile(index);
                RevealCurrentFile();
            });
            if (f.Source == DataSourceMode.DataConfig) {
                menu.AddItem(new GUIContent("Ping In Project"), false, () => {
                    SelectFile(index);
                    PingCurrentFile();
                });
            }
            else {
                menu.AddDisabledItem(new GUIContent("Ping In Project"));
            }

            menu.AddSeparator("");

            menu.AddItem(new GUIContent("Copy/Config Name"), false, () => {
                EditorGUIUtility.systemCopyBuffer = f.DisplayName;
                SetStatus($"📋 Config name copied: {f.DisplayName}", MessageType.Info);
            });
            menu.AddItem(new GUIContent("Copy/Relative Path"), false, () => {
                EditorGUIUtility.systemCopyBuffer = f.RelativePath;
                SetStatus("📋 Relative path copied.", MessageType.Info);
            });
            menu.AddItem(new GUIContent("Copy/Full Path"), false, () => {
                EditorGUIUtility.systemCopyBuffer = f.FullPath;
                SetStatus("📋 Full path copied.", MessageType.Info);
            });

            menu.AddSeparator("");

            menu.AddItem(new GUIContent("Copy JSON/Minified"), false, () => {
                try {
                    EditorGUIUtility.systemCopyBuffer = JToken.Parse(File.ReadAllText(f.FullPath, Encoding.UTF8)).ToString(Formatting.None);
                    SetStatus("📋 Minified JSON copied.", MessageType.Info);
                }
                catch (Exception e) { SetStatus($"❌ Copy failed: {e.Message}", MessageType.Error); }
            });

            menu.AddItem(new GUIContent("Copy JSON/Pretty"), false, () => {
                try {
                    EditorGUIUtility.systemCopyBuffer = JToken.Parse(File.ReadAllText(f.FullPath, Encoding.UTF8)).ToString(Formatting.Indented);
                    SetStatus("📋 Pretty JSON copied.", MessageType.Info);
                }
                catch (Exception e) { SetStatus($"❌ Copy failed: {e.Message}", MessageType.Error); }
            });

            menu.AddSeparator("");

            menu.AddItem(new GUIContent("Duplicate Config File As..."), false, () => {
                SelectFile(index);
                StringInputWindow.Open("Duplicate Config", "New config file name:", f.DisplayName + "_copy",
                    newName => DuplicateCurrentConfigFile(newName));
            });

            menu.ShowAsContext();
        }

        private void DuplicateCurrentConfigFile(string newName) {
            var file = CurrentFile;
            if (file == null) return;

            newName = (newName ?? "").Trim();
            if (string.IsNullOrEmpty(newName)) return;
            if (!newName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) newName += ".json";

            string dir = Path.GetDirectoryName(file.FullPath);
            string target = Path.Combine(dir, newName);
            if (File.Exists(target)) {
                SetStatus($"❌ File already exists: {newName}", MessageType.Error);
                return;
            }

            try {
                File.Copy(file.FullPath, target);
                AssetDatabase.Refresh();
                RefreshFileList();
                SetStatus($"✅ Duplicated config: {newName}", MessageType.Info);
            }
            catch (Exception e) {
                SetStatus($"❌ Duplicate failed: {e.Message}", MessageType.Error);
            }
        }

        // ═══════════════════════════════════════════════════════════
        //  File operations
        // ═══════════════════════════════════════════════════════════
        private void RefreshFileList() {
            _files.Clear();

            if (_sourceMode == DataSourceMode.DataConfig) {
                foreach (string guid in AssetDatabase.FindAssets("t:TextAsset")) {
                    string assetPath = AssetDatabase.GUIDToAssetPath(guid);
                    string normalized = assetPath.Replace('\\', '/');
                    if (!normalized.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
                    if (normalized.IndexOf(DataConfigFolderMarker, StringComparison.OrdinalIgnoreCase) < 0) continue;

                    string fullPath = Path.GetFullPath(assetPath);
                    string displayName = normalized.Substring(
                        normalized.IndexOf(DataConfigFolderMarker, StringComparison.OrdinalIgnoreCase) + DataConfigFolderMarker.Length);
                    if (displayName.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
                        displayName = displayName.Substring(0, displayName.Length - ".json".Length);

                    _files.Add(new JsonFileEntry {
                        FullPath = fullPath,
                        RelativePath = normalized,
                        DisplayName = displayName,
                        Source = _sourceMode
                    });
                }
            }
            else {
                string folder = GetSaveDirectory();
                Directory.CreateDirectory(folder);

                foreach (string p in Directory.GetFiles(folder, "*", SearchOption.TopDirectoryOnly)) {
                    if (IsHiddenSaveFile(p)) continue;

                    _files.Add(new JsonFileEntry {
                        FullPath = p,
                        RelativePath = p,
                        DisplayName = Path.GetFileName(p),
                        Source = _sourceMode
                    });
                }
            }

            _files = _files.OrderBy(f => f.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();

            SetStatus($"Found {_files.Count} {(_sourceMode == DataSourceMode.DataConfig ? "config" : "save")} file(s).", MessageType.Info);

            if (_selectedIndex >= _files.Count) {
                ClearCurrentFileState();
            }
        }

        private void SelectFile(int idx) {
            if (idx == _selectedIndex) return;

            if (_isDirty && _selectedIndex >= 0) {
                int result = EditorUtility.DisplayDialogComplex(
                    "Unsaved Changes",
                    $"'{_files[_selectedIndex].DisplayName}' has unsaved changes.",
                    "Save",
                    "Discard",
                    "Cancel");

                if (result == 0) SaveCurrent();
                else if (result == 1 && _selectedIndex < _files.Count) _files[_selectedIndex].IsDirty = false;
                else return;
            }

            _selectedIndex = idx;
            _isDirty = false;
            _collapsed.Clear();
            _columnWidths.Clear();
            _tableScrolls.Clear();
            _tableFilters.Clear();
            _lastSortRules.Clear();
            _lastSortLabels.Clear();
            _visualScroll = Vector2.zero;
            _textScroll = Vector2.zero;
            _selTablePath = "";
            _selRowIndex = -1;
            _selCellKey = "";
            _selCellRowIndex = -1;
            LoadFile(_files[idx]);
        }

        private void LoadFile(JsonFileEntry file) {
            try {
                string raw = File.ReadAllText(file.FullPath, Encoding.UTF8);
                _loadedRawFile = raw;
                file.IsDirty = false;

                string visibleJson = raw;

                if (file.Source == DataSourceMode.Datasave) {
                    if (!TryExtractSavePayload(raw, out visibleJson, out string payloadError)) {
                        _editText = "";
                        _rootToken = null;
                        _parseError = true;
                        _parseErrorMsg = payloadError;
                        SetStatus(payloadError, MessageType.Warning);
                        return;
                    }
                }

                _editText = FormatJsonStr(visibleJson);

                if (TryParseJson(_editText, out JToken tok, out string err)) {
                    _rootToken = tok;
                    _parseError = false;
                    _parseErrorMsg = "";
                }
                else {
                    _rootToken = null;
                    _parseError = true;
                    _parseErrorMsg = err;
                }

                SetStatus(file.Source == DataSourceMode.Datasave
                    ? $"Loaded payload: {file.DisplayName}"
                    : $"Loaded: {file.RelativePath}", MessageType.Info);
            }
            catch (Exception e) {
                _editText = "";
                _rootToken = null;
                _parseError = true;
                _parseErrorMsg = e.Message;
                SetStatus($"Error: {e.Message}", MessageType.Error);
            }
        }

        private void SaveCurrent() {
            if (_selectedIndex < 0 || _selectedIndex >= _files.Count) return;

            var file = _files[_selectedIndex];
            string err = "";

            string payloadOrConfig = _viewMode == ViewMode.Visual && _rootToken != null
                ? _rootToken.ToString(Formatting.Indented)
                : ValidateJson(_editText, out err)
                    ? FormatJsonStr(_editText)
                    : null;

            if (payloadOrConfig == null) {
                SetStatus($"❌ Invalid JSON: {err}", MessageType.Error);
                return;
            }

            try {
                BackupFile(file.FullPath);

                string toSave = file.Source == DataSourceMode.Datasave
                    ? BuildSaveFileContent(payloadOrConfig)
                    : payloadOrConfig;

                File.WriteAllText(file.FullPath, toSave, Encoding.UTF8);
                _loadedRawFile = toSave;
                _editText = payloadOrConfig;
                _isDirty = false;
                file.IsDirty = false;

                if (file.Source == DataSourceMode.DataConfig) {
                    string assetPath = AbsoluteToAssetPath(file.FullPath);
                    if (!string.IsNullOrEmpty(assetPath))
                        AssetDatabase.ImportAsset(assetPath);
                    AssetDatabase.Refresh();
                }

                SetStatus(file.Source == DataSourceMode.Datasave
                    ? $"✅ Payload saved: {file.DisplayName}"
                    : $"✅ Saved: {file.RelativePath}", MessageType.Info);
            }
            catch (Exception e) {
                SetStatus($"❌ Save failed: {e.Message}", MessageType.Error);
            }
        }

        private void ReloadCurrent() {
            if (_selectedIndex < 0 || _selectedIndex >= _files.Count) return;
            if (_isDirty &&
                !EditorUtility.DisplayDialog("Reload", "Discard unsaved changes?", "Reload", "Cancel")) return;
            _isDirty = false;
            LoadFile(_files[_selectedIndex]);
        }

        private void FormatJson() {
            if (!ValidateJson(_editText, out string err)) {
                SetStatus($"❌ Cannot format: {err}", MessageType.Error);
                return;
            }

            _editText = FormatJsonStr(_editText);
            if (_viewMode == ViewMode.Text)
                _isDirty = true;
            SetStatus("Formatted.", MessageType.Info);
        }

        private bool TryExtractSavePayload(string raw, out string payloadJson, out string error) {
            payloadJson = "";
            error = "";
            _savePayloadWasString = false;

            try {
                JToken root = JToken.Parse(raw);

                if (root is JObject envelope && envelope["Payload"] != null) {
                    JToken payload = envelope["Payload"];
                    _savePayloadWasString = payload.Type == JTokenType.String;

                    if (_savePayloadWasString) {
                        string payloadText = payload.Value<string>();
                        if (string.IsNullOrWhiteSpace(payloadText)) {
                            payloadJson = "{}";
                            return true;
                        }

                        try {
                            payloadJson = JToken.Parse(payloadText).ToString(Formatting.Indented);
                            return true;
                        }
                        catch {
                            payloadJson = JsonConvert.SerializeObject(payloadText, Formatting.Indented);
                            return true;
                        }
                    }

                    payloadJson = payload.ToString(Formatting.Indented);
                    return true;
                }

                payloadJson = root.ToString(Formatting.Indented);
                return true;
            }
            catch (Exception e) {
                error = "Cannot extract save Payload: " + e.Message;
                return false;
            }
        }

        private string BuildSaveFileContent(string payloadJson) {
            JToken payloadToken = JToken.Parse(payloadJson);

            try {
                JToken original = JToken.Parse(_loadedRawFile);

                if (original is JObject envelope && envelope["Payload"] != null) {
                    envelope["Payload"] = _savePayloadWasString
                        ? payloadToken.ToString(Formatting.None)
                        : payloadToken.DeepClone();

                    return envelope.ToString(Formatting.Indented);
                }
            }
            catch {
                // Fallback below.
            }

            return payloadToken.ToString(Formatting.Indented);
        }

        private void BackupFile(string fullPath) {
            if (string.IsNullOrEmpty(fullPath) || !File.Exists(fullPath)) return;

            string backupPath = $"{fullPath}.bak-{DateTime.Now:yyyyMMdd-HHmmss}";
            File.Copy(fullPath, backupPath, true);
        }

        private void CreateNewDataConfigFile() {
            if (_sourceMode != DataSourceMode.DataConfig) return;

            Directory.CreateDirectory(ToAbsolutePath(DefaultDataConfigFolder));

            string target = EditorUtility.SaveFilePanel(
                "Create DataConfig JSON",
                ToAbsolutePath(DefaultDataConfigFolder),
                "new_config.json",
                "json");

            if (string.IsNullOrEmpty(target)) return;

            if (!target.Replace('\\', '/').Contains("/Resources/DataConfig/")) {
                EditorUtility.DisplayDialog("Invalid Path",
                    "DataConfig file must be inside a Resources/DataConfig folder.", "OK");
                return;
            }

            if (!File.Exists(target)) {
                File.WriteAllText(target, "{\n  \"items\": []\n}\n", Encoding.UTF8);
            }

            AssetDatabase.Refresh();
            RefreshFileList();

            int index = _files.FindIndex(f => string.Equals(f.FullPath, target, StringComparison.OrdinalIgnoreCase));
            if (index >= 0) SelectFile(index);
        }

        private void DeleteAllSaveFiles() {
            if (_sourceMode != DataSourceMode.Datasave || _files.Count == 0) return;

            if (!EditorUtility.DisplayDialog("Delete All Saves",
                    $"Delete all visible save files?\nCount: {_files.Count}\nBackup files are skipped.",
                    "Delete", "Cancel")) return;

            foreach (var file in _files.ToList()) {
                try {
                    BackupFile(file.FullPath);
                    File.Delete(file.FullPath);
                }
                catch (Exception e) {
                    Debug.LogException(e);
                }
            }

            ClearCurrentFileState();
            RefreshFileList();
            SetStatus("Deleted all save files.", MessageType.Warning);
        }

        private void CopyRemoteConfigJsonTemplate() {
            if (_sourceMode != DataSourceMode.DataConfig) return;

            var obj = new JObject();

            foreach (var file in _files) {
                try {
                    string key = Path.GetFileNameWithoutExtension(file.DisplayName).Replace('\\', '/');
                    string json = JToken.Parse(File.ReadAllText(file.FullPath, Encoding.UTF8)).ToString(Formatting.None);
                    obj[key] = json;
                }
                catch {
                    // skip invalid config
                }
            }

            EditorGUIUtility.systemCopyBuffer = obj.ToString(Formatting.Indented);
            SetStatus("📋 Remote config JSON template copied to clipboard.", MessageType.Info);
        }

        private void ClearCurrentFileState() {
            _selectedIndex = -1;
            _editText = "";
            _loadedRawFile = "";
            _isDirty = false;
            _rootToken = null;
            _parseError = false;
            _parseErrorMsg = "";
            _collapsed.Clear();
            _columnWidths.Clear();
            _tableScrolls.Clear();
            _tableFilters.Clear();
            _lastSortRules.Clear();
            _lastSortLabels.Clear();
            ClearSelection();
        }

        private bool ConfirmSwitchSource() {
            if (!_isDirty) return true;

            int result = EditorUtility.DisplayDialogComplex(
                "Unsaved Changes",
                "Current file has unsaved changes.",
                "Save",
                "Discard",
                "Cancel");

            if (result == 0) {
                SaveCurrent();
                return true;
            }

            return result == 1;
        }

        private void OnDestroy() {
            if (_isDirty && _selectedIndex >= 0 && _selectedIndex < _files.Count)
                if (EditorUtility.DisplayDialog("Unsaved Changes",
                        $"Save '{_files[_selectedIndex].DisplayName}' before closing?", "Save", "Discard"))
                    SaveCurrent();
        }

        // ═══════════════════════════════════════════════════════════
        //  Path helpers
        // ═══════════════════════════════════════════════════════════
        private static string GetSaveDirectory() {
            return Path.Combine(Application.persistentDataPath, SaveDirectoryName);
        }

        private static bool IsHiddenSaveFile(string path) {
            if (path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)) return true;
            if (path.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)) return true;
            if (path.Contains(".bak-", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static string ToAbsolutePath(string path) {
            if (Path.IsPathRooted(path)) return path;
            return Path.GetFullPath(path);
        }

        private static string AbsoluteToAssetPath(string fullPath) {
            fullPath = fullPath.Replace('\\', '/');
            string dataPath = Application.dataPath.Replace('\\', '/');

            if (!fullPath.StartsWith(dataPath, StringComparison.OrdinalIgnoreCase))
                return null;

            return "Assets" + fullPath.Substring(dataPath.Length);
        }

        // ═══════════════════════════════════════════════════════════
        //  JSON utilities
        // ═══════════════════════════════════════════════════════════
        private static bool TryParseJson(string text, out JToken result, out string error) {
            result = null;
            error = null;
            if (string.IsNullOrWhiteSpace(text)) {
                error = "Empty";
                return false;
            }

            try {
                result = JToken.Parse(text);
                return true;
            }
            catch (JsonException e) {
                error = e.Message;
                return false;
            }
        }

        private static bool ValidateJson(string text, out string error) => TryParseJson(text, out _, out error);

        private static string FormatJsonStr(string json) {
            try {
                return JToken.Parse(json).ToString(Formatting.Indented);
            }
            catch {
                return json;
            }
        }

        private void SetStatus(string msg, MessageType type) {
            _statusMsg = msg;
            _statusType = type;
            Repaint();
        }


        // ─── Multi-field sort popup ───────────────────────────
        private class MultiFieldSortWindow : EditorWindow {
            private List<string> _keys;
            private List<SortRule> _rules;
            private bool _canSortVisible;
            private Action<List<SortRule>> _onApplyAll;
            private Action<List<SortRule>> _onApplyVisible;
            private Vector2 _scroll;

            public static void Open(List<string> keys, List<SortRule> initialRules, bool canSortVisible,
                Action<List<SortRule>> onApplyAll, Action<List<SortRule>> onApplyVisible) {
                var w = CreateInstance<MultiFieldSortWindow>();
                w.titleContent = new GUIContent("Sort Table By Fields");
                w._keys = keys != null ? new List<string>(keys) : new List<string>();
                w._rules = initialRules != null && initialRules.Count > 0
                    ? initialRules.Select(r => r.Clone()).ToList()
                    : new List<SortRule>();
                if (w._rules.Count == 0 && w._keys.Count > 0)
                    w._rules.Add(new SortRule { Key = w._keys[0], Ascending = true, Mode = SortValueMode.Auto, EmptyLast = true });
                w._canSortVisible = canSortVisible;
                w._onApplyAll = onApplyAll;
                w._onApplyVisible = onApplyVisible;
                w.minSize = new Vector2(640, 260);
                w.ShowUtility();
                w.Focus();
            }

            private void OnGUI() {
                EditorGUILayout.LabelField("Sort priority: rule 1 runs first, then rule 2, then rule 3...", EditorStyles.boldLabel);
                EditorGUILayout.HelpBox("Example: sort by rarity ASC, then level DESC, then price ASC. Empty/null values stay last unless you turn off Empty Last.", MessageType.Info);

                _scroll = EditorGUILayout.BeginScrollView(_scroll);
                for (int i = 0; i < _rules.Count; i++) {
                    var rule = _rules[i];
                    EditorGUILayout.BeginHorizontal();

                    GUILayout.Label((i + 1).ToString(), GUILayout.Width(24));

                    int keyIndex = Mathf.Max(0, _keys.IndexOf(rule.Key));
                    int nextKeyIndex = EditorGUILayout.Popup(keyIndex, _keys.ToArray(), GUILayout.MinWidth(170));
                    if (_keys.Count > 0) rule.Key = _keys[Mathf.Clamp(nextKeyIndex, 0, _keys.Count - 1)];

                    rule.Mode = (SortValueMode)EditorGUILayout.EnumPopup(rule.Mode, GUILayout.Width(92));
                    rule.Ascending = GUILayout.Toggle(rule.Ascending, rule.Ascending ? "ASC" : "DESC", EditorStyles.miniButton, GUILayout.Width(64));
                    rule.EmptyLast = GUILayout.Toggle(rule.EmptyLast, "Empty Last", EditorStyles.miniButton, GUILayout.Width(86));

                    using (new EditorGUI.DisabledScope(i <= 0)) {
                        if (GUILayout.Button("↑", EditorStyles.miniButton, GUILayout.Width(28))) {
                            (_rules[i - 1], _rules[i]) = (_rules[i], _rules[i - 1]);
                            GUI.FocusControl(null);
                        }
                    }

                    using (new EditorGUI.DisabledScope(i >= _rules.Count - 1)) {
                        if (GUILayout.Button("↓", EditorStyles.miniButton, GUILayout.Width(28))) {
                            (_rules[i + 1], _rules[i]) = (_rules[i], _rules[i + 1]);
                            GUI.FocusControl(null);
                        }
                    }

                    var old = GUI.color;
                    GUI.color = new Color(1f, 0.55f, 0.55f);
                    if (GUILayout.Button("✕", EditorStyles.miniButton, GUILayout.Width(28))) {
                        _rules.RemoveAt(i);
                        GUI.color = old;
                        EditorGUILayout.EndHorizontal();
                        break;
                    }
                    GUI.color = old;

                    EditorGUILayout.EndHorizontal();
                }
                EditorGUILayout.EndScrollView();

                GUILayout.Space(6);
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("+ Add Rule", GUILayout.Width(100))) {
                    string key = _keys.Count > 0 ? _keys[0] : "";
                    _rules.Add(new SortRule { Key = key, Ascending = true, Mode = SortValueMode.Auto, EmptyLast = true });
                }

                if (GUILayout.Button("Clear Rules", GUILayout.Width(100))) _rules.Clear();

                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Cancel", GUILayout.Width(90))) Close();

                using (new EditorGUI.DisabledScope(_rules.Count == 0 || !_canSortVisible)) {
                    if (GUILayout.Button("Apply Visible", GUILayout.Width(110))) {
                        _onApplyVisible?.Invoke(CloneRules());
                        Close();
                    }
                }

                using (new EditorGUI.DisabledScope(_rules.Count == 0)) {
                    if (GUILayout.Button("Apply All", GUILayout.Width(100))) {
                        _onApplyAll?.Invoke(CloneRules());
                        Close();
                    }
                }

                EditorGUILayout.EndHorizontal();
            }

            private List<SortRule> CloneRules() {
                return _rules.Where(r => r != null && !string.IsNullOrWhiteSpace(r.Key)).Select(r => r.Clone()).ToList();
            }
        }

        // ─── Small popup window for adding fields ─────────────────
        private class StringInputWindow : EditorWindow {
            private string _label;
            private string _value;
            private Action<string> _onApply;

            public static void Open(string title, string label, string initial, Action<string> onApply) {
                var w = CreateInstance<StringInputWindow>();
                w.titleContent = new GUIContent(title);
                w._label = label;
                w._value = initial ?? "";
                w._onApply = onApply;
                w.minSize = new Vector2(320, 82);
                w.maxSize = new Vector2(420, 82);
                w.ShowUtility();
                w.Focus();
            }

            private void OnGUI() {
                GUILayout.Space(8);
                GUILayout.Label(_label, EditorStyles.boldLabel);
                GUI.SetNextControlName("input");
                _value = EditorGUILayout.TextField(_value);
                GUILayout.FlexibleSpace();
                GUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Cancel", GUILayout.Width(80))) Close();
                if (GUILayout.Button("Apply", GUILayout.Width(80))) {
                    _onApply?.Invoke(_value);
                    Close();
                }
                GUILayout.EndHorizontal();

                if (Event.current.type == EventType.KeyDown && Event.current.keyCode == KeyCode.Return) {
                    _onApply?.Invoke(_value);
                    Close();
                    Event.current.Use();
                }

                EditorGUI.FocusTextInControl("input");
            }
        }

        // ─── JSON object/array cell editor ────────────────────────
        private class JsonTokenEditWindow : EditorWindow {
            private string _text;
            private Vector2 _scroll;
            private Action<JToken> _onApply;
            private GUIStyle _textStyle;
            private string _error;

            public static void Open(JToken token, Action<JToken> onApply) {
                var w = CreateInstance<JsonTokenEditWindow>();
                w.titleContent = new GUIContent(token.Type == JTokenType.Array ? "Edit JSON Array" : "Edit JSON Object");
                w._text = token.ToString(Formatting.Indented);
                w._onApply = onApply;
                w.minSize = new Vector2(520, 360);
                w.ShowUtility();
                w.Focus();
            }

            private void OnGUI() {
                if (_textStyle == null) {
                    _textStyle = new GUIStyle(EditorStyles.textArea)
                    {
                        font = GetMonoFont(),
                        fontSize = 12,
                        wordWrap = false
                    };
                }

                EditorGUILayout.LabelField("Edit nested JSON value", EditorStyles.boldLabel);
                _scroll = EditorGUILayout.BeginScrollView(_scroll);
                _text = EditorGUILayout.TextArea(_text, _textStyle, GUILayout.ExpandHeight(true));
                EditorGUILayout.EndScrollView();

                if (!string.IsNullOrEmpty(_error))
                    EditorGUILayout.HelpBox(_error, MessageType.Error);

                GUILayout.BeginHorizontal();
                if (GUILayout.Button("Format", GUILayout.Width(90))) {
                    try {
                        _text = JToken.Parse(_text).ToString(Formatting.Indented);
                        _error = "";
                    }
                    catch (Exception e) { _error = e.Message; }
                }

                GUILayout.FlexibleSpace();

                if (GUILayout.Button("Cancel", GUILayout.Width(90))) Close();
                if (GUILayout.Button("Apply", GUILayout.Width(90))) {
                    try {
                        var parsed = JToken.Parse(_text);
                        _onApply?.Invoke(parsed);
                        Close();
                    }
                    catch (Exception e) {
                        _error = e.Message;
                    }
                }

                GUILayout.EndHorizontal();
            }
        }

        // ─── Data model ───────────────────────────────────────────
        private class JsonFileEntry {
            public string FullPath;
            public string RelativePath;
            public string DisplayName;
            public bool IsDirty;
            public DataSourceMode Source;
        }
    }
}