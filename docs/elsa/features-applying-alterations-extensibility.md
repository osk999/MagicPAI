# Extensibility - Applying Alterations

Elsa Workflows enables developers to create custom alteration types by implementing the `IAlteration` interface. This extensibility feature allows for tailored workflow modifications.

## Implementation Steps

**Define the Alteration Type**

Start by implementing the `IAlteration` interface:

```csharp
public interface IAlteration
{
}
```

**Create an Alteration Handler**

Develop a handler using the `IAlterationHandler` interface:

```csharp
public interface IAlterationHandler
{
    bool CanHandle(IAlteration alteration);
    ValueTask HandleAsync(AlterationContext context);
}
```

Alternatively, developers can "derive from the `AlterationHandlerBase<T>` base class" to streamline the process.

**Register the Handler**

Add your handler to the service collection during configuration:

```csharp
services.AddElsa(elsa => 
{
    elsa.UseAlterations(alterations => 
    {
        alterations.AddAlteration<MyAlteration, MyAlterationHandler>();
    })
});
```

## Practical Example

This example illustrates creating a custom alteration and corresponding handler:

```csharp
public class MyAlteration : IAlteration
{
    public string Message { get; set; }
}

public class MyAlterationHandler : AlterationHandlerBase<MyAlteration>
{
    public override async ValueTask HandleAsync(AlterationContext context, MyAlteration alteration)
    {
        context.WorkflowExecutionContext.Output.Add("Message", context.Alteration.Message);
    }
}
```
