# Môi trường đo feed (GĐ4 · C5 · Đ-4.13)

Compose riêng (`socialapp-perf`), API giới hạn **2 CPU / 12 GB** (bằng VPS Ampere A1), DB
`socialapp_perf`, cổng host `15432` / `16379` / `18080` — chạy song song với `socialapp-dev` được.

Khóa JWT đo (`PERF_JWT_KEY`) **không** nằm trong repo, **không** chép từ staging/`deploy/.env`.

## Yêu cầu

- Docker Desktop / daemon
- `openssl` (sinh khóa)
- `psql` client (seed + xuất CSV) — bản đi kèm Postgres hoặc [PostgreSQL client tools](https://www.postgresql.org/download/)
- Bản Postgres image: **16-alpine** · k6 (cho C6, chưa cần ở C5): ghi bản khi cài (`k6 version`)

## 1. Tạo `.env`

```bash
cp tests/load/feed/.env.example tests/load/feed/.env
# Windows PowerShell:
# Copy-Item tests/load/feed/.env.example tests/load/feed/.env
```

Điền:

```bash
openssl rand -base64 24   # → POSTGRES_PASSWORD (hoặc mật khẩu mạnh tự chọn)
openssl rand -base64 48   # → PERF_JWT_KEY
```

## 2. Dựng hạ tầng + build API

Từ **gốc repo**:

```bash
docker compose -f tests/load/feed/docker-compose.perf.yml --env-file tests/load/feed/.env up -d --build
```

Project name lấy từ `name: socialapp-perf` trong file (không dùng `-p perf` — tránh lệch tên volume).

Chờ `api` healthy: `docker compose -f tests/load/feed/docker-compose.perf.yml ps`.

## 3. Migrate bốn schema

```bash
docker compose -f tests/load/feed/docker-compose.perf.yml --env-file tests/load/feed/.env run --rm migrate
```

## 4. Seed

Không cần `psql` trên host — dùng client trong container Postgres:

```bash
docker cp tests/load/feed/seed.sql socialapp-perf-postgres-1:/tmp/seed.sql
docker exec socialapp-perf-postgres-1 \
  psql -v ON_ERROR_STOP=1 -U socialapp -d socialapp_perf -f /tmp/seed.sql
```

PowerShell (đo thời gian):

```powershell
docker cp tests/load/feed/seed.sql socialapp-perf-postgres-1:/tmp/seed.sql
Measure-Command {
  docker exec socialapp-perf-postgres-1 `
    psql -v ON_ERROR_STOP=1 -U socialapp -d socialapp_perf -f /tmp/seed.sql
}
```

> Bắt buộc `-v ON_ERROR_STOP=1`. Thiếu cờ đó thì `psql` in lỗi rồi **chạy tiếp**.

### Đối chiếu số dòng (Bước 4 hướng dẫn)

```sql
SELECT count(*) FROM profile.profiles;          -- 10000
SELECT count(*) FROM content.posts;               -- 1000000
SELECT round(avg(c)::numeric, 1) FROM (
  SELECT count(*) AS c FROM socialgraph.friendships f
  JOIN public.perf_users u ON u.user_id IN (f.user_min_id, f.user_max_id)
  GROUP BY u.user_id
) t;                                              -- ~100–120
SELECT round(avg(c)::numeric, 1) FROM (
  SELECT count(*) AS c FROM socialgraph.follows GROUP BY follower_id
) t;                                              -- 20
```

### Xuất `users.csv` (gitignore — cho C6)

```bash
docker exec socialapp-perf-postgres-1 psql -U socialapp -d socialapp_perf \
  -c "\copy (SELECT user_id FROM public.perf_users ORDER BY n) TO '/tmp/users.csv' CSV HEADER"
docker cp socialapp-perf-postgres-1:/tmp/users.csv tests/load/feed/users.csv
```

## 5. Thử hai chốt chặn (phải đỏ)

```bash
# Chốt 1 — sai database (vd trỏ nhầm và tạo DB khác rồi chạy seed):
psql -v ON_ERROR_STOP=1 -h 127.0.0.1 -p 15432 -U socialapp -d postgres \
  -c "SELECT current_database();" -f tests/load/feed/seed.sql
# → EXCEPTION: seed.sql chỉ chạy trên socialapp_perf

# Chốt 2 — DB đã có user (sau khi đã seed, chèn một dòng identity.users giả rồi chạy lại seed
# sẽ đỏ ở chốt 2; hoặc đơn giản: seed lần hai sau khi đã INSERT vào identity.users).
```

Cách thử chốt 2 gọn (sau seed thành công):

```sql
INSERT INTO identity.users (
  user_id, email, password_hash, role_id, status, created_at, updated_at
) VALUES (
  gen_random_uuid(),
  'probe@perf.invalid',
  '$2a$12$012345678901234567890uabcdefghijklmnopqrstuv',  -- chỉ để thử chốt, không phải hash thật
  1,
  'active',
  now(),
  now()
);
-- rồi chạy lại seed.sql → EXCEPTION identity.users đã có dữ liệu
-- Xóa dòng probe hoặc down -v rồi dựng lại trước khi đo.
```

## 6. Xóa sạch

```bash
docker compose -f tests/load/feed/docker-compose.perf.yml --env-file tests/load/feed/.env down -v
```

## Thời gian seed (thi công 2026-09-22)

| Hạng mục | Giá trị |
| --- | --- |
| Máy | Windows 10 · Docker Desktop WSL2 |
| Docker RAM / CPU host | ~7,6 GB / (giới hạn API `cpus: 2`, `mem_limit: 12g`) |
| Postgres / Redis image | `postgres:16-alpine` · `redis:7-alpine` |
| Thời gian `seed.sql` (lần sạch sau sửa LATERAL) | **~28 s** |
| `count(*)` posts / profiles / friendships / follows | 1.000.000 / 10.000 / 600.000 / 200.000 |
| avg bạn / follows | 120,0 / 20,0 |
| privacy % public/friends/private · hidden | 70,0 / 20,0 / 10,0 · ~10.048 (≈1%) |
| Hai chốt chặn | đã thử đỏ (sai DB `postgres`; `identity.users` có dòng) |

## Ghi chú

- `ASPNETCORE_ENVIRONMENT=Development` cố ý: không cần Mailpit / R2 / CORS tường minh cho đường đo API thẳng.
- API `cpus: 2` + `mem_limit: 12g` — ghi vào báo cáo k6 (C6). Máy host ít hơn 12 GB RAM thì Docker chỉ cấp được phần còn trống; ghi rõ trong báo cáo.
- Project compose: `socialapp-perf` (`name:` trong file). Lệnh gốc có `-p perf` — dùng `name:` để volume
  khớp một tên, tránh lệch `perf_*` vs `socialapp-perf_*`.
- **LATERAL + `random()`:** subquery không tham chiếu cột ngoài thì Postgres gọi `random()` **một lần** cho cả
  1M dòng → mọi bài cùng `privacy`. Seed buộc `SELECT gs AS _row, random() …` trong LATERAL.
