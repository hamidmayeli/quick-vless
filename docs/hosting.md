# Hosting Guide

This guide walks through setting up easy-xray on a fresh VPS from scratch.

## Server Requirements

| Resource | Minimum |
|----------|---------|
| CPU      | 1 vCPU  |
| RAM      | 512 MB  |
| Disk     | 10 GB   |
| OS       | Ubuntu 22.04 LTS or 24.04 LTS (or any Linux with Docker support) |
| Network  | Public IPv4 address, ports 80 and 443 open inbound |

## Domain Name

You need a domain where the management UI will be hosted (e.g. `manage.example.com`).

1. Purchase a domain from any registrar, or use a subdomain of a domain you already own.
2. Create an **A record** pointing your chosen hostname to the server's public IPv4 address.
3. Wait for DNS to propagate before running the installer — Caddy uses ACME HTTP-01 challenges to issue a TLS certificate, and the challenge requires the domain to resolve to this server.

> **Note:** The Xray VPN does not need a domain. The `XRAY_SNI` setting (`dl.google.com` by default) is the domain that Xray *impersonates* using TLS Reality — it is a domain you do not own, chosen because it has a valid TLS certificate on port 443. Do not set `XRAY_SNI` to your own domain.

## Firewall Configuration

Open ports 80 and 443 before installing. On Ubuntu with ufw:

```bash
sudo ufw allow 80/tcp
sudo ufw allow 443/tcp
sudo ufw enable
sudo ufw status
```

If your VPS provider has a network-level firewall (security groups, ACLs), add the same rules there.

## Installing Docker

```bash
curl -fsSL https://get.docker.com | sh
sudo usermod -aG docker $USER
newgrp docker
```

Verify the installation:

```bash
docker --version
docker compose version
```

Docker Compose V2 (the `docker compose` plugin, not the standalone `docker-compose` binary) is required.

## Installing easy-xray

### Quick install (no clone required)

Run the installer directly from GitHub — no need to clone the repository first:

```bash
curl -fsSL https://raw.githubusercontent.com/hamidmayeli/quick-vless/refs/heads/main/install.sh \
  | sudo bash -s -- --domain manage.example.com
```

#### Sample from branch

```bash
read -rsp 'Cloudflare API token: ' CF_API_TOKEN && export CF_API_TOKEN && echo

curl -fsSL https://raw.githubusercontent.com/hamidmayeli/quick-vless/refs/heads/the-branch-name/install.sh \
  | sudo --preserve-env=CF_API_TOKEN bash -s -- \
      --domain manage.example.com \
      --cf-domain cdn.example.com \
      --branch the-branch-name \
      --image-tag pr-21

unset CF_API_TOKEN
```

Later to update to use main

```bash
curl -fsSL https://raw.githubusercontent.com/hamidmayeli/quick-vless/refs/heads/main/install.sh \
  | sudo bash -s -- --domain manage.example.com --upgrade --branch main --image-tag latest
```

Deployment files are written to `./easy-xray/` in the current directory.

### Clone-based install (alternative)

If you prefer to inspect the code before running it, clone the repository first:

```bash
git clone https://github.com/hamidmayeli/quick-vless.git
cd quick-vless
./install.sh --domain manage.example.com
```

When run from the cloned directory, deployment files go into the `deployables/` subdirectory.

### Installing from a branch

To try a branch before it is merged, take `install.sh` from that branch **and** pass the same branch to `--branch`. Every deployment file (the manifest, `docker-compose.yaml`, the scripts and the profiles) is then downloaded from it:

```bash
BRANCH=my-feature
curl -fsSL "https://raw.githubusercontent.com/hamidmayeli/quick-vless/refs/heads/$BRANCH/install.sh" \
  | sudo bash -s -- --domain manage.example.com --branch "$BRANCH"
```

`--branch` is needed because a script piped from `curl` can't tell which URL it came from. It is saved in `.env` as `INSTALL_BRANCH`, so later runs and `--upgrade` keep using that branch. Pass `--branch main` to go back.

From a clone, check the branch out instead. The script detects it, so `--upgrade` downloads from the same branch:

