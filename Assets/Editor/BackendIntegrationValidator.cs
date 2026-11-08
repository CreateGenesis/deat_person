using System;
using System.Linq;
using DataPerson.DigitalHuman.Conversation;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

public static class BackendIntegrationValidator
{
    [MenuItem("Tools/Digital Human/Validate Backend Integration")]
    public static void Validate()
    {
        ValidateAudioFrame();
        ValidateEvents();
        ValidateResampler();
        ValidateTicketUrl();
        Debug.Log("Backend integration validation passed: protocol, PCM framing, resampling, and ticket URL.");
    }

    private static void ValidateAudioFrame()
    {
        var streamId = Guid.Parse("00112233-4455-6677-8899-aabbccddeeff");
        var payload = new byte[RealtimeProtocol.InputFrameBytes];
        var encoded = RealtimeProtocol.EncodeInputAudio(streamId, 0x01020304, payload, true);
        Require(encoded.Length == 664, "Input frame must contain a 24-byte header and 640-byte payload.");
        Require(encoded.Take(8).SequenceEqual(new byte[] { 1, 1, 1, 0, 1, 2, 3, 4 }), "Audio header fields are incorrect.");
        Require(
            encoded.Skip(8).Take(16).SequenceEqual(new byte[]
            {
                0x00, 0x11, 0x22, 0x33, 0x44, 0x55, 0x66, 0x77,
                0x88, 0x99, 0xaa, 0xbb, 0xcc, 0xdd, 0xee, 0xff,
            }),
            "UUID must use RFC 4122 network byte order.");

        var output = new RealtimeAudioFrame(
            RealtimeAudioKind.OutputPcm,
            RealtimeAudioFlags.Start | RealtimeAudioFlags.End,
            0,
            streamId,
            Array.Empty<byte>()).Encode();
        var decoded = RealtimeProtocol.DecodeAudioFrame(output);
        Require(decoded.StreamId == streamId, "Audio UUID round trip failed.");
        Require(decoded.Payload.Length == 0, "A header-only output END must be accepted.");
    }

    private static void ValidateEvents()
    {
        var text = RealtimeProtocol.InputText("session-1", "hello", out var turnId);
        var value = JObject.Parse(text);
        Require(value.Value<int>("v") == 1, "Client event version is incorrect.");
        Require(value.Value<string>("type") == "input.text", "Text event type is incorrect.");
        Require(value.Value<string>("session_id") == "session-1", "Session ID is missing.");
        Require(value.Value<string>("turn_id") == turnId, "Turn ID is missing.");
        Require(value["data"]?.Value<string>("text") == "hello", "Text payload is incorrect.");

        var serverEvent = RealtimeServerEvent.Parse(
            "{\"v\":1,\"type\":\"response.text.delta\",\"seq\":2,\"event_id\":\"e\",\"session_id\":\"s\",\"data\":{\"delta\":\"ok\"}}");
        Require(serverEvent.Sequence == 2, "Server event sequence was not parsed.");
        Require(serverEvent.Data.Value<string>("delta") == "ok", "Server event data was not parsed.");
    }

    private static void ValidateResampler()
    {
        var encoder = new Pcm16FrameEncoder(48000, 1);
        var source = Enumerable.Repeat(0.25f, 960).ToArray();
        var frames = encoder.Push(source);
        Require(frames.Count == 1, "A 20 ms 48 kHz block must become one 20 ms 16 kHz frame.");
        Require(frames[0].Length == RealtimeProtocol.InputFrameBytes, "Resampled input frame size is incorrect.");
        Require(encoder.FlushPaddedFrame() == null, "An exact frame must not leave a padded tail.");
    }

    private static void ValidateTicketUrl()
    {
        var options = new BackendConnectionOptions("https://agent.example.com/base", "test-key", "Block_test_1");
        var client = new BackendSessionClient(options);
        var url = client.ResolveWebSocketUri(new BackendSessionTicket
        {
            WebSocketUrl = "/api/v1/realtime?ticket=once",
        });
        Require(url.AbsoluteUri == "wss://agent.example.com/api/v1/realtime?ticket=once", "Ticket URL resolution is incorrect.");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
