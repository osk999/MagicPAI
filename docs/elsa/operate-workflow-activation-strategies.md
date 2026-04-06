# Activation Strategies

Workflows in Elsa can be configured with an **activation strategy** that determines execution eligibility. The *Always* strategy permits unrestricted workflow execution, whereas the *Singleton* strategy restricts execution when an instance is already *Running*.

Elsa provides four built-in activation strategies:

| Strategy | Description |
|----------|-------------|
| Always | Allow workflow execution without restrictions. |
| Singleton | Prevent execution if a workflow instance is already running. |
| Correlation | Prevent execution with a given correlation ID if another workflow shares that ID. |
| Correlated Singleton | Prevent execution with a given correlation ID if the same workflow instance shares that ID. |
