# Workflow Patterns Guide

This comprehensive guide covers eight essential patterns for building workflow-driven applications with Elsa v3.

## Core Patterns

### Human-in-the-Loop Approval

Uses blocking activities and bookmarks to pause workflows awaiting human decisions. The approach leverages APIs like `CreateBookmark()` and `GenerateBookmarkTriggerUrl()` to create resumable approval processes with proper security considerations.

### Event-Driven Correlation

Matches incoming events to waiting bookmarks through stimulus hashing. When you create a bookmark with a payload, Elsa computes a deterministic hash. This enables systems to route order events, customer updates, and multi-step processes reliably.

### Fan-Out/Fan-In

Executes multiple branches in parallel and waits for completion. The pattern supports three fan-out options (Parallel Activity, ForEach, or Flowchart branching) and multiple fan-in strategies using Fork/Join or trigger-based aggregation.

### Timeout/Escalation

Combines blocking activities with timers using Fork/Join patterns where the first to complete wins. Distributed deployments should use Quartz clustering to prevent multiple timeout executions.

### Compensation/Saga-Lite

Handles workflow failures by undoing partial steps. While Elsa lacks built-in saga transactions, compensation can be modeled through inline branches or separate workflows storing compensation state.

## Essential Practices

### Idempotent External Calls

Require activities to check completion state before executing. The pattern uses idempotency keys when calling external services and stores receipts in workflow variables.

### Long-Running Workflows

Rely on bookmarks for pausing, persistence for state storage, correlation IDs for event matching, and retention policies for cleanup. Workflows spanning days or weeks demand careful state management and safe cancellation patterns.

## Key Takeaways

- Use stable, low-cardinality business identifiers for correlation
- Implement distributed locking for scheduled bookmarks in clustered environments
- Validate user permissions in approval resume handlers with tokenized URLs
- Configure retention policies to manage database growth
- Employ OpenTelemetry integration for observability

The guide includes JSON examples, C# code snippets, pitfall tables, and troubleshooting guidance for each pattern.
