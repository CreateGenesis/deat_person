using System;
using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace DataPerson.DigitalHuman.Conversation
{
    [DisallowMultipleComponent]
    public sealed class RealtimeConversationSession : MonoBehaviour
    {
        private readonly ConcurrentQueue<Action> mainThreadActions = new();
        private readonly SemaphoreSlim sendOrder = new(1, 1);
        private readonly MicrophonePcmCapture microphone = new();
        private BackendConnectionOptions options;
        private BackendSessionClient sessionClient;
        private WebSocketConversationTransport transport;
        private CancellationTokenSource lifetime;
        private ConversationController conversation;
        private SpeechAudioPlayer speechPlayer;
        private string sessionId = string.Empty;
        private string activeTurnId = string.Empty;
        private string activeResponseId = string.Empty;
        private Guid activeOutputStreamId;
        private bool hasActiveOutputStream;
        private bool playbackStarted;
        private uint expectedOutputSequence;
        private Guid inputStreamId;
        private uint inputSequence;
        private bool inputStreaming;
        private long lastEventSequence;
        private bool socketReady;
        private bool disconnecting;
        private bool reconnecting;
        private float nextPingTime;
        private float nextProgressTime;

        public event Action Changed;
        public event Action<string> ErrorRaised;

        public string Status { get; private set; } = "Not configured";
        public string Transcript { get; private set; } = string.Empty;
        public string ResponseText { get; private set; } = string.Empty;
        public string LastError { get; private set; } = string.Empty;
        public int InputAudioFrameCount { get; private set; }
        public int InputAudioMilliseconds => InputAudioFrameCount * RealtimeProtocol.InputFrameMilliseconds;
        public bool IsConfigured => options?.IsConfigured == true;
        public bool IsReady => socketReady && transport?.IsConnected == true;
        public bool IsListening => microphone.IsCapturing;
        public bool HasSpeechPlayer => speechPlayer && speechPlayer.isActiveAndEnabled;
        public bool CanConnect => IsConfigured && !IsReady && !disconnecting && !reconnecting && transport == null;

        private void Awake()
        {
            microphone.FrameReady += HandleMicrophoneFrame;
        }

        public void Configure(string baseUrl, string apiKey, string site)
        {
            options = new BackendConnectionOptions(baseUrl, apiKey, site);
            LastError = string.Empty;
            Status = options.IsConfigured ? "Disconnected" : "Not configured";
            Changed?.Invoke();
        }

        public void AttachAvatar(DigitalHumanController avatar)
        {
            if (speechPlayer)
            {
                speechPlayer.PlaybackStarted -= HandlePlaybackStarted;
                speechPlayer.PlaybackCompleted -= HandlePlaybackCompleted;
            }
            conversation = avatar ? avatar.Conversation : null;
            speechPlayer = avatar ? avatar.GetComponent<SpeechAudioPlayer>() : null;
            if (speechPlayer)
            {
                speechPlayer.PlaybackStarted += HandlePlaybackStarted;
                speechPlayer.PlaybackCompleted += HandlePlaybackCompleted;
                if (Status == "Error" && LastError == "The avatar speech player is unavailable.")
                {
                    LastError = string.Empty;
                    SetStatus("Ready");
                }
            }
        }

        public async Task ConnectAsync()
        {
            if (!CanConnect) return;
            disconnecting = false;
            reconnecting = false;
            socketReady = false;
            SetStatus("Connecting");
            lifetime?.Cancel();
            lifetime?.Dispose();
            lifetime = new CancellationTokenSource();
            sessionClient = new BackendSessionClient(options);
            try
            {
                var ticket = await sessionClient.CreateSessionAsync(lifetime.Token);
                sessionId = ticket.SessionId;
                lastEventSequence = 0;
                await ConnectSocketAsync(ticket, lifetime.Token);
                SetStatus("Awaiting server");
            }
            catch (Exception exception)
            {
                HandleOperationFailure(exception);
                throw;
            }
        }

        public async Task SendTextAsync(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return;
            EnsureReady();
            Transcript = string.Empty;
            ResponseText = string.Empty;
            var json = RealtimeProtocol.InputText(sessionId, text.Trim(), out activeTurnId);
            activeResponseId = string.Empty;
            await SendEventOrderedAsync(json, lifetime.Token);
            conversation?.StartThinking();
            SetStatus("Thinking");
        }

        public async Task StartListeningAsync()
        {
            EnsureReady();
            if (microphone.IsCapturing) return;
            if (!string.IsNullOrWhiteSpace(activeResponseId)) await CancelResponseAsync();
            await microphone.StartAsync(lifetime.Token);
            inputStreamId = Guid.NewGuid();
            inputSequence = 0;
            InputAudioFrameCount = 0;
            Transcript = string.Empty;
            inputStreaming = true;
            LastError = string.Empty;
            try
            {
                await SendEventOrderedAsync(RealtimeProtocol.AudioStart(sessionId, inputStreamId), lifetime.Token);
            }
            catch
            {
                inputStreaming = false;
                microphone.Stop(false);
                throw;
            }
            conversation?.StartListening();
            SetStatus("Listening");
        }

        public async Task CommitListeningAsync()
        {
            if (!microphone.IsCapturing) return;
            microphone.Stop(true);
            inputStreaming = false;
            if (InputAudioFrameCount == 0)
            {
                await SendEventOrderedAsync(RealtimeProtocol.AudioStop(sessionId, inputStreamId), lifetime.Token);
                throw new InvalidOperationException("No microphone audio was captured. Hold the button while speaking and check microphone permission.");
            }
            await SendEventOrderedAsync(RealtimeProtocol.AudioCommit(sessionId, inputStreamId), lifetime.Token);
            conversation?.StartThinking();
            SetStatus("Thinking");
        }

        public async Task StopListeningAsync()
        {
            if (!microphone.IsCapturing) return;
            microphone.Stop(false);
            inputStreaming = false;
            await SendEventOrderedAsync(RealtimeProtocol.AudioStop(sessionId, inputStreamId), lifetime.Token);
            conversation?.Interrupt();
            SetStatus("Ready");
        }

        public async Task CancelResponseAsync()
        {
            if (!IsReady) return;
            var responseId = activeResponseId;
            var turnId = activeTurnId;
            var streamId = activeOutputStreamId;
            var hadStream = hasActiveOutputStream;
            var playedMilliseconds = speechPlayer ? speechPlayer.StreamPlayedMilliseconds : 0;
            speechPlayer?.Stop();
            conversation?.Interrupt();
            ResetOutputStream();
            await SendEventOrderedAsync(
                RealtimeProtocol.ResponseCancel(sessionId, responseId, turnId),
                lifetime.Token);
            if (hadStream && !string.IsNullOrWhiteSpace(responseId))
            {
                await SendEventOrderedAsync(
                    RealtimeProtocol.PlaybackAcknowledgement(
                        sessionId,
                        "stopped",
                        responseId,
                        streamId,
                        playedMilliseconds),
                    lifetime.Token);
            }
            SetStatus("Ready");
        }

        public async Task DisconnectAsync()
        {
            if (disconnecting) return;
            disconnecting = true;
            reconnecting = false;
            socketReady = false;
            microphone.Stop(false);
            inputStreaming = false;
            speechPlayer?.Stop();
            var closingSessionId = sessionId;
            lifetime?.Cancel();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(4));
            try
            {
                if (transport != null) await transport.DisconnectAsync(timeout.Token);
            }
            catch (Exception exception) when (exception is OperationCanceledException || exception is WebSocketException)
            {
            }
            transport?.Dispose();
            transport = null;
            try
            {
                if (sessionClient != null && !string.IsNullOrWhiteSpace(closingSessionId))
                {
                    await sessionClient.CloseSessionAsync(closingSessionId, timeout.Token);
                }
            }
            catch (Exception)
            {
            }
            sessionId = string.Empty;
            ResetOutputStream();
            SetStatus(IsConfigured ? "Disconnected" : "Not configured");
            disconnecting = false;
        }

        private void Update()
        {
            while (mainThreadActions.TryDequeue(out var action)) action();
            try
            {
                microphone.Pump();
            }
            catch (Exception exception)
            {
                microphone.Stop(false);
                HandleOperationFailure(exception);
            }
            if (!IsReady) return;
            if (Time.unscaledTime >= nextPingTime)
            {
                nextPingTime = Time.unscaledTime + 15f;
                _ = SendWithoutThrowAsync(RealtimeProtocol.Ping(sessionId));
            }
            if (hasActiveOutputStream && playbackStarted && speechPlayer?.IsStreaming == true
                && Time.unscaledTime >= nextProgressTime)
            {
                nextProgressTime = Time.unscaledTime + 1f;
                _ = SendPlaybackAcknowledgementAsync("progress");
            }
        }

        private async Task ConnectSocketAsync(BackendSessionTicket ticket, CancellationToken cancellationToken)
        {
            var candidate = new WebSocketConversationTransport();
            candidate.TextMessageReceived += value => mainThreadActions.Enqueue(() => HandleServerMessage(value));
            candidate.BinaryMessageReceived += value => mainThreadActions.Enqueue(() => HandleServerAudio(value));
            candidate.Faulted += exception => mainThreadActions.Enqueue(() => HandleTransportFailure(candidate, exception));
            candidate.Closed += () => mainThreadActions.Enqueue(() => HandleTransportClosed(candidate));
            transport = candidate;
            try
            {
                await candidate.ConnectAsync(
                    sessionClient.ResolveWebSocketUri(ticket),
                    ticket.Subprotocol,
                    cancellationToken);
            }
            catch
            {
                if (ReferenceEquals(transport, candidate)) transport = null;
                candidate.Dispose();
                throw;
            }
        }

        private void HandleServerMessage(string json)
        {
            try
            {
                var serverEvent = RealtimeServerEvent.Parse(json);
                if (!string.IsNullOrWhiteSpace(serverEvent.SessionId)
                    && serverEvent.SessionId != sessionId)
                {
                    throw new RealtimeProtocolException("session_mismatch", "Server event belongs to another session.");
                }
                if (serverEvent.Sequence.HasValue)
                {
                    if (serverEvent.Sequence.Value <= lastEventSequence
                        && serverEvent.Type != "session.snapshot") return;
                    lastEventSequence = Math.Max(lastEventSequence, serverEvent.Sequence.Value);
                }

                switch (serverEvent.Type)
                {
                    case "session.ready":
                        socketReady = true;
                        nextPingTime = Time.unscaledTime + 15f;
                        SetStatus("Ready");
                        _ = SendWithoutThrowAsync(RealtimeProtocol.ClientReady(sessionId, lastEventSequence));
                        break;
                    case "input.speech.started":
                        SetStatus("Listening");
                        break;
                    case "input.transcript.delta":
                        Transcript += serverEvent.Data.Value<string>("delta") ?? string.Empty;
                        Changed?.Invoke();
                        break;
                    case "input.transcript.completed":
                        Transcript = serverEvent.Data.Value<string>("text") ?? Transcript;
                        conversation?.StartThinking();
                        SetStatus("Thinking");
                        break;
                    case "response.started":
                        activeTurnId = serverEvent.TurnId;
                        activeResponseId = serverEvent.ResponseId;
                        ResponseText = string.Empty;
                        conversation?.StartThinking();
                        SetStatus("Thinking");
                        break;
                    case "response.text.delta":
                        ResponseText += serverEvent.Data.Value<string>("delta") ?? string.Empty;
                        Changed?.Invoke();
                        break;
                    case "response.audio.started":
                        BeginOutputStream(serverEvent);
                        break;
                    case "response.completed":
                        if (!hasActiveOutputStream && speechPlayer?.IsStreaming != true)
                        {
                            conversation?.Interrupt();
                            SetStatus("Ready");
                        }
                        break;
                    case "response.cancelled":
                    case "response.audio.stopped":
                        StopOutputFromServer();
                        break;
                    case "error":
                        throw new RealtimeProtocolException(
                            serverEvent.Data.Value<string>("code") ?? "server_error",
                            serverEvent.Data.Value<string>("message") ?? "Backend returned an error.");
                }
            }
            catch (Exception exception)
            {
                HandleOperationFailure(exception);
            }
        }

        private void BeginOutputStream(RealtimeServerEvent serverEvent)
        {
            var streamValue = serverEvent.Data.Value<string>("stream_id");
            if (!Guid.TryParse(streamValue, out activeOutputStreamId))
            {
                throw new RealtimeProtocolException("invalid_audio_stream", "Output stream ID is invalid.");
            }
            if (serverEvent.Data.Value<string>("format") != "pcm16le"
                || serverEvent.Data.Value<int?>("sample_rate") != RealtimeProtocol.OutputSampleRate
                || serverEvent.Data.Value<int?>("channels") != RealtimeProtocol.Channels)
            {
                throw new RealtimeProtocolException("unsupported_audio_format", "Output audio format changed during the session.");
            }
            activeResponseId = serverEvent.ResponseId;
            activeTurnId = serverEvent.TurnId;
            hasActiveOutputStream = true;
            playbackStarted = false;
            expectedOutputSequence = 0;
            if (!HasSpeechPlayer)
            {
                throw new InvalidOperationException("The avatar speech player is unavailable.");
            }
            speechPlayer.BeginPcm16Stream(RealtimeProtocol.OutputSampleRate, RealtimeProtocol.Channels);
        }

        private void HandleServerAudio(byte[] value)
        {
            try
            {
                var frame = RealtimeProtocol.DecodeAudioFrame(value);
                if (frame.Kind != RealtimeAudioKind.OutputPcm)
                {
                    throw new RealtimeProtocolException("invalid_audio_kind", "Server sent a non-output audio frame.");
                }
                if ((frame.Flags & RealtimeAudioFlags.Start) != 0)
                {
                    if (frame.ChunkSequence != 0)
                    {
                        throw new RealtimeProtocolException("audio_sequence_gap", "Output stream did not start at chunk zero.");
                    }
                    if (hasActiveOutputStream && frame.StreamId != activeOutputStreamId)
                    {
                        throw new RealtimeProtocolException("audio_stream_mismatch", "Output stream ID does not match its start event.");
                    }
                    activeOutputStreamId = frame.StreamId;
                    hasActiveOutputStream = true;
                    playbackStarted = false;
                    expectedOutputSequence = 0;
                    if (speechPlayer && !speechPlayer.IsStreaming)
                    {
                        speechPlayer.BeginPcm16Stream(RealtimeProtocol.OutputSampleRate, RealtimeProtocol.Channels);
                    }
                }
                if (!hasActiveOutputStream || frame.StreamId != activeOutputStreamId)
                {
                    throw new RealtimeProtocolException("audio_stream_mismatch", "Output audio has no matching active stream.");
                }
                if (frame.ChunkSequence != expectedOutputSequence)
                {
                    throw new RealtimeProtocolException(
                        "audio_sequence_gap",
                        $"Expected output chunk {expectedOutputSequence}, received {frame.ChunkSequence}.");
                }
                expectedOutputSequence++;
                if (frame.Payload.Length > 0) speechPlayer?.AppendPcm16Stream(frame.Payload);
                if ((frame.Flags & RealtimeAudioFlags.End) != 0) speechPlayer?.EndPcm16Stream();
            }
            catch (Exception exception)
            {
                speechPlayer?.Stop();
                ResetOutputStream();
                HandleOperationFailure(exception);
            }
        }

        private void HandleMicrophoneFrame(byte[] pcm)
        {
            if (!IsReady || !microphone.IsCapturing || !inputStreaming) return;
            var encoded = RealtimeProtocol.EncodeInputAudio(
                inputStreamId,
                inputSequence,
                pcm,
                inputSequence == 0);
            inputSequence++;
            InputAudioFrameCount++;
            if (InputAudioFrameCount == 1)
            {
                Debug.Log("[Voice] First microphone audio frame captured.");
            }
            _ = SendAudioWithoutThrowAsync(encoded);
        }

        private void HandlePlaybackStarted()
        {
            playbackStarted = true;
            nextProgressTime = Time.unscaledTime + 1f;
            conversation?.StartSpeaking();
            SetStatus("Speaking");
            _ = SendPlaybackAcknowledgementAsync("started");
        }

        private void HandlePlaybackCompleted()
        {
            _ = SendPlaybackAcknowledgementAsync("completed");
            ResetOutputStream();
            SetStatus("Ready");
        }

        private void StopOutputFromServer()
        {
            var shouldAcknowledge = hasActiveOutputStream && !string.IsNullOrWhiteSpace(activeResponseId);
            var streamId = activeOutputStreamId;
            var responseId = activeResponseId;
            var playedMilliseconds = speechPlayer ? speechPlayer.StreamPlayedMilliseconds : 0;
            speechPlayer?.Stop();
            conversation?.Interrupt();
            ResetOutputStream();
            if (shouldAcknowledge)
            {
                _ = SendWithoutThrowAsync(RealtimeProtocol.PlaybackAcknowledgement(
                    sessionId,
                    "stopped",
                    responseId,
                    streamId,
                    playedMilliseconds));
            }
            SetStatus("Ready");
        }

        private void HandleTransportFailure(WebSocketConversationTransport candidate, Exception exception)
        {
            if (!ReferenceEquals(transport, candidate) || disconnecting) return;
            ErrorRaised?.Invoke(exception.Message);
        }

        private void HandleTransportClosed(WebSocketConversationTransport candidate)
        {
            if (!ReferenceEquals(transport, candidate) || disconnecting) return;
            socketReady = false;
            microphone.Stop(false);
            inputStreaming = false;
            speechPlayer?.Stop();
            ResetOutputStream();
            candidate.Dispose();
            transport = null;
            if (!reconnecting && lifetime?.IsCancellationRequested == false)
            {
                reconnecting = true;
                _ = ReconnectAsync(lifetime.Token);
            }
        }

        private async Task ReconnectAsync(CancellationToken cancellationToken)
        {
            SetStatus("Reconnecting");
            Exception lastFailure = null;
            for (var attempt = 0; attempt < 3 && !cancellationToken.IsCancellationRequested; attempt++)
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(1 << attempt), cancellationToken);
                    var ticket = await sessionClient.CreateReconnectTicketAsync(
                        sessionId,
                        lastEventSequence,
                        cancellationToken);
                    await ConnectSocketAsync(ticket, cancellationToken);
                    reconnecting = false;
                    SetStatus("Awaiting server");
                    return;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    lastFailure = exception;
                }
            }
            reconnecting = false;
            HandleOperationFailure(lastFailure ?? new InvalidOperationException("Realtime reconnect failed."));
        }

        private Task SendPlaybackAcknowledgementAsync(string state)
        {
            if (!hasActiveOutputStream || string.IsNullOrWhiteSpace(activeResponseId)) return Task.CompletedTask;
            return SendWithoutThrowAsync(RealtimeProtocol.PlaybackAcknowledgement(
                sessionId,
                state,
                activeResponseId,
                activeOutputStreamId,
                speechPlayer ? speechPlayer.StreamPlayedMilliseconds : 0));
        }

        private async Task SendEventOrderedAsync(string json, CancellationToken cancellationToken)
        {
            await sendOrder.WaitAsync(cancellationToken);
            try
            {
                if (transport == null) throw new InvalidOperationException("Realtime transport is unavailable.");
                await transport.SendEventAsync(json, cancellationToken);
            }
            finally
            {
                sendOrder.Release();
            }
        }

        private async Task SendAudioOrderedAsync(byte[] value, CancellationToken cancellationToken)
        {
            await sendOrder.WaitAsync(cancellationToken);
            try
            {
                if (transport == null) throw new InvalidOperationException("Realtime transport is unavailable.");
                await transport.SendAudioAsync(value, cancellationToken);
            }
            finally
            {
                sendOrder.Release();
            }
        }

        private async Task SendWithoutThrowAsync(string json)
        {
            try
            {
                await SendEventOrderedAsync(json, lifetime?.Token ?? CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                mainThreadActions.Enqueue(() => HandleOperationFailure(exception));
            }
        }

        private async Task SendAudioWithoutThrowAsync(byte[] value)
        {
            try
            {
                await SendAudioOrderedAsync(value, lifetime?.Token ?? CancellationToken.None);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception exception)
            {
                mainThreadActions.Enqueue(() => HandleOperationFailure(exception));
            }
        }

        private void EnsureReady()
        {
            if (!IsReady) throw new InvalidOperationException("Backend session is not ready.");
        }

        private void SetStatus(string value)
        {
            if (value == "Ready") LastError = string.Empty;
            Status = value;
            Changed?.Invoke();
        }

        private void HandleOperationFailure(Exception exception)
        {
            if (exception is OperationCanceledException && lifetime?.IsCancellationRequested == true) return;
            Status = "Error";
            LastError = exception?.Message ?? "Backend operation failed.";
            conversation?.ReportError();
            ErrorRaised?.Invoke(LastError);
            Changed?.Invoke();
        }

        private void ResetOutputStream()
        {
            hasActiveOutputStream = false;
            playbackStarted = false;
            activeOutputStreamId = Guid.Empty;
            expectedOutputSequence = 0;
            activeResponseId = string.Empty;
        }

        private void OnDestroy()
        {
            microphone.FrameReady -= HandleMicrophoneFrame;
            microphone.Dispose();
            lifetime?.Cancel();
            lifetime?.Dispose();
            transport?.Dispose();
            sendOrder.Dispose();
            AttachAvatar(null);
        }
    }
}
