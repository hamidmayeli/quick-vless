# easy-xray

Self-hosted VLESS VPN manager with a web UI. Provision users, track bandwidth usage, and distribute connection configs — all from a browser. Built on [Xray-core](https://github.com/XTLS/Xray-core) with VLESS + TCP + REALITY + Vision.

## Architecture

```
                        Port 443
  VPN client ──────────────────────── Caddy (SNI router)
                                          │
                      ┌───────────────────┴──────────────────────┐
                      │ SNI = manage.example.com                  │ SNI = anything else
                      ▼                                           ▼
             Management App                                 Xray (VPN core)
           (React UI + REST API)                         (VLESS + REALITY)
```

Port 443 is shared. Caddy reads the TLS SNI field before decrypting: traffic destined for your management domain gets TLS-terminated and proxied to the app; everything else is passed through as raw TCP directly to Xray. VPN users never touch the management app, and the management app never handles VPN traffic.

Port 80 is used only for ACME HTTP-01 certificate challenges and HTTP → HTTPS redirect.

## Requirements

- VPS with a public IPv4 address (minimum 1 vCPU, 512 MB RAM)
- Domain with an A record pointing to the server (for the management UI TLS cert)
- Ports 80 and 443 open inbound
- Docker and Docker Compose installed on the server

## Quick Start

```bash
# 1. Clone the repository
git clone https://github.com/hamidmayeli/easy-xray.git
cd easy-xray

# 2. Run the install script (all secrets are auto-generated)
./install.sh --domain manage.example.com

# 3. Open the management UI
#    https://manage.example.com
#    Enter any username and password — the first login creates the admin account

# 4. Add users on the Users page, then click "Copy Config URL" next to each user

# 5. Share the config URL with each VPN user
#    They open it in their VLESS client or scan the QR code on the Config page
```

## Documentation

- [Hosting guide](docs/hosting.md) — server setup, DNS, firewall, backups
- [Configuration reference](docs/configuration.md) — all environment variables explained
- [Upgrading and maintenance](docs/upgrading.md) — upgrades, rollbacks, backups, logs
