# Requirements

Review this checklist before installing easy-xray. For the install commands, see [Installation](installation.md).

## Server

| Resource | Minimum |
|----------|---------|
| CPU | 1 vCPU |
| RAM | 512 MB |
| Disk | 10 GB |
| Operating system | Ubuntu 22.04 or 24.04 recommended; another Linux distribution with Docker support can work |
| Network | Public server address; inbound TCP ports 80 and 443 reachable from the internet |

The installer can install Docker Engine, Docker Compose V2, OpenSSL, and curl automatically on apt-based systems. On other systems, install these prerequisites first. Docker Compose must be the `docker compose` plugin, not the standalone `docker-compose` command.

Allow TCP ports 80 and 443 in both the server firewall and any provider firewall/security group. For Ubuntu with UFW:

```bash
sudo ufw allow OpenSSH
sudo ufw allow 80/tcp
sudo ufw allow 443/tcp
sudo ufw enable
sudo ufw status
```

Port 80 is used for HTTP-01 certificate challenges; port 443 serves the management UI and VPN traffic.

## Hostnames and DNS

A domain or subdomain is required for the management UI. Create an A record pointing it to the server's public IPv4 address, and wait for DNS to resolve before installation. Keep this record DNS-only; do not proxy it through Cloudflare.

Additional hostnames are optional:

| Purpose | Example | DNS requirement |
|---------|---------|-----------------|
| Direct TLS VPN profiles | `vpn.example.com` | DNS-only, points directly to this server, and differs from the management hostname. Port 80 must be reachable for its Let's Encrypt certificate. |
| Cloudflare VPN profiles | `cdn.example.com` | Proxied through Cloudflare and one level deep (not `ws.cdn.example.com`). Requires a Cloudflare Origin CA certificate. |

The direct-TLS and Cloudflare hostnames are separate options. See [Hosting and profiles](hosting.md) for their setup and profile details.

The default REALITY profile does not require a VPN hostname. Its `XRAY_SNI` value (`dl.google.com` by default) is a public domain that Xray impersonates; it is not a domain you own. See [Configuration Reference](configuration.md#choosing-xray_sni).
