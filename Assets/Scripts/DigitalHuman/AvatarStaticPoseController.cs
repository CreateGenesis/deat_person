using UnityEngine;

namespace DataPerson.DigitalHuman
{
    [ExecuteAlways]
    [DefaultExecutionOrder(50)]
    [DisallowMultipleComponent]
    public sealed class AvatarStaticPoseController : MonoBehaviour
    {
        [SerializeField] private Vector3 leftUpperArmDirection =
            new(-0.24f, -0.96f, 0.14f);
        [SerializeField] private Vector3 rightUpperArmDirection =
            new(0.24f, -0.96f, 0.14f);
        [SerializeField] private Vector3 leftLowerArmDirection =
            new(0.12f, -0.97f, 0.22f);
        [SerializeField] private Vector3 rightLowerArmDirection =
            new(-0.12f, -0.97f, 0.22f);

        private Transform avatarRoot;
        private LimbPose leftUpperArm;
        private LimbPose rightUpperArm;
        private LimbPose leftLowerArm;
        private LimbPose rightLowerArm;
        private bool initialized;

        public void Initialize(Transform root)
        {
            if (initialized && avatarRoot == root)
            {
                return;
            }

            avatarRoot = root;
            var animator = root ? root.GetComponentInChildren<Animator>(true) : null;
            if (!animator || !animator.avatar || !animator.avatar.isHuman)
            {
                initialized = false;
                enabled = false;
                return;
            }

            leftUpperArm = CreateLimbPose(
                animator,
                HumanBodyBones.LeftUpperArm,
                HumanBodyBones.LeftLowerArm,
                leftUpperArmDirection);
            rightUpperArm = CreateLimbPose(
                animator,
                HumanBodyBones.RightUpperArm,
                HumanBodyBones.RightLowerArm,
                rightUpperArmDirection);
            leftLowerArm = CreateLimbPose(
                animator,
                HumanBodyBones.LeftLowerArm,
                HumanBodyBones.LeftHand,
                leftLowerArmDirection);
            rightLowerArm = CreateLimbPose(
                animator,
                HumanBodyBones.RightLowerArm,
                HumanBodyBones.RightHand,
                rightLowerArmDirection);
            initialized = leftUpperArm.IsValid
                && rightUpperArm.IsValid
                && leftLowerArm.IsValid
                && rightLowerArm.IsValid;
            enabled = initialized;
            ApplyPoseImmediately();
        }

        public void ApplyPoseImmediately()
        {
            if (!initialized || !avatarRoot)
            {
                return;
            }

            ApplyLimb(leftUpperArm);
            ApplyLimb(rightUpperArm);
            ApplyLimb(leftLowerArm);
            ApplyLimb(rightLowerArm);
        }

        private void OnEnable()
        {
            Initialize(transform);
        }

        private void LateUpdate()
        {
            ApplyPoseImmediately();
        }

        private void ApplyLimb(LimbPose limb)
        {
            limb.Bone.localRotation = limb.ReferenceLocalRotation;
            var currentDirection = limb.Child.position - limb.Bone.position;
            if (currentDirection.sqrMagnitude < 0.000001f)
            {
                return;
            }

            var desiredDirection = avatarRoot.TransformDirection(limb.Direction).normalized;
            limb.Bone.rotation = Quaternion.FromToRotation(
                currentDirection.normalized,
                desiredDirection) * limb.Bone.rotation;
        }

        private static LimbPose CreateLimbPose(
            Animator animator,
            HumanBodyBones boneId,
            HumanBodyBones childId,
            Vector3 direction)
        {
            var bone = animator.GetBoneTransform(boneId);
            var child = animator.GetBoneTransform(childId);
            return new LimbPose(bone, child, direction.normalized);
        }

        private readonly struct LimbPose
        {
            public LimbPose(Transform bone, Transform child, Vector3 direction)
            {
                Bone = bone;
                Child = child;
                Direction = direction;
                ReferenceLocalRotation = bone ? bone.localRotation : Quaternion.identity;
            }

            public Transform Bone { get; }
            public Transform Child { get; }
            public Vector3 Direction { get; }
            public Quaternion ReferenceLocalRotation { get; }
            public bool IsValid => Bone && Child;
        }
    }
}
