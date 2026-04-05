using MagicPAI.Core.Models;

namespace MagicPAI.Core.Services.Gates;

public class TestGate : IVerificationGate
{
    public string Name => "test";
    public bool IsBlocking => true;

    private static readonly (string pattern, string command)[] TestChecks =
    [
        ("*.Tests.csproj", "dotnet test"),
        ("jest.config*", "npm test"),
        ("pytest.ini", "pytest"),
        ("pyproject.toml", "pytest"),
        ("Cargo.toml", "cargo test"),
        ("go.mod", "go test ./..."),
    ];

    public async Task<bool> CanVerifyAsync(IContainerManager container,
        string containerId, string workDir, CancellationToken ct)
    {
        var result = await container.ExecAsync(containerId,
            "find . -maxdepth 3 \\( -name '*.Tests.csproj' -o -name 'jest.config*' " +
            "-o -name 'pytest.ini' -o -name 'pyproject.toml' " +
            "-o -name 'Cargo.toml' \\) 2>/dev/null | head -1",
            workDir, ct);
        return result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.Output);
    }

    public async Task<GateResult> VerifyAsync(IContainerManager container,
        string containerId, string workDir, CancellationToken ct)
    {
        var testCmd = await GateHelper.DetectToolAsync(
            container, containerId, workDir, TestChecks, "No test framework detected", ct);

        return await GateHelper.RunCommandGateAsync(
            Name, container, containerId, testCmd, workDir,
            output => GateHelper.ParseOutputLines(output, "FAIL", "failed", "Error"),
            ct);
    }
}
