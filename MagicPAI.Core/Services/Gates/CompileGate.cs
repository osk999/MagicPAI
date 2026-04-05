using System.Diagnostics;
using MagicPAI.Core.Models;

namespace MagicPAI.Core.Services.Gates;

public class CompileGate : IVerificationGate
{
    public string Name => "compile";
    public bool IsBlocking => true;

    public async Task<bool> CanVerifyAsync(IContainerManager container,
        string containerId, string workDir, CancellationToken ct)
    {
        // Check for any recognizable project files
        var result = await container.ExecAsync(containerId,
            "ls *.csproj *.fsproj package.json Cargo.toml go.mod Makefile 2>/dev/null",
            workDir, ct);
        return result.ExitCode == 0 && result.Output.Trim().Length > 0;
    }

    public async Task<GateResult> VerifyAsync(IContainerManager container,
        string containerId, string workDir, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var buildCmd = await DetectBuildCommand(container, containerId, workDir, ct);
        var result = await container.ExecAsync(containerId, buildCmd, workDir, ct);
        sw.Stop();

        var passed = result.ExitCode == 0;
        var issues = passed
            ? Array.Empty<string>()
            : ParseBuildErrors(result.Output + "\n" + result.Error);

        return new GateResult(Name, passed, result.Output, issues, sw.Elapsed);
    }

    private static async Task<string> DetectBuildCommand(IContainerManager container,
        string containerId, string workDir, CancellationToken ct)
    {
        // Check for project types in priority order
        var checks = new (string file, string command)[]
        {
            ("*.csproj", "dotnet build"),
            ("*.fsproj", "dotnet build"),
            ("package.json", "npm run build --if-present"),
            ("Cargo.toml", "cargo build"),
            ("go.mod", "go build ./..."),
            ("Makefile", "make"),
        };

        foreach (var (file, command) in checks)
        {
            var check = await container.ExecAsync(containerId,
                $"ls {file} 2>/dev/null", workDir, ct);
            if (check.ExitCode == 0 && check.Output.Trim().Length > 0)
                return command;
        }

        return "echo 'No build system detected'";
    }

    private static string[] ParseBuildErrors(string output)
    {
        return output.Split('\n')
            .Where(line => line.Contains("error", StringComparison.OrdinalIgnoreCase)
                           && !line.Contains("0 Error", StringComparison.OrdinalIgnoreCase))
            .Select(line => line.Trim())
            .Where(line => line.Length > 0)
            .ToArray();
    }
}
