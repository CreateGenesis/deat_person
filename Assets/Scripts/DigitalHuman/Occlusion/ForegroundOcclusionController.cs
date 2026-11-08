using System;
using easyar;
using Unity.InferenceEngine;
using UnityEngine;
using UnityEngine.Rendering;

namespace DataPerson.DigitalHuman.Occlusion
{
    /// <summary>
    /// Converts the EasyAR camera image into a foreground mask and renders that
    /// mask as near-plane depth so real people can occlude virtual content.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ForegroundOcclusionController : MonoBehaviour
    {
        private const string ModelResourcePath = "AI/Models/selfie_segmenter";
        private const string FilterShaderResourcePath = "Shaders/ForegroundMaskFilter";
        private const string OccluderShaderResourcePath = "Shaders/ForegroundOccluder";

        private static readonly int PreviousMaskId = Shader.PropertyToID("_PreviousMask");
        private static readonly int DilationPixelsId = Shader.PropertyToID("_DilationPixels");
        private static readonly int AttackId = Shader.PropertyToID("_Attack");
        private static readonly int ReleaseId = Shader.PropertyToID("_Release");
        private static readonly int MaskTextureId = Shader.PropertyToID("_MaskTex");
        private static readonly int ThresholdId = Shader.PropertyToID("_Threshold");
        private static readonly int FlipYId = Shader.PropertyToID("_FlipY");

        private CameraImageRenderer cameraImageRenderer;
        private Camera targetCamera;
        private RenderTexture cameraTexture;
        private RenderTexture rawMask;
        private RenderTexture filteredMaskA;
        private RenderTexture filteredMaskB;
        private Material filterMaterial;
        private Material occluderMaterial;
        private Mesh occluderMesh;
        private GameObject occluderObject;
        private MeshRenderer occluderRenderer;
        private Model runtimeModel;
        private Worker worker;
        private Tensor<float> inputTensor;
        private TextureTransform inputTransform;
        private TextureTransform outputTransform;
        private float inferenceInterval;
        private float nextInferenceTime;
        private float threshold;
        private float dilationPixels;
        private float attack;
        private float release;
        private bool flipY;
        private bool initialized;
        private bool targetTextureRequested;
        private bool usingCpuBackend;
        private bool shuttingDown;

        public bool Initialize(
            CameraImageRenderer imageRenderer,
            Camera camera,
            float inferenceFps,
            float maskThreshold,
            float maskDilationPixels,
            float temporalAttack,
            float temporalRelease,
            bool flipMaskVertically)
        {
            Shutdown();
            shuttingDown = false;

            cameraImageRenderer = imageRenderer;
            targetCamera = camera;
            inferenceInterval = 1f / Mathf.Clamp(inferenceFps, 1f, 30f);
            threshold = Mathf.Clamp01(maskThreshold);
            dilationPixels = Mathf.Clamp(maskDilationPixels, 0f, 4f);
            attack = Mathf.Clamp01(temporalAttack);
            release = Mathf.Clamp01(temporalRelease);
            flipY = flipMaskVertically;

            if (!cameraImageRenderer)
            {
                return FailInitialization("EasyAR CameraImageRenderer was not found.");
            }
            if (!targetCamera)
            {
                return FailInitialization("The AR camera was not found.");
            }

            try
            {
                var modelAsset = Resources.Load<ModelAsset>(ModelResourcePath);
                if (!modelAsset)
                {
                    return FailInitialization(
                        $"Foreground segmentation model was not imported at Resources/{ModelResourcePath}.");
                }

                runtimeModel = ModelLoader.Load(modelAsset);
                if (runtimeModel.inputs.Count != 1 || !runtimeModel.inputs[0].shape.IsStatic())
                {
                    return FailInitialization("The foreground model must have one static input tensor.");
                }

                var inputShape = runtimeModel.inputs[0].shape.ToTensorShape();
                if (inputShape.rank != 4
                    || inputShape[0] != 1
                    || inputShape[1] <= 0
                    || inputShape[2] <= 0
                    || inputShape[3] != 3)
                {
                    return FailInitialization(
                        $"Expected NHWC foreground input [1,H,W,3], received {inputShape}.");
                }
                if (runtimeModel.outputs.Count < 1)
                {
                    return FailInitialization("The foreground model does not expose an output tensor.");
                }

                var filterShader = Resources.Load<Shader>(FilterShaderResourcePath);
                var occluderShader = Resources.Load<Shader>(OccluderShaderResourcePath);
                if (!filterShader || !occluderShader)
                {
                    return FailInitialization("Foreground occlusion shaders could not be loaded from Resources.");
                }

                inputTensor = new Tensor<float>(inputShape);
                inputTransform = new TextureTransform().SetTensorLayout(TensorLayout.NHWC);
                outputTransform = new TextureTransform()
                    .SetTensorLayout(TensorLayout.NHWC)
                    .SetBroadcastChannels(true);

                filterMaterial = new Material(filterShader)
                {
                    name = "Foreground Mask Filter (Runtime)",
                    hideFlags = HideFlags.HideAndDontSave,
                };
                occluderMaterial = new Material(occluderShader)
                {
                    name = "Foreground Depth Occluder (Runtime)",
                    hideFlags = HideFlags.HideAndDontSave,
                };
                occluderMaterial.SetFloat(ThresholdId, threshold);
                occluderMaterial.SetFloat(FlipYId, flipY ? 1f : 0f);

                CreateMaskTextures(inputShape[2], inputShape[1]);
                CreateBestAvailableWorker();
                CreateOccluder(targetCamera);

                initialized = true;
                nextInferenceTime = 0f;
                RequestCameraTexture();
                Debug.Log(
                    $"M3 foreground occlusion initialized: input={inputShape}, " +
                    $"backend={(usingCpuBackend ? "CPU" : "GPUCompute")}, fps={1f / inferenceInterval:0.#}.");
                return true;
            }
            catch (Exception exception)
            {
                Debug.LogException(exception, this);
                return FailInitialization("Foreground occlusion initialization failed.");
            }
        }

        public void Shutdown()
        {
            if (shuttingDown)
            {
                return;
            }

            shuttingDown = true;
            initialized = false;
            ReleaseCameraTexture();
            cameraTexture = null;

            inputTensor?.Dispose();
            inputTensor = null;
            worker?.Dispose();
            worker = null;
            runtimeModel = null;

            ReleaseRenderTexture(ref rawMask);
            ReleaseRenderTexture(ref filteredMaskA);
            ReleaseRenderTexture(ref filteredMaskB);
            DestroyRuntimeObject(filterMaterial);
            DestroyRuntimeObject(occluderMaterial);
            DestroyRuntimeObject(occluderMesh);
            DestroyRuntimeObject(occluderObject);
            filterMaterial = null;
            occluderMaterial = null;
            occluderMesh = null;
            occluderObject = null;
            occluderRenderer = null;
            cameraImageRenderer = null;
            targetCamera = null;
        }

        private void Update()
        {
            if (!initialized || !cameraTexture || Time.unscaledTime < nextInferenceTime)
            {
                return;
            }

            nextInferenceTime = Time.unscaledTime + inferenceInterval;
            try
            {
                RunInference();
            }
            catch (Exception exception)
            {
                Debug.LogError("M3 foreground inference failed; occlusion has been disabled.", this);
                Debug.LogException(exception, this);
                Shutdown();
                enabled = false;
            }
        }

        private void OnEnable()
        {
            if (!initialized)
            {
                return;
            }

            shuttingDown = false;
            RequestCameraTexture();
            if (occluderRenderer && occluderMaterial && occluderMaterial.GetTexture(MaskTextureId))
            {
                occluderRenderer.enabled = true;
            }
        }

        private void OnDisable()
        {
            ReleaseCameraTexture();
            if (occluderRenderer)
            {
                occluderRenderer.enabled = false;
            }
        }

        private void OnDestroy()
        {
            Shutdown();
        }

        private void OnApplicationPause(bool pauseStatus)
        {
            if (!initialized)
            {
                return;
            }

            if (pauseStatus)
            {
                ReleaseCameraTexture();
                if (occluderRenderer)
                {
                    occluderRenderer.enabled = false;
                }
            }
            else if (isActiveAndEnabled)
            {
                RequestCameraTexture();
            }
        }

        private void RunInference()
        {
            TextureConverter.ToTensor(cameraTexture, inputTensor, inputTransform);

            try
            {
                worker.Schedule(inputTensor);
            }
            catch (Exception gpuException) when (!usingCpuBackend)
            {
                Debug.LogWarning(
                    $"GPU foreground inference was unavailable; retrying on CPU. {gpuException.Message}",
                    this);
                CreateWorker(BackendType.CPU);
                worker.Schedule(inputTensor);
            }

            if (worker.PeekOutput() is not Tensor<float> output || output.shape.rank != 4)
            {
                throw new InvalidOperationException("Foreground model output must be a rank-4 float tensor.");
            }

            TextureConverter.RenderToTexture(output, rawMask, outputTransform);

            filterMaterial.SetTexture(PreviousMaskId, filteredMaskA);
            filterMaterial.SetFloat(DilationPixelsId, dilationPixels);
            filterMaterial.SetFloat(AttackId, attack);
            filterMaterial.SetFloat(ReleaseId, release);
            Graphics.Blit(rawMask, filteredMaskB, filterMaterial);

            (filteredMaskA, filteredMaskB) = (filteredMaskB, filteredMaskA);
            occluderMaterial.SetTexture(MaskTextureId, filteredMaskA);
            occluderRenderer.enabled = true;
        }

        private void CreateWorker(BackendType backendType)
        {
            worker?.Dispose();
            worker = new Worker(runtimeModel, backendType);
            usingCpuBackend = backendType == BackendType.CPU;
        }

        private void CreateBestAvailableWorker()
        {
            if (SystemInfo.supportsComputeShaders)
            {
                try
                {
                    CreateWorker(BackendType.GPUCompute);
                    return;
                }
                catch (Exception exception)
                {
                    Debug.LogWarning(
                        $"GPU foreground worker could not be created; using CPU. {exception.Message}",
                        this);
                }
            }

            CreateWorker(BackendType.CPU);
        }

        private void CreateMaskTextures(int width, int height)
        {
            var format = SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RFloat)
                ? RenderTextureFormat.RFloat
                : RenderTextureFormat.ARGBFloat;

            rawMask = CreateMaskTexture(width, height, format, "Foreground Raw Mask", true);
            filteredMaskA = CreateMaskTexture(width, height, format, "Foreground Filtered Mask A", false);
            filteredMaskB = CreateMaskTexture(width, height, format, "Foreground Filtered Mask B", false);
            ClearRenderTexture(rawMask);
            ClearRenderTexture(filteredMaskA);
            ClearRenderTexture(filteredMaskB);
        }

