#!/usr/bin/env bash
# Builds the client bundles and publishes a GitHub Release for the Braid server.
#
# Outputs (under release/dist/):
#   braid-client-linux.zip    BepInEx (linux) + BotMasterPlugin — for Linux clients
#   braid-client-windows.zip  BepInEx (win_x64) + BotMasterPlugin — for Windows clients
#
# Usage:
#   release/build-bundle.sh                 # build bundles only (needs BOT_GAME_DIR + zip/unzip + dotnet)
#   release/build-bundle.sh --release 0.1.0 # build + create GitHub release with both assets
set -euo pipefail

ROOT="$(cd "$(dirname "$0")/.." && pwd)"
OUT="$ROOT/release/dist"
VER="${1:-}"

export DOTNET_CLI_TELEMETRY_OPTOUT=1
BOT_GAME_DIR="${BOT_GAME_DIR:?set BOT_GAME_DIR to a Book of Travels install (plugin needs its assemblies to build)}"

# --- build the plugin -------------------------------------------------------
echo "==> building plugin"
(cd "$ROOT/server/plugin" && dotnet build -c Release --no-incremental -v q >/dev/null)
PLUGIN="$ROOT/server/plugin/bin/Release/netstandard2.1/BotMasterPlugin.dll"
[ -f "$PLUGIN" ] || { echo "plugin build failed"; exit 1; }

rm -rf "$OUT/linux" "$OUT/win" "$OUT"/braid-client-*.zip

# --- linux bundle (BepInEx runtime we ship in this repo) ----------------------
echo "==> linux bundle"
mkdir -p "$OUT/linux/BepInEx/plugins"
cp -r "$ROOT/runtime/bepinex/BepInEx" "$OUT/linux/"
cp "$ROOT/runtime/bepinex/libdoorstop.so" "$ROOT/runtime/bepinex/run_bepinex.sh" "$OUT/linux/"
cp "$PLUGIN" "$OUT/linux/BepInEx/plugins/"
(cd "$OUT/linux" && zip -qr "$OUT/braid-client-linux.zip" .)

# --- windows bundle (BepInEx win_x64 from upstream, plugin is OS-agnostic) ----
echo "==> windows bundle"
# BepInEx 6 Unity Mono (win-x64) — the plugin is built for BepInEx 6, so we must
# NOT use the repo's "latest release" (still BepInEx 5.x) or the plugin won't load.
WIN_URL="$(curl -fsSL "https://api.github.com/repos/BepInEx/BepInEx/releases?per_page=40" \
  | python3 -c "import json,sys; rels=json.load(sys.stdin); print(next(a['browser_download_url'] for r in rels for a in r.get('assets',[]) if 'Unity.Mono-win-x64' in a['name']))")"
curl -fsSL "$WIN_URL" -o /tmp/bepinex-win.zip
mkdir -p "$OUT/win"
unzip -qo /tmp/bepinex-win.zip -d "$OUT/win"
chmod -R u+rwx "$OUT/win" # the BepInEx zip ships mode-000 dir entries
mkdir -p "$OUT/win/BepInEx/plugins"
cp "$PLUGIN" "$OUT/win/BepInEx/plugins/"
(cd "$OUT/win" && zip -qr "$OUT/braid-client-windows.zip" .)

sha256sum "$OUT"/braid-client-*.zip

# --- optional: create the GitHub release --------------------------------------
if [ -n "$VER" ]; then
  echo "==> creating release $VER"
  gh release create "$VER" "$OUT/braid-client-linux.zip" "$OUT/braid-client-windows.zip" \
    --title "Braid $VER" \
    --notes "Client bundles for the Braid private server. See https://braid.flightlessbirdlabs.io for setup."
  echo "==> done: https://github.com/JRustyHaner/book-of-travels-server/releases/tag/$VER"
fi
