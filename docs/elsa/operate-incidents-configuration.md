# Configuration

The workflow engine's handling of incidents can be customized through Incident Strategies.

## Global

The default approach is `FaultStrategy`, though this can be modified by adjusting the `IncidentStrategy` property within `WorkflowOptions`:

```csharp
services.Configure<IncidentOptions>(options =>
{
    options.DefaultIncidentStrategy = typeof(ContinueWithIncidentsStrategy);
});
```

"The default strategy will be used for all workflows that do not have a strategy configured explicitly."

## Workflow Specific

Individual workflows can override the incident strategy by setting the `IncidentStrategyType` property:

```csharp
public class MyWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        builder.WorkflowOptions.IncidentStrategyType = typeof(ContinueWithIncidentsStrategy);
    }
}
```

Configuration is also available through Elsa Studio's user interface, which provides a visual method for adjusting these incident settings within workflow definitions.
