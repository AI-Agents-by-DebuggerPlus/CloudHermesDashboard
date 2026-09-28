using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using CloudHermesDashboard.Models;
using CloudHermesDashboard.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace CloudHermesDashboard.ViewModels;

public partial class RoleChipViewModel : ObservableObject
{
    public RoleChipViewModel(AgentRole role)
    {
        Role = role;
        Label = role.ToString();
    }

    public AgentRole Role { get; }

    public string Label { get; }

    [ObservableProperty]
    private bool _isSelected;
}

public partial class ToolsetToggleViewModel : ObservableObject
{
    public ToolsetToggleViewModel(string key, bool locked, string? lockReason)
    {
        Key = key;
        IsLocked = locked;
        LockReason = lockReason;
    }

    public string Key { get; }

    public bool IsLocked { get; }

    public string? LockReason { get; }

    [ObservableProperty]
    private bool _isEnabled;

    [ObservableProperty]
    private bool _isEditable;
}

public partial class PermissionsViewModel : ObservableObject
{
    private static readonly string[] EditableKeys = ["terminal", "file", "web", "browser"];
    private static readonly string[] LockedKeys = ["delegation", "clarify", "memory", "code_execution", "send_message"];
    private const string LockTooltip = "заблокировано Hermes для всех подагентов";

    private static readonly SolidColorBrush RemoteBrush = Freeze("#3FB950");
    private static readonly SolidColorBrush PolicyBrush = Freeze("#E3B341");
    private static readonly SolidColorBrush ManualBrush = Freeze("#F0883E");
    private static readonly SolidColorBrush MutedBrush = Freeze("#8B97AB");
    private static readonly SolidColorBrush ErrorBrush = Freeze("#F85149");

    private readonly HermesCapabilityProbeService _probe;
    private readonly IHermesConfigService _config;
    private readonly ILoggingService _log;
    private readonly Dispatcher _dispatcher;
    private readonly Dictionary<AgentRole, HashSet<string>> _policy = [];
    private bool _loading = true;
    private bool _applying;

    public PermissionsViewModel(HermesCapabilityProbeService probe, IHermesConfigService config, ILoggingService log)
    {
        _probe = probe;
        _config = config;
        _log = log;
        _dispatcher = Dispatcher.CurrentDispatcher;

        foreach (var role in PolicyRoles)
        {
            _policy[role] = [];
            Roles.Add(new RoleChipViewModel(role));
        }

        foreach (var key in EditableKeys)
            Toolsets.Add(new ToolsetToggleViewModel(key, false, null));
        foreach (var key in LockedKeys)
            Toolsets.Add(new ToolsetToggleViewModel(key, true, LockTooltip) { IsEnabled = false, IsEditable = false });

        foreach (var toggle in Toolsets)
            toggle.PropertyChanged += OnToggleChanged;

        LoadDraft();
        SelectedRole = AgentRole.Developer;
        Roles[0].IsSelected = true;
        LoadRoleIntoToggles(SelectedRole);
        _loading = false;
        RefreshSnippet();
        ServerKnownSummary = "Текущие серверные значения неизвестны.";
        ConfigPathText = "$HERMES_HOME/config.yaml";
        _ = ProbeAsync();
    }

    public ObservableCollection<RoleChipViewModel> Roles { get; } = [];

    public ObservableCollection<ToolsetToggleViewModel> Toolsets { get; } = [];

