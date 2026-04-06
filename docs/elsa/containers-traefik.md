# Traefik Setup Guide for Elsa Workflows

## Overview

This guide demonstrates deploying Elsa Server and Studio using Docker Compose with PostgreSQL and Traefik as infrastructure components.

## Key Services

The configuration includes three main components:

1. **PostgreSQL**: Database service with a maximum connection limit of 2000
2. **Elsa Server and Studio**: The application layer, running on port 8080
3. **Traefik**: Reverse proxy handling HTTP routing on port 80

## Docker Compose Configuration

The setup defines services connected through a bridge network called `elsa-network`. PostgreSQL stores data in a named volume (`postgres-data`), while Traefik routes incoming requests to the appropriate backend.

Environment variables configure the Elsa application to connect to PostgreSQL using the credentials `elsa/elsa`. The base URL is set to `http://elsa.localhost:1280`, and Traefik routes traffic based on the hostname rule `Host(elsa.localhost)`.

## Accessing Services

Once running, users can reach:
- Elsa Studio via `http://elsa.localhost:1280`
- Traefik dashboard via `http://localhost:8080`

## Prerequisites

Users must add a hosts file entry mapping `127.0.0.1` to `elsa.localhost` before accessing the services. The location varies: `/etc/hosts` on Linux/Mac or `C:\Windows\System32\drivers\etc\hosts` on Windows.

## Common Issues

Problems typically stem from missing Docker installations, incorrect hosts file configuration, or service communication failures. The troubleshooting section recommends checking service logs through `docker-compose logs`.
