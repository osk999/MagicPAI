namespace MagicPAI.Core.Models;

public class ContainerConfig
{
    public string Image { get; set; } = "magicpai-env:latest";
    public string WorkspacePath { get; set; } = "";
    public string ContainerWorkDir { get; set; } = "/workspace";
    public int MemoryLimitMb { get; set; } = 4096;
    public int CpuCount { get; set; } = 2;
    public bool MountDockerSocket { get; set; }
    public bool EnableGui { get; set; }
    public int? GuiPort { get; set; }
    public Dictionary<string, string> Env { get; set; } = new();
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(30);

    /// <summary>
    /// Container labels (key=value) attached at create time. Used by the GC's
    /// fallback sweep to identify MagicPAI-owned containers across server
    /// restarts when the in-memory <c>SessionTracker</c> is empty.
    /// Conventional keys: <c>magicpai.session</c>, <c>magicpai.workflow</c>,
    /// <c>magicpai.workflow_id</c>, <c>magicpai.created_at</c>.
    /// </summary>
    public Dictionary<string, string> Labels { get; set; } = new();
}
