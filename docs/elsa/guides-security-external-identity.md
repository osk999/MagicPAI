# External Identity Providers

## Overview

This guide addresses integrating Elsa Server with external identity providers for authentication and authorization. Elsa Server supports industry-standard protocols including OpenID Connect (OIDC), OAuth 2.0, and SAML 2.0.

### Supported Providers

The documentation lists eight categories of supported identity providers:
- Microsoft Entra ID (Azure AD)
- Auth0
- Keycloak
- Okta
- Google Identity
- OpenIddict
- IdentityServer
- Any OIDC-compliant provider

## Key Benefits

The guide emphasizes several advantages:

- **Centralized user management**: Single source of truth for user identities
- **Single Sign-On (SSO)**: Users authenticate once across all applications
- **Multi-factor authentication**: Additional security layers
- **Audit capabilities**: Comprehensive logging of authentication events
- **Reduced development overhead**: Leverage existing identity infrastructure
- **Enterprise security features**: Compliance with organizational security policies

## General Integration Pattern

Four primary steps are outlined:

1. **Register Elsa in the Identity Provider** - Create application registration, configure redirect URIs, obtain credentials
2. **Configure ASP.NET Core Authentication** - Install NuGet packages, configure middleware
3. **Configure Elsa to Use ASP.NET Core Authentication** - Enable default authentication, map claims
4. **Configure Elsa Studio (if used)** - Set up token forwarding

## Provider-Specific Guides

### Microsoft Entra ID

Azure Active Directory was rebranded as Microsoft Entra ID in 2023. Integration requires:
- Azure Portal application registration
- API permissions configuration
- OIDC authentication middleware setup
- Studio configuration with token forwarding

### Auth0 Configuration

Auth0 integration emphasizes support for social login (Google, Facebook, GitHub, etc.) and includes creating applications, defining APIs with permissions, and JWT Bearer authentication setup.

### Keycloak Integration

As an open-source solution, Keycloak offers self-hosted deployment with full control, LDAP/Active Directory integration, user federation, and fine-grained authorization capabilities.

### Generic OIDC Provider

A flexible pattern accommodates any OIDC-compliant provider using standard configuration approaches.

## Authorization and Claims Mapping

The documentation provides code examples for mapping roles and implementing custom claims transformation during token validation.

## Elsa Studio Configuration

Studio requires authentication configuration matching the server's IdP setup, with token forwarding to the Elsa Server API using Bearer authentication.

## REST API Integration

External applications authenticate using bearer tokens: call the Elsa API with token via standard HTTP authorization headers.

## Security Best Practices

Eight recommendations are provided:

1. Use HTTPS exclusively
2. Validate tokens properly
3. Use short-lived access tokens
4. Implement refresh token rotation
5. Store secrets securely
6. Enable MFA
7. Implement audit logging
8. Use role-based access control

## Troubleshooting

Common issues addressed include:
- **Token validation failures** - Check issuer, audience, and signing key configuration
- **Unmapped claims** - Verify claims transformation is configured correctly
- **CORS errors** - Ensure proper CORS policy allows the identity provider's domain

## Related Documentation

- [Security & Authentication Guide](https://docs.elsaworkflows.io/guides/security)
- [Deployment Documentation](https://docs.elsaworkflows.io/guides/deployment)
- [Hosting Elsa in an Existing App](https://docs.elsaworkflows.io/guides/onboarding/hosting-elsa-in-existing-app)

---

**Last Updated:** 2025-12-02
