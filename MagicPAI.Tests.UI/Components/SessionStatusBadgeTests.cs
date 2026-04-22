// MagicPAI.Tests.UI/Components/SessionStatusBadgeTests.cs
// Post-Temporal-migration smoke tests for the MudBlazor-based Studio.
// Full component coverage was wiped with the Elsa-era custom components; these
// tests exercise the few stand-alone MudBlazor wrappers without requiring a
// full MudServices registration (which isn't trivial in bUnit).
using Bunit;
using MagicPAI.Studio.Components;
using MudBlazor;
using MudBlazor.Services;

namespace MagicPAI.Tests.UI.Components;

public class SessionStatusBadgeTests : TestContext
{
    public SessionStatusBadgeTests()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void RunningStatus_RendersBadge()
    {
        var cut = RenderComponent<SessionStatusBadge>(parameters => parameters
            .Add(p => p.Status, "Running"));

        Assert.Contains("Running", cut.Markup);
    }

    [Fact]
    public void CompletedStatus_RendersBadge()
    {
        var cut = RenderComponent<SessionStatusBadge>(parameters => parameters
            .Add(p => p.Status, "Completed"));

        Assert.Contains("Completed", cut.Markup);
    }

    [Fact]
    public void FailedStatus_RendersBadge()
    {
        var cut = RenderComponent<SessionStatusBadge>(parameters => parameters
            .Add(p => p.Status, "Failed"));

        Assert.Contains("Failed", cut.Markup);
    }
}

public class PipelineStageChipTests : TestContext
{
    public PipelineStageChipTests()
    {
        Services.AddMudServices();
        JSInterop.Mode = JSRuntimeMode.Loose;
    }

    [Fact]
    public void InitializingStage_Renders()
    {
        var cut = RenderComponent<PipelineStageChip>(parameters => parameters
            .Add(p => p.Stage, "initializing"));

        Assert.Contains("initializing", cut.Markup);
    }

    [Fact]
    public void UnknownStage_FallsBackToDefaultIcon()
    {
        var cut = RenderComponent<PipelineStageChip>(parameters => parameters
            .Add(p => p.Stage, "custom-stage"));

        Assert.Contains("custom-stage", cut.Markup);
    }
}
