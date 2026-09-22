# Hướng dẫn thực hiện — Khối B. Test + cổng CI · Khối C. Feed + hiệu năng · Khối D. Endpoint (GĐ4)

> Bản triển khai chi tiết của **B.4, B.5, B.6** trong [giai-doan-4.md](giai-doan-4.md). Tài liệu gốc trả lời *cái gì* và
> *vì sao*; tài liệu này trả lời *gõ vào file nào, theo thứ tự nào, và nhìn vào đâu để biết đã xong thật*.
>
> **Nguồn sự thật vẫn là** `giai-doan-4.md` (Mục 3 quyết định `Đ-4.*`, Mục 6 ba tầng quyền, Mục 7 luồng, Mục 8 hợp đồng,
> Mục 10 chiến lược test, Mục 9.3 bảng bước) và `AGENTS.md`. Chỗ nào tài liệu này lệch với hai file đó thì sửa ở đây —
> không sửa ngược. Muốn đổi một `Đ-4.*` thì đó là **quyết định mới**, có ngày tháng, ghi vào `giai-doan-4.md` trong cùng
> commit.
>
> **Vì sao ba khối một file, và sắp theo bước chứ không theo khối** (chốt 2026-09-22, ghi ở Mục 9.3): ở bước 4 và 5, việc
> của ba khối đan vào nhau từng giờ — `FRD-*` viết trước từng `D*`, `D2`–`D6` gọi `InvalidateAsync` của `C1`, `D7` là vỏ
> mỏng của `C1`–`C4`, `B4` cần cả hai. Ba file riêng là ba file trỏ chéo nhau liên tục. Đọc tài liệu này **từ trên xuống
> theo đúng thứ tự làm**; mục lục theo khối nằm ở Mục 0.
>
> Khuôn để chép: khối B+C GĐ2 ([huong-dan-khoi-b-c-test-va-luu-tru.md](../giai-doan-2/huong-dan-khoi-b-c-test-va-luu-tru.md)),
> khối D GĐ2 ([huong-dan-khoi-d-endpoint.md](../giai-doan-2/huong-dan-khoi-d-endpoint.md)), khối A GĐ4
> ([huong-dan-khoi-a-nen-du-lieu.md](huong-dan-khoi-a-nen-du-lieu.md)).

|                          |                                                                                                                                                                                                              |
| ------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| **Người làm**            | Một người (không chia lane — `giai-doan-4.md` Mục 9)                                                                                                                                                         |
| **Thời lượng**           | Bước 2 (phần `B1`–`B2`, ~0,4 ngày) · bước 3 (0,5) · bước 4 (1,2) · bước 5 (1,3) · bước 6 (1). Khoảng **4,4 ngày** trên tổng ~8 ngày của giai đoạn                                                            |
| **Cần trước**            | Khối A xong (2026-09-22): schema `socialgraph`, `idx_posts_public_recent`, `IFriendshipReader` thật, `IFeedSourceReader` chưa cache, harness migrate bốn module. `socialgraph-v1.yaml` đã commit ở cổng mở |
| **Chặn**                 | Khối E ráp thật (cần `D*` chạy), khối F (`F1` deploy, `F3` checklist đòi matrix + đột biến + báo cáo k6), GĐ3 (hàm hydrate `C3` là chỗ nó cắm `myReaction`)                                                  |
| **Không thuộc ba khối**  | Frontend, deploy staging, E2E hai tài khoản, đóng băng hợp đồng. Xem Mục 19                                                                                                                                 |

---

## 0. Danh sách công việc — mục tiêu và kết quả mong đợi

Mười tám đầu việc: năm của B, sáu của C, bảy của D. Mục tiêu khối chép từ B.4–B.6:

- **B** — biến BR-03, BR-02 thật và Đ-4.9 thành thứ **chặn merge**.
- **C** — feed đúng quyền, có chi phí bị chặn trên, chịu được Redis chết — và có một con số k6 để chứng minh.
- **D** — hai hợp đồng thành hệ thống chạy thật, khớp từng mã lỗi.

### 0.1 Khối B — Test và cổng CI

| Mã     | Đầu việc                                                                                              | Mục tiêu — việc này tồn tại để làm gì                                                                                                                                                                                                 | Kết quả mong đợi — thứ kiểm chứng được                                                                                                                                                                                                                                   |
| ------ | ----------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| **B1** | Harness: Redis thật cho `ModulesApiFactory`, URL ký khác nhau ở `FakeObjectStorage`, bộ đếm lệnh SQL, đo giờ | Cho test công cụ để thấy ba loại lỗi harness hiện mù: cache không chạy (Redis cổng 1), cache lưu URL đã ký (fake trả URL hằng), N+1 (không ai đếm lệnh). Thiếu `B1` thì `FEED-09/10/13`, `FEED-Q1` xanh vì lý do sai               | `ModulesApiFactory.UseRedis(...)`; `FakeObjectStorage.DistinctGetUrls` (tắt mặc định); `SqlCommandCounter` **đã từng đếm ra > 0** trên truy vấn biết trước; thời gian bộ integration trước/sau trong thân commit; toàn bộ test cũ xanh không sửa khẳng định                |
| **B2** | Sáu dòng AuthZ matrix (năm dòng Mục 6.3 + `READ-06b`), viết **trước** endpoint                          | Mỗi thao tác GĐ4 chạm quan hệ có chủ có một dòng chạy qua đủ ba tầng; có **bằng chứng đỏ thật** trước khi code làm nó xanh                                                                                                              | `AuthZMatrix.cs` 23 dòng (24 test); ngay sau commit: 3 dòng đỏ `ArrangePath 404`, `READ-06` + hai `TC-A01-*` xanh (401 anti-enumeration — lệch bảng cũ); link CI run đỏ đã lưu; sau `D3`: 24/24                                                                                                                          |
| **B3** | Test quan hệ `FRD-01..10`, `FOL-01..04` + bảng đột biến                                               | BR-03, FR-010/011/012 và race A↔B đúng qua HTTP trên Postgres thật; mỗi luật quan trọng **đã từng làm test đỏ** — thứ thay chỗ người review chéo (REV-01)                                                                               | Hai lớp test xanh, mười bốn ca mang mã Mục 10.1; `FRD-06` mười cặp song song, 0 lần 500; bảng đột biến Mục 17.4 tick đủ (nửa quan hệ ở bước 4, nửa feed ở bước 5)                                                                                                          |
| **B4** | Test feed `FEED-01..13`, `FEED-07b`, `FEED-09b`, `PAGE-04` + đếm truy vấn `FEED-Q1`                                | Ma trận quyền của feed, feed gợi ý, luật cache, degrade/503 thành cổng; lưới N+1 ở đúng endpoint trọng điểm hiệu năng                                                                                                                   | Ba lớp test xanh; mọi ca cache **khẳng định đã trúng cache** trước khi khẳng định nội dung; giá trị thô `feed:p1:*` không chứa URL, `canEdit`; `FEED-Q1`: số lệnh ở 50 nguồn = 200 nguồn = hằng số viết tay theo Mục 7.2                                                      |
| **B5** | Cổng hợp đồng `socialgraph-v1` + thử cho đỏ                                                           | Hợp đồng và controller không lệch được mà CI im lặng — cổng `API contract` hiện chỉ canh ba module                                                                                                                                      | Dòng `Content Include` trong csproj test; `SocialGraphContractTests` có trong `--list-tests --filter Category=Contract`; ba kiểu thử đỏ đã làm và khôi phục; codegen FE không sửa gì mà worktree sạch                                                                        |

### 0.2 Khối C — Feed và hiệu năng

| Mã     | Đầu việc                                                                                 | Mục tiêu                                                                                                                                                                                                  | Kết quả mong đợi                                                                                                                                                                                                                                                         |
| ------ | ---------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| **C1** | Cache nguồn feed trong `FeedSourceReader` + `IFeedSourceCache.InvalidateAsync`            | Bỏ 2 câu SQL nguồn khỏi phần lớn request feed (Đ-4.8), và để **module chủ dữ liệu** là nơi duy nhất xóa cache đúng lúc (Đ-4.4)                                                                               | Khóa `sg:feed-sources:{id}` TTL 60s; Redis chết → đọc DB + log cảnh báo, không lỗi; `InvalidateAsync(a, b)` xóa **cả hai** khóa; công tắc `Feed:SourceCache:Enabled`; `FeedSourceReaderTests` của khối A vẫn xanh; `FeedSourceCacheTests` trên Redis thật xanh              |
| **C2** | `IFeedStore`: truy vấn LATERAL + feed gợi ý; Đ-4.11 ở `ListByAuthorAsync`                  | Chi phí mỗi trang bị chặn bởi `số nguồn × (limit+1)`, không bởi tổng số bài (Đ-4.7); BR-02 và BR-07 nằm **trong** truy vấn                                                                                  | SQL tham số hóa bằng `FromSql`, không nối chuỗi; `EXPLAIN (ANALYZE, BUFFERS)` trên bộ dữ liệu tải: `Nested Loop` + `Index Scan using idx_posts_author_created`, **không** `Sort` toàn bộ bài — dán vào "Thực tế thi công"; `EXPLAIN` trước/sau Đ-4.11 trong thân commit           |
| **C3** | Hàm hydrate dùng chung `PostHydrator` + kiểm lại BR-02 trong bộ nhớ                        | **Một** đường dựng `PostResponse` cho mọi danh sách (chỗ GĐ3 cắm `myReaction`); cache không bao giờ là nguồn sự thật cuối (Đ-4.9)                                                                           | `GET /users/{id}/posts` đi qua `PostHydrator`, test GĐ2 xanh không sửa khẳng định; `FeedVisibility.CanSee` hàm thuần có unit test + test đối chiếu với `WHERE` của LATERAL; chữ ký `PostHydrator` ghi vào Mục 18 cho GĐ3                                                  |
| **C4** | `FeedService` + cache trang đầu + degrade + 503                                           | Ráp SEQ-03 (Mục 7.2) đúng thứ tự; trang đầu nhẹ khi trúng cache; DB quá tải thành 503 "thử lại", không thành 500                                                                                             | Khóa `feed:p1:{id}` TTL 30s chỉ chứa id + `mode` + `nextCursor` + dấu nguồn (Q-C1); tác giả đăng/sửa/xóa → khóa của tác giả bị xóa; `CommandTimeout` 5s **chỉ** cho truy vấn feed; công tắc `Feed:PageCache:Enabled`                                                          |
| **C5** | Môi trường đo + bộ dữ liệu tải                                                           | Có dữ liệu đủ lớn để `EXPLAIN` của `C2` nói thật **ngay lúc viết**, và để k6 đo trên máy có giới hạn bằng VPS (Đ-4.13)                                                                                       | `tests/load/feed/`: `docker-compose.perf.yml` (project `perf`, `cpus: 2`), `seed.sql` có **hai** chốt chặn, `README.md`; seed xong: 10.000 hồ sơ, ~1.000.000 bài, phân bố như Đ-4.13; khóa JWT đo **không** nằm trong repo                                                       |
| **C6** | Kịch bản k6 + ba lượt + báo cáo sơ bộ                                                   | GOAL-01 có con số trước GĐ8, còn biên để sửa                                                                                                                                                                 | `tests/load/feed/feed.js`; `docs/giai-doan-4/bao-cao-k6-so-bo.md` đủ mục của Mục 12 gốc; lượt (2) lạnh là con số kết luận — đạt, **hoặc** không đạt kèm `EXPLAIN` và việc chuyển GĐ8                                                                                        |

### 0.3 Khối D — Endpoint

| Mã     | Đầu việc                                                        | Mục tiêu                                                                                                             | Kết quả mong đợi                                                                                                                                                                                                  |
| ------ | --------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **D0** | Nền chung SocialGraph                                           | Mọi thứ `D1`–`D6` dùng chung ở **một** chỗ: nhóm Swagger, mã quyền, lỗi, DTO, store, service, event                    | `SocialGraphApiGroup`, `SocialGraphPermissions` + `SocialGraphPermissionsTests` (đã thử đỏ), `SocialGraphErrors`, `RelationshipResponse`; `Program.cs` có `AddApplicationPart` + dòng `apiGroups`; `swagger/socialgraph-v1/swagger.json` trả 200 |
| **D1** | `GET /relationships/{userId}`                                    | Một endpoint cho nút trên trang hồ sơ — thứ FE cần sớm nhất                                                            | Bốn trạng thái `friendship` × `following` đúng; chính mình → 400 `errors.userId`; người không tồn tại → 200 `none`/`false` (không 404 — không dò tài khoản)                                                     |
| **D2** | `POST /friends/requests`                                        | FR-010: tạo lời mời `pending` theo đúng thứ tự kiểm của Đ-4.14                                                        | 201 `outgoing`; tự gửi 400 **trước** DB; không hồ sơ 404; `23505` → 409; cache + event **sau** `COMMIT`; `TC-A01-friends` xanh                                                                                   |
| **D3** | `POST /friends/requests/{userId}/accept`                        | FR-011: chấp nhận bằng **một** câu `UPDATE` có điều kiện, không cửa sổ race                                            | 200 `friends`; 0 dòng → 403 một phản hồi cho mọi lý do; `accepted_at` + `updated_at` gán cùng câu; bốn dòng matrix còn đỏ chuyển xanh                                                                            |
| **D4** | `DELETE /friends/requests/{userId}` + `DELETE /friends/{userId}` | Hủy / từ chối / hủy kết bạn idempotent                                                                                 | 204 kể cả khi không có gì; xóa cache chỉ khi có dòng bị xóa; `FRD-07..09` xanh                                                                                                                                   |
| **D5** | `GET /friends` + `GET /friends/requests?direction=`             | Danh sách của chính người gọi, keyset, một lô `IUserDirectory` mỗi trang                                                | Keyset theo `accepted_at` / `created_at` + id người kia; `direction` lạ → 400 `errors.direction`; thẻ mất hồ sơ vắng mặt mà `nextCursor` vẫn đúng                                                                 |
| **D6** | `PUT` + `DELETE /follows/{userId}`                              | FR-012 idempotent                                                                                                    | `ON CONFLICT DO NOTHING`; tự theo dõi 400 trước DB; không hồ sơ 404; chỉ xóa cache của **người theo dõi**; `FOL-*` xanh                                                                                           |
| **D7** | `GET /feed` + phần `/feed` của `content-v1.yaml` + rà RFC 7807 hai nhóm | Feed thành endpoint thật; hợp đồng `content-v1` mở lại theo luật chỉ-thêm                                               | Controller mỏng gọi `FeedService`; yaml đúng Mục 8.2, `info.version` → `1.0.0-gd4`, `schema.d.ts` sinh lại **cùng commit**; 503 có `Retry-After: 5`; bảng rà mã lỗi hai nhóm trong thân commit; `TC-A01-feed` xanh |

### 0.4 Thứ tự thực thi

```
 BƯỚC 2 (chung với khối A — A đã xong)
   B1 ─→ B2 (5 dòng đỏ có chủ đích, push, chờ CI đỏ)

 BƯỚC 3
   C5 (môi trường đo + seed 1M bài) ─── phải xong trước khi C2 viết được EXPLAIN

 BƯỚC 4
   D0 ─→ C1 ─→ D1 ─→ D2 ─→ D3 ─→ D4 ─→ D5 ─→ D6 ─→ B3 ─→ B5
          │           ▲FRD-01..06 viết trước, đỏ      │
          │                 ▲FRD-05,10 · READ-06b xanh
          └─ D2–D6 gọi InvalidateAsync thật từ đầu

 BƯỚC 5
   C2 (+EXPLAIN trên dữ liệu tải) ─→ C3 ─→ C4 ─→ D7 ─→ B4 (+ nửa feed của bảng đột biến)
    ▲FEED-01..08 viết trước, đỏ          ▲FEED-09..13, Q1 viết trước
                                                   → matrix giữ 24/24

 BƯỚC 6
   C6 — ba lượt k6; nửa ngày sau để sửa theo EXPLAIN
```

- **`C1` trước `D1`** (lệch Mục 9.3, đã chốt ở đó): `D2`–`D6` gọi `InvalidateAsync` ngay khi được viết. Đăng ký một bản
  rỗng rồi thay ở bước 5 là đúng loại "đăng ký tạm" đã sinh ra bẫy `AlwaysStrangers`.
- **Viết test trước, code sau, commit test sau cùng**: viết `FRD-01` → chạy thấy đỏ (404, route chưa có) → làm `D2` →
  xanh. Test hình dạng của endpoint đi trong commit `D*`; bộ nghiệm thu `FRD/FOL/FEED` đi trong commit `B*` riêng (nếp
  `Q-B3` GĐ2 — Mục 8 "Ranh giới B3 / D").
- **`C2` cần `C5`**: `EXPLAIN` trên DB test vài chục dòng không nói gì — Postgres chọn `Seq Scan` cho bảng nhỏ bất kể index.
- **`D7` sau `C4`**, không trước: controller không có gì để gọi. Và yaml `/feed` vào **đúng commit `D7`** — sớm hơn là cổng
  `API contract` đỏ (lệch Mục 9.2 gốc).

### 0.5 Mỗi mốc mở khóa việc gì

| Xong      | Mở khóa                                                                                                     |
| --------- | ----------------------------------------------------------------------------------------------------------- |
| `B1`      | `C1`, `C4` tự kiểm trên Redis thật trong lúc viết; `B4` viết được ca cache                                  |
| `B2`      | Mọi `D*` có đích để làm xanh — "xong `D3`" nghĩa là bốn dòng matrix đổi màu, không phải cảm giác            |
| `C5`      | `EXPLAIN` của `C2`; `C6`                                                                                    |
| `D0`      | Mọi controller SocialGraph hiện trên Swagger; `B5` chạy được                                                |
| `C1`      | `D2`–`D6` gọi xóa cache thật                                                                                |
| `D3`      | Helper `MakeFriendsAsync` dùng được → toàn bộ `FEED-*` dựng cảnh "là bạn" qua API                            |
| `D6` + `B5` | Mở PR được nếu cần; khối E dựng trên hợp đồng CI đã chứng nhận                                            |
| `C3`      | GĐ3 biết chỗ cắm `myReaction`                                                                               |
| `D7` + `B4` | `C6` đo tốc độ của một feed **đã đúng** — đo feed sai là đo vô ích                                        |

### 0.6 Cột mốc tiến độ (STAFF-01)

| Hết      | Phải thấy                                                                               | Không thấy thì                                                  |
| -------- | --------------------------------------------------------------------------------------- | -------------------------------------------------------------- |
| Bước 2   | `B1` xanh; link CI run đỏ của `B2`                                                      | Khối A đang ăn sang bước 3 — ghi lịch ngay                     |
| Bước 3   | `seed.sql` chạy xong, `SELECT count(*) FROM content.posts` ≈ 1.000.000                   | `C2` sẽ viết mù — dừng, đừng qua bước 5 khi chưa có dữ liệu    |
| Bước 4   | `FRD/FOL` xanh; matrix 24/24 (từ `D3`); `B5` đã thử đỏ                                  | Cắt theo 0.7 ngay, không đợi                                  |
| Bước 5   | `EXPLAIN` đúng hình dạng Đ-4.7; `FEED-*`, `FEED-Q1` xanh; matrix giữ 24/24              | Đã tiêu ~4,5/8 ngày (STAFF-01) — cắt ngay                      |
| Bước 6   | Báo cáo k6 có số, kể cả khi không đạt                                                   | Không có báo cáo = GĐ4 chưa xong, bất kể con số (B.11)          |

