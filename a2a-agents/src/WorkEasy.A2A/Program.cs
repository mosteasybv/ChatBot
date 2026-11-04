using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using WorkEasy.A2A.A2AProtocol;
using WorkEasy.A2A.Agents;
using WorkEasy.A2A.Api;
using WorkEasy.A2A.Configuration;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<A2AOptions>(builder.Configuration.GetSection(A2AOptions.Section));
builder.Services.ConfigureHttpJsonOptions(o =>
{
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    o.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

builder.Services.AddHttpContextAccessor();
builder.Services.AddHttpClient(PeerInvoker.HttpClientName, c =>
{
    var timeout = builder.Configuration.GetValue<int?>($"{A2AOptions.Section}:RequestTimeoutSeconds") ?? 300;
    c.Timeout = TimeSpan.FromSeconds(timeout);
});

builder.Services.AddSingleton<AgentStore>();
builder.Services.AddSingleton<ConversationTraceStore>();
builder.Services.AddSingleton<TornadoClientFactory>();
builder.Services.AddSingleton<PeerInvoker>();
builder.Services.AddSingleton<AgentRuntime>();
builder.Services.AddSingleton<A2AAgentServer>();

var app = builder.Build();

var apiKey = app.Configuration[$"{A2AOptions.Section}:ApiKey"] ?? "";
if (string.IsNullOrEmpty(apiKey))
    app.Logger.LogWarning("A2A__ApiKey is niet gezet: de service accepteert verzoeken zonder authenticatie.");
if (string.IsNullOrEmpty(app.Configuration[$"{A2AOptions.Section}:OpenWebUiApiKey"]))
    app.Logger.LogWarning("A2A__OpenWebUiApiKey is niet gezet: agents met een WorkEasy-GPT als bron kunnen WorkEasy niet aanroepen.");

// Eenvoudige Bearer/X-API-Key-authenticatie. Agent cards zijn publiek zodat andere A2A-clients ze kunnen ontdekken.
app.Use(async (context, next) =>
{
    var path = context.Request.Path;
    var isPublic = path == "/health"
                   || (HttpMethods.IsGet(context.Request.Method) && (path.Value?.EndsWith("/.well-known/agent-card.json") == true || path.Value?.EndsWith("/.well-known/agent.json") == true));

    if (!string.IsNullOrEmpty(apiKey) && !isPublic)
    {
        var header = context.Request.Headers.Authorization.FirstOrDefault();
        var provided = header?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true
            ? header[7..].Trim()
            : context.Request.Headers["X-API-Key"].FirstOrDefault();

        if (provided is null || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(provided), Encoding.UTF8.GetBytes(apiKey)))
        {
            context.Response.StatusCode = StatusCodes.Status401Unauthorized;
            await context.Response.WriteAsJsonAsync(new { detail = "Ongeldige of ontbrekende API-sleutel." });
            return;
        }
    }

    await next();
});

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapManagementApi();
app.MapOpenAiCompatApi();
app.Services.GetRequiredService<A2AAgentServer>().Map(app);

app.Run();
