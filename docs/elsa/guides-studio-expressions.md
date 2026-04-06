# Expressions in Elsa Studio

Expressions represent a cornerstone capability within Elsa Studio, enabling dynamic property values, variable references, computational tasks, and conditional logic implementation.

## Expression Types Available

The platform supports five primary expression categories:

**Literal** expressions contain static, unchanging values suitable for hardcoded data like `"Hello, World!"`.

**JavaScript** expressions permit dynamic computation through JavaScript code, commonly utilized for accessing variables through syntax such as `variables.OrderId` or `variables.Customer.Name`.

**C#** expressions provide access to .NET types and methods with full typing support, exemplified by `Variable.Get<Guid>("OrderId")` or `Variable.OrderId`.

**JSON** expressions facilitate structured data provision directly within workflows.

**Liquid** expressions employ template language functionality, demonstrated as `Hello, {{ Variables.CustomerName }}!`

The platform additionally supports Python and other expression types contingent on server configuration.

## Selection Guidance

For straightforward static values, implement Literal expressions. JavaScript excels at simple calculations and frequent variable access scenarios. Deploy C# when strong typing or complex .NET library integration becomes necessary. JSON handles structured data objects, while Liquid suits text generation with embedded variables.

## Variable Access Patterns

Variables stored within the `variables` object follow a naming convention: a workflow variable designated `OrderId` becomes accessible via `variables.OrderId`.

Object variables permit nested property access using dot notation:

```javascript
variables.variable1.data.id
variables.Customer.Address.City
variables.OrderDetails.Items[0].Price
```

The `setVariable()` function enables value assignment:

```javascript
setVariable("OrderId", newGuid())
```

Null-safety practices protect against undefined variables:

```javascript
variables.OrderId ? variables.OrderId : "default-value"
variables.Customer?.Address?.City
variables.OrderId || "unknown"
```

## C# Variable Access Methods

Strongly-typed access leverages auto-generated properties: `Variable.OrderId` or `Variable.CustomerName`.

The `Get<T>()` method provides type-safe retrieval:

```csharp
Variable.Get<Guid>("OrderId")
Variable.Get<string>("CustomerName")
Variable.Get<int>("Quantity")
```

Dynamic access accommodates flexible scenarios:

```csharp
var variable1 = Variable.Get<dynamic>("variable1");
var id = variable1.data.id;
```

Dictionary-based access handles object variables stored as dictionaries:

```csharp
var variable1 = Variable.Get<Dictionary<string, object>>("variable1");
var data = variable1["data"] as Dictionary<string, object>;
var id = data["id"] as string;
```

Value assignment utilizes the `Set()` method:

```csharp
Variable.Set("OrderId", Guid.NewGuid());
Variable.Set("Status", "Completed");
```

## Practical Implementation Example

When extracting an `id` value from a nested object structure within `variable1` and storing it in `extractedId`:

**JavaScript approach:**
```javascript
variables.variable1.data.id
```

**C# dynamic approach:**
```csharp
Variable.Get<dynamic>("variable1").data.id
```

**C# dictionary approach:**
```csharp
var variable1 = Variable.Get<Dictionary<string, object>>("variable1");
var data = variable1["data"] as Dictionary<string, object>;
var id = data?["id"] as string;
return id ?? string.Empty;
```

## Advanced Scenarios

Multi-variable combinations in JavaScript:
```javascript
`${variables.FirstName} ${variables.LastName} - Order #${variables.OrderId}`
```

Conditional logic evaluation:
```javascript
variables.TotalAmount > 1000 ? "Premium" : "Standard"
```

Array operations:
```javascript
variables.Items[0].Name
variables.Items.length
variables.Tags.includes("urgent")
```

## Common Implementation Errors

Misspelled variable names cause access failures -- variable naming remains case-sensitive. Incorrect expression type selection prevents evaluation; verify the dropdown setting matches your intent. Null reference errors emerge when accessing properties on potentially null objects; employ optional chaining (`?.`) or conditional checks. Variables must exist before access, either through workflow definition or prior activity execution. Type mismatches in C# require correct type parameters in `Get<T>()`.

## Activity Outputs and Workflow Input

Access activity results via:

**JavaScript:** `getOutputFrom("MyHttpRequest", "Body")` or `getLastResult()`

**C#:** `Output.From<string>("MyHttpRequest", "Body")` or `Output.LastResult`

Workflow input retrieval:

**JavaScript:** `getInput("OrderId")` or `getOrderId()`

**C#:** `Input.Get<Guid>("OrderId")` or `Input.OrderId`

## Testing and Best Practices

Execute workflows within Studio, navigate to Workflow Instances, and inspect the variables tab to verify expression functionality. Employ descriptive variable naming conventions like `OrderId` or `CustomerEmail` rather than generic terms. Consider null-safety through default value assignment or null-coalescing operators. Prioritize strongly-typed C# access when variable structures are known. Fragment excessively complex expressions across multiple activities for maintainability.
