# Custom Activities in Elsa Workflows

## Overview

Elsa Workflows provides built-in activities for common tasks, but the platform's true power emerges through creating custom activities tailored to specific business domains. According to the documentation, custom activities enable developers to "encapsulate domain-specific business logic" and "integrate with external systems and APIs."

## Core Implementation Patterns

### Basic Activity Structure

Creating a custom activity requires implementing the `IActivity` interface or inheriting from a base class. The simplest approach uses `CodeActivity`, which automatically completes after execution:

```csharp
public class PrintMessage : CodeActivity
{
    protected override void Execute(ActivityExecutionContext context)
    {
        Console.WriteLine("Hello world!");
    }
}
```

For more complex scenarios requiring asynchronous operations or manual completion control, the `Activity` base class is appropriate, requiring explicit invocation of `context.CompleteActivityAsync()`.

### Input and Output Configuration

Activities accept parameters through properties decorated with `Input<T>` for dynamic value support through expressions:

```csharp
public Input<string> Message { get; set; } = default!;
var message = Message.Get(context);
```

Outputs are similarly defined using `Output<T>` properties, enabling downstream activities to access results through variables or direct access patterns.

## Advanced Features

**Composite Activities** combine multiple tasks with conditional branching. The documentation illustrates this through the `If` activity example, which evaluates conditions and schedules appropriate child activities.

**Blocking Activities** pause execution and create bookmarks for resuming when external events occur. These are essential for long-running workflows spanning hours or days. The `Trigger` base class extends this capability, allowing activities to both start new workflow instances and resume suspended ones.

**Dependency Injection** integrates services through `context.GetRequiredService<T>()`, enabling database access, API calls, and external system integration without constructor dependencies.

## Registration and Discovery

Activities must be registered before use. The framework supports individual registration, assembly-level scanning, and dynamic provider patterns. **Activity Providers** enable runtime generation from external sources like APIs or databases, supporting advanced scenarios including API integration and multi-tenant systems.

## Key Considerations

UI hints control designer experience, allowing customization of input controls through options like checkboxes, dropdowns, code editors, and custom pickers. The `ActivityAttribute` provides metadata including display names and hierarchical categories using slash separators.

The documentation notes that "dynamically provided activities cannot be used within programmatic workflows" defined in C#, though an open GitHub issue tracks this limitation.

Custom activities form the foundation for extending Elsa to meet domain-specific requirements while maintaining code reusability and designer-friendly configuration.
