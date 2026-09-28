using CloudHermesDashboard.Models;

namespace CloudHermesDashboard.Services;

public interface ITaskBoardService
{
    Task<TaskBoardRefresh> RefreshAsync(CancellationToken cancellationToken);
}

public sealed class TaskBoardRefresh
{
    public bool Recognized { get; init; }

    public IReadOnlyList<TaskItemModel> Tasks { get; init; } = [];

    public string Raw { get; init; } = "";
}
