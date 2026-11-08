using UnityEngine;
using easyar;
using DataPerson.DigitalHuman;
using DataPerson.DigitalHuman.Occlusion;
using DataPerson.Host;

// Runtime bootstrap for the configured cloud Block.

/// <summary>
/// Creates a Mega Block session and binds an optional content prefab to the
/// cloud-localized Block transform.
/// </summary>
public sealed class EasyARCloudBlockBootstrap : MonoBehaviour
{
    [Header("EasyAR cloud credentials")]
    [SerializeField] private string appId = "eb38ffefe3b3452ea9c910fd266a2d92";
    [SerializeField] private string serverAddress = "https://clsv3-api.easyar.com";
    [SerializeField] private string apiKey = "6b3931c317f6ed25c442598efcc80d2a";
    [SerializeField] private string apiSecret = "f1083291c9ab3ff76b04fad364758ad2d3f534f6ff17fbffd933490dc1b3715e";

    [Header("Cloud Block")]
    [SerializeField] private string blockId = "a78cd496-3106-4c1d-8788-8c4dec13acdd";
    [SerializeField] private string blockName = "Cloud Block";

    [Header("Localized content")]
    [SerializeField] private GameObject digitalHumanPrefab;
    [SerializeField] private Vector3 localPosition = new(0f, 0.15f, 0f);
    [SerializeField] private Vector3 localEulerAngles;
    [SerializeField] private Vector3 localScale = Vector3.one;
    [SerializeField] private RuntimeAnimatorController digitalHumanAnimatorController;
    [SerializeField] private bool enableDevelopmentControls = true;

    [Header("M3 foreground occlusion")]
    [SerializeField] private bool foregroundOcclusionEnabled = true;
    [SerializeField, Range(1f, 30f)] private float foregroundInferenceFps = 15f;
    [SerializeField, Range(0f, 1f)] private float foregroundMaskThreshold = 0.55f;
    [SerializeField, Range(0f, 4f)] private float foregroundMaskDilationPixels = 1f;
    [SerializeField, Range(0f, 1f)] private float foregroundMaskAttack = 0.85f;
    [SerializeField, Range(0f, 1f)] private float foregroundMaskRelease = 0.35f;
    [SerializeField] private bool flipForegroundMaskVertically;

    [Header("Fallback test cube")]
    [SerializeField, Min(0.01f)] private float testCubeSizeMeters = 0.3f;
    [SerializeField] private Color testCubeColor = new(0.1f, 0.85f, 0.25f, 1f);

    private ARSession session;
    private MegaTrackerFrameFilter tracker;
    private MegaBlockController block;
    private GameObject localizedContentInstance;
    private DigitalHumanController digitalHumanController;
    private ForegroundOcclusionController foregroundOcclusionController;
    private Material testCubeMaterial;
    private int previewScreenWidth;
    private int previewScreenHeight;

    public bool PreviewMode { get; private set; }
    public string LocalizationStatus { get; private set; } = "Locating";
    public DigitalHumanController DigitalHuman => digitalHumanController;
    public bool SessionPaused { get; private set; }

