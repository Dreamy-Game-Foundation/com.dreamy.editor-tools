using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace Dreamy.EditorTools.Scene
{
    [Overlay(typeof(SceneView), "Dreamy Play Toolbar", true)]
    public sealed class DreamyPlayToolbarOverlay : Overlay
    {
        private const string PlayFromBootstrapKey =
            "Dreamy.EditorTools.PlayFromBootstrap";

        private static readonly float[] TimeScales =
        {
            0f,
            0.25f,
            0.5f,
            1f,
            2f,
            4f
        };

        private PopupField<string> scenePopup;

        public override VisualElement CreatePanelContent()
        {
            VisualElement root = new VisualElement();
            root.style.flexDirection = FlexDirection.Row;

            Button previousButton = new(OpenPreviousScene) { text = "<" };
            previousButton.tooltip = "Open previous enabled Build Settings scene";
            root.Add(previousButton);

            List<string> sceneNames = GetSceneNames();
            int selectedIndex = GetCurrentSceneIndex();
            scenePopup = new PopupField<string>(
                sceneNames,
                Mathf.Clamp(selectedIndex, 0, Math.Max(0, sceneNames.Count - 1)));
            scenePopup.style.minWidth = 150f;
            scenePopup.tooltip = "Open an enabled Build Settings scene";
            scenePopup.RegisterValueChangedCallback(change =>
                OpenSceneAt(sceneNames.IndexOf(change.newValue)));
            root.Add(scenePopup);

            Button reloadButton = new(ReloadCurrentScene) { text = "Reload" };
            reloadButton.tooltip = "Reload current scene";
            root.Add(reloadButton);

            Button nextButton = new(OpenNextScene) { text = ">" };
            nextButton.tooltip = "Open next enabled Build Settings scene";
            root.Add(nextButton);

            Toggle bootstrapToggle = new("Play From Bootstrap")
            {
                value = EditorPrefs.GetBool(PlayFromBootstrapKey, false)
            };
            bootstrapToggle.tooltip =
                "Start Play Mode from the first enabled Build Settings scene";
            bootstrapToggle.RegisterValueChangedCallback(change =>
                SetPlayFromBootstrap(change.newValue));
            root.Add(bootstrapToggle);

            List<string> timeScaleLabels = TimeScales
                .Select(value => $"{value:0.##}x")
                .ToList();
            PopupField<string> timeScalePopup = new(
                "Time",
                timeScaleLabels,
                GetTimeScaleIndex());
            timeScalePopup.tooltip = "Change Time.timeScale in Play Mode";
            timeScalePopup.RegisterValueChangedCallback(change =>
            {
                int index = timeScaleLabels.IndexOf(change.newValue);
                if (index >= 0)
                {
                    Time.timeScale = TimeScales[index];
                }
            });
            root.Add(timeScalePopup);

            SetPlayFromBootstrap(bootstrapToggle.value);
            return root;
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
            OpenSceneAt(nextIndex);
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

        private static void OpenSceneAt(int index)
        {
            List<EditorBuildSettingsScene> scenes = GetEnabledScenes();
            if (index < 0 || index >= scenes.Count)
            {
                return;
            }

            OpenScene(scenes[index].path);
        }

        private static void OpenScene(string path)
        {
            if (EditorApplication.isPlaying)
            {
                SceneManager.LoadScene(path);
                return;
            }

            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                EditorSceneManager.OpenScene(path);
            }
        }

        internal static void SetPlayFromBootstrap(bool enabled)
        {
            EditorPrefs.SetBool(PlayFromBootstrapKey, enabled);
            EditorBuildSettingsScene bootstrap = GetEnabledScenes().FirstOrDefault();
            EditorSceneManager.playModeStartScene = enabled && bootstrap != null
                ? AssetDatabase.LoadAssetAtPath<SceneAsset>(bootstrap.path)
                : null;
        }

        private static int GetCurrentSceneIndex()
        {
            string currentPath = SceneManager.GetActiveScene().path;
            return GetEnabledScenes().FindIndex(scene =>
                string.Equals(scene.path, currentPath, StringComparison.Ordinal));
        }

        private static int GetTimeScaleIndex()
        {
            int index = Array.FindIndex(TimeScales, value =>
                Mathf.Approximately(value, Time.timeScale));
            return index >= 0 ? index : Array.IndexOf(TimeScales, 1f);
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
                .Where(scene => scene.enabled && File.Exists(scene.path))
                .ToList();
        }
    }

    [InitializeOnLoad]
    internal static class DreamyPlayToolbarInitializer
    {
        static DreamyPlayToolbarInitializer()
        {
            EditorApplication.delayCall += ApplyPlayModeStartScene;
        }

        private static void ApplyPlayModeStartScene()
        {
            bool enabled = EditorPrefs.GetBool(
                "Dreamy.EditorTools.PlayFromBootstrap",
                false);
            DreamyPlayToolbarOverlay.SetPlayFromBootstrap(enabled);
        }
    }
}
