// MagicPAI.Server/Workflows/Temporal/WebsiteAuditLoopWorkflow.cs
// Temporal port of the Elsa WebsiteAuditLoopWorkflow. Iterates over website
// sections sequentially, dispatching one WebsiteAuditCoreWorkflow child per
// section. Exposes a SkipRemainingSectionsAsync signal for early termination
// and SectionsDone/SectionsRemaining queries for UI progress. See temporal.md §H.12.
using Temporalio.Workflows;
using MagicPAI.Workflows.Contracts;

namespace MagicPAI.Server.Workflows;

/// <summary>
/// Sequential per-section audit loop. Uses
/// <see cref="Workflow.ExecuteChildWorkflowAsync{TWorkflow,TResult}"/> so each
/// section runs in its own child workflow id (<c>{sessionId}-sect-{sectionId}</c>),
/// making per-section history inspectable in Studio.
/// </summary>
/// <remarks>
/// The loop is sequential by design — auditing a website section by section
/// preserves dependencies (navigation may reference homepage). Parallelizing
/// is possible but out of scope for this port; see the commented-out pattern in
/// <see cref="OrchestrateComplexPathWorkflow"/> for a fan-out implementation.
/// </remarks>
[Workflow]
public class WebsiteAuditLoopWorkflow
{
    private int _sectionsDone;
    private bool _skipRemaining;
    private readonly List<WebsiteAuditCoreOutput> _results = new();

    private static readonly IReadOnlyList<string> DefaultSections = new[]
    {
        "homepage", "navigation", "forms", "checkout", "footer"
    };

    [WorkflowQuery]
    public int SectionsDone => _sectionsDone;

    /// <summary>
    /// Sections still pending given a caller-provided total. Total is a parameter
    /// because the loop runs against <see cref="WebsiteAuditInput.SectionIds"/>
    /// (or the default set when null), so callers already know the total.
    /// </summary>
    [WorkflowQuery]
    public int SectionsRemaining(int total) => Math.Max(0, total - _sectionsDone);

    [WorkflowSignal]
    public Task SkipRemainingSectionsAsync()
    {
        _skipRemaining = true;
        return Task.CompletedTask;
    }

    [WorkflowRun]
    public async Task<WebsiteAuditOutput> RunAsync(WebsiteAuditInput input)
    {
        var sections = input.SectionIds ?? DefaultSections;

        foreach (var sectionId in sections)
        {
            if (_skipRemaining)
                break;

            var childInput = new WebsiteAuditCoreInput(
                SessionId: input.SessionId,
                SectionId: sectionId,
                SectionDescription: $"{input.Prompt}\nSection: {sectionId}",
                ContainerId: input.ContainerId,
                WorkspacePath: input.WorkspacePath,
                AiAssistant: input.AiAssistant,
                Model: input.Model);

            var audit = await Workflow.ExecuteChildWorkflowAsync(
                (WebsiteAuditCoreWorkflow w) => w.RunAsync(childInput),
                new ChildWorkflowOptions
                {
                    Id = $"{input.SessionId}-sect-{sectionId}"
                });

            _results.Add(audit);
            _sectionsDone++;
        }

        var summary = string.Join("\n\n",
            _results.Select(r => $"## {r.SectionId}\nIssues: {r.IssueCount}\n{r.AuditReport}"));

        return new WebsiteAuditOutput(
            SectionsAudited: _results.Count,
            TotalIssueCount: _results.Sum(r => r.IssueCount),
            Summary: summary,
            CostUsd: _results.Sum(r => r.CostUsd));
    }
}
