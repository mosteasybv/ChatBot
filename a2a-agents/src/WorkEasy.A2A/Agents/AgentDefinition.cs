using System.Text.Json.Serialization;

namespace WorkEasy.A2A.Agents;

/// <summary>
/// Configuratie van één A2A-agent. De velden spiegelen een GPT (model) uit de WorkEasy-werkruimte:
/// basismodel, systeemprompt, parameters, kennis, tools en mogelijkheden. Daarbovenop komen de
/// A2A-instellingen: met welke andere agents deze agent mag praten en hoe hij zich presenteert.
/// </summary>
public sealed class AgentDefinition
{
    /// <summary>Unieke, URL-veilige id (bijv. "juridisch-adviseur").</summary>
    public string Id { get; set; } = "";

    public string Name { get; set; } = "";

    /// <summary>Wordt in de agent card gebruikt en door andere agents gelezen om te bepalen wanneer ze deze agent inschakelen.</summary>
    public string Description { get; set; } = "";

    public string ProfileImageUrl { get; set; } = "/static/favicon.png";

    public List<string> Tags { get; set; } = new();

    public bool Enabled { get; set; } = true;

    public AgentModelSource Source { get; set; } = new();

    /// <summary>Extra instructies voor de agent, bovenop de systeemprompt van de gekoppelde GPT.</summary>
    public string SystemPrompt { get; set; } = "";

    public AgentParams Params { get; set; } = new();

    /// <summary>Extra kennisbanken (WorkEasy knowledge-id's) die bij elke vraag worden doorzocht.</summary>
    public List<string> KnowledgeIds { get; set; } = new();

    /// <summary>WorkEasy-tools (toolkits) die de agent mag gebruiken.</summary>
    public List<string> ToolIds { get; set; } = new();

    public AgentFeatures Features { get; set; } = new();

    public AgentA2ASettings A2A { get; set; } = new();

    public string? CreatedBy { get; set; }

    public long CreatedAt { get; set; }

    public long UpdatedAt { get; set; }
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum AgentSourceType
{
    /// <summary>
    /// Gebruik een model/GPT uit WorkEasy (via /api/chat/completions). Kennis, filters, systeemprompt en
    /// parameters van die GPT worden automatisch toegepast — identiek aan chatten in WorkEasy.
    /// </summary>
    Workspace,

    /// <summary>Spreek rechtstreeks een LLM-provider aan via LlmTornado (OpenAI, Anthropic, Azure, Google, …).</summary>
    Provider
}

public sealed class AgentModelSource
{
    public AgentSourceType Type { get; set; } = AgentSourceType.Workspace;

    /// <summary>Bij Workspace: id van het WorkEasy-model/GPT. Bij Provider: modelnaam bij de provider (bijv. gpt-4o).</summary>
    public string Model { get; set; } = "";

    /// <summary>Bij Provider: LlmTornado-provider (OpenAi, Anthropic, AzureOpenAi, Google, Mistral, Groq, DeepSeek, Custom, …).</summary>
    public string? Provider { get; set; }

    /// <summary>Bij Provider: naam van de omgevingsvariabele met de API-sleutel (sleutels zelf worden nooit opgeslagen).</summary>
    public string? ApiKeyEnv { get; set; }

    /// <summary>Bij Provider "Custom": basis-URL van een OpenAI-compatibele API (bijv. http://ollama:11434/v1).</summary>
    public string? BaseUrl { get; set; }
}

public sealed class AgentParams
{
    public double? Temperature { get; set; }
    public double? TopP { get; set; }
    public int? MaxTokens { get; set; }
    public int? Seed { get; set; }
}

public sealed class AgentFeatures
{
    public bool WebSearch { get; set; }
    public bool ImageGeneration { get; set; }
    public bool CodeInterpreter { get; set; }
}

public sealed class AgentA2ASettings
{
    /// <summary>Id's van andere agents in deze service waarmee deze agent mag overleggen.</summary>
    public List<string> Peers { get; set; } = new();

    /// <summary>Externe A2A-agents (andere servers) waarmee deze agent mag overleggen.</summary>
    public List<RemoteAgentReference> RemoteAgents { get; set; } = new();

    /// <summary>Mag deze agent door andere agents/clients via A2A worden aangeroepen?</summary>
    public bool Expose { get; set; } = true;

    /// <summary>Maximale diepte van doorverwijzingen die vanaf deze agent mogen starten (null = servicestandaard).</summary>
    public int? MaxDelegationDepth { get; set; }

    /// <summary>Vaardigheden die in de agent card worden gepubliceerd.</summary>
    public List<AgentSkillDefinition> Skills { get; set; } = new();
}

public sealed class RemoteAgentReference
{
    /// <summary>Korte naam, wordt gebruikt in de toolnaam (ask_agent_{name}).</summary>
    public string Name { get; set; } = "";

    /// <summary>A2A JSON-RPC-endpoint van de externe agent.</summary>
    public string Url { get; set; } = "";

    public string Description { get; set; } = "";

    /// <summary>Optioneel: omgevingsvariabele met een Bearer-token voor de externe agent.</summary>
    public string? ApiKeyEnv { get; set; }
}

public sealed class AgentSkillDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public List<string> Examples { get; set; } = new();
    public List<string> Tags { get; set; } = new();
}
