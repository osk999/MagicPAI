// Resolve ambiguity between Microsoft.AspNetCore.Http.Endpoint
// and Elsa.Workflows.Activities.Flowchart.Models.Endpoint
// (occurs because workflow files moved from classlib to web project)
global using Endpoint = Elsa.Workflows.Activities.Flowchart.Models.Endpoint;
