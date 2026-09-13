# ── Stage 1: Backend — build natively, then publish for the target platform ───
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS backend-build
ARG TARGETARCH
WORKDIR /src

# Keep restore cacheable when application source changes.
COPY backend/OutlineManager.slnx ./
COPY backend/API/API.csproj API/
COPY backend/API.Tests/API.Tests.csproj API.Tests/
RUN dotnet restore API/API.csproj -a $TARGETARCH

COPY backend/ ./
RUN dotnet build -c Release
RUN dotnet test API.Tests/API.Tests.csproj -c Release --no-build
RUN dotnet publish API/API.csproj -c Release -a $TARGETARCH -o /publish

# ── Stage 2: Frontend ──────────────────────────────────────────────────────────
FROM --platform=$BUILDPLATFORM node:26-slim AS frontend-build
RUN npm install -g pnpm
WORKDIR /app
COPY frontend/package.json frontend/pnpm-lock.yaml ./
RUN --mount=type=cache,id=quick-vless-pnpm,target=/root/.local/share/pnpm/store \
	pnpm config set store-dir /root/.local/share/pnpm/store && \
	pnpm install --frozen-lockfile
COPY frontend/ ./
RUN pnpm build

# ── Stage 3: Xray CLI source (for gRPC client binary in the app container) ────
FROM teddysun/xray AS xray-src

# ── Stage 4: Runtime ──────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=backend-build /publish       ./
COPY --from=frontend-build /app/dist     ./wwwroot
COPY --from=xray-src /usr/bin/xray       /usr/bin/xray
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080
ENTRYPOINT ["dotnet", "API.dll"]
