# Runbook — Khôi phục dữ liệu PostgreSQL (GĐ7 khối A, A4)

> Đọc khi: **mất dữ liệu**, **mất VM**, hoặc **đang diễn tập A5**. Viết để một người khác trong nhóm — chưa từng
> làm — cầm vào làm được mà không phải hỏi. Nếu phải hỏi, sửa runbook này ngay sau đó.
>
> Cơ chế đằng sau: [giai-doan-7.md](giai-doan-7.md) Mục 6 (chính sách), Đ-7.10, Đ-7.11. Script:
> [`deploy/backup.sh`](../../deploy/backup.sh), [`deploy/restore.sh`](../../deploy/restore.sh),
> [`deploy/docker-compose.restore.yml`](../../deploy/docker-compose.restore.yml).

## 0. Ba luật trước khi gõ bất cứ thứ gì

1. **Khôi phục ra cạnh, không đè lên.** `restore.sh` luôn dựng vào stack riêng `socialapp-restore` (cổng
   `127.0.0.1:15432`, volume `pgdata-restore`). Chỉ khi đã đối chiếu xong mới quyết định thay dữ liệu (Mục 4).
2. **Cùng image.** `postgres:16` (Debian). Không bao giờ `-alpine` — collation khác, index text hỏng ngầm.
3. **Ghi giờ từng bước.** Dù là sự cố thật hay diễn tập — không có mốc giờ thì không có RTO, và không có RTO thì
   không biết lần sau có kịp không.

## 1. Bản sao nằm ở đâu

| Nơi | Đường dẫn | Ai có | Khi nào dùng |
|---|---|---|---|
| **Nóng** — trên VM | `~/app/deploy/backups/{base,wal,dump}/` (user `deploy`) | Ai có SSH | Xóa nhầm, migration hỏng, VM còn sống |
| **Nguội** — R2 | bucket `socialmedia-backup`, tiền tố `staging/` (sau D4: `production/`) | Token trong `deploy/backup.env` trên VM + bản trong kho bí mật nhóm | **Mất VM** |
| Log | `~/app/deploy/backup.log` — mỗi bản: tên, kích thước, sha256, trạng thái archiver | | Chọn bản, đối chiếu hash |

Tên bản sao: `daily-20260920T200000Z` / `weekly-…` (UTC). Bản `weekly` chỉ khôi phục được **tới đúng lúc chụp**
(WAL chỉ giữ 7 ngày); bản `daily` tua tiếp được bằng WAL.

Chọn bản: `ls ~/app/deploy/backups/base/` → lấy **daily mới nhất trước thời điểm sự cố** (không lấy bản sau sự
cố — nó đã chứa dữ liệu hỏng).

## 2. Kịch bản A — VM còn sống: xóa nhầm / migration hỏng / cần xem dữ liệu lúc X

```bash
sudo -iu deploy
cd ~/app/deploy
ls backups/base/                                   # chọn bản
grep 'daily-2026…' backup.log                      # đối chiếu sha256 nếu nghi ngờ file

# 1. Đếm số bản ghi DB đang chạy (để đối chiếu ở bước 4) — ghi giờ
docker compose -f docker-compose.staging.apache.yml exec -T postgres \
  psql -U socialapp -d socialapp -At -f - < dem-ban-ghi.sql

# 2. Khôi phục ra cạnh — tới cuối WAL có sẵn…
./restore.sh daily-20260920T200000Z
# …hoặc tới đúng một thời điểm (PITR) — trước lúc xóa nhầm:
./restore.sh daily-20260920T200000Z "2026-09-21 09:29:00+07"

# 3. Kiểm migration không có gì để áp (schema khớp code đang chạy)
docker compose -f docker-compose.staging.apache.yml run --rm \
  -e ConnectionStrings__Postgres="Host=host.docker.internal;Port=15432;Database=socialapp;Username=socialapp;Password=<mật khẩu DB gốc>" \
  --add-host host.docker.internal:host-gateway migrate

# 4. Đối chiếu — restore.sh đã in bảng đếm; so với bước 1
# 5. Lấy thứ cần (một bảng, vài dòng) ra bằng psql -h 127.0.0.1 -p 15432, hoặc đi tiếp Mục 4 nếu phải thay cả DB
# 6. Dọn
docker compose -f docker-compose.restore.yml down -v
```

