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
BASE="https://github.com/JRustyHaner/book-of-travels-server/releases/latest/download"

# Locate Book of Travels by scanning Steam library folders (libraryfolders.vdf),
# covering default installs and games on other drives/folders.
find_game() {
    for d in "$HOME/.steam/steam/steamapps" "$HOME/.local/share/Steam/steamapps"; do
        [ -x "$d/common/Book of Travels/BookOfTravels.x86_64" ] && { echo "$d/common/Book of Travels"; return; }
    done
    for vdf in "$HOME/.steam/steam/steamapps/libraryfolders.vdf" "$HOME/.local/share/Steam/steamapps/libraryfolders.vdf"; do
        [ -f "$vdf" ] || continue
        while IFS= read -r line; do
            p="$(printf '%s\n' "$line" | sed -n 's/.*"path"[[:space:]]*"\(.*\)".*/\1/p')"
            if [ -n "$p" ] && [ -x "$p/steamapps/common/Book of Travels/BookOfTravels.x86_64" ]; then
                echo "$p/steamapps/common/Book of Travels"; return
            fi
        done < "$vdf"
    done
    echo ""
}

if [ -z "${BRAID_GAME_DIR:-}" ]; then
    GAME="$(find_game)"
    [ -n "$GAME" ] || GAME="$HOME/.local/share/Steam/steamapps/common/Book of Travels"
else
    GAME="$BRAID_GAME_DIR"
fi

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
