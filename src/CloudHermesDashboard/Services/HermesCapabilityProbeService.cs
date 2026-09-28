using CloudHermesDashboard.Models;

namespace CloudHermesDashboard.Services;

public sealed class CapabilityProbe
{
    public CapabilityMode Mode { get; init; }

    public string Detail { get; init; } = "";

    public DelegationConfigRead? ServerConfig { get; init; }
}

public sealed class HermesCapabilityProbeService
{
    private readonly IHermesConfigService _config;
    private readonly IHermesChatClient _chat;
    private readonly ILoggingService _log;

    public HermesCapabilityProbeService(IHermesConfigService config, IHermesChatClient chat, ILoggingService log)
    {
        _config = config;
        _chat = chat;
        _log = log;
    }

    public async Task<CapabilityProbe> ProbeAsync(CancellationToken cancellationToken)
    {
        var access = await _config.DetectAsync(cancellationToken).ConfigureAwait(false);
        if (access.Kind != ConfigAccessKind.None)
        {
            var server = await _config.TryReadAsync(cancellationToken).ConfigureAwait(false);
            _log.Info("Probe", "Режим Remote: " + access.Detail);
            return new CapabilityProbe
            {
                Mode = CapabilityMode.Remote,
                Detail = access.Detail,
                ServerConfig = server
            };
        }

        var reach = await _chat.ProbeAsync(cancellationToken).ConfigureAwait(false);
        if (reach.Ok)
        {
            _log.Info("Probe", "Режим PolicyOnly: связной отвечает по Agent API, файл config.yaml недоступен.");
            return new CapabilityProbe
            {
                Mode = CapabilityMode.PolicyOnly,
                Detail = "Agent API HTTP " + reach.StatusCode
            };
        }

        _log.Warn("Probe", "Режим Manual. Причина: " + reach.Text);
        return new CapabilityProbe
        {
            Mode = CapabilityMode.Manual,
            Detail = reach.Text
        };
    }
}
