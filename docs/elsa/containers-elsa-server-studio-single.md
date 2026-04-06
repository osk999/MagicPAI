# Elsa Server + Studio - Single Image

## Docker Compose Configuration

The documentation provides a sample Docker Compose setup for deploying Elsa Server and Studio together:

```yaml
services:
    elsa-server-and-studio:
        image: elsaworkflows/elsa-server-and-studio-v3-5:latest
        pull_policy: always
        environment:
            ASPNETCORE_ENVIRONMENT: Development
            HTTP_PORTS: 8080
            HTTP__BASEURL: http://localhost:14000
        ports:
            - "14000:8080"
```

## Steps to Set Up

The setup process involves four main steps:

1. Create a `docker-compose.yml` file in your project directory using the provided configuration
2. "Ensure that Docker and Docker Compose are installed on your machine" (refer to prerequisites documentation)
3. Open a terminal in the directory containing your compose file
4. Execute `docker-compose up` to launch the container

## Accessing Elsa

After the container starts running, access Elsa Studio by navigating to http://localhost:14000 in your web browser.

Authentication requires these default credentials:
- **Username:** admin
- **Password:** password
