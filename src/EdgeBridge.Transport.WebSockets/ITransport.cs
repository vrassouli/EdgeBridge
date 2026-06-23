using EdgeBridge.Protocol;

namespace EdgeBridge.Transport.WebSockets;

public interface ITransportConnection : IAsyncDisposable
{
    ValueTask SendAsync(ProtocolMessage message, CancellationToken cancellationToken = default);

    IAsyncEnumerable<ProtocolMessage> ReceiveAsync(CancellationToken cancellationToken = default);
}

public interface ITransport
{
    ValueTask<ITransportConnection> ConnectAsync(Uri endpoint, CancellationToken cancellationToken = default);
}

