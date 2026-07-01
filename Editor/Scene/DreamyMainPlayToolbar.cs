using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Dreamy.EditorTools.Scene
{
    /// <summary>
    /// Registers Dreamy scene controls as supported Unity Editor toolbar elements.
    /// </summary>
    [InitializeOnLoad]
    public static class DreamyMainPlayToolbar
    {
        private const string SceneToolbarElementId = "Dreamy/Scene Controls";
        private const string TimeToolbarElementId = "Dreamy/Time Scale";

        private const string PlayFromBootstrapKeyPrefix =
            "Dreamy.EditorTools.PlayFromBootstrap.";
        private const string StartScenePathKeyPrefix =
            "Dreamy.EditorTools.StartScenePath.";
        private const string TimeScaleKeyPrefix =
            "Dreamy.EditorTools.TimeScale.";

        private const float SceneToolbarWidth = 390f;
        private const float TimeToolbarWidth = 150f;
        private const float ToolbarHeight = 22f;

        private static readonly float[] TimeScales =
        {
            0f,
            0.25f,
            0.5f,
            1f,
            2f,
            4f
        };

        private static readonly List<IMGUIContainer> SceneGuis = new List<IMGUIContainer>();
        private static readonly List<IMGUIContainer> TimeGuis = new List<IMGUIContainer>();

        static DreamyMainPlayToolbar()
        {
            EditorApplication.update += OnEditorUpdate;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.delayCall += ApplyPlayModeStartScene;

            EditorSceneManager.activeSceneChangedInEditMode += OnEditorActiveSceneChanged;
            SceneManager.activeSceneChanged += OnRuntimeActiveSceneChanged;
            EditorBuildSettings.sceneListChanged += OnBuildSettingsSceneListChanged;
        }

        private static void OnEditorUpdate()
        {
            if (EditorApplication.isPlaying)
            {
                RepaintToolbar();
            }
        }

        private static VisualElement CreateSceneToolbarContent()
        {
            VisualElement root = CreateRoot(SceneToolbarElementId, SceneToolbarWidth);
            root.style.marginRight = 4f;

            IMGUIContainer sceneGui = new IMGUIContainer(DrawSceneToolbarGui)
            {
                name = "DreamySceneToolbarIMGUI"
            };
            sceneGui.style.width = SceneToolbarWidth;
            sceneGui.style.height = ToolbarHeight;

            root.Add(sceneGui);
            SceneGuis.Add(sceneGui);
            sceneGui.RegisterCallback<DetachFromPanelEvent>(_ => SceneGuis.Remove(sceneGui));

            return root;
        }

        private static VisualElement CreateTimeToolbarContent()
        {
            VisualElement root = CreateRoot(TimeToolbarElementId, TimeToolbarWidth);
            root.style.marginLeft = 4f;

            IMGUIContainer timeGui = new IMGUIContainer(DrawTimeToolbarGui)
            {
                name = "DreamyTimeToolbarIMGUI"
            };
            timeGui.style.width = TimeToolbarWidth;
            timeGui.style.height = ToolbarHeight;

            root.Add(timeGui);
            TimeGuis.Add(timeGui);
            timeGui.RegisterCallback<DetachFromPanelEvent>(_ => TimeGuis.Remove(timeGui));

            return root;
        }

        private static VisualElement CreateRoot(string name, float width)
        {
            VisualElement root = new VisualElement
            {
                name = name
            };

            root.style.flexDirection = FlexDirection.Row;
            root.style.alignItems = Align.Center;
            root.style.width = width;
            root.style.height = ToolbarHeight;
            root.style.flexShrink = 0f;
            root.style.marginTop = 0f;
            root.style.marginBottom = 0f;
            root.style.paddingLeft = 0f;
            root.style.paddingRight = 0f;
            root.style.paddingTop = 0f;
            root.style.paddingBottom = 0f;

            return root;
        }

        private static void DrawSceneToolbarGui()
        {
            List<EditorBuildSettingsScene> scenes = GetEnabledScenes();
            List<string> labels = GetSceneLabels(scenes);
            int currentIndex = GetSafeCurrentSceneIndex(scenes);

            GUILayout.BeginHorizontal(GUILayout.Width(SceneToolbarWidth), GUILayout.Height(ToolbarHeight));

            using (new EditorGUI.DisabledScope(scenes.Count == 0))
            {
                if (GUILayout.Button("Prev", EditorStyles.toolbarButton, GUILayout.Width(38f), GUILayout.Height(20f)))
                {
                    OpenPreviousScene();
                }

                EditorGUI.BeginChangeCheck();
                int nextIndex = EditorGUILayout.Popup(
                    currentIndex,
                    labels.ToArray(),
                    EditorStyles.toolbarPopup,
                    GUILayout.Width(150f),
                    GUILayout.Height(20f));

                if (EditorGUI.EndChangeCheck() &&
                    nextIndex >= 0 &&
                    nextIndex < scenes.Count)
                {
                    OpenScene(scenes[nextIndex].path);
                }

                if (GUILayout.Button("Reload", EditorStyles.toolbarButton, GUILayout.Width(54f), GUILayout.Height(20f)))
                {
                    ReloadCurrentScene();
                }

                if (GUILayout.Button("Next", EditorStyles.toolbarButton, GUILayout.Width(38f), GUILayout.Height(20f)))
                {
                    OpenNextScene();
                }
            }

            if (GUILayout.Button(
                    "Start Scene",
                    EditorStyles.toolbarButton,
                    GUILayout.Width(90f),
                    GUILayout.Height(20f)))
            {
                Rect buttonRect = GUILayoutUtility.GetLastRect();
                UnityEditor.PopupWindow.Show(
                    GUIUtility.GUIToScreenRect(buttonRect),
                    new StartScenePopup());
            }

            GUILayout.EndHorizontal();
        }

        private static void DrawTimeToolbarGui()
        {
            List<string> labels = GetTimeScaleLabels();
            int currentIndex = GetTimeScaleIndex();

            GUILayout.BeginHorizontal(GUILayout.Width(TimeToolbarWidth), GUILayout.Height(ToolbarHeight));

            GUILayout.Label("Time", EditorStyles.miniLabel, GUILayout.Width(32f), GUILayout.Height(20f));

            EditorGUI.BeginChangeCheck();
            int nextIndex = EditorGUILayout.Popup(
                currentIndex,
                labels.ToArray(),
                EditorStyles.toolbarPopup,
                GUILayout.Width(62f),
                GUILayout.Height(20f));

            if (EditorGUI.EndChangeCheck() &&
                nextIndex >= 0 &&
                nextIndex < TimeScales.Length)
            {
                SetSavedTimeScale(TimeScales[nextIndex]);
            }

            if (GUILayout.Button(
                    "Reset",
                    EditorStyles.toolbarButton,
                    GUILayout.Width(46f),
                    GUILayout.Height(20f)))
            {
                SetSavedTimeScale(1f);
            }

            GUILayout.EndHorizontal();
        }

        internal static void OpenPreviousScene()
        {
            OpenRelativeScene(-1);
        }

        internal static void OpenNextScene()
        {
            OpenRelativeScene(1);
        }

        private static void OpenRelativeScene(int offset)
        {
            List<EditorBuildSettingsScene> scenes = GetEnabledScenes();

            if (scenes.Count == 0)
            {
                return;
            }

            int currentIndex = GetCurrentSceneIndex();
            int nextIndex = currentIndex < 0
                ? 0
                : (currentIndex + offset + scenes.Count) % scenes.Count;

            OpenScene(scenes[nextIndex].path);
        }

        internal static void ReloadCurrentScene()
        {
            string path = SceneManager.GetActiveScene().path;

            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            OpenScene(path);
        }

        private static void OpenScene(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                return;
            }

            if (EditorApplication.isPlaying)
            {
                int buildIndex = SceneUtility.GetBuildIndexByScenePath(path);

                if (buildIndex >= 0)
                {
                    SceneManager.LoadScene(buildIndex);
                }
                else
                {
                    SceneManager.LoadScene(Path.GetFileNameWithoutExtension(path));
                }

                EditorApplication.delayCall += RepaintToolbar;
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                RepaintToolbar();
                return;
            }

            EditorSceneManager.OpenScene(path);
            EditorApplication.delayCall += RepaintToolbar;
        }

        internal static void SetPlayFromBootstrap(bool enabled)
        {
            EditorPrefs.SetBool(GetProjectScopedPlayFromBootstrapKey(), enabled);

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            EditorBuildSettingsScene bootstrap = enabled
                ? GetSelectedStartScene()
                : null;

            EditorSceneManager.playModeStartScene = bootstrap == null
                ? null
                : AssetDatabase.LoadAssetAtPath<SceneAsset>(bootstrap.path);

            RepaintToolbar();
        }

        private static void ApplyPlayModeStartScene()
        {
            SetPlayFromBootstrap(EditorPrefs.GetBool(
                GetProjectScopedPlayFromBootstrapKey(),
                false));
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                Time.timeScale = GetSavedTimeScale();
            }

            if (state == PlayModeStateChange.EnteredEditMode)
            {
                ApplyPlayModeStartScene();
            }

            RepaintToolbar();
        }

        private static void OnEditorActiveSceneChanged(
            UnityEngine.SceneManagement.Scene oldScene,
            UnityEngine.SceneManagement.Scene newScene)
        {
            EditorApplication.delayCall += RepaintToolbar;
        }

        private static void OnRuntimeActiveSceneChanged(
            UnityEngine.SceneManagement.Scene oldScene,
            UnityEngine.SceneManagement.Scene newScene)
        {
            EditorApplication.delayCall += RepaintToolbar;
        }

        private static void OnBuildSettingsSceneListChanged()
        {
            EditorApplication.delayCall += RepaintToolbar;
            EditorApplication.delayCall += ApplyPlayModeStartScene;
        }

        private static void RepaintToolbar()
        {
            for (int i = SceneGuis.Count - 1; i >= 0; i--)
            {
                IMGUIContainer gui = SceneGuis[i];

                if (gui == null || gui.panel == null)
                {
                    SceneGuis.RemoveAt(i);
                    continue;
                }

                gui.MarkDirtyRepaint();
            }

            for (int i = TimeGuis.Count - 1; i >= 0; i--)
            {
                IMGUIContainer gui = TimeGuis[i];

                if (gui == null || gui.panel == null)
                {
                    TimeGuis.RemoveAt(i);
                    continue;
                }

                gui.MarkDirtyRepaint();
            }
        }

        private static int GetCurrentSceneIndex()
        {
            string currentPath = SceneManager.GetActiveScene().path;

            return string.IsNullOrEmpty(currentPath)
                ? -1
                : GetEnabledScenes().FindIndex(scene => string.Equals(
                    scene.path,
                    currentPath,
                    StringComparison.Ordinal));
        }

        private static int GetSafeCurrentSceneIndex(List<EditorBuildSettingsScene> scenes)
        {
            if (scenes == null || scenes.Count == 0)
            {
                return 0;
            }

            int currentIndex = GetCurrentSceneIndex();

            return currentIndex < 0
                ? 0
                : Mathf.Clamp(currentIndex, 0, scenes.Count - 1);
        }

        private static int GetTimeScaleIndex()
        {
            float selectedTimeScale = EditorApplication.isPlaying
                ? Time.timeScale
                : GetSavedTimeScale();
            int index = Array.FindIndex(TimeScales, value =>
                Mathf.Approximately(value, selectedTimeScale));

            if (index >= 0)
            {
                return index;
            }

            int defaultIndex = Array.IndexOf(TimeScales, 1f);
            return defaultIndex >= 0 ? defaultIndex : 0;
        }

        private static List<string> GetTimeScaleLabels()
        {
            return TimeScales
                .Select(value => value.ToString("0.##") + "x")
                .ToList();
        }

        private static List<string> GetSceneLabels(List<EditorBuildSettingsScene> scenes)
        {
            if (scenes == null || scenes.Count == 0)
            {
                return new List<string> { "No enabled scenes" };
            }

            List<string> names = scenes
                .Select(scene => Path.GetFileNameWithoutExtension(scene.path))
                .ToList();

            HashSet<string> duplicateNames = names
                .GroupBy(name => name)
                .Where(group => group.Count() > 1)
                .Select(group => group.Key)
                .ToHashSet();

            List<string> labels = new List<string>();

            foreach (EditorBuildSettingsScene scene in scenes)
            {
                string name = Path.GetFileNameWithoutExtension(scene.path);

                if (!duplicateNames.Contains(name))
                {
                    labels.Add(name);
                    continue;
                }

                string folder = Path.GetFileName(Path.GetDirectoryName(scene.path));
                labels.Add(string.IsNullOrEmpty(folder) ? name : name + " (" + folder + ")");
            }

            return labels;
        }

        private static List<EditorBuildSettingsScene> GetEnabledScenes()
        {
            return EditorBuildSettings.scenes
                .Where(scene =>
                    scene.enabled &&
                    !string.IsNullOrEmpty(scene.path) &&
                    AssetDatabase.LoadAssetAtPath<SceneAsset>(scene.path) != null)
                .ToList();
        }

        private static string GetProjectScopedPlayFromBootstrapKey()
        {
            return PlayFromBootstrapKeyPrefix + StableHash(Application.dataPath);
        }

        private static string GetProjectScopedStartScenePathKey()
        {
            return StartScenePathKeyPrefix + StableHash(Application.dataPath);
        }

        private static string GetProjectScopedTimeScaleKey()
        {
            return TimeScaleKeyPrefix + StableHash(Application.dataPath);
        }

        private static float GetSavedTimeScale()
        {
            return EditorPrefs.GetFloat(GetProjectScopedTimeScaleKey(), 1f);
        }

        private static void SetSavedTimeScale(float value)
        {
            EditorPrefs.SetFloat(GetProjectScopedTimeScaleKey(), value);
            if (EditorApplication.isPlaying)
            {
                Time.timeScale = value;
            }

            RepaintToolbar();
        }

        private static EditorBuildSettingsScene GetSelectedStartScene()
        {
            List<EditorBuildSettingsScene> scenes = GetEnabledScenes();
            if (scenes.Count == 0)
            {
                return null;
            }

            string savedPath = EditorPrefs.GetString(
                GetProjectScopedStartScenePathKey(),
                string.Empty);
            return scenes.FirstOrDefault(scene => scene.path == savedPath) ??
                   scenes[0];
        }

        private sealed class StartScenePopup : PopupWindowContent
        {
            public override Vector2 GetWindowSize()
            {
                return new Vector2(300f, 72f);
            }

            public override void OnGUI(Rect rect)
            {
                List<EditorBuildSettingsScene> scenes = GetEnabledScenes();
                bool enabled = EditorPrefs.GetBool(
                    GetProjectScopedPlayFromBootstrapKey(),
                    false);

                EditorGUI.BeginChangeCheck();
                bool nextEnabled = EditorGUILayout.ToggleLeft(
                    "Enable start scene",
                    enabled);
                if (EditorGUI.EndChangeCheck())
                {
                    SetPlayFromBootstrap(nextEnabled);
                }

                using (new EditorGUI.DisabledScope(scenes.Count == 0))
                {
                    List<string> labels = GetSceneLabels(scenes);
                    EditorBuildSettingsScene selected = GetSelectedStartScene();
                    int selectedIndex = selected == null
                        ? 0
                        : Mathf.Max(0, scenes.FindIndex(
                            scene => scene.path == selected.path));

                    EditorGUI.BeginChangeCheck();
                    int nextIndex = EditorGUILayout.Popup(
                        "Scene",
                        selectedIndex,
                        labels.ToArray());
                    if (EditorGUI.EndChangeCheck() &&
                        nextIndex >= 0 &&
                        nextIndex < scenes.Count)
                    {
                        EditorPrefs.SetString(
                            GetProjectScopedStartScenePathKey(),
                            scenes[nextIndex].path);
                        SetPlayFromBootstrap(nextEnabled);
                    }
                }
            }
        }

        private static string StableHash(string text)
        {
            unchecked
            {
                const ulong offsetBasis = 14695981039346656037UL;
                const ulong prime = 1099511628211UL;

                ulong hash = offsetBasis;

                foreach (char character in text ?? string.Empty)
                {
                    hash ^= character;
                    hash *= prime;
                }

                return hash.ToString("X16");
            }
        }

        [EditorToolbarElement(SceneToolbarElementId)]
        private sealed class SceneToolbarElement : VisualElement
        {
            public SceneToolbarElement()
            {
                Add(CreateSceneToolbarContent());
                ApplyPlayModeStartScene();
            }
        }

        [EditorToolbarElement(TimeToolbarElementId)]
        private sealed class TimeToolbarElement : VisualElement
        {
            public TimeToolbarElement()
            {
                Add(CreateTimeToolbarContent());
                ApplyPlayModeStartScene();
            }
        }
    }
}
