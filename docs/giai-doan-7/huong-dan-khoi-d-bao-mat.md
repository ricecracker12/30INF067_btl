# Hướng dẫn thực hiện — Khối D. Nợ bảo mật GĐ0B (GĐ7)

> Bản triển khai chi tiết của **B.4 Khối D** trong [giai-doan-7.md](giai-doan-7.md). Tài liệu gốc trả lời
> *cái gì* và *vì sao* (Đ-7.4 staging là môi trường cuối, Đ-7.12 HSTS, Đ-7.13 Swagger mở có chủ đích); tài liệu này
> trả lời *gõ lệnh nào, kiểm gì, và nhìn vào đâu để biết đã xong thật*.
>
> **Nguồn sự thật vẫn là `giai-doan-7.md`** (Mục 12 checklist, B.4). Cấu hình apache đã viết sẵn trong
> [`deploy/apache-socialapp.conf.example`](../../deploy/apache-socialapp.conf.example).

| | |
|---|---|
| **Người làm** | Một người có **sudo** trên VM (D1, D2 sửa apache — user `deploy` không làm được) + quyền vào dashboard Cloudflare / Brevo (D3) |
| **Thời lượng** | ~1–1,5 giờ (D1 15 phút · D2 20 phút · D3 30–45 phút) |
| **Khối này cần trước** | Không gì cả |
| **Gián đoạn dịch vụ** | D1, D2: không (apache `reload` không cắt kết nối). D3: **~15 giây mỗi lần** recreate `api` — báo nhóm |

---

## 0. Danh sách công việc — mục tiêu và kết quả mong đợi

| Mã | Đầu việc | Mục tiêu | Kết quả mong đợi — thứ kiểm chứng được |
|---|---|---|---|
| **D1** | HSTS cho mọi đường | Trình duyệt và máy quét (ZAP ở GĐ8) thấy HSTS trên **cả** API, không chỉ trang frontend | Đúng **một** header `Strict-Transport-Security: max-age=31536000` ở `/`, `/api/v1/ping`, `/health/ready`, `/swagger/index.html` |
| **D2** | Rà Swagger + chặn endpoint demo lỗi | Swagger mở có chủ đích (Đ-7.13) thì thứ nó phơi ra phải đúng bằng hợp đồng; không ai ngoài nhóm tự tạo được lỗi 500 | Mọi endpoint trên Swagger có trong `*-v1.yaml`; `/api/v1/ping/boom` + `/app-error` từ Internet → **403**; `/api/v1/ping` vẫn 200 |
| **D3** | Xoay các khóa đã lộ | Không còn khóa nào lộ ngày 2026-09-04 đang sống trên môi trường cuối | Sổ xoay khóa (Mục 4) đủ dòng, có ngày; staging vẫn xanh sau mỗi lần xoay |

### Điểm xuất phát — kiểm trực tiếp trên staging ngày 2026-09-23

| Đường | HSTS hiện tại | Ghi chú |
|---|---|---|
| `/`, `/login` | ✅ `max-age=31536000` | Next.js tự gửi từ GĐ1 (`src/frontend/next.config.ts`, chỉ khi `NODE_ENV=production`) |
| `/api/v1/ping`, `/health/ready`, `/swagger/index.html` | ❌ không có | Đi thẳng từ apache vào API, không qua Next |

Swagger công bố 5 nhóm: `platform-v1`, `identity-v1`, `profile-v1`, `content-v1`, `socialgraph-v1`. Nhóm Platform có
thêm hai endpoint demo của GĐ0 (`ping/app-error` → 409, `ping/boom` → 500), công khai, không cần đăng nhập.

---

## 1. Trước khi bắt đầu (5 phút)

```bash
# Trên VM, bằng user có sudo (không phải deploy)
sudo apache2ctl -M | grep -E 'headers|authz_core'        # headers_module phải có; thiếu thì: sudo a2enmod headers
ls -l /etc/apache2/sites-available/ | grep -i socialapp   # tìm đúng tên file vhost đang chạy
```

File vhost **đang chạy** trên VM có thể đã lệch file mẫu trong repo (ai đó sửa tay lúc GĐ0B/GĐ1). **Đừng chép đè
file mẫu lên** — so trước, rồi chỉ thêm hai khối D1, D2 vào file thật:

```bash
diff <(sed 's/[[:space:]]*$//' /etc/apache2/sites-available/socialapp.conf) \
     <(sed 's/[[:space:]]*$//' ~/app/deploy/apache-socialapp.conf.example) || true
sudo cp /etc/apache2/sites-available/socialapp.conf /etc/apache2/sites-available/socialapp.conf.truoc-gd7-d
```

