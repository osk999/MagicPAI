# MagicPAI Server - Multi-stage build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy solution and project files for restore
COPY MagicPAI.Core/MagicPAI.Core.csproj MagicPAI.Core/
COPY MagicPAI.Activities/MagicPAI.Activities.csproj MagicPAI.Activities/
COPY MagicPAI.Workflows/MagicPAI.Workflows.csproj MagicPAI.Workflows/
COPY MagicPAI.Server/MagicPAI.Server.csproj MagicPAI.Server/
COPY MagicPAI.Studio/MagicPAI.Studio.csproj MagicPAI.Studio/
RUN dotnet restore MagicPAI.Server/MagicPAI.Server.csproj

# Copy everything and publish
COPY . .
RUN dotnet publish MagicPAI.Server/MagicPAI.Server.csproj \
    -c Release -o /app --no-restore

# Runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .

# Docker CLI for managing worker containers
RUN apt-get update \
  && apt-get install -y --no-install-recommends \
    ca-certificates curl gnupg \
  && install -m 0755 -d /etc/apt/keyrings \
  && curl -fsSL https://download.docker.com/linux/debian/gpg \
     -o /etc/apt/keyrings/docker.asc \
  && echo "deb [arch=$(dpkg --print-architecture) \
     signed-by=/etc/apt/keyrings/docker.asc] \
     https://download.docker.com/linux/debian bookworm stable" \
     > /etc/apt/sources.list.d/docker.list \
  && apt-get update \
  && apt-get install -y --no-install-recommends docker-ce-cli \
  && rm -rf /var/lib/apt/lists/*

EXPOSE 8080 8081
ENTRYPOINT ["dotnet", "MagicPAI.Server.dll"]
