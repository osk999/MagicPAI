using System.Collections.Concurrent;

namespace MagicPAI.Core.Services;

public class SharedBlackboard
{
    private readonly ConcurrentDictionary<string, string> _fileClaims = new();
    private readonly ConcurrentDictionary<string, string> _taskOutputs = new();

    public bool ClaimFile(string filePath, string taskId)
        => _fileClaims.TryAdd(filePath, taskId);

    public bool ReleaseFile(string filePath, string taskId)
        => _fileClaims.TryRemove(filePath, out var owner) && owner == taskId;

    public string? GetFileOwner(string filePath)
        => _fileClaims.TryGetValue(filePath, out var owner) ? owner : null;

    public void SetTaskOutput(string taskId, string output)
        => _taskOutputs[taskId] = output;

    public string? GetTaskOutput(string taskId)
        => _taskOutputs.TryGetValue(taskId, out var output) ? output : null;

    public void Clear()
    {
        _fileClaims.Clear();
        _taskOutputs.Clear();
    }
}
