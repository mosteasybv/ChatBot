namespace WorkEasy.A2A.Configuration;

/// <summary>
/// Instellingen van de A2A-agentservice. Te zetten via appsettings.json (sectie "A2A")
/// of via omgevingsvariabelen, bijvoorbeeld <c>A2A__OpenWebUiApiKey</c>.
/// </summary>
public sealed class A2AOptions
{
    public const string Section = "A2A";

    /// <summary>Basis-URL van WorkEasy (Open WebUI), zonder /api. Bijv. http://open-webui:8080</summary>
    public string OpenWebUiBaseUrl { get; set; } = "http://localhost:8080";

    /// <summary>API-sleutel van een WorkEasy-gebruiker (Instellingen → Account → API-sleutels).</summary>
    public string OpenWebUiApiKey { get; set; } = "";

    /// <summary>
    /// Sleutel waarmee clients (WorkEasy-backend, andere agents, externe A2A-clients) zich aanmelden.
    /// Leeg = geen authenticatie (alleen voor lokale ontwikkeling).
    /// </summary>
    public string ApiKey { get; set; } = "";

    /// <summary>Publieke basis-URL van deze service; gebruikt in agent cards. Leeg = afgeleid van het verzoek.</summary>
    public string PublicBaseUrl { get; set; } = "";

    /// <summary>
    /// Interne basis-URL waarmee agents elkaar via A2A aanroepen. Leeg = de URL waarop deze service luistert.
    /// </summary>
    public string InternalBaseUrl { get; set; } = "";

    /// <summary>Pad naar het JSON-bestand waarin agentconfiguraties worden opgeslagen.</summary>
    public string DataPath { get; set; } = "data/agents.json";

    /// <summary>Standaard maximale diepte van agent → agent doorverwijzingen (voorkomt eindeloze lussen).</summary>
    public int MaxDelegationDepth { get; set; } = 3;

    /// <summary>Maximaal aantal model-aanroepen (tool-rondes) per agentbeurt.</summary>
    public int MaxToolIterations { get; set; } = 8;

    public int RequestTimeoutSeconds { get; set; } = 300;
}
