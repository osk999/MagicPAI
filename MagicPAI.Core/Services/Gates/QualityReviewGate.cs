using System.Diagnostics;
using System.Text.RegularExpressions;
using MagicPAI.Core.Models;

namespace MagicPAI.Core.Services.Gates;

public class QualityReviewGate : IVerificationGate
{
    public string Name => "quality";
    public bool IsBlocking => false;

    private static readonly (string Pattern, string Description)[] QualityPatterns =
    [
        (@"TODO|FIXME|HACK|XXX", "Contains TODO/FIXME markers"),
        (@"Console\.Write", "Uses Console.Write instead of proper logging"),
        (@"Thread\.Sleep", "Uses Thread.Sleep (blocking)"),
        (@"\.Result\b|\.Wait\(\)", "Synchronous wait on async (.Result/.Wait())"),
        (@"catch\s*\(\s*\)\s*\{?\s*\}", "Empty catch block"),
        (@"DateTime\.Now\b", "Uses DateTime.Now instead of DateTime.UtcNow"),
        (@"public\s+class\s+\w+.*\{[^}]{5000,}\}", "Class may be too large"),
    ];

    public Task<bool> CanVerifyAsync(IContainerManager container,
        string containerId, string workDir, CancellationToken ct)
    {
        return Task.FromResult(true); // Can always review
    }

    public async Task<GateResult> VerifyAsync(IContainerManager container,
        string containerId, string workDir, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        var result = await container.ExecAsync(containerId,
            "find . -type f \\( -name '*.cs' -o -name '*.js' -o -name '*.ts' " +
            "-o -name '*.py' -o -name '*.rs' -o -name '*.go' \\) " +
            "-not -path '*/node_modules/*' -not -path '*/bin/*' -not -path '*/obj/*' " +
            "| xargs grep -n -H '.' 2>/dev/null || true",
            workDir, ct);
        sw.Stop();

        var issues = new List<string>();
        foreach (var line in result.Output.Split('\n'))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            foreach (var (pattern, description) in QualityPatterns)
            {
                if (Regex.IsMatch(line, pattern))
                {
                    issues.Add($"{description}: {line.Trim()[..Math.Min(line.Trim().Length, 200)]}");
                    break;
                }
            }
        }

        var passed = issues.Count == 0;
        var output = passed
            ? "No quality issues detected."
            : $"Found {issues.Count} quality issue(s).";

        return new GateResult(Name, passed, output, issues.ToArray(), sw.Elapsed);
    }
}
