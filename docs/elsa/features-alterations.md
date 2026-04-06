# Alterations

An alteration represents a modification applicable to a workflow instance.

Alterations enable you to adjust a workflow instance's state, schedule activities, and perform other modifications.

## Alteration Types

Elsa Workflows supports the following alteration categories:

* **ModifyVariable**: Adjusts a variable.
* **Migrate**: Transitions a workflow instance to a newer version.
* **ScheduleActivity**: Plans an activity for execution.
* **CancelActivity**: Terminates an activity, such as `Delay`, `Event`, `MessageReceived`, etc.
