using Elsa.Studio.Contracts;

namespace MagicPAI.Studio.Services;

/// <summary>
/// Registers MagicPAI.Studio as an Elsa Studio feature/module.
/// This ensures Shell.App includes our assembly in AdditionalAssemblies
/// so our @page routes (Dashboard, Sessions, Costs, Settings) are discovered.
/// </summary>
public class MagicPaiFeature : IFeature
{
    public ValueTask InitializeAsync(CancellationToken cancellationToken = default) =>
        ValueTask.CompletedTask;
}
