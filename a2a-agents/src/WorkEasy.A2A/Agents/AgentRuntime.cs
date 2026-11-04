using System.Text;
using LlmTornado.Chat;
using LlmTornado.Code;
using LlmTornado.Common;
using Microsoft.Extensions.Options;
using Newtonsoft.Json.Linq;
using WorkEasy.A2A.A2AProtocol;
using WorkEasy.A2A.Configuration;

namespace WorkEasy.A2A.Agents;

public sealed record AgentRunResult(string Content, string TraceId);

/// <summary>
/// Voert één beurt van een agent uit met LlmTornado. Andere agents waarmee de agent mag praten worden
/// als tools ("ask_&lt;agent&gt;") aangeboden; roept het model zo'n tool aan, dan wordt de vraag via het
/// A2A-protocol naar die agent gestuurd en komt diens antwoord terug als toolresultaat.
/// </summary>
public sealed class AgentRuntime(
    AgentStore store,
    TornadoClientFactory clients,
    PeerInvoker peers,
    ConversationTraceStore traces,
    IOptions<A2AOptions> options,
    ILogger<AgentRuntime> logger)
{
    private const string ToolPrefix = "ask_";

    public async Task<AgentRunResult> RunAsync(
        AgentDefinition agent,
        IReadOnlyList<ChatMessage> history,
        RunContext context,
        CancellationToken ct)
    {
        var ctx = context.Enter(agent.Id, options.Value.MaxDelegationDepth, agent.A2A.MaxDelegationDepth);
        var lastUser = history.LastOrDefault(m => m.Role == ChatMessageRoles.User);
        traces.Add(ctx.TraceId, "request", ctx.Caller, agent.Id, Text(lastUser), ctx.Depth);

        try
        {
            var content = await RunLoopAsync(agent, history, ctx, ct);
            traces.Add(ctx.TraceId, "response", agent.Id, ctx.Caller, content, ctx.Depth);
            return new AgentRunResult(content, ctx.TraceId);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogError(ex, "Agent {Agent} faalde", agent.Id);
            traces.Add(ctx.TraceId, "error", agent.Id, ctx.Caller, ex.Message, ctx.Depth);
            throw;
        }
    }

    private async Task<string> RunLoopAsync(
        AgentDefinition agent, IReadOnlyList<ChatMessage> history, RunContext ctx, CancellationToken ct)
    {
        var (api, model) = clients.Create(agent);
        // Agents die al in deze keten actief zijn bieden we niet aan: dat zou een lus veroorzaken.
        var targets = ResolvePeers(agent)
            .Where(t => t.Value.AgentId is null || !ctx.Chain.Contains(t.Value.AgentId, StringComparer.OrdinalIgnoreCase))
            .ToDictionary(t => t.Key, t => t.Value, StringComparer.Ordinal);
        var canDelegate = targets.Count > 0 && ctx.Depth < (ctx.MaxDepth ?? options.Value.MaxDelegationDepth);

        var messages = new List<ChatMessage>();
        var systemPrompt = BuildSystemPrompt(agent, canDelegate ? targets : [], ctx);
        if (!string.IsNullOrWhiteSpace(systemPrompt))
            messages.Add(new ChatMessage(ChatMessageRoles.System, systemPrompt));
        messages.AddRange(history.Where(m => m.Role != ChatMessageRoles.System || string.IsNullOrEmpty(systemPrompt)));

        var tools = canDelegate
            ? targets.Values.Select(t => new Tool(new ToolFunction(t.ToolName, t.ToolDescription, AskSchema))).ToList()
            : null;

        var maxIterations = Math.Max(1, options.Value.MaxToolIterations);
        for (var iteration = 0; iteration < maxIterations; iteration++)
        {
            ct.ThrowIfCancellationRequested();
            var lastRound = iteration == maxIterations - 1;

            var request = new ChatRequest
            {
                Model = model,
                Messages = messages.ToList(),
                Temperature = agent.Params.Temperature,
                TopP = agent.Params.TopP,
                MaxTokens = agent.Params.MaxTokens,
                Seed = agent.Params.Seed,
                Tools = lastRound ? null : tools,
                CancellationToken = ct
            };

            if (agent.Source.Type == AgentSourceType.Workspace)
                request.OnSerialize = (json, _) => AddWorkspaceFields(json, agent);

            var result = await api.Chat.CreateChatCompletion(request)
                         ?? throw new InvalidOperationException("Geen antwoord van het model ontvangen.");

            var reply = result.Choices?.FirstOrDefault()?.Message
                        ?? throw new InvalidOperationException("Het model gaf een leeg antwoord.");

            if (reply.ToolCalls is not { Count: > 0 })
                return reply.Content ?? reply.GetMessageContent() ?? "";

            messages.Add(reply);

            // Voer de doorverwijzingen parallel uit; elk antwoord gaat als toolresultaat terug naar het model.
            var calls = reply.ToolCalls.Select(async call =>
            {
                var output = await ExecuteToolCallAsync(agent, call, targets, ctx, ct);
                return new ChatMessage(ChatMessageRoles.Tool, output) { ToolCallId = call.Id };
            });
            messages.AddRange(await Task.WhenAll(calls));
        }

        return "Ik kon binnen het maximale aantal stappen geen antwoord formuleren.";
    }

    private async Task<string> ExecuteToolCallAsync(
        AgentDefinition agent,
        LlmTornado.ChatFunctions.ToolCall call,
        IReadOnlyDictionary<string, PeerTarget> targets,
        RunContext ctx,
        CancellationToken ct)
    {
        var name = call.FunctionCall?.Name ?? "";
        if (!targets.TryGetValue(name, out var target))
            return $"Onbekende tool '{name}'.";

        string question;
        try
        {
            var args = JObject.Parse(string.IsNullOrWhiteSpace(call.FunctionCall?.Arguments) ? "{}" : call.FunctionCall!.Arguments);
            question = args.Value<string>("message") ?? args.Value<string>("question") ?? "";
        }
        catch
        {
            question = call.FunctionCall?.Arguments ?? "";
        }

        if (string.IsNullOrWhiteSpace(question))
            return "Geef een concrete vraag mee in het veld 'message'.";

        if (target.AgentId is not null && ctx.Chain.Contains(target.AgentId, StringComparer.OrdinalIgnoreCase))
        {
            var msg = $"{target.DisplayName} is al bij dit gesprek betrokken; doorverwijzen zou een lus veroorzaken. Beantwoord de vraag zelf.";
            traces.Add(ctx.TraceId, "error", agent.Id, target.Key, msg, ctx.Depth + 1);
            return msg;
        }

        traces.Add(ctx.TraceId, "delegation", agent.Id, target.Key, question, ctx.Depth + 1);
        try
        {
            var childCtx = ctx with { Depth = ctx.Depth + 1, Caller = agent.Id };
            var answer = await peers.AskAsync(target, question, childCtx, ct);
            traces.Add(ctx.TraceId, "delegation_result", target.Key, agent.Id, answer, ctx.Depth + 1);
            return answer;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Doorverwijzing van {From} naar {To} mislukt", agent.Id, target.Key);
            var msg = $"{target.DisplayName} kon niet worden bereikt: {ex.Message}";
            traces.Add(ctx.TraceId, "error", target.Key, agent.Id, msg, ctx.Depth + 1);
            return msg;
        }
    }

    /// <summary>Alle agents waarmee <paramref name="agent"/> mag praten, geïndexeerd op toolnaam.</summary>
    public Dictionary<string, PeerTarget> ResolvePeers(AgentDefinition agent)
    {
        var result = new Dictionary<string, PeerTarget>(StringComparer.Ordinal);

        foreach (var peerId in agent.A2A.Peers)
        {
            var peer = store.Get(peerId);
            if (peer is null || !peer.Enabled || !peer.A2A.Expose) continue;
            var target = new PeerTarget(
                Key: peer.Id,
                ToolName: ToolName(peer.Id),
                DisplayName: peer.Name,
                Description: peer.Description,
                AgentId: peer.Id,
                Url: null,
                ApiKeyEnv: null);
            result[target.ToolName] = target;
        }

        foreach (var remote in agent.A2A.RemoteAgents)
        {
            var key = "remote:" + remote.Name.ToLowerInvariant();
            var target = new PeerTarget(
                Key: key,
                ToolName: ToolName("remote_" + remote.Name.ToLowerInvariant()),
                DisplayName: remote.Name,
                Description: string.IsNullOrWhiteSpace(remote.Description) ? "Externe A2A-agent." : remote.Description,
                AgentId: null,
                Url: remote.Url,
                ApiKeyEnv: remote.ApiKeyEnv);
            result[target.ToolName] = target;
        }

        return result;
    }

    private static string ToolName(string id)
    {
        var sb = new StringBuilder(ToolPrefix);
        foreach (var c in id)
            sb.Append(char.IsAsciiLetterOrDigit(c) || c is '_' or '-' ? c : '_');
        return sb.Length > 64 ? sb.ToString(0, 64) : sb.ToString();
    }

    private static string BuildSystemPrompt(AgentDefinition agent, IReadOnlyDictionary<string, PeerTarget> targets, RunContext ctx)
    {
        var sb = new StringBuilder();
        if (!string.IsNullOrWhiteSpace(agent.SystemPrompt))
            sb.AppendLine(agent.SystemPrompt.Trim());

        if (targets.Count > 0)
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.AppendLine("Je werkt samen met de volgende collega-agents. Leg een (deel)vraag aan een collega voor via de bijbehorende tool " +
                          "wanneer diens expertise beter past dan de jouwe, en verwerk het antwoord in je eigen reactie. " +
                          "Stel collega's een duidelijke, zelfstandige vraag met alle benodigde context; zij zien het gesprek niet.");
            foreach (var t in targets.Values)
                sb.AppendLine($"- {t.ToolName}: {t.DisplayName} — {t.Description}");
        }

        if (ctx.Depth > 0)
        {
            if (sb.Length > 0) sb.AppendLine();
            sb.AppendLine($"Deze vraag komt van collega-agent '{ctx.Caller}'. Antwoord beknopt en inhoudelijk.");
        }

        return sb.ToString().Trim();
    }

    private static void AddWorkspaceFields(JObject json, AgentDefinition agent)
    {
        if (agent.ToolIds.Count > 0)
            json["tool_ids"] = new JArray(agent.ToolIds);

        if (agent.KnowledgeIds.Count > 0)
            json["files"] = new JArray(agent.KnowledgeIds.Select(id => new JObject
            {
                ["type"] = "collection",
                ["id"] = id
            }));

        if (agent.Features.WebSearch || agent.Features.ImageGeneration || agent.Features.CodeInterpreter)
            json["features"] = new JObject
            {
                ["web_search"] = agent.Features.WebSearch,
                ["image_generation"] = agent.Features.ImageGeneration,
                ["code_interpreter"] = agent.Features.CodeInterpreter
            };
    }

    private static string Text(ChatMessage? message) =>
        message is null ? "" : message.Content ?? message.GetMessageContent() ?? "";

    private static readonly object AskSchema = new
    {
        type = "object",
        properties = new
        {
            message = new
            {
                type = "string",
                description = "De vraag of opdracht voor de collega-agent, inclusief alle context die nodig is."
            }
        },
        required = new[] { "message" }
    };
}