    private void Awake()
    {
        ConfigureARCamera();

        PreviewMode = UnityFlutterBridge.PreviewRequested;
        if (PreviewMode)
        {
            var previewCamera = Camera.main;
            if (previewCamera)
            {
                previewCamera.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                previewCamera.backgroundColor = new Color(0.08f, 0.10f, 0.16f);
            }
            HandleTargetFound();
            FramePreview();
            return;
        }

        if (string.IsNullOrWhiteSpace(blockId))
        {
            Debug.LogError("EasyAR Block ID is empty.");
            enabled = false;
            return;
        }
        if (string.IsNullOrWhiteSpace(appId)
            || string.IsNullOrWhiteSpace(serverAddress)
            || string.IsNullOrWhiteSpace(apiKey)
            || string.IsNullOrWhiteSpace(apiSecret))
        {
            Debug.LogError("EasyAR Mega service authentication is incomplete.");
            enabled = false;
            return;
        }

        var sessionObject = ARSessionFactory.CreateSession(ARSessionFactory.ARSessionPreset.MegaBlock_MotionTracking_Inertial);
        session = sessionObject.GetComponent<ARSession>();
        tracker = sessionObject.GetComponentInChildren<MegaTrackerFrameFilter>(true);
        if (!session || !tracker)
        {
            Debug.LogError("Failed to create the EasyAR Mega Block session.");
            enabled = false;
            return;
        }

        if (foregroundOcclusionEnabled)
        {
            var cameraImageRenderer = sessionObject.GetComponent<CameraImageRenderer>();
            foregroundOcclusionController = gameObject.AddComponent<ForegroundOcclusionController>();
            foregroundOcclusionController.Initialize(
                cameraImageRenderer,
                Camera.main,
                foregroundInferenceFps,
                foregroundMaskThreshold,
                foregroundMaskDilationPixels,
                foregroundMaskAttack,
                foregroundMaskRelease,
                flipForegroundMaskVertically);
        }

        tracker.ServiceType = MegaApiType.Block;
        tracker.ServiceAccessSource = MegaServiceAccessSourceType.APIKey;
        tracker.LocationInputMode = Application.isEditor ? MegaLocationInputMode.Simulator : MegaLocationInputMode.Onsite;
        tracker.ServiceAccessData = new APIKeyAccessData
        {
            AppID = appId.Trim(),
            ServerAddress = serverAddress.Trim(),
            APIKey = apiKey.Trim(),
            APISecret = apiSecret.Trim(),
        };

        block = ARSessionFactory.CreateController<MegaBlockController>().GetComponent<MegaBlockController>();
        block.Source = new MegaBlockController.BlockInfoSourceData(blockId.Trim(), blockName.Trim());
        block.Tracker = tracker;
        block.TargetFound += HandleTargetFound;
        block.TargetLost += HandleTargetLost;
        tracker.LocalizationRespond += HandleLocalizationResponse;
    }

    private static void ConfigureARCamera()
    {
        var camera = Camera.main;
        if (!camera)
        {
            return;
        }

        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        camera.nearClipPlane = 0.01f;
    }

    private void OnDestroy()
    {
        if (foregroundOcclusionController)
        {
            foregroundOcclusionController.Shutdown();
        }
        if (block)
        {
            block.TargetFound -= HandleTargetFound;
            block.TargetLost -= HandleTargetLost;
        }
        if (tracker)
        {
            tracker.LocalizationRespond -= HandleLocalizationResponse;
        }
        if (session)
        {
            session.StopSession(false);
        }
        if (testCubeMaterial)
        {
            Destroy(testCubeMaterial);
        }
    }

    private void HandleTargetFound()
    {
        LocalizationStatus = PreviewMode ? "PreviewFound" : "Found";
        if (!localizedContentInstance)
        {
            localizedContentInstance = digitalHumanPrefab
                ? Instantiate(digitalHumanPrefab, PreviewMode ? transform : block.transform, false)
                : CreateTestCube();
            localizedContentInstance.transform.localPosition = PreviewMode
                ? new Vector3(0f, -0.7f, 2.4f)
                : localPosition;
            localizedContentInstance.transform.localRotation = Quaternion.Euler(localEulerAngles);
            if (digitalHumanPrefab)
            {
                localizedContentInstance.transform.localScale = localScale;
                digitalHumanController = localizedContentInstance
                    .GetComponent<DigitalHumanController>();
                if (!digitalHumanController)
                {
                    digitalHumanController = localizedContentInstance
                        .AddComponent<DigitalHumanController>();
                }
                digitalHumanController.Initialize(
                    Camera.main,
                    digitalHumanAnimatorController,
                    enableDevelopmentControls && !UnityFlutterBridge.IsHosted);
            }
        }
        localizedContentInstance.SetActive(true);
        if (digitalHumanController)
        {
            digitalHumanController.SetLocalizationFound(true);
        }
        Debug.Log(PreviewMode ? "Offline avatar preview ready; no localization or occlusion is being tested."
            : $"EasyAR Block found: {block.Info?.ID} ({block.Info?.Name})");
    }

