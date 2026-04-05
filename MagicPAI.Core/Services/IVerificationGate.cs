using MagicPAI.Core.Models;

namespace MagicPAI.Core.Services;

public interface IVerificationGate
{
    /// <summary>Gate name (e.g. "compile", "test", "security").</summary>
    string Name { get; }

    /// <summary>If true, pipeline stops on failure.</summary>
    bool IsBlocking { get; }

    /// <summary>Can this gate run for the given project?</summary>
    Task<bool> CanVerifyAsync(IContainerManager container,
        string containerId, string workDir, CancellationToken ct);

    /// <summary>Run the verification. Returns pass/fail + details.</summary>
    Task<GateResult> VerifyAsync(IContainerManager container,
        string containerId, string workDir, CancellationToken ct);
}
