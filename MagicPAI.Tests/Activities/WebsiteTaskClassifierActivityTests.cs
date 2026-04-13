using System.Reflection;
using MagicPAI.Activities.AI;
using MagicPAI.Core.Models;

namespace MagicPAI.Tests.Activities;

public class WebsiteTaskClassifierActivityTests
{
    private static readonly MethodInfo TryHeuristicClassificationMethod =
        typeof(WebsiteTaskClassifierActivity).GetMethod(
            "TryHeuristicClassification",
            BindingFlags.NonPublic | BindingFlags.Static)
        ?? throw new InvalidOperationException("Could not find website-task heuristic classifier.");

    [Theory]
    [InlineData("yochange the entire css of ramathayal770", true)]
    [InlineData("Fix the UI and fonts on the donation page so it looks different on mobile", true)]
    [InlineData("Review the website layout and responsive behavior in the browser", true)]
    [InlineData("Add an index to the orders table and optimize the API endpoint", false)]
    [InlineData("Refactor the background worker retry policy", false)]
    public void HeuristicClassification_Matches_BrowserVisibleWebsiteSignals(string prompt, bool expectedWebsiteTask)
    {
        var result = (WebsiteTaskClassificationResult?)TryHeuristicClassificationMethod.Invoke(null, [prompt]);

        if (expectedWebsiteTask)
        {
            Assert.NotNull(result);
            Assert.True(result!.IsWebsiteTask);
            Assert.True(result.Confidence >= 9);
            Assert.Contains("Heuristic website routing override", result.Rationale, StringComparison.Ordinal);
        }
        else
        {
            Assert.Null(result);
        }
    }
}