*(File mẫu không đi qua CD — lấy từ repo bằng `scp` nếu VM chưa có bản mới.)*

---

## 2. D1 — HSTS cho mọi đường

### Việc phải làm

Mở vhost 443 (`sudo nano /etc/apache2/sites-available/socialapp.conf`), thêm ngay sau dòng `RequestHeader set
X-Forwarded-Proto "https"`:

```apache
Header onsuccess unset Strict-Transport-Security
Header always set Strict-Transport-Security "max-age=31536000"
```

**Vì sao cần cả hai dòng:** header từ backend (Next) nằm ở bảng `onsuccess` của apache, còn `Header always set` ghi
vào bảng `always` — hai bảng khác nhau. Chỉ có dòng `set` thì trang frontend trả về **hai** header HSTS. Trình duyệt
chỉ đọc cái đầu nên không hỏng gì, nhưng máy quét ở GĐ8 sẽ gắn cờ, và đó là loại lỗi rất khó giải thích lúc bảo vệ.

```bash
sudo apache2ctl configtest && sudo systemctl reload apache2
```

### Kết quả mong đợi — checklist D1

```bash
for p in / /login /api/v1/ping /health/ready /swagger/index.html; do
  printf "%-22s %s\n" "$p" "$(curl -s -o /dev/null -D - https://mxh.banhgao.net$p | grep -ci '^strict-transport-security')"
done
```

- [ ] Cả năm dòng in ra **`1`** — không `0` (thiếu), không `2` (trùng)
- [ ] Giá trị là `max-age=31536000`, **không** có `includeSubDomains`, **không** có `preload` (Đ-7.12)
- [ ] Kuma ba monitor vẫn xanh sau `reload`

### Cạm bẫy

- **`configtest` báo `Invalid command 'Header'`** → thiếu `mod_headers`: `sudo a2enmod headers` rồi làm lại.
- **Thêm `includeSubDomains` "cho chắc"** → áp lên mọi subdomain của `banhgao.net` mà dự án không quản, và trình
  duyệt đã nhận thì nhớ 1 năm, không rút lại được.
- **Bật thêm HSTS ở Cloudflare** (SSL/TLS → Edge Certificates) → lại thành hai header. Chọn một chỗ: apache, vì nó nằm
  trong repo và review được.

---

## 3. D2 — Rà Swagger + chặn endpoint demo lỗi

### 3.1 Rà: thứ Swagger phơi ra phải đúng bằng hợp đồng

Trên máy dev (cần `node`) hoặc VM:

```bash
for g in $(curl -s https://mxh.banhgao.net/swagger/index.html | grep -o '/swagger/[^/"]*/swagger.json' | sort -u); do
  echo "== $g"
  curl -s "https://mxh.banhgao.net$g" > /tmp/sw.json
  node -e 'const j=require("/tmp/sw.json");for(const [p,o] of Object.entries(j.paths||{}))
    console.log("  "+Object.keys(o).map(m=>m.toUpperCase()).join(",").padEnd(14)+p)'
done
```

Đối chiếu từng dòng với file hợp đồng `src/backend/Modules/<Module>/Presentation/*-v1.yaml`. Kết quả rà ngày
2026-09-23: **khớp hết, trừ hai endpoint demo** của nhóm Platform. Lặp lại bước này mỗi khi một giai đoạn mới thêm
nhóm Swagger (GĐ5 Messaging, GĐ6 Notification/Moderation).

Thứ **không** được xuất hiện: endpoint thử nghiệm (probe của AuthZ matrix chỉ nằm trong assembly test — đúng), endpoint
admin chưa có dòng trong `AuthZMatrix.cs`, đường dẫn nội bộ (`/metrics` ở khối C).

### 3.2 Chặn `ping/boom` và `ping/app-error` ở apache

Thêm vào vhost 443, **trước** các dòng `ProxyPass`:

```apache
<LocationMatch "^/api/v1/ping/(boom|app-error)$">
    Require all denied
</LocationMatch>
```

```bash
sudo apache2ctl configtest && sudo systemctl reload apache2
```

**Vì sao chặn ở apache, không sửa code:** `PingController` là walking skeleton của GĐ0, integration test dùng hai
endpoint này để kiểm RFC 7807. Chặn ở apache thì code và test giữ nguyên, người ngoài không gọi được, còn bước C5
(thử cảnh báo tỷ lệ lỗi) vẫn bắn được từ chính VM qua `127.0.0.1:18080` — đi thẳng vào API, không qua apache.

### Kết quả mong đợi — checklist D2

