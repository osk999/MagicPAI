# Loading Workflows from JSON - Complete Page Content

## Overview

The documentation explains how to load workflows from JSON files, a useful approach for storing workflows in databases or file systems.

## Console Application Setup

The guide outlines a three-step process for creating a console application:

**Step 1: Create Console Project**

Initialize a new .NET 8.0 console application with necessary dependencies:

```bash
dotnet new console -n "ElsaConsole" -f net8.0
cd ElsaConsole
dotnet add package Elsa
dotnet add package Elsa.Testing.Shared.Integration
```

**Step 2: Update Program.cs**

The implementation involves:
- Setting up a service container with Elsa services
- "Populate registries. This is only necessary for applications that are not using hosted services."
- Reading the JSON file contents
- Using `IActivitySerializer` to deserialize the workflow
- Mapping the deserialized model using `WorkflowDefinitionMapper`
- Executing the workflow via `IWorkflowRunner`

**Step 3: Create Workflow JSON File**

A HelloWorld.json file defines the workflow structure with a Flowchart containing a WriteLine activity that outputs "Hello World!"

**Step 4: Run the Program**

Execute with `dotnet run` to produce the output: `Hello World!`

## Elsa Server Implementation

For Elsa Server deployments, the process is simplified -- create a *Workflows* folder and add JSON files directly. The workflow executes via API endpoint at `https://localhost:5001/elsa/api/workflow-definitions/{workflowId}/execute` or through Elsa Studio.

## Blob Storage Integration

The documentation notes that "the correct package name is `Elsa.WorkflowProviders.BlobStorage`" for cloud storage scenarios, correcting earlier references to an incorrect package name.

## Summary

The guide demonstrates configuring Elsa to host and execute workflows defined in JSON format through both console and server implementations.
