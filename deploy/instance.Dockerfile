# Runs the Book of Travels game as a headless instance inside a container.
# The game folder (with BepInEx + the plugin installed) is mounted at /game.
# NOTE: verify this image locally first — Unity's headless mode needs a few
# runtime libs; this set covers the standard Linux requirements.
FROM ubuntu:22.04

ENV DEBIAN_FRONTEND=noninteractive
RUN apt-get update && apt-get install -y --no-install-recommends \
        ca-certificates curl unzip file \
        libc6 libstdc++6 libgomp1 zlib1g \
        libgl1 libglx-mesa0 libx11-6 libxcursor1 libxrandr2 \
        libxi6 libxinerama1 libxss1 libasound2 libpulse0 \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /game
COPY deploy/instance-entrypoint.sh /entrypoint.sh
RUN chmod +x /entrypoint.sh

# 50050 = Mirror/TCP gameplay (the instance's world port)
EXPOSE 50050
ENTRYPOINT ["/entrypoint.sh"]
