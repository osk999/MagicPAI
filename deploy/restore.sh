#!/bin/bash
# deploy/restore.sh — restore both Postgres databases from a backup date.
# See temporal.md §18.5 and §LL.3.
set -euo pipefail

if [ $# -lt 1 ]; then
    echo "Usage: $0 <date YYYY-MM-DD> [backup-root]"
    echo "  Example: $0 2026-04-20"
    exit 1
fi

DATE=$1
BACKUP_ROOT=${2:-/backups}
BACKUP_DIR=$BACKUP_ROOT/$DATE

if [ ! -d "$BACKUP_DIR" ]; then
    echo "ERROR: Backup directory not found: $BACKUP_DIR"
    exit 1
fi

echo "=== MagicPAI restore from $DATE ==="
echo "⚠️  This will DROP and RECREATE both databases."
read -p "Continue? (yes/no) " -r
if [ "$REPLY" != "yes" ]; then
    echo "Aborted."
    exit 0
fi

# Stop Temporal (restore requires no writes)
echo "Stopping temporal..."
docker compose -f docker/docker-compose.temporal.yml stop temporal

# Restore temporal DB
echo "Restoring temporal database..."
docker exec mpai-temporal-db psql -U temporal -c "DROP DATABASE IF EXISTS temporal;"
docker exec mpai-temporal-db psql -U temporal -c "CREATE DATABASE temporal;"
gunzip -c "$BACKUP_DIR/temporal-$DATE.sql.gz" \
    | docker exec -i mpai-temporal-db psql -U temporal temporal

# Restore MagicPAI DB
echo "Restoring magicpai database..."
docker exec mpai-db psql -U magicpai -c "DROP DATABASE IF EXISTS magicpai;"
docker exec mpai-db psql -U magicpai -c "CREATE DATABASE magicpai;"
gunzip -c "$BACKUP_DIR/magicpai-$DATE.sql.gz" \
    | docker exec -i mpai-db psql -U magicpai magicpai

# Restart Temporal
echo "Starting temporal..."
docker compose -f docker/docker-compose.temporal.yml up -d temporal

# Wait for health
until docker exec mpai-temporal temporal operator cluster health 2>/dev/null; do
    echo "  Waiting for temporal..."
    sleep 2
done

echo "Restore complete."
