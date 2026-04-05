using MagicPAI.Core.Models;

namespace MagicPAI.Core.Services.Gates;

public class LintGate : IVerificationGate
{
    public string Name => "lint";
    public bool IsBlocking => false;

    private static readonly (string pattern, string command)[] LintChecks =
    [
        ("*.csproj", "dotnet format --verify-no-changes"),
        (".eslintrc*", "npx eslint ."),
        ("pyproject.toml", "ruff check ."),
        ("Cargo.toml", "cargo clippy"),
        ("go.mod", "golangci-lint run"),
    ];

    public async Task<bool> CanVerifyAsync(IContainerManager container,
        string containerId, string workDir, CancellationToken ct)
    {
        var result = await container.ExecAsync(containerId,
            "ls *.csproj .eslintrc* .eslintrc.json package.json pyproject.toml 2>/dev/null",
            workDir, ct);
        return result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.Output);
    }

    public async Task<GateResult> VerifyAsync(IContainerManager container,
        string containerId, string workDir, CancellationToken ct)
    {
        var lintCmd = await GateHelper.DetectToolAsync(
            container, containerId, workDir, LintChecks, "No linter detected", ct);

        return await GateHelper.RunCommandGateAsync(
            Name, container, containerId, lintCmd, workDir,
            output => GateHelper.ParseOutputLines(output, "warning", "error"),
            ct);
    }
}
