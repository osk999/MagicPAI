using System.Text.Json;
using MagicPAI.Core.Config;

namespace MagicPAI.Core.Services.Auth;

/// <summary>
/// Recovers Claude CLI auth by fetching fresh OAuth credentials from an external auth service.
/// Ported from MagicPrompt.Core/Services/Auth/AuthRecoveryService.cs (simplified, no Polly).
/// </summary>
public class AuthRecoveryService
{
    private static readonly SemaphoreSlim RecoverySemaphore = new(1, 1);
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(30) };

    private readonly MagicPaiConfig _config;

    public AuthRecoveryService(MagicPaiConfig config)
    {
        _config = config;
    }

    /// <summary>
    /// Attempt to recover auth by fetching fresh credentials from the auth service.
    /// Returns (success, error, credentialsJson).
    /// </summary>
    public async Task<(bool Success, string? Error, string? CredentialsJson)> RecoverAuthAsync(
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_config.AuthServiceUrl))
            return (false, "AuthServiceUrl not configured", null);

        var email = _config.AuthDefaultEmail;
        if (string.IsNullOrWhiteSpace(email))
            return (false, "AuthDefaultEmail not configured", null);

        // Coalesce concurrent recovery attempts
        if (!await RecoverySemaphore.WaitAsync(TimeSpan.FromSeconds(5), ct))
        {
            await RecoverySemaphore.WaitAsync(ct);
            RecoverySemaphore.Release();
            return (true, null, null); // Other caller did the work
        }

        try
        {
            var encodedEmail = Uri.EscapeDataString(email);
            var url = $"{_config.AuthServiceUrl.TrimEnd('/')}/credentials/{encodedEmail}";

            using var request = new HttpRequestMessage(HttpMethod.Get, url);
            if (!string.IsNullOrWhiteSpace(_config.AuthApiKey))
                request.Headers.Add("x-api-key", _config.AuthApiKey);

            var response = await Http.SendAsync(request, ct);
            var body = await response.Content.ReadAsStringAsync(ct);

            if ((int)response.StatusCode == 202)
                return (false, "Magic link login required. Check email inbox.", null);

            if (!response.IsSuccessStatusCode)
                return (false, $"Auth service returned {(int)response.StatusCode}: {body}", null);

            // Validate credential JSON
            using var doc = JsonDocument.Parse(body);
            if (!doc.RootElement.TryGetProperty("claudeAiOauth", out _))
                return (false, "Auth service returned invalid credentials (no claudeAiOauth)", null);

            // Write to local credential file
            var credPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".claude", ".credentials.json");
            Directory.CreateDirectory(Path.GetDirectoryName(credPath)!);
            await File.WriteAllTextAsync(credPath, body, ct);

            return (true, null, body);
        }
        catch (HttpRequestException ex)
        {
            return (false, $"Cannot reach auth service: {ex.Message}", null);
        }
        catch (Exception ex)
        {
            return (false, $"Auth recovery error: {ex.Message}", null);
        }
        finally
        {
            RecoverySemaphore.Release();
        }
    }
}
