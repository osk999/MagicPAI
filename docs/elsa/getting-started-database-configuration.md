# Database Configuration

## Supported Providers

- **SQL Server** – preferred for Windows production systems
- **PostgreSQL** – recommended for Linux/Unix production deployments
- **SQLite** – default choice, ideal for development
- **MySQL/MariaDB** – available but less frequently adopted
- **MongoDB** – document-oriented alternative

## Migration from SQLite to SQL Server

1. Install the required NuGet packages for SQL Server support
2. Modify `Program.cs` to call `UseSqlServer()` instead of SQLite
3. Supply connection details in configuration

## PostgreSQL Setup

Uses `UseNpgsql()` method with PostgreSQL-specific NuGet packages.

## MongoDB Configuration

Uses `UseMongoDb()` with MongoDB-specific driver packages and URI syntax.

## Database Operations

EF Core migrations require global tools. System supports separate contexts for workflow definitions and execution data.

## Advanced Scenarios

Distinct databases for management and runtime operations supporting independent scaling and data isolation.

## Key Production Practices

- Security: strong authentication, encryption, access restrictions, credential rotation
- Performance: connection pooling, appropriate limits, query monitoring
- Resilience: regular backups, restore testing, failover planning
