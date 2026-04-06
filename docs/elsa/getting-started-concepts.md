# Elsa Workflows: Core Concepts

## Workflow & Execution
A "sequence of steps called activities that represents a process" can be created visually or programmatically. Each workflow instance persists in a database during execution, while a "burst of execution" describes the active period when the engine runs activities continuously or resumes after blocking points.

## Activities & Composition
Activities serve as "a unit of work executed by the workflow engine" and implement the `IActivity` interface. Blocking activities pause workflows by creating bookmarks, enabling resumption later—commonly used in delay or event-listening scenarios.

## Control & Navigation
Activities connect through outcomes, which represent potential results. The `Decision` activity exemplifies this with "True" and "False" outcomes. Triggers (activities marked with `Trigger` kind) initiate new workflow instances, such as the `HttpEndpoint` activity responding to URL requests.

## Data Management
Workflows handle data through multiple mechanisms: activity inputs (public properties like `Text` in `WriteLine`), workflow-level inputs (such as `OrderId`), variables (workflow-scoped storage updated by activity outputs), and outputs (data passed between sequential activities for decisions or information transfer).

## Tracking & Modification
A "flexible identifier linking related workflows and external entities," the Correlation ID aids in distributed tracing. Incidents record errors during execution, while alterations enable modifications to workflow instance state and activity scheduling.
