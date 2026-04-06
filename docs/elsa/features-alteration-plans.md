# Alteration Plans

An alteration plan represents a collection of alterations that can be applied to a workflow instance or a set of workflow instances.

### Creating Alteration Plans

To create an alteration plan, instantiate the `NewAlterationPlan` class with a list of alterations and target workflow instance IDs:

```csharp
var plan = new NewAlterationPlan
{
    Alterations = new List<IAlteration>
    {
        new ModifyVariable("MyVariable", "MyValue")
    },
    WorkflowInstanceIds = new[] { "26cf02e60d4a4be7b99a8588b7ac3bb9" } 
};
```

### Submitting Alteration Plans

Submit your plan using the `IAlterationPlanScheduler` service:

```csharp
var scheduler = serviceProvider.GetRequiredService<IAlterationPlanScheduler>();
var planId = await scheduler.SubmitAsync(plan, cancellationToken);
```

When submitted, "an alteration job is created for each workflow instance, to which each alteration will be applied." Plans execute asynchronously in the background.

**Monitoring Execution:**

Use `IAlterationPlanStore` to retrieve plan status:

```csharp
var store = serviceProvider.GetRequiredService<IAlterationPlanStore>();
var plan = await _alterationPlanStore.FindAsync(new AlterationPlanFilter { Id = planId }, cancellationToken);
```

Access the associated alteration jobs via `IAlterationJobStore`:

```csharp
var store = serviceProvider.GetRequiredService<IAlterationJobStore>();
var jobs = (await _alterationJobStore.FindManyAsync(new AlterationJobFilter { PlanId = planId }, cancellationToken)).ToList();
```