### 0.7 Phần cắt được nếu trễ

Theo B.9 gốc, từ trên xuống, dừng khi kịp:

| Thứ tự | Cắt gì                                                                                     | Còn lại vẫn đạt                                                         |
| ------ | ------------------------------------------------------------------------------------------ | ---------------------------------------------------------------------- |
| 1      | Dòng đột biến "xóa cache nguồn **trước** `COMMIT`"                                          | B.4 đã cho phép "không tái hiện ổn định"; B.9 tự rà mục 2 canh thay     |
| 2      | Ca 200 nguồn của `FEED-Q1` (giữ ca 50 nguồn + hằng số)                                      | Hằng số viết tay vẫn bắt N+1                                            |
| 3      | Cache trang đầu (`C4` phần cache) — **chỉ khi** k6 lượt lạnh đã đạt; kéo theo `FEED-10`, `FEED-13`, Q-C1 | SEQ-03 còn cache nguồn; p95 đã chứng minh không cần nó      |
| **Không cắt** | `D0`–`D7` · `C1`–`C3` · `C4` phần timeout/503 · `C5`, `C6` · `B2`, `B3`, `B5` · `FEED-01..09b` (gồm `FEED-07b`), `FEED-Q1` 50 nguồn | "Lõi" + "Bắt buộc phi chức năng" của B.9 |

### 0.8 Mười lăm chỗ lệch B.4–B.6 và Mục 7.2 — đề xuất, chốt khi thi công

Phát hiện lúc đối chiếu B.4–B.6 và Mục 10 với code thật ngày 2026-09-22. Mỗi cái ghi ngược vào `giai-doan-4.md` **trong
commit của đầu việc tương ứng**, mở bằng "Lệch B.4/B.5/B.6 (nhóm chốt): …".

**Đã ghi ngược sẵn** trong commit chốt Q-B4/Q-C1/Q-C2 (2026-09-22), vì chúng sửa nguyên tắc chứ không chỉ cách làm: L2, L3, L5,
L9, L11, L15 (bảng Mục 10.2), L10 (Đ-4.8), L14 (Mục 7.2). Commit của đầu việc tương ứng chỉ nhắc lại một dòng, không ghi lần hai.

| #   | Tài liệu gốc viết                                                                    | Làm thế này                                                                                                                                                                                             | Vì sao                                                                                                                                                                                                                                                 |
| --- | ------------------------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| L1  | `B1`: migrate `socialgraph` trong harness + helper dựng cảnh                          | Migrate đã làm ở `A3`. `B1` còn: Redis, URL ký khác nhau, đếm lệnh, đo giờ. Helper dựng cảnh vào `ModulesTestClient` **cùng commit `D2`/`D3`/`D6`**                                                     | Luật đầu `ModulesTestClient`: phương thức gọi endpoint đi cùng commit endpoint đó. Còn Redis: `ModulesApiFactory` trỏ cổng 1 — test cache không bao giờ chạm cache                                                                                       |
| L2  | `FEED-10`: đồng hồ giả `TimeProvider` đi 16 phút → URL ký mới                         | `FakeObjectStorage.DistinctGetUrls` — mỗi lần ký một URL khác (`?sig=<n>`). `FEED-10` khẳng định lần 2 trúng cache **và** URL khác lần 1                                                                  | `R2ObjectStorage` tính hạn bằng `DateTime.UtcNow`, fake trả URL **hằng**. Đồng hồ giả không đổi gì — `FEED-10` xanh cả khi cache lưu `PostResponse`                                                                                                  |
| L3  | `FEED-12`: giả chậm bằng `pg_sleep`                                                  | Kết nối riêng `LOCK TABLE content.posts IN ACCESS EXCLUSIVE MODE` → truy vấn feed chờ khóa quá 5s                                                                                                       | Không có chỗ chèn `pg_sleep` vào truy vấn của app mà không thêm hook vào code sản phẩm                                                                                                                                                              |
| L4  | `FEED-Q1`: `DbCommandInterceptor` của test; dựng 50/200 nguồn                         | Đếm bằng `ActivityListener` nguồn `"Npgsql"` lọc theo database; quan hệ của các nguồn INSERT bằng SQL                                                                                                    | `Add*Module` tự gọi `AddDbContext`, EF 8 không có `ConfigureDbContext` để test gắn interceptor. Rate limit **100 req/phút theo user**: A gửi 200 lời mời là 429 từ lời mời thứ 101                                                                    |
| L5  | `FEED-13`: `canEdit`/`myReaction` đúng người                                         | Giữ, **thêm** khẳng định giá trị thô `feed:p1:{id}` không chứa `PostResponse`                                                                                                                           | Khóa theo người xem: cache lưu `PostResponse` của chính người đó thì `canEdit` vẫn đúng với người đó — `FEED-13` xanh với đúng đột biến bảng `B3` ghi nó phải bắt                                                                                     |
| L6  | `B5` gồm Mục 10.5 điểm 1–5                                                           | `B5` = điểm 1, 2 + thử đỏ. Điểm 3 là `B2`; điểm 5 đã xong ở `A1`; điểm 4 (`SocialGraphPermissionsTests`) viết **trong `D0`**, `B5` chỉ thử đỏ                                                            | Nếp `Q-D5` GĐ2: test canh hằng mã quyền ra đời cùng hằng. Để tới `B5` là ba bước `D2`–`D6` gõ mã không ai canh, mà Admin vẫn qua                                                                                                                     |
| L7  | Bảng đột biến `B3` ở bước 4                                                          | Tách: nửa quan hệ bước 4, nửa feed bước 5                                                                                                                                                               | Năm dòng cần `FEED-*`                                                                                                                                                                                                                                  |
| L8  | "Sáu dòng matrix đỏ trước"                                                           | **Năm** dòng đỏ; `READ-06` xanh ngay, bằng chứng đỏ đến từ đột biến                                                                                                                                      | `A5` đã bật BR-02 thật                                                                                                                                                                                                                                 |
| L9  | `FEED-12`: "mã `feed.unavailable`"                                                   | Test khẳng định 503 + `Retry-After` + `title` viết tay + `traceId`. `feed.unavailable` là `Error.Code` nội bộ, **không** lên dây                                                                          | `ResultHttpExtensions` cố ý không đưa `Error.Code` vào Problem Details — hợp đồng chốt `{type,title,status,errors,traceId}`                                                                                                                         |
| L10 | Cache trang đầu: "danh sách `post_id` (≤ 21) + `mode`"                                | Thêm `nextCursor` đã mã hóa và **dấu nguồn** (Q-C1)                                                                                                                                                      | `nextCursor` tính từ bài thứ `limit` trong danh sách **gốc** — bài đó bị xóa trong 30s thì lúc trúng cache không còn gì để dựng cursor. Dấu nguồn: xem Q-C1                                                                                          |
| L11 | `FEED-09` là lưới cho "bỏ kiểm lại BR-02 ở hydrate"                                  | Thêm `FEED-09b`: tác giả đổi bài `public` → `friends` khi trang đầu của **người chỉ theo dõi** đang cache                                                                                               | Với Q-C1, hủy kết bạn làm đổi dấu nguồn → trượt cache → `FEED-09` không còn đi qua bước kiểm lại. Đổi `privacy` bằng `PATCH` không đổi nguồn, không xóa cache người xem — chỉ bước kiểm lại chặn được                                                   |
| L12 | `C1`: cache trong `FeedSourceReader`                                                  | `FeedSourceReaderTests` (khối A) thêm `.AddSharedKernelRedis(ApiFactory.UnreachableRedis)` vào `ServiceCollection` trần                                                                                   | `FeedSourceReader` giờ cần `RedisConnection` — resolve từ `ServiceCollection` trần không có nó là ném. Redis cổng 1 = đường fail-open, đúng thứ test đó đang kiểm                                                                                     |
| L13 | `D5`: cursor "cùng bộ mã hóa keyset của GĐ2"                                          | **Chép** `PostCursor` thành `FriendCursor` trong `SocialGraph/Application/`                                                                                                                              | `PostCursor` là của Content; `ModuleBoundaryTests` chặn import chéo. Cùng lập luận L5 khối A (`LowercaseEnum`)                                                                                                                                       |
| L14 | Mục 7.2: trượt cache vẫn "nạp bài theo PK (1)"                                         | Trượt cache thì `FeedService` dùng luôn các dòng truy vấn LATERAL vừa trả; chỉ đường **trúng** cache mới nạp theo PK. `FEED-Q1` = **5** câu (nguồn 2 + feed 1 + ảnh 1 + tác giả 1)                      | LATERAL đã trả nguyên dòng `posts` (`SELECT p.*`) — nạp lại theo PK ngay sau đó là một câu thừa trên đường nóng nhất. Sửa Mục 7.2 cùng commit `C4`                                                                                                   |
| L15 | Không có ca nào canh Q-C1                                                             | Thêm `FEED-07b`: người mới đọc feed (gợi ý, được cache) → có kết nối đầu tiên → đọc lại **trong 30s** → `mode = "network"`                                                                               | Bỏ so dấu nguồn thì `FEED-09` vẫn xanh (bước kiểm lại BR-02 lọc hộ). Chỉ ca "gợi ý → mạng lưới" mới thấy trang cũ                                                                                                                                  |

### 0.9 Bốn quyết định — đã chốt cả bốn (2026-09-22)

#### Q-B4 — `FEED-12` chờ đủ 5 giây, hay thêm cấu hình thời hạn? ✅ **chốt 2026-09-22 theo đề xuất, đã ghi vào Đ-4.10**

**Đề xuất: chờ đủ 5 giây**, không thêm khóa cấu hình. Một ca 5 giây là rẻ; thêm `Feed:QueryTimeoutSeconds` chỉ để test
nhanh hơn là thêm một thứ cấu hình sai được trên server mà không ai cần. Hằng 5 giây ở **một** chỗ trong `C4`; test ghi số
5 bằng tay.

#### Q-B5 — `B5` dời lên cuối bước 4 ✅ **chốt 2026-09-22, đã ghi vào Mục 9.3**

#### Q-C1 — Cache trang đầu có kèm "dấu nguồn" không? ✅ **chốt 2026-09-22 theo đề xuất (cách a), đã ghi vào Đ-4.8, Mục 7.2, 7.3, 10.2**

**Vấn đề.** Đ-4.8 chỉ xóa `feed:p1:{id}` khi **chính người đó** đăng/sửa/xóa bài. Đi đúng lát cắt dọc của F2: A mới tinh
mở trang chủ (feed gợi ý, được cache 30s) → kết bạn với B → B chấp nhận → A quay lại trang chủ trong 30s → **vẫn feed gợi
ý**, không thấy bài của B. E2E hai tài khoản sẽ đỏ ngẫu nhiên theo tốc độ người bấm, và người dùng thật thấy "kết bạn
xong không có gì xảy ra".

**Đề xuất.** Giá trị cache mang thêm `sourcesFingerprint` — băm ngắn của hai tập `Friends` + `FollowingOnly` đã sắp xếp. Mục
7.2 đọc nguồn (bước 2) **trước** khi thử cache (bước 4), nên lúc thử cache đã có nguồn hiện tại trong tay: dấu khác → coi
như trượt. Không thêm truy vấn nào, không để SocialGraph biết khóa của Content (không xóa chéo module). Hủy kết bạn, kết
bạn, theo dõi đều có hiệu lực ngay trên trang chủ. Cái giá: bước kiểm lại BR-02 ở hydrate không còn được `FEED-09` canh —
bù bằng `FEED-09b` (L11). Chốt thì ghi vào Đ-4.8 thành một dòng "sửa ngày …".

#### Q-C2 — Công tắc cache đọc cấu hình thế nào mà `ServiceCollection` trần vẫn dựng được? ✅ **chốt 2026-09-22 theo đề xuất, đã ghi vào Đ-4.8**

**Vấn đề.** `MediaCleanupOptions` dùng `BindConfiguration` — chạy được vì test trần không bao giờ resolve worker. Nhưng
`FeedSourceReaderTests` **có** resolve `IFeedSourceReader`, và `IOptions<…>` bind bằng `BindConfiguration` sẽ đòi
`IConfiguration` không có ở đó.

**Đề xuất.** Bind có điều kiện:

```csharp
services.AddOptions<FeedSourceCacheOptions>()
    .Configure<IServiceProvider>((o, sp) =>
        sp.GetService<IConfiguration>()?.GetSection(FeedSourceCacheOptions.Section).Bind(o));
```

Có host → đọc `Feed:SourceCache:Enabled`; không host → mặc định `Enabled = true`. Cùng khuôn cho `FeedPageCacheOptions` ở
Content.

---

## 1. Trước khi gõ dòng đầu tiên

### 1.1 Điều kiện cần

```bash
# 1. Docker daemon chạy được — Testcontainers dựng Postgres 16 và Redis 7
docker ps

# 2. Postgres + Redis dev chạy (dotnet run, dotnet ef cần)
docker compose -f deploy/docker-compose.dev.yml up -d postgres redis

# 3. Solution build sạch, toàn bộ test XANH — mốc so sánh.
#    Đỏ nền đã biết trên máy dev: 1 test Startup R2 do user-secrets có khóa R2 (CI xanh) — ghi nhận, không sửa.
dotnet build SocialApp.sln && dotnet test SocialApp.sln

# 4. Đếm test theo nhóm — số này vào dòng `Test:` của mọi commit
dotnet test tests/SocialApp.IntegrationTests/SocialApp.IntegrationTests.csproj --list-tests --filter "Category=AuthZ"
dotnet test tests/SocialApp.IntegrationTests/SocialApp.IntegrationTests.csproj --list-tests --filter "Category=Contract"

# 5. GHI thời gian bộ integration TRƯỚC khi đụng vào (B1 cần). Chạy hai lần, lấy lần hai.
dotnet test tests/SocialApp.IntegrationTests/SocialApp.IntegrationTests.csproj --filter "Category!=AuthZ&Category!=Contract"

# 6. Máy chạy k6 + môi trường đo đã chốt ở cổng mở (Mục 9.2 bước 3). Chưa chốt thì chốt NGAY — C5 là bước 3.
k6 version
```

### 1.2 Impact analysis trước khi sửa symbol có sẵn

Luật `CLAUDE.md`. Ba khối sửa những chỗ có sẵn sau — mọi thứ khác là file mới:

| Symbol                                          | Sửa ở     | Người gọi đã biết (kiểm lại bằng `impact`)                                                             |
| ----------------------------------------------- | --------- | ------------------------------------------------------------------------------------------------------ |
| `ModulesApiFactory.ConfigureWebHost`            | `B1`      | Mọi lớp test khối D GĐ2 — **rộng**, giữ mặc định y nguyên                                               |
| `FakeObjectStorage.CreatePresignedGet`          | `B1`      | `AvatarTests:142`, `UpsertProfileTests:151` so **nguyên chuỗi** — lý do cờ tắt mặc định                 |
| `AuthZMatrix.Cases`                             | `B2`      | `AuthZMatrixTests` qua `MemberData` — chỉ thêm phần tử                                                  |
| `Program.cs` (`AddApplicationPart`, `apiGroups`) | `D0`      | Host; Swagger; `PresentationBoundaryTests`                                                             |
| `AddSocialGraphModule`                          | `D0`, `C1` | `Program.cs`, `PostgresFixture`, `ModulesApiFactory`, `FriendshipReaderTests`, `FeedSourceReaderTests`  |
| `FeedSourceReader`                              | `C1`      | Chỉ DI — `risk` sẽ ra `UNKNOWN`; xác nhận bằng `grep IFeedSourceReader`                                  |
| `PostStore.ListByAuthorAsync`                   | `C2`      | `PostReadService.ListByUserAsync`                                                                      |
| `PostReadService.ListByUserAsync` / `GetAsync`  | `C3`      | `PostsController`                                                                                      |
| `PostResponseMapper.ToResponses`                | `C3`      | `PostReadService` — sau `C3` chỉ còn `PostHydrator` gọi                                                 |
| `PostService.CreateAsync/UpdateAsync/DeleteAsync` | `C4`    | `PostsController`                                                                                      |
| `AddContentModule`                              | `C2`–`C4` | `Program.cs`, `PostgresFixture`, `ModulesApiFactory`, schema test                                       |
| `content-v1.yaml` (không symbol)                | `D7`      | `ContentContractTests`, `pnpm gen:api` → `lib/api/content/schema.d.ts`                                  |

```bash
node .gitnexus/run.cjs impact "ListByAuthorAsync" --direction upstream --repo .
node .gitnexus/run.cjs impact "PostReadService" --direction upstream --repo .
node .gitnexus/run.cjs impact "PostResponseMapper" --direction upstream --repo .
```

`risk: UNKNOWN` **không** phải `LOW`: DI, reflection của xUnit, `ApplyConfigurationsFromAssembly` đều làm đồ thị mất dấu.
Xác nhận bằng `grep` rồi mới sửa. `HIGH`/`CRITICAL` thì dừng và ghi vào "Thực tế thi công" trước khi sửa.

### 1.3 Mười luật áp thẳng vào ba khối

1. **`actorId` luôn từ `User.GetUserId()`**, không từ route/body. Cặp quan hệ luôn là `(actorId, userId của route)` —
   không endpoint nào nhận hai id người dùng (Mục 6.2). Test không phân biệt được — B.9 tự rà mục 1.
2. **Xóa cache và phát event SAU `COMMIT`** (Đ-4.15). Xóa trước là một request đọc chen vào nạp lại dữ liệu cũ, sống thêm
   60s. B.9 tự rà mục 2.
3. **SQL tham số hóa**, không nối chuỗi mảng id (B.9 mục 3). `FromSql` / `ExecuteSql` với chuỗi nội suy **là** tham số hóa;
   `FromSqlRaw` với `$"…"` thì **không** — đừng nhầm hai hàm.
4. **Không `StringSet` nào của feed chứa `PostResponse` hay URL đã ký** (Đ-4.9, B.9 mục 4).
5. **Khóa ký JWT của môi trường đo không nằm trong repo, không nằm trong `deploy/.env`** (B.9 mục 5).
6. **Module không import module khác** (`ModuleBoundaryTests`). SocialGraph ↔ Content chỉ gặp nhau ở `SharedKernel/Contracts`.
   SocialGraph cần URL avatar thì dùng `IObjectStorage` của SharedKernel, không qua Content.
7. **`Application` không chạm EF, Npgsql, StackExchange.Redis** (`PersistenceBoundaryTests` canh EF). Cache và timeout
   dịch thành interface / exception của `Application`, hiện thực ở `Infrastructure`.
8. **Kỳ vọng test viết tay theo tài liệu**, không đọc hằng của code (`PostPrivacy`, `SocialGraphErrors`, hằng timeout).
9. **Dựng cảnh qua API thật** — trừ `FEED-06` (bài `hidden`, GĐ6 mới có endpoint) và `FEED-Q1` (L4), mỗi chỗ có comment vì
   sao.
