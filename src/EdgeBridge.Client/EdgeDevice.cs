using EdgeBridge.Abstractions;
using EdgeBridge.Transport.WebSockets;

namespace EdgeBridge.Client;

public static class EdgeDevice
{
    public static async ValueTask<IDevice> ConnectAsync(string endpoint, CancellationToken cancellationToken = default)
    {
        return await ConnectAsync(new Uri(endpoint), cancellationToken).ConfigureAwait(false);
    }

    public static async ValueTask<IDevice> ConnectAsync(Uri endpoint, CancellationToken cancellationToken = default)
    {
        var transport = new WebSocketTransport();
        var device = new RemoteDevice(endpoint, transport);
        await device.StartAsync(cancellationToken).ConfigureAwait(false);
        return device;
    }
}
