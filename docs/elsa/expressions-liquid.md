# Liquid in Elsa: Complete Documentation

## Overview
Elsa supports dynamic expressions through Liquid templating. The framework implements Liquid via the Fluid library, enabling developers to use standard Liquid syntax plus additional custom tags and filters.

## Setup and Installation

To use Liquid expressions, install the dedicated package:
```bash
dotnet package add Elsa.Liquid
```

Enable the feature in your startup configuration:
```csharp
services.AddElsa(elsa =>
{
   elsa.UseLiquid();
});
```

## Configuration Options

The `UseLiquid` method accepts a configuration delegate for customizing `FluidOptions`:

```csharp
services.AddElsa(elsa =>
{
   elsa.UseLiquid(liquid =>
   {
      liquid.FluidOptionstions += options =>
      {
         options.Encoder = HtmlEncoder.Default;
      }
   });
});
```

## Available Filters

**json filter**: "serialises an input value to a JSON string"
```liquid
{{ some_value | json }}
```

**base64 filter**: Encodes values into base64 format
```liquid
{{ some_value | base64 }}
```

## Available Objects

| Object | Purpose |
|--------|---------|
| `Variables` | Access workflow variables (e.g., `{{ Variables.OrderId }}`) |
| `Input` | Access workflow input data (e.g., `{{ Input.OrderNumber }}`) |
| `WorkflowInstanceId` | Current execution instance identifier |
| `WorkflowDefinitionId` | Workflow definition identifier |
| `WorkflowDefinitionVersionId` | Version-specific definition identifier |
| `WorkflowDefinitionVersion` | Version number of the workflow |
| `CorrelationId` | Correlation identifier for the execution |

## Additional Resources

The Fluid library documentation contains extended tag and filter options, plus guidance for implementing custom tags and filters.
