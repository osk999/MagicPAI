// MagicPAI.Workflows/ActivityProfiles.cs
// Canonical ActivityOptions profiles used by every workflow's Workflow.ExecuteActivityAsync call.
// See temporal.md §7.9 for rationale per profile.
using Temporalio.Common;
using Temporalio.Workflows;

namespace MagicPAI.Workflows;

public static class ActivityProfiles
{
    /// <summary>
    /// Short synchronous work (classification, model routing, repair prompt generation).
    /// Completes in under 5 minutes in practice; no heartbeat needed.
    /// </summary>
    public static readonly ActivityOptions Short = new()
    {
        StartToCloseTimeout = TimeSpan.FromMinutes(5),
        RetryPolicy = new RetryPolicy
        {
            MaximumAttempts = 3,
            InitialInterval = TimeSpan.FromSeconds(2),
            BackoffCoefficient = 2.0f,
            NonRetryableErrorTypes = new[] { "ConfigError" }
        }
    };

    /// <summary>
    /// Medium AI work (triage, classify, route, prompt enhance, website classify, coverage).
    /// One full CLI invocation per activity; 15 minutes enough in practice.
    /// </summary>
    public static readonly ActivityOptions Medium = new()
    {
        StartToCloseTimeout = TimeSpan.FromMinutes(15),
        HeartbeatTimeout = TimeSpan.FromSeconds(60),
        RetryPolicy = new RetryPolicy
        {
            MaximumAttempts = 3,
            InitialInterval = TimeSpan.FromSeconds(5),
            BackoffCoefficient = 2.0f,
            NonRetryableErrorTypes = new[] { "ConfigError", "InvalidPrompt" }
        }
    };

    /// <summary>
    /// Long AI work (RunCliAgent, ResearchPrompt, Architect). Up to 2 hours.
    /// Cancellation waits for clean container teardown.
    /// </summary>
    public static readonly ActivityOptions Long = new()
    {
        StartToCloseTimeout = TimeSpan.FromHours(2),
        HeartbeatTimeout = TimeSpan.FromSeconds(60),
        CancellationType = ActivityCancellationType.WaitCancellationCompleted,
        RetryPolicy = new RetryPolicy
        {
            MaximumAttempts = 3,
            InitialInterval = TimeSpan.FromSeconds(10),
            BackoffCoefficient = 2.0f,
            NonRetryableErrorTypes = new[] { "AuthError", "ConfigError", "InvalidPrompt" }
        }
    };

    /// <summary>
    /// Container lifecycle (spawn, destroy). Must not retry spawn — we'd orphan containers.
    /// </summary>
    public static readonly ActivityOptions Container = new()
    {
        StartToCloseTimeout = TimeSpan.FromMinutes(3),
        RetryPolicy = new RetryPolicy
        {
            MaximumAttempts = 1,   // spawn/destroy are idempotent only with care
            NonRetryableErrorTypes = new[] { "ConfigError" }
        }
    };

    /// <summary>
    /// Container teardown that must run even when the parent workflow is being
    /// cancelled. Uses <see cref="CancellationToken.None"/> so the activity is
    /// scheduled even after Workflow cancellation propagated, and
    /// <see cref="ActivityCancellationType.Abandon"/> so the activity body itself
    /// doesn't receive the cancel signal. This is the "cleanup in finally after
    /// cancellation" pattern — losing the container to orphaning is worse than
    /// waiting a few extra seconds for docker rm.
    /// </summary>
    public static readonly ActivityOptions ContainerCleanup = new()
    {
        StartToCloseTimeout = TimeSpan.FromMinutes(3),
        RetryPolicy = new RetryPolicy
        {
            MaximumAttempts = 1,
            NonRetryableErrorTypes = new[] { "ConfigError" }
        },
        CancellationToken = CancellationToken.None,
        CancellationType = ActivityCancellationType.Abandon,
    };

    /// <summary>
    /// Verification gates. Compile/test can take long; retry once on transient failures.
    /// </summary>
    public static readonly ActivityOptions Verify = new()
    {
        StartToCloseTimeout = TimeSpan.FromMinutes(30),
        HeartbeatTimeout = TimeSpan.FromSeconds(60),
        RetryPolicy = new RetryPolicy
        {
            MaximumAttempts = 2,
            InitialInterval = TimeSpan.FromSeconds(10),
            NonRetryableErrorTypes = new[] { "GateConfigError" }
        }
    };
}
