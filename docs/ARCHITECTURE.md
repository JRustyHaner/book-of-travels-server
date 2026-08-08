# Architecture

How the emulated online stack works, reconstructed from the shipped client
(Unity 2022.3 LTS, Mono backend, "Years" framework by Might & Delight). The
game binary is both client and server: the same executable runs as a normal
client, a host, or a headless dedicated instance.

## Components

| Component | Runs as | Role |
|---|---|---|
| **Master server** | `server/master` (.NET 8, gRPC) | Accounts, room registry, JWT signing, server assignment |
| **Game instance** | The game, `-batchmode -nographics`, role=instance | Authoritative world simulation (Mirror), per-account characters |
| **Client** | The game, role=client | Player front-end; authenticates via master, connects to an instance |
| **MariaDB** | Docker (`bot-mariadb`) | Instance-side persistence (characters, inventory, quests, ...) |

## Wire contract

`server/proto/*.proto` is the exact gRPC contract the client speaks (verified
against the client's embedded descriptors):

- **Game** (client → master): `Authenticate`, `RegisterAccount`,
  `ChangeAccountPassword/Email`, `VerifyEmailToken`, `RedeemAccountStatus`,
  `GetRegionList`, `GetRoomList`, `GetRandomServer`,
  `GetRandomRegionServer`, `GetNews`, `GetPatchNotes`, `SendFeedbackEmail*`.
- **Instance** (game server → master): `Ping(room_port, player_count)` →
  pong with `next_ping_ms`, `status`, `max_player_count`, `security_key`
  (the JWT signing key), `time_zone_offset`.
- **Master** (bootstrap): `GetConfig` — hands instances the shared signing key.
- **Admin** (fleet view): `GetAllServers`, version/region management —
  minimal implementation, kept for the client's expectation surface.

## Login flow

1. Client → `Game.Authenticate(email, password, service_id)` → master issues a
   JWT (HS256, `iss=MasterServer`, `aud=ServerInstance`, `uid=<account id>`).
2. Client → `Game.GetRandomServer()` → master returns the host of a registered
   room (the IP the instance pings from).
3. Client connects to the instance over Mirror/TCP (Telepathy, port 50050) and
   sends `LoginMsg{token=JWT, version}`.
4. Instance validates the JWT against the signing key it received from
   `Instance.Ping`, then serves character select/create (saved to MariaDB).

## Instance lifecycle

- `OnStartServer` loads the world (all bundled levels), initializes the
  database from `MDY_DB_URL` (running the migrations shipped in
  `StreamingAssets/Migrations` via Evolve), and opens the Mirror server.
- Every ~5s the instance pings the master (`Instance.Ping`); the master records
  the room (peer IP + port + player count) and refreshes the JWT signing key.
- Character data is saved periodically (default every 60s) to MariaDB.

## Client mod (`server/plugin`)

The shipped build has several broken/disabled paths that the plugin repairs
with Harmony patches (all documented in the source):

- `Database.GetConnection` returns `null` in the shipped build (remote DB was
  stubbed); the plugin restores a real MySQL connection from `MDY_DB_URL`.
- `_tokenValidationParameters` is never initialized; the plugin creates it and
  the instance installs the signing key from each pong.
- Master host/port defaults are localhost; the plugin points them at the
  configured master (config in `BepInEx/config/dev.botmaster.plugin.cfg`).
- The shipped `StartInstanceConnection` mis-parses `host:port` (a colon bug)
  and always pings port 7689; the plugin reimplements the ping loop with the
  configured master address.

## Persistence

- Master: SQLite (`server/master/data/master.db`) — accounts, rooms.
- Instance: MariaDB — the full game schema (characters, character secondary
  data, inventory, pockets, equipment, strongbox, skills, reagents, quests,
  flags, diary, map notes/levels, effects, analytics, event balancing).
  Schema shape is described in `docs/RUNBOOK.md`; the shipped migration SQL is
  game-proprietary and is **not** reproduced here (the installer runs the
  migrations from the user's own game files).

## Security notes

- gRPC master traffic is plaintext h2c on a private network (matching the
  client's insecure-channel default when SSL is disabled). Keep the master on
  a trusted network or front it with a TLS terminator.
- Mirror/TCP gameplay traffic is unencrypted — trusted LAN or VPN only.
- No game credentials are used: accounts are master-local, auto-provisioned.
