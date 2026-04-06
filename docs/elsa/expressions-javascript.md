# JavaScript in Elsa Workflows

## Installation and Setup

To enable JavaScript expressions in Elsa workflows, install the dedicated package and configure it in your startup code:

```csharp
dotnet add package Elsa.JavaScript
```

Then register the feature:

```csharp
services.AddElsa(elsa =>
{
   elsa.UseJavaScript();
});
```

## Configuration Options

The `UseJavaScript` method accepts configuration for the underlying Jint engine. You can customize behavior like CLR access and register custom types:

```csharp
services.AddElsa(elsa =>
{
   elsa.UseJavaScript(options =>
   {
      options.AllowClrAccess = true;
      options.RegisterType<Order>();
   });
});
```

## Available Global Functions and Objects

Elsa provides numerous built-in globals for workflow expressions:

**Workflow Context Functions:**
- `getWorkflowDefinitionId()` - retrieves the current workflow definition identifier
- `getWorkflowInstanceId()` - retrieves the current execution instance identifier
- `getCorrelationId()` / `setCorrelationId(value)` - manages correlation identifiers

**Variable and Data Access:**
- `variables` - provides direct access to workflow variables
- `getVariable(name)` / `setVariable(name, value)` - manages variables by name
- `getInput(name)` - retrieves workflow inputs
- `getOutputFrom(activityIdOrName, outputName?)` - accesses activity results
- `getLastResult()` - retrieves the previous activity's output

**Utility Functions:**
- `JSON.stringify()` and `JSON.parse()` - handle JSON serialization
- `newGuid()`, `newGuidString()`, `newShortGuid()` - generate identifiers
- `isNullOrEmpty()`, `isNullOrWhiteSpace()` - validate strings

**Encoding Functions:**
- Base64 conversion: `stringToBase64()`, `stringFromBase64()`
- Byte operations: `bytesToBase64()`, `bytesFromBase64()`, `bytesToString()`, `bytesFromString()`
- Stream handling: `streamToBytes()`, `streamToBase64()`
