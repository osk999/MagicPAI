using System.Net.Http.Headers;

namespace MagicPAI.Studio.Services;

/// <summary>
/// HTTP message handler that adds the Elsa admin API key header.
/// In development with DisableSecurity, this is a no-op but ensures
/// the handler chain is properly formed for WASM HTTP clients.
/// </summary>
public class ElsaStudioApiKeyHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Add API key and JSON Accept header
        // Elsa's AdminApiKeyProvider uses Guid.Empty as the default admin key
        request.Headers.Authorization = new AuthenticationHeaderValue("ApiKey", "00000000-0000-0000-0000-000000000000");
        if (!request.Headers.Contains("Accept"))
            request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        return await base.SendAsync(request, cancellationToken);
    }
}
