#!/usr/bin/env bash
set -euo pipefail

DEPLOY_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
env_file="$DEPLOY_DIR/.env"
[[ -f "$env_file" ]] || exit 0

vpn_domain="$(sed -n 's/^XRAY_VPN_DOMAIN=//p' "$env_file" | head -1)"
[[ -n "$vpn_domain" ]] || exit 0
case " ${RENEWED_DOMAINS:-} " in
  *" $vpn_domain "*) ;;
  *) exit 0 ;;
esac

lineage="${RENEWED_LINEAGE:-}"
[[ -s "$lineage/fullchain.pem" && -s "$lineage/privkey.pem" ]] || {
  echo "Renewed certificate files are missing from $lineage." >&2
  exit 1
}

for profile in vless-ws-tls vless-xhttp-tls; do
  tls_dir="$DEPLOY_DIR/xray/$profile/tls"
  install -d -m 700 "$tls_dir"
  install -m 644 "$lineage/fullchain.pem" "$tls_dir/fullchain.pem"
  install -m 600 "$lineage/privkey.pem" "$tls_dir/privkey.pem"
done

active_profile="$(grep -o '"xray_active_profile": *"[^"]*"' "$DEPLOY_DIR/data/app/settings.json" 2>/dev/null | cut -d'"' -f4 || true)"
if [[ -z "$active_profile" ]]; then
  active_profile="$(sed -n 's/^XRAY_DEFAULT_PROFILE=//p' "$env_file" | head -1)"
fi
case "$active_profile" in
  vless-ws-tls|vless-xhttp-tls)
    if [[ -n "$(cd "$DEPLOY_DIR" && docker compose ps -q --status running xray 2>/dev/null || true)" ]]; then
      (cd "$DEPLOY_DIR" && docker compose restart xray)
    fi
    ;;
esac