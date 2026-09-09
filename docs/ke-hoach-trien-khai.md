# Kế hoạch triển khai (build) Mạng xã hội Facebook-like — Nhóm 5

## Context

Tài liệu PTTK (`BaoCao_Nhom4_v5.pdf`, bản v5.0) đã chốt toàn bộ phân tích & thiết kế: 6 UC lõi
(UC-02 đăng nhập, UC-04 đăng bài, UC-08 feed, UC-10/11 kết bạn, UC-15 chat realtime, UC-19 kiểm
duyệt), 13 entity, ma trận RBAC, 20 FR + 12 NFR, và các ADR (modular monolith, JWT rotation,
SignalR+Redis, fan-out-on-read). Thư mục hiện tại **chưa có mã nguồn ứng dụng** — chỉ có bộ công cụ
sinh tài liệu (pandoc/plantuml) và các file báo cáo `.docx`.

Mục tiêu của kế hoạch này: **hiện thực hóa toàn bộ MVP** đúng theo thiết kế — code, test, triển khai
thật lên OCI sau Cloudflare, và kiểm chứng được các mục tiêu GOAL-01..05 (feed p95 ≤ 500ms, chat
≤ 1s, 0 lỗ hổng IDOR, uptime ≥ 99%, tuân thủ NĐ 13/2023). Ràng buộc: ASP.NET Core 8 + PostgreSQL 16,
đội 3 người, ~26 ngày.

Nguyên tắc xuyên suốt: mỗi bước đều có **"làm gì → làm như nào → kiểm tra lại ra sao"**; mỗi tính năng
chỉ "xong" khi đạt Definition of Done (Mục 3.5 tài liệu): đủ AC + kiểm tra RBAC/ownership + validation
RFC 7807 + chạy thử staging + cập nhật Swagger.

---

## 0. Chuẩn bị & quy ước (áp dụng cả dự án)

**Tech stack cố định:** ASP.NET Core 8 (Web API + SignalR), EF Core + Npgsql, PostgreSQL 16, Redis 7,
Cloudflare R2 (S3-compatible), Docker Compose, Caddy (TLS), Next.js 14 (frontend), Serilog +
Prometheus + Grafana + Uptime Kuma.

**Cấu trúc solution (modular monolith — theo Mục 6.4, 7 module + Shared Kernel):**
```
SocialApp.sln
 ├─ src/
 │   ├─ SocialApp.Api                (host: controllers, SignalR Hubs, DI, middleware)
 │   ├─ SocialApp.SharedKernel       (AuthN/AuthZ, RFC7807, correlation ID, rate limit, result types)
 │   ├─ Modules/Identity             (CMP-01: users, roles, refresh_tokens, JWT)
 │   ├─ Modules/Profile              (CMP-02: profiles)
 │   ├─ Modules/SocialGraph          (CMP-03: friendships, follows)
 │   ├─ Modules/Content              (CMP-04: posts, comments, reactions, media, feed)
 │   ├─ Modules/Messaging            (CMP-05: conversations, messages, ChatHub)
 │   ├─ Modules/Notification         (CMP-06: notifications, Hub)
 │   └─ Modules/Moderation           (CMP-07: reports, audit_logs, admin)
 ├─ tests/  (Unit, Integration, Architecture[ArchUnitNET], Load[k6])
 ├─ deploy/ (docker-compose.*.yml, Caddyfile, prometheus.yml, grafana/)
 └─ frontend/ (Next.js 14)
```
Mỗi module: `Domain` (entity + business rule) / `Application` (service + DTO + validator) /
`Infrastructure` (EF repository). Module chỉ giao tiếp qua interface ở Application — ArchUnitNET
chặn tham chiếu chéo (ADR-001).

**Quy ước chung:** REST `/api/v1`, lỗi RFC 7807 Problem Details, cursor pagination (limit 20, max 50),
rate limit 100 req/phút/user (10 cho auth), UUID v7 cho PK, `created_at/updated_at` chuẩn, mọi thao
tác Mod/Admin ghi `audit_logs`.

**Kiểm tra GĐ0:** `docker compose up` chạy được API + Postgres + Redis + Mailpit; `GET /health/ready`
trả 200; CI (build + test) xanh; ArchUnitNET test khung chạy được (dù chưa có rule vi phạm).

---

## 0B. Thiết lập SERVER thật & CD sớm (Ngày 2–3) — làm ngay trước khi code nhiều

> ✅ **ĐÃ HOÀN THÀNH.** Staging đang chạy tại **https://mxh.banhgao.net** — đã kiểm chứng:
> `/health/live` và `/health/ready` = 200 Healthy (Postgres + Redis đều thông), `/api/v1/ping` trả
> đúng payload của walking skeleton, RFC 7807 + `traceId` + `X-Correlation-ID` hoạt động, Swagger
> mở được, Cloudflare đang proxy (TLS hợp lệ).
>
> Còn hai điểm nhỏ để dọn ở GĐ7: **chưa có header HSTS** (`Strict-Transport-Security`) — bật ở
> Cloudflare hoặc Caddy; và **Swagger đang mở công khai** trên staging (cố ý theo `Program.cs`,
> nhưng phải chắc chắn TẮT ở production).

> Mục tiêu: có **môi trường staging thật trên Internet** từ rất sớm để mỗi giai đoạn sau đều
> deploy liên tục (CD thật) và test bằng tài khoản thật — thay vì dồn deploy về cuối.

- **Làm gì:** Provision VPS OCI, hardening, cài Docker, gắn domain + Cloudflare + TLS, tạo R2 bucket,
  cấu hình secrets, và dựng pipeline CD tự động deploy nhánh `develop` lên staging.
- **Làm như nào (từng bước hạ tầng):**
  1. **Tạo VPS:** OCI Ampere A1 (2 OCPU/12GB), Ubuntu LTS; gán public IP; tạo user non-root.
  2. **SSH & firewall hardening:** SSH key-only (tắt password), fail2ban; OCI Security List +
     `ufw` chỉ mở **80/443** (và 22 giới hạn IP) — ISS-04.
  3. **Cài Docker + Compose plugin;** tạo thư mục `deploy/`, mạng nội bộ Docker (zone app/data).
  4. **Domain + Cloudflare:** trỏ DNS về IP, bật proxy (TLS + chống DDoS); Caddy làm reverse proxy
     TLS phía trong (Cloudflare Full/Strict).
  5. **Object Storage:** tạo Cloudflare R2 bucket (`-dev`, `-staging`), cấu hình **CORS** cho
     pre-signed PUT (ISS-02); tạo API token R2 (scope tối thiểu).
  6. **Secrets:** đưa vào CI protected variables (JWT key, DB pass, R2 keys, SMTP) — **không** nhúng
     secret vào image; server đọc qua biến môi trường.
  7. **CD pipeline:** GitHub Actions/GitLab CI: build image → push registry → SSH deploy lên staging
     (`docker compose -f docker-compose.staging.yml up -d`), rolling sau health check.
  8. **compose staging:** Caddy (TLS) → API → Postgres + Redis; volume dữ liệu; healthcheck từng service.