10. **Không `Skip`, không sửa khung matrix, `git status` sạch trước và sau mỗi lần thử đỏ.**

---

# Phần I — Bước 2: `B1`, `B2`

## 2. B1 — Harness

**Mục tiêu.** Cho test công cụ để thấy cache không chạy, cache lưu URL đã ký, và N+1; có con số giờ để biết khi nào tách
collection.

**Xong khi.** Bốn thay đổi dưới đây có trong harness, mỗi cái một test tự kiểm ở `Harness/`; toàn bộ test cũ xanh không sửa
khẳng định; giờ trước/sau trong thân commit.

### Các bước

**Bước 1 — `ModulesApiFactory.UseRedis`**, chép khuôn `IdentityApiFactory.UseRedis` (dùng bởi `TokenRevocationTests`):

```csharp
private string _redis = ApiFactory.UnreachableRedis;   // mặc định GIỮ NGUYÊN: mọi lớp cũ vẫn chạy không Redis

/// <summary>
/// B1 (GĐ4): Redis THẬT cho test cache feed. Gọi ở InitializeAsync, trước CreateClient đầu tiên — cùng luật với
/// UseFreshDatabaseAsync. Không gọi thì Redis là cổng 1: cache fail-open, và test cache xanh vì lý do sai.
/// </summary>
public void UseRedis(string connectionString) => _redis = connectionString;
```

Trong `ConfigureWebHost`: `builder.UseSetting("ConnectionStrings:Redis", _redis);`. Lớp test dùng nó khai **cả hai**
fixture: `IClassFixture<RedisFixture>, IClassFixture<ModulesApiFactory>`.

**Bước 2 — `FakeObjectStorage.DistinctGetUrls`** (L2):

```csharp
/// <summary>
/// B1 (GĐ4, lệch L2): bật thì mỗi lần ký GET ra một URL KHÁC (`?sig=<n>`) — để FEED-10 phân biệt "hydrate ký lại" với
/// "cache trả URL cũ". Tắt mặc định: AvatarTests và UpsertProfileTests so nguyên chuỗi URL.
/// </summary>
public bool DistinctGetUrls { get; set; }

public int PresignGetCalls => _presignGetCalls;
private int _presignGetCalls;

public string CreatePresignedGet(string key)
{
    var n = Interlocked.Increment(ref _presignGetCalls);
    return DistinctGetUrls ? $"https://fake.invalid/get/{key}?sig={n}" : $"https://fake.invalid/get/{key}";
}
```

Tự kiểm ở `Harness/FakeObjectStorageTests.cs`: tắt → hai lần ký bằng nhau; bật → khác nhau, cùng tiền tố.

**Bước 3 — `Harness/SqlCommandCounter.cs`** (L4):

```csharp
/// <summary>
/// B1 (GĐ4): đếm lệnh SQL APP gửi tới MỘT database, qua ActivitySource "Npgsql" — bắt cả EF, FromSql lẫn Npgsql trần mà
/// không sửa Add*Module (L4). Lọc theo tên database vì các collection khác chạy song song trong cùng tiến trình.
/// </summary>
public sealed class SqlCommandCounter : IDisposable
{
    private readonly ActivityListener _listener;
    private readonly ConcurrentQueue<string> _statements = new();

    public SqlCommandCounter(string connectionString)
    {
        var database = new NpgsqlConnectionStringBuilder(connectionString).Database;
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "Npgsql",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = a =>
            {
                if (Equals(a.GetTagItem("db.name"), database))
                    _statements.Enqueue(a.GetTagItem("db.statement") as string ?? a.DisplayName);
            },
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public IReadOnlyCollection<string> Statements => _statements;
    public void Reset() => _statements.Clear();
    public void Dispose() => _listener.Dispose();
}
```

**Tên nguồn và tên tag phải kiểm trên máy thật trước khi tin** — Npgsql 8 theo quy ước OpenTelemetry cũ (`db.name`,
`db.statement`), nhưng đây đúng là loại chi tiết nhớ sai được. Tự kiểm ở `Harness/SqlCommandCounterTests.cs`: gọi
`GET /users/{id}/posts` một lần → `Statements.Count > 0` **và** có câu chứa `content.posts`. Bộ đếm luôn ra 0 thì `FEED-Q1`
"50 nguồn = 200 nguồn" xanh vĩnh viễn (0 = 0). Tag khác tên thì sửa bộ lọc; nguồn không phát gì thì chuyển sang
`DiagnosticListener` `"Microsoft.EntityFrameworkCore"` (sự kiện `CommandExecuted`) — ghi vào "Thực tế thi công".

**Bước 4 — đo lại giờ** bằng lệnh 5 Mục 1.1, ghi `trước: … s → sau: … s`. Collection `postgres` vượt ~3 phút thì tách
**ngay trong `B1`** (ghi chú đầu `PostgresFixture`) — bước 4, 5 còn thêm ba lớp test quan hệ và ba lớp feed.

### Cạm bẫy đã biết

- **Đổi URL fake cho mọi người** → hai test Profile đỏ ở chỗ không liên quan. Cờ tắt mặc định.
- **Test cache chạy trước khi app kết nối Redis.** `RedisConnection` kết nối **ở nền**, không chờ; request đầu tiên có
  thể fail-open → không ghi cache → ca cache đỏ ngẫu nhiên. Chép `TokenRevocationTests.InitializeAsync`: chờ log
  `"Đã kết nối Redis"` trước ca đầu.
- **Khẳng định "không có khóa `feed:*` nào" trên container dùng chung.** Khóa luôn có `{userId}` mới tinh nên dùng chung
  được, nhưng khẳng định theo **khóa của chính ca đó**.
- **`ActivityListener` không `Dispose`** → đếm hộ test khác suốt lượt chạy. Luôn `using var counter = …`.

---

## 3. B2 — Sáu dòng AuthZ matrix, viết cho đỏ trước

**Mục tiêu.** Mỗi thao tác GĐ4 chạm quan hệ có chủ có một dòng chạy qua đủ ba tầng; có bằng chứng đỏ **trước** khi endpoint
tồn tại.

**Xong khi.** `AuthZMatrix.cs` 23 dòng (24 test); link CI run đỏ đã lưu; sau `D3`: 24/24 (sửa 2026-09-23 — trước
ghi "sau `D3` + `D7`", nhưng `TC-A01-feed` đã xanh 401 từ `B2`).

### Sáu dòng — kỳ vọng chép từ Mục 6.3, không lấy từ output

| Id                          | Người gọi          | Gọi gì                                  | Kỳ vọng | Ngay sau commit `B2`                                        | Xanh khi |
| --------------------------- | ------------------ | --------------------------------------- | ------- | ----------------------------------------------------------- | -------- |
| `TC-A03-friend-accept`      | `User` (C)         | `POST /friends/requests/{A}/accept`     | 403     | Đỏ: `ArrangePath hỏng — POST /friends/requests … 404`       | `D3`     |
| `TC-A03-friend-self-accept` | `User` (A)         | `POST /friends/requests/{B}/accept`     | 403     | Đỏ: như trên                                                | `D3`     |
| `READ-06`                   | `User` (người lạ)  | `GET /posts/{id bài friends của B}`     | 404     | **Xanh** (L8)                                               | đã xanh  |
| `READ-06b`                  | `User` (bạn của B) | `GET /posts/{id bài friends của B}`     | 200     | Đỏ: `ArrangePath hỏng`                                      | `D3`     |
| `TC-A01-feed`               | `Anonymous`        | `GET /feed`                             | 401     | **Xanh** ngay (sửa ngày 2026-09-22, lúc thi công `B2`): route chưa khớp + ẩn danh → 401 anti-enumeration, không 404 | giữ 401 khi `D7` có route |
| `TC-A01-friends`            | `Anonymous`        | `POST /friends/requests` (có body)      | 401     | **Xanh** ngay — cùng lý do với `TC-A01-feed`                | giữ 401 khi `D2` có route |

Bảng này vào thân commit `B2`. Một dòng đỏ **khác lý do** (ví dụ 500) là harness hỏng, không phải "đỏ có chủ đích".
**Sửa ngày 2026-09-22, lúc thi công `B2`:** hai dòng `TC-A01-*` không đỏ được trước endpoint — app cố ý 401 route lạ
(AGENTS.md Mục 9). Bằng chứng đỏ có chủ đích còn ba dòng `ArrangePath 404` + `READ-06` xanh.

### Các bước

**Bước 1 — helper `private static`** cạnh `TaoBaiCuaNguoiKhacAsync`, cùng luật "ném ngay với status + body":
`GuiLoiMoiAsync(client, tu, den)` (201 hoặc ném), `ChapNhanAsync(client, nguoiNhan, nguoiGui)` (200 hoặc ném),
`TaoBaiBanBeCuaBanAsync(a)` (B có hồ sơ + bài `friends`; người gọi có hồ sơ, gửi lời mời; B chấp nhận; trả id bài). Hằng
`PrivacyBanBe = "friends"` cạnh hai hằng privacy có sẵn.

**Bước 2 — sáu dòng**, dưới đầu mục `// --- GĐ4 (B2). Mục 6.3. …`. Hai dòng khó nhất:

```csharp
// US-010 AC-04. C có hồ sơ (dù accept không đòi) để 403 chỉ còn MỘT lý do — nếp TC-A03-media / Q-B2.
new("TC-A03-friend-accept", "C chấp nhận lời mời mà A gửi cho B", "GĐ4",
    Caller.User, HttpMethod.Post, "/api/v1/friends/requests/{A}/accept", HttpStatusCode.Forbidden,
    ArrangePath: async a =>
    {
        var (nguoiGui, nguoiNhan) = (Guid.NewGuid(), Guid.NewGuid());
        await TaoHoSoAsync(a.Client, nguoiGui);
        await TaoHoSoAsync(a.Client, nguoiNhan);
        await TaoHoSoAsync(a.Client, a.CallerUserId);
        await GuiLoiMoiAsync(a.Client, nguoiGui, nguoiNhan);
        return $"/api/v1/friends/requests/{nguoiGui:D}/accept";
    }),

// Dòng hay bị quên nhất (Mục 6.3): UPDATE thiếu vế requester_id = @other vẫn qua mọi happy path.
// Lời mời do CHÍNH người gọi gửi — chỉ làm được nhờ CallerUserId (Q-B2).
new("TC-A03-friend-self-accept", "A tự chấp nhận lời mời chính A gửi cho B", "GĐ4",
    Caller.User, HttpMethod.Post, "/api/v1/friends/requests/{B}/accept", HttpStatusCode.Forbidden,
    ArrangePath: async a =>
    {
        var b = Guid.NewGuid();
        await TaoHoSoAsync(a.Client, a.CallerUserId);
        await TaoHoSoAsync(a.Client, b);
        await GuiLoiMoiAsync(a.Client, a.CallerUserId, b);
        return $"/api/v1/friends/requests/{b:D}/accept";
    }),
```

`TC-A01-friends` mang `Body: new { userId = Guid.NewGuid() }` dù 401 đến trước model binding — nếp `TC-A01-posts`.

**Bước 3 — chạy `--filter "Category=AuthZ"`, đối chiếu từng dòng với bảng**, commit **chỉ** `AuthZMatrix.cs`.

**Bước 4 — chụp bằng chứng đỏ.** Push lên `loveart1210` (CI chạy trên push nhánh này), **chờ run xong** rồi mới push tiếp —
`cancel-in-progress: true` hủy run cũ, mất bằng chứng. Link vào "Thực tế thi công".

### Cạm bẫy đã biết

- **C không có hồ sơ.** `D3` mà thêm kiểm hồ sơ người gọi thì `TC-A03-friend-accept` xanh vì lý do sai — chuyện của
  `TC-A03-media` trước `Q-B2`.
- **`READ-06b` INSERT thẳng `friendships`** → chỉ chứng minh lại `FriendshipReaderTests` và bỏ lọt `D3` ghi sai trạng thái.
- **Đếm sai số test.** 23 dòng / 24 test. Thấy 18 test một lớp là `MemberData` đã gộp dòng.

---

# Phần II — Bước 3: `C5`

## 4. C5 — Môi trường đo + bộ dữ liệu tải (Đ-4.13)

**Mục tiêu.** Dữ liệu đủ lớn để `EXPLAIN` của `C2` nói thật ngay lúc viết, trên một môi trường tách hẳn staging và dev.

**Xong khi.** `docker compose -p perf -f tests/load/feed/docker-compose.perf.yml up -d` lên đủ ba service; `--migrate`
xong bốn schema; `seed.sql` chạy xong; số dòng khớp Bước 4; hai chốt chặn đã thử cho đỏ.

### Các bước

**Bước 1 — `tests/load/feed/docker-compose.perf.yml`.** Chép hình dạng `deploy/docker-compose.dev.yml` (Postgres 16,
Redis 7, `api` build từ `Dockerfile` gốc), khác ở năm chỗ:

| Chỗ                    | Giá trị                                                                         | Vì sao                                                                                                  |
| ---------------------- | ------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------- |
| `name:`                | `socialapp-perf`                                                                | Không đụng volume `pgdata` của dev                                                                      |
| Cổng host              | `15432`, `16379`, `18080`                                                       | Chạy song song với dev được                                                                             |
| `api.cpus` / `mem_limit` | `2` / `12g` — ghi vào báo cáo                                                 | Bằng VPS Ampere A1 2 OCPU / 12GB (Đ-4.13); không giới hạn thì p95 đo trên máy dev 16 nhân nói dối       |
| `Jwt__SigningKey`      | `${PERF_JWT_KEY:?…}` từ `tests/load/feed/.env` (đã `gitignore` bởi mẫu `.env`)  | Khóa riêng của môi trường đo, **không bao giờ** là khóa staging (B.9 mục 5)                             |
| Postgres `DB`          | `socialapp_perf`                                                                | Chốt chặn thứ hai của seed dựa vào tên này                                                              |

Không có Mailpit (đo không gửi mail). `ASPNETCORE_ENVIRONMENT=Development` để khỏi phải khai cấu hình mail/R2/CORS — ghi rõ
trong báo cáo. Mức log đặt `Warning` bằng biến môi trường: log `Information` mỗi request ở 1.000 VU là đo tốc độ ghi log.

**Bước 2 — `tests/load/feed/.env.example`** (chỉ tên biến, giá trị rỗng) + lệnh sinh khóa trong README:
`openssl rand -base64 48`. Khóa sinh lúc dựng, không commit, không chép sang đâu khác.

**Bước 3 — `seed.sql`, hai chốt chặn ở đầu file:**

```sql
DO $$
BEGIN
    IF current_database() <> 'socialapp_perf' THEN
        RAISE EXCEPTION 'seed.sql chỉ chạy trên socialapp_perf, đang ở %', current_database();
    END IF;
    IF EXISTS (SELECT 1 FROM identity.users) THEN
        RAISE EXCEPTION 'identity.users đã có dữ liệu — đây không phải DB đo rỗng. Dừng.';
    END IF;
END $$;
```

Chạy bằng `psql -v ON_ERROR_STOP=1` — thiếu cờ đó thì `psql` in lỗi rồi **chạy tiếp** phần còn lại.

**Bước 4 — nội dung seed** (Đ-4.13), bằng `generate_series`, trong một transaction, cuối file `ANALYZE`:

| Bảng                     | Số lượng                                                                               | Ghi chú                                                                                                               |
| ------------------------ | -------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------- |
| `profile.profiles`       | 10.000                                                                                  | **Cột lấy từ migration `InitialProfile`, không đoán.** Không cần `identity.users`: token ký tay, không FK (Đ-2.2)      |
| `socialgraph.friendships` | trung bình 100 bạn/người, 5% có 500                                                    | Cặp chuẩn hóa bằng `LEAST`/`GREATEST` của Postgres — khỏi dính GUID-01; `ON CONFLICT DO NOTHING`; `accepted`, `accepted_at` có giá trị (CHECK `ck_friendships_accepted`) |
| `socialgraph.follows`    | trung bình 20/người                                                                     | Loại tự theo dõi (`ck_follows_not_self`)                                                                               |
| `content.posts`          | 1.000.000 trải 180 ngày; privacy 70/20/10; 1% `hidden`                                   | `created_at` ngẫu nhiên trong 180 ngày; `post_id` sinh bằng `gen_random_uuid()` là chấp nhận được (keyset theo cặp `(created_at, post_id)`) |

Id người dùng lưu vào một bảng tạm `perf_users(n int, user_id uuid)` để các bước sau và kịch bản k6 (`C6`) cùng dùng —
xuất ra `tests/load/feed/users.csv` (**gitignore**, không commit: 10.000 dòng là rác repo).

**Bước 5 — README.md**: lệnh dựng, lệnh `--migrate` (chạy image `api` với `--migrate`), lệnh seed, lệnh **xóa**
(`docker compose -p perf … down -v`), thời gian seed đo được, bản Postgres/k6.

### Cạm bẫy đã biết

- **Chạy seed khi `--migrate` chưa xong** → `relation does not exist` giữa chừng, transaction rollback — may mắn. Không
  bọc transaction thì còn nửa dữ liệu.
- **Quên `ANALYZE`** → planner dùng thống kê bảng rỗng, `EXPLAIN` của `C2` chọn kế hoạch sai và ta sửa truy vấn đúng.
- **Trỏ nhầm `ConnectionStrings__Postgres` về cổng 5432 của dev.** Chốt chặn 1 bắt được — đó là lý do có nó.

---

# Phần III — Bước 4: `D0`, `C1`, `D1`–`D6`, `B3`, `B5`

## 5. D0 — Nền chung của SocialGraph

**Mục tiêu.** Mọi thứ `D1`–`D6` dùng chung nằm ở một chỗ, đặt đúng tầng.

**Xong khi.** `GET /swagger/socialgraph-v1/swagger.json` trả 200 (rỗng path cũng được); `SocialGraphPermissionsTests` xanh
và đã thử đỏ; `PresentationBoundaryTests`, `ModuleBoundaryTests` xanh.

### Các bước

