using System.Text.Json;
using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Activities.Flowchart.Attributes;
using Elsa.Workflows.Attributes;
using Elsa.Workflows.Models;
using Elsa.Workflows.UIHints;
using MagicPAI.Activities;
using MagicPAI.Core.Config;
using MagicPAI.Core.Models;
using MagicPAI.Core.Services;

namespace MagicPAI.Activities.AI;

[Activity("MagicPAI", "AI Agents", "Classify prompt complexity using a cheap model")]
[FlowNode("Simple", "Complex")]
public class TriageActivity : Activity
{
    [Input(DisplayName = "Prompt", UIHint = InputUIHints.MultiLine)]
    public Input<string> Prompt { get; set; } = default!;

    [Input(DisplayName = "Container ID")]
    public Input<string> ContainerId { get; set; } = default!;

    [Input(DisplayName = "Classification Instructions",
        UIHint = InputUIHints.MultiLine,
        Description = "Optional override instructions for how the prompt should be classified")]
    public Input<string?> ClassificationInstructions { get; set; } = default!;

    [Output(DisplayName = "Complexity")]
    public Output<int> Complexity { get; set; } = default!;

    [Output(DisplayName = "Category")]
    public Output<string> Category { get; set; } = default!;

    [Output(DisplayName = "Recommended Model")]
    public Output<string> RecommendedModel { get; set; } = default!;

    [Output(DisplayName = "Recommended Model Power")]
    public Output<int> RecommendedModelPower { get; set; } = default!;

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var containerMgr = context.GetRequiredService<IContainerManager>();
        var agentFactory = context.GetRequiredService<ICliAgentFactory>();
        var config = context.GetRequiredService<MagicPaiConfig>();

        try
        {
            if (config.RequireContainerizedAgentExecution && !config.UseWorkerContainers)
                throw new InvalidOperationException("AI agent execution is configured to run only inside worker containers, but no worker-container backend is enabled.");

            var assistantName = AiAssistantResolver.NormalizeAssistant(
                context.GetOptionalWorkflowInput<string>("AiAssistant")
                ?? context.GetOptionalWorkflowInput<string>("Agent")
                ?? config.DefaultAgent,
                config.DefaultAgent);
            var runner = agentFactory.Create(assistantName);
            var prompt = context.GetOptionalWorkflowInput<string>("Prompt") ?? Prompt.Get(context) ?? "";
            var workDir = config.UseWorkerContainers
                ? config.ContainerWorkDir ?? "/workspace"
                : context.GetOptionalWorkflowInput<string>("WorkspacePath")
                    ?? config.WorkspacePath
                    ?? ".";
            var instructions = ClassificationInstructions.GetOrDefault(context, () => null);
            var triagePrompt = BuildTriagePrompt(prompt, instructions);
            var triageSchema = SchemaGenerator.FromType<TriageResult>();
            var request = new AgentRequest
            {
                Prompt = triagePrompt,
                Model = AiAssistantResolver.ResolveModelForPower(runner, config, 3),
                OutputSchema = triageSchema,
                WorkDir = workDir,
                SessionId = AssistantSessionState.GetOrCreateSessionId(context, assistantName)
            };
            var plan = runner.BuildExecutionPlan(request);

            var cid = ContainerId.GetOrDefault(context, () => "");
            if (string.IsNullOrEmpty(cid))
                cid = TryGetVariable<string>(context, "ContainerId") ?? "";
            if (string.IsNullOrEmpty(cid))
                cid = context.GetOptionalWorkflowInput<string>("ContainerId") ?? "";
            if (string.IsNullOrWhiteSpace(cid))
                throw new InvalidOperationException("Container ID is required. Triage runs inside the spawned worker container.");

            foreach (var setupRequest in plan.SetupRequests ?? [])
                await containerMgr.ExecAsync(cid, setupRequest, context.CancellationToken);

            var result = await containerMgr.ExecAsync(
                cid, plan.MainRequest, context.CancellationToken);

            if (result.ExitCode != 0)
            {
                context.AddExecutionLogEntry("TriageCommandFailed",
                    JsonSerializer.Serialize(new
                    {
                        exitCode = result.ExitCode,
                        output = Truncate(result.Output),
                        error = Truncate(result.Error)
                    }));
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(result.Error)
                        ? $"Triage agent exited with code {result.ExitCode}."
                        : result.Error);
            }

            var parsedResponse = runner.ParseResponse(result.Output ?? "");
            if (!string.IsNullOrWhiteSpace(parsedResponse.SessionId))
                AssistantSessionState.SetSessionId(context, assistantName, parsedResponse.SessionId!);
            var parsed = ParseTriageResponse(parsedResponse.Output ?? result.Output ?? "");
            var recommendedModel = AiAssistantResolver.ResolveModelForPower(
                runner,
                config,
                parsed.RecommendedModelPower);
            Complexity.Set(context, parsed.Complexity);
            Category.Set(context, parsed.Category);
            RecommendedModel.Set(context, recommendedModel);
            RecommendedModelPower.Set(context, parsed.RecommendedModelPower);

            var threshold = config.ComplexityThreshold;
            var outcome = parsed.Complexity >= threshold ? "Complex" : "Simple";
            context.AddExecutionLogEntry("TriageResult",
                JsonSerializer.Serialize(new
                {
                    complexity = parsed.Complexity,
                    category = parsed.Category,
                    recommendedModel,
                    recommendedModelPower = parsed.RecommendedModelPower,
                    needsDecomposition = parsed.NeedsDecomposition,
                    threshold,
                    outcome
                }));
            await context.CompleteActivityWithOutcomesAsync(outcome);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            context.AddExecutionLogEntry("TriageFailed", ex.ToString());
            throw;
        }
    }

    private static T? TryGetVariable<T>(ActivityExecutionContext context, string name)
    {
        try
        {
            return context.GetVariable<T>(name);
        }
        catch
        {
            return default;
        }
    }

    private static string Truncate(string? value, int maxLength = 4000)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";
        return value.Length <= maxLength ? value : value[..maxLength];
    }

    private static string BuildTriagePrompt(string userPrompt, string? classificationInstructions)
    {
        var instructionBlock = string.IsNullOrWhiteSpace(classificationInstructions)
            ? "Analyze this coding task and respond with JSON only:"
            : $"{classificationInstructions.Trim()}\nRespond with JSON only:";

        return
        $$"""
        {{instructionBlock}}
        {
          "complexity": <1-10>,
          "category": "<code_gen|bug_fix|refactor|architecture|testing|docs>",
          "needs_decomposition": <true|false>,
          "recommended_model_power": <1|2>
        }

        Model power guide:
        1 = strongest / deepest reasoning (opus-class)
        2 = balanced default (sonnet-class)
        Always use 1 for complex tasks and 2 for simple tasks. Never recommend 3.

        Task: {{userPrompt}}
        """;
    }

    private static TriageResult ParseTriageResponse(string output)
    {
        try
        {
            using var doc = JsonDocument.Parse(output);
            var root = doc.RootElement;
            return new TriageResult(
                Complexity: root.TryGetProperty("complexity", out var c) ? c.GetInt32() : 5,
                Category: root.TryGetProperty("category", out var cat) ? cat.GetString() ?? "code_gen" : "code_gen",
                RecommendedModelPower: root.TryGetProperty("recommended_model_power", out var m) ? m.GetInt32() : 2,
                NeedsDecomposition: root.TryGetProperty("needs_decomposition", out var nd) && nd.GetBoolean());
        }
        catch
        {
            return new TriageResult(5, "code_gen", 2, false);
        }
    }
}