- **Kiểm tra:**
  - `https://<domain>` trả trang/`GET /health/ready` = 200 qua Cloudflare (chứng nhận TLS hợp lệ).
  - `nmap`/kiểm tra port: chỉ 80/443 mở ra ngoài; DB/Redis KHÔNG expose public.
  - Push 1 commit lên `develop` → CD tự deploy → phiên bản mới lên staging không cần thao tác tay.
  - Test pre-signed PUT 1 ảnh lên R2 từ trình duyệt (CORS OK).
  - SSH bằng password bị từ chối; chỉ key mới vào được.

---

## 0C. Nhánh FRONTEND chạy song song (từ GĐ1) — Next.js 14

> Kế hoạch gốc nhắc Next.js 14 ở tech stack nhưng **không giai đoạn nào giao việc làm frontend**.
> Mục này lấp lỗ hổng đó.
>
> **Bản B (đã chốt) — lát cắt dọc:** mỗi giai đoạn giao **một lát cắt hoàn chỉnh** (entity →
> migration → seeder → service → controller → màn hình), backend và frontend **xong cùng nhau,
> nghiệm thu cùng nhau**. Thay cho quy tắc bản A "FE trễ BE đúng một nhịp". Lý do đổi và bảng
> đối chiếu chi phí/lợi ích: xem cuối mục này.

**Phân bổ nhân lực:** **từ Ngày 3 (đầu GĐ1): 2 người backend, 1 người frontend.** Bản A tách người
ở GĐ2; bản B tách sớm hơn 2 ngày.

### Nhịp cố định của mọi giai đoạn

Ba bước, không giai đoạn nào được bỏ:

| Bước | Khi nào | Sản phẩm bắt buộc |
|---|---|---|
| **Cổng mở** | Nửa ngày đầu giai đoạn, cả nhóm | Danh sách endpoint + DTO + mã lỗi + quy tắc phân trang, viết thành **OpenAPI stub commit vào repo**. Không có stub thì không ai gõ dòng code nào |
| **Hai lane song song** | Thân giai đoạn | Backend hiện thực thật; frontend dựng UI trên **mock sinh từ stub** (MSW) — không chờ backend chạy được |
| **Cổng đóng** | Cuối giai đoạn | Frontend **bỏ mock, trỏ staging thật**, chạy E2E lát cắt, rà Definition of Done |

**Contract-first là điều kiện chặn, không phải lời khuyên.** Thiếu nó thì "FE cùng nhịp" thành
"FE ngồi chờ nửa giai đoạn" hoặc "FE dựng trên API đang viết dở rồi phải sửa lại" — đúng cái bản A
né bằng cách cho FE trễ một nhịp. Hai luật đi kèm:

- **Sinh type TypeScript từ OpenAPI stub** (`openapi-typescript`) → đổi hợp đồng là **compile lỗi**,
  không phải phát hiện lúc chạy.
- **Cấm nghiệm thu trên mock.** Cổng đóng bắt buộc trỏ staging thật.
- Hợp đồng đổi giữa chừng phải báo cả nhóm và **cập nhật stub trong cùng commit**.

### Lịch hai lane

| Ngày | Giai đoạn | Backend (2 người) | Frontend (1 người) |
|---|---|---|---|
| 3–6 | GĐ1 | Nền dữ liệu · SharedKernel AuthZ · 6 endpoint auth | Scaffold Next.js + design token + api client · màn đăng ký/đăng nhập/verify · app shell + route guard · **interceptor 401→refresh** |
| 6–9 | GĐ2 | Profile + Content + pre-signed R2 | Hồ sơ + avatar · composer đăng bài · **upload ảnh thật lên R2 từ trình duyệt** |
| 9–13 | GĐ4 | Social graph + Feed | Kết bạn/theo dõi · news feed cuộn vô hạn theo cursor |
| 13–15 | GĐ3 | Comment + Reaction | Cây bình luận 3 cấp · thanh cảm xúc optimistic update |
| 15–19 | GĐ5 | Chat realtime | **Chat UI thật** + SignalR client + reconnect/fallback |
| 19–21 | GĐ6 | Notification + Search + Moderation | Chuông thông báo · tìm kiếm · màn kiểm duyệt · màn admin |
| 21–23 | GĐ7 | Production + observability + backup | Build production, service `frontend` vào compose, hoàn thiện UI/UX |
| 23–26 | GĐ8 | Verification NFR + security + bàn giao | (cả nhóm) |

### Hai chỗ backend ngầm phụ thuộc frontend — không lùi được

| Chỗ | Yêu cầu | Vì sao bắt buộc có client thật |
|---|---|---|
| **GĐ2** — SEQ-01 upload ảnh | Test pre-signed PUT lên R2 **từ trình duyệt** (CORS OK) | CORS chỉ lộ ra trong trình duyệt; `curl` luôn xanh. Đây là rủi ro ISS-02 đã đăng ký |
| **GĐ5** — GOAL-02 | E2E **2 trình duyệt** đo p95 gửi→nhận ≤ 1s | Bảng verification tổng ghi GOAL-02 nghiệm thu ở GĐ5 |

**Bản B đóng cả hai sớm hơn bản A:** upload ảnh thật rơi vào GĐ2 (Ngày 6–9) thay vì phải chờ tới
GĐ4 — **ISS-02 lộ sớm hơn ~4 ngày**; và GOAL-02 đo bằng chính chat UI thật ở GĐ5.

### Hai trang HTML tối giản của bản A — đã loại bỏ

Bản A phải dựng một trang đo luồng refresh ở GĐ1 và một trang 2 client SignalR ở GĐ5, **chỉ vì UI
thật đến muộn hơn thời điểm cần nghiệm thu**. Bản B có UI thật ngay trong giai đoạn nên cả hai
trang này không còn lý do tồn tại — tiết kiệm ~2 giờ và bỏ được hai thứ vốn phải xóa đi sau đó.

Chỗ chúng từng bảo vệ vẫn phải được bảo vệ, giờ bằng chính UI thật:
- **GĐ1:** cookie `httpOnly` round-trip, `SameSite`, `Path` scoping, CORS preflight — kiểm chứng qua
  interceptor 401→refresh ở cổng đóng GĐ1.
- **GĐ5:** p95 gửi→nhận đo bằng công cụ gắn thẳng vào màn chat.

### Việc backend phải giao trước, nếu không nhánh FE đứng bánh

