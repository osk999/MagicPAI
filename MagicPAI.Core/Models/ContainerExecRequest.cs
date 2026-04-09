namespace MagicPAI.Core.Models;

public sealed record ContainerExecRequest(
    string FileName,
    IReadOnlyList<string> Arguments,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string?>? Environment = null);

public sealed record CliAgentExecutionPlan(
    ContainerExecRequest MainRequest,
    IReadOnlyList<ContainerExecRequest>? SetupRequests = null);
