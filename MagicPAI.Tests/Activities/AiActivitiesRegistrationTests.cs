using System.Reflection;
using FluentAssertions;
using MagicPAI.Activities.AI;
using Temporalio.Activities;

namespace MagicPAI.Tests.Activities;

/// <summary>
/// Registration sanity checks: confirm the expected [Activity] methods are present on
/// <see cref="AiActivities"/>. The Temporal hosted-worker scans the class for these
/// attributes at startup; adding a method without the attribute silently drops it.
/// </summary>
[Trait("Category", "Unit")]
public class AiActivitiesRegistrationTests
{
    [Theory]
    [InlineData(nameof(AiActivities.RunCliAgentAsync))]
    [InlineData(nameof(AiActivities.TriageAsync))]
    [InlineData(nameof(AiActivities.ClassifyAsync))]
    [InlineData(nameof(AiActivities.RouteModelAsync))]
    [InlineData(nameof(AiActivities.EnhancePromptAsync))]
    [InlineData(nameof(AiActivities.ArchitectAsync))]
    [InlineData(nameof(AiActivities.ResearchPromptAsync))]
    [InlineData(nameof(AiActivities.ClassifyWebsiteTaskAsync))]
    [InlineData(nameof(AiActivities.GradeCoverageAsync))]
    public void Method_IsDecoratedWithActivityAttribute(string methodName)
    {
        var method = typeof(AiActivities).GetMethod(methodName,
            BindingFlags.Public | BindingFlags.Instance);
        method.Should().NotBeNull($"{methodName} must be a public instance method");
        method!.GetCustomAttribute<ActivityAttribute>()
               .Should()
               .NotBeNull($"{methodName} must carry [Temporalio.Activities.Activity] " +
                          "so the hosted worker auto-registers it");
    }

    [Fact]
    public void Class_Exposes_Expected_Activity_Count()
    {
        var activityMethods = typeof(AiActivities)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetCustomAttribute<ActivityAttribute>() is not null)
            .Select(m => m.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        // Phase 2 Day 5 has all 9 planned AiActivities methods:
        // RunCliAgent, Triage, Classify, RouteModel, EnhancePrompt,
        // Architect, ResearchPrompt, ClassifyWebsiteTask, GradeCoverage.
        activityMethods.Should().BeEquivalentTo(new[]
        {
            nameof(AiActivities.ArchitectAsync),
            nameof(AiActivities.ClassifyAsync),
            nameof(AiActivities.ClassifyWebsiteTaskAsync),
            nameof(AiActivities.EnhancePromptAsync),
            nameof(AiActivities.GradeCoverageAsync),
            nameof(AiActivities.ResearchPromptAsync),
            nameof(AiActivities.RouteModelAsync),
            nameof(AiActivities.RunCliAgentAsync),
            nameof(AiActivities.TriageAsync),
        });
    }
}