- **CORS — làm ở GĐ1**, cùng giai đoạn với người FE chứ không còn là "giao trước cho giai đoạn sau".
- **Chốt nơi lưu refresh token ở GĐ1** — đây là quyết định *hợp đồng API*, không phải quyết định
  frontend (xem GĐ1, quyết định 6).
- **OpenAPI stub ở cổng mở mỗi giai đoạn** — thay cho vai trò "Swagger JSON ổn định trên staging"
  của bản A. Swagger trên staging vẫn là nguồn sự thật ở **cổng đóng**; stub là nguồn sự thật
  trong lúc hai lane đang chạy song song.
- Thêm service `frontend` vào `docker-compose.staging.yml`, Caddy route `/` → frontend, `/api` → API
  *(GĐ7, hoặc sớm hơn nếu muốn xem FE trên staging)*.

### Đối chiếu bản A và bản B

| | Bản A — FE trễ một nhịp | Bản B — lát cắt dọc *(đã chốt)* |
|---|---|---|
| Tách người FE | GĐ2 (Ngày 5) | **GĐ1 (Ngày 3)** |
| Nguồn sự thật lúc đang code | Swagger trên staging của giai đoạn trước | **OpenAPI stub chốt ở cổng mở** |
| ISS-02 (CORS R2) lộ ra | GĐ4, Ngày 8–12 | **GĐ2, Ngày 6–9** |
| Nghiệm thu GOAL-02 | Trang HTML tạm 2 SignalR client | **Chat UI thật** |
| Trang HTML tạm phải dựng rồi xóa | 2 | **0** |
| GĐ7 | Còn backlog FE bắt kịp GĐ6 | **Không còn backlog** |
| Backend ở GĐ4/GĐ5 | 2 người | 2 người *(không đổi)* |
| Ngân sách | 25 ngày | **26 ngày** (GĐ1 +1; GĐ7 nhẹ đi nên có thể về lại 25) |

> ⚠️ **Rủi ro đã biết, giữ nguyên từ bản A:** GĐ4 (feed, trọng điểm hiệu năng) và GĐ5 (realtime,
> trọng điểm kỹ thuật) chỉ còn **2 người** làm backend. Nếu thấy trễ ở GĐ4, phương án ứng phó là
> kéo người frontend về hỗ trợ và chấp nhận UI dừng ở mức mỏng cho tới GĐ7. Quyết định sớm, đừng
> đợi đến khi đã trễ.
>
> ⚠️ **Rủi ro riêng của bản B:**
> - **Bỏ qua cổng mở khi gấp** → FE dựng trên API đang viết dở → mất sạch lợi ích. Giảm thiểu:
>   OpenAPI stub là artifact bắt buộc, không có stub thì không ai bắt đầu.
> - **Mock trôi xa khỏi hiện thực thật** → FE xanh trên mock, đỏ trên staging. Giảm thiểu: sinh
>   type từ stub (đổi hợp đồng là compile lỗi) + cấm nghiệm thu trên mock.
> - **Chỉ một người biết frontend** (bus factor 1). Giảm thiểu: một người backend review chéo PR
>   frontend **từ GĐ1**, không để tới lúc cần mới đọc lần đầu.
> - **Hợp đồng SignalR ở GĐ5 không nằm trong Swagger** nên dễ quên chốt → FE không mock được
>   realtime, lại rơi về nhịp cũ. Giảm thiểu: viết hợp đồng hub thành văn bản ở cổng mở GĐ5,
>   ngang hàng với OpenAPI stub.

---

## Đường lõi & thứ tự ưu tiên

**Đường lõi** — luồng phải chạy được end-to-end trước mọi thứ khác, đóng lại ở **~Ngày 13**:

```
đăng ký → đăng nhập → đăng bài + ảnh → xem feed
```

Đây là lý do GĐ4 (feed) được đưa lên trước GĐ3 (bình luận/cảm xúc). Ba lợi ích: có sản phẩm demo
được sớm hơn 2 ngày; **GOAL-01 (feed p95 ≤ 500ms) — mục tiêu khó nhất — được đo sớm hơn 2 ngày**,
còn biên độ thêm index/cache nếu trượt; và thứ cắt được thì đứng sau thứ không cắt được.

**Thứ tự ưu tiên khi vỡ tiến độ** — có bảng này thì lúc trễ ở Ngày 19 biết cắt gì ngay, không phải họp:

| Ưu tiên | Tính năng | Cắt được không |
|---|---|---|
| **Lõi** | Đăng ký · đăng nhập · đăng bài + ảnh · feed | **Không** — mất là không còn sản phẩm |
| **Quan trọng** | Kết bạn · chat realtime | **Không** — GOAL-02 phụ thuộc chat |
| **Bắt buộc phi chức năng** | Kiểm duyệt + audit + RBAC | **Không** — GOAL-03 phụ thuộc |
| Mở rộng | Bình luận · cảm xúc · thông báo · tìm kiếm | **Cắt xuống mức tối thiểu được** |

---

## Lộ trình theo giai đoạn (8 giai đoạn / ~26 ngày)

> Mỗi giai đoạn ghi rõ 3 phần: **Làm gì** · **Làm như nào** · **Kiểm tra lại ra sao**.
> **Từ GĐ1**, mỗi giai đoạn chạy hai lane song song (backend + frontend) và mở/đóng bằng một
> cổng hợp đồng API — xem Mục 0C.

### GĐ 0 — Khởi tạo & Walking Skeleton (Ngày 1–2)
- **Làm gì:** Dựng khung solution, hạ tầng local, CI/CD, health check, error model, correlation ID,
  Swagger. Tạo 1 endpoint mẫu `GET /api/v1/ping` đi hết pipeline.
- **Làm như nào:**
  - `dotnet new` solution + các project module rỗng; thêm Serilog, Swashbuckle, EF Core, Npgsql,
    StackExchange.Redis.
  - `deploy/docker-compose.dev.yml`: Postgres 16, Redis 7, Mailpit, API. `.env` mẫu (không commit secret).
  - Middleware SharedKernel: exception → RFC 7807, correlation ID header, rate limit (fixed window).
  - GitHub Actions/GitLab CI: restore → build → test → (staging) deploy. ArchUnitNET project khởi tạo.
- **Kiểm tra:** compose up OK; `/health/live` + `/health/ready` (check DB+Redis) = 200; Swagger UI mở
  được; CI pipeline chạy xanh; gọi endpoint lỗi → nhận đúng JSON Problem Details có `traceId`.

### GĐ 1 — Identity & Access: UC-01, UC-02 (Ngày 3–6)

> 📄 **Tài liệu thi công chi tiết: [`giai-doan-1.md`](./giai-doan-1.md)** — schema DDL, dữ liệu seed,
> code mẫu `[RequirePermission]`, kế hoạch 3 người theo ngày, checklist nghiệm thu.
>
> **Bắt đầu bằng Mục 9.0 — dọn 4 khoản nợ kỹ thuật GĐ0 để lại** (nửa ngày đầu Ngày 3): thêm EF Core
> cho module Identity · dựng `IdentityDbContext` theo schema riêng · ghim 5 package · tách AuthZ
> matrix thành cổng chặn trong CI.

