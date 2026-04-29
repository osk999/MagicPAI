namespace MagicPAI.Core.Models;

/// <summary>
/// Lightweight description of a container observed via the host's container
/// engine, used by the worker garbage collector's fallback sweep to identify
/// MagicPAI-owned containers (those carrying the <c>magicpai.session</c> label)
/// when the in-memory <c>SessionTracker</c> has been lost across a server
/// restart.
/// </summary>
public sealed record LabeledContainer(
    string ContainerId,
    IReadOnlyDictionary<string, string> Labels,
    DateTime CreatedAtUtc,
    bool IsRunning);
