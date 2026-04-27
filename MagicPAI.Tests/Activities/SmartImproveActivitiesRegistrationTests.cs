using System.Reflection;
using FluentAssertions;
using MagicPAI.Activities.SmartImprove;
using Temporalio.Activities;

namespace MagicPAI.Tests.Activities;

/// <summary>
/// Registration sanity check for <see cref="SmartImproveActivities"/>.
/// The Temporal hosted-worker scans the class for [Activity] attributes at
/// startup; adding a method without the attribute silently drops it. This
/// test asserts the exact set of expected activities so accidental drops
/// or unintended additions surface during the build.
/// </summary>
[Trait("Category", "Unit")]
public class SmartImproveActivitiesRegistrationTests
{
    [Theory]
    [InlineData(nameof(SmartImproveActivities.SnapshotFilesystemAsync))]
    [InlineData(nameof(SmartImproveActivities.ComputeAstHashAsync))]
    [InlineData(nameof(SmartImproveActivities.GetGitStateAsync))]
    [InlineData(nameof(SmartImproveActivities.VerifyHarnessAsync))]
    public void Method_IsDecoratedWithActivityAttribute(string methodName)
    {
        var method = typeof(SmartImproveActivities).GetMethod(methodName,
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
        var activityMethods = typeof(SmartImproveActivities)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetCustomAttribute<ActivityAttribute>() is not null)
            .Select(m => m.Name)
            .OrderBy(n => n, StringComparer.Ordinal)
            .ToArray();

        // Locked-in set per newplan.md §2.2. If you add a new SmartImprove
        // activity, update this list intentionally; if this fails on an
        // unexpected addition, the change wasn't reviewed.
        activityMethods.Should().BeEquivalentTo(new[]
        {
            nameof(SmartImproveActivities.ComputeAstHashAsync),
            nameof(SmartImproveActivities.GetGitStateAsync),
            nameof(SmartImproveActivities.SnapshotFilesystemAsync),
            nameof(SmartImproveActivities.VerifyHarnessAsync),
        });
    }
}
