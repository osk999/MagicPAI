# Elsa Workflows: Plugins & Modules - Complete Content

## Overview

Elsa Workflows provides extensibility through three core concepts: **Modules** (feature containers), **Features** (self-contained functionality units), and **Activities** (workflow building blocks). This architecture enables domain-specific activity creation, reusable NuGet package distribution, clean separation of concerns, and fluent API configuration.

## Table of Contents

* [Key Concepts](#key-concepts)
  * [Modules & Features](#modules--features)
  * [Activity Discovery & Registration](#activity-discovery--registration)
* [Creating a Custom Feature](#creating-a-custom-feature)
  * [Step 1: Define Your Feature Class](#step-1-define-your-feature-class)
  * [Step 2: Configure Services](#step-2-configure-services)
  * [Step 3: Create Extension Methods](#step-3-create-extension-methods)
  * [Step 4: Register Your Feature](#step-4-register-your-feature)
* [Creating Custom Activities](#creating-custom-activities)
  * [Basic Activity Structure](#basic-activity-structure)
  * [Defining Inputs and Outputs](#defining-inputs-and-outputs)
  * [Activity Attributes](#activity-attributes)
  * [Registering Activities](#registering-activities)
* [Packaging & Distribution](#packaging--distribution)
* [Advanced Topics](#advanced-topics)
* [Complete Examples](#complete-examples)

## Key Concepts

### Modules & Features

The `IModule` interface represents a feature container, while `FeatureBase` is the foundation for all features. Features encapsulate related functionality, register dependency injection services, configure workflow options, and follow two-phase initialization.

**Lifecycle Methods:**

1. **Configure()**: Called during startup to register services, activities, workflows, and options
2. **Apply()**: Called post-configuration for tasks depending on other features, validation, and complex initialization

**UseXyz() Pattern:** Elsa conventions specify features enable via `UseXyz()` extension methods, providing fluent, discoverable APIs with optional lambda configuration and method chaining support.

Example usage:

```csharp
builder.Services.AddElsa(elsa => elsa
    .UseMyFeature()
    .UseAnotherFeature(feature =>
    {
        // Configure the feature
    })
);
```

### Activity Discovery & Registration

**AddActivitiesFrom()**: Scans the assembly containing type `T` and registers all `[Activity]`-marked classes with the activity registry, making them available in the workflow designer.

```csharp
Module.AddActivitiesFrom<MyFeature>();
```

**AddWorkflowsFrom()**: Similar functionality for workflow definition registration.

## Creating a Custom Feature

### Step 1: Define Your Feature Class

```csharp
using Elsa.Features.Abstractions;
using Elsa.Features.Services;
using Microsoft.Extensions.DependencyInjection;

namespace MyWorkflows.Features;

public class MyFeature : FeatureBase
{
    public MyFeature(IModule module) : base(module)
    {
    }

    public override void Configure()
    {
        // Configuration goes here
    }

    public override void Apply()
    {
        // Post-configuration goes here (optional)
    }
}
```

Key points: Constructor must accept `IModule`, override `Configure()` for service registration, and `Apply()` only when post-configuration logic is necessary.

### Step 2: Configure Services

Within `Configure()`, register activities and services:

```csharp
public override void Configure()
{
    // Register activities from this assembly
    Module.AddActivitiesFrom<MyFeature>();
    
    // Register custom services
    Services.AddSingleton<IMyCustomService, MyCustomService>();
    Services.AddScoped<IMyRepository, MyRepository>();
    
    // Configure workflow options
    Module.ConfigureWorkflowOptions(options =>
    {
        // Register UI hint handlers
        options.RegisterUIHintHandler<MyCustomUIHintHandler>("MyCustomHint");
    });
}
```

Available registration methods include `Module.AddActivitiesFrom<T>()`, `Module.AddWorkflowsFrom<T>()`, `Services.Add...()` for custom registrations, and `Module.ConfigureWorkflowOptions()` for workflow-specific settings.

### Step 3: Create Extension Methods

```csharp
using Elsa.Features.Services;
using MyWorkflows.Features;

namespace MyWorkflows.Extensions;

public static class ModuleExtensions
{
    public static IModule UseMyFeature(
        this IModule module, 
        Action<MyFeature>? configure = null)
    {
        module.Use(configure);
        return module;
    }
}
```

**Pattern with Options:**

```csharp
public static class ModuleExtensions
{
    public static IModule UseMyFeature(
        this IModule module, 
        Action<MyFeatureOptions>? configure = null)
    {
        return module.Use<MyFeature>(feature =>
        {
            if (configure != null)
            {
                var options = new MyFeatureOptions();
                configure(options);
                
                // Apply options to feature properties or services
                if (options.EnableAdvancedFeatures)
                {
                    feature.Services.AddSingleton<IAdvancedService, AdvancedService>();
                }
            }
        });
    }
}

public class MyFeatureOptions
{
    public bool EnableAdvancedFeatures { get; set; } = false;
    public string? ApiKey { get; set; }
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);
}
```

### Step 4: Register Your Feature

In `Program.cs` or `Startup.cs`:

```csharp
var builder = WebApplication.CreateBuilder(args);

builder.Services.AddElsa(elsa => elsa
    .UseMyFeature()
    // Or with configuration:
    .UseMyFeature(options =>
    {
        options.EnableAdvancedFeatures = true;
        options.ApiKey = builder.Configuration["MyFeature:ApiKey"];
    })
);
```

## Creating Custom Activities

Custom activities extend workflow functionality. See referenced examples for complete implementations.

### Basic Activity Structure

Activities inherit from `CodeActivity` or `CodeActivity<T>` for activities with outputs:

```csharp
using Elsa.Workflows;
using Elsa.Workflows.Attributes;

[Activity("MyWorkflows", "Sample", "Description of what this activity does")]
public class SampleActivity : CodeActivity<string>
{
    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        // Activity logic here
        
        // Set output if using CodeActivity<T>
        context.Set(Result, "output value");
        
        // Complete the activity
        await context.CompleteActivityAsync();
    }
}
```

Base classes: `CodeActivity` (no return value), `CodeActivity<T>` (single output), and `Activity` (complex behavior).

### Defining Inputs and Outputs

Use `[Input]` and `[Output]` attributes:

```csharp
[Activity("MyWorkflows", "Data", "Processes a message with optional prefix")]
public class ProcessMessage : CodeActivity<string>
{
    [Input(Description = "The message to process")]
    public Input<string> Message { get; set; } = default!;

    [Input(
        Description = "Optional prefix to prepend", 
        DefaultValue = "INFO")]
    public Input<string?> Prefix { get; set; } = default!;

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var message = context.Get(Message);
        var prefix = context.Get(Prefix);
        
        var result = $"{prefix}: {message}";
        
        context.Set(Result, result);
        await context.CompleteActivityAsync();
    }
}
```

Features include `Description`, `DefaultValue`, `UIHint` for custom property editors, and `Category` for designer grouping.

### Activity Attributes

The `[Activity]` attribute configures designer appearance:

```csharp
[Activity(
    Namespace = "MyCompany.MyProduct",    // Logical grouping
    Category = "Integration",              // Designer category
    Description = "Detailed description", // Shown in tooltips
    DisplayName = "My Activity"           // Display name (optional)
)]
public class MyActivity : CodeActivity
{
    // ...
}
```

Parameters: **Namespace** (logical grouping), **Category** (designer organization), **Description** (help text), **DisplayName** (designer override).

### Registering Activities

Register via features:

```csharp
public override void Configure()
{
    // Registers all activities in the assembly containing MyFeature
    Module.AddActivitiesFrom<MyFeature>();
}
```

This automatically discovers `[Activity]`-marked types and registers them with the activity registry, making them available in the designer, programmatic definitions, and execution engine.

## Packaging & Distribution

Share custom modules as NuGet packages:

### 1. Create a Class Library Project

```bash
dotnet new classlib -n MyWorkflows.Extensions
cd MyWorkflows.Extensions
dotnet add package Elsa
dotnet add package Elsa.Workflows.Core
```

### 2. Organize Your Code

```
MyWorkflows.Extensions/
├── Activities/
│   ├── SampleActivity.cs
│   └── AnotherActivity.cs
├── Features/
│   └── MyFeature.cs
├── Extensions/
│   └── ModuleExtensions.cs
└── MyWorkflows.Extensions.csproj
```

### 3. Configure the .csproj File

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <PackageId>MyCompany.MyWorkflows.Extensions</PackageId>
    <Version>1.0.0</Version>
    <Authors>Your Name</Authors>
    <Description>Custom Elsa Workflows extensions</Description>
    <PackageTags>elsa;workflows;extensions</PackageTags>
    <RepositoryUrl>https://github.com/yourorg/yourrepo</RepositoryUrl>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Elsa" Version="3.0.*" />
    <PackageReference Include="Elsa.Workflows.Core" Version="3.0.*" />
  </ItemGroup>
</Project>
```

### 4. Build and Publish

```bash
dotnet pack -c Release
dotnet nuget push bin/Release/MyCompany.MyWorkflows.Extensions.1.0.0.nupkg \
  --api-key YOUR_API_KEY \
  --source https://api.nuget.org/v3/index.json
```

### 5. Consume the Package

```bash
dotnet add package MyCompany.MyWorkflows.Extensions
```

```csharp
builder.Services.AddElsa(elsa => elsa
    .UseMyFeature()
);
```

## Advanced Topics

### Custom UI Hint Handlers

Control property editing in the workflow designer:

```csharp
public class MyCustomUIHintHandler : IUIHintHandler
{
    public string UIHint => "MyCustomHint";

    public object GetDefaultValue()
    {
        return new MyCustomData();
    }
}
```

Register in your feature:

```csharp
Module.ConfigureWorkflowOptions(options =>
{
    options.RegisterUIHintHandler<MyCustomUIHintHandler>("MyCustomHint");
});
```

### Custom Serializers

Implement serializers for complex data types:

```csharp
public class MyTypeSerializer : ISerializer
{
    public object Deserialize(string data)
    {
        // Deserialization logic
    }

    public string Serialize(object obj)
    {
        // Serialization logic
    }
}
```

Register in your feature:

```csharp
Services.AddSingleton<ISerializer, MyTypeSerializer>();
```

### Activity Execution Context

The `ActivityExecutionContext` provides access to workflow instance state, input/output management, journal logging, cancellation handling, and the dependency injection container:

```csharp
protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
{
    // Access workflow variables
    var workflowVar = context.GetWorkflowVariable<string>("MyVar");
    
    // Access services
    var myService = context.GetRequiredService<IMyService>();
    
    // Log to journal
    context.JournalData.Add("CustomKey", "CustomValue");
    
    // Check cancellation
    if (context.CancellationToken.IsCancellationRequested)
        return;
    
    // ... activity logic
}
```

## Complete Examples

Working examples are available at:

* SampleActivity.cs - Full custom activity with inputs and outputs
* MyFeature.cs - Complete feature implementation
* ModuleExtensions.cs - Extension methods following Elsa conventions

## Best Practices

1. **Follow Naming Conventions**
   * Use `UseXyz()` for feature extension methods
   * Name features as `XyzFeature`
   * Use clear, descriptive activity names

2. **Provide Good Metadata**
   * Use descriptive `[Activity]` attributes
   * Add meaningful descriptions to inputs/outputs
   * Include usage examples in XML comments

3. **Handle Errors Gracefully**
   * Validate inputs in activities
   * Provide helpful error messages
   * Consider retry logic for transient failures

4. **Test Thoroughly**
   * Unit test activities independently
   * Integration test features
   * Test with the workflow designer

5. **Document Your Extensions**
   * Include XML documentation comments
   * Provide usage examples
   * Document configuration options

## Further Reading

* Custom Activities Guide - Detailed activity creation guide
* Elsa Core Repository - Official source code and examples
* Feature Documentation - Built-in features reference

## Support

* GitHub Discussions - Community support forum
* GitHub Issues - Bug reports and feature requests
* Official Documentation - Comprehensive guides
