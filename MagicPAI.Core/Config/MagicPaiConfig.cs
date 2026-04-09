using MagicPAI.Core.Services;

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
    public string DefaultModel { get; set; } = "auto";
    public int MaxTurnsPerTask { get; set; } = 20;
    public int AgentTimeoutMinutes { get; set; } = 30;
    public bool RequireContainerizedAgentExecution { get; set; } = true;
    public Dictionary<string, Dictionary<string, string>> AssistantModelPowerMap { get; set; } = new();

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

    // --- Execution Backend ---
    public string ExecutionBackend { get; set; } = "docker"; // "docker" or "kubernetes"
    public bool UseWorkerContainers =>
        UseDocker || string.Equals(ExecutionBackend, "kubernetes", StringComparison.OrdinalIgnoreCase);

    // --- Kubernetes ---
    public string KubernetesNamespace { get; set; } = "magicpai";
    public string KubernetesServiceAccount { get; set; } = "magicpai-server";

    // --- Container Pool ---
    public int ContainerPoolSize { get; set; } = 3;
    public bool EnableContainerPool { get; set; }

    // --- Model Routing ---
    public bool EnableAdaptiveRouting { get; set; }
    public Dictionary<string, string> ModelOverrides { get; set; } = new();

    /// <summary>Validate config values and return list of problems (empty = valid).</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (MaxConcurrentContainers < 1) errors.Add("MaxConcurrentContainers must be >= 1");
        if (ContainerTimeoutMinutes < 1) errors.Add("ContainerTimeoutMinutes must be >= 1");
        if (DefaultMemoryLimitMb < 256) errors.Add("DefaultMemoryLimitMb must be >= 256");
        if (DefaultCpuCount < 1) errors.Add("DefaultCpuCount must be >= 1");
        if (MaxTurnsPerTask < 1) errors.Add("MaxTurnsPerTask must be >= 1");
        if (MaxRepairAttempts < 0) errors.Add("MaxRepairAttempts must be >= 0");
        if (CoverageThreshold is < 0 or > 100) errors.Add("CoverageThreshold must be 0-100");
        if (MaxBudgetUsd < 0) errors.Add("MaxBudgetUsd must be >= 0");
        if (ComplexityThreshold is < 1 or > 10) errors.Add("ComplexityThreshold must be 1-10");
        if (GuiPortRangeStart < 1024) errors.Add("GuiPortRangeStart must be >= 1024");
        if (GuiPortRangeEnd <= GuiPortRangeStart) errors.Add("GuiPortRangeEnd must be > GuiPortRangeStart");
        if (MaxParallelWorkers < 1) errors.Add("MaxParallelWorkers must be >= 1");
        if (SignalRBufferSize < 10) errors.Add("SignalRBufferSize must be >= 10");
        if (ExecutionBackend is not ("docker" or "kubernetes")) errors.Add("ExecutionBackend must be 'docker' or 'kubernetes'");
        if (ContainerPoolSize < 1) errors.Add("ContainerPoolSize must be >= 1");
        if (string.IsNullOrWhiteSpace(KubernetesNamespace)) errors.Add("KubernetesNamespace must not be empty");
        if (RequireContainerizedAgentExecution && !UseWorkerContainers)
            errors.Add("RequireContainerizedAgentExecution requires Docker or Kubernetes worker-container execution.");
        errors.AddRange(AiAssistantResolver.ValidateConfiguredPowerMaps(AssistantModelPowerMap));

        return errors;
    }
}
