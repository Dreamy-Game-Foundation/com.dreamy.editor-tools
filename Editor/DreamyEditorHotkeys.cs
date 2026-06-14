using System;
using System.Reflection;
using Dreamy.EditorTools.Scene;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.ShortcutManagement;
using UnityEngine;

namespace Dreamy.EditorTools
{
    public static class DreamyEditorHotkeys
    {
        private const BindingFlags InstanceFlags =
            BindingFlags.Instance |
            BindingFlags.NonPublic;

        [Shortcut("Dreamy/Compile Project", KeyCode.F5)]
        public static void CompileProject()
        {
            CompilationPipeline.RequestScriptCompilation(
                RequestScriptCompilationOptions.CleanBuildCache);
        }

        [Shortcut("Dreamy/Toggle Inspector Lock", KeyCode.L,
            ShortcutModifiers.Action)]
        public static void ToggleInspectorLock()
        {
            Type inspectorType = typeof(Editor).Assembly.GetType(
                "UnityEditor.InspectorWindow");
            Type lockTrackerType = typeof(EditorGUIUtility).Assembly.GetType(
                "UnityEditor.EditorGUIUtility+EditorLockTracker");
            MethodInfo flipLocked = lockTrackerType?.GetMethod(
                "FlipLocked",
                InstanceFlags);

            if (inspectorType == null || flipLocked == null)
            {
                ActiveEditorTracker.sharedTracker.isLocked =
                    !ActiveEditorTracker.sharedTracker.isLocked;
                return;
            }

            foreach (UnityEngine.Object inspector in
                     Resources.FindObjectsOfTypeAll(inspectorType))
            {
                object lockTracker = inspectorType.GetField(
                    "m_LockTracker",
                    InstanceFlags)?.GetValue(inspector);
                flipLocked.Invoke(lockTracker, Array.Empty<object>());
            }
        }

        [Shortcut("Dreamy/Close Focused Window", KeyCode.W,
            ShortcutModifiers.Action)]
        public static void CloseFocusedWindow()
        {
            EditorWindow.focusedWindow?.Close();
        }

        [Shortcut("Dreamy/Save Scene And Project", KeyCode.S,
            ShortcutModifiers.Action |
            ShortcutModifiers.Shift |
            ShortcutModifiers.Alt)]
        public static void SaveSceneAndProject()
        {
            EditorApplication.ExecuteMenuItem("File/Save");
            EditorApplication.ExecuteMenuItem("File/Save Project");
        }

        [Shortcut("Dreamy/Scene/Previous", KeyCode.PageUp,
            ShortcutModifiers.Alt)]
        public static void OpenPreviousScene()
        {
            DreamyMainPlayToolbar.OpenPreviousScene();
        }

        [Shortcut("Dreamy/Scene/Next", KeyCode.PageDown,
            ShortcutModifiers.Alt)]
        public static void OpenNextScene()
        {
            DreamyMainPlayToolbar.OpenNextScene();
        }

        [Shortcut("Dreamy/Scene/Reload", KeyCode.R,
            ShortcutModifiers.Alt)]
        public static void ReloadCurrentScene()
        {
            DreamyMainPlayToolbar.ReloadCurrentScene();
        }
    }
}
