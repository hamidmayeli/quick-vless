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

# ------------------------------------------------------------
# Usage
# ------------------------------------------------------------
usage() {
  cat <<EOF
Usage: $(basename "$0") [OPTIONS]

Non-interactive deployment script for easy-xray.

Run directly without cloning:
  bash <(curl -fsSL https://raw.githubusercontent.com/hamidmayeli/outline-ss-ws/refs/heads/main/install.sh) --domain manage.example.com

Required:
  --domain DOMAIN           Management UI domain (e.g. manage.example.com)

Optional:
  --host HOST               Server IP/domain in vless:// URLs
                            (default: same as --domain)
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
  --force STEP              Force a specific step to re-run even if already done
  --reset                   Clear the state file and start fresh
  -h, --help                Print this message and exit

Steps (in execution order):
  download_files            Download docker-compose.yaml, Caddyfile,
                            Dockerfile.caddy, xray/config.json, update.sh
  install_prerequisites     Install Docker Engine + openssl + curl via apt
                            (no-op on non-apt systems)
  check_prerequisites       Verify docker, docker compose (V2), openssl, curl
  generate_secrets          Fill in any missing secrets/keys
  write_env_file            Write \$DEPLOY_DIR/.env (chmod 600)
  write_xray_config         Patch xray/config.json with actual key and short ID
  pull_images               docker compose pull
  start_services            docker compose up -d
  setup_cron_jobs           Install crontab entry: update.sh daily at 3:00 AM
  verify_health             Poll http://localhost until ready (warn on timeout)

State file: \$DEPLOY_DIR/.install-state
  Completed step names are recorded one per line. Re-running the script
  skips steps already recorded. Use --force STEP to re-run one step, or
  --reset to clear all state and start over.
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
PARAM_DEPLOY_DIR=""
PARAM_SKIP_PULL=false
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
    --deploy-dir=*)     PARAM_DEPLOY_DIR="${1#*=}";        shift ;;
    --deploy-dir)       PARAM_DEPLOY_DIR="${2:-}";         shift 2 ;;
    --skip-pull)        PARAM_SKIP_PULL=true;              shift ;;
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
  # SNI and fingerprint: only load if they haven't been overridden and the file has them
  local _sni; _sni="$(_env_val XRAY_SNI)"
  local _fp;  _fp="$(_env_val XRAY_FINGERPRINT)"
  [[ -z "$_sni" ]] || PARAM_XRAY_SNI="$_sni"
  [[ -z "$_fp"  ]] || PARAM_XRAY_FINGERPRINT="$_fp"
}

load_env_if_present

# ------------------------------------------------------------
# State helpers
# ------------------------------------------------------------
step_is_done() {
  grep -qxF "$1" "$STATE_FILE" 2>/dev/null
}

mark_step_done() {
  grep -qxF "$1" "$STATE_FILE" 2>/dev/null || echo "$1" >> "$STATE_FILE"
}

