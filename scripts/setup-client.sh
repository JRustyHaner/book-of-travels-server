#!/usr/bin/env bash
# Book of Travels — Braid private-server client setup (Linux)
# Downloads and installs the BepInEx runtime + BotMaster plugin into your
# own Steam install. No game files are modified (all additions, removable).
set -euo pipefail

REPO="JRustyHaner/book-of-travels-server"
BASE="https://github.com/${REPO}/releases/latest/download"
MASTER_HOST="${BOT_MASTER_HOST:-braid.flightlessbirdlabs.io}"
MASTER_PORT="${BOT_MASTER_PORT:-1234}"
GAME="${BOT_GAME_DIR:-$HOME/.local/share/Steam/steamapps/common/Book of Travels}"

echo "==> Braid client setup"
echo "    server: $MASTER_HOST:$MASTER_PORT"
echo "    game:   $GAME"

[ -x "$GAME/BookOfTravels.x86_64" ] || { echo "ERROR: game not found at $GAME (set BOT_GAME_DIR)"; exit 1; }

# 1) BepInEx runtime (idempotent — only if not already installed)
if [ ! -f "$GAME/run_bepinex.sh" ]; then
  echo "==> downloading BepInEx runtime"
  curl -fsSL "$BASE/bepinex-linux.zip" -o /tmp/bot-bepinex.zip
  (cd "$GAME" && unzip -oq /tmp/bot-bepinex.zip && chmod +x run_bepinex.sh)
  rm -f /tmp/bot-bepinex.zip
  echo "    installed BepInEx runtime"
fi

# 2) plugin (always refresh to latest)
echo "==> downloading BotMaster plugin"
curl -fsSL "$BASE/botmaster-plugin.zip" -o /tmp/bot-plugin.zip
mkdir -p "$GAME/BepInEx/plugins"
unzip -oq /tmp/bot-plugin.zip -d "$GAME/BepInEx/plugins/"
rm -f /tmp/bot-plugin.zip
echo "    plugin installed"

# 3) config (role=client, points at the Braid master)
mkdir -p "$GAME/BepInEx/config"
cat > "$GAME/BepInEx/config/dev.botmaster.plugin.cfg" <<EOF
[general]
role = "client"
masterHost = "$MASTER_HOST"
masterPort = $MASTER_PORT
EOF
echo "    config written (role=client)"

# 4) steam appid so Steamworks works when launched outside Steam
printf '1152340' > "$GAME/steam_appid.txt"

echo
echo "==> done. Launch the game through the mod:"
echo
echo "    cd \"$GAME\""
echo "    ./run_bepinex.sh ./BookOfTravels.x86_64 \\"
echo "        -screen-fullscreen 0 -screen-width 1280 -screen-height 720"
echo
echo "    (or set these Steam launch options and use the Play button:)"
echo "    env LD_PRELOAD=\"$GAME/libdoorstop.so\" DOORSTOP_ENABLED=1 \\"
echo "        DOORSTOP_TARGET_ASSEMBLY=BepInEx/core/BepInEx.Unity.Mono.Preloader.dll \\"
echo "        DOORSTOP_MONO_DLL_SEARCH_PATH_OVERRIDE=BepInEx/core %command% \\"
echo "        -screen-fullscreen 0 -screen-width 1280 -screen-height 720"
echo
echo "Log in with any email + password (account is created automatically)."
