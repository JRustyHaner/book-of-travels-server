# Book of Travels — private server emulation

An open-source, self-hosted server stack for **Book of Travels** (Steam AppID
1152340, by Might & Delight AB), built from clean reverse-engineering of the
shipped client. It restores the game's online architecture after the official
servers were shut down: a master server for accounts/rooms, and the game
running as a dedicated server instance that friends connect to.

**This repository contains no game code or assets.** You must own Book of
Travels on Steam; the installer patches *your* local install (BYOG). The game
binary doubles as the server — that is how the game was designed — and remains
the property of its owners.

```
 PLAYERS (client mod, role=client)
    │  gRPC (login, accounts, room list)     Mirror TCP :50050 (world)
    ▼                                        ▼
 MASTER SERVER (this repo, .NET 8)      GAME INSTANCE (your Book of Travels,
    │  accounts · rooms · JWTs              headless, role=instance)
    ▼
 SQLite (accounts/rooms)                     │ MariaDB (characters, via MDY_DB_URL)
```

## Layout

| Path | What |
|---|---|
| `server/master/` | The master server (.NET 8, ASP.NET Core gRPC, SQLite) |
| `server/proto/` | The exact wire contract (`.proto` files) reconstructed from the client |
| `server/plugin/` | BepInEx plugin: client mod + headless-instance mod |
| `server/test/test_e2e.py` | End-to-end test against a running master |
| `runtime/bepinex/` | BepInEx 6 + Doorstop runtime (LGPL, see THIRD-PARTY-NOTICES.md) |
| `scripts/` | `setup-game.sh`, `start-master.sh`, `start-instance.sh` |
| `docs/` | `ARCHITECTURE.md`, `RUNBOOK.md` |

## Quickstart (Linux server, 5 commands)```bash
# 1. master server (requires .NET 8 SDK)
./scripts/start-master.sh                          # listens on :1234 (gRPC)

# 2. patch your game install (idempotent; Steam must own Book of Travels)
GAME="$HOME/.local/share/Steam/steamapps/common/Book of Travels"
./scripts/setup-game.sh client 127.0.0.1           # role=client | instance

# 3. MariaDB for the instance (Docker) + headless game server
./scripts/start-instance.sh 127.0.0.1:1234

# 4. verify the whole stack
python3 server/test/test_e2e.py 127.0.0.1:1234     # expect: ALL PASS

# 5. play — launch the game through the mod, log in with any email/password
#    (accounts are auto-provisioned by the master)
```

### Docker deployment (recommended for a VPS)

`deploy/` contains a full Docker stack — master + MariaDB + game instance +
Caddy (howto site and the gRPC `braid-connect` endpoint). The master advertises
a public hostname via `BOT_PUBLIC_HOST` so containerized instances are
reachable by clients. See `deploy/README.md`.

Client installers for friends (Windows + Linux) live in `release/`; the howto
site is `site/index.html`.

See `docs/RUNBOOK.md` for the full runbook and troubleshooting.

## What works

- Account creation/login via the master (email + password, JWT-authenticated)
- Room registry: headless instances register via `Instance.Ping` (up to 16 players/room)
- Character creation, world persistence in MariaDB, level travel, saves
- Multiple players per instance; multiple instances per master
- Auto-recovery: instances re-register if the master restarts

## Known limits

- Linux server only (the instance must run headless on Linux).
- Mirror/TCP gameplay traffic is unencrypted — use a trusted LAN or VPN.
- The world's level-randomizer layout is per-instance (as designed); server-side
  lazy level loading is planned to cut the instance's ~5.7 GB RAM footprint.

## License & legal

- This repository: **Apache-2.0** (`LICENSE`), third-party bits in
  `THIRD-PARTY-NOTICES.md`.
- Independent project: not affiliated with or endorsed by Might & Delight AB.
- You must own the game. Server-side modding may violate the game's EULA;
  running a private server for yourself and friends is your responsibility.
- No game code, assets, or credentials are included or referenced beyond the
  user-provided install.
