using MagicPAI.Core.Services;

namespace MagicPAI.Tests.Integration.Stubs;

/// <summary>
/// Stub factory that always returns the same StubCliAgentRunner.
/// </summary>
public class StubCliAgentFactory : ICliAgentFactory
{
    private readonly Dictionary<string, StubCliAgentRunner> _runners = new(StringComparer.OrdinalIgnoreCase);

    public string[] AvailableAgents => ["claude", "codex", "gemini"];

    public ICliAgentRunner Create(string agentName)
    {
        if (!_runners.TryGetValue(agentName, out var runner))
        {
            runner = new StubCliAgentRunner(agentName);
            _runners[agentName] = runner;
        }

        return runner;
    }
}
