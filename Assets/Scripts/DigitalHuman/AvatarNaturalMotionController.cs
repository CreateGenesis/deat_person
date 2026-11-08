using System;
using UnityEngine;
using VRM;

namespace DataPerson.DigitalHuman
{
    [DefaultExecutionOrder(75)]
    [DisallowMultipleComponent]
    public sealed class AvatarNaturalMotionController : MonoBehaviour
    {
        private static readonly BlendShapeKey BlinkKey =
            BlendShapeKey.CreateFromPreset(BlendShapePreset.Blink);

        [Header("Body motion")]
        [SerializeField, Range(0.05f, 0.5f)] private float breathingCyclesPerSecond = 0.2f;
        [SerializeField, Range(0f, 3f)] private float chestBreathingDegrees = 0.9f;
        [SerializeField, Range(0f, 3f)] private float bodySwayDegrees = 0.65f;
        [SerializeField, Range(0f, 4f)] private float armMotionDegrees = 1.1f;
        [SerializeField, Range(0f, 5f)] private float speakingGestureDegrees = 1.8f;
        [SerializeField, Min(0.05f)] private float stateBlendSeconds = 0.4f;

        [Header("Blink")]
        [SerializeField, Range(1f, 8f)] private float minimumBlinkInterval = 2.3f;
        [SerializeField, Range(1f, 10f)] private float maximumBlinkInterval = 5.2f;
        [SerializeField, Range(0.08f, 0.35f)] private float minimumBlinkDuration = 0.12f;
        [SerializeField, Range(0.08f, 0.35f)] private float maximumBlinkDuration = 0.19f;

        private AvatarStateMachine stateMachine;
        private VRMBlendShapeProxy vrmBlendShapeProxy;
        private BonePose spine;
        private BonePose chest;
        private BonePose upperChest;
        private BonePose neck;
        private BonePose leftShoulder;
        private BonePose rightShoulder;
        private BonePose leftUpperArm;
        private BonePose rightUpperArm;
        private BonePose leftLowerArm;
        private BonePose rightLowerArm;
        private BonePose leftHand;
        private BonePose rightHand;
        private System.Random blinkRandom;
        private float motionPhase;
        private float motionEnergy = 0.7f;
        private float motionEnergyVelocity;
        private float speakingAmount;
        private float speakingAmountVelocity;
        private float nextBlinkTime;
        private float blinkStartedAt = -1f;
        private float blinkDuration;
        private bool useVrmBlink;
        private bool initialized;

        public void Initialize(Transform avatarRoot, AvatarStateMachine avatarStateMachine)
        {
            ResetPose();
            ResetBlink();

            stateMachine = avatarStateMachine;
            var animator = avatarRoot
                ? avatarRoot.GetComponentInChildren<Animator>(true)
                : null;
            if (animator && animator.avatar && animator.avatar.isHuman)
            {
                spine = Bind(animator, HumanBodyBones.Spine);
                chest = Bind(animator, HumanBodyBones.Chest);
                upperChest = Bind(animator, HumanBodyBones.UpperChest);
                neck = Bind(animator, HumanBodyBones.Neck);
                leftShoulder = Bind(animator, HumanBodyBones.LeftShoulder);
                rightShoulder = Bind(animator, HumanBodyBones.RightShoulder);
                leftUpperArm = Bind(animator, HumanBodyBones.LeftUpperArm);
                rightUpperArm = Bind(animator, HumanBodyBones.RightUpperArm);
                leftLowerArm = Bind(animator, HumanBodyBones.LeftLowerArm);
                rightLowerArm = Bind(animator, HumanBodyBones.RightLowerArm);
                leftHand = Bind(animator, HumanBodyBones.LeftHand);
                rightHand = Bind(animator, HumanBodyBones.RightHand);
            }

            vrmBlendShapeProxy = avatarRoot
                ? avatarRoot.GetComponentInChildren<VRMBlendShapeProxy>(true)
                : null;
            useVrmBlink = HasVrmPreset(vrmBlendShapeProxy, BlendShapePreset.Blink);
            var seed = avatarRoot ? avatarRoot.GetInstanceID() : GetInstanceID();
            blinkRandom = new System.Random(seed);
            motionPhase = Mathf.Abs(seed % 997) / 997f * Mathf.PI * 2f;
            ScheduleNextBlink(Time.unscaledTime);

            initialized = spine.IsValid
                || chest.IsValid
                || upperChest.IsValid
                || neck.IsValid
                || leftUpperArm.IsValid
                || rightUpperArm.IsValid
                || useVrmBlink;
            enabled = initialized;
            if (!initialized)
            {
                Debug.LogWarning(
                    "Digital human natural motion requires a humanoid rig or VRM Blink expression.",
                    this);
            }
        }

