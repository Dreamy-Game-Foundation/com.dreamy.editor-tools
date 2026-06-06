using System;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace Dreamy.EditorTools
{
    public static class DreamyEditorMenu
    {
        private const string ProjectMenuRoot = "Tools/Dreamy/Project/";
        private const string PlayerPrefsMenuRoot = "Tools/Dreamy/PlayerPrefs/";
        private const string ManifestPath = "Packages/manifest.json";

        [MenuItem(PlayerPrefsMenuRoot + "Clear All")]
        public static void ClearPlayerPrefs()
        {
            bool confirmed = EditorUtility.DisplayDialog(
                "Clear PlayerPrefs",
                "Delete all PlayerPrefs for this project?",
                "Delete",
                "Cancel");

            if (!confirmed)
            {
                return;
            }

            PlayerPrefs.DeleteAll();
            PlayerPrefs.Save();
        }

        [MenuItem(ProjectMenuRoot + "Open manifest.json")]
        public static void OpenManifest()
        {
            UnityEditorInternal.InternalEditorUtility.OpenFileAtLineExternal(ManifestPath, 1);
        }

        [MenuItem(ProjectMenuRoot + "Clear Console")]
        public static void ClearConsole()
        {
            Assembly assembly = Assembly.GetAssembly(typeof(SceneView));
            Type logEntries = assembly.GetType("UnityEditor.LogEntries");
            MethodInfo clearMethod = logEntries?.GetMethod("Clear", BindingFlags.Static | BindingFlags.Public);
            clearMethod?.Invoke(null, null);
        }
    }
}
