using System.IO;
using System.Text.RegularExpressions;

namespace CloudHermesDashboard.Services;

public sealed record HermesEndpoint(string BaseUrl, string ApiKey, string SessionId, string Source);

public sealed class HermesEndpointResolver
{
    private static readonly Regex StringConst = new(
        @"const val (\w+) = ""([^""]*)""",
        RegexOptions.Compiled);

    private static readonly Regex IntConst = new(
        @"const val (\w+) = (\d+)",
        RegexOptions.Compiled);

    private static readonly Regex BoolConst = new(
        @"const val (\w+) = (true|false)",
        RegexOptions.Compiled);

    private readonly DashboardSettings _settings;

    public HermesEndpointResolver(DashboardSettings settings)
    {
        _settings = settings;
    }

    public HermesEndpoint Resolve()
    {
        var sessionId = string.IsNullOrWhiteSpace(_settings.SessionId)
            ? "hermes_dashboard"
            : _settings.SessionId.Trim();

        if (!string.IsNullOrWhiteSpace(_settings.AgentApiBaseUrl))
        {
            return new HermesEndpoint(
                _settings.AgentApiBaseUrl.Trim().TrimEnd('/'),
                _settings.ApiKey.Trim(),
                sessionId,
                "settings.json");
        }

        var kt = FindHermesConfig();
        if (kt == null)
        {
            return new HermesEndpoint("", _settings.ApiKey.Trim(), sessionId, "не найден HermesConfig.kt и пустой agentApiBaseUrl");
        }

        string text;
        try
        {
            text = File.ReadAllText(kt);
        }
        catch (Exception ex)
        {
            return new HermesEndpoint("", _settings.ApiKey.Trim(), sessionId, "не удалось прочитать HermesConfig.kt: " + ex.Message);
        }

        var host = Const(text, StringConst, "HOST");
        var agentApi = Const(text, StringConst, "AGENT_API_BASE_URL");
        var key = string.IsNullOrWhiteSpace(_settings.ApiKey)
            ? Const(text, StringConst, "API_KEY") ?? ""
            : _settings.ApiKey.Trim();
        var port = int.TryParse(Const(text, IntConst, "PORT"), out var parsedPort) ? parsedPort : 443;
        var tls = Const(text, BoolConst, "USE_TLS") switch
        {
            "false" => false,
            "true" => true,
            _ => port == 443
        };

        var baseUrl = !string.IsNullOrWhiteSpace(agentApi)
            ? agentApi.Trim().TrimEnd('/')
            : string.IsNullOrWhiteSpace(host)
                ? ""
                : tls
                    ? $"https://{host}"
                    : $"http://{host}:{port}";

        return new HermesEndpoint(baseUrl, key, sessionId, kt);
    }

    private static string? FindHermesConfig()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 10 && dir != null; i++, dir = dir.Parent)
        {
            var candidate = Path.Combine(
                dir.FullName,
                "NousPortalCloudHermes",
                "AndroidHermesCloudChat",
                "app",
                "src",
                "main",
                "java",
                "com",
                "nous",
                "ahcc",
                "config",
                "HermesConfig.kt");
            if (File.Exists(candidate))
                return candidate;
        }

        return null;
    }

    private static string? Const(string text, Regex pattern, string name)
    {
        foreach (Match match in pattern.Matches(text))
        {
            if (match.Groups[1].Value == name)
                return match.Groups[2].Value;
        }

        return null;
    }
}
