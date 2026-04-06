# Correlation ID

## Overview

A **Correlation ID** is a flexible identifier used to associate related workflow instances with each other and with external domain entities. This feature is particularly useful in scenarios where workflows are distributed, triggered asynchronously, or involve parent-child relationships.

## How is Correlation ID Used?

Common use cases:
* **Parent-Child Workflow Relationship**: Child workflows share the same Correlation ID as the parent for tracing execution chains.
* **Correlating Workflows with Domain Entities**: Use entity IDs (Documents, Customers, Orders) as Correlation IDs.
* **Multi-Step Processes**: Same Correlation ID ties different stages together (order processing, shipping, billing).
* **Distributed Systems**: Ensures related workflows are easily identifiable across services.

## When is Correlation ID Assigned?

1. **Manual Assignment**: Explicitly set via API calls or workflow triggers.
2. **Correlate Activity**: Dynamically assign or update during workflow execution.

```javascript
// Example JavaScript expression to set the Correlation ID
getOrder().Id
```

## Correlation with Domain Entities

* **Documents**: Document ID as Correlation ID for reviewing, approving, archiving workflows.
* **Customers**: Customer ID for registration, onboarding, support workflows.
* **Orders**: Order ID for processing, shipping, invoicing workflows.

## Restrictions on Correlation ID

* **String Format**: Any alphanumeric value, UUID, or string-based identifier.
* **Uniqueness**: Should be unique across logically distinct workflow groups.
* **Length**: Keep concise and readable; avoid special characters.

## Monitoring and Observability

The Correlation ID plays a central role in telemetry (OpenTelemetry) by grouping related workflows for tracing, identifying bottlenecks, and debugging.
