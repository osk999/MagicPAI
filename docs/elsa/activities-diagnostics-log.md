# Log Activity in Workflow Design

The Log activity enables workflow designers to generate structured logging entries during workflow execution.

## Core Functionality

The activity accepts message templates with placeholders that are populated at runtime. As stated in the documentation, "The `Message` input supports message templates, allowing placeholders such as `Hello {Name}`" to be replaced with values from the Arguments input.

## Available Properties

The Log activity provides several configurable properties:

- **Message**: Contains the template for the log entry
- **Level**: Specifies severity (Trace, Debug, Information, Warning, Error, Critical)
- **Category**: Identifies the log source (defaults to "Process")
- **Arguments**: Supplies values for template placeholders
- **Attributes**: Stores additional metadata as key/value pairs
- **SinkNames**: Designates which destinations receive the log entry

## Sink Selection

When multiple logging destinations are configured, the workflow designer can select one or more sinks through a picker interface.

## Implementation Examples

Basic usage creates a simple log entry:
```csharp
new Log("Workflow started", LogLevel.Information)
```

More advanced usage allows specifying destinations and metadata:
```csharp
new Log
{
    Message = new("Order received: {OrderId}"),
    Arguments = new(new { OrderId = orderId }),
    SinkNames = new(new[] { "FileJson" })
}
```

## Extension Options

The framework supports custom sink implementations through the Logging Framework, which is documented separately for developers needing specialized logging behavior.
