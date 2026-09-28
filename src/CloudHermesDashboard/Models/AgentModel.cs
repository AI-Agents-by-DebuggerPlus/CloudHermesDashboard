namespace CloudHermesDashboard.Models;

public sealed class AgentModel
{
    public required string Id { get; init; }

    public required string Name { get; set; }

    public required AgentRole Role { get; set; }

    public string RawRole { get; set; } = "";

    public required string Model { get; set; }

    public AgentStatus Status { get; set; }

    public string RawStatus { get; set; } = "";

    public bool StatusKnown { get; set; } = true;

    public required string CurrentTask { get; set; }

    public IReadOnlyList<string> Toolsets { get; set; } = [];

    public string? ParentId { get; set; }

    public DateTime? LastActivityUtc { get; set; }
}
