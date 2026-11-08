using DataPerson.DigitalHuman.Conversation;
using UnityEngine;

namespace DataPerson.DigitalHuman
{
    [DisallowMultipleComponent]
    public sealed class ConversationController : MonoBehaviour
    {
        private AvatarStateMachine stateMachine;
        private SpeechAudioPlayer speechPlayer;
        private IConversationTransport transport;

        public IConversationTransport Transport => transport;

        public void Initialize(AvatarStateMachine avatarStateMachine, SpeechAudioPlayer audioPlayer)
        {
            if (speechPlayer)
            {
                speechPlayer.PlaybackCompleted -= HandlePlaybackCompleted;
            }

            stateMachine = avatarStateMachine;
            speechPlayer = audioPlayer;
            speechPlayer.PlaybackCompleted += HandlePlaybackCompleted;
        }

        public void BindTransport(IConversationTransport conversationTransport)
        {
            transport = conversationTransport;
        }

        public void StartListening()
        {
            speechPlayer.Stop();
            stateMachine.SetConversationState(AvatarConversationState.Listening);
        }

        public void StartThinking()
        {
            stateMachine.SetConversationState(AvatarConversationState.Thinking);
        }

        public void StartSpeaking()
        {
            stateMachine.SetConversationState(AvatarConversationState.Speaking);
        }

        public void PlayAssistantAudio(AudioClip clip)
        {
            stateMachine.SetConversationState(AvatarConversationState.Speaking);
            speechPlayer.Play(clip);
        }

        public void PlayAssistantPcm16(byte[] pcmData, int sampleRate, int channels = 1)
        {
            stateMachine.SetConversationState(AvatarConversationState.Speaking);
            speechPlayer.PlayPcm16(pcmData, sampleRate, channels);
        }

        public void Interrupt()
        {
            speechPlayer.Stop();
            stateMachine.SetConversationState(AvatarConversationState.Idle);
        }

        public void ReportError()
        {
            speechPlayer.Stop();
            stateMachine.SetConversationState(AvatarConversationState.Error);
        }

        private void OnDestroy()
        {
            if (speechPlayer)
            {
                speechPlayer.PlaybackCompleted -= HandlePlaybackCompleted;
            }
        }

        private void HandlePlaybackCompleted()
        {
            stateMachine.SetConversationState(AvatarConversationState.Idle);
        }
    }
}
