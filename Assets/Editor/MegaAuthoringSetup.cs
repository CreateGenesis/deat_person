using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class MegaAuthoringSetup
{
    private const string RequestFile = ".request-mega-authoring-setup";
    private const string ScenePath = "Assets/Scenes/MegaAuthoring.unity";
    private const string ExpectedToolName = "EasyAR.Mega.Annotation";

    private static readonly string[] AnnotationToolMenuPaths =
    {
        "GameObject/EasyAR Mega/Tool/Annotation Tool (Edit Mode)",
        "EasyAR/Mega/Annotation Tool (Edit Mode)",
    };

    static MegaAuthoringSetup()
    {
        EditorApplication.delayCall += SetupWhenRequested;
    }

    [MenuItem("EasyAR/Project/Setup Mega Authoring Scene")]
    public static void SetupFromMenu()
    {
        Setup();
    }

    private static void SetupWhenRequested()
    {
        var requestPath = Path.Combine(GetProjectRoot(), RequestFile);
        if (!File.Exists(requestPath))
        {
            return;
        }

        File.Delete(requestPath);
        Setup();
    }

    private static void Setup()
    {
        var originalScene = SceneManager.GetActiveScene();
        var originalSelection = Selection.activeObject;
        Scene authoringScene = default;
        var closeWhenFinished = false;

        try
        {
            authoringScene = FindLoadedScene(ScenePath);
            if (!authoringScene.IsValid())
            {
                authoringScene = File.Exists(Path.Combine(GetProjectRoot(), ScenePath))
                    ? EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive)
                    : EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
                closeWhenFinished = true;
            }

            SceneManager.SetActiveScene(authoringScene);
            Selection.activeObject = null;

            var tool = authoringScene.GetRootGameObjects()
                .FirstOrDefault(gameObject => gameObject.name == ExpectedToolName);
            if (!tool)
            {
                var creationSource = AnnotationToolMenuPaths
                    .FirstOrDefault(EditorApplication.ExecuteMenuItem);
                if (string.IsNullOrEmpty(creationSource) && TryCreateAnnotationToolFromPackage())
                {
                    creationSource = "EasyAR Mega Studio package method";
                }

                if (string.IsNullOrEmpty(creationSource))
                {
                    throw new InvalidOperationException(
                        "EasyAR Mega Annotation Tool menu was not registered. " +
                        "Confirm that com.easyar.sense, com.easyar.mega, and " +
                        "com.easyar.mega.studio are installed and compiled.");
                }

                tool = authoringScene.GetRootGameObjects()
                    .FirstOrDefault(gameObject => gameObject.name == ExpectedToolName);
                if (!tool)
                {
                    throw new InvalidOperationException(
                        $"EasyAR creation source '{creationSource}' ran, but '{ExpectedToolName}' " +
                        "was not created in the authoring scene.");
                }
            }

            EditorSceneManager.MarkSceneDirty(authoringScene);
            if (!EditorSceneManager.SaveScene(authoringScene, ScenePath))
            {
                throw new IOException($"Failed to save '{ScenePath}'.");
            }

            AssetDatabase.SaveAssets();
            Debug.Log(
                $"EasyAR Mega authoring setup completed: {ScenePath}. " +
                "The scene is editor-only and was not added to Build Settings.");
        }
        catch (Exception exception)
        {
            Debug.LogException(exception);
            throw;
        }
        finally
        {
            if (originalScene.IsValid() && originalScene.isLoaded)
            {
                SceneManager.SetActiveScene(originalScene);
            }
            Selection.activeObject = originalSelection;

            if (closeWhenFinished && authoringScene.IsValid() && authoringScene.isLoaded)
            {
                EditorSceneManager.CloseScene(authoringScene, true);
            }
        }
    }

    private static bool TryCreateAnnotationToolFromPackage()
    {
        var studioEditorAssembly = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(assembly =>
                assembly.GetName().Name == "EasyAR.Mega.Studio.Tool.Editor");
        var studioToolEditor = studioEditorAssembly?
            .GetType("EasyAR.Mega.Studio.StudioToolEditor");
        var annotationToolMenu = studioToolEditor?.GetMethod(
            "AnnotationToolMenu",
            BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
        if (annotationToolMenu == null)
        {
            return false;
        }

        try
        {
            annotationToolMenu.Invoke(null, null);
            return true;
        }
        catch (TargetInvocationException exception) when (exception.InnerException != null)
        {
            throw exception.InnerException;
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
