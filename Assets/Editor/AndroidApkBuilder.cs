using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using easyar;

[InitializeOnLoad]
public static class AndroidApkBuilder
{
    private const string BuildRequestFile = ".request-android-apk-build";
    private const string OutputDirectory = "Builds";
    private const string OutputFileName = "EasyARCloudBlockTest-debug.apk";
    private static readonly string[] RendererDataPaths =
    {
        "Assets/Settings/Mobile_Renderer.asset",
        "Assets/Settings/PC_Renderer.asset",
    };
    private static readonly string[] RuntimeShaderNames =
    {
        "Universal Render Pipeline/Lit",
        "Universal Render Pipeline/Unlit",
    };

    static AndroidApkBuilder()
    {
        EditorApplication.delayCall += BuildWhenRequested;
    }

    [MenuItem("Build/EasyAR Cloud Block Test APK")]
    public static void BuildFromMenu()
    {
        BuildApk();
    }

    private static void BuildWhenRequested()
    {
        var requestPath = Path.Combine(GetProjectRoot(), BuildRequestFile);
        if (!File.Exists(requestPath))
        {
            return;
        }

        File.Delete(requestPath);
        BuildApk();
    }

    public static void BuildApk()
    {
        var enabledScenes = PrepareAndroidBuild();

        EditorUserBuildSettings.buildAppBundle = false;
        EditorUserBuildSettings.development = true;
        EditorUserBuildSettings.allowDebugging = true;

        var outputDirectory = Path.Combine(GetProjectRoot(), OutputDirectory);
        Directory.CreateDirectory(outputDirectory);
        var outputPath = Path.Combine(outputDirectory, OutputFileName);
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
        {
            scenes = enabledScenes,
            locationPathName = outputPath,
            target = BuildTarget.Android,
            targetGroup = BuildTargetGroup.Android,
            options = BuildOptions.Development | BuildOptions.AllowDebugging,
        });

        var summary = report.summary;
        var summaryText = string.Join(Environment.NewLine, new[]
        {
            $"Result: {summary.result}",
            $"Output: {outputPath}",
            $"Size: {summary.totalSize}",
            $"Warnings: {summary.totalWarnings}",
            $"Errors: {summary.totalErrors}",
            $"Duration: {summary.totalTime}",
        });
        File.WriteAllText(Path.Combine(outputDirectory, "android-build-summary.txt"), summaryText);
        Debug.Log(summaryText);

