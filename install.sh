#!/usr/bin/env bash
set -euo pipefail

# ============================================================
#  easy-xray installer
#  Non-interactive deployment script for the easy-xray
#  VLESS VPN manager (Xray + management API + Caddy).
# ============================================================

# When piped via curl, BASH_SOURCE[0] is empty or /dev/stdin — handle gracefully
_src="${BASH_SOURCE[0]:-}"
if [[ -n "$_src" && "$_src" != "/dev/stdin" && -f "$_src" ]]; then
  SCRIPT_DIR="$(cd "$(dirname "$_src")" && pwd)"
else
  SCRIPT_DIR=""
fi

# ------------------------------------------------------------
# Logging helpers
# ------------------------------------------------------------
log()  { echo "[$(date '+%H:%M:%S')] $*"; }
info() { echo "[$(date '+%H:%M:%S')] INFO  $*"; }
warn() { echo "[$(date '+%H:%M:%S')] WARN  $*" >&2; }
error(){ echo "[$(date '+%H:%M:%S')] ERROR $*" >&2; }
die()  { error "$*"; exit 1; }
valid_hostname() {
  [[ "$1" =~ ^[A-Za-z0-9]([A-Za-z0-9-]*[A-Za-z0-9])?(\.[A-Za-z0-9]([A-Za-z0-9-]*[A-Za-z0-9])?)*$ ]]
}

# ------------------------------------------------------------
# Usage
# ------------------------------------------------------------
usage() {
  cat <<EOF
Usage: $(basename "$0") [OPTIONS]

Non-interactive deployment script for easy-xray.

Run directly without cloning:
  bash <(curl -fsSL https://raw.githubusercontent.com/hamidmayeli/quick-vless/refs/heads/main/install.sh) --domain manage.example.com

Run from a branch (pass the same branch to --branch so every file comes from it):
  bash <(curl -fsSL https://raw.githubusercontent.com/hamidmayeli/quick-vless/refs/heads/BRANCH/install.sh) --domain manage.example.com --branch BRANCH

Required:
  --domain DOMAIN           Management UI domain (e.g. manage.example.com)

Optional:
  --host HOST               Server IP/domain in REALITY vless:// URLs
                            (default: same as --domain)
  --vpn-domain DOMAIN       DNS-only hostname for direct VLESS+WS and XHTTP+TLS
                            profiles. Gets a Let's Encrypt certificate via Caddy's
                            HTTP-01 webroot; must differ from --domain and --host
  --profile NAME            Xray profile active on first start: a folder in xray/
                            (default: vless-reality). Later, the super admin
                            switches profiles on the Profiles page.
  --cf-domain DOMAIN        Hostname proxied by Cloudflare for the Cloudflare
                            profiles, vless-cf-xhttp and vless-cf-ws
                            (e.g. cdn.example.com, one level deep)
  --cf-api-token TOKEN      Cloudflare API token with the permission
                            Zone → SSL and Certificates → Edit. Creates the
                            Origin CA certificate for --cf-domain (15 years).
                            Used once, never stored. Prefer the CF_API_TOKEN
                            environment variable to keep it out of shell history
  --cf-origin-cert FILE     Or: an existing Cloudflare Origin CA certificate (PEM)
                            covering --cf-domain
  --cf-origin-key FILE      Private key (PEM) of that certificate
  --branch NAME             Git branch to download every deployment file from
                            (default: the branch checked out in a clone, else
                            the branch of the last install, else main)
  --image-tag TAG           Tag of the hamidmayeli/quick-vless app image
                            (default: latest, built from main). E.g. pr-17
                            for the image a pull request build published
  --jwt-secret SECRET       JWT signing secret
                            (default: openssl rand -hex 32)
  --xray-private-key KEY    Xray Reality private key (pair with --xray-public-key)
  --xray-public-key KEY     Xray Reality public key  (pair with --xray-private-key)
  --xray-short-id ID        Xray Reality short ID
                            (default: openssl rand -hex 4)
  --xray-sni SNI            SNI hostname for Reality
                            (default: dl.google.com)
  --xray-fingerprint FP     TLS fingerprint for Reality
                            (default: chrome)
  --deploy-dir DIR          Directory for deployment files
                            (default: \$SCRIPT_DIR/deployables if it exists,
                             \$SCRIPT_DIR if cloned, or \$PWD/easy-xray otherwise)
  --skip-pull               Skip 'docker compose pull'
  --upgrade                 Refresh the deployment files of an existing install
                            (keeps .bak copies; never overwrites profile files),
                            migrate an old xray/config.json, then pull and restart
  --force STEP              Force a specific step to re-run even if already done
  --reset                   Clear the state file and start fresh
  -h, --help                Print this message and exit

Steps (in execution order; * = runs every time):
  download_files            Download the files listed in manifest.txt
  migrate_legacy_layout   * Move an old xray/config.json to xray/vless-reality/
  install_prerequisites     Install Docker Engine + openssl + curl via apt
                            (no-op on non-apt systems)
  install_vpn_prerequisites With --vpn-domain: install certbot and cron via apt
  check_prerequisites       Verify docker, docker compose (V2), openssl, curl
  generate_secrets        * Fill in any missing secrets/keys
  write_env_file          * Write \$DEPLOY_DIR/.env (chmod 600)
  install_cf_certificate  * With --cf-domain: install (or, with an API token,
                            create) the Origin certificate in the tls/ folder of
                            every Cloudflare profile
  write_xray_profiles     * Fill in the YOUR_* placeholders of every profile
  pull_images               docker compose pull
  install_vpn_certificate * With --vpn-domain: issue/renew the Let's Encrypt
                            certificate and copy it to both direct TLS profiles
  start_services            docker compose up -d (and restart Xray if its
                            certificate changed)
  setup_cron_jobs           Install crontab entry: update.sh daily at 3:00 AM
                            and Certbot renewal daily at 3:30 AM when configured
  verify_health             Poll http://localhost until ready (warn on timeout)
  verify_cloudflare       * With --cf-domain: check the hostname is proxied by
                            Cloudflare (warns only)

State file: \$DEPLOY_DIR/.install-state
  Completed step names are recorded one per line. Re-running the script
  skips steps already recorded (except the * steps, which only fill in what
  is missing). Use --force STEP to re-run one step, or --reset to clear all
  state and start over.
EOF
}

# ------------------------------------------------------------
# Defaults
# ------------------------------------------------------------
PARAM_DOMAIN=""
PARAM_HOST=""
PARAM_JWT_SECRET=""
PARAM_XRAY_PRIVATE_KEY=""
PARAM_XRAY_PUBLIC_KEY=""
PARAM_XRAY_SHORT_ID=""
PARAM_XRAY_SNI="dl.google.com"
PARAM_XRAY_FINGERPRINT="chrome"
PARAM_PROFILE=""
PARAM_VPN_DOMAIN=""
PARAM_CF_DOMAIN=""
PARAM_CF_ORIGIN_CERT=""
PARAM_CF_ORIGIN_KEY=""
PARAM_CF_API_TOKEN="${CF_API_TOKEN:-}"
PARAM_WS_ID=""
PARAM_XHTTP_ID=""
PARAM_VLESS_ENC_PRIVATE_KEY=""
PARAM_VLESS_ENC_PUBLIC_KEY=""
PARAM_BRANCH=""
PARAM_IMAGE_TAG=""
PARAM_DEPLOY_DIR=""
PARAM_SKIP_PULL=false
PARAM_UPGRADE=false
PARAM_FORCE_STEP=""
PARAM_RESET=false

# ------------------------------------------------------------
# Argument parsing  (--key value  or  --key=value)
# ------------------------------------------------------------
while [[ $# -gt 0 ]]; do
  case "$1" in
    --domain=*)         PARAM_DOMAIN="${1#*=}";            shift ;;
    --domain)           PARAM_DOMAIN="${2:-}";             shift 2 ;;
    --host=*)           PARAM_HOST="${1#*=}";              shift ;;
    --host)             PARAM_HOST="${2:-}";               shift 2 ;;
    --jwt-secret=*)     PARAM_JWT_SECRET="${1#*=}";        shift ;;
    --jwt-secret)       PARAM_JWT_SECRET="${2:-}";         shift 2 ;;
    --xray-private-key=*) PARAM_XRAY_PRIVATE_KEY="${1#*=}"; shift ;;
    --xray-private-key)   PARAM_XRAY_PRIVATE_KEY="${2:-}";  shift 2 ;;
    --xray-public-key=*)  PARAM_XRAY_PUBLIC_KEY="${1#*=}";  shift ;;
    --xray-public-key)    PARAM_XRAY_PUBLIC_KEY="${2:-}";   shift 2 ;;
    --xray-short-id=*)  PARAM_XRAY_SHORT_ID="${1#*=}";    shift ;;
    --xray-short-id)    PARAM_XRAY_SHORT_ID="${2:-}";      shift 2 ;;
    --xray-sni=*)       PARAM_XRAY_SNI="${1#*=}";          shift ;;
    --xray-sni)         PARAM_XRAY_SNI="${2:-}";           shift 2 ;;
    --xray-fingerprint=*) PARAM_XRAY_FINGERPRINT="${1#*=}"; shift ;;
    --xray-fingerprint)   PARAM_XRAY_FINGERPRINT="${2:-}";  shift 2 ;;
    --profile=*)        PARAM_PROFILE="${1#*=}";           shift ;;
    --profile)          PARAM_PROFILE="${2:-}";            shift 2 ;;
    --vpn-domain=*)     PARAM_VPN_DOMAIN="${1#*=}";        shift ;;
    --vpn-domain)       PARAM_VPN_DOMAIN="${2:-}";         shift 2 ;;
    --cf-domain=*)      PARAM_CF_DOMAIN="${1#*=}";         shift ;;
    --cf-domain)        PARAM_CF_DOMAIN="${2:-}";          shift 2 ;;
    --cf-origin-cert=*) PARAM_CF_ORIGIN_CERT="${1#*=}";    shift ;;
    --cf-origin-cert)   PARAM_CF_ORIGIN_CERT="${2:-}";     shift 2 ;;
    --cf-origin-key=*)  PARAM_CF_ORIGIN_KEY="${1#*=}";     shift ;;
    --cf-origin-key)    PARAM_CF_ORIGIN_KEY="${2:-}";      shift 2 ;;
    --cf-api-token=*)   PARAM_CF_API_TOKEN="${1#*=}";      shift ;;
    --cf-api-token)     PARAM_CF_API_TOKEN="${2:-}";       shift 2 ;;
    --branch=*)         PARAM_BRANCH="${1#*=}";            shift ;;
    --branch)           PARAM_BRANCH="${2:-}";             shift 2 ;;
    --image-tag=*)      PARAM_IMAGE_TAG="${1#*=}";         shift ;;
    --image-tag)        PARAM_IMAGE_TAG="${2:-}";          shift 2 ;;
    --deploy-dir=*)     PARAM_DEPLOY_DIR="${1#*=}";        shift ;;
    --deploy-dir)       PARAM_DEPLOY_DIR="${2:-}";         shift 2 ;;
    --skip-pull)        PARAM_SKIP_PULL=true;              shift ;;
    --upgrade)          PARAM_UPGRADE=true;                shift ;;
    --force=*)          PARAM_FORCE_STEP="${1#*=}";        shift ;;
    --force)            PARAM_FORCE_STEP="${2:-}";         shift 2 ;;
    --reset)            PARAM_RESET=true;                  shift ;;
    -h|--help)          usage; exit 0 ;;
    *)                  die "Unknown option: $1  (try --help)" ;;
  esac
