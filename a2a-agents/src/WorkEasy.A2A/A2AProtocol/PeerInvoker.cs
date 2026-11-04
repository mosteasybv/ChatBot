using System.Net.Http.Headers;
using System.Text.Json;
using A2A;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Options;
using WorkEasy.A2A.Agents;
using WorkEasy.A2A.Configuration;

namespace WorkEasy.A2A.A2AProtocol;

/// <summary>
/// Stuurt een vraag via het A2A-protocol (JSON-RPC <c>message/send</c>) naar een andere agent — zowel
/// naar agents in deze service als naar externe A2A-servers. Ook lokale agents worden bewust via HTTP
/// aangesproken, zodat agents later zonder wijzigingen over meerdere servers verdeeld kunnen worden.
/// </summary>
public sealed class PeerInvoker(
    IHttpClientFactory httpClientFactory,
    IServer server,
    IOptions<A2AOptions> options,
    ILogger<PeerInvoker> logger)
{
    public const string HttpClientName = "a2a-peers";

    private static readonly TaskState[] TerminalStates =
        [TaskState.Completed, TaskState.Failed, TaskState.Canceled, TaskState.Rejected, TaskState.InputRequired, TaskState.AuthRequired];

    public async Task<string> AskAsync(PeerTarget target, string question, RunContext ctx, CancellationToken ct)
    {
        var url = target.Url ?? $"{InternalBaseUrl()}/a2a/{Uri.EscapeDataString(target.AgentId!)}";

        var http = httpClientFactory.CreateClient(HttpClientName);
        var token = target.AgentId is not null
            ? options.Value.ApiKey
            : string.IsNullOrWhiteSpace(target.ApiKeyEnv) ? null : Environment.GetEnvironmentVariable(target.ApiKeyEnv);
        if (!string.IsNullOrEmpty(token))
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var client = new A2AClient(new Uri(url), http);

        var metadata = new Dictionary<string, JsonElement>
        {
            [RunContext.MetaTraceId] = JsonSerializer.SerializeToElement(ctx.TraceId),
            [RunContext.MetaDepth] = JsonSerializer.SerializeToElement(ctx.Depth),
            [RunContext.MetaChain] = JsonSerializer.SerializeToElement(ctx.Chain),
            [RunContext.MetaCaller] = JsonSerializer.SerializeToElement(ctx.Caller),
        };
        if (ctx.MaxDepth is { } maxDepth)
            metadata[RunContext.MetaMaxDepth] = JsonSerializer.SerializeToElement(maxDepth);

        var message = new AgentMessage
        {
            Role = MessageRole.User,
            MessageId = Guid.NewGuid().ToString("N"),
            // Vaste context per (gesprek, vrager, gevraagde) zodat vervolgvragen de eerdere uitwisseling kennen.
            ContextId = $"{ctx.TraceId}:{ctx.Caller}->{target.Key}",
            Parts = [new TextPart { Text = question }],
            Metadata = metadata
        };

        var response = await client.SendMessageAsync(new MessageSendParams { Message = message, Metadata = metadata }, ct);

        if (response is AgentTask task && !TerminalStates.Contains(task.Status.State))
            response = await WaitForTaskAsync(client, task, ct);

        if (response is AgentTask { Status.State: TaskState.Failed or TaskState.Rejected } failed)
            throw new InvalidOperationException(A2AText.FromTask(failed) is { Length: > 0 } reason ? reason : "taak mislukt");

        var text = A2AText.FromResponse(response);
        return string.IsNullOrWhiteSpace(text) ? "(leeg antwoord)" : text;
    }

    private async Task<AgentTask> WaitForTaskAsync(A2AClient client, AgentTask task, CancellationToken ct)
    {
        var deadline = DateTimeOffset.UtcNow.AddSeconds(options.Value.RequestTimeoutSeconds);
        var delay = TimeSpan.FromMilliseconds(500);
        while (DateTimeOffset.UtcNow < deadline)
        {
            await Task.Delay(delay, ct);
            task = await client.GetTaskAsync(task.Id, ct);
            if (TerminalStates.Contains(task.Status.State)) return task;
            delay = TimeSpan.FromMilliseconds(Math.Min(delay.TotalMilliseconds * 2, 5000));
        }
        logger.LogWarning("Time-out bij wachten op A2A-taak {TaskId}", task.Id);
        throw new TimeoutException("De andere agent reageerde niet op tijd.");
    }

    private string InternalBaseUrl()
    {
        if (!string.IsNullOrWhiteSpace(options.Value.InternalBaseUrl))
            return options.Value.InternalBaseUrl.TrimEnd('/');

        var address = server.Features.Get<IServerAddressesFeature>()?.Addresses
            .OrderBy(a => a.StartsWith("https", StringComparison.OrdinalIgnoreCase) ? 1 : 0)
            .FirstOrDefault() ?? "http://localhost:8080";

        return address
            .Replace("://+", "://localhost")
            .Replace("://*", "://localhost")
            .Replace("://0.0.0.0", "://localhost")
            .Replace("://[::]", "://localhost")
            .TrimEnd('/');
    }
}
