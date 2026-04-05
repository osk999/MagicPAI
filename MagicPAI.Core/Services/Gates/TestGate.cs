using System.Diagnostics;
using MagicPAI.Core.Models;

namespace MagicPAI.Core.Services.Gates;

public class TestGate : IVerificationGate
{
    public string Name => "test";
    public bool IsBlocking => true;

    public async Task<bool> CanVerifyAsync(IContainerManager container,
        string containerId, string workDir, CancellationToken ct)
    {
        // Check for test projects or test configs
        var result = await container.ExecAsync(containerId,
            "find . -maxdepth 3 \\( -name '*.Tests.csproj' -o -name 'jest.config*' " +
            "-o -name 'pytest.ini' -o -name 'pyproject.toml' " +
            "-o -name 'Cargo.toml' \\) 2>/dev/null | head -1",
            workDir, ct);
        return result.ExitCode == 0 && result.Output.Trim().Length > 0;
    }

    public async Task<GateResult> VerifyAsync(IContainerManager container,
        string containerId, string workDir, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var testCmd = await DetectTestCommand(container, containerId, workDir, ct);
        var result = await container.ExecAsync(containerId, testCmd, workDir, ct);
        sw.Stop();

        var passed = result.ExitCode == 0;
        var issues = passed
            ? Array.Empty<string>()
            : ParseTestFailures(result.Output + "\n" + result.Error);

        return new GateResult(Name, passed, result.Output, issues, sw.Elapsed);
    }

    private static async Task<string> DetectTestCommand(IContainerManager container,
        string containerId, string workDir, CancellationToken ct)
    {
        var checks = new (string pattern, string command)[]
        {
            ("*.Tests.csproj", "dotnet test"),
            ("jest.config*", "npm test"),
            ("pytest.ini", "pytest"),
            ("pyproject.toml", "pytest"),
            ("Cargo.toml", "cargo test"),
            ("go.mod", "go test ./..."),
        };

        foreach (var (pattern, command) in checks)
        {
            var check = await container.ExecAsync(containerId,
                $"find . -maxdepth 3 -name '{pattern}' 2>/dev/null | head -1",
                workDir, ct);
            if (check.ExitCode == 0 && check.Output.Trim().Length > 0)
                return command;
        }

        return "echo 'No test framework detected'";
    }

    private static string[] ParseTestFailures(string output)
    {
        return output.Split('\n')
            .Where(line => line.Contains("FAIL", StringComparison.OrdinalIgnoreCase)
                           || line.Contains("failed", StringComparison.OrdinalIgnoreCase)
                           || line.Contains("Error", StringComparison.OrdinalIgnoreCase))
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToArray();
    }
}
