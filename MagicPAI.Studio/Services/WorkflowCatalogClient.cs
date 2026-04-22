// MagicPAI.Studio/Services/WorkflowCatalogClient.cs
// Temporal migration §10.8: fetches the workflow catalog from /api/workflows.
// The server returns user-visible entries from MagicPAI.Server.Bridge.WorkflowCatalog.
using System.Net.Http.Json;
using System.Text.Json;

namespace MagicPAI.Studio.Services;

/// <summary>
/// Fetches the user-visible workflow catalog from the server. Used by the
/// Studio form to populate the workflow dropdown and by detail views to
/// look up display metadata.
/// </summary>
public class WorkflowCatalogClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;
    private IReadOnlyList<WorkflowCatalogEntry>? _cached;

    public WorkflowCatalogClient(HttpClient http)
    {
        _http = http;
    }

    /// <summary>GET /api/workflows — returns the user-visible catalog.</summary>
    public async Task<IReadOnlyList<WorkflowCatalogEntry>> GetWorkflowsAsync(
        bool forceRefresh = false,
        CancellationToken ct = default)
    {
        if (!forceRefresh && _cached is not null)
            return _cached;

        try
        {
            var list = await _http.GetFromJsonAsync<List<WorkflowCatalogEntry>>(
                "api/workflows", JsonOptions, ct);
            _cached = (list ?? new List<WorkflowCatalogEntry>())
                .OrderBy(e => e.SortOrder)
                .ThenBy(e => e.DisplayName)
                .ToList();
        }
        catch (HttpRequestException)
        {
            _cached = new List<WorkflowCatalogEntry>();
        }
        return _cached;
    }
}

/// <summary>
/// Client-side mirror of the server's WorkflowCatalogEntry projection returned
/// by <c>/api/workflows</c>. Mirrors the anonymous type in
/// <c>SessionController.ListWorkflows</c>.
/// </summary>
public record WorkflowCatalogEntry(
    string WorkflowTypeName,
    string DisplayName,
    string Description,
    string Category,
    int SortOrder,
    bool RequiresAiAssistant,
    string[] SupportedModels);