        if (summary.result != BuildResult.Succeeded)
        {
            throw new BuildFailedException($"Android APK build failed with result {summary.result}.");
        }
    }

    public static string[] PrepareAndroidBuild()
    {
        EnsureEasyARSettings();
        EnsureEasyARRendererFeatures();
        EnsureRuntimeShaders();

        var enabledScenes = EditorBuildSettings.scenes
            .Where(scene => scene.enabled)
            .Select(scene => scene.path)
            .ToArray();
        if (enabledScenes.Length == 0)
        {
            throw new BuildFailedException("No enabled scenes were found in Build Settings.");
        }

        PlayerSettings.companyName = "Data Person";
        PlayerSettings.productName = "EasyAR Cloud Block Test";
        PlayerSettings.bundleVersion = "0.1.0";
        PlayerSettings.SetApplicationIdentifier(NamedBuildTarget.Android, "com.example.virtual_agent");
        PlayerSettings.Android.bundleVersionCode = 1;
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel25;
        PlayerSettings.Android.targetSdkVersion = AndroidSdkVersions.AndroidApiLevelAuto;
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;

        ValidateEasyARLicense();
        return enabledScenes;
    }

    private static string GetProjectRoot()
    {
        return Directory.GetParent(Application.dataPath)?.FullName
            ?? throw new InvalidOperationException("Unable to resolve the Unity project root.");
    }

    private static void EnsureEasyARSettings()
    {
        var settings = AssetDatabase.FindAssets($"t:{nameof(EasyARSettings)}")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<EasyARSettings>)
            .SingleOrDefault();

        if (!settings)
        {
            EnsureAssetFolder("Assets/XR");
            EnsureAssetFolder("Assets/XR/Settings");
            settings = ScriptableObject.CreateInstance<EasyARSettings>();
            AssetDatabase.CreateAsset(settings, "Assets/XR/Settings/EasyAR Settings.asset");
            AssetDatabase.SaveAssets();
        }

        EditorBuildSettings.AddConfigObject("EasyAR.Settings", settings, true);
    }

    private static void EnsureAssetFolder(string path)
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
        AssetDatabase.CreateFolder(parent, name);
    }

    private static void EnsureEasyARRendererFeatures()
    {
        foreach (var path in RendererDataPaths)
        {
            var rendererData = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(path);
            if (!rendererData)
            {
                throw new BuildFailedException($"URP renderer data was not found at '{path}'.");
            }
            if (rendererData.rendererFeatures.Any(feature =>
                    feature is EasyARCameraImageRendererFeature))
            {
                continue;
            }

            var serializedRenderer = new SerializedObject(rendererData);
            serializedRenderer.Update();

            var feature = ScriptableObject.CreateInstance<EasyARCameraImageRendererFeature>();
            feature.name = nameof(EasyARCameraImageRendererFeature);
            feature.SetActive(true);
            AssetDatabase.AddObjectToAsset(feature, rendererData);
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out _, out long localId))
            {
                UnityEngine.Object.DestroyImmediate(feature, true);
                throw new BuildFailedException(
                    $"Failed to save {nameof(EasyARCameraImageRendererFeature)} in '{path}'.");
            }

            var features = serializedRenderer.FindProperty("m_RendererFeatures");
            var featureMap = serializedRenderer.FindProperty("m_RendererFeatureMap");
            features.arraySize++;
            features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
            featureMap.arraySize++;
            featureMap.GetArrayElementAtIndex(featureMap.arraySize - 1).longValue = localId;
            serializedRenderer.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(feature);
            EditorUtility.SetDirty(rendererData);
        }

        AssetDatabase.SaveAssets();
    }

    private static void EnsureRuntimeShaders()
    {
        var preloadedAssets = PlayerSettings.GetPreloadedAssets().Where(asset => asset).ToList();
        foreach (var shaderName in RuntimeShaderNames)
        {
            var shader = Shader.Find(shaderName);
            if (!shader)
            {
                throw new BuildFailedException($"Required runtime shader '{shaderName}' was not found.");
            }
            if (!preloadedAssets.Contains(shader))
            {
                preloadedAssets.Add(shader);
            }
        }
        PlayerSettings.SetPreloadedAssets(preloadedAssets.ToArray());
    }

    private static void ValidateEasyARLicense()
    {
        var settings = AssetDatabase.FindAssets($"t:{nameof(EasyARSettings)}")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<EasyARSettings>)
            .Single();
        if (string.IsNullOrWhiteSpace(settings.LicenseKey))
        {
            throw new BuildFailedException("EasyAR Sense License Key is empty.");
        }

        var engineAssembly = typeof(EasyARSettings).Assembly;
        var engineType = engineAssembly.GetType("easyar.Engine", true);
        var osType = engineAssembly.GetType("easyar.EngineOperatingSystem", true);
        var android = Enum.Parse(osType, "Android");
        var variantMethod = engineType.GetMethod(
            "variant",
            BindingFlags.Public | BindingFlags.Static);
        var variant = (string)variantMethod.Invoke(null, null);
        var matchMethod = engineType.GetMethod(
            "isLicenseKeyMatched",
            BindingFlags.Public | BindingFlags.Static);
        var packageName = PlayerSettings.GetApplicationIdentifier(NamedBuildTarget.Android);
        var matched = (bool)matchMethod.Invoke(
            null,
            new[] { settings.LicenseKey.Trim(), packageName, variant, android });
        if (!matched)
        {
            throw new BuildFailedException(
                $"EasyAR Sense License does not match Android package '{packageName}' and variant '{variant}'.");
        }
    }
}
