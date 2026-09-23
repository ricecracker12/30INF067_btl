# Hướng dẫn thực hiện — Khối B. Đồng hồ uptime (GĐ7, giờ đầu)

> Bản triển khai chi tiết của **B.3 Khối B** trong [giai-doan-7.md](giai-doan-7.md). Tài liệu gốc trả lời
> *cái gì* và *vì sao* (Đ-7.2, Đ-7.3); tài liệu này trả lời *gõ lệnh nào, bấm chỗ nào, và nhìn vào đâu để
> biết đã xong thật*.
>
> **Nguồn sự thật vẫn là `giai-doan-7.md`** (Mục 5.3 ngưỡng cảnh báo, Mục 12 checklist, B.3, B.9). Chỗ nào
> tài liệu này lệch thì sửa ở đây.

| | |
|---|---|
| **Người làm** | Một người, có SSH vào VM |
| **Thời lượng** | ~1 giờ (B1 30 phút · B2 10 phút · B3 20 phút) |
| **Khối này cần trước** | Không gì cả — đây là lý do nó đứng đầu |
| **Khối này chặn** | GOAL-04 (uptime ≥ 99%); C5 (cảnh báo dùng chung kênh của B3) |

---

## 0. Danh sách công việc — mục tiêu và kết quả mong đợi

| Mã | Đầu việc | Mục tiêu — việc này tồn tại để làm gì | Kết quả mong đợi — thứ kiểm chứng được |
|---|---|---|---|
| **B1** | Uptime Kuma trong stack ops | Bắt đầu **tích lũy** lịch sử uptime từ hôm nay; có chỗ chẩn đoán từng dịch vụ | `docker compose -f docker-compose.ops.yml ps` → `uptime-kuma` Up; qua SSH tunnel thấy 3 monitor xanh; **stop `api` → đỏ trong ≤ 2 phút** |
| **B2** | Monitor ngoài làm trọng tài | Có con số uptime **không nằm trên máy bị đo** (Đ-7.3) — con số báo cáo lấy từ đây | Một dịch vụ ngoài theo dõi `/health/ready`, có lịch sử; nhận được cảnh báo khi dừng thật |
| **B3** | Kênh nhận cảnh báo | Một kênh duy nhất, cả nhóm cùng nhận; B1, B2 và C5 sau này đều đổ về đây | Tin thử tới **cả ba người**; ảnh chụp tin "DOWN" và tin "UP" thật lưu ở `bang-chung/` |

### Thứ tự thực thi

```
B1 ─→ B3 ─→ B2
      └── B3 trước B2 vì monitor ngoài cũng cần kênh này để gửi cảnh báo
```

Ba đầu việc đều thao tác trên VM hoặc trên dashboard bên thứ ba, **không chạm code**. Phần vào repo chỉ có
`deploy/docker-compose.ops.yml` và thư mục bằng chứng.

---

## 1. Trước khi bắt đầu (5 phút)

| # | Kiểm | Kỳ vọng |
|---|---|---|
| 1 | SSH vào VM được bằng user `deploy` (hoặc user thường rồi `sudo -iu deploy`) | Vào được `~/app/deploy/`, thấy `docker-compose.staging.apache.yml` + `.env` |
| 2 | Staging đang sống | `curl -fsS https://mxh.banhgao.net/health/ready` → `Healthy` |
| 3 | Đĩa còn chỗ | `df -h /` — Kuma nhẹ (~100MB), nhưng nên biết mốc trước khi khối A đổ backup vào |
| 4 | **Báo nhóm** | Bước nghiệm thu B1 sẽ **dừng `api` staging ~2 phút** — ai đang test GĐ2 trên staging cần biết |

**Luật áp vào khối này** (từ `giai-doan-7.md`):
- Mọi thao tác trên VM dưới user `deploy` — không tạo file trong `/home/deploy` bằng `ubuntu`/`sudo` (sai chủ sở hữu).
- Kuma **không** mở ra Internet: chỉ `127.0.0.1:3002` trên VM, xem qua SSH tunnel (Đ-7.7, R7-06).
- Không commit token Telegram / mật khẩu Kuma vào repo (luật vàng 3). Chúng chỉ sống trong volume Kuma và
  trong dashboard bên thứ ba.