- **Làm gì:** Đăng ký + xác minh email (FR-001), đăng nhập cấp JWT + refresh rotation (FR-002),
  lockout 5 lần/15 phút (FR-003), RBAC 3 tầng (Mục 6.7.1), seed roles/permissions (ENT-10/10a/10b).
- **Quyết định thiết kế đã chốt** *(4 điểm đầu lệch v5.0 — xem Mục "Sai khác so với báo cáo v5.0")*:
  1. JWT claim `role` mang `code` chuỗi (`'USER'`/`'MODERATOR'`/`'ADMIN'`) — **mọi vai trò, không
     riêng Admin**; `role_id` kiểu số chỉ sống trong DB. `code` bất biến theo hợp đồng API.
     Hệ quả: vai trò được "đóng dấu" vào token nên đổi vai trò không tự có hiệu lực. Khắc phục
     bằng cơ chế **thu hồi theo `revoked:user` + `iat`** trên Redis (TTL = TTL access token):
     ghi một mốc thời gian, mọi token của user phát trước mốc đó bị từ chối → **nâng/hạ vai trò
     có hiệu lực ngay ở request kế tiếp, không phải đăng nhập lại**. Khóa/xóa tài khoản thì thu
     hồi kèm cả refresh family. *(Không dùng denylist theo `jti`: server stateless không biết
     `jti` nào đang lưu hành nên không chặn được hết các thiết bị.)*
  2. **Admin short-circuit ở tầng 2, tuyệt đối không ở tầng 3** — "short-circuit" = gặp
     `role == "ADMIN"` thì cho qua tầng 2 ngay, không tra `role_permissions` (nên Admin không có
     dòng nào trong bảng đó); bù lại mọi thao tác Admin ghi `audit_logs`. Đặt nhầm dòng `if` này
     xuống tầng 3 là mở toang IDOR cho toàn hệ thống.
  3. `roles` tách `code` (bất biến, cho máy) + `display_name` (sửa được, cho người).
  4. Bổ sung `refresh_tokens.family_id` → thu hồi cả chuỗi bằng 1 `UPDATE` thay vì lần theo
     `replaced_by_id` qua N truy vấn.
  5. **Không dùng cột `roles.is_system`** (đã cân nhắc rồi loại bỏ, không phải hoãn — tập vai trò
     cần bảo vệ vĩnh viễn chỉ có 3 cái nên trigger gọi thẳng tên là đủ). Thay bằng ba thứ:
     `users.role_id` FK `ON DELETE RESTRICT` + **kiểm tra vai trò hệ thống lúc khởi động** (app
     từ chối chạy nếu `roles.code` bị đổi tay) + bất biến "luôn còn ≥ 1 Admin hoạt động" (GĐ6/GĐ8).
  6. **Refresh token đặt trong `httpOnly` + `Secure` + `SameSite=Lax` cookie**, access token do
     client giữ trong memory. Đây là **quyết định hợp đồng API, không phải quyết định frontend**:
     nó đổi chữ ký endpoint — `POST /auth/login` trả `{accessToken, expiresIn}` + `Set-Cookie`,
     và `POST /auth/refresh` **đọc từ cookie, không nhận body**. Chốt muộn là phải mở lại hợp đồng
     vừa đóng băng. Lý do chọn: refresh token sống lâu nhất và nguy hiểm nhất nếu bị XSS lấy mất;
     `httpOnly` khiến JS không đọc được.
  7. **Bật CORS ngay ở GĐ1** cho origin frontend (`localhost:3000` dev, domain thật khi deploy) —
     nhánh FE khởi động ngày đầu GĐ2, để sang GĐ2 mới làm là họ ngồi chờ (Mục 0C).
- **Làm như nào:**
  - Entity: `users`, `roles`, `permissions`, `role_permissions`, `refresh_tokens`,
    `email_verification_tokens` (schema Mục 5.5) qua EF Core migration; seed 3 vai trò
    (USER/MODERATOR/ADMIN) + ma trận permission (Mục 6.7.2) idempotent bằng `ON CONFLICT DO NOTHING`
    — **không** `DO UPDATE`, để từ GĐ6 seeder không ghi đè cấu hình Admin đã sửa.
  - BCrypt cost 12 (NFR-SEC-01); JWT HS256, access 15 phút, claims `sub/role/iat/exp/jti`; refresh
    lưu **băm** trong DB, xoay vòng + reuse detection (ADR-002), ân hạn 10s chống race 2 tab.
  - Email verify qua Mailpit (dev); `IHostedService`/`IEmailSender` adapter; token verify lưu băm.
  - `ITokenRevocationStore` (Redis) — **bên đọc** (hook `OnTokenValidated` ở tầng 1) làm ở GĐ1,
    GĐ1 chỉ nối 1 trigger ghi là reuse detection; các trigger còn lại ở GĐ6/GĐ8. Fail-open khi
    Redis chết, có alert.
  - AuthZ: JWT middleware (AuthN) + **`[RequirePermission]` đặt trong SharedKernel, viết một lần
    cho cả dự án** (đọc quyền từ DB, cache TTL 60s) + fallback policy = default deny + ownership
    check ở service (tầng 3).
  - Endpoints: `POST /auth/register|login|refresh|logout`, `POST /auth/verify-email`, `GET /me`.
  - **Lane frontend (1 người, song song từ Ngày 3 — Mục 0C):** scaffold Next.js 14 App Router +
    design token + primitive · sinh type từ OpenAPI stub (`openapi-typescript`) · api client bọc
    `fetch` với **`credentials: 'include'`** · mock MSW để dựng UI không chờ backend · màn đăng ký /
    đăng nhập / xác minh email · app shell + route guard · **access token giữ trong memory, không
    `localStorage`** · **interceptor 401→refresh phải single-flight** (nhiều request 401 cùng lúc chỉ
    gọi refresh một lần, số còn lại xếp hàng — nếu không sẽ tự kích hoạt reuse detection và bị đăng
    xuất oan) · trang `/me`.
  - **Cổng mở (Ngày 3 sáng, cả nhóm ~2 giờ):** chốt 7 quyết định thiết kế → viết OpenAPI stub cho
    6 endpoint auth kèm mã lỗi 400/401/403/409/410/423 → commit. Không có stub thì không ai code.
  - **Cổng đóng (Ngày 6):** frontend bỏ mock, trỏ staging HTTPS thật; **đóng băng hợp đồng auth** —
    GĐ2 khởi động ngay sau đây.
