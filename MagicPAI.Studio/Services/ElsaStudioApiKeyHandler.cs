using System.Net.Http.Headers;

namespace MagicPAI.Studio.Services;

public sealed class ElsaStudioConnectionSettings
{
    public string ApiKey { get; set; } = "00000000-0000-0000-0000-000000000000";
}

/// <summary>
/// HTTP message handler that adds the Elsa admin API key header.
/// In development with DisableSecurity, this is a no-op but ensures
/// the handler chain is properly formed for WASM HTTP clients.
/// </summary>
public class ElsaStudioApiKeyHandler(ElsaStudioConnectionSettings settings) : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Add API key and JSON Accept header
        request.Headers.Authorization = new AuthenticationHeaderValue("ApiKey", settings.ApiKey);
        if (!request.Headers.Contains("Accept"))
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        return await base.SendAsync(request, cancellationToken);
    }
}
