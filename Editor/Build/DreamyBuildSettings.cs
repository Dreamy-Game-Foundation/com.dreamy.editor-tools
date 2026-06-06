using UnityEditor;

namespace Dreamy.EditorTools.Build
{
    [FilePath(
        "ProjectSettings/DreamyBuildSettings.asset",
        FilePathAttribute.Location.ProjectFolder)]
    public sealed class DreamyBuildSettings
        : ScriptableSingleton<DreamyBuildSettings>
    {
        public BuildTarget Target = BuildTarget.Android;
        public string OutputDirectory = "Builds";
        public bool DevelopmentBuild;
        public bool AllowDebugging;
        public bool ConnectProfiler;
        public bool DeepProfiling;
        public bool CleanBuildCache;

        public void SaveSettings()
        {
            Save(true);
        }
    }
}
