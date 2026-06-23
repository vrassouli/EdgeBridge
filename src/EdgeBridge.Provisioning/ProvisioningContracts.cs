namespace EdgeBridge.Provisioning;

public enum ProvisioningState
{
    NotRequired,
    WaitingForCredentials,
    ApplyingCredentials,
    Connected,
    Failed
}

public sealed record WifiCredentials(string Ssid, string Password);

public interface IProvisioningService
{
    ValueTask<ProvisioningState> GetStateAsync(CancellationToken cancellationToken = default);

    ValueTask StartAsync(CancellationToken cancellationToken = default);

    ValueTask StopAsync(CancellationToken cancellationToken = default);
}

public interface IWifiConfigurator
{
    ValueTask<bool> IsConfiguredAsync(CancellationToken cancellationToken = default);

    ValueTask ApplyAsync(WifiCredentials credentials, CancellationToken cancellationToken = default);
}

public interface IBluetoothProvisioningTransport
{
    IAsyncEnumerable<WifiCredentials> ReceiveCredentialsAsync(CancellationToken cancellationToken = default);
}