---

## 2. B1 — Uptime Kuma trong stack ops

### Mục tiêu

Đồng hồ bắt đầu chạy. Mọi phút trước khi bước này xong là một phút **không có** trong bằng chứng GOAL-04.

### Việc phải làm

**Bước 1 — Đưa file compose lên VM, vào thư mục RIÊNG `~/app/ops/`.** File đã có ở
[`deploy/docker-compose.ops.yml`](../../deploy/docker-compose.ops.yml). Stack ops **không đi qua CD** (có trạng
thái, hiếm khi đổi), nên chép tay:

```bash
# từ máy dev, trong thư mục mxh/
ssh deploy@<staging-host> mkdir -p app/ops
scp deploy/docker-compose.ops.yml deploy@<staging-host>:app/ops/
```

Nếu bạn SSH bằng user thường (không phải `deploy`), chép vào `/tmp` rồi trên VM `sudo -iu deploy` và
`cp /tmp/docker-compose.ops.yml ~/app/ops/` — để file thuộc về `deploy`.

> **Vì sao không để chung `~/app/deploy/`:** Compose tự nạp `.env` **nằm cùng thư mục với file compose**. Để
> chung thì stack ops đọc `.env` chứa secret của staging — hiện vô hại vì ops.yml chưa dùng biến nào, nhưng
> khối C thêm Grafana có `${...}` là bắt đầu lẫn lộn. *(CD thì không phải lo: nó chỉ `scp` đúng một file của
> staging, và `--remove-orphans` chỉ với tới container **cùng project**.)*
>
> Đã lỡ để chung thì chuyển được, **không cần `down`**: `mkdir -p ~/app/ops && mv ~/app/deploy/docker-compose.ops.yml ~/app/ops/`
> rồi `cd ~/app/ops && docker compose -f docker-compose.ops.yml up -d`. Volume `socialapp-ops_kuma-data` sinh
> theo **project name** (ghim bằng `name:` trong file), không theo thư mục — nên đổi chỗ không mất dữ liệu.

**Bước 2 — Kiểm image có arm64** (luật vàng 8), rồi bật:

```bash
# trên VM, dưới user deploy
cd ~/app/ops
docker manifest inspect louislam/uptime-kuma:1 | grep -c arm64      # phải ≥ 1
docker compose -f docker-compose.ops.yml up -d
docker compose -f docker-compose.ops.yml ps                        # uptime-kuma: Up (healthy)
curl -s -o /dev/null -w "%{http_code}\n" http://127.0.0.1:3002     # 200
```

> **Cổng:** VM đã có người dùng `3001`, nên Kuma publish ở **host 3002**. Bên trong container Kuma vẫn nghe
> `3001` — đó là lý do ánh xạ là `3002:3001`, không phải `3002:3002`. Đổi vế phải thành 3002 thì không có ai
> nghe ở đó và `curl` trả *connection reset*, triệu chứng trông hệt như container chết.
>
> Trước khi `up`, xác nhận 3002 cũng trống: `ss -ltnp | grep -E ':(3001|3002)\b'` — chỉ được thấy 3001.

**Bước 3 — Mở giao diện qua SSH tunnel** (trên máy dev, để terminal này mở suốt lúc cấu hình):

```bash
ssh -N -L 3002:127.0.0.1:3002 <user>@<staging-host>
```

*(Số bên trái là cổng trên **máy bạn** — đổi được nếu máy bạn cũng bận 3002; số bên phải phải là 3002, cổng Kuma
publish trên VM.)*

Rồi mở trình duyệt: `http://localhost:3002`. Lần đầu Kuma hỏi **tạo tài khoản admin** — làm ngay, đặt mật khẩu
mạnh, lưu vào chỗ giữ bí mật của nhóm. *(Trang tạo admin mở cho bất kỳ ai tới được cổng — vì vậy cổng chỉ bind
localhost; đừng bao giờ đổi thành `0.0.0.0` "cho tiện".)*

