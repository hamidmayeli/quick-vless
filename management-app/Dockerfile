# ── Stage 1: Backend — restore, build, test, publish ──────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend-build
WORKDIR /src
COPY backend/ ./
RUN dotnet restore OutlineManager.slnx
RUN dotnet build   OutlineManager.slnx -c Release --no-restore
RUN dotnet test    OutlineManager.slnx -c Release --no-build
RUN dotnet publish API/API.csproj      -c Release --no-build -o /publish

# ── Stage 2: Frontend ──────────────────────────────────────────────────────────
FROM node:26-slim AS frontend-build
RUN npm install -g pnpm
WORKDIR /app
COPY frontend/package.json frontend/pnpm-lock.yaml ./
RUN pnpm install --frozen-lockfile
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
