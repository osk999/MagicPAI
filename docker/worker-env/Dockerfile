# MagicPAI Worker Environment
# Debian bookworm with all tools needed for AI agent execution.
FROM debian:bookworm

ENV DEBIAN_FRONTEND=noninteractive

# Base utilities + GUI stack
RUN apt-get update \
  && apt-get install -y --no-install-recommends \
    ca-certificates curl git gnupg unzip xz-utils \
    build-essential cmake python3 python3-pip \
    xvfb fluxbox x11vnc novnc websockify \
    fonts-liberation netcat-openbsd sudo jq ripgrep \
  && rm -rf /var/lib/apt/lists/*

# Node.js 24 LTS
RUN curl -fsSL https://deb.nodesource.com/setup_24.x | bash - \
  && apt-get install -y --no-install-recommends nodejs \
  && rm -rf /var/lib/apt/lists/*

# .NET SDK 10.0 + 9.0 + 8.0
RUN curl -fsSL https://dot.net/v1/dotnet-install.sh -o dotnet-install.sh \
  && chmod +x dotnet-install.sh \
  && ./dotnet-install.sh --channel 10.0 --install-dir /usr/share/dotnet \
  && ./dotnet-install.sh --channel 9.0 --install-dir /usr/share/dotnet \
  && ./dotnet-install.sh --channel 8.0 --install-dir /usr/share/dotnet \
  && rm dotnet-install.sh \
  && ln -s /usr/share/dotnet/dotnet /usr/bin/dotnet
ENV DOTNET_ROOT=/usr/share/dotnet
ENV PATH="${PATH}:/usr/share/dotnet"

# Go
RUN curl -fsSL https://go.dev/dl/go1.24.2.linux-amd64.tar.gz | tar -C /usr/local -xzf -
ENV PATH="${PATH}:/usr/local/go/bin"

# Playwright + Chromium (pre-installed, no runtime download, headed mode for noVNC)
ENV PLAYWRIGHT_BROWSERS_PATH=/ms-playwright
ENV PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1
ENV PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH=/ms-playwright/chromium-1217/chrome-linux64/chrome
ENV PLAYWRIGHT_CHROMIUM_HEADLESS=0
ENV PLAYWRIGHT_MCP_HEADLESS=false
ENV HEADED=1
RUN mkdir -p /opt/playwright && cd /opt/playwright \
  && npm init -y && npm install playwright \
  && npx playwright install --with-deps chromium

# Docker CLI (sibling container management)
RUN install -m 0755 -d /etc/apt/keyrings \
  && curl -fsSL https://download.docker.com/linux/debian/gpg \
     -o /etc/apt/keyrings/docker.asc \
  && echo "deb [arch=$(dpkg --print-architecture) \
     signed-by=/etc/apt/keyrings/docker.asc] \
     https://download.docker.com/linux/debian bookworm stable" \
     > /etc/apt/sources.list.d/docker.list \
  && apt-get update \
  && apt-get install -y --no-install-recommends docker-ce-cli \
  && rm -rf /var/lib/apt/lists/*

# AI CLI Agents
# Claude Code
RUN npm install -g @anthropic-ai/claude-code @playwright/mcp

# OpenAI Codex CLI
RUN npm install -g @openai/codex \
  && codex --version

# Google Gemini CLI (may not be available yet)
RUN pip3 install --no-cache-dir google-genai --break-system-packages 2>/dev/null \
    || echo "Gemini CLI not yet available"

# Rust toolchain
RUN curl --proto '=https' --tlsv1.2 -sSf https://sh.rustup.rs \
  | sh -s -- -y --default-toolchain stable
ENV PATH="/root/.cargo/bin:${PATH}"

# Non-root user
RUN useradd -m -s /bin/bash worker \
  && echo "worker ALL=(ALL) NOPASSWD:ALL" > /etc/sudoers.d/worker \
  && mkdir -p /workspace && chown worker:worker /workspace

USER worker
WORKDIR /workspace

ENV DISPLAY=:99
EXPOSE 7900

COPY entrypoint.sh /entrypoint.sh
USER root
RUN chmod +x /entrypoint.sh
USER worker

ENTRYPOINT ["/entrypoint.sh"]
