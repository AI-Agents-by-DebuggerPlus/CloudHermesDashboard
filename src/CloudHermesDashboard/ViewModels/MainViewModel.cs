using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CloudHermesDashboard.Models;
using CloudHermesDashboard.Services;
using CloudHermesDashboard.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;

namespace CloudHermesDashboard.ViewModels;

public partial class MainViewModel : ObservableObject, IDisposable
{
    private const int FeedCap = 100;

    private static readonly string[] AccentHex =
    [
        "#6EA8FE",
        "#C4B5FD",
        "#67E8F9",
        "#F0B429",
        "#F9A8D4",
        "#FB923C"
    ];

    private readonly IAgentDataService _data;
    private readonly IServiceProvider _services;
    private readonly Dispatcher _dispatcher;
    private readonly Dictionary<string, AgentViewModel> _agentsById = [];
    private int _accentIndex;
    private bool _windowReady;
    private bool _logOpened;
    private bool _openLogWhenReady;

    public MainViewModel(IAgentDataService data, PermissionsViewModel permissions, IServiceProvider services)
    {
        _data = data;
        Permissions = permissions;
        _services = services;
        _dispatcher = Dispatcher.CurrentDispatcher;
        Filters.Add(new AgentFilterOption(null, "All agents"));
        SelectedFilter = Filters[0];
        RosterMessage = "Ожидание ответа связного…";

        _data.SnapshotReceived += OnSnapshotReceived;
        _data.EventAdded += OnEventAdded;
        _data.UnrecognizedPayload += OnUnrecognizedPayload;
        _data.Start();
    }

    public PermissionsViewModel Permissions { get; }

    public ObservableCollection<AgentViewModel> Agents { get; } = [];

    public ObservableCollection<AgentEventViewModel> Events { get; } = [];

    public ObservableCollection<AgentEventViewModel> FilteredEvents { get; } = [];

    public ObservableCollection<AgentFilterOption> Filters { get; } = [];

    [ObservableProperty]
    private AgentViewModel? _selectedAgent;

    [ObservableProperty]
    private AgentFilterOption? _selectedFilter;

    [ObservableProperty]
    private int _agentCount;

    [ObservableProperty]
    private string _rosterMessage = "";

    public void MarkWindowReady()
    {
        _windowReady = true;
        if (_openLogWhenReady)
            OpenLogs();
    }

    public void Dispose()
    {
        _data.SnapshotReceived -= OnSnapshotReceived;
        _data.EventAdded -= OnEventAdded;
        _data.UnrecognizedPayload -= OnUnrecognizedPayload;
        _data.Stop();
    }

    partial void OnSelectedFilterChanged(AgentFilterOption? value) => RebuildFilteredEvents();

    partial void OnSelectedAgentChanged(AgentViewModel? value)
    {
        if (value != null)
            Permissions.PreferRole(value.Role);
    }

    [RelayCommand]
    private void SelectAgent(AgentViewModel? agent)
    {
        if (SelectedAgent != null)
            SelectedAgent.IsSelected = false;

        SelectedAgent = agent;

        if (SelectedAgent != null)
            SelectedAgent.IsSelected = true;
    }

    [RelayCommand]
    private void OpenTasks() => ShowWindow<TaskManagerWindow>();

    [RelayCommand]
    private void OpenLogs()
    {
        _logOpened = true;
        _openLogWhenReady = false;
        ShowWindow<LogWindow>();
    }

    private void OnSnapshotReceived(object? sender, IReadOnlyList<AgentModel> snapshot)
    {
        RunOnUi(() => ApplySnapshot(snapshot));
    }

    private void OnEventAdded(object? sender, AgentEventModel model)
    {
        RunOnUi(() => AddEvent(model));
    }

    private void OnUnrecognizedPayload(object? sender, string raw)
    {
        RunOnUi(() =>
        {
            RosterMessage = "Ответ связного не содержит список агентов. Сырой текст записан в лог.";
            if (_logOpened)
                return;
            if (!_windowReady)
            {
                _openLogWhenReady = true;
                return;
            }

            OpenLogs();
        });
    }

    private void ApplySnapshot(IReadOnlyList<AgentModel> models)
    {
        var incoming = models.Select(model => model.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var model in models)
        {
            if (_agentsById.TryGetValue(model.Id, out var existing))
            {
                existing.Apply(model);
                RefreshFilterLabel(existing);
            }
            else
            {
                var created = new AgentViewModel(model, NextAccent());
                _agentsById[created.Id] = created;
                Agents.Add(created);
                Filters.Add(new AgentFilterOption(created.Id, created.Name));
            }
        }

        foreach (var id in _agentsById.Keys.Where(id => !incoming.Contains(id)).ToList())
        {
            var agent = _agentsById[id];
            Agents.Remove(agent);
            _agentsById.Remove(id);
            var filter = Filters.FirstOrDefault(item => item.AgentId == id);
            if (filter != null)
            {
                if (ReferenceEquals(SelectedFilter, filter))
                    SelectedFilter = Filters[0];
                Filters.Remove(filter);
            }

            if (SelectedAgent?.Id == id)
            {
                agent.IsSelected = false;
                SelectAgent(Agents.FirstOrDefault());
            }
        }

        AgentCount = Agents.Count;
        if (SelectedAgent == null && Agents.Count > 0)
            SelectAgent(Agents[0]);

        RosterMessage = Agents.Count == 0
            ? "Связной не сообщил ни одного агента."
            : "";
    }

    private void AddEvent(AgentEventModel model)
    {
        if (!_agentsById.TryGetValue(model.AgentId, out var agent))
            return;

        var evt = new AgentEventViewModel(model, agent.Name, agent.AccentBrush);
        Events.Insert(0, evt);
        agent.PushRecent(evt);
        if (MatchesFilter(evt))
            FilteredEvents.Insert(0, evt);

        while (Events.Count > FeedCap)
        {
            var oldest = Events[^1];
            Events.RemoveAt(Events.Count - 1);
            FilteredEvents.Remove(oldest);
        }
    }

    private void RefreshFilterLabel(AgentViewModel agent)
    {
        var filter = Filters.FirstOrDefault(item => item.AgentId == agent.Id);
        if (filter == null || filter.Label == agent.Name)
            return;

        var index = Filters.IndexOf(filter);
        var selected = ReferenceEquals(SelectedFilter, filter);
        Filters[index] = new AgentFilterOption(agent.Id, agent.Name);
        if (selected)
            SelectedFilter = Filters[index];
    }

    private void RebuildFilteredEvents()
    {
        FilteredEvents.Clear();
        foreach (var evt in Events)
        {
            if (MatchesFilter(evt))
                FilteredEvents.Add(evt);
        }
    }

    private bool MatchesFilter(AgentEventViewModel evt)
    {
        var agentId = SelectedFilter?.AgentId;
        return string.IsNullOrEmpty(agentId) || agentId == evt.AgentId;
    }

    private Color NextAccent()
    {
        var hex = AccentHex[_accentIndex % AccentHex.Length];
        _accentIndex++;
        return (Color)ColorConverter.ConvertFromString(hex)!;
    }

    private void ShowWindow<TWindow>() where TWindow : Window
    {
        var open = Application.Current.Windows.OfType<TWindow>().FirstOrDefault();
        if (open != null)
        {
            open.Activate();
            return;
        }

        var window = _services.GetRequiredService<TWindow>();
        window.Owner = Application.Current.MainWindow;
        window.Show();
    }

    private void RunOnUi(Action action)
    {
        if (_dispatcher.CheckAccess())
            action();
        else
            _dispatcher.Invoke(action);
    }
}
