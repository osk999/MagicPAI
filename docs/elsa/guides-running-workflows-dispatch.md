# Dispatch Workflow Activity

The **Dispatch Workflow** activity enables initiating a new workflow from within an existing workflow.

## Key Capabilities

This activity allows you to:
- Specify which workflow should execute
- Provide necessary input parameters
- Optionally wait for completion
- Capture the child workflow's output

## Implementation Example

### Parent Workflow

```csharp
using Elsa.Workflows;
using Elsa.Workflows.Activities;
using Elsa.Workflows.Runtime.Activities;

public class ParentWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        var childOutput = builder.WithVariable<IDictionary<string, object>>();

        builder.Root = new Sequence
        {
            Activities =
            {
                new DispatchWorkflow
                {
                    WorkflowDefinitionId = new(nameof(ChildWorkflow)),
                    Input = new(new Dictionary<string, object>
                    {
                        ["ParentMessage"] = "Hello from parent!"
                    }),
                    WaitForCompletion = new(true),
                    Result = new(childOutput)
                },
                new WriteLine(context => $"Child finished executing and said: {childOutput.Get(context)!["ChildMessage"]}")
            }
        };
    }
}
```

### Child Workflow

```csharp
using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Activities;
using Elsa.Workflows.Management.Activities.SetOutput;

namespace Elsa.Samples.AspNet.ChildWorkflows.Workflows;

public class ChildWorkflow : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        builder.Root = new Sequence
        {
            Activities =
            {
                new WriteLine(context => $"Input from parent: \"{context.GetInput<string>("ParentMessage")}\"."),
                new SetOutput
                {
                    OutputName = new("ChildMessage"),
                    OutputValue = new("Hello from child!")
                }
            }
        };
    }
}
```

## Visual Configuration

Step-by-step video guides demonstrate creating child and parent workflows using Elsa Studio's interface for dispatching child workflow execution.
