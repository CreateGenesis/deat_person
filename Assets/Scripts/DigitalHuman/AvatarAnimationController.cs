using System.Collections;
using UnityEngine;

namespace DataPerson.DigitalHuman
{
    [DisallowMultipleComponent]
    public sealed class AvatarAnimationController : MonoBehaviour
    {
        private const string LayerName = "Base Layer";

        [SerializeField] private Animator animator;
        [SerializeField, Min(0f)] private float crossFadeSeconds = 0.2f;

        private AvatarStateMachine stateMachine;
        private Coroutine gestureRoutine;

        public void Initialize(
            AvatarStateMachine avatarStateMachine,
            RuntimeAnimatorController runtimeController)
        {
            if (stateMachine)
            {
                stateMachine.ConversationStateChanged -= HandleConversationStateChanged;
            }

            stateMachine = avatarStateMachine;
            animator = animator ? animator : GetComponentInChildren<Animator>(true);
            if (!animator)
            {
                Debug.LogWarning("Digital human Animator was not found.", this);
                return;
            }

            if (runtimeController)
            {
                animator.runtimeAnimatorController = runtimeController;
                animator.Rebind();
                animator.Update(0f);
            }

            stateMachine.ConversationStateChanged += HandleConversationStateChanged;
            PlayConversationState(stateMachine.ConversationState);
        }

        public bool PlayGesture(AvatarGesture gesture)
        {
            if (!animator || !animator.runtimeAnimatorController)
            {
                return false;
            }

            var stateName = GetGestureStateName(gesture);
            if (!HasState(stateName))
            {
                Debug.LogWarning($"Animator state '{stateName}' was not found.", this);
                return false;
            }

            if (gestureRoutine != null)
            {
                StopCoroutine(gestureRoutine);
            }
            gestureRoutine = StartCoroutine(PlayGestureAndReturn(stateName));
            return true;
        }

        private void OnDestroy()
        {
            if (stateMachine)
            {
                stateMachine.ConversationStateChanged -= HandleConversationStateChanged;
            }
        }

        private void OnEnable()
        {
            if (stateMachine && animator)
            {
                PlayConversationState(stateMachine.ConversationState);
            }
        }

        private void OnDisable()
        {
            if (gestureRoutine == null)
            {
                return;
            }

            StopCoroutine(gestureRoutine);
            gestureRoutine = null;
        }

        private void HandleConversationStateChanged(
            AvatarConversationState previousState,
            AvatarConversationState nextState)
        {
            if (gestureRoutine != null)
            {
                StopCoroutine(gestureRoutine);
                gestureRoutine = null;
            }
            PlayConversationState(nextState);
        }

        private void PlayConversationState(AvatarConversationState state)
        {
            PlayState(state.ToString());
        }

        private void PlayState(string stateName)
        {
            if (!animator || !animator.runtimeAnimatorController || !HasState(stateName))
            {
                return;
            }

            animator.CrossFade(GetStateHash(stateName), crossFadeSeconds, 0);
        }

        private IEnumerator PlayGestureAndReturn(string stateName)
        {
            PlayState(stateName);
            yield return new WaitForSeconds(GetClipDuration(stateName));
            gestureRoutine = null;
            if (stateMachine)
            {
                PlayConversationState(stateMachine.ConversationState);
            }
        }

        private float GetClipDuration(string clipName)
        {
            foreach (var clip in animator.runtimeAnimatorController.animationClips)
            {
                if (clip && clip.name == clipName)
                {
                    return Mathf.Max(0.1f, clip.length - crossFadeSeconds);
                }
            }
            return 1f;
        }

        private bool HasState(string stateName)
        {
            return animator.HasState(0, GetStateHash(stateName));
        }

        private static int GetStateHash(string stateName)
        {
            return Animator.StringToHash($"{LayerName}.{stateName}");
        }

        private static string GetGestureStateName(AvatarGesture gesture)
        {
            return gesture switch
            {
                AvatarGesture.Greeting => "Wave",
                AvatarGesture.Yes => "Yes",
                AvatarGesture.No => "No",
                AvatarGesture.Positive => "ThumbsUp",
                _ => "Wave",
            };
        }
    }
}
