# Plan — Selectable Xray Profiles (REALITY + Cloudflare WebSocket)

## 1. Goal

| # | Requirement | Approach |
|---|-------------|----------|
| R1 | Support more than one way of serving VPN users (inbound/config) | **Profiles**: each folder under `deployables/xray/` that holds a `config.json` and a `client-config.template` is one profile |
| R2 | The super admin picks the **active** profile | A new super-admin-only API and a **Profiles** page. The choice is stored in `/data/settings.json` |
| R3 | The list of profiles is **dynamic** | The API reads the folder on every request. Nothing is hard-coded |
| R4 | `VlessUrlGenerator` uses the template next to the config | It renders the active profile's `client-config.template` |
| R5 | The API generates the config file that the Xray container runs | The app writes `data/xray/config.json`. `docker-compose.yaml` points Xray at it |
| R6 | The Cloudflare WebSocket profile needs a TLS certificate | **Cloudflare Origin CA** certificate, kept in the profile folder. Xray terminates TLS itself (§3) |
| R7 | `install.sh` sets up both profiles | New flags, a profile-aware patch step, and an upgrade path for existing servers |

---

## 2. Can several inbounds share port 443? → No. One active profile at a time

- **Xray itself:** No. Two inbounds can't bind the same port. Xray's own port-sharing feature (VLESS "fallbacks") can't put REALITY and WS+TLS on one port: REALITY has to own the raw TLS handshake, and Cloudflare can't speak REALITY.
- **Behind Caddy:** Yes, but at a cost. Caddy already owns the host's port 443 and routes by SNI, so each inbound could listen on its own internal port. Each profile would then need its own Caddyfile route and a unique port. Adding a profile folder would no longer be drop-in.
- **Chosen:** Profiles, with **one active at a time**, as you sketched. The active profile always listens on `xray:443` inside the Docker network. Caddy's existing rule already sends every non-management SNI to `xray:443`, so this works with no Caddy changes. The REALITY SNI and the Cloudflare hostname both land on the active profile.

What this costs:

- Switching profiles **restarts Xray**, so connections drop for a few seconds.
- Users' clients must refresh their config URL to get the new link. The URL `/api/v1/config/{id}` itself does not change.

§16 Q1 describes the "both at once" variant if you want it later.

---

## 3. TLS for the Cloudflare WebSocket profile → Cloudflare Origin CA certificate, terminated by Xray

How the traffic flows:

```
client ──TLS (CF edge cert)──▶ Cloudflare ──TLS, SNI=cdn.example.com──▶ server:443
       Caddy layer4 (SNI ≠ management) ──TCP passthrough──▶ xray:443 (VLESS+WS+TLS, Origin CA cert)
```

| Option | Reliability | Ease | Verdict |
|--------|-------------|------|---------|
| **A. Cloudflare Origin CA cert in the profile folder; Xray terminates TLS** | Valid for 15 years: no renewals, and no reloading certs into Xray. Cloudflare trusts it in "Full (strict)" mode. If the files are missing, the app refuses to activate the profile, so REALITY keeps running (§5.3) | Create it once in the Cloudflare dashboard (3 clicks) and pass two files to `install.sh` | **Chosen** |
| B. Same cert, but Caddy terminates TLS and proxies WS to Xray | Reliable cert. But a missing or broken cert file stops **all of Caddy** (management UI + REALITY), and it adds a proxy hop | The Caddyfile needs a site that exists only when Cloudflare is configured | Rejected |
| C. Let's Encrypt via Caddy for the Cloudflare hostname | HTTP-01 and TLS-ALPN-01 are unreliable behind the Cloudflare proxy. DNS-01 needs a long-lived Cloudflare DNS API token on the server. Xray would also need a reload after every 60–90-day renewal | Needs an extra Caddy DNS module | Rejected |
| D. Drop Caddy; Xray does everything ("REALITY steal-oneself") | Something still has to run ACME for the management UI. It also weakens REALITY's disguise, because the server then imitates your own site instead of a big third-party one | Complex | Rejected |

**Caddy stays as it is.** It still issues the management domain's certificate and routes by SNI.

**Cloudflare settings to document:**

- **DNS record:** the WS hostname is **Proxied** (orange cloud).
- **SSL/TLS mode:** Full (strict).
- **WebSockets:** On.
- **`MANAGEMENT_DOMAIN`:** stays DNS-only, so its ACME HTTP-01 keeps working. The WS hostname must differ from it, because Caddy routes the management SNI to itself.

**Hostnames needed.** You need two hostnames, not two registered domains. Two subdomains of one domain whose DNS is on Cloudflare are enough:

