# Báo cáo k6 sơ bộ — `GET /feed` @ 1.000 VU (GĐ4 · C6 · Đ-4.13)

> Đo **sơ bộ** cho GOAL-01 / NFR-PERF-01 (*feed p95 ≤ 500 ms @ 1.000 CCU*) khi GĐ4 còn thời gian sửa. GĐ8 chạy lại
> **chính thức** và so với mốc này. Cách dựng môi trường đo: [`tests/load/feed/README.md`](../../tests/load/feed/README.md);
> kịch bản: [`tests/load/feed/feed.js`](../../tests/load/feed/feed.js).

## Kết luận

**Đạt sơ bộ.** Lượt (2), con số kết luận (cache trang đầu **tắt**), cho **p95 = 40,6 ms**, p99 = 62,1 ms, **0 % lỗi** trên
251.440 request, tức khoảng 1/12 ngưỡng 500 ms. Lượt (3) (Redis dừng ở phút 4) giữ **0 % lỗi**, p95 = 308 ms.

**Sau hai sửa của Mục 5** (code cuối, đo lại 2026-09-23): lượt (2) **p95 37,6 ms**, 0 % lỗi; lượt (3) **p95 190 ms**, p99
300 ms, 0 % lỗi, kết nối DB đỉnh 81 thay vì chạm trần 100, 27 dòng Warning thay vì khoảng 436.000.

Hai vấn đề lộ ra ở lượt (3), dù ngưỡng đã đạt; cách xử lý và số trước/sau ở Mục 5:

1. **Redis dừng thì kết nối DB chạm trần.** Pool Npgsql mặc định 100 bằng đúng `max_connections` 100 của Postgres, nên app
   chiếm hết 100 chỗ. Chính app không lỗi, nhưng `psql`, `migrate` hay một instance thứ hai bị từ chối (`too many clients
   already`). Đây là PERF-03. **Đã sửa**: app tự đặt `Maximum Pool Size=80` khi chuỗi kết nối không ghi (Mục 5.3). Kết
   nối đỉnh 81, bộ đếm `psql` không lần nào bị từ chối, 0 % lỗi.
2. **Redis dừng thì log bị ngập.** Mỗi request ghi 3 dòng Warning fail-open (thu hồi token, cache nguồn, cache trang
   đầu): khoảng 436.000 dòng trong 4 phút, đúng lúc api chạm trần 2 CPU. **Đã sửa**: mỗi loại tối đa một dòng mỗi 30s, kèm
   số lần bỏ qua. Lượt (3) đo lại: 27 dòng Warning, p95 308/264 → **201 ms**, trung bình 90/80 → 60 ms.

## 1. Máy và cấu hình

| Hạng mục | Giá trị |
| --- | --- |
| Máy | Laptop Windows 11 Home, Intel Core i5-12450H (12 luồng), RAM 16 GB |
| Docker | Docker Desktop, engine 29.7.2, WSL2 (kernel 6.18.33.2): 12 CPU, 7,6 GB RAM cho VM |
| k6 | **v2.3.0** (`grafana/k6:2.3.0`, go1.27.1), chạy **trong** mạng compose `socialapp-perf_default`, gọi thẳng `http://api:8080` — không qua BFF |
| API đo | Image build từ `e50ed66` (HEAD sau bước 5); `cpus: 2`, `mem_limit: 12g` (bằng VPS Ampere A1 2 OCPU / 12 GB); `ASPNETCORE_ENVIRONMENT=Development`, log mức Warning |
| Postgres / Redis | `postgres:16-alpine` (`max_connections` 100, `log_min_duration_statement` 200 ms), `redis:7-alpine`; **không** giới hạn CPU |
| Pool Npgsql | Mặc định thư viện: `Maximum Pool Size` 100 (lượt 1–3); 80 ở lượt đo lại Mục 5 |

**Giới hạn của phép đo, cần đọc cùng con số:**

- k6, api, Postgres, Redis chạy chung **một** VM Docker. k6 dùng khoảng 35 % một nhân và 1,15 GB (`docker stats` ở phút
  4,5), Postgres dùng 1,5–5 nhân. Trên VPS thật, Postgres **chung** 2 OCPU với api, còn ở đây Postgres có thêm nhân. Con số
  này vì thế **lạc quan** về phía DB, và GĐ8 phải đo lại trên hạ tầng thật.
