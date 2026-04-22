using MagicPAI.Server.Bridge;
using Microsoft.Extensions.Logging.Abstractions;

namespace MagicPAI.Tests.Server;

/// <summary>
/// Sanity tests for the Temporal-backed <see cref="SessionHistoryReader"/>.
/// The Elsa-SQL-schema test suite from Phase 1 was removed here: the new
/// reader queries Temporal's visibility store, which needs a live Temporal
/// server. Integration coverage lives in MagicPAI.Tests.Integration.
/// </summary>
[Trait("Category", "Unit")]
public class SessionHistoryReaderTests
{
    [Fact]
    public void Constructor_AcceptsNullOrValidClient()
    {
        // The reader defers work to enumeration time; building it should not throw.
        var ctor = typeof(SessionHistoryReader).GetConstructors();
        Assert.Single(ctor);
        var parameters = ctor[0].GetParameters();
        Assert.Collection(parameters,
            p => Assert.Equal("temporal", p.Name),
            p => Assert.Equal("log", p.Name));
    }

    [Fact]
    public void SessionSummary_Record_CarriesAllFields()
    {
        var summary = new SessionSummary(
            SessionId: "mpai-1",
            WorkflowType: "SimpleAgent",
            Status: "Running",
            StartTime: new DateTime(2026, 4, 20, 0, 0, 0, DateTimeKind.Utc),
            CloseTime: null,
            AiAssistant: "claude",
            TotalCostUsd: 0m);

        Assert.Equal("mpai-1", summary.SessionId);
        Assert.Equal("SimpleAgent", summary.WorkflowType);
        Assert.Equal("Running", summary.Status);
        Assert.Null(summary.CloseTime);
        Assert.Equal("claude", summary.AiAssistant);
        Assert.Equal(0m, summary.TotalCostUsd);
    }

    [Fact]
    public void WorkflowHistoryEventSummary_Record_CarriesAllFields()
    {
        var now = DateTime.UtcNow;
        var summary = new WorkflowHistoryEventSummary(
            EventId: 42,
            EventType: "ActivityScheduled",
            EventTime: now,
            Attributes: "TestAttrs");

        Assert.Equal(42, summary.EventId);
        Assert.Equal("ActivityScheduled", summary.EventType);
        Assert.Equal(now, summary.EventTime);
        Assert.Equal("TestAttrs", summary.Attributes);
    }
}
