# SocialApp — Mạng xã hội (Facebook-like) · 30INF067

Đồ án mạng xã hội quy mô nhỏ, **triển khai thật lên Internet**: đăng ký/đăng nhập, hồ sơ, đăng bài theo quyền riêng tư,
News Feed, bình luận + cảm xúc, kết bạn/theo dõi, nhắn tin 1-1 realtime, thông báo, kiểm duyệt, quản trị.

| Lớp | Công nghệ |
|---|---|
| Backend | ASP.NET Core 8 · modular monolith (7 module) · EF Core + Npgsql · FluentValidation · Serilog |
| Dữ liệu | PostgreSQL 16 (mỗi module một schema) · Redis 7 |
| Frontend | Next.js 16 (App Router, TypeScript, Tailwind v4) · shadcn/ui trên Base UI · pnpm |
| Hạ tầng | Docker Compose · VM OCI Ampere (**ARM64**) · apache reverse proxy sau Cloudflare · CD bằng GitHub Actions |

**Mục lục:** [Trạng thái](#1-trạng-thái-hiện-tại) · [Bắt đầu từ đâu](#2-người-mới-bắt-đầu-từ-đâu) ·
[Cài đặt](#3-cài-đặt-công-cụ) · [Chạy dev](#4-chạy-backend--frontend-trên-máy-dev) · [Xem database](#5-xem-database-redis-và-mail) ·
[Swagger & hợp đồng API](#6-hợp-đồng-api-và-swagger) · [Staging](#7-staging-xem-sản-phẩm-trên-internet) · [Test & CI](#8-test-và-cổng-ci) ·
[Cấu trúc repo](#9-cấu-trúc-repo) · [Quy trình phát triển](#10-quy-trình-phát-triển) · [Sự cố thường gặp](#11-sự-cố-thường-gặp) ·
[Tài liệu](#12-tài-liệu)

---

## 1. Trạng thái hiện tại

Lộ trình 8 giai đoạn (GĐ0 → GĐ8) ở [`docs/ke-hoach-trien-khai.md`](docs/ke-hoach-trien-khai.md).

| Giai đoạn | Trạng thái |
|---|---|
| **GĐ0 + GĐ0B** — Walking Skeleton, staging, CD | Xong. API chạy thật ở `https://mxh.banhgao.net` |
| **GĐ1** — Identity & Access ([`docs/giai-doan-1/giai-doan-1.md`](docs/giai-doan-1/giai-doan-1.md)) | **Đang làm** |
| GĐ2 → GĐ8 | Chưa bắt đầu. Các module `Profile`, `SocialGraph`, `Content`, `Messaging`, `Notification`, `Moderation` mới có khung thư mục |

Chi tiết GĐ1:

- **Backend — xong** (đã merge vào `develop`, PR #10):
  - khối A: schema `identity`, seeder idempotent
  - khối B + C: harness Testcontainers, AuthZ matrix làm cổng CI, JWT + default deny, `[RequirePermission]`
  - khối D: 6 endpoint `register`, `verify-email`, `login`, `refresh`, `logout`, `GET /me`; refresh rotation + reuse
    detection; thu hồi token qua Redis; lỗi RFC 7807
- **Frontend (khối E) — đang làm:**
  - xong: E1 (scaffold + kit UI), E2 (api client sinh từ hợp đồng, MSW cho Vitest), E4 (màn đăng nhập), E3 (màn đăng ký),
    E5 (màn xác minh email)
  - còn lại: E6 guard + `/me`, E7 refresh single-flight, E8 đóng gói FE cho staging
- **Chưa làm:** ráp FE lên staging (F1–F3). Vì vậy **staging hiện chỉ có API**, chưa có giao diện (xem [Mục 7](#7-staging-xem-sản-phẩm-trên-internet)).

---

## 2. Người mới: bắt đầu từ đâu

1. **Chạy được project** — làm lần lượt [Mục 3](#3-cài-đặt-công-cụ) → [Mục 4](#4-chạy-backend--frontend-trên-máy-dev).
2. **Hiểu hệ thống** — đọc theo thứ tự:
   1. [`AGENTS.md`](AGENTS.md): tech stack, kiến trúc, ranh giới module, **luật vàng**. Người hay agent đều đọc.
   2. [`docs/ke-hoach-trien-khai.md`](docs/ke-hoach-trien-khai.md): thứ tự build chính thức, nhịp "cổng mở hợp đồng API →
      hai lane song song → cổng đóng trên staging".
   3. Tài liệu giai đoạn đang làm: [`docs/giai-doan-1/`](docs/giai-doan-1/). Mỗi khối có một file hướng dẫn, trong đó có
      "cạm bẫy đã biết" và "thực tế thi công".
   4. Bản PTTK gốc: [`docs/BaoCao_Nhom4_v5.pdf`](docs/BaoCao_Nhom4_v5.pdf) (yêu cầu, UC, ERD, ma trận RBAC).
3. **Trước khi sửa code** — đọc luật trong [`.claude/rules/`](.claude/rules/):
   - [`commit-rules.md`](.claude/rules/commit-rules.md): trước **mọi** commit
   - [`pull-request-rules.md`](.claude/rules/pull-request-rules.md): trước khi mở PR
   - [`frontend-rules.md`](.claude/rules/frontend-rules.md) + [`src/frontend/AGENTS.md`](src/frontend/AGENTS.md): trước khi
     chạm `src/frontend/`
4. **Nhận việc** — tìm mã việc tiếp theo (vd `E3`) trong file hướng dẫn khối tương ứng, làm theo mục "Các bước",
   kiểm bằng mục "Test".

---

## 3. Cài đặt công cụ

| Công cụ | Phiên bản | Ghi chú |
|---|---|---|
| Git | bất kỳ | Trên Windows nên có **Git Bash** (dùng `openssl` để sinh khóa) |
| Docker Desktop / Docker Engine + Compose v2 | mới | Chạy Postgres/Redis/Mailpit ở dev **và** bắt buộc cho integration test (Testcontainers) |
| .NET SDK | **8.0.424** trở lên trong dòng 8.0 | Ghim ở [`global.json`](global.json) |
| Node.js | **24 LTS** | Ghim ở [`src/frontend/.nvmrc`](src/frontend/.nvmrc) |
| pnpm | **10.33.1** | Ghim ở trường `packageManager`. Cài bằng `corepack enable`. **Không dùng npm/yarn** |
| Google Chrome | bản đã cài | Chỉ cần nếu chạy Playwright (`pnpm test:e2e`) |
| `dotnet-ef` | 8.x | Chỉ cần khi **tạo** migration: `dotnet tool install --global dotnet-ef --version 8.*` |

Kiểm tra nhanh:

```bash
docker compose version && dotnet --version && node --version && pnpm --version
```

---

## 4. Chạy backend + frontend trên máy dev

Mọi lệnh chạy từ **gốc repo**, trừ khi ghi khác.

### 4.1. Cấu hình lần đầu: `deploy/.env`

Máy dev chỉ cần **hai** giá trị bí mật. Repo **không** chứa mật khẩu hay khóa mặc định nào: thiếu thì compose, `dotnet run`
và `dotnet ef` đều từ chối chạy, kèm thông báo nêu đúng key còn thiếu.

```bash
cp deploy/.env.example deploy/.env
openssl rand -base64 24   # → dán vào POSTGRES_PASSWORD
openssl rand -base64 48   # → dán vào Jwt__SigningKey (≥ 32 byte)
```

Mở `deploy/.env` và điền **đúng hai dòng** đó:

```dotenv
POSTGRES_PASSWORD=<chuỗi vừa sinh>
Jwt__SigningKey=<chuỗi vừa sinh>
```

> - Các key còn lại trong `.env.example` (`Smtp__*`, `Cors__*`, `Frontend__BaseUrl`, `R2__*`…) dùng cho **staging**. Ở dev
>   để trống: code tự dùng mặc định Development (Mailpit `localhost:1025`, CORS `http://localhost:3000`).
> - `deploy/.env` đã nằm trong `.gitignore`. **Không bao giờ commit nó.**
> - Postgres chỉ đọc `POSTGRES_PASSWORD` **lúc tạo volume lần đầu**. Đổi mật khẩu sau đó thì phải xóa volume
>   (xem [Mục 11](#11-sự-cố-thường-gặp)).

### 4.2. Bật hạ tầng: Postgres + Redis + Mailpit

```bash
docker compose -f deploy/docker-compose.dev.yml up -d postgres redis mailpit
docker compose -f deploy/docker-compose.dev.yml ps      # chờ postgres, redis ở trạng thái healthy
```

Chỉ bật ba service này, **không** bật `api`: API sẽ chạy bằng `dotnet run` ở bước sau để debug được và hot-reload.

### 4.3. Tạo schema + dữ liệu nền (migrate)

```bash
dotnet run --project src/backend/SocialApp.Api -- --migrate
```

Lệnh này apply EF migration, seed vai trò/quyền (`USER`, `MODERATOR`, `ADMIN`), kiểm tra vai trò hệ thống, rồi **thoát**.
API **không** tự migrate khi khởi động. Chạy lại lệnh này mỗi khi `git pull` về migration mới. Lệnh an toàn để chạy
nhiều lần.

### 4.4. Chạy backend

```bash
dotnet run --project src/backend/SocialApp.Api
```

API lắng nghe ở **http://localhost:5259** (profile `http` trong `launchSettings.json`, môi trường `Development`). Kiểm tra:

| URL | Kết quả mong đợi |
|---|---|
| http://localhost:5259/api/v1/ping | `{"message":"pong","traceId":"…"}` |
| http://localhost:5259/health/ready | `Healthy` (Postgres + Redis đều thông) |
| http://localhost:5259/swagger | Swagger UI ([Mục 6](#6-hợp-đồng-api-và-swagger)) |

Log ra console dạng JSON (Serilog compact), mỗi request có `X-Correlation-ID`.

### 4.5. Chạy frontend

```bash
cd src/frontend
pnpm install          # lần đầu, hoặc khi pnpm-lock.yaml đổi
pnpm dev
```

Mở **http://localhost:3000**. FE gọi thẳng `http://localhost:5259/api/v1` (mặc định trong `lib/api/config.ts`, **không cần**
file `.env.local`). CORS với `credentials` được bật cho origin `http://localhost:3000`.

> **Cấm trỏ FE local sang API staging.** Khác site thì cookie refresh (`SameSite=Lax`) không được gửi, `/auth/refresh`
> luôn 401 mà không lỗi nào nói lý do.

### 4.6. Tóm tắt cổng trên máy dev

| Dịch vụ | Địa chỉ |
|---|---|
| Frontend (Next.js) | http://localhost:3000 |
| API (`dotnet run`) | http://localhost:5259 |
| Swagger UI | http://localhost:5259/swagger |
| Mailpit, xem mail xác minh | http://localhost:8025 (SMTP `localhost:1025`) |
| PostgreSQL | `localhost:5432`, db `socialapp`, user `socialapp` |
| Redis | `localhost:6379` |
| API trong container (tùy chọn) | http://localhost:8080 |

### 4.7. Thử luồng đăng ký → đăng nhập trên dev

DB không seed sẵn người dùng nào. Cả vòng làm được trên giao diện:

1. Đăng ký ở http://localhost:3000/register → màn "Kiểm tra hộp thư".
2. Mở Mailpit http://localhost:8025, mở mail xác minh, bấm link (`http://localhost:3000/verify-email?token=…`) → màn báo
   "đã được xác minh". Link chỉ dùng được **một lần**: mở lại là "đã hết hạn hoặc đã được sử dụng" — đúng, không phải lỗi.
3. Đăng nhập ở http://localhost:3000/login.

Không chạy FE thì làm cùng ba bước qua Swagger: `POST /api/v1/auth/register` →
`POST /api/v1/auth/verify-email` với `{"token":"<token trong link>"}` → `POST /api/v1/auth/login`.

Muốn tài khoản `ADMIN`/`MODERATOR`: đổi `role_id` trong DB ([Mục 5](#5-xem-database-redis-và-mail)), rồi **đăng nhập
lại** (vai trò nằm trong access token).

Rate limit nhóm `/auth/*` là **10 request/phút/IP**. Thử nhiều sẽ gặp 429, chờ một phút.

### 4.8. Các cách chạy khác

Frontend **không có chế độ mock** (đổi Đ-E7 ngày 2026-09-17): muốn xem giao diện thì chạy đủ Mục 4.2–4.5. MSW chỉ còn
trong Vitest (`msw/node`) để dựng nhánh lỗi khó tạo thật (410, 423, 429, 500).

**Toàn bộ backend trong Docker** (không cần cài .NET, nhưng không debug được):

```bash
docker compose -f deploy/docker-compose.dev.yml up -d --build
docker compose -f deploy/docker-compose.dev.yml run --rm --entrypoint "dotnet SocialApp.Api.dll --migrate" api
```

API khi đó ở **http://localhost:8080**. Muốn FE gọi vào đây thì tạo `src/frontend/.env.local` với
`NEXT_PUBLIC_API_BASE_URL=http://localhost:8080/api/v1`. Đừng chạy song song với `dotnet run` nếu không cần: hai API cùng
trỏ một DB.

**Rút ngắn access token để thử refresh** (E7) — mặc định 900 giây:

```powershell
$env:Jwt__AccessTokenSeconds = "10"; dotnet run --project src/backend/SocialApp.Api     # PowerShell
```

```bash
Jwt__AccessTokenSeconds=10 dotnet run --project src/backend/SocialApp.Api                # bash
```

**Tắt hạ tầng:** `docker compose -f deploy/docker-compose.dev.yml down`. Thêm `-v` để **xóa sạch dữ liệu** DB.

---

## 5. Xem database, Redis và mail

### 5.1. PostgreSQL trên dev

**Cách 1 — `psql` trong container** (không cần cài gì thêm):

```bash
docker compose -f deploy/docker-compose.dev.yml exec postgres psql -U socialapp -d socialapp
```

```sql
\dn                                   -- liệt kê schema: mỗi module một schema (hiện có: identity)
\dt identity.*                        -- bảng của module Identity
\d identity.users                     -- cấu trúc một bảng

SELECT user_id, email, role_id, status, email_verified_at, failed_login_count, locked_until
FROM identity.users ORDER BY created_at DESC;

SELECT r.code, p.code AS permission
FROM identity.role_permissions rp
JOIN identity.roles r USING (role_id)
JOIN identity.permissions p USING (permission_id)
ORDER BY 1, 2;                        -- ADMIN cố ý không có dòng nào (short-circuit ở tầng 2)

SELECT * FROM identity."__EFMigrationsHistory";

-- Nâng quyền để thử (role_id: 1 USER · 2 MODERATOR · 3 ADMIN), rồi đăng nhập lại
UPDATE identity.users SET role_id = 3 WHERE email = 'a@example.com';
```

Thoát bằng `\q`. Nguồn sự thật của schema là `src/backend/Modules/Identity/Infrastructure/Configurations/` và migration
trong `Infrastructure/Migrations/`.

**Cách 2 — công cụ GUI** (DBeaver, pgAdmin, DataGrip, extension PostgreSQL của VS Code):

| Trường | Giá trị |
|---|---|
| Host / Port | `localhost` / `5432` |
| Database | `socialapp` |
| User | `socialapp` |
| Password | giá trị `POSTGRES_PASSWORD` trong `deploy/.env` |
| SSL | tắt |

Bảng nằm trong schema `identity`, không phải `public`. Nhớ bật hiển thị schema đó trong công cụ.

Bảng của module Identity: `users`, `roles`, `permissions`, `role_permissions`, `refresh_tokens` (chỉ lưu **băm**, có
`family_id`), `email_verification_tokens` (chỉ lưu băm).

### 5.2. Redis trên dev

```bash
docker compose -f deploy/docker-compose.dev.yml exec redis redis-cli
> KEYS revoked:user:*       # mốc thu hồi access token theo user (đăng xuất mọi nơi / reuse detection)
> TTL  revoked:user:<id>
```

### 5.3. Mail trên dev

Mọi mail (xác minh đăng ký…) rơi vào **Mailpit**: http://localhost:8025. Không có mail nào đi ra Internet.

### 5.4. Database trên staging

Postgres và Redis staging **không mở cổng ra ngoài**. Chỉ người có SSH vào VM mới xem được:

```bash
ssh <user>@<staging-host>
cd ~/app/deploy
docker compose -f docker-compose.staging.apache.yml exec postgres psql -U socialapp -d socialapp
docker compose -f docker-compose.staging.apache.yml exec redis redis-cli
```

Đây là dữ liệu dùng để demo. Chỉ **đọc**, đừng `UPDATE`/`DELETE` khi chưa báo nhóm.

---

## 6. Hợp đồng API và Swagger

### 6.1. Hai thứ khác nhau

| | Là gì | Ở đâu |
|---|---|---|
| **Hợp đồng API** (nguồn sự thật) | File OpenAPI chốt ở cổng mở mỗi giai đoạn; FE sinh type + mock từ đây | `src/backend/Modules/<Module>/Presentation/<nhóm>.yaml`, hiện có [`identity-v1.yaml`](src/backend/Modules/Identity/Presentation/identity-v1.yaml) |
| **Swagger runtime** | Điều code **thật sự** làm, sinh từ controller | `/swagger` của API đang chạy |

Cổng CI `API contract` so hai bên (tập path × method, status code, field bắt buộc) và **đỏ nếu lệch**. Đổi hợp đồng thì sửa
`.yaml` **trong cùng commit** với code. Chi tiết: [`Identity/Presentation/README.md`](src/backend/Modules/Identity/Presentation/README.md).

### 6.2. Xem Swagger

| Môi trường | Swagger UI | JSON của nhóm Identity |
|---|---|---|
| Dev | http://localhost:5259/swagger | http://localhost:5259/swagger/identity-v1/swagger.json |
| Staging | https://mxh.banhgao.net/swagger | https://mxh.banhgao.net/swagger/identity-v1/swagger.json |

Swagger **tách theo module**: chọn trang ở dropdown **"Select a definition"** góc trên bên phải.

- `Platform`: `/api/v1/ping` và hai endpoint demo lỗi RFC 7807 (`app-error` → 409, `boom` → 500).
- `Identity`: `/auth/register`, `/auth/verify-email`, `/auth/login`, `/auth/refresh`, `/auth/logout`, `/me`.

Swagger bật ở Development và Staging, **tắt ở Production**.

### 6.3. Gọi endpoint cần đăng nhập

Swagger UI hiện **chưa có nút Authorize**. Endpoint cần token (`GET /me`) thì gọi bằng `curl`:

```bash
# 1) Đăng nhập, lấy accessToken (refresh token nằm trong cookie HttpOnly, path /api/v1/auth)
curl -s -c cookies.txt -H "Content-Type: application/json" \
  -d '{"email":"a@example.com","password":"matkhau123"}' \
  http://localhost:5259/api/v1/auth/login

# 2) Gọi /me với access token
curl -s -H "Authorization: Bearer <accessToken>" http://localhost:5259/api/v1/me

# 3) Xoay refresh token (không có body; cookie tự gửi)
curl -s -b cookies.txt -c cookies.txt -X POST http://localhost:5259/api/v1/auth/refresh
```

Trên staging thay gốc URL bằng `https://mxh.banhgao.net`. `/auth/refresh` và `/auth/logout` gọi thẳng trong Swagger UI
cũng được, vì trình duyệt tự gửi cookie cùng origin.

### 6.4. Kiểm hợp đồng tại chỗ

```bash
npx @redocly/cli lint src/backend/Modules/Identity/Presentation/identity-v1.yaml   # 2 warning đã biết, 0 error
dotnet test tests/SocialApp.IntegrationTests --filter "Category=Contract"

cd src/frontend && pnpm gen:api     # sinh lại lib/api/schema.d.ts từ yaml, COMMIT file sinh ra
```

---

## 7. Staging: xem sản phẩm trên Internet

**URL:** https://mxh.banhgao.net

| Đường dẫn | Hiện có gì |
|---|---|
| https://mxh.banhgao.net/swagger | Swagger UI của API staging. **Đây là cách chính để thử sản phẩm lúc này** |
| https://mxh.banhgao.net/api/v1/ping | `pong`, kiểm tra API sống |
| https://mxh.banhgao.net/health/ready | `Healthy` khi Postgres + Redis thông |
| https://mxh.banhgao.net/ | **Chưa có giao diện**: vẫn là trang mặc định của apache. Frontend lên staging ở E8 + F1 của GĐ1 |

### 7.1. Thử luồng GĐ1 trên staging

1. Mở `/swagger`, chọn definition **Identity**.
2. `POST /api/v1/auth/register` bằng **email thật của bạn**. Staging gửi mail qua Brevo, có hạn mức theo ngày, nên đừng
   đăng ký hàng loạt.
3. Mail xác minh chứa link `https://mxh.banhgao.net/verify-email?token=…`. Trang đó **chưa tồn tại** khi FE chưa deploy:
   chép giá trị `token` rồi gọi `POST /api/v1/auth/verify-email` trong Swagger.
4. `POST /api/v1/auth/login` → nhận `accessToken`. Gọi `GET /me` bằng `curl` như [Mục 6.3](#63-gọi-endpoint-cần-đăng-nhập).

Khi FE đã deploy (sau F1), sản phẩm xem ở chính https://mxh.banhgao.net: apache chuyển `/api`, `/swagger`, `/health` về
backend và mọi đường dẫn còn lại về Next.js, cùng một domain.

### 7.2. Staging được cập nhật thế nào

```
merge vào develop ──▶ GitHub Actions "deploy-staging"
                      ├─ build image linux/arm64 → ghcr.io/ricecracker12/30inf067_btl/api:staging
                      └─ SSH vào VM, trong ~/app/deploy:
                           compose pull → run --rm migrate → up -d --remove-orphans
```

- **Không deploy tay.** Muốn staging có code mới thì merge PR vào `develop`.
- Script deploy có `set -e`: migrate hỏng hoặc thiếu cấu hình thì workflow **đỏ**, api cũ vẫn chạy.
- Xem tiến trình ở tab **Actions** của repo GitHub.
- API **từ chối khởi động** khi `.env` trên server thiếu key bắt buộc (`ConnectionStrings__*`, `Jwt__SigningKey`,
  `Smtp__Host/Port/From`, `Frontend__BaseUrl`, `Cors__AllowedOrigins__0`). PR thêm key mới phải ghi tên key ở mục
  "Trước khi merge" để người có quyền đặt lên server **trước** khi merge.

Người có SSH xem log:

```bash
cd ~/app/deploy
docker compose -f docker-compose.staging.apache.yml ps
docker compose -f docker-compose.staging.apache.yml logs -f --tail 200 api
```

Dựng hạ tầng staging từ đầu (Docker, apache, TLS Cloudflare, secrets, CD): [`docs/oci-setup.md`](docs/oci-setup.md) và
[`deploy/apache-socialapp.conf.example`](deploy/apache-socialapp.conf.example).

---

## 8. Test và cổng CI

### 8.1. Backend

**Docker phải đang chạy**: integration test tự dựng Postgres + Redis bằng Testcontainers, không dùng DB dev. CI cũng không
cần `deploy/.env`.

```bash
dotnet build SocialApp.sln
dotnet test SocialApp.sln                                           # tất cả
dotnet test tests/SocialApp.UnitTests                               # nhanh, không cần Docker
dotnet test tests/SocialApp.ArchitectureTests                       # ranh giới module (ArchUnitNET)
dotnet test tests/SocialApp.IntegrationTests --filter "Category=AuthZ"      # ma trận quyền / IDOR
dotnet test tests/SocialApp.IntegrationTests --filter "Category=Contract"   # yaml ↔ Swagger
```

### 8.2. Frontend

Chạy từ `src/frontend/`. **Bốn lệnh đầu phải xanh trước khi commit code FE.**

```bash
pnpm lint         # ESLint: chặn fetch ngoài lib/api/http.ts, localStorage, màu thô, import chéo features/
pnpm typecheck
pnpm test         # Vitest + Testing Library + msw/node
pnpm build
pnpm test:e2e     # Playwright trên Chrome đã cài, workers: 1 — cần API dev + hạ tầng đang chạy; KHÔNG chạy trong CI
```

### 8.3. CI trên GitHub (`.github/workflows/ci.yml`)

Chạy khi push lên `main`, `develop`, `loveart1210` và khi mở PR vào `main`/`develop`.

| Job | Bước chặn merge |
|---|---|
| `build-test` (.NET) | Không có repo Git lồng trong `src/` · build · unit + integration · **API contract** · **AuthZ matrix** |
| `frontend` | **API types khớp hợp đồng** (`pnpm gen:api` không làm đổi `schema.d.ts`) · lint · typecheck · test · build · **bundle production sạch MSW + URL API dev** |

---

## 9. Cấu trúc repo

```
30INF067_btl/
├─ AGENTS.md                         # kiến trúc + luật vàng — đọc trước khi sửa code
├─ .claude/rules/                    # luật bắt buộc: commit, PR, frontend
├─ SocialApp.sln · global.json
├─ Dockerfile                        # image API (context = gốc repo, arm64, non-root)
├─ .github/workflows/
│  ├─ ci.yml                         # cổng CI
│  └─ deploy-staging.yml             # CD: develop → staging
├─ deploy/
│  ├─ docker-compose.dev.yml         # Postgres, Redis, Mailpit (+ api tùy chọn) cho máy dev
│  ├─ docker-compose.staging.apache.yml   # biến thể ĐANG DÙNG trên VM (apache giữ 80/443)
│  ├─ docker-compose.staging.yml + Caddyfile   # biến thể Caddy (dự phòng)
│  ├─ apache-socialapp.conf.example  # vhost path-based: /api /swagger /health → API, / → FE
│  └─ .env.example                   # TÊN các key; giá trị thật chỉ ở deploy/.env (gitignore)
├─ docs/
│  ├─ ke-hoach-trien-khai.md         # lộ trình GĐ0 → GĐ8
│  ├─ oci-setup.md                   # hạ tầng staging
│  ├─ BaoCao_Nhom4_v5.pdf            # PTTK gốc
│  └─ giai-doan-1/                   # thiết kế GĐ1 + hướng dẫn từng khối A–E
├─ src/
│  ├─ backend/
│  │  ├─ SocialApp.Api               # host: Program.cs, DI, middleware, nhóm Swagger
│  │  ├─ SocialApp.SharedKernel      # AuthN/AuthZ, RFC 7807, correlation ID, rate limit, Redis, Result
│  │  └─ Modules/<Module>/           # Identity · Profile · SocialGraph · Content · Messaging · Notification · Moderation
│  │     ├─ Domain/                  # entity, quy tắc nghiệp vụ — không EF, không HTTP
│  │     ├─ Application/             # use case, interface, validator
│  │     ├─ Infrastructure/          # EF DbContext (schema riêng), Migrations/, store, email
│  │     └─ Presentation/            # controller + <nhóm>.yaml (hợp đồng API)
│  └─ frontend/                      # Next.js 16 — phụ thuộc một chiều: app/ → features/ → components/ + lib/
│     ├─ app/                        # route, layout, page mỏng
│     ├─ features/<màn>/             # nghiệp vụ theo màn (auth/…)
│     ├─ components/                 # ui/ (kit shadcn, sinh bằng CLI) · form/ · shell/
│     ├─ lib/                        # api/ (client + schema.d.ts sinh) · auth/ · validation/
│     ├─ mocks/                      # handler MSW cho Vitest (dev không có mock)
│     └─ e2e/                        # spec Playwright
└─ tests/
   ├─ SocialApp.UnitTests
   ├─ SocialApp.IntegrationTests     # Testcontainers; AuthZ/ (ma trận quyền), *ContractTests
   ├─ SocialApp.ArchitectureTests    # chặn tham chiếu chéo module, EF/HTTP lọt tầng
   └─ load/                          # k6 (GĐ8)
```

**Quy ước API:** REST `/api/v1` · lỗi RFC 7807 (`application/problem+json`, có `traceId`) · cursor pagination (limit 20,
tối đa 50) · UUID v7 · JWT HS256 access 15 phút trong memory + refresh token xoay vòng trong cookie `HttpOnly` · phân
quyền 3 tầng (AuthN → RBAC `[RequirePermission]` → ownership), **default deny**.

---

## 10. Quy trình phát triển

### 10.1. Nhánh

```
loveart1210 (nhánh làm việc) ──PR──▶ develop (tự deploy staging) ──PR phát hành──▶ main
```

- Không mở PR thẳng vào `main`. Merge bằng **merge commit**, không squash.
- **Một PR = một khối công việc**, mở khi khối đã xong.

### 10.2. Commit

Theo [`commit-rules.md`](.claude/rules/commit-rules.md): Conventional Commits, **nội dung tiếng Việt có dấu**, scope là
khối công việc.

```
feat(gd1-e): E4 — màn đăng nhập: thông điệp theo mã lỗi, token chỉ ở memory

<vì sao, chỗ lệch so với kế hoạch>

Test: Vitest 24 → 37
detect-changes: low, 0 luồng
```

Không có dòng bút ký/đồng tác giả của công cụ hay AI, ở cả commit lẫn mô tả PR.

### 10.3. Công việc thường gặp

**Thêm migration cho một module:**

```bash
dotnet ef migrations add <TenMigration> \
  --project src/backend/Modules/Identity/SocialApp.Modules.Identity.csproj \
  --startup-project src/backend/Modules/Identity/SocialApp.Modules.Identity.csproj \
  --output-dir Infrastructure/Migrations
dotnet run --project src/backend/SocialApp.Api -- --migrate      # apply lên DB dev
```

Không cần đặt biến: design-time factory đọc `POSTGRES_PASSWORD` từ `deploy/.env`. Migration theo kiểu expand–contract
(tương thích ngược một phiên bản).

**Thêm endpoint:** controller ở `Modules/<Module>/Presentation/` với `[ApiExplorerSettings(GroupName = …)]`. Thiếu thuộc
tính này thì endpoint **biến mất khỏi Swagger** mà không báo lỗi. Tiếp theo:

- sửa `<nhóm>.yaml` trong cùng commit
- endpoint chạm tài nguyên có chủ sở hữu thì thêm dòng vào `tests/SocialApp.IntegrationTests/AuthZ/AuthZMatrix.cs`
- chạy `pnpm gen:api` nếu FE dùng tới

**Thêm component UI:** `cd src/frontend && pnpm exec shadcn add <tên>`. Dùng bản CLI ghim trong dự án, **không**
`pnpm dlx shadcn@latest`. Màu và radius chỉ khai ở `app/globals.css`.

**Thêm key cấu hình bắt buộc:**

- thêm **tên** key vào `deploy/.env.example`
- ghi vào mục "Trước khi merge" của PR
- đặt giá trị lên `.env` của server trước khi merge

### 10.4. GitNexus (tùy chọn, dành cho agent)

Repo được index bởi GitNexus để phân tích tác động trước khi sửa. Xem [`CLAUDE.md`](CLAUDE.md). Index cũ thì chạy
`node .gitnexus/run.cjs analyze --index-only`. Thư mục `.gitnexus/` không commit.

---

## 11. Sự cố thường gặp

| Triệu chứng | Nguyên nhân / cách sửa |
|---|---|
| `Thiếu POSTGRES_PASSWORD trong deploy/.env` hoặc compose báo `Đặt POSTGRES_PASSWORD…` | Chưa làm [Mục 4.1](#41-cấu-hình-lần-đầu-deployenv) |
| `Cấu hình JWT không hợp lệ… Jwt:SigningKey trống hoặc ngắn hơn 32 byte` | Điền `Jwt__SigningKey` bằng `openssl rand -base64 48` |
| `password authentication failed for user "socialapp"` | Volume Postgres tạo với mật khẩu cũ. Xóa dữ liệu dev: `docker compose -f deploy/docker-compose.dev.yml down -v`, bật lại, migrate lại |
| `relation "identity.users" does not exist` / 500 khi đăng ký | Chưa chạy `-- --migrate` ([Mục 4.3](#43-tạo-schema--dữ-liệu-nền-migrate)) |
| `/health/ready` trả `Unhealthy` | Postgres/Redis chưa chạy: `docker compose -f deploy/docker-compose.dev.yml ps` |
| Cổng 5432/6379 đã bị chiếm | Máy đã có Postgres/Redis cài sẵn. Tắt service đó, hoặc đặt `ConnectionStrings__Postgres` / `ConnectionStrings__Redis` trỏ nơi khác |
| FE báo lỗi mạng / CORS | API chưa chạy ở `5259`, hoặc FE không chạy ở đúng `http://localhost:3000` (origin khác thì CORS chặn) |
| `/auth/refresh` luôn 401 | FE đang trỏ sang API khác site (vd staging), hoặc gọi thiếu `credentials: 'include'` |
| 429 Too Many Requests | Rate limit `/auth/*` 10 request/phút/IP. Chờ một phút |
| Integration test lỗi `Docker is either not running…` | Bật Docker Desktop |
| `pnpm` sai phiên bản / lockfile lỗi | `corepack enable` rồi `pnpm install`. Không dùng `npm install` |
| Không thấy mail xác minh (dev) | Mailpit chưa bật: `docker compose -f deploy/docker-compose.dev.yml up -d mailpit` |
| Staging `/login` 404, `/` là trang apache | Bình thường ở thời điểm này: FE chưa deploy (E8/F1) |

---

## 12. Tài liệu

| Tài liệu | Nội dung |
|---|---|
| [`AGENTS.md`](AGENTS.md) | Tech stack, kiến trúc, ranh giới module, bảo mật, luật vàng |
| [`docs/ke-hoach-trien-khai.md`](docs/ke-hoach-trien-khai.md) | Lộ trình 8 giai đoạn, thứ tự build chính thức |
| [`docs/giai-doan-1/giai-doan-1.md`](docs/giai-doan-1/giai-doan-1.md) | Thiết kế + kế hoạch GĐ1 (quyết định, schema, 3 tầng phân quyền, luồng token) |
| [`docs/giai-doan-1/huong-dan-khoi-*.md`](docs/giai-doan-1/) | Hướng dẫn thi công từng khối A–E, kèm "thực tế thi công" |
| [`docs/oci-setup.md`](docs/oci-setup.md) | Hạ tầng staging OCI, Cloudflare, secrets, CD |
| [`src/backend/Modules/Identity/Presentation/README.md`](src/backend/Modules/Identity/Presentation/README.md) | Hợp đồng API Identity và cách FE dùng |
| [`src/frontend/README.md`](src/frontend/README.md) · [`src/frontend/AGENTS.md`](src/frontend/AGENTS.md) | Lệnh FE, luật UI kit, cấu trúc bốn tầng |
| [`.claude/rules/`](.claude/rules/) | Luật commit, PR, frontend |
| [`docs/BaoCao_Nhom4_v5.pdf`](docs/BaoCao_Nhom4_v5.pdf) | PTTK: yêu cầu, UC, FR/NFR, ERD, ma trận RBAC, ADR |
