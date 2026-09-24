# Hướng dẫn thực hiện — Khối A. Sao lưu và khôi phục (GĐ7, Ngày 1 → Ngày 2 sáng)

> Bản triển khai chi tiết của **B.5 Khối A** trong [giai-doan-7.md](giai-doan-7.md). Tài liệu gốc trả lời
> *cái gì* và *vì sao* (Mục 6 chính sách, Đ-7.10, Đ-7.11); tài liệu này trả lời *gõ lệnh nào, theo thứ tự
> nào, kiểm gì, và nhìn vào đâu để biết đã xong thật*.
>
> **Nguồn sự thật vẫn là `giai-doan-7.md`** (Mục 6, Mục 7 khuôn biên bản, Mục 12 checklist, B.5, B.9).
> Script và compose đã viết sẵn trong `deploy/` — tài liệu này chỉ nói cách dùng và cách kiểm.

| | |
|---|---|
| **Người làm** | Một người, có SSH vào VM (user `deploy`) và quyền vào dashboard Cloudflare R2 |
| **Thời lượng** | Ngày 1: A1–A4 (~1 ngày) · Ngày 2 sáng: A5 (~nửa ngày) |
| **Khối này cần trước** | **B1** (Kuma — để có monitor Push cho "backup không chạy") |
| **Khối này chặn** | NFR-REL-02 |

**Dựng ở đâu:** trên **staging** — môi trường cuối (Đ-7.4, sửa 2026-09-23: không có production riêng). Dữ liệu
staging là dữ liệu thật, và bản sao của nó là bản được báo cáo.

---

## 0. Danh sách công việc — mục tiêu và kết quả mong đợi

| Mã | Đầu việc | Mục tiêu — việc này tồn tại để làm gì | Kết quả mong đợi — thứ kiểm chứng được |
|---|---|---|---|
| **A1** | Bật WAL archiving + `archive_timeout=15min` | Có "băng ghi" liên tục để tua tới bất kỳ thời điểm nào → RPO ≤ 15 phút | `pg_stat_archiver.archived_count` tăng, `failed_count = 0`; để yên 20 phút vẫn thấy file WAL mới trong `backups/wal/` |
| **A2** | `backup.sh full` chạy tay rồi chạy cron | Bản sao vật lý + logic hằng ngày, có kích thước + sha256 trong log, tự xóa bản quá hạn | Chạy tay xong: `backups/base/daily-…/base.tar.gz` ≥ 100KB; `backup.log` có dòng sha256; cron chạy đúng giờ → có bản thứ hai **không do tay** |
| **A3** | Đẩy lên R2 bucket riêng + Kuma Push | Bản sao **rời khỏi VM** (Đ-7.10); backup im lặng thất bại thì có người biết | Object trên R2 dashboard; tải một bản về máy khác giải nén được; monitor Push trong Kuma xanh, và **đỏ** khi cố tình không chạy |
| **A4** | Runbook khôi phục | Người **khác** trong nhóm khôi phục được mà không hỏi | [runbook-khoi-phuc.md](runbook-khoi-phuc.md) có sẵn — đọc, chạy thử từng lệnh, sửa chỗ sai |
| **A5** | Restore drill + biên bản ⭐ | Biến "có backup" thành "**đã khôi phục được**" — sản phẩm được chấm | `bien-ban-restore-YYYY-MM-DD.md` đủ Mục 1–5, RPO/RTO **thực đo**, số bản ghi khớp |

### Thứ tự thực thi

```
A1 ─→ A2 ─→ A3 ─→ A4 ─→ A5
 │           │
 │           └── A3 cần bucket + token R2 (dashboard, ~15 phút) — làm trước khi tới A3 để không ngồi chờ
 └── A1 recreate container postgres (≈10 giây mất DB) — báo nhóm
```

---

## 1. Trước khi bắt đầu (10 phút)

