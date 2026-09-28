namespace CloudHermesDashboard.Services;

public interface IHermesChatClient : IDisposable
{
    HermesEndpoint Endpoint { get; }

    Task<HermesChatResult> SendAsync(string message, CancellationToken cancellationToken);

    Task<HermesChatResult> ProbeAsync(CancellationToken cancellationToken);
}

public sealed class HermesChatResult
{
    public bool Ok { get; init; }

    public int StatusCode { get; init; }

    public string Text { get; init; } = "";

    public static HermesChatResult Success(int statusCode, string text) =>
        new() { Ok = true, StatusCode = statusCode, Text = text };

    public static HermesChatResult Fail(int statusCode, string text) =>
        new() { Ok = false, StatusCode = statusCode, Text = text };
}
