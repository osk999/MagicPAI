using Temporalio.Client;

namespace MagicPAI.Tests.Workflows;

/// <summary>
/// Shared helper for integration tests — captures a workflow handle's history
/// to <c>Workflows/Histories/&lt;workflow-name&gt;/&lt;fileName&gt;</c> both in
/// <see cref="AppContext.BaseDirectory"/> (so the Replay tests can load the
/// fixture from the copied bin output) and back in the source tree (so the
/// file is easy to commit). Idempotent — does not overwrite if the fixture
/// already exists. See temporal.md §15.5.
/// </summary>
internal static class ReplayFixtureHelper
{
    /// <summary>
    /// Captures a workflow's history to the named fixture file if it doesn't
    /// already exist. Non-generic — accepts any <see cref="WorkflowHandle"/>.
    /// </summary>
    /// <param name="handle">The handle whose history will be fetched.</param>
    /// <param name="workflowKebabName">Kebab-case workflow name — used as the
    /// subdirectory under <c>Workflows/Histories</c>.</param>
    /// <param name="fileName">File name like <c>happy-path-v1.json</c>.</param>
    public static async Task CaptureIfMissingAsync(
        WorkflowHandle handle,
        string workflowKebabName,
        string fileName)
    {
        var fixtureRel = Path.Combine("Workflows", "Histories", workflowKebabName, fileName);
        var absBinPath = Path.Combine(AppContext.BaseDirectory, fixtureRel);

        // Ensure bin-dir fixture exists (primary lookup path for replay tests).
        Directory.CreateDirectory(Path.GetDirectoryName(absBinPath)!);
        if (!File.Exists(absBinPath))
        {
            var history = await handle.FetchHistoryAsync();
            await File.WriteAllTextAsync(absBinPath, history.ToJson());
        }

        // Best-effort: mirror to the source tree so the file shows up in git.
        // bin\Debug\net10.0 → walk up three levels to the project dir.
        try
        {
            var binDir = new DirectoryInfo(AppContext.BaseDirectory);
            var projectDir = binDir.Parent?.Parent?.Parent;
            if (projectDir is null) return;

            var srcDir = Path.Combine(projectDir.FullName, "Workflows",
                "Histories", workflowKebabName);
            Directory.CreateDirectory(srcDir);
            var srcPath = Path.Combine(srcDir, fileName);
            if (!File.Exists(srcPath))
            {
                // If we already fetched above, re-use the bin file; otherwise fetch.
                if (File.Exists(absBinPath))
                {
                    File.Copy(absBinPath, srcPath);
                }
                else
                {
                    var history = await handle.FetchHistoryAsync();
                    await File.WriteAllTextAsync(srcPath, history.ToJson());
                }
            }
        }
        catch
        {
            // Best-effort mirroring. The test has already asserted success.
        }
    }
}
