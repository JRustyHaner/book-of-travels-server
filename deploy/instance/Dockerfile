# Game instance — runtime container (BYOG: game folder bind-mounted at /game)
# The image contains no game bytes. The game itself (Book of Travels Linux
# build, with the BepInEx runtime + plugin installed by setup scripts) is
# provided by the host at ${GAME_DIR} and mounted read-write at /game.
FROM ubuntu:22.04

# Runtime libs the Unity Mono build links against (headless: no X needed)
RUN apt-get update && apt-get install -y --no-install-recommends \
        ca-certificates \
        file \
        libc6 libstdc++6 libgcc-s1 libdl.so.2 librt1 libm6 libpthread-stubs0-dev \
    && rm -rf /var/lib/apt/lists/*

# The instance writes logs into the game dir (BepInEx/LogOutput.log) and
# Player.log into $HOME/.config/unity3d/... — keep HOME writable.
ENV HOME=/home/bot
RUN mkdir -p /home/bot && chmod 777 /home/bot

WORKDIR /game
COPY entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh

# Mirror/TCP game port (published by compose); 7689 is bound internally too
# (the game's default Telepathy port) but not published.
EXPOSE 50050 7689

ENTRYPOINT ["/entrypoint.sh"]
