using System.IO;
using System.Text;
using System.Text.Json;
using CloudHermesDashboard.Models;
using Renci.SshNet;

namespace CloudHermesDashboard.Services;

public enum ConfigAccessKind
{
    None,
    LocalFile,
    Ssh
}

public sealed class ConfigAccess
{
    public static readonly ConfigAccess None = new() { Kind = ConfigAccessKind.None };

    public ConfigAccessKind Kind { get; init; }

    public string Path { get; init; } = "";

    public string Detail { get; init; } = "";
}

public sealed class ConfigApplyResult
{
    public bool Written { get; init; }

    public bool Proposed { get; init; }

    public string Message { get; init; } = "";
}

public interface IHermesConfigService
{
    ConfigAccess Access { get; }

    Task<ConfigAccess> DetectAsync(CancellationToken cancellationToken);

    Task<DelegationConfigRead?> TryReadAsync(CancellationToken cancellationToken);

    Task<ConfigApplyResult> WriteRoleToolsetsAsync(AgentRole role, IReadOnlyList<string> toolsets, CancellationToken cancellationToken);

    Task<ConfigApplyResult> WriteServerSettingsAsync(ServerSettingsDraft draft, CancellationToken cancellationToken);

    Task<ConfigApplyResult> ProposeAsync(string message, CancellationToken cancellationToken);

    string DescribePath();

    string BuildManualSnippet(ManualSnippetDraft draft);
}

public sealed class ManualSnippetDraft
{
    public required IReadOnlyDictionary<AgentRole, IReadOnlyList<string>> Roles { get; init; }

    public string? MaxSpawnDepth { get; init; }

    public string? MaxConcurrentChildren { get; init; }

    public bool? OrchestratorEnabled { get; init; }
}

public sealed class HermesConfigService : IHermesConfigService
{
    private static readonly AgentRole[] PolicyRoles =
    [
        AgentRole.Developer,
        AgentRole.Researcher,
        AgentRole.Reviewer,
        AgentRole.Planner,
        AgentRole.Universal
    ];

    private readonly DashboardSettings _settings;
    private readonly IHermesChatClient _chat;
    private readonly ILoggingService _log;
    private ConfigAccess _access = ConfigAccess.None;

    public HermesConfigService(DashboardSettings settings, IHermesChatClient chat, ILoggingService log)
    {
        _settings = settings;
        _chat = chat;
        _log = log;
    }

    public ConfigAccess Access => _access;

    public Task<ConfigAccess> DetectAsync(CancellationToken cancellationToken) =>
        Task.Run(() => Detect(cancellationToken), cancellationToken);

    public Task<DelegationConfigRead?> TryReadAsync(CancellationToken cancellationToken) =>
        Task.Run(ReadCore, cancellationToken);

    public Task<ConfigApplyResult> WriteRoleToolsetsAsync(AgentRole role, IReadOnlyList<string> toolsets, CancellationToken cancellationToken) =>
        Task.Run(() => Write(yaml => YamlConfigEditor.WriteRole(yaml, role, toolsets), "toolsets " + role), cancellationToken);

    public Task<ConfigApplyResult> WriteServerSettingsAsync(ServerSettingsDraft draft, CancellationToken cancellationToken) =>
        Task.Run(() => Write(yaml => YamlConfigEditor.WriteServer(yaml, draft), "server settings"), cancellationToken);

    public async Task<ConfigApplyResult> ProposeAsync(string message, CancellationToken cancellationToken)
    {
        _log.Info("Config", "Служебное сообщение политики отправлено связному.");
        var result = await _chat.SendAsync(message, cancellationToken).ConfigureAwait(false);
        if (!result.Ok)
        {
            _log.Error("Config", "Связной не принял политику: " + result.Text);
            return new ConfigApplyResult { Message = result.Text };
        }

        _log.Warn("Config", "Ответ на политику (не подтверждение записи):\n" + result.Text);
        return new ConfigApplyResult
        {
            Proposed = true,
            Message = "Предложено агенту, не гарантировано"
        };
    }