        private void LateUpdate()
        {
            if (!initialized)
            {
                return;
            }

            var conversationState = stateMachine
                ? stateMachine.ConversationState
                : AvatarConversationState.Idle;
            motionEnergy = Mathf.SmoothDamp(
                motionEnergy,
                GetTargetEnergy(conversationState),
                ref motionEnergyVelocity,
                stateBlendSeconds);
            speakingAmount = Mathf.SmoothDamp(
                speakingAmount,
                conversationState == AvatarConversationState.Speaking ? 1f : 0f,
                ref speakingAmountVelocity,
                stateBlendSeconds);

            var time = Time.time + motionPhase;
            var breath = Mathf.Sin(time * breathingCyclesPerSecond * Mathf.PI * 2f);
            var slowSway = Mathf.Sin(time * 0.62f);
            var secondarySway = Mathf.Sin(time * 0.91f + 1.7f);
            var speechPulse = speakingAmount
                * (Mathf.Sin(time * 1.75f) + 0.35f * Mathf.Sin(time * 2.65f + 0.8f));
            var attentivePitch = conversationState switch
            {
                AvatarConversationState.Listening => -0.35f,
                AvatarConversationState.Thinking => 0.2f,
                AvatarConversationState.Error => 0.25f,
                _ => 0f,
            };

            spine.Apply(new Vector3(
                attentivePitch * 0.35f + breath * chestBreathingDegrees * 0.25f,
                slowSway * bodySwayDegrees * 0.35f * motionEnergy,
                secondarySway * bodySwayDegrees * 0.3f * motionEnergy));
            chest.Apply(new Vector3(
                attentivePitch * 0.65f + breath * chestBreathingDegrees * 0.5f,
                slowSway * bodySwayDegrees * 0.55f * motionEnergy,
                secondarySway * bodySwayDegrees * 0.45f * motionEnergy));
            upperChest.Apply(new Vector3(
                breath * chestBreathingDegrees * 0.3f,
                slowSway * bodySwayDegrees * 0.3f * motionEnergy,
                secondarySway * bodySwayDegrees * 0.25f * motionEnergy));
            neck.Apply(new Vector3(
                speechPulse * 0.2f,
                slowSway * bodySwayDegrees * 0.18f,
                secondarySway * bodySwayDegrees * 0.16f));

            var shoulderLift = breath * chestBreathingDegrees * 0.2f;
            leftShoulder.Apply(new Vector3(0f, slowSway * 0.18f, shoulderLift));
            rightShoulder.Apply(new Vector3(0f, -slowSway * 0.18f, -shoulderLift));

            var idleArm = armMotionDegrees * motionEnergy;
            var leftArmWave = Mathf.Sin(time * 0.7f + 0.3f) * idleArm
                + speechPulse * speakingGestureDegrees;
            var rightArmWave = Mathf.Sin(time * 0.67f + 2.1f) * idleArm
                - speechPulse * speakingGestureDegrees * 0.75f;
            leftUpperArm.Apply(new Vector3(leftArmWave * 0.3f, 0f, leftArmWave));
            rightUpperArm.Apply(new Vector3(rightArmWave * 0.3f, 0f, rightArmWave));
            leftLowerArm.Apply(new Vector3(0f, leftArmWave * 0.35f, leftArmWave * 0.2f));
            rightLowerArm.Apply(new Vector3(0f, rightArmWave * 0.35f, rightArmWave * 0.2f));
            leftHand.Apply(new Vector3(0f, leftArmWave * 0.22f, secondarySway * 0.25f));
            rightHand.Apply(new Vector3(0f, rightArmWave * 0.22f, -secondarySway * 0.25f));

            UpdateBlink(Time.unscaledTime, conversationState);
        }

