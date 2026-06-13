using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Dreamy.EditorTools.Scene
{
    /// <summary>
    /// Adds Dreamy scene controls around Unity's main Play/Pause controls.
    /// Scene controls are inserted on the left side of the Play controls.
    /// Time scale controls are inserted on the right side of the Play controls.
    ///
    /// Note: Unity does not expose a public API for extending the main editor toolbar,
    /// so this uses reflection against UnityEditor.Toolbar.
    /// </summary>
    [InitializeOnLoad]
    public static class DreamyMainPlayToolbar
    {
        private const string LegacyRootElementName = "DreamyMainPlayToolbarRoot";
        private const string SceneRootElementName = "DreamyMainPlayToolbarSceneRoot";
        private const string TimeRootElementName = "DreamyMainPlayToolbarTimeRoot";

        private const string PlayFromBootstrapKeyPrefix =
            "Dreamy.EditorTools.PlayFromBootstrap.";

        private static readonly Type ToolbarType =
            typeof(Editor).Assembly.GetType("UnityEditor.Toolbar");

        private static readonly BindingFlags InstanceFlags =
            BindingFlags.Instance |
            BindingFlags.Public |
            BindingFlags.NonPublic;

        private static readonly float[] TimeScales =
        {
            0f,
            0.25f,
            0.5f,
            1f,
            2f,
            4f
        };

        private static VisualElement sceneRoot;
        private static VisualElement timeRoot;

        private static PopupField<string> scenePopup;
        private static PopupField<string> timeScalePopup;
        private static Toggle bootstrapToggle;

        private static bool isRefreshingUi;

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
            TryAttachToMainToolbar();

            if (EditorApplication.isPlaying)
            {
                RefreshTimeScalePopup();
            }
        }

        private static void TryAttachToMainToolbar()
        {
            if (ToolbarType == null)
            {
                return;
            }

            if (sceneRoot != null &&
                sceneRoot.panel != null &&
                timeRoot != null &&
                timeRoot.panel != null)
            {
                return;
            }

            UnityEngine.Object[] toolbars =
                Resources.FindObjectsOfTypeAll(ToolbarType);

            if (toolbars == null ||
                toolbars.Length == 0 ||
                toolbars[0] is not ScriptableObject toolbar)
            {
                return;
            }

            VisualElement toolbarRoot = GetToolbarRoot(toolbar);

            if (toolbarRoot == null)
            {
                return;
            }

            RemoveExistingDreamyToolbar(toolbarRoot);

            VisualElement playZone = toolbarRoot.Q<VisualElement>("ToolbarZonePlayMode");

            sceneRoot = CreateSceneToolbarContent();
            timeRoot = CreateTimeToolbarContent();

            if (playZone != null)
            {
                // Insert scene controls before Unity Play/Pause/Step.
                playZone.Insert(0, sceneRoot);

                // Add time controls after Unity Play/Pause/Step.
                playZone.Add(timeRoot);
            }
            else
            {
                // Fallback for Unity versions/layouts where ToolbarZonePlayMode is unavailable.
                VisualElement leftZone = toolbarRoot.Q<VisualElement>("ToolbarZoneLeftAlign");
                VisualElement rightZone = toolbarRoot.Q<VisualElement>("ToolbarZoneRightAlign");

                if (leftZone == null && rightZone == null)
                {
                    return;
                }

                leftZone?.Add(sceneRoot);
                rightZone?.Insert(0, timeRoot);
            }

            RefreshUi();
            ApplyPlayModeStartScene();
        }

        private static VisualElement GetToolbarRoot(ScriptableObject toolbar)
        {
            FieldInfo rootField = ToolbarType.GetField("m_Root", InstanceFlags);

            if (rootField?.GetValue(toolbar) is VisualElement fieldRoot)
            {
                return fieldRoot;
            }

            PropertyInfo rootProperty = toolbar.GetType()
                .GetProperty("rootVisualElement", InstanceFlags);

            return rootProperty?.GetValue(toolbar) as VisualElement;
        }

        private static void RemoveExistingDreamyToolbar(VisualElement toolbarRoot)
        {
            RemoveElementByName(toolbarRoot, LegacyRootElementName);
            RemoveElementByName(toolbarRoot, SceneRootElementName);
            RemoveElementByName(toolbarRoot, TimeRootElementName);
        }

        private static void RemoveElementByName(VisualElement root, string elementName)
        {
            VisualElement element = root.Q<VisualElement>(elementName);

            if (element != null)
            {
                element.RemoveFromHierarchy();
            }
        }

        private static VisualElement CreateSceneToolbarContent()
        {
            VisualElement root = CreateGroupRoot(SceneRootElementName);
            root.style.marginRight = 5f;

            root.Add(CreateToolbarButton(
                "Prev",
                "Open previous enabled Build Settings scene",
                OpenPreviousScene,
                42f));

            List<EditorBuildSettingsScene> scenes = GetEnabledScenes();
            List<string> sceneLabels = GetSceneLabels(scenes);

            scenePopup = new PopupField<string>(
                sceneLabels,
                GetSafeCurrentSceneIndex(scenes))
            {
                tooltip = "Open an enabled Build Settings scene"
            };
            ApplyPopupStyle(scenePopup, 168f);
            scenePopup.RegisterValueChangedCallback(OnScenePopupChanged);
            root.Add(scenePopup);

            root.Add(CreateToolbarButton(
                "Reload",
                "Reload current scene",
                ReloadCurrentScene,
                56f));

            root.Add(CreateToolbarButton(
                "Next",
                "Open next enabled Build Settings scene",
                OpenNextScene,
                42f));

            bootstrapToggle = new Toggle("Bootstrap")
            {
                value = EditorPrefs.GetBool(
                    GetProjectScopedPlayFromBootstrapKey(),
                    false),
                tooltip = "Start Play Mode from the first enabled Build Settings scene"
            };
            ApplyToggleStyle(bootstrapToggle, 92f);
            bootstrapToggle.RegisterValueChangedCallback(change =>
                SetPlayFromBootstrap(change.newValue));
            root.Add(bootstrapToggle);

            ScheduleStyleRefresh(root);

            return root;
        }

        private static VisualElement CreateTimeToolbarContent()
        {
            VisualElement root = CreateGroupRoot(TimeRootElementName);
            root.style.marginLeft = 5f;

            Label timeLabel = new Label("Time");
            ApplyLabelStyle(timeLabel, 34f);
            root.Add(timeLabel);

            List<string> timeScaleLabels = GetTimeScaleLabels();

            timeScalePopup = new PopupField<string>(
                timeScaleLabels,
                GetTimeScaleIndex())
            {
                tooltip = "Change Time.timeScale in Play Mode"
            };
            ApplyPopupStyle(timeScalePopup, 70f);
            timeScalePopup.RegisterValueChangedCallback(OnTimeScalePopupChanged);
            root.Add(timeScalePopup);

            ScheduleStyleRefresh(root);

            return root;
        }

        private static VisualElement CreateGroupRoot(string name)
        {
            VisualElement root = new VisualElement
            {
                name = name
            };

            root.style.flexDirection = FlexDirection.Row;
            root.style.alignItems = Align.Center;
            root.style.height = 24f;
            root.style.flexShrink = 0f;
            root.style.paddingLeft = 4f;
            root.style.paddingRight = 4f;
            root.style.paddingTop = 1f;
            root.style.paddingBottom = 1f;

            Color groupColor = EditorGUIUtility.isProSkin
                ? new Color(0.18f, 0.18f, 0.18f, 0.65f)
                : new Color(0.76f, 0.76f, 0.76f, 0.55f);

            Color borderColor = EditorGUIUtility.isProSkin
                ? new Color(0.08f, 0.08f, 0.08f, 0.85f)
                : new Color(0.52f, 0.52f, 0.52f, 0.85f);

            root.style.backgroundColor = groupColor;
            root.style.borderTopColor = borderColor;
            root.style.borderBottomColor = borderColor;
            root.style.borderLeftColor = borderColor;
            root.style.borderRightColor = borderColor;
            root.style.borderTopWidth = 1f;
            root.style.borderBottomWidth = 1f;
            root.style.borderLeftWidth = 1f;
            root.style.borderRightWidth = 1f;
            root.style.borderTopLeftRadius = 4f;
            root.style.borderTopRightRadius = 4f;
            root.style.borderBottomLeftRadius = 4f;
            root.style.borderBottomRightRadius = 4f;

            return root;
        }

        private static Button CreateToolbarButton(
            string text,
            string tooltip,
            Action action,
            float width)
        {
            Button button = new Button(action)
            {
                text = text,
                tooltip = tooltip
            };

            ApplyButtonStyle(button, width);
            return button;
        }

        private static void ApplyButtonStyle(Button button, float width)
        {
            button.style.width = width;
            button.style.height = 20f;
            button.style.marginLeft = 1f;
            button.style.marginRight = 1f;
            button.style.paddingLeft = 4f;
            button.style.paddingRight = 4f;
            button.style.paddingTop = 0f;
            button.style.paddingBottom = 0f;
            button.style.unityTextAlign = TextAnchor.MiddleCenter;
            button.style.fontSize = 11f;
            button.style.color = GetTextColor();

            button.style.backgroundColor = EditorGUIUtility.isProSkin
                ? new Color(0.28f, 0.28f, 0.28f, 1f)
                : new Color(0.88f, 0.88f, 0.88f, 1f);

            Color borderColor = EditorGUIUtility.isProSkin
                ? new Color(0.10f, 0.10f, 0.10f, 1f)
                : new Color(0.56f, 0.56f, 0.56f, 1f);

            button.style.borderTopColor = borderColor;
            button.style.borderBottomColor = borderColor;
            button.style.borderLeftColor = borderColor;
            button.style.borderRightColor = borderColor;
            button.style.borderTopWidth = 1f;
            button.style.borderBottomWidth = 1f;
            button.style.borderLeftWidth = 1f;
            button.style.borderRightWidth = 1f;
            button.style.borderTopLeftRadius = 3f;
            button.style.borderTopRightRadius = 3f;
            button.style.borderBottomLeftRadius = 3f;
            button.style.borderBottomRightRadius = 3f;
        }

        private static void ApplyPopupStyle(PopupField<string> popup, float width)
        {
            popup.style.width = width;
            popup.style.height = 20f;
            popup.style.marginLeft = 2f;
            popup.style.marginRight = 2f;
            popup.style.paddingTop = 0f;
            popup.style.paddingBottom = 0f;
            popup.style.fontSize = 11f;
            popup.style.color = GetTextColor();
            popup.style.unityTextAlign = TextAnchor.MiddleLeft;

            popup.labelElement.style.display = DisplayStyle.None;

            popup.style.backgroundColor = EditorGUIUtility.isProSkin
                ? new Color(0.24f, 0.24f, 0.24f, 1f)
                : new Color(0.92f, 0.92f, 0.92f, 1f);

            Color borderColor = EditorGUIUtility.isProSkin
                ? new Color(0.10f, 0.10f, 0.10f, 1f)
                : new Color(0.56f, 0.56f, 0.56f, 1f);

            popup.style.borderTopColor = borderColor;
            popup.style.borderBottomColor = borderColor;
            popup.style.borderLeftColor = borderColor;
            popup.style.borderRightColor = borderColor;
            popup.style.borderTopWidth = 1f;
            popup.style.borderBottomWidth = 1f;
            popup.style.borderLeftWidth = 1f;
            popup.style.borderRightWidth = 1f;
            popup.style.borderTopLeftRadius = 3f;
            popup.style.borderTopRightRadius = 3f;
            popup.style.borderBottomLeftRadius = 3f;
            popup.style.borderBottomRightRadius = 3f;
        }

        private static void ApplyToggleStyle(Toggle toggle, float width)
        {
            toggle.style.width = width;
            toggle.style.height = 20f;
            toggle.style.marginLeft = 4f;
            toggle.style.marginRight = 1f;
            toggle.style.fontSize = 11f;
            toggle.style.color = GetTextColor();
            toggle.labelElement.style.color = GetTextColor();
            toggle.labelElement.style.unityTextAlign = TextAnchor.MiddleLeft;
        }

        private static void ApplyLabelStyle(Label label, float width)
        {
            label.style.width = width;
            label.style.height = 20f;
            label.style.fontSize = 11f;
            label.style.color = GetTextColor();
            label.style.unityTextAlign = TextAnchor.MiddleCenter;
        }

        private static void ScheduleStyleRefresh(VisualElement root)
        {
            root.schedule.Execute(() => ApplyTextColorRecursively(root)).ExecuteLater(50);
            root.schedule.Execute(() => ApplyTextColorRecursively(root)).ExecuteLater(500);
        }

        private static void ApplyTextColorRecursively(VisualElement element)
        {
            if (element == null)
            {
                return;
            }

            element.style.color = GetTextColor();

            foreach (VisualElement child in element.Children())
            {
                ApplyTextColorRecursively(child);
            }
        }

        private static Color GetTextColor()
        {
            return EditorGUIUtility.isProSkin
                ? new Color(0.88f, 0.90f, 0.94f, 1f)
                : new Color(0.08f, 0.08f, 0.08f, 1f);
        }

        private static void OnScenePopupChanged(ChangeEvent<string> change)
        {
            if (isRefreshingUi)
            {
                return;
            }

            List<EditorBuildSettingsScene> scenes = GetEnabledScenes();
            List<string> labels = GetSceneLabels(scenes);

            int index = labels.IndexOf(change.newValue);

            if (index < 0 || index >= scenes.Count)
            {
                RefreshUi();
                return;
            }

            OpenScene(scenes[index].path);
        }

        private static void OnTimeScalePopupChanged(ChangeEvent<string> change)
        {
            if (isRefreshingUi)
            {
                return;
            }

            int index = GetTimeScaleLabels().IndexOf(change.newValue);

            if (index < 0)
            {
                return;
            }

            if (!EditorApplication.isPlaying)
            {
                RefreshTimeScalePopup();
                Debug.LogWarning("Dreamy Play Toolbar: Time scale can only be changed in Play Mode.");
                return;
            }

            Time.timeScale = TimeScales[index];
        }

        private static void OpenPreviousScene()
        {
            OpenRelativeScene(-1);
        }

        private static void OpenNextScene()
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

        private static void ReloadCurrentScene()
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

                EditorApplication.delayCall += RefreshUi;
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                RefreshUi();
                return;
            }

            EditorSceneManager.OpenScene(path);
            EditorApplication.delayCall += RefreshUi;
        }

        private static void RefreshUi()
        {
            isRefreshingUi = true;

            RefreshScenePopup();
            RefreshBootstrapToggle();
            RefreshTimeScalePopup();

            if (sceneRoot != null)
            {
                ApplyTextColorRecursively(sceneRoot);
            }

            if (timeRoot != null)
            {
                ApplyTextColorRecursively(timeRoot);
            }

            isRefreshingUi = false;
        }

        private static void RefreshScenePopup()
        {
            if (scenePopup == null)
            {
                return;
            }

            List<EditorBuildSettingsScene> scenes = GetEnabledScenes();
            List<string> labels = GetSceneLabels(scenes);
            int sceneIndex = GetSafeCurrentSceneIndex(scenes);

            scenePopup.choices = labels;
            scenePopup.SetValueWithoutNotify(labels[sceneIndex]);
            scenePopup.SetEnabled(scenes.Count > 0);
        }

        private static void RefreshBootstrapToggle()
        {
            if (bootstrapToggle == null)
            {
                return;
            }

            bootstrapToggle.SetValueWithoutNotify(EditorPrefs.GetBool(
                GetProjectScopedPlayFromBootstrapKey(),
                false));
        }

        private static void RefreshTimeScalePopup()
        {
            if (timeScalePopup == null)
            {
                return;
            }

            List<string> labels = GetTimeScaleLabels();

            timeScalePopup.choices = labels;
            timeScalePopup.SetValueWithoutNotify(labels[GetTimeScaleIndex()]);
            timeScalePopup.SetEnabled(EditorApplication.isPlaying);
        }

        internal static void SetPlayFromBootstrap(bool enabled)
        {
            EditorPrefs.SetBool(GetProjectScopedPlayFromBootstrapKey(), enabled);

            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            EditorBuildSettingsScene bootstrap = enabled
                ? GetEnabledScenes().FirstOrDefault()
                : null;

            EditorSceneManager.playModeStartScene = bootstrap == null
                ? null
                : AssetDatabase.LoadAssetAtPath<SceneAsset>(bootstrap.path);
        }

        private static void ApplyPlayModeStartScene()
        {
            SetPlayFromBootstrap(EditorPrefs.GetBool(
                GetProjectScopedPlayFromBootstrapKey(),
                false));
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                Time.timeScale = 1f;
            }

            if (state == PlayModeStateChange.EnteredEditMode)
            {
                ApplyPlayModeStartScene();
            }

            RefreshUi();
        }

        private static void OnEditorActiveSceneChanged(
            UnityEngine.SceneManagement.Scene oldScene,
            UnityEngine.SceneManagement.Scene newScene)
        {
            EditorApplication.delayCall += RefreshUi;
        }

        private static void OnRuntimeActiveSceneChanged(
            UnityEngine.SceneManagement.Scene oldScene,
            UnityEngine.SceneManagement.Scene newScene)
        {
            EditorApplication.delayCall += RefreshUi;
        }

        private static void OnBuildSettingsSceneListChanged()
        {
            EditorApplication.delayCall += RefreshUi;
            EditorApplication.delayCall += ApplyPlayModeStartScene;
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
            int index = Array.FindIndex(TimeScales, value =>
                Mathf.Approximately(value, Time.timeScale));

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
    }
}
