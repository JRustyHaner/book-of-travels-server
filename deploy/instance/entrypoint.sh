#!/bin/sh
# Launches the headless game instance with the BepInEx mod (role=instance).
# Expects the game (with runtime/plugin installed) bind-mounted at /game.
set -e

cd /game || { echo "game dir not mounted at /game"; exit 1; }

[ -x ./run_bepinex.sh ] || { echo "run_bepinex.sh missing — install the mod first"; exit 1; }

echo "==> launching headless instance (master=$MDY_INSTANCE_SERVER)"
exec ./run_bepinex.sh ./BookOfTravels.x86_64 -batchmode -nographics