        private static RenderTexture CreateMaskTexture(
            int width,
            int height,
            RenderTextureFormat format,
            string textureName,
            bool randomWrite)
        {
            var texture = new RenderTexture(width, height, 0, format, RenderTextureReadWrite.Linear)
            {
                name = textureName,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                enableRandomWrite = randomWrite,
                useMipMap = false,
                autoGenerateMips = false,
                hideFlags = HideFlags.HideAndDontSave,
            };
            texture.Create();
            return texture;
        }

        private void CreateOccluder(Camera camera)
        {
            occluderObject = new GameObject("Foreground Depth Occluder")
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            occluderObject.transform.SetParent(camera.transform, false);
            occluderObject.transform.localPosition = Vector3.forward * Mathf.Max(camera.nearClipPlane * 2f, 0.02f);

            var meshFilter = occluderObject.AddComponent<MeshFilter>();
            occluderRenderer = occluderObject.AddComponent<MeshRenderer>();

            occluderMesh = new Mesh
            {
                name = "Foreground Fullscreen Quad (Runtime)",
                hideFlags = HideFlags.HideAndDontSave,
                vertices = new[]
                {
                    new Vector3(-1f, -1f, 0f),
                    new Vector3(-1f, 1f, 0f),
                    new Vector3(1f, 1f, 0f),
                    new Vector3(1f, -1f, 0f),
                },
                uv = new[]
                {
                    new Vector2(0f, 0f),
                    new Vector2(0f, 1f),
                    new Vector2(1f, 1f),
                    new Vector2(1f, 0f),
                },
                triangles = new[] { 0, 1, 2, 0, 2, 3 },
                bounds = new Bounds(Vector3.zero, Vector3.one * 100000f),
            };
            occluderMesh.UploadMeshData(true);

            meshFilter.sharedMesh = occluderMesh;
            occluderRenderer.sharedMaterial = occluderMaterial;
            occluderRenderer.shadowCastingMode = ShadowCastingMode.Off;
            occluderRenderer.receiveShadows = false;
            occluderRenderer.lightProbeUsage = LightProbeUsage.Off;
            occluderRenderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            occluderRenderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            occluderRenderer.allowOcclusionWhenDynamic = false;
            occluderRenderer.enabled = false;
        }

