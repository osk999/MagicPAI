using MagicPAI.Activities.AI;

namespace MagicPAI.Tests.Activities;

public class AssistantSessionStateTests
{
    [Fact]
    public void CreateSessionMapKey_Includes_ActivityId_When_Present()
    {
        var key = AssistantSessionState.CreateSessionMapKey("claude", "phase1-discovery-runner");

        Assert.Equal("claude::phase1-discovery-runner", key);
    }

    [Fact]
    public void CreateSessionMapKey_Falls_Back_To_Assistant_When_ActivityId_Missing()
    {
        var key = AssistantSessionState.CreateSessionMapKey("claude", "");

        Assert.Equal("claude", key);
    }
}
