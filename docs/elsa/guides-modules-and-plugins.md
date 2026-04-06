# Elsa Workflows v3 Extensibility Guide - Complete Content

## Overview

Elsa Workflows v3 features a "powerful **module and plugin architecture**" enabling custom functionality extension through modules containing activities, services, and API endpoints.

## Module Fundamentals

**What constitutes a module:**

A module represents "a logical unit that groups related functionality together," functioning as an installable plugin. Key attributes include self-containment, composability, configurability, and discoverability following consistent patterns.

**Module vs. Feature distinction:**

| Element | Role | Instance |
|---------|------|----------|
| Module | Container via `IModule` | Elsa configuration object |
| Feature | Self-contained unit from `FeatureBase` | `HttpFeature`, `EmailFeature` |

## Registration Process

Modules initialize through `AddElsa()` during startup:

```csharp
builder.Services.AddElsa(elsa => elsa
    .UseWorkflowRuntime()
    .UseHttp()
    .UseEmail()
    .UseJavaScript()
    .UseMyCustomModule()
);
```

Each `UseXyz()` method creates/retrieves feature instances, configures them, registers services and activities, and enables method chaining.

## Module Contributions

Modules provide three primary contribution types:

1. **Activities** - Custom workflow designer components
2. **Services** - Dependency injection-registered services
3. **API Endpoints** - REST extensions for Elsa Server

## Creating Custom Modules: Complete Example

### Feature Class Implementation

```csharp
using Elsa.Features.Abstractions;
using Elsa.Features.Services;
using Microsoft.Extensions.DependencyInjection;

namespace MyCompany.Elsa.Reporting.Features;

public class ReportingFeature : FeatureBase
{
    public ReportingFeature(IModule module) : base(module)
    {
    }

    public override void Configure()
    {
        Module.AddActivitiesFrom<ReportingFeature>();
        Services.AddSingleton<IReportGenerator, ReportGenerator>();
        Services.AddScoped<IReportRepository, ReportRepository>();
        
        Module.ConfigureWorkflowOptions(options =>
        {
        });
    }

    public override void Apply()
    {
    }
}
```

### Custom Activity Example

```csharp
using Elsa.Workflows;
using Elsa.Workflows.Attributes;
using Elsa.Workflows.Models;

namespace MyCompany.Elsa.Reporting.Activities;

[Activity(
    Namespace = "MyCompany.Reporting",
    Category = "Reporting",
    Description = "Generates a report and stores it")]
public class GenerateReport : CodeActivity<string>
{
    private readonly IReportGenerator _reportGenerator;

    public GenerateReport(IReportGenerator reportGenerator)
    {
        _reportGenerator = reportGenerator;
    }

    [Input(
        Description = "The name of the report to generate",
        UIHint = "single-line")]
    public Input<string> ReportName { get; set; } = default!;

    [Input(
        Description = "Data to include in the report as JSON",
        UIHint = "multi-line")]
    public Input<string?> Data { get; set; } = default!;

    protected override async ValueTask ExecuteAsync(ActivityExecutionContext context)
    {
        var reportName = context.Get(ReportName);
        var data = context.Get(Data) ?? "{}";
        
        var reportId = await _reportGenerator.GenerateAsync(reportName, data);
        
        context.Set(Result, reportId);
        
        context.JournalData.Add("ReportId", reportId);
        context.JournalData.Add("ReportName", reportName);
    }
}
```

### Service Implementation

