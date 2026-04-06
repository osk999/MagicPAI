# Persistent Database Setup Guide

The documentation describes how to configure Elsa Server and Studio with PostgreSQL through Docker Compose, though the system supports multiple database engines.

## Key Configuration Components

**PostgreSQL Service Setup:**
The database container uses credentials "elsa/elsa" with a maximum connection limit of 2000. Data persists through a named Docker volume storing PostgreSQL files.

**Elsa Application Configuration:**
The application container connects to PostgreSQL via a connection string specifying the server, credentials, and performance parameters. The service runs on port 14000 and depends on the database service being available first.

## Supported Database Options

The system accepts four database provider values through environment configuration:
- SqlServer
- Sqlite (factory default)
- MySql
- PostgreSql

## Deployment Instructions

Starting the environment requires executing `docker-compose up` from the directory containing the configuration file. Once operational, the web interface becomes accessible at the designated localhost address.

## Related Resources

The documentation references three supplementary guides covering broader database configuration concepts, general persistence architecture, and SQL Server-specific implementation details for users selecting alternative database systems.
