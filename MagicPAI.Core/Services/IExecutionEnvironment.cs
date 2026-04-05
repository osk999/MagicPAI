using System.Diagnostics;

namespace MagicPAI.Core.Services;

public interface IExecutionEnvironment
{
    /// <summary>Run a shell command and return its output.</summary>
    Task<string> RunCommandAsync(string command, string workDir, CancellationToken ct);

    /// <summary>Start a process with full control over stdin/stdout.</summary>
    Task<Process> StartProcessAsync(ProcessStartInfo psi, CancellationToken ct);

    /// <summary>"docker" or "local".</summary>
    string Kind { get; }
}
