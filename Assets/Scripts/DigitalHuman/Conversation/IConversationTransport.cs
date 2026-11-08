using System;
using System.Threading;
using System.Threading.Tasks;

namespace DataPerson.DigitalHuman.Conversation
{
    public interface IConversationTransport
    {
        event Action<string> TextMessageReceived;
        event Action<byte[]> BinaryMessageReceived;
        event Action<Exception> Faulted;
        event Action Closed;

        bool IsConnected { get; }

        Task ConnectAsync(Uri url, string subprotocol, CancellationToken cancellationToken);
        Task SendAudioAsync(byte[] pcmData, CancellationToken cancellationToken);
        Task SendEventAsync(string json, CancellationToken cancellationToken);
        Task DisconnectAsync(CancellationToken cancellationToken);
    }
}
