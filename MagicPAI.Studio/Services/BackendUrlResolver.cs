using System.Net;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Components.WebAssembly.Hosting;

namespace MagicPAI.Studio.Services;

internal static class BackendUrlResolver
{
    public static Uri ResolveBackendUri(IConfiguration config, string currentBaseUri)
    {
        var currentUri = new Uri(currentBaseUri);
        var configured = config["Backend:Url"];

        if (string.IsNullOrWhiteSpace(configured))
            return currentUri;

        if (!Uri.TryCreate(configured, UriKind.Absolute, out var configuredUri))
            return currentUri;

        // Hosted Studio: when both URLs are loopback AND on the same port,
        // use the current origin. This keeps self-hosted deployments working.
        // But when ports differ (standalone Studio vs separate Server), honour the config.
        if (IsLoopbackHost(configuredUri.Host) &&
            IsLoopbackHost(currentUri.Host) &&
            configuredUri.Port == currentUri.Port)
        {
            return currentUri;
        }

        return configuredUri;
    }

    public static Uri ResolveBackendUri(IConfiguration config, NavigationManager navigation) =>
        ResolveBackendUri(config, navigation.BaseUri);

    public static Uri ResolveBackendUri(IConfiguration config, IWebAssemblyHostEnvironment environment) =>
        ResolveBackendUri(config, environment.BaseAddress);

    public static string ResolveHubUrl(IConfiguration config, NavigationManager navigation) =>
        new Uri(ResolveBackendUri(config, navigation), "hub").ToString();

    private static Uri EnsureTrailingSlash(Uri uri)
    {
        var value = uri.ToString();
        return value.EndsWith("/") ? uri : new Uri($"{value}/");
    }

    private static bool IsLoopbackHost(string host)
    {
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
            return true;

        return IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address);
    }
}