    public string DescribePath() => _access.Kind switch
    {
        ConfigAccessKind.LocalFile => _access.Path,
        ConfigAccessKind.Ssh => _access.Detail,
        _ => string.IsNullOrWhiteSpace(_settings.RemoteConfigPath)
            ? "$HERMES_HOME/config.yaml"
            : _settings.RemoteConfigPath
    };

    public string BuildManualSnippet(ManualSnippetDraft draft)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Paste into $HERMES_HOME/config.yaml");
        builder.AppendLine("# A service restart may be required before new delegate_task spawns pick this up.");
        builder.AppendLine("# Already running agents keep the toolsets they were spawned with.");
        builder.AppendLine("delegation:");
        if (draft.OrchestratorEnabled is bool enabled)
            builder.AppendLine("  orchestrator_enabled: " + (enabled ? "true" : "false"));
        else
            builder.AppendLine("  # orchestrator_enabled: true");

        AppendNumber(builder, "max_spawn_depth", draft.MaxSpawnDepth);
        AppendNumber(builder, "max_concurrent_children", draft.MaxConcurrentChildren);
        builder.AppendLine("  models:");
        foreach (var role in PolicyRoles)
        {
            draft.Roles.TryGetValue(role, out var tools);
            tools ??= [];
            builder.AppendLine("    " + role.ToString().ToLowerInvariant() + ":");
            builder.AppendLine("      toolsets: [" + string.Join(", ", tools) + "]");
        }