```bash
for p in /api/v1/ping /api/v1/ping/boom /api/v1/ping/app-error; do
  printf "%-26s Internet: %s   VM trực tiếp: %s\n" "$p" \
    "$(curl -s -o /dev/null -w '%{http_code}' https://mxh.banhgao.net$p)" \
    "$(curl -s -o /dev/null -w '%{http_code}' http://127.0.0.1:18080$p)"      # cột này chỉ đúng khi chạy TRÊN VM
done
```

- [ ] `ping` → Internet **200** · VM **200**
- [ ] `ping/boom` → Internet **403** · VM **500**
- [ ] `ping/app-error` → Internet **403** · VM **409**
- [ ] Mọi endpoint còn lại trên Swagger có trong file hợp đồng
- [ ] Kuma `api ping` vẫn xanh

### Cạm bẫy

- **`<Location /api/v1/ping>`** thay vì `LocationMatch` hai đường cụ thể → chặn luôn `/api/v1/ping`: monitor `api ping`
  của Kuma đỏ, và bạn vừa tự tạo một sự cố uptime thật.
- **Đặt khối chặn sau `ProxyPass /`** vẫn chạy (kiểm quyền xảy ra trước khi proxy) — nhưng đặt trước cho dễ đọc,
  người sau thấy ngay.

---

## 4. D3 — Xoay các khóa đã lộ

### 4.1 Sổ xoay khóa

Ghi **tên khóa và ngày**, không bao giờ ghi giá trị. Đây là bằng chứng nghiệm thu D3.

| Khóa | Lộ ngày | Tình trạng lúc mở D3 | Việc | Ưu tiên | Đã xong (ngày · ai) |
|---|---|---|---|---|---|
| Khóa SMTP **Brevo** | 2026-09-04 | **Không còn dùng** — GĐ1 đã đổi sang Resend | **Thu hồi** trên dashboard Brevo. Khóa còn sống thì ai cầm cũng gửi mail được, có thể dưới tên domain của nhóm | **Cao** · 2 phút | |
| Token **R2** cũ | 2026-09-04 | Chưa rõ còn sống không | Cloudflare → R2 → *Manage R2 API Tokens*: thu hồi mọi token tạo **trước hoặc đúng 2026-09-04**. Nếu token ứng dụng đang dùng nằm trong số đó → xoay theo 4.2 trước rồi mới thu hồi | **Cao** | |
| `Jwt__SigningKey` | 2026-09-04 | **Đã xoay** sau GĐ1 F5 (`huong-dan-khoi-f-cong-dong.md`) | Chỉ xác nhận. Xoay lại là đăng xuất mọi người — chỉ làm khi nghi lộ lần nữa | — | 2026-09-18 (GĐ1 F5) |
| Mật khẩu **Postgres** | 2026-09-04 | Chưa xoay | Xoay theo 4.3 | Thấp nhất — DB không mở cổng ra ngoài, kẻ cầm mật khẩu phải vào được VM trước | |

### 4.2 Xoay token R2 của ứng dụng — không gián đoạn quá 15 giây

Thứ tự **tạo mới → đổi → kiểm → mới thu hồi cũ**. Thu hồi trước là ảnh trên toàn site gãy ngay lập tức.

1. Cloudflare → R2 → *Manage R2 API Tokens* → *Create*: **Object Read & Write**, *Specify bucket* = bucket ứng dụng
   của staging. Lưu hai khóa vào kho bí mật nhóm.
2. Trên VM (user `deploy`):
   ```bash
   cd ~/app/deploy
   cp .env .env.truoc-xoay-r2 && chmod 600 .env.truoc-xoay-r2
   nano .env                                   # sửa đúng hai dòng R2__AccessKey, R2__SecretKey
   docker compose -f docker-compose.staging.apache.yml up -d --force-recreate api
   curl -fsS https://mxh.banhgao.net/health/ready
   ```
3. **Kiểm trên trình duyệt** (không phải `curl` — CORS chỉ lộ ra ở trình duyệt, bài học ISS-02 của GĐ2): đăng một
   bài kèm ảnh mới, và mở một bài **cũ** có ảnh — ảnh cũ phải hiện (URL đọc được ký lại bằng khóa mới).
4. Thu hồi token cũ trên dashboard. Mở lại bài cũ một lần nữa — vẫn hiện.
5. Xóa `.env.truoc-xoay-r2`.

### 4.3 Xoay mật khẩu Postgres

