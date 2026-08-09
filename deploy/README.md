# Braid deployment (Docker, Ionos VPS)

Stack: master (gRPC) + mariadb + game instance + caddy (site + gRPC proxy).

## Prereqs on the VPS

- Docker + compose plugin
- A Book of Travels install rsynced to the host (see below)
- DNS: `braid.flightlessbirdlabs.io` → VPS IP, `braid-connect.flightlessbirdlabs.io` → VPS IP
- Firewall open: `80`, `443` (caddy), `1234` (master via caddy h2c), `50050` (game)

## Install the game on the VPS (one-time)

```bash
# from your workstation:
rsync -avP "$HOME/.local/share/Steam/steamapps/common/Book of Travels/" \
      ionos-vps:/srv/braid/game/

# on the VPS: patch the game for the instance role (adds BepInEx + plugin)
ssh ionos-vps 'cd /srv/braid/game && ./run_bepinex.sh 2>/dev/null || true'
git clone https://github.com/JRustyHaner/book-of-travels-server /srv/braid/bot-server
cd /srv/braid/bot-server && BOT_GAME_DIR=/srv/braid/game bash scripts/setup-game.sh instance braid-connect.flightlessbirdlabs.io
```

> The game needs `steam_appid.txt` (1152340) in the folder — `setup-game.sh`
> writes nothing there; add it: `echo 1152340 > /srv/braid/game/steam_appid.txt`.

## Bring it up

```bash
cd /srv/braid/bot-server/deploy
cp .env.example .env && $EDITOR .env     # BRAID_GAME_DIR, DB passwords
docker compose up -d --build
```

## Verify

```bash
docker compose logs -f master        # expect: instance ping: <ip>:50050 players=N -> advertised braid-connect...:50050
docker compose ps                    # all healthy
```

Then run the e2e against the public endpoint:

```bash
python3 server/test/test_e2e.py braid-connect.flightlessbirdlabs.io:1234   # ALL PASS
```

## Client install

Point players at https://braid.flightlessbirdlabs.io — Windows and Linux
install scripts fetch the latest client bundle from the GitHub release
(`release/build-bundle.sh --release <ver>` publishes it).

## Notes / gotchas

- **Instance image unverified**: the `instance.Dockerfile` lib set is the
  standard Unity-headless set, but test it locally first (`docker compose up
  instance` and watch for missing-library errors). SteamAPI_Init fails inside
  the container (no Steam client) — the instance path doesn't gate on it, but
  verify once.
- **Plaintext**: master gRPC (h2c) and Mirror/TCP are unencrypted by design
  (the game client uses insecure channels). braid-connect:1234 is effectively
  open — fine for friends, not for strangers.
- Master data: `master-data` volume; characters: `mariadb-data` volume. Back
  both up.
- BOT_PUBLIC_HOST: the master advertises this hostname (instead of the
  container IP) as the game server address clients connect to on :50050.

## Existing Caddy host (this VPS)

The Ionos box already ran Caddy on :80/:443. Braid integrates with it instead
of running a second Caddy:

- **Site**: the howto page is served by the existing Caddy — copy `../site/`
  to `/root/assets/braid-site/`, add the `braid.flightlessbirdlabs.io` block
  (root `* /assets/braid-site`) to `/root/assets/Caddyfile`, then
  `docker exec caddy caddy reload --config /etc/caddy/Caddyfile`.
- **gRPC**: no proxy — `docker-compose.ionos.yml` publishes the master on
  `:1234` directly; clients reach `braid-connect.flightlessbirdlabs.io:1234`.
- Open firewall ports: `80`, `443`, `1234`, `50050`.
