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
    /// Adds Dreamy play controls to Unity's main toolbar.
    /// </summary>
    [InitializeOnLoad]
    public static class DreamyMainPlayToolbar
    {
        private const string RootElementName = "DreamyMainPlayToolbarRoot";
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
        private static readonly string[] PreferredToolbarZones =
        {
            "ToolbarZonePlayMode",
            "ToolbarZoneRightAlign",
            "ToolbarZoneLeftAlign"
        };

        private static VisualElement dreamyRoot;
        private static PopupField<string> scenePopup;
        private static PopupField<string> timeScalePopup;
        private static Toggle bootstrapToggle;
        private static bool isRefreshingUi;

        static DreamyMainPlayToolbar()
        {
            EditorApplication.update += OnEditorUpdate;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.delayCall += ApplyPlayModeStartScene;
            EditorSceneManager.activeSceneChangedInEditMode +=
                OnEditorActiveSceneChanged;
            SceneManager.activeSceneChanged += OnRuntimeActiveSceneChanged;
            EditorBuildSettings.sceneListChanged +=
                OnBuildSettingsSceneListChanged;
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
            if (ToolbarType == null ||
                dreamyRoot != null && dreamyRoot.panel != null)
            {
                return;
            }

            UnityEngine.Object[] toolbars =
                Resources.FindObjectsOfTypeAll(ToolbarType);
            if (toolbars == null || toolbars.Length == 0 ||
                toolbars[0] is not ScriptableObject toolbar)
            {
                return;
            }

            VisualElement toolbarRoot = GetToolbarRoot(toolbar);
            if (toolbarRoot == null)
            {
                return;
            }

            VisualElement existing =
                toolbarRoot.Q<VisualElement>(RootElementName);
            if (existing != null)
            {
                dreamyRoot = existing;
                return;
            }

            VisualElement targetZone = FindTargetZone(toolbarRoot);
            if (targetZone == null)
            {
                return;
            }

            dreamyRoot = CreateDreamyToolbarContent();
            targetZone.Add(dreamyRoot);
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

        private static VisualElement FindTargetZone(VisualElement root)
        {
            foreach (string zoneName in PreferredToolbarZones)
            {
                VisualElement zone = root.Q<VisualElement>(zoneName);
                if (zone != null)
                {
                    return zone;
                }
            }

            return null;
        }

        private static VisualElement CreateDreamyToolbarContent()
        {
            VisualElement root = new VisualElement
            {
                name = RootElementName
            };
            root.style.flexDirection = FlexDirection.Row;
            root.style.alignItems = Align.Center;
            root.style.marginLeft = 6f;
            root.style.marginRight = 4f;
            root.style.height = 22f;
            root.style.flexShrink = 0f;

            root.Add(CreateToolbarButton(
                "<",
                "Open previous enabled Build Settings scene",
                OpenPreviousScene,
                24f));

            List<string> sceneNames = GetSceneNames();
            scenePopup = new PopupField<string>(
                sceneNames,
                GetSafeCurrentSceneIndex(sceneNames))
            {
                tooltip = "Open an enabled Build Settings scene"
            };
            scenePopup.style.width = 150f;
            scenePopup.style.height = 20f;
            scenePopup.style.marginLeft = 2f;
            scenePopup.style.marginRight = 2f;
            scenePopup.RegisterValueChangedCallback(OnScenePopupChanged);
            root.Add(scenePopup);

            root.Add(CreateToolbarButton(
                "Reload",
                "Reload current scene",
                ReloadCurrentScene,
                54f));
            root.Add(CreateToolbarButton(
                ">",
                "Open next enabled Build Settings scene",
                OpenNextScene,
                24f));

            bootstrapToggle = new Toggle("Bootstrap")
            {
                value = EditorPrefs.GetBool(
                    GetProjectScopedPlayFromBootstrapKey(),
                    false),
                tooltip =
                    "Start Play Mode from the first enabled Build Settings scene"
            };
            bootstrapToggle.style.marginLeft = 6f;
            bootstrapToggle.style.height = 20f;
            bootstrapToggle.RegisterValueChangedCallback(change =>
                SetPlayFromBootstrap(change.newValue));
            root.Add(bootstrapToggle);

            List<string> timeScaleLabels = GetTimeScaleLabels();
            timeScalePopup = new PopupField<string>(
                timeScaleLabels,
                GetTimeScaleIndex())
            {
                tooltip = "Change Time.timeScale in Play Mode"
            };
            timeScalePopup.style.width = 64f;
            timeScalePopup.style.height = 20f;
            timeScalePopup.style.marginLeft = 4f;
            timeScalePopup.RegisterValueChangedCallback(
                OnTimeScalePopupChanged);
            root.Add(timeScalePopup);

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
            button.style.width = width;
            button.style.height = 20f;
            button.style.marginLeft = 1f;
            button.style.marginRight = 1f;
            button.style.paddingLeft = 3f;
            button.style.paddingRight = 3f;
            button.style.unityTextAlign = TextAnchor.MiddleCenter;
            return button;
        }

        private static void OnScenePopupChanged(ChangeEvent<string> change)
        {
            if (isRefreshingUi)
            {
                return;
            }

            List<EditorBuildSettingsScene> scenes = GetEnabledScenes();
            int index = GetSceneNames().IndexOf(change.newValue);
            if (index < 0 || index >= scenes.Count)
            {
                RefreshUi();
                return;
            }

            OpenScene(scenes[index].path);
        }

        private static void OnTimeScalePopupChanged(
            ChangeEvent<string> change)
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
            OpenScene(SceneManager.GetActiveScene().path);
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
                SceneManager.LoadScene(buildIndex >= 0
                    ? buildIndex
                    : Path.GetFileNameWithoutExtension(path));
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
            isRefreshingUi = false;
        }

        private static void RefreshScenePopup()
        {
            if (scenePopup == null)
            {
                return;
            }

            List<string> sceneNames = GetSceneNames();
            int sceneIndex = GetSafeCurrentSceneIndex(sceneNames);
            scenePopup.choices = sceneNames;
            scenePopup.SetValueWithoutNotify(sceneNames[sceneIndex]);
            scenePopup.SetEnabled(GetEnabledScenes().Count > 0);
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
            EditorPrefs.SetBool(
                GetProjectScopedPlayFromBootstrapKey(),
                enabled);
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

        private static int GetSafeCurrentSceneIndex(List<string> sceneNames)
        {
            int currentIndex = GetCurrentSceneIndex();
            return currentIndex < 0
                ? 0
                : Mathf.Clamp(currentIndex, 0, sceneNames.Count - 1);
        }

        private static int GetTimeScaleIndex()
        {
            int index = Array.FindIndex(TimeScales, value =>
                Mathf.Approximately(value, Time.timeScale));
            return index >= 0 ? index : Array.IndexOf(TimeScales, 1f);
        }

        private static List<string> GetTimeScaleLabels()
        {
            return TimeScales
                .Select(value => value.ToString("0.##") + "x")
                .ToList();
        }

        private static List<string> GetSceneNames()
        {
            List<string> names = GetEnabledScenes()
                .Select(scene => Path.GetFileNameWithoutExtension(scene.path))
                .ToList();
            if (names.Count == 0)
            {
                names.Add("No enabled scenes");
            }

            return names;
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
                const ulong OffsetBasis = 14695981039346656037UL;
                const ulong Prime = 1099511628211UL;
                ulong hash = OffsetBasis;
                foreach (char character in text ?? string.Empty)
                {
                    hash ^= character;
                    hash *= Prime;
                }

                return hash.ToString("X16");
            }
        }
    }
}