| Hostname | DNS record in Cloudflare | Used by | Certificate |
|----------|--------------------------|---------|-------------|
| `manage.example.com` (`--domain`, and `--host` by default) | **DNS only** (grey cloud) → server IP | Management UI, plus the REALITY client address | Let's Encrypt via Caddy, as today |
| `cdn.example.com` (`--cf-domain`) | **Proxied** (orange cloud) → server IP | Cloudflare WebSocket profile | Cloudflare's edge cert (client side) and the Origin CA cert (server side) |

- **REALITY's address must stay DNS-only.** REALITY can't pass through Cloudflare, because Cloudflare terminates TLS. Nothing else changes for REALITY: same keys, same SNI, same client link.
- **The WS hostname must be one level deep.** Use `cdn.example.com`, not `ws.cdn.example.com`. Cloudflare's free edge certificate only covers the apex and first-level subdomains.
- **One Origin CA certificate is enough.** A certificate for `example.com` + `*.example.com` covers every first-level subdomain, so the same files can serve any future Cloudflare-proxied profile.
- **If your current domain's DNS isn't on Cloudflare,** either move its nameservers to Cloudflare or use a second domain that is on Cloudflare for the WS hostname.

---

## 4. Target layout on the server

```
deploy-dir/
  docker-compose.yaml
  Caddyfile                      (unchanged)
  xray-run.sh                    NEW  Xray launcher (§6.2)
  xray/                          profiles — written by install.sh, read-only for the containers
    vless-reality/
      config.json                (was xray/config.json)
      client-config.template     NEW
    vless-cf-ws/
      config.json                (your new file, adjusted — §5.4)
      client-config.template     NEW
      tls/fullchain.pem          Cloudflare Origin CA cert (git-ignored)
      tls/privkey.pem
  data/
    app/  users.json, admins.json, settings.json (NEW), usage_history/
    xray/                        generated by the app
      config.json                the config Xray runs
      structure.sha256           hash of the config without clients (written by the app)
      running.sha256             hash of the config Xray is running (written by xray-run.sh)
    caddy/ …
```

**Mounts.** Both containers mount the folders at the **same paths**:

- `./xray` → `/etc/xray/profiles` (read-only)
- `./data/xray` → `/etc/xray/runtime`

Because the paths match, certificate paths inside a profile resolve the same way in both containers. The app can then check that the cert files exist and run `xray run -test` on the exact file Xray will load.

---

## 5. Profile format

### 5.1 `config.json`

A normal Xray config. Rules:

- **Users go into every `vless` inbound.** Each must have a unique `tag` (not `api`) and listen on **port 443**.
- **`clients` is ignored**, because the app fills it in.
- **No `api`, `stats` or `policy` sections, and no `api` inbound or routing rule are needed.** The app adds them (§6.1). If they are present, the app normalizes them, so legacy configs that already have them still load.
- **Certificate paths must be absolute container paths** under `/etc/xray/profiles/<name>/…`.

### 5.2 `client-config.template`

- **One link per line.** Usually there is exactly one line. More than one line renders as a newline-separated list, which most clients import as a subscription.
- **Runtime placeholders**, filled in by the API:
  - `{{secret}}`: the user's current Xray UUID.
  - `{{name}}`: the user's name, URL-encoded.
- **Install-time placeholders** (`YOUR_*`) are filled in by `install.sh`, the same `sed` mechanism used today.

`vless-reality/client-config.template`. It renders the same URL `VlessUrlGenerator` builds today:

```
vless://{{secret}}@YOUR_HOST:443?type=tcp&security=reality&pbk=YOUR_PUBLIC_KEY&fp=YOUR_FINGERPRINT&sni=YOUR_SNI&sid=YOUR_SHORT_ID&spx=%2F&flow=xtls-rprx-vision#{{name}}
```

`vless-cf-ws/client-config.template`:

```
vless://{{secret}}@YOUR_CF_DOMAIN:443?encryption=none&security=tls&sni=YOUR_CF_DOMAIN&fp=YOUR_FINGERPRINT&alpn=http%2F1.1&type=ws&host=YOUR_CF_DOMAIN&path=%2FYOUR_WS_ID#{{name}}
```

### 5.3 Validation

`XrayProfileCatalog` runs these checks on every profile, every time profiles are listed. A profile is **valid** only if all of them pass:

