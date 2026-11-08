using System;
using System.Text;
using UnityEngine;
using VRM;

namespace DataPerson.DigitalHuman
{
    [DefaultExecutionOrder(110)]
    [DisallowMultipleComponent]
    public sealed class AudioLipSyncController : MonoBehaviour
    {
        private static readonly BlendShapeKey VrmMouthKey =
            BlendShapeKey.CreateFromPreset(BlendShapePreset.A);

        private static readonly string[] MouthBlendShapeCandidates =
        {
            "jawopen",
            "mouthopen",
            "openmouth",
            "visemeaa",
            "vrcvaa",
            "fclmtha",
            "moutha",
            "aa",
        };

        [Header("Mouth target")]
        [SerializeField] private SkinnedMeshRenderer faceRenderer;
        [SerializeField] private string mouthBlendShapeName;
        [SerializeField] private Transform jaw;
        [SerializeField] private Vector3 jawOpenEulerAngles = new(12f, 0f, 0f);

        [Header("Audio response")]
        [SerializeField, Range(0f, 0.05f)] private float silenceThreshold = 0.004f;
        [SerializeField, Range(0.01f, 0.3f)] private float speakingThreshold = 0.08f;
        [SerializeField, Range(0f, 100f)] private float maximumBlendShapeWeight = 85f;
        [SerializeField, Min(0.1f)] private float attackSpeed = 18f;
        [SerializeField, Min(0.1f)] private float releaseSpeed = 10f;

        private readonly float[] outputSamples = new float[256];
        private AudioSource speechSource;
        private VRMBlendShapeProxy vrmBlendShapeProxy;
        private bool useVrmMouth;
        private int mouthBlendShapeIndex = -1;
        private Quaternion jawClosedLocalRotation;
        private bool hasJawClosedRotation;
        private float currentOpenness;

        public void Initialize(Transform avatarRoot, AudioSource audioSource)
        {
            ResetMouth();
            speechSource = audioSource;
            vrmBlendShapeProxy = avatarRoot
                ? avatarRoot.GetComponentInChildren<VRMBlendShapeProxy>(true)
                : null;
            useVrmMouth = HasVrmPreset(vrmBlendShapeProxy, BlendShapePreset.A);
            mouthBlendShapeIndex = useVrmMouth ? -1 : ResolveBlendShape(avatarRoot);
            hasJawClosedRotation = false;
            if (!useVrmMouth && mouthBlendShapeIndex < 0)
            {
                jaw = jaw ? jaw : FindJawBone(avatarRoot);
                if (jaw)
                {
                    jawClosedLocalRotation = jaw.localRotation;
                    hasJawClosedRotation = true;
                }
            }

            enabled = speechSource && (useVrmMouth || mouthBlendShapeIndex >= 0 || jaw);
            if (!enabled)
            {
                Debug.LogWarning(
                    "Digital human mouth target was not found. Assign a mouth BlendShape or Jaw bone to AudioLipSyncController.",
                    this);
                return;
            }

            if (useVrmMouth)
            {
                Debug.Log("[LipSync] Using VRM 'A' expression.", this);
            }
            else if (mouthBlendShapeIndex >= 0)
            {
                Debug.Log(
                    $"[LipSync] Using '{faceRenderer.sharedMesh.GetBlendShapeName(mouthBlendShapeIndex)}' on '{faceRenderer.name}'.",
                    this);
            }
            else
            {
                Debug.Log($"[LipSync] Using Jaw bone '{jaw.name}'.", this);
            }
        }

        private void LateUpdate()
        {
            var targetOpenness = speechSource && speechSource.isPlaying
                ? MeasureOpenness()
                : 0f;
            var speed = targetOpenness > currentOpenness ? attackSpeed : releaseSpeed;
            currentOpenness = Mathf.MoveTowards(
                currentOpenness,
                targetOpenness,
                speed * Time.deltaTime);
            ApplyMouth(currentOpenness);
        }

        private void OnDisable()
        {
            ResetMouth();
        }

        private float MeasureOpenness()
        {
            speechSource.GetOutputData(outputSamples, 0);
            var squareSum = 0f;
            foreach (var sample in outputSamples)
            {
                squareSum += sample * sample;
            }

            var rootMeanSquare = Mathf.Sqrt(squareSum / outputSamples.Length);
            var upperThreshold = Mathf.Max(speakingThreshold, silenceThreshold + 0.001f);
            return Mathf.SmoothStep(
                0f,
                1f,
                Mathf.InverseLerp(silenceThreshold, upperThreshold, rootMeanSquare));
        }

