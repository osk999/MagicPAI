# External Application Interaction

A common scenario is to have a separate workflow server that handles the orchestration of tasks, and a separate application that is responsible for executing these tasks.

To see how this works, we will create two ASP.NET Core Web applications that communicate with each other using **webhooks**:

* **ElsaServer**: an ASP.NET Core Web application scaffolded from [this guide](https://docs.elsaworkflows.io/application-types/elsa-server).
* **Onboarding**: another ASP.NET Core Web Application that exposes a webhook endpoint to receive events from the workflow server and provides UI to the user to view and complete tasks.

Together, the two applications implement an employee onboarding process. The role of the workflow server is to orchestrate the process, while the onboarding app is responsible for executing individual tasks requested by the workflow server to execute. The workflow server will leverage the `RunTask` activity to request tasks to be executed by the *Onboarding* app.

These tasks will be completed by a human user. As a task is marked as completed, a signal in the form of an HTTP request is sent back to the workflow server, which then proceeds to the next step in the process.

## Before you start

For this guide, we will need the following:

* An [Elsa Server](https://docs.elsaworkflows.io/application-types/elsa-server) project
* An [Elsa Studio](https://docs.elsaworkflows.io/getting-started/containers/docker#elsa-studio) container

Please return here when you are ready.

## Elsa Server

We will start by configuring the Elsa Server project with the ability to send webhook events.

### Configuring Webhooks

**Add Webhooks Package**

Add the following package to ElsaServer.csproj:

```bash
dotnet add package Elsa.Webhooks
```

**Update Program.cs**

To enable webhooks, update `Program.cs` by adding the following code to the Elsa builder delegate:

```csharp
elsa.UseWebhooks(webhooks => webhooks.ConfigureSinks += options => 
    builder.Configuration.GetSection("Webhooks")
    .Bind(options)
);
```

This will add webhook definitions from `appsettings.json`, which we configure next.

**Update appsettings.json**

Update `appsettings.json` by adding the following section:

```json
"Webhooks": {
    "Sinks": [
        {
            "Id": "1",
            "Name": "Run Task",
            "Filters": [
                {
                    "EventType": "Elsa.RunTask"
                }
            ],
            "Url": "https://localhost:5002/api/webhooks/run-task"
        }
    ]
}
```

With this setup, the workflow server will invoke the configured URL every time the `RunTask` activity executes.

## Creating the Workflow

We'll see how to create the workflow using the programmatic approach as well as using the designer.

### Programmatic

To create the Onboarding workflow, follow these steps:

**Create Workflow Class**

Create a new class called `Onboarding`:

```csharp
using Elsa.Extensions;
using Elsa.Workflows;
using Elsa.Workflows.Activities;
using Elsa.Workflows.Contracts;
using Elsa.Workflows.Runtime.Activities;
using Parallel = Elsa.Workflows.Activities.Parallel;

namespace ElsaServer.Workflows;

public class Onboarding : WorkflowBase
{
    protected override void Build(IWorkflowBuilder builder)
    {
        var employee = builder.WithVariable<object>();
        builder.Root = new Sequence
        {
            Activities =
            {
                new SetVariable
                {
                    Variable = employee,
                    Value = new(context => context.GetInput("Employee"))
                },
                new RunTask("Create Email Account")
                {
                    Payload = new(context => new Dictionary<string, object>
                    {
                        ["Employee"] = employee.Get(context)!,
                        ["Description"] = "Create an email account for the new employee."
                    })
                },
                new Parallel
                {
                    Activities =
                    {
                        new RunTask("Create Slack Account")
                        {
                            Payload = new(context => new Dictionary<string, object>
                            {
                                ["Employee"] = employee.Get(context)!,
                                ["Description"] = "Create a Slack account for the new employee."
                            })
                        },
                        new RunTask("Create GitHub Account")
                        {
                            Payload = new(context => new Dictionary<string, object>
                            {
                                ["Employee"] = employee.Get(context)!,
                                ["Description"] = "Create a GitHub account for the new employee."
                            })
                        },
                        new RunTask("Add to HR System")
                        {
                            Payload = new(context => new Dictionary<string, object>
                            {
                                ["Employee"] = employee.Get(context)!,
                                ["Description"] = "Add the new employee to the HR system."
                            })
                        }
                    }
                },
                new End()
            }
        };
    }
}
```

The above workflow will be registered with the workflow engine automatically since the Elsa Server is configured to find all workflows in the same assembly of the `Program` class.

With that in place, let's create the *Onboarding* application next.

### Designer

We will create the following workflow using Elsa Studio.

**Download and Import**

You can download the workflow and import it using Elsa Studio.

#### Designing the Workflow

Start the workflow server application and the Elsa Studio container connected to the server.

To create the workflow, follow these steps:

**Create Workflow**

* From the main menu, select **Workflows | Definitions** and click the **Create Workflow** button.
* Enter `Employee Onboarding` in the **Name** field.
* Click **OK** to create the workflow.

**Add Employee Variable**

When we execute the workflow later on, we will be sending along information about the employee to onboard.

To capture this employee input, we will store it in a variable called `Employee`.

From the **Variables** tab, create a new variable called `Employee` of type `Object`.

**Add Set Employee Activity**

From the Activity Picker, drag and drop the **Set Variable** activity on the design canvas and configure its input fields as follows:

* Variable: `Employee`
* Value:

JavaScript:
```javascript
getInput("Employee")
```

C#:
```csharp
return Input.Get("Employee");
```

**Add Create Email Account Activity**

Now it is time to create an email account for the new employee.

The workflow server itself will not perform this task; instead, it will send a webhook event to the Onboarding application that we will create later on.

To send this webhook event, we leverage the Run Task activity.

Add the **Run Task** activity to the design surface and configure it as follows:

* **Task Name**: `Create Email Account`
* **Payload**:

JavaScript:
```javascript
return { 
   employee: getEmployee(), 
   description: "Create an email account for the new employee." 
}
```

C#:
```csharp
return new
{
   Employee = Variables.Employee,
   Description = "Create an email account for the new employee." 
};
```

**Add Create Slack Account Activity**

Now that the email account has been setup for the new employee, it is time to setup their Slack account.

Just like the Create Email Account task, the workflow should send a webhook event to the Onboarding application using another Run Task activity.

Add the **Run Task** activity to the design surface and configure it as follows:

* **Task Name**: `Create Slack Account`
* **Payload**:

JavaScript:
```javascript
return {
    employee: getEmployee(),
    description: "Create a Slack account for the new employee."
}
```

C#:
```csharp
return new {
    Employee = Variables.Employee,
    Description = "Create a Slack account for the new employee."
};
```

**Add Create GitHub Account Activity**

At the same time that the Slack account is being created, the Onboarding app should be able to go ahead and create a GitHub account at the same time.

Here, too, the workflow should send a webhook event to the Onboarding application using another Run Task activity.

Add another **Run Task** activity to the design surface and configure it as follows:

* **Task Name**: `Create GitHub Account`
* **Payload**:

JavaScript:
```javascript
return {
    employee: getEmployee(),
    description: "Create a GitHub account for the new employee."
}
```

C#:
```csharp
return new {
    Employee = Variables.Employee,
    Description = "Create a GitHub account for the new employee."
};
```

**Add Add to HR System Activity**

While a Slack account and a GitHub account are being provisioned for the new employee, they should be added to the HR system.

As you might have guessed, the workflow should send a webhook event to the Onboarding application using another Run Task activity.

Add another **Run Task** activity to the design surface and configure it as follows:

* **Task Name**: `Add to HR System`
* **Payload**:

JavaScript:
```javascript
return {
    employee: getEmployee(),
    description: "Add the new employee to the HR system."
}
```

C#:
```csharp
return new {
    Employee = Variables.Employee,
    Description = "Add the new employee to the HR system."
};
```

**Add End Activity**

Although this step is optional, it is generally a good idea to be explicit and signify the end of the workflow.

**Connect Activities**

Now that we have all the pieces on the board, let's connect them together as shown in the above visual.

**Publish the Workflow**

Before we can invoke the workflow, we need to publish our changes by clicking the **Publish** button.

## Creating the Onboarding Application

To create the Onboarding application, we will create a new project based on the MVC Web Application template.

The purpose of this application is to receive webhook events from the workflow server and create Task records in the database.

The UI of the application will display a list of these tasks and allow the user to click a **Complete** button.

Upon clicking this button, the application will send an HTTP request to the workflow server to resume the Onboarding workflow.

Follow these steps to create the *Onboarding* application:

**Create Project**

Run the following command to generate a new MVC Application:

```bash
dotnet new mvc -o Onboarding
```

**Add Packages**

Navigate into the project directory:

```bash
cd Onboarding
```

Then add the following packages:

```bash
dotnet add package Microsoft.EntityFrameworkCore
dotnet add package Microsoft.EntityFrameworkCore.Sqlite
dotnet add package Microsoft.EntityFrameworkCore.Design
dotnet add package Microsoft.EntityFrameworkCore.Sqlite.Design
dotnet add package Elsa.EntityFrameworkCore
dotnet add package Elsa.EntityFrameworkCore.Sqlite
```

**Create OnboardingTask Entity**

For this application, we'll use Entity Framework Core to store the onboarding tasks in a SQLite database. First, let's model the onboarding task by creating a new class called `OnboardingTask`:

```csharp
namespace Onboarding.Entities;

/// <summary>
/// A task that needs to be completed by the user.
/// </summary>
public class OnboardingTask
{
    /// <summary>
    /// The ID of the task.
    /// </summary>
    public long Id { get; set; }

    /// <summary>
    /// An external ID that can be used to reference the task.
    /// </summary>
    public string ExternalId { get; set; } = default!;

    /// <summary>
    /// The ID of the onboarding process that the task belongs to.
    /// </summary>
    public string ProcessId { get; set; } = default!;

    /// <summary>
    /// The name of the task.
    /// </summary>
    public string Name { get; set; } = default!;

    /// <summary>
    /// The task description.
    /// </summary>
    public string Description { get; set; } = default!;

    /// <summary>
    /// The name of the employee being onboarded.
    /// </summary>
    public string EmployeeName { get; set; } = default!;

    /// <summary>
    /// The email address of the employee being onboarded.
    /// </summary>
    public string EmployeeEmail { get; set; } = default!;

    /// <summary>
    /// Whether the task has been completed.
    /// </summary>
    public bool IsCompleted { get; set; }

    /// <summary>
    /// The date and time when the task was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// The date and time when the task was completed.
    /// </summary>
    public DateTimeOffset? CompletedAt { get; set; }
}
```

**OnboardingDbContext**

Next, let's create the database context:

```csharp
using Onboarding.Entities;
using Microsoft.EntityFrameworkCore;

namespace Onboarding.Data;

public class OnboardingDbContext(DbContextOptions<OnboardingDbContext> options) : DbContext(options)
{
    public DbSet<OnboardingTask> Tasks { get; set; } = default!;
}
```

**Program.cs**

Update `Program.cs` to register the DB context with DI:

```csharp
builder.Services.AddDbContextFactory<OnboardingDbContext>(options => options.UseSqlite("Data Source=onboarding.db"));
```

**Create Migrations**

In order to have the application generate the necessary database structure automatically for us, we need to generate migration classes.

Run the following command to do so:

```bash
dotnet ef migrations add Initial
```

**Apply Migrations**

Run the following command to apply the migrations:

```bash
dotnet ef database update
```

This will apply the migration and generate the Task table in the onboarding.db SQLite database.

**Task List UI**

Now that we have our database access layer setup, let's work on the UI to display a list of tasks. For that, we will first introduce a view model called `IndexViewModel` for the `Index` action of the `homeController`:

```csharp
using Onboarding.Entities;

namespace Onboarding.Views.Home;

public class IndexViewModel(ICollection<OnboardingTask> tasks)
{
    public ICollection<OnboardingTask> Tasks { get; set; } = tasks;
}
```

**HomeController**

Update the `Index` action of the `HomeController` to use the view model:

```csharp
using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Onboarding.Data;
using Onboarding.Models;
using Onboarding.Services;
using Onboarding.Views.Home;

namespace Onboarding.Controllers;

public class HomeController(OnboardingDbContext dbContext, ElsaClient elsaClient, ILogger<HomeController> logger) : Controller
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var tasks = await dbContext.Tasks.Where(x => !x.IsCompleted).ToListAsync(cancellationToken: cancellationToken);
        var model = new IndexViewModel(tasks);
        return View(model);
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
```

**Index.cshtml**

Update the `Index.cshtml` view to display the list of tasks:

```cshtml
@model Onboarding.Views.Home.IndexViewModel
@{
    ViewData["Title"] = "Home Page";
}

<div class="text-center">
    <h1 class="display-4">Tasks</h1>
    <p>Please complete the following tasks.</p>
</div>

<div class="container">
    <table class="table table-bordered table-hover">
        <thead class="table-light">
        <tr>
            <th scope="col">Task ID</th>
            <th scope="col">Name</th>
            <th scope="col">Description</th>
            <th scope="col">Employee</th>
            <th scope="col"></th>
        </tr>
        </thead>
        <tbody>
        @foreach (var task in Model.Tasks)
        {
            <tr>

                <th scope="row">@task.Id</th>
                <td>@task.Name</td>
                <td>@task.Description</td>
                <td>@($"{task.EmployeeName} <{task.EmployeeEmail}>")</td>
                <td>
                    <form asp-action="CompleteTask">
                        <input type="hidden" name="TaskId" value="@task.Id"/>
                        <button type="submit" class="btn btn-primary">Complete</button>
                    </form>
                </td>
            </tr>
        }
        </tbody>
    </table>
</div>
```

**Handling Task Completion**

The `HomeController` is able to list pending tasks. Now, let's add another action to it that can handle the event when a user clicks the Complete button.

Add the following action method to `HomeController`:

```csharp
public async Task<IActionResult> CompleteTask(int taskId, CancellationToken cancellationToken)
{
    var task = dbContext.Tasks.FirstOrDefault(x => x.Id == taskId);

    if (task == null)
        return NotFound();

    await elsaClient.ReportTaskCompletedAsync(task.ExternalId, cancellationToken: cancellationToken);

    task.IsCompleted = true;
    task.CompletedAt = DateTimeOffset.Now;

    dbContext.Tasks.Update(task);
    await dbContext.SaveChangesAsync(cancellationToken);

    return RedirectToAction("Index");
}
```

The complete controller should look like this:

```csharp
using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Onboarding.Data;
using Onboarding.Models;
using Onboarding.Services;
using Onboarding.Views.Home;

namespace Onboarding.Controllers;

public class HomeController(OnboardingDbContext dbContext, ElsaClient elsaClient, ILogger<HomeController> logger) : Controller
{
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var tasks = await dbContext.Tasks.Where(x => !x.IsCompleted).ToListAsync(cancellationToken: cancellationToken);
        var model = new IndexViewModel(tasks);
        return View(model);
    }
    
    public async Task<IActionResult> CompleteTask(int taskId, CancellationToken cancellationToken)
    {
        var task = dbContext.Tasks.FirstOrDefault(x => x.Id == taskId);

        if (task == null)
            return NotFound();

        await elsaClient.ReportTaskCompletedAsync(task.ExternalId, cancellationToken: cancellationToken);

        task.IsCompleted = true;
        task.CompletedAt = DateTimeOffset.Now;

        dbContext.Tasks.Update(task);
        await dbContext.SaveChangesAsync(cancellationToken);

        return RedirectToAction("Index");
    }

    public IActionResult Privacy()
    {
        return View();
    }

    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}
```

The above listing uses the `ElsaClient` to report the task as completed, which we will create next.

**Elsa API Client**

To interact with the Elsa Server's REST API, we will create an HTTP client called `ElsaClient`.

Create a new class called `ElsaClient`:

```csharp
namespace Onboarding.Services;

/// <summary>
/// A client for the Elsa API.
/// </summary>
public class ElsaClient(HttpClient httpClient)
{
    /// <summary>
    /// Reports a task as completed.
    /// </summary>
    /// <param name="taskId">The ID of the task to complete.</param>
    /// <param name="result">The result of the task.</param>
    /// <param name="cancellationToken">An optional cancellation token.</param>
    public async Task ReportTaskCompletedAsync(string taskId, object? result = default, CancellationToken cancellationToken = default)
    {
        var url = new Uri($"tasks/{taskId}/complete", UriKind.Relative);
        var request = new { Result = result };
        await httpClient.PostAsJsonAsync(url, request, cancellationToken);
    }
}
```

**Register ElsaClient**

Update `Program.cs` to configure the Elsa HTTP client as follows:

```csharp
var configuration = builder.Configuration;

builder.Services.AddHttpClient<ElsaClient>(httpClient =>
{
    var url = configuration["Elsa:ServerUrl"]!.TrimEnd('/') + '/';
    var apiKey = configuration["Elsa:ApiKey"]!;
    httpClient.BaseAddress = new Uri(url);
    httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("ApiKey", apiKey);
});
```

**appsettings.json**

The Elsa configuration section used in the previous step is defined in appsettings.json as follows:

```json
{
    "Elsa": {
        "ServerUrl": "https://localhost:5001/elsa/api",
        "ApiKey": "00000000-0000-0000-0000-000000000000"
    }
}
```

**Receiving Webhooks**

Now that we have a way to display the list of task, let's setup a webhook controller that can receive tasks from the workflow server.

Create a new controller called `WebhookController`:

```csharp
using Onboarding.Data;
using Onboarding.Entities;
using Onboarding.Models;
using Microsoft.AspNetCore.Mvc;

namespace Onboarding.Controllers;

[ApiController]
[Route("api/webhooks")]
public class WebhookController(OnboardingDbContext dbContext) : Controller
{
    [HttpPost("run-task")]
    public async Task<IActionResult> RunTask(WebhookEvent webhookEvent)
    {
        var payload = webhookEvent.Payload;
        var taskPayload = payload.TaskPayload;
        var employee = taskPayload.Employee;

        var task = new OnboardingTask
        {
            ProcessId = payload.WorkflowInstanceId,
            ExternalId = payload.TaskId,
            Name = payload.TaskName,
            Description = taskPayload.Description,
            EmployeeEmail = employee.Email,
            EmployeeName = employee.Name,
            CreatedAt = DateTimeOffset.Now
        };

        await dbContext.Tasks.AddAsync(task);
        await dbContext.SaveChangesAsync();

        return Ok();
    }
}
```

The above listing uses the `WebhookEvent` model to deserialise the webhook payload. The `WebhookEvent` and related models are defined as follows:

```csharp
namespace Onboarding.Models;

public record WebhookEvent(string EventType, RunTaskWebhook Payload, DateTimeOffset Timestamp);
public record RunTaskWebhook(string WorkflowInstanceId, string TaskId, string TaskName, TaskPayload TaskPayload);
public record TaskPayload(Employee Employee, string Description);
public record Employee(string Name, string Email);
```

## Running the Onboarding Process

Now that we have both the Elsa Server and Onboarding applications ready, let's try it out.

**Start Onboarding App**

Run the Onboarding project:

```bash
dotnet run --urls=https://localhost:5002
```

**Start Onboarding Workflow**

To initiate a new execution of the Onboarding workflow, we will send an HTTP request to Elsa Server's REST API that can execute a workflow by its definition ID and receive input.

As input, we will send a small JSON payload that represents the new employee to onboard:

```bash
curl --location 'https://localhost:5001/elsa/api/workflow-definitions/{workflow_definition_id}/execute' \
        --header 'Content-Type: application/json' \
        --header 'Authorization: ApiKey 00000000-0000-0000-0000-000000000000' \
        --data-raw '{
        "input": {
            "Employee": {
            "Name": "Alice Smith",
            "Email": "alice.smith@acme.com"
        }
    }
}'
```

Make sure to replace `{workflow_definition_id}` with the actual workflow definition ID of the Onboarding workflow.

**View Tasks**

The effect of the above request is that a new task will be created in the database, which will be displayed in the web application.

**Complete Tasks**

When you click the Complete button, the task will be marked as completed in the database and the workflow will continue. When you refresh the Task list page, the task will be gone, but 3 new tasks will be created in the database.

**Workflow Completed**

Once you complete all tasks, the workflow will be completed.

## Summary

In this guide, we have seen how to set up an Elsa Server project and configure it to send webhook events to the **Onboarding** application.

We have seen how to leverage the **Run Task** activity that generates Run Task webhook events.

From the Onboarding app, we leveraged an Elsa REST API to report a given task as completed, which causes the workflow to resume.

## Source Code

The completed code for this guide can be found [here](https://github.com/elsa-workflows/elsa-guides/tree/main/src/guides/external-app-interaction).
