# REST API - Alteration Plans

The Alterations module provides REST endpoints for managing alteration plans in workflow instances. Here's a summary of key capabilities:

## Core Functionality

The API allows you to submit plans containing multiple alteration types. You can "submit a plan that modifies a variable, migrates the workflow instance to a new version and to schedule an activity."

**Submission Process:**
- POST request to `/alterations/submit` with alterations array and workflow instance IDs
- Response includes a `planId` for tracking

**Status Checking:**
- GET request using the Plan ID to retrieve execution status
- Response includes plan details, job information, and execution logs

## Synchronous Execution

Rather than asynchronous plan submission, you can apply alterations immediately using the `IAlterationRunner` service. The documentation notes that "alterations are applied synchronously and the results are returned."

After immediate execution, use `IAlteredWorkflowDispatcher` to resume workflow processing.

## Custom Alterations

The framework supports extensibility through custom alteration types. Implementation requires:

1. Creating a class implementing `IAlteration` interface
2. Building an alteration handler (implementing `IAlterationHandler<T>` or extending `AlterationHandlerBase<T>`)
3. Registering via the service collection with `AddAlteration<TType, THandler>()`

The documentation provides a working example demonstrating how to create a custom alteration that adds output to the workflow execution context.
