using System.Collections.ObjectModel;
using System.Windows.Media;
using CloudHermesDashboard.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace CloudHermesDashboard.ViewModels;

public partial class AgentViewModel : ObservableObject
{
    public const int RecentLimit = 8;

    private static readonly SolidColorBrush IdleBrush = Freeze("#8B97AB");
    private static readonly SolidColorBrush ThinkingBrush = Freeze("#E3B341");
    private static readonly SolidColorBrush WaitingBrush = Freeze("#F0883E");
    private static readonly SolidColorBrush WorkingBrush = Freeze("#3FB950");
    private static readonly SolidColorBrush ErrorBrush = Freeze("#F85149");

    public AgentViewModel(AgentModel model, Color accent)
    {
        Id = model.Id;
        _name = model.Name;
        _role = model.Role;
        _rawRole = model.RawRole;
        _status = model.Status;
        _rawStatus = model.RawStatus;
        _statusKnown = model.StatusKnown;
        _currentTask = model.CurrentTask;
        _model = model.Model;
        _toolsets = model.Toolsets;

        var accentBrush = new SolidColorBrush(accent);
        accentBrush.Freeze();
        AccentBrush = accentBrush;
    }

    public string Id { get; }

    public Brush AccentBrush { get; }

    public ObservableCollection<AgentEventViewModel> RecentEvents { get; } = [];

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RoleLabel))]
    private AgentRole _role;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(RoleLabel))]
    private string _rawRole = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusLabel))]
    [NotifyPropertyChangedFor(nameof(StatusBrush))]
    private AgentStatus _status;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusLabel))]
    private string _rawStatus = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusLabel))]
    private bool _statusKnown = true;

    [ObservableProperty]
    private string _currentTask;

    [ObservableProperty]
    private string _model;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToolsetsAtSpawn))]
    private IReadOnlyList<string> _toolsets = [];

    [ObservableProperty]
    private bool _isSelected;

    public string RoleLabel =>
        Role == AgentRole.Unknown && !string.IsNullOrWhiteSpace(RawRole) ? RawRole : Role.ToString();

    public string StatusLabel =>
        !StatusKnown && !string.IsNullOrWhiteSpace(RawStatus)
            ? RawStatus
            : Status switch
            {
                AgentStatus.WaitingApproval => "Waiting approval",
                _ => Status.ToString()
            };

    public string ToolsetsAtSpawn => Toolsets.Count == 0 ? "—" : string.Join(", ", Toolsets);

    public Brush StatusBrush => Status switch
    {
        AgentStatus.Thinking => ThinkingBrush,
        AgentStatus.WaitingApproval => WaitingBrush,
        AgentStatus.Working => WorkingBrush,
        AgentStatus.Error => ErrorBrush,
        _ => IdleBrush
    };

    public void Apply(AgentModel model)
    {
        Name = model.Name;
        Role = model.Role;
        RawRole = model.RawRole;
        Status = model.Status;
        RawStatus = model.RawStatus;
        StatusKnown = model.StatusKnown;
        CurrentTask = model.CurrentTask;
        Model = model.Model;
        Toolsets = model.Toolsets;
    }

    public void PushRecent(AgentEventViewModel evt)
    {
        RecentEvents.Insert(0, evt);
        while (RecentEvents.Count > RecentLimit)
            RecentEvents.RemoveAt(RecentEvents.Count - 1);
    }

    private static SolidColorBrush Freeze(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
        brush.Freeze();
        return brush;
    }
}