| File                                                           | Nội dung                                                                                                                                                                   |
| -------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `Presentation/SocialGraphApiGroup.cs`                          | `Name = "socialgraph-v1"`, `Title` — chép `ContentApiGroup`                                                                                                                |
| `Application/SocialGraphPermissions.cs`                        | `FriendRequest = "friend.request"`, `FriendRespond = "friend.respond"`, `All` — chép `ContentPermissions` (Đ-4.12: theo dõi dùng `friend.request`, không mã thứ 18)          |
| `tests/SocialApp.ArchitectureTests/SocialGraphPermissionsTests.cs` | Chép `ContentPermissionsTests` (L6). Thử đỏ: `"friend.reques"` → đỏ → khôi phục                                                                                          |
| `Application/SocialGraphErrors.cs`                             | `SelfRequest` (400 `userId`), `SelfFollow` (400 `userId`), `SelfRelationship` (400 `userId`), `UserNotFound` (404), `RelationshipExists` (409). Câu chép **đúng** `example` của `socialgraph-v1.yaml` |
| `Application/Relationships/RelationshipResponse.cs`            | `record RelationshipResponse(Guid UserId, FriendshipView Friendship, bool Following)` — enum ghi ra chữ thường nhờ converter CamelCase của host                              |
| `Application/Relationships/IRelationshipStore.cs` + `Infrastructure/Persistence/RelationshipStore.cs` | Store của `D1`–`D6` — **lớn dần theo từng `D*`**, không khai trước phương thức chưa có người gọi (nếp `IPostStore`) |
| `Application/Relationships/RelationshipService.cs`             | Scoped; nhận `IRelationshipStore`, `IUserDirectory`, `IFeedSourceCache` (từ `C1`), `SocialGraphEvents`, `TimeProvider`                                                     |
| `Application/SocialGraphEvents.cs`                             | Hai phương thức `FriendRequestSent(a, b)`, `FriendRequestAccepted(a, b)` — GĐ4 **chỉ log** (Đ-4.15); GĐ6 thay thân hàm. Log **không** kèm id người dùng ở mức Information |
| `DependencyInjection/SocialGraphModuleExtensions.cs`           | `AddValidatorsFromAssembly(…, ServiceLifetime.Singleton)`; `TryAddSingleton(TimeProvider.System)`; scoped store/service                                                    |
| `SocialApp.Api/Program.cs`                                     | `.AddApplicationPart(typeof(SocialGraphApiGroup).Assembly)` và dòng `(SocialGraphApiGroup.Name, SocialGraphApiGroup.Title)` trong `apiGroups`                               |

### Cạm bẫy đã biết

- **Controller quên `[ApiExplorerSettings(GroupName = SocialGraphApiGroup.Name)]`** → biến mất khỏi Swagger, không lỗi
  (`Program.cs` ghi rõ). `PresentationBoundaryTests` bắt — chạy nó sau `D1`.
- **Gọi `AddFluentValidationAutoValidation` trong module** → mỗi lỗi hiện hai lần trong `errors`. Host đã gọi.
- **`FriendshipView` ra JSON dạng số** nếu ai đó thêm `[JsonConverter]` riêng hay đổi policy — `B3`/`D1` đọc bằng
  `ModulesTestClient.Json` (CamelCase riêng của test) nên bắt được.

---

## 6. C1 — Cache nguồn feed + `InvalidateAsync` (Đ-4.8, Đ-4.15)

**Mục tiêu.** Hai câu SQL nguồn ra khỏi phần lớn request feed; module chủ dữ liệu là nơi duy nhất xóa cache.

**Xong khi.** `FeedSourceCacheTests` (Redis thật) xanh: trượt → DB → ghi; trúng → không lệnh SQL nào; `InvalidateAsync(a, b)`
xóa cả hai khóa; công tắc tắt → luôn DB; Redis chết → DB + log cảnh báo. `FeedSourceReaderTests` khối A xanh (sau L12).

### Các bước

**Bước 1 — contract xóa cache trong `SocialGraph/Application/`:**

```csharp
/// <summary>
/// Đ-4.4/Đ-4.15: chỉ SocialGraph xóa sg:feed-sources:*. Gọi SAU khi thay đổi quan hệ đã COMMIT, từ RelationshipService —
/// một chỗ, không rải trong controller. Redis lỗi thì nuốt + log: TTL 60s chặn trên độ cũ, còn ném ra là thao tác
/// kết bạn đã COMMIT mà người dùng nhận 500.
/// </summary>
public interface IFeedSourceCache
{
    Task InvalidateAsync(Guid userId, Guid? otherUserId = null, CancellationToken ct = default);
}
```

**Bước 2 — `FeedSourceReader` thêm cache**, cùng lớp (Đ-4.4: cache nằm trong hiện thực ở SocialGraph):

1. Công tắc tắt → đọc DB như `A5`.
2. `RedisConnection.ConnectedOrNull()` — **không bao giờ chờ** (cùng bên đọc thu hồi token). `null` → DB.
3. `StringGetAsync("sg:feed-sources:{userId:D}")` → có → giải JSON `{"f":[…],"fo":[…]}` → trả.
4. Trượt → DB → `StringSetAsync(…, TimeSpan.FromSeconds(60))`.
5. Bắt `RedisException` / `RedisTimeoutException` ở bước 3–4 → log **Warning** (không id người dùng) → trả kết quả DB.

Timeout lệnh Redis của app là 250ms (`AddSharedKernelRedis`) — Redis treo thì feed chậm tối đa chừng đó mỗi lần, không treo.

**Bước 3 — `FeedSourceCache : IFeedSourceCache`** trong `Infrastructure/`, dùng chung hằng khóa với reader (một hàm
`Key(Guid)` `internal static`, **không** gõ chuỗi khóa hai nơi). Đăng ký trong `AddSocialGraphModule`.

**Bước 4 — công tắc** `Feed:SourceCache:Enabled` theo Q-C2. **Bước 5 — sửa `FeedSourceReaderTests`** (L12).

**Bước 6 — `FeedSourceCacheTests`** (integration, `RedisFixture` + `PostgresFixture`, `ServiceCollection` trần có
`AddSharedKernelRedis(redis.ConnectionString)`), dùng `SqlCommandCounter` của `B1` cho vế "trúng thì không SQL".

### Cạm bẫy đã biết

- **Khóa viết hoa/thường lẫn lộn.** `Guid` in `:D` chữ thường; ai dùng `ToString("N")` ở một nơi là hai khóa khác nhau và
  xóa không bao giờ trúng. Một hàm `Key`.
- **Serialize `HashSet<Guid>` thẳng.** `IReadOnlySet` không round-trip qua `System.Text.Json` như mong đợi — ghi mảng, đọc
  mảng, dựng set.
- **Cache kết quả rỗng?** Có — người không kết nối là trường hợp phổ biến (người mới) và đó chính là feed gợi ý. Đừng coi
  giá trị rỗng là "không có cache".

---

## 7. D1–D6 — Bảy endpoint quan hệ

Chung cho cả sáu đầu việc — lặp lại từng lần là nhiễu:

- **Controller** `Presentation/FriendsController.cs`, `FollowsController.cs`, `RelationshipsController.cs`:
  `[Route("api/v1")]`, `[Authorize]`, `[ApiExplorerSettings(GroupName = SocialGraphApiGroup.Name)]`. `[ProducesResponseType]`
  khai **đúng** bộ mã của `socialgraph-v1.yaml` (trừ 429/500 — `ContractTestsBase.CrossCuttingStatusCodes`).
- **Route không ràng buộc `:guid`**, tham số `Guid userId` — id sai dạng thành 400 ở model binding (nếp `PostsController`,
  Mục 1.5 GĐ2). Ràng buộc `:guid` là 404, lệch hợp đồng.
- **Thứ tự kiểm của mọi thao tác ghi** (Đ-4.14, Mục 6.1): tầng 2 (attribute) → "khác mình" (400, **trước** DB) → hồ sơ
  (404, `IUserDirectory`) → câu SQL → `SaveChanges` / `Execute*` → **sau** đó xóa cache → **sau** đó event.
- **`ExecuteUpdateAsync` / `ExecuteDeleteAsync` bỏ qua `SaveChanges`** → override đóng dấu `updated_at` của
  `SocialGraphDbContext` **không chạy**. Câu nào đổi `friendships` bằng `Execute*` phải tự gán `updated_at` từ
  `TimeProvider`.
- **Helper test** trong `ModulesTestClient` đi cùng commit của endpoint (L1): `SendFriendRequestAsync` + `D2`;
  `AcceptAsync` + `MakeFriendsAsync` + `D3`; `DeclineOrCancelAsync`, `UnfriendAsync` + `D4`; `ListFriendsAsync`,
  `ListRequestsAsync` + `D5`; `FollowAsync`, `FollowOkAsync`, `UnfollowAsync` + `D6`; `GetRelationshipAsync` + `D1`.

### D1 — `GET /relationships/{userId}`

Hai lần tra theo khóa (dòng `friendships` của cặp chuẩn hóa, dòng `follows` có hướng) → `RelationshipState` của khối A →
`RelationshipResponse`. Chính mình → 400 `SelfRelationship`. Người không tồn tại → **200 `none`/`false`**, không gọi
`IUserDirectory` (chốt 2026-09-22 ở Mục 8.1). Test hình dạng (commit `D1`): bốn giá trị `friendship` × hai giá trị
`following` dựng bằng SQL trong lớp test **của `D1`** được — đây là test hình dạng, không phải nghiệm thu BR-03.

### D2 — `POST /friends/requests`

`[RequirePermission(SocialGraphPermissions.FriendRequest)]`. Validator: `userId` bắt buộc, khác `Guid.Empty`. Service:
khác mình → hồ sơ → `db.Friendships.Add(Friendship.Request(actor, userId, now))` → `SaveChangesAsync` → bắt
`DbUpdateException { InnerException: PostgresException { SqlState: "23505", ConstraintName: "PK_friendships" } }` → 409.
Bắt **đúng một** constraint (nếp `PostStore.StorageKeyUniqueIndex`) — tên PK kiểm trong migration `InitialSocialGraph`,
không đoán. Sau `COMMIT`: `InvalidateAsync(actor, userId)`, `FriendRequestSent`. 201 + `RelationshipResponse`
(`following` đọc thật — người gửi có thể đã theo dõi từ trước).

**Không `SELECT` kiểm "đã có quan hệ" trước `INSERT`.** Đ-4.14: `INSERT` trần, PK trả lời. Có `SELECT` trước là hai đường
tới 409, và đường `23505` chỉ còn chạy khi race thật — `FRD-06` mất tính tất định (Mục 8 Bước 2).

### D3 — `POST /friends/requests/{userId}/accept`

`[RequirePermission(SocialGraphPermissions.FriendRespond)]`. Một câu:

```csharp
var (min, max) = FriendPair.Of(actorId, userId);
var changed = await db.Friendships
    .Where(f => f.UserMinId == min && f.UserMaxId == max
             && f.Status == FriendshipStatus.Pending
             && f.RequesterId == userId)          // lời mời phải ĐẾN từ người kia — TC-A03-friend-self-accept canh vế này
    .ExecuteUpdateAsync(s => s
        .SetProperty(f => f.Status, FriendshipStatus.Accepted)
        .SetProperty(f => f.AcceptedAt, now)
        .SetProperty(f => f.UpdatedAt, now), ct);  // Execute* bỏ qua SaveChanges — tự gán updated_at
if (changed == 0) return Result<RelationshipResponse>.Forbidden();
```

0 dòng → 403 **không** nói lý do. Sau `COMMIT`: invalidate cả hai, `FriendRequestAccepted`. 200 `friends`.

### D4 — `DELETE /friends/requests/{userId}` + `DELETE /friends/{userId}`

`[Authorize]` trần (Đ-4.12). `ExecuteDeleteAsync` với cặp chuẩn hóa **và** `Status == Pending` / `Status == Accepted` tương
ứng — thiếu vế trạng thái là `DELETE /friends/{id}` hủy luôn lời mời đang chờ, và ngược lại. Hủy lời mời theo **chiều nào
cũng được** (người gửi hủy, người nhận từ chối — một endpoint). 204 kể cả 0 dòng; `changed > 0` mới invalidate. Test hình dạng (commit `D4`): `DELETE /friends/{id}` khi
chỉ có lời mời `pending` → lời mời **vẫn còn**; `DELETE /friends/requests/{id}` khi đã là bạn → vẫn là bạn.

### D5 — `GET /friends` + `GET /friends/requests?direction=`

- **Query class** theo nếp `ListUserPostsQuery`: `Cursor`, `Limit` (mặc định 20, tối đa 50), `Direction` bind là
  **`string?`** rồi validate bằng FluentValidation → `errors.direction`. Bind thẳng enum thì giá trị lạ ra thông điệp tiếng
  Anh của MVC.
- **`FriendCursor`** (L13) — chép `PostCursor`, đổi nghĩa hai trường thành `(since, otherUserId)`.
- Truy vấn bạn bè: `Status == Accepted && (UserMinId == me || UserMaxId == me)`, chiếu `other`, sắp `AcceptedAt DESC,
  other DESC`, keyset, `Take(limit + 1)`. Lời mời: `Status == Pending`, `incoming` = `RequesterId != me`, `outgoing` =
  `RequesterId == me`, sắp `CreatedAt DESC`.
- **Một** `IUserDirectory.GetManyAsync` cho cả trang; avatar ký bằng `IObjectStorage.CreatePresignedGet`.
- Người kia mất hồ sơ → thẻ **vắng mặt** (hợp đồng đã ghi); `nextCursor` tính từ dòng thứ `limit` của danh sách **gốc**
  trước khi lọc — cùng luật Đ-4.9.

### D6 — `PUT` + `DELETE /follows/{userId}`

`PUT`: `[RequirePermission(SocialGraphPermissions.FriendRequest)]` (Đ-4.12), khác mình (400) → hồ sơ (404) →

```csharp
var inserted = await db.Database.ExecuteSqlAsync(
    $"INSERT INTO socialgraph.follows (follower_id, followee_id, created_at) VALUES ({actorId}, {userId}, {now}) ON CONFLICT DO NOTHING", ct);
```

`ExecuteSqlAsync` với chuỗi nội suy là **tham số hóa** (luật 3). `inserted == 1` → `InvalidateAsync(actorId)` — **chỉ**
người theo dõi: theo dõi không đổi nguồn feed của người được theo dõi. 204. `DELETE`: `[Authorize]`, `ExecuteDeleteAsync`,
204, xóa cache khi có dòng.

### Cạm bẫy chung của D1–D6

- **`FriendPair.Of` gọi ở hai chỗ khác nhau với hai thứ tự đối số** — không sao, hàm đối xứng. Nhưng **tự chuẩn hóa bằng
  `CompareTo` tại chỗ** "cho nhanh" là mở lại GUID-01. Chỉ `FriendPair.Of`.
- **Trả 404 từ accept khi không có lời mời** — quy ước 3b: ghi cần sở hữu → 403. `TC-A03-friend-accept` bắt.
- **Event phát trong `try` cùng khối với `SaveChanges`** → lỗi log làm hỏng thao tác đã `COMMIT`. Event sau cùng, không ném.

---

## 8. B3 — Test quan hệ `FRD-*`, `FOL-*` + nửa bảng đột biến

**Mục tiêu.** BR-03, FR-010/011/012 và race đúng qua HTTP trên Postgres thật; mỗi luật quan trọng đã từng làm test đỏ.

**Xong khi.** Hai lớp xanh, mười bốn ca mang mã Mục 10.1; nửa quan hệ của bảng Mục 17.4 tick; `git status` sạch.

### Ranh giới B3 / D (tiền lệ `Q-B3` GĐ2)

| Của `B3` — luật nghiệp vụ còn nguyên   | Của `D*` — endpoint chạy được                                                    |
| -------------------------------------- | -------------------------------------------------------------------------------- |
| `FRD-01..10`, `FOL-01..04`             | Bốn trạng thái `/relationships` (`D1`), phân trang + `direction` (`D5`), id sai dạng → 400 |

### Các bước

**Bước 1 — `SocialGraph/FriendRequestTests.cs`** (`[Collection(PostgresCollection.Name)]`,
`IClassFixture<ModulesApiFactory>`, database riêng). Mỗi ca một `[Fact]` tên bắt đầu bằng mã. Khẳng định **ngoài** status:

| Mã       | Khẳng định bắt buộc                                                                                               |
| -------- | ----------------------------------------------------------------------------------------------------------------- |
| `FRD-01` | `friendship = "outgoing"`; `GET /friends/requests?direction=incoming` của **B** thấy A                             |
| `FRD-02` | Vẫn **đúng một** dòng cho cặp (`QueryRowAsync`)                                                                  |
| `FRD-03` | 400 + `errors.userId`; **không** dòng nào; **không** 500                                                          |
| `FRD-04` | 404 — `userId` mới tinh chưa có hồ sơ                                                                              |
| `FRD-05` | `friends`; `accepted_at` khác null; **cả hai** thấy nhau trong `GET /friends`                                      |
| `FRD-06` | Bước 2                                                                                                            |
| `FRD-07` | B từ chối → 204, dòng mất; A gửi lại → 201                                                                        |
| `FRD-08` | A hủy lời mời mình gửi → 204, dòng mất                                                                            |
| `FRD-09` | Hủy kết bạn → 204; bài `friends` của B với A ngay request kế là 404 — quan sát qua `GET /posts/{id}`, không qua DI  |
| `FRD-10` | A gửi → A hủy → B chấp nhận → 403; body không nói lý do                                                           |

**`SocialGraph/FollowTests.cs`**: `FOL-01` theo dõi → 204, `/relationships` ra `following: true` · `FOL-02` lần hai → 204,
**đúng một** dòng · `FOL-03` tự theo dõi → 400, không 500 · `FOL-04` bỏ theo dõi → 204, bỏ lần hai vẫn 204.

**Bước 2 — `FRD-06` mười cặp mới**, mỗi cặp `Task.WhenAll(A→B, B→A)`; **từng cặp**: đúng một 201, đúng một 409, đúng một
dòng, không 500. Tất định nhờ `D2` không `SELECT` trước (Mục 7 D2): request thứ hai luôn đi qua `23505` dù va hay không.

**Bước 3 — nửa quan hệ của bảng đột biến** (Mục 17.4, dòng đánh dấu "bước 4").

### Cạm bẫy đã biết

- **429 giữa lớp.** Rate limit theo **user** — mỗi ca người dùng mới; `FRD-06` là hai mươi người, không phải một A.
- **Chạy đột biến bằng cả bộ** → chậm, và đỏ lây che test dự kiến. Lọc
  `--filter "FullyQualifiedName~FriendRequestTests|Category=AuthZ"`.

---

## 9. B5 — Cổng hợp đồng `socialgraph-v1` + thử cho đỏ

**Mục tiêu.** Hợp đồng và controller không lệch được mà CI im lặng — từ bước 5 trở đi (Q-B5).

**Xong khi.** Lớp mới có trong `--list-tests --filter Category=Contract` và xanh; ba kiểu thử đỏ đã làm; codegen FE sạch.

### Các bước

1. `SocialApp.IntegrationTests.csproj`, cạnh ba dòng có sẵn:

   ```xml
   <Content Include="..\..\src\backend\Modules\SocialGraph\Presentation\socialgraph-v1.yaml"
            Link="Contracts\socialgraph-v1.yaml"
            CopyToOutputDirectory="PreserveNewest" />
   ```

2. `SocialGraphContractTests.cs` chép `ContentContractTests`: `[Trait("Category","Contract")]` trên **chính lớp**,
   `IClassFixture<ApiFactory>`, tên nhóm từ `SocialGraphApiGroup.Name`.
3. Số test `Contract` tăng **đúng** bằng số `[Fact]` của `ContractTestsBase` — tăng ít hơn là thiếu trait.
4. Thử đỏ, mỗi kiểu một lần rồi khôi phục:

   | Thử                                                                           | Phải thấy                                                    |
   | ----------------------------------------------------------------------------- | ------------------------------------------------------------ |
   | `[ProducesResponseType(StatusCodes.Status418ImATeapot)]` lên một action        | Test tập status code đỏ, nêu đúng path × method               |
   | Comment dòng `Content Include`                                                | `Không thấy file hợp đồng ở …`                                |
   | Xóa `[Trait]` khỏi lớp                                                        | `--list-tests` **mất** lớp trong khi cổng vẫn xanh — ghi lại: `TreatNoTestsAsError` không bịt được lỗ này |

