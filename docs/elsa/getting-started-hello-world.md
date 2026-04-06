# Hello World

## Console

### 1. Create Console App

```bash
dotnet new console -n "ElsaConsole"
```

### 2. Add Packages

```bash
cd ElsaConsole
dotnet add package Elsa
```

### 3. Modify Program.cs

```csharp
using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Activities;
using Microsoft.Extensions.DependencyInjection;

// Setup service container.
var services = new ServiceCollection();

// Add Elsa services to the container.
services.AddElsa();

// Build the service container.
var serviceProvider = services.BuildServiceProvider();

// Define a simple workflow with multiple activities.
var workflow = new Sequence
{
    Activities =
    {
        new WriteLine("Hello World!"),
        new WriteLine("We can do more than a one-liner!")
    }
};

// Resolve a workflow runner to execute the workflow.
var workflowRunner = serviceProvider.GetRequiredService<IWorkflowRunner>();

// Run the workflow.
await workflowRunner.RunAsync(workflow);
```

## ASP.NET Core

### 1. Create the Project

```bash
dotnet new web -n "ElsaWeb"
```

### 2. Add Packages

```bash
cd ElsaWeb
dotnet add package Elsa
dotnet add package Elsa.Http
```

### 3. Modify Program.cs

```csharp
using Elsa.Extensions;
using ElsaWeb.Workflows;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddElsa(elsa =>
{
    elsa.AddWorkflow<HttpHelloWorld>();
    elsa.UseHttp(http => http.ConfigureHttpOptions = options =>
    {
        options.BaseUrl = new Uri("https://localhost:5001");
        options.BasePath = "/workflows";
    });
});

var app = builder.Build();

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();
app.UseWorkflows();
app.Run();
```

### 4. Add HttpHelloWorld Workflow

**Workflows/HttpHelloWorld.cs**

```csharp
using Elsa.Http;
using Elsa.Workflows;
using Elsa.Workflows.Activities;
using Elsa.Workflows.Contracts;

namespace ElsaWeb.Workflows;

public class HttpHelloWorld : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        var queryStringsVariable = builder.WithVariable<IDictionary<string, object>>();
        var messageVariable = builder.WithVariable<string>();

        builder.Root = new Sequence
        {
            Activities =
            {
                new HttpEndpoint
                {
                    Path = new("/hello-world"),
                    CanStartWorkflow = true,
                    QueryStringData = new(queryStringsVariable)
                },
                new SetVariable
                {
                    Variable = messageVariable,
                    Value = new(context =>
                    {
                        var queryStrings = queryStringsVariable.Get(context)!;
                        var message = queryStrings.TryGetValue("message", out var messageValue) ? messageValue.ToString() : "Hello world of HTTP workflows!";
                        return message;
                    })
                },
                new WriteHttpResponse
                {
                    Content = new(messageVariable)
                }
            }
        };
    }
}
```

## Source Code

* [Console app](https://github.com/elsa-workflows/elsa-guides/tree/main/src/installation/elsa-console)
* [ASP.NET Core app](https://github.com/elsa-workflows/elsa-guides/tree/main/src/installation/elsa-web)
