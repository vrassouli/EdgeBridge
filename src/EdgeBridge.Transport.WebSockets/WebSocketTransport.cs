using System.Net.WebSockets;
using EdgeBridge.Protocol;

namespace EdgeBridge.Transport.WebSockets;

public sealed class WebSocketTransport : ITransport
{
    public async ValueTask<ITransportConnection> ConnectAsync(Uri endpoint, CancellationToken cancellationToken = default)
    {
        var socket = new ClientWebSocket();
        await socket.ConnectAsync(endpoint, cancellationToken).ConfigureAwait(false);
        return new WebSocketTransportConnection(socket);
    }
}

public sealed class WebSocketTransportConnection : ITransportConnection
{
    private readonly WebSocket _socket;

    public WebSocketTransportConnection(WebSocket socket)
    {
        _socket = socket;
    }

    public async ValueTask SendAsync(ProtocolMessage message, CancellationToken cancellationToken = default)
    {
        var payload = WebSocketMessageSerializer.Serialize(message);
        await _socket.SendAsync(payload, WebSocketMessageType.Text, true, cancellationToken).ConfigureAwait(false);
    }

    public async IAsyncEnumerable<ProtocolMessage> ReceiveAsync(
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var buffer = new byte[64 * 1024];

        while (_socket.State == WebSocketState.Open && !cancellationToken.IsCancellationRequested)
        {
            using var stream = new MemoryStream();
            WebSocketReceiveResult result;

            do
            {
                result = await _socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    yield break;
                }

                stream.Write(buffer, 0, result.Count);
            }
            while (!result.EndOfMessage);

            yield return WebSocketMessageSerializer.Deserialize(stream.ToArray());
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Closing EdgeBridge connection", CancellationToken.None)
                .ConfigureAwait(false);
        }

        _socket.Dispose();
    }
}

