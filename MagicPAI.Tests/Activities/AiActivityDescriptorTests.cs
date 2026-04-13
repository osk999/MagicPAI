using System.Reflection;
using Elsa.Workflows;
using Elsa.Workflows.Attributes;
using MagicPAI.Activities.AI;

namespace MagicPAI.Tests.Activities;

public class AiActivityDescriptorTests
{
    [Theory]
    [InlineData(typeof(RunCliAgentActivity))]
    [InlineData(typeof(AiAssistantActivity))]
    [InlineData(typeof(TriageActivity))]
    [InlineData(typeof(WebsiteTaskClassifierActivity))]
    [InlineData(typeof(ArchitectActivity))]
    [InlineData(typeof(PromptEnhancementActivity))]
    [InlineData(typeof(ResearchPromptActivity))]
    public void LongRunningAiActivities_Run_As_BackgroundTasks(Type activityType)
    {
        var attribute = activityType.GetCustomAttribute<ActivityAttribute>();

        Assert.NotNull(attribute);
        Assert.Equal(ActivityKind.Task, attribute!.Kind);
        Assert.True(attribute.RunAsynchronously);
    }
}