1. The folder name matches `^[a-z0-9][a-z0-9._-]{0,63}$`, and both files exist.
2. `config.json` is a JSON object with an `inbounds` array.
3. There is at least one `vless` inbound. Every `vless` inbound has a unique `tag` other than `api`.
4. **No unreplaced placeholders.** Neither file contains `YOUR_[A-Z_]+`. The error says which flag to pass to `install.sh`, e.g. "run install.sh with --cf-domain".
5. **Template lines are well-formed.** Each line starts with `vless://` and contains `{{secret}}`. Only known `{{…}}` placeholders are used.
6. **Flow consistency:** `flow=xtls-rprx-vision` appears in the template **only if** the inbound's derived flow is Vision (§6.1). This catches the WebSocket mismatch you would otherwise get.
7. **Certificate files exist:** every `certificateFile` / `keyFile` in `tlsSettings` exists and can be read.

Activation also runs `xray run -test` on the generated config (§6.3).

Error messages never include file contents. Private keys live in `config.json`.

### 5.4 Changes needed in your `vless-cf-ws/config.json`

1. **Certificate paths:** `/etc/xray-cf/tls/…` → `/etc/xray/profiles/vless-cf-ws/tls/fullchain.pem` and `…/privkey.pem`. Nothing mounts `/etc/xray-cf`.
2. **WebSocket path:** the hard-coded `/ws-22d3903610fb1e28792127a4` → `/YOUR_WS_ID`. The repo is public, so as written every install would share the same path. `install.sh` generates one per server.
3. **Missing `api` / `stats` / `policy`:** the backend needs them for live user changes and usage stats. The app adds them, so no edit is needed.
4. **Missing client template:** add `client-config.template` (§5.2).
5. **Optional:** add the `block` outbound and the ads rule, to match the REALITY profile.

---

## 6. Runtime config and switching

### 6.1 What the app generates

This is `XrayRuntimeConfigBuilder`, a pure function: given a profile and the enabled users, it returns JSON. It uses `System.Text.Json.Nodes`, so key order is kept and the output is deterministic.

1. Start from the profile's `config.json`.
2. Ensure the management pieces are present:
   - `stats: {}`;
   - `api { tag: "api", services ⊇ [HandlerService, StatsService] }`;
   - a `dokodemo-door` inbound tagged `api` on the port from `Xray:StatsServer` (10085);
   - routing rule `api → api` as the **first** rule;
   - `policy.levels."0".statsUserUplink` / `statsUserDownlink` set to `true`.
3. In every `vless` inbound, set `settings.clients` to `{ id: secret, email: secret, flow }`. `flow` is left out when it's empty.
   - **Derived flow:** `xtls-rprx-vision` when the network is `tcp`/`raw` (the default is `tcp`) **and** security is `tls` or `reality`. Otherwise it is empty (WS, gRPC, xhttp…). Xray rejects Vision on WebSocket, so this can't be one global setting.
   - **`email = secret` everywhere.** This fixes an existing bug: today the config file writes `email = Name` while gRPC uses `email = Secret`. After any Xray-only restart (for example a daily image update), two things break:
     - usage is not recorded, because stats arrive keyed by name;
     - disabling a user over gRPC fails, so a user over quota can keep connecting.

     Switching profiles restarts Xray, so this has to be fixed first.
4. `structure.sha256` = SHA-256 of the same document with every `clients` array emptied, plus the profile name. If only users changed, the hash doesn't change, and Xray is **not** restarted.

### 6.2 `xray-run.sh` — restarting Xray without the Docker socket

The Xray container's entrypoint becomes a small POSIX `sh` script, mounted read-only. It uses only `cat`/`sleep`/`kill`, which the Alpine-based `teddysun/xray` image has (verify during implementation):

```sh
#!/bin/sh
# Runs Xray from the config the management app generates, and restarts it when the
# app changes the config structure (e.g. a profile switch). User-only changes don't restart it.
R=/etc/xray/runtime
pid=""
trap '[ -n "$pid" ] && kill "$pid" 2>/dev/null; wait; exit 0' TERM INT
until [ -f "$R/config.json" ] && [ -f "$R/structure.sha256" ]; do sleep 2; done
while true; do
  want=$(cat "$R/structure.sha256")
  rm -f "$R/running.sha256"
  xray run -config "$R/config.json" & pid=$!
  sleep 3
  kill -0 "$pid" 2>/dev/null && echo "$want" > "$R/running.sha256"
  while kill -0 "$pid" 2>/dev/null && [ "$(cat "$R/structure.sha256")" = "$want" ]; do sleep 2; done
  kill "$pid" 2>/dev/null; wait "$pid" 2>/dev/null
  sleep 1
done
```

Rejected alternatives:

- **Mounting `/var/run/docker.sock` into the app:** an app compromise would mean root on the host.
- **Hot-swapping inbounds with `xray api rmi/adi`:** it can leave things half-applied (the old inbound removed, the new one failing). It would also be a second source of truth beside the file.

### 6.3 Activation sequence

This is `XrayRuntime.ActivateAsync`. It holds the **single runtime lock** (see §6.4).

1. Static validation (§5.3). If it fails, return **400** with the errors.
2. Build the config and write it to `config.json.next`. Run `xray run -test -config config.json.next` with the app image's `xray` binary. If it fails, return **400** with Xray's message, and leave the running config untouched.
3. Atomically move `config.json.next` → `config.json`, then write `structure.sha256`.
4. Wait up to `Xray:RestartConfirmTimeoutSeconds` (default 30) for **both**:
   - `running.sha256` to equal the new hash;
   - a gRPC probe to answer: `QueryStats` with pattern `__probe__` and `reset = false`. It must **not** reset counters, which the metrics query does.
5. On success, save `xray_active_profile` to `/data/settings.json` and return **200**.
6. On timeout, **roll back**: rebuild the previous profile's config, wait again, then return **503** saying "activation failed, rolled back to X".

### 6.4 One lock for all Xray changes

- `XrayRuntime` takes over from both `XrayConfigFileService` and the endpoints' direct `XrayService` calls. These are the user-change methods:
  - `ApplyUserAddedAsync`
  - `ApplyUserRemovedAsync`
  - `ApplyUserChangedAsync`
  - `ApplySecretRotatedAsync`
- Each one writes the file **and** makes the gRPC calls for every `vless` tag in the active profile, all under the same lock as activation.
- A user edit therefore happens entirely before or entirely after a switch. It can never be applied to an Xray process that is just about to be replaced.
- Edits wait for the lock (up to ~30 s) while a switch is in progress.

### 6.5 Startup

`XrayConfigSyncService` runs on startup:

1. **Pick the profile:** the saved active profile, or else `Xray:DefaultProfile`.
2. **If it's valid:** regenerate the config. Xray restarts only if the structure hash changed, for example after `install.sh` patched a profile.
3. **If it's invalid:** keep the last good `config.json`, so the VPN keeps running, and show an error in the UI.

The app then re-adds users over gRPC and treats "already exists" as success, logged at Debug. Today this would log an Error for every user.

---

## 7. Backend changes

| File | Change |
|------|--------|
| **New** `Services/Xray/XrayProfile.cs` | Records: `XrayProfile(Name, ConfigPath, TemplatePath, Inbounds, Errors)` and `XrayInboundInfo(Tag, Network, Security, Port, Flow)` |
| **New** `Services/Xray/XrayProfileCatalog.cs` | Lists `Xray:ProfilesDir` on each call and validates each profile (§5.3) |
| **New** `Services/Xray/XrayRuntimeConfigBuilder.cs` | Pure builder and structure hash (§6.1) |
| **New** `Services/Xray/IXrayConfigTester.cs` + `XrayBinaryConfigTester.cs` | Runs `xray run -test` through `Xray:BinaryPath`. If the binary isn't found, it logs a warning and skips the test (dev machines) |
| **New** `Services/Xray/IXrayRestartMonitor.cs` + `FileRestartMonitor.cs` | Waits for `running.sha256` plus the gRPC probe. A timeout of `0` turns it off (CI, no launcher) |
| **New** `Services/Xray/XrayRuntime.cs` | The lock, activation, rollback and user-change methods (§6.3–6.4). Supports **profile mode** and **legacy mode** (§12) |
| **New** `Storage/SettingsRepository.cs` | `/data/settings.json` (`xray_active_profile`). Writes atomically, like `AdminRepository` |
| `Services/XrayService.cs` | `AddUserAsync(user, tag, flow)` and `RemoveUserAsync(user, tag)`. It no longer reads `Xray:InboundTag`, and the hard-coded Vision flow goes. Add `ProbeAsync()` (no reset). Treat `AlreadyExists` on add as success |
| `Services/XrayConfigFileService.cs` | Removed. It is folded into `XrayRuntime` and the builder. Legacy mode reuses the builder's client injection on `Xray:ConfigPath` |
| `Services/VlessUrlGenerator.cs` | `Generate(secret, name)` renders the **active profile's** template (`{{secret}}`, `{{name}}` URL-encoded). In legacy mode it builds today's URL from `Xray:*` settings. It returns `null` when no valid profile is active |
| `Services/XrayConfigSyncService.cs` | Startup behavior from §6.5 |
| `Services/QuotaEnforcerService.cs` | `xray.RemoveUserAsync` + `configFile.SyncAsync` → `runtime.ApplyUserChangedAsync` |
| `Services/MetricsCollectorService.cs` | No change. Stats are keyed by secret, so usage continues across switches |
| `Endpoints/UsersEndpoints.cs` | Replace the `xray.*` + `configFile.SyncAsync` pairs with `XrayRuntime` calls |
| `Endpoints/ConfigEndpoints.cs` | Single-connection secret rotation calls `runtime.ApplySecretRotatedAsync`. This **also writes the file**: today it doesn't, so after an Xray-only restart the old secret comes back. Return **503** when no valid profile is active |
| **New** `Endpoints/XrayEndpoints.cs` | Super admin only (§8) |
| `Program.cs` | Register the new services and map `MapXrayEndpoints()` |
| `appsettings.json` | Add `Xray:ProfilesDir`, `Xray:RuntimeDir`, `Xray:DefaultProfile`, `Xray:RestartConfirmTimeoutSeconds`, `Storage:SettingsPath`. Keep the legacy keys |
| `management-app/Dockerfile` | Also copy `geoip.dat`/`geosite.dat` from `teddysun/xray` (`/usr/share/xray/`). Without them, `xray run -test` fails on the REALITY profile's `geosite:` rule. Verify the path |

