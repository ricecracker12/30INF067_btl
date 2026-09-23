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
| **Nguội** — R2 | bucket `socialmedia-backup`, tiền tố `staging/` | Token trong `deploy/backup.env` trên VM + bản trong kho bí mật nhóm | **Mất VM** |
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

# 3. Kiểm migration không có gì để áp (schema khớp code đang chạy).
#    Chạy image api CHỈ trong mạng của stack restore: ở đó tên "postgres" là DB khôi phục, nên chuỗi kết nối
#    Host=postgres có sẵn trong .env dùng được nguyên — không phải gõ mật khẩu ra dòng lệnh.
#    KHÔNG thêm mạng nào khác: gắn nhầm mạng staging thì "postgres" thành DB đang chạy.
docker run --rm --network socialapp-restore_default --env-file .env \
  ghcr.io/ricecracker12/30inf067_btl/api:staging --migrate
#    (Không dùng host.docker.internal:15432 — cổng đó chỉ bind 127.0.0.1 của host, từ trong container
#     đi qua host-gateway là bị từ chối kết nối.)
#    Bằng chứng no-op: dòng "[migrate] Đã áp dụng migration…" in ra CẢ KHI không có gì để áp — exit 0 chỉ chứng
#    minh schema tương thích. Đếm lại các bảng __EFMigrationsHistory: số dòng phải BẰNG bảng restore.sh vừa in.
docker compose -f docker-compose.restore.yml exec -T postgres psql -U socialapp -d socialapp -At -f - \
  < dem-ban-ghi.sql | grep EFMigrationsHistory

# 4. Đối chiếu — restore.sh đã in bảng đếm; so với bước 1
# 5. Chỉ mất/hỏng vài dòng → Kịch bản C (Mục 5), KHÔNG dừng dịch vụ. Hỏng diện rộng → Mục 4 (có dừng).
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
S=docker-compose.staging.apache.yml          # môi trường cuối (Đ-7.4) — không có stack production

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

## 5. Kịch bản C — Khôi phục một phần, KHÔNG dừng dịch vụ

Trường hợp hay gặp nhất, và lý do cả thiết kế "khôi phục ra cạnh": DB khôi phục chạy song song rồi thì chỉ **lấy đúng
phần bị mất** chép sang DB đang chạy. Dịch vụ không dừng giây nào.

### Chọn đường

| Sự cố | Đường | Dừng dịch vụ |
|---|---|---|
| Người dùng xóa nhầm bài của mình | **Không cần backup** — bài là xóa mềm (Đ-2.10): `UPDATE content.posts SET status='published', deleted_at=NULL WHERE post_id=…` | Không |
| Mất vài dòng (xóa cứng nhầm, script dọn chạy sai) | **Kịch bản C**, chế độ *bù dòng thiếu* | Không |
| Vài dòng bị sửa sai nội dung | **Kịch bản C**, chế độ *ghi đè cột* | Không |
| Hỏng diện rộng, migration hỏng, không biết dòng nào sai | Mục 4 — thay toàn bộ | **Có**, ~1–2 phút |

### Thứ tự bảng — cha trước con (module Content)

```
content.posts                                   ← cha của mọi thứ
  └─ content.comments                           ← FK post_id → posts (ON DELETE CASCADE)
       └─ content.comments (parent_id)          ← FK tự trỏ, depth 1 → 2 → 3
  └─ content.media_attachments                  ← owner_type='post'  + owner_id   (KHÔNG có FK)
  └─ content.reactions                          ← target_type='post'|'comment' + target_id (KHÔNG có FK)
```

Hai bảng cuối **không có khóa ngoại** — Postgres không chặn nếu chép thiếu hay chép lệch. Kỷ luật "cha trước con,
chép trọn bộ" là việc của người làm, không phải của DB. Tác giả (`author_id`) nằm ở schema `identity`/`profile`,
cũng không có FK (Đ-2.2) — phải tự kiểm tác giả còn tồn tại (bước 3).

> ⚠️ **Không bao giờ "thay" một bài bằng `DELETE` rồi `INSERT`.** `comments.post_id` là `ON DELETE CASCADE`: xóa
> dòng bài là xóa luôn mọi bình luận đang sống của nó trên DB thật. Ghi đè thì dùng `ON CONFLICT … DO UPDATE`.

### Quy trình (ví dụ: cứu một bài và mọi thứ đi kèm)

Tiền đề: đã có stack restore đang chạy với dữ liệu tới **trước** sự cố (Kịch bản A bước 2, thường là PITR tới mốc
ngay trước lúc hỏng), và `--migrate` đã no-op — tức hai DB **cùng schema**, nên `SELECT *` khớp thứ tự cột.

