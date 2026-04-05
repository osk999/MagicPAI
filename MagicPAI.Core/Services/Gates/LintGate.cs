using System.Diagnostics;
using MagicPAI.Core.Models;

namespace MagicPAI.Core.Services.Gates;

public class LintGate : IVerificationGate
{
    public string Name => "lint";
    public bool IsBlocking => false;

    public async Task<bool> CanVerifyAsync(IContainerManager container,
        string containerId, string workDir, CancellationToken ct)
    {
        var result = await container.ExecAsync(containerId,
            "ls *.csproj .eslintrc* .eslintrc.json package.json pyproject.toml 2>/dev/null",
            workDir, ct);
        return result.ExitCode == 0 && result.Output.Trim().Length > 0;
    }

    public async Task<GateResult> VerifyAsync(IContainerManager container,
        string containerId, string workDir, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var lintCmd = await DetectLintCommand(container, containerId, workDir, ct);
        var result = await container.ExecAsync(containerId, lintCmd, workDir, ct);
        sw.Stop();

        var passed = result.ExitCode == 0;
        var issues = passed
            ? Array.Empty<string>()
            : ParseLintIssues(result.Output + "\n" + result.Error);

        return new GateResult(Name, passed, result.Output, issues, sw.Elapsed);
    }

    private static async Task<string> DetectLintCommand(IContainerManager container,
        string containerId, string workDir, CancellationToken ct)
    {
        var checks = new (string file, string command)[]
        {
            ("*.csproj", "dotnet format --verify-no-changes"),
            (".eslintrc*", "npx eslint ."),
            ("pyproject.toml", "ruff check ."),
            ("Cargo.toml", "cargo clippy"),
            ("go.mod", "golangci-lint run"),
        };

        foreach (var (file, command) in checks)
        {
            var check = await container.ExecAsync(containerId,
                $"ls {file} 2>/dev/null", workDir, ct);
            if (check.ExitCode == 0 && check.Output.Trim().Length > 0)
                return command;
        }

        return "echo 'No linter detected'";
    }

    private static string[] ParseLintIssues(string output)
    {
        return output.Split('\n')
            .Where(line => line.Contains("warning", StringComparison.OrdinalIgnoreCase)
                           || line.Contains("error", StringComparison.OrdinalIgnoreCase))
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToArray();
    }
}
