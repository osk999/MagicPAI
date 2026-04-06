# Studio User Guide

Elsa Studio is a Blazor-based web application serving as the visual designer and administrative interface for Elsa Workflows v3. It enables users to create, edit, and manage workflows visually, monitor executions, and configure automation systems.

## Primary Functions

The platform provides four core capabilities:

- **Visual Workflow Design**: Drag-and-drop interface for creating and editing workflows
- **Workflow Management**: Organization, versioning, and publishing of workflow definitions
- **Instance Monitoring**: Tracking executions, status viewing, and variable inspection
- **Administration**: Configuration and settings management

## Key Concepts

**Workflows** are sequences of activities representing business processes. **Activities** serve as building blocks -- individual units of work like logging, HTTP requests, conditional logic, variable manipulation, and event triggering. Each activity has configurable properties and can produce outputs.

**Variables** store and retrieve data within workflows at the workflow level, set via activities like `SetVariable`, and referenced in expressions throughout the workflow.

**Inputs and Outputs** function as follows: workflow inputs are data passed at startup; activity inputs are configured properties. Activity outputs are named values that subsequent activities can reference, with the last executed activity's result available as `LastResult`.

**Expressions** enable dynamic property values through references to variables, activity outputs, calculations, and conditional logic. Studio supports JavaScript, C#, Liquid, and additional expression types.

## Interface Components

The sidebar provides navigation to Workflows, Workflow Instances, and Settings sections. The workflow list displays all definitions with create, edit, publish, unpublish, and delete capabilities plus version viewing.

The designer canvas includes an activity toolbox, main workspace, visual connection lines, and zoom controls. The property panel displays activity names, input properties, expression type selectors, and output configuration options.

## Getting Started Steps

1. Navigate to your Elsa Studio URL
2. Login with credentials
3. Create a new workflow via the Workflows sidebar section
4. Drag activities from the toolbox onto the canvas
5. Configure activities through the inspector panel
6. Connect activities by dragging from outcome ports
7. Save and run the workflow

## Additional Resources

Recommended learning paths include guides on expressions, custom UI components, integration with React/Angular/Blazor/MVC, workflow editor advanced features, and workflow execution methods.

## Success Tips

Use descriptive activity names for easier output referencing and workflow comprehension. Begin with simple workflows before increasing complexity. Leverage variables for improved readability and maintainability. Select appropriate expression types to avoid common configuration errors.