        return builder.ToString().TrimEnd();
    }

    private ConfigAccess Detect(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var local = ResolveLocalPath();
        if (local != null)
        {
            _access = new ConfigAccess
            {
                Kind = ConfigAccessKind.LocalFile,
                Path = local,
                Detail = local
            };
            _log.Info("Config", "Локальный config.yaml: " + local);
            return _access;
        }

        if (!SshConfigured())
        {
            _access = ConfigAccess.None;
            _log.Info("Config", "SSH и локальный config.yaml не настроены. Remote-запись недоступна.");
            return _access;
        }

        try
        {
            using var sftp = OpenSftp();
            sftp.Connect();
            if (!sftp.Exists(_settings.RemoteConfigPath))
            {
                _access = ConfigAccess.None;
                _log.Warn("Config", "SSH открылся, но файл не найден: " + _settings.RemoteConfigPath);
                return _access;
            }

            _access = new ConfigAccess
            {
                Kind = ConfigAccessKind.Ssh,
                Path = _settings.RemoteConfigPath,
                Detail = $"{_settings.SshUser}@{_settings.SshHost}:{_settings.RemoteConfigPath}"
            };
            _log.Info("Config", "SSH config.yaml: " + _access.Detail);
            return _access;
        }
        catch (Exception ex)
        {
            _access = ConfigAccess.None;
            _log.Warn("Config", "SSH-проверка не удалась: " + ex.Message);
            return _access;
        }
    }

    private DelegationConfigRead? ReadCore()
    {
        if (_access.Kind == ConfigAccessKind.None)
            return null;

        try
        {
            var yaml = ReadText();
            return YamlConfigEditor.Read(yaml);
        }
        catch (Exception ex)
        {
            _log.Error("Config", "Чтение config.yaml: " + ex.Message);
            return null;
        }
    }

    private ConfigApplyResult Write(Func<string, string> edit, string what)
    {
        if (_access.Kind == ConfigAccessKind.None)
            return new ConfigApplyResult { Message = "Нет доступа к config.yaml." };

        try
        {
            var yaml = ReadText();
            var updated = edit(yaml);
            _log.Warn("Config", "Запись пересобирает YAML. Комментарии в config.yaml могут исчезнуть. Рядом сохраняется .hermes-dashboard.bak.");
            WriteText(yaml, updated);
            _log.Info("Config", "Записано " + what + " в " + DescribePath());
            return new ConfigApplyResult { Written = true, Message = "Записано в " + DescribePath() };
        }
        catch (Exception ex)
        {
            _log.Error("Config", "Запись " + what + ": " + ex.Message);
            return new ConfigApplyResult { Message = ex.Message };
        }
    }

    private string ReadText()
    {
        if (_access.Kind == ConfigAccessKind.LocalFile)
            return File.ReadAllText(_access.Path);

        using var sftp = OpenSftp();
        sftp.Connect();
        using var stream = new MemoryStream();
        sftp.DownloadFile(_access.Path, stream);
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private void WriteText(string original, string updated)
    {
        var bytes = Encoding.UTF8.GetBytes(original);
        if (_access.Kind == ConfigAccessKind.LocalFile)
        {
            var backup = _access.Path + ".hermes-dashboard.bak";
            File.Copy(_access.Path, backup, true);
            File.WriteAllText(_access.Path, updated);
            _log.Info("Config", "Резервная копия: " + backup);
            return;
        }

        using var sftp = OpenSftp();
        sftp.Connect();
        using (var backup = new MemoryStream(bytes))
            sftp.UploadFile(backup, _access.Path + ".hermes-dashboard.bak", true);
        using var upload = new MemoryStream(Encoding.UTF8.GetBytes(updated));
        sftp.UploadFile(upload, _access.Path, true);
        _log.Info("Config", "Резервная копия: " + _access.Path + ".hermes-dashboard.bak");
    }

    private string? ResolveLocalPath()
    {
        if (!string.IsNullOrWhiteSpace(_settings.LocalConfigPath) && File.Exists(_settings.LocalConfigPath))
            return _settings.LocalConfigPath;

        var home = Environment.GetEnvironmentVariable("HERMES_HOME");
        if (string.IsNullOrWhiteSpace(home))
            return null;
        var path = Path.Combine(home, "config.yaml");
        return File.Exists(path) ? path : null;
    }

    private bool SshConfigured() =>
        !string.IsNullOrWhiteSpace(_settings.SshHost)
        && !string.IsNullOrWhiteSpace(_settings.SshUser)
        && !string.IsNullOrWhiteSpace(_settings.RemoteConfigPath)
        && (!string.IsNullOrWhiteSpace(_settings.SshKeyPath) || !string.IsNullOrWhiteSpace(_settings.SshPassword));

    private SftpClient OpenSftp()
    {
        var methods = new List<AuthenticationMethod>();
        if (!string.IsNullOrWhiteSpace(_settings.SshKeyPath))
            methods.Add(new PrivateKeyAuthenticationMethod(_settings.SshUser, new PrivateKeyFile(_settings.SshKeyPath)));
        if (!string.IsNullOrWhiteSpace(_settings.SshPassword))
            methods.Add(new PasswordAuthenticationMethod(_settings.SshUser, _settings.SshPassword));

        var info = new ConnectionInfo(_settings.SshHost, _settings.SshPort <= 0 ? 22 : _settings.SshPort, _settings.SshUser, methods.ToArray())
        {
            Timeout = TimeSpan.FromSeconds(5)
        };
        var client = new SftpClient(info)
        {
            OperationTimeout = TimeSpan.FromSeconds(8)
        };
        return client;
    }

    private static void AppendNumber(StringBuilder builder, string key, string? raw)
    {
        if (int.TryParse(raw, out var value))
            builder.AppendLine("  " + key + ": " + value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        else
            builder.AppendLine("  # " + key + ":");
    }

    public static string PolicyMessage(AgentRole role, IReadOnlyList<string> toolsets)
    {
        var payload = JsonSerializer.Serialize(new
        {
            role = role.ToString().ToLowerInvariant(),
            toolsets
        });
        return "system: update_delegation_policy\n"
               + "Apply this toolset policy for future delegate_task spawns of this role only. Do not change agents that are already running.\n"
               + payload;
    }

    public static string ServerPolicyMessage(ServerSettingsDraft draft)
    {
        var payload = new Dictionary<string, object?>();
        if (draft.OrchestratorEnabled is bool enabled)
            payload["orchestrator_enabled"] = enabled;
        if (draft.MaxSpawnDepth is int depth)
            payload["max_spawn_depth"] = depth;
        if (draft.MaxConcurrentChildren is int children)
            payload["max_concurrent_children"] = children;

        return "system: update_delegation_policy\n"
               + "Update these global delegation settings for future spawns. Do not change agents that are already running.\n"
               + JsonSerializer.Serialize(payload);
    }
}
