# Runbook

Operational guide for the private server stack. Linux server assumed; the
game's Linux build is used as the headless instance.

## Prerequisites

- Book of Travels owned on Steam (Linux build installed)
- .NET 8 SDK (master server)
- Docker (MariaDB for the instance)
- Python 3 + `grpcio`/`grpcio-tools` (e2e test only)

## 1. Master server

```bash
./scripts/start-master.sh
# listens on 0.0.0.0:1234 (gRPC h2c) and :7689
# state: server/master/data/master.db (SQLite: accounts, rooms)
```

Environment: `BOT_MASTER_PORT` (default 1234), `BOT_MASTER_HOST` (default
0.0.0.0). Logs to stdout; business events logged as `instance ping: ...`,
`random server requested -> ...`, `authenticated ... -> account ...`.

## 2. Install the mod into your game

```bash
GAME="$HOME/.local/share/Steam/steamapps/common/Book of Travels"
./scripts/setup-game.sh client 127.0.0.1     # role: client | instance; 2nd arg: master host
```

Idempotent. Adds `BepInEx/`, `libdoorstop.so`, `run_bepinex.sh`, the plugin
DLL, and `BepInEx/config/dev.botmaster.plugin.cfg` to the game folder. To
uninstall, delete those items (Steam "Verify integrity" also restores).

> Plugin build note: the csproj resolves the game's assemblies via the
> `BOT_GAME_DIR` environment variable — `setup-game.sh` sets it for you. When
> building manually: `BOT_GAME_DIR="$GAME" dotnet build -c Release server/plugin`.

## 3. Game instance (dedicated server)

```bash
./scripts/start-instance.sh 127.0.0.1:1234
```

Creates/starts a MariaDB container (`bot-mariadb`, db/user/pass `bot`), waits
for it, then launches the game headless:

```bash
cd "$GAME"
MDY_DB_URL="Server=127.0.0.1;Port=3306;Database=bot;Uid=bot;Pwd=bot" \
MDY_INSTANCE_SERVER="127.0.0.1:1234" \
./run_bepinex.sh ./BookOfTravels.x86_64 -batchmode -nographics
```

With `role=instance` the plugin: forces remote mode + master host, restores
the DB connection, runs the shipped migrations, starts the Mirror server
(port 50050), and pings the master to register the room.

Watch: `grep 'Instance.Ping OK' <instance log>` and on the master
`instance ping: <ip>:50050 players=N -> 1 room(s) registered`.

## 4. Connect players

Each player machine: install the mod (`role=client`), set `masterHost` to the
master's IP, launch via `run_bepinex.sh` (see Troubleshooting), and log in
with any email/password — the master auto-provisions the account. The client
then gets a room from `GetRandomServer` and connects to the instance on
TCP 50050.

Multi-machine: the master advertises the IP the instance pings *from*, so put
the instance on a reachable host and open TCP 50050 (and 1234 for the master,
or front it with a TLS terminator).

## Troubleshooting

**Plugin not loading** — `BepInEx/LogOutput.log` shows
`Skipping [BotMaster Plugin] because of process filters`. The
`[BepInProcess]` value must equal the game executable's basename *without* its
`.x86_64` extension (BepInEx compares against `Path.GetFileNameWithoutExtension`),
i.e. `"BookOfTravels"`.

**"DNS resolution failed" / game freeze on login** — the plugin reads
`masterHost` from the BepInEx config; if the value is stored with quotes
(`masterHost = "127.0.0.1"`), the channel target becomes
`"127.0.0.1":1234` (with quote chars) and `getaddrinfo` fails. The plugin
trims quotes; if you see this in an older build, re-run `setup-game.sh`.

**Launching the client** — Doorstop activates via LD_PRELOAD only, so launch
through `run_bepinex.sh` (not the Steam play button):

```bash
cd "$GAME" && DISPLAY=:0 ./run_bepinex.sh ./BookOfTravels.x86_64 \
  -screen-fullscreen 0 -screen-width 1280 -screen-height 720
```

Add `steam_appid.txt` (`1152340`) to the game folder so Steamworks initializes
when launched outside Steam.

**Vulkan crash on startup (fullscreen)** — the game can crash creating a
fullscreen swapchain on some compositors; use the windowed launch args above.

**"token validation failed"** — the instance validates JWTs against the key
from `Instance.Ping`; make sure the instance is registering (master log shows
`instance ping` lines) before clients log in.

**Master restarts** — gameplay runs on the instance, not the master; a master
restart only interrupts login. Instances re-register within ~5s.

## Public host advertisement

In Docker/NAT setups the instance pings from an unroutable container IP. Set
`BOT_PUBLIC_HOST` on the master (compose passes it automatically) so rooms are
advertised as a hostname clients can actually reach:

```bash
BOT_PUBLIC_HOST=braid-connect.example.com ./scripts/start-master.sh
# master log: instance ping: 172.20.0.5:50050 players=N -> advertised braid-connect.example.com:50050
```

## Docker

`deploy/docker-compose.yml` runs master + MariaDB + instance + Caddy; see
`deploy/README.md` for the one-time game install and bring-up steps.

## Tests

```bash
python3 server/test/test_e2e.py 127.0.0.1:1234    # ALL PASS expected
```

Requires the generated protobuf stubs (`grpc_tools.protoc` against
`server/proto/*.proto`). Covers: auth, JWT signature, room registration,
random-server assignment, region/news, admin view, account persistence.

## Invites & registration (accounts are invite-only)

- `POST /admin/invite` (Bearer `BRAID_ADMIN_TOKEN`, env) → `{"code":"XXXXXX"}`.
  Optional body: `{"email":"friend@x", "reusable":true}` (email-bound and/or reusable).
- `POST /register` (public, rate-limited) body `{"email","password","invite"}` creates
  the account; then the player logs in in-game with that email + password.
- `Authenticate` no longer auto-provisions: unknown emails are rejected.
- The game's `RegisterAccount` gRPC is invite-gated too (the invite code goes in the
  email-token field).
- Rate limiting: 60 requests / 10 min per IP on auth endpoints.
- The braid site has a registration form; on a host with an existing Caddy, proxy
  `/api/*` → the master REST (see `deploy/braid-caddy-block.txt`).
- **Note for the Caddy host**: the running config is autosaved in the container's
  `/config` volume — after editing the Caddyfile, `caddy reload` can silently keep the
  old config; delete `/root/data/caddy_config/caddy_config/caddy/autosave.json` and
  restart the container to force a fresh load.
