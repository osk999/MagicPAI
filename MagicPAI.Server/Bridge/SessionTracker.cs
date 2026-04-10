using System.Collections.Concurrent;
using MagicPAI.Core.Models;
using MagicPAI.Core.Services;

namespace MagicPAI.Server.Bridge;

/// <summary>
/// Tracks active sessions and their output buffers.
/// Thread-safe singleton used by SessionHub, controllers, and event bridges.
/// </summary>
public class SessionTracker : ISessionContainerRegistry
{
    private readonly ConcurrentDictionary<string, SessionInfo> _sessions = new();
    private readonly ConcurrentDictionary<string, ConcurrentQueue<string>> _outputBuffers = new();
    private readonly ConcurrentDictionary<string, ConcurrentDictionary<string, string>> _activityStates = new();
    private readonly ConcurrentDictionary<string, ConcurrentQueue<TaskInsightEvent>> _insightBuffers = new();
    private readonly int _maxBufferSize;
    private const int MaxInsightBufferSize = 50;

    public SessionTracker(int maxBufferSize = 1000)
    {
        _maxBufferSize = maxBufferSize;
    }

    public void RegisterSession(string sessionId, SessionInfo info)
    {
        info.CreatedAt = info.CreatedAt == default ? DateTime.UtcNow : info.CreatedAt;
        info.LastUpdatedAt ??= info.CreatedAt;
        _sessions[sessionId] = info;
        _outputBuffers.TryAdd(sessionId, new ConcurrentQueue<string>());
        _activityStates.TryAdd(sessionId, new ConcurrentDictionary<string, string>());
        _insightBuffers.TryAdd(sessionId, new ConcurrentQueue<TaskInsightEvent>());
    }

    public void UpdateState(string sessionId, string state)
    {
        var now = DateTime.UtcNow;
        _sessions.AddOrUpdate(sessionId,
            _ => new SessionInfo { Id = sessionId, State = state, CreatedAt = now, LastUpdatedAt = now },
            (_, existing) => Clone(existing, clone =>
            {
                clone.State = state;
                clone.LastUpdatedAt = now;
            }));
    }

    public void UpdateContainer(string sessionId, string? containerId, string? guiUrl = null)
    {
        var now = DateTime.UtcNow;
        _sessions.AddOrUpdate(sessionId,
            _ => new SessionInfo
            {
                Id = sessionId,
                ContainerId = containerId,
                GuiUrl = guiUrl,
                CreatedAt = now,
                LastUpdatedAt = now
            },
            (_, existing) => Clone(existing, clone =>
            {
                clone.ContainerId = containerId;
                clone.GuiUrl = guiUrl ?? clone.GuiUrl;
                clone.LastUpdatedAt = now;
            }));
    }

    public void UpdateCost(string sessionId, decimal totalCostUsd)
    {
        var now = DateTime.UtcNow;
        _sessions.AddOrUpdate(sessionId,
            _ => new SessionInfo { Id = sessionId, TotalCostUsd = totalCostUsd, CreatedAt = now, LastUpdatedAt = now },
            (_, existing) => Clone(existing, clone =>
            {
                clone.TotalCostUsd = totalCostUsd;
                clone.LastUpdatedAt = now;
            }));
    }

    public void AppendOutput(string sessionId, string text)
    {
        var buffer = _outputBuffers.GetOrAdd(sessionId, _ => new ConcurrentQueue<string>());

        buffer.Enqueue(text);

        while (buffer.Count > _maxBufferSize)
            buffer.TryDequeue(out _);

        var now = DateTime.UtcNow;
        _sessions.AddOrUpdate(sessionId,
            _ => new SessionInfo
            {
                Id = sessionId,
                State = "running",
                CreatedAt = now,
                LastOutputAt = now,
                LastUpdatedAt = now
            },
            (_, existing) => Clone(existing, clone =>
            {
                clone.LastOutputAt = now;
                clone.LastUpdatedAt = now;
            }));
    }

    public string[] GetOutput(string sessionId)
    {
        if (_outputBuffers.TryGetValue(sessionId, out var buffer))
            return buffer.ToArray();

        return [];
    }

    public void UpdateActivity(string sessionId, string activityName, string status)
    {
        var activities = _activityStates.GetOrAdd(sessionId, _ => new ConcurrentDictionary<string, string>());
        activities[activityName] = status;

        var now = DateTime.UtcNow;
        _sessions.AddOrUpdate(sessionId,
            _ => new SessionInfo
            {
                Id = sessionId,
                State = "running",
                CreatedAt = now,
                LastActivityAt = now,
                LastActivityName = activityName,
                LastUpdatedAt = now
            },
            (_, existing) => Clone(existing, clone =>
            {
                clone.LastActivityAt = now;
                clone.LastActivityName = activityName;
                clone.LastUpdatedAt = now;
            }));
    }

    public IReadOnlyList<ActivityState> GetActivities(string sessionId)
    {
        if (!_activityStates.TryGetValue(sessionId, out var activities))
            return [];

        return activities
            .Select(x => new ActivityState(x.Key, x.Value))
            .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public void AppendInsight(string sessionId, TaskInsightEvent insight)
    {
        var buffer = _insightBuffers.GetOrAdd(sessionId, _ => new ConcurrentQueue<TaskInsightEvent>());
        buffer.Enqueue(insight);

        while (buffer.Count > MaxInsightBufferSize)
            buffer.TryDequeue(out _);

        var now = insight.TimestampUtc == default ? DateTime.UtcNow : insight.TimestampUtc;
        _sessions.AddOrUpdate(sessionId,
            _ => new SessionInfo { Id = sessionId, State = "running", CreatedAt = now, LastUpdatedAt = now },
            (_, existing) => Clone(existing, clone => clone.LastUpdatedAt = now));
    }

    public void AppendContainerLog(string sessionId, string line)
    {
        var now = DateTime.UtcNow;
        AppendOutput(sessionId, $"[container] {line}{(line.EndsWith('\n') ? "" : "\n")}");

        _sessions.AddOrUpdate(sessionId,
            _ => new SessionInfo
            {
                Id = sessionId,
                State = "running",
                CreatedAt = now,
                LastContainerLogAt = now,
                LastUpdatedAt = now
            },
            (_, existing) => Clone(existing, clone =>
            {
                clone.LastContainerLogAt = now;
                clone.LastUpdatedAt = now;
            }));
    }

    public IReadOnlyList<TaskInsightEvent> GetInsights(string sessionId)
    {
        if (_insightBuffers.TryGetValue(sessionId, out var buffer))
            return buffer.ToArray();

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
        _activityStates.TryRemove(sessionId, out _);
        _insightBuffers.TryRemove(sessionId, out _);
    }

    private static SessionInfo Clone(SessionInfo existing, Action<SessionInfo> mutate)
    {
        var clone = new SessionInfo
        {
            Id = existing.Id,
            WorkflowId = existing.WorkflowId,
            State = existing.State,
            TotalCostUsd = existing.TotalCostUsd,
            Agent = existing.Agent,
            ContainerId = existing.ContainerId,
            GuiUrl = existing.GuiUrl,
            PromptPreview = existing.PromptPreview,
            CreatedAt = existing.CreatedAt,
            LastActivityAt = existing.LastActivityAt,
            LastActivityName = existing.LastActivityName,
            LastOutputAt = existing.LastOutputAt,
            LastContainerLogAt = existing.LastContainerLogAt,
            LastUpdatedAt = existing.LastUpdatedAt,
        };

        mutate(clone);
        return clone;
    }
}

public record ActivityState(string Name, string Status);
