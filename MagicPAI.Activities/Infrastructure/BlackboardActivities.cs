using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Temporalio.Activities;
using MagicPAI.Activities.Contracts;
using MagicPAI.Core.Services;

namespace MagicPAI.Activities.Infrastructure;

/// <summary>
/// Temporal activity group for the shared blackboard — atomic file-ownership
/// primitives used by parallel agents in the complex-orchestration path.
/// See temporal.md §I.4 and §7.6.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="SharedBlackboard.ClaimFile"/> is implemented as
/// <c>ConcurrentDictionary.TryAdd</c>, which is already atomic. That means the
/// "check owner, then claim" pattern shown in temporal.md §I.4 is technically
/// racy — between the <c>GetFileOwner</c> read and the <c>ClaimFile</c> call,
/// another task could win. We use the single atomic <c>TryAdd</c> instead, and
/// only report the losing owner on failure. This preserves the
/// <see cref="ClaimFileOutput"/> contract (Claimed + CurrentOwner) without a
/// race window.
/// </para>
/// </remarks>
public class BlackboardActivities
{
    private readonly SharedBlackboard _blackboard;
    private readonly ILogger<BlackboardActivities> _log;

    public BlackboardActivities(
        SharedBlackboard blackboard,
        ILogger<BlackboardActivities>? log = null)
    {
        _blackboard = blackboard;
        _log = log ?? NullLogger<BlackboardActivities>.Instance;
    }

    /// <summary>
    /// Atomically claim ownership of a file path for the given task. If the file
    /// is already owned by a different task, returns Claimed=false with the
    /// current owner's id. Re-claiming a file you already own is a no-op success.
    /// </summary>
    [Activity]
    public Task<ClaimFileOutput> ClaimFileAsync(ClaimFileInput input)
    {
        // Fast path: atomic TryAdd. TryAdd returns true only on first insertion.
        var claimed = _blackboard.ClaimFile(input.FilePath, input.TaskId);
        if (claimed)
        {
            _log.LogInformation("File {F} claimed by {T}", input.FilePath, input.TaskId);
            return Task.FromResult(new ClaimFileOutput(Claimed: true, CurrentOwner: null));
        }

        // Contended path: someone else owns it — or this same task already claimed
        // it earlier (idempotent re-claim).
        var currentOwner = _blackboard.GetFileOwner(input.FilePath);
        if (currentOwner == input.TaskId)
        {
            _log.LogInformation("File {F} already owned by {T} (idempotent)", input.FilePath, input.TaskId);
            return Task.FromResult(new ClaimFileOutput(Claimed: true, CurrentOwner: null));
        }

        _log.LogInformation("File {F} already claimed by {Owner}; {T} denied",
            input.FilePath, currentOwner, input.TaskId);
        return Task.FromResult(new ClaimFileOutput(Claimed: false, CurrentOwner: currentOwner));
    }

    /// <summary>
    /// Release a claim on a file path. Only the owning task may release a claim
    /// — <see cref="SharedBlackboard.ReleaseFile"/> checks ownership atomically
    /// via <c>TryRemove(KeyValuePair)</c>. Releasing a file you don't own is a no-op.
    /// </summary>
    [Activity]
    public Task ReleaseFileAsync(ReleaseFileInput input)
    {
        var released = _blackboard.ReleaseFile(input.FilePath, input.TaskId);
        if (released)
            _log.LogInformation("File {F} released by {T}", input.FilePath, input.TaskId);
        else
            _log.LogDebug("File {F} release by {T} was a no-op (not owned)",
                input.FilePath, input.TaskId);
        return Task.CompletedTask;
    }
}
