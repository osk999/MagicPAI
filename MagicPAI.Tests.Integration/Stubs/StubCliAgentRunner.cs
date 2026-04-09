using MagicPAI.Core.Models;
using MagicPAI.Core.Services;

namespace MagicPAI.Tests.Integration.Stubs;

/// <summary>
/// Stub CLI agent runner that returns canned responses for integration tests.
/// </summary>
public class StubCliAgentRunner : ICliAgentRunner
{
    public string AgentName => "stub-agent";
    public string DefaultModel => "stub-model";
    public string[] AvailableModels => ["stub-model"];
    public bool SupportsNativeSchema => false;

    public CliAgentResponse CannedResponse { get; set; } = new(
        Success: true,
        Output: "Stub agent completed successfully.",
        CostUsd: 0.01m,
        FilesModified: [],
        InputTokens: 100,
        OutputTokens: 50,
        SessionId: null);

    public string BuildCommand(AgentRequest request) => "echo stub-agent-command";

    public CliAgentExecutionPlan BuildExecutionPlan(AgentRequest request) =>
        new(new ContainerExecRequest("echo", ["stub-agent-command"], request.WorkDir));

    public CliAgentResponse ParseResponse(string rawOutput) => CannedResponse;
}
