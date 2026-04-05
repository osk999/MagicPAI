using System.Diagnostics;

namespace MagicPAI.Core.Services;

public class LocalExecutionEnvironment : IExecutionEnvironment
{
    public string Kind => "local";

    public async Task<string> RunCommandAsync(string command, string workDir, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = OperatingSystem.IsWindows() ? "cmd.exe" : "bash",
            WorkingDirectory = workDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        // Use ArgumentList to avoid shell injection
        if (OperatingSystem.IsWindows())
        {
            psi.ArgumentList.Add("/c");
            psi.ArgumentList.Add(command);
        }
        else
        {
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add(command);
        }

        using var process = await StartProcessAsync(psi, ct);
        var output = await process.StandardOutput.ReadToEndAsync(ct);
        var error = await process.StandardError.ReadToEndAsync(ct);
        await process.WaitForExitAsync(ct);

        return process.ExitCode == 0 ? output : $"{output}\n{error}";
    }

    public Task<Process> StartProcessAsync(ProcessStartInfo psi, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start process.");
        return Task.FromResult(process);
    }
}
