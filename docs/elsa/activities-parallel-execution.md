# Parallel Execution in Elsa Workflows

## Key Mechanisms

Elsa v3 provides three primary approaches for concurrent task execution:

1. **Parallel Activity** - Executes multiple branches simultaneously, waiting for all to complete
2. **ForEach with Parallel Mode** - Processes collection items concurrently by setting `Mode = ForEachMode.Parallel`
3. **Flowchart Design** - Multiple connections from a single activity create parallel branches visually

## Critical Considerations

### Race Conditions with Shared Variables

The documentation warns that "when multiple branches access the same workflow variable concurrently, race conditions can occur." Rather than incrementing a shared counter in parallel branches (which produces incorrect results), the recommended approach involves either using separate variables and combining results afterward, or leveraging collections with proper aggregation.

### Error Handling

By default, if one branch encounters a fault, the workflow enters a faulted state, though other branches may continue running. The guide suggests wrapping risky operations and configuring incident handling strategies for fault tolerance.

### Resource Management

Considerations include thread pool exhaustion when running excessive parallel branches, potential memory overhead from execution contexts, and ensuring external resources can handle concurrent requests without hitting rate limits.

## Best Practices Summary

Parallel execution works best for independent operations, I/O-bound tasks, and fan-out/fan-in patterns. Conversely, avoid it for sequential operations, CPU-intensive work exceeding available cores, and operations sharing mutable state without synchronization mechanisms.
