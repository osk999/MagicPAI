# Docker Quickstart for Elsa Workflows

## Overview
This guide enables you to deploy Elsa Workflows using Docker Compose for evaluation and development purposes.

## Prerequisites Required
You'll need Docker Desktop (Windows/Mac) or Docker Engine (Linux), Docker Compose V2+, at least 4GB RAM, and ports 14000 and 5432 available.

## Quick Start Options

### SQLite Configuration
The simplest approach uses SQLite with minimal dependencies. Create a `docker-compose.yml` file configuring the Elsa server image with SQLite as the database provider, then execute `docker compose up -d`. Access the Studio at http://localhost:14000 using default credentials (admin/password).

### PostgreSQL Configuration
For production-like evaluation, this setup includes a PostgreSQL container alongside Elsa. The database requires credentials configured in environment variables, with the connection string specifying the PostgreSQL server details. Health checks monitor both services' readiness before full startup.

## Key Configuration Details

**Environment Variables** control:
- ASP.NET environment and HTTP port settings
- Database provider selection (Sqlite, PostgreSql, SqlServer, MySql)
- Connection strings specific to each database type
- Optional logging and CORS configuration

**Health Checks** verify service readiness through:
- HTTP endpoint testing for Elsa (30-second intervals)
- Database connectivity validation for PostgreSQL (10-second intervals)

## Common Operations

Monitor logs with `docker-compose logs -f`, stop services via `docker-compose stop`, or restart with `docker-compose restart`. Update to the latest version by pulling new images and recreating containers.

## Troubleshooting Approaches

Address startup failures by checking logs and port availability. Database connection issues require verifying PostgreSQL health status and credentials. Studio access problems need service verification and firewall configuration review. Performance concerns may require increased Docker resource allocation or connection pool adjustments.

## Production Guidelines

Security measures include changing default credentials, implementing HTTPS, restricting database access, and managing secrets properly. Scalability requires external databases, load balancing, and resource limits. Reliability depends on backups, monitoring, health checks, and planned update strategies.

## Additional Resources

The documentation references Hello World tutorials, workflow concepts, HTTP workflow APIs, custom activity development, and alternative deployment scenarios including Traefik integration and distributed hosting configurations.
