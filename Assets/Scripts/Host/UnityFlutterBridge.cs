using System;
using System.Collections;
using DataPerson.DigitalHuman;
using DataPerson.DigitalHuman.Conversation;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Scripting;

namespace DataPerson.Host
{
    [Preserve]
    public sealed class UnityFlutterBridge : MonoBehaviour
    {
        public static bool IsHosted { get; private set; }
        public static bool PreviewRequested { get; private set; }
        public static float InterfaceScale => Mathf.Max(1f,
            Mathf.Min(Screen.width / 430f, Screen.height / 780f));

        private const string NativeClass = "com.example.virtual_agent.UnityHostBridge";
        private EasyARCloudBlockBootstrap bootstrap;
        private DigitalHumanController attachedAvatar;
        private RealtimeConversationSession backendSession;
        private bool opening;
        private bool closed;
        private bool backgrounded;
        private string lastStatus;
        private float nextStatusTime;
        private bool backendDirty;
        private bool pushToTalkPressed;
        private bool pushToTalkStarting;
        private bool pushToTalkCommitting;
        private int pushToTalkFingerId = -1;
        private string pushToTalkFeedback = "Hold to speak";
        private GUIStyle wrappedLabel;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetState()
        {
            IsHosted = false;
            PreviewRequested = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Install()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var native = new AndroidJavaClass(NativeClass);
                var mode = native.CallStatic<string>("getLaunchMode");
                IsHosted = mode == "preview" || mode == "onsite";
                PreviewRequested = mode == "preview";
            }
            catch (AndroidJavaException)
            {
                return;
            }
#elif UNITY_EDITOR
            PreviewRequested = UnityEditor.SessionState.GetBool("DataPerson.FlutterPreview", false);
            UnityEditor.SessionState.SetBool("DataPerson.FlutterPreview", false);
            IsHosted = PreviewRequested;
#endif
            if (IsHosted)
            {
                var bridgeObject = new GameObject("FlutterUnityBridge");
                DontDestroyOnLoad(bridgeObject);
                bridgeObject.AddComponent<UnityFlutterBridge>();
            }
        }

        private void Awake()
        {
            backendSession = GetComponent<RealtimeConversationSession>();
            if (!backendSession) backendSession = gameObject.AddComponent<RealtimeConversationSession>();
            backendSession.Changed += HandleBackendChanged;
            backendSession.ErrorRaised += HandleBackendError;
        }

        private void Start()
        {
            bootstrap = FindFirstObjectByType<EasyARCloudBlockBootstrap>();
            Emit("Ready");
        }

        [Preserve]
        public void ReceiveCommand(string json)
        {
            BridgeCommand command = null;
            try
            {
                command = JsonUtility.FromJson<BridgeCommand>(json);
                if (command == null || command.version != 1)
                {
                    throw new ArgumentException("Unsupported bridge protocol version.");
                }
                if (command.type == "OpenPlace")
                {
                    if (opening || (command.mode != "preview" && command.mode != "onsite"))
                    {
                        throw new InvalidOperationException("Unity is opening, or the requested mode is invalid.");
                    }
                    StartCoroutine(Open(command));
                    return;
                }
                if (command.type == "CloseAR")
                {
                    closed = true;
                    pushToTalkPressed = false;
                    pushToTalkFingerId = -1;
                    _ = RunBackendCommand(backendSession.DisconnectAsync, command.requestId);
                    if (bootstrap)
                    {
                        Pause(true);
                    }
                    Emit("Closed", command.requestId);
                    return;
                }
                if (!bootstrap || opening)
                {
                    throw new InvalidOperationException("Unity scene is not ready.");
                }
                switch (command.type)
                {
                    case "PauseAR":
                        Pause(true);
                        Emit("Paused", command.requestId);
                        break;
                    case "ResumeAR":
                        if (!closed)
                        {
                            Pause(false);
                            Emit("Resumed", command.requestId);
                        }
                        break;
                    case "SetConversationState":
                        if (!bootstrap.DigitalHuman || !Enum.TryParse(command.value, out AvatarConversationState state)
                            || !Enum.IsDefined(typeof(AvatarConversationState), state))
                        {
                            throw new ArgumentException("Avatar or conversation state is unavailable.");
                        }
                        bootstrap.DigitalHuman.StateMachine.SetConversationState(state);
                        Emit("Status", command.requestId);
                        break;
                    case "PlayGesture":
                        if (!bootstrap.DigitalHuman || !Enum.TryParse(command.value, out AvatarGesture gesture)
                            || !Enum.IsDefined(typeof(AvatarGesture), gesture)
                            || !bootstrap.DigitalHuman.PlayGesture(gesture))
                        {
                            throw new InvalidOperationException("Gesture is not available in the current state.");
                        }
                        Emit("GesturePlayed", command.requestId, command.value);
                        break;
                    case "PreviewTracking":
                        if (command.value != "found" && command.value != "lost")
                        {
                            throw new ArgumentException("PreviewTracking expects found or lost.");
                        }
                        bootstrap.SetPreviewTracking(command.value == "found");
                        Emit("Status", command.requestId);
                        break;
                    case "ConnectBackend":
                        _ = RunBackendCommand(backendSession.ConnectAsync, command.requestId);
                        break;
                    case "SendText":
                        _ = RunBackendCommand(() => backendSession.SendTextAsync(command.value), command.requestId);
                        break;
                    case "StartListening":
                        _ = RunBackendCommand(backendSession.StartListeningAsync, command.requestId);
                        break;
                    case "CommitListening":
                        _ = RunBackendCommand(backendSession.CommitListeningAsync, command.requestId);
                        break;
                    case "StopListening":
                        _ = RunBackendCommand(backendSession.StopListeningAsync, command.requestId);
                        break;
                    case "CancelResponse":
                        _ = RunBackendCommand(backendSession.CancelResponseAsync, command.requestId);
                        break;
                    default:
                        throw new ArgumentException("Unknown bridge command.");
                }
            }
            catch (Exception exception)
            {
                Emit("Error", command?.requestId, exception.Message);
            }
        }