| # | Kiểm | Kỳ vọng |
|---|---|---|
| 1 | B1 xong — Kuma đang chạy | `docker compose -f docker-compose.ops.yml ps` → Up |
| 2 | Đĩa | `df -h /` — ghi con số; DB nhỏ nên vài trăm MB là đủ, nhưng phải **biết** mốc |
| 3 | Múi giờ VM | `date` — thường là UTC. 03:00 VN = **20:00 UTC**; cron viết theo giờ VM |
| 4 | Các file khối A đã có trong repo, nhánh sẽ merge `develop` | `deploy/backup.sh`, `restore.sh`, `dem-ban-ghi.sql`, `backup.env.example`, `docker-compose.restore.yml`, và `docker-compose.staging.apache.yml` đã có `command:` + `./backups` |
| 5 | **Báo nhóm** | A1 recreate postgres ~10 giây; A5 không chạm stack đang chạy nhưng tốn CPU/đĩa |

**Luật áp vào khối này:**
- Mọi lệnh dưới user `deploy`, trong `~/app/deploy/`. Không `sudo` vào `/home/deploy`.
- `backup.env` chỉ tồn tại trên VM (gitignore). Token R2 backup là token **mới**, phạm vi chỉ bucket backup.
- **Không bao giờ `restore.sh` bằng volume của stack đang chạy.** Script không cho phép, nhưng đừng thử sửa nó.
- Hai script cần quyền chạy: trong repo `git update-index --chmod=+x deploy/backup.sh deploy/restore.sh`
  (Windows không giữ mode); trên VM `chmod +x` nếu lỡ mất.

---

## 2. A1 — Bật WAL archiving

### Mục tiêu

Postgres chép mỗi segment WAL vào `backups/wal/` ngay khi segment đầy **hoặc** sau 15 phút — cái sau là thứ giữ
cam kết RPO (Đ-7.11).

### Việc phải làm

**Bước 1 — Chuẩn bị thư mục TRƯỚC khi compose mới chạy.** Nếu để Docker tự tạo `./backups` thì nó thuộc
`root:root 755` và tiến trình Postgres (uid 999) **không ghi được** → `archive_command` thất bại liên tục, WAL ứ
trong `pg_wal`, đĩa đầy dần trong im lặng.

```bash
sudo -iu deploy
cd ~/app/deploy
mkdir -p backups
docker run --rm -v "$PWD/backups:/b" postgres:16 sh -c 'mkdir -p /b/wal /b/base /b/dump && chown -R 999:999 /b'
ls -ln backups            # wal/ base/ dump/ thuộc 999
```

**Bước 2 — Đưa compose mới lên.** Hai cách, chọn một:
- **Merge vào `develop`** → CD scp compose + `up -d` → Postgres được recreate với `command:` mới. Đây là đường
  chuẩn (README Mục 7.2: không deploy tay).
- Hoặc nếu chưa merge được: `scp deploy/docker-compose.staging.apache.yml deploy@<host>:app/deploy/` rồi
  `docker compose -f docker-compose.staging.apache.yml up -d postgres`. Lần CD kế tiếp sẽ ghi đè bằng đúng file
  này nên không lệch.

**Bước 3 — Kiểm.**

```bash
S=docker-compose.staging.apache.yml
docker compose -f $S exec -T postgres psql -U socialapp -d socialapp -Atc \
  "select name||'='||setting from pg_settings where name in ('archive_mode','archive_command','archive_timeout')"
# archive_mode=on · archive_command=test ! -f /backups/wal/%f && cp %p /backups/wal/%f · archive_timeout=900

# Ép một segment đóng để thấy archive chạy ngay, khỏi đợi 15 phút
docker compose -f $S exec -T postgres psql -U socialapp -d socialapp -Atc "select pg_switch_wal()"
sleep 5
docker compose -f $S exec -T postgres psql -U socialapp -d socialapp -Atc \
  "select archived_count, failed_count, last_archived_wal from pg_stat_archiver"
ls backups/wal/            # có file 000000010000000000000001 (hoặc tương tự)
```

### Kết quả mong đợi — checklist A1

- [ ] Ba tham số đúng như trên (`archive_timeout=900` giây)
- [ ] `failed_count = 0`, `archived_count ≥ 1`
- [ ] Để yên **20 phút không thao tác gì** → `ls backups/wal/` có thêm file (đây mới là bằng chứng `archive_timeout`
      chạy, không phải `pg_switch_wal`)