**Bước 4 — Tạo ba monitor** (nút *Add New Monitor*), thông số giống nhau: **Heartbeat Interval 60s**,
**Retries 1**, **Retry Interval 20s**:

| Tên | Loại | URL / cấu hình | Vì sao |
|---|---|---|---|
| `staging · api ready` | **HTTP(s) – Keyword** | `https://mxh.banhgao.net/health/ready` · keyword `Healthy` | Đây là monitor **chính**. Keyword mạnh hơn status code: apache trả 200 HTML lỗi thì vẫn bị bắt |
| `staging · api ping` | HTTP(s) | `https://mxh.banhgao.net/api/v1/ping` · expect 200 | Chứng minh route `/api` không bị Next nuốt (cạm bẫy ProxyPass đã ghi từ GĐ1) |
| `staging · frontend` | HTTP(s) | `https://mxh.banhgao.net/login` · expect 200 | Frontend chết mà API sống thì người dùng vẫn không dùng được |

Staging là môi trường cuối (Đ-7.4, sửa 2026-09-23) nên **ba monitor này chính là thứ báo cáo** — không có monitor
production nào thêm sau.

### Kết quả mong đợi — checklist nghiệm thu B1

- [ ] `docker compose -f docker-compose.ops.yml ps` → `uptime-kuma` **Up**, restart policy `unless-stopped`
- [ ] Ba monitor xanh liên tục ≥ 5 phút
- [ ] **Phá thử:** `docker compose -f docker-compose.staging.apache.yml stop api` → monitor `api ready` và
      `api ping` **đỏ trong ≤ 2 phút**; `frontend` có thể vẫn xanh (đúng — Next vẫn phục vụ trang login)
- [ ] `docker compose -f docker-compose.staging.apache.yml start api` → xanh lại trong ≤ 2 phút
- [ ] Ảnh chụp lúc đỏ và lúc xanh lại → `docs/giai-doan-7/bang-chung/B1-kuma-down-up.png`
- [ ] Tắt tunnel, xác nhận `https://mxh.banhgao.net:3002` và `http://<ip>:3002` **không** vào được

### Cạm bẫy

- **Chung project với stack ứng dụng.** Đặt Kuma vào `docker-compose.staging.apache.yml` "cho gọn" thì
  `--remove-orphans` của CD sẽ xóa nó ở lần deploy kế tiếp, và mỗi `up -d` là một lần đồng hồ tự tắt.
- **Probe `/` thay vì `/health/ready`.** Trang `/` là Next — backend chết nó vẫn 200. Monitor chính phải là
  `/health/ready` với keyword.
- **Cloudflare cache.** `/health/ready` không có phần mở rộng tĩnh nên Cloudflare mặc định không cache; nhưng
  nếu ai bật *Cache Everything* hoặc *Always Online* cho domain thì Kuma sẽ thấy xanh giả. Kiểm bằng header
  `cf-cache-status: DYNAMIC` trong `curl -I`.
- **Docker DNS trên OCI.** Kuma probe qua domain công khai nên không phụ thuộc DNS nội bộ của Docker — nhưng
  nó cần resolve `mxh.banhgao.net`. Nếu `getaddrinfo EAGAIN` tái phát (tiền sử GĐ0B), Kuma báo đỏ dù site
  vẫn sống → đó là lúc nhìn sang B2 để biết ai đúng.

---

## 3. B3 — Kênh nhận cảnh báo *(làm trước B2)*

### Mục tiêu

**Một** kênh, cả ba người cùng nhận, dùng chung cho Kuma (B1), monitor ngoài (B2) và Grafana (C5). Kênh chưa
bao giờ kêu là kênh chưa tồn tại.

### Cách thực thi — Telegram (khuyến nghị)

Chọn Telegram vì: miễn phí, tức thì, cả nhóm thấy cùng một tin, và cả Kuma lẫn UptimeRobot lẫn Grafana đều hỗ
trợ sẵn. Email qua Resend là phương án dự phòng (Kuma có loại thông báo SMTP; dùng lại `Smtp__*` trong `.env`).

