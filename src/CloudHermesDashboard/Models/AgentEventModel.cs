namespace CloudHermesDashboard.Models;

public sealed class AgentEventModel
{
    public required DateTime Timestamp { get; init; }

    public required string AgentId { get; init; }

    public required AgentEventType EventType { get; init; }

    public required string Text { get; init; }
}
