# Third-Party Notices

This project (the server-emulation stack in this repository) is licensed under
Apache-2.0 (see `LICENSE`). It depends on and bundles the following
third-party components. This project does **not** bundle any part of the
Book of Travels game or the "Years" framework — those remain the property of
Might & Delight AB and are never distributed here.

## Bundled runtime (in `runtime/`)

| Component | Version | License | Notes |
|---|---|---|---|
| BepInEx 6 | 6.0.0-be.697 | LGPL-3.0 | Unity modding framework; loaded dynamically via Doorstop; sources: https://github.com/BepInEx/BepInEx |
| Unity Doorstop | (with BepInEx) | LGPL-3.0 | `libdoorstop.so`, `run_bepinex.sh` |
| 0Harmony | (with BepInEx) | MIT | Runtime patching library, https://github.com/BepInEx/HarmonyX |

LGPL note: the plugin (`server/plugin/`) is loaded as a separate, dynamically
linked module against BepInEx's public API; no modifications to BepInEx itself
are distributed.

## Build-time references (not bundled)

The plugin compiles against assemblies from a **user-provided** Book of Travels
install (via `BOT_GAME_DIR`). Those assemblies are proprietary to their
respective owners and are never included in this repository:

- Game assemblies: © Might & Delight AB (proprietary; do not redistribute)
- Unity engine runtime: © Unity Technologies (proprietary; redistributable only per Unity's terms)
- Sirenix Odin Inspector: commercial asset (Sirenix)
- Steamworks.NET / Steamworks SDK: © Valve Corporation (proprietary SDK)
- Discord Game SDK: © Discord Inc. (proprietary SDK)
- Autodesk FBX SDK: © Autodesk (proprietary)
- Various asset-store packages (Coffee.UIParticle, PathCreator, ParadoxNotion/NodeCanvas, Kronnect Volumetric Fog, etc.): commercial or permissive, per their own licenses

## Open-source libraries used by the master server and tests

- ASP.NET Core / .NET 8 (MIT)
- Grpc.Net.Client / Grpc.Tools (Apache-2.0)
- Google.Protobuf (BSD-3-Clause)
- SQLite / Microsoft.Data.Sqlite (MIT) — via the EF Core stack or direct
- grpcio / grpcio-tools (Apache-2.0) — used by the e2e test only
- Newtonsoft.Json (MIT) — used by the game, referenced by the plugin
- Microsoft.IdentityModel.* (MIT) — JWT handling in the plugin

## Trademarks

"Book of Travels" and "Years" are trademarks of Might & Delight AB. This
project is an independent, community-run server emulation and is not
affiliated with, endorsed by, or sponsored by Might & Delight AB.
