#!/usr/bin/env bash
# install-client-linux.sh --- installs the Braid client mod into a Linux
# Book of Travels install (Steam). Downloads the latest client bundle from the
# Braid GitHub release and patches nothing --- only adds BepInEx + the plugin.
#
# Usage: ./install-client-linux.sh [masterHost [masterPort]]
# Env: BRAID_GAME_DIR overrides the game folder.
set -euo pipefail

MASTER_HOST="${1:-braid-connect.flightlessbirdlabs.io}"
MASTER_PORT="${2:-1234}"
GAME="${BRAID_GAME_DIR:-$HOME/.local/share/Steam/steamapps/common/Book of Travels}"
BASE="https://github.com/JRustyHaner/book-of-travels-server/releases/latest/download"

echo "==> Braid client installer"
echo "    master: $MASTER_HOST:$MASTER_PORT"
echo "    game:   $GAME"

[ -x "$GAME/BookOfTravels.x86_64" ] || { echo "ERROR: Book of Travels not found at '$GAME'. Set BRAID_GAME_DIR or pass the correct path."; exit 1; }

TMP="$(mktemp -d)"
trap 'rm -rf "$TMP"' EXIT

echo "==> downloading bundle"
curl -fsSL "$BASE/braid-client-linux.zip" -o "$TMP/bundle.zip"

echo "==> extracting into game folder (additive, no game files touched)"
unzip -qo "$TMP/bundle.zip" -d "$GAME"

mkdir -p "$GAME/BepInEx/config"
cat > "$GAME/BepInEx/config/dev.botmaster.plugin.cfg" <<EOF
[general]
role = "client"
masterHost = "$MASTER_HOST"
masterPort = $MASTER_PORT
EOF

echo "==> done. Launch the game with:"
echo "    cd \"$GAME\" && ./run_bepinex.sh ./BookOfTravels.x86_64"
echo "    (Log in with any email/password --- the Braid server provisions the account.)"
