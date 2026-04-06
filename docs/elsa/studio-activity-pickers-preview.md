# Activity Pickers (3.7-preview)

## Overview

The documentation describes methods for selecting different activity picker interfaces within the workflow editor. Two options are currently available: Accordion (the default choice) and Treeview.

## Accordion

Implementation requires this service registration:

```csharp
builder.Services.AddScoped<IActivityPickerComponentProvider, AccordionActivityPickerComponentProvider>();
```

The accordion style presents activities in a collapsible format. When working with hierarchical activity groupings, the default behavior extracts the initial category from a delimited string. You can modify this by implementing a custom `CategoryDisplayResolver`:

```csharp
builder.Services.AddScoped<IActivityPickerComponentProvider>(sp => new AccordionActivityPickerComponentProvider
{
    // Example - Replace the default category resolver with a custom one.
    CategoryDisplayResolver = category => category.Split('/').Last().Trim()
});
```

## Treeview

Implementing the TreeView picker requires this registration:

```csharp
builder.Services.AddScoped<IActivityPickerComponentProvider, TreeviewActivityPickerComponentProvider>();
```

The treeview variant organizes activities in an expandable hierarchical structure, offering an alternative visual presentation to the accordion format.
