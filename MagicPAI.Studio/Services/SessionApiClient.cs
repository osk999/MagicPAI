using System.Net.Http.Json;
using System.Text.Json;
using MagicPAI.Shared.Models;
using MagicPAI.Studio.Models;

namespace MagicPAI.Studio.Services;

/// <summary>
/// REST API client for session management endpoints.
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

    /// <summary>List all sessions.</summary>
    public async Task<List<SessionInfo>> ListSessionsAsync()
    {
        try
        {
            return await _http.GetFromJsonAsync<List<SessionInfo>>("api/sessions")
                ?? [];
        }
        catch (HttpRequestException)
        {
            return [];
        }
    }

    /// <summary>Create a new session via the REST API.</summary>
    public async Task<string> CreateSessionAsync(
        string prompt,
        string workspacePath,
        string aiAssistant = "claude",
        string model = "auto",
        int modelPower = 0,
        string structuredOutputSchema = "",
        string workflowName = "full-orchestrate")
    {
        var request = new CreateSessionRequest(
            Prompt: prompt,
            WorkspacePath: workspacePath,
            AiAssistant: aiAssistant,
            Agent: aiAssistant,
            Model: model,
            ModelPower: modelPower,
            StructuredOutputSchema: structuredOutputSchema,
            WorkflowName: workflowName);

        var response = await _http.PostAsJsonAsync("api/sessions", request);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<CreateSessionResponse>();
        return result?.SessionId ?? "";
    }

    /// <summary>Get a specific session by ID.</summary>
    public async Task<SessionInfo?> GetSessionAsync(string id)
    {
        try
        {
            return await _http.GetFromJsonAsync<SessionInfo>($"api/sessions/{id}");
        }
        catch (HttpRequestException)
        {
            return null;
        }
    }

    /// <summary>Get buffered output for a session.</summary>
    public async Task<string[]> GetOutputAsync(string id)
    {
        try
        {
            return await _http.GetFromJsonAsync<string[]>($"api/sessions/{id}/output")
                ?? [];
        }
        catch (HttpRequestException)
        {
            return [];
        }
    }

    /// <summary>Get tracked activity states for a session.</summary>
    public async Task<List<ActivityStateDto>> GetActivitiesAsync(string id)
    {
        try
        {
            return await _http.GetFromJsonAsync<List<ActivityStateDto>>(
                $"api/sessions/{id}/activities",
                JsonOptions) ?? [];
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>Get tracked workflow insights for a session.</summary>
    public async Task<List<TaskInsightEvent>> GetInsightsAsync(string id)
    {
        try
        {
            return await _http.GetFromJsonAsync<List<TaskInsightEvent>>(
                $"api/sessions/{id}/insights",
                JsonOptions) ?? [];
        }
        catch (Exception)
        {
            return [];
        }
    }

    /// <summary>Delete / stop a session.</summary>
    public async Task<bool> DeleteSessionAsync(string id)
    {
        try
        {
            var response = await _http.DeleteAsync($"api/sessions/{id}");
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    /// <summary>Browse directories on the server.</summary>
    public async Task<BrowseResult> BrowseAsync(string? path = null)
    {
        try
        {
            var url = string.IsNullOrWhiteSpace(path)
                ? "api/browse"
                : $"api/browse?path={Uri.EscapeDataString(path)}";
            return await _http.GetFromJsonAsync<BrowseResult>(url)
                ?? new BrowseResult("", []);
        }
        catch (HttpRequestException)
        {
            return new BrowseResult("", []);
        }
    }

    /// <summary>Fetch a raw HTTP response from any server path (for API explorer).</summary>
    public async Task<(int StatusCode, string Body)> FetchRawAsync(string path)
    {
        var response = await _http.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();
        return ((int)response.StatusCode, body);
    }

    /// <summary>List available workflows.</summary>
    public async Task<List<WorkflowOption>> ListWorkflowsAsync()
    {
        try
        {
            return await _http.GetFromJsonAsync<List<WorkflowOption>>("api/browse/workflows")
                ?? [];
        }
        catch (HttpRequestException)
        {
            return [];
        }
    }
}

public record DirectoryEntry(string Name, string FullPath);
public record BrowseResult(string CurrentPath, List<DirectoryEntry> Directories, string? ParentPath = null);
public record WorkflowOption(string Id, string DisplayName, bool IsDefault = false);
public record CreateSessionRequest(
    string Prompt,
    string? WorkspacePath = null,
    string? AiAssistant = null,
    string? Agent = null,
    string? Model = null,
    int? ModelPower = null,
    string? StructuredOutputSchema = null,
    string? WorkflowName = null);
public record CreateSessionResponse(string SessionId, string? WorkflowId);
