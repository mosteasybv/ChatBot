using System.Text.Json;
using LlmTornado.Chat;
using LlmTornado.Code;
using Microsoft.Extensions.Options;
using WorkEasy.A2A.A2AProtocol;
using WorkEasy.A2A.Agents;
using WorkEasy.A2A.Configuration;

namespace WorkEasy.A2A.Api;

public sealed record ChatTurn(string Role, JsonElement Content);

public sealed record AgentChatRequest(List<ChatTurn> Messages);

/// <summary>REST-API waarmee WorkEasy agents beheert en test.</summary>
public static class ManagementEndpoints
{
    public const string UserHeader = "X-WorkEasy-User";

    public static void MapManagementApi(this IEndpointRouteBuilder app)
    {
        var api = app.MapGroup("/api");

        api.MapGet("/status", (AgentStore store, IOptions<A2AOptions> options) => Results.Ok(new
        {
            status = "ok",
            agents = store.List().Count,
            openWebUiBaseUrl = options.Value.OpenWebUiBaseUrl,
            openWebUiApiKeyConfigured = !string.IsNullOrEmpty(options.Value.OpenWebUiApiKey),
            maxDelegationDepth = options.Value.MaxDelegationDepth,
            providers = TornadoClientFactory.SupportedProviders
        }));

        api.MapGet("/agents", (AgentStore store, A2AAgentServer server) =>
            Results.Ok(store.List().Select(a => View(a, server))));

        api.MapGet("/agents/{id}", (string id, AgentStore store, A2AAgentServer server) =>
            store.Get(id) is { } agent ? Results.Ok(View(agent, server)) : NotFound(id));

        api.MapPost("/agents", async (AgentDefinition agent, HttpRequest req, AgentStore store, A2AAgentServer server) =>
        {
            try
            {
                var created = await store.CreateAsync(agent, req.Headers[UserHeader].FirstOrDefault());
                return Results.Ok(View(created, server));
            }
            catch (AgentValidationException ex)
            {
                return Results.BadRequest(new { detail = string.Join(" ", ex.Errors), errors = ex.Errors });
            }
        });

        api.MapPut("/agents/{id}", async (string id, AgentDefinition agent, AgentStore store, A2AAgentServer server) =>
        {
            try
            {
                var updated = await store.UpdateAsync(id, agent);
                return updated is null ? NotFound(id) : Results.Ok(View(updated, server));
            }
            catch (AgentValidationException ex)
            {
                return Results.BadRequest(new { detail = string.Join(" ", ex.Errors), errors = ex.Errors });
            }
        });

        api.MapDelete("/agents/{id}", async (string id, AgentStore store) =>
            await store.DeleteAsync(id) ? Results.Ok(new { deleted = true }) : NotFound(id));

        api.MapGet("/agents/{id}/card", (string id, AgentStore store, A2AAgentServer server) =>
            store.Get(id) is { } agent ? Results.Json(server.BuildCard(agent), global::A2A.A2AJsonUtilities.DefaultOptions) : NotFound(id));

        // Test een agent vanuit WorkEasy; het antwoord bevat ook het volledige verloop tussen de agents.
        api.MapPost("/agents/{id}/chat", async (
            string id, AgentChatRequest body, HttpRequest req,
            AgentStore store, AgentRuntime runtime, ConversationTraceStore traces, CancellationToken ct) =>
        {
            var agent = store.Get(id);
            if (agent is null) return NotFound(id);
            if (!agent.Enabled) return Results.BadRequest(new { detail = "Deze agent is uitgeschakeld." });

            var history = ToTornadoMessages(body.Messages);
            if (!history.Any(m => m.Role == ChatMessageRoles.User))
                return Results.BadRequest(new { detail = "Geen gebruikersbericht ontvangen." });

            try
            {
                var caller = req.Headers[UserHeader].FirstOrDefault() is { Length: > 0 } u ? $"gebruiker:{u}" : "gebruiker";
                var result = await runtime.RunAsync(agent, history, new RunContext { Caller = caller }, ct);
                return Results.Ok(new { content = result.Content, traceId = result.TraceId, trace = traces.Get(result.TraceId) });
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Results.Json(new { detail = ex.Message }, statusCode: StatusCodes.Status502BadGateway);
            }
        });

        api.MapGet("/traces/{traceId}", (string traceId, ConversationTraceStore traces) =>
            traces.Get(traceId) is { } trace ? Results.Ok(trace) : Results.NotFound(new { detail = "Gesprek niet gevonden." }));

        // Netwerkoverzicht: wie mag met wie praten.
        api.MapGet("/network", (AgentStore store) =>
        {
            var agents = store.List();
            return Results.Ok(new
            {
                nodes = agents.Select(a => new { id = a.Id, name = a.Name, enabled = a.Enabled, expose = a.A2A.Expose })
                    .Concat(agents.SelectMany(a => a.A2A.RemoteAgents)
                        .Select(r => new { id = "remote:" + r.Name.ToLowerInvariant(), name = r.Name, enabled = true, expose = true })
                        .DistinctBy(n => n.id)),
                edges = agents.SelectMany(a =>
                    a.A2A.Peers.Select(p => new { from = a.Id, to = p })
                        .Concat(a.A2A.RemoteAgents.Select(r => new { from = a.Id, to = "remote:" + r.Name.ToLowerInvariant() })))
            });
        });
    }

    public static List<ChatMessage> ToTornadoMessages(IEnumerable<ChatTurn>? turns)
    {
        var result = new List<ChatMessage>();
        foreach (var turn in turns ?? [])
        {
            var role = turn.Role?.ToLowerInvariant() switch
            {
                "system" => ChatMessageRoles.System,
                "assistant" => ChatMessageRoles.Assistant,
                "user" => ChatMessageRoles.User,
                _ => (ChatMessageRoles?)null
            };
            if (role is null) continue;

            var text = ContentToText(turn.Content);
            if (!string.IsNullOrEmpty(text))
                result.Add(new ChatMessage(role.Value, text));
        }
        return result;
    }

    /// <summary>OpenAI-content is een string of een lijst met delen ({type:"text", text:"…"}).</summary>
    public static string ContentToText(JsonElement content) => content.ValueKind switch
    {
        JsonValueKind.String => content.GetString() ?? "",
        JsonValueKind.Array => string.Join("\n", content.EnumerateArray()
            .Where(p => p.ValueKind == JsonValueKind.Object
                        && p.TryGetProperty("type", out var t) && t.GetString() == "text"
                        && p.TryGetProperty("text", out _))
            .Select(p => p.GetProperty("text").GetString())),
        _ => ""
    };

    private static object View(AgentDefinition agent, A2AAgentServer server) => new
    {
        agent.Id,
        agent.Name,
        agent.Description,
        agent.ProfileImageUrl,
        agent.Tags,
        agent.Enabled,
        agent.Source,
        agent.SystemPrompt,
        agent.Params,
        agent.KnowledgeIds,
        agent.ToolIds,
        agent.Features,
        a2a = agent.A2A,
        agent.CreatedBy,
        agent.CreatedAt,
        agent.UpdatedAt,
        endpoints = new
        {
            a2a = server.AgentUrl(agent.Id),
            card = server.AgentUrl(agent.Id) + "/.well-known/agent-card.json",
            openAiModel = OpenAiCompatEndpoints.ModelPrefix + agent.Id
        }
    };

    private static IResult NotFound(string id) => Results.NotFound(new { detail = $"Agent '{id}' niet gevonden." });
}