---

## 8. API (super admin only — `RequireAuthorization(AdminRoles.SuperAdminPolicy)`)

| Route | Response |
|-------|----------|
| `GET /api/v1/xray/profiles` | `{ mode: "profiles"\|"legacy", active, applied, last_error, profiles: [{ name, active, valid, errors[], inbounds: [{ tag, network, security, port, flow }] }] }`. `applied` means `running.sha256 == structure.sha256`. Private keys, secrets and file contents are never returned |
| `PUT /api/v1/xray/active-profile` `{ name }` | **200** `{ active }`. **404** if the name isn't in the list (it is only compared to listed names, never used as a path). **400** with `{ errors }` if validation or `-test` fails. **409** in legacy mode or while another activation is running. **503** if the restart wasn't confirmed and the change was rolled back |
| `POST /api/v1/xray/active-profile/reapply` | Rebuilds the active profile from disk, after you edit its files by hand. Same responses as `PUT` |

---

## 9. Frontend

- `types`: `XrayProfile`, `XrayProfilesState`.
- `services/api.ts`: `xrayApi.list()`, `activate(name)`, `reapply()`.
- New `hooks/useXrayProfiles.ts`.
- **New `pages/ProfilesPage.tsx`**, reached through a **Profiles** nav link and `/profiles` route that only the super admin sees:
  - one card or row per profile: name, inbounds (`network`/`security`), and an **Active** badge;
  - a validation error list on invalid profiles, which also disables **Activate**;
  - **Activate** opens a confirmation: *"Xray restarts and active connections drop for a few seconds. Users must refresh their config URL to get the new link."*;
  - a "not applied yet" warning when `applied = false`, plus a **Re-apply** button;
  - a legacy-mode banner: *"Run install.sh --upgrade to enable profiles."*
- `App.tsx` / `Layout.tsx`: the route and nav link, with the same guard as `/admins`.
- **Mock API:** add a `profiles` state to `mock/middleware.cjs` (in memory) for `GET /v1/xray/profiles` and `PUT /v1/xray/active-profile`.

---

## 10. Deployables

### 10.1 `docker-compose.yaml`

```yaml
xray:
  image: teddysun/xray:latest
  entrypoint: ["/bin/sh", "/usr/local/bin/xray-run.sh"]
  volumes:
    - ./xray-run.sh:/usr/local/bin/xray-run.sh:ro
    - ./xray:/etc/xray/profiles:ro
    - ./data/xray:/etc/xray/runtime          # launcher writes running.sha256
app:
  volumes:
    - ./data/app:/data
    - ./xray:/etc/xray/profiles:ro
    - ./data/xray:/etc/xray/runtime
  environment:
    - Xray__ProfilesDir=/etc/xray/profiles
    - Xray__RuntimeDir=/etc/xray/runtime
    - Xray__DefaultProfile=${XRAY_DEFAULT_PROFILE:-vless-reality}
    - Xray__StatsServer=xray:10085
    - Storage__SettingsPath=/data/settings.json
    # removed: Xray__ConfigPath, InboundTag, Host, Port, RealityPublicKey, RealityShortId, Sni, Fingerprint
```

### 10.2 Other files

