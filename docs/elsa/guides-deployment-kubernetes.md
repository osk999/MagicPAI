# Kubernetes Basics

## Overview

This guide introduces deploying Elsa Workflows on Kubernetes with PostgreSQL persistence, transitioning from SQLite (development) to PostgreSQL (production). See the Full Kubernetes Deployment Guide for comprehensive coverage including Helm charts, autoscaling, monitoring, and service mesh integration.

## Prerequisites

- Kubernetes cluster (v1.24+) including Minikube, k3s, or cloud providers (EKS, AKS, GKE)
- kubectl CLI configured for cluster access
- Basic Kubernetes concepts understanding (Pods, Services, ConfigMaps, Secrets)
- PostgreSQL database (managed or self-hosted)

## Manifest Structure

The elsa-core repository contains sample manifests in the `scripts/kubernetes/` directory providing deployment starting points for Elsa Server and Studio. Components include deployment configurations, services (ClusterIP or LoadBalancer), ConfigMaps for application settings, and Secrets templates for sensitive data.

## SQLite Default Configuration

By default, Elsa Server deployments use SQLite with the connection string `Data Source=/app/data/elsa.db` and provider set to `Sqlite`. While offering zero configuration and suitable for demonstrations, SQLite presents production challenges:

- Single-file database limitations prevent horizontal scaling
- Concurrent writes cause errors under load
- Data loss occurs on pod restarts without PersistentVolumes
- Performance lacks optimization for high-throughput scenarios

> **Critical Warning:** Never use SQLite in production Kubernetes deployments. Always use PostgreSQL, SQL Server, or MySQL for production workloads.

## PostgreSQL Implementation

### Step 1 - Database Deployment

A sample PostgreSQL deployment includes a PersistentVolumeClaim (10Gi storage), deployment specification using postgres:16 image, environment variables for database initialization, and a ClusterIP Service. The production recommendation suggests managed PostgreSQL services (Amazon RDS, Azure Database for PostgreSQL, Google Cloud SQL) for reliability, automated backups, and reduced operational overhead.

### Step 2 - Configuration Updates

Configuration requires updating connection strings AND persistence providers. Two approaches exist: Environment variables directly in deployments, or mounting appsettings.Production.json via ConfigMap. Critical configuration includes:

- `ConnectionStrings__PostgreSql` pointing to the PostgreSQL service
- `Elsa__Persistence__Provider` set to `PostgreSql`
- Module-specific providers for Management and Runtime

### Step 3 - Program.cs Configuration

Custom Elsa Server images require explicit provider configuration in Program.cs using EntityFrameworkCore extensions with PostgreSQL options and migrations history table specifications.

## Critical Configuration Warning

A common mistake is to update the connection string but forget to configure the persistence provider. Symptoms include:

- Continued creation of elsa.db files
- Ignored connection strings
- Data not persisting to PostgreSQL

The root cause: Elsa modules contain default persistence providers in code; changing connection strings alone doesn't change providers. Each module (Management, Runtime) requires explicit PostgreSQL configuration.

## Database Migrations

Three migration approaches exist:

### Option 1 - Init Container

Runs migrations before main application startup using command: `dotnet ef database update --context ManagementElsaDbContext` and similar for RuntimeElsaDbContext.

> Warning: Most production images do not include this tool for security and size reasons.

### Option 2 - Auto-Migration

Enable via `Elsa__AutoMigrate=true` configuration, running migrations on startup through app code.

> Warning: Not recommended for production due to potential race conditions with multiple pods starting simultaneously.

### Option 3 - Kubernetes Job

Creates one-time migration Job for running schema updates before deployment, preventing race conditions from multiple pod startups.

## Troubleshooting

### SQLite Still Being Used

Diagnosis involves checking logs for provider messages and verifying environment variable configurations. Fixes include confirming persistence provider environment variables, validating ConfigMap mounting, and ensuring PostgreSQL packages exist in Docker images.

### Connection Refused/Timeout

Diagnosis checks PostgreSQL pod status, service configuration, and connection testing. Fixes verify service names match connection strings, add readiness probes to PostgreSQL deployment, and check namespace-specific service FQDNs.

### Tables Not Created

Diagnosis queries PostgreSQL for existing tables and migration history. Fixes ensure migrations executed successfully, manually run migrations if needed, and check migration history tables for applied migrations.

### Missing Environment Variables

Symptoms show literal `$(POSTGRES_PASSWORD)` in connection strings instead of actual values. Solutions include referencing secrets directly in environment fields or building connection strings in application code.

### ConfigMap Not Mounted

Verification checks ConfigMap existence, confirms file mounting in pods, and validates file content. Fixes ensure correct volume mount paths, set ASPNETCORE_ENVIRONMENT to "Production", and verify ConfigMap exists in the deployment namespace.

## Verification Methods

### 1. Log Inspection

Check logs for PostgreSQL provider messages and executed database commands.

### 2. Database Inspection

Connect to PostgreSQL to verify expected tables including:
- Elsa_ActivityExecutionRecords
- Elsa_Bookmarks
- Elsa_WorkflowDefinitions
- Elsa_WorkflowInstances

### 3. Test Workflow Creation

Create test workflows via API and verify persistence in PostgreSQL instances table.

## Production Best Practices

**Use Managed PostgreSQL:** Amazon RDS, Azure Database, or Google Cloud SQL provide automated backups, point-in-time recovery, and high availability.

**Separate Database Users:** Create read-only monitoring users, migration users with schema modification rights, and application users with limited permissions.

**Connection Pooling:** Configure `Pooling=true` with `MinPoolSize=1` and `MaxPoolSize=20` in connection strings.

**TLS for Connections:** Enable `SSL Mode=Require` with appropriate certificate validation.

**Health Checks:** Implement liveness probes (`/health/live`) and readiness probes (`/health/ready`) with appropriate delay and period settings.

**Resource Limits:** Set requests (memory: 512Mi, cpu: 250m) and limits (memory: 2Gi, cpu: 1000m).

**Production Scaling:**
- Development/Staging: 1-2 replicas
- Production low-traffic: 3 replicas
- Production high-traffic: 5-10+ with Horizontal Pod Autoscaling
- Enterprise deployments: 10+ with affinity rules

## Related Resources

- Full Kubernetes Deployment Guide
- Database Configuration
- Clustering Guide
- Security & Authentication
- Troubleshooting
