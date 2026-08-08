#!/usr/bin/env bash
# Starts the emulated master server on 0.0.0.0:1234.
set -euo pipefail
export DOTNET_CLI_TELEMETRY_OPTOUT=1
# .NET 8 SDK must be on PATH (or set DOTNET_ROOT + add \$DOTNET_ROOT to PATH)
cd "$(dirname "$0")/../server/master"
exec dotnet run