- **Kiểm tra:** map thẳng AC US-002 & test 401/403 (Mục 6.7.5):
  - AC-01 login đúng → 200 + cặp token; AC-02 sai mật khẩu → 401 không lộ email + `failed_login_count++`
    (chạy BCrypt giả cho email không tồn tại để không lộ qua thời gian phản hồi);
  - AC-03 sai 5 lần → 423 Locked 15 phút; AC-04 chưa verify → 403.
  - TC-A01 (không JWT → 401), TC-A02 (token hết hạn/chữ ký sai → 401) — **dựng bảng AuthZ matrix
    data-driven ngay tại đây, làm CI gate; mỗi giai đoạn sau chỉ thêm dòng.**
  - refresh reuse → thu hồi cả chuỗi. Seeder chạy 2 lần → dữ liệu không đổi; sửa `role_permissions`
    rồi restart → không bị ghi đè. Integration test trên Postgres thật (Testcontainers).
  - **E2E lát cắt ở cổng đóng:** đăng ký → nhận mail Mailpit → xác minh → đăng nhập → `GET /me` →
    ép hết hạn access token → interceptor refresh → gọi lại thành công, **trên domain HTTPS thật**.
    Đây là chỗ cookie `httpOnly`, `SameSite`, `Path` scoping và CORS preflight được kiểm chứng —
    integration test không chạm tới. *(Bản A dùng một trang HTML tạm cho việc này; bản B không cần.)*

### GĐ 2 — Profile + Content (đăng/sửa/xóa bài + ảnh): UC-03, UC-04, UC-05 (Ngày 6–9)
- **Làm gì:** Hồ sơ + avatar (FR-013), đăng bài văn bản + ≤10 ảnh với privacy (FR-004, BR-01/02),
  sửa/xóa mềm bài (FR-005), upload ảnh qua pre-signed URL thẳng lên R2 (không qua API).
- **Làm như nào:**
  - Entity `profiles`, `posts`, `comments`(khung), `reactions`(khung), `media_attachments`.
  - Luồng SEQ-01: client xin pre-signed URL (hạn 10 phút) → PUT ảnh thẳng R2 → `POST /posts`
    validate BR-01 (≤5000 ký tự hoặc ≥1 ảnh; ≤10 ảnh; ≤10MB/ảnh) → lưu post+media trong 1 transaction
    → phát event `PostCreated`.
  - CHECK constraint privacy (public/friends/private) + status (published/hidden/deleted).
  - Background worker dọn media mồ côi (upload dở).
  - **Dùng lại nguyên xi hạ tầng AuthZ của GĐ1** — đây là module đầu tiên tiêu thụ nó, làm chuẩn
    cho GĐ3–GĐ6: quyền khai báo bằng `[RequirePermission("post.create")]`; ownership `(own)` kiểm
    ở **tầng service** (không ở controller, không ở attribute) và trả `Result.Forbidden` → 403
    RFC 7807, thông điệp không tiết lộ tài nguyên có tồn tại hay không. Không module nào tự chế
    cách kiểm tra riêng.
  - **Lane frontend (đã chạy từ GĐ1 — Mục 0C):** hồ sơ + avatar · composer đăng bài (validate BR-01
    phía client **cùng ngưỡng với server**) · **upload ảnh thật từ trình duyệt thẳng lên R2** qua
    pre-signed PUT kèm tiến trình và lỗi từng ảnh · danh sách + chi tiết bài · sửa/xóa bài của mình ·
    trạng thái rỗng/đang tải, và hiển thị 403 **không tiết lộ tài nguyên có tồn tại hay không**.
    Backend phải giao **endpoint pre-signed URL sớm nhất trong giai đoạn** — frontend chặn ở đó.
- **Kiểm tra:** AC US-004: AC-01 đăng công khai → 201; AC-02 bài rỗng không ảnh → 400 (BR-01);
  AC-03 chọn 11 ảnh → 400; AC-04 JWT hết hạn → 401. TC-A03 (User A `PATCH /posts/{id của B}` → 403 IDOR).
  Test upload R2 (dev bucket) + rollback khi lỗi ghi DB (ảnh mồ côi được job dọn).
  **Upload ảnh thật từ trình duyệt lên R2** — bước duy nhất chứng minh CORS đúng; `curl` luôn xanh.
  **ISS-02 lộ ra tại đây, sớm hơn bản A ~4 ngày** (bản A phải chờ tới GĐ4 mới có client thật).

> ⚠️ **Thứ tự thi hành đã đổi:** GĐ4 (feed) chạy TRƯỚC GĐ3 (bình luận/cảm xúc).
> Số hiệu giai đoạn giữ nguyên để mọi tham chiếu chéo trong tài liệu không gãy —
> **số hiệu không còn bằng thứ tự**. Lý do đổi: đóng vòng lặp lõi và đo GOAL-01 sớm hơn 2 ngày
> (xem "Đường lõi & thứ tự ưu tiên"). Kế hoạch gốc đã chừa sẵn: *"GĐ3 chèn linh hoạt sau GĐ2"*.

### GĐ 4 — Social Graph + News Feed: UC-10/11, UC-13, UC-08 (Ngày 9–13) ⚠️ trọng điểm hiệu năng
- **Làm gì:** Kết bạn Pending→Accepted (FR-010/011, BR-03), theo dõi 1 chiều (FR-012),
  News Feed fan-out-on-read + cache Redis (FR-009, BR-02/07, ADR-004).
- **Làm như nào:**
  - `friendships` PK(user_min,user_max) + CHECK user_min<user_max + requester_id; `follows` PK cặp.
  - Feed (SEQ-03): lấy danh sách bạn+following (cache Redis 60s) → cache trang đầu TTL 30s → miss thì
    query index `idx(author_id, created_at DESC) WHERE published` → lọc BR-02 (quyền riêng tư) + loại
    Hidden BR-07 → trả 20 bài + cursor. Degrade khi Redis chết (đọc thẳng DB); DB timeout >5s → 503.
  - Cursor keyset theo (created_at, id), không OFFSET.
  - **Lane frontend:** gửi/hủy/chấp nhận/từ chối lời mời kết bạn · danh sách bạn · nút theo dõi ·
    news feed cuộn vô hạn theo cursor + skeleton + trạng thái rỗng cho tài khoản mới · xử lý 503
    thành thông báo thử lại, không phải màn hình trắng.
  - **Cổng mở:** chốt hình dạng **cursor** trước khi code — frontend bám chặt nhất vào nó; đổi giữa
    chừng là viết lại toàn bộ phần cuộn vô hạn.
