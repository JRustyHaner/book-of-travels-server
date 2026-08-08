#!/usr/bin/env bash
# Instance entrypoint: runs the mounted game headless with BepInEx enabled.
# Expects /game to contain a Book of Travels install with BepInEx + the
# BotMaster plugin configured for role=instance (run scripts/setup-game.sh
# instance on the host game dir first).
set -euo pipefail

cd /game
if [ ! -x ./BookOfTravels.x86_64 ]; then
    echo "ERROR: /game does not contain BookOfTravels.x86_64" >&2
    echo "Mount the game dir (with BepInEx installed) at /game" >&2
    exit 1
fi
if [ ! -x ./run_bepinex.sh ]; then
    echo "ERROR: /game missing BepInEx runtime (run scripts/setup-game.sh instance)" >&2
    exit 1
fi

echo "==> braid instance starting (headless)"
exec ./run_bepinex.sh ./BookOfTravels.x86_64 -batchmode -nographics "$@"