- API ở `Development` (không R2, không CORS tường minh). Bộ dữ liệu không có ảnh, nên hydrate không ký URL nào. Ký URL
  là HMAC cục bộ (Đ-2.9) nên phần bỏ qua nhỏ, nhưng vẫn là phần bỏ qua.

## 2. Bộ dữ liệu

`tests/load/feed/seed.sql` (C5, seed lại 2026-09-23): **10.000** hồ sơ, **1.000.000** bài (70/20/10
`public`/`friends`/`private`, khoảng 1 % `hidden`), **600.000** quan hệ bạn bè: 5 % người dùng (n ≤ 500) có khoảng 500 bạn,
còn lại khoảng 100. Có **200.000** dòng theo dõi, 20 mỗi người, và không dòng nào trùng bạn, nên mọi người đều có
`FollowingOnly` khác rỗng.

VU thứ *i* là người dùng thứ `10·(i−1)` trong `users.csv`. 1.000 VU trải đều phân bố, nên 50 VU đầu là người khoảng 500 bạn
(521 nguồn).

## 3. Kịch bản

- Tăng lên 1.000 VU trong 2 phút, giữ 5 phút, giảm trong 1 phút.
- Mỗi vòng `GET /api/v1/feed` (trang đầu, `limit` mặc định). 30 % số vòng đi tiếp một trang bằng `nextCursor`. Nghỉ 1–3 s.
  Như vậy mỗi người dùng gửi khoảng 40 request/phút, dưới rate limit 100.
- Token HS256 tự ký trong `setup()` bằng khóa **riêng** của môi trường đo (`PERF_JWT_KEY`, chỉ nằm trong
  `tests/load/feed/.env`, gitignore). Claim giống `JwtAccessTokenIssuer`, sống 30 phút.
- Ngưỡng: `http_req_duration p(95) < 500`, `http_req_failed < 1%`.
- Tự kiểm trước (5 VU × 30 s): 93/93 request 200, không 401, không 429.
- Mỗi lượt dựng lại container api và `FLUSHALL` Redis. Số kết nối DB đo bằng
  `SELECT count(*) FROM pg_stat_activity WHERE datname = 'socialapp_perf'` mỗi 10 s.

## 4. Ba lượt

| Lượt | Cấu hình | Request | p50 | p95 | p99 | max | Lỗi | Kết nối DB đỉnh | CPU api / Postgres ở phút 4,5 |
| --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| (1) | Mọi cache bật | 253.065 (525/s) | 5,7 ms | 18,5 ms | 35,2 ms | 1,13 s | **0 %** | 40 | 182 % / 154 % |
| **(2)** | `Feed__PageCache__Enabled=false` | 251.440 (521/s) | 11,6 ms | **40,6 ms** | 62,1 ms | 1,05 s | **0 %** | 64 | 197 % / 352 % |
| (3) | Cache bật, Redis dừng lúc 03:28:11 (phút 4) | 240.017 (498/s) | 17,3 ms | 308,3 ms | 456,0 ms | 1,32 s | **0 %** | 39 trước khi dừng → **chạm 100** sau khi dừng | 208 % / 491 % |

- Cả ba lượt đạt hai ngưỡng. Lượt (2) là con số kết luận: nó không phụ thuộc tỷ lệ trúng cache trang đầu, vốn cao giả tạo
  vì kịch bản đọc trang đầu liên tục.
- **Lượt (3) đã chạy hai lần** với cùng kết quả. Lần đầu (03:14–03:22): p95 264 ms, p99 382 ms, 0 % lỗi, bộ đếm kết nối
  bị từ chối từ ngay sau khi Redis dừng. Lần hai (bảng trên) chạy lại chỉ để bộ đếm ghi được thông điệp lỗi: Postgres trả
  `FATAL: sorry, too many clients already` cho mọi lần đếm kể từ 03:28:18.
- Thông lượng khoảng 500 request/s do thời gian nghỉ của kịch bản quyết định, không do trần hệ thống.
- `max` khoảng 1 s ở cả ba lượt rơi vào lúc tăng VU (khởi động lạnh: JIT, pool, model EF), không lặp lại ở pha giữ tải.
- Log api lượt (1) và (2): **không** có dòng Warning/Error nào. Lượt (3): 436.147 dòng Warning (ba loại fail-open, mỗi
  request một lần mỗi loại) và 25 lần `/health/ready` trả 503 (health check Redis). Readiness báo đúng; Docker đánh dấu
  api `unhealthy` cho tới khi Redis lên lại.

