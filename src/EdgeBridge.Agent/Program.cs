using System.Text.Json;
using EdgeBridge.Agent;
using EdgeBridge.Protocol;

var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, args) =>
{
    args.Cancel = true;
    cancellation.Cancel();
};

var (config, configSource) = await LoadConfigAsync(args, cancellation.Token).ConfigureAwait(false);
Console.WriteLine($"EdgeBridge Agent config: {configSource}");
Console.WriteLine($"EdgeBridge Agent device: {config.DeviceId} ({config.DeviceName})");

IDisposable? disposableDevice = null;
try
{
    var device = DeviceFactory.Create(config);
    disposableDevice = device as IDisposable;
    var server = new AgentWebSocketServer(device, new Uri(config.Transports.WebSocket.Url));

    await server.RunAsync(cancellation.Token).ConfigureAwait(false);
}
catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
{
}
catch (Exception ex)
{
    Console.Error.WriteLine($"EdgeBridge Agent failed: {ex.Message}");

    if (ex.InnerException is not null)
    {
        Console.Error.WriteLine($"Cause: {ex.InnerException.Message}");
    }

    Environment.ExitCode = 1;
}
finally
{
    disposableDevice?.Dispose();
}

static async ValueTask<(AgentConfig Config, string Source)> LoadConfigAsync(
    string[] args,
    CancellationToken cancellationToken)
{
    var configPath = args.FirstOrDefault(arg => arg.StartsWith("--config=", StringComparison.OrdinalIgnoreCase))?
        .Split('=', 2)[1];

    if (configPath is null)
    {
        const string defaultConfigPath = "/etc/edgebridge/agent.json";

        if (!File.Exists(defaultConfigPath))
        {
            return (new AgentConfig(), "built-in defaults");
        }

        configPath = defaultConfigPath;
    }

    await using var stream = File.OpenRead(configPath);
    var config = await JsonSerializer.DeserializeAsync<AgentConfig>(stream, ProtocolJson.Options, cancellationToken).ConfigureAwait(false)
        ?? new AgentConfig();

    return (config, configPath);
}
