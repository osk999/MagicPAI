namespace MagicPAI.Core.Services;

public interface ISessionContainerRegistry
{
    void UpdateContainer(string sessionId, string? containerId, string? guiUrl = null);
}