- **Kiểm tra:** AC US-010 (AC-01 accept → hai bên là bạn; AC-02 gửi trùng → 409; AC-03 tự gửi → 400;
  AC-04 người thứ 3 accept → 403). AC US-008 (AC-01 20 bài mới nhất + cursor; AC-02 bài "bạn bè" của
  người lạ KHÔNG hiện; AC-03 bài Hidden không hiện). **k6 load test feed @1.000 CCU → p95 ≤ 500ms**
  (NFR-PERF-01) — mốc kiểm chứng GOAL-01, chạy lại cuối GĐ8 sau tối ưu index.

### GĐ 3 — Tương tác: bình luận 3 cấp + cảm xúc: UC-06, UC-07 (Ngày 13–15)
- **Làm gì:** Bình luận ≤1000 ký tự, trả lời tối đa 3 cấp, xóa giữ nhánh (FR-007, BR-08);
  thả/đổi/gỡ 1 cảm xúc/đối tượng + cập nhật bộ đếm (FR-008, BR-05).
- **Làm như nào:**
  - `comments.parent_id` self-FK + CHECK ≤3 cấp; status visible/deleted → hiển thị "Bình luận đã bị xóa".
  - `reactions` PK (user, target_type, target_id); `PUT /reactions` idempotent, đổi loại thì thay thế;
    cập nhật `posts.reaction_counts` (jsonb) + `comment_count` cùng transaction.
  - API-Comment (401/403 theo BR-02 quyền xem bài), API-Reaction (PUT idempotent).
  - **Lane frontend:** cây bình luận 3 cấp + thu gọn nhánh dài + ô trả lời tại chỗ · thanh cảm xúc
    **optimistic update kèm rollback khi lỗi** · hiển thị bình luận đã xóa mà không mất nhánh con.
- **Kiểm tra:** test 3 cấp comment (cấp 4 bị chặn); thả 2 loại cảm xúc liên tiếp → chỉ còn 1 (BR-05);
  bộ đếm khớp bản ghi thật; bình luận trên bài không có quyền xem → 403.

### GĐ 5 — Nhắn tin 1-1 realtime: UC-15 (Ngày 15–19) ⚠️ trọng điểm realtime
- **Làm gì:** Chat 1-1 realtime ≤1s, trạng thái Sent→Delivered→Seen, chỉ 2 thành viên & chỉ bạn bè
  (FR-015/016, BR-06/09), idempotency chống trùng tin.
- **Làm như nào:**
  - `conversations` UQ(a,b)+CHECK a<b+seq_counter; `messages` UQ(conv,seq)+UQ(conv,client_msg_id).
  - SignalR `ChatHub` + Redis backplane (ADR-003); SEQ-02: SendMessage → validate JWT+BR-06+BR-09 →
    INSERT (message+seq+last_message atomically) → ACK Sent → tra presence Redis → đẩy B → Delivered/Seen.
  - B offline → tăng badge chưa đọc + tạo notification; mất WebSocket → fallback REST
    `POST /conversations/{id}/messages`; retry cùng `client_msg_id` khử trùng.
  - **Lane frontend:** danh sách hội thoại + cửa sổ chat + lịch sử cuộn ngược · SignalR JS client
    (kết nối kèm access token, **tự kết nối lại**, mất kết nối thì chuyển REST fallback) · hiển thị
    Sent/Delivered/Seen · sinh `client_msg_id` phía client để retry không tạo tin trùng · badge chưa
    đọc · **công cụ đo p95 gửi→nhận gắn thẳng vào màn chat thật**.
  - **Cổng mở — chốt CẢ HAI hợp đồng:** REST cho lịch sử, **và hợp đồng SignalR** (tên hub method,
    payload, thứ tự sự kiện, quy tắc `client_msg_id`). Hợp đồng realtime không nằm trong Swagger nên
    dễ quên chốt; quên là frontend không mock được và lại rơi về nhịp "chờ backend".
- **Kiểm tra:** AC US-015 (AC-01 B online nhận ≤1s + đủ trạng thái; AC-02 B offline → badge khi online;
  AC-03 retry cùng clientMsgId không trùng; AC-04 không phải bạn → 403 hội thoại chỉ đọc).
  TC-A04 (đọc conversation không phải thành viên → 403), TC-A07 (gửi tin cho người lạ → 403).
  **E2E 2 trình duyệt đo p95 gửi→nhận ≤ 1s** (NFR-PERF-03, GOAL-02) — đo bằng **chính chat UI thật**,
  vì lát cắt dọc cho chat UI xong ngay trong GĐ5. *(Bản A phải dựng một trang HTML tạm với 2 SignalR
  client cho việc này, vì FE trễ một nhịp nên chat UI mãi GĐ6 mới có. Bản B không cần — Mục 0C.)*

### GĐ 6 — Notification + Search + Moderation/Admin: UC-16,17,18,19,20 (Ngày 19–21)
- **Làm gì:** Thông báo (comment/reaction/tag/friend/message) gộp cùng loại (FR-018); tìm người dùng
  không dấu tiền tố (FR-017); báo cáo nội dung (FR-019); kiểm duyệt ẩn/gỡ + audit (FR-020, BR-07);
  admin khóa/mở tài khoản + gán vai trò; **quản lý vai trò (role CRUD) — phần hoãn từ GĐ1**.
- **Làm như nào:**
  - `notifications` UQ(recipient, group_key) để gộp; đẩy realtime qua Hub.
  - Search: GIN pg_trgm trên `unaccent(display_name)`, prefix match, q≥2 ký tự.
  - Moderation (UC-19): `post.status=Hidden` + `report=Resolved` + ghi `audit_log` cùng transaction;
    A1 không vi phạm → Dismissed. Admin endpoints ghi audit.
  - **Role CRUD + các guard hoãn từ GĐ1** (xem `giai-doan-1.md` Mục 2 & 3.4):
    - **Không thêm cột `is_system`** — đã cân nhắc và loại bỏ ở GĐ1. Tập vai trò cần bảo vệ vĩnh
      viễn chỉ có ADMIN/USER/MODERATOR nên guard gọi thẳng tên là đủ; cột cờ chỉ lặp lại thông
      tin đã cố định.
    - Guard xóa/đổi `code` vai trò hệ thống ở tầng service; nếu muốn chặn cứng thì thêm trigger
      `IF OLD.code IN ('ADMIN','USER','MODERATOR') THEN RAISE EXCEPTION`. `users.role_id` FK
      RESTRICT vẫn chặn xóa vai trò còn người mang (đã có từ GĐ1).
    - **Bất biến "luôn còn ≥ 1 Admin đang hoạt động"** — áp cho cả `role.assign` (hạ quyền Admin
      cuối) và `user.lock` (khóa Admin cuối). Đây mới là bất biến thực sự bảo vệ hệ thống: hệ
      thống chết vì hết *user* Admin, không phải vì mất *role* Admin.
    - Endpoint sửa vai trò chỉ nhận `display_name`, **không nhận `code`**.
    - Đẩy invalidate cache quyền khi Admin sửa `role_permissions` — không có thì Admin sửa xong
      phải chờ TTL 60s mới thấy tác dụng, triệu chứng nhìn rất giống bug.
    - Cảnh báo + xác nhận + ghi audit khi sửa quyền của vai trò `USER`: gỡ nhầm `post.create`
      là cả hệ thống thành read-only ngay lập tức.
  - **Nối bên ghi của `revoked:user` + `iat`** (bên đọc đã có từ GĐ1 — `giai-doan-1.md` Mục 7.5):
    - `role.assign` (nâng **và** hạ quyền): thu hồi access, **giữ** refresh family → người dùng
      nhận vai trò mới ngay ở request kế tiếp mà không bị đăng xuất.
    - `user.lock`: thu hồi **cả** access **và** refresh family → văng ra hoàn toàn.
    - **Thứ tự bắt buộc: `UPDATE` DB trước, `SET` Redis sau.** Đảo lại là user giữ vai trò cũ
      thêm 15 phút và không gì chặn được — không test tự động nào bắt được, phải code review.
  - **Lane frontend:** chuông thông báo realtime + danh sách đã gộp + đánh dấu đã đọc · ô tìm kiếm
    không dấu (chặn truy vấn <2 ký tự ngay ở client) · form báo cáo nội dung · màn kiểm duyệt (hàng
    đợi báo cáo, ẩn bài, bỏ qua, xem audit) · màn admin (khóa/mở tài khoản, gán vai trò, quản lý vai
    trò và ma trận quyền). **Kiểm chứng ngay trên UI** rằng nâng/hạ quyền có hiệu lực ở request kế
    tiếp mà người dùng không bị đăng xuất — đây là bằng chứng cơ chế `revoked:user` chạy đúng.