```bash
cd ~/app/deploy
R="docker compose -f docker-compose.restore.yml         exec -T postgres psql -U socialapp -d socialapp -v ON_ERROR_STOP=1"
L="docker compose -f docker-compose.staging.apache.yml exec -T postgres psql -U socialapp -d socialapp -v ON_ERROR_STOP=1"
ID='<post_id>'

# 1. Trên DB KHÔI PHỤC: gom đúng các dòng cần cứu vào schema tạm "rescue" — trọn bộ cha + con
$R <<SQL
DROP SCHEMA IF EXISTS rescue CASCADE;
CREATE SCHEMA rescue;
CREATE TABLE rescue.posts     AS SELECT * FROM content.posts    WHERE post_id = '$ID';
CREATE TABLE rescue.comments  AS SELECT * FROM content.comments WHERE post_id = '$ID';
CREATE TABLE rescue.media     AS SELECT * FROM content.media_attachments WHERE owner_type = 'post' AND owner_id = '$ID';
CREATE TABLE rescue.reactions AS SELECT * FROM content.reactions
  WHERE (target_type = 'post'    AND target_id = '$ID')
     OR (target_type = 'comment' AND target_id IN (SELECT comment_id FROM rescue.comments));
SELECT 'posts', count(*) FROM rescue.posts UNION ALL SELECT 'comments', count(*) FROM rescue.comments
UNION ALL SELECT 'media', count(*) FROM rescue.media UNION ALL SELECT 'reactions', count(*) FROM rescue.reactions;
SQL

# 2. Chuyển schema rescue sang DB ĐANG CHẠY — chỉ thêm một schema, chưa chạm bảng thật nào
$L -c "DROP SCHEMA IF EXISTS rescue CASCADE"
docker compose -f docker-compose.restore.yml exec -T postgres pg_dump -U socialapp -d socialapp -n rescue -Fc \
  | docker compose -f docker-compose.staging.apache.yml exec -T postgres pg_restore -U socialapp -d socialapp

# 3. XEM TRƯỚC trên DB đang chạy: dòng nào đang thiếu, tác giả còn tồn tại không
$L <<'SQL'
SELECT 'posts thiếu',    count(*) FROM rescue.posts r    WHERE NOT EXISTS (SELECT 1 FROM content.posts p    WHERE p.post_id    = r.post_id)
UNION ALL
SELECT 'comments thiếu', count(*) FROM rescue.comments r WHERE NOT EXISTS (SELECT 1 FROM content.comments c WHERE c.comment_id = r.comment_id)
UNION ALL
SELECT 'tác giả đã mất', count(*) FROM rescue.posts r    WHERE NOT EXISTS (SELECT 1 FROM identity.users u   WHERE u.user_id    = r.author_id);
SQL
```

Dừng ở đây nếu `tác giả đã mất > 0` — chép bài của một tài khoản đã xóa là làm sống lại dữ liệu mà người đó đã yêu cầu
xóa (NĐ 13/2023, GOAL-05). Hỏi nhóm trước.

```bash
# 4. ÁP — một transaction, cha trước con. Chế độ mặc định: BÙ DÒNG THIẾU (dòng đang có thì để yên)
$L <<'SQL'
BEGIN;
INSERT INTO content.posts             SELECT * FROM rescue.posts                    ON CONFLICT (post_id)    DO NOTHING;
INSERT INTO content.comments          SELECT * FROM rescue.comments ORDER BY depth  ON CONFLICT (comment_id) DO NOTHING;
INSERT INTO content.media_attachments SELECT * FROM rescue.media                    ON CONFLICT (media_id)   DO NOTHING;
INSERT INTO content.reactions         SELECT * FROM rescue.reactions ON CONFLICT (user_id, target_type, target_id) DO NOTHING;
COMMIT;
SQL

# 5. Dọn cả hai phía
$L -c "DROP SCHEMA rescue CASCADE"
$R -c "DROP SCHEMA rescue CASCADE"
docker compose -f docker-compose.restore.yml down -v
```

**Chế độ ghi đè cột** — khi dòng vẫn còn nhưng nội dung bị sửa sai. Thay dòng `posts` ở bước 4 bằng:

```sql
INSERT INTO content.posts SELECT * FROM rescue.posts
ON CONFLICT (post_id) DO UPDATE SET
  body = excluded.body, privacy = excluded.privacy, status = excluded.status, hidden_reason = excluded.hidden_reason,
  media_count = excluded.media_count, edited_at = excluded.edited_at, deleted_at = excluded.deleted_at,
  updated_at = now();
```

Cố ý **không** ghi đè `comment_count` và `reaction_counts`: DB đang chạy có thể đã có tương tác mới sau mốc khôi
phục, ghi đè là kéo bộ đếm lùi về quá khứ.

### Bộ đếm và ảnh — hai chỗ Kịch bản C không tự lo được

- **Bộ đếm** (`comment_count`, `reaction_counts`, `media_count` trên `posts`) là số ghi sẵn, không phải số tính lúc
  đọc. Chép **trọn bộ** một bài (như ví dụ trên) thì bộ đếm trong dòng bài khớp sẵn với con của nó. Chép **lẻ**
  (chỉ vài bình luận vào một bài đang sống) thì bộ đếm lệch — tính lại theo đúng quy tắc của module Content, **không**
  tự viết câu `UPDATE … count(*)` trong lúc xử lý sự cố.
- **Ảnh không nằm trong bản sao.** Backup chỉ chứa DB; object trên R2 là nguồn riêng. Nếu worker dọn rác đã xóa
  object của bài (Đ-2.10: object của bài xóa mềm bị xóa trễ; Đ-2.13: object mồ côi), dòng `media_attachments` khôi
  phục sẽ trỏ vào ảnh không còn tồn tại. Kiểm trên R2 dashboard (bucket ứng dụng, theo `storage_key`) trước khi báo
  "đã khôi phục xong". Đây là **giới hạn đã biết** của khối A, không phải lỗi thao tác.

## 6. Khi nào KHÔNG được khôi phục

- Chưa đối chiếu số bản ghi giữa bản sao và DB gốc (hoặc với kỳ vọng, nếu DB gốc đã mất).
- Bản sao được chọn tạo **sau** thời điểm sự cố.
- Chưa có ai thứ hai trong nhóm biết việc này đang diễn ra.

## 7. Sau mỗi lần dùng runbook này

Mở `bien-ban-restore-mau.md`, chép thành `bien-ban-restore-YYYY-MM-DD.md`, điền — kể cả khi là sự cố thật chứ
không phải diễn tập. Sửa runbook ở đúng chỗ đã làm bạn phải dừng lại suy nghĩ.
