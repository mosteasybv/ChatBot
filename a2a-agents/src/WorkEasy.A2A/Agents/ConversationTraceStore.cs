using System.Collections.Concurrent;

namespace WorkEasy.A2A.Agents;

/// <summary>Eén gebeurtenis in een gesprek tussen agents (voor de testweergave in WorkEasy).</summary>
public sealed record TraceEvent(
    DateTimeOffset Timestamp,
    string Type,          // request | delegation | delegation_result | response | error
    string From,
    string To,
    string Content,
    int Depth);

public sealed class ConversationTrace
{
    public string Id { get; init; } = "";
    public DateTimeOffset StartedAt { get; init; } = DateTimeOffset.UtcNow;
    public List<TraceEvent> Events { get; } = new();
}

/// <summary>
/// Houdt de laatste gesprekken in het geheugen bij, zodat in de UI zichtbaar is welke agent met welke
/// andere agent heeft gesproken en wat ze elkaar hebben gevraagd/geantwoord.
/// </summary>
public sealed class ConversationTraceStore
{
    private const int MaxTraces = 200;
    private const int MaxContentLength = 8000;

    private readonly ConcurrentDictionary<string, ConversationTrace> _traces = new();
    private readonly ConcurrentQueue<string> _order = new();

    public void Add(string traceId, string type, string from, string to, string content, int depth)
    {
        var trace = _traces.GetOrAdd(traceId, id =>
        {
            _order.Enqueue(id);
            while (_order.Count > MaxTraces && _order.TryDequeue(out var old))
                _traces.TryRemove(old, out _);
            return new ConversationTrace { Id = id };
        });

        if (content.Length > MaxContentLength)
            content = content[..MaxContentLength] + " …";

        lock (trace.Events)
            trace.Events.Add(new TraceEvent(DateTimeOffset.UtcNow, type, from, to, content, depth));
    }

    public ConversationTrace? Get(string traceId)
    {
        if (!_traces.TryGetValue(traceId, out var trace)) return null;
        var copy = new ConversationTrace { Id = trace.Id, StartedAt = trace.StartedAt };
        lock (trace.Events) copy.Events.AddRange(trace.Events);
        return copy;
    }
}