        private IEnumerator Open(BridgeCommand command)
        {
            opening = true;
            closed = false;
            var preview = command.mode == "preview";
            PreviewRequested = preview;
            AudioListener.pause = false;
            if (!bootstrap || bootstrap.PreviewMode != preview)
            {
                var load = SceneManager.LoadSceneAsync(0);
                yield return load;
                bootstrap = FindFirstObjectByType<EasyARCloudBlockBootstrap>();
            }
            opening = false;
            if (!bootstrap || !bootstrap.enabled)
            {
                Emit("Error", command.requestId, "The avatar scene failed to initialize.");
                yield break;
            }
            try
            {
                backendSession.Configure(command.backendBaseUrl, command.backendApiKey, command.site);
                attachedAvatar = null;
                AttachBackendAvatar();
                if (backendSession.IsConfigured) _ = RunBackendCommand(backendSession.ConnectAsync, command.requestId);
                Pause(closed || backgrounded);
                Emit(closed ? "Closed" : "Opened", command.requestId, preview
                    ? "Offline preview; localization and M3 occlusion are not running."
                    : "Onsite mode; real localization is required.");
            }
            catch (Exception exception)
            {
                Emit("Error", command.requestId, exception.Message);
            }
        }

        private void Pause(bool paused)
        {
            bootstrap.SetSessionPaused(paused);
            AudioListener.pause = paused;
        }

        private void OnApplicationPause(bool paused)
        {
            backgrounded = paused;
            if (!bootstrap || opening || closed)
            {
                return;
            }
            try
            {
                if (paused && backendSession.IsListening)
                {
                    pushToTalkPressed = false;
                    pushToTalkFingerId = -1;
                    _ = RunBackendCommand(backendSession.StopListeningAsync);
                }
                Pause(paused);
                Emit(paused ? "Paused" : "Resumed");
            }
            catch (Exception exception)
            {
                Emit("Error", message: exception.Message);
            }
        }

        private void Update()
        {
            if (!bootstrap || opening || closed)
            {
                return;
            }
            AttachBackendAvatar();
            if (Time.unscaledTime < nextStatusTime) return;
            nextStatusTime = Time.unscaledTime + 0.25f;
            var status = $"{bootstrap.LocalizationStatus}:{bootstrap.DigitalHuman?.StateMachine?.ConversationState}";
            if (lastStatus != status)
            {
                lastStatus = status;
                Emit("Status");
            }
            if (backendDirty)
            {
                backendDirty = false;
                Emit("BackendStatus");
            }
        }

        private void Emit(string type, string requestId = null, string message = null)
        {
            var payload = new BridgeEvent
            {
                type = type,
                requestId = requestId ?? string.Empty,
                mode = PreviewRequested ? "preview" : "onsite",
                localization = bootstrap ? bootstrap.LocalizationStatus : "Loading",
                conversation = bootstrap && bootstrap.DigitalHuman
                    ? bootstrap.DigitalHuman.StateMachine.ConversationState.ToString() : "Unavailable",
                message = message ?? string.Empty,
                backend = backendSession ? backendSession.Status : "Unavailable",
                transcript = backendSession ? backendSession.Transcript : string.Empty,
                response = backendSession ? backendSession.ResponseText : string.Empty,
            };
            var json = JsonUtility.ToJson(payload);
#if UNITY_ANDROID && !UNITY_EDITOR
            using var native = new AndroidJavaClass(NativeClass);
            native.CallStatic("emitEvent", json);
#else
            Debug.Log($"Flutter bridge: {json}");
#endif
        }

