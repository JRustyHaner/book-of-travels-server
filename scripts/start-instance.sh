#!/usr/bin/env bash
# Launches the headless game instance (dedicated server) wired to the master.
# Starts a MariaDB docker container on first run and waits for it.
# Usage: start-instance.sh [masterHost:port]
set -euo pipefail

MASTER="${1:-127.0.0.1:1234}"
GAME="${BOT_GAME_DIR:-$HOME/.local/share/Steam/steamapps/common/Book of Travels}"

# --- MariaDB (idempotent) ---
if ! docker ps --format '{{.Names}}' 2>/dev/null | grep -qx 'bot-mariadb'; then
  if docker ps -a --format '{{.Names}}' 2>/dev/null | grep -qx 'bot-mariadb'; then
    docker start bot-mariadb >/dev/null
  else
    echo "==> creating mariadb container (bot-mariadb)"
    docker run -d --name bot-mariadb \
      -e MARIADB_ROOT_PASSWORD=botroot \
      -e MARIADB_DATABASE=bot \
      -e MARIADB_USER=bot \
      -e MARIADB_PASSWORD=bot \
      -p 3306:3306 mariadb:10.6 >/dev/null
  fi
fi
echo "==> waiting for mariadb..."
for _ in $(seq 1 60); do
  docker exec bot-mariadb mysqladmin -ubot -pbot ping >/dev/null 2>&1 && break
  sleep 1
done
docker exec bot-mariadb mysqladmin -ubot -pbot ping >/dev/null 2>&1 || { echo "mariadb not reachable"; exit 1; }
echo "==> mariadb ready"

# --- game instance ---
cd "$GAME"
export MDY_DB_URL="Server=127.0.0.1;Port=3306;Database=bot;Uid=bot;Pwd=bot"
export MDY_INSTANCE_SERVER="$MASTER"
echo "==> launching headless instance (master=$MASTER)"
exec ./run_bepinex.sh ./BookOfTravels.x86_64 -batchmode -nographics
