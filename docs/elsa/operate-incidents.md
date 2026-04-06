# Incidents

Based on the provided content, an incident represents "an error event that occurred in the workflow," such as when an activity fails during execution.

## How Incidents Are Created

The system generates incident records through an automatic process: when an unhandled exception occurs during activity execution, the workflow runtime captures it and creates an incident record. These records are maintained in the `Incidents` collection within the `WorkflowExecutionContext` and are persisted as `WorkflowInstance` records in the database.

The documentation indicates that incidents serve as a mechanism for tracking and storing error information throughout the workflow lifecycle, enabling visibility into what went wrong during execution.
