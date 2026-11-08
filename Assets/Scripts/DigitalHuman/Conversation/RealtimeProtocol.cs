using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace DataPerson.DigitalHuman.Conversation
{
    [Flags]
    public enum RealtimeAudioFlags : byte
    {
        None = 0,
        Start = 1,
        End = 2,
    }

    public enum RealtimeAudioKind : byte
    {
        InputPcm = 1,
        OutputPcm = 2,
    }

    public sealed class RealtimeProtocolException : Exception
    {
        public RealtimeProtocolException(string code, string message) : base(message)
        {
            Code = code;
        }

        public string Code { get; }
    }

    public readonly struct RealtimeAudioFrame
    {
        public RealtimeAudioFrame(
            RealtimeAudioKind kind,
            RealtimeAudioFlags flags,
            uint chunkSequence,
            Guid streamId,
            byte[] payload)
        {
            Kind = kind;
            Flags = flags;
            ChunkSequence = chunkSequence;
            StreamId = streamId;
            Payload = payload ?? Array.Empty<byte>();
        }

        public RealtimeAudioKind Kind { get; }
        public RealtimeAudioFlags Flags { get; }
        public uint ChunkSequence { get; }
        public Guid StreamId { get; }
        public byte[] Payload { get; }

        public byte[] Encode()
        {
            var value = new byte[RealtimeProtocol.AudioHeaderBytes + Payload.Length];
            value[0] = RealtimeProtocol.Version;
            value[1] = (byte)Kind;
            value[2] = (byte)Flags;
            value[3] = 0;
            value[4] = (byte)(ChunkSequence >> 24);
            value[5] = (byte)(ChunkSequence >> 16);
            value[6] = (byte)(ChunkSequence >> 8);
            value[7] = (byte)ChunkSequence;
            var streamBytes = RealtimeProtocol.GuidToRfc4122Bytes(StreamId);
            Buffer.BlockCopy(streamBytes, 0, value, 8, streamBytes.Length);
            Buffer.BlockCopy(Payload, 0, value, RealtimeProtocol.AudioHeaderBytes, Payload.Length);
            return value;
        }
    }

    public sealed class RealtimeServerEvent
    {
        private RealtimeServerEvent(JObject value)
        {
            Value = value;
            Type = value.Value<string>("type") ?? string.Empty;
            Sequence = value.Value<long?>("seq");
            EventId = value.Value<string>("event_id") ?? string.Empty;
            SessionId = value.Value<string>("session_id") ?? string.Empty;
            TurnId = value.Value<string>("turn_id") ?? string.Empty;
            ResponseId = value.Value<string>("response_id") ?? string.Empty;
            Data = value["data"] as JObject ?? new JObject();
        }

        public JObject Value { get; }
        public string Type { get; }
        public long? Sequence { get; }
        public string EventId { get; }
        public string SessionId { get; }
        public string TurnId { get; }
        public string ResponseId { get; }
        public JObject Data { get; }

        public static RealtimeServerEvent Parse(string json)
        {
            JObject value;
            try
            {
                value = JObject.Parse(json);
            }
            catch (JsonException exception)
            {
                throw new RealtimeProtocolException("invalid_json", exception.Message);
            }

            if (value.Value<int?>("v") != RealtimeProtocol.Version)
            {
                throw new RealtimeProtocolException("unsupported_version", "Server event version is not supported.");
            }
            if (string.IsNullOrWhiteSpace(value.Value<string>("type")))
            {
                throw new RealtimeProtocolException("invalid_event", "Server event type is required.");
            }
            return new RealtimeServerEvent(value);
        }
    }

    public static class RealtimeProtocol
    {
        public const byte Version = 1;
        public const string WebSocketSubprotocol = "virtual-agent.v1";
        public const int AudioHeaderBytes = 24;
        public const int InputSampleRate = 16000;
        public const int OutputSampleRate = 24000;
        public const int Channels = 1;
        public const int InputFrameMilliseconds = 20;
        public const int OutputFrameMilliseconds = 100;
        public const int InputFrameBytes = 640;
        public const int OutputFrameBytes = 4800;

        public static RealtimeAudioFrame DecodeAudioFrame(byte[] value)
        {
            if (value == null || value.Length < AudioHeaderBytes)
            {
                throw new RealtimeProtocolException("invalid_audio_frame", "Binary frame must contain a 24-byte header.");
            }
            if (value[0] != Version)
            {
                throw new RealtimeProtocolException("unsupported_version", "Audio frame version is not supported.");
            }
            if (value[3] != 0)
            {
                throw new RealtimeProtocolException("invalid_audio_frame", "Reserved audio header byte must be zero.");
            }
            if (value[1] != (byte)RealtimeAudioKind.InputPcm && value[1] != (byte)RealtimeAudioKind.OutputPcm)
            {
                throw new RealtimeProtocolException("invalid_audio_kind", "Audio frame kind is not supported.");
            }
            if ((value[2] & ~(byte)(RealtimeAudioFlags.Start | RealtimeAudioFlags.End)) != 0)
            {
                throw new RealtimeProtocolException("invalid_audio_flags", "Audio frame contains unknown flags.");
            }

            var sequence = ((uint)value[4] << 24)
                | ((uint)value[5] << 16)
                | ((uint)value[6] << 8)
                | value[7];
            var streamBytes = new byte[16];
            Buffer.BlockCopy(value, 8, streamBytes, 0, streamBytes.Length);
            var payload = new byte[value.Length - AudioHeaderBytes];
            Buffer.BlockCopy(value, AudioHeaderBytes, payload, 0, payload.Length);
            if (payload.Length % 2 != 0)
            {
                throw new RealtimeProtocolException("invalid_audio_frame_size", "PCM16 payload must be 16-bit aligned.");
            }
            return new RealtimeAudioFrame(
                (RealtimeAudioKind)value[1],
                (RealtimeAudioFlags)value[2],
                sequence,
                GuidFromRfc4122Bytes(streamBytes),
                payload);
        }

        public static byte[] EncodeInputAudio(Guid streamId, uint sequence, byte[] pcm, bool start)
        {
            if (pcm == null || pcm.Length != InputFrameBytes)
            {
                throw new RealtimeProtocolException(
                    "invalid_audio_frame_size",
                    $"Input PCM frames must contain exactly {InputFrameBytes} bytes.");
            }
            return new RealtimeAudioFrame(
                RealtimeAudioKind.InputPcm,
                start ? RealtimeAudioFlags.Start : RealtimeAudioFlags.None,
                sequence,
                streamId,
                pcm).Encode();
        }

        public static string ClientReady(string sessionId, long lastEventSequence)
        {
            return ClientEvent("client.ready", sessionId, new JObject
            {
                ["last_event_seq"] = lastEventSequence,
            });
        }

        public static string InputText(string sessionId, string text, out string turnId)
        {
            turnId = Guid.NewGuid().ToString("D");
            return ClientEvent("input.text", sessionId, new JObject
            {
                ["text"] = text,
            }, turnId);
        }

        public static string AudioStart(string sessionId, Guid streamId)
        {
            return ClientEvent("audio.start", sessionId, new JObject
            {
                ["stream_id"] = streamId.ToString("D"),
            });
        }

        public static string AudioCommit(string sessionId, Guid streamId)
        {
            return ClientEvent("audio.commit", sessionId, new JObject
            {
                ["stream_id"] = streamId.ToString("D"),
            });
        }

        public static string AudioStop(string sessionId, Guid streamId)
        {
            return ClientEvent("audio.stop", sessionId, new JObject
            {
                ["stream_id"] = streamId.ToString("D"),
                ["commit"] = false,
            });
        }

        public static string ResponseCancel(string sessionId, string responseId, string turnId)
        {
            var data = new JObject();
            if (!string.IsNullOrWhiteSpace(responseId)) data["response_id"] = responseId;
            if (!string.IsNullOrWhiteSpace(turnId)) data["turn_id"] = turnId;
            return ClientEvent("response.cancel", sessionId, data);
        }

        public static string PlaybackAcknowledgement(
            string sessionId,
            string state,
            string responseId,
            Guid streamId,
            double playedMilliseconds)
        {
            return ClientEvent("playback.ack", sessionId, new JObject
            {
                ["state"] = state,
                ["response_id"] = responseId,
                ["stream_id"] = streamId.ToString("D"),
                ["played_ms"] = Math.Max(0, playedMilliseconds),
            });
        }

        public static string Ping(string sessionId)
        {
            return ClientEvent("ping", sessionId, new JObject());
        }

        internal static byte[] GuidToRfc4122Bytes(Guid value)
        {
            var bytes = value.ToByteArray();
            Swap(bytes, 0, 3);
            Swap(bytes, 1, 2);
            Swap(bytes, 4, 5);
            Swap(bytes, 6, 7);
            return bytes;
        }

        internal static Guid GuidFromRfc4122Bytes(byte[] value)
        {
            if (value == null || value.Length != 16)
            {
                throw new ArgumentException("A UUID must contain exactly 16 bytes.", nameof(value));
            }
            var bytes = (byte[])value.Clone();
            Swap(bytes, 0, 3);
            Swap(bytes, 1, 2);
            Swap(bytes, 4, 5);
            Swap(bytes, 6, 7);
            return new Guid(bytes);
        }

        private static string ClientEvent(
            string type,
            string sessionId,
            JObject data,
            string turnId = null)
        {
            var value = new JObject
            {
                ["v"] = Version,
                ["type"] = type,
                ["event_id"] = Guid.NewGuid().ToString("D"),
                ["data"] = data,
            };
            if (!string.IsNullOrWhiteSpace(sessionId)) value["session_id"] = sessionId;
            if (!string.IsNullOrWhiteSpace(turnId)) value["turn_id"] = turnId;
            return value.ToString(Formatting.None);
        }

        private static void Swap(byte[] value, int left, int right)
        {
            (value[left], value[right]) = (value[right], value[left]);
        }
    }
}
