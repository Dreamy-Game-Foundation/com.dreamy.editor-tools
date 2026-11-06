using System;
using System.IO;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;

namespace Dreamy.EditorTools.Capture
{
    public sealed class DreamyScreenshotWindow : EditorWindow
    {
        private static readonly string[] ModeLabels = { "Copy", "Save", "Copy + Save" };
        private static readonly string[] DelayLabels = { "0 seconds", "3 seconds", "5 seconds" };
        private static readonly int[] Delays = { 0, 3, 5 };
        private bool actualSize;
        private bool showClipboardSetup;
        private Vector2 scroll;

        [MenuItem("Tools/Dreamy/Capture/Game View Screenshot")]
        public static void Open()
        {
            var window = GetWindow<DreamyScreenshotWindow>("Game View Screenshot");
            window.minSize = new Vector2(420, 340);
        }

        [MenuItem("Tools/Dreamy/Capture/Capture Game View")]
        [Shortcut("Dreamy/Capture Game View")]
        public static void CaptureQuick()
        {
            var preferences = CapturePreferences.instance;
            GameViewCapture.Start(preferences.Mode, preferences.DelaySeconds, preferences.OutputDirectory);
        }

        [MenuItem("Tools/Dreamy/Capture/Open Capture Folder")]
        public static void OpenCaptureFolder()
        {
            try
            {
                string directory = GameViewCapture.ResolveDirectory(CapturePreferences.instance.OutputDirectory);
                if (Directory.Exists(directory)) EditorUtility.RevealInFinder(directory);
                else Debug.Log("[Dreamy Capture] Folder will be created on Save: " + directory);
            }
            catch (Exception ex) { Debug.LogWarning("[Dreamy Capture] " + ex.Message); }
        }

        private void OnEnable() { GameViewCapture.Changed += Repaint; }
        private void OnDisable()
        {
            GameViewCapture.Changed -= Repaint;
            GameViewCapture.ClearPreview(this);
        }

