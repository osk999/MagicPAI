using MagicPAI.Core.Models;

namespace MagicPAI.Core.Services.Gates;

public class CompileGate : IVerificationGate
{
    public string Name => "compile";
    public bool IsBlocking => true;

    private static readonly (string pattern, string command)[] BuildChecks =
    [
        ("*.csproj", "dotnet build"),
        ("*.fsproj", "dotnet build"),
        ("package.json", "npm run build --if-present"),
        ("Cargo.toml", "cargo build"),
        ("go.mod", "go build ./..."),
        ("Makefile", "make"),
    ];

    public async Task<bool> CanVerifyAsync(IContainerManager container,
        string containerId, string workDir, CancellationToken ct)
    {
        var result = await container.ExecAsync(containerId,
            "ls *.csproj *.fsproj package.json Cargo.toml go.mod Makefile 2>/dev/null",
            workDir, ct);
        return result.ExitCode == 0 && !string.IsNullOrWhiteSpace(result.Output);
    }

    public async Task<GateResult> VerifyAsync(IContainerManager container,
        string containerId, string workDir, CancellationToken ct)
    {
        var buildCmd = await GateHelper.DetectToolAsync(
            container, containerId, workDir, BuildChecks, "No build system detected", ct);

        return await GateHelper.RunCommandGateAsync(
            Name, container, containerId, buildCmd, workDir,
            output => GateHelper.ParseOutputLines(output, "error")
                .Where(l => !l.Contains("0 Error", StringComparison.OrdinalIgnoreCase))
                .ToArray(),
            ct);
    }
}
