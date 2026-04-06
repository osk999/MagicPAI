# Multi-Tenant Setup in Elsa

## Overview

Setting up multitenancy in Elsa involves three core steps: adding the `Elsa.Tenants` package reference, installing the `TenantsFeature`, and configuring both the tenant resolution pipeline and tenant provider.

## Configuration Example

The documentation provides a practical example using configuration-based tenant provisioning with claims-based resolution. Here's the setup pattern:

**Program.cs Implementation:**
The configuration registers Elsa with tenant support, adding a `ClaimsTenantResolver` to the pipeline and enabling a configuration-based tenant provider that reads from the application settings.

**appsettings.json Structure:**
Tenant definitions are stored in a "Multitenancy" section containing an array of tenant objects with `Id` and `Name` properties.

## Identity Integration

When using Elsa's default Identity module, users linked to tenants automatically receive a tenant ID claim. The `ClaimsTenantResolver` leverages this claim to determine the current tenant context.

The Identity configuration section allows defining roles, users, and applications with explicit `TenantId` assignments. This enables per-tenant role-based access control and application registrations.

## Important Constraint

A warning notes that "Primary keys (Id) must be unique across tenants since there's no constraint with tenant IDs." This requirement exists in the current version, though future changes are possible.
