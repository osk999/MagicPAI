# Docker

The Elsa project currently offers three different Docker images:

* [Elsa Server + Studio](https://hub.docker.com/repository/docker/elsaworkflows/elsa-server-and-studio-v3/general)
* [Elsa Server](https://hub.docker.com/repository/docker/elsaworkflows/elsa-server-v3/general)
* [Elsa Studio](https://hub.docker.com/repository/docker/elsaworkflows/elsa-studio-v3/general)

These images simplify getting started with Elsa by eliminating the need to create an ASP.NET application and configure Elsa manually. Ensure Docker is installed before running any image.

### Elsa Server + Studio

This image runs an ASP.NET Core application functioning as both Elsa Server and Studio. Execute these commands:

```bash
docker pull elsaworkflows/elsa-server-and-studio-v3-5:latest
docker run -t -i -e ASPNETCORE_ENVIRONMENT='Development' -e HTTP_PORTS=8080 -e HOSTING__BASEURL=http://localhost:13000 -p 13000:8080 elsaworkflows/elsa-server-and-studio-v3-5:latest
```

Once running, access the application at [http://localhost:13000](http://localhost:13000/) using these credentials:

```shell-session
username: admin
password: password
```

### Elsa Server

This image runs an ASP.NET Core application as Elsa Server only. Execute:

```bash
docker pull elsaworkflows/elsa-server-v3-5:latest
docker run -t -i -e ASPNETCORE_ENVIRONMENT=Development -e HTTP_PORTS=8080 -e HTTP__BASEURL=http://localhost:13000 -p 13000:8080 elsaworkflows/elsa-server-v3-5:latest
```

Navigate to [http://localhost:13000](http://localhost:13000/) in your browser. View API documentation at [http://localhost:13000/swagger](http://localhost:13000/swagger).

### Elsa Studio

This image runs an ASP.NET Core application for Elsa Studio. Execute:

```bash
docker pull elsaworkflows/elsa-studio-v3-5:latest
docker run -t -i -e ASPNETCORE_ENVIRONMENT='Development' -e HTTP_PORTS=8080 -e ELSASERVER__URL=http://localhost:13000/elsa/api -p 14000:8080 elsaworkflows/elsa-studio-v3-5:latest
```

**Requires Elsa Server**

"Elsa Studio needs to connect to an existing Elsa Server instance" configured via the `ELSASERVER__URL` environment variable. Run the Elsa Server image to establish this instance.

Once running, access [http://localhost:14000](http://localhost:14000/) with:

```shell-session
username: admin
password: password
```
