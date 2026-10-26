# Hosting and Profiles

This guide covers day-to-day use of an installed easy-xray server: accounts, connection profiles, certificates, and service data. Start with [Requirements](requirements.md) and [Installation](installation.md); see [Updating and maintenance](upgrading.md) for upgrades, backups, and service commands.

## First Login

Open `https://<management-domain>` in a browser. The first successful sign-in creates the super admin account using the username and password entered on the login page. Later sign-ins require those credentials.

The super admin can see all VPN users and usage and is the only account that can manage other admins.

## Admins

1. As the super admin, open **Admins** and select **Add Admin**.
2. Enter a username using letters, digits, `.`, `_`, `@`, or `-` (up to 64 characters), then save. The account has no password yet and is shown as **Pending first login**.
3. Share the username with the admin. Their first sign-in sets their password.

Admins can see only the VPN users they created and those users' usage. Until a pending admin signs in, anyone who knows the username could claim it; ask them to sign in promptly. If the wrong person claims an account, delete it and create it again.

Deleting an admin signs them out immediately. Their users are reassigned to the super admin, so existing client configurations keep working.

### Recover a Lost Password

There is no password reset in the UI. On the server, edit `<deploy-dir>/data/app/admins.json` and set the account's `password_hash` and `password_salt` to `null`. The next sign-in sets a new password. Do this only when you can sign in immediately, because the account is claimable until then.

## VPN Users

1. Open **Users** in the management UI and select **Add User**.
2. Enter a name and, if desired, set a quota and expiry.
3. Select **Copy Config URL** or open the user's QR code from the **Config** page.
4. The user imports the URL or QR code into a VLESS-compatible client, such as v2rayN, Shadowrocket, or sing-box.

The config URL always returns a link for the active profile. Subscription clients can refresh the same URL after the super admin changes profiles.

## Connection Profiles

A profile is a directory under `<deploy-dir>/xray/` containing an Xray `config.json` and a `client-config.template`. Only one profile is active at a time. The Profiles page explains why a profile is unavailable; select **Activate** to test and apply one. Xray checks the configuration before switching, and the app restores the previous profile if Xray does not start within 30 seconds. Existing connections may briefly drop, and users should refresh their config URL after a switch.

| Profile | Transport | Requirements |
|---------|-----------|--------------|
| `vless-reality` | TCP + REALITY + Vision | Default profile; no VPN hostname required. |
| `vless-ws-tls` | WebSocket + TLS | DNS-only VPN hostname and Let's Encrypt certificate; see [Direct TLS profiles](#direct-tls-profiles). |
| `vless-xhttp-tls` | XHTTP + TLS with VLESS Encryption and Vision | DNS-only VPN hostname and Let's Encrypt certificate; recent Xray clients; see [Direct TLS profiles](#direct-tls-profiles). |
| `vless-cf-ws` | WebSocket + TLS through Cloudflare | Proxied Cloudflare hostname and Origin certificate; see [Cloudflare profiles](#cloudflare-profiles). |
| `vless-cf-xhttp` | XHTTP + TLS with VLESS Encryption and Vision through Cloudflare | Proxied Cloudflare hostname and Origin certificate; recent Xray clients; see [Cloudflare profiles](#cloudflare-profiles). |

For custom profile files and install-time placeholders, see [Writing your own profile](configuration.md#writing-your-own-profile).

## Direct TLS Profiles

Direct TLS profiles connect to the server itself. Their hostname must be DNS-only, point directly to this server, and differ from the management hostname. Port 80 must be reachable from the internet for the Let's Encrypt HTTP-01 challenge. See [Requirements](requirements.md#hostnames-and-dns) and [Installation](installation.md#install-from-main) for setup.

The installer stores the certificate in both direct-TLS profile folders. It configures daily Certbot renewal and a deploy hook that updates the profile copies and restarts Xray when the certificate renews. The default `vless-reality` profile does not use this certificate.

After installation, activate `vless-ws-tls` or `vless-xhttp-tls` on the Profiles page. To select one on a new installation, use `--profile vless-ws-tls` or `--profile vless-xhttp-tls`.

## Cloudflare Profiles

The Cloudflare profiles route client traffic through Cloudflare. They require a Cloudflare-proxied hostname and an Origin CA certificate installed on the server. Use a hostname one level deep, such as `cdn.example.com`, and set Cloudflare's SSL/TLS mode to **Full (strict)**. The management and REALITY hostname should remain DNS-only.

### Choose a Profile

| | `vless-cf-xhttp` (recommended) | `vless-cf-ws` |
|---|---|---|
| Transport | XHTTP (`packet-up`) | WebSocket; deprecated by Xray 26 in favor of XHTTP |
| VLESS traffic at Cloudflare | VLESS Encryption keeps it opaque, although Cloudflare terminates TLS | TLS is terminated at Cloudflare, which can read the VLESS traffic |
| Flow | XTLS Vision | None |
| Client support | Recent Xray-core client with XHTTP and VLESS Encryption (`mlkem768x25519plus`) | VLESS client with WebSocket support |

Use `vless-cf-ws` if a user's client does not support `vless-cf-xhttp`. Both use the same hostname and certificate.

### DNS and Cloudflare Settings

1. Create an A record for the Cloudflare hostname pointing to the server and enable **Proxied** (orange cloud).
2. Keep the management hostname DNS-only (grey cloud).
3. Set **SSL/TLS → Overview** to **Full (strict)**.
4. Leave **WebSockets** enabled if using `vless-cf-ws` (it is enabled by default).

The free Cloudflare edge certificate covers first-level subdomains, not names such as `ws.cdn.example.com`.

### Origin Certificate

The server needs an Origin CA certificate for the Cloudflare hostname. The certificate is installed into each Cloudflare profile's `tls/` directory. Only Cloudflare needs to trust it; VPN clients see Cloudflare's edge certificate.

During installation, either let the installer create the certificate using a Cloudflare API token with **Zone → SSL and Certificates → Edit** permission, or provide an existing certificate and key with `--cf-origin-cert` and `--cf-origin-key`. The token is used once and is not stored. See [Installation](installation.md#install-from-main) for commands.

On reruns, the installer reuses a valid installed certificate. With an API token, it requests a replacement if the installed certificate does not cover the configured hostname or expires within 30 days.

After setup, activate `vless-cf-xhttp` or `vless-cf-ws` on the Profiles page. The installer checks the hostname through Cloudflare and warns if it is not proxied or has a certificate error.

## Persistent Data

All persistent files live under `<deploy-dir>/` and are bind-mounted into containers:

| Host path | Contents |
|-----------|----------|
| `data/app/users.json` | VPN users, including UUID, quota, expiry, enabled state, and creator. |
| `data/app/admins.json` | Admin accounts and password hashes. |
| `data/app/settings.json` | Active profile selection. |
| `data/app/usage_history/` | Daily bandwidth snapshots in CSV format. |
| `data/xray/` | Generated Xray runtime configuration; do not edit it. |
| `data/caddy/` | Caddy certificates and state. |
| `xray/` | Profile configuration, REALITY keys, and profile certificates. Treat as sensitive. |
| `.env` | Domains, generated secrets, and install settings. Treat as sensitive. |

For backups, restores, logs, and service status commands, see [Updating and maintenance](upgrading.md).
