using System;
using System.IO;
using System.Linq;
using DataPerson.DigitalHuman;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class RobotExpressivePrototypeSetup
{
    private const string RequestFileName = ".request-digital-human-prototype-build";
    private const string ModelPath =
        "Assets/Models/RobotExpressive_aa2603d917384b68bb4a086f32dabe83.glb";
    private const string ControllerFolder = "Assets/DigitalHuman";
    private const string ControllerPath = ControllerFolder + "/RobotExpressive.controller";
    private const string RuntimeScenePath = "Assets/Scenes/SampleScene.unity";

    [InitializeOnLoadMethod]
    private static void ScheduleRequestedSetup()
    {
        EditorApplication.delayCall += RunWhenRequested;
    }

    [MenuItem("Tools/Digital Human/Configure RobotExpressive Prototype")]
    public static void ConfigureFromMenu()
    {
        ConfigureRuntimeScene();
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
            ConfigureRuntimeScene();
            File.Delete(requestPath);
            Debug.Log("Digital human prototype configuration completed.");
            EditorApplication.delayCall += BuildConfiguredApk;
        }
        catch (Exception exception)
        {
            Debug.LogError($"Digital human prototype configuration failed: {exception.Message}");
            Debug.LogException(exception);
        }
    }

    private static void BuildConfiguredApk()
    {
        try
        {
            Debug.Log("Starting Digital Human prototype Android APK build.");
            AndroidApkBuilder.BuildApk();
            Debug.Log("Digital Human prototype Android APK build succeeded.");
        }
        catch (Exception exception)
        {
            Debug.LogError($"Digital Human prototype Android APK build failed: {exception.Message}");
            Debug.LogException(exception);
        }
    }

    private static void ConfigureRuntimeScene()
    {
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        if (!model)
        {
            throw new InvalidOperationException($"Robot model is unavailable at {ModelPath}");
        }

        var controller = CreateOrUpdateAnimatorController();
        var runtimeScene = SceneManager.GetSceneByPath(RuntimeScenePath);
        var wasLoaded = runtimeScene.IsValid() && runtimeScene.isLoaded;
        if (!wasLoaded)
        {
            runtimeScene = EditorSceneManager.OpenScene(RuntimeScenePath, OpenSceneMode.Additive);
        }

        try
        {
            var bootstrap = runtimeScene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<EasyARCloudBlockBootstrap>(true))
                .SingleOrDefault();
            if (!bootstrap)
            {
                throw new InvalidOperationException(
                    $"{nameof(EasyARCloudBlockBootstrap)} was not found in {RuntimeScenePath}");
            }

            var serializedBootstrap = new SerializedObject(bootstrap);
            var prefabProperty = serializedBootstrap.FindProperty("digitalHumanPrefab");
            var controllerProperty = serializedBootstrap.FindProperty("digitalHumanAnimatorController");
            var controlsProperty = serializedBootstrap.FindProperty("enableDevelopmentControls");
            if (prefabProperty == null || controllerProperty == null || controlsProperty == null)
            {
                throw new InvalidOperationException(
                    "Digital human fields were not found on EasyARCloudBlockBootstrap.");
            }

            prefabProperty.objectReferenceValue = model;
            controllerProperty.objectReferenceValue = controller;
            controlsProperty.boolValue = true;
            serializedBootstrap.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(bootstrap);
            if (!EditorSceneManager.SaveScene(runtimeScene))
            {
                throw new InvalidOperationException($"Failed to save {RuntimeScenePath}");
            }
        }
        finally
        {
            if (!wasLoaded && runtimeScene.IsValid() && runtimeScene.isLoaded)
            {
                EditorSceneManager.CloseScene(runtimeScene, true);
            }
        }
    }

    private static AnimatorController CreateOrUpdateAnimatorController()
    {
        EnsureFolder(ControllerFolder);
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (!controller)
        {
            controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        }

        var clips = AssetDatabase.LoadAllAssetsAtPath(ModelPath)
            .OfType<AnimationClip>()
            .Where(clip => clip && !clip.name.StartsWith("__preview__", StringComparison.Ordinal))
            .ToArray();
        var idleClip = FindClip(clips, "Idle")
            ?? throw new InvalidOperationException("RobotExpressive Idle animation was not imported.");

        var stateMachine = controller.layers[0].stateMachine;
        var idleState = ConfigureState(stateMachine, "Idle", idleClip);
        ConfigureState(stateMachine, "Listening", idleClip);
        ConfigureState(stateMachine, "Thinking", idleClip);
        ConfigureState(stateMachine, "Speaking", idleClip);
        ConfigureState(stateMachine, "Error", idleClip);
        ConfigureState(stateMachine, "Wave", FindClip(clips, "Wave") ?? idleClip);
        ConfigureState(stateMachine, "Yes", FindClip(clips, "Yes") ?? idleClip);
        ConfigureState(stateMachine, "No", FindClip(clips, "No") ?? idleClip);
        ConfigureState(stateMachine, "ThumbsUp", FindClip(clips, "ThumbsUp") ?? idleClip);
        stateMachine.defaultState = idleState;

        EditorUtility.SetDirty(stateMachine);
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return controller;
    }

    private static AnimatorState ConfigureState(
        AnimatorStateMachine stateMachine,
        string stateName,
        Motion motion)
    {
        var state = stateMachine.states
            .Select(childState => childState.state)
            .FirstOrDefault(candidate => candidate.name == stateName)
            ?? stateMachine.AddState(stateName);
        state.motion = motion;
        EditorUtility.SetDirty(state);
        return state;
    }

    private static AnimationClip FindClip(AnimationClip[] clips, string clipName)
    {
        return clips.FirstOrDefault(clip =>
            string.Equals(clip.name, clipName, StringComparison.OrdinalIgnoreCase));
    }

    private static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path))
        {
            return;
        }

        var parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
        var name = Path.GetFileName(path);
        if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name))
        {
            throw new InvalidOperationException($"Invalid asset folder path: {path}");
        }
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, name);
    }

    private static string GetProjectRoot()
    {
        return Directory.GetParent(Application.dataPath)?.FullName
            ?? throw new InvalidOperationException("Unable to resolve the Unity project root.");
    }
}
