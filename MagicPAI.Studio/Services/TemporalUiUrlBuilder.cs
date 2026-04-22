// MagicPAI.Studio/Services/TemporalUiUrlBuilder.cs
// Temporal migration §10.10: builds deep-link URLs to Temporal Web UI.
using System.Net.Http.Json;
using System.Text.Json;

namespace MagicPAI.Studio.Services;

/// <summary>
/// Builds deep-link URLs into the Temporal Web UI for a given session id.
/// Configuration is fetched once from <c>/api/config/temporal</c>; defaults
/// are used if the server has not exposed that endpoint yet.
/// </summary>
public class TemporalUiUrlBuilder
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private string _baseUrl = "http://localhost:8233";
    private string _namespace = "magicpai";
    private bool _initialized;

    public TemporalUiUrlBuilder(HttpClient http)
    {
        _http = http;
    }

    public string BaseUrl => _baseUrl;
    public string Namespace => _namespace;

    /// <summary>Deep link to a workflow by id.</summary>
    public string ForSession(string sessionId) =>
        $"{_baseUrl.TrimEnd('/')}/namespaces/{_namespace}/workflows/{sessionId}";

    /// <summary>Deep link to the namespace's workflow list.</summary>
    public string ForNamespace() =>
        $"{_baseUrl.TrimEnd('/')}/namespaces/{_namespace}/workflows";

    /// <summary>
    /// Load the UI base URL and namespace from <c>/api/config/temporal</c>.
    /// Safe to call multiple times; subsequent calls are no-ops.
    /// </summary>
    public async Task InitializeAsync(CancellationToken ct = default)
    {
        if (_initialized) return;
        try
        {
            var cfg = await _http.GetFromJsonAsync<TemporalConfigResponse>(
                "api/config/temporal", JsonOptions, ct);
            if (cfg is not null)
            {
                if (!string.IsNullOrWhiteSpace(cfg.UiBaseUrl))
                    _baseUrl = cfg.UiBaseUrl;
                if (!string.IsNullOrWhiteSpace(cfg.Namespace))
                    _namespace = cfg.Namespace;
            }
        }
        catch
        {
            // Config endpoint may not exist yet — fall back to defaults.
        }
        _initialized = true;
    }

    private sealed record TemporalConfigResponse(string? UiBaseUrl, string? Namespace);
}