1. Trong Telegram, chat với **@BotFather** → `/newbot` → đặt tên → nhận **bot token** (dạng `123456:ABC-…`).
   Token này là bí mật — không dán vào repo, không dán vào chat nhóm công khai.
2. Tạo **group** "SocialApp alerts", thêm cả ba người và thêm bot vào group.
3. Gửi một tin bất kỳ trong group, rồi mở trong trình duyệt:
   `https://api.telegram.org/bot<token>/getUpdates` → tìm `"chat":{"id":-100…}` — đó là **chat id** (số âm).
4. Trong Kuma: *Settings → Notifications → Setup Notification* → loại **Telegram** → dán token + chat id →
   bấm **Test** → tin thử phải tới group. Tick **Default enabled** và **Apply on all existing monitors**.
5. Mỗi monitor: mở ra, xác nhận thông báo Telegram đã bật.

### Kết quả mong đợi — checklist nghiệm thu B3

- [ ] Tin **Test** tới group; cả ba người xác nhận đã thấy
- [ ] Lặp lại phép phá thử của B1 → group nhận tin **DOWN** rồi tin **UP**, có giờ
- [ ] Ảnh chụp hai tin đó → `docs/giai-doan-7/bang-chung/B3-telegram-down-up.png`
- [ ] Ghi vào runbook (khối A4 sẽ tạo file; tạm thời ghi ở cuối tài liệu này, Mục 6): tên bot, ai giữ token,
      chat id lấy lại bằng cách nào

### Cạm bẫy

- **Bot chưa được thêm vào group** → `getUpdates` trống, và Test trong Kuma lỗi `chat not found`.
- **Chat id thiếu dấu trừ** — group id luôn là số âm; chép thiếu dấu là gửi vào hư không mà không báo lỗi rõ.
- **Chỉ bật thông báo cho monitor đang mở** — quên *Apply on all existing monitors* thì hai monitor còn lại
  im lặng khi đỏ.

---

## 4. B2 — Monitor ngoài làm trọng tài

### Mục tiêu

Con số uptime **không nằm trên máy bị đo** (Đ-7.3). VM chết thì Kuma chết cùng và không ghi gì — chỉ monitor
ngoài mới thấy khoảng đó. **Con số đưa vào báo cáo lấy từ đây**, Kuma chỉ để chẩn đoán.

### Cách thực thi — UptimeRobot (gói miễn phí)

Bất kỳ dịch vụ nào tương đương đều được (Better Stack, Cloudflare Health Check nếu gói cho phép); dưới đây lấy
UptimeRobot vì gói miễn phí đủ dùng: 50 monitor, chu kỳ 5 phút, cảnh báo email + Telegram.

1. Tạo tài khoản bằng **email nhóm** (không phải email cá nhân — người rời nhóm thì tài khoản vẫn còn).
2. *Add New Monitor* → **HTTP(s)** → URL `https://mxh.banhgao.net/health/ready` → tên `SocialApp staging ready`
   → interval **5 phút** (giới hạn gói miễn phí; đủ cho con số tháng).
   Nếu dịch vụ có loại **Keyword**, dùng keyword `Healthy` như Kuma.
3. *Alert Contacts*: gắn **email nhóm** vào monitor.
   > **Thực tế thi công (2026-09-23):** gói miễn phí của UptimeRobot **không cho gửi Telegram** (yêu cầu premium),
   > nên monitor ngoài chỉ báo qua email. Chấp nhận được: vai trò của nó là **nguồn con số uptime** (Đ-7.3), còn
   > cảnh báo tức thì đã có Kuma → Telegram. Điều kiện đi kèm: email phải là **hộp thư nhóm** cả ba người đọc —
   > nếu VM chết cả máy thì Kuma im lặng cùng, và email này là **kênh duy nhất** còn báo được.
4. Monitor này theo dõi staging — môi trường cuối — nên **chính nó là con số báo cáo** (Đ-7.4).

### Kết quả mong đợi — checklist nghiệm thu B2

