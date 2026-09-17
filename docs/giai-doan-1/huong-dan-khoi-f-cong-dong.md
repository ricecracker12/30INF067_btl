# Hướng dẫn thực hiện — Khối F. Cổng đóng (GĐ1)

> Bản triển khai chi tiết của **B.8 Khối F** trong [giai-doan-1.md](giai-doan-1.md). Tài liệu gốc trả lời
> *cái gì* và *vì sao*; tài liệu này trả lời *làm theo thứ tự nào, kiểm gì, và nhìn vào đâu để biết đã
> xong thật*.
>
> **Nguồn sự thật vẫn là `giai-doan-1.md`** (Mục 11 Definition of Done, Mục 12 checklist nghiệm thu,
> B.8, B.9, B.11), [oci-setup.md](../oci-setup.md), và phần "Chuyển cho F1" ở cuối Mục 9 của
> [huong-dan-khoi-e-frontend.md](huong-dan-khoi-e-frontend.md). Chỗ nào tài liệu này lệch thì sửa ở đây.

> **Trạng thái (2026-09-17):** khối D và E đã merge `develop`. **F1 — code trong repo** (CD + compose +
> `.env.example` + apache mẫu). Trước khi merge F1 vào `develop`: bổ sung biến BFF/`ReverseProxy__*` trên
> VPS và bật `ProxyPass /` (một lần). F2–F7 chưa mở. Không chia lane: `F1 → F2 → … → F7`.

| | |
|---|---|
| **Người làm** | Cả nhóm — không chia lane |
| **Thời lượng** | Ngày 6 |
| **Khối này chặn** | Toàn bộ **GĐ2** (không bắt đầu khi hợp đồng chưa đóng băng) |
| **Khối này cần trước** | **D** (endpoint + cookie/CORS + revocation) và **E** (UI auth + BFF + image FE standalone) |

---

## 0. Danh sách công việc — mục tiêu và kết quả mong đợi

Bảy đầu việc, **một lane duy nhất**, thứ tự tuyến tính. Đây là ranh giới giữa "code chạy" và "giai đoạn
hoàn thành": thiếu bất kỳ đầu nào thì **chưa tuyên bố GĐ1 xong**, dù CI xanh và máy local chạy được.

| Mã | Đầu việc | Mục tiêu — việc này tồn tại để làm gì | Kết quả mong đợi — thứ kiểm chứng được |
|---|---|---|---|
| **F1** | Deploy staging qua CD tự động (api + frontend) | Loại bỏ "chạy được trên máy tôi" khỏi định nghĩa xong — sản phẩm chỉ được coi là sống khi lên domain HTTPS thật bằng đường CD, không SSH sửa tay | Merge/`push` `develop` → CD build **hai** image arm64 (`api:staging`, `frontend:staging`) → GHCR → `migrate` rồi `up`; `curl -fsS https://mxh.banhgao.net/health/ready` → 200; `https://mxh.banhgao.net/login` → 200 + header CSP có nonce; `/api/v1/ping` vẫn JSON của API (apache không nuốt `/api` vào Next) |
| **F2** | FE trên staging chạy BFF với dữ liệu thật | Đóng rủi ro "xanh trên mock / máy local, đỏ trên staging" — nghiệm thu chỉ trên hệ thống thật | Trình duyệt **chỉ** gọi `/bff/*` cùng origin; không MSW, không `NEXT_PUBLIC_*` API URL; ba màn auth (đăng ký / xác minh / đăng nhập) + `/me` chạy trên staging; DevTools: không có `Authorization` ra trình duyệt, Web Storage không chứa access token |
| **F3** | E2E-01 — lát cắt dọc trên trình duyệt thật | Kiểm những thứ integration test không chạm: cookie phiên BFF, CORS/proxy cùng origin, mail thật, refresh sau hết hạn | Đăng ký → mail Brevo vào hộp thư thật → bấm link xác minh → đăng nhập → `/me` → ép hết hạn access (đợi TTL hoặc hạ TTL tạm) → request kế tiếp vẫn thành công nhờ refresh; ghi lại bằng chứng (ảnh/log/PR) |
| **F4** | E2E-02 — single-flight dưới tải đồng thời | Đóng rủi ro đăng xuất oan khi nhiều tab cùng 401 — triệu chứng trông hệt lỗi backend | 3 tab cùng phiên, ép token hết hạn, quan sát **đúng một** `POST …/auth/refresh` (log api hoặc Playwright `single-flight`); không tab nào bị đá về `/login`; reuse detection **không** kích hoạt |
| **F5** | Checklist nghiệm thu (Mục 12) | Rà bốn nhóm Bảo mật / Phân quyền / Dữ liệu / Vận hành bằng cách **kiểm tận nơi**, không suy đoán từ CI xanh | Mọi dòng Mục 12 được tick **hoặc** ghi lý do hoãn + địa chỉ hoãn (vd `revoked:user` ghi → GĐ6); có dòng bắt buộc psql / DevTools / code review |
| **F6** | Definition of Done (Mục 11) | Chốt bằng tiêu chuẩn chung của dự án, không bằng cảm giác "chắc xong rồi" | Cả **7** mục Mục 11 tick; đặc biệt: đã chạy thử staging bằng tài khoản thật + lát cắt trình duyệt thật (phụ thuộc F1–F4) |
| **F7** | Đóng băng hợp đồng API + bàn giao | GĐ2 khởi động trên nền ổn định — không phải trên hợp đồng còn đang đổi giữa chừng | Thông báo nhóm: `identity-v1.yaml` đóng băng cho GĐ1; ghi phần hoãn có địa chỉ + điều dễ hiểu nhầm ("vai trò đóng dấu trong token → đổi role không hiệu lực ngay"); **GĐ2 được phép bắt đầu** |

