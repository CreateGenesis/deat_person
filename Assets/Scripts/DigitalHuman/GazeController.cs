using System;
using UnityEngine;
using VRM;

namespace DataPerson.DigitalHuman
{
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    public sealed class GazeController : MonoBehaviour
    {
        private static readonly BlendShapeKey LookUpKey =
            BlendShapeKey.CreateFromPreset(BlendShapePreset.LookUp);
        private static readonly BlendShapeKey LookDownKey =
            BlendShapeKey.CreateFromPreset(BlendShapePreset.LookDown);
        private static readonly BlendShapeKey LookLeftKey =
            BlendShapeKey.CreateFromPreset(BlendShapePreset.LookLeft);
        private static readonly BlendShapeKey LookRightKey =
            BlendShapeKey.CreateFromPreset(BlendShapePreset.LookRight);

        [SerializeField] private Transform head;
        [SerializeField, Range(0f, 90f)] private float maxYawDegrees = 20f;
        [SerializeField, Range(0f, 60f)] private float maxPitchDegrees = 12f;
        [SerializeField, Min(1f)] private float rotationSpeedDegrees = 120f;

        [Header("VRM eye look")]
        [SerializeField, Range(1f, 90f)] private float maxEyeYawDegrees = 35f;
        [SerializeField, Range(1f, 60f)] private float maxEyePitchDegrees = 20f;
        [SerializeField, Min(1f)] private float eyeSpeedDegrees = 180f;

        [Header("Natural gaze motion")]
        [SerializeField, Range(0f, 3f)] private float microYawDegrees = 0.8f;
        [SerializeField, Range(0f, 2f)] private float microPitchDegrees = 0.4f;
        [SerializeField, Range(0f, 3f)] private float speakingNodDegrees = 0.55f;

        private Transform avatarRoot;
        private Transform gazeTarget;
        private Quaternion referenceLocalRotation;
        private float currentYaw;
        private float currentPitch;
        private float currentEyeYaw;
        private float currentEyePitch;
        private VRMBlendShapeProxy vrmBlendShapeProxy;
        private AvatarStateMachine stateMachine;
        private float motionPhase;
        private bool useVrmEyeLook;
        private bool isTracking;

        public void Initialize(Transform root, Transform target)
        {
            ResetEyeLook();
            avatarRoot = root;
            gazeTarget = target;
            stateMachine = root ? root.GetComponent<AvatarStateMachine>() : null;
            var seed = root ? root.GetInstanceID() : GetInstanceID();
            motionPhase = Mathf.Abs(seed % 991) / 991f * Mathf.PI * 2f;
            vrmBlendShapeProxy = root
                ? root.GetComponentInChildren<VRMBlendShapeProxy>(true)
                : null;
            useVrmEyeLook = HasVrmEyeExpressions(vrmBlendShapeProxy);
            var importedLookAt = root
                ? root.GetComponentInChildren<VRMLookAtHead>(true)
                : null;
            if (importedLookAt)
            {
                importedLookAt.UpdateType = VRM.UpdateType.None;
            }
            head = head ? head : FindHeadBone(root);
            if (!head)
            {
                Debug.LogWarning("Digital human Head bone was not found; gaze is disabled.", this);
                enabled = false;
                return;
            }

            referenceLocalRotation = head.localRotation;
            enabled = true;
        }

        public void SetTracking(bool value)
        {
            isTracking = value;
        }

        private void LateUpdate()
        {
            if (!head || !avatarRoot)
            {
                return;
            }

            var targetYaw = 0f;
            var targetPitch = 0f;
            if (isTracking && gazeTarget)
            {
                var direction = gazeTarget.position - head.position;
                var localDirection = avatarRoot.InverseTransformDirection(direction.normalized);
                targetYaw = Mathf.Clamp(
                    Mathf.Atan2(localDirection.x, localDirection.z) * Mathf.Rad2Deg,
                    -maxYawDegrees,
                    maxYawDegrees);
                var horizontal = Mathf.Sqrt(
                    localDirection.x * localDirection.x + localDirection.z * localDirection.z);
                targetPitch = Mathf.Clamp(
                    -Mathf.Atan2(localDirection.y, horizontal) * Mathf.Rad2Deg,
                    -maxEyePitchDegrees,
                    maxEyePitchDegrees);

                var time = Time.time + motionPhase;
                targetYaw += Mathf.Sin(time * 0.61f) * microYawDegrees
                    + Mathf.Sin(time * 1.13f + 1.2f) * microYawDegrees * 0.22f;
                targetPitch += Mathf.Sin(time * 0.73f + 0.8f) * microPitchDegrees;
                if (stateMachine
                    && stateMachine.ConversationState == AvatarConversationState.Speaking)
                {
                    targetPitch += Mathf.Sin(time * 1.9f) * speakingNodDegrees;
                }
            }

            var step = rotationSpeedDegrees * Time.deltaTime;
            currentYaw = Mathf.MoveTowardsAngle(
                currentYaw,
                Mathf.Clamp(targetYaw, -maxYawDegrees, maxYawDegrees),
                step);
            currentPitch = Mathf.MoveTowardsAngle(
                currentPitch,
                Mathf.Clamp(targetPitch, -maxPitchDegrees, maxPitchDegrees),
                step);
            var eyeStep = eyeSpeedDegrees * Time.deltaTime;
            currentEyeYaw = Mathf.MoveTowardsAngle(
                currentEyeYaw,
                Mathf.Clamp(targetYaw, -maxEyeYawDegrees, maxEyeYawDegrees),
                eyeStep);
            currentEyePitch = Mathf.MoveTowardsAngle(
                currentEyePitch,
                Mathf.Clamp(targetPitch, -maxEyePitchDegrees, maxEyePitchDegrees),
                eyeStep);
            head.localRotation = referenceLocalRotation
                * Quaternion.Euler(currentPitch, currentYaw, 0f);
            ApplyEyeLook();
        }

        private void OnDisable()
        {
            if (head)
            {
                head.localRotation = referenceLocalRotation;
            }
            ResetEyeLook();
        }

        private void ApplyEyeLook()
        {
            if (!useVrmEyeLook || !vrmBlendShapeProxy)
            {
                return;
            }

            vrmBlendShapeProxy.ImmediatelySetValue(
                LookLeftKey,
                Mathf.Clamp01(-currentEyeYaw / maxEyeYawDegrees));
            vrmBlendShapeProxy.ImmediatelySetValue(
                LookRightKey,
                Mathf.Clamp01(currentEyeYaw / maxEyeYawDegrees));
            vrmBlendShapeProxy.ImmediatelySetValue(
                LookUpKey,
                Mathf.Clamp01(-currentEyePitch / maxEyePitchDegrees));
            vrmBlendShapeProxy.ImmediatelySetValue(
                LookDownKey,
                Mathf.Clamp01(currentEyePitch / maxEyePitchDegrees));
        }

        private void ResetEyeLook()
        {
            currentEyeYaw = 0f;
            currentEyePitch = 0f;
            ApplyEyeLook();
        }

        private static Transform FindHeadBone(Transform root)
        {
            var animator = root.GetComponentInChildren<Animator>(true);
            if (animator && animator.avatar && animator.avatar.isHuman)
            {
                var humanoidHead = animator.GetBoneTransform(HumanBodyBones.Head);
                if (humanoidHead)
                {
                    return humanoidHead;
                }
            }

            foreach (var renderer in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                foreach (var bone in renderer.bones)
                {
                    if (bone && IsHeadBoneName(bone.name))
                    {
                        return bone;
                    }
                }
            }

            foreach (var child in root.GetComponentsInChildren<Transform>(true))
            {
                if (IsHeadBoneName(child.name))
                {
                    return child;
                }
            }
            return null;
        }

        private static bool IsHeadBoneName(string boneName)
        {
            return string.Equals(boneName, "Head", StringComparison.OrdinalIgnoreCase)
                || boneName.EndsWith(":Head", StringComparison.OrdinalIgnoreCase)
                || boneName.EndsWith("_Head", StringComparison.OrdinalIgnoreCase)
                || boneName.EndsWith(".Head", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasVrmEyeExpressions(VRMBlendShapeProxy proxy)
        {
            return HasVrmPreset(proxy, BlendShapePreset.LookUp)
                && HasVrmPreset(proxy, BlendShapePreset.LookDown)
                && HasVrmPreset(proxy, BlendShapePreset.LookLeft)
                && HasVrmPreset(proxy, BlendShapePreset.LookRight);
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
    }
}
