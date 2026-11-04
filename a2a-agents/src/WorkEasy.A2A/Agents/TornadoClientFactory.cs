using System.Collections.Concurrent;
using LlmTornado;
using LlmTornado.Chat.Models;
using LlmTornado.Code;
using Microsoft.Extensions.Options;
using WorkEasy.A2A.Configuration;

namespace WorkEasy.A2A.Agents;

/// <summary>
/// Maakt LlmTornado-clients aan voor een agent. Bij bron "Workspace" wordt WorkEasy zelf als
/// OpenAI-compatibele server gebruikt (http://…/api/chat/completions), zodat de agent precies dezelfde
/// functionaliteit krijgt als de GPT in de werkruimte (kennis, filters, tools, webzoeken, systeemprompt).
/// </summary>
public sealed class TornadoClientFactory(IOptions<A2AOptions> options)
{
    private readonly ConcurrentDictionary<string, TornadoApi> _clients = new();

    /// <summary>Providers die zonder extra configuratie met alleen een API-sleutel werken.</summary>
    public static readonly string[] SupportedProviders =
    [
        nameof(LLmProviders.OpenAi), nameof(LLmProviders.Anthropic), nameof(LLmProviders.Google),
        nameof(LLmProviders.Mistral), nameof(LLmProviders.Groq), nameof(LLmProviders.DeepSeek),
        nameof(LLmProviders.Cohere), nameof(LLmProviders.XAi), nameof(LLmProviders.Perplexity),
        nameof(LLmProviders.OpenRouter), nameof(LLmProviders.DeepInfra), nameof(LLmProviders.Custom)
    ];

    public static bool TryParseProvider(string? value, out LLmProviders provider)
    {
        provider = LLmProviders.Unknown;
        return value is not null
               && SupportedProviders.Contains(value, StringComparer.OrdinalIgnoreCase)
               && Enum.TryParse(value, ignoreCase: true, out provider);
    }

    public (TornadoApi Api, ChatModel Model) Create(AgentDefinition agent)
    {
        var source = agent.Source;

        if (source.Type == AgentSourceType.Workspace)
        {
            var baseUrl = options.Value.OpenWebUiBaseUrl.TrimEnd('/') + "/api";
            var api = _clients.GetOrAdd($"workspace|{baseUrl}", _ => new TornadoApi(new Uri(baseUrl), options.Value.OpenWebUiApiKey, LLmProviders.Custom)
            {
                // WorkEasy biedt /api/chat/completions (zonder /v1); {1} is het endpoint, bijv. "chat/completions".
                ApiUrlFormat = baseUrl + "/{1}"
            });
            return (api, new ChatModel(source.Model, LLmProviders.Custom));
        }

        if (!TryParseProvider(source.Provider, out var provider))
            throw new InvalidOperationException($"Onbekende provider '{source.Provider}'.");

        var apiKey = string.IsNullOrWhiteSpace(source.ApiKeyEnv)
            ? ""
            : Environment.GetEnvironmentVariable(source.ApiKeyEnv) ?? "";

        if (provider == LLmProviders.Custom)
        {
            var baseUrl = (source.BaseUrl ?? "").TrimEnd('/');
            var api = _clients.GetOrAdd($"custom|{baseUrl}|{source.ApiKeyEnv}",
                _ => new TornadoApi(new Uri(baseUrl), apiKey, LLmProviders.Custom));
            return (api, new ChatModel(source.Model, LLmProviders.Custom));
        }

        if (string.IsNullOrEmpty(apiKey))
            throw new InvalidOperationException(
                $"Geen API-sleutel gevonden voor {provider}: zet omgevingsvariabele '{source.ApiKeyEnv}'.");

        var client = _clients.GetOrAdd($"{provider}|{source.ApiKeyEnv}", _ => new TornadoApi(provider, apiKey));
        return (client, new ChatModel(source.Model, provider));
    }
}