    public AgentRole SelectedRole { get; private set; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ModeBadge))]
    [NotifyPropertyChangedFor(nameof(ModeBrush))]
    [NotifyPropertyChangedFor(nameof(IsManual))]
    private CapabilityMode? _mode;

    [ObservableProperty]
    private string _applyStatus = "";

    [ObservableProperty]
    private Brush _applyStatusBrush = MutedBrush;

    [ObservableProperty]
    private bool _isApplying;

    [ObservableProperty]
    private bool _proposalPending;

    [ObservableProperty]
    private string _manualSnippet = "";

    [ObservableProperty]
    private string _serverKnownSummary = "";

    [ObservableProperty]
    private string _configPathText = "";

    [ObservableProperty]
    private string _maxSpawnDepth = "";

    [ObservableProperty]
    private string _maxConcurrentChildren = "";

    [ObservableProperty]
    private bool? _orchestratorEnabled;

    public bool IsManual => Mode == CapabilityMode.Manual;

    public string ModeBadge => Mode switch
    {
        CapabilityMode.Remote => "Режим: удалённое применение",
        CapabilityMode.PolicyOnly => "Отправлено агенту, не гарантировано",
        CapabilityMode.Manual => "Требуется ручное действие",
        _ => "Проверка режима…"
    };

    public Brush ModeBrush => Mode switch
    {
        CapabilityMode.Remote => RemoteBrush,
        CapabilityMode.PolicyOnly => PolicyBrush,
        CapabilityMode.Manual => ManualBrush,
        _ => MutedBrush
    };

    public void PreferRole(AgentRole role)
    {
        if (!IsPolicyRole(role) || SelectedRole == role)
            return;
        SelectRole(FindChip(role));
    }

    [RelayCommand]
    private void SelectRole(RoleChipViewModel? chip)
    {
        if (chip == null)
            return;

        _loading = true;
        SelectedRole = chip.Role;
        foreach (var role in Roles)
            role.IsSelected = role.Role == chip.Role;
        LoadRoleIntoToggles(chip.Role);
        _loading = false;
        RefreshSnippet();
    }

    [RelayCommand]
    private void CopySnippet()
    {
        try
        {
            Clipboard.SetText(ManualSnippet);
            ApplyStatus = "Сниппет скопирован.";
            ApplyStatusBrush = MutedBrush;
        }
        catch (Exception ex)
        {
            ApplyStatus = ex.Message;
            ApplyStatusBrush = ErrorBrush;
        }
    }

    [RelayCommand]
    private async Task ApplyServerAsync()
    {
        if (Mode is null || _applying)
            return;

        int? depth = null;
        int? children = null;
        if (!string.IsNullOrWhiteSpace(MaxSpawnDepth))
        {
            if (!int.TryParse(MaxSpawnDepth, out var parsed) || parsed < 1)
            {
                ApplyStatus = "max_spawn_depth должен быть целым числом больше нуля.";
                ApplyStatusBrush = ErrorBrush;
                return;
            }

            depth = parsed;
        }

        if (!string.IsNullOrWhiteSpace(MaxConcurrentChildren))
        {
            if (!int.TryParse(MaxConcurrentChildren, out var parsed) || parsed < 1)
            {
                ApplyStatus = "max_concurrent_children должен быть целым числом больше нуля.";
                ApplyStatusBrush = ErrorBrush;
                return;
            }

            children = parsed;
        }

        if (depth is null && children is null && OrchestratorEnabled is null)
        {
            ApplyStatus = "Нет значений для записи.";
            ApplyStatusBrush = ErrorBrush;
            return;
        }

        var draft = new ServerSettingsDraft
        {
            MaxSpawnDepth = depth,
            MaxConcurrentChildren = children,
            OrchestratorEnabled = OrchestratorEnabled
        };

        await RunApplyAsync(async () =>
        {
            if (Mode == CapabilityMode.Remote)
                return await _config.WriteServerSettingsAsync(draft, CancellationToken.None);

            if (Mode == CapabilityMode.PolicyOnly)
                return await _config.ProposeAsync(HermesConfigService.ServerPolicyMessage(draft), CancellationToken.None);

            return new ConfigApplyResult { Message = "Скопируйте YAML и вставьте его на сервере." };
        });
    }

    partial void OnMaxSpawnDepthChanged(string value) => RefreshSnippet();

    partial void OnMaxConcurrentChildrenChanged(string value) => RefreshSnippet();

    partial void OnOrchestratorEnabledChanged(bool? value) => RefreshSnippet();

    private async Task ProbeAsync()
    {
        try
        {
            var result = await _probe.ProbeAsync(CancellationToken.None);
            RunOnUi(() =>
            {
                Mode = result.Mode;
                ConfigPathText = _config.DescribePath();
                if (result.ServerConfig != null)
                    ApplyServerRead(result.ServerConfig);
                else if (result.Mode != CapabilityMode.Remote)
                    ServerKnownSummary = "Текущие серверные значения неизвестны.";
                UpdateEditable();
                RefreshSnippet();
            });
        }
        catch (Exception ex)
        {
            _log.Error("Probe", ex.Message);
            RunOnUi(() =>
            {
                Mode = CapabilityMode.Manual;
                ApplyStatus = ex.Message;
                ApplyStatusBrush = ErrorBrush;
                UpdateEditable();
            });
        }
    }

    private void ApplyServerRead(DelegationConfigRead read)
    {
        _loading = true;
        foreach (var pair in read.RoleToolsets)
        {
            if (!IsPolicyRole(pair.Key))
                continue;

            var locked = pair.Value.Where(key => LockedKeys.Contains(key, StringComparer.OrdinalIgnoreCase)).ToList();
            if (locked.Count > 0)
            {
                _log.Warn(
                    "Config",
                    $"Роль {pair.Key}: в файле есть заблокированные toolsets ({string.Join(", ", locked)}). В панели они остаются выключенными.");
            }

            _policy[pair.Key] = pair.Value
                .Where(key => EditableKeys.Contains(key, StringComparer.OrdinalIgnoreCase))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
        }

        if (read.MaxSpawnDepth is int depth)
            MaxSpawnDepth = depth.ToString();
        if (read.MaxConcurrentChildren is int children)
            MaxConcurrentChildren = children.ToString();
        OrchestratorEnabled = read.OrchestratorEnabled;
        LoadRoleIntoToggles(SelectedRole);
        _loading = false;

        var parts = new List<string>();
        if (read.MaxSpawnDepth is int knownDepth)
            parts.Add("max_spawn_depth=" + knownDepth);
        if (read.MaxConcurrentChildren is int knownChildren)
            parts.Add("max_concurrent_children=" + knownChildren);
        if (read.OrchestratorEnabled is bool enabled)
            parts.Add("orchestrator_enabled=" + enabled.ToString().ToLowerInvariant());
        ServerKnownSummary = parts.Count == 0
            ? "В config.yaml нет известных ключей delegation."
            : "С сервера: " + string.Join(", ", parts);
        SaveDraft();
    }

    private void OnToggleChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_loading || _applying || e.PropertyName != nameof(ToolsetToggleViewModel.IsEnabled))
            return;
        if (sender is not ToolsetToggleViewModel toggle || toggle.IsLocked)
            return;

        _policy[SelectedRole] = Toolsets
            .Where(item => !item.IsLocked && item.IsEnabled)
            .Select(item => item.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        SaveDraft();
        RefreshSnippet();
        _ = ApplyRoleAsync();
    }

    private async Task ApplyRoleAsync()
    {
        if (Mode is null)
            return;

        var role = SelectedRole;
        var tools = _policy[role].ToList();
        await RunApplyAsync(async () =>
        {
            if (Mode == CapabilityMode.Remote)
                return await _config.WriteRoleToolsetsAsync(role, tools, CancellationToken.None);

            if (Mode == CapabilityMode.PolicyOnly)
                return await _config.ProposeAsync(HermesConfigService.PolicyMessage(role, tools), CancellationToken.None);

            return new ConfigApplyResult { Message = "Тумблер сохранён локально. Скопируйте YAML и вставьте его на сервере." };
        });
    }

    private async Task RunApplyAsync(Func<Task<ConfigApplyResult>> action)
    {
        _applying = true;
        IsApplying = true;
        UpdateEditable();
        try
        {
            var result = await action();
            ProposalPending = result.Proposed;
            ApplyStatus = result.Message;
            ApplyStatusBrush = result.Written ? RemoteBrush : result.Proposed ? PolicyBrush : string.IsNullOrWhiteSpace(result.Message) ? MutedBrush : ErrorBrush;
            if (Mode == CapabilityMode.Manual)
                ApplyStatusBrush = ManualBrush;
        }
        catch (Exception ex)
        {
            _log.Error("Config", ex.Message);
            ApplyStatus = ex.Message;
            ApplyStatusBrush = ErrorBrush;
        }
        finally
        {
            _applying = false;
            IsApplying = false;
            UpdateEditable();
        }
    }

    private void LoadRoleIntoToggles(AgentRole role)
    {
        var enabled = _policy[role];
        foreach (var toggle in Toolsets)
        {
            if (!toggle.IsLocked)
                toggle.IsEnabled = enabled.Contains(toggle.Key);
        }
    }

    private void UpdateEditable()
    {
        var editable = Mode is not null && !IsApplying;
        foreach (var toggle in Toolsets)
            toggle.IsEditable = editable && !toggle.IsLocked;
    }

    private void RefreshSnippet()
    {
        if (_loading && ManualSnippet.Length > 0)
            return;

        ManualSnippet = _config.BuildManualSnippet(new ManualSnippetDraft
        {
            Roles = _policy.ToDictionary(pair => pair.Key, pair => (IReadOnlyList<string>)pair.Value.ToList()),
            MaxSpawnDepth = MaxSpawnDepth,
            MaxConcurrentChildren = MaxConcurrentChildren,
            OrchestratorEnabled = OrchestratorEnabled
        });
    }

    private void LoadDraft()
    {
        try
        {
            if (!File.Exists(DraftPath))
                return;
            var draft = JsonSerializer.Deserialize<DraftFile>(File.ReadAllText(DraftPath));
            if (draft?.Roles == null)
                return;
            foreach (var pair in draft.Roles)
            {
                if (!Enum.TryParse<AgentRole>(pair.Key, true, out var role) || !IsPolicyRole(role))
                    continue;
                _policy[role] = pair.Value?.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? [];
            }

            MaxSpawnDepth = draft.MaxSpawnDepth ?? "";
            MaxConcurrentChildren = draft.MaxConcurrentChildren ?? "";
            OrchestratorEnabled = draft.OrchestratorEnabled;
        }
        catch (Exception ex)
        {
            _log.Warn("Config", "Черновик политики не прочитан: " + ex.Message);
        }
    }

    private void SaveDraft()
    {
        try
        {
            var dir = Path.GetDirectoryName(DraftPath);
            if (!string.IsNullOrWhiteSpace(dir))
                Directory.CreateDirectory(dir);
            var draft = new DraftFile
            {
                Roles = _policy.ToDictionary(pair => pair.Key.ToString(), pair => pair.Value.ToArray()),
                MaxSpawnDepth = MaxSpawnDepth,
                MaxConcurrentChildren = MaxConcurrentChildren,
                OrchestratorEnabled = OrchestratorEnabled
            };
            File.WriteAllText(DraftPath, JsonSerializer.Serialize(draft, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex)
        {
            _log.Warn("Config", "Черновик политики не сохранён: " + ex.Message);
        }
    }

    private RoleChipViewModel FindChip(AgentRole role) =>
        Roles.First(chip => chip.Role == role);

    private void RunOnUi(Action action)
    {
        if (_dispatcher.CheckAccess())
            action();
        else
            _dispatcher.Invoke(action);
    }

    private static bool IsPolicyRole(AgentRole role) => PolicyRoles.Contains(role);

    private static readonly AgentRole[] PolicyRoles =
    [
        AgentRole.Developer,
        AgentRole.Researcher,
        AgentRole.Reviewer,
        AgentRole.Planner,
        AgentRole.Universal
    ];

    private static string DraftPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "HermesDashboard",
            "policy-draft.json");

    private static SolidColorBrush Freeze(string hex)
    {
        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hex)!);
        brush.Freeze();
        return brush;
    }

    private sealed class DraftFile
    {
        public Dictionary<string, string[]>? Roles { get; set; }

        public string? MaxSpawnDepth { get; set; }

        public string? MaxConcurrentChildren { get; set; }

        public bool? OrchestratorEnabled { get; set; }
    }
}
