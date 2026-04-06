# Workflow Editor (3.5-preview)

The workflow editor received a minor UI refresh starting with version 3.5.0.

## Reverting to Previous Design

If you prefer the earlier appearance, you can modify your Blazor project's Program.cs file. The configuration involves registering the V1 activity wrapper component instead of the default V2 version:

```csharp
builder.Services.AddServerSideBlazor(options =>
{
    // Comment out the V2 activity wrapper (default).
    //options.RootComponents.RegisterCustomElsaStudioElements();
    
    // To use V1 activity wrapper layout, specify the V1 component instead:
    options.RootComponents.RegisterCustomElsaStudioElements(typeof(Elsa.Studio.Workflows.Designer.Components.ActivityWrappers.V1.EmbeddedActivityWrapper));
    
    options.RootComponents.MaxJSRootComponents = 1000;
});

// Add this for the V1 designer theme (default is V2).
builder.Services.Configure<DesignerOptions>(options =>
{
    options.DesignerCssClass = "elsa-flowchart-diagram-designer-v1";
    options.GraphSettings.Grid.Type = "mesh";
});
```

Additionally, update your _Host.cshtml (or index.html for WebAssembly) file's `<head>` section to reference the older stylesheet:

```cshtml
@* Comment out the V2 designer.css. *@
@* <link href="_content/Elsa.Studio.Workflows.Designer/designer.css" rel="stylesheet"> *@
    
@* To use designer.v1.css for the old designer. *@
<link href="_content/Elsa.Studio.Workflows.Designer/designer.v1.css" rel="stylesheet">
```

These modifications will restore the previous design appearance.