run_step() {
  local name="$1"
  local func="$2"

  if step_is_done "$name" && [[ "$PARAM_FORCE_STEP" != "$name" ]]; then
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
GITHUB_RAW="https://raw.githubusercontent.com/hamidmayeli/quick-vless/refs/heads/main/deployables"

step_download_files() {
  if [[ -f "$DEPLOY_DIR/docker-compose.yaml" ]]; then
    info "docker-compose.yaml already present in $DEPLOY_DIR — skipping download."
    return 0
  fi

  info "Downloading deployment files from GitHub..."
  mkdir -p "$DEPLOY_DIR/xray"

  local files=(
    "docker-compose.yaml"
    "Caddyfile"
    "Dockerfile.caddy"
    "xray/config.json"
    "update.sh"
  )

  for f in "${files[@]}"; do
    local dest="$DEPLOY_DIR/$f"
    mkdir -p "$(dirname "$dest")"
    info "  Downloading $f ..."
    curl -fsSL --max-time 30 "$GITHUB_RAW/$f" -o "$dest" \
      || die "Failed to download $f from $GITHUB_RAW/$f"
  done

  chmod +x "$DEPLOY_DIR/update.sh"
  info "All deployment files downloaded."
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
# 3. check_prerequisites
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
# 4. generate_secrets
# ------------------------------------------------------------
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
    # PKCS8 DER for x25519 is always 48 bytes; last 32 are the raw private key.
    # SubjectPublicKeyInfo DER for x25519 is always 44 bytes; last 32 are the raw public key.
    local xray_key_pem
    xray_key_pem="$(openssl genpkey -algorithm x25519 2>/dev/null)" \
      || die "openssl failed to generate an x25519 key pair."
    PARAM_XRAY_PRIVATE_KEY="$(printf '%s' "$xray_key_pem" \
      | openssl pkey -outform DER 2>/dev/null | tail -c 32 \
      | base64 | tr '+/' '-_' | tr -d '=')"
    PARAM_XRAY_PUBLIC_KEY="$(printf '%s' "$xray_key_pem" \
      | openssl pkey -pubout -outform DER 2>/dev/null | tail -c 32 \
      | base64 | tr '+/' '-_' | tr -d '=')"
    [[ -n "$PARAM_XRAY_PRIVATE_KEY" && -n "$PARAM_XRAY_PUBLIC_KEY" ]] \
      || die "Failed to derive x25519 key pair from openssl output."
    info "  Xray key pair generated."
  else
    info "  Xray key pair: (provided)"
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

  info "  SNI         : $PARAM_XRAY_SNI"
  info "  Fingerprint : $PARAM_XRAY_FINGERPRINT"
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
EOF

  chmod 600 "$env_file"
  info ".env written and permissions set to 600."
}

# ------------------------------------------------------------
# 6. write_xray_config
# ------------------------------------------------------------
step_write_xray_config() {
  local config="$DEPLOY_DIR/xray/config.json"

  [[ -f "$config" ]] || die "xray/config.json not found at $config"
  [[ -n "$PARAM_XRAY_PRIVATE_KEY" ]] || die "XRAY_REALITY_PRIVATE_KEY is empty."
  [[ -n "$PARAM_XRAY_SHORT_ID"    ]] || die "XRAY_REALITY_SHORT_ID is empty."

  local patched=false

  if grep -qF 'YOUR_PRIVATE_KEY' "$config"; then
    info "  Patching YOUR_PRIVATE_KEY in xray/config.json ..."
    sed -i "s|YOUR_PRIVATE_KEY|${PARAM_XRAY_PRIVATE_KEY}|g" "$config"
    patched=true
  else
    info "  YOUR_PRIVATE_KEY placeholder not found — already patched."
  fi

  if grep -qF 'YOUR_SHORT_ID' "$config"; then
    info "  Patching YOUR_SHORT_ID in xray/config.json ..."
    sed -i "s|YOUR_SHORT_ID|${PARAM_XRAY_SHORT_ID}|g" "$config"
    patched=true
  else
    info "  YOUR_SHORT_ID placeholder not found — already patched."
  fi

  if grep -qF 'YOUR_SNI' "$config"; then
    info "  Patching YOUR_SNI in xray/config.json ..."
    sed -i "s|YOUR_SNI|${PARAM_XRAY_SNI}|g" "$config"
    patched=true
  else
    info "  YOUR_SNI placeholder not found — already patched."
  fi

  if [[ "$patched" == true ]]; then
    info "xray/config.json patched."
  else
    info "xray/config.json required no changes (already configured)."
  fi
}

# ------------------------------------------------------------
# 7. pull_images
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
# 8. start_services
# ------------------------------------------------------------
step_start_services() {
  info "Starting services with 'docker compose up -d' ..."
  (cd "$DEPLOY_DIR" && docker compose up -d) \
    || die "docker compose up failed."
  info "Services started."
}

# ------------------------------------------------------------
# 9. setup_cron_jobs
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

  info "Installing cron job for automatic daily updates..."

  crontab -l 2>/dev/null \
    | grep -v -F "$update_script" \
    | { cat; echo "$cron_line"; } \
    | crontab -

  info "Cron job configured: daily at 3:00 AM → $update_script"
}

# ------------------------------------------------------------
# 10. verify_health
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

# ============================================================
# Main
# ============================================================
mkdir -p "$DEPLOY_DIR"

log "============================================================"
log "  easy-xray deployment"
log "  Domain : $PARAM_DOMAIN"
log "============================================================"

run_step "download_files"          step_download_files
run_step "install_prerequisites"   step_install_prerequisites
run_step "check_prerequisites"     step_check_prerequisites
run_step "generate_secrets"     step_generate_secrets
run_step "write_env_file"       step_write_env_file
run_step "write_xray_config"    step_write_xray_config
run_step "pull_images"          step_pull_images
run_step "start_services"       step_start_services
run_step "setup_cron_jobs"      step_setup_cron_jobs
run_step "verify_health"        step_verify_health

echo ""
echo "============================================================"
echo "  easy-xray is running!"
echo "  Management UI: https://${PARAM_DOMAIN}"
echo "  First login: enter any username and password to create the admin account"
echo "  Documentation: https://github.com/hamidmayeli/easy-xray/blob/main/docs/hosting.md"
echo "============================================================"
