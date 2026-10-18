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

> **Note:** The Xray VPN does not need a domain. The `XRAY_SNI` setting (`www.microsoft.com` by default) is the domain that Xray *impersonates* using TLS Reality — it is a domain you do not own, chosen because it has a valid TLS certificate on port 443. Do not set `XRAY_SNI` to your own domain.

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

Deployment files are written to `./easy-xray/` in the current directory.

### Clone-based install (alternative)

If you prefer to inspect the code before running it, clone the repository first:

```bash
git clone https://github.com/hamidmayeli/easy-xray.git
cd easy-xray
./install.sh --domain manage.example.com
```

When run from the cloned directory, deployment files go into the `deployables/` subdirectory.

### What the script does

1. Downloads `docker-compose.yaml`, `Caddyfile`, `Dockerfile.caddy`, and `xray/config.json` into the deploy directory (skipped if files are already present).
2. Verifies Docker, Docker Compose, openssl, and curl are installed.
3. Generates a JWT signing secret, an Xray Reality key pair, and a short ID — all automatically.
4. Auto-detects the server's public IP address (used in the vless:// URLs given to users).
5. Writes a `.env` file in the deploy directory with all configuration (permissions 600).
6. Patches `xray/config.json` with the generated private key and short ID.
7. Pulls the Docker images.
8. Starts all services with `docker compose up -d`.
9. Polls `http://localhost` until the management UI responds.

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

## Data Storage

All persistent data lives in a named Docker volume `quick-vless_app-data`, mounted at `/data` inside the management app container:

| Path | Contents |
|------|----------|
| `/data/users.json` | VPN user accounts (name, UUID, quota, expiry, enabled, `created_by` admin) |
| `/data/admins.json` | Management UI admin accounts (role, hashed password — `null` while pending first login) |
| `/data/usage_history/` | Daily bandwidth snapshots in CSV format (`YYYYMMDD.csv`) |

The Xray configuration template lives in `xray/config.json` inside the deploy directory on the host and is bind-mounted into both the `xray` and `app` containers.

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

Back up the `app-data` volume and the deploy directory (which contains `.env` and `xray/config.json`).

```bash
cd <deploy-dir>

# Backup data volume
docker run --rm \
  -v quick-vless_app-data:/data \
  -v "$(pwd)":/backup \
  ubuntu \
  tar czf /backup/easy-xray-data-$(date +%Y%m%d).tar.gz /data

# Backup deploy directory (contains .env and xray config — keep this secure)
tar czf ~/easy-xray-config-$(date +%Y%m%d).tar.gz .env xray/config.json
```

Store backups off the server (object storage, another VPS, your own machine).

## Restore from Backup

```bash
cd <deploy-dir>
docker compose down

docker run --rm \
  -v quick-vless_app-data:/data \
  -v "$(pwd)":/backup \
  ubuntu \
  sh -c "rm -rf /data/* && tar xzf /backup/easy-xray-data-YYYYMMDD.tar.gz -C /"

docker compose up -d
```
