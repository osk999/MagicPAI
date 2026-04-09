namespace MagicPAI.Server.Middleware;

/// <summary>
/// Middleware that rewrites requests for _content/ and _framework/ paths
/// when they're prefixed (e.g., from Elsa API routes). Required for
/// Blazor WASM assets to be found when hosted alongside other APIs.
/// Based on Elsa.Studio.Host.HostedWasm reference implementation.
/// </summary>
public class WasmAssetsRewritingMiddleware(RequestDelegate next, ILogger<WasmAssetsRewritingMiddleware> logger)
{
    private static readonly string[] TargetSegments = ["_content", "_framework"];

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.Request.Path.HasValue && NeedsRewriting(context.Request.Path.Value!))
        {
            var path = context.Request.Path.Value!;
            var segmentIndex = GetTargetSegmentIndex(path.AsSpan());

            if (segmentIndex > 0)
            {
                var newPath = path.AsSpan(segmentIndex).ToString();
                context.Request.Path = newPath.StartsWith('/') ? newPath : $"/{newPath}";

                if (logger.IsEnabled(LogLevel.Debug))
                    logger.LogDebug("WASM asset rewrite: {Path} -> {NewPath}", path, context.Request.Path.Value);
            }
        }

        await next(context);
    }

    private static bool NeedsRewriting(string path)
    {
        foreach (var segment in TargetSegments)
        {
            if (path.Contains(segment, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static int GetTargetSegmentIndex(ReadOnlySpan<char> path)
    {
        foreach (var segment in TargetSegments)
        {
            var segmentAsSpan = $"{segment}/".AsSpan();
            var index = path.IndexOf(segmentAsSpan, StringComparison.OrdinalIgnoreCase);
            if (index >= 0) return index;
        }
        return -1;
    }
}
