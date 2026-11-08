using System;
using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace DataPerson.DigitalHuman.Conversation
{
    public sealed class WebSocketConversationTransport : IConversationTransport, IDisposable
    {
        private readonly SemaphoreSlim sendGate = new(1, 1);
        private ClientWebSocket socket;
        private CancellationTokenSource receiveLifetime;
        private Task receiveTask;
        private int closedRaised;
        private bool disposed;

        public event Action<string> TextMessageReceived;
        public event Action<byte[]> BinaryMessageReceived;
        public event Action<Exception> Faulted;
        public event Action Closed;

        public bool IsConnected => socket?.State == WebSocketState.Open;

        public async Task ConnectAsync(Uri url, string subprotocol, CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            if (socket != null) throw new InvalidOperationException("The transport has already been used.");
            if (url == null || (url.Scheme != "ws" && url.Scheme != "wss"))
            {
                throw new ArgumentException("A ws:// or wss:// URL is required.", nameof(url));
            }
            if (string.IsNullOrWhiteSpace(subprotocol))
            {
                throw new ArgumentException("A WebSocket subprotocol is required.", nameof(subprotocol));
            }

            socket = new ClientWebSocket();
            socket.Options.AddSubProtocol(subprotocol);
            await socket.ConnectAsync(url, cancellationToken);
            receiveLifetime = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            receiveTask = ReceiveLoopAsync(receiveLifetime.Token);
        }

        public Task SendAudioAsync(byte[] pcmData, CancellationToken cancellationToken)
        {
            if (pcmData == null) throw new ArgumentNullException(nameof(pcmData));
            return SendAsync(new ArraySegment<byte>(pcmData), WebSocketMessageType.Binary, cancellationToken);
        }

        public Task SendEventAsync(string json, CancellationToken cancellationToken)
        {
            if (json == null) throw new ArgumentNullException(nameof(json));
            return SendAsync(new ArraySegment<byte>(Encoding.UTF8.GetBytes(json)), WebSocketMessageType.Text, cancellationToken);
        }

        public async Task DisconnectAsync(CancellationToken cancellationToken)
        {
            if (socket == null) return;
            receiveLifetime?.Cancel();
            try
            {
                if (socket.State == WebSocketState.Open || socket.State == WebSocketState.CloseReceived)
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "client_close", cancellationToken);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (WebSocketException)
            {
            }
            finally
            {
                RaiseClosed();
            }
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            receiveLifetime?.Cancel();
            receiveLifetime?.Dispose();
            socket?.Dispose();
            sendGate.Dispose();
        }

        private async Task SendAsync(
            ArraySegment<byte> value,
            WebSocketMessageType messageType,
            CancellationToken cancellationToken)
        {
            ThrowIfDisposed();
            if (!IsConnected) throw new InvalidOperationException("The realtime socket is not connected.");
            await sendGate.WaitAsync(cancellationToken);
            try
            {
                await socket.SendAsync(value, messageType, true, cancellationToken);
            }
            finally
            {
                sendGate.Release();
            }
        }

        private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            var buffer = new byte[8192];
            try
            {
                while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
                {
                    using var message = new MemoryStream();
                    WebSocketReceiveResult result;
                    do
                    {
                        result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), cancellationToken);
                        if (result.MessageType == WebSocketMessageType.Close)
                        {
                            RaiseClosed();
                            return;
                        }
                        message.Write(buffer, 0, result.Count);
                    }
                    while (!result.EndOfMessage);

                    var value = message.ToArray();
                    if (result.MessageType == WebSocketMessageType.Text)
                    {
                        TextMessageReceived?.Invoke(Encoding.UTF8.GetString(value));
                    }
                    else if (result.MessageType == WebSocketMessageType.Binary)
                    {
                        BinaryMessageReceived?.Invoke(value);
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (Exception exception)
            {
                Faulted?.Invoke(exception);
            }
            finally
            {
                RaiseClosed();
            }
        }

        private void RaiseClosed()
        {
            if (Interlocked.Exchange(ref closedRaised, 1) == 0) Closed?.Invoke();
        }

        private void ThrowIfDisposed()
        {
            if (disposed) throw new ObjectDisposedException(nameof(WebSocketConversationTransport));
        }
    }
}
