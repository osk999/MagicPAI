# Elsa Workflows Architecture Overview

## Core Structure

Elsa Workflows implements a layered architecture with four main tiers:
- **Presentation**: Studio, APIs, SignalR
- **Application**: Management, activities, triggers
- **Runtime**: Execution engine, bookmarks, dispatcher
- **Persistence**: Database abstraction

## Key Components

**Elsa Server** runs as an ASP.NET Core application handling workflow execution, REST APIs, trigger processing, and background services. Uses EF Core or MongoDB for storage.

**Elsa Studio** is a Blazor WebAssembly designer offering drag-and-drop workflow creation, activity configuration, execution monitoring, and instance management.

**Activities** are discrete work units composed into workflows. They support control flow, data transformation, HTTP operations, blocking operations, and custom implementations. Activity states: scheduled → executing → suspending → suspended → resuming → completing → completed.

## Execution Model

**Execute** provides synchronous, inline execution within the caller's context, blocking until completion or suspension. Suits short-lived workflows and testing.

**Dispatch** queues asynchronous background execution, returning immediately. Supports long-running workflows and distributed processing.

## Advanced Concepts

**Bookmarks** represent suspension points allowing workflows to pause and resume later when external events provide matching stimuli.

**Triggers** are entry points (marked with `ActivityKind.Trigger`) that automatically start new instances matching incoming events.

**Stimuli** are external events either starting workflows via triggers or resuming them via bookmarks.

## Scalability

Single instances handle 100-1000+ workflows per second. Horizontal scaling requires:
- Distributed runtime configuration
- Distributed locking (PostgreSQL, Redis)
- Distributed caching (MassTransit with RabbitMQ)
- Quartz.NET clustering for scheduled tasks

## Deployment Options

- **Development**: All-in-one single servers
- **Production**: Separate Studio from Server behind load balancers
- **Kubernetes**: StatefulSets for databases and RabbitMQ with HPA
- **Microservices**: Isolated workflow domains via message buses

## Extensibility

Custom activities, triggers, middleware, persistence providers, expression evaluators, and Studio UI hints. Multi-tenancy supports shared/separate/isolated databases.

## Security & Operations

Authentication: API keys, JWT tokens, or OIDC. Structured logging captures activity execution, workflow lifecycle events, and exceptions. Health checks, metrics (Application Insights, Prometheus, OpenTelemetry), and execution logs.