- [ ] `curl -fsS https://mxh.banhgao.net/health/ready` → `Healthy` (recreate xong, API nối lại)

### Cạm bẫy

- **`failed_count > 0`** → `docker compose -f $S logs --tail 50 postgres` — gần như chắc chắn là quyền thư mục
  (Bước 1). Sửa quyền xong Postgres tự thử lại, không cần restart.
- **Quên `archive_timeout`** → mọi thứ vẫn "chạy", nhưng RPO thực là "tới lúc segment 16MB đầy" — với DB ít ghi
  có thể là nhiều giờ. Đây là lỗi không test nào bắt được ngoài việc **nhìn vào `pg_settings`**.

---

## 3. A2 — `backup.sh` chạy tay rồi chạy cron

### Mục tiêu

Một bản sao đầy đủ mỗi ngày, có bằng chứng (kích thước, sha256) trong log, tự dọn bản quá hạn. Chưa có R2 cũng
chạy được — script bỏ qua bước đẩy khi thiếu `backup.env` (và **ghi rõ** là bỏ qua).

### Việc phải làm

```bash
cd ~/app/deploy
chmod +x backup.sh restore.sh

# Chạy tay lần 1 — đọc log từ đầu tới cuối
./backup.sh full
ls -ln backups/base/ backups/dump/
tail -20 backup.log 2>/dev/null || true       # lần chạy tay in ra màn hình; cron mới ghi vào backup.log
```

**Thử hạn giữ THẬT — làm già một bản sao, không sửa script.** Hạn giữ là hằng số trong script (cố ý — để cron
không vô tình chạy với biến môi trường khác). **Đừng** sửa nó thành số âm: `find -mtime +-1` không phải đối số hợp
lệ, `find` báo lỗi và script dừng giữa chừng. Lùi mtime của bản cũ nhất rồi chạy lại:

```bash
S=docker-compose.staging.apache.yml
B=<tên bản cũ nhất trong backups/base/>
docker compose -f $S exec -T postgres touch -d '10 days ago' /backups/base/$B /backups/dump/$B.dump
./backup.sh full
```

Dòng `còn giữ:` phải **không còn `$B`**; `sync` ở cuối cũng xóa nó khỏi R2. Chụp ảnh log.

**Cron** (dưới `deploy`, `crontab -e`):

```cron
PATH=/usr/local/bin:/usr/bin:/bin
# GĐ7 khối A — 03:00 VN = 20:00 UTC (kiểm `date` trên VM); sync mỗi 15 phút để WAL rời VM kịp RPO
0 20 * * *    /home/deploy/app/deploy/backup.sh full >> /home/deploy/app/deploy/backup.log 2>&1
*/15 * * * *  /home/deploy/app/deploy/backup.sh sync >> /home/deploy/app/deploy/backup.log 2>&1
```

Không muốn đợi tới 20:00 để biết cron có chạy: tạm thêm một dòng `*/5 * * * * … full`, đợi 5 phút, thấy bản mới
trong `backups/base/` và dòng log **có prefix giờ** trong `backup.log`, rồi xóa dòng tạm.

### Kết quả mong đợi — checklist A2

- [ ] `backups/base/daily-<stamp>/` có `base.tar.gz` (≥ 100KB), `pg_wal.tar.gz`, `backup_manifest`
- [ ] `backups/dump/daily-<stamp>.dump` tồn tại
- [ ] Log có dòng `base.tar.gz <n> byte · <sha256> …` và `pg_stat_archiver … = <n>|0|f`
- [ ] Hạn giữ **đã thấy xóa thật** một lần (ảnh log)
- [ ] Có bản thứ hai sinh bởi **cron**, không phải tay (`backup.log` có dòng lúc 20:00 UTC hoặc lúc dòng tạm chạy)

### Cạm bẫy

- **`docker compose exec` trong cron lỗi "no such service"** → cron không thấy `COMPOSE_FILE`; script đã tự đặt
  đường dẫn tuyệt đối theo vị trí của nó, nên lỗi này nghĩa là script nằm sai chỗ (phải ở `~/app/deploy/`).