- **Repo profiles:** `deployables/xray/config.json` → `deployables/xray/vless-reality/config.json`. Remove the `api`/`stats`/`policy` boilerplate, since the app adds it. Add both templates. Adjust `vless-cf-ws/config.json` (§5.4).
- **New** `deployables/xray-run.sh` (§6.2).
- **New** `deployables/manifest.txt`: the files `install.sh` downloads. New profiles in the repo then don't need script changes.
- **`.env.example`:** add `XRAY_DEFAULT_PROFILE`, `XRAY_CF_DOMAIN` and `XRAY_WS_ID`.
- **`.gitignore`:** add `deployables/xray/*/tls/` and `deployables/data/`.
- **Caddy:** no change to `Caddyfile` or `Dockerfile.caddy`.

---

## 11. `install.sh`

### 11.1 Issues found in the current script

1. **The file list is hard-coded.** `download_files` names `xray/config.json`, and is skipped entirely once `docker-compose.yaml` exists. Existing servers have no way to get new deployables.
2. **Config steps are skipped on re-runs.** Steps are recorded in `.install-state` and skipped next time, so adding Cloudflare later (`--cf-domain`) wouldn't re-patch anything.
3. **`write_xray_config` patches one file only.**
4. **Inconsistent repository names:**
   - the usage text says `hamidmayeli/outline-ss-ws`;
   - `GITHUB_RAW` uses `hamidmayeli/quick-vless`;
   - the README clones `hamidmayeli/easy-xray`.
5. **Inconsistent SNI default:** `install.sh` and compose default to `dl.google.com`, while `.env.example` and `configuration.md` say `www.microsoft.com`.
6. **Docs overclaim:** `hosting.md` says the script auto-detects the public IP. It doesn't.

### 11.2 Changes

- **New options:**

  | Option | Purpose |
  |--------|---------|
  | `--profile NAME` | Initial active profile. Default `vless-reality`; written as `XRAY_DEFAULT_PROFILE` |
  | `--cf-domain DOMAIN` | The Cloudflare-proxied hostname. Must differ from `--domain` |
  | `--cf-origin-cert FILE`, `--cf-origin-key FILE` | The Origin CA certificate and key. Both are required when `--cf-domain` is used for the first time |
  | `--upgrade` | Re-downloads compose, `xray-run.sh` and `update.sh`, keeping `.bak` copies. Downloads any **missing** profile files but never overwrites existing ones. Then migrates (§12) |

- **Steps:**

  | Step | Change |
  |------|--------|
  | `download_files` | Driven by `manifest.txt`. Profile files are only downloaded if missing |
  | `migrate_legacy_layout` | **New.** Moves the old `xray/config.json` (already patched) to `xray/vless-reality/config.json` |
  | `generate_secrets` | Also generates `XRAY_WS_ID` (`ws-$(openssl rand -hex 12)`) |
  | `write_env_file` | Adds the new variables |
  | `install_cf_certificate` | **New.** Copies the two files to `xray/vless-cf-ws/tls/` (key `chmod 600`) and checks them with `openssl`: the files parse, the key matches the certificate (compare public keys), and a SAN covers `--cf-domain`. The SAN check understands wildcards: `*.example.com` covers `cdn.example.com` but not `a.b.example.com`. It also refuses a `--cf-domain` equal to `--domain` or `--host` |
  | `write_xray_profiles` | **Replaces `write_xray_config`.** For each `xray/*/` folder, replaces the `YOUR_*` placeholders in `config.json` and `client-config.template`. Without `--cf-domain`, the CF profile's placeholders are left alone, so the app shows it as "not configured" |
  | `verify_cloudflare` | **New. Runs after `verify_health`, and only with `--cf-domain`. Warns only, never fails.** It sends one request: `curl -sS -o /dev/null -D - --max-time 15 https://$CF_DOMAIN/` (details below the table) |

  What `verify_cloudflare` reports:
  - **The TLS check fails:** Cloudflare's edge certificate isn't valid for the hostname. It is probably more than one level deep, or the zone isn't active on Cloudflare yet.
  - **No `server: cloudflare` response header:** the record isn't proxied. VPN clients would connect straight to the server, see the Origin CA certificate, and fail. Turn on the orange cloud.
  - **Only when `vless-cf-ws` is the starting profile**, it also warns on Cloudflare status `525`/`526`: the server's TLS failed, or its certificate was rejected. Check the Origin certificate and SSL mode Full (strict). This check is skipped when REALITY is active, because Cloudflare's connection to the server then lands on REALITY, which always looks like a certificate error.
  - **Otherwise** it prints "Cloudflare proxy OK".