5. `SocialGraphPermissionsTests` thử đỏ lại một lần nếu chưa làm ở `D0`.
6. `cd src/frontend && pnpm gen:api && git status --porcelain` rỗng — cổng codegen không sửa gì.

### Cạm bẫy đã biết

- **Controller inject thứ mở kết nối DB trong constructor** → `ApiFactory` (Postgres không tới được) làm **cả** cổng hợp
  đồng đỏ. Sửa controller, đừng sửa `ApiFactory`.
- **`$ref` chéo file** sang `content-v1` "cho gọn" → parse lỗi; `UserCard` định nghĩa lại là có chủ đích (Mục 8.1).

---

# Phần IV — Bước 5: `C2`, `C3`, `C4`, `D7`, `B4`

Viết `FEED-01..08` (Mục 14 Bước 1) **trước** `C2` — chúng đỏ 404 tới `D7`, nhưng viết trước là buộc mình đọc lại Đ-4.5 trước
khi viết SQL. Chạy tay qua service trong lúc chờ `D7` thì dùng test của `C2` Bước 4.

## 10. C2 — Truy vấn LATERAL + feed gợi ý + Đ-4.11

**Mục tiêu.** Chi phí mỗi trang chặn trên bởi `số nguồn × (limit+1)`; BR-02, BR-07 trong truy vấn.

**Xong khi.** `EXPLAIN` trên bộ dữ liệu tải đúng hình dạng và đã dán; test đối chiếu `FeedVisibility` ↔ SQL xanh; `EXPLAIN`
trước/sau Đ-4.11 trong thân commit; test GĐ2 của `/users/{id}/posts` xanh không sửa khẳng định.

### Các bước

**Bước 1 — contract trong `Content/Application/Feed/`:**

```csharp
public interface IFeedStore
{
    /// <summary>Đ-4.7. Một LATERAL mỗi nguồn; BR-02 + status='published' trong truy vấn. take = limit + 1.</summary>
    Task<IReadOnlyList<Post>> NetworkPageAsync(Guid me, FeedSources sources, PostCursor? cursor, int take, CancellationToken ct);

    /// <summary>Đ-4.6. Bài public mới nhất toàn hệ thống, TRỪ bài của chính mình, trên idx_posts_public_recent.</summary>
    Task<IReadOnlyList<Post>> SuggestedPageAsync(Guid me, PostCursor? cursor, int take, CancellationToken ct);
}
```

**Bước 2 — `Infrastructure/Persistence/FeedStore.cs`**: SQL của Đ-4.7 nguyên văn qua `db.Posts.FromSql($"…")`, mảng id
truyền như tham số `Guid[]` (Npgsql map sang `uuid[]`). Ba điểm EF:

- **Global query filter bọc ngoài `FromSql`** (EF ghép câu thô thành subquery rồi thêm `WHERE status <> 'deleted'`). Vô hại
  với kết quả, nhưng **thứ tự của subquery không được bảo đảm** sau khi bọc — sắp lại trong bộ nhớ (≤ 21 dòng) theo
  `(CreatedAt DESC, PostId DESC)`, hoặc `.OrderByDescending(...).ThenByDescending(...)` sau `FromSql`.
- **`AsNoTracking()`** — đường đọc.
- **Cursor null**: truyền hai tham số có kiểu (`DateTimeOffset?`, `Guid?`); Postgres không suy được kiểu của `NULL` trần
  trong `@cursor_at IS NULL` → ép kiểu trong SQL (`@cursor_at::timestamptz`).

Feed gợi ý: `WHERE status='published' AND privacy='public' AND author_id <> @me AND keyset ORDER BY created_at DESC, post_id
DESC LIMIT @take` — mệnh đề `WHERE` phải **khớp nguyên văn** điều kiện của index một phần, không thì planner không dùng.

**Bước 3 — Đ-4.11**: thêm `.Where(p => p.Status == PostStatus.Published)` vào `PostStore.ListByAuthorAsync`. `EXPLAIN` trên
bộ dữ liệu tải **trước và sau** (một tác giả nhiều bài), dán cả hai vào thân commit. Impact analysis trước (Mục 1.2).

**Bước 4 — `FeedVisibility` + test đối chiếu** (Mục 10.4):

```csharp
/// <summary>Bảng Đ-4.5 dạng hàm thuần. Bản thứ hai của luật trong WHERE của LATERAL — test đối chiếu canh hai bản.</summary>
public static bool CanSee(PostPrivacy privacy, PostStatus status, Guid authorId, Guid me, FeedSources sources) => …
```

Unit test bảng Đ-4.5 (ba nguồn × ba mức × `published`/`hidden`). Integration `FeedStoreTests`: dựng ~30 bài đủ tổ hợp bằng
SQL, so tập id `NetworkPageAsync` trả với tập id `CanSee` chọn trên cùng dữ liệu — cùng nếp `PostVisibilityTests` GĐ2.

**Bước 5 — `EXPLAIN (ANALYZE, BUFFERS)` trên môi trường đo** với một người ở phân vị cao (500 bạn), trang đầu và trang có
cursor. Phải thấy `Nested Loop` → `Index Scan using idx_posts_author_created` bên trong, **không** `Sort` trên toàn bộ bài
(một `Sort` trên ≤ `số nguồn × take` dòng ở ngoài cùng là đúng). Feed gợi ý: `Index Scan using idx_posts_public_recent`,
không `Sort`. Dán cả hai vào "Thực tế thi công".

### Cạm bẫy đã biết

- **`FromSqlRaw` với chuỗi nội suy** → SQL injection qua mảng id. Chỉ `FromSql` (luật 3).
- **`unnest` của mảng rỗng** trả 0 dòng — đúng; nhưng truyền `null` thay mảng rỗng là lỗi kiểu. Luôn `Array.Empty<Guid>()`.
- **Tin `EXPLAIN` trên DB test.** Bảng nhỏ → `Seq Scan` bất kể index. Chỉ `EXPLAIN` trên dữ liệu `C5` mới tính.

---

## 11. C3 — `PostHydrator` dùng chung + kiểm lại BR-02 (Đ-4.9)

**Mục tiêu.** Một đường dựng `PostResponse` cho mọi danh sách; cache không bao giờ là nguồn sự thật cuối.

**Xong khi.** `ListByUserAsync` đi qua `PostHydrator`, test GĐ2 xanh không sửa khẳng định; chữ ký ghi vào Mục 18.

### Các bước

**Bước 1 — tách** phần "danh sách `Post` → `PostResponse`" của `PostReadService.ListByUserAsync` (lô ảnh + lô tác giả +
`mapper.ToResponses`) thành:

```csharp
/// <summary>
/// C3 (GĐ4, Đ-4.9): MỘT chỗ dựng PostResponse cho mọi danh sách — trang cá nhân, feed. Số câu truy vấn cố định: một lô
/// ảnh, một lô tác giả. GĐ3 thêm một lô myReaction VÀO ĐÂY (Mục 9.1 #1) — feed, trang cá nhân có trường mới cùng lúc.
/// </summary>
public sealed class PostHydrator(IPostStore posts, IUserDirectory directory, PostResponseMapper mapper)
{
    public async Task<IReadOnlyList<PostResponse>> HydrateAsync(IReadOnlyList<Post> page, Guid actorId, CancellationToken ct)
    { … }
}
```

Scoped, đăng ký trong `AddContentModule`. `GetAsync` (một bài) có thể giữ nguyên — nó đã dùng cùng `mapper`; chỉ chuyển nếu
không làm đổi số câu truy vấn.

**Bước 2 — `IPostStore.FindManyPublishedAsync(IReadOnlyCollection<Guid> ids, ct)`**: nạp bài theo PK cho đường trúng cache,
`Status == Published`, một câu. Trả **không** theo thứ tự — service sắp lại theo thứ tự id trong cache.

**Bước 3 — kiểm lại BR-02** trong `FeedService` (`C4`) bằng `FeedVisibility.CanSee` với nguồn **hiện tại** — trong bộ nhớ,
không truy vấn thêm (Mục 7.3 gốc).

### Cạm bẫy đã biết

- **Đổi thứ tự câu truy vấn khi tách** → `ListPostsTests` vẫn xanh nhưng `FEED-Q1` sau này mang con số khác Mục 7.2. Tách
  nguyên văn, không "tối ưu" cùng lúc.
- **`PostHydrator` nhận `FeedSources`** "cho tiện kiểm lại BR-02" → trang cá nhân phải bịa nguồn. Kiểm lại là việc của
  `FeedService`, hydrator không biết feed tồn tại.

---

## 12. C4 — `FeedService` + cache trang đầu + degrade + 503 (Đ-4.8, Đ-4.10)

**Mục tiêu.** Ráp SEQ-03 đúng thứ tự; trang đầu nhẹ khi trúng cache; DB quá tải thành 503.

**Xong khi.** `FeedService` chạy đúng bảy bước Mục 7.2; `FeedPageCacheTests` (Redis thật) xanh; tác giả đăng bài → khóa
của tác giả mất; truy vấn chậm → `FeedQueryTimeoutException` → `ContentErrors.FeedUnavailable` (503).

### Các bước

**Bước 1 — `FeedService.GetAsync(me, rawCursor, limit, ct)`** trong `Content/Application/Feed/`, đúng thứ tự Mục 7.2:

```
1. sources ← IFeedSourceReader.GetAsync(me)
2. mode ← sources.IsEmpty ? suggested : network
3. trang đầu + limit mặc định + công tắc bật → IFeedPageCache.GetAsync(me)
      trúng VÀ fingerprint == Fingerprint(sources) (Q-C1) → ids, next từ cache
4. trượt → IFeedStore.*PageAsync(take = limit + 1)  [CommandTimeout 5s]
      next ← rows.Count > limit ? cursor của rows[limit-1] : null
      ids ← rows.Take(limit)
      trang đầu → IFeedPageCache.SetAsync(me, {ids, mode, next, fingerprint}, 30s)
5. posts ← (trúng cache) FindManyPublishedAsync(ids) sắp lại theo ids  |  (trượt) rows.Take(limit)
6. posts ← posts.Where(FeedVisibility.CanSee(…, sources hiện tại))      ← kiểm lại, KỂ CẢ khi trượt (rẻ, giữ một đường)
7. items ← PostHydrator.HydrateAsync(posts, me)
← FeedPage(items, next, mode)
```

**Bước 2 — `IFeedPageCache`** (Application) + `RedisFeedPageCache` (Infrastructure), cùng khuôn fail-open của `C1`: khóa
`feed:p1:{me:D}`, giá trị JSON `{"ids":[…],"mode":"network","next":"…","fp":"…"}` — **không có gì khác** (Đ-4.9, L10).
`Fingerprint(sources)` = SHA-256 cắt 16 byte, base64, của hai mảng id đã sắp — tính trong Application, hàm thuần, có unit test.

**Bước 3 — xóa khóa của tác giả** trong `PostService.CreateAsync` / `UpdateAsync` / `DeleteAsync`, **sau** `SaveChanges` /
`AddWithMediaAsync` thành công. Lỗi Redis nuốt + log (TTL 30s chặn trên).

**Bước 4 — timeout 5s chỉ cho truy vấn feed** trong `FeedStore`:

```csharp
var previous = db.Database.GetCommandTimeout();
db.Database.SetCommandTimeout(TimeSpan.FromSeconds(FeedQueryTimeoutSeconds));   // = 5, một hằng
try { return await …ToListAsync(ct); }
catch (Exception ex) when (IsTimeout(ex)) { throw new FeedQueryTimeoutException(ex); }
finally { db.Database.SetCommandTimeout(previous); }   // DbContext scoped: không trả lại là hydrate cùng request bị 5s theo
```

`IsTimeout` phải nhận **cả hai** dạng Npgsql có thể ném khi hết `CommandTimeout`: `NpgsqlException { InnerException:
TimeoutException }` và `PostgresException { SqlState: "57014" }` (query_canceled — Npgsql gửi lệnh hủy lên server). Loại nào
thật sự xảy ra thì `FEED-12` trả lời; ghi vào "Thực tế thi công". `OperationCanceledException` do **client ngắt** thì
**không** phải 503. `FeedQueryTimeoutException` khai ở `Application` (luật 7) — Application không bắt được kiểu Npgsql.

**Bước 5 — lỗi**: `ContentErrors.FeedUnavailable = new("feed.unavailable", "<câu example của yaml /feed 503>", 503)`.

**Bước 6 — công tắc** `Feed:PageCache:Enabled` theo Q-C2. **Bước 7 — `FeedPageCacheTests`** (integration, Redis thật):
set/get round-trip, giá trị thô đúng bốn trường, Redis chết → `null`, không ném.

### Cạm bẫy đã biết

- **`nextCursor` từ danh sách sau lọc** (bước 6) thay vì danh sách gốc (bước 4) → trang sau lặp bài đã bị lọc ở trang trước
  hoặc bỏ sót. Luôn từ bước 4 (Đ-4.9).
- **Cache cả trang có cursor** → khóa phải kèm cursor, số khóa nổ. Đ-4.8: **chỉ** trang đầu, `limit` mặc định.
- **Ghi cache trước khi hydrate thành công** — vô hại (chỉ id), nhưng **đừng** ghi `items` đã hydrate "để lần sau khỏi
  hydrate": đó chính là CACHE-01.
- **`SetCommandTimeout` không khôi phục** → mọi câu sau trong request (hydrate, và mọi service scoped khác) mang 5s.

---

## 13. D7 — `GET /feed` + `content-v1.yaml` + rà RFC 7807 hai nhóm

**Mục tiêu.** Feed thành endpoint thật; hợp đồng `content-v1` mở lại theo luật chỉ-thêm; mọi mã lỗi của hai nhóm khớp code.

**Xong khi.** Cổng `API contract` xanh cho cả bốn module **trong commit này** (yaml + controller cùng lúc); `schema.d.ts`
sinh lại cùng commit; bảng rà mã lỗi trong thân commit; `TC-A01-feed` **giữ** 401 khi đã có route → matrix giữ 24/24.

### Các bước

**Bước 1 — `Content/Presentation/FeedController.cs`** (tách khỏi `PostsController` — một controller một nhóm tài nguyên):

```csharp
[HttpGet("feed")]
[RequirePermission(ContentPermissions.PostReadPublic)]
[ProducesResponseType<FeedPage>(StatusCodes.Status200OK)]
[ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status503ServiceUnavailable, "application/problem+json")]
public async Task<ActionResult<FeedPage>> Get([FromQuery] FeedQuery query, CancellationToken ct)
{
    var result = await feed.GetAsync(User.GetUserId(), query.Cursor, query.EffectiveLimit, ct);
    if (result.Error is { Status: StatusCodes.Status503ServiceUnavailable })
        Response.Headers.RetryAfter = "5";   // Result → Problem không gắn header nào; gắn ở đây, trước khi trả
    return result.ToActionResult(this);
}
```

`FeedQuery` + validator chép `ListUserPostsQuery` (cursor rác → 400 `errors.cursor`). `FeedPage(Items, NextCursor, Mode)`,
`FeedMode { Network, Suggested }` — ra dây `network` / `suggested` nhờ converter CamelCase của host.

**Bước 2 — `content-v1.yaml`** theo **đúng** Mục 8.2: path `/feed`, schema `FeedPage` (`items` `$ref` `PostResponse`,
`nextCursor` nullable với câu mô tả nguyên văn, `mode` enum), 503 có header `Retry-After`, `info.version` →
`1.0.0-gd4`. **Chỉ thêm** — `git diff` của yaml không có dòng `-` nào ngoài `version`.

**Bước 3 — `cd src/frontend && pnpm gen:api`**, commit `lib/api/content/schema.d.ts` cùng commit (luật frontend Mục 7).

**Bước 4 — rà RFC 7807 hai nhóm** (nếp `D9` GĐ2): bảng `endpoint × mã trong yaml × nơi code sinh ra mã đó × test canh`, cho
toàn bộ `socialgraph-v1` và phần `/feed`. Mã nào có trong yaml mà không có test → viết test hoặc ghi lý do. Không thông điệp
nào chứa id, tên kiểu .NET, tên constraint.

### Cạm bẫy đã biết

- **Yaml trước controller** hoặc ngược lại trong hai commit → `ContentContractTests` đỏ ở commit giữa (chính lý do lệch Mục
  9.2). Một commit.
- **Quên header trong yaml** → cổng hợp đồng không so header nên vẫn xanh, còn FE sinh type thiếu. Rà tay ở Bước 4.

---

## 14. B4 — Test feed `FEED-*` + `FEED-Q1`

**Mục tiêu.** Ma trận quyền, feed gợi ý, luật cache, degrade thành cổng; N+1 bị chặn.

**Xong khi.** Ba lớp xanh; mọi ca cache khẳng định **trúng cache** trước; nửa feed của bảng Mục 17.4 tick.

### Ba lớp, chia theo harness

| Lớp                               | Harness                                                              | Ca                                             |
| --------------------------------- | -------------------------------------------------------------------- | ---------------------------------------------- |
| `Content/FeedTests.cs`            | `ModulesApiFactory` mặc định — Redis chết, không cache               | `FEED-01..08`, `FEED-11`, `FEED-12`, `PAGE-04`  |
| `Content/FeedCacheTests.cs`       | `+ UseRedis` + `RedisFixture` + `DistinctGetUrls`                    | `FEED-07b`, `FEED-09`, `FEED-09b`, `FEED-10`, `FEED-13` |
| `Content/FeedQueryCountTests.cs`  | Mặc định + `SqlCommandCounter`                                       | `FEED-Q1`                                      |

`FEED-01..08` chạy **không cache** có chủ đích: chúng canh truy vấn — có cache thì bug truy vấn bị che bởi dữ liệu cũ.
`FEED-11` nằm ở đây vì harness mặc định **chính là** Redis không tới được: 200 + nội dung đúng + log cảnh báo
(`CapturingLogSink`).

### Các bước

**Bước 1 — `FeedTests`** (dựng cảnh bằng `MakeFriendsAsync`, `FollowOkAsync`, `CreatePostOkAsync`):

- `FEED-01`: 25 bài của **một** bạn (một tác giả, dưới rate limit). Trang 1: 20, mới trước, `nextCursor` khác null; trang
  2: 5, `nextCursor = null`; **không trùng `postId`** giữa hai trang.
- `FEED-02..05`: theo bảng Mục 10.2 gốc. `FEED-03` (chỉ theo dõi, không bạn) khẳng định thêm `mode = "network"` — người
  chỉ theo dõi mà rơi vào gợi ý thì bài `public` của người được theo dõi vẫn hiện, và ca xanh nếu không soi `mode`.
  `FEED-05` một ca hai vế: `private` của bạn **không**, của mình **có**.
- `FEED-06`: đăng qua API rồi `UPDATE content.posts SET status='hidden'` bằng Npgsql — comment tại chỗ (luật 9).
- `FEED-07`: chưa kết nối, người khác có bài `public` → `mode = "suggested"`, **không** có bài của mình.
- `FEED-08`: có một bạn chưa đăng gì, **và có** bài `public` của người lạ → `mode = "network"`, `items` rỗng,
  `nextCursor = null`. Không có bài người lạ thì ca xanh cả khi code trộn gợi ý.
