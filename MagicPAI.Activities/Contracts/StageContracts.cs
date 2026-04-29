// MagicPAI.Activities/Contracts/StageContracts.cs
// Temporal activity input record for the Stage activity group — pipeline
// stage transitions emitted to the session stream sink so Studio chips
// reflect the current workflow phase.
namespace MagicPAI.Activities.Contracts;

public record EmitStageInput(
    string SessionId,
    string Stage);

/// <summary>
/// Input for <see cref="MagicPAI.Activities.Stage.StageActivities.EmitCostAsync"/>.
/// Carries the running total-cost-USD so the Studio cost tile can update
/// mid-session instead of only at completion.
/// </summary>
public record EmitCostInput(
    string SessionId,
    decimal TotalCostUsd);