done

# ------------------------------------------------------------
# Validate required args
# ------------------------------------------------------------
[[ -n "$PARAM_DOMAIN" ]] || die "--domain is required. Run with --help for usage."

# Validate key pair: if one is given, the other must be too
if [[ -n "$PARAM_XRAY_PRIVATE_KEY" && -z "$PARAM_XRAY_PUBLIC_KEY" ]]; then
  die "--xray-private-key requires --xray-public-key to be supplied as well."
fi
if [[ -n "$PARAM_XRAY_PUBLIC_KEY" && -z "$PARAM_XRAY_PRIVATE_KEY" ]]; then
  die "--xray-public-key requires --xray-private-key to be supplied as well."
fi
if [[ -n "$PARAM_CF_DOMAIN" && ! "$PARAM_CF_DOMAIN" =~ ^[A-Za-z0-9]([A-Za-z0-9.-]*[A-Za-z0-9])?$ ]]; then
  die "--cf-domain must be a hostname such as cdn.example.com."
fi
if [[ -n "$PARAM_VPN_DOMAIN" ]] && ! valid_hostname "$PARAM_VPN_DOMAIN"; then
  die "--vpn-domain must be a hostname such as vpn.example.com."
fi
if [[ -n "$PARAM_CF_API_TOKEN" && -n "$PARAM_CF_ORIGIN_CERT" ]]; then
  die "Use either --cf-api-token or --cf-origin-cert/--cf-origin-key, not both."
fi
if [[ -n "$PARAM_BRANCH" && ! "$PARAM_BRANCH" =~ ^[A-Za-z0-9._/-]+$ ]]; then
  die "--branch must be a branch name (letters, digits, . _ / -)."
fi
if [[ -n "$PARAM_IMAGE_TAG" && ! "$PARAM_IMAGE_TAG" =~ ^[A-Za-z0-9._-]+$ ]]; then
  die "--image-tag must be an image tag (letters, digits, . _ -)."
fi
if [[ -n "$PARAM_CF_ORIGIN_CERT" || -n "$PARAM_CF_ORIGIN_KEY" ]]; then
  [[ -n "$PARAM_CF_ORIGIN_CERT" && -n "$PARAM_CF_ORIGIN_KEY" ]] \
    || die "--cf-origin-cert and --cf-origin-key must be supplied together."
  # Resolve now: later steps run from other directories.
  [[ -f "$PARAM_CF_ORIGIN_CERT" ]] || die "Certificate file not found: $PARAM_CF_ORIGIN_CERT"
  [[ -f "$PARAM_CF_ORIGIN_KEY"  ]] || die "Key file not found: $PARAM_CF_ORIGIN_KEY"
  PARAM_CF_ORIGIN_CERT="$(cd "$(dirname "$PARAM_CF_ORIGIN_CERT")" && pwd)/$(basename "$PARAM_CF_ORIGIN_CERT")"
  PARAM_CF_ORIGIN_KEY="$(cd "$(dirname "$PARAM_CF_ORIGIN_KEY")" && pwd)/$(basename "$PARAM_CF_ORIGIN_KEY")"
fi

# ------------------------------------------------------------
# Resolve DEPLOY_DIR
# ------------------------------------------------------------
if [[ -n "$PARAM_DEPLOY_DIR" ]]; then
  DEPLOY_DIR="$PARAM_DEPLOY_DIR"
elif [[ -n "$SCRIPT_DIR" && -d "$SCRIPT_DIR/deployables" ]]; then
  DEPLOY_DIR="$SCRIPT_DIR/deployables"
elif [[ -n "$SCRIPT_DIR" ]]; then
  DEPLOY_DIR="$SCRIPT_DIR"
else
  # Running via pipe (e.g. curl | bash) — deploy into a new subdirectory
  DEPLOY_DIR="$PWD/easy-xray"
fi

mkdir -p "$DEPLOY_DIR"
DEPLOY_DIR="$(cd "$DEPLOY_DIR" && pwd)"
STATE_FILE="$DEPLOY_DIR/.install-state"

info "Script dir : $SCRIPT_DIR"
info "Deploy dir : $DEPLOY_DIR"
info "State file : $STATE_FILE"

# ------------------------------------------------------------
# --reset: wipe state file
# ------------------------------------------------------------
if [[ "$PARAM_RESET" == true ]]; then
  if [[ -f "$STATE_FILE" ]]; then
    rm -f "$STATE_FILE"
    info "State file cleared (--reset)."
  else
    info "--reset: no state file found, nothing to clear."
  fi