- `FEED-12` (L3, L9, Q-B4):

```csharp
await using var locker = new NpgsqlConnection(factory.ConnectionString);
await locker.OpenAsync();
await using var tx = await locker.BeginTransactionAsync();
await new NpgsqlCommand("LOCK TABLE content.posts IN ACCESS EXCLUSIVE MODE", locker, tx).ExecuteNonQueryAsync();
try
{
    using var response = await client.GetFeedAsync(a);   // truy vấn feed chờ khóa > 5s
    Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    Assert.Equal(TimeSpan.FromSeconds(5), response.Headers.RetryAfter?.Delta);
    // + title viết tay theo yaml, có traceId — KHÔNG khẳng định "feed.unavailable": nó không lên dây (L9)
}
finally
{
    await tx.RollbackAsync();
}
```

  Người đọc `a` phải có ít nhất một nguồn (nếu không, câu đi `idx_posts_public_recent` — vẫn bị khóa, vẫn đúng, nhưng ghi rõ
  ca đi nhánh nào).
- `PAGE-04`: `?cursor=rac` → 400 `errors.cursor`.

**Bước 2 — `FeedCacheTests`.** Khuôn chung: đọc lần 1 → **khẳng định khóa `feed:p1:{id}` tồn tại** (`redis.Database`) → gây
thay đổi → đọc lần 2. Bỏ bước khẳng định khóa thì feed không cache gì vẫn qua cả năm ca.

- `FEED-07b` (L15, Q-C1): A mới tinh, có bài `public` của người lạ. A đọc → `suggested`, khóa có → A kết bạn với B (B có
  bài) → A đọc lại **ngay** (khóa vẫn còn, TTL 30s chưa hết): `mode = "network"`, có bài của B. Đây chính là lát cắt `F2`.
- `FEED-09`: A, B là bạn **và** A theo dõi B; B có bài `friends` + `public`. A đọc (khóa có) → A hủy kết bạn → A đọc lại:
  **không** còn bài `friends`, **còn** bài `public`. "A theo dõi B" là cần — không có nó thì B biến mất hẳn khỏi nguồn và ca
  không phân biệt "loại đúng bài" với "loại cả người" (Đ-4.5). Cuộn bằng `nextCursor` không lặp bài nào.
- `FEED-09b` (L11): A **chỉ theo dõi** B; B có bài `public`. A đọc (khóa có) → B `PATCH` bài đó thành `friends` → A đọc lại
  (khóa **vẫn** trúng — kiểm dấu nguồn không đổi): bài **không** còn. Đây là lưới duy nhất của bước kiểm lại BR-02.
- `FEED-10` (L2): bài có ảnh. Lần 2 trúng cache → URL ảnh **khác** lần 1; `StringGet("feed:p1:{a}")` **không** chứa
  `fake.invalid`.
- `FEED-13` (L5): A, B là bạn, mỗi người một bài. Mỗi người đọc feed mình hai lần (lần 2 trúng): `canEdit = true` **chỉ**
  trên bài của người đọc. Giá trị thô của **cả hai** khóa parse được thành đúng bốn trường `ids`, `mode`, `next`, `fp` — không
  `canEdit`, `media`, `author`. Tách phần khẳng định giá trị thô thành helper để GĐ3 gọi lại khi thêm `myReaction`.

**Bước 3 — `FeedQueryCountTests` (`FEED-Q1`, L4):**

1. A + 20 tác giả có hồ sơ; mỗi tác giả một bài có ảnh qua API (`PutPostObject` + `CreatePostOkAsync`).
2. Quan hệ A với **50** nguồn (20 tác giả + 30 người không bài) INSERT bằng SQL, chuẩn hóa bằng Postgres:
   `INSERT … SELECT LEAST(@a, x), GREATEST(@a, x), @a, 'accepted', now(), now(), now() FROM unnest(@ids) x`.
3. Gọi feed một lần **làm nóng** (cache quyền tầng 2, pool) → `Reset()` → gọi → `n50`.
4. Thêm 150 nguồn bằng SQL → `Reset()` → gọi → `n200`.
5. `n50 == n200` **và** `n50 == <hằng số viết tay>`, comment liệt kê từng câu theo Mục 7.2: nguồn 2 (Redis chết → DB) +
   feed 1 + ảnh 1 + tác giả 1 = **5**. Không có "bài theo PK" vì trượt cache (bước 5 của `C4` dùng luôn `rows`). Fail thì in
   `counter.Statements`.

Ra khác 5 thì hoặc code thừa một câu, hoặc Mục 7.2 sai — quyết định cái nào rồi sửa đúng chỗ, ghi vào "Thực tế thi công".

### Cạm bẫy đã biết

- **Bài tạo cùng mili giây** → khẳng định thứ tự theo thứ tự tạo trong test, không theo `created_at` đọc lại.
- **`FEED-Q1` đếm lệnh của `MediaCleanupWorker`** — worker chạy nền trên cùng database. Thấy câu lạ trong `Statements` thì
  kiểm `Media:Cleanup:Enabled` trước khi nghi feed.
- **So `Retry-After` bằng chuỗi header thô** → dạng giây hay ngày tùy ASP.NET. Đọc qua `Headers.RetryAfter.Delta`.

---

# Phần V — Bước 6: `C6`

## 15. C6 — Kịch bản k6 + ba lượt + báo cáo sơ bộ (Đ-4.13)

**Mục tiêu.** GOAL-01 có con số trước GĐ8, khi còn thời gian sửa.

**Xong khi.** `docs/giai-doan-4/bao-cao-k6-so-bo.md` có đủ: máy chạy, bản k6, cấu hình giới hạn, bộ dữ liệu, ba lượt ×
p50/p95/p99/tỷ lệ lỗi, số kết nối DB đỉnh, `EXPLAIN` truy vấn chậm nhất, việc chuyển GĐ8 (nếu có).

### Các bước

**Bước 1 — `tests/load/feed/feed.js`:**

- `setup()`: đọc `users.csv` (`SharedArray`), ký JWT HS256 **cho từng VU** bằng `k6/crypto` `hmac('sha256', key, …)` +
  `k6/encoding` `b64encode(…, 'rawurl')`. Claim giống `JwtAccessTokenIssuer`: `sub`, `role = "USER"`, `iat`, `exp`, `jti`,
  `iss = https://mxh.banhgao.net`, `aud = socialapp-api` (từ `appsettings.json`, **kiểm lại** trước khi chạy). Khóa đọc từ
  `__ENV.PERF_JWT_KEY` — **không** gõ vào file.
- Kịch bản: tăng lên 1.000 VU trong 2 phút, giữ 5 phút, giảm 1 phút. Mỗi vòng `GET /api/v1/feed` (trang đầu); 30% số vòng
  đi tiếp một trang bằng `nextCursor`; nghỉ 1–3s (≤ 60 req/phút/user — dưới rate limit 100).
- `thresholds`: `http_req_duration: ['p(95)<500']`, `http_req_failed: ['rate<0.01']`.
- Gọi **thẳng API** port `18080`, không qua BFF (Đ-4.13).

**Bước 2 — tự kiểm trước lượt thật:** 5 VU × 30s → mọi request 200, không 401 (claim sai), không 429 (nghỉ quá ngắn).

**Bước 3 — ba lượt**, lượt nào cũng dựng lại api container (cache sạch):

| Lượt | Cấu hình                                           | Dùng để                                              |
| ---- | -------------------------------------------------- | ---------------------------------------------------- |
| (1)  | Mọi cache bật                                      | Con số thực tế                                        |
| (2)  | `Feed__PageCache__Enabled=false`                   | **Con số kết luận GOAL-01** — không phụ thuộc tỷ lệ trúng cache |
| (3)  | Bật cache, `docker compose -p perf stop redis` ở phút thứ 4 | Chứng minh degrade: lỗi < 1%                  |

Trong lúc chạy, ghi `SELECT count(*) FROM pg_stat_activity WHERE datname = 'socialapp_perf'` mỗi 10s — số kết nối đỉnh.

**Bước 4 — không đạt thì làm theo thứ tự Đ-4.13**, không họp: `EXPLAIN` câu chậm nhất (bật `log_min_duration_statement =
200` trên Postgres đo) → pool (`Maximum Pool Size` so với `max_connections` — 1.000 VU trên pool mặc định 100 là hàng đợi ở
app, PERF-03) → người ở đuôi p99 (số nguồn). Mỗi thay đổi: con số trước/sau vào báo cáo. **Không** giảm VU, không tăng thời
gian nghỉ cho đẹp số (PERF-02).

### Cạm bẫy đã biết

- **Máy chạy k6 cũng là máy chạy api** → k6 tranh CPU với api đã bị giới hạn 2 nhân. Ghi rõ trong báo cáo; lý tưởng là hai máy.
- **Token hết hạn giữa lượt.** `exp` phải phủ cả 8 phút + dự phòng.
- **Đo lượt (1) rồi kết luận.** Kịch bản đọc trang đầu liên tục → tỷ lệ trúng cache cao giả tạo. Kết luận chỉ từ lượt (2).

---

## 16. Kế hoạch commit

Scope theo khối (`gd4-b`, `gd4-c`, `gd4-d`). Mỗi commit chạm code có `Test:` (trước → sau) và `detect-changes:`; chạy
`node .gitnexus/run.cjs detect-changes --scope all --repo .` trước mỗi commit, `partial`/`truncated` thì chạy lại. Footer
**sạch bút ký**. Thứ tự dưới đây **là** thứ tự làm.

| #  | Bước | Tiêu đề                                                                                                   | Ghi ngược        |
| -- | ---- | --------------------------------------------------------------------------------------------------------- | ---------------- |
| 1  | 2    | `test(gd4-b): B1 — harness feed: Redis thật cho ModulesApiFactory, URL ký khác nhau, đếm lệnh SQL`         | L1, L2, L4       |
| 2  | 2    | `test(gd4-b): B2 — sáu dòng AuthZ matrix của GĐ4, năm dòng đỏ có chủ đích chờ D2/D3/D7`                    | L8               |
| 3  | 3    | `test(gd4-c): C5 — môi trường đo perf và seed 1M bài có hai chốt chặn`                                     | —                |
| 4  | 4    | `feat(gd4-d): D0 — nền SocialGraph: nhóm Swagger, mã quyền có test canh, lỗi, DTO quan hệ`                 | L6               |
| 5  | 4    | `feat(gd4-c): C1 — cache nguồn feed 60s fail-open, InvalidateAsync xóa cả hai phía`                        | L12, Q-C2        |
| 6  | 4    | `feat(gd4-d): D1 — GET /relationships, bốn trạng thái, không dò tài khoản`                                 | —                |
| 7  | 4    | `feat(gd4-d): D2 — gửi lời mời, 23505 thành 409, xóa cache và event sau COMMIT`                            | —                |
| 8  | 4    | `feat(gd4-d): D3 — chấp nhận bằng một UPDATE có điều kiện, 403 cho mọi lý do`                              | —                |
| 9  | 4    | `feat(gd4-d): D4 — hủy, từ chối, hủy kết bạn idempotent`                                                   | —                |
| 10 | 4    | `feat(gd4-d): D5 — danh sách bạn bè và lời mời, keyset, một lô IUserDirectory`                             | L13              |
| 11 | 4    | `feat(gd4-d): D6 — theo dõi ON CONFLICT DO NOTHING, chỉ xóa cache người theo dõi`                           | —                |
| 12 | 4    | `test(gd4-b): B3 — FRD/FOL trên Postgres thật, race A↔B không 500, nửa bảng đột biến quan hệ`               | L7               |
| 13 | 4    | `test(gd4-b): B5 — cổng hợp đồng canh socialgraph-v1, đã thử đỏ ba kiểu`                                    | —                |
| 14 | 5    | `feat(gd4-c): C2 — feed một LATERAL mỗi nguồn trên idx_posts_author_created, BR-02 trong truy vấn`          | — (EXPLAIN trong thân) |
| 15 | 5    | `refactor(gd4-c): C3 — PostHydrator dùng chung cho trang cá nhân và feed`                                  | —                |
| 16 | 5    | `feat(gd4-c): C4 — FeedService, cache trang đầu chỉ lưu id kèm dấu nguồn, timeout 5s thành 503`            | L10, L14, Q-C1, Q-B4 |
| 17 | 5    | `feat(gd4-d): D7 — GET /feed, content-v1 1.0.0-gd4 chỉ-thêm, rà RFC 7807 hai nhóm`                          | —                |
| 18 | 5    | `test(gd4-b): B4 — FEED-01..13 theo ma trận quyền, cache chỉ lưu id, FEED-Q1 hằng số truy vấn`              | L3, L5, L9, L11, L15 |
| 19 | 6    | `docs(gd4-c): C6 — kịch bản k6 và báo cáo sơ bộ ba lượt`                                                    | —                |

`C3` là `refactor` chỉ khi không đổi hành vi quan sát được — số câu truy vấn của `/users/{id}/posts` giữ nguyên là điều kiện.
`C6` là `docs` nếu chỉ thêm `feed.js` + báo cáo; có sửa code theo `EXPLAIN` thì sửa đó là commit `fix(gd4-c)` riêng, trước báo cáo.

---

## 17. Checklist nghiệm thu B + C + D

Tick từng dòng, có bằng chứng. Dòng không áp dụng thì ghi lý do, **không xóa dòng**.

### 17.1 Hạ tầng test và cổng

- [x] Mặc định của `ModulesApiFactory` và `FakeObjectStorage` không đổi — test GĐ1–GĐ2 xanh, không sửa khẳng định
- [x] `SqlCommandCounterTests` chứng minh bộ đếm ra > 0
- [x] Giờ bộ integration trước/sau trong commit #1; < ~3 phút hoặc đã tách collection
- [x] `AuthZMatrix.cs` 23 dòng; commit #2 chỉ chạm file đó; link CI đỏ đã lưu (24/24 sau `D3`)
- [x] `SocialGraphContractTests` trong `--list-tests --filter Category=Contract`; ba kiểu thử đỏ đã làm
- [ ] `pnpm gen:api` → worktree sạch; không sửa `ci.yml`; CI xanh cả năm nhóm trên commit cuối

### 17.2 Endpoint

- [x] Bảy endpoint quan hệ + `GET /feed` hiện trên Swagger đúng nhóm
- [x] `content-v1.yaml`: chỉ thêm, `1.0.0-gd4`, `schema.d.ts` cùng commit `D7`
- [x] Bảng rà mã lỗi hai nhóm (`D7` Bước 4) trong thân commit #17
- [ ] 403 của accept không phân biệt lý do; 503 có `Retry-After: 5`

### 17.3 Feed và hiệu năng

- [x] `EXPLAIN` LATERAL + gợi ý trên bộ dữ liệu tải dán vào "Thực tế thi công", đúng hình dạng Đ-4.7
- [x] `EXPLAIN` trước/sau Đ-4.11 trong thân commit #14
- [ ] `FEED-01..13`, `FEED-07b`, `FEED-09b`, `PAGE-04`, `FEED-Q1` xanh; ca cache khẳng định trúng cache trước
- [ ] Giá trị thô `feed:p1:*` đúng bốn trường; môi trường đo: `redis-cli --scan --pattern 'feed:*'` + `GET` vài khóa không
      thấy `X-Amz-Signature` (Mục 12 gốc)
- [ ] Báo cáo k6 ba lượt ở `docs/giai-doan-4/bao-cao-k6-so-bo.md`; lượt (3) lỗi < 1%

### 17.4 Bảng đột biến

Mỗi dòng: sửa tạm → chạy lọc → thấy **đúng** test dự kiến đỏ → hoàn tác → `git status` sạch.

| Đột biến                                                                | Test phải đỏ                                         | Bước | Bắt loại hỏng nào                         |
| ----------------------------------------------------------------------- | ---------------------------------------------------- | ---- | ----------------------------------------- |
| Bỏ vế `RequesterId == userId` trong câu chấp nhận                        | `TC-A03-friend-self-accept` — **chỉ** dòng đó        | 4    | Tự biến lời mời của mình thành tình bạn    |
| Đổi 403 của accept thành 404                                             | `TC-A03-friend-accept`, `FRD-10`                     | 4    | Trộn quy ước 3b                            |
| Khôi phục `AlwaysStrangers` ở `AddContentModule`                         | Test khởi động · `READ-06b` · `FEED-04`              | 4, 5 | DI-01                                     |
| `FriendshipReader.AreFriendsAsync` luôn `true`                           | `READ-06` · `READ_02_05(friends, false)`             | 4    | Đối chứng L8                              |
| Không bắt `23505`                                                        | `FRD-02`, `FRD-06` (500)                             | 4    | Race A↔B thành 500                         |
| Bỏ kiểm "khác mình" trước DB ở `D2` / `D6`                               | `FRD-03` / `FOL-03` (500 từ CHECK)                   | 4    | CHECK làm việc của validator               |
| Bỏ `ON CONFLICT DO NOTHING`                                              | `FOL-02`                                             | 4    | Idempotent bị phá                          |
| `DELETE /friends/{id}` thiếu vế `Status == Accepted`                     | ca D4 "hủy kết bạn không đụng lời mời đang chờ"      | 4    | Hai endpoint xóa nhầm nhau                 |
| Không gọi `InvalidateAsync` sau chấp nhận (thêm 2026-09-23)              | ca D3 `Chap_nhan_xoa_cache_nguon_…` — **chỉ** ca đó  | 4    | Kết bạn xong, nguồn feed rỗng cũ sống 60s  |
| Không gọi `InvalidateAsync` sau hủy kết bạn                              | `FEED-09`                                            | 5    | Nguồn cũ 60s                               |
| Xóa cache nguồn **trước** `COMMIT`                                       | `FEED-09` — có thể không tái hiện ổn định            | 5    | Đ-4.15; B.9 tự rà mục 2 canh               |
| Bỏ kiểm lại BR-02 (bước 6 của `FeedService`)                             | `FEED-09b`                                           | 5    | Lộ bài vừa đổi sang `friends`               |
| Bỏ so dấu nguồn (Q-C1)                                                   | `FEED-07b`                                           | 5    | Kết bạn xong trang chủ vẫn là gợi ý 30s    |
| Cache trang đầu lưu `PostResponse`                                       | `FEED-10` · `FEED-13`                                | 5    | CACHE-01                                   |
| Bỏ `status = 'published'` trong LATERAL                                  | `FEED-06`                                            | 5    | BR-07                                      |
| Bỏ nhánh `lvl = 3`                                                       | `FEED-05` vế "của mình"                              | 5    | Đ-4.5                                      |
| Điều kiện `suggested` thành `Friends.Count == 0`                         | `FEED-03` (vế `mode`)                                | 5    | Đ-4.6 — chỉ theo dõi vẫn là mạng lưới      |
| Hydrate tác giả từng bài (`foreach` gọi `IUserDirectory`)                | `FEED-Q1`                                            | 5    | N+1                                       |
| Trượt cache vẫn nạp lại bài theo PK (bỏ L14)                              | `FEED-Q1` (6 ≠ 5)                                    | 5    | Câu thừa trên đường nóng                   |
| Không khôi phục `CommandTimeout` sau truy vấn feed                       | — không test nào; **ghi nhận**, B.9 tự rà            | 5    | Đối chứng có chủ đích: không phải lưới vạn năng |

