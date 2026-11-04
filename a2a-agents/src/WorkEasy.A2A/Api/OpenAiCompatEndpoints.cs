using System.Text.Json;
using LlmTornado.Code;
using WorkEasy.A2A.Agents;

namespace WorkEasy.A2A.Api;

public sealed record OpenAiChatRequest(string Model, List<ChatTurn> Messages, bool? Stream);

/// <summary>
/// OpenAI-compatibele endpoints (<c>/v1/models</c>, <c>/v1/chat/completions</c>). Voeg deze service in
/// WorkEasy toe als OpenAI-verbinding (Beheer → Instellingen → Verbindingen) en de agents verschijnen in
/// de modelkiezer, zodat gebruikers er gewoon mee kunnen chatten — inclusief onderling overleg.
/// </summary>
public static class OpenAiCompatEndpoints
{
    public const string ModelPrefix = "a2a.";

    public static void MapOpenAiCompatApi(this IEndpointRouteBuilder app)
    {
        app.MapGet("/v1/models", (AgentStore store) => Results.Ok(new
        {
            @object = "list",
            data = store.List().Where(a => a.Enabled).Select(a => new
            {
                id = ModelPrefix + a.Id,
                @object = "model",
                created = a.CreatedAt,
                owned_by = "workeasy-a2a",
                name = a.Name,
                description = a.Description
            })
        }));

        app.MapPost("/v1/chat/completions", async (
            OpenAiChatRequest body, HttpContext http, AgentStore store, AgentRuntime runtime, CancellationToken ct) =>
        {
            var id = body.Model?.StartsWith(ModelPrefix, StringComparison.OrdinalIgnoreCase) == true
                ? body.Model[ModelPrefix.Length..]
                : body.Model ?? "";
            var agent = store.Get(id);
            if (agent is null || !agent.Enabled)
                return Results.NotFound(new { error = new { message = $"Model '{body.Model}' niet gevonden.", type = "invalid_request_error" } });

            var history = ManagementEndpoints.ToTornadoMessages(body.Messages);
            if (!history.Any(m => m.Role == ChatMessageRoles.User))
                return Results.BadRequest(new { error = new { message = "Geen gebruikersbericht.", type = "invalid_request_error" } });

            AgentRunResult result;
            try
            {
                result = await runtime.RunAsync(agent, history, new RunContext { Caller = "openai-client" }, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                return Results.Json(new { error = new { message = ex.Message, type = "agent_error" } }, statusCode: 502);
            }

            var created = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            var completionId = "chatcmpl-" + result.TraceId;

            if (body.Stream == true)
            {
                // De agent levert pas een antwoord na eventueel overleg; we sturen het daarom als één chunk.
                http.Response.ContentType = "text/event-stream";
                http.Response.Headers.CacheControl = "no-cache";
                await WriteSse(http, new
                {
                    id = completionId, @object = "chat.completion.chunk", created, model = body.Model,
                    choices = new[] { new { index = 0, delta = new { role = "assistant", content = result.Content }, finish_reason = (string?)null } }
                }, ct);
                await WriteSse(http, new
                {
                    id = completionId, @object = "chat.completion.chunk", created, model = body.Model,
                    choices = new[] { new { index = 0, delta = new { }, finish_reason = "stop" } }
                }, ct);
                await http.Response.WriteAsync("data: [DONE]\n\n", ct);
                return Results.Empty;
            }

            return Results.Ok(new
            {
                id = completionId,
                @object = "chat.completion",
                created,
                model = body.Model,
                choices = new[]
                {
                    new { index = 0, message = new { role = "assistant", content = result.Content }, finish_reason = "stop" }
                },
                usage = new { prompt_tokens = 0, completion_tokens = 0, total_tokens = 0 }
            });
        });
    }

    private static async Task WriteSse(HttpContext http, object payload, CancellationToken ct)
    {
        await http.Response.WriteAsync("data: " + JsonSerializer.Serialize(payload) + "\n\n", ct);
        await http.Response.Body.FlushAsync(ct);
    }
}
