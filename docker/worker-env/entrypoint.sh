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

mkdir -p "$HOME/.config" "$HOME/.codex" "$HOME/.claude"

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

# If a command was provided, execute it
if [ $# -gt 0 ]; then
    exec "$@"
else
    # Keep running until stopped
    wait $XVFB_PID
fi
