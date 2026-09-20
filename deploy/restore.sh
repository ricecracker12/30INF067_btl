#!/usr/bin/env bash
# deploy/restore.sh — khôi phục một base backup vào stack RIÊNG `socialapp-restore`
# (docker-compose.restore.yml, cổng 127.0.0.1:15432). KHÔNG chạm stack đang chạy — dùng cho:
#   - A5 restore drill (giai-doan-7.md Mục 7), và
#   - sự cố thật: khôi phục ra cạnh, đối chiếu, rồi mới quyết định thay dữ liệu (runbook-khoi-phuc.md).
#
#   restore.sh <tên-bản-sao> [recovery_target_time]
#   restore.sh daily-20260920T200000Z
#   restore.sh daily-20260920T200000Z "2026-09-21 09:30:00+07"      # PITR: tua tới đúng thời điểm này
#
# Bản sao lấy từ ./backups/base/<tên>/ (đã có sẵn trên VM, hoặc vừa rclone copy từ R2 về — xem runbook).
set -euo pipefail

DEPLOY_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
export COMPOSE_FILE="$DEPLOY_DIR/docker-compose.restore.yml"
NAME="${1:?cần tên bản sao, ví dụ daily-20260920T200000Z  (xem: ls backups/base)}"
TARGET="${2:-}"

log() { echo "$(date -u +%FT%TZ) $*"; }
[ -d "$DEPLOY_DIR/backups/base/$NAME" ] || { echo "không thấy backups/base/$NAME" >&2; exit 1; }

log "xóa stack restore cũ (nếu có) — volume pgdata-restore về trống"
docker compose down -v --remove-orphans >/dev/null 2>&1 || true

log "nạp $NAME vào volume trống${TARGET:+ (mục tiêu: $TARGET)}"
docker compose run --rm -T --no-deps --entrypoint bash \
  -e BASE="/backups/base/$NAME" -e TARGET="$TARGET" postgres -s <<'EOF'
set -e
cd "$PGDATA"
tar xzf "$BASE/base.tar.gz"
mkdir -p pg_wal
tar xzf "$BASE/pg_wal.tar.gz" -C pg_wal
touch recovery.signal
{
  echo "restore_command = 'cp /backups/wal/%f %p'"
  echo "recovery_target_action = 'promote'"
  if [ -n "$TARGET" ]; then echo "recovery_target_time = '$TARGET'"; fi
} >> postgresql.auto.conf
chown -R postgres:postgres "$PGDATA"
chmod 700 "$PGDATA"
EOF

log "khởi động — Postgres tua WAL từ /backups/wal rồi tự promote"
docker compose up -d --wait --wait-timeout 300

for _ in $(seq 1 60); do
  state="$(docker compose exec -T postgres psql -U socialapp -d socialapp -Atc 'select pg_is_in_recovery()' 2>/dev/null || echo t)"
  [ "$state" = "f" ] && break
  sleep 5
done
[ "${state:-t}" = "f" ] || { log "LỖI: sau 5 phút vẫn đang recovery — xem docker compose logs postgres"; exit 1; }

log "đã promote. Số bản ghi từng bảng:"
docker compose exec -T postgres psql -U socialapp -d socialapp -At -f - < "$DEPLOY_DIR/dem-ban-ghi.sql"

log "xong. Kết nối: psql -h 127.0.0.1 -p 15432 -U socialapp socialapp (mật khẩu = của DB gốc, vì dữ liệu là bản sao)"
log "sau khi đối chiếu xong: docker compose -f $COMPOSE_FILE down -v"
