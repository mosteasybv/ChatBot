using System.Collections.Concurrent;
using System.Text.Json;
using A2A;
using A2A.AspNetCore;
using LlmTornado.Chat;
using LlmTornado.Code;
using Microsoft.Extensions.Options;
using WorkEasy.A2A.Agents;
using WorkEasy.A2A.Configuration;

namespace WorkEasy.A2A.A2AProtocol;

/// <summary>
/// Publiceert elke agent als A2A-server:
/// <list type="bullet">
/// <item><c>POST /a2a/{agentId}</c> — JSON-RPC (message/send, message/stream, tasks/get, tasks/cancel)</item>
/// <item><c>GET /a2a/{agentId}/.well-known/agent-card.json</c> — agent card</item>
/// </list>
/// Eén <see cref="TaskManager"/> bedient alle agents; de agent wordt bepaald uit de route.
/// </summary>
public sealed class A2AAgentServer
{
    private const int MaxContexts = 1000;
    private const int MaxContextMessages = 40;

    private readonly AgentStore _store;
    private readonly AgentRuntime _runtime;
    private readonly IHttpContextAccessor _http;
    private readonly IOptions<A2AOptions> _options;
    private readonly ConcurrentDictionary<string, List<ChatMessage>> _contexts = new();
    private readonly ConcurrentQueue<string> _contextOrder = new();

    public TaskManager TaskManager { get; } = new();

    public A2AAgentServer(AgentStore store, AgentRuntime runtime, IHttpContextAccessor http, IOptions<A2AOptions> options)
    {
        _store = store;
        _runtime = runtime;
        _http = http;
        _options = options;
        TaskManager.OnMessageReceived = OnMessageReceivedAsync;
        TaskManager.OnAgentCardQuery = (url, ct) => Task.FromResult(BuildCard(CurrentAgent(), url));
    }

    public void Map(IEndpointRouteBuilder endpoints)
    {
        endpoints.MapA2A(TaskManager, "/a2a/{agentId}");

        IResult Card(string agentId)
        {
            var agent = _store.Get(agentId);
            return agent is null || !agent.Enabled || !agent.A2A.Expose
                ? Results.NotFound(new { detail = $"Agent '{agentId}' niet gevonden." })
                : Results.Json(BuildCard(agent), A2AJsonUtilities.DefaultOptions);
        }

        // Huidige (agent-card.json) en oudere (agent.json) A2A-ontdekkingspaden.
        endpoints.MapGet("/a2a/{agentId}/.well-known/agent-card.json", Card);
        endpoints.MapGet("/a2a/{agentId}/.well-known/agent.json", Card);
    }

    private AgentDefinition CurrentAgent()
    {
        var id = _http.HttpContext?.GetRouteValue("agentId") as string;
        var agent = id is null ? null : _store.Get(id);
        if (agent is null || !agent.Enabled || !agent.A2A.Expose)
            throw new A2AException($"Agent '{id}' bestaat niet of is niet beschikbaar via A2A.", A2AErrorCode.InvalidParams);
        return agent;
    }

    private async Task<A2AResponse> OnMessageReceivedAsync(MessageSendParams request, CancellationToken ct)
    {
        var agent = CurrentAgent();
        var incoming = request.Message;
        var text = A2AText.FromParts(incoming.Parts);

        var meta = request.Metadata ?? incoming.Metadata ?? new Dictionary<string, JsonElement>();
        var ctx = new RunContext
        {
            TraceId = GetString(meta, RunContext.MetaTraceId) ?? Guid.NewGuid().ToString("N"),
            Depth = GetInt(meta, RunContext.MetaDepth) ?? 0,
            Chain = GetStringList(meta, RunContext.MetaChain),
            Caller = GetString(meta, RunContext.MetaCaller) ?? "a2a-client",
            MaxDepth = GetInt(meta, RunContext.MetaMaxDepth)
        };

        var contextId = string.IsNullOrWhiteSpace(incoming.ContextId) ? Guid.NewGuid().ToString("N") : incoming.ContextId;
        var history = GetContext($"{agent.Id}|{contextId}");

        List<ChatMessage> turn;
        lock (history)
        {
            history.Add(new ChatMessage(ChatMessageRoles.User, text));
            turn = history.ToList();
        }

        var result = await _runtime.RunAsync(agent, turn, ctx, ct);

        lock (history)
        {
            history.Add(new ChatMessage(ChatMessageRoles.Assistant, result.Content));
            if (history.Count > MaxContextMessages) history.RemoveRange(0, history.Count - MaxContextMessages);
        }

        return new AgentMessage
        {
            Role = MessageRole.Agent,
            MessageId = Guid.NewGuid().ToString("N"),
            ContextId = contextId,
            Parts = [new TextPart { Text = result.Content }],
            Metadata = new Dictionary<string, JsonElement>
            {
                [RunContext.MetaTraceId] = JsonSerializer.SerializeToElement(result.TraceId),
                ["workeasy.agentId"] = JsonSerializer.SerializeToElement(agent.Id)
            }
        };
    }