- **Always run:** `generate_secrets`, `write_env_file`, `install_cf_certificate`, `write_xray_profiles` and `verify_cloudflare` are all idempotent (the first four fill only what's missing), so they run every time instead of being state-skipped.
- **Optional, not in scope:** create the Origin CA certificate through the Cloudflare API (`--cf-api-token`). Cloudflare's docs confirm API tokens can do this with the permission **Zone → SSL and Certificates → Edit**. The token would be used once and not stored.

---

## 12. Migrating existing installs

- **Legacy mode.** Existing servers run `update.sh` daily, which pulls the **new app image with the old compose file**. The app must keep working in that state.
  - **Profile mode:** `Xray:ProfilesDir` is set and exists.
  - **Legacy mode:** anything else, which is what the old compose produces.

  In legacy mode the app behaves exactly as it does today:
  - it updates `Xray:ConfigPath` in place;
  - it uses one tag, `Xray:InboundTag`, with Vision flow;
  - it builds the URL from the `Xray:*` environment variables;
  - activation returns 409, and the UI shows the upgrade banner.

  Legacy mode still gets the `email = secret` fix.
- **Upgrading.** The operator runs `install.sh --upgrade`. On first start, the app adopts `vless-reality` as the active profile.
- **Effect on existing users:** their links stay the same, because the REALITY values are identical.

---

## 13. Tests

### Unit (xUnit)

- **`XrayRuntimeConfigBuilderTests`:**
  - adds the missing `api`/`stats`/`policy` sections, the api inbound and the routing rule;
  - normalizes them when they are already there, and is idempotent;
  - preserves the rest of the profile JSON;
  - writes clients with `email = secret`;
  - writes Vision flow only for tcp/raw with tls/reality (`[Theory]` over network × security);
  - keeps the structure hash the same when only users change, and changes it when the profile changes.
- **`XrayProfileCatalogTests`** (temp-dir fixtures):
  - valid profile;
  - each validation rule in §5.3 produces its specific error;
  - files that aren't folders and invalid folder names are ignored.
- **`VlessUrlGeneratorTests`:**
  - template rendering, name encoding and multi-line templates;
  - legacy mode keeps today's exact URL (update the existing tests).
- **`XrayRuntimeTests`**, with a fake `IXrayConfigTester`, a fake `IXrayRestartMonitor` and a fake `XrayService`:
  - activation succeeds;
  - a `-test` failure leaves `config.json` untouched;
  - a restart timeout rolls back to the previous profile;
  - a user change that waits for an activation in progress is applied to the new profile's tags;
  - legacy mode refuses activation.
- **`SettingsRepositoryTests`:** a round trip, and the file is written atomically.

### Integration

- **`XrayIntegrationTests`:**
  - a normal admin gets 403, anonymous gets 401;
  - list profiles (fixtures under `DataDir`);
  - activate a valid profile, then check that `GET /config/{id}` renders the new template (e.g. `type=ws`);
  - activating an invalid profile gives 400; an unknown one gives 404.
- **`ConfigIntegrationTests`:** single-connection rotation rewrites the runtime file. Returns 503 when no valid profile is active.
- **Legacy mode:** the existing tests keep running in legacy mode (the factory doesn't set `ProfilesDir`).

### E2E (Playwright) + CI

- **Fixtures:** `e2e/fixtures/xray-profiles/` with `fixture-reality`, `fixture-ws` and `fixture-broken` (it still contains an unreplaced placeholder).
- **New `profiles.spec.ts`:**
  - the super admin sees the profiles and the broken one's error;
  - activating `fixture-ws` moves the **Active** badge, and the user's config URL then returns `type=ws`;
  - a normal admin has no Profiles link, is redirected away from `/profiles`, and gets 403 from the API;
  - `afterEach` restores the original active profile, because the backend is shared.
- **`publish.yml`:** the e2e job mounts the fixtures and a temp runtime folder, sets the new env variables, and sets `Xray__RestartConfirmTimeoutSeconds=0` because CI has no launcher. Its fake `xray` binary (`exit 0`) makes `-test` pass.

### Manual checks (VM or local Docker)

- A fresh `install.sh` with and without `--cf-domain`.
- `--upgrade` on a server with the legacy layout.
- A switch REALITY → CF-WS → REALITY with a real client:
  - usage keeps being recorded;
  - disabling a user over quota cuts them off after the switch.
- Cert files removed: the CF profile becomes non-activatable, and REALITY keeps running.
- `verify_cloudflare`:
  - with the WS record set to DNS-only (grey cloud), it warns "not proxied";
  - with a two-level hostname, it warns about the edge certificate;
  - with a wrong Origin certificate and `--profile vless-cf-ws`, it warns about 525/526;
  - when everything is set up correctly, it prints "Cloudflare proxy OK".
- `xray-run.sh` exits within 1 s on `docker compose stop` (signal handling).

---

## 14. Docs

| File | Change |
|------|--------|
| `docs/hosting.md` | New sections: **Connection profiles** (what they are, switching, the effect on users) and **Cloudflare WebSocket setup**. The setup section covers: the two-hostname table from §3; DNS proxied, Full (strict) and WebSockets on; creating one wildcard Origin certificate; the installer flags; and what each `verify_cloudflare` warning means. Fix the "auto-detects IP" claim |
| `docs/configuration.md` | Environment table updated with the new variables, and a note that the `XRAY_*` REALITY values now only feed `install.sh` templates. A "writing your own profile" guide: the rules in §5.1–5.3 and the placeholders |
| `docs/upgrading.md` | `install.sh --upgrade` and what legacy mode means. Back up `xray/` (now folders) instead of `xray/config.json` |
| `management-app/backend/README.md` | Profile, runtime and launcher architecture |
| `README.md` | Quick start mentions profiles and the Cloudflare option |

---

## 15. Implementation order

1. [x] **Fix the existing bugs first:** `email = secret` in the file, and file sync on secret rotation, plus tests. This ships on its own and makes later Xray restarts safe.
2. [x] Builder, profile catalog, settings repository, template rendering in `VlessUrlGenerator`, plus unit tests.
3. [x] `XrayRuntime` with legacy mode, the single lock, the `XrayService` changes and the endpoint rewiring, plus tests. Legacy behavior must not change, and the existing tests stay green.
4. [x] Activation (tester, restart monitor, rollback) and `XrayEndpoints`, plus integration tests.
5. [x] Deployables: the profile folders, `xray-run.sh`, compose, manifest, `.env.example`, `.gitignore`. App Dockerfile: the geo files.
6. [x] `install.sh`: flags, new and always-run steps, `--upgrade`, migration.
7. [x] Frontend Profiles page and mock API, then e2e plus the CI fixture wiring.
8. [x] Docs, then the manual checks in §13.

---

### As built — where the implementation differs from this plan

- **Candidate file is `config.next.json`, not `config.json.next`.** Xray picks the config format from the file extension, so `xray run -test` rejected every `.next` file ("Failed to get format"). Found by the real-stack test.
- **Port rule:** at least one inbound must listen on 443, instead of every `vless` inbound. This still allows VLESS fallback setups, where a second inbound listens on an internal port.
- **The applied template is copied to `data/xray/client-config.template`.** Links keep matching the running config even if the profile folder is later broken.
- **`xray-run.sh` sleeps in the background** (`sleep & wait`), so `docker compose stop` takes under a second instead of waiting for the current sleep.
- **New `IXrayClient` interface** in front of `XrayService`, and an `XrayOptions` record holding all `Xray:*` settings, both for testability.
- **Error bodies in the UI:** `ApiError` carries the JSON error body, so the Profiles page shows Xray's message and the rollback reason.
- **The e2e `fixture-ws` profile uses `security: none`,** so CI needs no certificate files.
- **Docs fixes beyond the plan:**
  - the storage location: compose uses bind mounts under `data/`, not a named volume, so the old backup commands archived nothing;
  - the repository name (`quick-vless`);
  - the `XRAY_SNI` default (`dl.google.com`).
- **Not done:** the manual checks in §13 that need a real server, a Cloudflare zone and a VPN client. These were checked locally instead:
  - **`install.sh`:** 28 checks in a container (fresh install, adding Cloudflare, certificate rejections, `--upgrade` from the legacy layout).
  - **The real stack** (app image + `teddysun/xray` + `xray-run.sh`), 22 checks:
    - a `-test` rejection;
    - a confirmed switch to WebSocket in 6–7 s;
    - users added to the running WS inbound without flow;
    - a real crash-loop that rolled back in 38 s;
    - an app restart that left Xray alone.

## 16. Open questions

1. **One active profile, or several at once?** The plan has one. Running both would mean:
   - each profile on a unique internal port, plus one Caddy layer4 SNI route per profile;
   - users attached only to the "active" inbounds, so switching needs no restart.

   The public config URL would also need a rule for which link(s) to return.
2. **Automate creating the Origin CA certificate** with a one-time Cloudflare API token (§11.2)? It's confirmed possible. Or is the manual dashboard step acceptable?
3. **Profile display names:** the folder name is used as-is. Add an optional `profile.json` with `{ "title", "description" }` for nicer UI labels?
