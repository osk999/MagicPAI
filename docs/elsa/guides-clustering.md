# Clustering in Elsa Workflows

## Overview

This comprehensive guide addresses running Elsa Workflows in clustered environments for production deployments. The documentation covers essential concepts, architecture patterns, practical configurations, and operational considerations needed for high-availability workflow systems.

## Core Problems Clustering Solves

The documentation identifies three critical challenges in multi-node deployments:

### Duplicate Timer Execution
Without clustering coordination, multiple nodes can independently trigger the same scheduled task. For example, a workflow with a timer activity deployed across 3 nodes might cause each node to send duplicate reminder emails. Quartz.NET clustering resolves this through database-backed job locks ensuring single execution.

### Concurrent Modification Issues
An HTTP workflow receives two simultaneous requests that both attempt to resume the same workflow instance, risking state corruption. Distributed locking prevents this by serializing access through mechanisms like Redis or PostgreSQL-backed locks.

### Cache Invalidation Problems
When administrators update workflow definitions, other nodes keep stale cache unless explicitly invalidated. MassTransit publishes cache invalidation events across the message bus ensuring consistency.

## Key Mitigation Strategies

Elsa implements four protective mechanisms:

1. **Bookmark Hashing** - Deterministic hashes prevent duplicate bookmark creation across nodes
2. **Distributed Locking** - WorkflowResumer acquires locks before resuming instances
3. **Quartz Clustering** - Centralized scheduler prevents duplicate job execution
4. **Cache Invalidation Events** - MassTransit broadcasts updates across the cluster

## Architectural Patterns

### Pattern 1: Shared Database + Distributed Locks
Recommended for most production scenarios. All nodes connect to a central database with external lock providers (Redis or PostgreSQL).

### Pattern 2: Leader-Election Scheduler
Designates one node for scheduling while others handle requests, reducing coordination overhead.

### Pattern 3: Quartz Clustering
All nodes participate equally with automatic failover, simplifying deployment configuration.

### Pattern 4: External Scheduler
Uses platform-native scheduling (Kubernetes CronJobs, AWS Lambda) independent of Elsa's internal scheduler.

## Configuration Implementation

### Distributed Locks via Redis
Redis 6.0+ deployed and accessible with Medallion.Threading package enables fast, in-memory lock acquisition (typically <100ms).

### Database-Backed Locks
PostgreSQL or SQL Server provide lock capabilities without additional infrastructure but with slower acquisition times due to disk I/O.

### Quartz Configuration
Enable clustering through `quartz.jobStore.clustered = true` in quartz.properties or through fluent configuration in Program.cs.

## Operational Validation

The documentation provides a comprehensive checklist covering:
- Distributed runtime validation through concurrent request testing
- Scheduled task validation ensuring single execution
- Cache invalidation verification
- Failover behavior confirmation
- High availability testing during rolling restarts

## Security Considerations

- Use TLS/SSL for all database connections
- Implement dedicated database users with minimal permissions
- Store all timestamps in UTC to prevent timezone-related issues
- Generate cryptographically secure tokens for workflow resume URLs
- Monitor for unusual resume attempt patterns

## Studio Deployment Notes

Elsa Studio operates as a stateless SPA when using WebAssembly, eliminating session affinity requirements. This allows free routing across pod replicas. Token-based authentication (JWT) is recommended over session-based approaches for clustered environments.

## Summary

A clustered setup allows multiple Elsa instances to work together, distributing workload across nodes while maintaining consistency and preventing data corruption. Success requires coordinating distributed locks, centralizing job scheduling, and invalidating caches across the cluster through proven technologies like Quartz.NET, Redis, and MassTransit.
