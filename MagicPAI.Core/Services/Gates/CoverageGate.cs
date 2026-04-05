using System.Diagnostics;
using System.Text.RegularExpressions;
using MagicPAI.Core.Models;

namespace MagicPAI.Core.Services.Gates;

public partial class CoverageGate : IVerificationGate
{
    private readonly double _threshold;

    public CoverageGate(double threshold = 70.0)
    {
        _threshold = threshold;
    }

    public string Name => "coverage";
    public bool IsBlocking => true;

    public async Task<bool> CanVerifyAsync(IContainerManager container,
        string containerId, string workDir, CancellationToken ct)
    {
        // Coverage only makes sense if tests exist
        var result = await container.ExecAsync(containerId,
            "find . -maxdepth 3 \\( -name '*.Tests.csproj' -o -name 'jest.config*' " +
            "-o -name 'pytest.ini' \\) 2>/dev/null | head -1",
            workDir, ct);
        return result.ExitCode == 0 && result.Output.Trim().Length > 0;
    }

    public async Task<GateResult> VerifyAsync(IContainerManager container,
        string containerId, string workDir, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        // Try dotnet test with coverage first
        var result = await container.ExecAsync(containerId,
            "dotnet test --collect:\"XPlat Code Coverage\" --results-directory ./coverage 2>&1 || " +
            "npx jest --coverage 2>&1 || " +
            "pytest --cov 2>&1",
            workDir, ct);
        sw.Stop();

        var coverage = ParseCoveragePercentage(result.Output);
        var passed = coverage >= _threshold;
        var issues = passed
            ? Array.Empty<string>()
            : [$"Line coverage {coverage:F1}% is below threshold {_threshold:F1}%"];

        var output = $"Coverage: {coverage:F1}% (threshold: {_threshold:F1}%)\n{result.Output}";
        return new GateResult(Name, passed, output, issues, sw.Elapsed);
    }

    private static double ParseCoveragePercentage(string output)
    {
        // Match patterns like "Line coverage: 85.3%" or "Stmts   : 85.3%"
        var match = CoverageRegex().Match(output);
        if (match.Success && double.TryParse(match.Groups[1].Value, out var pct))
            return pct;
        return 0;
    }

    [GeneratedRegex(@"(?:coverage|stmts|lines)\s*[:\|]?\s*([\d.]+)%", RegexOptions.IgnoreCase)]
    private static partial Regex CoverageRegex();
}
