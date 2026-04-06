# MongoDB Setup for Elsa Workflows

## Overview
This guide establishes a foundational configuration for integrating Elsa Workflows v3.x with MongoDB persistence. The documentation covers both basic setup and production-ready considerations.

## Core Requirements
The setup necessitates ".NET 8.0 or later," "MongoDB 4.4 or later," and appropriate Elsa packages installed via NuGet.

## Essential Configuration Steps

**Service Registration:** The `Program.cs` file must invoke `AddElsa()` with MongoDB-specific options for both workflow management (definitions and instances) and workflow runtime (bookmarks and execution logs). Connection strings should be externalized in `appsettings.json` rather than hardcoded.

**Connection Patterns:** Standard local connections use `mongodb://localhost:27017/elsa`, while authenticated deployments require credentials and an `authSource` parameter. Replica set deployments specify multiple nodes with a `replicaSet` identifier.

## Production Considerations

**Index Creation:** MongoDB requires manual index establishment. Collections like `WorkflowDefinitions`, `WorkflowInstances`, `Bookmarks`, and `WorkflowExecutionLogRecords` benefit from strategic indexing on frequently queried fields such as `DefinitionId`, `Status`, and `CorrelationId`.

**TTL Indexes:** Automatic document expiration can be configured for inbox messages and execution logs. The example demonstrates a "TTL: 7 days" configuration for inbox cleanup.

**Advanced Tuning:** Connection pool sizing, read preferences for replica sets, and BSON serialization conventions address high-concurrency scenarios and complex data requirements.

## Diagnostics
Troubleshooting guidance addresses connectivity failures, authentication problems, and performance optimization through index verification and query analysis.
