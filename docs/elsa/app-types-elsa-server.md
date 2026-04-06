# Elsa Server Setup Guide

## Overview

An Elsa Server is "an ASP.NET Core web application that lets you manage workflows using a REST API and execute them." It supports storing workflows across "databases, file systems, or even cloud storage."

## Installation Steps

The setup process involves four main phases:

**1. Project Creation**
Initialize a new ASP.NET web project using the dotnet CLI with the command `dotnet new web -n "ElsaServer"`.

**2. Navigation**
Move into the newly created directory with `cd ElsaServer`.

**3. Package Installation**
Add twelve NuGet packages including core Elsa libraries, Entity Framework Core providers, HTTP activities, scheduling capabilities, and language support (C#, JavaScript, Liquid).

**4. Configuration**
Replace the Program.cs file with provided boilerplate code that establishes:
- Entity Framework Core integration with SQLite
- Identity and authentication management
- REST API endpoint exposure
- Real-time SignalR communication
- Workflow expression support
- HTTP activity configuration
- CORS policies for cross-origin requests
- Health check endpoints

## Running the Application

Execute `dotnet run --urls "https://localhost:5001"` to launch the server on the specified HTTPS endpoint.

## Additional Resources

Complete source code is available through the official Elsa Guides repository on GitHub.
