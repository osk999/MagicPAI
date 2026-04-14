#!/bin/bash
set -e

sync_credential_file() {
    local src="$1"
    local dest="$2"

    if [ ! -f "$src" ]; then
        return
    fi

    mkdir -p "$(dirname "$dest")"
    cp "$src" "$dest"
    chmod 600 "$dest"
}

export HOME=/home/worker
export XDG_CONFIG_HOME=/home/worker/.config

# Playwright: use pre-installed Chromium, never download, headed mode for noVNC
export PLAYWRIGHT_BROWSERS_PATH=/ms-playwright
export PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1
export PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH=/ms-playwright/chromium-1217/chrome-linux64/chrome
export PLAYWRIGHT_CHROMIUM_HEADLESS=0
export PLAYWRIGHT_MCP_HEADLESS=false
export HEADED=1

mkdir -p "$HOME/.config" "$HOME/.codex" "$HOME/.claude"

# Symlink pre-installed browsers to user cache (fallback for npx)
if [ -d "/ms-playwright" ] && [ ! -e "$HOME/.cache/ms-playwright" ]; then
    mkdir -p "$HOME/.cache"
    ln -sf /ms-playwright "$HOME/.cache/ms-playwright"
fi

# Persist env vars for docker exec sessions (login shells from Claude/Codex CLI)
{
  echo "export DISPLAY=:99"
  echo "export PLAYWRIGHT_BROWSERS_PATH=/ms-playwright"
  echo "export PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD=1"
  echo "export PLAYWRIGHT_CHROMIUM_EXECUTABLE_PATH=/ms-playwright/chromium-1217/chrome-linux64/chrome"
  echo "export PLAYWRIGHT_CHROMIUM_HEADLESS=0"
  echo "export PLAYWRIGHT_MCP_HEADLESS=false"
  echo "export HEADED=1"
} >> "$HOME/.bashrc"

# === MCP Config Injection (MagicPrompt pattern) ===
# Claude Code reads MCP servers from TWO places:
#   1. ~/.claude.json (project-scoped: projects["/workspace"]["mcpServers"])
#   2. /workspace/.mcp.json (workspace-level)
# Both must be set, and hasTrustDialogAccepted must be true.

# 1. Project-scoped MCP config in ~/.claude.json
cat > "$HOME/.claude.json" <<'CLAUDEJSON_EOF'
{
  "projects": {
    "/workspace": {
      "mcpServers": {
        "playwright": {
          "command": "npx",
          "args": ["@playwright/mcp@latest", "--no-sandbox", "--output-dir", "/workspace/screenshots"],
          "env": {
            "DISPLAY": ":99",
            "PLAYWRIGHT_MCP_HEADLESS": "false",
            "PLAYWRIGHT_BROWSERS_PATH": "/ms-playwright",
            "PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD": "1"
          }
        }
      },
      "allowedTools": ["mcp__playwright__*"],
      "hasTrustDialogAccepted": true
    }
  }
}
CLAUDEJSON_EOF
chmod 644 "$HOME/.claude.json"

# 2. Workspace-level .mcp.json
cat > /workspace/.mcp.json <<'MCPJSON_EOF'
{
  "mcpServers": {
    "playwright": {
      "command": "npx",
      "args": ["@playwright/mcp@latest", "--no-sandbox", "--output-dir", "/workspace/screenshots"],
      "env": {
        "DISPLAY": ":99",
        "PLAYWRIGHT_MCP_HEADLESS": "false",
        "PLAYWRIGHT_BROWSERS_PATH": "/ms-playwright",
        "PLAYWRIGHT_SKIP_BROWSER_DOWNLOAD": "1"
      }
    }
  }
}
MCPJSON_EOF
chmod 644 /workspace/.mcp.json

# 3. User-level settings.json with permissions
cat > "$HOME/.claude/settings.json" <<'SETTINGS_EOF'
{
  "permissions": {
    "allow": ["Bash(*)", "Read(*)", "Write(*)", "Edit(*)", "Glob(*)", "Grep(*)", "mcp__playwright__*"],
    "deny": []
  }
}
SETTINGS_EOF
chmod 644 "$HOME/.claude/settings.json"

sync_credential_file /tmp/magicpai-host-claude.json "$HOME/.claude.json"
sync_credential_file /tmp/magicpai-host-claude-credentials.json "$HOME/.claude/.credentials.json"
sync_credential_file /tmp/magicpai-host-codex-auth.json "$HOME/.codex/auth.json"
sync_credential_file /tmp/magicpai-host-codex-cap-sid "$HOME/.codex/cap_sid"

# Configure Docker socket permissions if mounted
if [ -S /var/run/docker.sock ]; then
    DOCKER_GID=$(stat -c '%g' /var/run/docker.sock)
    if ! getent group "$DOCKER_GID" > /dev/null 2>&1; then
        sudo groupadd -g "$DOCKER_GID" docker-host
    fi
    DOCKER_GROUP=$(getent group "$DOCKER_GID" | cut -d: -f1)
    sudo usermod -aG "$DOCKER_GROUP" worker
fi

# Start Xvfb (virtual X server)
Xvfb :99 -screen 0 1280x1024x24 -ac +extension GLX +render -noreset &
XVFB_PID=$!
sleep 1

# Start fluxbox window manager
fluxbox &
FLUXBOX_PID=$!
sleep 0.5

# Start x11vnc
x11vnc -display :99 -forever -nopw -shared -rfbport 5900 &
VNC_PID=$!

# Start noVNC (websocket proxy for browser-based VNC)
/usr/share/novnc/utils/novnc_proxy --vnc localhost:5900 --listen 7900 &
NOVNC_PID=$!

echo "MagicPAI worker environment ready."
echo "  VNC: port 5900"
echo "  noVNC: http://localhost:7900/vnc.html"
echo "  Playwright MCP: headed mode on DISPLAY=:99"

# If a command was provided, execute it
if [ $# -gt 0 ]; then
    exec "$@"
else
    # Keep running until stopped
    wait $XVFB_PID
fi
