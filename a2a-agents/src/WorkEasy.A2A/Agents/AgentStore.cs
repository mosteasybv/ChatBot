using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;
using WorkEasy.A2A.Configuration;

namespace WorkEasy.A2A.Agents;

/// <summary>
/// Thread-veilige opslag van agentconfiguraties in een JSON-bestand.
/// </summary>
public sealed partial class AgentStore
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly ConcurrentDictionary<string, AgentDefinition> _agents = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly string _path;
    private readonly ILogger<AgentStore> _logger;

    public AgentStore(IOptions<A2AOptions> options, ILogger<AgentStore> logger)
    {
        _logger = logger;
        _path = Path.GetFullPath(options.Value.DataPath);
        Load();
    }

    public IReadOnlyList<AgentDefinition> List() =>
        _agents.Values.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase).ToList();

    public AgentDefinition? Get(string id) => _agents.TryGetValue(id, out var agent) ? agent : null;

    public async Task<AgentDefinition> CreateAsync(AgentDefinition agent, string? userId)
    {
        Normalize(agent);
        var errors = Validate(agent, isNew: true);
        if (errors.Count > 0) throw new AgentValidationException(errors);

        var now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        agent.CreatedAt = now;
        agent.UpdatedAt = now;
        agent.CreatedBy = userId;

        if (!_agents.TryAdd(agent.Id, agent))
            throw new AgentValidationException(new() { $"Er bestaat al een agent met id '{agent.Id}'." });

        await SaveAsync();
        return agent;
    }

    public async Task<AgentDefinition?> UpdateAsync(string id, AgentDefinition update)
    {
        var existing = Get(id);
        if (existing is null) return null;

        update.Id = existing.Id;
        Normalize(update);
        var errors = Validate(update, isNew: false);
        if (errors.Count > 0) throw new AgentValidationException(errors);

        update.CreatedAt = existing.CreatedAt;
        update.CreatedBy = existing.CreatedBy;
        update.UpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        _agents[id] = update;
        await SaveAsync();
        return update;
    }

    public async Task<bool> DeleteAsync(string id)
    {
        if (!_agents.TryRemove(id, out _)) return false;

        // Verwijder de agent ook uit de peer-lijsten van andere agents.
        foreach (var agent in _agents.Values)
            agent.A2A.Peers.RemoveAll(p => string.Equals(p, id, StringComparison.OrdinalIgnoreCase));

        await SaveAsync();
        return true;
    }

    private static void Normalize(AgentDefinition agent)
    {
        agent.Id = (agent.Id ?? "").Trim().ToLowerInvariant();
        agent.Name = (agent.Name ?? "").Trim();
        agent.Description ??= "";
        agent.SystemPrompt ??= "";
        agent.Tags ??= new();
        agent.KnowledgeIds ??= new();
        agent.ToolIds ??= new();
        agent.Source ??= new();
        agent.Params ??= new();
        agent.Features ??= new();
        agent.A2A ??= new();
        agent.A2A.Peers = (agent.A2A.Peers ?? new())
            .Select(p => p.Trim().ToLowerInvariant())
            .Where(p => p.Length > 0 && p != agent.Id)
            .Distinct()
            .ToList();
        agent.A2A.RemoteAgents ??= new();
        agent.A2A.Skills ??= new();
    }

    private List<string> Validate(AgentDefinition agent, bool isNew)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(agent.Id) || !IdRegex().IsMatch(agent.Id))
            errors.Add("Id is verplicht en mag alleen kleine letters, cijfers, '-' en '_' bevatten (max. 64 tekens).");
        if (string.IsNullOrWhiteSpace(agent.Name))
            errors.Add("Naam is verplicht.");
        if (string.IsNullOrWhiteSpace(agent.Source.Model))
            errors.Add("Kies een (basis)model of GPT.");
        if (agent.Source.Type == AgentSourceType.Provider)
        {
            if (!TornadoClientFactory.TryParseProvider(agent.Source.Provider, out var provider))
                errors.Add($"Onbekende provider '{agent.Source.Provider}'.");
            else if (provider == LlmTornado.Code.LLmProviders.Custom && string.IsNullOrWhiteSpace(agent.Source.BaseUrl))
                errors.Add("Bij provider 'Custom' is een basis-URL verplicht.");
        }

        foreach (var peer in agent.A2A.Peers)
            if (!_agents.ContainsKey(peer))
                errors.Add($"Agent '{peer}' (in 'mag praten met') bestaat niet.");

        var remoteNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var remote in agent.A2A.RemoteAgents)
        {
            if (string.IsNullOrWhiteSpace(remote.Name) || !IdRegex().IsMatch(remote.Name.ToLowerInvariant()))
                errors.Add("Elke externe agent heeft een korte naam nodig (letters, cijfers, '-' of '_').");
            else if (!remoteNames.Add(remote.Name))
                errors.Add($"Externe agentnaam '{remote.Name}' komt dubbel voor.");
            if (!Uri.TryCreate(remote.Url, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https"))
                errors.Add($"Ongeldige URL voor externe agent '{remote.Name}'.");
        }

        if (agent.A2A.MaxDelegationDepth is < 0 or > 10)
            errors.Add("Maximale doorverwijsdiepte moet tussen 0 en 10 liggen.");

        return errors;
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_path)) return;
            var json = File.ReadAllText(_path);
            var agents = JsonSerializer.Deserialize<List<AgentDefinition>>(json, JsonOptions) ?? new();
            foreach (var agent in agents)
            {
                Normalize(agent);
                _agents[agent.Id] = agent;
            }
            _logger.LogInformation("{Count} agent(s) geladen uit {Path}", _agents.Count, _path);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Kon agents niet laden uit {Path}", _path);
        }
    }

    private async Task SaveAsync()
    {
        await _writeLock.WaitAsync();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var tmp = _path + ".tmp";
            await File.WriteAllTextAsync(tmp, JsonSerializer.Serialize(List(), JsonOptions));
            File.Move(tmp, _path, overwrite: true);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    [GeneratedRegex("^[a-z0-9][a-z0-9_-]{0,63}$")]
    private static partial Regex IdRegex();
}

public sealed class AgentValidationException(List<string> errors) : Exception(string.Join(" ", errors))
{
    public List<string> Errors { get; } = errors;
}
