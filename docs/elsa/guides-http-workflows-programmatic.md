# Programmatic Workflow Guide - Complete Content

## Before you start

To follow this guide, you need:
* An Elsa Server project

## Workflow Overview

The guide walks through creating a `GetUser` workflow that handles inbound HTTP requests. The workflow retrieves user data by ID from a backend API and returns it in JSON format.

The example uses [reqres.in](https://reqres.in/) as a fake backend API. The workflow functions as a proxy, accepting requests at a route parameter path, extracting the user ID, making an API call to reqres, and returning the relevant data portion.

Sample API response from reqres:
```json
{
    "data": {
        "id": 2,
        "email": "janet.weaver@reqres.in",
        "first_name": "Janet",
        "last_name": "Weaver",
        "avatar": "https://reqres.in/img/faces/2-image.jpg"
    },
    "support": {
        "url": "https://reqres.in/#support-heading",
        "text": "To keep ReqRes free, contributions towards server costs are appreciated!"
    }
}
```

## Create C# Workflow

### Step 1: Create Workflow

Create `GetUser.cs` with the provided C# code shown in the source document.

### Workflow Variables

Three variables are defined:
- `routeDataVariable`: Captures route data from the HTTP endpoint (dictionary type)
- `userIdVariable`: Stores the extracted user ID (string type)
- `userVariable`: Captures the parsed JSON response (ExpandoObject type)

### HttpEndpoint Activity

This activity is configured as a workflow trigger with:
- Path: `users/{userid}`
- Supported method: GET
- `CanStartWorkflow`: true
- Route parameter capture for the `userid` key

### SetVariable Activity

Extracts the user ID from the route data dictionary and assigns it to `userIdVariable`.

### SendHttpRequest Activity

Sends a GET request to reqres API using the extracted user ID. It handles two response scenarios:
- **200 OK**: Returns the `data` field from the response
- **404 Not Found**: Returns "User not found" message

## Run Workflow

Access the workflow at `https://localhost:5001/workflows/users/2` to trigger it.

Expected response:
```json
{
    "id": 2,
    "email": "janet.weaver@reqres.in",
    "first_name": "Janet",
    "last_name": "Weaver",
    "avatar": "https://reqres.in/img/faces/2-image.jpg"
}
```

## Summary

The guide demonstrates defining workflows programmatically using the `HttpEndpoint` activity as a trigger, reading route parameters into variables, making external API calls, and handling multiple HTTP response status codes appropriately.

Source code is available on GitHub in the elsa-guides repository.