### Thứ tự thực thi

```
F1 ─→ F2 ─→ F3 ─→ F4 ─→ F5 ─→ F6 ─→ F7
              └───────┘
           có thể gối nhẹ
           (F4 không cần F3 xong hết)
```

- **`F1` trước mọi thứ.** Không có staging HTTPS + FE cùng domain thì F2–F4 vô nghĩa.
- **`F2` ngay sau F1.** Xác nhận biến BFF trên server đúng trước khi đốt thời gian vào E2E dài.
- **`F3` và `F4`:** F4 chỉ cần F2 + E7; có thể chạy song song với phần sau của F3 nếu đủ người, nhưng **không** tick F5 khi còn đỏ.
- **`F5` trước `F6`:** DoD Mục 11 là chốt tổng; checklist Mục 12 là bằng chứng chi tiết nuôi các tick đó.
- **`F7` cuối cùng.** Đóng băng sớm khi còn mục đỏ = GĐ2 dựng trên nền lung lay.

**Ba điều kiện tuyên bố GĐ1 xong** (B.11) — thiếu một là chưa xong:

1. Chạy trên staging, domain HTTPS, deploy bằng CD — không phải máy local (F1, F3).
2. Frontend không nghiệm thu trên mock; thao tác trên dữ liệu thật (F2).
3. CI xanh đủ nhóm: unit + integration, AuthZ matrix, hợp đồng API, ArchUnitNET (+ job frontend).

---

## 1. Trước khi mở cổng đóng

### Điều kiện cần (cả nhóm xác nhận trong 10 phút)

| # | Kiểm | Kỳ vọng |
|---|---|---|
| 1 | Khối D + E đã có trên nhánh sẽ merge `develop` | Endpoint auth, cookie/CORS, BFF, image FE `standalone` arm64 |
| 2 | CI trên PR/`develop` | Xanh: test BE, `Category=AuthZ`, `Category=Contract`, ArchUnitNET, job `frontend` |
| 3 | `deploy/.env` **trên VPS staging** (không commit) | Đủ khóa ở mục F1 bên dưới — app **fail-fast** nếu thiếu |
| 4 | Brevo | `Smtp__From` là người gửi đã xác thực; còn hạn mức gửi trong ngày |
| 5 | Cloudflare / VM | Chỉ mở 443 cho dải IP Cloudflare (bẫy `TRUSTED_PROXY_HOPS`) |

### Luật áp vào khối này

1. **Không thao tác tay trên VPS để "vá cho xanh"** — sửa trong repo + CD. SSH chỉ để đọc log, kiểm `.env`, `docker compose ps`.
2. **Không nghiệm thu trên mock hoặc chỉ trên `localhost`.** Mục 12 cấm rõ.
3. **Không sửa `identity-v1.yaml` ở cổng đóng** trừ lỗi hợp đồng blocking đã thống nhất cả nhóm — mọi đổi hình dạng API sau F7 thuộc GĐ2 + cổng mở giai đoạn đó.
4. **Không commit secret.** `SESSION_ENCRYPTION_KEY`, `Smtp__Password`, `Jwt__SigningKey` chỉ trên server.
5. Trước commit liên quan CD/compose: `node .gitnexus/run.cjs detect-changes --scope all --repo .`.

---

## 2. F1 — Deploy staging qua CD tự động

### Mục tiêu

Chứng minh hệ thống sống trên Internet theo đúng đường vận hành chính thức.

### Việc phải làm (phần lớn chuyển từ E8)

