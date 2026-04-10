namespace MagicPAI.Core.Services;

public interface ISessionContainerLogStreamer
{
    void StartStreaming(string sessionId, string containerId);
    Task StopStreamingAsync(string sessionId);
}