### 17.5 Luật repo

- [ ] Mười lăm chỗ lệch L1–L15 và Q-B4, Q-C1, Q-C2 đã ghi ngược vào `giai-doan-4.md`, mỗi cái ở commit của nó
- [ ] Năm mục tự rà B.9 đã chạy trước khi mở PR, kết quả vào "Thực tế thi công"
- [ ] Không kỳ vọng test nào đọc hằng code sản phẩm; không `Skip` mới
- [ ] Không secret trong diff; khóa JWT đo không ở repo/`deploy/.env`; `users.csv` không commit
- [ ] Mọi commit có `Test:` và `detect-changes:`; footer sạch bút ký

---

## 18. Ba khối để lại gì

| Di sản                                                           | Ai thừa hưởng ngay            | Ai thừa hưởng về sau                                                    |
| ---------------------------------------------------------------- | ----------------------------- | ----------------------------------------------------------------------- |
| `PostHydrator.HydrateAsync(IReadOnlyList<Post>, Guid actorId, CancellationToken)` | Feed, trang cá nhân | **GĐ3** — thêm một lô `myReaction` vào đúng hàm này                     |
| `FeedService` + `IFeedPageCache` (id + dấu nguồn)                | `D7`                          | GĐ5/GĐ6 chép khuôn "cache chỉ lưu id, hydrate lúc trả"                  |
| `IFeedSourceCache.InvalidateAsync`, event sau `COMMIT`           | `D2`–`D6`                     | GĐ6 nối notification vào `SocialGraphEvents`                            |
| `ModulesApiFactory.UseRedis`, `DistinctGetUrls`, `SqlCommandCounter` | `B4`                      | GĐ3 chạy lại `FEED-Q1` khi thêm `myReaction` (hằng số tăng đúng 1)       |
| `MakeFriendsAsync`, `FollowOkAsync`                              | `B4`                          | GĐ3 `READ-CMT-*`/`READ-REACT-*` với bạn thật; GĐ5 BR-09                 |
| Sáu dòng matrix + bảng đột biến                                  | `F3`                          | GĐ5 `TC-A07` chép khuôn `READ-06b`                                      |
| Môi trường đo + seed + `feed.js` + báo cáo sơ bộ                 | `F4`                          | GĐ8 chạy lại **chính thức**, so với mốc GĐ4                             |

---

## 19. Ranh giới — cái gì **không** thuộc ba khối

| Không thuộc                                                          | Thuộc về                       | Vì sao dễ nhầm                                        |
| -------------------------------------------------------------------- | ------------------------------ | ----------------------------------------------------- |
| Migrate `socialgraph` trong harness, test khởi động `IFriendshipReader`, canh gác namespace | Khối A (đã xong)   | B.4 và Mục 10.5 còn nhắc                              |
| Client `socialgraph-api.ts`, ngữ cảnh `errorMessage`, fixture `msw`  | `E1`                           | `schema.d.ts` sinh ở `D7`/cổng mở                      |
| Nút quan hệ, màn `/friends`, `FeedList`, slot ở `app/`               | `E2`–`E5`                      | Hình dạng `RelationshipResponse`, `FeedPage` do `D` chốt |
| Vitest, Playwright                                                   | `E6`                           | Mục 10.6 cùng mục chiến lược test                     |
| Deploy staging, `migrate` bốn module trên server                     | `F1`                           | `D0` sửa `Program.cs`                                  |
| E2E hai tài khoản trên staging                                       | `F2`                           | Q-C1 tồn tại vì lát cắt đó                             |
| Đóng băng `socialgraph-v1` + `/feed`                                 | `F4`                           | `D7` là lần cuối yaml đổi trong GĐ4                    |
| Sửa `ci.yml`                                                         | **Không ai**                   | Cổng lọc theo trait — thêm lớp là đủ                   |
| Seeder ứng dụng                                                      | **Không ai** (Mục 5 gốc)       | `seed.sql` của `C5` chỉ cho môi trường đo              |

---

## Thực tế thi công

### B1 — 2026-09-22

- Impact trước sửa: `ModulesApiFactory` risk UNKNOWN (xUnit fixture, không có cạnh gọi) — xác nhận bằng text search mọi
  lớp `IClassFixture<ModulesApiFactory>` giữ mặc định Redis cổng 1. `CreatePresignedGet` / `FakeObjectStorage` risk
  **CRITICAL** (Avatar/UpsertProfile so nguyên chuỗi URL) — giữ `DistinctGetUrls` tắt mặc định; không sửa khẳng định.
- Tag Activity Npgsql 8 trên máy thật: nguồn `"Npgsql"`, tag `db.name` + `db.statement` — `SqlCommandCounterTests` đếm
  > 0 và thấy câu chứa `content.posts` khi gọi `GET /users/{id}/posts`. Không cần fallback `DiagnosticListener`.
- Bộ integration `Category!=AuthZ&Category!=Contract` (lần hai ấm): **trước 1 m 24 s (314 pass + 1 đỏ R2 nền) → sau
  1 m 21 s (316 pass + 1 đỏ R2 nền)**. Dưới ~3 phút — chưa tách collection.
- Đỏ nền đã biết: `StartupConfigurationTests.Development_boots_without_r2_…` vì user-secrets có khóa R2 (CI xanh) —
  không sửa.
- Lệch L1/L2/L4: nhắc lại trong thân commit — migrate socialgraph đã ở A3; `DistinctGetUrls`; đếm bằng ActivitySource.

### B2 — 2026-09-22 (local trước push)

- `AuthZMatrix.cs` 23 dòng / 24 test. Chạy `Category=AuthZ`: **21 pass / 3 fail**.
  - Đỏ đúng lý do: `TC-A03-friend-accept`, `TC-A03-friend-self-accept`, `READ-06b` — đều
    `ArrangePath hỏng — POST /friends/requests … 404`.
  - Xanh: `READ-06` (L8); `TC-A01-feed`, `TC-A01-friends` (401 anti-enumeration trên route chưa khớp — lệch bảng
    hướng dẫn cũ giả định 404).
- Link CI run đỏ: https://github.com/ricecracker12/30INF067_btl/actions/runs/35695741504
  (`build-test` / AuthZ matrix GATE — 3 fail: `READ-06b`, `TC-A03-friend-accept`, `TC-A03-friend-self-accept`,
  đều `ArrangePath hỏng — POST /friends/requests … 404`).

### C5 — 2026-09-22

- `tests/load/feed/`: compose `socialapp-perf`, seed, README, `.env.example`. API `cpus: 2` / `mem_limit: 12g`.
- Migrate bốn schema OK; seed **~28 s** trên Docker Desktop WSL2 (~7,6 GB RAM host).
- Đếm: profiles 10.000 · posts 1.000.000 · friendships 600.000 (avg bậc 120) · follows 200.000 (avg 20).
- Privacy 70/20/10 · hidden ≈ 10.048. Hai chốt chặn đã thử đỏ.
- Lệch lúc thi công: LATERAL `random()` không tham chiếu `gs` → một giá trị cho cả bảng; sửa bằng `SELECT gs AS _row,
  random()…`. README dùng `docker exec`/`docker cp` vì host Windows thường không có `psql`.
- `users.csv` xuất local (gitignore). `PERF_JWT_KEY` chỉ trong `tests/load/feed/.env`.
- **Sửa 2026-09-23 (rà bước 2–4):** bản đầu theo dõi offset `1..20` — nằm trọn trong vùng bạn `1..50`/`1..250`, nên
  mọi dòng `follows` trùng một bạn và `FeedSourceReader` loại hết khỏi `FollowingOnly`: nhánh "bài `public` của người
  chỉ theo dõi" (Đ-4.5) không bao giờ vào `EXPLAIN` của `C2` hay k6 của `C6`. Đổi sang `251..270`, seed lại (**~42 s**):
  theo dõi trùng bạn **0** (trước 200.000/200.000), 10.000/10.000 người có `FollowingOnly` khác rỗng. README thêm câu
  đối chiếu. `users.csv` xuất lại vì id sinh mới.

### D0 — 2026-09-22

- Impact trước sửa: repo `30INF067_btl`, index 6 commit sau HEAD. `AddSocialGraphModule` risk UNKNOWN
  (`receiverTyping: 6`) — grep xác nhận đúng 6 chỗ gọi, cùng chữ ký `(cs)`: `Program.cs`, `ModulesApiFactory`,
  `PostgresFixture`, `SocialGraphDbContextSchemaTests`, `FriendshipReaderTests`, `FeedSourceReaderTests`. Thêm
  đăng ký DI, không đổi chữ ký; không HIGH/CRITICAL.
- Thử đỏ `SocialGraphPermissionsTests`: đổi `FriendRequest` thành `"friend.reques"` → đỏ đúng thông điệp
  `…không có trong PermissionCodes…: friend.reques` → khôi phục, 2/2 xanh.
- `GET /swagger/socialgraph-v1/swagger.json` 200 (`SocialGraphHarnessTests`, `ApiFactory`, `paths` còn rỗng).
- `PresentationBoundaryTests` + `ModuleBoundaryTests` + `PersistenceBoundaryTests` + `PermissionCodeUsageTests` xanh
  (14). `FriendshipReaderTests` + schema tests xanh sau thêm validator/`TimeProvider`/store/service.
- Lệch thứ tự kế hoạch (C1 đã lên trước): `RelationshipService` nhận `IFeedSourceCache` ngay trong commit này —
  C1 đã đăng ký DI, không đăng ký bản rỗng (`AlwaysStrangers`).
- `SelfRelationship`: yaml `GET /relationships` 400 không có `example` riêng — câu "Không thể xem quan hệ với chính
  mình." đặt ở D0, cùng key `userId`.

### C1 — 2026-09-22

- Repo `30INF067_btl`, index `7700827` = HEAD. `FeedSourceReader` / `AddSocialGraphModule` / `RelationshipService`
  risk UNKNOWN (DI). `IFeedSourceReader` impact HIGH vì 16 import namespace `SharedKernel.Contracts` — **không đổi
  chữ ký** interface; grep `IFeedSourceReader` trong `src/`: chỉ đăng ký DI + chính file hiện thực. Không HIGH/CRITICAL
  trên symbol đang sửa.
- `AddSocialGraphModule` `receiverTyping: 6` — grep đúng 6 chỗ gọi, cùng chữ ký `(cs)`. Thêm options + `IFeedSourceCache`,
  không đổi chữ ký. `FeedSourceReaderTests` thêm Redis cổng 1 (L12) + `AddLogging` vì reader giờ inject `ILogger`.
- `ServiceCollection` trần không chạy `RedisConnectionStarter` — `FeedSourceCacheTests` `await GetAsync()` trước ca đầu.

### D1 — 2026-09-22

- Repo `30INF067_btl`. Impact trước sửa: `RelationshipService` / `SocialGraphErrors` / `RelationshipState` risk
  UNKNOWN — grep: service chưa có caller (chỉ DI), `RelationshipState.Of` chỉ unit test, `SocialGraphErrors` chưa ai
  gọi. `IRelationshipStore` LOW (d=1: `RelationshipStore` + DI). `ModulesTestClient` **CRITICAL** (96 caller) —
  chỉ thêm `GetRelationshipAsync` / `ExecuteSqlAsync`, không đổi chữ ký cũ. Không HIGH/CRITICAL trên symbol đang
  đổi hành vi.
- `RelationshipTests` **13/13** + `SocialGraphHarnessTests` 1/1. Tám ca 4×2 đọc JSON thô (`friendship` là
  string, không số). Chính mình → 400 `errors.userId` câu D0. Guid mới không hồ sơ → 200 `none`/`false`. Id sai
  dạng → 400; ẩn danh → 401.
- `PresentationBoundaryTests` + `PersistenceBoundaryTests` + `ModuleBoundaryTests` + `PermissionCodeUsageTests`
  + `SocialGraphPermissionsTests`: **14/14**.

### D2 — 2026-09-22

- Repo `30INF067_btl`. Impact trước sửa: `RelationshipService` UNKNOWN (grep: `RelationshipsController` + DI);
  `IRelationshipStore` LOW. Không HIGH/CRITICAL. Thêm `SendRequestAsync` / `AddRequestAsync`, không đổi `GetAsync`.
- 23505 bắt ở **store** (nếp `PostStore`, `PersistenceBoundaryTests`), không ở service — lệch mô tả rút gọn Mục 7 D2,
  khớp `SocialGraphErrors.RelationshipExists`. Tên PK kiểm trong migration `InitialSocialGraph`: `PK_friendships`.
- `SendFriendRequestTests` **11/11** + `TC-A01-friends` xanh (401 khi có route). Tự gửi 400 không dòng; không hồ sơ
  404; cùng cặp / chiều ngược / đã là bạn → 409 đúng một dòng. `TC-A03-friend-*` / `READ-06b` vẫn đỏ chờ D3.

### D3 — 2026-09-22

- Repo `30INF067_btl`. Impact trước sửa: `RelationshipService` UNKNOWN (grep: `FriendsController.SendRequest` + DI);
  `IRelationshipStore` LOW (d=1: `RelationshipStore`); `FriendRequestAccepted` UNKNOWN (chưa có caller). Không
  HIGH/CRITICAL trên symbol đổi hành vi. `ModulesTestClient` CRITICAL (111 caller) — chỉ thêm `AcceptAsync` /
  `AcceptOkAsync` / `MakeFriendsAsync`, không đổi chữ ký cũ.
- `ExecuteUpdateAsync` nằm ở **store** (cùng lệch D2: Application không chạm EF). Vế `RequesterId == userId` giữ
  nguyên — `TC-A03-friend-self-accept` canh. `accepted_at` + `updated_at` gán cùng câu vì `Execute*` bỏ qua
  `SaveChanges`.
- Không tra hồ sơ: yaml không có 404; `TC-A03-friend-accept` không xanh vì lý do sai. `userId` chính mình → 400
  `errors.userId` trước `FriendPair.Of` (tránh 500). 0 dòng → 403 cùng câu yaml, không nêu lý do.
- `AcceptFriendRequestTests` **12/12**. `SendFriendRequestTests` 11/11 không đổi. AuthZ **24/24** — ba dòng đỏ
  của B2 (`TC-A03-friend-accept`, `TC-A03-friend-self-accept`, `READ-06b`) xanh; `TC-A01-feed` đã xanh từ B2
  (401 anti-enumeration) nên matrix đủ sớm hơn kế hoạch "D3 + D7". Architecture 14/14.
  `FriendshipReaderTests` + `SocialGraphHarnessTests` 6/6.

### D4 — 2026-09-22

- Repo `30INF067_btl`, index 1 commit sau HEAD. Impact trước sửa: `RelationshipService` / `SocialGraphErrors` UNKNOWN
  (grep: controller + DI, thêm phương thức); `IRelationshipStore` LOW (d=1: `RelationshipStore`);
  `FriendsController` / `RelationshipStore` LOW. `ModulesTestClient` CRITICAL (111 caller) — chỉ thêm
  `DeclineOrCancelAsync` / `DeclineOrCancelOkAsync` / `UnfriendAsync` / `UnfriendOkAsync`, không đổi chữ ký cũ.
- `ExecuteDeleteAsync` nằm ở **store**, kèm vế `Status == Pending` / `Status == Accepted`. 0 dòng vẫn 204;
  `changed > 0` mới `InvalidateAsync` cả hai phía, sau COMMIT. Không event — Đ-4.15 chỉ `FriendRequestSent` /
  `FriendRequestAccepted`. Không tra hồ sơ: yaml không có 404. Chính mình → 400 `errors.userId` trước
  `FriendPair.Of` (yaml không có example riêng, cùng nếp `SelfAccept`).
- `DeleteFriendshipTests` **13/13** trên Redis thật: hủy và từ chối đều xóa `pending`; `DELETE /friends` không đụng
  lời mời đang chờ; `DELETE /friends/requests` không đụng quan hệ `accepted`; dòng `follows` còn sau hủy kết bạn;
  khóa `sg:feed-sources` chỉ mất khi có dòng bị xóa. `SendFriendRequestTests` + `AcceptFriendRequestTests` +
  `SocialGraphHarnessTests` **24/24** không đổi. Ba lớp ranh giới kiến trúc **10/10**. `FRD-07..09` chờ commit `B3`.

### D5 — 2026-09-22

- Repo `30INF067_btl`, index 1 commit sau HEAD. Impact trước sửa: `RelationshipService` UNKNOWN (grep: không có
  `new`, chỉ DI + controller); `IRelationshipStore` LOW; `FriendsController` LOW. `ModulesTestClient` CRITICAL —
  chỉ thêm `ListFriendsAsync` / `ListFriendsOkAsync` / `ListRequestsAsync` / `ListRequestsOkAsync`.
- `FriendCursor` chép `PostCursor` trong SocialGraph (L13), không import Content. `direction` bind `string?`.
  `nextCursor` từ dòng thứ `limit` của danh sách gốc, trước khi bỏ thẻ mất hồ sơ. Một `GetManyAsync` cho cửa sổ
  đó; avatar ký bằng `CreatePresignedGet`.
- `ListFriendsTests` **14/14**. `FriendCursorTests` **11/11**. Ba lớp ranh giới kiến trúc **10/10**.
  `?direction=` (chuỗi rỗng) model binding thành null nên là incoming — không phải 400.

### D6 — 2026-09-22

- Repo `30INF067_btl`, index 1 commit sau HEAD. Impact trước sửa: `RelationshipService` UNKNOWN (grep: không có
  `new`, chỉ DI + `FriendsController` / `RelationshipsController`); `IRelationshipStore` LOW (d=1: `RelationshipStore`);
  `RelationshipStore` LOW. `ModulesTestClient` CRITICAL (111 caller) — chỉ thêm `FollowAsync` / `FollowOkAsync` /
  `UnfollowAsync`, không đổi chữ ký cũ.
- `INSERT … ON CONFLICT DO NOTHING` nằm ở **store** (cùng lệch D2: Application không chạm EF). `inserted == 1` mới
  `InvalidateAsync(actorId)` — không truyền người được theo dõi. `DELETE` cùng một phía, chỉ khi có dòng.
  Không event. Không tra hồ sơ trên `DELETE`: yaml không có 404. Chính mình trên `PUT` → 400 trước DB; trên `DELETE`
  vẫn 204 vì không có dòng (CHECK chỉ chặn lúc tạo) và yaml không có example tự-bỏ-theo-dõi.
- `FollowWriteTests` **11/11** trên Redis thật. `FOL-*` nằm ở commit `B3` (Mục 8).
  Bốn lớp ranh giới (`Presentation` / `Persistence` / `Module` / `PermissionCodeUsage`) **12/12**.

### B3 — 2026-09-22

