using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class VrmCandidateImporter
{
    private static readonly string[] ModelPaths =
    {
        "Assets/Models/AIAvatarCandidates/VIPE_503_Headphones.vrm",
        "Assets/Models/AIAvatarCandidates/VIPE_824_Braid.vrm",
        "Assets/Models/AIAvatarCandidates/VIPE_1531_Anime.vrm",
        "Assets/Models/AIAvatarCandidates/VIPE_2668_Metaverse.vrm",
    };

    static VrmCandidateImporter()
    {
        EditorApplication.delayCall += ImportMissingCandidates;
    }

    [MenuItem("Tools/Digital Human/Reimport VRM Candidates")]
    public static void ImportFromMenu()
    {
        ImportMissingCandidates();
    }

    private static void ImportMissingCandidates()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += ImportMissingCandidates;
            return;
        }

        var missingModels = ModelPaths
            .Where(path => !AssetDatabase.LoadAssetAtPath<GameObject>(Path.ChangeExtension(path, ".prefab")))
            .ToArray();
        if (missingModels.Length == 0)
        {
            return;
        }

        foreach (var modelPath in missingModels)
        {
            AssetDatabase.ImportAsset(
                modelPath,
                ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
        }

        Debug.Log($"Requested UniVRM import for {missingModels.Length} candidate models.");
    }
}