fi

# ------------------------------------------------------------
# Re-run secret preservation:
# If .env already exists, load any values not already set by CLI.
# CLI args always take precedence.
# ------------------------------------------------------------
load_env_if_present() {
  local env_file="$DEPLOY_DIR/.env"
  [[ -f "$env_file" ]] || return 0

  info "Found existing .env — loading persisted values (CLI args take precedence)."

  _env_val() {
    grep -E "^${1}=" "$env_file" 2>/dev/null | head -1 | cut -d'=' -f2- || true
  }

  [[ -z "$PARAM_JWT_SECRET"        ]] && PARAM_JWT_SECRET="$(_env_val JWT_SECRET)"
  [[ -z "$PARAM_XRAY_PRIVATE_KEY"  ]] && PARAM_XRAY_PRIVATE_KEY="$(_env_val XRAY_REALITY_PRIVATE_KEY)"
  [[ -z "$PARAM_XRAY_PUBLIC_KEY"   ]] && PARAM_XRAY_PUBLIC_KEY="$(_env_val XRAY_REALITY_PUBLIC_KEY)"
  [[ -z "$PARAM_XRAY_SHORT_ID"     ]] && PARAM_XRAY_SHORT_ID="$(_env_val XRAY_REALITY_SHORT_ID)"
  [[ -z "$PARAM_HOST"              ]] && PARAM_HOST="$(_env_val XRAY_HOST)"
  [[ -z "$PARAM_PROFILE"           ]] && PARAM_PROFILE="$(_env_val XRAY_DEFAULT_PROFILE)"
  [[ -z "$PARAM_VPN_DOMAIN"        ]] && PARAM_VPN_DOMAIN="$(_env_val XRAY_VPN_DOMAIN)"
  [[ -z "$PARAM_CF_DOMAIN"         ]] && PARAM_CF_DOMAIN="$(_env_val XRAY_CF_DOMAIN)"
  [[ -z "$PARAM_WS_ID"             ]] && PARAM_WS_ID="$(_env_val XRAY_WS_ID)"
  [[ -z "$PARAM_XHTTP_ID"          ]] && PARAM_XHTTP_ID="$(_env_val XRAY_XHTTP_ID)"
  [[ -z "$PARAM_VLESS_ENC_PRIVATE_KEY" ]] && PARAM_VLESS_ENC_PRIVATE_KEY="$(_env_val XRAY_VLESS_ENC_PRIVATE_KEY)"
  [[ -z "$PARAM_VLESS_ENC_PUBLIC_KEY"  ]] && PARAM_VLESS_ENC_PUBLIC_KEY="$(_env_val XRAY_VLESS_ENC_PUBLIC_KEY)"
  [[ -z "$PARAM_BRANCH"            ]] && PARAM_BRANCH="$(_env_val INSTALL_BRANCH)"
  [[ -z "$PARAM_IMAGE_TAG"         ]] && PARAM_IMAGE_TAG="$(_env_val APP_IMAGE_TAG)"
  # SNI and fingerprint: only load if they haven't been overridden and the file has them
  local _sni; _sni="$(_env_val XRAY_SNI)"
  local _fp;  _fp="$(_env_val XRAY_FINGERPRINT)"
  [[ -z "$_sni" ]] || PARAM_XRAY_SNI="$_sni"
  [[ -z "$_fp"  ]] || PARAM_XRAY_FINGERPRINT="$_fp"
}

# In a clone, the checked-out branch is where the local files came from, so it wins over .env.
if [[ -z "$PARAM_BRANCH" && -n "$SCRIPT_DIR" ]] && command -v git &>/dev/null; then
  PARAM_BRANCH="$(git -c safe.directory="$SCRIPT_DIR" -C "$SCRIPT_DIR" symbolic-ref --quiet --short HEAD 2>/dev/null || true)"
fi

load_env_if_present

# The direct TLS profiles use a separate DNS-only SNI so Caddy can continue
# routing --domain to the management UI.
if [[ -n "$PARAM_VPN_DOMAIN" ]]; then
  valid_hostname "$PARAM_VPN_DOMAIN" \
    || die "XRAY_VPN_DOMAIN must be a hostname such as vpn.example.com."
  for other in "$PARAM_DOMAIN" "$PARAM_HOST" "$PARAM_CF_DOMAIN"; do
    [[ -z "$other" || "${PARAM_VPN_DOMAIN,,}" != "${other,,}" ]] \
      || die "--vpn-domain ($PARAM_VPN_DOMAIN) must differ from --domain, --host, and --cf-domain."
  done
fi
if [[ "$PARAM_PROFILE" == "vless-ws-tls" || "$PARAM_PROFILE" == "vless-xhttp-tls" ]]; then
  [[ -n "$PARAM_VPN_DOMAIN" ]] || die "--profile $PARAM_PROFILE needs --vpn-domain."
fi

# A re-run may take the Cloudflare hostname from .env, so check this only now.
if [[ -z "$PARAM_CF_DOMAIN" ]]; then
  [[ -z "$PARAM_CF_API_TOKEN"   ]] || die "--cf-api-token (or CF_API_TOKEN) requires --cf-domain."
  [[ -z "$PARAM_CF_ORIGIN_CERT" ]] || die "--cf-origin-cert/--cf-origin-key require --cf-domain."
fi

# ------------------------------------------------------------
# Branch and image every file and container come from
# ------------------------------------------------------------
[[ -n "$PARAM_BRANCH"    ]] || PARAM_BRANCH="main"
[[ -n "$PARAM_IMAGE_TAG" ]] || PARAM_IMAGE_TAG="latest"

# EASY_XRAY_RAW_ROOT overrides where branches are read from, e.g. file:///path/to/branches when testing.
RAW_ROOT="${EASY_XRAY_RAW_ROOT:-https://raw.githubusercontent.com/hamidmayeli/quick-vless/refs/heads}"
GITHUB_RAW="$RAW_ROOT/$PARAM_BRANCH/deployables"

info "Branch     : $PARAM_BRANCH"
info "App image  : hamidmayeli/quick-vless:$PARAM_IMAGE_TAG"
if [[ "$PARAM_BRANCH" != "main" && "$PARAM_IMAGE_TAG" == "latest" ]]; then
  warn "Deployment files come from branch '$PARAM_BRANCH', but the app image is 'latest' (built from main)."
  warn "  If the branch changes the app, pass --image-tag with the image built from it (e.g. pr-17)."
fi

# ------------------------------------------------------------
# State helpers
# ------------------------------------------------------------
step_is_done() {
  grep -qxF "$1" "$STATE_FILE" 2>/dev/null
}

mark_step_done() {
  grep -qxF "$1" "$STATE_FILE" 2>/dev/null || echo "$1" >> "$STATE_FILE"
}

# run_step NAME FUNC [always]
#   "always" runs the step even if it is recorded as done (for idempotent steps).
run_step() {
  local name="$1"
  local func="$2"
  local mode="${3:-}"

  if [[ "$mode" != "always" ]] && step_is_done "$name" && [[ "$PARAM_FORCE_STEP" != "$name" ]]; then
    info "Step '$name' already done — skipping."
    return 0
  fi

  if [[ "$PARAM_FORCE_STEP" == "$name" ]] && step_is_done "$name"; then
    info "Step '$name' forced — re-running."
  else
    info "Running step: $name"
  fi

  if ! "$func"; then
    error "Step '$name' FAILED."
    exit 1
  fi

  mark_step_done "$name"
  info "Step '$name' completed."
}

# ============================================================
# Step implementations
# ============================================================

# ------------------------------------------------------------
# 1. download_files
# ------------------------------------------------------------
download() {
  curl -fsSL --max-time 30 "$GITHUB_RAW/$1" -o "$2" \
    || die "Failed to download $1 from $GITHUB_RAW/$1 — does branch '$PARAM_BRANCH' exist?"
}