- Repo `30INF067_btl`. Chỉ thêm hai lớp test, không đổi hành vi sản phẩm. Impact trước các lần sửa tạm:
  `AreFriendsAsync` / `AddRequestAsync` LOW; `AddContentModule` UNKNOWN (DI, `receiverTyping: 4` — grep `Program.cs`
  và harness). `AcceptIncomingAsync` / `DeleteAcceptedAsync` chưa có trong index (1 commit sau HEAD) — caller là
  `RelationshipService`. Không HIGH/CRITICAL. Mọi đột biến đã hoàn tác; `ContentModuleExtensions.cs` và
  `FriendshipReader.cs` không còn diff.
- `FriendRequestTests` + `FollowTests` **14/14** sau khi hoàn tác. `FRD-06`: mười cặp, mỗi cặp `WhenAll` hai chiều,
  mỗi cặp một 201 + một 409 + một dòng, không 500. `FRD-09` nhìn qua `GET /posts/{id}` (200 rồi 404), không qua DI.

Nửa quan hệ của Mục 17.4 (bước 4), mỗi dòng sửa tạm → lọc → hoàn tác:

| Đột biến | Kết quả |
| --- | --- |
| Bỏ `RequesterId == userId` khi chấp nhận | Đỏ đúng `TC-A03-friend-self-accept` (200 thay vì 403). `TC-A03-friend-accept`, `FRD-05`, `FRD-10` vẫn xanh. Ca hình dạng `Tu_chap_nhan_loi_minh_gui` cũng đỏ — cùng luật, không phải dòng matrix khác. |
| Đổi 403 của accept thành 404 | Đỏ `TC-A03-friend-accept` (nhận 404), `FRD-10` (NotFound). `FRD-05` vẫn xanh. Cùng đường 0 dòng nên `TC-A03-friend-self-accept` và `Khong_co_loi_moi` cũng đỏ. |
| Khôi phục `AlwaysStrangers` trong `AddContentModule` | Test khởi động đỏ: hai đăng ký `[AlwaysStrangers, FriendshipReader]`. `READ-06b` **vẫn xanh** — `AddSocialGraphModule` đăng ký sau nên cái sau thắng. `FEED-04` chưa có (bước 5). |
| `AreFriendsAsync` luôn `true` | Đỏ đúng `READ-06` (200 thay vì 404) và `READ_02_05(friends, false)`. Năm ca còn lại của ma trận BR-02 và `READ-06b` vẫn xanh. |
| Không bắt `23505` | Đỏ `FRD-02` (500 thay vì 409) và `FRD-06` (`[201, 500]` thay vì `[201, 409]`). |
| Bỏ kiểm "khác mình" trước DB ở D2 và D6 | Đỏ `FRD-03` và `FOL-03`, nhưng là **404** chứ không phải 500 từ CHECK: người gọi chưa có hồ sơ nên bước tra hồ sơ (ngay sau) trả 404 trước khi tới `FriendPair` / `ck_follows_not_self`. Lưới vẫn bắt (không còn 400). **Sửa 2026-09-23:** hai ca tạo hồ sơ cho A; thử lại → cả hai đỏ **500** (`FriendPair.Of` / CHECK), đúng cột dự kiến. |
| Bỏ `ON CONFLICT DO NOTHING` | Đỏ `FOL-02` (500 thay vì 204). `FOL-01` vẫn xanh. |
| `DELETE /friends` thiếu `Status == Accepted` | Đỏ `Huy_ket_ban_khi_chi_co_loi_moi_khong_xoa_loi_moi` (dòng pending mất). `Huy_ket_ban_tra_204` và `FRD-09` vẫn xanh. |

### B5 — 2026-09-22

- Repo `30INF067_btl`, index 1 commit sau HEAD. Không sửa hành vi sản phẩm. Impact trước lần gắn
  `[ProducesResponseType(418)]` tạm lên `FriendsController.SendRequest`: risk UNKNOWN (MVC gọi action, không có cạnh
  gọi) — grep chỉ thấy đúng định nghĩa. Không HIGH/CRITICAL. Đã hoàn tác; controller không còn 418.
- `--list-tests --filter Category=Contract`: **6 → 8**, tăng đúng 2 `[Fact]` của `ContractTestsBase`. Sau khôi phục:
  **8/8** xanh, có cả hai fact của `SocialGraphContractTests`.
- Thử đỏ rồi khôi phục:
  - 418 trên `POST /friends/requests` → đỏ `Runtime_must_not_expose_anything_outside_the_contract`, thông điệp
    `POST /friends/requests: 418`.
  - Comment `Content Include` của `socialgraph-v1.yaml` (và xóa bản copy cũ trong output — `PreserveNewest` không gỡ
    file đã chép) → đỏ cả hai fact, câu `Không thấy file hợp đồng ở …\Contracts\socialgraph-v1.yaml`.
  - Gỡ `[Trait("Category","Contract")]` → `--list-tests` mất `SocialGraphContractTests` trong khi cổng
    `Category=Contract` + `TreatNoTestsAsError` vẫn xanh **6/6**. `TreatNoTestsAsError` không bịt được lỗ này: còn lớp
    khác mang trait thì cổng không thấy lớp bị quên.
- `SocialGraphPermissionsTests` đã thử đỏ ở D0 — không thử lại.
- `pnpm gen:api` sinh lại bốn yaml, `git status --porcelain -- src/frontend` rỗng. Không sửa `ci.yml`.

### Rà bước 2–4 — 2026-09-23

Tự rà thay review chéo (một người làm), trên `e120090..a062107`. Mỗi chỗ sửa một commit riêng:

- `.gitignore` bị ghi bằng codepage ANSI ở `C5` — chú thích tiếng Việt thành `?`. Khôi phục UTF-8 từ `e120090`.
- Seed: theo dõi trùng hết bạn → `FollowingOnly` rỗng. Chi tiết ở mục `C5` phía trên.
- Sáu chỗ gọi `InvalidateAsync` nhận token của request: client ngắt sau `COMMIT` là bỏ xóa cache. Đổi sang
  `CancellationToken.None`; `RelationshipServicePostCommitTests` 6 ca, trả service về bản cũ → 6/6 đỏ.
- **Hai chỗ trong thân commit đã push — đã sửa bằng `filter-branch` ngày 2026-09-23**, trước khi nhánh `loveart1210`
  merge vào `develop` (msg-filter khóa theo sha gốc, nội dung cây không đổi; `refs/original/` giữ bản cũ):
  1. `D1` `ca2a9d5`, `D2` `a7a4aff`, `D3` `ff5ab81` mang trailer ghi công công cụ — trái `commit-rules.md` Mục 6.
     Đã gỡ; `git log e120090..HEAD --format=%B | grep -c "^Co-authored-by:"` ra **0**. (Đừng kiểm bằng
     `grep -i co-authored`: nó khớp cả đoạn văn xuôi của chính commit này và cho báo động giả.)
  2. Dòng `Test:` của `D4` `eb2d9a5`, `D5` `297a063`, `D6` `5c43188`, `B3` `64cebec` chỉ ghi số của bộ lọc, thiếu tổng
     trước → sau (Mục 5.5). Số đếm lại bằng `--list-tests` ở từng commit rồi ghi vào thân commit:
     `D4` Integration **390 → 403**; `D5` Integration **403 → 417**, Unit **227 → 238**; `D6` Integration
     **417 → 428**; `B3` Integration **428 → 442**. Mốc `D3` (390) khớp dòng `Test:` sẵn có của `ff5ab81` nên cách
     đếm này tin được.
- Không ca nào canh `D3` xóa cache nguồn — bỏ dòng `InvalidateAsync` trong `AcceptRequestAsync` thì `FRD-*`, AuthZ và
  cả lớp `AcceptFriendRequestTests` vẫn xanh. Thêm `Chap_nhan_xoa_cache_nguon_ca_hai_phia_0_dong_thi_khong` (lớp chuyển
  sang Redis thật, khuôn `DeleteFriendshipTests`) + một dòng Mục 17.4. Thử đỏ: bỏ dòng đó → **chỉ** ca mới đỏ (57 ca
  lọc `AcceptFriendRequestTests|FriendRequestTests|Category=AuthZ` còn lại xanh).

### C2 — 2026-09-23

- Impact trước sửa: `ListByAuthorAsync` LOW (d=1 `PostReadService.ListByUserAsync` → `PostsController.ListByUser`);
  `AddContentModule` UNKNOWN (`receiverTyping: 4`) — grep đúng 4 chỗ gọi cùng chữ ký `(cs)`: `Program.cs`,
  `ModulesApiFactory`, `PostgresFixture`, `ContentDbContextSchemaTests`. Chỉ thêm một đăng ký. Không HIGH/CRITICAL.
- SQL của Đ-4.7 **nguyên văn** (kể cả CTE) qua `FromSql`: global query filter bọc nó thành subquery và Npgsql cho bọc câu
  mở bằng `WITH` (đã thử — ghi lại vì tưởng phải đổi CTE thành subquery). Literal `'published'`/`'public'`/`'friends'`
  viết thẳng: nội suy là tham số, planner không chứng minh được điều kiện index một phần từ tham số.
- Lệch thứ tự Phần IV: `FEED-01..08` **chưa** viết trước `C2` — chúng cần `FeedPage` và route (`D7`) mới compile. Lưới
  trong lúc chờ là `FeedStoreTests` (đối chiếu SQL ↔ `FeedVisibility` trên 36 bài 4 tác giả × 3 mức × 3 trạng thái).
  Thử đỏ: bỏ `p.status = 'published'` trong LATERAL → 3/5 ca đỏ; đã khôi phục.
- `FeedVisibility` có thêm `CanSeeSuggested` — hướng dẫn chỉ ghi `CanSee(…, sources)`, mà ở chế độ gợi ý nguồn rỗng theo
  định nghĩa: kiểm lại (C4 bước 6) bằng `CanSee` là loại sạch bài người lạ.
- `EXPLAIN (ANALYZE, BUFFERS)` trên `socialapp_perf` (1M bài), người nhiều bạn nhất: **500 bạn + 20 chỉ theo dõi + chính
  mình = 521 nguồn**. Câu đúng dạng EF gửi (CTE bọc trong `WHERE status <> 'deleted'` + `ORDER BY` ngoài), tham số thay
  bằng hằng — Npgsql không auto-prepare nên mỗi lần chạy là custom plan với giá trị thật. Mảng id rút gọn thành `{…}`.
  Lần hai (ấm):

  (1) LATERAL, trang đầu — **9,8 ms**; lần lạnh đầu tiên 237 ms (1.532 buffer đọc đĩa):

  ```
  Subquery Scan on p (actual time=9.771..9.777 rows=21 loops=1)
    Filter: ((p.status)::text <> 'deleted'::text)
    ->  Limit (rows=21)
          ->  Sort (rows=21)   Sort Key: created_at DESC, post_id DESC   Sort Method: top-N heapsort  Memory: 29kB
                ->  Nested Loop (actual time=0.024..7.560 rows=10941 loops=1)
                      Buffers: shared hit=13812
                      ->  Append (rows=521 loops=1)        -- 20 chỉ theo dõi + 500 bạn + 1 chính mình
                      ->  Limit (rows=21 loops=521)
                            ->  Index Scan using idx_posts_author_created on posts p_1 (rows=21 loops=521)
                                  Index Cond: (author_id = (unnest('{…}'::uuid[])))
                                  Filter: (((1) = 3) OR ((privacy)::text = 'public'::text) OR (((1) = 2) AND ((privacy)::text = 'friends'::text)))
  Execution Time: 9.830 ms
  ```

  (2) LATERAL, trang có cursor — **15,5 ms** (lần một 9,1 ms), cursor thành `Index Cond` dạng `ROW(created_at, post_id) < ROW(…)`:

  ```
  ->  Index Scan using idx_posts_author_created on posts p_1 (rows=21 loops=521)
        Index Cond: ((author_id = (unnest('{…}'::uuid[]))) AND (ROW(created_at, post_id) < ROW('2026-09-22 14:48:51.55545+00'::timestamptz, 'c8e2a996-…'::uuid)))
  ```

  (3) Gợi ý, trang đầu — **0,08 ms**; (4) có cursor — **0,09 ms**; không `Sort`:

  ```
  Subquery Scan on p (rows=21)
    ->  Limit (rows=21)
          ->  Index Scan using idx_posts_public_recent on posts p_1 (rows=21 loops=1)
                Index Cond: (ROW(created_at, post_id) < ROW(…))          -- chỉ ở (4)
                Filter: (author_id <> '01ae50c7-…'::uuid)
                Buffers: shared hit=24
  ```

  Đúng hình dạng Đ-4.7: `Sort` duy nhất là top-N trên 10.941 = 521 × 21 dòng (`số nguồn × take`), không trên toàn bộ bài;
  EF bọc ngoài không thêm `Sort` thứ hai. Chi phí tăng theo số nguồn (521 lần dò index) — đúng "giới hạn đã biết" của Đ-4.7.
- Đ-4.11, cùng máy, tác giả 100 bài, người đọc là người lạ: **trước** `Parallel Seq Scan on posts` + `Sort` (29,3 ms,
  16.697 buffer, 333.308 dòng bị lọc mỗi worker); **sau** `Index Scan using idx_posts_author_created` (0,18 ms, 32 buffer).
  Chi tiết trong thân commit.

### C3 — 2026-09-23

- Impact trước sửa: `PostReadService`, `PostResponseMapper` risk UNKNOWN (DI) — grep: `PostReadService` chỉ
  `PostsController` + DI; `PostResponseMapper` chỉ `PostService`, `PostReadService`, DI và `PostResponseMapperTests` (dựng
  bằng `new`, không đổi chữ ký). `ListByUserAsync` LOW (d=1 `PostsController.ListByUser`). Không HIGH/CRITICAL.
- `PostHydrator.HydrateAsync(IReadOnlyList<Post>, Guid actorId, CancellationToken)` — đúng chữ ký Mục 18. Tách nguyên văn
  (ảnh rồi tác giả). `GetAsync` (một bài) giữ nguyên `mapper.ToResponse`: chuyển sang hydrator không đổi số câu nhưng
  cũng không được gì.
- Số câu của `GET /users/{id}/posts` đo bằng `SqlCommandCounter` (test tạm, đã xóa) trước và sau: người lạ **4**
  (quan hệ → bài → ảnh → tác giả), tác giả **3** — hai bản giống hệt từng câu và thứ tự. Đó là điều kiện để `C3` là
  `refactor`.
- Lệch Bước 2: `IPostStore.FindManyPublishedAsync` dời sang `C4` — nó chỉ có người gọi ở đường trúng cache của
  `FeedService`, và `IPostStore` giữ luật "không khai trước thứ chưa có người gọi".

### C4 — 2026-09-23

- Impact trước sửa: `PostService.CreateAsync` / `UpdateAsync` / `DeleteAsync` LOW (d=1 `PostsController`); `IPostStore`
  LOW — grep: một hiện thực (`PostStore`), không fake nào trong test; `AddContentModule` UNKNOWN như C2 (4 chỗ gọi, chỉ thêm
  đăng ký). Không HIGH/CRITICAL.
- `FeedService` đúng bảy bước Mục 7.2 (bản đã sửa L14): trượt cache thì dùng luôn các dòng LATERAL, chỉ đường trúng mới
  `FindManyPublishedAsync` (dời từ C3). Kiểm lại chạy cả khi trượt. Trúng đòi `fp` **và** `mode` khớp — `mode` suy từ nguồn
  nên thừa, giữ để một giá trị cache lạ không trả trang gợi ý cho người đã có kết nối.
- `FeedFingerprint`: SHA-256 cắt 16 byte, base64 (24 ký tự) của hai mảng đã sắp, **gắn nhãn riêng** `f:`/`o:` — bạn
  chuyển sang chỉ theo dõi (hủy kết bạn khi vẫn theo dõi) không đổi hợp hai tập mà vẫn phải đổi dấu.
- `RedisFeedPageCache` serialize qua kiểu `Payload` riêng (bốn trường), không serialize thẳng `CachedFeedPage` — thêm thuộc
  tính vào record Application không được lặng lẽ thêm trường vào Redis. Công tắc kiểm **bên trong** cache (tắt → đọc
  `null`, không ghi); `InvalidateAsync` vẫn xóa khi tắt.
- `PostService` xóa khóa của tác giả sau khi lưu, token `CancellationToken.None` — cùng bài học `53f0370` của
  `RelationshipService`.
- `ContentErrors.FeedUnavailable` đặt `Title` riêng ("Bảng tin đang quá tải"): `ProblemTitles.For(503)` rơi vào nhánh
  `>= 500` → "Đã xảy ra lỗi không mong muốn", sai nghĩa với 503.
- Timeout: `FeedStore` đặt `CommandTimeout` 5s rồi trả lại trong `finally`; `IsTimeout` nhận `NpgsqlException` bọc
  `TimeoutException` và `57014` trần hoặc bọc. Dạng nào thật sự xảy ra — ghi ở `FEED-12` (B4).
- Thử đỏ (đã khôi phục): bỏ xóa khóa sau đăng bài → `FeedPageCacheTests.Dang_sua_xoa_…` đỏ; `next` tính từ danh sách đã
  lọc → `FeedServiceTests.Truot_cache_kiem_lai_va_next_tu_danh_sach_goc` đỏ.

### D7 — 2026-09-23

- Impact trước sửa: `content-v1.yaml` không phải symbol — người dùng là `ContentContractTests` và `pnpm gen:api`.
  `ModulesTestClient` CRITICAL (100+ caller) — chỉ thêm `GetFeedAsync` / `GetFeedOkAsync`, không đổi chữ ký cũ.
  `FeedController`, `FeedQuery` là file mới.
- `content-v1.yaml` chỉ-thêm: `git diff` có đúng **một** dòng `-` (`version: 1.0.0-gd2`). Thêm path `/feed`, response
  `ServiceUnavailable` (header `Retry-After` + `X-Correlation-ID`), schema `FeedMode`, `FeedPage` (mô tả `nextCursor`
  nguyên văn Mục 8.2), tag `feed`, và một đoạn "MỞ LẠI Ở GĐ4" trong comment đầu file. `nextCursor` của ví dụ 200 giải mã
  được thật (đã kiểm).
- `pnpm gen:api` → chỉ `lib/api/content/schema.d.ts` đổi; `pnpm typecheck` xanh. Alias `FeedPage` trong `lib/api/types.ts`
  là việc của `E1` (Mục 19).
- Thử đỏ cổng hợp đồng: bỏ `[ProducesResponseType(503)]` khỏi `FeedController` → `Contract_must_be_fully_implemented`
  đỏ `GET /feed: 503`; đã khôi phục. `TC-A01-feed` vẫn 401 khi route đã có — matrix giữ 24/24.
- `FeedQueryValidator` dùng CÙNG hai câu của `ListUserPostsQueryValidator` — một loại lỗi, một cách nói.
- Bảng rà RFC 7807 hai nhóm (mọi mã trừ 429/500 — mã middleware, cổng hợp đồng đã trừ) nằm trong thân commit D7. Không
  mã nào thiếu test; hai mã của `/feed` (`400 errors.cursor`, `503`) canh bằng `PAGE-04`, `FEED-12` ở commit B4.

*Ghi tiếp khi làm: dạng exception timeout thật (`C4`/`FEED-12`), hằng số `FEED-Q1` so với Mục 7.2, nửa feed của bảng đột
biến (bước 5), năm mục tự rà B.9.*
