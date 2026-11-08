using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine.Networking;

namespace DataPerson.DigitalHuman.Conversation
{
    public sealed class BackendConnectionOptions
    {
        public BackendConnectionOptions(string baseUrl, string apiKey, string site)
        {
            BaseUri = ParseBaseUri(baseUrl);
            ApiKey = apiKey?.Trim() ?? string.Empty;
            Site = string.IsNullOrWhiteSpace(site) ? "flutter-android" : site.Trim();
        }

        public Uri BaseUri { get; }
        public string ApiKey { get; }
        public string Site { get; }
        public bool IsConfigured => BaseUri != null && !string.IsNullOrWhiteSpace(ApiKey);

        private static Uri ParseBaseUri(string value)
        {
            if (!Uri.TryCreate(value?.Trim(), UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                return null;
            }
            return uri;
        }
    }

    public sealed class BackendSessionTicket
    {
        [JsonProperty("session_id")]
        public string SessionId { get; set; }

        [JsonProperty("expires_at")]
        public DateTimeOffset ExpiresAt { get; set; }

        [JsonProperty("websocket_url")]
        public string WebSocketUrl { get; set; }

        [JsonProperty("subprotocol")]
        public string Subprotocol { get; set; }

        [JsonProperty("input_audio")]
        public BackendAudioFormat InputAudio { get; set; }

        [JsonProperty("output_audio")]
        public BackendAudioFormat OutputAudio { get; set; }
    }

    public sealed class BackendAudioFormat
    {
        [JsonProperty("format")]
        public string Format { get; set; }

        [JsonProperty("sample_rate")]
        public int SampleRate { get; set; }

        [JsonProperty("channels")]
        public int Channels { get; set; }

        [JsonProperty("frame_ms")]
        public int FrameMilliseconds { get; set; }
    }

    public sealed class BackendHttpException : Exception
    {
        public BackendHttpException(long statusCode, string responseBody)
            : base($"Backend request failed with HTTP {statusCode}: {responseBody}")
        {
            StatusCode = statusCode;
            ResponseBody = responseBody;
        }

        public long StatusCode { get; }
        public string ResponseBody { get; }
    }

    public sealed class BackendSessionClient
    {
        private readonly BackendConnectionOptions options;

        public BackendSessionClient(BackendConnectionOptions connectionOptions)
        {
            options = connectionOptions ?? throw new ArgumentNullException(nameof(connectionOptions));
            if (!options.IsConfigured)
            {
                throw new ArgumentException("Backend URL and realtime API key are required.", nameof(connectionOptions));
            }
        }

        public async Task<BackendSessionTicket> CreateSessionAsync(CancellationToken cancellationToken)
        {
            var body = JsonConvert.SerializeObject(new
            {
                client_reference = "flutter-android",
                metadata = new { site = options.Site },
                debug = false,
            });
            var endpoint = new Uri(options.BaseUri, "/api/v1/sessions");
            using var request = CreateJsonRequest(endpoint, UnityWebRequest.kHttpVerbPOST, body);
            await SendAsync(request, cancellationToken);
            var ticket = JsonConvert.DeserializeObject<BackendSessionTicket>(request.downloadHandler.text);
            ValidateTicket(ticket);
            return ticket;
        }

        public async Task<BackendSessionTicket> CreateReconnectTicketAsync(
            string sessionId,
            long lastEventSequence,
            CancellationToken cancellationToken)
        {
            var endpoint = new Uri(options.BaseUri, $"/api/v1/sessions/{Uri.EscapeDataString(sessionId)}/tickets");
            var body = JsonConvert.SerializeObject(new { last_event_seq = Math.Max(0, lastEventSequence) });
            using var request = CreateJsonRequest(endpoint, UnityWebRequest.kHttpVerbPOST, body);
            await SendAsync(request, cancellationToken);
            var ticket = JsonConvert.DeserializeObject<BackendSessionTicket>(request.downloadHandler.text);
            ValidateTicket(ticket);
            return ticket;
        }

        public async Task CloseSessionAsync(string sessionId, CancellationToken cancellationToken)
        {
            if (string.IsNullOrWhiteSpace(sessionId)) return;
            var endpoint = new Uri(options.BaseUri, $"/api/v1/sessions/{Uri.EscapeDataString(sessionId)}");
            using var request = CreateJsonRequest(endpoint, UnityWebRequest.kHttpVerbDELETE, null);
            await SendAsync(request, cancellationToken);
        }

        public Uri ResolveWebSocketUri(BackendSessionTicket ticket)
        {
            var endpoint = new Uri(options.BaseUri, ticket.WebSocketUrl);
            var scheme = endpoint.Scheme switch
            {
                "https" => "wss",
                "http" => "ws",
                "wss" => "wss",
                "ws" => "ws",
                _ => throw new RealtimeProtocolException("invalid_ticket", "Ticket WebSocket URL has an unsupported scheme."),
            };
            var builder = new UriBuilder(endpoint)
            {
                Scheme = scheme,
                Port = endpoint.IsDefaultPort ? -1 : endpoint.Port,
            };
            return builder.Uri;
        }

        private UnityWebRequest CreateJsonRequest(Uri endpoint, string method, string body)
        {
            var request = new UnityWebRequest(endpoint, method)
            {
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = 20,
            };
            if (body != null)
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(body));
                request.SetRequestHeader("Content-Type", "application/json");
            }
            request.SetRequestHeader("Authorization", "Bearer " + options.ApiKey);
            return request;
        }

        private static async Task SendAsync(
            UnityWebRequest request,
            CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var operation = request.SendWebRequest();
            operation.completed += _ => completion.TrySetResult(true);
            using var registration = cancellationToken.Register(() =>
            {
                request.Abort();
                completion.TrySetCanceled(cancellationToken);
            });
            await completion.Task;
            cancellationToken.ThrowIfCancellationRequested();
            if (request.result != UnityWebRequest.Result.Success)
            {
                throw new BackendHttpException(request.responseCode, request.downloadHandler?.text ?? request.error);
            }
        }

        private static void ValidateTicket(BackendSessionTicket ticket)
        {
            if (ticket == null
                || string.IsNullOrWhiteSpace(ticket.SessionId)
                || string.IsNullOrWhiteSpace(ticket.WebSocketUrl)
                || ticket.Subprotocol != RealtimeProtocol.WebSocketSubprotocol)
            {
                throw new RealtimeProtocolException("invalid_ticket", "Backend returned an invalid realtime ticket.");
            }
            if (ticket.InputAudio?.Format != "pcm16le"
                || ticket.InputAudio.SampleRate != RealtimeProtocol.InputSampleRate
                || ticket.InputAudio.Channels != RealtimeProtocol.Channels
                || ticket.InputAudio.FrameMilliseconds != RealtimeProtocol.InputFrameMilliseconds
                || ticket.OutputAudio?.Format != "pcm16le"
                || ticket.OutputAudio.SampleRate != RealtimeProtocol.OutputSampleRate
                || ticket.OutputAudio.Channels != RealtimeProtocol.Channels
                || ticket.OutputAudio.FrameMilliseconds != RealtimeProtocol.OutputFrameMilliseconds)
            {
                throw new RealtimeProtocolException("unsupported_audio_format", "Backend audio format is not supported by this client.");
            }
        }
    }
}