- **File trong `backups/` thuộc root, `deploy` không xóa tay được** — đúng thiết kế: xóa qua container
  (`docker compose -f $S exec -T postgres rm -rf /backups/base/<tên>`), đừng `sudo`.
- **`sync` chạy trước khi có bản base nào** → script từ chối (chốt an toàn), log `base rỗng`. Đúng, không phải lỗi.

---

## 4. A3 — Rời VM: R2 bucket riêng + Kuma Push

### Mục tiêu

Bản sao nằm trên VM chưa phải bản sao (Đ-7.10). Và "backup không chạy" phải có người biết (Mục 5.3).

### Việc phải làm

**Bước 1 — Cloudflare dashboard (chủ tài khoản, ~15 phút):**
1. R2 → *Create bucket* → `socialmedia-backup`. Không bật public access. Không CORS (không có trình duyệt nào đọc nó).
2. R2 → *Manage R2 API Tokens* → *Create* → quyền **Object Read & Write**, *Specify bucket* = `socialmedia-backup`
   **chỉ bucket này** → lưu Access Key ID + Secret Access Key + endpoint `https://<account-id>.r2.cloudflarestorage.com`
   vào kho bí mật nhóm. **Không** dùng lại token của `-dev`/`-staging` (GĐ2) — và nhớ đây là token mới, không dính
   vụ lộ khóa 2026-09-04.

**Bước 2 — Kuma (qua tunnel `localhost:3002`):** *Add New Monitor* → loại **Push** → tên `staging · backup hằng ngày`
→ Heartbeat Interval **93600** (26 giờ) → Retries 0 → thông báo Telegram bật. Kuma đưa một URL dạng
`http://localhost:3002/api/push/<token>?status=up&msg=OK&ping=` — chỉ lấy phần **trước dấu `?`**, đổi host thành
`127.0.0.1` (giữ nguyên cổng **3002** — `backup.sh` chạy trên host, gọi vào cổng Kuma đã publish, không phải
cổng bên trong container).

**Bước 3 — Trên VM:**

```bash
cd ~/app/deploy
cp backup.env.example backup.env && chmod 600 backup.env
nano backup.env      # điền 3 khóa R2 + endpoint + BACKUP_KUMA_PUSH_URL; BACKUP_PREFIX=staging

./backup.sh sync     # lần đầu pull image rclone; xem dòng "đã đồng bộ lên r2:…"
./backup.sh full     # lần full đầu tiên có đẩy R2 + báo Kuma → monitor Push chuyển xanh
```

**Bước 4 — Chứng minh bản sao dùng được từ nơi khác:** trên **máy dev** (không phải VM), với cùng `backup.env`:

```bash
mkdir -p /tmp/r2-check
docker run --rm --env-file backup.env -v /tmp/r2-check:/data rclone/rclone:latest \
  copy r2:socialmedia-backup/staging/base /data --max-depth 2 --stats-one-line
ls -R /tmp/r2-check | head; tar tzf /tmp/r2-check/daily-*/base.tar.gz | head -3     # liệt kê được = file lành
```

### Kết quả mong đợi — checklist A3

- [ ] R2 dashboard: `staging/base/…`, `staging/wal/…`, `staging/dump/…` có object
- [ ] Tải về máy khác, `tar tzf` liệt kê được
- [ ] Kuma monitor Push **xanh** sau `backup.sh full`
- [ ] **Phá thử:** tạm đổi Heartbeat Interval thành 120 giây, không chạy backup 3 phút → Push **đỏ** + Telegram;
      chạy `./backup.sh full` → xanh lại; trả interval về 93600. Ảnh → `bang-chung/A3-kuma-push-down-up.png`
- [ ] `backup.env` **không** có trong `git status` (gitignore đã chặn)

### Cạm bẫy

- **`sync` thấy nguồn rỗng thì từ chối** — cố ý (rclone `sync` với nguồn rỗng = xóa sạch đích).
- **`--max-delete 400`**: mỗi ngày hạn giữ có thể xóa ~100 file WAL + vài base; vượt 400 là bất thường → rclone
  dừng, log lỗi. Nếu một ngày nào đó gặp, kiểm tay trước khi nâng số.
