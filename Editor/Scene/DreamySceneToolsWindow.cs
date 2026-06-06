using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace Dreamy.EditorTools.Scene
{
    public sealed class DreamySceneToolsWindow : EditorWindow
    {
        private const string WindowTitle = "Dreamy Scenes";
        private const float ButtonWidth = 56f;

        private Vector2 scroll;
        private string searchText = string.Empty;

        [MenuItem("Tools/Dreamy/Scene/Scene Manager")]
        public static void Open()
        {
            DreamySceneToolsWindow window =
                GetWindow<DreamySceneToolsWindow>();
            window.titleContent = new GUIContent(WindowTitle);
            window.minSize = new Vector2(680f, 360f);
            window.Show();
        }

        private void OnGUI()
        {
            DrawToolbar();
            DrawValidation();

            scroll = EditorGUILayout.BeginScrollView(scroll);
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            for (int index = 0; index < scenes.Length; index++)
            {
                if (MatchesSearch(scenes[index].path))
                {
                    DrawSceneRow(scenes, index);
                }
            }

            EditorGUILayout.EndScrollView();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);
            searchText = GUILayout.TextField(
                searchText,
                EditorStyles.toolbarSearchField,
                GUILayout.MinWidth(160f));

            if (GUILayout.Button("Add Open", EditorStyles.toolbarButton))
            {
                AddScene(EditorSceneManager.GetActiveScene().path);
            }

            if (GUILayout.Button("Add Selected", EditorStyles.toolbarButton))
            {
                AddSelectedScenes();
            }

            if (GUILayout.Button("Add All", EditorStyles.toolbarButton))
            {
                AddAllProjectScenes();
            }

            if (GUILayout.Button("Remove Missing", EditorStyles.toolbarButton))
            {
                RemoveMissingScenes();
            }

            EditorGUILayout.EndHorizontal();
        }

        private static void DrawValidation()
        {
            int enabledCount = EditorBuildSettings.scenes.Count(
                scene => scene.enabled && File.Exists(scene.path));

            if (enabledCount == 0)
            {
                EditorGUILayout.HelpBox(
                    "No valid enabled scenes. Build will fail.",
                    MessageType.Error);
            }
        }

        private void DrawSceneRow(
            EditorBuildSettingsScene[] scenes,
            int index)
        {
            EditorBuildSettingsScene scene = scenes[index];
            EditorGUILayout.BeginHorizontal(EditorStyles.helpBox);

            bool enabled = EditorGUILayout.Toggle(
                scene.enabled,
                GUILayout.Width(18f));
            if (enabled != scene.enabled)
            {
                scene.enabled = enabled;
                ApplyScenes(scenes);
            }

            string label = File.Exists(scene.path)
                ? $"{index}. {scene.path}"
                : $"{index}. MISSING: {scene.path}";
            EditorGUILayout.LabelField(
                new GUIContent(label, scene.path),
                GUILayout.MinWidth(220f));

            using (new EditorGUI.DisabledScope(!File.Exists(scene.path)))
            {
                if (GUILayout.Button("Open", GUILayout.Width(ButtonWidth)))
                {
                    OpenScene(scene.path);
                }
            }

            using (new EditorGUI.DisabledScope(index == 0))
            {
                if (GUILayout.Button("First", GUILayout.Width(ButtonWidth)))
                {
                    MoveScene(scenes, index, 0);
                }

                if (GUILayout.Button("Up", GUILayout.Width(40f)))
                {
                    MoveScene(scenes, index, index - 1);
                }
            }

            using (new EditorGUI.DisabledScope(index == scenes.Length - 1))
            {
                if (GUILayout.Button("Down", GUILayout.Width(ButtonWidth)))
                {
                    MoveScene(scenes, index, index + 1);
                }
            }

            if (GUILayout.Button("X", GUILayout.Width(28f)))
            {
                RemoveScene(scenes, index);
            }

            EditorGUILayout.EndHorizontal();
        }

        private bool MatchesSearch(string path)
        {
            return string.IsNullOrWhiteSpace(searchText) ||
                   path.IndexOf(
                       searchText,
                       StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static void AddSelectedScenes()
        {
            foreach (SceneAsset scene in Selection.objects.OfType<SceneAsset>())
            {
                AddScene(AssetDatabase.GetAssetPath(scene));
            }
        }

        private static void AddAllProjectScenes()
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Scene"))
            {
                AddScene(AssetDatabase.GUIDToAssetPath(guid));
            }
        }

        private static void AddScene(string path)
        {
            if (string.IsNullOrEmpty(path) ||
                !path.EndsWith(".unity", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            List<EditorBuildSettingsScene> scenes =
                EditorBuildSettings.scenes.ToList();
            if (scenes.Any(scene => scene.path == path))
            {
                return;
            }

            scenes.Add(new EditorBuildSettingsScene(path, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static void RemoveScene(
            EditorBuildSettingsScene[] scenes,
            int index)
        {
            List<EditorBuildSettingsScene> result = scenes.ToList();
            result.RemoveAt(index);
            ApplyScenes(result);
        }

        private static void RemoveMissingScenes()
        {
            ApplyScenes(EditorBuildSettings.scenes.Where(
                scene => File.Exists(scene.path)));
        }

        private static void MoveScene(
            EditorBuildSettingsScene[] scenes,
            int sourceIndex,
            int destinationIndex)
        {
            List<EditorBuildSettingsScene> result = scenes.ToList();
            EditorBuildSettingsScene scene = result[sourceIndex];
            result.RemoveAt(sourceIndex);
            result.Insert(destinationIndex, scene);
            ApplyScenes(result);
        }

        private static void ApplyScenes(
            IEnumerable<EditorBuildSettingsScene> scenes)
        {
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static void OpenScene(string path)
        {
            if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
            {
                EditorSceneManager.OpenScene(path);
            }
        }
    }
}
