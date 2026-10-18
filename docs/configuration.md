# Configuration Reference

All configuration is supplied through environment variables in `deployables/.env`. The install script generates this file automatically; edit it manually if you need to change values after the initial install.

## Environment Variables

| Variable | Required | Default | Description |
|---|---|---|---|
| `JWT_SECRET` | Yes | Auto-generated | Random secret used to sign JWT tokens for the management UI. Must be at least 32 characters. Never share or commit this value. |
| `MANAGEMENT_DOMAIN` | Yes | — | Fully-qualified domain name for the management UI (e.g. `manage.example.com`). Caddy issues a TLS certificate for this domain automatically. |
| `INSTALL_BRANCH` | No | `main` | The branch `install.sh` downloads the deployment files from. Set by `--branch`; later runs (including `--upgrade`) use it again. |
| `APP_IMAGE_TAG` | No | `latest` | Tag of the `hamidmayeli/quick-vless` app image. `latest` is built from `main`; a pull request build publishes `pr-<number>`. Set by `--image-tag`. |
| `XRAY_DEFAULT_PROFILE` | No | `vless-reality` | The profile active on first start (a folder in `xray/`). After that, the super admin switches profiles on the **Profiles** page, and this value is no longer used. |
| `XRAY_HOST` | Yes | Same as `MANAGEMENT_DOMAIN` | Public IP address or domain name of this server, used as the address in REALITY `vless://` links. Override with `--host` if your VPN traffic should route through a different address than the management UI. Must not be proxied by Cloudflare. |
| `XRAY_REALITY_PRIVATE_KEY` | Yes | Auto-generated | Xray Reality private key in base64 format. Used by Xray internally during the TLS handshake. This value goes into `xray/vless-reality/config.json`. Keep it secret. |
| `XRAY_REALITY_PUBLIC_KEY` | Yes | Auto-generated | Xray Reality public key in base64 format. Goes into `xray/vless-reality/client-config.template`, so clients can verify the server. |
| `XRAY_REALITY_SHORT_ID` | Yes | Auto-generated | An 8-character hex string (e.g. `a1b2c3d4`). Part of the REALITY handshake. |
| `XRAY_SNI` | No | `dl.google.com` | The domain that Xray impersonates. Must be a public domain with a valid TLS certificate on port 443. Do not use your own domain here. |
| `XRAY_FINGERPRINT` | No | `chrome` | TLS client fingerprint to mimic. Options: `chrome`, `firefox`, `safari`, `ios`, `android`, `edge`, `360`, `qq`, `random`, `randomized`. |
| `XRAY_CF_DOMAIN` | No | — | Hostname proxied by Cloudflare for the `vless-cf-ws` profile (e.g. `cdn.example.com`). Empty keeps that profile disabled. See [Cloudflare WebSocket Setup](hosting.md#cloudflare-websocket-setup). |
| `XRAY_WS_ID` | No | Auto-generated | WebSocket path of the `vless-cf-ws` profile, without the leading slash (e.g. `ws-3f9c…`). Random per server. |

The `XRAY_*` values above (except `XRAY_DEFAULT_PROFILE`) aren't read by the running containers. `install.sh` writes them into the profile files and keeps them in `.env` so a re-run fills in the same values.

## Generating Values Manually

### Xray Reality key pair

```bash
docker run --rm teddysun/xray xray x25519
```

Output:
```
Private key: <base64-private-key>
Public key:  <base64-public-key>
```

Place the private key in `xray/vless-reality/config.json` under `realitySettings.privateKey`, and the public key in `xray/vless-reality/client-config.template` as the `pbk=` value. Also record both in `.env` (`XRAY_REALITY_PRIVATE_KEY`, `XRAY_REALITY_PUBLIC_KEY`).

### Short ID

```bash
openssl rand -hex 4
```

The short ID must appear in `xray/vless-reality/config.json` under `realitySettings.shortIds` and in the template as the `sid=` value.

### JWT secret

```bash
openssl rand -hex 32
```

## Applying Configuration Changes

After editing `deployables/.env`:

```bash
cd deployables
docker compose up -d
```

Docker Compose re-creates containers that have changed environment variables.

After editing a profile's `config.json` or `client-config.template` by hand, open the **Profiles** page and click **Re-apply** on the active profile (or activate the edited profile). No container restart is needed.

> **Warning:** Changing the REALITY private key, public key or short ID invalidates all existing REALITY links. Users must refresh their config URL afterwards.

> **Warning:** Changing `JWT_SECRET` invalidates all active sessions in the management UI. All admins will be logged out and must log in again.

## Choosing XRAY_SNI

The SNI target is a domain that your server pretends to be when a non-management client connects on port 443. Requirements:

- Must have a valid TLS certificate (i.e. HTTPS works on it)
- Must be hosted on a server that does not block non-TLS traffic on port 443
- Should be a well-known domain to avoid raising suspicion

Common choices: `dl.google.com`, `www.apple.com`, `www.amazon.com`, `www.cloudflare.com`. Xray warns that `www.microsoft.com` makes the server's IP more likely to be blocked.

Do not use `localhost`, your own domain, or any domain you control.

## Writing Your Own Profile

A profile is a folder in `deployables/xray/` (on the server: `<deploy-dir>/xray/`) whose name uses lowercase letters, digits, `.`, `_` or `-`. It appears on the **Profiles** page as soon as it contains a `config.json`.

### config.json

A normal Xray config:

- **At least one `vless` inbound, each with a unique `tag`** (not `api`). Users are added to every `vless` inbound; the `clients` list is filled in by the app.
- **Something listens on port 443.** Caddy forwards all VPN traffic to `xray:443`.
- **No `api`, `stats` or `policy` sections are needed.** The app adds what it relies on: the stats service, a gRPC API inbound on port 10085, its routing rule, and per-user traffic stats.
- **Certificate paths are container paths:** a file at `<deploy-dir>/xray/my-profile/tls/cert.pem` is `/etc/xray/profiles/my-profile/tls/cert.pem`.
- Comments and trailing commas are allowed.

The app picks each user's `flow` per inbound: `xtls-rprx-vision` on raw TCP with `tls` or `reality`, and none on every other transport (WebSocket, gRPC, XHTTP…).

### client-config.template

The link each user gets, one per line. Two placeholders are filled in for each user:

| Placeholder | Value |
|-------------|-------|
| `{{secret}}` | The user's Xray UUID. Required. |
| `{{name}}` | The user's name, URL-encoded. |

The link must match the inbound: include `flow=xtls-rprx-vision` exactly when the inbound uses Vision.

### Install-time placeholders

The shipped profiles use `YOUR_*` placeholders that `install.sh` fills in from `.env`: `YOUR_PRIVATE_KEY`, `YOUR_PUBLIC_KEY`, `YOUR_SHORT_ID`, `YOUR_SNI`, `YOUR_HOST`, `YOUR_FINGERPRINT`, `YOUR_CF_DOMAIN` and `YOUR_WS_ID`. Your own profiles can use them too, or contain literal values. A profile that still contains a `YOUR_*` placeholder can't be activated.

### Validation

The Profiles page shows why a profile can't be activated. When you activate it, the app also runs `xray run -test` on the generated config, and rejects the profile with Xray's message if that fails. If Xray then doesn't come up within 30 seconds, the previous profile is restored.
