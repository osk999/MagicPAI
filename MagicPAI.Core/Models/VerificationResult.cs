namespace MagicPAI.Core.Models;

public class PipelineResult
{
    public List<GateResult> Gates { get; set; } = new();
    public bool AllPassed { get; set; }
    public bool IsInconclusive => Gates.Count == 0;
}