        private void ApplyMouth(float openness)
        {
            if (useVrmMouth && vrmBlendShapeProxy)
            {
                vrmBlendShapeProxy.ImmediatelySetValue(
                    VrmMouthKey,
                    openness * maximumBlendShapeWeight / 100f);
                return;
            }

            if (mouthBlendShapeIndex >= 0 && faceRenderer)
            {
                faceRenderer.SetBlendShapeWeight(
                    mouthBlendShapeIndex,
                    openness * maximumBlendShapeWeight);
                return;
            }

            if (jaw && hasJawClosedRotation)
            {
                jaw.localRotation = jawClosedLocalRotation
                    * Quaternion.Euler(jawOpenEulerAngles * openness);
            }
        }

        private void ResetMouth()
        {
            currentOpenness = 0f;
            ApplyMouth(0f);
        }

        private int ResolveBlendShape(Transform avatarRoot)
        {
            if (!avatarRoot)
            {
                return -1;
            }

            var renderers = faceRenderer
                ? new[] { faceRenderer }
                : avatarRoot.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            if (!string.IsNullOrWhiteSpace(mouthBlendShapeName))
            {
                foreach (var renderer in renderers)
                {
                    var index = FindNamedBlendShape(renderer, mouthBlendShapeName);
                    if (index < 0)
                    {
                        continue;
                    }

                    faceRenderer = renderer;
                    return index;
                }
            }

            var bestScore = -1;
            var bestIndex = -1;
            SkinnedMeshRenderer bestRenderer = null;
            foreach (var renderer in renderers)
            {
                if (!renderer || !renderer.sharedMesh)
                {
                    continue;
                }

                for (var index = 0; index < renderer.sharedMesh.blendShapeCount; index++)
                {
                    var score = ScoreBlendShape(renderer.sharedMesh.GetBlendShapeName(index));
                    if (score <= bestScore)
                    {
                        continue;
                    }

                    bestScore = score;
                    bestIndex = index;
                    bestRenderer = renderer;
                }
            }

            if (bestIndex >= 0)
            {
                faceRenderer = bestRenderer;
                mouthBlendShapeName = bestRenderer.sharedMesh.GetBlendShapeName(bestIndex);
            }
            return bestIndex;
        }

        private static int FindNamedBlendShape(SkinnedMeshRenderer renderer, string requestedName)
        {
            if (!renderer || !renderer.sharedMesh)
            {
                return -1;
            }

            var normalizedRequestedName = NormalizeName(requestedName);
            for (var index = 0; index < renderer.sharedMesh.blendShapeCount; index++)
            {
                var normalizedName = NormalizeName(renderer.sharedMesh.GetBlendShapeName(index));
                if (normalizedName == normalizedRequestedName
                    || (normalizedRequestedName.Length >= 5
                        && normalizedName.EndsWith(normalizedRequestedName, StringComparison.Ordinal)))
                {
                    return index;
                }
            }
            return -1;
        }

        private static int ScoreBlendShape(string blendShapeName)
        {
            var normalizedName = NormalizeName(blendShapeName);
            for (var index = 0; index < MouthBlendShapeCandidates.Length; index++)
            {
                var candidate = MouthBlendShapeCandidates[index];
                if (normalizedName == candidate)
                {
                    return 1000 - index;
                }
                if (candidate.Length >= 5
                    && normalizedName.EndsWith(candidate, StringComparison.Ordinal))
                {
                    return 500 - index;
                }
            }
            return -1;
        }

        private static Transform FindJawBone(Transform avatarRoot)
        {
            if (!avatarRoot)
            {
                return null;
            }

            var animator = avatarRoot.GetComponentInChildren<Animator>(true);
            if (animator && animator.avatar && animator.avatar.isHuman)
            {
                var humanoidJaw = animator.GetBoneTransform(HumanBodyBones.Jaw);
                if (humanoidJaw)
                {
                    return humanoidJaw;
                }
            }

            foreach (var child in avatarRoot.GetComponentsInChildren<Transform>(true))
            {
                var normalizedName = NormalizeName(child.name);
                if (normalizedName == "jaw" || normalizedName == "mandible")
                {
                    return child;
                }
            }
            return null;
        }

        private static bool HasVrmPreset(
            VRMBlendShapeProxy proxy,
            BlendShapePreset preset)
        {
            if (!proxy || !proxy.BlendShapeAvatar || proxy.BlendShapeAvatar.Clips == null)
            {
                return false;
            }

            foreach (var clip in proxy.BlendShapeAvatar.Clips)
            {
                if (clip && clip.Preset == preset)
                {
                    return true;
                }
            }
            return false;
        }

        private static string NormalizeName(string value)
        {
            var builder = new StringBuilder(value?.Length ?? 0);
            if (value == null)
            {
                return builder.ToString();
            }

            foreach (var character in value)
            {
                if (char.IsLetterOrDigit(character))
                {
                    builder.Append(char.ToLowerInvariant(character));
                }
            }
            return builder.ToString();
        }
    }
}
