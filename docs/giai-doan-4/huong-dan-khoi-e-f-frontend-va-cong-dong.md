# Hướng dẫn thực hiện — Khối E. Lane frontend + Khối F. Cổng đóng (GĐ4)

> Bản triển khai chi tiết của **B.7 Khối E** và **B.8 Khối F** trong [giai-doan-4.md](giai-doan-4.md). Tài liệu gốc
> trả lời *cái gì* và *vì sao*; tài liệu này trả lời *gõ vào file nào, theo thứ tự nào, và nhìn vào đâu để biết đã
> xong thật*.
>
> **Hai khối một file, một luồng tuần tự, không chia lane.** Lý do giống hệt GĐ2
> ([huong-dan-khoi-e-f-frontend-va-cong-dong.md](../giai-doan-2/huong-dan-khoi-e-f-frontend-va-cong-dong.md)): một người
> làm cả giai đoạn (Mục 9), khối A–D đã xong trên `loveart1210` (tới `1a70225`), và ba trong bốn đầu việc của khối F
> (`F2`, `F3`, `F4`) chỉ kiểm được **sau khi** khối E lên staging — tách hai file thì chỗ nối rơi vào khoảng giữa.
>
> **Nguồn sự thật, theo thứ tự ưu tiên khi mâu thuẫn:**
>
> 1. Hai hợp đồng — `src/backend/Modules/SocialGraph/Presentation/socialgraph-v1.yaml` và phần `/feed` của
>    `src/backend/Modules/Content/Presentation/content-v1.yaml` (`1.0.0-gd4`)
> 2. `giai-doan-4.md` — Mục 3 (`Đ-4.5`, `Đ-4.6`, `Đ-4.9`, `Đ-4.10`, `Đ-4.16`), Mục 7 (luồng), Mục 8 (hợp đồng),
>    Mục 10.6 (test FE), Mục 11–12
> 3. `.claude/rules/frontend-rules.md` và mười bảy quyết định `Đ-E1`–`Đ-E17` trong
>    [huong-dan-khoi-e-frontend.md](../giai-doan-1/huong-dan-khoi-e-frontend.md)
> 4. File này
>
> Chỗ nào file này lệch với 1–3 thì **sửa file này**, không sửa ngược. Muốn đổi một `Đ-4.*` hay một `Đ-E*` thì đó là
> **quyết định mới**, có ngày tháng, ghi vào tài liệu gốc **trong cùng commit** (luật frontend Mục 11 #4).

|  |  |
|---|---|
| **Người làm** | Một người (không chia lane — `giai-doan-4.md` Mục 9) |
| **Thời lượng** | Bước 7 (`E1`–`E6`, ~2 ngày) · bước 8 (`F1`–`F4`, ~0,5 ngày) — Mục 9.3 |
| **Cần trước** | Khối A–D xong: bảy endpoint quan hệ + `GET /feed` chạy thật, matrix 24/24, `API contract` canh bốn module. Hai file sinh `lib/api/socialgraph/schema.d.ts` (cổng mở) và `lib/api/content/schema.d.ts` có `/feed` (`D7`, `fdb3662`) **đã nằm trong repo**. Báo cáo k6 sơ bộ đã có (`C6`, đạt: lượt lạnh p95 37,6 ms) |
| **Hai khối này chặn** | **GĐ3** (khởi động ngay sau `F4`, cắm thanh cảm xúc vào chỗ ráp của `E5`) · **GĐ5** (`IFriendshipReader` thật, khuôn event sau `COMMIT`) |
| **Không thuộc hai khối này** | Mọi thay đổi trong `src/backend/**` (trừ lỗi hợp đồng blocking — Mục 1.2 luật 3) · route BFF mới · tìm người (GĐ6) · thông báo lời mời (GĐ6) · danh sách bạn của người khác · realtime. Xem Mục 15 |

---

## 0. Danh sách công việc — mục tiêu và kết quả mong đợi

Mười đầu việc: **sáu** của khối E, **bốn** của khối F. Ranh giới giữa hai khối vẫn là ranh giới giữa *"chạy trên
máy tôi"* và *"chứng minh được trên hệ thống thật"*: khối E nghiệm thu bằng `pnpm test`, `pnpm test:e2e` và
`localhost`; khối F nghiệm thu bằng domain HTTPS, hai trình duyệt thật, và psql trên server.

### 0.1 Khối E — Lane frontend

> **Mục tiêu khối (B.7):** trang chủ là feed thật; người mới có đường để gặp người khác; không màn trắng khi server quá
> tải.

| Mã | Đầu việc | Mục tiêu — việc này tồn tại để làm gì | Kết quả mong đợi — thứ kiểm chứng được |
|---|---|---|---|
| **E1** | Codegen, api client, ngữ cảnh lỗi, fixture `msw/node` | Cho năm đầu việc sau **một** bề mặt gọi API: kiểu sinh từ hai hợp đồng (hợp đồng đổi = đỏ compile), một chỗ gọi `fetch`, một bảng thông điệp lỗi phân biệt được 403/404/409 **theo endpoint** | `pnpm gen:api` → worktree **sạch** (file sinh đã có); `lib/api/types.ts` re-export đủ kiểu `socialgraph` + `FeedPage`/`FeedMode`, **không** khai tay dòng nào; `lib/api/socialgraph-api.ts` đủ chín endpoint + `contentApi.feed`, mọi id `encodeURIComponent`, **không** route BFF mới; `ErrorContext` có năm ngữ cảnh mới (Q-E3) và câu 409/404 chép **đúng** `detail` của hợp đồng; fixture gắn kiểu bằng `satisfies`; `pnpm typecheck` + `pnpm lint` xanh |
| **E2** | Nút quan hệ trên hồ sơ người khác (`RelationshipButtons`) | Đóng FR-010/011/012 phía người dùng ở **đúng chỗ** người dùng quyết định — trang hồ sơ. Bốn nhánh trạng thái vẽ theo **phản hồi server**, không đoán trước (Đ-4.16) | Bốn trạng thái `friendship` × `following` vẽ đúng nút; bấm → **mọi** nút khóa + spinner → vẽ lại theo `RelationshipResponse` (hoặc theo kết quả tất định của 204, Mục 3 Bước 2); 409/403/404 → câu đúng + **đọc lại** `GET /relationships` để vẽ sự thật; Hủy kết bạn qua `AlertDialog`; không hiện trên hồ sơ **chính mình** (Q-E2); Vitest đủ nhánh + **đúng một** ca `<StrictMode>` |
| **E3** | Màn `/friends`: lời mời đến, lời mời đi, danh sách bạn | Nơi người nhận **thấy** lời mời — không có màn này thì lời mời chỉ hiện khi người nhận tình cờ mở đúng hồ sơ người gửi (trước GĐ6 không có thông báo) | Ba danh sách theo cursor (`direction=incoming`, `outgoing`, `GET /friends`), "Xem thêm" theo `nextCursor`; Chấp nhận / Từ chối / Hủy / Hủy kết bạn **tại chỗ**, thẻ biến mất sau 2xx; chấp nhận xong thì danh sách bạn nạp lại; 403 chấp nhận → "lời mời không còn hiệu lực" + gỡ thẻ; mỗi thẻ dẫn tới `/users/{id}`; trạng thái rỗng riêng từng mục |
| **E4** | Feed trang chủ (`FeedList` + `useFeedPage`) | Biến `GET /feed` thành trang chủ đọc được: cuộn vô hạn theo `nextCursor` **mà không suy "hết" từ độ dài trang** (Đ-4.9), nhãn gợi ý cho người mới (Đ-4.6), 503 thành "thử lại" (Đ-4.10) | Cuộn vô hạn bằng `IntersectionObserver` **tạo trong effect**, **không** nút lúc bình thường (Q-E5) — trang sau lỗi → dòng lỗi + Thử lại cuối danh sách, hết → "Bạn đã xem hết", trần 5 trang rỗng → nút "Xem tiếp", nối trang → báo `role=status`; trang ngắn hơn `limit` mà `nextCursor ≠ null` **vẫn nạp tiếp**, kể cả trang **rỗng**; skeleton; `mode=suggested` → nhãn "Gợi ý cho bạn — kết bạn để thấy bài của bạn bè"; `mode=network` rỗng → câu mời kết bạn/theo dõi; 503 mang `type` `feed-overloaded` (Q-E4) → thẻ "Bảng tin đang quá tải" + Thử lại, **không** màn trắng, **không** đếm ngược, **không** tự thử lại; `features/feed/` **không** import `features/post/` |
| **E5** | Slot và ráp ở `app/` | Chỗ **duy nhất** biết cả `feed` + `post`, `profile` + `friend` (Đ-E13) — và là chỗ GĐ3 cắm thanh cảm xúc mà không chạm `features/feed/` (Mục 9.1 #3) | `/` là feed (route trùng đã xử — Q-E1); trang chủ ráp `FeedList` với `PostItem` qua `renderPost` (Q-E6); `PublicProfile` nhận slot `actions`, chỉ vẽ khi hồ sơ **đã nạp được**; `/users/[userId]` truyền `RelationshipButtons` trừ khi là chính mình; header có liên kết Trang chủ · Bạn bè · Trang của tôi qua slot, `AppHeader` không biết nghiệp vụ; lượt tay trên `localhost` với hai tài khoản đi hết vòng của `E6` |
| **E6** | Vitest + Playwright | Biến ba màn thành thứ **chặn merge** (Vitest ở CI) và thành bằng chứng chạy tay có ghi lại cho lát cắt hai tài khoản (Playwright local, Đ-E8) | Vitest phủ đủ Mục 10.6 dòng 1 và 2; `e2e/friend-feed.spec.ts` hai tài khoản: feed gợi ý → Kết bạn → Chấp nhận → bài `friends` hiện → Hủy kết bạn → bài biến mất; **cả bộ** `pnpm test:e2e` xanh (không chỉ spec mới — bài học GĐ2); kết quả + **bản Chrome** dán vào PR |

### 0.2 Khối F — Cổng đóng

> **Mục tiêu khối (B.10):** "xong" thành sự kiện kiểm chứng được, kể cả con số hiệu năng.

| Mã | Đầu việc | Mục tiêu — việc này tồn tại để làm gì | Kết quả mong đợi — thứ kiểm chứng được |
|---|---|---|---|
| **F1** | Deploy staging qua CD | Bốn module, bốn schema, hai cache Redis lần đầu chạy cùng nhau trên server thật qua đường CD chính thức — **không** SSH sửa tay | Mô tả PR ghi rõ **không có key `.env` mới** và **hai migration EF** (`InitialSocialGraph`, `PublicRecentIndex`); sau merge vào `develop`: log `migrate` áp đủ `identity`, `profile`, `content`, `socialgraph`; `__EFMigrationsHistory` ở **bốn** schema; `/health/ready` 200; `/swagger/socialgraph-v1/swagger.json` 200; `/swagger/content-v1/swagger.json` có `/feed` |
| **F2** | E2E lát cắt trên staging, hai tài khoản | Điều kiện 1 của B.11 — thứ duy nhất chứng minh `IFriendshipReader` thật, dấu nguồn của cache trang đầu và xóa cache sau `COMMIT` **cùng chạy đúng** trên hạ tầng thật | Trên `https://mxh.banhgao.net`, hai hồ sơ Chrome: tài khoản mới thấy **nhãn gợi ý** → kết bạn → bên kia chấp nhận → bài `friends` hiện trên trang chủ → hủy kết bạn → tải lại, bài **biến mất ngay**. Bằng chứng: ba ảnh chụp + bảng tab Network (chỉ `/bff/*`, `GET` ảnh R2, script Cloudflare đã chấp nhận ở Đ-E15; không `Authorization`; Web Storage trống) |
| **F3** | Checklist Mục 12 + Definition of Done Mục 11 | Rà bằng cách **kiểm tận nơi** (psql, redis-cli, tab Network, báo cáo k6), không suy từ CI xanh | Mọi dòng Mục 11 + Mục 12 của `giai-doan-4.md` được tick **kèm bằng chứng tại dòng**, hoặc ghi "chờ server" + lệnh để chạy — **không xóa dòng**; dòng US-008 AC-04 trỏ vào `bao-cao-k6-so-bo.md` |
| **F4** | Đóng băng + bàn giao | GĐ3 và GĐ5 khởi động trên nền ổn định; **nợ có địa chỉ** không thành nợ vô chủ | Khối chú thích `ĐÓNG BĂNG` trong `socialgraph-v1.yaml` và ở phần `/feed` của `content-v1.yaml` (chỉ comment: `pnpm gen:api` sạch, `Category=Contract` xanh); bảng hoãn Mục 2 đủ dòng; bàn giao cho **GĐ3** ba chỗ cắm (Mục 9.1) + hai việc phải làm lại, cho **GĐ5** ba thứ của B.8; ba điều kiện B.11 xác nhận kèm link |

### 0.3 Thứ tự thực thi

```
 E1 ──→ E4 ──→ E2 ──→ E3 ──→ E5 ──→ E6
 (api)  (feed)  (nút)  (/friends) (ráp app/) (Playwright + rà Mục 10.6)
                                    │
                                    └─ lượt tay localhost, hai tài khoản

 [E xong, PR khi được bảo] ──→ F1 ──→ F2 ──→ F3 ──→ F4
```

Bốn phụ thuộc **thật**:

- **`E1` → mọi thứ.** Không có kiểu và client thì mọi màn khai tay payload — đúng thứ luật frontend Mục 4 cấm.
- **`E4` đứng ngay sau `E1`, trước `E2`/`E3`.** Không phải vì kỹ thuật cần, mà vì **thứ tự cắt** (B.9): feed là "Không
  cắt"; màn lời mời đã gửi (thuộc `E3`) và nút Theo dõi (thuộc `E2`) là hai thứ cắt đầu tiên. Trễ thì cái bị bỏ phải là
  cái nằm cuối hàng.
- **`E2`, `E3`, `E4` → `E5`.** `E5` là chỗ ráp; ba màn trước nghiệm thu bằng Vitest + `msw/node`, `E5` là lần đầu chúng
  chạy trên API dev thật trong trình duyệt. Tách như vậy để **một** commit xử trùng route `/` (Q-E1) và mọi slot, thay vì
  rải `app/` qua ba commit.
- **Toàn bộ `E` → `F1`.** Cổng đóng không phải ngày code.

Hai chỗ **không** phải phụ thuộc:

- **Backend đã xong** — B.2 ghi "khối E không phụ thuộc backend, dựng trên `msw/node`" là để dùng khi backend kẹt. Tới
  bước 7 thì `D7` đã có, nên mỗi màn chạy thử được trên API dev ngay khi Vitest xanh. Vẫn viết Vitest **trước** lượt tay:
  nhánh 409/403/503 không tạo được bằng tay một cách ổn định.
- **`E6` không phải lúc mới viết test.** Vitest nằm cạnh mã nguồn, viết cùng từng màn; `E6` gom Playwright và rà Mục 10.6
  còn thiếu dòng nào.

### 0.4 Cột mốc tiến độ (STAFF-01)

| Mốc | Phải xong | Trễ thì |
|---|---|---|
| Giữa ngày 1 | `E1` + `E4` (Vitest xanh, gồm ca trang rỗng có `nextCursor`) | Chưa cắt gì — feed là "Không cắt"; dồn thời gian từ `E3` |
| Cuối ngày 1 | `E2` (Vitest xanh) | Cắt nút Theo dõi trên UI (B.9 thứ tự 3) |
| Giữa ngày 2 | `E3` + `E5`: lượt tay hai tài khoản trên `localhost` đi hết vòng | Cắt mục "Lời mời đã gửi" (B.9 thứ tự 1) |
| Cuối ngày 2 | `E6`: **cả bộ** Playwright xanh | Không cắt — dời `F*` sang sáng hôm sau, ghi vào PR |

### 0.5 Phần cắt được nếu trễ

Cắt thì **ghi rõ vào PR và vào `giai-doan-4.md`** (B.9), không lặng lẽ bỏ.

| Ứng viên | Cắt được vì | Cái giá |
|---|---|---|
| Mục "Lời mời đã gửi" ở `/friends` (`E3`) | Người gửi vẫn hủy được từ nút trên hồ sơ (`outgoing` → Hủy) | Người gửi không có một chỗ xem mình đang chờ ai |
| Nút Theo dõi / Bỏ theo dõi (`E2`) | FR-012 đã phủ ở mức API (`FOL-*`) | Không ai tạo được nguồn `FollowingOnly` từ UI — feed chỉ còn bạn bè + mình |
| **Không cắt:** cuộn tự động bằng observer (`E4`) *(sửa 2026-09-23 theo Q-E4/Q-E5)* | Q-E5 chốt **không** nút dự phòng — observer là đường **duy nhất** nạp thêm; cắt nó là feed chỉ còn trang đầu | Trễ thật thì phương án thay (không phải cắt): đổi observer thành nút trong `feed-list.tsx`, hook không chạm — ghi như lệch Q-E5 |
| **Không cắt:** feed trang chủ, nhãn gợi ý, 503 → Thử lại (`E4`) | "Lõi" của bảng ưu tiên; nhãn gợi ý là đường **duy nhất** để người mới gặp người khác trước GĐ6 | — |
| **Không cắt:** Kết bạn / Chấp nhận (`E2`, `E3` phần lời mời đến) | Thiếu thì `F2` không đi được vòng | — |
| **Không cắt:** `F2` | Điều kiện 1 của B.11 | — |

### 0.6 Chỗ lệch phát hiện khi viết hướng dẫn — ghi ngược vào `giai-doan-4.md`

Ghi ngược **trong commit của đầu việc tương ứng**, mở bằng "Lệch …" (luật commit Mục 5.3).

| # | Tài liệu gốc ghi | Thực tế / đề xuất | Ghi ngược ở commit |
|---|---|---|---|
| L1 | Mục 11 "Tick ở `F4`"; Mục 11 và 12 gọi E2E staging là "`F3`" | B.8: `F2` là E2E, `F3` là checklist. Sửa ba chỗ tham chiếu cho khớp B.8 | `F3` |
| L2 | B.8 `F4` bàn giao cho **GĐ5** | GĐ3 chạy **ngay sau** GĐ4 (Mục 9.1, Mục 9.4). `F4` bàn giao cho cả GĐ3 (ba chỗ cắm) lẫn GĐ5 | `F4` |
| L3 | B.7 `E1`: bốn ngữ cảnh `friend-request`, `friend-respond`, `follow`, `feed` | Năm — thêm `relationship` cho các lời gọi không có mã riêng (Q-E3) | `E1` |
| L4 | Đ-4.16: trang chủ ráp `FeedList` với **`PostCard`** | Với **`PostItem`** — feed có bài của chính mình (Đ-4.5), nên cần nút Sửa/Xóa theo `canEdit` và gỡ bài khỏi feed sau khi xóa (Q-E6) | `E5` |
| L5 | Đ-4.16 + Mục 10.6: "đúng một ca `<StrictMode>` cho `feed-list`" | Luật frontend Mục 9 áp cho **mọi** màn sở hữu tài nguyên hủy được: `relationship-buttons` và màn `/friends` cũng có `AbortController` → mỗi cái đúng một ca. Danh sách "năm ca hiện có" trong `frontend-rules.md` Mục 9 thành **tám** | `E2`, `E3`, `E4` (mỗi commit thêm tên mình vào danh sách) |

---

## 1. Trước khi gõ dòng đầu tiên

### 1.1 Điều kiện cần

| # | Kiểm | Kỳ vọng |
|---|---|---|
| 1 | `git log --oneline -1` trên `loveart1210` | Khối A–D xong; `dotnet test` xanh (trừ đỏ nền `Startup R2` của máy dev); CI của commit cuối xanh cả năm nhóm |
| 2 | Postgres + Redis + Mailpit dev | `docker compose -f deploy/docker-compose.dev.yml up -d` |
| 3 | API local | `dotnet run --project src/backend/SocialApp.Api` → `/swagger/socialgraph-v1/swagger.json` 200, `/swagger/content-v1/swagger.json` có `/feed`. `--migrate` đã chạy cho **bốn** schema |
| 4 | Frontend xanh **trước khi** sửa dòng đầu | `cd src/frontend && pnpm install --frozen-lockfile && pnpm lint && pnpm typecheck && pnpm test && pnpm build`. **Ghi số Vitest** — vào dòng `Test:` của mọi commit |
| 5 | Codegen đã khớp | `pnpm gen:api && git status --porcelain -- .` → rỗng |
| 6 | Playwright chạy được | `pnpm test:e2e` **cả bộ** xanh trên API dev — mốc so sánh; ghi bản Chrome (`chrome://version`) |
| 7 | Một bài `public` của người khác trong DB dev | Feed gợi ý rỗng thì nhãn gợi ý vẫn hiện nhưng không có tên tác giả nào để bấm — lượt tay không đi tiếp được. Spec của `E6` tự tạo bài này |

### 1.2 Tám luật áp thẳng vào hai khối

1. **Next 16 và Base UI ở đây không giống bản trong trí nhớ.** Đọc `node_modules/next/dist/docs/` trước khi viết
   (`src/frontend/AGENTS.md`); component tra bằng `pnpm exec shadcn docs <tên>`; Base UI ghép bằng `render`, không `asChild`.
2. **Không thêm route BFF nào.** Mười endpoint mới đi qua proxy chung `app/bff/api/[...path]/route.ts` (đã export đủ
   `GET|POST|PUT|PATCH|DELETE`, chuyển nguyên query string). Thêm route là vi phạm Đ-E14 và luật frontend Mục 1 #7.
3. **Không sửa `src/backend/**`.** Thấy hợp đồng lệch thực tế thì **dừng lại**: sửa hợp đồng sau `D7` là mở lại thứ vừa
   chốt, kéo theo `.yaml` + `pnpm gen:api` + `ContractTests` trong cùng commit. Chỉ ngoại lệ cho lỗi hợp đồng blocking.
4. **Không import chéo `features/`.** `features/feed/` không import `features/post/` (Đ-4.16 — luật frontend Mục 12 ghi
   đích danh `PostCard` là ví dụ *không đạt*); `features/friend/` không import `features/profile/`. Ráp ở `app/`.
5. **Không optimistic cho nút quan hệ** (Đ-4.16). Bấm → khóa → vẽ theo phản hồi.
6. **Hết dữ liệu khi và chỉ khi `nextCursor === null`** (Đ-4.9) — áp cho cả feed lẫn ba danh sách của `/friends`. Không
   một dòng nào so `items.length` với `limit`.
7. **`useRef(new …)` bị cấm** (luật frontend Mục 1 #14, cổng `USE_REF_NEW`). `IntersectionObserver` và
   `AbortController` tạo **trong effect**; ref chỉ là hộp đựng.
8. **Không log** token, `nextCursor`, URL ảnh đã ký — kể cả `console.log` tạm lúc dò lỗi.

### 1.3 Impact analysis trước khi sửa symbol có sẵn

`CLAUDE.md`: chạy `node .gitnexus/run.cjs impact "<symbol>" --direction upstream --repo .` **trước** khi sửa, ghi mức rủi
ro vào thân commit. `UNKNOWN` không phải `LOW` — xác nhận lại bằng tìm chữ.

| Symbol | Đầu việc | Người gọi đã biết (tìm chữ 2026-09-23) | Vì sao sửa |
|---|---|---|---|
| `pageQuery` (`lib/api/content-api.ts`) | `E1` | `contentApi.listUserPosts` | Dời sang `lib/api/page-query.ts` để `socialgraph-api.ts` dùng chung |
| `ErrorContext`, `FieldErrorKey` (`lib/api/messages.ts`) | `E1` | Mọi màn gọi `errorMessage`/`fieldMessage` | Chỉ **thêm** thành viên union — không đổi câu cũ nào |
| `PublicProfile` | `E5` | `app/(app)/(with-profile)/users/[userId]/page.tsx`, `public-profile.test.tsx` | Thêm prop `actions?: ReactNode` (tùy chọn — người gọi cũ không đổi) |
| `AppHeader` | `E5` | `app/(app)/layout.tsx` | Thêm prop `nav?: ReactNode` (Q-E7) |
| `safeNext` | `E5` | `login-form.tsx`, `profile-form.tsx` | Mặc định `"/me"` → `"/"` (Q-E1) — đổi nơi người dùng hạ cánh sau đăng nhập/onboarding |
| `OnboardingPage` redirect (`features/profile/onboarding.tsx:30`) | `E5` | — | `router.replace("/me")` → `"/"` (Q-E1) |
| `app/page.tsx` | `E5` | — (route) | **Xóa** — trùng route `/` với trang chủ mới (Q-E1) |

### 1.4 Tám câu phải chốt trước khi gõ code

Cùng nếp `Q-E1`–`Q-E8` của GĐ2: nêu đề xuất kèm lý do, chốt, rồi **ghi ngược** vào tài liệu gốc trong commit tương ứng.
Làm một mình thì "chốt" là một dòng *✅ chốt <ngày>* ngay dưới câu hỏi — chưa chốt thì không gõ đầu việc liên quan.

#### Q-E1 — Trang chủ ở `/`: xử trùng route và nơi hạ cánh sau đăng nhập

`app/page.tsx` hiện `redirect("/me")`. Thêm `app/(app)/(with-profile)/page.tsx` là **hai trang cùng giải về `/`** — Next
báo lỗi build (route group không tạo segment URL).

- **Đề xuất:** xóa `app/page.tsx`; trang chủ là `app/(app)/(with-profile)/page.tsx` — nhờ vậy nó tự có `RequireAuth` +
  `RequireProfile` + header mà không viết guard nào. Người chưa đăng nhập vào `/` bị `RequireAuth` đưa về
  `/login?next=%2F` như mọi trang `(app)` khác.
- **Kèm theo:** mặc định của `safeNext` đổi `"/me"` → `"/"`, và `onboarding.tsx` chuyển về `"/"` sau khi tạo hồ sơ.
  Trang chủ là feed thì đăng nhập xong phải thấy feed; người mới onboarding xong thấy **feed gợi ý** — chính là đường gặp
  người khác của Đ-4.6. `post-detail.tsx` (xóa bài xong về `/me`) **giữ nguyên**: người vừa xóa bài muốn thấy bài
  còn lại của mình.
- **Test phải sửa khẳng định có chủ đích:** `safe-next.test.ts` (mặc định), và spec Playwright nào đăng nhập **không**
  kèm `next` hoặc đi qua onboarding rồi chờ URL `/me` (`login-storage.spec.ts`, `guard.spec.ts`, `onboarding.spec.ts` —
  rà bằng `grep -n "toHaveURL\|waitForURL" e2e/*.ts`). `dangNhapUi` của `e2e/post-helpers.ts` đi `/login?next=%2Fme` nên
  **không** đổi. Ghi rõ trong thân commit: hành vi đã chốt của GĐ1/GĐ2 **đổi có chủ đích**.
- **Đã cân nhắc rồi loại:** giữ `/` redirect sang `/feed`. Loại vì thêm một bước điều hướng cho mọi lần mở app, và
  `AppHeader` đã trỏ logo về `/` từ GĐ1.

*✅ chốt 2026-09-23 như đề xuất. Rà `e2e/`: chỉ `smoke.spec.ts` (vào `/` chưa đăng nhập) đổi khẳng định → `/login?next=%2F`;
mọi spec khác đăng nhập với `?next=%2Fme` tường minh nên không đổi.*

#### Q-E2 — Hồ sơ chính mình: không vẽ nút quan hệ

`GET /relationships/{mình}` → **400** (Mục 8.1). Người dùng tự mở `/users/{id của mình}` (bấm tên mình trên một bài
trong feed — Đ-4.5 đưa bài của mình vào feed) thì `RelationshipButtons` hiện lỗi.

- **Đề xuất:** quyết định ở `app/` — tầng duy nhất được biết cả `profile` (ai đang đăng nhập) lẫn `friend`.
  `users/[userId]/page.tsx` đổi thành client component (khuôn `me/page.tsx`): `const { userId } = use(params)`,
  `const { profile } = useProfile()`, truyền `actions={profile?.userId === userId ? undefined : <RelationshipButtons
  userId={userId} />}`. `profile` luôn có ở đây vì trang nằm dưới `(with-profile)`.
- **Đã cân nhắc rồi loại:** `RelationshipButtons` tự bắt 400 rồi ẩn đi — dùng mã lỗi để suy "đây là chính mình" là đoán
  luật server từ status; và vẫn tốn một request hỏng mỗi lần mở hồ sơ mình.
- **Đã cân nhắc rồi loại:** redirect `/users/{mình}` → `/me`. Tốt về trải nghiệm nhưng là hành vi mới không ai yêu cầu;
  để GĐ sau nếu cần.

*✅ chốt 2026-09-23 như đề xuất (slot `actions` thành hàm nhận hồ sơ — xem "Thực tế thi công" E5).*

#### Q-E3 — `ErrorContext` cho SocialGraph và feed — **lệch B.7 (L3)**

B.7 ghi bốn ngữ cảnh. Nhưng `GET /relationships`, `GET /friends`, `GET /friends/requests`, ba `DELETE` và `DELETE
/follows` cũng gọi `errorMessage` — chúng không có mã riêng (chỉ 400/401/429/5xx), và mượn `friend-request` cho một lời
gọi `GET` là mời người sau thêm câu 409 "đã có lời mời" vào một màn đọc.

- **Đề xuất: năm ngữ cảnh.**

  | Ngữ cảnh | Endpoint | Status có câu riêng | Câu |
  |---|---|---|---|
  | `relationship` | `GET /relationships/{id}`, `GET /friends`, `GET /friends/requests`, `DELETE /friends/requests/{id}`, `DELETE /friends/{id}`, `DELETE /follows/{id}` | — | Nhánh chung |
  | `friend-request` | `POST /friends/requests` | 403 · 404 · 409 | 403 "Tài khoản của bạn chưa được phép kết bạn." · 404 "Không tìm thấy người dùng." · 409 "Đã có lời mời hoặc quan hệ bạn bè giữa hai người." (**nguyên văn `detail`** của hợp đồng) |
  | `friend-respond` | `POST /friends/requests/{id}/accept` | 403 | "Lời mời này không còn hiệu lực." — **một** câu cho mọi lý do (Đ-4.14: không có lời mời · lời mời của chính mình · đã là bạn · người thứ ba) |
  | `follow` | `PUT /follows/{id}` | 403 · 404 | 403 "Tài khoản của bạn chưa được phép theo dõi người khác." · 404 "Không tìm thấy người dùng." |
  | `feed` | `GET /feed` | 503 | "Bảng tin đang quá tải. Vui lòng thử lại sau ít phút." — **không** kèm `traceId`: 503 không phải lỗi hệ thống (Đ-4.10). *(sửa 2026-09-23 theo Q-E4/Q-E5)* Câu này **không** nằm ở `BY_CONTEXT` mà ở `BY_TYPE`, chọn theo `type` `feed-overloaded`; `feed` trong `BY_CONTEXT` rỗng — 503 không mang `type` riêng là lỗi hệ thống |

- **Kèm theo:** `FieldErrorKey` thêm `"userId"` và `"direction"` — hai key `errors` mới của `socialgraph-v1` (tự gửi /
  tự theo dõi → `errors.userId`, câu server *"Không thể gửi lời mời kết bạn cho chính mình."* / *"Không thể theo dõi
  chính mình."*). UI không cho bấm hai nút đó trên hồ sơ mình (Q-E2), nhưng Đ-E5 vẫn đòi hiện đúng câu server nếu 400 tới.
- Ghi ngược vào B.7 `E1`: "Lệch B.7 (nhóm chốt): năm ngữ cảnh — thêm `relationship` …".

*✅ chốt 2026-09-23: năm ngữ cảnh như đề xuất. Đã ghi ngược vào B.7 `E1` trong commit `E1`.*

#### Q-E4 — 503 từ BFF không phải lúc nào cũng là feed quá tải

BFF tự trả 503 *"Dịch vụ phiên đăng nhập tạm thời không sẵn sàng"* khi Redis của **phiên** chết (`lib/bff/handlers.ts`).
Với `/bff/api/feed`, FE không phân biệt được hai loại 503.

- **Đề xuất: không phân biệt.** Cả hai đều là "đợi rồi thử lại", và nút Thử lại đúng cho cả hai. Câu Q-E3 nói "bảng tin
  đang quá tải" — với ca Redis phiên chết thì hơi sai chữ nhưng đúng việc phải làm. **Không** so `title` của Problem
  Details để chọn câu: `title` là nhãn của server, FE dựa vào nó là dựa vào một chuỗi không có trong hợp đồng.
- `Retry-After` **không** dùng ở FE (Đ-4.10: không hứa thời điểm). Không cần kiểm proxy BFF có chuyển header đó hay không.

*✅ chốt 2026-09-23 — **khác đề xuất**: phân biệt, bằng `type` khai trong hợp đồng.* 503 feed mang
`urn:socialapp:problem:feed-overloaded` (schema `FeedOverloadedProblem` trong `content-v1.yaml`, `Error.Type` ở SharedKernel);
503 BFF mất kho phiên mang `urn:socialapp:problem:bff-session-unavailable` (`bff-contract.ts`). `errorMessage` tra `type`
**trước** `(ngữ cảnh, status)`; 503 không mang `type` riêng là lỗi hệ thống (có mã tra cứu). Vẫn không so `title`, vẫn
không đọc `Retry-After`. **Lệch Mục 1.2 luật 3** (chạm `src/backend/**` sau `D7`): nhóm chốt mở lại hợp đồng theo luật
chỉ-thêm — một commit riêng trước `E4`, `.yaml` + `pnpm gen:api` + test backend cùng commit. Ghi ngược Đ-4.10 và Đ-E6.

#### Q-E5 — Cuộn vô hạn: observer tự nạp + nút "Xem thêm" dự phòng

GĐ2 chọn **nút** (`post-list.tsx`): cuộn tự động làm người dùng bàn phím không tới được cuối trang và nuốt lỗi. B.7 lại
ghi "cuộn vô hạn" cho feed.

- **Đề xuất:** cả hai. Một phần tử canh (sentinel) cuối danh sách, observer thấy nó thì `loadMore()`; **nút "Xem thêm"
  vẫn hiện** khi `nextCursor !== null` — cho bàn phím, và cho trình duyệt không có observer. Observer **ngừng tự nạp khi
  có lỗi** — lỗi thì hiện nút Thử lại, không vòng thử lại tự động (đúng tinh thần 503 của Đ-4.10).
- **Cạm bẫy phải xử, không phải tùy chọn:** observer chỉ bắn khi trạng thái giao nhau **đổi**. Trang về mà sentinel vẫn
  nằm trong khung nhìn (trang ngắn vì hydrate lọc bớt, hoặc trang **rỗng** mà `nextCursor ≠ null` — Đ-4.9) thì observer
  **không bắn lại** và feed đứng im với nút "Xem thêm" duy nhất. Cách xử: giữ `isIntersecting` mới nhất trong ref; sau mỗi
  lượt nạp xong, nếu sentinel vẫn giao nhau và `nextCursor !== null` thì nạp tiếp. Có trần: tối đa **5** lượt nạp liên
  tiếp không thêm được bài nào thì dừng tự nạp, để nút làm việc — chặn vòng request khi server trả liên tục trang rỗng.

*✅ chốt 2026-09-23 — **khác đề xuất**: chỉ observer tự nạp, **không** nút "Xem thêm" lúc bình thường.* Trang sau hỏng →
ngừng tự nạp, dòng lỗi + **Thử lại** ở cuối danh sách; bấm là nạp lại đúng lô vừa hỏng (cùng `nextCursor`) và observer bật
lại. Hết dữ liệu → "Bạn đã xem hết". Giữ cách xử sentinel còn trong khung nhìn và trần 5 lượt rỗng liên tiếp; tới trần thì
hiện nút **Xem tiếp** (trạng thái bất thường, không phải "lúc bình thường" — không có nút thì feed kẹt).
Xác nhận thêm (2026-09-23): nút **Xem tiếp** ở trần giữ nguyên — hiện "Bạn đã xem hết" ở đó là sai vì `nextCursor` còn khác
null (Đ-4.9). Bỏ nút thì mất tín hiệu "có thêm bài" cho trình đọc màn hình → vùng `role=status` báo "Đã tải thêm N bài, đang
hiển thị M bài." mỗi lần tự nối trang. **Q-E5 chỉ áp cho feed:** `/friends` (E3) giữ nút "Xem thêm" — ba danh sách xếp
chồng trên một trang, tự nạp mục giữa đẩy mục dưới đi mãi.

#### Q-E6 — `renderPost` trả `PostItem`, không `PostCard` — **lệch Đ-4.16 (L4)**

Đ-4.16 ghi trang chủ ráp `FeedList` với `PostCard`. Nhưng feed chứa **bài của chính mình** (Đ-4.5): bài đó cần nút
Sửa/Xóa theo `canEdit`, và xóa xong phải biến khỏi feed — để lại card là để lại liên kết chết (lý do `PostList` của GĐ2
dùng `PostItem`).

- **Đề xuất:** `FeedList` nhận `renderPost: (post: PostResponse, onChanged: (next: PostResponse | null) => void) =>
  ReactNode`. `FeedList` truyền `onChanged` nối vào `replaceItem` của hook; trang chủ ráp
  `renderPost={(post, onChanged) => <PostItem post={post} onChanged={onChanged} />}`. `FeedList` vẫn **không** biết
  `PostItem` là gì — GĐ3 thay/bọc ở đúng dòng ráp này.
- Ghi ngược vào Đ-4.16: "Lệch Đ-4.16 (nhóm chốt, 2026-09-…): ráp với `PostItem` …".

*✅ chốt 2026-09-23 như đề xuất. `E4` dựng chữ ký `RenderPost`; lệch Đ-4.16 ghi ngược ở `E5` (chỗ ráp `PostItem`).*

#### Q-E7 — Liên kết điều hướng trong header

B.7 `E5`: "Liên kết 'Bạn bè' trong `AppHeader`". `AppHeader` ở `components/shell/` — **không** được biết nghiệp vụ.

- **Đề xuất:** thêm slot `nav?: ReactNode` cạnh `actions`; `app/(app)/layout.tsx` truyền ba `Link`: **Trang chủ** (`/`),
  **Bạn bè** (`/friends`), **Trang của tôi** (`/me`). Chuỗi đường dẫn nằm ở `app/`, shell chỉ đặt chỗ. Ở màn hẹp, ba liên
  kết là chữ nhỏ cùng hàng — không thêm menu thả xuống (không có trong kit, và không đáng một component mới).

*✅ chốt 2026-09-23 như đề xuất.* *Đổi cùng ngày, sau `E5`: bỏ liên kết **Trang chủ** — logo đã dẫn về `/`; header còn
**Bạn bè** · **Trang của tôi**.*

#### Q-E8 — Hook phân trang: chép, hay tách một hook chung?

Feed và **ba** danh sách của `/friends` đều cần đúng khuôn của `use-post-page.ts`: key + suy ra, chốt `run`, khử trùng,
`AbortController` dùng chung cho trang sau, không `setState` đồng bộ trong effect. B.7 ghi *"chép logic cuộn … thành bản
của feed"* — nhưng đó là hai bản; `/friends` làm thành bốn.

- **Đề xuất: một hook chung `hooks/use-cursor-pages.ts`** — nhận `key`, `fetchPage(cursor, signal)`, `getId(item)`; trả
  đúng `PostPageState` nhưng generic `<T>`. Không biết nghiệp vụ (không biết bài, bạn, feed là gì). Thư mục `hooks/` đã có
  sẵn (`hooks/.gitkeep`, alias `@/hooks` trong `components.json`) nhưng **chưa có dòng nào** trong bảng tầng của luật
  frontend Mục 2 → thêm một dòng *"`hooks/` — hook React dùng lại, không biết nghiệp vụ; tầng ngang `components/`"* **trong
  cùng commit** (`E1`), và ESLint cấm `hooks/**` import `@/features/*`, `@/app/*` (thử cho đỏ một lần).
- `use-post-page.ts` của GĐ2 **không** chuyển sang hook chung trong GĐ4 — nó đang xanh với năm ca test, gồm một ca
  `<StrictMode>`; chuyển là rủi ro không cần cho giai đoạn này. Ghi thành việc dọn nợ có địa chỉ (GĐ5, khi lịch sử hội
  thoại là người dùng thứ ba của khuôn).
- **Phương án dự phòng nếu không muốn mở tầng mới:** chép như B.7, **một** bản trong `features/feed/`, **một** bản trong
  `features/friend/` dùng chung cho ba danh sách. Cái giá: ba bản của cùng một khuôn đã từng có lỗi StrictMode (GĐ2 dọn nợ
  2026-09-21) — sửa một lỗi phải nhớ sửa ba chỗ.

*✅ chốt 2026-09-23: hook chung `hooks/use-cursor-pages.ts`. Commit `E1` mở tầng (luật frontend Mục 2, `src/frontend/AGENTS.md`,
Đ-E13 ghi mở rộng có ngày, ESLint đã thử đỏ) và ghi "Lệch B.7" dưới B.7 `E1`; hook viết ở `E4`.*

---

## 2. E1 — Codegen, api client, ngữ cảnh lỗi

### Mục tiêu

Một bề mặt gọi API cho năm đầu việc sau. Hợp đồng đổi thì phải là **lỗi compile**, không phải lỗi lúc chạy ở một màn nào đó.

### Các bước

**Bước 1 — xác nhận file sinh.** `pnpm gen:api` → `git status --porcelain -- .` **rỗng**. Không có gì để sinh: hai file
đã vào repo ở cổng mở và ở `D7`. Bước này chỉ để chắc chúng còn khớp hợp đồng sau các commit `test(gd4-b)` cuối.

**Bước 2 — `lib/api/types.ts`.** Thêm khối SocialGraph, và hai dòng vào khối Content:

```ts
import type { components as SocialGraphComponents } from "./socialgraph/schema"
type G = SocialGraphComponents["schemas"]

// --- SocialGraph (socialgraph-v1.yaml) — GĐ4 E1 ---
export type FriendshipState = G["FriendshipState"]
export type FriendRequestDirection = G["FriendRequestDirection"]
export type CreateFriendRequest = G["CreateFriendRequest"]
export type RelationshipResponse = G["RelationshipResponse"]
export type UserCard = G["UserCard"]
export type FriendCard = G["FriendCard"]
export type FriendPage = G["FriendPage"]
export type FriendRequestPage = G["FriendRequestPage"]

// --- Content, thêm ở GĐ4 ---
export type FeedMode = C["FeedMode"]
export type FeedPage = C["FeedPage"]
```

`ProblemDetails` có ở cả bốn schema — **không** re-export thêm bản nào (bản của `identity` đã có).

**Bước 3 — `lib/api/page-query.ts`.** Dời `pageQuery` ra khỏi `content-api.ts` (impact analysis trước — Mục 1.3), giữ
nguyên thân hàm và chú thích (`cursor` chỉ vào query string **khi có**). `content-api.ts` import lại.

**Bước 4 — `lib/api/socialgraph-api.ts`** theo đúng hình dạng `profile-api.ts`. Mọi id `encodeURIComponent`:

| Hàm | Gọi | Trả |
|---|---|---|
| `relationship(userId, signal?)` | `GET /relationships/{userId}` | `RelationshipResponse` |
| `sendRequest(userId)` | `POST /friends/requests` body `{ userId }` | `RelationshipResponse` (201) |
| `accept(userId)` | `POST /friends/requests/{userId}/accept` | `RelationshipResponse` (200) |
| `removeRequest(userId)` | `DELETE /friends/requests/{userId}` — **cả** hủy (người gửi) lẫn từ chối (người nhận) | `void` (204) |
| `unfriend(userId)` | `DELETE /friends/{userId}` | `void` (204) |
| `follow(userId)` | `PUT /follows/{userId}` | `void` (204) |
| `unfollow(userId)` | `DELETE /follows/{userId}` | `void` (204) |
| `listFriends(opts, signal?)` | `GET /friends?cursor=&limit=` | `FriendPage` |
| `listRequests(direction, opts, signal?)` | `GET /friends/requests?direction=&cursor=&limit=` — `direction` **luôn** gửi, không dựa vào mặc định `incoming` của server | `FriendRequestPage` |

Và thêm vào `contentApi`: `feed(opts, signal?)` → `GET /feed${pageQuery(opts)}` → `FeedPage`.

**Bước 5 — `lib/api/messages.ts`** theo Q-E3: năm ngữ cảnh, hai `FieldErrorKey`. Chép câu 409/404 **nguyên văn** từ
`detail` trong `socialgraph-v1.yaml` (dòng `detail: Đã có lời mời…`, `detail: Không tìm thấy người dùng.`) — một lỗi không
hiện hai cách nói (luật frontend Mục 6).

**Bước 6 — fixture và handler `msw/node`.**

- `mocks/fixtures.ts`: `relationship(friendship, following)`, `friendCard`, `friendPage`, `feedPage(mode)` — **giá trị**
  chép từ `example` của hai hợp đồng, gắn kiểu bằng `satisfies T.RelationshipResponse` v.v. Hợp đồng đổi hình dạng thì
  fixture đỏ compile (luật frontend Mục 8).
- `mocks/handlers.ts`: handler cho `/bff/api/relationships/:userId`, `/bff/api/friends*`, `/bff/api/follows/:userId`,
  `/bff/api/feed`. Kịch bản chọn bằng **dữ liệu nhập** như handler GĐ1/GĐ2 (`userIdDaCoLoiMoi` → 409, `userIdKhongTonTai` →
  404, cursor `"rong-con-trang"` → trang `items: []` + `nextCursor` khác null, cursor `"qua-tai"` → 503) — test đọc là thấy
  nhánh nào đang chạy. 503 trả `application/problem+json` như mọi lỗi khác (`toApiError` kiểm content-type).

### Test

- `lib/api/socialgraph/schema.test-d.ts` (khuôn `content/schema.test-d.ts`): ghim `FriendshipState` đúng bốn giá trị,
  `FeedPage.nextCursor` là `string | null`, `FeedPage.mode` là `"network" | "suggested"`. GĐ3 thêm `myReaction` vào
  `PostResponse` thì `FeedPage.items` tự có — không ghim gì về chuyện đó.
- `lib/api/messages.test.ts`: mỗi ngữ cảnh mới ít nhất một ca; `feed` 503 **không** có "Mã tra cứu"; `feed` 500 **có**.
- `lib/api/http.test.ts`: không cần ca mới — `PUT` không body (`follow`) đã đi qua nhánh body `undefined`. Kiểm lại bằng
  một ca nếu `request()` gửi `Content-Type` khi không có body.

### Cạm bẫy đã biết

| Cạm bẫy | Triệu chứng | Chặn bằng |
|---|---|---|
| Khai tay `type RelationshipResponse = {...}` | Hợp đồng đổi, không ai biết tới lúc chạy | Luật frontend Mục 12; grep ở checklist Mục 13 |
| Gọi `follow` bằng `POST` | 405 | Bảng Bước 4, test method trong `E2` |
| `listRequests` bỏ `direction` "vì server mặc định `incoming`" | Mục "lời mời đã gửi" hiện lời mời **đến** | Luôn gửi `direction` |
| Câu 409 tự viết khác `detail` của server | Hai cách nói cho một lỗi | Chép nguyên văn |
| Handler 503 trả body không phải JSON (trang HTML) | `problem: null` — test 503 xanh vì lý do sai | `PROBLEM_HEADERS` như mọi handler lỗi; `http.test.ts` khẳng định `problem.title`. *Sửa 2026-09-23: bản đầu ghi "`application/json`" — sai, `toApiError` nhận mọi content-type có chữ `json`, đột biến đó tương đương* |

### Kết quả mong đợi

`typecheck` + `lint` + `test` xanh; `git status` sau `pnpm gen:api` rỗng; không file nào trong `features/` đổi.

---

## 3. E2 — Nút quan hệ trên hồ sơ người khác

### Mục tiêu

FR-010/011/012 ở đúng chỗ người dùng quyết định. Bốn nhánh trạng thái, vẽ theo **sự thật của server** — đoán trước là
hiện "Đã là bạn" trong khi server trả 409 (Đ-4.16).

### Các bước

**Bước 1 — `features/friend/relationship-buttons.tsx`** nhận `userId`. Nạp `GET /relationships/{userId}` trong effect
(`AbortController` tạo trong effect, khuôn key + suy ra của `PublicProfile`). Trong lúc chờ: hai `Skeleton` cỡ nút. Lỗi:
câu `errorMessage("relationship", e)` + nút Thử lại.

**Bước 2 — bảng vẽ nút.**

| `friendship` | Nút kết bạn | Bấm → gọi | Thành công → trạng thái mới |
|---|---|---|---|
| `none` | **Kết bạn** | `sendRequest` | Theo `RelationshipResponse` (201) |
| `outgoing` | "Đã gửi lời mời" (nhãn) + **Hủy lời mời** | `removeRequest` | `none` — **tất định** (204, xem dưới) |
| `incoming` | **Chấp nhận** + **Từ chối** | `accept` / `removeRequest` | Theo `RelationshipResponse` (200) / `none` |
| `friends` | "Bạn bè" (nhãn) + **Hủy kết bạn** → `AlertDialog` xác nhận | `unfriend` | `none` |

| `following` | Nút theo dõi | Bấm → gọi | Thành công → |
|---|---|---|---|
| `false` | **Theo dõi** | `follow` | `following = true` |
| `true` | **Bỏ theo dõi** | `unfollow` | `following = false` |

**Vẽ lại sau 204 không phải optimistic.** Bốn thao tác 204 là **idempotent** và kết quả của chúng đã định sẵn bởi hợp
đồng: `DELETE /friends/requests/{id}` xong thì giữa hai người **không còn** lời mời nào (theo cả hai chiều), `PUT /follows`
xong thì **đang** theo dõi. Cập nhật **sau khi** 204 về là vẽ theo phản hồi — chỉ khác là phản hồi không có body. Chỗ bị
cấm là cập nhật **trước** khi phản hồi về. Một ngoại lệ: `DELETE /friends/{id}` **không** xóa lời mời `pending` (mô tả của
endpoint đó trong `socialgraph-v1.yaml`) — nhưng nút này chỉ hiện khi `friendship = friends`, tức không có lời mời nào, nên `none` vẫn đúng.

**Bước 3 — khóa và lỗi.** Một `pending` cho **cả cụm**: bấm bất kỳ nút nào → mọi nút `disabled`, nút vừa bấm có
`Spinner` + `aria-busy`. Lỗi của thao tác ghi:

| Lỗi | Hiện | Rồi |
|---|---|---|
| 409 (`friend-request`) | Câu Q-E3 | **Đọc lại** `GET /relationships` — thường là người kia vừa gửi trước, và nút đúng bây giờ là "Chấp nhận" |
| 403 (`friend-respond`) | "Lời mời này không còn hiệu lực." | Đọc lại — lời mời đã bị hủy trong lúc màn đang mở (`FRD-10`) |
| 404 (`friend-request`, `follow`) | "Không tìm thấy người dùng." | Không đọc lại |
| Khác | `errorMessage(<ngữ cảnh>, e)` | Giữ trạng thái cũ — không đoán |

**Bước 4 — `AlertDialog` cho Hủy kết bạn** (kit đã có từ GĐ2). Nội dung nói rõ hệ quả: *"Bài chỉ dành cho bạn bè của
{tên} sẽ không còn hiện với bạn."* — tên lấy từ prop `displayName?` do `app/` truyền (nếu cắt thì bỏ tên, giữ câu).
Hủy trong hộp thoại → **không** request nào.

### Test (`features/friend/relationship-buttons.test.tsx`)

- Bốn `friendship` × hai `following`: đúng nhãn nút (đặt `data-testid` cho từng nút).
- Mỗi nút gửi **đúng method + path** (ghi request qua `msw/node`), và vẽ đúng trạng thái sau phản hồi.
- 409 khi Kết bạn → câu nguyên văn + lượt `GET` thứ hai + nút "Chấp nhận" hiện.
- 403 khi Chấp nhận → câu Q-E3 + đọc lại.
- Đang `pending` → mọi nút `disabled`; bấm lần hai **không** gửi request thứ hai.
- `AlertDialog` → Hủy → 0 request `DELETE`.
- **Đúng một** ca `<StrictMode>`: khẳng định trạng thái cuối (nút đúng), **không** đếm số `GET` (L5; luật frontend Mục 9).
  Thêm `relationship-buttons` vào danh sách ca của luật frontend Mục 9 trong cùng commit.

### Cạm bẫy đã biết

| Cạm bẫy | Triệu chứng | Chặn bằng |
|---|---|---|
| Đặt trạng thái mới **trước** khi request về "cho mượt" | 409 mà nút đã ghi "Đã gửi lời mời" | Đ-4.16; test `pending` |
| Hai nút (kết bạn + theo dõi) bấm song song | Hai phản hồi về lệch thứ tự, trạng thái cuối là của phản hồi về sau | Một `pending` cho cả cụm |
| `following` suy từ `friendship` ("bạn thì là đang theo dõi") | Sai Đ-4.5 — kết bạn **không** tạo dòng `follows` | Hai trường độc lập, hai hàng nút độc lập |
| `useRef(new AbortController())` | Dưới StrictMode request đầu dùng controller **đã hủy** → không bao giờ nạp được | Luật frontend Mục 1 #14, cổng ESLint, ca StrictMode |

### Kết quả mong đợi

Vitest xanh, gồm ca StrictMode; chưa có trang nào dùng component (ráp ở `E5`).

---

## 4. E3 — Màn `/friends`: lời mời đến, lời mời đi, danh sách bạn

### Mục tiêu

Chỗ người nhận **thấy** lời mời. Trước GĐ6 không có thông báo; thiếu màn này thì người nhận chỉ biết có lời mời khi tình
cờ mở hồ sơ người gửi.

### Các bước

**Bước 1 — `features/friend/friends-screen.tsx`**: ba mục **xếp dọc**, theo thứ tự việc cần làm: **Lời mời kết bạn**
(`incoming`) · **Bạn bè** · **Lời mời đã gửi** (`outgoing`). Không dùng tab: kit không có `tabs`, và ba mục ngắn đọc
một lượt nhanh hơn ba lần bấm. Mục "Lời mời đã gửi" là ứng viên cắt số 1 (B.9) — đặt nó cuối để cắt là xóa một khối.

**Bước 2 — một thẻ dùng chung `features/friend/friend-card.tsx`**: `Avatar` + tên (liên kết `/users/{userId}`) + thời điểm
(`since`, định dạng như `PostCard`: `vi-VN`, `Asia/Ho_Chi_Minh`) + vùng nút do màn truyền vào.

| Mục | Nút trên thẻ | Gọi | 2xx → |
|---|---|---|---|
| Lời mời đến | **Chấp nhận** · **Từ chối** | `accept` · `removeRequest` | Gỡ thẻ; Chấp nhận thì **nạp lại** mục Bạn bè (bạn mới nằm đầu theo `accepted_at DESC`) |
| Bạn bè | **Hủy kết bạn** → `AlertDialog` | `unfriend` | Gỡ thẻ |
| Lời mời đã gửi | **Hủy** | `removeRequest` | Gỡ thẻ |

403 khi Chấp nhận → câu Q-E3 **ở đầu mục** "Lời mời kết bạn", kèm tên người gửi, rồi gỡ thẻ (lời mời không còn) — *sửa
2026-09-23: bản đầu ghi "dưới thẻ", tự mâu thuẫn vì thẻ bị gỡ thì câu dưới thẻ mất theo*. Lỗi khác → câu dưới thẻ, thẻ giữ nguyên.
Mỗi thẻ khóa nút **riêng** trong lúc chờ — khác `E2`: ở đây các thẻ là các người khác nhau, không giẫm lên nhau.

**Bước 3 — phân trang:** mỗi mục một lượt `useCursorPages` (Q-E8), `PAGE_SIZE` 20, nút "Xem thêm" khi `nextCursor !== null`
— không observer (ba danh sách cùng trang, ba observer là thừa; xác nhận khi chốt Q-E5 2026-09-23 — Q-E5 chỉ áp cho feed). Gỡ thẻ tại chỗ đi qua `replaceItem(id, null)` để
tập khử trùng quên luôn id đó.

**Bước 4 — trạng thái rỗng**, mỗi mục một câu: *"Chưa có lời mời nào."* · *"Bạn chưa có bạn bè nào. Mở trang chủ để xem
bài công khai và kết bạn với tác giả."* · *"Bạn chưa gửi lời mời nào."* Câu thứ hai trỏ về feed gợi ý — đường gặp người
khác duy nhất trước GĐ6 (Đ-4.6).

### Test (`features/friend/friends-screen.test.tsx`)

- Ba mục gọi đúng ba URL (`direction=incoming`, `direction=outgoing`, `/friends`).
- Chấp nhận → thẻ biến mất khỏi "Lời mời đến" **và** mục Bạn bè có lượt `GET /friends` mới.
- 403 Chấp nhận → câu Q-E3, thẻ biến mất.
- "Xem thêm" nối trang theo `nextCursor`, không lặp thẻ khi bấm hai lần nhanh.
- Trang ngắn hơn `limit` mà `nextCursor ≠ null` → nút "Xem thêm" **vẫn hiện**.
- **Đúng một** ca `<StrictMode>` cho cả màn; thêm `friends-screen` vào danh sách luật frontend Mục 9.
- `waitFor` chờ handler có `delay` → ghi `timeout` tay (luật frontend Mục 9 — bài học `user-posts`).

### Cạm bẫy đã biết

| Cạm bẫy | Triệu chứng | Chặn bằng |
|---|---|---|
| Thẻ dùng `userId` làm `key` ở cả ba mục rồi gộp một danh sách | Người vừa chấp nhận xuất hiện hai lần với cùng `key` | Ba danh sách tách, mỗi cái một hook |
| Chấp nhận xong tự chèn thẻ vào mục Bạn bè với `since = now()` | Thời điểm lệch server; lặp khi nạp lại | Nạp lại mục Bạn bè, không tự dựng `FriendCard` |
| Khóa cả màn khi một thẻ đang chờ | Người có 20 lời mời phải đợi từng cái | Khóa theo thẻ |

### Kết quả mong đợi

Vitest xanh; màn chưa có route (ráp ở `E5`).

---

## 5. E4 — Feed trang chủ

### Mục tiêu

`GET /feed` thành trang chủ: đúng SEQ-03 phía client, không bao giờ màn trắng, và **không tự dựng lại luật của server** —
không đoán "hết bài" từ độ dài trang, không đoán "gợi ý" từ tác giả.

### Các bước

**Bước 1 — `features/feed/use-feed-page.ts`**: bọc `useCursorPages` (Q-E8) với `fetchPage = (cursor, signal) =>
contentApi.feed({ cursor, limit: PAGE_SIZE }, signal)`, `getId = p => p.postId`, ngữ cảnh lỗi `feed`. Giữ thêm `mode` của
**trang đầu** — trang sau không đổi nhãn giữa chừng. Bấm "Làm mới" hoặc Thử lại → key mới → `mode` đọc lại.

**Bước 2 — `features/feed/feed-list.tsx`**, props `renderPost` (Q-E6). Thứ tự vẽ:

| Trạng thái | Vẽ |
|---|---|
| Trang đầu chưa về | Skeleton ba thẻ (khuôn `PostListSkeleton` — chép, không import) |
| Trang đầu lỗi 503 **mang `type` `feed-overloaded`** (Q-E4) | Thẻ "Bảng tin đang quá tải. Vui lòng thử lại sau ít phút." + **Thử lại**. `data-testid="feed-overloaded"` |
| Trang đầu lỗi khác — kể cả 503 BFF mất kho phiên, 503 HTML của apache | `FormAlert` với `errorMessage("feed", e)` + **Thử lại** |
| `mode = suggested` | Dải nhãn **trên** danh sách: *"Gợi ý cho bạn — kết bạn để thấy bài của bạn bè"* (`Badge` hoặc `Alert` của kit, token màu). `data-testid="feed-suggested"` |
| `mode = network`, trang đầu `items: []`, `nextCursor: null` | *"Chưa có bài nào — kết bạn hoặc theo dõi để thấy bài."* |
| `mode = suggested`, rỗng | *"Chưa có bài công khai nào."* — hệ thống mới tinh, hiếm nhưng có |
| Có bài | `items.map(p => <Fragment key={p.postId}>{renderPost(p, next => replaceItem(p.postId, next))}</Fragment>)` |
| `nextCursor !== null` | Sentinel, **không** nút (Q-E5 chốt). Tới trần 5 trang rỗng liên tiếp → nút **Xem tiếp** |
| Tự nối trang | Vùng `role=status` ẩn: "Đã tải thêm N bài, đang hiển thị M bài." |
| `nextCursor === null`, có bài | "Bạn đã xem hết" |
| Lỗi ở trang sau | Danh sách giữ nguyên + dòng lỗi + nút **Thử lại** cuối danh sách — nạp lại **đúng lô hỏng** (`loadMore`, cùng `nextCursor`), không nạp từ đầu; observer ngừng tự nạp tới khi lô đó về |

Nút **Làm mới** ở đầu feed: gọi `reload()`. Đây là thay thế cho realtime (Mục 2, ngoài phạm vi) — rẻ, và là thứ người vừa
kết bạn bấm để thấy bài bạn mới.

**Bước 3 — observer (Q-E5).** Trong effect phụ thuộc `nextCursor`/`error`: tạo `IntersectionObserver` với
`rootMargin: "400px 0px"` (nạp trước khi chạm đáy), `observe(sentinel)`, cleanup `disconnect()`. Callback ghi
`isIntersecting` vào ref rồi `loadMore()` nếu giao nhau. Sau mỗi lượt nạp **xong** (effect theo `items.length` +
`nextCursor`): sentinel còn giao nhau + còn trang + chưa quá 5 lượt rỗng liên tiếp → `loadMore()`.

**Bước 4 — không import `features/post/`.** Skeleton, thẻ rỗng, nút — dựng bằng kit trong `features/feed/`. ESLint đã chặn
import chéo (`@/features/*` trong khối `features/**`); `lint` đỏ là tín hiệu đúng, không tắt luật.

### Test (`features/feed/feed-list.test.tsx`)

`jsdom` **không có** `IntersectionObserver`. Thêm một stub điều khiển được vào `test/setup.ts` (harness, không phải ca
test — hợp luật frontend Mục 9): lớp giả ghi lại các observer, và hàm `kichHoatGiaoNhau(isIntersecting)` để test tự bắn.
Test mặc định **không** bắn gì — ca nào cần cuộn thì bắn tay.

| Ca | Khẳng định |
|---|---|
| Trang đầu | Skeleton → 20 bài qua `renderPost` (đếm lời gọi `renderPost` giả) |
| Cuộn | Bắn giao nhau → lượt `GET /feed?cursor=…` với **nguyên** chuỗi `nextCursor` |
| **Trang rỗng mà `nextCursor ≠ null`** | Sau lượt về, sentinel vẫn giao nhau → **tự** nạp trang tiếp, không cần bắn lại (Q-E5 cạm bẫy) |
| Trần 5 lượt rỗng | Handler trả trang rỗng mãi → dừng sau 5 lượt, nút **Xem tiếp** hiện (Q-E5 chốt) |
| `nextCursor = null` | Không sentinel, không nút, "Bạn đã xem hết" |
| `mode = suggested` | `feed-suggested` hiện; `mode = network` thì không |
| `network` rỗng | Câu mời kết bạn |
| 503 trang đầu | `feed-overloaded` + Thử lại; **không** "Mã tra cứu"; bấm Thử lại → gọi lại |
| 503 trang sau | Bài cũ còn, dòng lỗi + Thử lại, observer **không** tự gọi thêm khi bắn giao nhau; Thử lại nạp đúng lô hỏng |
| `onChanged(null)` | Bài biến khỏi feed |
| **`<StrictMode>`** (đúng một ca) | Trạng thái cuối: đủ bài trang đầu, không lặp; **không** đếm request (L5) |

### Cạm bẫy đã biết

| Cạm bẫy | Triệu chứng | Chặn bằng |
|---|---|---|
| `hasMore = items.length === limit` | Hydrate lọc bớt một bài → feed "hết" ở trang 1 dù còn trăm bài (Đ-4.9) | Chỉ `nextCursor`; ca trang ngắn |
| Observer không bắn lại khi sentinel vẫn trong khung nhìn | Feed đứng im sau trang rỗng | Q-E5, ca trang rỗng |
| `useRef(new IntersectionObserver(…))` | StrictMode: observer của lần mount đầu bị `disconnect`, lần mount hai dùng lại nó → không bao giờ cuộn | Tạo trong effect; ESLint; ca StrictMode |
| Suy `suggested` từ "tác giả không phải bạn" | FE tự dựng lại luật Đ-4.6, lệch ngay khi server đổi | Chỉ đọc `mode` |
| Tự thử lại 503 theo `Retry-After` | Nghìn người cùng thử lại đúng giây thứ 5 — dồn tải đúng lúc server đang quá tải | Đ-4.10: chỉ nút |
| Đổi nhãn gợi ý giữa chừng khi trang 2 về `mode` khác | Nhãn nhảy khi cuộn | Giữ `mode` của trang đầu |

### Kết quả mong đợi

Vitest xanh, gồm stub observer ở harness và ca StrictMode; `features/feed/` không có dòng `@/features/`.

---

## 6. E5 — Slot và ráp ở `app/`

### Mục tiêu

Ghép bốn feature ở tầng duy nhất được biết cả hai phía, và để lại **một dòng ráp** cho GĐ3 cắm thanh cảm xúc.

### Các bước

**Bước 1 — trang chủ (Q-E1).** Xóa `app/page.tsx`. Tạo `app/(app)/(with-profile)/page.tsx` (client — cần `renderPost`
là hàm):

```tsx
"use client"
// Trang chủ = feed (GĐ4 E5). Chỗ DUY NHẤT biết cả `features/feed` lẫn `features/post` (Đ-4.16).
// GĐ3 cắm thanh cảm xúc vào đúng dòng `renderPost` này — không chạm `features/feed/` (Mục 9.1 #3).
export default function HomePage() {
  return (
    <FeedList
      action={<ComposeButton />}
      renderPost={(post, onChanged) => <PostItem post={post} onChanged={onChanged} />}
    />
  )
}
```

Đổi mặc định `safeNext` và đích của `onboarding.tsx` về `"/"`; sửa khẳng định test liên quan (Q-E1).

**Bước 2 — `/friends`.** `app/(app)/(with-profile)/friends/page.tsx` chỉ `return <FriendsScreen />`.

**Bước 3 — hồ sơ người khác (Q-E2).** `PublicProfile` thêm `actions?: ReactNode`, vẽ **chỉ** ở nhánh đã nạp được hồ sơ
(cạnh tên) — người không tồn tại (`profile-not-found`) thì không có nút Kết bạn nào để bấm vào hư không.
`users/[userId]/page.tsx` thành client component, truyền `RelationshipButtons` trừ khi `profile.userId === userId`.

**Bước 4 — header (Q-E7).** `AppHeader` thêm `nav?: ReactNode`; `app/(app)/layout.tsx` truyền ba `Link`.

**Bước 5 — lượt tay trên `localhost`**, hai cửa sổ (một thường, một ẩn danh), hai tài khoản dev:

```
B đăng bài public + bài friends
A (mới): / → nhãn gợi ý, thấy bài public của B, KHÔNG thấy bài friends
A bấm tên B → hồ sơ B → Kết bạn → nút thành "Đã gửi lời mời"
B: /friends → Lời mời đến có A → Chấp nhận → A sang mục Bạn bè
A: / → Làm mới → không còn nhãn gợi ý; thấy CẢ HAI bài của B
A: hồ sơ B → Hủy kết bạn → xác nhận → / → Làm mới → bài friends biến mất; nhãn gợi ý TRỞ LẠI (hết kết nối, Đ-4.6)
A: hồ sơ B → Theo dõi → / → bài public của B quay lại, bài friends thì không
A: /users/{id của A} → không có nút quan hệ
```

*Sửa 2026-09-23:* dòng hủy kết bạn bản đầu ghi "bài public cũng mất" — mâu thuẫn Đ-4.6: A hết kết nối thì feed về
`suggested`, bài public của B **có thể** hiện lại dưới nhãn gợi ý. Khẳng định đúng là bài `friends` mất và nhãn gợi ý trở lại.

Dòng thứ năm canh **dấu nguồn** của cache trang đầu (`FEED-07b`): A vừa có kết nối đầu tiên thì trong 30s vẫn phải thấy
`network`, không phải feed gợi ý đã cache. Đỏ ở đây là lỗi backend (`C4`) — dừng lại, không vá ở FE.

### Test

- `public-profile.test.tsx`: slot `actions` hiện ở nhánh đã nạp, **không** hiện ở nhánh 404 và nhánh lỗi. Các ca cũ xanh
  **không** sửa khẳng định.
- `safe-next.test.ts`: mặc định mới `"/"` (khẳng định đổi có chủ đích — ghi trong thân commit).
- `pnpm build` xanh — lỗi trùng route `/` chỉ lộ ở `build`, không lộ ở `typecheck`.

### Cạm bẫy đã biết

| Cạm bẫy | Triệu chứng | Chặn bằng |
|---|---|---|
| Quên xóa `app/page.tsx` | `pnpm build` đỏ: hai trang giải về `/` | Q-E1; `build` trong cổng |
| Trang chủ đặt ngoài `(with-profile)` | Người chưa có hồ sơ thấy feed, bấm Kết bạn → 404 vì chính họ chưa "tồn tại" (Đ-2.4) | Đặt dưới `(with-profile)` |
| `AppHeader` import `@/features/…` để tự vẽ liên kết | Vi phạm Đ-E13; ESLint đỏ | Slot `nav` |
| Spec Playwright cũ chờ URL `/me` sau đăng nhập | Cả bộ E2E đỏ ở `E6` dù không spec nào sai logic | Rà `e2e/` ngay trong commit này (Q-E1) |

### Kết quả mong đợi

`pnpm lint`, `typecheck`, `test`, `build` xanh cả bốn; lượt tay Bước 5 đi hết, ghi kết quả vào "Thực tế thi công".

---

## 7. E6 — Vitest + Playwright

### Mục tiêu

Vitest chặn merge (CI); Playwright là bằng chứng chạy tay có ghi lại cho lát cắt **hai tài khoản** — thứ Vitest không nói
được vì nó không có backend thật.

### Các bước

**Bước 1 — rà Mục 10.6 dòng 1 và 2** với ca đã viết ở `E1`–`E5`; dòng nào thiếu thì bổ sung, ghi vào bảng ở "Thực tế thi
công".

**Bước 2 — helper `e2e/friend-helpers.ts`:** `ketBanApi(request, a, b)` (A gửi, B chấp nhận — hai lời gọi API thật với
token của từng người, qua `dangNhapApi` đã có), `taoBaiApi` dùng lại từ `post-helpers.ts`.

**Bước 3 — `e2e/friend-feed.spec.ts`**, hai `BrowserContext` (A và B), `workers: 1`:

```
giuHanMucAuth(n)          ← đếm đủ lượt register + verify + login API + login UI của CẢ HAI tài khoản
B: tạo tài khoản có hồ sơ, đăng bài public "P" và bài friends "F" (API)
A: tạo tài khoản có hồ sơ (API), đăng nhập UI ở context A
A: / → feed-suggested hiện; thấy "P"; không thấy "F"
A: bấm tên B trên bài "P" → /users/{B} → Kết bạn → thấy "Đã gửi lời mời"
B: đăng nhập UI ở context B → /friends → Chấp nhận A
A: / → Làm mới → feed-suggested KHÔNG hiện; thấy "P" và "F"
A: /users/{B} → Hủy kết bạn → xác nhận → / → Làm mới → không thấy "F"
```

Dọn rác trong `afterEach` như `post-forbidden.spec.ts`. Feed gợi ý trên DB dev chứa bài công khai của **mọi** lượt chạy
trước — không khẳng định "P" là bài đầu tiên, chỉ khẳng định nó **có mặt** (tìm theo nội dung có hậu tố ngẫu nhiên; bài
dev dồn nhiều thì cuộn tới nó hoặc giới hạn khẳng định ở trang đầu — "P" mới nhất nên nằm trang đầu).

**Bước 4 — chạy cả bộ** `pnpm test:e2e`. Không chỉ spec mới: GĐ2 từng có bốn spec đỏ suốt ba commit vì mỗi đầu việc chỉ
chạy spec của chính nó (checklist GĐ2 Mục 16). Ở GĐ4 lý do còn mạnh hơn — Q-E1 đổi nơi hạ cánh sau đăng nhập.

### Cạm bẫy đã biết

| Cạm bẫy | Triệu chứng | Chặn bằng |
|---|---|---|
| Hai tài khoản đăng nhập vượt hạn mức 10/phút theo IP | 429 giữa spec | `giuHanMucAuth` khai **đủ** lượt của cả hai |
| Dùng một context cho hai người | Cookie `__Host-sid` của người sau đè người trước | Hai `browser.newContext()` |
| Khẳng định "F" biến mất **ngay** mà không Làm mới | Trang đang mở không tự nạp lại — lỗi của test, không phải của app | Làm mới / `goto("/")` rồi mới khẳng định |
| `retries: 1` che ca flaky | Ca đỏ rồi xanh không ai nhìn | Dán cả dòng `flaky` vào PR như `passed`/`failed` |

### Kết quả mong đợi

Số Vitest trước → sau; `pnpm test:e2e` cả bộ xanh; bản Chrome; tất cả vào thân commit và mô tả PR.

---

## 8. F1 — Deploy staging qua CD

### Mục tiêu

Bốn module lần đầu chạy cùng nhau trên server thật, qua đường CD chính thức (`deploy-staging.yml`, chạy khi `develop`
nhận push) — **không** SSH sửa tay.

### Trước khi merge — mục "Trước khi merge" của mô tả PR

| Mục | Nội dung ghi vào PR |
|---|---|
| Key `.env` | **Không có key mới.** Hai công tắc `Feed:SourceCache:Enabled`, `Feed:PageCache:Enabled` mặc định bật; trần pool Postgres 80 là mặc định trong code (`PostgresPool`); khóa ký JWT của môi trường đo **không bao giờ** lên server (Đ-4.13) |
| Migration EF | **Hai migration mới:** `20260921165316_InitialSocialGraph` (schema `socialgraph`: `friendships`, `follows`, `idx_friendships_user_max`) · `20260921170351_PublicRecentIndex` (Content: `idx_posts_public_recent`). `CREATE INDEX` khóa ghi `posts` trong lúc dựng — vô hại ở quy mô staging (Mục 4 cạm bẫy 4) |
| Thao tác tay trên VPS | Không |

PR `loveart1210` → `develop` **chỉ mở khi được bảo**, và người trong đội bấm merge (luật PR Mục 2). Agent không
`gh pr merge`.

### Các bước sau merge

1. CD build hai image arm64 → GHCR → service `migrate` → `up`. Log `migrate` phải có **bốn** schema, thứ tự Identity →
   Profile → Content → SocialGraph (Mục 5).
2. Trên VPS (`~/app/deploy`, `C="docker compose -f docker-compose.staging.apache.yml"`):

   ```bash
   $C exec postgres psql -U socialapp -d socialapp -At -c "
     select table_schema from information_schema.tables
     where table_name = '__EFMigrationsHistory' order by 1;"
   # → content, identity, profile, socialgraph
   ```
3. `curl -fsS https://mxh.banhgao.net/health/ready` → 200.
4. `curl -fsS https://mxh.banhgao.net/swagger/socialgraph-v1/swagger.json | head -c 200` → JSON;
   `curl -fsS https://mxh.banhgao.net/swagger/content-v1/swagger.json | grep -c '"/api/v1/feed"'` → ≥ 1.

### Cạm bẫy đã biết

| Cạm bẫy | Triệu chứng | Chặn bằng |
|---|---|---|
| `migrate` chưa nối SocialGraph | Api chạy, mọi bài `friends` → 500 `relation "socialgraph.friendships" does not exist` | Bước 1 đọc log, Bước 2 psql |
| Staging chạy image cũ còn `AlwaysStrangers` (DI-01) | Bài `friends` của bạn không hiện — `F2` đỏ ở bước "thấy F" | Kiểm tag image của CD run khớp commit merge |
| Kiểm `/swagger` bằng trình duyệt đã cache | Thấy bản cũ | `curl` |

---

## 9. F2 — E2E lát cắt trên staging, hai tài khoản

### Mục tiêu

Điều kiện 1 của B.11. Ba cơ chế chỉ đúng khi **cùng** chạy trên hạ tầng thật: `IFriendshipReader` thật (Đ-4.3), dấu nguồn
của cache trang đầu (Đ-4.8, sửa 2026-09-22), xóa cache nguồn **sau** `COMMIT` (Đ-4.15).

### Kịch bản (một người, hai hồ sơ Chrome — Mục 9.4)

```
Hồ sơ 2 (B, tài khoản staging của nhóm): đăng bài public "P-<ngày>" + bài friends "F-<ngày>"
Hồ sơ 1 (A, tài khoản MỚI: đăng ký qua UI → mail thật → xác minh → đăng nhập → onboarding)
  → trang chủ: nhãn gợi ý, có "P", không có "F"                                   [ảnh 1]
  → bấm tên B → Kết bạn
B: /friends → Chấp nhận
A: Làm mới → không nhãn gợi ý, có "P" và "F"                                     [ảnh 2]
A: hồ sơ B → Hủy kết bạn → trang chủ → Làm mới → không còn "F"                     [ảnh 3]
```

Tài khoản mới vì "người chưa có kết nối" là điều kiện của feed gợi ý (Đ-4.6) — tài khoản đội đã kết bạn nhau thì không bao
giờ thấy nhãn.

### Bằng chứng **phải giữ** (dán vào PR)

| # | Bằng chứng | Phải thấy gì |
|---|---|---|
| 1 | Ảnh trang chủ của A lúc mới | Nhãn "Gợi ý cho bạn…", bài "P" |
| 2 | Ảnh trang chủ sau khi chấp nhận | Không nhãn; có "F" với nhãn riêng tư "Bạn bè" |
| 3 | Ảnh sau hủy kết bạn | Không còn "F" |
| 4 | Bảng tab Network của cả lượt (khuôn `F2` GĐ2) | Origin: chỉ `mxh.banhgao.net` (`/bff/*`, trang, RSC) + `GET` ảnh R2 + hai script Cloudflare đã chấp nhận ở Đ-E15; 0 header `Authorization`; 0 JWT trong body; Local/Session Storage = 0; `Set-Cookie` chỉ `__Host-sid` |
| 5 | Bản Chrome + thời điểm chạy | `chrome://version` |

Chạy bằng script Playwright (`channel: "chrome"`) ghi mọi request như GĐ2 thì **không commit** script đó (Mục 12:
`F1`–`F2` không sinh commit code). Che email và id người dùng trong ảnh.

### Nếu đỏ — loại trừ theo thứ tự, rẻ trước

| Triệu chứng | Nghi phạm | Kiểm |
|---|---|---|
| Sau khi chấp nhận, A vẫn thấy nhãn gợi ý quá 30s | Dấu nguồn `fp` không so, hoặc cache nguồn chưa bị xóa sau accept | `redis-cli GET feed:p1:{A}` — xem `fp`; `TTL sg:feed-sources:{A}` ngay sau accept phải là `-2` (đã xóa) |
| A thấy nhãn `network` nhưng không có "F" | DI-01 — host dùng `AlwaysStrangers` | `F1` kiểm image; log khởi động |
| "F" còn sau hủy kết bạn | Xóa cache **trước** `COMMIT`, hoặc hydrate không kiểm lại BR-02 | Không vá ở FE — mở lại `C1`/`C3`, ghi lỗi vào "Thực tế thi công" |
| 401 lặp / về `/login` giữa lượt | Phiên BFF (Redis phiên) | Log `web`, không phải lỗi GĐ4 |

**Không** dừng Redis trên staging để thử degrade: dừng Redis là **đăng xuất mọi phiên BFF** (GĐ2 `F4`). Degrade đã có
bằng chứng ở k6 lượt (3) trên môi trường đo.

---

## 10. F3 — Checklist Mục 12 + Definition of Done Mục 11

### Mục tiêu

Tick từng dòng của `giai-doan-4.md` Mục 11 và Mục 12 **kèm bằng chứng tại dòng**. Dòng cần server mà chưa có thì ghi
"chờ server" + lệnh — **không xóa dòng**, không tick bằng suy luận (nếp `F4` GĐ2).

### Dòng nào lấy bằng chứng ở đâu

| Dòng | Nguồn bằng chứng | Trạng thái lúc viết hướng dẫn |
|---|---|---|
| Mục 11 — đủ AC (Mục 10.1, 10.2 xanh) | CI run của PR: `Test (unit + integration)` | Có từ `B3`, `B4` |
| Mục 11 — US-008 AC-04 sơ bộ | `bao-cao-k6-so-bo.md`: lượt (2) p95 37,6 ms, 0 % lỗi | **Đạt** — tick, trỏ link |
| Mục 11 — RBAC + tầng 3; matrix + `READ-06b`; bảng đột biến `B3` | CI `AuthZ matrix` 24/24; bảng đột biến trong hướng dẫn B+C+D Mục 17.4 | Có |
| Mục 11 — RFC 7807, `errors` đúng key; 403 accept một phản hồi | Rà `D7` trong thân commit `fdb3662` | Có |
| Mục 11 — chạy thử trên staging hai tài khoản | `F2` | Chờ `F2` |
| Mục 11 — hợp đồng khớp Swagger; `gen:api` sạch | CI `API contract` + `API types khop hop dong` | Có |
| Mục 11 — không lộ secret/PII | Tự rà B.9 mục 5 (`42df85b`) + log staging (lệnh dưới) | Nửa — log chờ server |
| Mục 12 — schema `socialgraph`, `--migrate` hai lần | `F1` Bước 2 + chạy `migrate` lần hai | Chờ server |
| Mục 12 — không FK chéo schema | psql (lệnh dưới) | Chờ server |
| Mục 12 — `EXPLAIN` feed đúng hình dạng; `EXPLAIN` trước/sau Đ-4.11 | "Thực tế thi công" `C2` trong hướng dẫn B+C+D; thân commit `a1bbede` | Có |
| Mục 12 — test khởi động; thử đỏ `requester_id` + `AlwaysStrangers` | Bảng đột biến `B3`/`B4` | Có |
| Mục 12 — Redis đo: không `X-Amz-Signature`/`myReaction` trong `feed:p1:*` | `redis-cli` trên môi trường đo (`C6`) | Kiểm lại nếu chưa ghi |
| Mục 12 — báo cáo k6; lượt Redis dừng < 1 % lỗi | `bao-cao-k6-so-bo.md` (0 % lỗi) | Có |
| Mục 12 — lát cắt dọc | `F2` | Chờ `F2` |

### Lệnh cho các dòng "chờ server"

```bash
# Mục 12 — migrate lần hai: exit 0, không áp migration nào mới
$C run --rm migrate; echo "exit=$?"

# Mục 12 — FK chéo schema (phải ra 0) — lệnh của GĐ2 F4, giờ phủ cả socialgraph
$C exec postgres psql -U socialapp -d socialapp -At -c "
  select count(*) from information_schema.referential_constraints rc
  join information_schema.table_constraints a on a.constraint_name=rc.constraint_name and a.constraint_schema=rc.constraint_schema
  join information_schema.table_constraints b on b.constraint_name=rc.unique_constraint_name and b.constraint_schema=rc.unique_constraint_schema
  where a.table_schema<>b.table_schema;"

# Mục 11 — log api: 0 URL đã ký
$C logs api | grep -c "X-Amz-Signature"
```

### Kèm theo

Ghi ngược **L1** (Mục 0.6) vào `giai-doan-4.md` trong commit này: Mục 11 "Tick ở `F4`" → `F3`; "(`F3`)" của dòng staging
và dòng lát cắt dọc → `F2`.

---

## 11. F4 — Đóng băng + bàn giao

### Các bước

1. **Đóng băng.** Thêm khối chú thích `ĐÓNG BĂNG (<ngày> …)` vào đầu `socialgraph-v1.yaml` (khuôn `identity-v1.yaml`,
   `profile-v1.yaml`), và một khối tương tự **ngay trên** path `/feed` trong `content-v1.yaml` — phần còn lại của
   `content-v1` đã đóng băng từ GĐ2 và GĐ3 sẽ mở lại theo luật **chỉ-thêm**. Chỉ comment: `pnpm gen:api` → `git status`
   rỗng; `dotnet test --filter Category=Contract` xanh.
2. **Hoãn có địa chỉ.** Rà bảng Mục 2 của `giai-doan-4.md` với "Thực tế thi công" của các hướng dẫn khối: nợ nào chỉ nằm
   trong thân tài liệu (ví dụ: hook chung chưa áp cho `use-post-page.ts` — Q-E8; mục cắt ở Mục 0.5 nếu có) thì thêm dòng
   vào bảng, kèm giai đoạn nhận.
3. **Bàn giao cho GĐ3** (L2 — chạy ngay sau), ghi vào `giai-doan-3.md` nếu chưa có:
   - Ba chỗ cắm (Mục 9.1): hàm hydrate `PostHydrator` (chữ ký ở hướng dẫn B+C+D Mục 18) · `FEED-13` hai người xem · dòng
     `renderPost` của `app/(app)/(with-profile)/page.tsx`.
   - Hai việc phải làm lại: k6 lượt (2) sau khi `myReaction` vào đường hydrate; `READ-CMT-*` / `READ-REACT-*` với bài
     `friends` giữa hai người là **bạn thật**.
4. **Bàn giao cho GĐ5** (B.8): `IFriendshipReader` thật (BR-09 chat chỉ giữa bạn bè), khuôn event sau `COMMIT`
   (`FriendRequestSent`, `FriendRequestAccepted`), báo cáo k6 làm mốc so sánh.
5. **Xác nhận ba điều kiện B.11**, mỗi điều một link:

   | # | Điều kiện | Bằng chứng |
   |---|---|---|
   | 1 | Hai tài khoản thật đi hết vòng trên staging | `F2` ảnh 1–3 |
   | 2 | Báo cáo k6 ba lượt trên 1M bài | `bao-cao-k6-so-bo.md` |
   | 3 | CI xanh cả năm nhóm; cổng `socialgraph-v1` đã từng đỏ; matrix + `READ-06b` đã từng đỏ | CI run của PR; `bcd507d` (B5 thử đỏ ba kiểu); bảng đột biến `B3` |

6. **GĐ3 được phép bắt đầu.**

---

## 12. Kế hoạch commit

Scope `gd4-e` cho khối E, `gd4-f` cho khối F (luật commit Mục 3). Mọi commit có dòng `Test:` (số Vitest trước → sau) và dòng
`detect-changes:` (`node .gitnexus/run.cjs detect-changes --scope all --repo .` trước **mọi** commit; `partial`/`truncated`
không phải kết quả sạch). Commit chạm code FE qua đủ bốn cổng `lint`/`typecheck`/`test`/`build` (luật frontend Mục 11).

| # | Tiêu đề đề xuất | Ghi chú thân bài |
|---|---|---|
| 1 | `feat(gd4-e): E1 — client quan hệ và bảng tin, năm ngữ cảnh lỗi theo endpoint` | **Lệch B.7** (Q-E3, L3); nếu chọn Q-E8 hook chung: dòng `hooks/` trong luật frontend Mục 2 + luật ESLint đã thử đỏ |
| 2 | `feat(gd4-e): E4 — feed cuộn theo nextCursor, nhãn gợi ý, 503 thành thử lại` | Stub `IntersectionObserver` ở `test/setup.ts`; `feed-list` vào danh sách StrictMode (L5) |
| 3 | `feat(gd4-e): E2 — nút kết bạn và theo dõi vẽ theo phản hồi server` | L5 cho `relationship-buttons` |
| 4 | `feat(gd4-e): E3 — màn bạn bè: lời mời đến, lời mời đã gửi, danh sách bạn` | L5 cho `friends-screen`; nếu cắt mục đã gửi thì ghi ở đây và vào B.9 |
| 5 | `feat(gd4-e): E5 — trang chủ là feed, nút quan hệ trên hồ sơ, liên kết bạn bè` | **Lệch Đ-4.16** (Q-E6, L4) ghi vào `giai-doan-4.md`; Q-E1 đổi nơi hạ cánh — nêu rõ test/spec **đổi khẳng định có chủ đích**; impact analysis `safeNext`, `PublicProfile`, `AppHeader` |
| 6 | `test(gd4-e): E6 — Playwright hai tài khoản: kết bạn, bài friends hiện rồi biến mất` | Kết quả cả bộ E2E + **bản Chrome** |
| 7 | `docs(gd4-f): F3 + F4 — tick Mục 11 và Mục 12, đóng băng socialgraph-v1 và /feed` | L1, L2; `F1`–`F2` không sinh commit code — bằng chứng nằm ở mô tả PR |

**Nhánh và PR:** commit trên `loveart1210`, push khi được bảo. Một PR vào `develop` cho **cả GĐ4** khi `E6` xong và **chỉ
khi được bảo** (Mục 9.3; luật PR Mục 2). Mô tả theo khuôn luật PR Mục 4, mục "Ảnh màn hình" bắt buộc (PR chạm
`src/frontend/`): trang chủ gợi ý, trang chủ mạng lưới, hồ sơ có nút, `/friends` — sáng và tối nếu đổi token màu (GĐ4 không
đổi). **Không bút ký** ở cả commit lẫn mô tả PR.

---

## 13. Checklist nghiệm thu hai khối

**Khối E**

- [ ] Không `fetch` ngoài `lib/api/http.ts`; không `localStorage`/`sessionStorage`/`document.cookie`
- [ ] Không route BFF mới — `git diff --stat develop -- src/frontend/app/bff` rỗng
- [ ] Kiểu API lấy từ `lib/api/types.ts`; `grep -rnE "type \w+(Response|Page|Card) = \{" src/frontend/features src/frontend/lib/api/*.ts` rỗng
- [ ] `grep -rn "@/features/" src/frontend/features/feed src/frontend/features/friend` rỗng (Đ-E13, Đ-4.16)
- [ ] `grep -rnE "items\.length\s*(<|===|>=)\s*(limit|PAGE_SIZE)" src/frontend/features src/frontend/hooks` rỗng (Đ-4.9)
- [ ] Không `useRef(new …)` — ESLint xanh là đủ, cổng đã thử đỏ ở GĐ2
- [ ] Ba ca `<StrictMode>` mới (`feed-list`, `relationship-buttons`, `friends-screen`), đã ghi vào luật frontend Mục 9 (L5)
- [ ] UI dùng kit và token; không màu thô; không component kit mới (nếu có thì `pnpm exec shadcn add`, bản ghim)
- [ ] `pnpm lint`, `pnpm typecheck`, `pnpm test`, `pnpm build` xanh cả bốn
- [ ] **`pnpm test:e2e` chạy CẢ BỘ, xanh**, kèm bản Chrome
- [ ] `pnpm gen:api` xong worktree sạch
- [ ] Lệch L3, L4, L5 đã ghi ngược, mở bằng "Lệch …"
- [ ] Không `console.log` token, `nextCursor`, URL đã ký

**Khối F**

- [ ] Mô tả PR: "Không có key `.env` mới"; hai migration EF nêu tên; không thao tác tay
- [ ] `migrate` xanh cho **bốn** schema; chạy lần hai không đổi gì
- [ ] `/health/ready` 200; `socialgraph-v1/swagger.json` 200; `content-v1` có `/feed`
- [ ] Năm bằng chứng `F2` trong PR, email và id đã che
- [ ] Mục 11 + Mục 12 tick hết hoặc "chờ server" kèm lệnh; L1 đã sửa
- [ ] `socialgraph-v1` + `/feed` tuyên bố đóng băng; `gen:api` sạch; `Category=Contract` xanh
- [ ] Bàn giao GĐ3 (ba chỗ cắm + hai việc làm lại) và GĐ5 đã ghi (L2)
- [ ] Mô tả PR **sạch bút ký**; không giá trị secret

---

## 14. Hai khối để lại gì

| Di sản | Ai thừa hưởng |
|---|---|
| Dòng `renderPost` ở trang chủ — chỗ ráp duy nhất biết feed + post | **GĐ3** — thanh cảm xúc cắm vào đây, `features/feed/` không đổi |
| `useCursorPages` (nếu chốt Q-E8) + khuôn "không suy hết từ độ dài trang" + observer có xử sentinel còn trong khung nhìn | **GĐ5** (lịch sử hội thoại, danh sách hội thoại), **GĐ6** (danh sách thông báo) |
| `RelationshipButtons` + năm ngữ cảnh lỗi quan hệ | **GĐ5** — nút "Nhắn tin" chỉ hiện khi `friendship = friends` (BR-09) đặt cạnh cụm này |
| Màn `/friends` | **GĐ6** — thông báo "có lời mời" dẫn thẳng về đây |
| Nhãn gợi ý + câu trạng thái rỗng trỏ về feed | **GĐ6** — tìm kiếm mở thêm đường; nhãn gợi ý giữ cho tài khoản mới |
| Hợp đồng `socialgraph-v1` đóng băng | GĐ5, GĐ6 — mở lại theo luật chỉ-thêm |
| Bằng chứng hai tài khoản trên staging | Cả dự án: BR-02 mức `friends` lần đầu chạy thật trước mắt người dùng |

---

## 15. Ranh giới — cái gì **không** thuộc hai khối này

| Việc | Thuộc về |
|---|---|
| Sửa bất cứ gì trong `src/backend/**` | Khối A–D — đã đóng. Hợp đồng lệch thì **dừng** (Mục 1.2 luật 3); lỗi backend lộ ở `F2` thì mở lại đầu việc `C*`/`D*` tương ứng, không vá ở FE |
| Thêm route dưới `app/bff/**` | Không ai — proxy chung đủ cho mười endpoint (Đ-E14) |
| Tìm người để kết bạn, gợi ý kết bạn, bạn chung | **GĐ6** (UC-16) / ngoài MVP |
| Thông báo lời mời, huy hiệu số lời mời trên header | **GĐ6** — header GĐ4 chỉ có liên kết, không đếm |
| Danh sách bạn của người khác | Ngoài MVP (Mục 2) |
| Nút cảm xúc, bình luận trong feed | **GĐ3** |
| Đẩy bài mới vào feed đang mở | Ngoài MVP — nút Làm mới thay thế |
| Chuyển `use-post-page.ts` sang hook chung | Nợ có địa chỉ (Q-E8) |
| Dừng Redis trên staging để thử degrade | Không ai — đã chứng minh ở k6 lượt (3) (Mục 9) |
| Playwright vào CI | Không ở GĐ4 (Đ-E8, Mục 10.5 điểm 7) |

---

## Thực tế thi công

*Ghi khi làm, mỗi đầu việc một mục `### <mã> — <ngày>`: câu Q-E* đã chốt thế nào, số test trước → sau, chỗ lệch mới,
bằng chứng. Nếp của hướng dẫn B+C+D.*

### E1 — 2026-09-23

**Câu đã chốt:** Q-E3 (năm ngữ cảnh) và Q-E8 (hook chung `hooks/`) — cả hai theo đề xuất, dòng *✅ chốt* ngay dưới câu hỏi.
Sáu câu còn lại chốt ở đầu việc dùng tới chúng.

**Đã làm:**

- `lib/api/types.ts`: tám kiểu `socialgraph` + `FeedMode`/`FeedPage`, toàn alias — không khai tay dòng nào.
- `lib/api/page-query.ts`: `pageQuery` dời khỏi `content-api.ts`, thân hàm và chú thích nguyên vẹn. Impact: **LOW**, một
  người gọi (`contentApi.listUserPosts`).
- `lib/api/socialgraph-api.ts` (`socialGraphApi`): chín endpoint theo bảng Bước 4; `contentApi.feed` thêm vào `content-api.ts`.
  `listRequests` luôn gửi `direction` trước `cursor`/`limit`. Không route BFF mới.
- `lib/api/messages.ts`: năm ngữ cảnh, `FieldErrorKey` thêm `userId`, `direction`. Impact `ErrorContext` **LOW** (3 người
  gọi trực tiếp), `FieldErrorKey` **LOW** (1) — chỉ thêm thành viên union, không đổi câu cũ nào.
- `mocks/`: fixture `relationship()`, `userCard`, `friendCard`, `friendPage`, `feedPost`, `feedPage(mode, nextCursor)`; handler
  cho chín endpoint + `/feed`. Kịch bản chọn bằng dữ liệu nhập: `SOCIAL_SCENARIO` (năm id: `banBe`, `loiMoiDen`, `loiMoiDi`,
  `daCoLoiMoi` → 409, `khongTonTai` → 404) và ba cursor (`CURSOR_TRANG_RONG` → trang rỗng có `nextCursor`, `CURSOR_QUA_TAI` →
  503). Hỏi quan hệ / gửi lời mời / theo dõi với chính mình → 400 `errors.userId` bằng **đúng câu** của `SocialGraphErrors`;
  `direction` lạ → câu thật của `ListFriendRequestsQueryValidator`.
- Tầng `hooks/` (Q-E8): khối ESLint cấm import ngược thêm `hooks/**`; dòng mới ở luật frontend Mục 1 #10 và Mục 2,
  `src/frontend/AGENTS.md` mục 3, và mở rộng có ngày dưới Đ-E13. Chưa có hook nào — `use-cursor-pages.ts` viết ở `E4`.

**Chỗ lệch với file này:**

- *Fixture "người kia":* `example` của `socialgraph-v1` dùng `0192f3c1-…2a10` cho người kia — trùng `userId` (người đang
  đăng nhập) của `identity-v1`, tức quan hệ với chính mình, mà hợp đồng trả 400 ca đó. Người kia mặc định lấy tác giả trong
  `example` của `GET /feed` (`0192f3c0-…3b4c`, "Nguyễn Văn An").
- *Ghim kiểu feed:* `FeedPage`/`FeedMode` ghim ở `lib/api/content/schema.test-d.ts`, không ở `socialgraph/` như Mục "Test" ghi
  — kiểu sinh từ `content-v1`, hợp đồng đổi thì file của đúng hợp đồng đó đỏ.
- *Mock không giữ trạng thái:* gửi lời mời xong `GET /relationships` vẫn trả trạng thái gốc của id. `E2` cần "đọc lại ra sự
  thật khác" (sau 409) thì `server.use` riêng — ghi trong chú thích `SOCIAL_SCENARIO`.
- *`http.test.ts`:* thêm 9 ca dù Mục "Test" ghi "không cần ca mới" — cái không cần là ca `Content-Type` (đã có); chín ca mới
  canh method/đường (`follow` là `PUT`), `direction` luôn gửi, `encodeURIComponent`, cursor nguyên vẹn, và 503 đọc được
  Problem Details.
- *Cạm bẫy 503:* bản đầu ghi "handler trả `application/json`" — sai (đã sửa trong bảng cạm bẫy). Xem thử đột biến dưới.

**Thử đột biến** — năm đột biến bị bắt, một tương đương:

| Đột biến | Ca đỏ |
|---|---|
| `follow` gửi `POST` thay `PUT` | 2 ca `http.test.ts` (bảng method, ca `PUT` không body) |
| `listRequests("incoming")` bỏ `direction` | `listRequests LUÔN gửi direction` |
| Bỏ câu `feed` 503 khỏi bảng | `feed 503: câu quá tải, KHÔNG kèm Mã tra cứu` |
| Bỏ `encodeURIComponent` ở `relationship` | `userId luôn qua encodeURIComponent` |
| Handler 503 trả body HTML | `feed 503 là ApiError có Problem Details đọc được` |
| Handler 503 trả `application/json` thay `problem+json` | **Không đỏ — tương đương**: `toApiError` nhận mọi content-type có `json` |
| ESLint: `hooks/do-thu.ts` import `@/features/post/post-item`, `@/app/layout` | 2 lỗi `no-restricted-imports` (thông điệp mới); xóa file, `git status` như trước |

**Bằng chứng:** `pnpm gen:api` → không file sinh nào đổi. `lint`, `typecheck`, `build` xanh. Vitest **39 → 40 file, 453 → 478
ca** (+9 `messages.test.ts`, +9 `http.test.ts`, +4 `socialgraph/schema.test-d.ts` mới, +3 `content/schema.test-d.ts`).
Không file nào trong `features/` hay `app/` đổi.

### Q-E4 — 2026-09-23 (commit riêng, trước `E4`)

Chốt **khác đề xuất** (xem dòng ✅ dưới Q-E4): 503 phân biệt bằng `type` khai trong hợp đồng.

- **Backend (lệch Mục 1.2 luật 3, nhóm chốt):** `Error` thêm tham số cuối `Type` có mặc định — cùng nếp chỉ-thêm của Q-D4;
  `ResultHttpExtensions.Problem` truyền `type: error.Type`, factory vẫn điền `https://httpstatuses.io/{status}` khi `null`.
  `ContentErrors.FeedUnavailable` mang `FeedOverloadedType`. Impact: `Problem` LOW (3); `Error` UNKNOWN (tên trùng) — tìm
  chữ xác nhận không chỗ nào dùng `Error` theo vị trí (deconstruct / pattern), nên thêm tham số cuối biên dịch an toàn; đường
  ra của mọi lỗi ở 24 action, hành vi không đổi khi `Type = null` (ca đối chứng trong `ResultTests` khẳng định `type` mặc định).
- **Hợp đồng:** `content-v1.yaml` thêm schema `FeedOverloadedProblem` (`allOf` `ProblemDetails` + `type` enum một giá trị),
  response `ServiceUnavailable` trỏ vào nó. Không đổi tập status, không đổi required của request — `ContentContractTests`
  không so schema response. `pnpm gen:api` sinh `type: "urn:socialapp:problem:feed-overloaded"` (literal).
- **BFF:** `problem()` của `lib/bff/http.ts` thêm tham số cuối `type` (impact MEDIUM, 6 người gọi trực tiếp — chỉ-thêm, mặc
  định như cũ); `sessionUnavailable` mang `BFF_PROBLEM_TYPES.sessionUnavailable` (`bff-contract.ts`).
- **FE:** `PROBLEM_TYPES` + `hasProblemType` ở `lib/api/problem.ts` (giá trị API ràng bằng `satisfies` vào kiểu sinh);
  `errorMessage` tra `BY_TYPE` trước `BY_CONTEXT`. `feed: { 503 }` của `E1` **bỏ** — đổi có chủ đích: 503 không mang `type`
  riêng giờ là lỗi hệ thống, hiện mã tra cứu. Fixture `feedOverloadedProblem()` gắn `satisfies FeedOverloadedProblem`.
- **Ca mới:** `ResultTests` (Type lên dây nguyên vẹn + đối chứng mặc định), `FEED-12` và `FeedServiceTests` khẳng định `type`,
  BFF "Redis phiên chết → 503 bff-session-unavailable" (trước đó **không** có ca nào canh nhánh này), `hasProblemType`,
  `messages.test.ts` phân nhánh theo `type` (gồm ca "title đúng mà type mặc định vẫn là lỗi hệ thống").

**Bằng chứng:** Unit 291 → 292, Architecture 16, Integration 483 (482 đạt + 1 đỏ nền `StartupConfigurationTests` R2 của máy
dev). Vitest 478 → 483.

### E4 — 2026-09-23

**Câu đã chốt:** Q-E5 (khác đề xuất — không nút "Xem thêm" lúc bình thường) và Q-E6 (như đề xuất), dòng *✅ chốt* dưới câu
hỏi. Q-E4 chốt khác đề xuất và đi commit riêng trước (mục Q-E4 ở trên).

**Đã làm:**

- `hooks/use-cursor-pages.ts` (Q-E8): chép khuôn `use-post-page.ts`, generic `<T, P>`. Khác bản GĐ2: `error` là lỗi **thô** (màn
  tự chọn câu và cách vẽ — feed tách 503 quá tải bằng `type`), trả `head` (trang đầu, feed đọc `mode` từ đây), đếm
  `emptyStreak`. `fetchPage`/`getId` giữ trong ref để effect trang đầu không chạy lại mỗi render. `hooks/.gitkeep` bỏ.
- `features/feed/use-feed-page.ts`: bọc hook chung, `limit` 20, `mode` theo trang đầu.
- `features/feed/feed-list.tsx`: bảng trạng thái của Bước 2, trừ chỗ Q-E5 đổi — sentinel khi còn trang, không nút lúc bình
  thường; trang sau lỗi → dòng lỗi + **Thử lại** gọi `loadMore` (cùng `nextCursor`, không nạp lại từ đầu); tới trần 5 trang
  rỗng → nút **Xem tiếp**; hết → "Bạn đã xem hết". Thẻ quá tải chọn bằng `hasProblemType(…, feedOverloaded)`; 503 khác
  (apache HTML, BFF mất kho phiên) đi `FormAlert` với câu của `errorMessage`. Observer tạo trong effect, `rootMargin
  400px 0px`; effect thứ hai nạp tiếp khi sentinel vẫn giao nhau sau mỗi lượt về (Q-E5 cạm bẫy).
- `test/intersection-observer.ts` (harness): stub ghi observer, không tự bắn; `kichHoatGiaoNhau`, `soObserverDangTheoDoi`.
  `setup.ts` cài stub khi có `window`, gỡ observer sau mỗi ca. Luật frontend Mục 9 ghi thêm file harness này.

**Chỗ lệch với file này:**

- *Bảng Test:* "Trần 5 lượt rỗng … nút 'Xem thêm' còn" → nút **Xem tiếp** chỉ hiện ở trần; "`nextCursor = null` — không
  sentinel, không nút" → thêm "Bạn đã xem hết". Thêm ca: không giao nhau thì không gọi; `mode` giữ theo trang đầu; Làm mới đọc
  lại `mode`; **Làm mới rồi cuộn** (observer được tạo lại); 503 không `type` riêng → lỗi hệ thống; 503 BFF kho phiên; 500 có
  mã tra cứu; `renderPost` nhận đúng `PostResponse`.
- *Ca StrictMode:* tài nguyên StrictMode chạm tới ở màn này là `AbortController` trang đầu của hook — **không** phải observer
  như bảng cạm bẫy ngụ ý: observer chỉ tạo **sau** khi trang đầu về, lúc đó không còn mount lại. Đột biến "observer chỉ tạo
  một lần cho cả đời component" không bị ca StrictMode bắt → thêm ca thường "Làm mới rồi cuộn". Đã ghi vào luật frontend Mục 9.
- *Thời gian chờ:* ca "503 ở trang SAU" đỏ 1/8 lượt khi chạy cả bộ với mức chờ mặc định 1s (không giữ được log lượt đỏ; luồng
  state không có chỗ đua — lỗi và `pending=false` gộp một render). Mọi `waitFor`/`findBy` của file dùng hằng `CHO` 5s; sau đó
  6/6 lượt cả bộ xanh. Theo dõi tiếp ở `E6`.
  **Sửa chẩn đoán (2026-09-23, ở E3):** gốc thật **không** phải mức chờ 1s mà là cuộc đua trong dàn test — `kichHoatGiaoNhau`
  bắn khi effect tạo observer chưa chạy (xem mục E3 dưới). Chẩn đoán trên sai; `CHO` 5s giữ lại nhưng không phải cách chữa.

**Thử đột biến** — tám đột biến, đều bị bắt:

| Đột biến | Ca đỏ |
|---|---|
| Bỏ effect nạp tiếp khi sentinel vẫn giao nhau (Q-E5 cạm bẫy) | trang RỖNG mà `nextCursor ≠ null`; trần 5 trang |
| `nextCursor = null` khi `items.length < 20` (suy hết từ độ dài trang) | 5 ca (cuộn, trang rỗng, trần, `mode` trang đầu, 503 trang sau) |
| Bỏ trần `emptyStreak` | trần 5 trang rỗng |
| `head` lấy trang mới nhất | `mode` giữ theo trang đầu |
| Lỗi trang sau vẫn cho tự nạp | 503 ở trang SAU |
| Thẻ quá tải chọn theo status 503 thay `type` | 503 apache; 503 BFF kho phiên |
| Observer chỉ tạo một lần (cờ ref) | Làm mới rồi cuộn |
| Effect trang đầu chỉ chạy một lần cho mỗi key (controller đã hủy bị dùng lại) | **chỉ** ca StrictMode |

**Bằng chứng (trước lượt rà dưới):** Vitest 41 file / 503 ca (483 → 503: +20 `feed-list.test.tsx`). `lint`, `typecheck`, `build` xanh. `grep`
checklist Mục 13: không `@/features/` trong `features/feed` + `hooks`, không so `items.length` với `limit`, không
`useRef(new …)` (chỉ còn trong chú thích), không `console.`.

**Bổ sung sau lượt rà hệ quả Q-E4/Q-E5 (2026-09-23, cùng commit E4):**

- **`/friends` giữ nút "Xem thêm"** — Q-E5 chỉ áp cho feed (ghi dưới dòng chốt Q-E5 và ở E3 Bước 3).
- **Mục 0.5:** cuộn tự động chuyển sang *Không cắt* — không còn nút dự phòng; phương án khi trễ là đổi observer thành nút.
- **Nút "Xem tiếp" ở trần** xác nhận giữ — "Bạn đã xem hết" ở đó sai vì `nextCursor` còn khác null.
- **`aria-live`:** hook chung thêm `lastAdded` (số mục trang SAU gần nhất thêm được; trang đầu 0); `FeedList` có vùng
  `role=status` ẩn "Đã tải thêm N bài, đang hiển thị M bài." — kèm tổng để hai lượt cùng +20 vẫn là hai câu khác nhau. Test
  chọn vùng theo `data-testid` (`Spinner` của kit cũng mang `role=status`) và khẳng định `role` riêng.
- **`content-v1.yaml` `info.version` → `1.0.1-gd4`** (vá sau D7). `pnpm gen:api` không đổi file sinh (version không vào
  `schema.d.ts`); cổng hợp đồng backend 12/12.
- **Swagger lệch yaml ở tên schema 503:** giữ `ProblemDetails` trong `[ProducesResponseType]` — thêm DTO chỉ để đặt tên không
  đáng. Ghi ở header yaml và `giai-doan-4.md` Mục 8.2.
- **Màn GĐ1 đổi câu khi BFF mất Redis** (đổi có chủ đích của Q-E4): ca mới ở `login-form.test.tsx` — 503
  `bff-session-unavailable` → "Dịch vụ đăng nhập tạm thời gián đoạn…", không mã tra cứu, không điều hướng. Đột biến "bỏ nhánh
  `BY_TYPE`" làm đỏ ca này cùng ba ca 503 của feed.
- **Tài liệu lệch đã sửa:** hướng dẫn Mục 0.1 (E4), Mục 0.5, bảng Q-E3 (câu 503 nằm ở `BY_TYPE`), bảng E4 Bước 2 và bảng Test;
  `giai-doan-4.md` Mục 8.2 (schema `FeedOverloadedProblem`, version, lệch Swagger).

**Bằng chứng cuối:** Vitest 41 file / 483 → 505 ca (+21 `feed-list.test.tsx`, +1 `login-form.test.tsx`), 3/3 lượt cả bộ
xanh. `lint`, `typecheck`, `build` xanh. Backend: chỉ đổi yaml (version + chú thích) — `Contract` 12/12.

### E2 — 2026-09-23

Không câu Q-E* nào chặn `E2`: Q-E2 (hồ sơ chính mình) quyết ở `app/` — việc của `E5`; component không tự hỏi "có phải tôi".

**Đã làm:** `features/friend/relationship-buttons.tsx` theo đúng Bước 1–4 — khuôn "key + suy ra" của `PublicProfile`,
`AbortController` tạo trong effect; bảng vẽ nút hai hàng độc lập (Đ-4.5); một `pending` cho cả cụm, nút vừa bấm có
`Spinner` + `aria-busy`; 204 vẽ theo kết quả tất định **sau** khi phản hồi về; 409 kết bạn / 403 chấp nhận → đọc lại; lỗi ghi
đi qua `fieldMessage(e, "userId", <ngữ cảnh>)` để 400 tự-quan-hệ hiện đúng câu server (Đ-E5). `AlertDialog` Hủy kết bạn nêu
tên từ prop `displayName?`, nút huỷ trong hộp thoại ghi "Không" (tránh hai nút cùng chữ "Hủy").

**Lỗi tìm ra khi tự rà, đã sửa trước commit:** phản hồi ghi của người **cũ** về sau khi đã sang hồ sơ khác ghi đè thẳng slot
`data` → slot mang `key` cũ, hồ sơ người mới quay lại skeleton mãi. Sửa: cập nhật hàm chỉ khi `prev.key` còn là lượt đã bấm;
đọc lại sau 409/403 chỉ khi vẫn đang xem đúng lượt đó (`shownKeyRef`, ghi trong effect). Có ca test riêng.

**Chỗ lệch với file này:**

- *Lỗi thao tác ghi gắn theo `userId`*, không theo lượt đọc: đọc lại sau 409/403 đổi `key` mà câu lỗi phải còn.
- *Test:* ca "đang chờ → mọi nút disabled" và ca "sang hồ sơ khác" dùng **chốt do test mở** thay `delay(80)` — lượt đầu ca
  khóa cụm đỏ vì POST về trước cú bấm thứ hai của `userEvent` (dàn test sai, không phải component: `pendingRef` chặn mọi
  lượt khi đang bay). Thêm ca 404 Theo dõi và ca lỗi nạp → Thử lại.

**Thử đột biến** — tám đột biến, bảy bị bắt, một tương đương:

| Đột biến | Ca đỏ |
|---|---|
| Optimistic: vẽ "Đã gửi lời mời" trước khi 201 về | khóa cả cụm; 404 Kết bạn |
| Không khóa cả cụm (`disabled={false}`) | khóa cả cụm |
| Ghi đè thẳng slot bằng phản hồi của người cũ | sang hồ sơ khác khi thao tác đang bay |
| Không đọc lại sau 409/403 | 409 Kết bạn; 403 Chấp nhận |
| Hủy kết bạn bỏ luôn theo dõi (sai Đ-4.5) | Hủy kết bạn … vẫn đang theo dõi |
| Nút "Không" trong hộp thoại vẫn gửi DELETE | hộp thoại → Không → 0 DELETE |
| Effect đọc chỉ chạy một lần mỗi key | **chỉ** ca StrictMode |
| Bỏ chốt `pendingRef` | **Không đỏ — tương đương qua UI**: React commit sự kiện rời rạc đồng bộ, `disabled` có trên DOM trước cú bấm kế; chốt giữ lại làm lớp phòng thủ thứ hai |

L5: `relationship-buttons` vào danh sách StrictMode của luật frontend Mục 9 (bảy ca).

**Bằng chứng:** Vitest 41 → 42 file, 505 → 524 ca (+19 `relationship-buttons.test.tsx`), 3/3 lượt cả bộ xanh. `lint`,
`typecheck`, `build` xanh. `grep` checklist Mục 13 trên `features/friend`: không `@/features/`, không `useRef(new …)`, không
`console.`, không `fetch`.

### E3 — 2026-09-23

**Đã làm:** `features/friend/friend-card.tsx` (chỉ trình bày: avatar, tên dẫn `/users/{id}`, `since` định dạng như
`PostCard`, vùng nút và câu lỗi do màn truyền vào) và `features/friend/friends-screen.tsx` — ba mục xếp dọc, mỗi mục **một**
`useCursorPages` riêng (`friends:incoming`, `friends:list`, `friends:outgoing`), nút "Xem thêm" (Q-E5 chỉ áp cho feed). Mỗi thẻ
một `useCardAction` — `pending` + câu lỗi riêng, khóa theo thẻ. Chấp nhận → gỡ thẻ + `friends.reload()` (không tự dựng thẻ).
Trang sau hỏng → câu lỗi + nút "Xem thêm" đổi thành "Thử lại" (nạp lại đúng lô hỏng).

**Chỗ lệch với file này:**

- *403 Chấp nhận:* câu Q-E3 lên **đầu mục** kèm tên (`Trần Bình: Lời mời này không còn hiệu lực.`) — Bước 2 ghi "dưới thẻ rồi
  gỡ thẻ", tự mâu thuẫn; đã sửa câu ở Bước 2.
- *Hộp thoại Hủy kết bạn:* nút hủy ghi "Không", cùng nếp `E2`.
- *Test thêm:* thẻ dẫn `/users/{id}`; lỗi trang đầu của **một** mục không kéo hai mục kia; lỗi khác → câu dưới thẻ, thẻ giữ;
  khóa theo thẻ dùng chốt do test mở (nếp `E2`).

**Thử đột biến** — sáu đột biến, đều bị bắt:

| Đột biến | Ca đỏ |
|---|---|
| Chấp nhận không nạp lại mục Bạn bè | Chấp nhận → … NẠP LẠI |
| Nút "Xem thêm" theo `items.length >= 20` (suy từ độ dài) | Xem thêm nối trang; trang RỖNG mà `nextCursor ≠ null` |
| 403 Chấp nhận không gỡ thẻ | 403 Chấp nhận |
| Thẻ lời mời không khóa khi đang chờ | khóa THEO THẺ |
| "Rỗng" = `items` rỗng, bỏ điều kiện `nextCursor === null` | trang RỖNG mà `nextCursor ≠ null` |
| Effect trang đầu của hook chỉ chạy một lần mỗi key | **chỉ** ca StrictMode |

L5: `friends-screen` vào danh sách StrictMode của luật frontend Mục 9 (tám ca).

**Độ ổn định của cả bộ — hai gốc tìm ra khi chạy lặp (2026-09-23, cùng commit E3):**

1. **Cuộc đua trong dàn test của feed** (`feed-list.test.tsx`, từ E4). Log lượt đỏ: ca StrictMode `soObserverDangTheoDoi()`
   ra 0 thay vì 1; ca cuộn bắn giao nhau mà không có trang sau. Observer tạo trong `useEffect` — chạy SAU khi DOM vẽ;
   `waitFor` thấy đủ bài là trả về trong lúc observer có thể chưa có, `kichHoatGiaoNhau` bắn vào khoảng không. Trình duyệt
   thật không có khe này (observer thật tự bắn một lượt khi `observe()`) — lỗi dàn test, không phải lỗi màn. Sửa:
   `cuonToiDay` chờ có observer sống rồi mới bắn; ca "không giao nhau" chờ observer trước khi bắn; ca "503 ở trang SAU" khẳng
   định **0** observer sống sau lỗi (mạnh hơn bản cũ). Đây mới là gốc của lượt đỏ E4 ghi nhầm là "thiếu thời gian chờ".
2. **Thời hạn cả ca bằng thời hạn chờ** (`user-posts.test.tsx`, ca GĐ2, không observer): "Test timed out in 5000ms" 1/10
   lượt — lượt chờ viết tay 5s bên trong một ca có thời hạn cả ca 5s. Sửa: `testTimeout: 15_000` ở `vitest.config.ts`.
   *Trình tự thật:* lần đầu tôi áp `testTimeout` dựa trên lượt đỏ của ca "trần" feed — sai gốc (đó là cuộc đua 1), đã hoàn
   tác; áp lại chỉ khi có bằng chứng riêng từ `user-posts`.

Ghi cả hai vào luật frontend Mục 9. Sau hai sửa: **10/10 lượt cả bộ xanh**; đột biến "bỏ nạp tiếp" và "observer một lần" vẫn
bị bắt với cách bắn mới.

**Bằng chứng E3:** Vitest 42 → 43 file, 524 → 538 ca (+14 `friends-screen.test.tsx`). `lint`, `typecheck`, `build` xanh.

### E5 — 2026-09-23

**Câu đã chốt:** Q-E1, Q-E2, Q-E7 — cả ba như đề xuất (dòng *✅ chốt* dưới từng câu).

**Đã làm:**

- **Q-E1:** xóa `app/page.tsx`; trang chủ `app/(app)/(with-profile)/page.tsx` (client) ráp `FeedList` + `PostItem` qua
  `renderPost` — dòng ráp GĐ3 cắm thanh cảm xúc. `safeNext` mặc định `"/me"` → `"/"`, `onboarding.tsx` về `"/"`. Khẳng định
  đổi **có chủ đích**: `safe-next.test.ts` (mặc định + open redirect → `/`), `login-form.test.tsx` (không `next` / `next` độc →
  `/`), `profile-form.test.tsx` (onboarding không `next` → `/`), `e2e/smoke.spec.ts` (`/` chưa đăng nhập → `/login?next=%2F`).
  Rà `e2e/`: mọi spec khác đăng nhập với `?next=%2Fme` tường minh — không đổi.
- **`/friends`:** `app/(app)/(with-profile)/friends/page.tsx` chỉ ráp `FriendsScreen`.
- **Q-E2:** `users/[userId]/page.tsx` thành client (`use(params)` + `useProfile()`); hồ sơ của chính mình không truyền
  `RelationshipButtons`, câu rỗng của danh sách bài đổi theo "mình / người khác".
- **Q-E7:** `AppHeader` thêm slot `nav` (`<nav aria-label="Điều hướng chính">`); `app/(app)/layout.tsx` truyền ba `Link`.
- Impact: `safeNext`, `PublicProfile`, `AppHeader`, `OnboardingPage` đều ra **UNKNOWN** (chỉ mục không lần được lời gọi JSX) —
  tìm chữ xác nhận: `safeNext` 2 người gọi, `PublicProfile` 1, `AppHeader` 1, đích onboarding 1 chỗ.

**Chỗ lệch với file này:**

- *Slot `actions` của `PublicProfile` là HÀM* `(profile) => ReactNode`, không `ReactNode` như Q-E2/Bước 3: `app/` lấy tên người
  kia cho hộp thoại Hủy kết bạn mà không nạp hồ sơ lần hai. Ghi "Lệch Đ-4.16" trong `giai-doan-4.md`.
- *`FeedList` thêm slot `action`* (nút "Đăng bài" của trang chủ) — code mẫu Bước 1 đã dùng prop này nhưng `E4` chưa dựng.
- *Bước 5 dòng hủy kết bạn* mâu thuẫn Đ-4.6 — đã sửa ở Bước 5.
- *Lệch Đ-4.16 (L4)* — ráp `PostItem`, không `PostCard` — ghi ngược vào `giai-doan-4.md` trong commit này.

**Lượt tay Bước 5 — do Playwright đi thay** (Chrome **153.0.8010.53**, API dev + FE dev thật trên `localhost`): một spec
**tạm** (không commit — spec chính thức là `E6`) đi đúng bảy dòng Bước 5 bằng hai context trình duyệt, hai tài khoản mới, bài
tạo qua API: gợi ý + không thấy bài `friends` → bấm tên B trên feed → Kết bạn → "Đã gửi lời mời" → B `/friends` Chấp nhận → A
thấy cả hai bài, **không** còn nhãn gợi ý (dấu nguồn cache trang đầu, `FEED-07b`, đúng) → Hủy kết bạn → bài `friends` mất, nhãn
gợi ý trở lại → Theo dõi → bài public trong mạng lưới, bài `friends` không → `/users/{A}` không có nút quan hệ. Header có đủ ba
liên kết. **1 passed (10,9s)**, lượt đầu. `smoke.spec.ts` (khẳng định mới) 2/2.

**Bằng chứng:** Vitest 538 → 543 (+4 slot `actions` của `public-profile`, +1 slot `action` của `feed-list`), 43 file, 3/3 lượt
cả bộ xanh. `lint`, `typecheck`, `build` xanh — `build` liệt kê `/` và `/friends`, không trùng route.

### Sau E5 — hai yêu cầu chỉnh (2026-09-23)

1. **Bài của chính mình hiện trên trang chủ khi chưa có kết nối.** Nguyên nhân: feed gợi ý **cố ý** loại bài của mình (Đ-4.6
   cũ: `author_id <> me` trong `SuggestedPageAsync`, `authorId != me` trong `CanSeeSuggested`) — không phải cache (đăng bài
   có xóa `feed:p1:{tác giả}` sau COMMIT). Đổi Đ-4.6 (ghi có ngày trong `giai-doan-4.md`): gợi ý = public của người khác ∪ bài
   của mình mọi mức. **Lệch Mục 1.2 luật 3** (chạm `src/backend/**`): nhóm chốt. Impact: `SuggestedPageAsync` UNKNOWN (tên qua
   interface) / 12 phụ thuộc LOW, `CanSeeSuggested` LOW (13, 1 luồng) — cả hai chỉ đổi tập bài của MỘT trường hợp (mình).
   Test đổi khẳng định có chủ đích: `FeedVisibilityTests` (+3 dòng: của mình friends/private/hidden), `FeedStoreTests` (3 → 6 bài;
   cursor cắt 3 + 3 qua ranh giới hai nhánh), `FEED-07` (đổi tên, thêm bài private của mình và bài friends của người lạ).
   *Sự cố khi chạy test:* API dev đang chạy khóa DLL trong `SocialApp.Api/bin` → build lỗi, và lượt `--no-build` đầu **chạy trên
   bản cũ** (tên ca `FEED_07_…khong_co_bai_cua_minh` còn trong danh sách) — kết quả đó bỏ. Build test ra thư mục riêng rồi chạy
   lại; không tắt tiến trình API của người dùng.
2. **Bỏ liên kết "Trang chủ"** trên header — logo đã dẫn về `/`. Ghi dưới Q-E7.

### E6 — 2026-09-23

**Bước 1 — rà Mục 10.6** (`giai-doan-4.md`) với ca đã có. Không dòng nào thiếu:

| Mục 10.6 | Ca canh |
|---|---|
| Nút quan hệ đủ bốn trạng thái | `relationship-buttons.test.tsx` — `friendship = %s` × cả hai `following` |
| 409 / 403 / 404 | cùng file — 409 Kết bạn + đọc lại; 403 Chấp nhận + đọc lại; 404 Kết bạn / Theo dõi giữ nút |
| Màn lời mời chấp nhận / từ chối | `friends-screen.test.tsx` — Chấp nhận (nạp lại Bạn bè), 403 Chấp nhận, Từ chối |
| Feed: trang đầu, cuộn theo `nextCursor` | `feed-list.test.tsx` — skeleton → 20 bài; sentinel giao nhau → nguyên chuỗi `nextCursor` |
| Trang ngắn / rỗng mà `nextCursor ≠ null` vẫn cuộn tiếp | cùng file — trang RỖNG tự nạp tiếp; `friends-screen` — trang rỗng vẫn có "Xem thêm" |
| Nhãn gợi ý khi `mode = suggested` | cùng file — `feed-suggested` hiện / không hiện; giữ theo trang đầu |
| 503 hiện nút Thử lại | cùng file — 503 `feed-overloaded` trang đầu + trang sau |
| Đúng một ca `<StrictMode>` cho `feed-list` | cùng file (và thêm cho `relationship-buttons`, `friends-screen` — L5) |

Số ca của ba màn: `feed-list` 22, `relationship-buttons` 15, `friends-screen` 14.

**Bước 2–3 — `e2e/friend-feed.spec.ts`:** hai `BrowserContext`, `giuHanMucAuth(8)`, dọn rác trong `afterEach`. Đi đúng tám dòng
của Bước 3, thêm một vế cho Đ-4.6 vừa đổi: A tự đăng bài `private` qua API, bài đó phải hiện trong feed gợi ý của A. Sau hủy kết
bạn, khẳng định `F` vắng và nhãn gợi ý trở lại — **không** khẳng định `P` vắng (hết kết nối thì về gợi ý, `P` có thể hiện lại).

**Chỗ lệch với file này:** *không* tạo `e2e/friend-helpers.ts` / `ketBanApi` (Bước 2) — spec đi vòng kết bạn bằng **UI** như
Bước 3 viết, helper sẽ là mã không ai gọi. Để lại cho spec nào cần dựng sẵn quan hệ.

**Bước 4 — cả bộ `pnpm test:e2e`** (Chrome **153.0.8010.53**, API + FE dev thật, `workers: 1`): **18 passed, 1 skipped,
0 flaky, 0 failed** — 9,9 phút. `friend-feed.spec.ts` 1,3 phút trong lượt cả bộ (8,6s khi chạy riêng — phần còn lại là chờ hạn
mức `/auth/*`). Ca skip là `single-flight.spec.ts`: **bỏ qua theo thiết kế** từ GĐ1 — chỉ chạy khi đặt `PLAYWRIGHT_API_URL`
trỏ một API token 10 giây riêng (cách dựng ở đầu file spec). GĐ4 không chạm luồng refresh (BFF chỉ thêm `type` cho 503 kho
phiên), nên không dựng lượt riêng ở `E6`; ghi rõ ở PR như một dòng `skipped`, không phải `passed`.

Vitest không đổi ở `E6`: 543 ca, 43 file.

### Trước khi mở PR — lỗi `nativeButton` ở trang chủ (2026-09-23)

Chụp ảnh cho mô tả PR thì huy hiệu dev của Next ở trang chủ báo **"1 Issue"**. Ghi console: Base UI báo *"A component that acts
as a button expected a native <button>…"* từ `ComposeButton` (`features/post/post-list.tsx`). Gốc: ba chỗ của GĐ2 E5 (`e48a5c0`)
dùng `<Button render={<Link …/>}>` cho điều hướng — `ComposeFirstPostButton`, `ComposeButton`, nút "Về trang của tôi" ở
`post-detail.tsx` — trái khuôn đã có từ GĐ1 (`verify-email.tsx`: `<Link className={buttonVariants()}>`). Có từ GĐ2 (ở `/me`),
GĐ4 chỉ làm nó lộ ở trang chủ. Không test nào bắt vì đó là `console.error` của bản dev.

Sửa: ba chỗ theo khuôn `verify-email.tsx`; luật ESLint `BUTTON_RENDER_LINK` chặn cả lớp lỗi (thử đỏ một lần, `git status` như
trước); luật frontend Mục 1 #15. Kiểm lại bằng spec tạm: `/` và `/me` **0** `console.error`, "Đăng bài" là liên kết thật. Không
spec/ca nào phụ thuộc cách render sai: các `getByRole("button", { name: "Đăng bài" })` của `e2e/` là nút gửi của composer.

### PR #21 — CI đỏ `ci / frontend (pull_request)` (2026-09-23)

Lượt `pull_request` của PR đỏ đúng ca GĐ2 `user-posts` "bấm Xem thêm hai lần KHI LƯỢT ĐẦU CÒN BAY": sau **5 giây** vẫn 1 bài —
không phải chậm, trang hai **không bao giờ** được nạp. Lượt `push` cùng commit thì xanh.

**Gốc (đo, không đoán):** `loadMore` đọc `currentRef`, mà ref đồng bộ trong `useEffect` — chạy SAU khi DOM vẽ, trong task riêng.
`waitFor` thấy 1 bài là trả về; `fireEvent.click` rơi vào khe ref còn `null` → `loadMore` lặng lẽ bỏ qua. Gắn tạm bộ đếm "ref
`null` lúc bấm" rồi chạy lặp file: **2/10 lượt đỏ, trùng đúng 2 lượt có ref `null`** (cả hai cú bấm); sau sửa **0/15 đỏ, 0 lần ref
`null`**. `hooks/use-cursor-pages.ts` (GĐ4) chép cùng khuôn nên cùng lỗi.

**Sửa:** `useEffect` → `useLayoutEffect` cho ref mà event handler đọc, ở cả `features/post/use-post-page.ts` và
`hooks/use-cursor-pages.ts`. Impact: `useUserPosts` LOW (1); `useCursorPages` UNKNOWN — tìm chữ: 4 lượt dùng (feed + ba mục
`/friends`). React 19.2: `useLayoutEffect` khi render phía server không còn cảnh báo.

**Đính chính hai chẩn đoán trước:** lượt "Test timed out in 5000ms" của `user-posts` ghi ở E3 là CHÍNH lỗi này, không phải máy
bận; `testTimeout` 15s giữ lại như nguyên tắc (thời hạn cả ca > mức chờ từng `waitFor`), không phải cách chữa — đã sửa chú
thích ở `vitest.config.ts` và luật frontend Mục 9 (thêm gạch về `useLayoutEffect` cho ref mà handler đọc).
