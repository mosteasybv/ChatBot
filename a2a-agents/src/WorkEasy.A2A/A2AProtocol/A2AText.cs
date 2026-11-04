using System.Text;
using A2A;

namespace WorkEasy.A2A.A2AProtocol;

internal static class A2AText
{
    public static string FromParts(IEnumerable<Part>? parts)
    {
        if (parts is null) return "";
        var sb = new StringBuilder();
        foreach (var part in parts)
        {
            switch (part)
            {
                case TextPart text when !string.IsNullOrEmpty(text.Text):
                    if (sb.Length > 0) sb.AppendLine();
                    sb.Append(text.Text);
                    break;
                case DataPart data:
                    if (sb.Length > 0) sb.AppendLine();
                    sb.Append(System.Text.Json.JsonSerializer.Serialize(data.Data));
                    break;
                case FilePart file:
                    if (sb.Length > 0) sb.AppendLine();
                    sb.Append("[bestand]");
                    break;
            }
        }
        return sb.ToString();
    }

    /// <summary>Haalt de tekst uit een A2A-antwoord (een bericht of een taak).</summary>
    public static string FromResponse(A2AResponse? response) => response switch
    {
        AgentMessage message => FromParts(message.Parts),
        AgentTask task => FromTask(task),
        _ => ""
    };

    public static string FromTask(AgentTask task)
    {
        var artifacts = task.Artifacts?.SelectMany(a => a.Parts ?? new()).ToList();
        if (artifacts is { Count: > 0 }) return FromParts(artifacts);
        if (task.Status.Message is not null) return FromParts(task.Status.Message.Parts);
        var last = task.History?.LastOrDefault(m => m.Role == MessageRole.Agent);
        return last is null ? "" : FromParts(last.Parts);
    }
}
