#!/usr/bin/env bash
# deploy/backup.sh — sao lưu Postgres theo giai-doan-7.md Mục 6 (A2) và đẩy rời VM (A3).
#
# Hai chế độ:
#   backup.sh full   base backup + dump logic + kiểm archiver + hạn giữ + đẩy R2 + báo Kuma   — cron hằng ngày
#   backup.sh sync   chỉ đẩy thư mục backups/ lên R2 (để WAL mới rời VM trong 15 phút)         — cron mỗi 15 phút
#
# Mọi thao tác trên file sao lưu chạy BÊN TRONG container postgres (root trong container, host không cần
# sudo). Host chỉ điều phối. Chạy dưới user `deploy`. Log ra stdout — cron chuyển vào ~/app/deploy/backup.log.
#
# Biến môi trường (đều có mặc định):
#   COMPOSE_FILE  compose của stack cần sao lưu   (mặc định: docker-compose.staging.apache.yml cạnh script)
#   BACKUP_ENV    khóa R2 + URL push Kuma          (mặc định: backup.env cạnh script; thiếu → bỏ qua bước đẩy)
set -euo pipefail

DEPLOY_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
export COMPOSE_FILE="${COMPOSE_FILE:-$DEPLOY_DIR/docker-compose.staging.apache.yml}"
BACKUP_ENV="${BACKUP_ENV:-$DEPLOY_DIR/backup.env}"
BACKUP_DIR_HOST="$DEPLOY_DIR/backups"      # bind-mount vào /backups của container postgres

KEEP_DAILY_DAYS=7       # bản ngày giữ 7 ngày
KEEP_WEEKLY_DAYS=28     # bản Chủ nhật giữ 4 tuần (chỉ khôi phục tới đúng lúc chụp — Mục 6)
WAL_KEEP_DAYS=7         # cửa sổ PITR = 7 ngày
MIN_BASE_BYTES=100000   # base.tar.gz nhỏ hơn mức này là hỏng, không phải "DB nhỏ"

MODE="${1:-full}"
STAMP="$(date -u +%Y%m%dT%H%M%SZ)"
if [ "$(date -u +%u)" = "7" ]; then KIND=weekly; else KIND=daily; fi

log() { echo "$(date -u +%FT%TZ) [$MODE] $*"; }
pg()  { docker compose exec -T postgres "$@"; }
env_value() { { grep -E "^$1=" "$BACKUP_ENV" 2>/dev/null || true; } | head -1 | cut -d= -f2-; }

# full (03:00) và sync (*/15) có thể gặp nhau — không cho chạy chồng
exec 9>/tmp/socialmedia-backup.lock
flock -n 9 || { log "đang có lần chạy khác — bỏ qua"; exit 0; }

sync_to_r2() {
  if [ ! -f "$BACKUP_ENV" ]; then
    log "không có $BACKUP_ENV — BỎ QUA đẩy R2 (chỉ chấp nhận được lúc đang dựng; Đ-7.10)"
    return 0
  fi
  local bucket prefix
  bucket="$(env_value BACKUP_R2_BUCKET)"
  prefix="$(env_value BACKUP_PREFIX)"
  [ -n "$bucket" ] || { log "LỖI: BACKUP_R2_BUCKET trống trong $BACKUP_ENV"; return 1; }
  # Chốt an toàn: nguồn phải có ít nhất một base backup — `sync` với nguồn rỗng sẽ xóa sạch phía R2
  if [ -z "$(ls -A "$BACKUP_DIR_HOST/base" 2>/dev/null)" ]; then
    log "LỖI: $BACKUP_DIR_HOST/base rỗng — không sync để khỏi xóa R2"; return 1
  fi
  docker run --rm --env-file "$BACKUP_ENV" -v "$BACKUP_DIR_HOST:/data:ro" rclone/rclone:latest \
    sync /data "r2:${bucket}/${prefix:-staging}" \
    --max-delete 400 --s3-no-check-bucket --stats-one-line -v
  log "đã đồng bộ lên r2:${bucket}/${prefix:-staging}"
}

do_full() {
  local base="/backups/base/${KIND}-${STAMP}" dump="/backups/dump/${KIND}-${STAMP}.dump"
  log "bắt đầu $KIND-$STAMP"
  pg sh -c 'mkdir -p /backups/base /backups/dump /backups/wal && chown postgres:postgres /backups/wal'

  # 1. Base backup (vật lý) + dump (logic) — Đ-7.11
  pg pg_basebackup -U socialapp -D "$base" -Ft -z -X stream --checkpoint=fast --label "$KIND-$STAMP"
  pg pg_dump -U socialapp -Fc -f "$dump" socialapp

  # 2. Kích thước + mã băm — "file 0 byte" không được tính là thành công (Mục 6)
  local size
  size="$(pg stat -c %s "$base/base.tar.gz")"
  [ "$size" -ge "$MIN_BASE_BYTES" ] || { log "LỖI: base.tar.gz chỉ $size byte"; exit 1; }
  log "base.tar.gz $size byte · $(pg sh -c "cd $base && sha256sum base.tar.gz pg_wal.tar.gz backup_manifest | tr '\n' ' '")"
  log "dump $(pg stat -c %s "$dump") byte · $(pg sha256sum "$dump" | cut -d' ' -f1)"

  # 3. Archiver còn khỏe không — lần archive gần nhất mà thất bại thì WAL đang ứ trong pg_wal, đĩa sẽ đầy (A1 cạm bẫy)
  local arch
  arch="$(pg psql -U socialapp -d socialapp -Atc \
    "select archived_count, failed_count, coalesce(last_failed_time > coalesce(last_archived_time, '-infinity'), false) from pg_stat_archiver")"
  log "pg_stat_archiver archived|failed|đang-hỏng = $arch"
  case "$arch" in *"|t") log "LỖI: archive_command đang thất bại — xem docker compose logs postgres"; exit 1 ;; esac

  # 4. Hạn giữ — theo tên (daily-/weekly-) và tuổi
  pg sh -c "find /backups/base -mindepth 1 -maxdepth 1 -type d -name 'daily-*'  -mtime +$KEEP_DAILY_DAYS  -exec rm -rf {} +"
  pg sh -c "find /backups/base -mindepth 1 -maxdepth 1 -type d -name 'weekly-*' -mtime +$KEEP_WEEKLY_DAYS -exec rm -rf {} +"
  pg sh -c "find /backups/dump -type f -name 'daily-*'  -mtime +$KEEP_DAILY_DAYS  -delete"
  pg sh -c "find /backups/dump -type f -name 'weekly-*' -mtime +$KEEP_WEEKLY_DAYS -delete"
  pg sh -c "find /backups/wal  -type f -mtime +$WAL_KEEP_DAYS -delete"
  log "còn giữ: $(pg sh -c 'ls /backups/base | tr "\n" " "')"
  log "dung lượng /backups: $(pg du -sh /backups | cut -f1)"

  # 5. Rời VM + báo đồng hồ (Kuma push — cảnh báo "backup không chạy", Mục 5.3)
  sync_to_r2
  local push
  push="$(env_value BACKUP_KUMA_PUSH_URL)"
  if [ -n "$push" ]; then
    if curl -fsS -m 10 "${push}?status=up&msg=${KIND}-${STAMP}&ping=" >/dev/null; then
      log "đã báo Kuma"
    else
      log "CẢNH BÁO: không báo được Kuma (backup vẫn xong)"
    fi
  fi
}

case "$MODE" in
  full) do_full ;;
  sync) sync_to_r2 ;;
  *) echo "cách dùng: $0 [full|sync]" >&2; exit 2 ;;
esac
log "xong"