- [ ] Monitor ngoài xanh, có ≥ 3 lần kiểm liên tiếp
- [ ] Phá thử lần nữa (stop `api` **≥ 6 phút** để chắc chắn rơi vào ít nhất một chu kỳ 5 phút) → nhận cảnh
      báo từ dịch vụ ngoài; start lại → nhận tin phục hồi
- [ ] Ảnh chụp → `docs/giai-doan-7/bang-chung/B2-monitor-ngoai-down-up.png`
- [ ] Ghi vào Mục 6: dịch vụ nào, tài khoản nào, ai có quyền vào, con số uptime xem ở trang nào

### Cạm bẫy

- **Phá thử quá ngắn.** Chu kỳ 5 phút: dừng 2 phút có thể lọt khe giữa hai lần kiểm và không thấy gì — không
  phải monitor hỏng. Dừng ≥ 6 phút và báo nhóm trước.
- **Đăng ký bằng email cá nhân** rồi bận/rời nhóm → mất luôn lịch sử uptime, tức mất bằng chứng.
- **Hai nguồn lệch nhau** (Kuma đỏ, ngoài xanh hoặc ngược lại) → **tin nguồn ngoài**, ghi lý do lệch vào biên
  bản. Lệch thường gặp nhất: Docker DNS trên VM hỏng (Kuma đỏ giả), hoặc Cloudflare cache (ngoài xanh giả).

---

## 5. Đưa vào repo

Phần vào repo chỉ có file compose và thư mục bằng chứng. Commit theo `.claude/rules/commit-rules.md`:

```
feat(gd7-b): B1 — stack ops với Uptime Kuma, tách project khỏi stack ứng dụng

Đồng hồ uptime bật trước mọi việc khác của GĐ7 (Đ-7.2, Đ-7.3): GOAL-04 đo bằng thời gian
tích lũy, bật muộn là mất bằng chứng không bù được. Project riêng để deploy stack ứng dụng
không kéo đồng hồ xuống theo. Chỉ bind 127.0.0.1, xem qua SSH tunnel.

Test: phá thử stop/start api staging — Kuma đỏ ≤ 2 phút, xanh lại ≤ 2 phút; Telegram nhận DOWN/UP
detect-changes: low, 0 luồng
```

Bằng chứng (ảnh) commit cùng: `docs/giai-doan-7/bang-chung/B1-*.png`, `B2-*.png`, `B3-*.png`. Không commit
token, mật khẩu Kuma, hay ảnh có lộ token trong khung hình.

Sau khi commit, cập nhật `README.md` Mục 1: dòng GĐ7 ghi **"Khối B (đồng hồ uptime) đang chạy từ <ngày>"** —
để người đọc README biết đồng hồ đã bắt đầu từ bao giờ.

---

## 6. Ghi chép vận hành (tạm — chuyển vào runbook ở A4)

| Khoản | Giá trị | Ai giữ |
|---|---|---|
| Kuma — địa chỉ | `127.0.0.1:3002` trên VM (3001 đã có người dùng), qua `ssh -L 3002:127.0.0.1:3002` | |
| Kuma — tài khoản admin | *(trong kho bí mật nhóm, không ghi ở đây)* | |
| Telegram — tên bot | | |
| Telegram — group | | |
| Monitor ngoài — dịch vụ / tài khoản | | |
| Monitor ngoài — trang xem uptime | | |
| Ngày đồng hồ bắt đầu chạy | | |

**Dòng cuối cùng là dòng quan trọng nhất của bảng** — nó là mốc mà con số uptime trong báo cáo tính từ đó.

---

## 7. Khối B để lại gì

| Di sản | Ai dùng |
|---|---|
| `docker-compose.ops.yml` project `socialapp-ops` | **Khối C** thêm prometheus/grafana/node-exporter vào đúng file này |
| Kênh Telegram | **C5** — Grafana alerting đổ về cùng group |
| Ba monitor staging | **Báo cáo GOAL-04** — staging là môi trường cuối (Đ-7.4) |
| Ngày bắt đầu đồng hồ | **F2** — mốc tính con số GOAL-04 trong báo cáo |
