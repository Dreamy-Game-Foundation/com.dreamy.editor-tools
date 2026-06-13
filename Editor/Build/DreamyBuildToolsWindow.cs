using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Dreamy.EditorTools.Build
{
    public sealed class DreamyBuildToolsWindow : EditorWindow
    {
        private const string WindowTitle = "Dreamy Build";
        private const string DefaultProductName = "Game";

        private DreamyBuildSettings settings;
        private Vector2 scroll;
        private string statusMessage = string.Empty;

        [MenuItem("Tools/Dreamy/Build/Build Manager")]
        public static void Open()
        {
            DreamyBuildToolsWindow window =
                GetWindow<DreamyBuildToolsWindow>();
            window.titleContent = new GUIContent(WindowTitle);
            window.minSize = new Vector2(520f, 440f);
            window.Show();
        }

        [MenuItem("Tools/Dreamy/Build/Build Current Target")]
        public static void BuildCurrentTarget()
        {
            BuildPlayer(DreamyBuildSettings.Load(), false);
        }

        private void OnEnable()
        {
            settings = DreamyBuildSettings.Load();
        }

        private void OnGUI()
        {
            scroll = EditorGUILayout.BeginScrollView(scroll);
            DrawProjectInfo();
            DrawBuildSettings();
            DrawValidation();
            DrawActions();
            EditorGUILayout.EndScrollView();
        }

        private static void DrawProjectInfo()
        {
            EditorGUILayout.LabelField(
                "Player Settings",
                EditorStyles.boldLabel);

            PlayerSettings.productName = EditorGUILayout.TextField(
                "Product Name",
                PlayerSettings.productName);
            PlayerSettings.bundleVersion = EditorGUILayout.TextField(
                "Version",
                PlayerSettings.bundleVersion);
            PlayerSettings.Android.bundleVersionCode =
                EditorGUILayout.IntField(
                    "Android Version Code",
                    PlayerSettings.Android.bundleVersionCode);
            PlayerSettings.iOS.buildNumber = EditorGUILayout.TextField(
                "iOS Build Number",
                PlayerSettings.iOS.buildNumber);
        }

        private void DrawBuildSettings()
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField(
                "Build Settings",
                EditorStyles.boldLabel);

            EditorGUI.BeginChangeCheck();
            settings.Target = (BuildTarget)EditorGUILayout.EnumPopup(
                "Target",
                settings.Target);
            settings.OutputDirectory = EditorGUILayout.TextField(
                "Output Directory",
                settings.OutputDirectory);

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(EditorGUIUtility.labelWidth);
            if (GUILayout.Button("Choose Folder"))
            {
                string selected = EditorUtility.OpenFolderPanel(
                    "Build Output",
                    GetAbsoluteOutputRoot(settings),
                    string.Empty);
                if (!string.IsNullOrEmpty(selected))
                {
                    settings.OutputDirectory = ToProjectRelativeOrAbsolute(
                        selected);
                }
            }

            if (GUILayout.Button("Open Folder"))
            {
                EditorUtility.RevealInFinder(
                    GetAbsoluteOutputRoot(settings));
            }

            EditorGUILayout.EndHorizontal();

            settings.DevelopmentBuild = EditorGUILayout.Toggle(
                "Development Build",
                settings.DevelopmentBuild);

            using (new EditorGUI.DisabledScope(!settings.DevelopmentBuild))
            {
                settings.AllowDebugging = EditorGUILayout.Toggle(
                    "Script Debugging",
                    settings.AllowDebugging);
                settings.ConnectProfiler = EditorGUILayout.Toggle(
                    "Autoconnect Profiler",
                    settings.ConnectProfiler);
                settings.DeepProfiling = EditorGUILayout.Toggle(
                    "Deep Profiling",
                    settings.DeepProfiling);
            }

            settings.CleanBuildCache = EditorGUILayout.Toggle(
                "Clean Build Cache",
                settings.CleanBuildCache);

            if (EditorGUI.EndChangeCheck())
            {
                settings.Save();
            }
        }

        private void DrawValidation()
        {
            string[] scenes = GetEnabledScenes();
            if (scenes.Length == 0)
            {
                EditorGUILayout.HelpBox(
                    "No valid enabled scenes in Build Settings.",
                    MessageType.Error);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    $"{scenes.Length} scene(s). First: {scenes[0]}",
                    MessageType.Info);
            }

            BuildTargetGroup group =
                BuildPipeline.GetBuildTargetGroup(
                    settings.Target);
            string identifier =
                PlayerSettings.GetApplicationIdentifier(
                    NamedBuildTarget.FromBuildTargetGroup(group));
            if (string.IsNullOrWhiteSpace(identifier))
            {
                EditorGUILayout.HelpBox(
                    "Application identifier is empty for this target.",
                    MessageType.Warning);
            }
        }

        private void DrawActions()
        {
            EditorGUILayout.Space();
            using (new EditorGUI.DisabledScope(
                GetEnabledScenes().Length == 0))
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("Build", GUILayout.Height(34f)))
                {
                    BuildPlayer(settings, false);
                }

                if (GUILayout.Button(
                    "Build & Run",
                    GUILayout.Height(34f)))
                {
                    BuildPlayer(settings, true);
                }

                EditorGUILayout.EndHorizontal();
            }

            if (!string.IsNullOrEmpty(statusMessage))
            {
                EditorGUILayout.HelpBox(
                    statusMessage,
                    MessageType.None);
            }
        }

        private static void BuildPlayer(
            DreamyBuildSettings buildSettings,
            bool runAfterBuild)
        {
            string[] scenes = GetEnabledScenes();
            if (scenes.Length == 0)
            {
                Debug.LogError(
                    "Dreamy Build: no valid enabled scenes.");
                return;
            }

            BuildTargetGroup targetGroup =
                BuildPipeline.GetBuildTargetGroup(buildSettings.Target);
            if (EditorUserBuildSettings.activeBuildTarget !=
                buildSettings.Target &&
                !EditorUserBuildSettings.SwitchActiveBuildTarget(
                    targetGroup,
                    buildSettings.Target))
            {
                Debug.LogError(
                    $"Dreamy Build: failed to switch to {buildSettings.Target}.");
                return;
            }

            string outputPath = GetBuildOutputPath(buildSettings);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

            BuildPlayerOptions options = new()
            {
                scenes = scenes,
                target = buildSettings.Target,
                locationPathName = outputPath,
                options = CreateBuildOptions(buildSettings, runAfterBuild)
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            if (summary.result == BuildResult.Succeeded)
            {
                Debug.Log(
                    $"Dreamy Build succeeded: {outputPath} " +
                    $"({summary.totalSize} bytes, {summary.totalTime}).");
            }
            else
            {
                Debug.LogError(
                    $"Dreamy Build failed: {summary.result}, " +
                    $"{summary.totalErrors} error(s).");
            }
        }

        private static BuildOptions CreateBuildOptions(
            DreamyBuildSettings buildSettings,
            bool runAfterBuild)
        {
            BuildOptions options = BuildOptions.None;
            if (buildSettings.DevelopmentBuild)
            {
                options |= BuildOptions.Development;
            }

            if (buildSettings.AllowDebugging)
            {
                options |= BuildOptions.AllowDebugging;
            }

            if (buildSettings.ConnectProfiler)
            {
                options |= BuildOptions.ConnectWithProfiler;
            }

            if (buildSettings.DeepProfiling)
            {
                options |= BuildOptions.EnableDeepProfilingSupport;
            }

            if (buildSettings.CleanBuildCache)
            {
                options |= BuildOptions.CleanBuildCache;
            }

            if (runAfterBuild)
            {
                options |= BuildOptions.AutoRunPlayer;
            }

            return options;
        }

        private static string[] GetEnabledScenes()
        {
            return EditorBuildSettings.scenes
                .Where(scene => scene.enabled && File.Exists(scene.path))
                .Select(scene => scene.path)
                .ToArray();
        }

        private static string GetBuildOutputPath(
            DreamyBuildSettings buildSettings)
        {
            string productName = SanitizeFileName(
                string.IsNullOrWhiteSpace(PlayerSettings.productName)
                    ? DefaultProductName
                    : PlayerSettings.productName);
            string targetFolder = buildSettings.Target.ToString();
            string root = Path.Combine(
                GetAbsoluteOutputRoot(buildSettings),
                targetFolder);

            return buildSettings.Target switch
            {
                BuildTarget.StandaloneWindows =>
                    Path.Combine(root, productName + ".exe"),
                BuildTarget.StandaloneWindows64 =>
                    Path.Combine(root, productName + ".exe"),
                BuildTarget.Android =>
                    Path.Combine(
                        root,
                        productName +
                        (EditorUserBuildSettings.buildAppBundle
                            ? ".aab"
                            : ".apk")),
                _ => Path.Combine(root, productName)
            };
        }

        private static string GetAbsoluteOutputRoot(
            DreamyBuildSettings buildSettings)
        {
            string output = string.IsNullOrWhiteSpace(
                buildSettings.OutputDirectory)
                ? "Builds"
                : buildSettings.OutputDirectory;
            return Path.IsPathRooted(output)
                ? output
                : Path.GetFullPath(
                    Path.Combine(
                        Directory.GetCurrentDirectory(),
                        output));
        }

        private static string ToProjectRelativeOrAbsolute(string path)
        {
            string projectRoot = Path.GetFullPath(
                Directory.GetCurrentDirectory())
                .TrimEnd(Path.DirectorySeparatorChar) +
                Path.DirectorySeparatorChar;
            string fullPath = Path.GetFullPath(path);

            return fullPath.StartsWith(
                projectRoot,
                StringComparison.OrdinalIgnoreCase)
                ? fullPath.Substring(projectRoot.Length)
                : fullPath;
        }

        private static string SanitizeFileName(string value)
        {
            foreach (char invalidCharacter in
                     Path.GetInvalidFileNameChars())
            {
                value = value.Replace(invalidCharacter, '_');
            }

            return value;
        }
    }
}
