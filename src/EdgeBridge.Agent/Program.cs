using System.Text.Json;
using EdgeBridge.Agent;
using EdgeBridge.Protocol;

var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, args) =>
{
    args.Cancel = true;
    cancellation.Cancel();
};

var configStore = await AgentConfigStore.LoadAsync(args, cancellation.Token).ConfigureAwait(false);
var config = configStore.Current;
Console.WriteLine($"EdgeBridge Agent config: {configStore.Source}");
Console.WriteLine($"EdgeBridge Agent device: {config.DeviceId} ({config.DeviceName})");

IDisposable? disposableDevice = null;
try
{
    var device = DeviceFactory.Create(config);
    disposableDevice = device as IDisposable;
    var server = new AgentWebSocketServer(device, configStore, new Uri(config.Transports.WebSocket.Url));

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

internal sealed class AgentConfigStore
{
    private readonly SemaphoreSlim _lock = new(1, 1);

    private AgentConfigStore(AgentConfig current, string path, string source)
    {
        Current = current;
        Path = path;
        Source = source;
    }

    public AgentConfig Current { get; private set; }

    public string Path { get; }

    public string Source { get; }

    public static async ValueTask<AgentConfigStore> LoadAsync(
        string[] args,
        CancellationToken cancellationToken)
    {
        var configPath = args.FirstOrDefault(arg => arg.StartsWith("--config=", StringComparison.OrdinalIgnoreCase))?
            .Split('=', 2)[1];

        if (configPath is null)
        {
            const string defaultConfigPath = "/etc/edgebridge/agent.json";

            if (File.Exists(defaultConfigPath))
            {
                configPath = defaultConfigPath;
            }
            else
            {
                configPath = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "EdgeBridge",
                    "agent.json");
            }
        }

        if (!File.Exists(configPath))
        {
            return new AgentConfigStore(new AgentConfig(), configPath, $"built-in defaults; updates persist to {configPath}");
        }

        await using var stream = File.OpenRead(configPath);
        var config = await JsonSerializer.DeserializeAsync<AgentConfig>(stream, ProtocolJson.Options, cancellationToken).ConfigureAwait(false)
            ?? new AgentConfig();

        return new AgentConfigStore(config, configPath, configPath);
    }

    public async ValueTask<AgentConfigUpdateResult> UpdateAsync(
        AgentConfigDto config,
        CancellationToken cancellationToken)
    {
        var next = config.ToAgentConfig();
        Validate(next);

        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var directory = System.IO.Path.GetDirectoryName(Path);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            await using var stream = File.Create(Path);
            await JsonSerializer.SerializeAsync(stream, next, ProtocolJson.Options, cancellationToken).ConfigureAwait(false);
            Current = next;
        }
        finally
        {
            _lock.Release();
        }

        return new AgentConfigUpdateResult(
            Accepted: true,
            RestartRequired: true,
            Message: "Configuration was saved. Restart the Agent for runtime changes to take effect.",
            Config: Current.ToDto());
    }

    private static void Validate(AgentConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.DeviceId))
        {
            throw new InvalidOperationException("Device ID is required.");
        }

        if (string.IsNullOrWhiteSpace(config.DeviceName))
        {
            throw new InvalidOperationException("Device name is required.");
        }

        if (config.Hardware.PwmFrequency <= 0)
        {
            throw new InvalidOperationException("PWM frequency must be greater than zero.");
        }

        if (config.Transports.WebSocket.Enabled &&
            !Uri.TryCreate(config.Transports.WebSocket.Url, UriKind.Absolute, out _))
        {
            throw new InvalidOperationException("WebSocket URL must be an absolute URI.");
        }
    }
}
