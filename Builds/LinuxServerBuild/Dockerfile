FROM ubuntu:22.04

WORKDIR /app

RUN apt-get update \
    && DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends \
        ca-certificates \
        libasound2 \
        libatk-bridge2.0-0 \
        libatk1.0-0 \
        libc6 \
        libcairo2 \
        libdbus-1-3 \
        libexpat1 \
        libfontconfig1 \
        libfreetype6 \
        libgcc-s1 \
        libglib2.0-0 \
        libgl1 \
        libgtk-3-0 \
        libicu70 \
        libnss3 \
        libpango-1.0-0 \
        libpangocairo-1.0-0 \
        libpulse0 \
        libsm6 \
        libstdc++6 \
        libuuid1 \
        libx11-6 \
        libxcomposite1 \
        libxcursor1 \
        libxdamage1 \
        libxext6 \
        libxfixes3 \
        libxi6 \
        libxinerama1 \
        libxkbcommon0 \
        libxrandr2 \
        libxrender1 \
        libxss1 \
        libxtst6 \
        zlib1g \
    && rm -rf /var/lib/apt/lists/*

COPY . .

RUN set -eu; \
    found=0; \
    for candidate in /app/*.x86_64; do \
        if [ -f "$candidate" ]; then \
            chmod +x "$candidate"; \
            found=1; \
        fi; \
    done; \
    if [ "$found" -eq 0 ]; then \
        echo "No Unity server executable found in /app" >&2; \
        exit 1; \
    fi

ENV SEAWARS_PORT=7777

EXPOSE 7777/udp

STOPSIGNAL SIGINT

ENTRYPOINT ["/bin/sh", "-c", "set -eu; exe=$(ls -1t /app/*.x86_64 2>/dev/null | head -n 1 || true); if [ -z \"$exe\" ] || [ ! -x \"$exe\" ]; then echo 'No Unity server executable found' >&2; exit 1; fi; exec \"$exe\" \"$@\"", "unity-server"]
CMD ["-batchmode", "-nographics", "-mode", "server", "-logFile", "/dev/stdout"]
