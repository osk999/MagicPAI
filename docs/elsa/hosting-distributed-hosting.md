# Distributed Hosting Configuration for Elsa Workflows

## Key Components Overview

Elsa requires four essential configurations for distributed deployment:

1. **Distributed Runtime** - Replaces the default local runtime
2. **Distributed Locking** - Synchronizes workflow execution across nodes
3. **Distributed Caching** - Propagates cache invalidation via pub/sub
4. **Quartz.NET Clustering** - Coordinates scheduled job execution

## Distributed Runtime Setup

The default `LocalWorkflowRuntime` is unsuitable for multi-instance environments. Instead, implement either `DistributedWorkflowRuntime` or `ProtoActorWorkflowRuntime` to prevent concurrent execution of the same workflow instance.

Configuration requires pairing with a distributed locking provider.

## Distributed Locking Requirements

"To prevent multiple nodes from executing and updating the same workflow instance simultaneously, Elsa employs distributed locking." This mechanism acquires leases on shared resources like databases, Redis, or blob storage.

**Critical Warning:** Filesystem-based locks are unreliable for production. Use robust providers such as PostgreSQL, Redis, or cloud blob storage instead.

## Distributed Caching Implementation

Nodes maintain local in-memory caches requiring event-driven cache invalidation across clusters. MassTransit with RabbitMQ provides the messaging infrastructure for this synchronization.

## Quartz.NET Clustering

Clustering is automatically enabled when configuring Quartz.NET with database providers (PostgreSQL, SQL Server, MySQL). It prevents duplicate execution of scheduled activities like timers, delays, and cron triggers across multiple instances.

Clustering is unnecessary for single-instance deployments or workflows without time-based activities.
