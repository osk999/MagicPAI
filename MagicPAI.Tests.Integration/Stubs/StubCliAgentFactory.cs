using MagicPAI.Core.Services;

namespace MagicPAI.Tests.Integration.Stubs;

/// <summary>
/// Stub factory that always returns the same StubCliAgentRunner.
/// </summary>
public class StubCliAgentFactory : ICliAgentFactory
{
    private readonly StubCliAgentRunner _runner = new();

    public string[] AvailableAgents => ["claude", "codex", "gemini"];

    public ICliAgentRunner Create(string agentName) => _runner;
}