- **Kiểm tra:** AC US-019 (AC-01 ẩn → Hidden+Resolved+audit; AC-02 bỏ qua → Dismissed; AC-03 user
  thường gọi endpoint kiểm duyệt → 403 default deny + audit; AC-04 xử lý lại → 409). TC-A05
  (user gọi `/admin/*` → 403), TC-A06 (user gọi `PATCH /reports/{id}` → 403). Tìm "nguyen" khớp
  "Nguyễn"; thông báo cùng loại được gộp.
  Role CRUD: tạo vai trò mới chỉ có `report.resolve` (không `post.hide`) → hoạt động đúng **không
  sửa dòng code nghiệp vụ nào**; xóa vai trò hệ thống → bị chặn; hạ quyền/khóa Admin cuối cùng →
  bị chặn; sửa `role_permissions` → có hiệu lực ngay, không phải chờ TTL.

### GĐ 7 — Lên PRODUCTION + Observability + Backup/DR (Ngày 21–23)
- **Làm gì:** Nâng staging (đã dựng ở GĐ0B) lên **production** đầy đủ HA, giám sát và sao lưu (GOAL-04).
  *(Hạ tầng nền — VPS, Docker, Cloudflare, TLS, R2, CD — đã có từ GĐ0B; GĐ này tập trung production-grade.)*
- **Làm như nào (Mục 6.3/6.8/6.9):**
  - Nhân bản compose production: Caddy (TLS) → **2 API container** (stateless) → Postgres/Redis; deploy theo tag.
  - Serilog JSON + correlation ID (redact PII); Prometheus (RED metrics + business metrics) + Grafana; Uptime Kuma → `/health`.
  - Backup: `pg_basebackup` + WAL archiving (RPO ≤ 15 phút); **thực hiện 1 lần restore drill** có biên bản.
  - Rolling từng container sau health check; migration expand–contract (backward-compatible 1 phiên bản); rollback theo image tag.
  - **Trả 2 khoản nợ GĐ0B:** bật header **HSTS**; **TẮT Swagger ở production** (đang mở công khai trên staging).
  - **Lane frontend:** build production Next.js · thêm service `frontend` vào compose, Caddy route
    `/` → frontend và `/api` → API · hoàn thiện UI/UX, responsive, a11y cơ bản. **Không còn backlog
    bắt kịp** — đây là chỗ lát cắt dọc trả lại phần lớn chi phí đã bỏ ra ở GĐ1 (Mục 0C).
- **Kiểm tra:** truy cập domain HTTPS thật; Caddy loại instance fail (kill 1 container vẫn phục vụ);
  Grafana hiển thị metrics; alert error rate >1%/5 phút; **restore drill có biên bản** (NFR-REL-02);
  Uptime Kuma theo dõi ≥ 99%.

### GĐ 8 — Kiểm chứng NFR + Security + Hardening + Bàn giao (Ngày 23–26)
- **Làm gì:** Chạy đủ Verification Plan (Mục 7.2), vá lỗi, hoàn tất tài liệu & sign-off.
- **Làm như nào:**
  - k6 feed @1.000 CCU (NFR-PERF-01) — báo cáo p95 + Grafana; nếu vượt 400–500ms → thêm index/cache
    (ADR-004 review), cân nhắc hybrid.
  - AuthZ matrix test tự động TC-A01..A07 chạy trên CI (NFR-SEC-02) — mục tiêu **0 lỗ hổng IDOR** (GOAL-03).
  - Security scan ZAP + SQLi/XSS, TLS 1.2+ (NFR-SEC-04); code review BCrypt/JWT.
  - NĐ 13/2023: quyền xóa tài khoản → vô hiệu hóa ngay + ẩn danh PII sau 30 ngày; audit_logs giữ 12 tháng
    (NFR-COMP-01) — inspection. **Áp bất biến "luôn còn ≥ 1 Admin hoạt động" lên cả đường tự xóa
    tài khoản** — Admin cuối cùng dùng quyền này là hệ thống mất chỗ quản trị (nối tiếp GĐ6).
  - Coverage ≥ 70% tầng nghiệp vụ; build+test CI ≤ 10 phút (NFR-MAINT).
- **Kiểm tra:** RTM (Mục 7.1) đi xuôi & ngược — mỗi GOAL có đường hiện thực + test; checklist review
  Mục 8.1–8.3 tick đủ; điền Sign-off 8.4.

---

## Chiến lược test (xuyên suốt)
- **Unit:** business rule/domain (BR-01..09), validator, JWT/BCrypt.
- **Integration:** endpoint + Postgres thật (Testcontainers), ma trận quyền xem BR-02.
- **AuthZ matrix (CI gate):** TC-A01..A07 — chặn IDOR trước khi merge. **Dựng dạng data-driven ngay
  ở GĐ1** (TC-A01/A02 + RBAC-01/02); mỗi giai đoạn sau chỉ thêm dòng dữ liệu, không sửa khung —
  GĐ2 thêm TC-A03, GĐ5 thêm TC-A04/A07, GĐ6 thêm TC-A05/A06. Quy ước: **endpoint chạm tới tài
  nguyên có chủ sở hữu mà chưa có dòng trong bảng này thì coi như chưa xong.**
