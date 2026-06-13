using System;
using UnityEditor;
using UnityEngine;

namespace Dreamy.EditorTools.Build
{
    [Serializable]
    public sealed class DreamyBuildSettings
    {
        private const string EditorPrefsKeyPrefix =
            "Dreamy.EditorTools.BuildSettings.";

        public BuildTarget Target = BuildTarget.Android;
        public string OutputDirectory = "Builds";
        public bool DevelopmentBuild;
        public bool AllowDebugging;
        public bool ConnectProfiler;
        public bool DeepProfiling;
        public bool CleanBuildCache;

        public static DreamyBuildSettings Load()
        {
            string key = GetEditorPrefsKey();
            string json = EditorPrefs.GetString(key, string.Empty);
            if (string.IsNullOrEmpty(json))
            {
                return new DreamyBuildSettings();
            }

            try
            {
                return JsonUtility.FromJson<DreamyBuildSettings>(json)
                       ?? new DreamyBuildSettings();
            }
            catch (ArgumentException)
            {
                return new DreamyBuildSettings();
            }
        }

        public void Save()
        {
            EditorPrefs.SetString(
                GetEditorPrefsKey(),
                JsonUtility.ToJson(this));
        }

        private static string GetEditorPrefsKey()
        {
            return EditorPrefsKeyPrefix +
                   Application.dataPath.GetHashCode();
        }
    }
}
