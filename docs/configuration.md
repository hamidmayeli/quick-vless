# Configuration Reference

All configuration is supplied through environment variables in `deployables/.env`. The install script generates this file automatically; edit it manually if you need to change values after the initial install.

## Environment Variables

| Variable | Required | Default | Description |
|---|---|---|---|
| `JWT_SECRET` | Yes | Auto-generated | Random secret used to sign JWT tokens for the management UI. Must be at least 32 characters. Never share or commit this value. |
| `MANAGEMENT_DOMAIN` | Yes | — | Fully-qualified domain name for the management UI (e.g. `manage.example.com`). Caddy issues a TLS certificate for this domain automatically. |
| `XRAY_HOST` | Yes | Same as `MANAGEMENT_DOMAIN` | Public IP address or domain name of this server. This value is embedded in the `vless://` URLs given to users; their clients connect to it. Override with `--host` if your VPN traffic should route through a different address than the management UI. |
| `XRAY_REALITY_PRIVATE_KEY` | Yes | Auto-generated | Xray Reality private key in base64 format. Used by Xray internally during the TLS handshake. This value goes into `xray/config.json`. Keep it secret. |
| `XRAY_REALITY_PUBLIC_KEY` | Yes | Auto-generated | Xray Reality public key in base64 format. Embedded in the `vless://` URL so clients can verify the server. |
| `XRAY_REALITY_SHORT_ID` | Yes | Auto-generated | An 8-character hex string (e.g. `a1b2c3d4`). Part of the REALITY handshake. |
| `XRAY_SNI` | No | `www.microsoft.com` | The domain that Xray impersonates. Must be a public domain with a valid TLS certificate on port 443. Do not use your own domain here. |
| `XRAY_FINGERPRINT` | No | `chrome` | TLS client fingerprint to mimic. Options: `chrome`, `firefox`, `safari`, `ios`, `android`, `edge`, `360`, `qq`, `random`, `randomized`. |

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

Place the private key in `xray/config.json` under `realitySettings.privateKey` and in `.env` as `XRAY_REALITY_PRIVATE_KEY`. Place the public key only in `.env` as `XRAY_REALITY_PUBLIC_KEY` (it is included in user config URLs, not in the server config).

### Short ID

```bash
openssl rand -hex 4
```

The short ID must also appear in `xray/config.json` under `realitySettings.shortIds`.

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

> **Warning:** Changing `XRAY_REALITY_PRIVATE_KEY`, `XRAY_REALITY_PUBLIC_KEY`, or `XRAY_REALITY_SHORT_ID` invalidates all existing `vless://` URLs. Every user will need a new config URL after such a change.

> **Warning:** Changing `JWT_SECRET` invalidates all active sessions in the management UI. All admins will be logged out and must log in again.

## Choosing XRAY_SNI

The SNI target is a domain that your server pretends to be when a non-management client connects on port 443. Requirements:

- Must have a valid TLS certificate (i.e. HTTPS works on it)
- Must be hosted on a server that does not block non-TLS traffic on port 443
- Should be a well-known domain to avoid raising suspicion

Common choices: `www.microsoft.com`, `www.apple.com`, `www.amazon.com`, `www.cloudflare.com`.

Do not use `localhost`, your own domain, or any domain you control.
