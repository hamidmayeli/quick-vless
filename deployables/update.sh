#!/usr/bin/env bash
set -euo pipefail

# Ensure the script runs from its parent directory
cd "$(dirname "${BASH_SOURCE[0]}")"

# Pull updated images and recreate running containers
docker compose pull
docker compose up -d --remove-orphans

# Brief delay to allow containerd to finalize background layer/attestation commits
sleep 5

# Safely prune untagged/dangling images
docker image prune -f