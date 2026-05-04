FROM mcr.microsoft.com/dotnet/sdk:10.0

ENV DEBIAN_FRONTEND=noninteractive

RUN apt-get update \
 && apt-get install -y --no-install-recommends curl ca-certificates git ripgrep \
 && curl -fsSL https://deb.nodesource.com/setup_22.x | bash - \
 && apt-get install -y --no-install-recommends nodejs \
 && rm -rf /var/lib/apt/lists/*

RUN npm install -g @anthropic-ai/claude-code

RUN useradd -m -s /bin/bash dev \
 && mkdir -p /workspace /home/dev/.claude \
 && chown -R dev:dev /workspace /home/dev/.claude

USER dev
WORKDIR /workspace

ENV ASPNETCORE_URLS=http://+:8081
EXPOSE 8081

CMD ["claude"]
