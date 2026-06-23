using System.Text.Json;
using EdgeBridge.Agent;
using EdgeBridge.Protocol;

var cancellation = new CancellationTokenSource();
Console.CancelKeyPress += (_, args) =>
{
    args.Cancel = true;
    cancellation.Cancel();
};

var config = await LoadConfigAsync(args, cancellation.Token).ConfigureAwait(false);
var device = new MockDevice(config);
var server = new AgentWebSocketServer(device, new Uri(config.Transports.WebSocket.Url));

await server.RunAsync(cancellation.Token).ConfigureAwait(false);

static async ValueTask<AgentConfig> LoadConfigAsync(string[] args, CancellationToken cancellationToken)
{
    var configPath = args.FirstOrDefault(arg => arg.StartsWith("--config=", StringComparison.OrdinalIgnoreCase))?
        .Split('=', 2)[1];

    if (configPath is null)
    {
        return new AgentConfig();
    }

    await using var stream = File.OpenRead(configPath);
    return await JsonSerializer.DeserializeAsync<AgentConfig>(stream, ProtocolJson.Options, cancellationToken).ConfigureAwait(false)
        ?? new AgentConfig();
}
