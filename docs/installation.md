# Installation

This guide installs easy-xray directly from GitHub. A repository clone is not required. Check [Requirements](requirements.md) first.

## Choose a Deployment Directory

The installer creates `./easy-xray/` in the directory where you run it. Run the command from a stable location; the installer uses the existing `.env` and state file there on later runs. If you move to a different working directory, pass `--deploy-dir /absolute/path/to/easy-xray`.

## Install from `main`

Create the management hostname's DNS A record pointing directly to the server, then run:

```bash
curl -fsSL https://raw.githubusercontent.com/hamidmayeli/quick-vless/refs/heads/main/install.sh \
  | sudo bash -s -- --domain manage.example.com
```

Replace `manage.example.com` with your hostname. The installer uses deployment files from `main` and the `latest` app image by default. It creates secrets automatically and saves configuration in `.env`.

Optional arguments:

- `--host SERVER_IP` sets the address used in REALITY client links. By default, the management hostname is used.
- `--vpn-domain vpn.example.com` enables direct TLS profiles. The hostname must be DNS-only and point directly to this server.
- `--cf-domain cdn.example.com` enables Cloudflare profiles. The hostname must be proxied through Cloudflare.
- `--profile PROFILE` selects the profile active on first start. The default is `vless-reality`.

See [Hosting and profiles](hosting.md) for DNS and certificate details for the optional profiles.

### Complete example with all three hostnames

This example prompts for the deployment branch and app image tag, defaulting to `main` and `latest`. It uses sample hostnames for the management UI, direct TLS VPN profiles, and Cloudflare VPN profiles. Point `manage.example.com` and `vpn.example.com` directly to the server with Cloudflare proxying off; proxy `cdn.example.com` through Cloudflare. Create an API token with **Zone → SSL and Certificates → Edit** permission to have the installer create the Cloudflare Origin certificate. For a feature branch that changes the app, enter its matching image tag (for example, `pr-21`).

```bash
read -rp 'Branch name [main]: ' BRANCH_NAME
BRANCH_NAME=${BRANCH_NAME:-main}
read -rp 'Docker tag [latest]: ' DOCKER_TAG
DOCKER_TAG=${DOCKER_TAG:-latest}
read -rsp 'Cloudflare API token: ' CF_API_TOKEN && export CF_API_TOKEN && echo
curl -fsSL "https://raw.githubusercontent.com/hamidmayeli/quick-vless/refs/heads/$BRANCH_NAME/install.sh" \
  | sudo --preserve-env=CF_API_TOKEN bash -s -- \
      --domain manage.example.com \
      --vpn-domain vpn.example.com \
      --cf-domain cdn.example.com \
      --branch "$BRANCH_NAME" \
      --image-tag "$DOCKER_TAG"
unset CF_API_TOKEN
```

The token is used once and is not saved in `.env`. For an existing Origin certificate, pass `--cf-origin-cert` and `--cf-origin-key` instead. See [Cloudflare profiles](hosting.md#cloudflare-profiles).

## Install from a Branch

Use the branch's installer URL and pass the same branch to `--branch`. If that branch changes the app, set `--image-tag` to the matching image; pull request builds use tags such as `pr-21`.

```bash
BRANCH=my-feature
IMAGE_TAG=pr-21
curl -fsSL "https://raw.githubusercontent.com/hamidmayeli/quick-vless/refs/heads/$BRANCH/install.sh" \
  | sudo bash -s -- \
      --domain manage.example.com \
      --branch "$BRANCH" \
      --image-tag "$IMAGE_TAG"
```

A piped installer cannot detect the branch from its URL, which is why `--branch` is needed. The installer saves the selected branch as `INSTALL_BRANCH` and the image tag as `APP_IMAGE_TAG` in `.env`. If the branch only changes deployment files and not the app, omit `--image-tag`; the default `latest` image is built from `main`.

## Deployment Files

| Path under `<deploy-dir>/` | Contents |
|----------------------------|----------|
| `.env` | Domains, branch and image tag, and generated secrets. Treat it as sensitive. |
| `.install-state` | Completed installer steps; used to resume after a failure. |
| `docker-compose.yaml`, `Caddyfile`, `update.sh`, `xray-run.sh` | Service and deployment files. |
| `xray/<profile>/` | Per-profile Xray configuration and client template. TLS certificates and private keys may also be present; treat this directory as sensitive. |
| `data/app/` | Users, admins, settings, and usage history. |
| `data/caddy/`, `data/xray/` | Caddy state and generated Xray runtime configuration. |

The installer downloads deployment files but does not replace existing profile files. The app image is separate from these files and is selected by `APP_IMAGE_TAG`.

## Resume an Installation

If an installation fails, rerun from the same working directory and use the same installer branch. Saved settings are read from `.env`, and completed steps are skipped. On a first run, `--domain` is required; on a rerun, the installer reads it from `.env`.

```bash
curl -fsSL https://raw.githubusercontent.com/hamidmayeli/quick-vless/refs/heads/main/install.sh \
  | sudo bash
```

For a feature-branch installation, use that branch's installer URL again. If the deployment is elsewhere, add `--deploy-dir /absolute/path/to/easy-xray`.

## Next Steps

- Sign in and manage users and profiles in [Hosting and profiles](hosting.md).
- Change branches or update images and deployment files using [Updating and maintenance](upgrading.md).
- Change environment variables or profile configuration using [Configuration Reference](configuration.md).
