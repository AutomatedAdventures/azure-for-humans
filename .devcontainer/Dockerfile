FROM mcr.microsoft.com/devcontainers/dotnet:1-8.0-noble

USER root

RUN apt-get update && apt-get install -y --no-install-recommends \
    curl \
    ca-certificates \
    gnupg2 \
  && rm -rf /var/lib/apt/lists/*

RUN curl -fsSL https://dl.yarnpkg.com/debian/pubkey.gpg | gpg --dearmor -o /usr/share/keyrings/yarnkey.gpg \
  && echo "deb [signed-by=/usr/share/keyrings/yarnkey.gpg] https://dl.yarnpkg.com/debian/ stable main" > /etc/apt/sources.list.d/yarn.list \
  && apt-get update \
  && apt-get install -y --no-install-recommends \
       curl \
       ca-certificates \
       gnupg2 \
       yarn \
  && rm -rf /var/lib/apt/lists/*