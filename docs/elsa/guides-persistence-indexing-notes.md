# Indexing Notes for Elsa Workflows

## Document Summary

This reference guide outlines database indexing strategies for Elsa Workflows persistence stores across PostgreSQL, SQL Server, and MongoDB platforms.

## Core Indexing Recommendations

The document identifies six primary query patterns requiring optimization:

1. **Resume by bookmark hash** — Requires `(activity_type_name, hash)` composite index
2. **Instance filtering by status** — Needs `(status)` and `(status, definition_id)` indexes
3. **Correlation-based lookups** — Uses `(correlation_id)` index
4. **Data retention operations** — Relies on `(updated_at)` and `(finished_at)` indexes
5. **Bookmark discovery** — Requires `(workflow_instance_id)` index
6. **Incident tracking** — Uses `(workflow_instance_id)` and `(timestamp)` indexes

## Database-Specific Implementation

**PostgreSQL** uses standard `CREATE INDEX` syntax with optional `WHERE` clauses for partial indexing. Key examples include descending timestamp indexes and conditional indexes on non-null values.

**SQL Server** implements `NONCLUSTERED INDEX` structures, with the document demonstrating covering indexes that "Include (id, definition_id, correlation_id, created_at)" to minimize key lookups.

**MongoDB** requires explicit index creation via shell commands, supporting partial filter expressions and TTL (time-to-live) indexes for automatic data expiration.

## Maintenance and Monitoring

The guide provides database-native commands for analyzing index health, identifying unused indexes, and detecting performance bottlenecks. It emphasizes "Regular maintenance — Schedule index maintenance during low-traffic periods" and recommends testing with production-representative datasets.
