# Designer

## Before you start

To follow this guide, you'll need:

* An [Elsa Server](https://elsa-workflows.github.io/elsa-documentation/elsa-server.html?section=Designer) project
* An [Elsa Studio](https://elsa-workflows.github.io/elsa-documentation/docker.html?section=Designer#elsa-studio) instance

```bash
docker pull elsaworkflows/elsa-studio-v3:latest
docker run -t -i -e ASPNETCORE_ENVIRONMENT='Development' -e HTTP_PORTS=8080 -e ELSASERVER__URL=https://localhost:5001/elsa/api -p 14000:8080 elsaworkflows/elsa-studio-v3:latest
```

> **Port Numbers**
>
> When starting Elsa Studio, ensure you provide the correct URL to the Elsa Server application. For example, if Elsa Server runs on https://localhost:5001, use:
>
> `docker run -t -i -e ASPNETCORE_ENVIRONMENT='Development' -e HTTP_PORTS=8080 -e ELSASERVER__URL=https://localhost:5001/elsa/api -p 14000:8080 elsaworkflows/elsa-studio-v3:latest`

Return here when ready to proceed.

## Workflow Overview

This guide creates a workflow called `GetUser` that "handles inbound HTTP requests by fetching a user by a given user ID from a backend API and writing them back to the client in JSON format."

The workflow uses [JSONPlaceholder](https://jsonplaceholder.typicode.com/) as the backend API, which provides fake but realistic HTTP responses. The workflow extracts the user ID from route parameters and uses it to query the external API.

Example request: https://jsonplaceholder.typicode.com/users/2

Sample response:

```json
{
  "id": 2,
  "name": "Ervin Howell",
  "username": "Antonette",
  "email": "Shanna@melissa.tv",
  "address": {
    "street": "Victor Plains",
    "suite": "Suite 879",
    "city": "Wisokyburgh",
    "zipcode": "90566-7771",
    "geo": {
      "lat": "-43.9509",
      "lng": "-34.4618"
    }
  },
  "phone": "010-692-6593 x09125",
  "website": "anastasia.net",
  "company": {
    "name": "Deckow-Crist",
    "catchPhrase": "Proactive didactic contingency",
    "bs": "synergize scalable supply-chains"
  }
}
```

The workflow acts as a proxy in front of the JSONPlaceholder API.

## Designing the Workflow

Follow these steps to create the workflow using Elsa Studio.

### Step 1: Create Get User Workflow

Create a new workflow called Get User

### Step 2: Add Activities

Add and connect the following activities to the design surface:

* HTTP Endpoint
* Set Variable
* HTTP Request (flow)
* HTTP Response (for 200 OK)
* HTTP Response (for 404 Not Found)

### Step 3: Create Variables

Create the following variables:

| Name          | Type             | Storage           |
| ------------- | ---------------- | ----------------- |
| RouteData     | ObjectDictionary | Workflow Instance |
| UserId        | string           | Workflow Instance |
| User          | Object           | Workflow Instance |

### Step 4: Configure Activities

Configure the activities as follows:

**HTTP Endpoint**

Input:

| Property          | Value            | Syntax  |
| ----------------- | ---------------- | ------- |
| Path              | `users/{userid}` | Default |
| Supported Methods | `Get`            | Default |

Output:

| Property     | Value     |
| ------------ | --------- |
| Route Data   | RouteData |

Common:

| Property         | Value   |
| ---------------- | ------- |
| Trigger Workflow | Checked |

**Set Variable**

Input:

| Property | Value                              | Syntax |
|----------|-----------------------------------|--------|
| Variable | `UserId`                          | Default |
| Value    | `{{ Variables.RouteData.userid }}` | Liquid |

**HTTP Request (flow)**

Input:

| Property              | Value                                                           | Syntax |
|-----------------------|-----------------------------------------------------------------|--------|
| Expected Status Codes | `200, 404`                                                      | Default |
| Url                   | `return $"https://jsonplaceholder.typicode.com/users/{Variables.UserId}";` | C#   |
| Method                | `GET`                                                           | Default |

Output:

| Property       | Value  |
| -------------- | ------ |
| Parsed Content | `User` |

**HTTP Response (200)**

Input:

| Property    | Value            | Syntax     |
|-------------|------------------|------------|
| Status Code | `OK`             | Default    |
| Content     | `variables.User` | JavaScript |

**HTTP Response (404)**

Input:

| Property    | Value            | Syntax  |
|-------------|------------------|---------|
| Status Code | `NotFound`       | Default |
| Content     | `User not found` | Default |

### Step 5: Connect Activities

Connect each activity to the next. Ensure that you "connect the `200` and `404` outcomes of the HTTP Request (flow) activity to the appropriate HTTP Response activity."

### Step 6: Publish

Publish the workflow.

## Running the Workflow

Since the workflow uses the HTTP Endpoint activity, it will trigger when you send an HTTP request to the /workflows/users/{userId} path.

Test it by navigating to https://localhost:5001/workflows/users/2.

The response should resemble:

```json
{
  "id": 2,
  "name": "Ervin Howell",
  "username": "Antonette",
  "email": "Shanna@melissa.tv",
  "address": {
    "street": "Victor Plains",
    "suite": "Suite 879",
    "city": "Wisokyburgh",
    "zipcode": "90566-7771",
    "geo": {
      "lat": "-43.9509",
      "lng": "-34.4618"
    }
  },
  "phone": "010-692-6593 x09125",
  "website": "anastasia.net",
  "company": {
    "name": "Deckow-Crist",
    "catchPhrase": "Proactive didactic contingency",
    "bs": "synergize scalable supply-chains"
  }
}
```

## Summary

This guide demonstrates how to design a workflow using Elsa Studio. The workflow leverages the `HttpEndpoint` activity as a trigger to initiate execution. It extracts route parameters and stores them in variables, then uses those values to make an API call to JSONPlaceholder, handling both successful (200 OK) and error (404 Not Found) responses.

The completed workflow is available [here](https://raw.githubusercontent.com/elsa-workflows/elsa-guides/main/src/guides/http-workflows/Workflows/get-user.json).
