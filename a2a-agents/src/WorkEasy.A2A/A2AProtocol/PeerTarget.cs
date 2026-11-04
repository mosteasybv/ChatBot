namespace WorkEasy.A2A.A2AProtocol;

/// <summary>Een agent (lokaal of extern) die als tool aan een andere agent wordt aangeboden.</summary>
public sealed record PeerTarget(
    string Key,
    string ToolName,
    string DisplayName,
    string Description,
    string? AgentId,
    string? Url,
    string? ApiKeyEnv)
{
    public string ToolDescription =>
        $"Stel een vraag aan collega-agent '{DisplayName}' en ontvang diens antwoord. {Description}".Trim();
}