```csharp
namespace MyCompany.Elsa.Reporting.Services;

public interface IReportGenerator
{
    Task<string> GenerateAsync(string reportName, string data);
}

public class ReportGenerator : IReportGenerator
{
    private readonly IReportRepository _repository;
    private readonly ILogger<ReportGenerator> _logger;

    public ReportGenerator(
        IReportRepository repository,
        ILogger<ReportGenerator> logger)
    {
        _repository = repository;
        _logger = logger;
    }

    public async Task<string> GenerateAsync(string reportName, string data)
    {
        _logger.LogInformation("Generating report: {ReportName}", reportName);
        
        var reportId = Guid.NewGuid().ToString();
        
        var report = new Report
        {
            Id = reportId,
            Name = reportName,
            Data = data,
            GeneratedAt = DateTime.UtcNow
        };
        
        await _repository.SaveAsync(report);
        
        _logger.LogInformation("Report generated: {ReportId}", reportId);
        
        return reportId;
    }
}

public interface IReportRepository
{
    Task SaveAsync(Report report);
    Task<Report?> GetByIdAsync(string id);
}

public class ReportRepository : IReportRepository
{
    private readonly Dictionary<string, Report> _reports = new();

    public Task SaveAsync(Report report)
    {
        _reports[report.Id] = report;
        return Task.CompletedTask;
    }

    public Task<Report?> GetByIdAsync(string id)
    {
        _reports.TryGetValue(id, out var report);
        return Task.FromResult(report);
    }
}

public class Report
{
    public string Id { get; set; } = default!;
    public string Name { get; set; } = default!;
    public string Data { get; set; } = default!;
    public DateTime GeneratedAt { get; set; }
}
```

### API Endpoints

```csharp
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace MyCompany.Elsa.Reporting.Endpoints;

public static class ReportingEndpoints
{
    public static IEndpointRouteBuilder MapReportingEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/reporting");
        
        group.MapGet("/health", () => Results.Ok(new 
        { 
            module = "reporting",
            status = "healthy",
            timestamp = DateTime.UtcNow
        }))
        .WithName("ReportingHealth")
        .WithTags("Reporting");
        
        group.MapGet("/reports/{id}", async (
            string id,
            IReportRepository repository) =>
        {
            var report = await repository.GetByIdAsync(id);
            return report != null 
                ? Results.Ok(report) 
                : Results.NotFound();
        })
        .WithName("GetReport")
        .WithTags("Reporting");
        
        return endpoints;
    }
}
```

Feature update for endpoints:

```csharp
public override void Apply()
{
    Services.Configure<WebApplicationOptions>(options =>
    {
    });
}
```

Program.cs integration:

```csharp
var app = builder.Build();

app.UseWorkflowsApi();

app.MapReportingEndpoints();

app.Run();
```

### Extension Methods

```csharp
using Elsa.Features.Services;
using MyCompany.Elsa.Reporting.Features;

namespace MyCompany.Elsa.Reporting.Extensions;

public static class ReportingModuleExtensions
{
    public static IModule UseReporting(
        this IModule module,
        Action<ReportingFeature>? configure = null)
    {
        module.Use(configure);
        return module;
    }
}
```

### Module Usage

```csharp
using MyCompany.Elsa.Reporting.Extensions;
using MyCompany.Elsa.Reporting.Endpoints;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddElsa(elsa => elsa
    .UseWorkflowRuntime()
    .UseReporting()
);

var app = builder.Build();

app.UseWorkflowsApi();
app.MapReportingEndpoints();

app.Run();
```

## Advanced Configuration

### Module Options Pattern

```csharp
public class ReportingOptions
{
    public string StoragePath { get; set; } = "./reports";
    public int MaxReportSizeMb { get; set; } = 50;
    public bool EnableCompression { get; set; } = true;
}

public class ReportingFeature : FeatureBase
{
    public ReportingOptions Options { get; set; } = new();

    public ReportingFeature(IModule module) : base(module)
    {
    }

    public override void Configure()
    {
        Module.AddActivitiesFrom<ReportingFeature>();
        
        Services.AddSingleton(Options);
        Services.AddSingleton<IReportGenerator, ReportGenerator>();
    }
}

public static IModule UseReporting(
    this IModule module,
    Action<ReportingOptions>? configure = null)
{
    return module.Use<ReportingFeature>(feature =>
    {
        if (configure != null)
        {
            configure(feature.Options);
        }
    });
}

// Usage
builder.Services.AddElsa(elsa => elsa
    .UseReporting(options =>
    {
        options.StoragePath = "/data/reports";
        options.MaxReportSizeMb = 100;
        options.EnableCompression = false;
    })
);
```

## Discovery Pattern

Modules follow consistent conventions:

**Naming:**
- Feature: `XyzFeature`
- Extension: `UseXyz()`
- Options: `XyzOptions`

**Registration Flow:**
```
UseXyz() -> Creates/Configures Feature -> Feature.Configure() 
-> Registers Services/Activities -> Feature.Apply()
```

**Method Chaining:**
```csharp
.UseWorkflowRuntime()
.UseHttp()
.UseEmail()
.UseReporting()
```

## Recommended Project Structure

```
MyCompany.Elsa.Reporting/
├── Activities/
│   ├── GenerateReport.cs
│   └── ExportReport.cs
├── Features/
│   ├── ReportingFeature.cs
│   └── ReportingOptions.cs
├── Services/
│   ├── IReportGenerator.cs
│   ├── ReportGenerator.cs
│   ├── IReportRepository.cs
│   └── ReportRepository.cs
├── Endpoints/
│   └── ReportingEndpoints.cs
├── Extensions/
│   └── ReportingModuleExtensions.cs
└── Models/
    └── Report.cs
```

## Best Practices

### 1. Naming Conventions
- Maintain `XyzFeature` format for features
- Use `UseXyz()` for extension methods
- Apply `XyzOptions` for configuration

### 2. Minimal Dependencies
- Reference only necessary Elsa packages
- Keep third-party dependencies lean
- Use interfaces for external dependencies

### 3. Configuration Priority
- Establish sensible defaults
- Enable configuration via options
- Document configuration properties thoroughly

### 4. Documentation Standards
- Implement XML documentation for public APIs
- Include examples in feature descriptions
- Document all activity inputs and outputs

### 5. Testing Strategy
- Unit test activities independently
- Integration test features
- Test multiple configurations

## NuGet Packaging

### Project File Configuration

```xml
<!-- MyCompany.Elsa.Reporting.csproj -->
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net8.0</TargetFramework>
    <PackageId>MyCompany.Elsa.Reporting</PackageId>
    <Version>1.0.0</Version>
    <Authors>Your Name</Authors>
    <Description>Reporting module for Elsa Workflows</Description>
    <PackageTags>elsa;workflows;reporting</PackageTags>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Elsa" Version="3.0.*" />
    <PackageReference Include="Elsa.Workflows.Core" Version="3.0.*" />
    <PackageReference Include="Elsa.Workflows.Runtime" Version="3.0.*" />
  </ItemGroup>
</Project>
```

### Publishing Commands

```bash
dotnet pack -c Release
dotnet nuget push bin/Release/MyCompany.Elsa.Reporting.1.0.0.nupkg
```

## Real-World Examples from Elsa

### HTTP Feature Configuration

```csharp
builder.Services.AddElsa(elsa => elsa
    .UseHttp(http => 
    {
        http.ConfigureHttpOptions(options =>
        {
            options.BaseUrl = new Uri("https://api.example.com");
        });
    })
);
```

### Email Feature Configuration

```csharp
builder.Services.AddElsa(elsa => elsa
    .UseEmail(email =>
    {
        email.ConfigureOptions(options =>
        {
            options.SmtpHost = "smtp.example.com";
            options.SmtpPort = 587;
        });
    })
);
```

### MassTransit Feature Configuration

```csharp
builder.Services.AddElsa(elsa => elsa
    .UseMassTransit(mt =>
    {
        mt.UseRabbitMq("amqp://localhost");
    })
);
```

## Additional Resources

- **Custom Activities** - Detailed activity creation guidance
- **Plugins & Modules** - Extended examples and patterns
- **Architecture Overview** - Elsa structural understanding
- **HTTP Workflows** - HTTP module implementation reference

## Key Takeaways

Creating custom Elsa v3 modules involves:

1. **Feature Creation** - Inheriting from `FeatureBase`
2. **Component Registration** - Adding activities, services, configuration
3. **Extension Method Development** - Following `UseXyz()` convention
4. **Distribution** - Sharing via NuGet packages

This modular architecture "makes it easy to extend the framework with custom functionality," allowing domain-specific or integration-focused extensions while maintaining "consistency with the rest of the Elsa ecosystem."
