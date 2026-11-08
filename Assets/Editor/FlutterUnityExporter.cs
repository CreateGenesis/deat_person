using System;
using System.IO;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class FlutterUnityExporter
{
    static FlutterUnityExporter()
    {
        EditorApplication.playModeStateChanged += RestorePreviewScene;
    }

    private static void RestorePreviewScene(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredEditMode
            || !SessionState.GetBool("DataPerson.RestorePreviewScene", false))
        {
            return;
        }
        var previous = SessionState.GetString("DataPerson.PreviousPreviewScene", string.Empty);
        EditorSceneManager.playModeStartScene = string.IsNullOrEmpty(previous)
            ? null : AssetDatabase.LoadAssetAtPath<SceneAsset>(previous);
        SessionState.SetBool("DataPerson.RestorePreviewScene", false);
    }

    [MenuItem("Build/Export Unity Library for Flutter")]
    public static void Export()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            throw new BuildFailedException("Stop Play mode before exporting Unity for Flutter.");
        }
        var root = Directory.GetParent(Application.dataPath)?.FullName
            ?? throw new InvalidOperationException("Cannot resolve Unity project root.");
        var output = Path.Combine(root, "flutter_app", "unity_export");
        var scenes = AndroidApkBuilder.PrepareAndroidBuild();
        var previousExport = EditorUserBuildSettings.exportAsGoogleAndroidProject;
        var previousBundle = EditorUserBuildSettings.buildAppBundle;
        var previousEntry = PlayerSettings.Android.applicationEntry;
        try
        {
            EditorUserBuildSettings.exportAsGoogleAndroidProject = true;
            EditorUserBuildSettings.buildAppBundle = false;
            PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.Activity;
            Directory.CreateDirectory(output);
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = output,
                target = BuildTarget.Android,
                targetGroup = BuildTargetGroup.Android,
                options = BuildOptions.Development | BuildOptions.AllowDebugging
                    | BuildOptions.AcceptExternalModificationsToPlayer,
            });
            File.WriteAllText(Path.Combine(output, "export-summary.txt"),
                $"Result: {report.summary.result}\nUnity: {Application.unityVersion}\n"
                + $"Warnings: {report.summary.totalWarnings}\nErrors: {report.summary.totalErrors}\n"
                + $"Duration: {report.summary.totalTime}\n");
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new BuildFailedException($"Flutter Unity export failed: {report.summary.result}.");
            }
            Debug.Log($"Flutter Unity library exported to {output}");
        }
        finally
        {
            EditorUserBuildSettings.exportAsGoogleAndroidProject = previousExport;
            EditorUserBuildSettings.buildAppBundle = previousBundle;
            PlayerSettings.Android.applicationEntry = previousEntry;
        }
    }

    [MenuItem("Tools/Digital Human/Play Flutter Offline Preview")]
    public static void PlayOfflinePreview()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            throw new InvalidOperationException("Stop Play mode before selecting offline preview.");
        }
        SessionState.SetBool("DataPerson.FlutterPreview", true);
        SessionState.SetString("DataPerson.PreviousPreviewScene",
            AssetDatabase.GetAssetPath(EditorSceneManager.playModeStartScene));
        SessionState.SetBool("DataPerson.RestorePreviewScene", true);
        EditorSceneManager.playModeStartScene = AssetDatabase.LoadAssetAtPath<SceneAsset>("Assets/Scenes/SampleScene.unity");
        EditorApplication.EnterPlaymode();
    }
}