Checklist gốc nằm ở "Chuyển cho F1" trong hướng dẫn khối E — tóm tắt thi công:

1. **CD** (`.github/workflows/deploy-staging.yml`): build + push `api:staging` **và**
   `frontend:staging` (`context: src/frontend`, `linux/arm64`); trước `up` thì **SCP**
   `docker-compose.staging.apache.yml` lên `~/app/deploy/` (compose trên VPS không nằm trong image).
2. **Compose staging** (`deploy/docker-compose.staging.apache.yml`): service `frontend` —
   image trên, `restart: unless-stopped`, `ports: ["127.0.0.1:3000:3000"]`, `networks: [internal]`
   (subnet cố định `172.28.0.0/16`), `env_file: [./.env]`, `depends_on: [api, redis]`.
   Biến thể Caddy: cùng service + `Caddyfile` path-split API/FE.
3. **`.env` trên VPS** — bổ sung / xác nhận **trước khi merge** (CD không sửa `.env`):

   | Biến | Ghi chú |
   |---|---|
   | `ConnectionStrings__Postgres`, `__Redis` | Bắt buộc — thiếu = crash-loop |
   | `Jwt__SigningKey` | ≥ 32 byte |
   | `Cors__AllowedOrigins__0` | `https://mxh.banhgao.net` |
   | `Frontend__BaseUrl` | `https://mxh.banhgao.net` (link mail xác minh) |
   | `Smtp__Host` / `Port` / `User` / `Password` / `From` | Brevo; `From` đã xác thực |
   | `API_INTERNAL_URL` | `http://api:8080/api/v1` (FE/BFF) |
   | `REDIS_URL` | `redis://redis:6379` |
   | `APP_ORIGIN` | `https://mxh.banhgao.net` |
   | `SESSION_ENCRYPTION_KEY` | `openssl rand -base64 32` — **sinh riêng staging** |
   | `TRUSTED_PROXY_HOPS` | `1` |
   | `ReverseProxy__TrustedNetworks__0` | `172.28.0.0/16` (khớp subnet compose) |

4. **Apache trên VM (một lần):** bật `ProxyPass /` → `127.0.0.1:3000`, **giữ sau** `/api`, `/swagger`, `/health`
   — copy từ `deploy/apache-socialapp.conf.example`, `apache2ctl configtest` + `reload`. CD **không** sửa apache.
5. Merge vào `develop` → chờ CD. **Không** `docker compose` tay thay cho CD trừ khi đang sửa chính workflow.

### Kết quả mong đợi — checklist nghiệm thu F1

- [ ] Job `deploy-staging` xanh trên GitHub Actions
- [ ] `docker compose … ps` trên VPS: `migrate` đã chạy; `api`, `frontend`, `postgres`, `redis` healthy
- [ ] `curl -fsS https://mxh.banhgao.net/health/ready` → 200
- [ ] `https://mxh.banhgao.net/login` → 200, header `Content-Security-Policy` có `nonce-…`
- [ ] `https://mxh.banhgao.net/api/v1/ping` (hoặc endpoint health API tương đương) → JSON API, không phải HTML Next
- [ ] Đăng nhập sai ~11 lần từ máy A → 429; máy B vẫn login được (trusted proxy / rate limit theo user thật)

### Cạm bẫy

| # | Bẫy | Hệ quả |
|---|---|---|
| 1 | Thiếu biến trong `.env` staging | Crash-loop; chỗ image cũ từng boot được dễ bị hiểu nhầm là "CD hỏng" |
| 2 | `ProxyPass /` đặt **trước** `/api` | Mọi API thành 404 HTML của Next |
| 3 | `frontend` không vào mạng `internal` | BFF 502/503 tới `api`/`redis` |
| 4 | Quên `ReverseProxy__TrustedNetworks__0` | Mọi user sau NAT/Cloudflare ăn chung hạn mức auth 10 req/phút |
| 5 | `Smtp__From` chưa xác thực Brevo | Đăng ký 201 nhưng không có mail → F3 chết |

---

## 3. F2 — Frontend trên staging, dữ liệu thật

### Mục tiêu

Đóng khoảng cách mock/dev ↔ hiện thực staging trước khi viết biên bản E2E.

### Cách thực thi (cập nhật sau Đ-E7 / Đ-E14)

- Mock trình duyệt **đã gỡ** khỏi app (Đ-E7) — F2 **không** còn bước "tắt MSW".
- Trình duyệt **không** cấu hình base URL API (Đ-E14) — chỉ gọi `/bff/*` cùng origin.
- Việc còn lại: biến server BFF trên staging đúng (đã liệt kê ở F1) + smoke ba màn trên domain thật.