## 5. Đo lại lượt (3) — trước và sau hai sửa

Mỗi dòng dưới đây là một lượt (3) đầy đủ: cache bật, Redis dừng ở phút 4, 1.000 VU. Chỉ đổi đúng một thứ so với dòng
trước.

### 5.1 Pool 80 tường minh trong compose đo (PERF-03)

PERF-03 (giai-doan-4.md Mục 14) chốt biện pháp: đặt `Maximum Pool Size` tường minh, nhỏ hơn `max_connections` và chừa
chỗ cho `migrate`/backup. Đo lại lượt (3), chỉ đổi pool thành 80 (chừa 20):

| Lượt (3) | Pool | Request | p50 | p95 | p99 | Lỗi | Kết nối DB sau khi Redis dừng | Bộ đếm `psql` |
| --- | --- | ---: | ---: | ---: | ---: | ---: | --- | --- |
| lần 1 (03:14) | 100 | 241.400 | 16,4 ms | 264,1 ms | 381,6 ms | 0 % | chạm trần | bị từ chối từ 03:18:41 |
| lần 2 (03:24) | 100 | 240.017 | 17,3 ms | 308,3 ms | 456,0 ms | 0 % | chạm trần (`too many clients already`) | bị từ chối từ 03:28:18 |
| **lần 3 (03:36)** | **80** | 241.478 | 16,0 ms | **268,6 ms** | 424,9 ms | **0 %** | **81** = 80 của app + 1 bộ đếm, đứng yên suốt 4 phút | chạy suốt lượt |

Pool 80 không làm p95 tệ đi: 269 ms nằm giữa hai lần đo với pool 100 (264 và 308 ms), tức chênh lệch nằm trong nhiễu của
lượt degrade. Trong log api không có dòng nào về pool hay timeout. Postgres còn 19 chỗ cho `migrate`, backup và `psql`.
Trước khi Redis dừng, mọi lượt chỉ dùng 28–64 kết nối: trần 80 chỉ có tác dụng khi hệ thống degrade.

### 5.2 Giới hạn tần suất log fail-open (commit `fix(gd4-c)` sau `84e4f1a`)

`FailOpenLogThrottle` (SharedKernel, singleton theo host) cho mỗi loại cảnh báo tối đa một dòng mỗi 30s. Dòng kế tiếp mang
`{Suppressed}` = số lần đã bỏ qua. Áp cho bốn chỗ: thu hồi token, đọc nguồn feed, xóa nguồn feed, cache trang đầu. Đo lại
với image build từ code đã sửa, **pool để 100** như hai lần đo đầu, để chỉ đổi đúng phần log:

| Lượt (3) | Log | Pool | Request | p50 | p95 | p99 | Lỗi | Dòng Warning | Kết nối DB đỉnh |
| --- | --- | --- | ---: | ---: | ---: | ---: | ---: | ---: | --- |
| lần 1–2 (Mục 4) | mỗi request | 100 | 241.400 / 240.017 | 16,4 / 17,3 ms | 264 / 308 ms | 382 / 456 ms | 0 % | ≈ 436.000 | chạm trần 100 |
| **lần 4 (06:46)** | **≤ 1 dòng/30s/loại** | 100 | 244.718 | 17,8 ms | **201,3 ms** | 389,4 ms | **0 %** | **27** | **84** |

- Trung bình giảm 90/80 → 60 ms, p90 252 → 157 ms. p99 gần như không đổi (389 ms): đuôi còn lại do Postgres gánh thêm hai
  câu nguồn feed mỗi request (CPU Postgres khoảng 490 %) và api chạm trần 2 nhân, không phải do log.
- Dòng log vẫn đủ thông tin, ví dụ `Suppressed: 18356` cho mỗi loại mỗi 30s (khoảng 610 request/s bị fail-open).
- Kết nối DB không còn chạm trần (84): request xong nhanh hơn thì giữ kết nối ngắn hơn. Nhưng 84 vẫn chỉ cách trần 16,
  nên trần pool vẫn cần (Mục 5.1 và 5.3).

### 5.3 Trần pool 80 mặc định trong code (commit `fix(gd4-c)` sau 5.2)

`PostgresPool.WithDefaultMaxPoolSize` (SharedKernel) thêm `Maximum Pool Size=80` khi chuỗi kết nối chưa ghi; ghi rồi thì
giữ nguyên. Compose đo **không** ghi trần nữa, để đo đúng mặc định của code (đã kiểm biến môi trường của container: không
có khóa pool). Đo lại cả lượt (2) lẫn lượt (3) trên code cuối, có cả sửa 5.2:

