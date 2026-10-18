#!/bin/sh
# Entrypoint of the xray container.
#
# Runs Xray from the config the management app generates, and restarts it when the app
# changes the config's structure (for example a profile switch). User changes alone don't
# restart Xray: the app applies those through Xray's API.
#
#   config.json       written by the app — the config Xray runs
#   structure.sha256  written by the app — changes only when Xray needs a restart
#   running.sha256    written here once Xray has stayed up with that structure
set -u

R=/etc/xray/runtime
CONFIG="$R/config.json"
WANTED="$R/structure.sha256"
RUNNING="$R/running.sha256"
pid=""

# Sleep in the background so a stop signal is handled straight away.
nap() {
  sleep "$1" &
  wait $!
}

stop() {
  if [ -n "$pid" ]; then
    kill "$pid" 2>/dev/null
    wait "$pid" 2>/dev/null
  fi
  rm -f "$RUNNING"
  exit 0
}
trap stop TERM INT

until [ -f "$CONFIG" ] && [ -f "$WANTED" ]; do
  echo "xray-run: waiting for the management app to write $CONFIG"
  nap 2
done

while true; do
  want=$(cat "$WANTED")
  rm -f "$RUNNING"
  echo "xray-run: starting Xray (structure $(echo "$want" | cut -c1-12))"
  xray run -config "$CONFIG" &
  pid=$!

  nap 3
  if kill -0 "$pid" 2>/dev/null; then
    echo "$want" > "$RUNNING"
  fi

  while kill -0 "$pid" 2>/dev/null && [ "$(cat "$WANTED" 2>/dev/null)" = "$want" ]; do
    nap 2
  done

  if kill -0 "$pid" 2>/dev/null; then
    echo "xray-run: the config structure changed — restarting Xray"
    kill "$pid" 2>/dev/null
    wait "$pid" 2>/dev/null
  else
    wait "$pid" 2>/dev/null
    echo "xray-run: Xray exited with status $? — restarting it"
  fi
  pid=""
  nap 1
done