    private void HandleTargetLost()
    {
        LocalizationStatus = PreviewMode ? "PreviewLost" : "Lost";
        if (digitalHumanController)
        {
            digitalHumanController.SetLocalizationFound(false);
        }
        if (localizedContentInstance)
        {
            localizedContentInstance.SetActive(false);
        }
        Debug.Log("EasyAR Block lost; localized content hidden.");
    }

    private void HandleLocalizationResponse(MegaLocalizationResponse response)
    {
        Debug.Log($"EasyAR Mega localization: {response.Status}");
    }

    private GameObject CreateTestCube()
    {
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = $"Localization Test Cube ({testCubeSizeMeters:0.##}m)";
        cube.transform.SetParent(PreviewMode ? transform : block.transform, false);
        cube.transform.localScale = Vector3.one * testCubeSizeMeters;

        var shader = Shader.Find("Universal Render Pipeline/Unlit")
            ?? Shader.Find("Unlit/Color")
            ?? Shader.Find("Standard");
        if (shader)
        {
            testCubeMaterial = new Material(shader)
            {
                color = testCubeColor,
            };
            cube.GetComponent<Renderer>().material = testCubeMaterial;
        }

        Debug.Log($"Created {testCubeSizeMeters:0.##}m EasyAR localization test cube.");
        return cube;
    }

    public void SetPreviewTracking(bool found)
    {
        if (!PreviewMode)
        {
            throw new System.InvalidOperationException("Simulated tracking is only available in preview mode.");
        }
        if (found)
        {
            HandleTargetFound();
        }
        else
        {
            HandleTargetLost();
        }
    }

    private void Update()
    {
        if (PreviewMode && (previewScreenWidth != Screen.width || previewScreenHeight != Screen.height))
        {
            FramePreview();
        }
    }

    private void FramePreview()
    {
        var previewCamera = Camera.main;
        if (!previewCamera || !localizedContentInstance)
        {
            return;
        }
        var renderers = localizedContentInstance.GetComponentsInChildren<Renderer>(true);
        if (renderers.Length == 0)
        {
            return;
        }
        var bounds = renderers[0].bounds;
        foreach (var contentRenderer in renderers)
        {
            bounds.Encapsulate(contentRenderer.bounds);
        }
        var scale = UnityFlutterBridge.InterfaceScale;
        var bottomInset = 180f * scale;
        var availableHeight = Mathf.Max(Screen.height * 0.25f, Screen.height - 308f * scale);
        previewCamera.rect = new Rect(0f, bottomInset / Screen.height, 1f, availableHeight / Screen.height);
        previewCamera.aspect = Screen.width / availableHeight;
        var tangent = Mathf.Tan(previewCamera.fieldOfView * Mathf.Deg2Rad * 0.5f);
        var distance = Mathf.Max(bounds.extents.y / tangent,
            bounds.extents.x / (tangent * previewCamera.aspect)) * 1.3f + bounds.extents.z;
        previewCamera.transform.SetPositionAndRotation(
            bounds.center - Vector3.forward * Mathf.Max(distance, 1f), Quaternion.identity);
        previewScreenWidth = Screen.width;
        previewScreenHeight = Screen.height;
    }

    public void SetSessionPaused(bool paused)
    {
        if (SessionPaused == paused)
        {
            return;
        }
        SessionPaused = paused;
        if (foregroundOcclusionController)
        {
            foregroundOcclusionController.enabled = !paused;
        }
        if (session)
        {
            if (paused)
            {
                session.StopSession(false);
                HandleTargetLost();
            }
            else
            {
                LocalizationStatus = "Locating";
                session.StartSession();
            }
        }
    }
}
