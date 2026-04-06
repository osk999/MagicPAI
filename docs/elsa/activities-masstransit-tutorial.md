# Tutorial

The following example highlights creating and registering a fictive message type called `OrderCreated`.

## Code Examples

**OrderCreated.cs**
```csharp
public record OrderCreated(string Id, string ProductId, int Quantity);
```

**Program.cs**
```csharp
services.AddElsa(elsa =>
{
    // Enable and configure MassTransit
    elsa.AddMassTransit(massTransit =>
    {
        // Register our message type.
        massTransit.AddMessageType<OrderCreated>();
    };
});
```

## Resulting Activities

With this configuration, the workflow server automatically generates two activities for handling `OrderCreated` messages:

* Order Created
* Publish Order Created

The **Order Created** activity functions as a trigger, automatically initiating the containing workflow upon receiving a matching message. The **Publish Order Created** activity dispatches messages of this type.
