using MagicPAI.Server.Bridge;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Configuration;

namespace MagicPAI.Tests.Server;

public class SessionHistoryReaderTests : IDisposable
{
    private readonly string _dbPath;

    public SessionHistoryReaderTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"magicpai-history-{Guid.NewGuid():N}.db");
    }

    [Fact]
    public async Task GetSessionAsync_ReadsWorkflowStateFromSqlite()
    {
        await using var connection = await CreateOpenConnectionAsync();
        await CreateSchemaAsync(connection);

        await InsertWorkflowInstanceAsync(
            connection,
            id: "session-1",
            definitionId: "FullOrchestrateWorkflow",
            status: "Running",
            subStatus: "Suspended",
            createdAt: new DateTime(2026, 4, 13, 0, 0, 0, DateTimeKind.Utc));

        var reader = CreateReader();

        var session = await reader.GetSessionAsync("session-1", tracked: null, CancellationToken.None);

        Assert.NotNull(session);
        Assert.Equal("session-1", session!.Id);
        Assert.Equal("full-orchestrate", session.WorkflowId);
        Assert.Equal("running", session.State);
    }

    [Fact]
    public async Task GetActivitiesAsync_ReadsRecursiveWorkflowLogStateFromSqlite()
    {
        await using var connection = await CreateOpenConnectionAsync();
        await CreateSchemaAsync(connection);

        await InsertWorkflowInstanceAsync(connection, "root", "FullOrchestrateWorkflow", "Running", "Suspended", DateTime.UtcNow);
        await InsertWorkflowInstanceAsync(connection, "child", "PromptEnhancerWorkflow", "Running", null, DateTime.UtcNow, parentWorkflowInstanceId: "root");

        await InsertWorkflowLogAsync(connection, "root", "spawn-container", "Spawn Container Activity", "MagicPAI.Activities.Docker.SpawnContainerActivity", "Started", 1);
        await InsertWorkflowLogAsync(connection, "root", "spawn-container", "Spawn Container Activity", "MagicPAI.Activities.Docker.SpawnContainerActivity", "Completed", 2);
        await InsertWorkflowLogAsync(connection, "child", "enhance-prompt", "Ai Assistant Activity", "MagicPAI.Activities.AI.RunCliAgentActivity", "Started", 3);

        var reader = CreateReader();

        var activities = await reader.GetActivitiesAsync("root", [], CancellationToken.None);

        Assert.Collection(
            activities.OrderBy(x => x.Name, StringComparer.Ordinal),
            activity =>
            {
                Assert.Equal("enhance-prompt", activity.Name);
                Assert.Equal("running", activity.Status);
            },
            activity =>
            {
                Assert.Equal("spawn-container", activity.Name);
                Assert.Equal("completed", activity.Status);
            });
    }

    [Fact]
    public async Task GetOutputAsync_ReadsOutputChunksFromSqlite()
    {
        await using var connection = await CreateOpenConnectionAsync();
        await CreateSchemaAsync(connection);

        await InsertWorkflowInstanceAsync(connection, "root", "FullOrchestrateWorkflow", "Running", null, DateTime.UtcNow);
        await InsertWorkflowLogAsync(connection, "root", "elaborate", "Ai Assistant Activity", "MagicPAI.Activities.AI.RunCliAgentActivity", "OutputChunk", 1, "{\"text\":\"first chunk\"}");
        await InsertWorkflowLogAsync(connection, "root", "elaborate", "Ai Assistant Activity", "MagicPAI.Activities.AI.RunCliAgentActivity", "StreamChunk", 2, "{\"text\":\"second chunk\"}");

        var reader = CreateReader();

        var output = await reader.GetOutputAsync("root", [], CancellationToken.None);

        Assert.Equal(["first chunk", "second chunk"], output);
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_dbPath))
                File.Delete(_dbPath);
        }
        catch (IOException)
        {
            // SQLite can hold a short-lived file handle after the assertion path completes.
        }
    }

    private SessionHistoryReader CreateReader()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:MagicPai"] = $"Data Source={_dbPath}"
            })
            .Build();

        return new SessionHistoryReader(configuration);
    }

    private async Task<SqliteConnection> CreateOpenConnectionAsync()
    {
        var connection = new SqliteConnection($"Data Source={_dbPath}");
        await connection.OpenAsync();
        return connection;
    }

    private static async Task CreateSchemaAsync(SqliteConnection connection)
    {
        var sql = """
            CREATE TABLE "WorkflowInstances" (
                "Id" TEXT PRIMARY KEY,
                "DefinitionId" TEXT NOT NULL,
                "Status" TEXT NOT NULL,
                "SubStatus" TEXT NULL,
                "CreatedAt" TEXT NOT NULL,
                "ParentWorkflowInstanceId" TEXT NULL
            );

            CREATE TABLE "WorkflowExecutionLogRecords" (
                "WorkflowInstanceId" TEXT NOT NULL,
                "ActivityId" TEXT NULL,
                "ActivityName" TEXT NULL,
                "ActivityType" TEXT NULL,
                "EventName" TEXT NOT NULL,
                "Message" TEXT NULL,
                "Timestamp" TEXT NOT NULL,
                "Sequence" INTEGER NOT NULL
            );
            """;

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertWorkflowInstanceAsync(
        SqliteConnection connection,
        string id,
        string definitionId,
        string status,
        string? subStatus,
        DateTime createdAt,
        string? parentWorkflowInstanceId = null)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO "WorkflowInstances" ("Id", "DefinitionId", "Status", "SubStatus", "CreatedAt", "ParentWorkflowInstanceId")
            VALUES (@id, @definitionId, @status, @subStatus, @createdAt, @parentWorkflowInstanceId);
            """;
        command.Parameters.AddWithValue("@id", id);
        command.Parameters.AddWithValue("@definitionId", definitionId);
        command.Parameters.AddWithValue("@status", status);
        command.Parameters.AddWithValue("@subStatus", (object?)subStatus ?? DBNull.Value);
        command.Parameters.AddWithValue("@createdAt", createdAt);
        command.Parameters.AddWithValue("@parentWorkflowInstanceId", (object?)parentWorkflowInstanceId ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task InsertWorkflowLogAsync(
        SqliteConnection connection,
        string workflowInstanceId,
        string activityId,
        string activityName,
        string activityType,
        string eventName,
        long sequence,
        string? message = null)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO "WorkflowExecutionLogRecords"
                ("WorkflowInstanceId", "ActivityId", "ActivityName", "ActivityType", "EventName", "Message", "Timestamp", "Sequence")
            VALUES
                (@workflowInstanceId, @activityId, @activityName, @activityType, @eventName, @message, @timestamp, @sequence);
            """;
        command.Parameters.AddWithValue("@workflowInstanceId", workflowInstanceId);
        command.Parameters.AddWithValue("@activityId", activityId);
        command.Parameters.AddWithValue("@activityName", activityName);
        command.Parameters.AddWithValue("@activityType", activityType);
        command.Parameters.AddWithValue("@eventName", eventName);
        command.Parameters.AddWithValue("@message", (object?)message ?? DBNull.Value);
        command.Parameters.AddWithValue("@timestamp", DateTime.UtcNow);
        command.Parameters.AddWithValue("@sequence", sequence);
        await command.ExecuteNonQueryAsync();
    }
}
