using System.Text.Json;
using MagicPAI.Core.Models;
using Npgsql;

namespace MagicPAI.Server.Bridge;

public class SessionHistoryReader
{
    private readonly string? _pgConn;

    public SessionHistoryReader(IConfiguration configuration)
    {
        _pgConn = configuration.GetConnectionString("MagicPai");
    }

    public async Task<SessionInfo?> GetSessionAsync(string sessionId, SessionInfo? tracked, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_pgConn))
            return tracked;

        try
        {
            await using var conn = new NpgsqlConnection(_pgConn);
            await conn.OpenAsync(ct);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                SELECT "DefinitionId", "Status", "SubStatus", "CreatedAt"
                FROM "Elsa"."WorkflowInstances"
                WHERE "Id" = @id
                LIMIT 1;
                """;
            cmd.Parameters.AddWithValue("id", sessionId);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            if (!await reader.ReadAsync(ct))
                return tracked;

            var session = tracked ?? new SessionInfo { Id = sessionId };
            session.WorkflowId ??= FriendlyWorkflowName(reader.GetString(0));
            session.State = MapState(reader.GetString(1), reader.IsDBNull(2) ? null : reader.GetString(2));
            if (session.CreatedAt == default)
                session.CreatedAt = reader.GetFieldValue<DateTime>(3);
            return session;
        }
        catch
        {
            // Schema may not exist yet — fall back to in-memory tracker data
            return tracked;
        }
    }

    public async Task<IReadOnlyList<ActivityState>> GetActivitiesAsync(
        string sessionId,
        IReadOnlyList<ActivityState> tracked,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_pgConn))
            return tracked;

        try
        {
            var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var order = new List<string>();

            foreach (var activity in tracked)
            {
                if (merged.ContainsKey(activity.Name))
                    continue;

                merged[activity.Name] = activity.Status;
                order.Add(activity.Name);
            }

            await using var conn = new NpgsqlConnection(_pgConn);
            await conn.OpenAsync(ct);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                WITH RECURSIVE workflow_tree AS (
                    SELECT "Id"
                    FROM "Elsa"."WorkflowInstances"
                    WHERE "Id" = @id
                    UNION ALL
                    SELECT child."Id"
                    FROM "Elsa"."WorkflowInstances" child
                    INNER JOIN workflow_tree parent ON child."ParentWorkflowInstanceId" = parent."Id"
                )
                SELECT logs."ActivityId", logs."ActivityName", logs."ActivityType", logs."EventName", logs."Timestamp", logs."Sequence"
                FROM "Elsa"."WorkflowExecutionLogRecords" logs
                INNER JOIN workflow_tree tree ON tree."Id" = logs."WorkflowInstanceId"
                ORDER BY "Timestamp" ASC, "Sequence" ASC;
                """;
            cmd.Parameters.AddWithValue("id", sessionId);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                var activityId = reader.IsDBNull(0) ? "" : reader.GetString(0);
                var activityName = reader.IsDBNull(1) ? "" : reader.GetString(1);
                var activityType = reader.IsDBNull(2) ? "" : reader.GetString(2);
                var eventName = reader.IsDBNull(3) ? "" : reader.GetString(3);

                var name = !string.IsNullOrWhiteSpace(activityId)
                    ? activityId
                    : !string.IsNullOrWhiteSpace(activityName)
                        ? activityName
                        : activityType;
                var status = eventName switch
                {
                    "Started" => "running",
                    "Completed" => "completed",
                    "Faulted" => "failed",
                    _ => null
                };

                if (!string.IsNullOrWhiteSpace(name) && status is not null)
                {
                    if (!merged.ContainsKey(name))
                        order.Add(name);

                    merged[name] = status;
                }
            }

            return order
                .Select(name => new ActivityState(name, merged[name]))
                .ToList();
        }
        catch
        {
            return tracked;
        }
    }

    public async Task<string[]> GetOutputAsync(string sessionId, string[] tracked, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_pgConn))
            return tracked;

        try
        {
            var chunks = new List<string>();

            await using var conn = new NpgsqlConnection(_pgConn);
            await conn.OpenAsync(ct);

            await using var cmd = conn.CreateCommand();
            cmd.CommandText = """
                WITH RECURSIVE workflow_tree AS (
                    SELECT "Id"
                    FROM "Elsa"."WorkflowInstances"
                    WHERE "Id" = @id
                    UNION ALL
                    SELECT child."Id"
                    FROM "Elsa"."WorkflowInstances" child
                    INNER JOIN workflow_tree parent ON child."ParentWorkflowInstanceId" = parent."Id"
                )
                SELECT logs."Message"
                FROM "Elsa"."WorkflowExecutionLogRecords" logs
                INNER JOIN workflow_tree tree ON tree."Id" = logs."WorkflowInstanceId"
                WHERE logs."EventName" IN ('OutputChunk', 'StreamChunk')
                ORDER BY "Timestamp" ASC, "Sequence" ASC;
                """;
            cmd.Parameters.AddWithValue("id", sessionId);

            await using var reader = await cmd.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
            {
                if (reader.IsDBNull(0))
                    continue;

                var message = reader.GetString(0);
                chunks.Add(ExtractOutputText(message));
            }

            if (chunks.Count == 0)
                return tracked;

            if (tracked.Length == 0)
                return chunks.ToArray();

            var merged = new List<string>(chunks);
            foreach (var trackedChunk in tracked)
            {
                if (!merged.Contains(trackedChunk, StringComparer.Ordinal))
                    merged.Add(trackedChunk);
            }

            return merged.ToArray();
        }
        catch
        {
            return tracked;
        }
    }

    private static string ExtractOutputText(string message)
    {
        try
        {
            using var doc = JsonDocument.Parse(message);
            if (doc.RootElement.TryGetProperty("text", out var text))
                return text.GetString() ?? "";
        }
        catch (JsonException)
        {
        }

        return message;
    }

    private static string MapState(string status, string? subStatus) => status switch
    {
        "Running" => "running",
        "Finished" when string.Equals(subStatus, "Finished", StringComparison.OrdinalIgnoreCase) => "completed",
        "Finished" when string.Equals(subStatus, "Faulted", StringComparison.OrdinalIgnoreCase) => "failed",
        "Finished" when string.Equals(subStatus, "Cancelled", StringComparison.OrdinalIgnoreCase) => "cancelled",
        "Finished" => "completed",
        _ => "unknown"
    };

    private static string FriendlyWorkflowName(string definitionId)
    {
        if (definitionId.EndsWith("Workflow", StringComparison.Ordinal))
            definitionId = definitionId[..^"Workflow".Length];

        return string.Concat(definitionId.Select((c, i) =>
            i > 0 && char.IsUpper(c) ? "-" + char.ToLowerInvariant(c) : char.ToLowerInvariant(c).ToString()));
    }
}