### Kết quả mong đợi — checklist nghiệm thu F2

- [ ] Mở `https://mxh.banhgao.net/register` — form hoạt động, không lỗi hydrate/CSP chặn script
- [ ] Đăng ký một tài khoản thử → 201 đường BFF (Network: `/bff/...`, không gọi thẳng host API khác origin)
- [ ] Tab Network: **không** header `Authorization` từ trình duyệt; Application → Local/Session Storage **không** access token
- [ ] Cookie phiên BFF (`__Host-sid` hoặc tên đang dùng) là `HttpOnly` / `Secure` / cùng site
- [ ] `/login` → `/me` sau khi đã verify (có thể dùng user seed/test nếu mail chậm — nhưng F3 vẫn bắt buộc mail thật)

**Cấm:** tick F2 chỉ vì Playwright xanh trên `localhost`.

---

## 4. F3 — E2E-01: lát cắt dọc trình duyệt thật

### Mục tiêu

Một đường FR-001 → FR-002 trên HTTPS + mail thật + refresh sau hết hạn.

### Kịch bản (ghi bằng chứng vào PR / biên bản Ngày 6)

1. Trình duyệt thật (Chrome/Firefox), cửa sổ sạch / profile sạch.
2. Đăng ký email **nhóm kiểm được** (Brevo gửi được).
3. Mở hộp thư → click link `{Frontend__BaseUrl}/verify-email?token=…`.
4. Đăng nhập → vào `/me` thấy đúng email / role.
5. Ép access hết hạn (đợi đủ TTL staging, hoặc hạ `Jwt__AccessTokenSeconds` tạm trên staging **chỉ** cho phiên kiểm rồi trả lại — ghi rõ nếu đổi).
6. Thao tác lại (reload `/me` hoặc hành động gọi BFF) → vẫn đăng nhập; có dấu hiệu refresh phía server (một `POST /api/v1/auth/refresh` trong log api).

### Kết quả mong đợi

- [ ] Chạy xuyên suốt không lỗi trên `https://mxh.banhgao.net`
- [ ] Ảnh hoặc ghi chép: mail Brevo + URL xác minh + màn `/me` sau login + request sau hết hạn thành công
- [ ] Cookie / proxy: không lỗi CORS (cùng origin qua apache thì preflight cross-origin không còn là điểm nóng — vẫn xác nhận không 401 oan)

Có thể tái sử dụng Playwright với `PLAYWRIGHT_BASE_URL=https://mxh.banhgao.net` nếu đã có spec; **bước mail phải là hộp thư thật**, không Mailpit.

---

## 5. F4 — E2E-02: single-flight đồng thời

### Mục tiêu

Chứng minh nhiều tab cùng 401 không tự kích hoạt reuse detection.

### Cách thực thi

- **Ưu tiên:** `pnpm exec playwright test single-flight` với `PLAYWRIGHT_BASE_URL=https://mxh.banhgao.net` (và cấu hình TTL ngắn nếu spec yêu cầu — trên staging cân nhắc cửa sổ bảo trì ngắn hoặc chạy lại trên compose staging local nếu không hạ TTL production).
- **Bổ sung tay:** 3 tab cùng phiên đã login → chờ/ép hết hạn → thao tác đồng thời trên cả 3 → không ai về `/login`.

Đếm refresh: log container `api` hoặc instrumentation trong spec E7 (hướng dẫn E, Mục 8A) — **đúng 1** lần refresh cho một đợt hết hạn.

### Kết quả mong đợi

- [ ] Đúng một lời gọi refresh cho một đợt tranh chấp
- [ ] Không tab nào mất phiên
- [ ] Không có chuỗi refresh family bị thu hồi vì reuse (DB `refresh_tokens` / hành vi 401 hàng loạt)

---

## 6. F5 — Checklist nghiệm thu (Mục 12)

### Mục tiêu

Biên bản nghiệm thu có dấu tick kèm cách đã kiểm — không "tin CI".

### Cách thực thi

Mở [giai-doan-1.md](giai-doan-1.md) Mục 12. Với mỗi dòng:

| Nhóm | Việc tiêu biểu | Cách kiểm |
|---|---|---|
| Bảo mật | BCrypt cost 12; token chỉ lưu băm | `psql` đọc `password_hash` / `token_hash` |
| Bảo mật | JWT key không trong repo/image | `git grep` + `docker history` / inspect env image |
| Phân quyền | 401 `/me` không token; fallback policy; Admin short-circuit | Gọi tay hoặc nhìn lại AuthZ matrix + một thử `DELETE role_permissions` |
| Phân quyền | TTL `revoked:user` = access + skew; thứ tự DB→Redis | Redis TTL + **code review** (máy không bắt được đảo thứ tự) |
| Dữ liệu | Seeder idempotent; FK RESTRICT; đổi `roles.code` → từ chối boot | Lặp seeder / SQL / restart có chủ đích trên **dev hoặc staging có cửa sổ** |
| Vận hành | CD, mail thật, bỏ mock, single-flight, CI gates | F1–F4 + GitHub Actions |