        private void RequestCameraTexture()
        {
            if (targetTextureRequested || !cameraImageRenderer)
            {
                return;
            }

            cameraImageRenderer.RequestTargetTexture(HandleTargetTextureChanged);
            targetTextureRequested = true;
        }

        private void ReleaseCameraTexture()
        {
            if (!targetTextureRequested || !cameraImageRenderer)
            {
                targetTextureRequested = false;
                return;
            }

            cameraImageRenderer.DropTargetTexture(HandleTargetTextureChanged);
            targetTextureRequested = false;
            cameraTexture = null;
        }

        private void HandleTargetTextureChanged(Camera camera, RenderTexture texture)
        {
            cameraTexture = texture;
            if (!texture && occluderRenderer)
            {
                occluderRenderer.enabled = false;
            }

            if (camera && camera != targetCamera)
            {
                targetCamera = camera;
                if (occluderObject)
                {
                    occluderObject.transform.SetParent(camera.transform, false);
                    occluderObject.transform.localPosition =
                        Vector3.forward * Mathf.Max(camera.nearClipPlane * 2f, 0.02f);
                }
            }
        }

        private bool FailInitialization(string message)
        {
            Debug.LogError($"M3 foreground occlusion disabled: {message}", this);
            Shutdown();
            enabled = false;
            return false;
        }

        private static void ClearRenderTexture(RenderTexture texture)
        {
            var previous = RenderTexture.active;
            RenderTexture.active = texture;
            GL.Clear(false, true, Color.clear);
            RenderTexture.active = previous;
        }

        private static void ReleaseRenderTexture(ref RenderTexture texture)
        {
            if (!texture)
            {
                return;
            }

            texture.Release();
            DestroyRuntimeObject(texture);
            texture = null;
        }

        private static void DestroyRuntimeObject(UnityEngine.Object target)
        {
            if (!target)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(target);
            }
            else
            {
                DestroyImmediate(target);
            }
        }
    }
}
