using UnityEngine;
using UnityEditor;
using GlimmerDiary.Flora;

public class PlantAssetCreator
{
    [MenuItem("GlimmerDiary/Create Plant Assets")]
    public static void CreateAssets()
    {
        CreatePlantAsset("Baobab", PlantPresets.ApplyBaobabPreset);
        CreatePlantAsset("Acacia", PlantPresets.ApplyAcaciaPreset);
        CreatePlantAsset("Shrub", PlantPresets.ApplyShrubPreset);
    }

    private static void CreatePlantAsset(string name, System.Action<PlantDefinition> presetAction)
    {
        // 创建一个新的 ScriptableObject
        PlantDefinition asset = ScriptableObject.CreateInstance<PlantDefinition>();
        
        // 应用你的预设代码
        presetAction(asset);

        // 保存到项目文件夹
        string path = $"Assets/GlimmerDiary/Flora/Resources/Plants/{name}.asset";
        
        // 确保文件夹存在
        System.IO.Directory.CreateDirectory("Assets/GlimmerDiary/Flora/Resources/Plants");

        AssetDatabase.CreateAsset(asset, path);
        AssetDatabase.SaveAssets();
        
        Debug.Log($"Created Plant Asset: {path}");
    }
}