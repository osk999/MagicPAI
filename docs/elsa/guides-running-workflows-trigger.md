# Using a Trigger

A trigger is an activity mechanism that communicates workflow activation details to external services. Elsa provides several built-in trigger types:

* **HTTP Endpoint**: triggers the workflow when a given HTTP request is sent to the workflow server
* **Timer**: triggers the workflow each given interval based on a TimeSpan expression
* **Cron**: triggers the workflow each given interval based on a CRON expression
* **Event**: triggers when a given event is received by the workflow server

## Code Example

The framework demonstrates implementation through a C# class that extends `WorkflowBase`. The sample configures an HTTP endpoint accepting GET requests at `/hello-world` with the ability to initiate workflows. Once triggered, it responds with "Hello world!" and an HTTP 200 status code.

The workflow structure uses a `Sequence` container with two activities: the HTTP trigger configuration and a response writer activity.

## Visual Workflow Design

The documentation references a step-by-step guide for creating HTTP-triggered workflows through Elsa Studio, the visual design interface.
