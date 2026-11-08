using System;
using System.IO;
using System.Linq;
using DataPerson.DigitalHuman;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class VrmAvatarSetup
{
    private const string RequestFileName = ".request-vrm-avatar-setup";
    private const string ModelPath =
        "Assets/Models/AIAvatarCandidates/VIPE_2668_Metaverse.prefab";
    private const string AuthoringScenePath = "Assets/Scenes/MegaAuthoring.unity";
    private const string RuntimeScenePath = "Assets/Scenes/SampleScene.unity";

    static VrmAvatarSetup()
    {
        EditorApplication.delayCall += RunWhenRequested;
    }

    [MenuItem("Tools/Digital Human/Configure Placed VRM Avatar")]
    public static void ConfigureFromMenu()
    {
        Configure();
    }

    private static void RunWhenRequested()
    {
        var requestPath = Path.Combine(GetProjectRoot(), RequestFileName);
        if (!File.Exists(requestPath))
        {
            return;
        }
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += RunWhenRequested;
            return;
        }

        try
        {
            Configure();
            File.Delete(requestPath);
        }
        catch (Exception exception)
        {
            Debug.LogError($"VRM avatar configuration failed: {exception.Message}");
            Debug.LogException(exception);
        }
    }

    private static void Configure()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (!model)
        {
            throw new InvalidOperationException($"VRM model is unavailable at {ModelPath}");
        }

        var placement = FindPlacement();
        if (!placement)
        {
            throw new InvalidOperationException(
                $"Place {Path.GetFileNameWithoutExtension(ModelPath)} in {AuthoringScenePath} first.");
        }

        var localPosition = placement.transform.localPosition;
        var localEulerAngles = placement.transform.localEulerAngles;
        var localScale = placement.transform.localScale;
        ConfigureAuthoringPose(placement);
        ConfigureRuntimeScene(model, localPosition, localEulerAngles, localScale);
        AssetDatabase.SaveAssets();
        Debug.Log(
            $"Configured {model.name}: natural standing pose, camera gaze, and VRM lip sync. " +
            $"Runtime placement is {localPosition}, rotation {localEulerAngles}, scale {localScale}.");
    }

    private static GameObject FindPlacement()
    {
        var scene = FindLoadedScene(AuthoringScenePath);
        if (!scene.IsValid())
        {
            scene = EditorSceneManager.OpenScene(AuthoringScenePath, OpenSceneMode.Additive);
        }

        return scene.GetRootGameObjects().FirstOrDefault(root =>
            PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(root) == ModelPath);
    }

    private static void ConfigureAuthoringPose(GameObject placement)
    {
        var pose = placement.GetComponent<AvatarStaticPoseController>();
        if (!pose)
        {
            pose = placement.AddComponent<AvatarStaticPoseController>();
        }
        pose.Initialize(placement.transform);
        pose.ApplyPoseImmediately();
        EditorUtility.SetDirty(pose);
        EditorSceneManager.MarkSceneDirty(placement.scene);
        if (!EditorSceneManager.SaveScene(placement.scene))
        {
            throw new InvalidOperationException($"Failed to save {placement.scene.path}");
        }
    }

    private static void ConfigureRuntimeScene(
        GameObject model,
        Vector3 localPosition,
        Vector3 localEulerAngles,
        Vector3 localScale)
    {
        var scene = FindLoadedScene(RuntimeScenePath);
        var closeWhenFinished = false;
        if (!scene.IsValid())
        {
            scene = EditorSceneManager.OpenScene(RuntimeScenePath, OpenSceneMode.Additive);
            closeWhenFinished = true;
        }

        try
        {
            var bootstrap = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<EasyARCloudBlockBootstrap>(true))
                .SingleOrDefault();
            if (!bootstrap)
            {
                throw new InvalidOperationException(
                    $"{nameof(EasyARCloudBlockBootstrap)} was not found in {RuntimeScenePath}");
            }

            var serializedBootstrap = new SerializedObject(bootstrap);
            serializedBootstrap.FindProperty("digitalHumanPrefab").objectReferenceValue = model;
            serializedBootstrap.FindProperty("digitalHumanAnimatorController").objectReferenceValue = null;
            serializedBootstrap.FindProperty("localPosition").vector3Value = localPosition;
            serializedBootstrap.FindProperty("localEulerAngles").vector3Value = localEulerAngles;
            serializedBootstrap.FindProperty("localScale").vector3Value = localScale;
            serializedBootstrap.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(bootstrap);
            if (!EditorSceneManager.SaveScene(scene))
            {
                throw new InvalidOperationException($"Failed to save {RuntimeScenePath}");
            }
        }
        finally
        {
            if (closeWhenFinished && scene.IsValid() && scene.isLoaded)
            {
                EditorSceneManager.CloseScene(scene, true);
            }
        }
    }

    private static Scene FindLoadedScene(string path)
    {
        for (var index = 0; index < SceneManager.sceneCount; index++)
        {
            var scene = SceneManager.GetSceneAt(index);
            if (scene.path == path)
            {
                return scene;
            }
        }
        return default;
    }

    private static string GetProjectRoot()
    {
        return Directory.GetParent(Application.dataPath)?.FullName
            ?? throw new InvalidOperationException("Unable to resolve the Unity project root.");
    }
}
