// MagicPAI.Activities/Contracts/DockerContracts.cs
// Temporal activity input/output records for the Docker activity group.
// See temporal.md §7.3 for spec.
namespace MagicPAI.Activities.Contracts;

public record SpawnContainerInput(
    string SessionId,               // workflow id (used for session registry)
    string Image = "magicpai-env:latest",
    string WorkspacePath = "",
    int MemoryLimitMb = 4096,
    bool EnableGui = false,
    Dictionary<string, string>? EnvVars = null);

public record SpawnContainerOutput(
    string ContainerId,
    string? GuiUrl);

public record ExecInput(
    string ContainerId,
    string Command,
    string WorkingDirectory = "/workspace",
    int TimeoutSeconds = 600);

public record ExecOutput(
    int ExitCode,
    string Output,                  // capped at 64 KB
    string? Error);

public record StreamInput(
    string ContainerId,
    string Command,
    string WorkingDirectory = "/workspace",
    int TimeoutMinutes = 120,
    string? SessionId = null);      // for SignalR streaming

public record StreamOutput(
    int ExitCode,
    int LineCount,
    string? SummaryLine);           // last non-empty output line, for quick inspection

public record DestroyInput(
    string ContainerId,
    bool ForceKill = false);
