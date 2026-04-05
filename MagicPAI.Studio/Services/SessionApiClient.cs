using System.Net.Http.Json;
using MagicPAI.Core.Models;

namespace MagicPAI.Studio.Services;

/// <summary>
/// REST API client for session management endpoints.
/// </summary>
public class SessionApiClient
{
    private readonly HttpClient _http;

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
}