- **E2E lát cắt (cổng đóng mỗi giai đoạn):** frontend bỏ mock, trỏ staging thật, chạy hết lát cắt
  vừa làm. GĐ1 đăng ký→verify→login→`/me`→refresh · GĐ2 đăng bài kèm ảnh · GĐ4 feed · GĐ5 realtime
  chat 2 client. **Cấm nghiệm thu trên mock.**
- **Load (k6):** feed @1.000 CCU; báo cáo p95/p99/error rate + Grafana.
- **Architecture (ArchUnitNET):** chặn tham chiếu chéo module.
- **API (Postman/newman):** FR-004..020 xanh trên CI.

## Rủi ro cần theo dõi (Mục 7.3)
- ISS-01 SignalR tốn thời gian → fallback polling 3s, giữ nguyên hợp đồng API.
- ISS-02 R2 CORS/pre-signed trục trặc → tạm lưu volume VPS, giữ bảng metadata để chuyển lại R2.
- ISS-04 spam đăng ký → rate limit + lockout + firewall 80/443.

## Thứ tự phụ thuộc (không đảo được)
GĐ0 → **GĐ0B (server + CD sớm, có staging thật)** → GĐ1 (auth là nền) → GĐ2 (content cần auth) →
GĐ4 (feed cần content + social graph) → GĐ5/GĐ6 (song song được sau GĐ4) → GĐ7 (production hardening)
→ GĐ8 (verify). GĐ3 chèn linh hoạt sau GĐ2. Từ GĐ0B trở đi **mỗi giai đoạn đều deploy lên staging thật**.

**Nhánh frontend (Mục 0C)** chạy song song **từ GĐ1**, cùng nhịp với backend chứ không trễ một
nhịp: mỗi giai đoạn mở bằng cổng hợp đồng (OpenAPI stub), frontend dựng trên mock sinh từ stub, và
đóng bằng cổng ráp thật trên staging. Không đảo được ba mốc: **CORS phải xong trong GĐ1** (frontend
khởi động cùng lúc), **upload ảnh thật từ trình duyệt phải xong trong GĐ2** (chỗ duy nhất ISS-02 lộ
ra), và **chat UI phải xong trong GĐ5** (nếu không GOAL-02 không có cách nghiệm thu).

## Definition of Done cho mỗi UC (Mục 3.5)
Đủ AC · có RBAC + ownership check · validation RFC 7807 · chạy thử staging bằng tài khoản thật ·
cập nhật Swagger · không lộ secret/PII.

## Sai khác so với báo cáo v5.0

Ghi lại để lúc bảo vệ giải thích được — chắc chắn sẽ có người đối chiếu với bản đã chốt.
Chi tiết đầy đủ ở [`giai-doan-1.md`](./giai-doan-1.md) Mục 13.

| # | Báo cáo v5.0 | Thực hiện | Lý do | GĐ |
|---|---|---|---|---|
| 1 | `roles(role_id, name)` | Tách `code` (bất biến) + `display_name` (sửa được) | `code` là thứ JWT/policy/test bám vào nên phải bất biến; tên hiển thị thì cần sửa được | GĐ1 |
| 2 | Admin có đủ dòng trong ma trận `role_permissions` | Admin **không có dòng nào**, short-circuit tầng 2 (không áp cho tầng 3) | Thêm permission mới về sau Admin tự động có; loại rủi ro quên `INSERT` khiến Admin bị chặn khỏi tính năng mới. Bù bằng audit đầy đủ | GĐ1 |
| 3 | "Seed cố định 3 vai trò" | Vẫn seed 3 vai trò, nhưng vai trò là **dữ liệu mở** — thêm role mới lúc runtime không cần sửa code | Chính Mục 6.7.2 đã hứa "nâng cấp là thay dữ liệu, không thay code" | GĐ1 → GĐ6 |
| 4 | `refresh_tokens` chỉ có `replaced_by_id` | Bổ sung `family_id` | Thu hồi cả chuỗi bằng 1 `UPDATE` thay vì lần theo linked list qua N truy vấn | GĐ1 |
| 5 | Không đề cập cách thu hồi access token | Cơ chế `revoked:user` + `iat` trên Redis | JWT stateless không thu hồi được; thiếu nó thì nâng/hạ vai trò và khóa tài khoản trễ tới 15 phút. Denylist theo `jti` không dùng được vì server không biết `jti` nào đang lưu hành | GĐ1 (đọc) → GĐ6/GĐ8 (ghi) |
| 6 | — | Kiểm tra vai trò hệ thống lúc khởi động — app từ chối chạy nếu `roles.code` bị đổi tay | Đổi `code` của ADMIN là short-circuit không khớp nữa, mà Admin lại không có dòng `role_permissions` nào để rơi về → mất sạch quyền quản trị âm thầm | GĐ1 |
| 7 | — | Bất biến **"luôn còn ≥ 1 Admin hoạt động"** | Bảo vệ đúng thứ đáng bảo vệ: hệ thống chết vì hết *user* Admin, không phải vì mất *role* Admin | GĐ6 + GĐ8 |

**Phương án đã cân nhắc rồi loại bỏ — không phải bỏ sót:** cột `roles.is_system` + trigger dựa
trên cột đó. Tập vai trò cần bảo vệ vĩnh viễn chỉ có 3 cái nên guard gọi thẳng tên là đủ, cột chỉ
lặp lại thông tin đã cố định; và nó canh sai bất biến (xem dòng 7). Chi tiết: `giai-doan-1.md`
Mục 3.4.

---

## Verification tổng (đối chiếu GOAL)
| GOAL | Kiểm chứng | Giai đoạn |
|---|---|---|
| GOAL-01 feed p95 ≤ 500ms | k6 @1.000 CCU | GĐ4 (sơ bộ) → GĐ8 (chính thức) |
| GOAL-02 chat ≤ 1s | E2E 2 trình duyệt, **đo bằng chat UI thật** | GĐ5 |
| GOAL-03 0 IDOR | AuthZ matrix TC-A01..A07 trên CI | GĐ1,2,5,6 → GĐ8 |
| GOAL-04 uptime ≥ 99% | Uptime Kuma + Caddy failover | GĐ7 |
| GOAL-05 NĐ 13/2023 | Inspection quyền xóa + ẩn danh PII | GĐ8 |

## Đầu ra dạng Artifact
Trang HTML trực quan của lộ trình bản B (timeline 8 giai đoạn, hai lane backend/frontend, cổng mở
và cổng đóng từng giai đoạn, bảng rủi ro, bảng cắt khi vỡ tiến độ, bảng verify GOAL):
**https://claude.ai/code/artifact/2c6724f8-6606-49d8-a2fa-5ad484a6f3b5**
