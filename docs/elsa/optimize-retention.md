# Retention

## Configuration

To activate the retention module, enable it with specific settings:

```csharp
elsa.UseRetention(r =>
{
    r.SweepInterval = TimeSpan.FromMinutes(30);
    r.AddDeletePolicy("Delete all finished workflows", _ => new RetentionWorkflowInstanceFilter()
    {
        WorkflowStatus = WorkflowStatus.Finished
    });
});
```

The `SweepInterval` establishes "how often the retention feature will check for workflows that match any of the configured policies." By default, the retention module includes an `AddDeletePolicy` method for removing matching workflow instances.

The policy accepts a function returning a `RetentionWorkflowInstanceFilter`, which gets invoked during each sweep cycle.

## Example

To remove a workflow after it finishes for over an hour:

```csharp
elsa.UseRetention(r =>
{
    r.SweepInterval = TimeSpan.FromSeconds(30);
    r.AddDeletePolicy("Delete all finished workflows", sp =>
    {
        ISystemClock clock = sp.GetRequiredService<ISystemClock>();
        DateTimeOffset threshold = clock.UtcNow.Subtract(TimeSpan.FromHours(1));
        
        return new RetentionWorkflowInstanceFilter()
        {
            TimestampFilters =
            [
                new TimestampFilter()
                {
                    Column = nameof(WorkflowInstance.FinishedAt),
                    Operator = TimestampFilterOperator.LessThanOrEqual,
                    Timestamp = threshold
                }
            ],
            WorkflowStatus = WorkflowStatus.Finished
        };
    });
});
```

This example uses Elsa's `ISystemClock` to obtain current time and identify instances finishing over an hour prior. Since filters rebuild each sweep interval, `clock.UtcNow` dynamically calculates the threshold.

## Extending

The retention module supports extending functionality by adding new policy types or including additional entities in existing policies.

### Extra Entities

If you maintain custom `WorkflowInstanceData` tied to each workflow instance requiring removal:

#### **Entity Collector**

Establish an `IRelatedEntityCollector<TEntity>` that retrieves associated `WorkflowInstanceData` records from workflow instances:

```csharp
public class WorkflowInstanceDataRecordCollector(WorkflowInstanceDataDbContext store) : IRelatedEntityCollector<WorkflowInstanceData>
{
    public async IAsyncEnumerable<ICollection<WorkflowInstanceData>> GetRelatedEntities(ICollection<WorkflowInstance> workflowInstances)
    {
        // TODO: Get WorkflowInstanceData for the given workflowInstances
    }
}
```

#### **Cleanup Strategy**

Create a cleanup strategy for supporting policies. Here's an implementation for deleting `WorkflowInstanceData`:

```csharp
public class DeleteWorkflowInstanceDataRecordStrategy(WorkflowInstanceDataDbContext store, ILogger<DeleteWorkflowInstanceDataRecordStrategy> logger) : IDeletionCleanupStrategy<WorkflowInstanceData>
{
    public async Task Cleanup(ICollection<WorkflowInstanceData> collection)
    {
        // TODO: Delete WorkflowInstanceData
    }
}
```

#### **Register Dependencies**

Register both components in the dependency container:

```csharp
Services.AddScoped<IDeletionCleanupStrategy<WorkflowInstanceData>, DeleteWorkflowInstanceDataRecordStrategy>();
Services.AddScoped<IRelatedEntityCollector<WorkflowInstanceData>, WorkflowInstanceDataRecordCollector>();
```

### Different Cleanup Strategies

Extend retention functionality by implementing alternate cleanup strategies. For instance, you could archive workflow instances to alternative storage systems.

#### **Defining a Marker Interface**

Establish a marker interface for archiving strategies:

```csharp
public interface IArchivingStrategy<TEntity> : ICleanupStrategy<TEntity>
{
}
```

#### **Defining the Policy**

Develop a policy structure:

```csharp
/// <summary>
/// A policy that archives the workflow instance and its related entities.
/// </summary>
public class ArchivingRetentionPolicy(string name, Func<IServiceProvider, RetentionWorkflowInstanceFilter> filter) : IRetentionPolicy
{
    public string Name { get; } = name;
    public Func<IServiceProvider, RetentionWorkflowInstanceFilter> FilterFactory { get; } = filter;

    public Type CleanupStrategy => typeof(IArchivingStrategy<>);
}
```

**Note:** "In the `CleanupStrategy` property, we specify our marker interface (`IArchivingStrategy`). This allows the retention module to scan for all implementations of `IArchivingStrategy`" during execution.

#### **Implementing the Strategy**

Implement `IArchivingStrategy` for each entity requiring archival. Rather than deleting, transfer entities to alternate storage providers.

When implementing a new policy, supply an `ICleanupStrategy<TEntity>` for these entities:

* `ActivityExecutionRecord`
* `StoredBookmark`
* `WorkflowExecutionLogRecord`
* `WorkflowInstance`
