# UI Hints

## Overview
UI Hints is a feature in Elsa Studio that enables developers to define specific input editor types through the `InputAttribute` class using the `UIHint` property. The `InputUIHints` class provides access to all available built-in hints.

## Implementation Example
Developers can configure UI hints like this:

```csharp
[Input(
    Description = "Choose to download one file or entire folder",
    DefaultValue = "File",
    Options = new[] { "File", "Folder" },
    UIHint = InputUIHints.RadioList
    )]
public Input<string> SelectedRadioOption { get; set; } = default!;
```

## Available UI Hints
Elsa Studio provides the following built-in hint options:

- Checkbox
- CheckList
- CodeEditor
- DateTimePicker
- DropDown
- DynamicOutcomes
- ExpressionEditor
- HttpStatusCodes
- JsonEditor
- MultiLine
- MultiText
- OutcomePicker
- OutputPicker
- RadioList
- SingleLine
- FlowSwitchEditor
- SwitchEditor
- TypePicker
- VariablePicker
- WorkflowDefinitionPicker

These hints allow developers to customize how input fields are presented to users in the studio interface.
