using System.Text.Json;
using Elsa.Extensions;
using Elsa.Workflows;
using MagicPAI.Core.Config;
using MagicPAI.Core.Models;
using MagicPAI.Core.Services;

namespace MagicPAI.Activities.AI;

/// <summary>
/// The ONE place that handles all AI CLI interaction.
/// Every activity that needs to call an AI assistant delegates here.
/// Handles: service resolution, assistant normalization, model resolution,
/// execution plan building, container execution (streaming/non-streaming),
/// response parsing, session tracking, and retry.
/// </summary>
internal static class AiCliExecutor
{
    public record ExecutionParams
    {
        public required string ContainerId { get; init; }
        public required string Prompt { get; init; }
        public string? AiAssistant { get; init; }
        public string? Model { get; init; }
        public int? ModelPower { get; init; }
        public string WorkDir { get; init; } = "/workspace";
        public string? OutputSchema { get; init; }
        public int MaxRetries { get; init; } = 1;
        public int TimeoutMinutes { get; init; } = 30;
        public bool UseStreaming { get; init; }
        public Action<string>? OnOutputChunk { get; init; }
    }

    public record ExecutionResult
    {
        public required string Response { get; init; }
        public string? StructuredOutputJson { get; init; }
        public required bool Success { get; init; }
        public decimal CostUsd { get; init; }
        public string[] FilesModified { get; init; } = [];
        public int ExitCode { get; init; }
    }

    public static async Task<ExecutionResult> ExecuteAsync(
        ActivityExecutionContext context, ExecutionParams p)
    {
        // 1. RESOLVE SERVICES
        var containerMgr = context.GetRequiredService<IContainerManager>();
        var agentFactory = context.GetRequiredService<ICliAgentFactory>();
        var config = context.GetRequiredService<MagicPaiConfig>();

        // 2. VALIDATE
        if (config.RequireContainerizedAgentExecution && !config.UseWorkerContainers)
            throw new InvalidOperationException(
                "AI agent execution is configured to run only inside worker containers, but no worker-container backend is enabled.");
        if (string.IsNullOrWhiteSpace(p.ContainerId))
            throw new InvalidOperationException(
                "Container ID is required. AI agents execute inside the spawned worker container.");

        // 3. RESOLVE ASSISTANT + RUNNER
        var assistantName = AiAssistantResolver.NormalizeAssistant(
            p.AiAssistant, config.DefaultAgent);
        var runner = agentFactory.Create(assistantName);

        // 4. RESOLVE MODEL
        var resolved = ResolveAssistantOptions(runner, config, assistantName, p.Model, p.ModelPower);

        // 5. SESSION TRACKING
        var sessionId = AssistantSessionState.GetOrCreateSessionId(context, resolved.Assistant);

        // 6. BUILD REQUEST
        var workDir = config.UseWorkerContainers
            ? (string.IsNullOrWhiteSpace(p.WorkDir) ? config.ContainerWorkDir ?? "/workspace" : p.WorkDir)
            : p.WorkDir;

        var request = new AgentRequest
        {
            Prompt = p.Prompt,
            Model = resolved.Model,
            WorkDir = workDir,
            OutputSchema = string.IsNullOrWhiteSpace(p.OutputSchema) ? null : p.OutputSchema,
            SessionId = sessionId
        };
        var plan = runner.BuildExecutionPlan(request);

        // 7. LOG
        context.AddExecutionLogEntry("AiCliExecution",
            JsonSerializer.Serialize(new
            {
                activityId = context.Activity.Id,
                assistant = resolved.Assistant,
                model = resolved.Model ?? runner.DefaultModel,
                modelPower = resolved.ModelPower ?? 0,
                hasSchema = !string.IsNullOrWhiteSpace(p.OutputSchema),
                streaming = p.UseStreaming,
                resumedSessionId = sessionId
            }));

        // 8. EXECUTE SETUP
        foreach (var setup in plan.SetupRequests ?? [])
        {
            var setupResult = await containerMgr.ExecAsync(
                p.ContainerId, setup, context.CancellationToken);
            if (setupResult.ExitCode != 0)
                throw new InvalidOperationException(
                    $"Failed to prepare assistant execution: {setupResult.Error}");
        }

        // 9. EXECUTE MAIN (with retry)
        ExecResult result = new(1, "", "No execution attempted");
        CliAgentResponse parsed = new(false, "", 0, [], 0, 0, null);
        var maxAttempts = Math.Max(1, p.MaxRetries);

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            if (p.UseStreaming)
            {
                result = await containerMgr.ExecStreamingAsync(
                    p.ContainerId, plan.MainRequest,
                    onOutput: chunk =>
                    {
                        p.OnOutputChunk?.Invoke(chunk);
                    },
                    timeout: TimeSpan.FromMinutes(p.TimeoutMinutes),
                    ct: context.CancellationToken);
            }
            else
            {
                result = await containerMgr.ExecAsync(
                    p.ContainerId, plan.MainRequest,
                    context.CancellationToken);
            }

            // PARSE
            try
            {
                parsed = runner.ParseResponse(result.Output ?? "");
            }
            catch (Exception parseEx)
            {
                context.AddExecutionLogEntry("ParseError", parseEx.Message);
                parsed = new(false, result.Output ?? "", 0, [], 0, 0, null);
            }

            // CHECK SUCCESS
            if (result.ExitCode == 0 && parsed.Success)
                break;

            if (attempt < maxAttempts)
            {
                context.AddExecutionLogEntry("RetryAttempt",
                    JsonSerializer.Serialize(new
                    {
                        attempt,
                        maxAttempts,
                        exitCode = result.ExitCode,
                        parsedSuccess = parsed.Success
                    }));
            }
        }

        // 10. SAVE SESSION
        if (!string.IsNullOrWhiteSpace(parsed.SessionId))
            AssistantSessionState.SetSessionId(context, resolved.Assistant, parsed.SessionId!);

        // 11. BUILD RESULT
        var succeeded = result.ExitCode == 0 && parsed.Success;
        var response = !string.IsNullOrWhiteSpace(parsed.Output)
            ? parsed.Output
            : !string.IsNullOrWhiteSpace(result.Error)
                ? result.Error
                : result.Output ?? "";
        var structuredOutput = !string.IsNullOrWhiteSpace(parsed.StructuredOutputJson)
            ? parsed.StructuredOutputJson
            : ActivityHelpers.TryGetJson(response);

        return new ExecutionResult
        {
            Response = response,
            StructuredOutputJson = structuredOutput,
            Success = succeeded,
            CostUsd = parsed.CostUsd,
            FilesModified = parsed.FilesModified,
            ExitCode = result.ExitCode
        };
    }

    private static ResolvedAssistantOptions ResolveAssistantOptions(
        ICliAgentRunner runner,
        MagicPaiConfig config,
        string assistantName,
        string? explicitModel,
        int? modelPower)
    {
        // If explicit model or power provided, use the full resolver
        if (!string.IsNullOrWhiteSpace(explicitModel) ||
            (modelPower.HasValue && modelPower.Value > 0))
        {
            return AiAssistantResolver.Resolve(
                runner, config, assistantName, explicitModel, modelPower);
        }

        // Otherwise, just return normalized assistant with no specific model
        return new ResolvedAssistantOptions(assistantName, null, null);
    }
}