    public AgentCard BuildCard(AgentDefinition agent, string? requestedUrl = null)
    {
        var url = AgentUrl(agent.Id, requestedUrl);
        var skills = agent.A2A.Skills.Count > 0
            ? agent.A2A.Skills.Select(s => new AgentSkill
            {
                Id = string.IsNullOrWhiteSpace(s.Id) ? s.Name.ToLowerInvariant().Replace(' ', '-') : s.Id,
                Name = s.Name,
                Description = s.Description,
                Examples = s.Examples,
                Tags = s.Tags
            }).ToList()
            : [new AgentSkill { Id = agent.Id, Name = agent.Name, Description = agent.Description, Tags = agent.Tags }];

        var card = new AgentCard
        {
            Name = agent.Name,
            Description = agent.Description,
            Url = url,
            IconUrl = agent.ProfileImageUrl?.StartsWith("http", StringComparison.OrdinalIgnoreCase) == true ? agent.ProfileImageUrl : null,
            Version = "1.0.0",
            Provider = new AgentProvider { Organization = "WorkEasy", Url = PublicBaseUrl() ?? url },
            Capabilities = new AgentCapabilities { Streaming = false, PushNotifications = false },
            DefaultInputModes = ["text"],
            DefaultOutputModes = ["text"],
            Skills = skills
        };

        if (!string.IsNullOrEmpty(_options.Value.ApiKey))
        {
            card.SecuritySchemes = new Dictionary<string, SecurityScheme>
            {
                ["bearer"] = new HttpAuthSecurityScheme("bearer", null)
            };
            card.Security = [new Dictionary<string, string[]> { ["bearer"] = [] }];
        }

        return card;
    }

    public string AgentUrl(string agentId, string? requestedUrl = null)
    {
        var baseUrl = PublicBaseUrl();
        if (baseUrl is not null) return $"{baseUrl}/a2a/{agentId}";

        var req = _http.HttpContext?.Request;
        if (req is not null) return $"{req.Scheme}://{req.Host}{req.PathBase}/a2a/{agentId}";
        return requestedUrl ?? $"/a2a/{agentId}";
    }

    private string? PublicBaseUrl() =>
        string.IsNullOrWhiteSpace(_options.Value.PublicBaseUrl) ? null : _options.Value.PublicBaseUrl.TrimEnd('/');

    private List<ChatMessage> GetContext(string key) => _contexts.GetOrAdd(key, k =>
    {
        _contextOrder.Enqueue(k);
        while (_contextOrder.Count > MaxContexts && _contextOrder.TryDequeue(out var old))
            _contexts.TryRemove(old, out _);
        return new List<ChatMessage>();
    });

    private static string? GetString(Dictionary<string, JsonElement> meta, string key) =>
        meta.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static int? GetInt(Dictionary<string, JsonElement> meta, string key) =>
        meta.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : null;

    private static IReadOnlyList<string> GetStringList(Dictionary<string, JsonElement> meta, string key) =>
        meta.TryGetValue(key, out var v) && v.ValueKind == JsonValueKind.Array
            ? v.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).ToList()
            : Array.Empty<string>();
}
