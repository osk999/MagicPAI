# Elsa Studio Setup Guide

Elsa Studio is a Blazor-based SPA for managing workflows through a user interface, connecting to an Elsa Server backend.

## Installation Steps

**Create the Project**
Initialize a new Blazor WebAssembly application with: `dotnet new blazorwasm -n "ElsaStudioBlazorWasm"`

**Add Required Packages**
Install four NuGet packages: Elsa.Studio, Elsa.Studio.Core.BlazorWasm, Elsa.Studio.Login.BlazorWasm, and Elsa.Api.Client.

**Configure Program.cs**
The main configuration file requires importing multiple Elsa namespaces and registering services like AddCore(), AddShell(), AddRemoteBackend(), and AddWorkflowsModule(). A BackendApiConfig object specifies API endpoint details and authentication handling.

**Clean Up Project Structure**
Remove the default Blazor directories (wwwroot/css, Pages) and files (App.razor, MainLayout.razor, _Imports.razor).

**Create Settings File**
Add wwwroot/appsettings.json with backend URL configuration pointing to "https://localhost:5001/elsa/api"

**Update HTML Entry Point**
Replace index.html with content that references Elsa Studio stylesheets and JavaScript libraries (MudBlazor, Monaco Editor, Radzen components).

## Running the Application

Execute `dotnet run --urls https://localhost:6001` to launch the application at https://localhost:6001. Default credentials are username "admin" and password "password".

The complete implementation source code is available in the [official GitHub repository](https://github.com/elsa-workflows/elsa-guides/tree/main/src/installation/elsa-studio/ElsaStudioBlazorWasm).
