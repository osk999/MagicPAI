// MagicPAI.Studio/Services/SessionApiClient.cs
// Temporal migration §10.8 / §X: REST client for /api/sessions.
// Request/response shape matches MagicPAI.Server.Controllers.SessionController.
using System.Net.Http.Json;
using System.Text.Json;

namespace MagicPAI.Studio.Services;

/// <summary>
/// REST API client for session management endpoints. All calls target the
/// unified Temporal-backed /api/sessions endpoint on the server.
/// </summary>
public class SessionApiClient
{
    private readonly HttpClient _http;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public Uri BaseAddress => _http.BaseAddress!;

    public SessionApiClient(HttpClient http)
    {
        _http = http;
    }

    /// <summary>Create a new session via Temporal dispatch.</summary>
    public async Task<CreateSessionResponse> CreateAsync(CreateSessionRequest req, CancellationToken ct = default)
    {
        var response = await _http.PostAsJsonAsync("api/sessions", req, JsonOptions, ct);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<CreateSessionResponse>(JsonOptions, ct);
        return result ?? new CreateSessionResponse("", "");
    }

    /// <summary>List recent sessions via Temporal visibility.</summary>
    public async Task<List<SessionSummary>> ListAsync(int take = 100, CancellationToken ct = default)
    {
        try
        {
            var url = $"api/sessions?take={take}";
            return await _http.GetFromJsonAsync<List<SessionSummary>>(url, JsonOptions, ct)
                ?? new List<SessionSummary>();
        }
        catch (HttpRequestException)
        {
            return new List<SessionSummary>();
        }
    }

    /// <summary>Describe a single session via Temporal DescribeAsync.</summary>
    public async Task<SessionDescription?> GetAsync(string id, CancellationToken ct = default)
    {
        try
        {
            return await _http.GetFromJsonAsync<SessionDescription>(
                $"api/sessions/{id}", JsonOptions, ct);
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    /// <summary>Request graceful cancellation via workflow handle.</summary>
    public async Task<bool> CancelAsync(string id, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.DeleteAsync($"api/sessions/{id}", ct);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    /// <summary>Force-terminate a workflow.</summary>
    public async Task<bool> TerminateAsync(string id, string? reason = null, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync(
                $"api/sessions/{id}/terminate",
                new { Reason = reason },
                JsonOptions,
                ct);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    /// <summary>Approve a gate (signal ApproveGate to workflow).</summary>
    public async Task<bool> ApproveGateAsync(string id, string? comment = null, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync(
                $"api/sessions/{id}/approve",
                new ApprovalRequest(true, comment),
                JsonOptions,
                ct);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    /// <summary>Reject a gate (signal RejectGate to workflow).</summary>
    public async Task<bool> RejectGateAsync(string id, string? reason = null, CancellationToken ct = default)
    {
        try
        {
            var response = await _http.PostAsJsonAsync(
                $"api/sessions/{id}/approve",
                new ApprovalRequest(false, reason),
                JsonOptions,
                ct);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }
}

// ───────────────────────────────────────────────────────────────────────────
// DTOs shared with MagicPAI.Server.Controllers.SessionController.
// Mirrored here (Blazor WASM cannot reference server assembly).
// ───────────────────────────────────────────────────────────────────────────

/// <summary>
/// Generic request for creating a session. Workflow-specific optional
/// parameters are carried here so the server-side planner can project them
/// into the appropriate typed workflow input.
/// </summary>
public record CreateSessionRequest(
    string Prompt,
    string WorkflowType = "FullOrchestrate",
    string? AiAssistant = null,
    string? Model = null,
    int ModelPower = 0,
    string? WorkspacePath = null,
    bool? EnableGui = null,

    // Workflow-specific passthroughs ────────────────────────────────────────
    string? SectionId = null,
    IReadOnlyList<string>? SectionIds = null,
    string? EvalTaskId = null,
    string? TaskId = null,
    IReadOnlyList<string>? DependsOn = null,
    IReadOnlyList<string>? FilesTouched = null,
    string? ParentSessionId = null,
    string? ContainerId = null,
    string? WorkerOutput = null,
    IReadOnlyList<string>? Gates = null,
    int? MaxRepairAttempts = null,
    Dictionary<string, string>? CustomParams = null);

public record CreateSessionResponse(string SessionId, string WorkflowType);

public record ApprovalRequest(bool Approved, string? Comment = null);

/// <summary>Summary row in the session list (per §10.12).</summary>
public record SessionSummary(
    string SessionId,
    string WorkflowType,
    string Status,
    DateTime StartTime,
    DateTime? CloseTime,
    string AiAssistant,
    decimal TotalCostUsd);

/// <summary>Workflow describe response from Temporal.</summary>
public record SessionDescription(
    string SessionId,
    string Status,
    string WorkflowType,
    DateTime StartTime,
    DateTime? CloseTime,
    string RunId,
    string TaskQueue);
