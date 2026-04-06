# EF Core Setup for Elsa Workflows

## Overview

This guide demonstrates how to configure Elsa Workflows v3.x with Entity Framework Core persistence across multiple database platforms.

## Database Options

The documentation covers setup for four database systems:

- **PostgreSQL** via `Elsa.EntityFrameworkCore.PostgreSQL`
- **SQL Server** via `Elsa.EntityFrameworkCore.SqlServer`
- **SQLite** via `Elsa.EntityFrameworkCore.Sqlite`
- **MySQL** (mentioned in prerequisites)

## Core Configuration Steps

The setup involves two primary DbContexts:

1. **ManagementElsaDbContext** — handles workflow definitions and instances
2. **RuntimeElsaDbContext** — manages bookmarks, inbox, and execution logs

Both require separate migration commands when using manual approaches.

## Migration Strategies

The guide presents two approaches:

**Development approach:** Setting `RunMigrations = true` applies changes automatically at startup, though this isn't recommended for production.

**Production approach:** "Install EF Core Tools: `dotnet tool install --global dotnet-ef`" then run migrations manually for greater control and auditability.

## Key Features Highlighted

- Connection pooling configuration for high-concurrency scenarios
- Retry logic setup for transient database failures
- Optional separation of management and runtime databases
- Logging configuration for diagnostic purposes

The documentation emphasizes verifying connection string credentials and database permissions when troubleshooting authentication failures.
