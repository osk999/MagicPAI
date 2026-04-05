using Elsa.Workflows;
using Elsa.Workflows.Activities.Flowchart.Activities;
using Elsa.Workflows.Activities.Flowchart.Models;
using Elsa.Workflows.Models;
using MagicPAI.Activities.AI;
using MagicPAI.Activities.Verification;

namespace MagicPAI.Workflows;

/// <summary>
/// Post-execution quality pipeline:
/// Completeness audit -> review loop -> quality gates -> E2E test -> verify and repair.
/// Ensures the agent output meets quality standards after the main execution.
/// </summary>
public class PostExecutionPipelineWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        builder.Name = "Post-Execution Pipeline";
        builder.Description =
            "Post-execution quality: audit, review, quality gates, E2E test, verify and repair";

        var prompt = builder.WithVariable<string>("Prompt", "");
        var containerId = builder.WithVariable<string>("ContainerId", "");
        var agent = builder.WithVariable<string>("Agent", "claude");
        var model = builder.WithVariable<string>("Model", "sonnet");
        var workerOutput = builder.WithVariable<string>("WorkerOutput", "");

        // Step 1: Completeness audit
        var completenessAudit = new RunCliAgentActivity
        {
            Agent = new Input<string>("claude"),
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            Model = new Input<string>("sonnet"),
            Id = "completeness-audit"
        };

        // Step 2: Review loop (code review agent)
        var reviewAgent = new RunCliAgentActivity
        {
            Agent = new Input<string>("claude"),
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            Model = new Input<string>("sonnet"),
            Id = "review-agent"
        };

        // Step 3: Review check (is review satisfactory?)
        var reviewCheck = new TriageActivity
        {
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            Id = "review-check"
        };

        // Step 4: Quality gates verification
        var qualityGates = new RunVerificationActivity
        {
            ContainerId = new Input<string>(containerId),
            WorkerOutput = new Input<string?>(workerOutput),
            Id = "quality-gates"
        };

        // Step 5: E2E test
        var e2eTest = new RunCliAgentActivity
        {
            Agent = new Input<string>("claude"),
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            Model = new Input<string>("sonnet"),
            Id = "e2e-test"
        };

        // Step 6: Final verify and repair
        var finalVerify = new RunVerificationActivity
        {
            ContainerId = new Input<string>(containerId),
            Id = "final-verify"
        };

        // Step 7: Repair on failure
        var repair = new RepairActivity
        {
            ContainerId = new Input<string>(containerId),
            FailedGates = new Input<string[]>([]),
            OriginalPrompt = new Input<string>(prompt),
            GateResultsJson = new Input<string>(""),
            Id = "post-repair"
        };

        var repairAgent = new RunCliAgentActivity
        {
            Agent = new Input<string>(agent),
            Prompt = new Input<string>(prompt),
            ContainerId = new Input<string>(containerId),
            Model = new Input<string>(model),
            Id = "post-repair-agent"
        };

        var flowchart = new Flowchart
        {
            Id = "post-execution-pipeline-flow",
            Start = completenessAudit,
            Connections =
            {
                // Audit -> Review
                new Connection(
                    new Endpoint(completenessAudit, "Done"),
                    new Endpoint(reviewAgent)),
                new Connection(
                    new Endpoint(completenessAudit, "Failed"),
                    new Endpoint(reviewAgent)),

                // Review -> ReviewCheck
                new Connection(
                    new Endpoint(reviewAgent, "Done"),
                    new Endpoint(reviewCheck)),
                new Connection(
                    new Endpoint(reviewAgent, "Failed"),
                    new Endpoint(qualityGates)),

                // ReviewCheck Complex -> loop back to review
                new Connection(
                    new Endpoint(reviewCheck, "Complex"),
                    new Endpoint(reviewAgent)),

                // ReviewCheck Simple (review ok) -> quality gates
                new Connection(
                    new Endpoint(reviewCheck, "Simple"),
                    new Endpoint(qualityGates)),

                // Quality gates -> E2E test
                new Connection(
                    new Endpoint(qualityGates, "Passed"),
                    new Endpoint(e2eTest)),
                new Connection(
                    new Endpoint(qualityGates, "Inconclusive"),
                    new Endpoint(e2eTest)),

                // Quality gates failed -> repair
                new Connection(
                    new Endpoint(qualityGates, "Failed"),
                    new Endpoint(repair)),

                // E2E test -> final verify
                new Connection(
                    new Endpoint(e2eTest, "Done"),
                    new Endpoint(finalVerify)),
                new Connection(
                    new Endpoint(e2eTest, "Failed"),
                    new Endpoint(finalVerify)),

                // Final verify passed -> terminal (done)

                // Final verify failed -> repair
                new Connection(
                    new Endpoint(finalVerify, "Failed"),
                    new Endpoint(repair)),

                // Repair -> repair agent -> final verify (loop)
                new Connection(
                    new Endpoint(repair, "Done"),
                    new Endpoint(repairAgent)),
                new Connection(
                    new Endpoint(repairAgent, "Done"),
                    new Endpoint(finalVerify)),
                new Connection(
                    new Endpoint(repairAgent, "Failed"),
                    new Endpoint(finalVerify)),
            }
        };

        builder.Root = flowchart;
    }
}
