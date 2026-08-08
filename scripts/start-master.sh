#!/usr/bin/env bash
# Starts the emulated master server on 0.0.0.0:1234.
set -euo pipefail
# .NET 8 SDK: prefer $DOTNET_ROOT, else the dotnet-install.sh default (~/.dotnet)
if [ -d "${DOTNET_ROOT:-$HOME/.dotnet}" ]; then
    export DOTNET_ROOT="${DOTNET_ROOT:-$HOME/.dotnet}"
    export PATH="$PATH:$DOTNET_ROOT"
fi
export DOTNET_CLI_TELEMETRY_OPTOUT=1
cd "$(dirname "$0")/../server/master"
exec dotnet run
