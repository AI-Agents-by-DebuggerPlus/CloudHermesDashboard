using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace CloudHermesDashboard.Services;

/// <summary>
/// Wraps the existing Hermes Agent session stream used by HermesHttpSseClient.streamAgentSession:
/// POST /api/sessions/{id}/chat/stream with {"input":"..."}.
/// The Inference /v1/chat/completions path is not called.
/// </summary>
public sealed class HermesSessionChatClient : IHermesChatClient
{
    private readonly HttpClient _http = new(new SocketsHttpHandler
    {
        ConnectTimeout = TimeSpan.FromSeconds(8)
    })
    {
        Timeout = TimeSpan.FromMinutes(3)
    };

    private readonly SemaphoreSlim _sendGate = new(1, 1);
    private readonly ILoggingService _log;
    private readonly HermesEndpointResolver _resolver;
    private bool _sessionReady;

    public HermesSessionChatClient(HermesEndpointResolver resolver, ILoggingService log)
    {
        _resolver = resolver;
        _log = log;
        Endpoint = resolver.Resolve();
        _log.Info(
            "Chat",
            $"Сессия Hermes: {DescribeEndpoint()}");
    }

    public HermesEndpoint Endpoint { get; }

    public async Task<HermesChatResult> ProbeAsync(CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(Endpoint.BaseUrl))
            return HermesChatResult.Fail(0, "Адрес Agent API пуст. Заполните agentApiBaseUrl в " + DashboardSettings.FilePath);

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(8));
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Get,
                $"{Endpoint.BaseUrl}/api/sessions/{Uri.EscapeDataString(Endpoint.SessionId)}");
            AddAuth(request);
            using var response = await _http.SendAsync(request, timeout.Token).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            var text = Trim(body);
            if (response.IsSuccessStatusCode || (int)response.StatusCode == 404)
            {
                _log.Info("Chat", $"Проверка связного HTTP {(int)response.StatusCode}.");
                return HermesChatResult.Success((int)response.StatusCode, text);
            }

            _log.Warn("Chat", $"Проверка связного HTTP {(int)response.StatusCode}: {text}");
            return HermesChatResult.Fail((int)response.StatusCode, text);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _log.Warn("Chat", "Проверка связного не удалась: " + ex.Message);
            return HermesChatResult.Fail(0, ex.Message);
        }
    }

    public async Task<HermesChatResult> SendAsync(string message, CancellationToken cancellationToken)
    {
        await _sendGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (string.IsNullOrWhiteSpace(Endpoint.BaseUrl))
                return HermesChatResult.Fail(0, "Адрес Agent API пуст.");

            var ready = await EnsureSessionAsync(cancellationToken).ConfigureAwait(false);
            if (!ready.Ok)
                return ready;

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"{Endpoint.BaseUrl}/api/sessions/{Uri.EscapeDataString(Endpoint.SessionId)}/chat/stream");
            AddAuth(request);
            request.Content = new StringContent(
                JsonSerializer.Serialize(new { input = message }),
                Encoding.UTF8,
                "application/json");

            using var response = await _http.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken).ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = Trim(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
                _log.Warn("Chat", $"chat/stream HTTP {(int)response.StatusCode}: {errorBody}");
                if ((int)response.StatusCode == 404)
                    _sessionReady = false;
                return HermesChatResult.Fail((int)response.StatusCode, errorBody);
            }

            var parsed = await ReadStreamAsync(response, cancellationToken).ConfigureAwait(false);
            _log.Info("Chat", $"Ответ связного, {parsed.Length} символов.");
            return HermesChatResult.Success((int)response.StatusCode, parsed);
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            _log.Error("Chat", ex.Message);
            return HermesChatResult.Fail(0, ex.Message);
        }
        finally
        {
            _sendGate.Release();
        }
    }

    public void Dispose()
    {
        _http.Dispose();
        _sendGate.Dispose();
    }

    private async Task<HermesChatResult> EnsureSessionAsync(CancellationToken cancellationToken)
    {
        if (_sessionReady)
            return HermesChatResult.Success(200, "");

        using var probe = new HttpRequestMessage(
            HttpMethod.Get,
            $"{Endpoint.BaseUrl}/api/sessions/{Uri.EscapeDataString(Endpoint.SessionId)}");
        AddAuth(probe);
        using var response = await _http.SendAsync(probe, cancellationToken).ConfigureAwait(false);
        if (response.IsSuccessStatusCode)
        {
            _sessionReady = true;
            return HermesChatResult.Success((int)response.StatusCode, "");
        }

        if ((int)response.StatusCode != 404)
        {
            var body = Trim(await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            return HermesChatResult.Fail((int)response.StatusCode, body);
        }

        using var create = new HttpRequestMessage(HttpMethod.Post, $"{Endpoint.BaseUrl}/api/sessions");
        AddAuth(create);
        create.Content = new StringContent(
            JsonSerializer.Serialize(new { id = Endpoint.SessionId, title = "Hermes Dashboard" }),
            Encoding.UTF8,
            "application/json");
        using var created = await _http.SendAsync(create, cancellationToken).ConfigureAwait(false);
        if (!created.IsSuccessStatusCode && (int)created.StatusCode != 409)
        {
            var body = Trim(await created.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false));
            return HermesChatResult.Fail((int)created.StatusCode, body);
        }

        _sessionReady = true;
        _log.Info("Chat", "Сессия dashboard создана или уже была.");
        return HermesChatResult.Success((int)created.StatusCode, "");
    }

    private async Task<string> ReadStreamAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        var assistant = new StringBuilder();
        var data = new StringBuilder();
        var raw = new StringBuilder();
        var eventName = "";
        var sawData = false;

        while (true)
        {
            var line = await reader.ReadLineAsync(cancellationToken).ConfigureAwait(false);
            if (line == null)
                break;
            if (raw.Length < 200_000)
                raw.AppendLine(line);
            if (line.Length == 0 || line.StartsWith(':'))
                continue;
            if (line.StartsWith("event:", StringComparison.Ordinal))
            {
                eventName = line["event:".Length..].Trim();
                continue;
            }

            if (!line.StartsWith("data:", StringComparison.Ordinal))
                continue;

            sawData = true;
            var payload = line["data:".Length..].Trim();
            if (payload == "[DONE]")
                break;
            if (data.Length < 200_000)
            {
                if (data.Length > 0)
                    data.Append('\n');
                data.Append(payload);
            }

            if (eventName is "run.failed" or "error")
            {
                var err = ExtractError(payload) ?? payload;
                _log.Warn("Chat", "Событие ошибки потока: " + Trim(err));
            }

            var delta = ExtractDelta(payload);
            if (!string.IsNullOrEmpty(delta) && assistant.Length < 200_000)
                assistant.Append(delta);
        }

        if (assistant.Length > 0)
            return assistant.ToString();

        if (sawData)
        {
            _log.Warn("Chat", "В потоке не было assistant.delta. Дальше используется сырой SSE data.");
            return data.ToString();
        }

        _log.Warn("Chat", "Ответ не похож на SSE. Дальше используется сырое тело.");
        return raw.ToString();
    }

    private void AddAuth(HttpRequestMessage request)
    {
        if (!string.IsNullOrWhiteSpace(Endpoint.ApiKey))
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Endpoint.ApiKey);
    }

    private string DescribeEndpoint()
    {
        var keyLen = Endpoint.ApiKey.Length;
        var host = string.IsNullOrWhiteSpace(Endpoint.BaseUrl) ? "(пусто)" : Endpoint.BaseUrl;
        return $"{host} session={Endpoint.SessionId} keyLen={keyLen} source={Endpoint.Source}";
    }

    private static string? ExtractDelta(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return null;
            if (doc.RootElement.TryGetProperty("delta", out var delta) && delta.ValueKind == JsonValueKind.String)
                return delta.GetString();
            if (doc.RootElement.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
                return content.GetString();
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string? ExtractError(string payload)
    {
        try
        {
            using var doc = JsonDocument.Parse(payload);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return null;
            if (doc.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String)
                return error.GetString();
            if (doc.RootElement.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
                return message.GetString();
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string Trim(string text)
    {
        if (string.IsNullOrEmpty(text))
            return "";
        var flat = text.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return flat.Length <= 4000 ? flat : flat[..4000] + "…";
    }
}
