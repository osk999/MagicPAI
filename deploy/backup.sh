#!/bin/bash
# deploy/backup.sh — nightly backup of both Postgres databases. See temporal.md §18.5.
set -euo pipefail

DATE=$(date +%F)
BACKUP_ROOT=${BACKUP_ROOT:-/backups}
BACKUP_DIR=$BACKUP_ROOT/$DATE
RETENTION_DAYS=${RETENTION_DAYS:-14}

mkdir -p "$BACKUP_DIR"

echo "=== MagicPAI backup $DATE ==="

echo "Dumping temporal..."
docker exec mpai-temporal-db pg_dump -U temporal temporal \
    | gzip > "$BACKUP_DIR/temporal-$DATE.sql.gz"
ls -lh "$BACKUP_DIR/temporal-$DATE.sql.gz"

echo "Dumping magicpai..."
docker exec mpai-db pg_dump -U magicpai magicpai \
    | gzip > "$BACKUP_DIR/magicpai-$DATE.sql.gz"
ls -lh "$BACKUP_DIR/magicpai-$DATE.sql.gz"

# Retention: delete backups older than $RETENTION_DAYS days
find "$BACKUP_ROOT" -type d -mtime +$RETENTION_DAYS -exec rm -rf {} + 2>/dev/null || true

# Optional S3 upload
if [ -n "${S3_BUCKET:-}" ]; then
    echo "Uploading to s3://$S3_BUCKET/..."
    aws s3 sync "$BACKUP_ROOT" "s3://$S3_BUCKET/"
fi

echo "Backup complete: $BACKUP_DIR"