- **Endpoint thiếu `https://`** hoặc sai account-id → lỗi `NoSuchBucket`/`403` trông giống sai khóa. Kiểm endpoint trước.
- **Push URL còn nguyên `?status=up…`** → script nối thêm `?` lần nữa → Kuma không nhận. Chỉ lấy phần trước `?`.

---

## 5. A4 — Runbook khôi phục

### Mục tiêu

[runbook-khoi-phuc.md](runbook-khoi-phuc.md) đã viết sẵn ba kịch bản (VM còn sống · mất VM · đưa dữ liệu khôi
phục vào stack). A4 là **đọc và chạy thử từng lệnh** — không phải viết mới.

### Việc phải làm

1. Đọc runbook một lượt. Mỗi lệnh không hiểu → sửa runbook cho hiểu, không phải hỏi.
2. Chạy thử **Kịch bản A bước 1–2** ngay bây giờ (nó chính là A5 thu nhỏ): `./restore.sh <bản mới nhất>` → xem
   bảng đếm → `down -v`. Bấm giờ. Nếu dưới 10 phút thì A5 ngày mai chủ yếu là điền biên bản.
3. Kiểm tên volume thật bằng `docker volume ls` và sửa vào Mục 4 của runbook nếu khác `socialapp-staging_pgdata`.

### Kết quả mong đợi — checklist A4

- [ ] Một người **khác** trong nhóm đọc runbook và chỉ ra ≥ 1 chỗ chưa rõ → đã sửa
- [ ] `restore.sh` chạy thành công ít nhất một lần, `down -v` sau đó
- [ ] Tên volume trong runbook Mục 4 khớp `docker volume ls`

---

## 6. A5 — Restore drill + biên bản ⭐ *(Ngày 2 sáng)*

### Mục tiêu

Diễn tập **như thật**, có người chứng kiến, bấm giờ, điền [bien-ban-restore-mau.md](bien-ban-restore-mau.md). Đây
là thứ được chấm (NFR-REL-02) — không phải `restore.sh` chạy xanh.

### Kịch bản khuyến nghị: "mất VM lúc 09:30, chỉ còn R2"

Mô phỏng trung thực nhất có thể mà không phá VM:

```bash
sudo -iu deploy && cd ~/app/deploy
# 0. Tạo vài dữ liệu mới trên staging (đăng 1–2 bài qua UI) rồi ghi giờ — đây là "dữ liệu trước sự cố"
# 1. Đếm bản ghi DB gốc — ghi giờ (đây là cột "DB gốc" của Mục 3 biên bản)
docker compose -f docker-compose.staging.apache.yml exec -T postgres psql -U socialapp -d socialapp -At -f - < dem-ban-ghi.sql
# 2. Ép archive để WAL chứa dữ liệu vừa tạo đã rời VM (đời thật thì là archive_timeout 15 phút + sync)
docker compose -f docker-compose.staging.apache.yml exec -T postgres psql -U socialapp -d socialapp -Atc "select pg_switch_wal()"
./backup.sh sync
# 2b. TẠM TẮT CRON suốt buổi drill: sync */15 chạy giữa chừng sẽ đồng bộ thư mục backups/ đang dở dang lên R2
crontab -l > /tmp/crontab.truoc-drill && crontab -r
# 3. GIẢ VỜ MẤT VM: đổi tên thư mục bản sao nóng — không xóa.
#    (postgres staging bind-mount theo inode nên vẫn archive tiếp vào backups.truoc-drill — không mất WAL nào)
mv backups backups.truoc-drill && mkdir backups
# 4. Kéo về từ R2 — BẤM GIỜ (bước tốn nhất)
docker run --rm --env-file backup.env -v "$PWD/backups:/data" rclone/rclone:latest copy r2:socialmedia-backup/staging /data --stats-one-line -v
# 5. Khôi phục ra cạnh — BẤM GIỜ
./restore.sh $(ls backups/base | grep daily | tail -1)
# 6. --migrate phải no-op (runbook Kịch bản A bước 3) — BẤM GIỜ
# 7. So bảng đếm với bước 1; max(created_at) của content.posts so với giờ bước 0 → RPO thực đo
# 8. Dọn: down -v; trả lại thư mục nóng; bật lại cron
docker compose -f docker-compose.restore.yml down -v
#    File rclone tải về thuộc root → `rm -rf` bằng user deploy bị "Permission denied". Xóa qua container:
docker run --rm -v "$PWD:/w" alpine rm -rf /w/backups
mv backups.truoc-drill backups
crontab /tmp/crontab.truoc-drill && crontab -l
```

