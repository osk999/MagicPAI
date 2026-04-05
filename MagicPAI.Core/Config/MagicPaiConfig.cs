namespace MagicPAI.Core.Config;

public class MagicPaiConfig
{
    // --- Docker ---
    public bool UseDocker { get; set; } = true;
    public string WorkerImage { get; set; } = "magicpai-env:latest";
    public int DefaultMemoryLimitMb { get; set; } = 4096;
    public int DefaultCpuCount { get; set; } = 2;
    public int MaxConcurrentContainers { get; set; } = 5;
    public int ContainerTimeoutMinutes { get; set; } = 30;
    public bool MountDockerSocket { get; set; }

    // --- AI Agents ---
    public string DefaultAgent { get; set; } = "claude";
    public string DefaultModel { get; set; } = "sonnet";
    public int MaxTurnsPerTask { get; set; } = 20;
    public int AgentTimeoutMinutes { get; set; } = 30;

    // --- Verification ---
    public bool EnableVerification { get; set; } = true;
    public bool EnableRepair { get; set; } = true;
    public int MaxRepairAttempts { get; set; } = 5;
    public string[] DefaultGates { get; set; } = ["compile", "test", "hallucination"];
    public double CoverageThreshold { get; set; } = 70.0;

    // --- Budget ---
    public decimal MaxBudgetUsd { get; set; } = 0; // 0 = unlimited
    public bool TrackCosts { get; set; } = true;
    public decimal WarnAtBudgetPercent { get; set; } = 80;

    // --- Git ---
    public bool EnableWorktreeIsolation { get; set; } = true;
    public string DefaultBranch { get; set; } = "main";

    // --- Triage ---
    public int ComplexityThreshold { get; set; } = 7; // >= threshold = complex
    public string TriageModel { get; set; } = "haiku";
    public int MaxSubTasks { get; set; } = 10;

    // --- GUI ---
    public bool EnableContainerGui { get; set; }
    public int GuiPortRangeStart { get; set; } = 6080;
    public int GuiPortRangeEnd { get; set; } = 6180;

    // --- Logging ---
    public bool EnableDetailedLogging { get; set; }
    public bool LogAgentOutput { get; set; } = true;
    public bool LogTokenUsage { get; set; } = true;

    // --- Workspace ---
    public string WorkspacePath { get; set; } = "";
    public string ContainerWorkDir { get; set; } = "/workspace";

    // --- Parallel Execution ---
    public int MaxParallelWorkers { get; set; } = 3;
    public bool EnableSharedBlackboard { get; set; } = true;

    // --- SignalR ---
    public bool EnableSignalR { get; set; } = true;
    public int SignalRBufferSize { get; set; } = 1000;

    // --- Security ---
    public bool EnableSecurityScan { get; set; } = true;
    public bool BlockOnSecurityIssues { get; set; }

    // --- Model Routing ---
    public bool EnableAdaptiveRouting { get; set; }
    public Dictionary<string, string> ModelOverrides { get; set; } = new();
}
