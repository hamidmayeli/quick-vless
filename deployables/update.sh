#!/usr/bin/env bash
set -euo pipefail

# Run from the script's own directory so docker compose finds the project files.
cd "$(dirname "${BASH_SOURCE[0]}")"

docker compose pull
docker compose up -d --remove-orphans
docker image prune -f