Điền biên bản **trong lúc làm**, không phải sau — số giờ nhớ lại luôn đẹp hơn số giờ thật.

### Kết quả mong đợi — checklist A5

- [ ] `docs/giai-doan-7/bien-ban-restore-YYYY-MM-DD.md` đủ Mục 1–5, có hai chữ ký
- [ ] RPO thực đo ≤ 15 phút; RTO thực đo ≤ 2 giờ — **ghi số**, kể cả khi trượt (trượt thì ghi việc phải sửa)
- [ ] Số bản ghi mọi bảng khớp; `--migrate` no-op
- [ ] Mục 4 biên bản có ≥ 1 dòng (diễn tập không gặp gì thường là diễn tập chưa đủ thật)
- [ ] Những gì học được đã sửa vào runbook/script **cùng commit** với biên bản

### Cạm bẫy

- **Bước 3 gõ `rm -rf backups`** thay vì `mv` → mất bản nóng. Bản trên R2 vẫn còn, nhưng đó không phải cách bạn
  muốn phát hiện ra R2 có hoạt động hay không. Đọc lệnh hai lần.
- **Khôi phục vào `-alpine`** vì "có sẵn image dev" → collation khác, index text hỏng ngầm. `docker-compose.restore.yml`
  đã ghim `postgres:16`; đừng đổi.
- **Quên `down -v` sau drill** → volume `pgdata-restore` chiếm đĩa và lần drill sau `restore.sh` tự xóa nó — không
  mất gì, nhưng tốn đĩa vô ích suốt thời gian giữa hai lần.

---

## 7. Đưa vào repo

Một PR cho cả khối (README Mục 10.1: một PR = một khối). Commit theo `.claude/rules/commit-rules.md`, ví dụ:

```
feat(gd7-a): A1–A3 — WAL archiving 15 phút, backup.sh hằng ngày + sync 15 phút lên R2, Kuma Push

Base backup vật lý + dump logic (Đ-7.11), hạn giữ 7 ngày + 4 tuần, bản sao rời VM qua bucket
R2 riêng với token mới (Đ-7.10). "Backup không chạy" báo qua monitor Push của Kuma (B1) thay vì
Grafana — có ngay từ khối A, không đợi khối C.

Test: archived_count tăng sau 20 phút không thao tác; hạn giữ đã xóa thật; Push đỏ khi ngưng 3 phút
detect-changes: low, 0 luồng
```

```
docs(gd7-a): A5 — biên bản restore drill 2026-09-2x, RPO x phút / RTO y phút

<những gì học được và đã sửa vào runbook>
```

Nhớ `git update-index --chmod=+x deploy/backup.sh deploy/restore.sh` trước khi commit lần đầu.

Sau khi merge: `README.md` Mục 1 dòng GĐ7 thêm **"Khối A (sao lưu) chạy từ <ngày>; drill <ngày>: RPO …/RTO …"**.

---

## 8. Khối A để lại gì

| Di sản | Ai dùng |
|---|---|
| `command:` + `./backups` trong compose staging | **Khối E** — nếu làm `edge` + 2 bản sao, giữ nguyên phần `postgres` |
| `backup.sh` / `restore.sh` / `dem-ban-ghi.sql` | **GĐ8** — bắt buộc một bản `full` tay ngay trước mỗi buổi k6/ZAP (Đ-7.4) |
| Monitor Push "backup hằng ngày" | **C5** — một trong năm cảnh báo Mục 5.3 đã xong từ đây |
| Biên bản drill | **F2** — bằng chứng NFR-REL-02 trong báo cáo |
| Runbook Mục 4 (đưa dữ liệu vào stack) | **E4** — cùng khuôn với rollback theo tag |
