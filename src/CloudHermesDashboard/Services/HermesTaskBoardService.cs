namespace CloudHermesDashboard.Services;

public sealed class HermesTaskBoardService : ITaskBoardService
{
    public const string ListCommand = "system: list_tasks";

    private readonly IHermesChatClient _chat;
    private readonly ILoggingService _log;

    public HermesTaskBoardService(IHermesChatClient chat, ILoggingService log)
    {
        _chat = chat;
        _log = log;
    }

    public async Task<TaskBoardRefresh> RefreshAsync(CancellationToken cancellationToken)
    {
        _log.Info("Tasks", "Запрос " + ListCommand);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(170));
        var result = await _chat.SendAsync(ListCommand, timeout.Token).ConfigureAwait(false);
        if (!result.Ok)
        {
            _log.Warn("Tasks", "Нераспознанный ответ list_tasks:\n" + result.Text);
            return new TaskBoardRefresh { Recognized = false, Raw = result.Text };
        }

        var parsed = ReplyParsers.ParseTasks(result.Text);
        if (!parsed.Recognized)
        {
            _log.Warn("Tasks", "Нераспознанный ответ list_tasks:\n" + result.Text);
            return new TaskBoardRefresh { Recognized = false, Raw = result.Text };
        }

        foreach (var warning in parsed.Warnings)
            _log.Warn("Tasks", warning);

        _log.Info("Tasks", "Задач в ответе: " + parsed.Tasks.Count);
        return new TaskBoardRefresh { Recognized = true, Tasks = parsed.Tasks, Raw = result.Text };
    }
}
