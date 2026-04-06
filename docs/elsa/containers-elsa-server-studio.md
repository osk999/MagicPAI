# Elsa Server + Studio Deployment Guide

## Docker Compose Structure

The provided configuration deploys two interconnected services using Docker containers. The setup includes an Elsa Server instance and an Elsa Studio interface that communicates with the server.

## Service Configuration Overview

**Elsa Server** operates on port 12000 and manages the workflow backend. Its environment variables establish the development context and define the base URL for API access.

**Elsa Studio** runs on port 13000 and serves as the user interface. The configuration specifies `ELSASERVER__URL: http://localhost:12000/elsa/api` to establish the connection between Studio and Server.

## Deployment Steps

To launch the services, execute `docker-compose up` in the directory containing your configuration file. This command pulls the necessary Docker images and starts both containers automatically.

## Access Points

Once operational, the services are accessible at:
- Elsa Server: http://localhost:12000
- Elsa Studio: http://localhost:13000

## Important Considerations

The documentation notes: "Ensure the ports specified in the docker-compose.yml file (e.g., `12000:8080` and `13000:8080`) are not already in use on your system."

If port conflicts occur, modify the port mappings in the compose file accordingly. The configuration also establishes a dependency relationship where Elsa Studio waits for Elsa Server to be ready before initialization.
