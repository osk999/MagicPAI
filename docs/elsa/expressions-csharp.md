# C# in Elsa Workflows

## Overview

The C# Expressions feature enables dynamic expression writing in workflows. It relies on Roslyn for implementation.

## Installation & Setup

Install via: `dotnet package add Elsa.CSharp`

Enable in Program.cs:
```csharp
services.AddElsa(elsa => elsa.UseCSharp());
```

## Configuration Options

The `UseCSharp()` method accepts configuration for:
- Additional assemblies and namespaces
- Custom global methods via `AppendScript()`

**Pre-loaded namespaces** include System, System.Collections.Generic, System.Linq, and JSON-related packages.

## Available Global Objects

### Core Globals

| Member | Type | Purpose |
|--------|------|---------|
| `WorkflowInstanceId` | String | Current workflow instance identifier |
| `CorrelationId` | String | Workflow correlation tracking |
| `Variable` | Object | Access workflow variables |
| `Output` | Object | Retrieve activity outputs |
| `Input` | Object | Access workflow inputs |

### Variable Object

Provides generic and non-generic access methods:
- `T? Get<T>(string name)` - typed retrieval
- `void Set(string name, object? value)` - assignment
- Strongly-typed properties for declared variables

### Output Object

Retrieves activity results:
- `T? From<T>(string activityIdOrName, string? outputName)` - typed output
- `object? LastResult` - previous activity's result

### Input Object

Accesses workflow inputs via `Get()` methods and strongly-typed properties matching declared workflow inputs.
