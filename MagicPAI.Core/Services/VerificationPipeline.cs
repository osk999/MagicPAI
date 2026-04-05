using MagicPAI.Core.Models;

namespace MagicPAI.Core.Services;

public class VerificationPipeline
{
    private readonly IEnumerable<IVerificationGate> _gates;

    public VerificationPipeline(IEnumerable<IVerificationGate> gates)
    {
        _gates = gates;
    }

    public async Task<PipelineResult> RunAsync(
        IContainerManager container, string containerId,
        string workDir, string[] gateFilter,
        string? workerOutput, CancellationToken ct)
    {
        var results = new List<GateResult>();
        var gates = _gates
            .Where(g => gateFilter.Contains(g.Name))
            .ToList();

        if (gates.Count == 0)
        {
            return new PipelineResult { Gates = results, AllPassed = true };
        }

        foreach (var gate in gates)
        {
            ct.ThrowIfCancellationRequested();

            if (!await gate.CanVerifyAsync(container, containerId, workDir, ct))
                continue;

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = await gate.VerifyAsync(container, containerId, workDir, ct);
            sw.Stop();

            results.Add(result);

            // Early stop on blocking gate failure
            if (gate.IsBlocking && !result.Passed)
                break;
        }

        return new PipelineResult
        {
            Gates = results,
            AllPassed = results.All(r => r.Passed || !IsBlocking(r.Name)),
        };
    }

    private bool IsBlocking(string gateName) =>
        _gates.FirstOrDefault(g => g.Name == gateName)?.IsBlocking ?? true;
}