### Kết quả mong đợi

- [ ] Mọi dòng Mục 12: tick **hoặc** `HOÃN → <giai đoạn/mã việc>` (vd bất biến "≥ 1 Admin" → GĐ6/GĐ8)
- [ ] Không còn dòng "sẽ kiểm sau" không địa chỉ

---

## 7. F6 — Definition of Done (Mục 11)

### Mục tiêu

Bảy câu hỏi đóng/mở — đủ thì được phép nói "GĐ1 xong".

### Bảng đối chiếu nhanh

| # | Mục DoD | Nuôi bởi |
|---|---|---|
| 1 | Đủ AC-01→04 / FR-001..003 | Test D + F3 |
| 2 | RBAC + ownership (tầng 2 + khuôn tầng 3) | C + AuthZ matrix |
| 3 | RFC 7807 + `traceId` | D9 + gọi tay một 400 |
| 4 | Staging tài khoản thật | F1 + F3 |
| 5 | Swagger khớp stub cổng mở | `Category=Contract` xanh |
| 6 | Không lộ secret/PII | F5 nhóm Bảo mật + review log |
| 7 | Lát cắt trình duyệt thật, không mock | F2 + F3 (+ F4) |

### Kết quả mong đợi

- [ ] Cả 7 mục Mục 11 được tick trong bản ghi Ngày 6 / PR cổng đóng

---

## 8. F7 — Đóng băng hợp đồng + bàn giao

### Mục tiêu

GĐ2 không bắt đầu bằng việc "sửa nhẹ identity cho tiện".

### Cách thực thi

1. Thông báo cả nhóm (chat/PR): **`Modules/Identity/Presentation/identity-v1.yaml` đóng băng** cho phạm vi GĐ1.
2. Ghi vào biên bản / cập nhật trạng thái `README.md` (nếu đội đang giữ mục trạng thái GĐ1):
   - Phần **hoãn có địa chỉ** (ví dụ bên ghi `revoked:user` → GĐ6; bất biến "≥ 1 user Admin" → GĐ6/GĐ8).
   - Điều dễ hiểu nhầm: **vai trò nằm trong JWT** → đổi role trên DB **không** có hiệu lực ngay trên access đang sống; GĐ6 dùng `revoked:user` — không phải bug FE.
3. Xác nhận ba điều kiện B.11 đã thỏa → **mở GĐ2**.

### Kết quả mong đợi

- [ ] Nhóm xác nhận đã nhận thông báo đóng băng
- [ ] Danh sách hoãn có địa chỉ (không nợ vô chủ)
- [ ] Việc đầu GĐ2 được phép kéo (cổng mở module kế tiếp)

---

## 9. Bằng chứng nên giữ lại (một chỗ)

Dán vào PR cổng đóng hoặc thư mục biên bản nhóm:

| Bằng chứng | Từ đầu việc |
|---|---|
| Link run CD xanh + thời điểm | F1 |
| `curl` health + ảnh `/login` CSP | F1 |
| Ảnh Network: chỉ `/bff`, không JWT trình duyệt | F2 |
| Ảnh mail Brevo + `/me` sau verify/login | F3 |
| Log/đếm một lần refresh / kết quả Playwright single-flight | F4 |
| Bản tick Mục 12 + Mục 11 | F5, F6 |
| Thông báo đóng băng + danh sách hoãn | F7 |

---

## 10. Khối F để lại gì

| Ai nhận | Nhận cái gì |
|---|---|
| **GĐ2** | Nền Identity đã chứng minh trên staging; hợp đồng `identity-v1` đóng băng; AuthZ matrix + BFF + CD có FE sẵn để mở module mới |
| **GĐ6** | Việc ghi `revoked:user` khi hạ quyền / khóa TK; nhớ JWT vẫn mang role cũ đến khi thu hồi |
| **GĐ7 / vận hành** | Checklist Mục 12 nhóm Vận hành đã từng chạy một lần end-to-end — làm mẫu cho cổng đóng các giai đoạn sau |
| **Cả đội** | Định nghĩa "xong giai đoạn" = staging + E2E thật + DoD, không phải "CI xanh trên laptop" |
