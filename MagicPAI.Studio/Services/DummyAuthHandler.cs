using System.Net.Http;

namespace MagicPAI.Studio.Services;

/// <summary>
/// A no-op authentication handler for development.
/// In production, replace with a real auth handler.
/// </summary>
public class DummyAuthHandler : DelegatingHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return base.SendAsync(request, cancellationToken);
    }
}