| Lượt | Code | Request | p50 | p95 | p99 | Lỗi | Kết nối DB đỉnh | Dòng Warning |
| --- | --- | ---: | ---: | ---: | ---: | ---: | --- | ---: |
| (2) | `e50ed66` (Mục 4) | 251.440 | 11,6 ms | 40,6 ms | 62,1 ms | 0 % | 64 | 0 |
| **(2)** | **cuối** | 251.852 | 11,0 ms | **37,6 ms** | 59,3 ms | **0 %** | 68 | 0 |
| (3) | `e50ed66`, pool 100 (Mục 4) | 240.017 | 17,3 ms | 308,3 ms | 456,0 ms | 0 % | chạm trần 100 | ≈ 436.000 |
| **(3)** | **cuối** | 245.792 | 15,6 ms | **190,2 ms** | **299,6 ms** | **0 %** | **81** (80 app + 1 bộ đếm), bộ đếm không lần nào bị từ chối | **27** |

- Lượt (2) không đổi đáng kể (40,6 → 37,6 ms, trong nhiễu): lúc bình thường pool chỉ dùng 68 kết nối, trần 80 không chạm.
- Lượt (3): hai sửa cộng lại đưa p95 308 → 190 ms, p99 456 → 300 ms. Trần 80 chạm đúng lúc Redis dừng mà không sinh lỗi
  (Npgsql cho request chờ kết nối rảnh, tối đa 15 s) và không làm đuôi dài thêm.

## 6. Truy vấn chậm nhất

Bật `log_min_duration_statement = 200` trong cả năm lượt (kể cả hai lần đo lại lượt 3): **không câu SQL nào vượt
200 ms**. Đã kiểm bộ log hoạt động bằng `SELECT pg_sleep(0.3)`, và câu đó được ghi. Độ trễ đuôi của lượt (3) vì thế nằm ở tầng app (CPU api chạm trần 2 nhân,
log mỗi request, chờ kết nối), không nằm ở một câu SQL chậm.

Câu nặng nhất theo thiết kế là LATERAL của người có 521 nguồn. `EXPLAIN (ANALYZE, BUFFERS)` trên cùng bộ dữ liệu (đầy đủ ở
"Thực tế thi công" C2 của [hướng dẫn B+C+D](huong-dan-khoi-b-c-d-test-feed-endpoint.md)) cho **9,8 ms** ấm:
`Nested Loop` (521 vòng) → `Limit` → `Index Scan using idx_posts_author_created`, và `Sort` duy nhất là top-N trên
10.941 = 521 × 21 dòng. Feed gợi ý đi `idx_posts_public_recent`, dưới 0,1 ms.

## 7. Cache không lưu gì theo người xem (Mục 12 gốc)

Trong lúc chạy smoke có cache: `redis-cli --scan --pattern 'feed:*'` trả 5 khóa. `GET` cả 5 khóa: mỗi giá trị chỉ có
`{"ids":[…],"mode":…,"next":…,"fp":…}`, **0** chuỗi `X-Amz-Signature`, 0 `http`, 0 `canEdit`, 0 `myReaction`. Môi trường
đo không có ảnh nên đây là bằng chứng yếu cho vế "URL"; lưới thật là `FEED-10` và `FEED-13` (Redis thật,
`DistinctGetUrls`).

## 8. Việc chuyển tiếp

| Việc | Vì sao | Chuyển cho |
| --- | --- | --- |
| ~~Đặt `Maximum Pool Size=80`~~ — **đã làm** trong code, mặc định khi chuỗi kết nối không ghi (Mục 5.3); `deploy/.env` không phải sửa | PERF-03 | Kiểm lại ở GĐ8 nếu `max_connections` của VPS khác 100 |
| ~~Giới hạn tần suất log fail-open~~ — **đã làm** trong GĐ4 (Mục 5.2) | Redis dừng thì mỗi request 3 dòng Warning: ngập log và tốn CPU api đúng lúc hệ thống đang degrade | — |
| Đo lại trên hạ tầng giống VPS: Postgres chung 2 OCPU với api, k6 ở máy khác | Mục 1: Postgres ở đây có nhiều nhân hơn thật, và k6 chung VM | GĐ8, bản chính thức |
