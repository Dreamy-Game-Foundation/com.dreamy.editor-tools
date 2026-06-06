using System.IO;
using UnityEditor;
using UnityEngine;

namespace Dreamy.EditorTools
{
    public static class DreamyScriptTemplateMenu
    {
        private const string SaveDataTemplateName = "NewSaveData.cs";
        private const string GameServiceTemplateName = "NewGameService.cs";

        [MenuItem("Assets/Create/Dreamy/Script/Save Data", priority = 81)]
        public static void CreateSaveDataScript()
        {
            CreateScript(SaveDataTemplateName, SaveDataTemplate);
        }

        [MenuItem("Assets/Create/Dreamy/Script/Game Service", priority = 82)]
        public static void CreateGameServiceScript()
        {
            CreateScript(GameServiceTemplateName, GameServiceTemplate);
        }

        private static void CreateScript(string fileName, string content)
        {
            string folder = GetSelectedFolder();
            string path = AssetDatabase.GenerateUniqueAssetPath(Path.Combine(folder, fileName).Replace('\\', '/'));
            File.WriteAllText(path, content);
            AssetDatabase.ImportAsset(path);
            Object asset = AssetDatabase.LoadAssetAtPath<Object>(path);
            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        private static string GetSelectedFolder()
        {
            Object selected = Selection.activeObject;
            if (selected == null)
            {
                return "Assets";
            }

            string path = AssetDatabase.GetAssetPath(selected);
            if (string.IsNullOrEmpty(path))
            {
                return "Assets";
            }

            return Directory.Exists(path) ? path : Path.GetDirectoryName(path)?.Replace('\\', '/') ?? "Assets";
        }

        private const string SaveDataTemplate =
@"using System;
using Dreamy.Datasave;

[Serializable]
public sealed class NewSaveData : SaveData
{
    public int Value;
}
";

        private const string GameServiceTemplate =
@"public interface INewGameService
{
    void Initialize();
}

public sealed class NewGameService : INewGameService
{
    public void Initialize()
    {
    }
}
";
    }
}
