namespace WorkEasy.A2A.Agents;

/// <summary>
/// Context van één agentbeurt binnen een (mogelijk meerlaags) gesprek tussen agents.
/// Wordt via A2A-metadata doorgegeven aan aangeroepen agents.
/// </summary>
public sealed record RunContext
{
    public const string MetaTraceId = "workeasy.traceId";
    public const string MetaDepth = "workeasy.depth";
    public const string MetaChain = "workeasy.chain";
    public const string MetaMaxDepth = "workeasy.maxDepth";
    public const string MetaCaller = "workeasy.caller";

    /// <summary>Id waaronder het hele gesprek (inclusief alle doorverwijzingen) wordt gelogd.</summary>
    public string TraceId { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>0 = rechtstreeks door een gebruiker aangeroepen; 1 = door een agent; enz.</summary>
    public int Depth { get; init; }

    /// <summary>Agents die al in deze keten actief zijn (om lussen te voorkomen).</summary>
    public IReadOnlyList<string> Chain { get; init; } = Array.Empty<string>();

    /// <summary>Wie deze beurt startte: "gebruiker", een agent-id of een externe client.</summary>
    public string Caller { get; init; } = "gebruiker";

    /// <summary>Maximale diepte voor deze keten; wordt bepaald door de agent waar het gesprek begon.</summary>
    public int? MaxDepth { get; init; }

    public RunContext Enter(string agentId, int defaultMaxDepth, int? agentMaxDepth) => this with
    {
        Chain = Chain.Append(agentId).ToList(),
        MaxDepth = MaxDepth ?? agentMaxDepth ?? defaultMaxDepth
    };
}
