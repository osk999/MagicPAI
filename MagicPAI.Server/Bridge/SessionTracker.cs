using System.Collections.Concurrent;
using MagicPAI.Core.Models;

namespace MagicPAI.Server.Bridge;

/// <summary>
/// Tracks active sessions and their output buffers.
/// Thread-safe singleton used by SessionHub, controllers, and event bridges.
/// </summary>
public class SessionTracker
{
    private readonly ConcurrentDictionary<string, SessionInfo> _sessions = new();
    private readonly ConcurrentDictionary<string, ConcurrentQueue<string>> _outputBuffers = new();
    private readonly int _maxBufferSize;

    public SessionTracker(int maxBufferSize = 1000)
    {
        _maxBufferSize = maxBufferSize;
    }

    public void RegisterSession(string sessionId, SessionInfo info)
    {
        _sessions[sessionId] = info;
        _outputBuffers.TryAdd(sessionId, new ConcurrentQueue<string>());
    }

    public void UpdateState(string sessionId, string state)
    {
        _sessions.AddOrUpdate(sessionId,
            _ => new SessionInfo { Id = sessionId, State = state },
            (_, existing) =>
            {
                // Thread-safe: create new instance with updated state
                return new SessionInfo
                {
                    Id = existing.Id,
                    WorkflowId = existing.WorkflowId,
                    State = state,
                    TotalCostUsd = existing.TotalCostUsd,
                    Agent = existing.Agent,
                    PromptPreview = existing.PromptPreview,
                    CreatedAt = existing.CreatedAt,
                };
            });
    }

    public void AppendOutput(string sessionId, string text)
    {
        if (!_outputBuffers.TryGetValue(sessionId, out var buffer))
        {
            buffer = new ConcurrentQueue<string>();
            _outputBuffers.TryAdd(sessionId, buffer);
        }

        buffer.Enqueue(text);

        // Trim buffer if it exceeds max size
        while (buffer.Count > _maxBufferSize)
        {
            buffer.TryDequeue(out _);
        }
    }

    public string[] GetOutput(string sessionId)
    {
        if (_outputBuffers.TryGetValue(sessionId, out var buffer))
        {
            return buffer.ToArray();
        }
        return [];
    }

    public SessionInfo? GetSession(string sessionId)
    {
        _sessions.TryGetValue(sessionId, out var info);
        return info;
    }

    public IReadOnlyCollection<SessionInfo> GetAllSessions()
    {
        return _sessions.Values.ToList().AsReadOnly();
    }

    public void RemoveSession(string sessionId)
    {
        _sessions.TryRemove(sessionId, out _);
        _outputBuffers.TryRemove(sessionId, out _);
    }
}
