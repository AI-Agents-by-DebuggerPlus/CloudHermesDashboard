using CloudHermesDashboard.Models;

namespace CloudHermesDashboard.Services;

public sealed class HermesIntrospectionService : IAgentDataService, IDisposable
{
    public const string ListCommand = "system: list_subagents";

    private readonly IHermesChatClient _chat;
    private readonly ILoggingService _log;
    private readonly DashboardSettings _settings;
    private readonly Dictionary<string, AgentModel> _live = [];
    private readonly Dictionary<string, (AgentModel Agent, DateTime RemoveAt)> _archived = [];
    private readonly CancellationTokenSource _cts = new();
    private string? _lastUnrecognized;
    private int _stopped;

    public HermesIntrospectionService(IHermesChatClient chat, ILoggingService log, DashboardSettings settings)
    {
        _chat = chat;
        _log = log;
        _settings = settings;
    }

    public event EventHandler<IReadOnlyList<AgentModel>>? SnapshotReceived;

    public event EventHandler<AgentEventModel>? EventAdded;

    public event EventHandler<string>? UnrecognizedPayload;

    public void Start()
    {
        _ = Task.Run(() => Loop(_cts.Token));
    }

    public void Stop()
    {
        if (Interlocked.Exchange(ref _stopped, 1) == 1)
            return;
        _cts.Cancel();
    }

    public void Dispose()
    {
        Stop();
        _cts.Dispose();
    }

    private async Task Loop(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                await PollOnce(cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _log.Error("Introspection", ex.Message);
            }

            try
            {
                await Task.Delay(TimeSpan.FromSeconds(_settings.ClampedPollSeconds), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    private async Task PollOnce(CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(170));
        _log.Info("Introspection", "Запрос " + ListCommand);
        var result = await _chat.SendAsync(ListCommand, timeout.Token).ConfigureAwait(false);
        if (!result.Ok)
        {
            NoteUnrecognized(string.IsNullOrWhiteSpace(result.Text) ? "Пустой ответ связного." : result.Text);
            return;
        }

        var parsed = ReplyParsers.ParseRoster(result.Text);
        if (!parsed.Recognized)
        {
            NoteUnrecognized(result.Text);
            return;
        }

        foreach (var warning in parsed.Warnings)
            _log.Warn("Introspection", warning);

        _lastUnrecognized = null;
        _log.Info("Introspection", "Агентов в ответе: " + parsed.Agents.Count);
        Apply(parsed.Agents);
    }

    private void NoteUnrecognized(string raw)
    {
        if (string.Equals(raw, _lastUnrecognized, StringComparison.Ordinal))
        {
            _log.Info("Introspection", "Повтор того же нераспознанного ответа. Полный текст уже записан.");
            return;
        }

        _lastUnrecognized = raw;
        _log.Warn("Introspection", "Нераспознанный ответ list_subagents:\n" + raw);
        UnrecognizedPayload?.Invoke(this, raw);
    }

    private void Apply(IReadOnlyList<AgentModel> incoming)
    {
        var now = DateTime.UtcNow;
        var ids = incoming.Select(agent => agent.Id).ToHashSet(StringComparer.Ordinal);
        var pending = new List<AgentEventModel>();

        foreach (var agent in incoming)
        {
            _archived.Remove(agent.Id);
            if (_live.TryGetValue(agent.Id, out var previous))
            {
                if (previous.Status != agent.Status || previous.CurrentTask != agent.CurrentTask)
                {
                    pending.Add(Event(agent, agent.Status == AgentStatus.Error ? AgentEventType.Error : AgentEventType.Message,
                        $"{agent.Status}: {agent.CurrentTask}"));
                }
            }
            else
            {
                pending.Add(Event(agent, AgentEventType.Message, "Появился в ответе связного."));
            }

            _live[agent.Id] = agent;
        }

        foreach (var id in _live.Keys.ToList())
        {
            if (ids.Contains(id))
                continue;

            var gone = _live[id];
            gone.Status = AgentStatus.Archived;
            gone.StatusKnown = true;
            _live.Remove(id);
            _archived[id] = (gone, now.AddSeconds(_settings.ClampedArchiveSeconds));
            pending.Add(Event(gone, AgentEventType.Message, "Пропал из ответа связного и помечен Archived."));
        }

        foreach (var id in _archived.Keys.ToList())
        {
            if (now >= _archived[id].RemoveAt)
            {
                _log.Info("Introspection", "Агент убран из архива: " + id);
                _archived.Remove(id);
            }
        }

        var snapshot = _live.Values
            .Concat(_archived.Values.Select(entry => entry.Agent))
            .Select(Clone)
            .ToList();
        SnapshotReceived?.Invoke(this, snapshot);
        foreach (var evt in pending)
            EventAdded?.Invoke(this, evt);
    }

    private static AgentEventModel Event(AgentModel agent, AgentEventType type, string text) => new()
    {
        Timestamp = DateTime.Now,
        AgentId = agent.Id,
        EventType = type,
        Text = text
    };

    private static AgentModel Clone(AgentModel agent) => new()
    {
        Id = agent.Id,
        Name = agent.Name,
        Role = agent.Role,
        RawRole = agent.RawRole,
        Model = agent.Model,
        Status = agent.Status,
        RawStatus = agent.RawStatus,
        StatusKnown = agent.StatusKnown,
        CurrentTask = agent.CurrentTask,
        Toolsets = agent.Toolsets.ToList(),
        ParentId = agent.ParentId,
        LastActivityUtc = agent.LastActivityUtc
    };
}
