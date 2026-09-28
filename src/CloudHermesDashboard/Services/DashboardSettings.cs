using System.IO;
using System.Text.Json;

namespace CloudHermesDashboard.Services;

public sealed class DashboardSettings
{
    public int PollIntervalSeconds { get; set; } = 7;

    public int ArchiveSeconds { get; set; } = 20;

    public string SessionId { get; set; } = "hermes_dashboard";

    public string AgentApiBaseUrl { get; set; } = "";

    public string ApiKey { get; set; } = "";

    public string LocalConfigPath { get; set; } = "";

    public string SshHost { get; set; } = "";

    public int SshPort { get; set; } = 22;

    public string SshUser { get; set; } = "";

    public string SshKeyPath { get; set; } = "";

    public string SshPassword { get; set; } = "";

    public string RemoteConfigPath { get; set; } = "";

    public int ClampedPollSeconds => Math.Clamp(PollIntervalSeconds, 5, 10);

    public int ClampedArchiveSeconds => Math.Clamp(ArchiveSeconds <= 0 ? 20 : ArchiveSeconds, 5, 600);

    public static string FilePath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "HermesDashboard",
            "settings.json");

    public static DashboardSettings Load()
    {
        try
        {
            if (!File.Exists(FilePath))
            {
                var created = new DashboardSettings();
                created.Save();
                return created;
            }

            var json = File.ReadAllText(FilePath);
            return JsonSerializer.Deserialize<DashboardSettings>(json) ?? new DashboardSettings();
        }
        catch
        {
            return new DashboardSettings();
        }
    }

    public void Save()
    {
        var dir = Path.GetDirectoryName(FilePath);
        if (!string.IsNullOrWhiteSpace(dir))
            Directory.CreateDirectory(dir);

        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(FilePath, json);
    }
}
