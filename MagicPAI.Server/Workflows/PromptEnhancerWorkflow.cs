using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Activities.Flowchart.Activities;
using Elsa.Workflows.Activities.Flowchart.Models;
using Elsa.Workflows.Models;
using MagicPAI.Activities.AI;

namespace MagicPAI.Server.Workflows;

/// <summary>
/// Classify prompt vagueness, enhance with Sonnet, quality check, escalate to Opus if needed.
/// Flow: Triage (vagueness check) -> Enhance with Sonnet -> Quality Check Triage -> [if still vague: Opus enhancement]
/// </summary>
public class PromptEnhancerWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        builder.Name = "Prompt Enhancer";
        builder.Description =
            "Classify prompt vagueness and enhance using Sonnet, escalating to Opus if needed";

        var prompt = builder.WithVariable<string>("Prompt", "");
        var agent = builder.WithVariable<string>("AiAssistant", "claude");
        var containerId = builder.WithVariable<string>("ContainerId", "");
        var enhancedPrompt = builder.WithVariable<string>("EnhancedPrompt", "");

        // Step 1: Classify vagueness
        var classifyVagueness = new TriageActivity
        {
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            Id = "classify-vagueness"
        };

        // Step 2: Enhance with Sonnet (for vague/complex prompts)
        var enhanceSonnet = new AiAssistantActivity
        {
            AiAssistant = new Input<string>(agent),
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            ModelPower = new Input<int>(2),
            Response = new Output<string>(enhancedPrompt),
            Id = "enhance-sonnet"
        };

        // Step 3: Quality check the enhanced prompt
        var qualityCheck = new TriageActivity
        {
            Prompt = new Input<string>(enhancedPrompt),
            ContainerId = new Input<string>(containerId),
            Id = "quality-check"
        };

        // Step 4: Escalate to Opus if quality check shows still complex
        var enhanceOpus = new AiAssistantActivity
        {
            AiAssistant = new Input<string>(agent),
            Prompt = new Input<string>(ctx =>
                string.IsNullOrWhiteSpace(ctx.GetVariable<string>("EnhancedPrompt"))
                    ? ctx.GetVariable<string>("Prompt") ?? ""
                    : ctx.GetVariable<string>("EnhancedPrompt") ?? ""),
            ContainerId = new Input<string>(containerId),
            ModelPower = new Input<int>(1),
            Response = new Output<string>(enhancedPrompt),
            Id = "enhance-opus"
        };

        // Simple prompts go directly to Sonnet enhancement (still benefit from it)
        var flowchart = new Flowchart
        {
            Id = "prompt-enhancer-flow",
            Start = classifyVagueness,
            Activities = { classifyVagueness, enhanceSonnet, qualityCheck, enhanceOpus },
            Connections =
            {
                // Vague/Complex -> Sonnet enhancement
                new Connection(
                    new Endpoint(classifyVagueness, "Complex"),
                    new Endpoint(enhanceSonnet)),

                // Simple prompts also get enhanced
                new Connection(
                    new Endpoint(classifyVagueness, "Simple"),
                    new Endpoint(enhanceSonnet)),

                // After Sonnet enhancement -> quality check
                new Connection(
                    new Endpoint(enhanceSonnet, "Done"),
                    new Endpoint(qualityCheck)),

                // Quality check says simple -> done (good enough)
                // (terminal - no further connections needed)

                // Quality check says still complex -> escalate to Opus
                new Connection(
                    new Endpoint(qualityCheck, "Complex"),
                    new Endpoint(enhanceOpus)),

                // Sonnet failed -> try Opus directly
                new Connection(
                    new Endpoint(enhanceSonnet, "Failed"),
                    new Endpoint(enhanceOpus)),
            }
        };

        builder.Root = flowchart.WithAttachedVariables(builder);
    }
}