        private void UpdateBlink(float now, AvatarConversationState conversationState)
        {
            if (!useVrmBlink || !vrmBlendShapeProxy)
            {
                return;
            }

            if (blinkStartedAt < 0f)
            {
                if (now < nextBlinkTime)
                {
                    return;
                }
                blinkStartedAt = now;
                blinkDuration = RandomRange(minimumBlinkDuration, maximumBlinkDuration);
            }

            var progress = Mathf.Clamp01((now - blinkStartedAt) / blinkDuration);
            var weight = progress < 0.38f
                ? Mathf.SmoothStep(0f, 1f, progress / 0.38f)
                : Mathf.SmoothStep(1f, 0f, (progress - 0.38f) / 0.62f);
            vrmBlendShapeProxy.ImmediatelySetValue(BlinkKey, weight);
            if (progress < 1f)
            {
                return;
            }

            blinkStartedAt = -1f;
            var stateMultiplier = conversationState switch
            {
                AvatarConversationState.Thinking => 0.82f,
                AvatarConversationState.Speaking => 0.9f,
                AvatarConversationState.Error => 1.25f,
                _ => 1f,
            };
            ScheduleNextBlink(now, stateMultiplier);
        }

        private void ScheduleNextBlink(float now, float multiplier = 1f)
        {
            nextBlinkTime = now + RandomRange(
                minimumBlinkInterval * multiplier,
                Mathf.Max(minimumBlinkInterval, maximumBlinkInterval) * multiplier);
        }

        private float RandomRange(float minimum, float maximum)
        {
            if (blinkRandom == null)
            {
                return minimum;
            }
            return Mathf.Lerp(minimum, Mathf.Max(minimum, maximum), (float)blinkRandom.NextDouble());
        }

        private void OnDisable()
        {
            ResetPose();
            ResetBlink();
        }

        private void ResetPose()
        {
            spine.Reset();
            chest.Reset();
            upperChest.Reset();
            neck.Reset();
            leftShoulder.Reset();
            rightShoulder.Reset();
            leftUpperArm.Reset();
            rightUpperArm.Reset();
            leftLowerArm.Reset();
            rightLowerArm.Reset();
            leftHand.Reset();
            rightHand.Reset();
        }

        private void ResetBlink()
        {
            if (useVrmBlink && vrmBlendShapeProxy)
            {
                vrmBlendShapeProxy.ImmediatelySetValue(BlinkKey, 0f);
            }
            blinkStartedAt = -1f;
        }

        private static float GetTargetEnergy(AvatarConversationState state)
        {
            return state switch
            {
                AvatarConversationState.Listening => 0.78f,
                AvatarConversationState.Thinking => 0.9f,
                AvatarConversationState.Speaking => 1.2f,
                AvatarConversationState.Error => 0.35f,
                _ => 0.68f,
            };
        }

        private static BonePose Bind(Animator animator, HumanBodyBones boneId)
        {
            return new BonePose(animator.GetBoneTransform(boneId));
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

        private readonly struct BonePose
        {
            public BonePose(Transform bone)
            {
                Bone = bone;
                ReferenceLocalRotation = bone ? bone.localRotation : Quaternion.identity;
            }

            private Transform Bone { get; }
            private Quaternion ReferenceLocalRotation { get; }
            public bool IsValid => Bone;

            public void Apply(Vector3 eulerAngles)
            {
                if (Bone)
                {
                    Bone.localRotation = ReferenceLocalRotation * Quaternion.Euler(eulerAngles);
                }
            }

            public void Reset()
            {
                if (Bone)
                {
                    Bone.localRotation = ReferenceLocalRotation;
                }
            }
        }
    }
}
