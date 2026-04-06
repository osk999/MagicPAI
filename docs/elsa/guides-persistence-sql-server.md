# SQL Server Configuration Guide for Elsa Workflows

## Summary

This documentation covers setting up Elsa Workflows to use SQL Server as the persistence provider instead of SQLite. The guide is designed for production environments, particularly on Windows.

## Key Requirements

The setup necessitates ".NET 8.0 or later" and "SQL Server 2016 or later (Express, Standard, or Enterprise)." Three NuGet packages are needed: `Elsa`, `Elsa.EntityFrameworkCore.SqlServer`, and the Entity Framework Core SQL Server driver.

## Configuration Approach

The process involves two main steps:

1. **Code Setup**: Configure both workflow management (definitions and instances) and workflow runtime (bookmarks, messages, logs) in `Program.cs` using Entity Framework Core with SQL Server.

2. **Connection String**: Add credentials to `appsettings.json` with parameters like Server, Database, User ID, Password, and optional settings such as `TrustServerCertificate` and `MultipleActiveResultSets`.

## Migration Strategy

Two approaches exist: automatic migrations during startup (development only) and manual migrations using the EF Core CLI (recommended for production). The documentation recommends generating SQL scripts for review before applying changes to production systems.

## Advanced Considerations

Organizations can implement separate databases for management versus runtime data, enable connection resilience through retry policies, and optimize performance via connection pooling and proper indexing.

## Migration Path

Transitioning from SQLite involves removing SQLite packages, updating configuration to use SQL Server, modifying connection strings, and applying migrations to establish the new schema.
