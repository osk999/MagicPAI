# Authentication & Authorization for Elsa Workflows

## Overview

Elsa Workflows supports multiple authentication mechanisms, including no authentication (development only), built-in identity systems, OpenID Connect integration, API keys, and custom providers.

## Key Authentication Methods

**Elsa.Identity** provides a built-in user management system with JWT tokens. Configuration requires installing the NuGet package and setting signing keys in `appsettings.json`. The system supports role-based access control with configurable token lifetimes.

**OpenID Connect** enables integration with external identity providers. Azure AD, Auth0, and Keycloak are common choices. Each requires registering your application with the provider, obtaining credentials, and configuring the appropriate NuGet packages like `Microsoft.AspNetCore.Authentication.OpenIdConnect`.

**API Key Authentication** supports machine-to-machine communication. While Elsa doesn't provide built-in API key support, you can implement custom authentication handlers that validate keys stored in memory or databases. Keys can be managed through dedicated endpoints.

**Custom Authentication** allows implementing proprietary authentication logic through ASP.NET Core authentication handlers. This approach provides flexibility for specific organizational requirements.

## Studio Configuration

Elsa Studio requires separate authentication configuration. It can authenticate using JWT bearer tokens, OIDC, or API keys depending on your server setup. Configuration differs for server-side Blazor versus WebAssembly implementations.

## Security Recommendations

Use HTTPS exclusively in production. Store signing keys securely using Azure Key Vault or similar services--never hardcode credentials. Implement role-based access control and enforce appropriate token lifetimes (short-lived access tokens, longer-lived refresh tokens). Enable rate limiting to prevent brute force attacks and add comprehensive logging for security auditing.

Regularly rotate API keys, validate redirect URIs to prevent open redirects, and implement security headers. Use distributed caching for tokens in multi-instance deployments and maintain backup authentication methods for disaster recovery.

## Troubleshooting Common Issues

401 Unauthorized errors typically indicate missing tokens, invalid formats, or expired credentials. Verify authentication middleware is registered with `app.UseAuthentication()` and `app.UseAuthorization()`. CORS issues when Studio cannot reach the API require proper CORS policy configuration. Token expiration problems often resolve by increasing token lifetime or implementing refresh token mechanisms.
