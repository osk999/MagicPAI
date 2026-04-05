namespace MagicPAI.Core.Services;

public interface ICliAgentFactory
{
    /// <summary>Create a runner for the named agent.</summary>
    ICliAgentRunner Create(string agentName);

    /// <summary>List of supported agent names.</summary>
    string[] AvailableAgents { get; }
}
