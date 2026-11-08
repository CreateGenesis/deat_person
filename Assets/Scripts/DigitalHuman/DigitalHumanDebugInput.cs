using UnityEngine;

namespace DataPerson.DigitalHuman
{
    [DisallowMultipleComponent]
    public sealed class DigitalHumanDebugInput : MonoBehaviour
    {
        private DigitalHumanController controller;
        private int maximumTouchCount;
        private bool touchSequenceActive;

        public void Initialize(DigitalHumanController digitalHumanController)
        {
            controller = digitalHumanController;
        }

        private void Update()
        {
            if (!controller || controller.StateMachine == null)
            {
                return;
            }

            HandleKeyboard();
            HandleTouch();
        }

        private void HandleKeyboard()
        {
            if (Input.GetKeyDown(KeyCode.Alpha1))
            {
                SetConversationState(AvatarConversationState.Idle);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha2))
            {
                SetConversationState(AvatarConversationState.Listening);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha3))
            {
                SetConversationState(AvatarConversationState.Thinking);
            }
            else if (Input.GetKeyDown(KeyCode.Alpha4))
            {
                SetConversationState(AvatarConversationState.Speaking);
            }

            if (Input.GetKeyDown(KeyCode.W))
            {
                controller.PlayGesture(AvatarGesture.Greeting);
            }
            else if (Input.GetKeyDown(KeyCode.Y))
            {
                controller.PlayGesture(AvatarGesture.Yes);
            }
            else if (Input.GetKeyDown(KeyCode.N))
            {
                controller.PlayGesture(AvatarGesture.No);
            }
            else if (Input.GetKeyDown(KeyCode.T))
            {
                controller.PlayGesture(AvatarGesture.Positive);
            }
        }

        private void HandleTouch()
        {
            if (Input.touchCount == 0)
            {
                touchSequenceActive = false;
                maximumTouchCount = 0;
                return;
            }

            if (!touchSequenceActive)
            {
                touchSequenceActive = true;
                maximumTouchCount = Input.touchCount;
            }
            else
            {
                maximumTouchCount = Mathf.Max(maximumTouchCount, Input.touchCount);
            }

            for (var index = 0; index < Input.touchCount; index++)
            {
                var phase = Input.GetTouch(index).phase;
                if (phase != TouchPhase.Ended && phase != TouchPhase.Canceled)
                {
                    return;
                }
            }

            if (maximumTouchCount >= 2)
            {
                controller.PlayGesture(AvatarGesture.Greeting);
            }
            else
            {
                CycleConversationState();
            }

            touchSequenceActive = false;
            maximumTouchCount = 0;
        }

        private void CycleConversationState()
        {
            var nextState = controller.StateMachine.ConversationState switch
            {
                AvatarConversationState.Idle => AvatarConversationState.Listening,
                AvatarConversationState.Listening => AvatarConversationState.Thinking,
                AvatarConversationState.Thinking => AvatarConversationState.Speaking,
                _ => AvatarConversationState.Idle,
            };
            SetConversationState(nextState);
        }

        private void SetConversationState(AvatarConversationState state)
        {
            controller.StateMachine.SetConversationState(state);
            Debug.Log($"Digital human debug state: {state}", this);
        }
    }
}
