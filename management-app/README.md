The docker file containing both backend and frontend built together. It should support but AMD and ARM CPUs

Something like:

```Dockerfile
# Force the SDK to run natively on the host architecture (amd64) for maximum speed
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build

ARG TARGETARCH

WORKDIR /sln

COPY ./backend/OutlineManager.slnx ./
COPY ./backend/API/*.csproj ./API/
COPY ./backend/API.Tests/*.csproj ./API.Tests/

# Standard .NET restore handles architecture natively without any native OS libraries
RUN dotnet restore ./API/API.csproj -a $TARGETARCH

COPY ./backend .

# Build the solution normally
RUN dotnet build -c Release

# ==========================
# Test Stage
# ==========================
FROM build AS test-api
WORKDIR /sln
RUN dotnet test ./OutlineManager.slnx -c Release --no-build

# ==========================
# Publish Stage
# ==========================
FROM build AS publish

ARG TARGETARCH

# Force PublishAOT to false to override any property inside the .csproj file
RUN dotnet publish ./API/API.csproj \
    -c Release \
    -a $TARGETARCH \
    -o /app/publish \
    -p:PublishAot=false

# ==========================
# Frontend Build
# ==========================
FROM --platform=$BUILDPLATFORM node:26-slim AS build-client

RUN apt-get update && \
    apt-get install -y jq && \
    rm -rf /var/lib/apt/lists/*

RUN npm install -g pnpm

WORKDIR /app

COPY ./frontend/package.json ./
COPY ./frontend/pnpm-lock.yaml ./
COPY ./frontend/pnpm-workspace.yaml ./

RUN pnpm install --frozen-lockfile

COPY ./frontend .

RUN pnpm run build

# ==========================
# Runtime Image
# ==========================
# This automatically pulls the right native architecture (amd64 or arm64) for the final layer
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

RUN apt-get update && \
    apt-get install -y --no-install-recommends curl && \
    rm -rf /var/lib/apt/lists/*

WORKDIR /app

COPY --from=publish /app/publish .
COPY --from=build-client /app/dist ./wwwroot

ENV ASPNETCORE_HTTP_PORTS=80
ENV ASPNETCORE_HTTPS_PORTS=443

EXPOSE 80
EXPOSE 443

HEALTHCHECK --interval=30s --timeout=10s --retries=3 --start-period=5s \
    CMD curl -fsS http://localhost/ >/dev/null || exit 1

ENTRYPOINT ["dotnet", "API.dll"]
```