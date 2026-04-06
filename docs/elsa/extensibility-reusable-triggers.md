# Reusable Triggers in Elsa Workflows

Elsa Workflows provides base classes that streamline the creation of trigger-based activities. Rather than handling low-level infrastructure concerns, developers can focus on implementing specific behavior using pre-built abstractions.

## Key Base Classes Available

The framework offers four primary tools for building trigger-based functionality:

1. **EventBase<T>** - Manages event subscriptions and resumption for domain-specific events
2. **TimerBase** - Handles interval-based recurring activities
3. **HttpEndpointBase** - Creates HTTP-triggered workflow entry points
4. **Activity.DelayFor(...)** - Enables delayed execution within any custom activity

## Implementation Patterns

Each base class requires overriding specific methods. For events, developers implement `GetEventName()` and `OnEventReceived()` to handle domain-specific notifications. Timer implementations override `GetInterval()` and `OnTimerElapsed()` for periodic tasks. HTTP endpoints define their configuration through `GetOptions()` and respond to requests via `OnHttpRequestReceivedAsync()`.

The `DelayFor()` method offers an alternative approach, allowing activities to schedule continuation callbacks without creating separate trigger logic. This proves especially useful when delays form part of larger activity workflows.

## Benefits

This architecture emphasizes three core advantages: reusability through existing infrastructure, simplicity by eliminating boilerplate scheduling code, and consistency with Elsa's execution model. Developers maintain full control over activity-specific logic while leveraging battle-tested triggering mechanisms.
