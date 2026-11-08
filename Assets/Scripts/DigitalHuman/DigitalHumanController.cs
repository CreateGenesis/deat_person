using UnityEngine;

namespace DataPerson.DigitalHuman
{
    [DisallowMultipleComponent]
    public sealed class DigitalHumanController : MonoBehaviour
    {
        private AvatarStateMachine stateMachine;
        private AvatarAnimationController animationController;
        private AvatarStaticPoseController staticPoseController;
        private AvatarNaturalMotionController naturalMotionController;
        private GazeController gazeController;
        private SpeechAudioPlayer speechPlayer;
        private AudioLipSyncController lipSyncController;
        private ConversationController conversationController;

        public AvatarStateMachine StateMachine => stateMachine;
        public ConversationController Conversation => conversationController;

        public void Initialize(
            Camera arCamera,
            RuntimeAnimatorController runtimeAnimatorController,
            bool enableDevelopmentControls)
        {
            stateMachine = GetOrAddComponent<AvatarStateMachine>();
            animationController = GetOrAddComponent<AvatarAnimationController>();
            staticPoseController = GetOrAddComponent<AvatarStaticPoseController>();
            naturalMotionController = GetOrAddComponent<AvatarNaturalMotionController>();
            gazeController = GetOrAddComponent<GazeController>();
            speechPlayer = GetOrAddComponent<SpeechAudioPlayer>();
            lipSyncController = GetComponentInChildren<AudioLipSyncController>(true);
            if (!lipSyncController)
            {
                lipSyncController = gameObject.AddComponent<AudioLipSyncController>();
            }
            conversationController = GetOrAddComponent<ConversationController>();

            speechPlayer.Initialize();
            lipSyncController.Initialize(transform, speechPlayer.Source);
            conversationController.Initialize(stateMachine, speechPlayer);
            animationController.Initialize(stateMachine, runtimeAnimatorController);
            staticPoseController.Initialize(transform);
            naturalMotionController.Initialize(transform, stateMachine);
            gazeController.Initialize(transform, arCamera ? arCamera.transform : null);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (enableDevelopmentControls)
            {
                GetOrAddComponent<DigitalHumanDebugInput>().Initialize(this);
            }
#endif
        }

        public void SetLocalizationFound(bool isFound)
        {
            stateMachine.SetLocalizationState(
                isFound ? AvatarLocalizationState.Found : AvatarLocalizationState.Lost);
            gazeController.SetTracking(isFound);
            if (isFound)
            {
                speechPlayer.Resume();
            }
            else
            {
                speechPlayer.Pause();
            }
        }

        public bool PlayGesture(AvatarGesture gesture)
        {
            return animationController.PlayGesture(gesture);
        }

        private T GetOrAddComponent<T>() where T : Component
        {
            var component = GetComponent<T>();
            return component ? component : gameObject.AddComponent<T>();
        }
    }
}
