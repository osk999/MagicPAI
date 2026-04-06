# Elsa Workflows: API & Client Guide

## Overview

This guide explains how to interact with Elsa Workflows through two approaches:

1. **Direct HTTP REST calls** - Language-agnostic but requires manual handling
2. **elsa-api-client library** - .NET-specific with type safety and built-in resilience

For .NET projects, the client library is recommended; for polyglot teams or webhooks, use HTTP directly.

## Core Architecture

Elsa centers on three main entities:

- **Workflow Definitions** - Template blueprints
- **Workflow Instances** - Active or completed executions
- **Bookmarks** - Suspension points for resuming workflows

The lifecycle flows through design -> publish -> instantiate -> execute -> suspend (bookmark) -> resume -> complete.

## Authentication Methods

The system supports bearer tokens and API keys:

```csharp
// Client configuration
services.AddElsaClient(client =>
{
    client.BaseUrl = new Uri("https://your-server.com");
    client.ApiKey = "YOUR_API_KEY";
});
```

Or use `curl` with headers like `Authorization: Bearer TOKEN` or `X-Api-Key: KEY`.

## Publishing Workflows

Use the client library to save and publish definitions programmatically. Key properties include `DefinitionId`, `Version`, `Root` activity, and options for commit strategy and activation type.

## Starting & Querying Workflows

Instantiate workflows with a definition ID, optional correlation ID, and input data. Query instances by status, correlation ID, definition ID, or version using pagination.

## Bookmarks & Resuming

When workflows hit blocking activities, they create bookmarks. Resume them via:
- Tokenized URLs (HTTP trigger-generated)
- Stimulus payloads (direct resumption)
- Instance ID + bookmark ID (client library)

## Resilience & Error Handling

Configure retry strategies at the activity level. Monitor workflow health through status checks and handle common HTTP errors (400, 401, 404, 410).

## Best Practices

- Use meaningful correlation IDs for tracking
- Implement idempotent resume handlers
- Choose commit strategies based on durability needs (`WorkflowExecuted` for throughput, `ActivityExecuted` for durability)
- Always paginate results
- Prefer server-side filtering over client-side processing
