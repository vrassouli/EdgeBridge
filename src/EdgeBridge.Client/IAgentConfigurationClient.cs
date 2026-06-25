using EdgeBridge.Protocol;

namespace EdgeBridge.Client;

public interface IAgentConfigurationClient
{
    ValueTask<AgentConfigDto> GetAgentConfigAsync(CancellationToken cancellationToken = default);

    ValueTask<AgentConfigUpdateResult> UpdateAgentConfigAsync(
        AgentConfigDto config,
        CancellationToken cancellationToken = default);
}