        private void OnGUI()
        {
            var preferences = CapturePreferences.instance;
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("GAME VIEW SCREENSHOT", EditorStyles.boldLabel);
            using (new EditorGUI.DisabledScope(GameViewCapture.IsBusy))
            {
                EditorGUI.BeginChangeCheck();
                preferences.Mode = (CaptureMode)EditorGUILayout.Popup("After capture", Mathf.Clamp((int)preferences.Mode, 0, 2), ModeLabels);
                int delayIndex = Array.IndexOf(Delays, preferences.DelaySeconds);
                preferences.DelaySeconds = Delays[EditorGUILayout.Popup("Countdown", Math.Max(0, delayIndex), DelayLabels)];
                using (new EditorGUI.DisabledScope(!CaptureOutput.NeedsSave(preferences.Mode)))
                using (new EditorGUILayout.HorizontalScope())
                {
                    preferences.OutputDirectory = EditorGUILayout.TextField("Save folder", preferences.OutputDirectory);
                    if (GUILayout.Button("…", GUILayout.Width(28)))
                    {
                        string selected = EditorUtility.OpenFolderPanel("Screenshot folder", GameViewCapture.ProjectRoot, "");
                        if (!string.IsNullOrEmpty(selected)) preferences.OutputDirectory = selected;
                    }
                }
                if (EditorGUI.EndChangeCheck()) preferences.Persist();
            }
            DrawClipboardSetup();
            string unavailable = GameViewCapture.GetUnavailableReason(preferences.Mode);
            if (unavailable != null) EditorGUILayout.HelpBox(unavailable, MessageType.Info);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(GameViewCapture.IsBusy || unavailable != null))
                    if (GUILayout.Button("Capture Game View", GUILayout.Height(30)))
                        GameViewCapture.Start(preferences.Mode, preferences.DelaySeconds, preferences.OutputDirectory, this);
                using (new EditorGUI.DisabledScope(!GameViewCapture.IsBusy))
                    if (GUILayout.Button("Cancel", GUILayout.Width(70), GUILayout.Height(30))) GameViewCapture.Cancel();
            }
            EditorGUILayout.HelpBox(GameViewCapture.Status, MessageType.None);
            Texture2D preview = GameViewCapture.Preview;
            if (preview == null)
            {
                GUILayout.FlexibleSpace();
                EditorGUILayout.LabelField("Your last capture will appear here.", EditorStyles.centeredGreyMiniLabel);
                GUILayout.FlexibleSpace();
                return;
            }
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                GUILayout.Label(preview.width + " × " + preview.height);
                GUILayout.FlexibleSpace();
                actualSize = GUILayout.Toggle(actualSize, "100%", EditorStyles.toolbarButton);
            }
            scroll = EditorGUILayout.BeginScrollView(scroll, GUILayout.ExpandHeight(true));
            if (actualSize)
            {
                Rect rect = GUILayoutUtility.GetRect(preview.width, preview.height, GUILayout.ExpandWidth(false), GUILayout.ExpandHeight(false));
                GUI.DrawTexture(rect, preview, ScaleMode.StretchToFill);
            }
            else
            {
                Rect rect = GUILayoutUtility.GetRect(100, 100, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));
                GUI.DrawTexture(rect, preview, ScaleMode.ScaleToFit);
            }
            EditorGUILayout.EndScrollView();
            using (new EditorGUI.DisabledScope(GameViewCapture.IsBusy))
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(GameViewCapture.GetUnavailableReason(CaptureMode.Copy, false) != null))
                    if (GUILayout.Button("Copy Image")) GameViewCapture.OutputPreview(CaptureMode.Copy, null, this);
                if (GUILayout.Button("Save As…"))
                {
                    string directory = EditorUtility.OpenFolderPanel("Save capture to folder", GameViewCapture.ProjectRoot, "");
                    if (!string.IsNullOrEmpty(directory)) GameViewCapture.OutputPreview(CaptureMode.Save, directory, this);
                }
                using (new EditorGUI.DisabledScope(string.IsNullOrEmpty(GameViewCapture.SavedPath)))
                {
                    if (GUILayout.Button("Copy Path")) EditorGUIUtility.systemCopyBuffer = GameViewCapture.SavedPath;
                    if (GUILayout.Button("Reveal")) EditorUtility.RevealInFinder(GameViewCapture.SavedPath);
                    if (GUILayout.Button("Open")) EditorUtility.OpenWithDefaultApp(GameViewCapture.SavedPath);
                }
            }
            if (!string.IsNullOrEmpty(GameViewCapture.SavedPath))
                EditorGUILayout.SelectableLabel(GameViewCapture.SavedPath, EditorStyles.miniLabel, GUILayout.Height(32));
        }

        private void DrawClipboardSetup()
        {
            bool missing = ImageClipboard.Create().UnavailableReason != null;
            if (missing) EditorGUILayout.LabelField("Clipboard requirements", EditorStyles.boldLabel);
            else
            {
                showClipboardSetup = EditorGUILayout.Foldout(showClipboardSetup, "Clipboard requirements", true);
                if (!showClipboardSetup) return;
            }

            string instructions = ImageClipboard.GetSetupInstructions(out string command);
            EditorGUILayout.HelpBox(instructions, MessageType.Info);
            if (command == null) return;
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.SelectableLabel(command, EditorStyles.textField,
                    GUILayout.Height(EditorGUIUtility.singleLineHeight));
                if (GUILayout.Button(new GUIContent("Copy Install Command",
                    "Copy command text to clipboard. Replaces current clipboard contents; does not run the command."),
                    GUILayout.Width(150)))
                {
                    EditorGUIUtility.systemCopyBuffer = command;
                    ShowNotification(new GUIContent("Install command copied"));
                }
            }
        }
    }
}