## 3. Kịch bản B — Mất VM: dựng lại từ R2 trên máy mới

Tiền đề trên máy mới: Docker + user `deploy` + clone repo vào `~/app` (theo `docs/oci-setup.md` Mục iii), và
`deploy/backup.env` với token R2 lấy từ kho bí mật nhóm.

```bash
sudo -iu deploy
cd ~/app/deploy
mkdir -p backups

# 1. Kéo toàn bộ bản sao về — ghi giờ bắt đầu/kết thúc, đây là bước tốn thời gian nhất
docker run --rm --env-file backup.env -v "$PWD/backups:/data" rclone/rclone:latest \
  copy "r2:socialmedia-backup/staging" /data --stats-one-line -v
ls backups/base/ backups/wal/ | head

# 2. Khôi phục ra cạnh và đối chiếu (giống Kịch bản A, bước 2–4)
./restore.sh daily-20260920T200000Z

# 3. Đưa vào phục vụ: Mục 4
```

## 4. Đưa dữ liệu khôi phục vào stack đang chạy

Chỉ làm sau khi Mục 2/3 đã đối chiếu xong và cả nhóm đồng ý. **Có dừng dịch vụ.**

```bash
cd ~/app/deploy
S=docker-compose.staging.apache.yml          # hoặc docker-compose.prod.yml sau D4

# 1. Dừng stack ứng dụng (api, frontend, postgres…) — báo nhóm; Kuma sẽ đỏ, đúng như mong đợi
docker compose -f $S down

# 2. Giữ lại volume cũ phòng hờ (đổi tên bằng cách sao chép sang volume mới)
docker volume create socialapp-staging_pgdata-hong-$(date -u +%Y%m%dT%H%M)
docker run --rm -v socialapp-staging_pgdata:/from -v socialapp-staging_pgdata-hong-$(date -u +%Y%m%dT%H%M):/to alpine sh -c 'cp -a /from/. /to/'

# 3. Chép dữ liệu đã khôi phục (đã promote, đang ở socialapp-restore) đè vào volume của stack
docker compose -f docker-compose.restore.yml down            # KHÔNG -v: giữ pgdata-restore
docker run --rm -v socialapp-restore_pgdata-restore:/from -v socialapp-staging_pgdata:/to alpine \
  sh -c 'rm -rf /to/* && cp -a /from/. /to/'

# 4. Bật lại, kiểm sức khỏe, kiểm số bản ghi lần nữa
docker compose -f $S up -d --wait
curl -fsS https://mxh.banhgao.net/health/ready
docker compose -f $S exec -T postgres psql -U socialapp -d socialapp -At -f - < dem-ban-ghi.sql

# 5. Ép một base backup mới NGAY — lịch sử WAL cũ không còn nối tiếp được với timeline mới sau promote
./backup.sh full

# 6. Dọn stack restore; volume "-hong-" giữ 7 ngày rồi xóa tay
docker compose -f docker-compose.restore.yml down -v
```

Tên volume: `<project>_<tên trong compose>` — kiểm bằng `docker volume ls` trước khi gõ, đừng đoán.

## 5. Khi nào KHÔNG được khôi phục

- Chưa đối chiếu số bản ghi giữa bản sao và DB gốc (hoặc với kỳ vọng, nếu DB gốc đã mất).
- Bản sao được chọn tạo **sau** thời điểm sự cố.
- Chưa có ai thứ hai trong nhóm biết việc này đang diễn ra.

## 6. Sau mỗi lần dùng runbook này

Mở `bien-ban-restore-mau.md`, chép thành `bien-ban-restore-YYYY-MM-DD.md`, điền — kể cả khi là sự cố thật chứ
không phải diễn tập. Sửa runbook ở đúng chỗ đã làm bạn phải dừng lại suy nghĩ.
