# Elsa Workflows Troubleshooting Guide

This comprehensive troubleshooting document addresses common Elsa Workflows issues across several key areas.

## Core Problem Areas

### Workflows Don't Start

Verify that workflow definitions are published, triggers are properly configured, and the runtime has begun. Check database connectivity and review logs for exceptions.

### Resume & Bookmark Issues

When workflows suspend at blocking activities but never resume, the problem typically involves bookmark matching failures or missing distributed lock configurations. Bookmarks are matched using a deterministic hash based on activity type and stimulus data.

### Concurrency Problems

Duplicate execution occurs when distributed locking isn't enabled. The fix involves configuring lock providers like Redis or PostgreSQL through the `UseDistributedRuntime()` method.

### Timer Mismanagement

Scheduled workflows may fire multiple times or not at all due to improper Quartz clustering, clock synchronization issues, or misfire settings.

### Stuck Workflows

Long-running instances require monitoring for orphaned locks, incident creation, and proper graceful shutdown handling.

### Database Performance

High load stems from missing indexes, undersized connection pools, or accumulated historical data requiring retention policies.

## Diagnostic Tools

The guide provides specific SQL queries, configuration checks, and logging strategies. Key recommendations include enabling Debug-level logging for Elsa namespaces and using structured logging with correlation IDs for traceability.

## Production Readiness

A final checklist covers infrastructure (connection pooling, backups), Elsa configuration (distributed runtime, clustering), observability (logging, tracing), and security requirements.
