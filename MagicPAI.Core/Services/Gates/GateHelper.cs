using System.Diagnostics;
using MagicPAI.Core.Models;

namespace MagicPAI.Core.Services.Gates;

/// <summary>
/// Shared utilities for verification gates to reduce code duplication.
/// </summary>
internal static class GateHelper
{
    /// <summary>
    /// Detect which tool/command to use by checking for project files in the container.
    /// Returns the first matching command, or a fallback echo message.
    /// </summary>
    internal static async Task<string> DetectToolAsync(
        IContainerManager container, string containerId, string workDir,
        (string pattern, string command)[] checks, string fallbackMessage,
        CancellationToken ct)
    {
        foreach (var (pattern, command) in checks)
        {
            var useFind = pattern.Contains('*') || pattern.Contains('?');
            var checkCmd = useFind
                ? $"find . -maxdepth 3 -name '{pattern}' 2>/dev/null | head -1"
                : $"ls {pattern} 2>/dev/null";

            var check = await container.ExecAsync(containerId, checkCmd, workDir, ct);
            if (check.ExitCode == 0 && !string.IsNullOrWhiteSpace(check.Output))
                return command;
        }

        return $"echo '{fallbackMessage}'";
    }

    /// <summary>
    /// Run a gate command and return a structured GateResult.
    /// </summary>
    internal static async Task<GateResult> RunCommandGateAsync(
        string gateName, IContainerManager container,
        string containerId, string command, string workDir,
        Func<string, string[]> parseErrors, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        var result = await container.ExecAsync(containerId, command, workDir, ct);
        sw.Stop();

        var passed = result.ExitCode == 0;
        var combinedOutput = result.Output + "\n" + result.Error;
        var issues = passed ? [] : parseErrors(combinedOutput);

        return new GateResult(gateName, passed, result.Output, issues, sw.Elapsed);
    }

    /// <summary>
    /// Parse output lines matching any of the given keywords (case-insensitive).
    /// </summary>
    internal static string[] ParseOutputLines(string output, params string[] keywords)
    {
        return output.Split('\n')
            .Select(line => line.Trim())
            .Where(line => line.Length > 0
                && keywords.Any(kw => line.Contains(kw, StringComparison.OrdinalIgnoreCase)))
            .ToArray();
    }
}