step_download_files() {
  if [[ -f "$DEPLOY_DIR/docker-compose.yaml" && "$PARAM_UPGRADE" != true ]]; then
    info "docker-compose.yaml already present in $DEPLOY_DIR — skipping download (use --upgrade to refresh)."
    return 0
  fi

  info "Downloading deployment files from $GITHUB_RAW ..."
  local manifest
  manifest="$(mktemp)"
  download "manifest.txt" "$manifest"

  local f dest
  while IFS= read -r f || [[ -n "$f" ]]; do
    f="${f%$'\r'}"
    [[ -z "$f" || "$f" == \#* ]] && continue
    dest="$DEPLOY_DIR/$f"
    mkdir -p "$(dirname "$dest")"
    if [[ -f "$dest" ]]; then
      # Profile files hold values filled in by this script (or edited by hand): never replace them.
      if [[ "$f" == xray/* ]]; then
        info "  $f exists — kept."
        continue
      fi
      cp -p "$dest" "$dest.bak"
    fi
    info "  Downloading $f ..."
    download "$f" "$dest"
  done < "$manifest"
  rm -f "$manifest"

  chmod +x "$DEPLOY_DIR/update.sh" "$DEPLOY_DIR/xray-run.sh" "$DEPLOY_DIR/renew-vpn-certificate.sh"
  info "All deployment files downloaded."
}

# ------------------------------------------------------------
# migrate_legacy_layout
# Installs from before profiles kept a single xray/config.json.
# ------------------------------------------------------------
step_migrate_legacy_layout() {
  local legacy="$DEPLOY_DIR/xray/config.json"
  local profile="$DEPLOY_DIR/xray/vless-reality/config.json"

  if [[ ! -f "$legacy" ]]; then
    info "No legacy xray/config.json — nothing to migrate."
    return 0
  fi

  if [[ -f "$profile" ]] && ! grep -qF 'YOUR_PRIVATE_KEY' "$profile"; then
    mv "$legacy" "$legacy.bak"
    info "xray/vless-reality/ is already configured — kept the old xray/config.json as xray/config.json.bak."
    return 0
  fi

  # The old file is already patched with this server's REALITY keys, so it becomes the profile.
  mkdir -p "$(dirname "$profile")"
  mv "$legacy" "$profile"
  info "Moved xray/config.json to xray/vless-reality/config.json."
}

# ------------------------------------------------------------
# 2. install_prerequisites
# ------------------------------------------------------------
step_install_prerequisites() {
  if ! command -v apt-get &>/dev/null; then
    info "Not an apt-based system — skipping automatic dependency installation."
    info "Please ensure docker, docker compose (V2), openssl, and curl are installed."
    return 0
  fi

  info "apt-based system detected — installing dependencies..."
  export DEBIAN_FRONTEND=noninteractive
  apt-get update -qq

  # curl and openssl
  local pkgs=()
  command -v curl    &>/dev/null || pkgs+=("curl")
  command -v openssl &>/dev/null || pkgs+=("openssl")
  pkgs+=("ca-certificates" "gnupg" "lsb-release")

  if [[ ${#pkgs[@]} -gt 0 ]]; then
    apt-get install -y -qq "${pkgs[@]}"
  fi

  # Docker Engine (official repository)
  if ! command -v docker &>/dev/null; then
    info "  Installing Docker Engine..."

    # Remove legacy packages if present
    for pkg in docker.io docker-doc docker-compose docker-compose-v2 podman-docker containerd runc; do
      apt-get remove -y -qq "$pkg" 2>/dev/null || true
    done

    install -m 0755 -d /etc/apt/keyrings
    local distro
    distro="$(. /etc/os-release && echo "$ID")"
    curl -fsSL "https://download.docker.com/linux/${distro}/gpg" \
      | gpg --dearmor --yes -o /etc/apt/keyrings/docker.gpg
    chmod a+r /etc/apt/keyrings/docker.gpg

    local arch
    arch="$(dpkg --print-architecture)"
    local codename
    codename="$(. /etc/os-release && echo "$VERSION_CODENAME")"
    echo "deb [arch=${arch} signed-by=/etc/apt/keyrings/docker.gpg] \
https://download.docker.com/linux/${distro} ${codename} stable" \
      > /etc/apt/sources.list.d/docker.list

    apt-get update -qq
    apt-get install -y -qq \
      docker-ce docker-ce-cli containerd.io \
      docker-buildx-plugin docker-compose-plugin

    systemctl enable --now docker 2>/dev/null || true
    info "  Docker installed."
  else
    info "  docker: already installed."
  fi

  # Add current user to docker group so non-root invocations work
  if [[ -n "${SUDO_USER:-}" ]] && ! groups "$SUDO_USER" | grep -q docker; then
    usermod -aG docker "$SUDO_USER"
    info "  Added $SUDO_USER to the docker group (re-login to apply)."
  fi

  info "Prerequisites installed."
}

# ------------------------------------------------------------
# 3. install_vpn_prerequisites
# ------------------------------------------------------------
step_install_vpn_prerequisites() {
  [[ -n "$PARAM_VPN_DOMAIN" ]] || return 0

  local pkgs=()
  command -v certbot &>/dev/null || pkgs+=("certbot")
  command -v crontab &>/dev/null || pkgs+=("cron")
  [[ ${#pkgs[@]} -gt 0 ]] || return 0

  if ! command -v apt-get &>/dev/null; then
    info "Not an apt-based system — Certbot and cron must be installed manually."
    return 0
  fi

  info "Installing VPN certificate dependencies: ${pkgs[*]}..."
  export DEBIAN_FRONTEND=noninteractive
  apt-get update -qq
  apt-get install -y -qq "${pkgs[@]}"
}

# ------------------------------------------------------------
# 4. check_prerequisites
# ------------------------------------------------------------
step_check_prerequisites() {
  local missing=()

  info "Checking prerequisites..."

  # docker
  if command -v docker &>/dev/null; then
    info "  docker: $(docker --version)"
  else
    missing+=("docker")
  fi

  # docker compose (V2 plugin)
  if docker compose version &>/dev/null 2>&1; then
    info "  docker compose: $(docker compose version)"
  else
    missing+=("docker-compose-v2 (docker compose plugin)")
  fi

  # openssl
  if command -v openssl &>/dev/null; then
    info "  openssl: $(openssl version)"
  else
    missing+=("openssl")
  fi

  # curl
  if command -v curl &>/dev/null; then
    info "  curl: $(curl --version | head -1)"
  else
    missing+=("curl")
  fi

  if [[ -n "$PARAM_VPN_DOMAIN" ]]; then
    command -v certbot &>/dev/null || missing+=("certbot")
    command -v crontab &>/dev/null || missing+=("crontab")
  fi

  if [[ ${#missing[@]} -gt 0 ]]; then
    error "Missing prerequisites:"
    for m in "${missing[@]}"; do
      error "  - $m"
    done
    return 1
  fi

  info "All prerequisites satisfied."
}

# ------------------------------------------------------------
# 5. generate_secrets
# ------------------------------------------------------------

# Prints "PRIVATE PUBLIC": a raw X25519 key pair, base64url without padding, as Xray uses for
# REALITY and VLESS Encryption.
#   PKCS8 DER for x25519 is always 48 bytes; the last 32 are the raw private key.
#   SubjectPublicKeyInfo DER for x25519 is always 44 bytes; the last 32 are the raw public key.
x25519_pair() {
  local pem private public
  pem="$(openssl genpkey -algorithm x25519 2>/dev/null)" \
    || die "openssl failed to generate an x25519 key pair."
  private="$(printf '%s' "$pem" | openssl pkey -outform DER 2>/dev/null | tail -c 32 \
    | base64 | tr '+/' '-_' | tr -d '=')"
  public="$(printf '%s' "$pem" | openssl pkey -pubout -outform DER 2>/dev/null | tail -c 32 \
    | base64 | tr '+/' '-_' | tr -d '=')"
  [[ -n "$private" && -n "$public" ]] || die "Failed to derive an x25519 key pair from openssl output."
  echo "$private $public"
}

step_generate_secrets() {
  # JWT secret
  if [[ -z "$PARAM_JWT_SECRET" ]]; then
    info "  Generating JWT secret..."
    PARAM_JWT_SECRET="$(openssl rand -hex 32)"
  else
    info "  JWT secret: (provided)"
  fi

  # Xray Reality key pair
  if [[ -z "$PARAM_XRAY_PRIVATE_KEY" || -z "$PARAM_XRAY_PUBLIC_KEY" ]]; then
    info "  Generating Xray x25519 key pair via openssl..."
    read -r PARAM_XRAY_PRIVATE_KEY PARAM_XRAY_PUBLIC_KEY <<<"$(x25519_pair)"
    [[ -n "$PARAM_XRAY_PUBLIC_KEY" ]] || die "Could not generate the REALITY key pair."
    info "  Xray key pair generated."
  else
    info "  Xray key pair: (provided)"
  fi

  # VLESS Encryption key pair of the Cloudflare XHTTP profile: the X25519 authentication of
  # mlkem768x25519plus. Xray calls the public key the "password".
  if [[ -z "$PARAM_VLESS_ENC_PRIVATE_KEY" || -z "$PARAM_VLESS_ENC_PUBLIC_KEY" ]]; then
    read -r PARAM_VLESS_ENC_PRIVATE_KEY PARAM_VLESS_ENC_PUBLIC_KEY <<<"$(x25519_pair)"
    [[ -n "$PARAM_VLESS_ENC_PUBLIC_KEY" ]] || die "Could not generate the VLESS Encryption key pair."
    info "  VLESS Encryption key pair generated."
  else
    info "  VLESS Encryption key pair: (provided)"
  fi

  # Short ID
  if [[ -z "$PARAM_XRAY_SHORT_ID" ]]; then
    info "  Generating Xray short ID..."
    PARAM_XRAY_SHORT_ID="$(openssl rand -hex 4)"
  else
    info "  Xray short ID: (provided)"
  fi

  # Host (server IP/domain in vless:// URLs)
  if [[ -z "$PARAM_HOST" ]]; then
    PARAM_HOST="$PARAM_DOMAIN"
    info "  Host: (defaulting to domain) $PARAM_HOST"
  else
    info "  Host: (provided) $PARAM_HOST"
  fi

  # Paths of the Cloudflare profiles: random per server, so installs don't share one.
  if [[ -z "$PARAM_WS_ID" ]]; then
    PARAM_WS_ID="ws-$(openssl rand -hex 12)"
    info "  WebSocket path: (generated) /$PARAM_WS_ID"
  else
    info "  WebSocket path: (provided) /$PARAM_WS_ID"
  fi
  if [[ -z "$PARAM_XHTTP_ID" ]]; then
    PARAM_XHTTP_ID="xh-$(openssl rand -hex 12)"
    info "  XHTTP path    : (generated) /$PARAM_XHTTP_ID"
  else
    info "  XHTTP path    : (provided) /$PARAM_XHTTP_ID"
  fi

  [[ -n "$PARAM_PROFILE" ]] || PARAM_PROFILE="vless-reality"

  info "  SNI         : $PARAM_XRAY_SNI"
  info "  Fingerprint : $PARAM_XRAY_FINGERPRINT"
  info "  Profile     : $PARAM_PROFILE"
  [[ -z "$PARAM_CF_DOMAIN" ]] || info "  CF domain   : $PARAM_CF_DOMAIN"
  info "Secrets ready."
}

# ------------------------------------------------------------
# 5. write_env_file
# ------------------------------------------------------------
step_write_env_file() {
  local env_file="$DEPLOY_DIR/.env"

  # Verify all required values are populated
  [[ -n "$PARAM_JWT_SECRET"       ]] || die "JWT_SECRET is empty — run generate_secrets first."
  [[ -n "$PARAM_DOMAIN"           ]] || die "MANAGEMENT_DOMAIN is empty."
  [[ -n "$PARAM_HOST"             ]] || die "XRAY_HOST is empty."
  [[ -n "$PARAM_XRAY_PRIVATE_KEY" ]] || die "XRAY_REALITY_PRIVATE_KEY is empty."
  [[ -n "$PARAM_XRAY_PUBLIC_KEY"  ]] || die "XRAY_REALITY_PUBLIC_KEY is empty."
  [[ -n "$PARAM_XRAY_SHORT_ID"    ]] || die "XRAY_REALITY_SHORT_ID is empty."

  info "Writing .env to $env_file ..."
  cat > "$env_file" <<EOF
JWT_SECRET=${PARAM_JWT_SECRET}
MANAGEMENT_DOMAIN=${PARAM_DOMAIN}
XRAY_HOST=${PARAM_HOST}
XRAY_REALITY_PRIVATE_KEY=${PARAM_XRAY_PRIVATE_KEY}
XRAY_REALITY_PUBLIC_KEY=${PARAM_XRAY_PUBLIC_KEY}
XRAY_REALITY_SHORT_ID=${PARAM_XRAY_SHORT_ID}
XRAY_SNI=${PARAM_XRAY_SNI}
XRAY_FINGERPRINT=${PARAM_XRAY_FINGERPRINT}
XRAY_DEFAULT_PROFILE=${PARAM_PROFILE}
XRAY_VPN_DOMAIN=${PARAM_VPN_DOMAIN}
XRAY_CF_DOMAIN=${PARAM_CF_DOMAIN}
XRAY_WS_ID=${PARAM_WS_ID}
XRAY_XHTTP_ID=${PARAM_XHTTP_ID}
XRAY_VLESS_ENC_PRIVATE_KEY=${PARAM_VLESS_ENC_PRIVATE_KEY}
XRAY_VLESS_ENC_PUBLIC_KEY=${PARAM_VLESS_ENC_PUBLIC_KEY}
INSTALL_BRANCH=${PARAM_BRANCH}
APP_IMAGE_TAG=${PARAM_IMAGE_TAG}
EOF

  chmod 600 "$env_file"
  info ".env written and permissions set to 600."
}

# ------------------------------------------------------------
# 6. install_cf_certificate
# ------------------------------------------------------------

# san_covers HOST "SAN SAN ..." — a wildcard covers exactly one level:
# *.example.com covers cdn.example.com, but not a.b.example.com or example.com.
san_covers() {
  local host="${1,,}" san
  for san in $2; do
    san="${san,,}"
    [[ "$san" == "$host" ]] && return 0
    if [[ "$san" == \*.* && "$host" == *.* && "${host#*.}" == "${san#\*.}" ]]; then
      return 0
    fi
  done
  return 1
}

# certificate_problem CERT KEY HOST — prints why CERT/KEY can't serve HOST, and fails; silent if they can.
certificate_problem() {
  local cert="$1" key="$2" host="$3"

  if ! openssl x509 -in "$cert" -noout 2>/dev/null; then echo "$cert is not a PEM certificate."; return 1; fi
  if ! openssl pkey -in "$key" -noout 2>/dev/null;  then echo "$key is not a PEM private key."; return 1; fi

  local cert_pub key_pub
  cert_pub="$(openssl x509 -in "$cert" -noout -pubkey 2>/dev/null)"
  key_pub="$(openssl pkey -in "$key" -pubout 2>/dev/null)"
  if [[ -z "$cert_pub" || "$cert_pub" != "$key_pub" ]]; then
    echo "The private key $key does not belong to the certificate $cert."
    return 1
  fi

  local sans
  sans="$(openssl x509 -in "$cert" -noout -ext subjectAltName 2>/dev/null \
    | grep -o 'DNS:[^,]*' | cut -d: -f2 | tr -d ' ' | tr '\n' ' ' || true)"
  if ! san_covers "$host" "$sans"; then
    echo "The certificate does not cover $host (it covers: ${sans:-nothing}). Note that *.example.com covers cdn.example.com but not a.b.example.com."
    return 1
  fi
}

# check_certificate CERT KEY HOST — dies unless CERT/KEY are a matching PEM pair covering HOST.
check_certificate() {
  local problem
  problem="$(certificate_problem "$1" "$2" "$3")" || die "$problem"
  info "  Certificate covers $3 and expires $(cert_end_date "$1")."
}

cert_end_date() { openssl x509 -in "$1" -noout -enddate | cut -d= -f2; }

# True when CERT expires within 30 days.
cert_expires_soon() { ! openssl x509 -in "$1" -noout -checkend 2592000 >/dev/null 2>&1; }

# install_pem_pair CERT KEY DEST_CERT DEST_KEY — copies the pair into place; notes whether it changed.
install_pem_pair() {
  local src_cert="$1" src_key="$2" cert="$3" key="$4"
  mkdir -p "$(dirname "$cert")"
  if ! cmp -s "$src_cert" "$cert" 2>/dev/null || ! cmp -s "$src_key" "$key" 2>/dev/null; then
    XRAY_CERT_CHANGED=true
  fi
  if [[ "$src_cert" != "$cert" ]]; then cp "$src_cert" "$cert"; fi
  if [[ "$src_key"  != "$key"  ]]; then cp "$src_key"  "$key";  fi
  chmod 644 "$cert"
  chmod 600 "$key"
}

# EASY_XRAY_CF_API overrides the Cloudflare API, for testing.
CF_API="${EASY_XRAY_CF_API:-https://api.cloudflare.com/client/v4}"

# create_origin_certificate CERT KEY — asks Cloudflare's Origin CA for a 15-year certificate
# covering --cf-domain and writes the pair to CERT and KEY. The private key is generated here
# and never leaves the server: Cloudflare only receives a certificate signing request.
create_origin_certificate() {
  local cert="$1" key="$2" work response pem reasons
  work="$(mktemp -d)"
  chmod 700 "$work"

  if ! openssl req -new -newkey rsa:2048 -nodes -keyout "$work/privkey.pem" \
      -out "$work/request.csr" -subj "/CN=$PARAM_CF_DOMAIN" 2>/dev/null; then
    rm -rf "$work"
    die "openssl could not create a certificate signing request."
  fi

  printf '{"hostnames":["%s"],"requested_validity":5475,"request_type":"origin-rsa","csr":"%s"}' \
    "$PARAM_CF_DOMAIN" "$(awk '{ printf "%s\\n", $0 }' "$work/request.csr")" > "$work/request.json"
  # The header is read from a file so the token never shows up in the process list.
  printf 'Authorization: Bearer %s\n' "$PARAM_CF_API_TOKEN" > "$work/auth.header"

  info "  Asking Cloudflare to create an Origin certificate for $PARAM_CF_DOMAIN ..."
  if ! response="$(curl -sS --max-time 30 -X POST "$CF_API/certificates" \
      -H @"$work/auth.header" -H "Content-Type: application/json" \
      --data-binary @"$work/request.json" 2>&1)"; then
    rm -rf "$work"
    die "Could not reach the Cloudflare API: $response"
  fi
  rm -f "$work/auth.header"

  # The PEM comes back as one JSON string with \n escapes (and possibly \/).
  response="$(tr -d '\r\n' <<<"$response")"
  pem="$(sed -n 's/.*"certificate" *: *"\([^"]*\)".*/\1/p' <<<"$response" | sed 's#\\/#/#g')"
  if ! grep -q '"success" *: *true' <<<"$response" || [[ -z "$pem" ]]; then
    reasons="$(grep -o '"message" *: *"[^"]*"' <<<"$response" | cut -d'"' -f4 | tr '\n' ';' | sed 's/;$//; s/;/; /g' || true)"
    rm -rf "$work"
    die "Cloudflare did not create the certificate: ${reasons:-unexpected response}. The token needs the permission Zone → SSL and Certificates → Edit on the zone of $PARAM_CF_DOMAIN."
  fi
  printf '%b' "$pem" > "$cert"
  mv "$work/privkey.pem" "$key"
  rm -rf "$work"

  check_certificate "$cert" "$key" "$PARAM_CF_DOMAIN"
  info "  Cloudflare created the Origin certificate."
}

# Prints the profile folders that terminate TLS with the Origin certificate: their config.json
# reads it from their own tls/ folder (e.g. vless-cf-ws, vless-cf-xhttp).
cf_profile_dirs() {
  local dir
  for dir in "$DEPLOY_DIR"/xray/*/; do
    dir="${dir%/}"
    if grep -qF "/etc/xray/profiles/$(basename "$dir")/tls/fullchain.pem" "$dir/config.json" 2>/dev/null; then
      echo "$dir"
    fi
  done
}

step_install_cf_certificate() {
  if [[ -z "$PARAM_CF_DOMAIN" ]]; then
    info "No --cf-domain — the Cloudflare profiles stay disabled."
    return 0
  fi

  # Caddy routes the management SNI to itself, and REALITY clients must reach the server directly.
  local other
  for other in "$PARAM_DOMAIN" "$PARAM_HOST"; do
    [[ "${PARAM_CF_DOMAIN,,}" != "${other,,}" ]] \
      || die "--cf-domain ($PARAM_CF_DOMAIN) must differ from --domain and --host."
  done

  local -a dirs
  mapfile -t dirs < <(cf_profile_dirs)
  if [[ ${#dirs[@]} -eq 0 ]]; then
    warn "No profile in xray/ reads an Origin certificate from its tls/ folder — nothing to install."
    return 0
  fi

  # The certificate already installed in one of those profiles, if it still covers --cf-domain.
  local d cert="" key="" problem=""
  for d in "${dirs[@]}"; do
    [[ -f "$d/tls/fullchain.pem" && -f "$d/tls/privkey.pem" ]] || continue
    if problem="$(certificate_problem "$d/tls/fullchain.pem" "$d/tls/privkey.pem" "$PARAM_CF_DOMAIN")"; then
      cert="$d/tls/fullchain.pem"
      key="$d/tls/privkey.pem"
      break
    fi
  done

  local staging=""
  if [[ -n "$PARAM_CF_ORIGIN_CERT" ]]; then
    check_certificate "$PARAM_CF_ORIGIN_CERT" "$PARAM_CF_ORIGIN_KEY" "$PARAM_CF_DOMAIN"
    cert="$PARAM_CF_ORIGIN_CERT"
    key="$PARAM_CF_ORIGIN_KEY"
  elif [[ -n "$cert" ]] && ! { cert_expires_soon "$cert" && [[ -n "$PARAM_CF_API_TOKEN" ]]; }; then
    # A usable certificate is already installed: don't ask Cloudflare for another one on every run.
    if cert_expires_soon "$cert"; then
      warn "The Origin certificate expires on $(cert_end_date "$cert")."
      warn "  Re-run with --cf-api-token (or CF_API_TOKEN) to replace it."
    fi
    info "Using the installed Origin certificate (expires $(cert_end_date "$cert"))."
  elif [[ -n "$PARAM_CF_API_TOKEN" ]]; then
    if [[ -n "$cert" ]]; then
      info "The Origin certificate expires on $(cert_end_date "$cert") — replacing it."
    elif [[ -n "$problem" ]]; then
      info "The installed certificate can't be used: $problem"
    fi
    staging="$(mktemp -d)"
    chmod 700 "$staging"
    create_origin_certificate "$staging/fullchain.pem" "$staging/privkey.pem"
    cert="$staging/fullchain.pem"
    key="$staging/privkey.pem"
  elif [[ -n "$problem" ]]; then
    die "$problem Pass --cf-api-token to create a new certificate, or --cf-origin-cert and --cf-origin-key."
  else
    die "--cf-domain needs a certificate the first time: pass --cf-api-token (or set CF_API_TOKEN) to have Cloudflare create one, or --cf-origin-cert and --cf-origin-key with one you created in Cloudflare (SSL/TLS → Origin Server)."
  fi

  # Every Cloudflare profile gets its own copy, so each profile folder stays self-contained.
  for d in "${dirs[@]}"; do
    install_pem_pair "$cert" "$key" "$d/tls/fullchain.pem" "$d/tls/privkey.pem"
  done
  [[ -z "$staging" ]] || rm -rf "$staging"
  info "Origin certificate in place for: $(for d in "${dirs[@]}"; do basename "$d"; done | tr '\n' ' ')"
}

# Installs a Certbot deploy hook that copies only this install's VPN certificate.
install_vpn_certificate_hook() {
  local hook_dir="${EASY_XRAY_CERTBOT_HOOK_DIR:-/etc/letsencrypt/renewal-hooks/deploy}"
  local hook_id hook_file
  hook_id="$(printf '%s' "$DEPLOY_DIR" | sha256sum | cut -c1-16)"
  hook_file="$hook_dir/quick-vless-$hook_id.sh"
  mkdir -p "$hook_dir"
  printf '#!/usr/bin/env bash\nset -euo pipefail\nexec /usr/bin/env bash %q\n' \
    "$DEPLOY_DIR/renew-vpn-certificate.sh" > "$hook_file"
  chmod 700 "$hook_file"
}

# ------------------------------------------------------------
# 7. install_vpn_certificate
# ------------------------------------------------------------
step_install_vpn_certificate() {
  if [[ -z "$PARAM_VPN_DOMAIN" ]]; then
    info "No --vpn-domain — the direct TLS profiles stay disabled."
    return 0
  fi

  local profile cert key webroot
  for profile in vless-ws-tls vless-xhttp-tls; do
    [[ -f "$DEPLOY_DIR/xray/$profile/config.json" && -f "$DEPLOY_DIR/xray/$profile/client-config.template" ]] \
      || die "Direct TLS profiles are missing. Re-run with --upgrade to download them."
  done
  [[ -f "$DEPLOY_DIR/renew-vpn-certificate.sh" ]] \
    || die "Certificate renewal helper is missing. Re-run with --upgrade to download it."

  webroot="$DEPLOY_DIR/data/acme"
  mkdir -p "$webroot"

  info "Starting Caddy for the HTTP-01 challenge on $PARAM_VPN_DOMAIN ..."
  (cd "$DEPLOY_DIR" && docker compose up -d --no-deps caddy) \
    || die "Could not start Caddy for the certificate challenge."
  (cd "$DEPLOY_DIR" && docker compose exec -T caddy caddy reload --config /etc/caddy/Caddyfile) \
    || die "Could not reload Caddy with the ACME webroot route. Run install.sh --upgrade and try again."

  certbot certonly --non-interactive --agree-tos --register-unsafely-without-email \
    --webroot --webroot-path "$webroot" --cert-name "$PARAM_VPN_DOMAIN" \
    --domains "$PARAM_VPN_DOMAIN" --keep-until-expiring \
    || die "Certbot could not issue or renew a certificate for $PARAM_VPN_DOMAIN. Verify DNS points directly to this server and port 80 is reachable."

  cert="/etc/letsencrypt/live/$PARAM_VPN_DOMAIN/fullchain.pem"
  key="/etc/letsencrypt/live/$PARAM_VPN_DOMAIN/privkey.pem"
  check_certificate "$cert" "$key" "$PARAM_VPN_DOMAIN"

  for profile in vless-ws-tls vless-xhttp-tls; do
    install_pem_pair "$cert" "$key" \
      "$DEPLOY_DIR/xray/$profile/tls/fullchain.pem" \
      "$DEPLOY_DIR/xray/$profile/tls/privkey.pem"
  done
  install_vpn_certificate_hook
  info "Let's Encrypt certificate installed for $PARAM_VPN_DOMAIN."
}

# ------------------------------------------------------------
# 8. write_xray_profiles
# ------------------------------------------------------------

sed_escape() { printf '%s' "$1" | sed -e 's/[\\|&]/\\&/g'; }

# Fills in the install-time placeholders of one profile file. A placeholder whose value
# is empty (e.g. YOUR_CF_DOMAIN without --cf-domain) is left for the app to report.
patch_profile_file() {
  local file="$1" i placeholder value
  local -a pairs=(
    YOUR_PRIVATE_KEY "$PARAM_XRAY_PRIVATE_KEY"
    YOUR_PUBLIC_KEY  "$PARAM_XRAY_PUBLIC_KEY"
    YOUR_SHORT_ID    "$PARAM_XRAY_SHORT_ID"
    YOUR_SNI         "$PARAM_XRAY_SNI"
    YOUR_HOST        "$PARAM_HOST"
    YOUR_FINGERPRINT "$PARAM_XRAY_FINGERPRINT"
    YOUR_VPN_DOMAIN "$PARAM_VPN_DOMAIN"
    YOUR_CF_DOMAIN   "$PARAM_CF_DOMAIN"
    YOUR_WS_ID       "$PARAM_WS_ID"
    YOUR_XHTTP_ID    "$PARAM_XHTTP_ID"
    YOUR_VLESS_ENC_PRIVATE_KEY "$PARAM_VLESS_ENC_PRIVATE_KEY"
    YOUR_VLESS_ENC_PUBLIC_KEY  "$PARAM_VLESS_ENC_PUBLIC_KEY"
  )
  for (( i = 0; i < ${#pairs[@]}; i += 2 )); do
    placeholder="${pairs[i]}"
    value="${pairs[i + 1]}"
    grep -qF "$placeholder" "$file" || continue
    [[ -n "$value" ]] || continue
    sed -i "s|$placeholder|$(sed_escape "$value")|g" "$file"
    info "  ${file#"$DEPLOY_DIR/"}: filled in $placeholder"
  done
}

step_write_xray_profiles() {
  local dir file count=0
  shopt -s nullglob
  for dir in "$DEPLOY_DIR"/xray/*/; do
    for file in "${dir}config.json" "${dir}client-config.template"; do
      [[ -f "$file" ]] || continue
      patch_profile_file "$file"
    done
    count=$((count + 1))
  done
  shopt -u nullglob

  [[ $count -gt 0 ]] || die "No profiles found in $DEPLOY_DIR/xray/."
  [[ -d "$DEPLOY_DIR/xray/$PARAM_PROFILE" ]] \
    || die "--profile $PARAM_PROFILE: there is no xray/$PARAM_PROFILE folder."
  if [[ "$PARAM_PROFILE" == "vless-ws-tls" || "$PARAM_PROFILE" == "vless-xhttp-tls" ]]; then
    [[ -n "$PARAM_VPN_DOMAIN" ]] || die "--profile $PARAM_PROFILE needs --vpn-domain."
  fi
  if grep -qF 'YOUR_CF_DOMAIN' "$DEPLOY_DIR/xray/$PARAM_PROFILE/client-config.template" 2>/dev/null; then
    die "--profile $PARAM_PROFILE needs --cf-domain."
  fi
  if [[ "$PARAM_PROFILE" == "vless-ws-tls" || "$PARAM_PROFILE" == "vless-xhttp-tls" ]] \
      && grep -qF 'YOUR_VPN_DOMAIN' "$DEPLOY_DIR/xray/$PARAM_PROFILE/client-config.template"; then
    die "--profile $PARAM_PROFILE needs --vpn-domain."
  fi
  info "Profiles ready ($count found; the app reports any that still need values)."
}

# ------------------------------------------------------------
# 8. pull_images
# ------------------------------------------------------------
step_pull_images() {
  if [[ "$PARAM_SKIP_PULL" == true ]]; then
    info "--skip-pull specified — skipping 'docker compose pull'."
    return 0
  fi

  info "Pulling Docker images..."
  (cd "$DEPLOY_DIR" && docker compose pull) \
    || die "docker compose pull failed."
  info "Images pulled."
}

# ------------------------------------------------------------
# 9. start_services
# ------------------------------------------------------------
step_start_services() {
  info "Starting services with 'docker compose up -d' ..."
  (cd "$DEPLOY_DIR" && docker compose up -d) \
    || die "docker compose up failed."
  info "Services started."
}

# Xray reads certificates only when it starts, and replacing one doesn't change the config's
# structure, so the app won't restart it. Do it here when an already running install got a new one.
step_reload_xray() {
  # A container started by this run already loaded the new certificate.
  if [[ "$XRAY_CERT_CHANGED" != true || "$XRAY_WAS_RUNNING" != true ]]; then
    return 0
  fi
  info "A TLS certificate changed — restarting Xray to load it ..."
  (cd "$DEPLOY_DIR" && docker compose restart xray) \
    || warn "Could not restart Xray. Run 'docker compose restart xray' in $DEPLOY_DIR."
}

# ------------------------------------------------------------
# 10. setup_cron_jobs
# ------------------------------------------------------------
step_setup_cron_jobs() {
  if ! command -v crontab &>/dev/null; then
    warn "crontab not found — skipping cron job setup."
    warn "To update manually: $DEPLOY_DIR/update.sh"
    return 0
  fi

  local update_script="$DEPLOY_DIR/update.sh"
  local log_file="$DEPLOY_DIR/update.log"
  local cron_line="0 3 * * * $update_script >> $log_file 2>&1"
  local certbot_bin="$(command -v certbot 2>/dev/null || true)"
  local renewal_line=""
  if [[ -n "$PARAM_VPN_DOMAIN" && -n "$certbot_bin" ]]; then
    renewal_line="30 3 * * * $certbot_bin renew --quiet >> $DEPLOY_DIR/certbot-renew.log 2>&1 # easy-xray certificate renewal"
  fi

  local current_crontab line
  current_crontab="$(crontab -l 2>/dev/null || true)"
  info "Installing cron jobs..."

  {
    while IFS= read -r line; do
      [[ -z "$line" || "$line" == *"$update_script"* || "$line" == *"# easy-xray certificate renewal"* ]] && continue
      printf '%s\n' "$line"
    done <<< "$current_crontab"
    printf '%s\n' "$cron_line"
    [[ -z "$renewal_line" ]] || printf '%s\n' "$renewal_line"
  } | crontab -

  info "Daily update configured at 3:00 AM: $update_script"
  if [[ -n "$renewal_line" ]]; then
    info "Certbot renewal configured at 3:30 AM."
  fi
}

# ------------------------------------------------------------
# 11. verify_health
# ------------------------------------------------------------
step_verify_health() {
  local max_attempts=30
  local sleep_sec=3
  local attempt=1

  info "Waiting for http://localhost to become ready (up to $((max_attempts * sleep_sec))s) ..."

  while [[ $attempt -le $max_attempts ]]; do
    if curl -sf --max-time 5 http://localhost &>/dev/null; then
      info "http://localhost responded — service is up."
      return 0
    fi
    info "  Attempt $attempt/$max_attempts — not ready yet, waiting ${sleep_sec}s ..."
    sleep "$sleep_sec"
    (( attempt++ )) || true
  done

  warn "Service did not respond on http://localhost after $((max_attempts * sleep_sec))s."
  warn "It may still be starting up. Check with: docker compose -f $DEPLOY_DIR/docker-compose.yaml ps"
  # Warn, don't fail
  return 0
}

# ------------------------------------------------------------
# 12. verify_cloudflare
# ------------------------------------------------------------

# The profile Xray runs: the super admin's choice once made, else the first-start default.
active_profile() {
  local settings="$DEPLOY_DIR/data/app/settings.json" active=""
  if [[ -f "$settings" ]]; then
    active="$(grep -o '"xray_active_profile": *"[^"]*"' "$settings" | cut -d'"' -f4 || true)"
  fi
  echo "${active:-$PARAM_PROFILE}"
}

step_verify_cloudflare() {
  [[ -n "$PARAM_CF_DOMAIN" ]] || return 0
  info "Checking https://$PARAM_CF_DOMAIN through Cloudflare ..."

  local headers status
  if ! headers="$(curl -sS -o /dev/null -D - --max-time 15 "https://$PARAM_CF_DOMAIN/" 2>&1)"; then
    warn "Could not open https://$PARAM_CF_DOMAIN: $(tail -1 <<<"$headers")"
    warn "  Cloudflare's edge certificate isn't valid for this hostname. Use a hostname one level deep"
    warn "  (cdn.example.com, not a.cdn.example.com), and check the zone is active on Cloudflare."
    return 0
  fi

  if ! grep -qi '^server: *cloudflare' <<<"$headers"; then
    warn "$PARAM_CF_DOMAIN is not proxied by Cloudflare (no 'server: cloudflare' header)."
    warn "  Turn on the orange cloud for its DNS record. Otherwise VPN clients connect straight to"
    warn "  this server, see the Origin CA certificate, and fail."
    return 0
  fi

  status="$(head -1 <<<"$headers" | awk '{print $2}')"
  # Only meaningful while a Cloudflare profile runs: with REALITY active, Cloudflare's
  # connection lands on REALITY and always looks like a certificate error.
  local active
  active="$(active_profile)"
  if cf_profile_dirs | grep -qxF "$DEPLOY_DIR/xray/$active" && [[ "$status" == "525" || "$status" == "526" ]]; then
    warn "Cloudflare answered $status: its TLS connection to this server failed (525) or the"
    warn "  certificate was rejected (526). Check the Origin certificate in xray/$active/tls/"
    warn "  and set SSL/TLS mode to Full (strict)."
    return 0
  fi

  info "Cloudflare proxy OK ($PARAM_CF_DOMAIN answered HTTP $status through Cloudflare)."
}

# ============================================================
# Main
# ============================================================
mkdir -p "$DEPLOY_DIR"

log "============================================================"
log "  easy-xray deployment"
log "  Domain : $PARAM_DOMAIN"
log "============================================================"

# --upgrade re-runs the steps that bring an existing install up to date.
UPGRADE_MODE=""
[[ "$PARAM_UPGRADE" == true ]] && UPGRADE_MODE="always"

XRAY_CERT_CHANGED=false
XRAY_WAS_RUNNING=false

run_step "download_files"          step_download_files          "$UPGRADE_MODE"
run_step "migrate_legacy_layout"   step_migrate_legacy_layout   always
run_step "install_prerequisites"   step_install_prerequisites
run_step "install_vpn_prerequisites" step_install_vpn_prerequisites always
run_step "check_prerequisites"     step_check_prerequisites     always
run_step "generate_secrets"        step_generate_secrets        always
run_step "write_env_file"          step_write_env_file          always
run_step "install_cf_certificate"  step_install_cf_certificate  always
run_step "write_xray_profiles"     step_write_xray_profiles     always
run_step "pull_images"             step_pull_images             "$UPGRADE_MODE"
if command -v docker &>/dev/null \
    && [[ -n "$(cd "$DEPLOY_DIR" && docker compose ps -q --status running xray 2>/dev/null || true)" ]]; then
  XRAY_WAS_RUNNING=true
fi
run_step "install_vpn_certificate" step_install_vpn_certificate always
run_step "start_services"          step_start_services          "$UPGRADE_MODE"
run_step "reload_xray"             step_reload_xray             always
run_step "setup_cron_jobs"         step_setup_cron_jobs         always
run_step "verify_health"           step_verify_health
run_step "verify_cloudflare"       step_verify_cloudflare       always

echo ""
echo "============================================================"
echo "  easy-xray is running!"
echo "  Management UI: https://${PARAM_DOMAIN}"
echo "  First login: enter any username and password to create the super admin account"
echo "  Active profile on first start: ${PARAM_PROFILE} (switch it on the Profiles page)"
if [[ -n "$PARAM_CF_DOMAIN" ]]; then
  echo "  Cloudflare profiles on ${PARAM_CF_DOMAIN}: $(cf_profile_dirs | xargs -r -n1 basename | tr '\n' ' ')"
fi
if [[ -n "$PARAM_VPN_DOMAIN" ]]; then
  echo "  Direct TLS profiles on ${PARAM_VPN_DOMAIN}: vless-ws-tls vless-xhttp-tls"
fi
if [[ "$PARAM_BRANCH" != "main" || "$PARAM_IMAGE_TAG" != "latest" ]]; then
  echo "  Installed from branch ${PARAM_BRANCH} with app image tag ${PARAM_IMAGE_TAG}"
fi
echo "  Documentation: https://github.com/hamidmayeli/quick-vless/blob/${PARAM_BRANCH}/docs/hosting.md"
echo "============================================================"
