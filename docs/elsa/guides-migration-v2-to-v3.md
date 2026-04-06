# V2 to V3 Migration Guide - Complete Summary

## Overview and Key Changes

The Elsa Workflows V3 represents a complete architectural rewrite rather than an incremental update. As the guide states, "There is no automated migration path from V2 to V3," requiring manual recreation of workflows and custom activities.

## Primary Migration Strategies

Organizations can pursue three distinct approaches:

1. **Parallel Operation** - Run both versions simultaneously while transitioning new workflows to V3
2. **Incremental Migration** - Phase workflows gradually, beginning with simpler implementations
3. **Fresh Start** - Rebuild from scratch for smaller deployments

## Critical Breaking Changes

### Package Structure Transformation
V2's monolithic package approach (`Elsa.Core`, `Elsa.Server.Api`) has shifted to modular packages in V3 (`Elsa.Workflows.Core`, `Elsa.Workflows.Management`, `Elsa.Workflows.Runtime`). The platform now requires ".NET 8+ (no longer supports .NET Standard 2.0)."

### Namespace Reorganization
Significant namespace changes affect all imports:
- `Elsa.Activities` becomes `Elsa.Workflows.Activities`
- `Elsa.Services` maps to `Elsa.Workflows.Core` or `Elsa.Workflows.Runtime`
- `Elsa.Models` shifts to `Elsa.Workflows.Models`

### Configuration Architecture
V3 implements separate configuration paths for management and runtime operations, replacing V2's unified approach. Configuration now requires explicit middleware registration and more granular feature control.

## Custom Activity Development

### Core Implementation Differences

V2 activities inherited from a base `Activity` class and returned `IActivityExecutionResult` objects. V3 introduces an `Input<T>` and `Output<T>` wrapper pattern:

**V2 Input Pattern:**
```csharp
[ActivityInput(Label = "Message")]
public string Message { get; set; }
```

**V3 Input Pattern:**
```csharp
[Input(Description = "The message to print.")]
public Input<string> Message { get; set; }
```

Accessing values requires method invocation: `Message.Get(context)` instead of direct property access.

### Completion and Resumption
V2 used explicit return statements (`Done()`, `Suspend()`). V3 delegates completion responsibility:
- `CodeActivity` auto-completes after execution
- `Activity` subclasses require explicit `await context.CompleteActivityAsync()` calls
- Blocking activities now use bookmarks: `context.CreateBookmark(payload)`

### Async Activity Adjustments
Method naming shifted from `OnExecuteAsync` to `ExecuteAsync`, and service dependencies must use location patterns (`context.GetRequiredService<T>()`) rather than constructor injection.

## Workflow JSON Schema Migration

The most substantial structural change involves JSON representation:

### Root Container Requirement
V3 necessitates wrapping activities in a root object:
```json
{
  "root": {
    "type": "Elsa.Flowchart",
    "activities": []
  }
}
```

### Activity Type Qualification
Activity types require full namespace qualification: `"WriteLine"` becomes `"Elsa.WriteLine"`

### Property Structure Transformation
Properties shifted from array-based collections to direct object properties with explicit type information:

**V2 approach:**
```json
"properties": [{"name": "Text", "expressions": {"Literal": "Hello"}}]
```

**V3 approach:**
```json
"text": {
  "typeName": "String",
  "expression": {"type": "Literal", "value": "Hello"}
}
```

### Connection Redesign
Connection definitions evolved from simple identifiers to nested source/target structures specifying activity and port information.

## Programmatic Workflow Construction

V2 utilized fluent builder patterns with `StartWith`/`Then` methods. V3 employs direct activity composition:

```csharp
builder.Root = new Sequence
{
    Activities = {
        new WriteLine("Hello World!"),
        new WriteLine("Goodbye!")
    }
};
```

## Database and Persistence Evolution

V3 fundamentally alters database schemas with incompatible structures and serialization formats. The guide explicitly recommends: "Use separate databases for V2 and V3" and "Run V2 and V3 in parallel until V2 workflows complete."

Persistence configuration now separates management and runtime concerns, requiring distinct EntityFrameworkCore setup for each concern.

## Built-in Background Scheduling

A significant improvement involves removing Hangfire dependency for standard scenarios. V3 includes native background activity scheduling using .NET Channels, with activities marked via `ActivityKind.Job`.

## Common Pitfalls and Solutions

The guide identifies ten frequent migration mistakes:

1. **Direct JSON Import** - V2 JSON cannot transfer directly; structural transformation is mandatory
2. **Assuming API Compatibility** - Complete rewrites necessary for extensions
3. **Database Migration** - Not feasible; parallel operation recommended
4. **Constructor Injection** - Service location replaces dependency injection
5. **Missing Completion Calls** - Activities must explicitly complete unless using CodeActivity
6. **Direct Input/Output Access** - Properties require `.Get(context)` and `.Set(context, value)` methods
7. **Bookmark Resumption** - V3 eliminates separate resume methods
8. **Root Container Omission** - JSON parsing fails without proper structure
9. **Mixed Package Versions** - All packages must use consistent version numbers
10. **Trigger Implementation** - Must check `IsTriggerOfWorkflow()` status

## Testing and Validation Approach

Comprehensive testing should progress through three phases: unit testing custom activities, integration testing workflows, and validating JSON workflow loading. The guide provides code examples for each testing tier.

## Timeline Expectations

A realistic migration follows a 13-week phased approach: preparation (weeks 1-2), custom activity rewriting (weeks 3-4), workflow migration (weeks 5-8), infrastructure setup (weeks 9-10), parallel operation (weeks 11-12), and final cutover (week 13).

## Key Takeaway

V3 represents substantial architectural advancement offering "significant improvements in scalability and performance," but requires committing to comprehensive manual migration rather than automated tooling.