        private void OnGUI()
        {
            if (closed || !bootstrap)
            {
                return;
            }
            var scale = InterfaceScale;
            var previousMatrix = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            GUILayout.BeginArea(new Rect(12, 32, Screen.width / scale - 24, 86), GUI.skin.box);
            GUILayout.Label(PreviewRequested ? "OFFLINE PREVIEW - NOT AN AR TEST" : "EASYAR ONSITE - WAIT FOR LOCALIZATION");
            GUILayout.Label($"{bootstrap.LocalizationStatus} / {bootstrap.DigitalHuman?.StateMachine?.ConversationState}");
            if (GUILayout.Button("Back to Flutter", GUILayout.Height(32)))
            {
                ReceiveCommand("{\"version\":1,\"type\":\"CloseAR\"}");
            }
            GUILayout.EndArea();
            var backendHeight = backendSession && backendSession.IsConfigured ? 238 : 78;
            var backendArea = new Rect(12, Screen.height / scale - backendHeight - 15, Screen.width / scale - 24, backendHeight);
            GUILayout.BeginArea(backendArea, GUI.skin.box);
            if (backendSession && backendSession.IsConfigured)
            {
                GUILayout.Label($"AI: {backendSession.Status} / {PushToTalkStatus()}");
                DrawPushToTalkButton(backendArea.position, scale);
                wrappedLabel ??= new GUIStyle(GUI.skin.label) { wordWrap = true };
                if (!string.IsNullOrWhiteSpace(backendSession.LastError))
                    GUILayout.Label(backendSession.LastError, wrappedLabel);
                if (!string.IsNullOrWhiteSpace(backendSession.Transcript))
                    GUILayout.Label($"You: {DisplayTail(backendSession.Transcript)}", wrappedLabel);
                if (!string.IsNullOrWhiteSpace(backendSession.ResponseText))
                    GUILayout.Label($"AI: {DisplayTail(backendSession.ResponseText)}", wrappedLabel);
            }
            else
            {
                GUILayout.Label("AI backend is not configured.");
            }
            GUILayout.EndArea();
            GUI.matrix = previousMatrix;
        }

        private void DrawPushToTalkButton(Vector2 areaPosition, float scale)
        {
            var canStart = backendSession.IsReady && backendSession.HasSpeechPlayer
                && !pushToTalkStarting && !pushToTalkCommitting;
            var label = !backendSession.HasSpeechPlayer
                ? "WAIT FOR AVATAR"
                : backendSession.IsListening || pushToTalkPressed
                ? "RELEASE TO SEND"
                : backendSession.Status switch
                {
                    "Thinking" => "THINKING...",
                    "Speaking" => "SPEAKING...",
                    "Ready" => "HOLD TO TALK",
                    _ => "CONNECTING...",
                };
            var previousEnabled = GUI.enabled;
            GUI.enabled = canStart || pushToTalkPressed;
            var buttonRect = GUILayoutUtility.GetRect(
                GUIContent.none,
                GUI.skin.button,
                GUILayout.ExpandWidth(true),
                GUILayout.Height(64));
            GUI.Button(buttonRect, label);
            GUI.enabled = previousEnabled;

            if (Application.isMobilePlatform)
            {
                if (Event.current.type == EventType.Repaint)
                {
                    var screenRect = new Rect(
                        (areaPosition.x + buttonRect.x) * scale,
                        (areaPosition.y + buttonRect.y) * scale,
                        buttonRect.width * scale,
                        buttonRect.height * scale);
                    HandleTouchInput(screenRect, canStart);
                }
                return;
            }

            var current = Event.current;
            if (current.button != 0) return;
            if (current.rawType == EventType.MouseDown && buttonRect.Contains(current.mousePosition) && canStart)
            {
                BeginPushToTalk();
            }
            else if (current.rawType == EventType.MouseUp && pushToTalkPressed)
            {
                EndPushToTalk();
            }
        }

        private void HandleTouchInput(Rect buttonScreenRect, bool canStart)
        {
            var trackedTouchFound = false;
            for (var index = 0; index < Input.touchCount; index++)
            {
                var touch = Input.GetTouch(index);
                var screenPosition = new Vector2(touch.position.x, Screen.height - touch.position.y);
                if (pushToTalkFingerId < 0 && touch.phase == TouchPhase.Began
                    && buttonScreenRect.Contains(screenPosition) && canStart)
                {
                    pushToTalkFingerId = touch.fingerId;
                    BeginPushToTalk();
                }
                if (touch.fingerId != pushToTalkFingerId) continue;
                trackedTouchFound = true;
                if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                {
                    pushToTalkFingerId = -1;
                    EndPushToTalk();
                }
            }
            if (pushToTalkPressed && pushToTalkFingerId >= 0 && !trackedTouchFound && Input.touchCount == 0)
            {
                pushToTalkFingerId = -1;
                EndPushToTalk();
            }
        }

