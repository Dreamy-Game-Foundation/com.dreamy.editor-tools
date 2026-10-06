using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Toolbars;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Dreamy.EditorTools.Scene
{
    /// <summary>
    /// Registers Dreamy scene controls as supported Unity Editor toolbar elements.
    /// </summary>
    [InitializeOnLoad]
    public static class DreamyMainPlayToolbar
    {
        private const string SceneToolbarElementId = "Dreamy/Scene Controls";
        private const string ViewToolbarElementId = "Dreamy/Audio and Scene View";
        private static bool lastMuted;
        private static bool last2D;
        private static SceneView lastView;
        private static double nextViewRefresh;
        private const string TimeToolbarElementId = "Dreamy/Time Scale";
        private const string SceneToolbarTooltip = "Open, reload, and choose Dreamy project scenes.";
        private const string TimeToolbarTooltip = "Set the game time scale.";

        private const string PlayFromBootstrapKeyPrefix =
            "Dreamy.EditorTools.PlayFromBootstrap.";
        private const string StartScenePathKeyPrefix =
            "Dreamy.EditorTools.StartScenePath.";
        private const string TimeScaleKeyPrefix =
            "Dreamy.EditorTools.TimeScale.";

        private const float MinTimeScale = 0f;
        private const float MaxTimeScale = 5f;

        static DreamyMainPlayToolbar()
        {
            EditorApplication.update += UpdateViewToolbar;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            EditorApplication.delayCall += ApplyPlayModeStartScene;

            EditorSceneManager.activeSceneChangedInEditMode += OnEditorActiveSceneChanged;
            SceneManager.activeSceneChanged += OnRuntimeActiveSceneChanged;
            EditorBuildSettings.sceneListChanged += OnBuildSettingsSceneListChanged;
        }

        [MainToolbarElement(SceneToolbarElementId, defaultDockPosition = MainToolbarDockPosition.Left)]
        public static IEnumerable<MainToolbarElement> InstantiateSceneToolbar()
        {
            yield return new MainToolbarButton(
                new MainToolbarContent("Prev", "Open previous enabled scene"),
                OpenPreviousScene);

            yield return new MainToolbarDropdown(
                new MainToolbarContent("Scenes", SceneToolbarTooltip),
                ShowSceneDropdown);

            yield return new MainToolbarButton(
                new MainToolbarContent("Reload", "Reload current scene"),
                ReloadCurrentScene);

            yield return new MainToolbarButton(
                new MainToolbarContent("Next", "Open next enabled scene"),
                OpenNextScene);

            yield return new MainToolbarDropdown(
                new MainToolbarContent("Start Scene", "Choose Play Mode start scene"),
                ShowStartSceneDropdown);
        }

        [MainToolbarElement(TimeToolbarElementId, defaultDockPosition = MainToolbarDockPosition.Left)]
        public static IEnumerable<MainToolbarElement> InstantiateTimeScaleToolbar()
        {
            yield return new MainToolbarLabel(new MainToolbarContent("Time:"));

            MainToolbarContent content = new MainToolbarContent(
                "Time Scale",
                TimeToolbarTooltip);

            yield return new MainToolbarSlider(
                content,
                EditorApplication.isPlaying ? Time.timeScale : GetSavedTimeScale(),
                MinTimeScale,
                MaxTimeScale,
                SetTimeScale);

            yield return new MainToolbarButton(
                new MainToolbarContent("Reset", "Reset time scale to 1"),
                () => SetTimeScale(1f));
        }

        [MainToolbarElement(ViewToolbarElementId, defaultDockPosition = MainToolbarDockPosition.Left)]
        public static IEnumerable<MainToolbarElement> InstantiateViewToolbar()
        {
            yield return new MainToolbarToggle(
                new MainToolbarContent("Mute", "Mute audio in the Unity Editor"),
                EditorUtility.audioMasterMute,
                muted =>
                {
                    EditorUtility.audioMasterMute = muted;
                    MainToolbar.Refresh(ViewToolbarElementId);
                });

            SceneView view = SceneView.lastActiveSceneView;
            yield return new MainToolbarToggle(
                new MainToolbarContent("2D", "Toggle 2D/3D in the last active Scene View"),
                view != null && view.in2DMode,
                enabled =>
                {
                    SceneView target = SceneView.lastActiveSceneView;
                    if (target == null) target = EditorWindow.GetWindow<SceneView>();
                    target.in2DMode = enabled;
                    target.Repaint();
                    MainToolbar.Refresh(ViewToolbarElementId);
                });
        }

        private static void UpdateViewToolbar()
        {
            if (EditorApplication.timeSinceStartup < nextViewRefresh) return;
            nextViewRefresh = EditorApplication.timeSinceStartup + 0.25;
            SceneView view = SceneView.lastActiveSceneView;
            bool muted = EditorUtility.audioMasterMute;
            bool mode2D = view != null && view.in2DMode;
            if (view == lastView && muted == lastMuted && mode2D == last2D) return;
            lastView = view;
            lastMuted = muted;
            last2D = mode2D;
            MainToolbar.Refresh(ViewToolbarElementId);
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
            MainToolbar.Refresh(SceneToolbarElementId);
            MainToolbar.Refresh(TimeToolbarElementId);
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

        private static void SetTimeScale(float value)
        {
            value = Mathf.Clamp(value, MinTimeScale, MaxTimeScale);
            if (EditorApplication.isPlaying)
            {
                Time.timeScale = value;
            }
            else
            {
                EditorPrefs.SetFloat(GetProjectScopedTimeScaleKey(), value);
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

        private static void ShowSceneDropdown(Rect dropDownRect)
        {
            List<EditorBuildSettingsScene> scenes = GetEnabledScenes();
            string currentPath = SceneManager.GetActiveScene().path;
            GenericMenu menu = new GenericMenu();

            if (scenes.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("No enabled scenes"));
            }

            foreach (EditorBuildSettingsScene scene in scenes)
            {
                string label = GetSceneDropdownLabel(scene, scenes);
                bool selected = string.Equals(scene.path, currentPath, StringComparison.Ordinal);
                menu.AddItem(new GUIContent(label), selected, () => OpenScene(scene.path));
            }

            menu.DropDown(dropDownRect);
        }

        private static void ShowStartSceneDropdown(Rect dropDownRect)
        {
            List<EditorBuildSettingsScene> scenes = GetEnabledScenes();
            EditorBuildSettingsScene selectedScene = GetSelectedStartScene();
            bool enabled = EditorPrefs.GetBool(GetProjectScopedPlayFromBootstrapKey(), false);
            GenericMenu menu = new GenericMenu();

            menu.AddItem(
                new GUIContent("Enable start scene"),
                enabled,
                () => SetPlayFromBootstrap(!enabled));

            menu.AddSeparator(string.Empty);

            if (scenes.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("No enabled scenes"));
            }

            foreach (EditorBuildSettingsScene scene in scenes)
            {
                string label = "Scene/" + GetSceneDropdownLabel(scene, scenes);
                bool selected = selectedScene != null &&
                    string.Equals(scene.path, selectedScene.path, StringComparison.Ordinal);

                menu.AddItem(new GUIContent(label), selected, () =>
                {
                    EditorPrefs.SetString(GetProjectScopedStartScenePathKey(), scene.path);
                    SetPlayFromBootstrap(EditorPrefs.GetBool(
                        GetProjectScopedPlayFromBootstrapKey(),
                        false));
                });
            }

            menu.DropDown(dropDownRect);
        }

        private static string GetSceneDropdownLabel(
            EditorBuildSettingsScene scene,
            List<EditorBuildSettingsScene> scenes)
        {
            List<string> labels = GetSceneLabels(scenes);
            int index = scenes.FindIndex(item =>
                string.Equals(item.path, scene.path, StringComparison.Ordinal));

            return index >= 0 && index < labels.Count
                ? labels[index]
                : Path.GetFileNameWithoutExtension(scene.path);
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
