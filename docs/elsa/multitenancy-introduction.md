# Multitenancy in Elsa: Key Concepts

Elsa implements multitenancy through two primary architectures. Organizations can choose either a **shared database model**, where entities include a `TenantId` property, or a **separate database approach** that uses runtime connection string resolution via the `ITenantAccessor` service.

## Tenant Structure

A tenant is defined by the `Tenant` class, which extends the `Entity` base class and includes a name plus an `IConfiguration` object. This configuration enables "tenant-specific settings like connection strings and host names."

## Resolution and Provider Systems

The framework employs two complementary systems:

**Tenant Resolution** identifies the current tenant from application context, typically by examining HTTP requests. The available resolvers span multiple packages -- from `ClaimsTenantResolver` to `HostTenantResolver` -- offering flexibility in how tenants are determined.

**Tenants Providers** enumerate registered tenants. Three implementations exist: `DefaultTenantsProvider` (single-tenant setups), `ConfigurationTenantsProvider` (configuration-based), and `StoreTenantsProvider` (database-backed via EF Core or MongoDB).

This dual-system approach allows organizations to scale from simple single-tenant deployments to complex multi-tenant scenarios with robust isolation and flexible configuration management.
