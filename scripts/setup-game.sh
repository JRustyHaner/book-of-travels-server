#!/usr/bin/env bash
# Installs BepInEx + BotMasterPlugin into the game folder and writes the plugin cfg.
# Usage: setup-game.sh <client|instance> [masterHost]
set -euo pipefail

ROLE="${1:-client}"
MASTER="${2:-127.0.0.1}"
GAME="${BOT_GAME_DIR:-$HOME/.local/share/Steam/steamapps/common/Book of Travels}"
DIR="$(cd "$(dirname "$0")" && pwd)"

echo "==> role=$ROLE  master=$MASTER  game=$GAME"

# 1) BepInEx 6 runtime (idempotent)
if [ ! -f "$GAME/doorstop_config.ini" ]; then
  cp -r "$DIR/../runtime/bepinex/." "$GAME/"
  chmod +x "$GAME/run_bepinex.sh"
  echo "==> copied BepInEx runtime into game folder"
else
  echo "==> BepInEx already installed"
fi

# 2) build + install the plugin
# .NET 8 SDK: prefer $DOTNET_ROOT, else the dotnet-install.sh default (~/.dotnet)
if [ -d "${DOTNET_ROOT:-$HOME/.dotnet}" ]; then
    export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
    export PATH="$PATH:$DOTNET_ROOT"
fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1
export BOT_GAME_DIR="$GAME"
(cd "$DIR/../server/plugin" && dotnet build -c Release -v q >/dev/null)
mkdir -p "$GAME/BepInEx/plugins"
cp "$DIR/../server/plugin/bin/Release/netstandard2.1/BotMasterPlugin.dll" "$GAME/BepInEx/plugins/"
echo "==> plugin installed"

# 3) plugin config (GUID-based filename)
mkdir -p "$GAME/BepInEx/config"
cat > "$GAME/BepInEx/config/dev.botmaster.plugin.cfg" <<EOF
[general]
role = "$ROLE"
masterHost = "$MASTER"
masterPort = 1234
EOF
echo "==> cfg written (role=$ROLE)"
echo "DONE"