        private void BeginPushToTalk()
        {
            if (pushToTalkPressed) return;
            pushToTalkPressed = true;
            pushToTalkFeedback = "Touch received; starting microphone";
            Debug.Log("[Voice] Push-to-talk pressed.");
            _ = StartPushToTalkAsync();
        }

        private void EndPushToTalk()
        {
            if (!pushToTalkPressed) return;
            pushToTalkPressed = false;
            pushToTalkFeedback = pushToTalkStarting
                ? "Release received; waiting for microphone"
                : $"Release received; captured {backendSession.InputAudioMilliseconds} ms";
            Debug.Log($"[Voice] Push-to-talk released after {backendSession.InputAudioMilliseconds} ms.");
            if (!pushToTalkStarting) _ = CommitPushToTalkAsync();
        }

        private async System.Threading.Tasks.Task StartPushToTalkAsync()
        {
            if (pushToTalkStarting || !backendSession.IsReady) return;
            pushToTalkStarting = true;
            try
            {
                await RunBackendCommand(backendSession.StartListeningAsync);
                if (backendSession.IsListening)
                {
                    pushToTalkFeedback = "Recording";
                    Debug.Log("[Voice] Recording started.");
                }
            }
            finally
            {
                pushToTalkStarting = false;
            }
            if (!pushToTalkPressed && backendSession.IsListening)
            {
                await CommitPushToTalkAsync();
            }
        }

        private async System.Threading.Tasks.Task CommitPushToTalkAsync()
        {
            if (pushToTalkCommitting || !backendSession.IsListening) return;
            pushToTalkCommitting = true;
            try
            {
                await RunBackendCommand(backendSession.CommitListeningAsync);
                if (backendSession.Status != "Error")
                {
                    pushToTalkFeedback = $"Sent {backendSession.InputAudioMilliseconds} ms; waiting for recognition";
                    Debug.Log($"[Voice] Audio committed: {backendSession.InputAudioFrameCount} frame(s), {backendSession.InputAudioMilliseconds} ms.");
                }
            }
            finally
            {
                pushToTalkCommitting = false;
            }
        }

        private async System.Threading.Tasks.Task RunBackendCommand(
            Func<System.Threading.Tasks.Task> command,
            string requestId = null)
        {
            try
            {
                await command();
                Emit("BackendStatus", requestId);
            }
            catch (Exception exception)
            {
                Emit("BackendError", requestId, exception.Message);
            }
        }

        private void HandleBackendChanged()
        {
            backendDirty = true;
        }

        private void HandleBackendError(string message)
        {
            pushToTalkFeedback = message;
            Emit("BackendError", message: message);
        }

        private void AttachBackendAvatar()
        {
            var avatar = bootstrap ? bootstrap.DigitalHuman : null;
            if (avatar == attachedAvatar) return;
            attachedAvatar = avatar;
            backendSession.AttachAvatar(avatar);
            backendDirty = true;
            if (avatar) Debug.Log("[Voice] Avatar speech player attached.");
        }

        private string PushToTalkStatus()
        {
            if (!backendSession.HasSpeechPlayer) return "Waiting for avatar";
            if (backendSession.IsListening)
            {
                return $"Recording {backendSession.InputAudioMilliseconds / 1000f:0.0}s";
            }
            if (!string.IsNullOrWhiteSpace(backendSession.LastError)) return "Voice error";
            if (backendSession.Status == "Thinking") return "Waiting for recognition";
            if (backendSession.Status == "Speaking") return "Playing answer";
            return pushToTalkFeedback;
        }

        private static string DisplayTail(string value)
        {
            const int limit = 240;
            return value.Length <= limit ? value : "..." + value[^limit..];
        }

        private void OnDestroy()
        {
            if (!backendSession) return;
            backendSession.Changed -= HandleBackendChanged;
            backendSession.ErrorRaised -= HandleBackendError;
            backendSession.AttachAvatar(null);
        }

        [Serializable]
        private sealed class BridgeCommand
        {
            public int version;
            public string type;
            public string requestId;
            public string mode;
            public string value;
            public string backendBaseUrl;
            public string backendApiKey;
            public string site;
        }

        [Serializable]
        private sealed class BridgeEvent
        {
            public int version = 1;
            public string type;
            public string requestId;
            public string mode;
            public string localization;
            public string conversation;
            public string message;
            public string backend;
            public string transcript;
            public string response;
        }
    }
}
