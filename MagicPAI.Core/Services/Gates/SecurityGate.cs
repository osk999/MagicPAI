using System.Diagnostics;
using System.Text.RegularExpressions;
using MagicPAI.Core.Models;

namespace MagicPAI.Core.Services.Gates;

public partial class SecurityGate : IVerificationGate
{
    public string Name => "security";
    public bool IsBlocking => false;

    private static readonly (string Pattern, string Description)[] SecurityPatterns =
    [
        (@"(?i)password\s*=\s*[""'][^""']+[""']", "Hardcoded password"),
        (@"(?i)api[_-]?key\s*=\s*[""'][^""']+[""']", "Hardcoded API key"),
        (@"(?i)secret\s*=\s*[""'][^""']+[""']", "Hardcoded secret"),
        (@"(?i)(?:sk|pk)[-_][a-zA-Z0-9]{20,}", "Potential secret key token"),
        (@"(?i)eval\s*\(", "Use of eval()"),
        (@"(?i)exec\s*\(\s*[""']", "Shell injection risk"),
        (@"(?i)innerHTML\s*=", "Potential XSS via innerHTML"),
        (@"(?i)(?:SELECT|INSERT|UPDATE|DELETE).*\+\s*(?:request|input|user|param)",
            "Potential SQL injection"),
    ];

    public Task<bool> CanVerifyAsync(IContainerManager container,
        string containerId, string workDir, CancellationToken ct)
    {
        return Task.FromResult(true); // Can always scan
    }

    public async Task<GateResult> VerifyAsync(IContainerManager container,
        string containerId, string workDir, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        // Get source files content via grep in the container
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
            foreach (var (pattern, description) in SecurityPatterns)
            {
                if (Regex.IsMatch(line, pattern))
                {
                    issues.Add($"{description}: {line.Trim()[..Math.Min(line.Trim().Length, 200)]}");
                    break; // One issue per line is enough
                }
            }
        }

        var passed = issues.Count == 0;
        var output = passed
            ? "No security issues detected."
            : $"Found {issues.Count} potential security issue(s).";

        return new GateResult(Name, passed, output, issues.ToArray(), sw.Elapsed);
    }
}
