namespace CloudHermesDashboard.Models;

public sealed class TaskItemModel
{
    public required string Id { get; init; }

    public required string Title { get; init; }

    public required string Status { get; init; }

    public string Priority { get; init; } = "";

    public DateTime? CreatedUtc { get; init; }
}