```bash
cd ~/app/deploy
S=docker-compose.staging.apache.yml
cp .env .env.truoc-xoay-pg && chmod 600 .env.truoc-xoay-pg
NEW="$(openssl rand -hex 24)"     # hex: không có ; = / + làm gãy chuỗi kết nối

# 1. Đổi trong DB — psql qua socket bên trong container, không cần mật khẩu cũ
docker compose -f $S exec -T postgres psql -U socialapp -d socialapp -v p="$NEW" <<'SQL'
ALTER ROLE socialapp PASSWORD :'p';
SQL

# 2. Đổi trong .env — HAI chỗ, và chỉ đúng hai chỗ
sed -i "s/^POSTGRES_PASSWORD=.*/POSTGRES_PASSWORD=$NEW/" .env
sed -i "/^ConnectionStrings__Postgres=/s/Password=[^;]*/Password=$NEW/" .env
grep -c "$NEW" .env               # phải là 2

# 3. api đọc lại .env
docker compose -f $S up -d --force-recreate api
curl -fsS https://mxh.banhgao.net/health/ready    # Healthy
unset NEW
```

Rồi đăng nhập thử trên UI. Lưu mật khẩu mới vào kho bí mật nhóm, **ghi nhãn ngày xoay**; xóa `.env.truoc-xoay-pg`.

**Ba điều hay làm sai ở đây:**

- **`sed` lệnh thứ hai thiếu phần `/^ConnectionStrings__Postgres=/`** → nó thay luôn chữ `Password=` trong
  `Smtp__Password=…` — mất khóa gửi mail, và lỗi chỉ lộ ra khi có người đăng ký mới.
- **Chỉ sửa `.env` mà không `ALTER ROLE`** → `POSTGRES_PASSWORD` chỉ có tác dụng lúc Postgres khởi tạo DB trống; DB
  đang có dữ liệu vẫn giữ mật khẩu cũ, và `api` không vào được DB nữa → `/health/ready` đỏ.
- **Quên hệ quả với bản sao cũ** → mọi bản backup tạo **trước** ngày xoay mang mật khẩu **cũ**. `restore.sh` không
  sao (dùng socket, không hỏi mật khẩu), nhưng bước `--migrate` của runbook dùng `.env` (mật khẩu mới) sẽ báo sai mật
  khẩu. Cách xử khi khôi phục bản cũ: đặt lại mật khẩu trong DB khôi phục bằng đúng lệnh `ALTER ROLE` ở bước 1, nhưng
  chạy với `-f docker-compose.restore.yml`. Đã ghi vào runbook.

### Kết quả mong đợi — checklist D3

- [ ] Sổ xoay khóa (4.1) đủ bốn dòng, cột cuối có ngày và người
- [ ] Brevo: không còn khóa SMTP / API nào đang hoạt động
- [ ] R2: không còn token nào tạo trước hoặc đúng 2026-09-04; ảnh cũ và ảnh mới đều hiện
- [ ] Postgres: đăng nhập UI được sau khi xoay; `backup.sh full` lần kế tiếp vẫn xanh
- [ ] Không còn file `.env.truoc-xoay-*` nằm lại trên VM

---

## 5. Đưa vào repo

Chỉ có file mẫu apache và tài liệu — **không có giá trị bí mật nào**. Commit theo `.claude/rules/commit-rules.md`:

```
cd(gd7-d): D1 + D2 — HSTS cho mọi đường, chặn hai endpoint demo lỗi của GĐ0 ở apache

Frontend đã tự gửi HSTS từ GĐ1 (next.config.ts); /api, /health, /swagger thì chưa. apache unset header của backend
rồi mới set, để trang frontend không nhận hai header. Swagger để mở có chủ đích (Đ-7.13); rà thấy khớp hợp đồng trừ
ping/boom (500) và ping/app-error (409) công khai — chặn ở apache, không sửa code, integration test giữ nguyên.

Test: curl năm đường đều đúng một header HSTS; ping 200, boom/app-error 403 từ Internet, 500/409 từ 127.0.0.1:18080.
detect-changes: <dán kết quả thật>
```

```
docs(gd7-d): D3 — sổ xoay khóa: Brevo thu hồi, R2 và Postgres đã xoay, JWT xác nhận từ GĐ1 F5
```

Cập nhật `README.md` Mục 1 nếu trạng thái GĐ7 có ghi ở đó.

---

## 6. Khối D để lại gì

| Di sản | Ai dùng |
|---|---|
| HSTS trên mọi đường | **GĐ8** — ZAP không còn gắn cờ thiếu HSTS trên API |
| Danh sách endpoint Swagger đã rà | **GĐ8** — điểm xuất phát của quét bảo mật; GĐ5/GĐ6 lặp lại D2 khi thêm nhóm mới |
| `ping/boom` chặn từ Internet | **Khối C** — số liệu tỷ lệ lỗi sạch; C5 bắn từ VM |
| Sổ xoay khóa | **Báo cáo** — bằng chứng xử lý sự cố lộ khóa 2026-09-04 |
