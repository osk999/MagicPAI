# Elsa Studio Integration Guide

## Key Integration Patterns

Elsa Studio can be deployed using four primary approaches:

1. **Separate Application** - Studio runs independently with reverse proxy routing
2. **Iframe Embedding** - Studio loads within your app's UI boundary
3. **Direct Embedding** - Studio integrates directly into ASP.NET Core applications
4. **Same Process** - Studio and Server share the same runtime

## Framework-Specific Recommendations

**React & Angular:** These frameworks typically use either "Separate App with Reverse Proxy" or "Iframe Integration" approaches. Both require configuring Studio's backend API URL and handling CORS when services operate on different origins.

**Blazor:** The recommended pattern is that Studio runs in the same ASP.NET Core application. This provides the tightest integration since both use Blazor technology.

**MVC/Razor Pages:** These can implement either separate-route patterns or iframe-based embedding within views.

## Essential Configuration Requirements

Regardless of approach, you must configure:

- **Base API URL** - Points Studio to your Elsa Server backend
- **Authentication** - Choose between API keys, bearer tokens, or cookies
- **CORS** - Required when Studio and Server operate on different origins

## Security Considerations

**Important**: Do **not** store authentication tokens in `localStorage` or `sessionStorage`, as they are accessible to JavaScript and vulnerable to XSS attacks.

Recommended alternatives include HttpOnly cookies or in-memory token storage when using OAuth2/OIDC flows.

## Deployment Options

Production deployments can use Docker Compose for multi-container orchestration or Kubernetes for larger-scale infrastructure requiring multiple replicas.
