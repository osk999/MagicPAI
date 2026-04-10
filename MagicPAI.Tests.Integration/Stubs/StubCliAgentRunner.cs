using System.Text.Json;
using MagicPAI.Core.Models;
using MagicPAI.Core.Services;

namespace MagicPAI.Tests.Integration.Stubs;

/// <summary>
/// Stub CLI agent runner that serializes request metadata into the exec plan and
/// can parse simple JSON envelopes from the stub container manager.
/// </summary>
public class StubCliAgentRunner : ICliAgentRunner
{
    private readonly string _agentName;

    public StubCliAgentRunner(string agentName = "claude")
    {
        _agentName = agentName;
    }

    public string AgentName => _agentName;
    public string DefaultModel => "auto";
    public string[] AvailableModels => [
        "auto",
        "haiku", "sonnet", "opus",
        "gpt-5.4", "gpt-5.4-mini", "gpt-5.3-codex",
        "gemini-3.1-pro-preview", "gemini-3-flash", "gemini-3.1-flash-lite-preview"
    ];
    public bool SupportsNativeSchema => true;

    public CliAgentResponse CannedResponse { get; set; } = new(
        Success: true,
        Output: "Stub agent completed successfully.",
        CostUsd: 0.01m,
        FilesModified: [],
        InputTokens: 100,
        OutputTokens: 50,
        SessionId: null);

    public string BuildCommand(AgentRequest request) =>
        JsonSerializer.Serialize(new
        {
            type = "stub-agent-command",
            prompt = request.Prompt,
            model = request.Model,
            workDir = request.WorkDir,
            outputSchema = request.OutputSchema,
            sessionId = request.SessionId
        });

    public CliAgentExecutionPlan BuildExecutionPlan(AgentRequest request) =>
        new(new ContainerExecRequest(
            "stub-agent",
            [
                "--prompt", request.Prompt ?? "",
                "--model", request.Model ?? "",
                "--schema", request.OutputSchema ?? "",
                "--session", request.SessionId ?? ""
            ],
            request.WorkDir));

    public CliAgentResponse ParseResponse(string rawOutput)
    {
        try
        {
            var envelope = JsonSerializer.Deserialize<StubCliResponseEnvelope>(rawOutput);
            if (envelope is not null)
            {
                return new CliAgentResponse(
                    Success: envelope.Success,
                    Output: envelope.Output ?? "",
                    CostUsd: envelope.CostUsd,
                    FilesModified: envelope.FilesModified ?? [],
                    InputTokens: envelope.InputTokens,
                    OutputTokens: envelope.OutputTokens,
                    SessionId: envelope.SessionId,
                    StructuredOutputJson: envelope.StructuredOutputJson);
            }
        }
        catch (JsonException)
        {
        }

        return CannedResponse with { Output = rawOutput };
    }

    private sealed record StubCliResponseEnvelope(
        bool Success,
        string? Output,
        decimal CostUsd = 0.01m,
        string[]? FilesModified = null,
        int InputTokens = 100,
        int OutputTokens = 50,
        string? SessionId = null,
        string? StructuredOutputJson = null);
}
