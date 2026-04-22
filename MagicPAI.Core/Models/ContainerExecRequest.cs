namespace MagicPAI.Core.Models;

public sealed record ContainerExecRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string?>? Environment = null,
    // When non-null the runner writes this payload to the child process's
    // standard input and closes it. Used to route oversized prompts (>28 KB)
    // past the Windows CreateProcess / Linux MAX_ARG_STRLEN argv caps for
    // CLIs that accept stdin input (e.g. `claude -p` with no prompt arg).
    string? StdinInput = null);

public sealed record CliAgentExecutionPlan(
    ContainerExecRequest MainRequest,
    IReadOnlyList<ContainerExecRequest>? SetupRequests = null);
