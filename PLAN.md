# easy-xray — Build Plan

## Project Overview

A self-hosted management layer for [Xray-core](https://github.com/XTLS/Xray-core). Provides user provisioning, bandwidth quota enforcement, periodic metrics collection, and a web UI — all backed by flat files (no external database).

**Protocol:** VLESS + TCP + REALITY + Vision

---

## Architecture

### Storage (file-based)
| File | Purpose |
|------|---------|
| `users.json` | User identities: UUID `id`, UUID `secret`, quota GB (nullable), expiry (nullable), `single_connection`, `enabled` |
| `admins.json` | Admin accounts: username, hashed password, refresh token |
| `usage_history/YYYYMMDD.csv` | Append-only daily snapshots: user, uplink bytes, downlink bytes, total bytes |

### Backend — ASP.NET Minimal API (.NET 10)
- Solution: `OutlineManager.slnx` / Project: `API.csproj`
- Xray integration via CLI commands:
  - **Hot-reload:** `xray api command HandlerService` (add/remove users without restart)
  - **Metrics:** `xray api statsquery --server=127.0.0.1:10085`
  - **Startup sync:** reads `users.json` → writes to `/usr/local/etc/xray/config.json`
- Hosted services:
  - **Metrics collector** — runs every 300s, appends to daily CSV
  - **Quota enforcer** — if cumulative total ≥ quota, sets `enabled: false`, removes from Xray
- **Single-connection flow** — on public config hit with `single_connection: true`, rotate `secret` UUID and hot-reload Xray

### Frontend — React + TypeScript + Vite
- Package manager: pnpm
- React 19, react-router-dom 7, recharts 3, qrcode.react 4
- vite-plugin-pwa + workbox-window
- Mock API: `rest_api_faker` on port 3001 (backend-free development)
- Scripts: `dev` (Vite + mock concurrently), `build`, `lint`, `preview`

### E2E — Playwright + pnpm
### Docker — multi-stage (amd64 + arm64), aspnet:10.0 runtime, ports 80/443
### CI/CD — GitHub Actions (test + publish)

---

## Phases & Tasks

### Phase 1 — Frontend ✅
- [x] 1.1 Scaffold Vite 8 + React 19 + TypeScript 7 with pnpm
- [x] 1.2 Install runtime deps: react-router-dom@7.18, recharts@3.10, qrcode.react@4.2
- [x] 1.3 Install dev deps: vite-plugin-pwa@1.3, workbox-window@7.4, eslint@10, typescript-eslint@8.70
- [x] 1.4 Configure ESLint flat config + TypeScript strict mode
- [x] 1.5 Configure vite-plugin-pwa (PWA manifest + service worker + `@` path alias)
- [x] 1.6 Set up `rest_api_faker` mock API (`mock/db.json`, port 3001, `dev` concurrent script)
- [x] 1.7 Create folder structure: `pages/`, `components/`, `hooks/`, `services/`, `types/`
- [x] 1.8 Implement auth flow (Login page, `useAuth`, `ProtectedRoute`, `Layout`)
- [x] 1.9 Implement Users page (list, add, edit, delete, enable/disable + inline form)
- [x] 1.10 Implement Usage page (summary cards + recharts bar chart, quota progress bars)
- [x] 1.11 Implement Config page (vless:// URL display + QR code via qrcode.react)
- Build: ✅ `pnpm run build` passes, PWA generated, 0 TS errors

### Phase 2 — Backend ✅
- [x] 2.1 Create .NET 10 solution (`OutlineManager.slnx`), `API` project, `API.Tests` xUnit project
- [x] 2.2 Domain models: `User`, `Admin`, `UsageRecord`
- [x] 2.3 File-based repositories: `UserRepository`, `AdminRepository`, `UsageRepository` (JSON + CSV)
- [x] 2.4 JWT auth: `TokenService` (HMAC-SHA256 access token + random refresh token), `AdminRepository`
- [x] 2.5 Users CRUD endpoints (`GET/POST/PUT/DELETE /users`) with Xray hot-reload on enable/disable
- [x] 2.6 Public config endpoint (`GET /config?secret=…` → vless:// URL + single_connection rotation)
- [x] 2.7 Usage report endpoint (`GET /usage`)
- [x] 2.8 `XrayService` wraps `xray api` CLI (HandlerService AddInbound/RemoveUser, statsquery)
- [x] 2.9 `MetricsCollectorService` hosted service (300s loop → StatsService → CSV append)
- [x] 2.10 `QuotaEnforcerService` hosted service (300s loop → cumulative total → disable/remove)
- [x] 2.10 `XrayConfigSyncService` startup sync (users.json → config.json patch + gRPC hot-add)
- [x] 2.11 Unit tests: 14 passing (VlessUrlGenerator ×6, QuotaEnforcer ×4, UsageRepository ×4)

### Phase 3 — E2E Tests ✅
> Tests run against the running app (Docker image or local). Configure `BASE_URL` env var.
- [x] 3.1 Initialize Playwright project with pnpm (`e2e/package.json`, `playwright.config.ts`)
- [x] 3.2 Write auth flow tests (login, first-login bootstrap, logout, token refresh)
- [x] 3.3 Write user management tests (CRUD, enable/disable)
- [x] 3.4 Write usage report tests
- [x] 3.5 Write config tests (URL display, QR code, per-user config)

### Phase 4 — Docker ✅
- [x] 4.1 Write multi-stage Dockerfile (`management-app/Dockerfile`):
  - Stage 1 (`backend-build`): `dotnet/sdk:10.0` — restore → build → **test** → publish
  - Stage 2 (`frontend-build`): `node:26-slim` + corepack/pnpm — `pnpm build`
  - Stage 3 (`xray-src`): copy `xray` CLI binary from `teddysun/xray`
  - Stage 4 (runtime): `dotnet/aspnet:10.0` + wwwroot + `/usr/bin/xray`
- [x] 4.2 Write `management-app/.dockerignore`
- [ ] 4.3 Verify build for linux/amd64 and linux/arm64 (requires CI or Docker buildx)

### Phase 5 — Deployables ✅
- [x] 5.1 Write `deployables/docker-compose.yaml`:
  - `xray`: `teddysun/xray`, port 443, bind-mounts `./xray` config dir
  - `app`: `hamidmayeli/quick-vless`, all env vars injected, shares `./xray` for config sync
  - `caddy`: `caddy:2-alpine`, ports 80 (ACME) + 8443 (management HTTPS)
- [x] 5.2 Write `deployables/Caddyfile` (Caddy reverse proxy, env-var domain, auto-HTTPS)
- [x] 5.3 Write `deployables/xray/config.json` (VLESS + TCP + REALITY + Vision template)
- [x] 5.4 Write `deployables/.env.example` (all required variables with generation hints)

### Phase 6 — CI/CD
- [ ] 6.1 Write `.github/workflows/test.yml` (dotnet test + playwright)
- [ ] 6.2 Write `.github/workflows/publish.yml` (build + push multi-arch Docker image)

### Phase 7 — Install Script
- [ ] 7.1 Write `install.sh` (pull docker-compose, prompt for config values, start services)

### Phase 8 — Documentation
- [ ] 8.1 Write root `README.md` (overview, quick-start, architecture diagram)
- [ ] 8.2 Write `docs/` pages (API reference, configuration guide, upgrading)

---

## Status Legend
- [ ] Not started
- [~] In progress
- [x] Done

---

## Change Log
| Date | Phase | Update |
|------|-------|--------|
| 2026-09-13 | All | Initial plan created |
| 2026-09-13 | 1 | Frontend complete: Vite 8 + React 19 + TS 7, all 4 pages, mock API, PWA, 0 build errors |
| 2026-09-13 | 2 | Backend complete: .NET 10 Minimal API, JWT auth, file storage, Xray CLI wrapper, 3 hosted services, 14 unit tests passing |
| 2026-09-13 | 4 | Docker complete: multi-stage Dockerfile (test → backend → frontend → runtime), xray CLI copied from teddysun/xray |
| 2026-09-13 | 5 | Deployables complete: docker-compose (xray + app + caddy), Caddyfile (port 8443, auto-HTTPS), xray config.json template, .env.example |
| 2026-09-13 | 3 | Phase 3 deferred — E2E tests will run against the Docker image (Phase 4 is a prerequisite) |
| 2026-09-13 | 2,3 | Auth bootstrap (first login creates admin), PBKDF2+salt password hashing, Scalar API docs, /api route prefix, global snake_case JSON, GET /config/{userId} admin endpoint, structured logging on all endpoints, 23 integration tests (37 total passing), Playwright E2E tests, fixed frontend API contract (authApi, UsageRecord type, useUsage hook) |
