using System;
using UnityEngine;

namespace DataPerson.DigitalHuman
{
    [DisallowMultipleComponent]
    public sealed class AvatarStateMachine : MonoBehaviour
    {
        [SerializeField] private AvatarLocalizationState localizationState =
            AvatarLocalizationState.Locating;
        [SerializeField] private AvatarConversationState conversationState =
            AvatarConversationState.Idle;

        public event Action<AvatarLocalizationState, AvatarLocalizationState>
            LocalizationStateChanged;
        public event Action<AvatarConversationState, AvatarConversationState>
            ConversationStateChanged;

        public AvatarLocalizationState LocalizationState => localizationState;
        public AvatarConversationState ConversationState => conversationState;

        public void SetLocalizationState(AvatarLocalizationState nextState)
        {
            if (localizationState == nextState)
            {
                return;
            }

            var previousState = localizationState;
            localizationState = nextState;
            LocalizationStateChanged?.Invoke(previousState, nextState);
        }

        public void SetConversationState(AvatarConversationState nextState)
        {
            if (conversationState == nextState)
            {
                return;
            }

            var previousState = conversationState;
            conversationState = nextState;
            ConversationStateChanged?.Invoke(previousState, nextState);
        }
    }
}
