using Microsoft.AspNetCore.Mvc;

namespace MagicPAI.Server.Controllers;

/// <summary>
/// REST API for browsing the server filesystem (directories only).
/// Used by the dashboard to let users pick a workspace path.
/// </summary>
[ApiController]
[Route("api/browse")]
public class BrowseController : ControllerBase
{
    /// <summary>
    /// List drives (roots) and subdirectories of a given path.
    /// </summary>
    [HttpGet]
    public ActionResult<BrowseResult> Browse([FromQuery] string? path = null)
    {
        // If no path, return available drives / roots
        if (string.IsNullOrWhiteSpace(path))
        {
            var roots = DriveInfo.GetDrives()
                .Where(d => d.IsReady)
                .Select(d => new DirectoryEntry(d.Name, d.Name))
                .ToList();

            return Ok(new BrowseResult("", roots));
        }

        var normalizedPath = Path.GetFullPath(path);

        if (!Directory.Exists(normalizedPath))
            return NotFound(new { Message = $"Directory not found: {path}" });

        try
        {
            var dirs = Directory.GetDirectories(normalizedPath)
                .Where(d =>
                {
                    var name = Path.GetFileName(d);
                    // Hide hidden/system directories for cleaner UX
                    if (name.StartsWith('.')) return false;
                    try
                    {
                        var attr = new DirectoryInfo(d).Attributes;
                        return (attr & (FileAttributes.Hidden | FileAttributes.System)) == 0;
                    }
                    catch { return true; }
                })
                .Select(d => new DirectoryEntry(Path.GetFileName(d), d))
                .OrderBy(d => d.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var parent = Directory.GetParent(normalizedPath)?.FullName;

            return Ok(new BrowseResult(normalizedPath, dirs, parent));
        }
        catch (UnauthorizedAccessException)
        {
            return Ok(new BrowseResult(normalizedPath, []));
        }
    }

    /// <summary>
    /// List available workflow names for the workflow picker.
    /// </summary>
    [HttpGet("workflows")]
    public ActionResult<List<WorkflowOption>> ListWorkflows()
    {
        var workflows = new List<WorkflowOption>
        {
            new("full-orchestrate", "Full Orchestrator", true),
            new("simple-agent", "Simple Agent"),
            new("standard-orchestrate", "Standard Orchestrator"),
            new("verify-and-repair", "Verify & Repair"),
            new("prompt-enhancer", "Prompt Enhancer"),
            new("prompt-grounding", "Prompt Grounding"),
            new("context-gatherer", "Context Gatherer"),
            new("loop-verifier", "Loop Verifier"),
            new("website-audit-loop", "Website Audit"),
            new("research-pipeline", "Research Pipeline"),
            new("claw-eval-agent", "CLAW Eval Agent"),
        };

        return Ok(workflows);
    }
}

public record DirectoryEntry(string Name, string FullPath);
public record BrowseResult(string CurrentPath, List<DirectoryEntry> Directories, string? ParentPath = null);
public record WorkflowOption(string Id, string DisplayName, bool IsDefault = false);
