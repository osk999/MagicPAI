// MagicPAI.Server/Services/MagicPaiMetrics.cs
// OpenTelemetry counters, histograms, and up-down counters for MagicPAI
// per temporal.md §16.5. Registered as a singleton so both controllers and
// activities can record metrics. The "MagicPAI" meter name is picked up by
// the OTel pipeline in Program.cs (`.AddMeter("MagicPAI")`).
using System.Diagnostics.Metrics;

namespace MagicPAI.Server.Services;

public class MagicPaiMetrics : IDisposable
{
    public static readonly Meter Meter = new("MagicPAI", "1.0");

    public readonly Counter<long> SessionsStarted =
        Meter.CreateCounter<long>(
            "magicpai_sessions_started_total",
            description: "Total sessions started by workflow type.");

    public readonly Counter<long> SessionsCompleted =
        Meter.CreateCounter<long>(
            "magicpai_sessions_completed_total",
            description: "Total sessions completed by status.");

    public readonly Histogram<double> SessionDurationSeconds =
        Meter.CreateHistogram<double>(
            "magicpai_session_duration_seconds",
            unit: "s",
            description: "Session duration from start to completion.");

    public readonly Histogram<double> CostPerSessionUsd =
        Meter.CreateHistogram<double>(
            "magicpai_session_cost_usd",
            unit: "USD",
            description: "Total cost per session.");

    public readonly UpDownCounter<int> ActiveContainers =
        Meter.CreateUpDownCounter<int>(
            "magicpai_active_containers",
            description: "Currently running worker containers.");

    public readonly Counter<long> VerificationGatesRun =
        Meter.CreateCounter<long>(
            "magicpai_verification_gates_total",
            description: "Verification gates evaluated, by name and result.");

    public readonly Counter<long> AuthRecoveriesAttempted =
        Meter.CreateCounter<long>(
            "magicpai_auth_recoveries_total",
            description: "Auth recovery attempts, by outcome.");

    public void Dispose() => Meter.Dispose();
}
