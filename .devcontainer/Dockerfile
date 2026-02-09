FROM mcr.microsoft.com/devcontainers/dotnet:1-8.0-noble

USER root

# 1) If the base image already includes the Yarn repo, disable it first so apt-get update works.
RUN rm -f /etc/apt/sources.list.d/yarn.list /etc/apt/sources.list.d/yarn*.list \
    && apt-get update \
    && apt-get install -y --no-install-recommends \
        ca-certificates \
        curl \
        gnupg \
    && rm -rf /var/lib/apt/lists/*

# 2) Add Yarn repo back with a modern signed-by keyring and install yarn
RUN set -eux; \
    curl -fsSL https://dl.yarnpkg.com/debian/pubkey.gpg \
      | gpg --dearmor -o /usr/share/keyrings/yarn.gpg; \
    echo "deb [signed-by=/usr/share/keyrings/yarn.gpg] https://dl.yarnpkg.com/debian stable main" \
      > /etc/apt/sources.list.d/yarn.list; \
    apt-get update; \
    apt-get install -y --no-install-recommends yarn; \
    rm -rf /var/lib/apt/lists/*