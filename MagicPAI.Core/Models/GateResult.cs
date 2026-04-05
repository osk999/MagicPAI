namespace MagicPAI.Core.Models;

public record GateResult(
    string Name,
    bool Passed,
    string Output,
    string[] Issues,
    TimeSpan Duration);