```bash
git clone -b my-feature https://github.com/hamidmayeli/quick-vless.git
cd quick-vless
./install.sh --domain manage.example.com
```

The branch only decides the deployment files. The app runs from the `hamidmayeli/quick-vless` Docker image, which is `latest` (built from `main`) unless you pass `--image-tag`. If the branch changes the app, use the image built from it. A pull request build publishes `pr-<number>`:

```bash
curl -fsSL "https://raw.githubusercontent.com/hamidmayeli/quick-vless/refs/heads/$BRANCH/install.sh" \
  | sudo bash -s -- --domain manage.example.com --branch "$BRANCH" --image-tag pr-17
```

The script warns when the branch isn't `main` but the image is still `latest`. The tag is saved in `.env` as `APP_IMAGE_TAG`, and the daily `update.sh` keeps pulling that tag.

### What the script does

1. Downloads the files listed in `manifest.txt` into the deploy directory, from `--branch` (default `main`): `docker-compose.yaml`, `Caddyfile`, `Dockerfile.caddy`, `update.sh`, `xray-run.sh`, and the profile folders in `xray/`. Skipped if the files are already present (see [Upgrading](upgrading.md) for `--upgrade`).
2. Verifies Docker, Docker Compose, openssl, and curl are installed.
3. Generates a JWT signing secret, an Xray Reality key pair, a short ID, a VLESS Encryption key pair, and random WebSocket and XHTTP paths — all automatically.
4. Writes a `.env` file in the deploy directory with all configuration (permissions 600).
5. With `--cf-domain`, installs the Cloudflare Origin certificate into the `tls/` folder of each Cloudflare profile, creating it through the Cloudflare API when you pass an API token.
6. Fills in the `YOUR_*` placeholders in every profile's `config.json` and `client-config.template`.
7. Pulls the Docker images.
8. Starts all services with `docker compose up -d`.
9. Polls `http://localhost` until the management UI responds.
10. With `--cf-domain`, checks that the hostname is proxied by Cloudflare (warnings only — see [Cloudflare Setup](#cloudflare-setup)).

The address in the REALITY `vless://` links is `--host`, which defaults to `--domain`. The script does not detect the server's IP; pass `--host <ip>` to use an IP address instead.

On success, the management UI is available at `https://manage.example.com`.

### Providing your own keys

If you want to supply your own Xray key pair instead of generating one:

```bash
# Generate a key pair manually (requires openssl 3.x)
KEY_PEM=$(openssl genpkey -algorithm x25519)
PRIVATE_KEY=$(printf '%s' "$KEY_PEM" | openssl pkey -outform DER | tail -c 32 | base64 | tr '+/' '-_' | tr -d '=')
PUBLIC_KEY=$(printf '%s' "$KEY_PEM"  | openssl pkey -pubout -outform DER | tail -c 32 | base64 | tr '+/' '-_' | tr -d '=')
echo "Private: $PRIVATE_KEY"
echo "Public:  $PUBLIC_KEY"

# Then pass both keys to the installer
curl -fsSL https://raw.githubusercontent.com/hamidmayeli/quick-vless/refs/heads/main/install.sh \
  | sudo bash -s -- --domain manage.example.com \
    --xray-private-key <PRIVATE_KEY> \
    --xray-public-key <PUBLIC_KEY>
```

### Re-running after a failure

If the script fails partway through, re-run it with the same arguments from the same directory:

```bash
curl -fsSL https://raw.githubusercontent.com/hamidmayeli/quick-vless/refs/heads/main/install.sh \
  | sudo bash -s -- --domain manage.example.com
```

Completed steps are tracked in `.install-state` inside the deploy directory. The script skips them and continues from where it stopped. Existing secrets in `.env` are loaded automatically so they are never regenerated.

To force a single step to re-run:

```bash
curl -fsSL https://raw.githubusercontent.com/hamidmayeli/quick-vless/refs/heads/main/install.sh \
  | sudo bash -s -- --domain manage.example.com --force start_services
```

To clear all state and start from scratch:

```bash
curl -fsSL https://raw.githubusercontent.com/hamidmayeli/quick-vless/refs/heads/main/install.sh \
  | sudo bash -s -- --domain manage.example.com --reset
```

## First Login

Navigate to `https://manage.example.com` in a browser. Enter any username and password — the first successful login creates the **super admin** account with those credentials. Subsequent logins require the same credentials.

The super admin sees every VPN user and all usage, and is the only account that can manage other admins.

## Adding Admins

1. As the super admin, open the **Admins** page and click **Add Admin**.
2. Enter a username (letters, digits, `.`, `_`, `@`, `-`; up to 64 characters) and click **Save**. No password is set — the admin is listed as **Pending first login**.
3. Share the username with the new admin. The first time they sign in, the password they type becomes their password.
4. Admins only see the VPN users they created, and only those users' usage.

> **Note:** Until a pending admin signs in, anyone who knows the username can sign in first and choose the password. Ask new admins to sign in soon after you create them. If an account was claimed by the wrong person, delete it and create it again.

Deleting an admin signs them out immediately. Their VPN users are not deleted — they are reassigned to the super admin, so their clients keep working.

### Recovering a Lost Password

There is no password reset in the UI. To let an admin (including the super admin) choose a new password, edit `/data/admins.json` on the server and set that admin's `password_hash` and `password_salt` to `null`. Their next sign-in sets the new password. Do this only when you can sign in right away, since the account is claimable until then.

## Adding VPN Users

1. Open the **Users** page in the management UI.
2. Click **Add User**, enter a name, set quota and expiry if desired, and click **Save**.
3. Click **Copy Config URL** next to the user. Share that URL with them.
4. The user opens the URL in their VLESS-compatible client (v2rayN, Shadowrocket, sing-box, etc.) or scans the QR code shown on the **Config** page.

The config URL always returns the link of the **active profile**, so it stays the same when the super admin switches profiles. Clients that use it as a subscription pick up the new link when they refresh it.

## Connection Profiles

A profile is one way of serving VPN users. Each profile is a folder in `<deploy-dir>/xray/` holding:

| File | Contents |
|------|----------|
| `config.json` | An Xray config with at least one `vless` inbound. Users are added to every `vless` inbound. |
| `client-config.template` | The `vless://` link users get, with `{{secret}}` (the user's UUID) and `{{name}}` filled in by the app. |

Three profiles ship with easy-xray:

| Profile | Transport | Needs |
|---------|-----------|-------|
| `vless-reality` | VLESS + TCP + REALITY + Vision. Clients connect straight to the server and look like visits to `XRAY_SNI`. | Nothing extra — the default. |
| `vless-cf-xhttp` | VLESS Encryption + Vision over XHTTP + TLS, through Cloudflare's network. Useful when the server's IP is blocked. **The recommended Cloudflare profile.** | A hostname on Cloudflare and an Origin certificate — see [Cloudflare Setup](#cloudflare-setup). Clients with a recent Xray core. |
| `vless-cf-ws` | VLESS over WebSocket + TLS, through Cloudflare's network. | The same as `vless-cf-xhttp`. Kept for clients that don't support XHTTP or VLESS Encryption. |

Only one profile is active at a time. To switch, open the **Profiles** page as the super admin and click **Activate**. The app first checks the profile with `xray run -test`, then restarts Xray with it. Active connections drop for a few seconds, and users must refresh their config URL to get the new link. If Xray doesn't confirm the restart within 30 seconds, the app puts the previous profile back.

The Profiles page lists every folder in `xray/`, and explains why a profile can't be activated yet (for example, a placeholder `install.sh` hasn't filled in, or a missing certificate file). After editing a profile's files by hand, click **Re-apply** on the active profile. See [Writing your own profile](configuration.md#writing-your-own-profile) to add one.

## Cloudflare Setup

The Cloudflare profiles, `vless-cf-xhttp` and `vless-cf-ws`, send VPN traffic through Cloudflare. Clients connect to Cloudflare, and Cloudflare connects to your server. Both use the same hostname and the same Origin certificate, so setting up Cloudflare once enables both.

### Which Cloudflare profile

| | `vless-cf-xhttp` (recommended) | `vless-cf-ws` |
|---|---|---|
| Transport | XHTTP (`packet-up` mode, designed for CDNs) | WebSocket — Xray 26 logs that it is deprecated in favour of XHTTP |
| What Cloudflare can read | Nothing useful: VLESS Encryption keeps the VLESS traffic (user IDs, destinations) opaque, although Cloudflare terminates TLS | Cloudflare terminates TLS, so it sees the VLESS traffic in clear |
| Flow | XTLS Vision, which VLESS Encryption makes possible over XHTTP | None — "VLESS without flow" is deprecated in Xray |
| Clients | A recent Xray-core-based app (e.g. a current v2rayN or v2rayNG) that supports XHTTP and VLESS Encryption (`mlkem768x25519plus`) | Any VLESS client with WebSocket support |

If some users' apps can't connect with `vless-cf-xhttp`, activate `vless-cf-ws` instead. Both work with the same Cloudflare setup.

### Hostnames

You need two hostnames, not two registered domains. Two subdomains of one domain whose DNS is on Cloudflare are enough:

| Hostname | Cloudflare DNS record | Used by |
|----------|-----------------------|---------|
| `manage.example.com` (`--domain`, and `--host` by default) | **DNS only** (grey cloud) → server IP | The management UI, and the address REALITY clients connect to |
| `cdn.example.com` (`--cf-domain`) | **Proxied** (orange cloud) → server IP | The Cloudflare profiles |

- Keep the management hostname DNS only: Caddy gets its certificate from Let's Encrypt, and REALITY can't pass through Cloudflare.
- Keep the Cloudflare hostname one level deep (`cdn.example.com`, not `ws.cdn.example.com`). Cloudflare's free edge certificate only covers first-level subdomains.
- If your domain's DNS isn't on Cloudflare, either move its nameservers to Cloudflare, or use a second domain that is on Cloudflare for the Cloudflare hostname.

### Cloudflare settings

1. **DNS:** create the Cloudflare hostname's A record and turn on **Proxied**.
2. **SSL/TLS → Overview:** set the encryption mode to **Full (strict)**.
3. **Network:** make sure **WebSockets** is on (it is by default) if you use `vless-cf-ws`.

Nothing else is needed for XHTTP. Cloudflare connects to the server over HTTP/1.1, and `vless-cf-xhttp` only offers HTTP/1.1 to it. This is deliberate: Xray's XHTTP server picks HTTP/1.1 or HTTP/2 from the first protocol it advertises, not per connection, and offering only HTTP/1.1 keeps Cloudflare on that even if **HTTP/2 to Origin** is turned on. Clients still use HTTP/2 to Cloudflare.

### The Origin certificate

Cloudflare connects to your server over TLS, so the server needs a certificate for the Cloudflare hostname. The script installs it into the `tls/` folder of every Cloudflare profile. A Cloudflare **Origin CA** certificate is free and valid for up to 15 years. Only Cloudflare trusts it, which is all this profile needs: clients only ever see Cloudflare's own certificate.

**Option A — let the script create it (recommended).** Create an API token, and the script asks Cloudflare for a certificate covering `--cf-domain`:

1. In Cloudflare, open **My Profile → API Tokens → Create Token → Create Custom Token**.
2. Add the permission **Zone → SSL and Certificates → Edit**.
3. Under **Zone Resources**, choose **Include → Specific zone →** your domain.
4. Create the token and copy it.

```bash
read -rsp 'Cloudflare API token: ' CF_API_TOKEN && export CF_API_TOKEN   # typed, not saved in history
curl -fsSL https://raw.githubusercontent.com/hamidmayeli/quick-vless/refs/heads/main/install.sh \
  | sudo --preserve-env=CF_API_TOKEN bash -s -- --domain manage.example.com --cf-domain cdn.example.com
unset CF_API_TOKEN
```

How the token is handled:

- **It is used once.** The script doesn't store it in `.env` or anywhere else, and passes it to `curl` through a temporary file so it doesn't appear in the process list.
- **Prefer the `CF_API_TOKEN` environment variable** over `--cf-api-token TOKEN`, which would leave the token in your shell history.
- **You can delete it afterwards** in the Cloudflare dashboard.

How the certificate is made and reused:

- **The private key never leaves the server.** It is generated on the server, and Cloudflare only receives a signing request.
- **Re-runs reuse the installed certificate.** A new certificate is only requested when the installed one doesn't cover `--cf-domain`, or expires within 30 days.

**Option B — create it yourself.** In Cloudflare, open **SSL/TLS → Origin Server → Create Certificate**. Keep the default hostnames (`example.com` and `*.example.com`; one certificate covers every first-level subdomain), choose a validity of up to 15 years, and save the certificate and the private key as two files:

```bash
./install.sh --domain manage.example.com --cf-domain cdn.example.com \
  --cf-origin-cert origin.pem --cf-origin-key origin.key
```

Either way, this works on a new server and on an existing install (re-run with the same `--domain`). The script refuses a certificate that doesn't match its key or doesn't cover `--cf-domain`. On an existing install, it restarts Xray when the certificate changes.

The same run also fills in the Cloudflare profiles. For `vless-cf-xhttp` it generates a VLESS Encryption key pair and a random XHTTP path, saved in `.env` like the REALITY keys. Then open the **Profiles** page and activate `vless-cf-xhttp` (or `vless-cf-ws`).

At the end, the script requests `https://cdn.example.com/` once and warns if something is off:

| Warning | Meaning |
|---------|---------|
| Could not open `https://cdn.example.com` | Cloudflare's edge certificate isn't valid for the hostname: it is more than one level deep, or the zone isn't active on Cloudflare yet. |
| Not proxied by Cloudflare | The DNS record is grey. VPN clients would reach the server directly, see the Origin certificate, and fail. Turn on the orange cloud. |
| Cloudflare answered 525 or 526 | Only checked while a Cloudflare profile is active: Cloudflare couldn't complete TLS with the server (525) or rejected its certificate (526). Check the Origin certificate files and the Full (strict) mode. |

## Data Storage

Persistent data lives in `<deploy-dir>/data/`, bind-mounted into the containers:

| Path on the host | Contents |
|------|----------|
| `data/app/users.json` | VPN user accounts (name, UUID, quota, expiry, enabled, `created_by` admin) |
| `data/app/admins.json` | Management UI admin accounts (role, hashed password — `null` while pending first login) |
| `data/app/settings.json` | The active profile chosen on the Profiles page |
| `data/app/usage_history/` | Daily bandwidth snapshots in CSV format (`YYYYMMDD.csv`) |
| `data/xray/` | The config Xray runs, generated by the app from the active profile — don't edit it |
| `data/caddy/` | Caddy's certificates and state |

The profiles live in `<deploy-dir>/xray/`. They are mounted read-only into both the `xray` and `app` containers at `/etc/xray/profiles`. They contain the REALITY private key and the Cloudflare Origin key, so treat that folder like `.env`.

> **Deploy directory:** `./easy-xray/` when installed via curl, `./deployables/` when installed from a clone. The commands below use `<deploy-dir>` as a placeholder — substitute the correct path for your setup.

## Checking Service Status

Run from the deploy directory:

```bash
cd <deploy-dir>
docker compose ps
docker compose logs app
docker compose logs xray
docker compose logs caddy
```

To follow logs in real time:

```bash
docker compose logs -f app
```

## Backup

Back up `data/app/` (users, admins, settings, usage) and the configuration: `.env` and the `xray/` profile folders, which hold your keys and certificates.

```bash
cd <deploy-dir>

# User data (stop the app first so the files are consistent)
docker compose stop app
sudo tar czf ~/easy-xray-data-$(date +%Y%m%d).tar.gz data/app
docker compose start app

# Configuration (contains secrets — keep this secure)
sudo tar czf ~/easy-xray-config-$(date +%Y%m%d).tar.gz .env xray
```

Store backups off the server (object storage, another VPS, your own machine). `data/xray/` and `data/caddy/` don't need backing up: the app regenerates the Xray config, and Caddy re-issues its certificate.

## Restore from Backup

```bash
cd <deploy-dir>
docker compose down

sudo rm -rf data/app
sudo tar xzf ~/easy-xray-data-YYYYMMDD.tar.gz
sudo tar xzf ~/easy-xray-config-YYYYMMDD.tar.gz   # only if .env or xray/ were lost

docker compose up -d
```
