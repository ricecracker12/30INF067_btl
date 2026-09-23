# GĐ 4 — Social Graph + News Feed (UC-10/11, UC-13, UC-08) · lịch gốc Ngày 9–13 ⚠️ trọng điểm hiệu năng

> Nguồn: [`ke-hoach-trien-khai.md`](../ke-hoach-trien-khai.md) mục "GĐ 4 — Social Graph + News Feed", nhịp cổng mở / cổng
> đóng ở Mục 0C, và báo cáo PTTK v5.0 (UC-08 và UC-10/11 ở Mục 2, US-008 và US-010 ở Mục 3, FR-009–FR-012 ở Mục 4, ENT-04 +
> `follows` ở Mục 5.5, Mục 5.6 index, ADR-004 fan-out-on-read).
> Nền móng: [`giai-doan-2.md`](../giai-doan-2/giai-doan-2.md) — GĐ4 **tiêu thụ** module Content, cursor keyset (Đ-2.11),
> `IUserDirectory` batch (Đ-2.3) và null-object `IFriendshipReader` (Đ-2.9). Không dựng lại thứ nào.
> Kế tiếp: [`giai-doan-3.md`](../giai-doan-3/giai-doan-3.md) — làm **sau** GĐ4, đúng thứ tự của kế hoạch gốc. GĐ4 dựng
> sẵn ba chỗ để GĐ3 cắm vào mà không sửa feed (Mục 9.1).
>
> **Người thi công: một người**, làm cả backend lẫn frontend, tuần tự (Mục 9). Bảng lịch và thứ tự cắt viết theo đúng
> điều kiện đó.
>
> **Ba mốc không lùi được của giai đoạn này:**
> 1. **k6 feed @ 1.000 CCU, p95 ≤ 500ms** (NFR-PERF-01, GOAL-01) — đo sơ bộ ngay trong GĐ4, còn biên độ để thêm index /
>    cache trước GĐ8. Đây là mục tiêu khó nhất của cả dự án.
> 2. **Bài `friends` hiện đúng với bạn, và chỉ với bạn.** `IFriendshipReader` chuyển từ `AlwaysStrangers` sang hiện thực
>    thật; `READ-01` và các test `PostVisibility` của GĐ2 phải xanh lại **mà không sửa khẳng định nào** (Đ-4.3).
> 3. **Không cache nào chứa thứ theo người xem hay URL đã ký** — bẫy chéo giai đoạn GĐ2 để lại, và GĐ3 (`myReaction`)
>    sẽ giẫm vào ngay nếu GĐ4 dựng cache sai (Đ-4.9).

## Tài liệu này có hai phần

| Phần | Trả lời câu hỏi | Đọc khi |
|---|---|---|
| **A — Thiết kế và quyết định** | *Cái gì* và *vì sao* | Trước khi gõ dòng đầu tiên; lúc review PR |
| **B — Kế hoạch triển khai** | *Làm gì, theo thứ tự nào* | Lúc lên lịch; lúc kiểm tiến độ |

Hướng dẫn thi công từng bước nằm ở các file `huong-dan-khoi-*.md` cùng thư mục, **viết khi khối đó bắt đầu** — đúng nếp
GĐ1 và GĐ2.

> **Trạng thái các quyết định:** mười sáu quyết định ở Mục 3 là **đề xuất**, viết ngày 2026-09-21 trên nền code nhánh
> `loveart1210` (sau PR #20). Cổng mở chốt hoặc sửa từng cái; cái nào sửa thì ghi ngày và lý do ngay dưới quyết định đó.
> Làm một mình thì "cổng mở" là một buổi tự rà có sản phẩm: hợp đồng commit trước, code sau — không bỏ bước này.

---

# Phần A — Thiết kế và quyết định

## 0. Thuật ngữ

| Từ | Nghĩa trong tài liệu này |
|---|---|
| **quan hệ bạn bè** | Một dòng `friendships` của một cặp người dùng, trạng thái `pending` (lời mời) hoặc `accepted` (đã là bạn) |
| **cặp chuẩn hóa** | `(user_min, user_max)` với `user_min < user_max` — mỗi cặp đúng một khóa, bất kể ai gửi (BR-03) |
| **theo dõi** | Quan hệ một chiều `follower → followee`, không cần phê duyệt (FR-012, UC-13) |
| **nguồn feed** | Tập tác giả đổ bài vào feed của một người: **bạn bè** (thấy `public` + `friends`), **người đang theo dõi mà không phải bạn** (chỉ thấy `public`), và **chính mình** (thấy mọi mức) |
| **fan-out-on-read** | Feed dựng lúc **đọc** bằng cách truy vấn bài của các nguồn, không ghi sẵn vào hộp thư từng người lúc đăng bài (ADR-004) |
| **feed gợi ý** | Bài công khai mới nhất của toàn hệ thống, trả cho người **chưa có kết nối nào** (UC-08 luồng A1) |
| **hydrate** | Bước biến danh sách `post_id` thành `PostResponse` đầy đủ: nạp dòng bài, ảnh (URL ký mới), tác giả, `myReaction`, `canEdit` |
| **CCU** | Số người dùng đồng thời (concurrent users) — ở k6 là số VU đang chạy |

## 1. Mục tiêu giai đoạn

### Phát biểu một câu

> **Người dùng thật trên staging gửi, hủy, chấp nhận, từ chối lời mời kết bạn và theo dõi người khác; mở trang chủ thấy
> bài mới nhất của bạn bè và người mình theo dõi, đúng quyền riêng tư, cuộn vô hạn — và feed đó giữ p95 ≤ 500ms khi
> 1.000 người cùng tải.**

### Mục tiêu chính thức và khối nào gánh

| Mã | Mục tiêu | Đạt bằng | Kiểm bằng |
|---|---|---|---|
| **FR-010** | Tạo lời mời kết bạn `pending` khi thỏa BR-03 | A1–A3, D2 | US-010 AC-01..03, FRD-01..06 |
| **FR-011** | Chuyển `accepted` khi chấp nhận; **xóa bản ghi** khi từ chối / hủy | D3, D4 | US-010 AC-04, FRD-07..10 |
| **FR-012** | Theo dõi / bỏ theo dõi một chiều, không phê duyệt | A1, D6 | FOL-01..04 |
| **BR-03** | Mỗi cặp tối đa một quan hệ; không tự kết bạn | PK cặp chuẩn hóa + CHECK (A2) | FRD-02, FRD-03, FRD-06 |
| **FR-009** | Feed: bạn bè (công khai + bạn bè) + đang theo dõi (chỉ công khai), mới trước, cursor 20 bài | C1–C4, D7 | US-008 AC-01..03, FEED-01..10 |
| **BR-02** | Mức `friends` đánh giá thật, tại thời điểm đọc | A5 (đổi DI) | `READ-01..05` của GĐ2 + `READ-06` mới |
| **BR-07** | Bài `hidden` không vào feed (dù GĐ6 mới có người ghi `hidden`) | C2 | FEED-06 |
| **NFR-PERF-01 / GOAL-01** | Feed p95 ≤ 500ms @ 1.000 CCU | C1–C4, B5 | Báo cáo k6 sơ bộ (F4) |
| **GOAL-03** | Không IDOR ở quan hệ bạn bè | Khuôn tầng 3 | 5 dòng AuthZ matrix mới |

### Vì sao GĐ4 là giai đoạn rủi ro nhất cho tới giờ

1. **Đây là lần đầu một endpoint đọc dữ liệu của hai module trong một request, trên đường nóng.** Feed cần danh sách
   bạn bè (SocialGraph) để truy vấn bài (Content), mà hai module không được `JOIN` qua ranh giới schema (Đ-2.2). Cách
   ghép hai module ở đây quyết định luôn p95 của GOAL-01.
2. **Đây là lần đầu có cache trên đường đọc.** Cache sai không làm test đỏ — nó làm người B thấy bài `friends` của người
   vừa hủy kết bạn, hoặc thấy ảnh vỡ sau 15 phút, hoặc thấy nút tim sáng theo cảm xúc của người A. Ba bẫy này đã được
   GĐ2 báo trước (và GĐ3 sẽ thêm cái thứ ba); GĐ4 là nơi chúng nổ nếu quên.
3. **Đây là lần đầu ta phải chứng minh một con số hiệu năng.** Chứng minh cần dữ liệu đủ lớn, người dùng giả đủ nhiều, và
   một môi trường đo trung thực — không cái nào có sẵn trong repo (thư mục `tests/load/` đang rỗng).

---

## 2. Phạm vi

### Trong phạm vi

| Nhóm | Nội dung |
|---|---|
| **Module SocialGraph** | Schema `socialgraph`; entity `Friendship`, `Follow`; `SocialGraphDbContext` + migration đầu tiên |
| **Quan hệ bạn bè** | Gửi · hủy (người gửi) · từ chối (người nhận) · chấp nhận · hủy kết bạn · danh sách bạn · danh sách lời mời đến / đi |
| **Theo dõi** | Theo dõi · bỏ theo dõi; tự theo dõi bị chặn |
| **Trạng thái quan hệ** | `GET /relationships/{userId}` — một endpoint cho nút trên trang hồ sơ |
| **Contract chéo module** | Hiện thực thật của `IFriendshipReader`; contract mới `IFeedSourceReader` ở SharedKernel |
| **News feed** | `GET /feed` trong nhóm `content-v1`; fan-out-on-read; cache nguồn feed 60s + cache trang đầu 30s; feed gợi ý cho người chưa có kết nối; degrade khi Redis chết; 503 khi DB quá 5s |
| **Hiệu năng** | Bộ dữ liệu tải (seed SQL), kịch bản k6, báo cáo p95 sơ bộ |
| **Lane frontend** | Nút kết bạn / theo dõi trên hồ sơ · màn lời mời + danh sách bạn · feed trang chủ cuộn vô hạn, skeleton, trạng thái rỗng, nhãn "gợi ý", 503 thành thông báo thử lại |
| **Test** | 5 dòng AuthZ matrix · cổng hợp đồng `socialgraph-v1` mới · test feed theo ma trận quyền · test đổi DI của `IFriendshipReader` |

### Ngoài phạm vi — hoãn có địa chỉ

| Việc | Hoãn tới | Lý do |
|---|---|---|
| Thông báo "có lời mời kết bạn" / "đã chấp nhận" (UC-10 bước 2, 4) | **GĐ6** | GĐ4 phát event trong tiến trình và chỉ log (Đ-4.15), GĐ6 nối notification vào đó |
| Tìm người để kết bạn | **GĐ6** (UC-16) | Trước GĐ6, người mới tìm người khác qua **feed gợi ý** (Đ-4.6) → bấm tên tác giả → trang hồ sơ → nút Kết bạn |
| Chặn người dùng (block) | **Ngoài MVP** | Không có FR nào; thêm vào là thêm một nhánh BR-02 thứ tư |
| Xem danh sách bạn của **người khác** | **Ngoài MVP** | Đi kèm một câu hỏi riêng tư mới (ai được xem danh sách bạn của ai) mà PTTK không trả lời |
| "Bạn chung", gợi ý kết bạn | **Ngoài MVP** | Truy vấn đồ thị hai bước, không có FR |
| Bỏ theo dõi một người bạn mà vẫn là bạn | **Ngoài MVP** | Nguồn feed = bạn bè ∪ đang theo dõi (Đ-4.5); tắt riêng một người bạn cần thêm cột trạng thái |
| Xếp hạng feed (không theo thời gian) | **Ngoài MVP** | FR-009 chốt "sắp theo thời gian giảm dần" |
| Đẩy bài mới vào feed đang mở (realtime) | **Ngoài MVP** | Feed tự nạp lại khi người dùng kéo về đầu trang |
| Bài `hidden` biến mất khỏi `GET /posts/{id}` và trang cá nhân | **GĐ6** | GĐ4 chỉ chặn `hidden` ở feed và ở danh sách bài theo tác giả (Đ-4.11); đường đọc một bài thuộc luồng kiểm duyệt |
| Dọn `friendships`/`follows` khi xóa tài khoản | **GĐ8** | Hệ quả của Đ-2.2 (không FK chéo schema) — cùng nợ với `profiles`/`posts` của GĐ2 |
| Đo k6 **chính thức** trên production | **GĐ8** | GĐ4 đo **sơ bộ** trên môi trường đo riêng (Đ-4.13), đúng lịch "GĐ4 (sơ bộ) → GĐ8 (chính thức)" |

---

## 3. Quyết định thiết kế

Mười sáu quyết định. **Đ-4.1 → Đ-4.4** là kiến trúc của module mới và cách nó nối vào Content — chốt ở **cổng mở**.
**Đ-4.7 → Đ-4.10** quyết định GOAL-01 — sai ở đó thì k6 là nơi phát hiện, và lúc đó chỉ còn GĐ8 để sửa.

### Đ-4.1 SocialGraph là module thứ tư: schema `socialgraph`, một `DbContext`, không FK sang `identity`

Chép nguyên khuôn Đ-2.1 và Đ-2.2: `SocialGraphDbContext` + `SocialGraphDbContextOptions` + design-time factory +
`__EFMigrationsHistory` trong schema `socialgraph`; `AddSocialGraphModule` + `MigrateSocialGraphModuleAsync` nối vào
`Program.cs` cùng chỗ ba module trước.

**Lệch ENT-04 của PTTK, có chủ đích và đã có tiền lệ:** PTTK ghi `user_min_id`, `user_max_id`, `requester_id` là
`FK users`. GĐ4 để chúng là `uuid` trần — luật Đ-2.2 áp cho mọi module từ GĐ2, không có ngoại lệ. Hệ quả giống hệt GĐ2:
tồn tại được một quan hệ trỏ tới user đã bị xóa, và GĐ8 phải fan-out việc dọn dẹp sang SocialGraph.

Tên schema `socialgraph` (không gạch dưới) để trùng với tên nhóm Swagger `socialgraph-v1` và thư mục sinh
`lib/api/socialgraph/` — ba chỗ một tên, không ai phải nhớ quy tắc chuyển đổi.

### Đ-4.2 Hợp đồng: file mới `socialgraph-v1.yaml` cho quan hệ; `GET /feed` nằm trong `content-v1`

Feed trả **bài** — `PostPage` dùng lại `PostResponse` của Content. Đặt `/feed` sang nhóm SocialGraph là bắt module đó
biết hình dạng bài viết, và bắt FE sinh `PostResponse` ở hai thư mục. PTTK cũng xếp feed vào CMP-04 (Content,
FR-004–009) và `/friends/`, `/follows/` vào CMP-03.

| File | Nhóm Swagger | FE sinh ra |
|---|---|---|
| `Modules/SocialGraph/Presentation/socialgraph-v1.yaml` (**mới**) | `socialgraph-v1` | `lib/api/socialgraph/schema.d.ts` — tự động, không thêm script (luật frontend Mục 7) |
| `Modules/Content/Presentation/content-v1.yaml` (thêm `/feed`) | `content-v1` | `lib/api/content/schema.d.ts` |

`content-v1` đã đóng băng ở GĐ2 → mở lại theo luật **chỉ-thêm** (không đổi, không xóa trường nào), `info.version` →
`1.0.0-gd4`. GĐ3 sẽ mở lại nó một lần nữa theo cùng luật.

### Đ-4.3 `IFriendshipReader` thật: gỡ dòng `AlwaysStrangers` khỏi Content, đăng ký hiện thực ở SocialGraph

GĐ2 hẹn *"GĐ4 đổi đúng một dòng DI"*. Chính xác hơn: dòng đó **không thể đổi tại chỗ** — `ContentModuleExtensions` không
được nhìn thấy kiểu của SocialGraph (`ModuleBoundaryTests`). Nên:

1. **Xóa** `services.AddSingleton<IFriendshipReader, AlwaysStrangers>()` khỏi `AddContentModule`.
2. `AddSocialGraphModule` đăng ký `services.AddScoped<IFriendshipReader, FriendshipReader>()` (scoped vì đọc qua
   `SocialGraphDbContext`).
3. Giữ lớp `AlwaysStrangers` trong SharedKernel — test của Content vẫn dùng nó để dựng cảnh "không ai là bạn".

Không logic nào của Content đổi: `PostVisibility` và `PostReadService` đã hỏi `AreFriendsAsync` từ GĐ2.

**Hai bẫy của bước này:**

- **Đăng ký hai lần thì cái sau thắng, im lặng.** Nếu quên bước 1, thứ tự `AddContentModule` / `AddSocialGraphModule`
  trong `Program.cs` quyết định BR-02 chạy thật hay chạy giả. Chặn bằng test khởi động: resolve `IFriendshipReader` từ host
  thật → khẳng định **không** phải `AlwaysStrangers`, và `GetServices<IFriendshipReader>()` có **đúng một** phần tử.
- **Test pin của GĐ2 (A6) phải đổi, không được xóa.** Nó đang khẳng định host dùng `AlwaysStrangers`; GĐ4 đổi khẳng định
  thành câu ở gạch trên. Test đơn vị pin hành vi `AlwaysStrangers` luôn trả `false` thì **giữ nguyên**.

Nghiệm thu bắt buộc (kế hoạch gốc, "Hai bẫy chéo giai đoạn GĐ2 để lại"): chạy lại `READ-01` và `READ-02..05` —
bài `friends` của **người lạ** vẫn 404, của **bạn** thì thấy (`READ-06` mới).

`AreFriendsAsync` đọc **thẳng DB** (một lần tra PK cặp chuẩn hóa), không qua cache. Lệch PTTK Mục 5.6 ("cache Redis TTL
60s, chấp nhận trễ 60s khi hủy kết bạn") theo hướng **chặt hơn**: tra PK một dòng rẻ hơn một vòng Redis, và hủy kết bạn có
hiệu lực ngay với `GET /posts/{id}`. Cache 60s chỉ dùng cho feed (Đ-4.8), nơi nó thật sự tiết kiệm.

### Đ-4.4 Contract mới `IFeedSourceReader` ở SharedKernel — batch, chỉ đọc, chủ dữ liệu giữ cache

Feed cần **tập** tác giả, không phải câu hỏi có/không cho từng cặp. Theo ba luật contract của Đ-2.3 (chỉ đọc, chỉ chiếu,
batch trước):

```
SharedKernel/Contracts/IFeedSourceReader.cs
    Task<FeedSources> GetAsync(Guid userId, CancellationToken ct);
    record FeedSources(IReadOnlySet<Guid> Friends, IReadOnlySet<Guid> FollowingOnly);
        // FollowingOnly = đang theo dõi TRỪ ĐI bạn bè — hai tập rời nhau, để mỗi tác giả có đúng một mức nhìn
Modules/SocialGraph/Infrastructure/FeedSourceReader.cs      // hiện thực + cache Redis (Đ-4.8)
Modules/Content/Application/Feed/FeedService.cs              // tiêu thụ
```

**Cache nằm trong hiện thực ở SocialGraph, không ở Content.** Chỉ module chủ dữ liệu biết khi nào dữ liệu đổi (kết bạn,
hủy, theo dõi) để xóa cache; đặt cache ở Content là cache không bao giờ được xóa đúng lúc.

Không gộp vào `IFriendshipReader`: hai contract có hai người tiêu thụ khác nhau (BR-02 từng bài vs feed) và hai chính
sách tươi khác nhau (luôn tươi vs trễ ≤ 60s). GĐ5 (chat chỉ giữa bạn bè, BR-09) sẽ dùng `IFriendshipReader`, không dùng
cái này.

### Đ-4.5 Nguồn feed = bạn bè ∪ đang theo dõi ∪ **chính mình**; kết bạn không tự tạo dòng `follows`

| Nguồn | Mức riêng tư được thấy |
|---|---|
| Bạn bè (`accepted`) | `public`, `friends` |
| Đang theo dõi nhưng **không** phải bạn | `public` |
| Chính mình | `public`, `friends`, `private` |

**Lệch FR-009, có chủ đích:** PTTK chỉ kể bạn bè và người đang theo dõi. GĐ4 thêm **bài của chính mình** — không có nó
thì người vừa đăng bài mở trang chủ không thấy bài mình, và triệu chứng đó trông y hệt "đăng bài thất bại". Mức nhìn của
tác giả với bài mình thì đã có sẵn trong BR-02.

Kết bạn **không** INSERT hai dòng `follows`. Nguồn feed tính bằng hợp của hai bảng lúc đọc. Nhờ vậy "bạn bè" và "theo
dõi" là hai sự thật độc lập: hủy kết bạn thì người kia vẫn còn trong feed **nếu** mình có theo dõi họ riêng (chỉ còn bài
`public`) — đúng như cách người dùng hiểu hai nút tách biệt trên trang hồ sơ. Cái giá: không "bỏ theo dõi một người bạn"
được (Mục 2, ngoài MVP).

### Đ-4.6 Người chưa có kết nối nào nhận **feed gợi ý** — bài công khai mới nhất toàn hệ thống

UC-08 luồng A1 và FR-009: *"người dùng mới chưa kết nối trả feed gợi ý công khai"*. Kế hoạch gốc chỉ ghi "trạng thái rỗng
cho tài khoản mới" — thiếu nửa sau. Và đây không chỉ là chuyện đẹp: **trước GĐ6 không có tìm kiếm**, nên feed gợi ý là
đường **duy nhất** để người mới gặp người khác (bấm tên tác giả → hồ sơ → Kết bạn).

- Điều kiện: `Friends` **và** `FollowingOnly` đều rỗng. Có dù chỉ một kết nối → feed mạng lưới, kể cả khi feed đó rỗng.
  (Có bạn nhưng bạn chưa đăng gì là trạng thái rỗng thật, không phải lý do để trộn bài người lạ vào.)
- Nội dung: `privacy = 'public' AND status = 'published'`, **trừ** bài của chính mình, mới trước, cùng cursor.
- `FeedPage` mang thêm trường `mode: "network" | "suggested"` để FE hiện nhãn "Gợi ý cho bạn — kết bạn để thấy bài của
  bạn bè". Không có trường này thì FE phải đoán từ việc tác giả có phải bạn không — tức là tự dựng lại luật của server.
- Cần một index riêng (Mục 4): index theo tác giả không phục vụ được truy vấn toàn hệ thống.

### Đ-4.7 Truy vấn feed: một `LATERAL` cho mỗi nguồn trên index có sẵn, gộp lấy `limit + 1`

PTTK Mục 5.6 mô tả truy vấn là `author_id IN (bạn bè + theo dõi) ORDER BY created_at DESC` trên
`idx_posts_author_created`. Viết đúng như vậy thì Postgres 16 **không** đọc được thứ tự từ index cho nhiều giá trị
`author_id`: nó lấy **mọi** bài của mọi nguồn (bitmap scan) rồi sắp xếp. Người có 200 bạn, mỗi bạn 100 bài là sắp 20.000
dòng để trả 21 — và con số đó tăng theo tuổi của hệ thống.

Chốt: mỗi nguồn lấy tối đa `limit + 1` bài của riêng nó qua index (dừng sớm), rồi gộp.

```sql
WITH src(author_id, lvl) AS (                       -- lvl: 1 = chỉ theo dõi · 2 = bạn bè · 3 = chính mình
    SELECT unnest(@following_only::uuid[]), 1
    UNION ALL SELECT unnest(@friends::uuid[]), 2
    UNION ALL SELECT @me, 3
)
SELECT p.*
FROM src
CROSS JOIN LATERAL (
    SELECT * FROM content.posts p
    WHERE p.author_id = src.author_id
      AND p.status = 'published'                                         -- BR-07, và điều kiện của index một phần
      AND (src.lvl = 3 OR p.privacy = 'public' OR (src.lvl = 2 AND p.privacy = 'friends'))   -- BR-02
      AND (@cursor_at IS NULL OR (p.created_at, p.post_id) < (@cursor_at, @cursor_id))
    ORDER BY p.created_at DESC, p.post_id DESC
    LIMIT @take
) p
ORDER BY p.created_at DESC, p.post_id DESC
LIMIT @take;                                        -- @take = limit + 1
```

- Chi phí bị chặn trên bởi `số nguồn × (limit + 1)` dòng đọc qua index, **không** phụ thuộc tổng số bài.
- BR-02 nằm **trong** truy vấn — cùng luật của GĐ2 (lọc sau `Take` là trang ngắn ngẫu nhiên).
- Viết bằng SQL thô tham số hóa (`FromSql`) trong `Content.Infrastructure`, cùng chỗ với `PostStore`. **Không** nối chuỗi.
- **Nghiệm thu bằng `EXPLAIN (ANALYZE, BUFFERS)`** trên bộ dữ liệu tải: phải thấy `Index Scan using
  idx_posts_author_created` bên trong `Nested Loop` và **không** có `Sort` trên toàn bộ bài. Dán kế hoạch vào hướng dẫn khối C.

**Giới hạn đã biết:** ai theo dõi 5.000 người thì mỗi trang là 5.000 lần dò index. GĐ4 **không** giới hạn số theo dõi;
k6 đo ở phân bố thực tế (Đ-4.13). Nếu GĐ8 thấy đuôi p99 do vài người dùng như vậy, đường lui là giới hạn số theo dõi hoặc
hybrid fan-out cho người có nhiều nguồn — ADR-004 review, không phải bây giờ.

### Đ-4.8 Hai tầng cache Redis, cả hai có thể tắt, cả hai **fail-open**

Theo SEQ-03 của PTTK:

| Cache | Khóa | Giá trị | TTL | Ai xóa, khi nào |
|---|---|---|---|---|
| Nguồn feed | `sg:feed-sources:{userId}` | Hai mảng id (`friends`, `followingOnly`) | 60s | **SocialGraph**, sau `COMMIT` mỗi thao tác đổi quan hệ — xóa khóa của **cả hai** người trong cặp |
| Trang đầu | `feed:p1:{userId}` | Danh sách `post_id` (≤ 21) + `mode` | 30s | **Content**, khi chính người đó đăng / sửa / xóa bài (xóa khóa của **tác giả**) |

- Chỉ cache **trang đầu với `limit` mặc định**. Trang có cursor luôn đi DB.
- **Redis lỗi → bỏ qua cache, đọc thẳng DB, ghi log cảnh báo** (UC-08 luồng E2). Ngược với khóa của worker dọn rác GĐ2
  (Redis chết thì *bỏ lượt*): ở đây thiếu cache chỉ chậm hơn, còn từ chối phục vụ thì mất feed.
- Hai công tắc cấu hình `Feed:SourceCache:Enabled`, `Feed:PageCache:Enabled` — mặc định **bật**; k6 chạy một lượt với cache
  trang đầu **tắt** để có con số lạnh (Đ-4.13). Không có công tắc thì không đo được cache giúp bao nhiêu, và p95 "đẹp"
  có thể chỉ là tỷ lệ trúng cache của kịch bản.
- Cache trang đầu **không** bị xóa khi *bạn bè* đăng bài: bạn bè thấy bài mới trễ tối đa 30s. Đó là cái giá SEQ-03 đã
  chấp nhận. Chỉ tác giả được xóa cache của chính mình — vì với tác giả thì 30s trông như "đăng bài không lên".

**Sửa ngày 2026-09-22 (Q-C1, Q-C2 của hướng dẫn B+C+D):**

- **Giá trị cache trang đầu** là đúng bốn trường `{ ids, mode, next, fp }`: danh sách `post_id` (≤ `limit`), `mode`,
  `nextCursor` đã mã hóa, và `fp` — **dấu nguồn**, băm ngắn của hai tập `Friends` + `FollowingOnly` đã sắp xếp. Lúc thử
  cache (Mục 7.2 bước 4) nguồn hiện tại đã có trong tay (bước 2): `fp` khác → coi như trượt. Lý do: bản cũ chỉ xóa cache khi
  chính người đó đăng bài, nên người vừa kết bạn xong quay lại trang chủ trong 30s vẫn thấy **feed gợi ý** — lát cắt E2E
  của `F2` đỏ ngẫu nhiên. Dấu nguồn không thêm truy vấn nào và không bắt SocialGraph biết khóa của Content. `next` lưu sẵn vì
  bài thứ `limit` có thể đã bị xóa khi trúng cache, lúc đó không còn gì để dựng cursor.
- **Hai công tắc** bind có điều kiện: có `IConfiguration` (host) thì đọc, `ServiceCollection` trần của test thì mặc định
  bật — không test nào phải dựng cấu hình chỉ để resolve reader.

### Đ-4.9 Cache **chỉ lưu `post_id`**; hydrate luôn chạy lúc trả response và là nguồn sự thật cuối

Đây là chỗ ba bẫy chéo giai đoạn gặp nhau:

| Bẫy | Đến từ | Nếu cache lưu `PostResponse` dựng sẵn |
|---|---|---|
| URL ảnh presigned GET hết hạn sau 15 phút | GĐ2 Đ-2.9, `F5` | Ảnh vỡ, log không có lỗi nào (R2 trả 403 thẳng cho trình duyệt) |
| `canEdit` (có từ GĐ2) và `myReaction` (GĐ3 thêm) là trường **theo người xem** | GĐ2 Mục 8.2; GĐ3 Đ-3.11 | Người B thấy nút Sửa trên bài không phải của mình; sau GĐ3 thì thấy nút tim sáng theo cảm xúc của A |
| Bộ đếm cảm xúc / bình luận (GĐ3 bắt đầu ghi) | GĐ3 Đ-3.8 | Số "nhảy lùi" ngay sau khi người dùng vừa bấm tim |

Chốt: cache lưu **danh sách `post_id`**. Mỗi response — kể cả khi trúng cache — chạy **hydrate** qua **đúng hàm dựng
`PostResponse` mà `GET /posts/{id}` và `GET /users/{id}/posts` đang dùng** (`PostResponseMapper` của GĐ2, hàm batch):
nạp dòng bài theo PK (bộ đếm tươi), một lô ảnh (ký URL mới), một lô `IUserDirectory`, tính `canEdit`. GĐ3 thêm một lô
`myReaction` **vào chính hàm này** — feed có trường đó mà không sửa dòng nào của feed.

Hydrate còn **kiểm lại BR-02** cho từng bài bằng nguồn feed hiện tại và loại bài đã xóa / `hidden` / không còn được thấy
(vừa hủy kết bạn trong 30s cache). Hệ quả: **trang có thể ngắn hơn `limit`**. Hợp đồng ghi rõ: *hết dữ liệu khi và chỉ khi
`nextCursor = null`* — FE không bao giờ suy "hết" từ độ dài `items`. `nextCursor` tính từ `post_id` cuối **trong danh sách
đã cache**, không phải trong danh sách sau khi lọc, để trang sau không lặp lại bài.

### Đ-4.10 Degrade và 503: timeout 5s chỉ cho truy vấn feed

- Truy vấn feed (Đ-4.7) chạy với `CommandTimeout = 5s` riêng. Quá hạn → **503** RFC 7807, `Retry-After: 5`, mã
  `feed.unavailable` (UC-08 luồng E3). Không để rơi thành 500: 500 là "hệ thống hỏng, báo dev", 503 là "đông quá, thử lại".
- Chỉ **feed** có hành vi này. Các endpoint khác giữ timeout mặc định — không đổi `CommandTimeout` toàn context.
- `feed.unavailable` là `Error.Code` **nội bộ** — Problem Details của repo cố ý không mang mã lỗi
  (`{type,title,status,errors,traceId}`). Trên dây chỉ có status 503, `title` và header `Retry-After`; header gắn ở
  controller vì `Result → Problem` không gắn header nào (sửa ngày 2026-09-22). Hằng 5s ở **một** chỗ, không thành khóa cấu
  hình; test `FEED-12` chờ đủ 5s (Q-B4).
- FE: 503 → thẻ "Bảng tin đang quá tải" + nút Thử lại, **không** màn trắng, **không** đồng hồ đếm ngược (luật frontend
  Mục 4 cho 429 áp tương tự: không hứa thời điểm).
- *Bổ sung 2026-09-23 (khối E, Q-E4 — quyết định mới, nhóm chốt):* 503 của feed mang **`type` riêng**
  `urn:socialapp:problem:feed-overloaded` (schema `FeedOverloadedProblem` trong `content-v1.yaml`; `Error.Type` — tham số
  cuối có mặc định, cùng nếp chỉ-thêm của Q-D4). Lý do: tới trình duyệt, 503 còn có thể là **BFF mất kho phiên** (Redis) —
  BFF gắn `urn:socialapp:problem:bff-session-unavailable` (`lib/api/bff-contract.ts`). FE phân nhánh theo `type`, **không**
  theo `title` (nhãn hiển thị, đổi chữ không báo ai) và **không** chỉ theo status: 503 không mang `type` riêng (trang HTML
  của apache) là lỗi hệ thống, hiện mã tra cứu như 5xx khác. Dòng "Problem Details không mang mã lỗi" ở trên vẫn đúng —
  `type` là định danh RFC 7807, không phải `Error.Code`; `feed.unavailable` vẫn không lên dây.

### Đ-4.11 Danh sách bài theo tác giả thêm `status = 'published'` tường minh — sửa một lỗ nhỏ của GĐ2

Global query filter của `Post` là `status <> 'deleted'` (GĐ2 Đ-2.10), còn `idx_posts_author_created` là index **một phần**
`WHERE status = 'published'`. Hai điều hệ quả, phát hiện khi viết tài liệu này:

1. `PostStore.ListByAuthorAsync` **không** có điều kiện `status = 'published'` — Postgres không suy được
   `status <> 'deleted'` ⇒ `status = 'published'`, nên **có thể không dùng** index một phần. Phải kiểm bằng `EXPLAIN`.
2. Bài `hidden` (GĐ6) sẽ lọt vào trang cá nhân.

GĐ4 thêm `Status == PostStatus.Published` vào truy vấn danh sách (một dòng, cùng lúc viết truy vấn feed), `EXPLAIN` trước
và sau, ghi kết quả vào commit. Không đổi global filter — đường đọc **một** bài (`GET /posts/{id}` cho tác giả xem bài bị
ẩn của mình) là việc của GĐ6.

### Đ-4.12 Không thêm mã quyền; ba thao tác "gỡ quan hệ của mình" chỉ cần `[Authorize]`

Ma trận 17 mã có `friend.request` (9) và `friend.respond` (10), cả hai đã seed cho `USER` và `MODERATOR`. Không có mã nào
cho theo dõi.

| Endpoint | Tầng 2 | Tầng 3 (service) |
|---|---|---|
| `POST /friends/requests` | `friend.request` | người nhận tồn tại (có hồ sơ) · khác mình · chưa có quan hệ |
| `POST /friends/requests/{userId}/accept` | `friend.respond` | có lời mời `pending` **từ** `userId` **tới** người gọi |
| `DELETE /friends/requests/{userId}` | `[Authorize]` | — (xóa lời mời giữa mình và `userId`, theo chiều nào cũng được) |
| `DELETE /friends/{userId}` | `[Authorize]` | — |
| `PUT /follows/{userId}` | `friend.request` | người được theo dõi tồn tại · khác mình |
| `DELETE /follows/{userId}` | `[Authorize]` | — |
| `GET /friends`, `GET /friends/requests`, `GET /relationships/{userId}` | `[Authorize]` | — (luôn là dữ liệu của chính người gọi) |
| `GET /feed` | `post.read.public` | BR-02 trong truy vấn |

- **Theo dõi dùng `friend.request`**: cả hai là "chủ động tạo một kết nối xã hội". Thêm mã thứ 18 kéo theo sửa seeder,
  `PermissionCodes.All`, test ma trận — cho một khác biệt mà chưa ai cần. Ghi vào Mục 13.
- **Hủy lời mời, từ chối, hủy kết bạn, bỏ theo dõi chỉ `[Authorize]`**: gỡ một quan hệ có mình trong đó là quyền của chủ
  dữ liệu (GĐ3 dùng lại đúng lập luận này cho xóa bình luận, Đ-3.2). Người bị Admin gỡ quyền kết bạn vẫn phải hủy kết
  bạn được.
- Hằng mã quyền của module nằm ở `SocialGraphPermissions` của chính nó (không import Identity), kèm
  `SocialGraphPermissionsTests` ở ArchitectureTests chép khuôn `ContentPermissionsTests`.

### Đ-4.13 k6 sơ bộ chạy trên một môi trường đo riêng, dữ liệu sinh bằng SQL, token ký bằng khóa riêng

Ba ràng buộc làm việc đo khó hơn "chạy k6 vào staging":

| Ràng buộc | Hệ quả | Chốt |
|---|---|---|
| Staging chứa tài khoản thật của đội; nhồi 10.000 user giả vào đó là làm bẩn nó vĩnh viễn | Không đo trên staging | Môi trường đo = compose riêng (project `perf`), API giới hạn `cpus: 2`, `mem_limit` bằng VPS (Ampere A1 2 OCPU / 12GB); ghi rõ máy chạy trong báo cáo |
| Lấy 1.000 token bằng `POST /auth/login` là 1.000 lần BCrypt cost 12 và đụng rate limit auth 10/phút | Không đăng nhập thật trong kịch bản | k6 `setup()` **tự ký JWT HS256** bằng khóa chỉ có trong `.env` của môi trường đo (claim `sub`, `role=USER`, `iat`, `exp`, `jti` đúng như `JwtTokenIssuer`). Khóa này **không bao giờ** là khóa staging |
| Rate limit chung 100 req/phút/user | Một VU gọi liên tục sẽ ăn 429 và làm bẩn p95 | Mỗi VU là một user riêng; nghỉ 1–3s giữa hai lần gọi (≤ 60 req/phút) |

**Bộ dữ liệu** (`tests/load/feed/seed.sql`, `generate_series`, chạy trên DB rỗng sau `--migrate`): 10.000 user có hồ sơ;
mỗi user trung bình **100 bạn** (phân bố lệch: 5% có 500 bạn) và **20 người theo dõi**; **1.000.000 bài** trải 180 ngày,
tỷ lệ riêng tư 70/20/10 (`public`/`friends`/`private`), 1% `hidden`. Quy mô theo ước lượng năm 1 của PTTK Mục 5.6
(10k user, 1,8M bài), làm tròn xuống để seed chạy trong vài phút. Không có ảnh (media không nằm trên đường truy vấn
feed; ký URL là HMAC cục bộ).

**Kịch bản** (`tests/load/feed/feed.js`): tăng lên 1.000 VU trong 2 phút, giữ 5 phút, giảm 1 phút. Mỗi vòng: `GET /feed`
(trang đầu), 30% số vòng đi tiếp một trang bằng `nextCursor`. Ngưỡng k6: `http_req_duration p(95) < 500`,
`http_req_failed < 1%`. Gọi **thẳng API** (`/api/v1/feed` + bearer) — GOAL-01 là độ trễ của API; BFF đo riêng nếu còn
thời gian, không trộn vào con số chính.

**Ba lượt, cả ba vào báo cáo:** (1) mọi cache bật; (2) cache trang đầu tắt — con số **lạnh**; (3) Redis dừng giữa chừng —
chứng minh degrade (Đ-4.8) không đẩy lỗi lên quá 1%. Con số dùng để kết luận GOAL-01 sơ bộ là **lượt (2)**: nó không phụ
thuộc tỷ lệ trúng cache của kịch bản.

**Không đạt thì làm gì** — có sẵn, không phải họp: `EXPLAIN` truy vấn chậm nhất (log truy vấn > 200ms) → kiểm pool kết nối
(`Maximum Pool Size` của Npgsql so với `max_connections` của Postgres — 1.000 VU trên pool mặc định 100 là hàng đợi ở tầng
ứng dụng) → xem lại số nguồn của user ở đuôi p99. Ghi mọi thay đổi và con số trước/sau vào báo cáo.

### Đ-4.14 Chấp nhận lời mời là một câu `UPDATE` có điều kiện; mọi thất bại của thao tác ghi cần sở hữu là 403

UC-10 bước 3: *"Chuyển Pending → Accepted (1 UPDATE atomically)"*:

```sql
UPDATE socialgraph.friendships
SET status = 'accepted', accepted_at = @now, updated_at = @now
WHERE user_min_id = @min AND user_max_id = @max
  AND status = 'pending'
  AND requester_id = @other            -- lời mời phải ĐẾN từ người kia, tức là người gọi là người NHẬN
```

0 dòng bị đổi → **403**, **một** phản hồi cho mọi lý do: không có lời mời nào · lời mời do chính mình gửi (tự chấp nhận
lời mời của mình) · đã là bạn rồi · người gọi là người thứ ba. Đây chính là US-010 AC-04 ("người không phải người nhận
chấp nhận → 403"), mở rộng theo quy ước 3b của GĐ1: với một **thao tác ghi cần sở hữu**, "không tồn tại" và "không phải
của bạn" trả cùng một mã. Không có lần `SELECT` rồi mới `UPDATE` nên không có cửa sổ race giữa hai bước.

Các thao tác khác của BR-03:

| Thao tác | Câu lệnh | Race đồng thời |
|---|---|---|
| Gửi lời mời | `INSERT` cặp chuẩn hóa, `status = 'pending'` | A→B và B→A cùng lúc: một cái đụng PK → `23505` → **409** (AC-02), không 500 |
| Hủy / từ chối | `DELETE … WHERE cặp AND status = 'pending'` | Idempotent: 0 dòng vẫn **204** |
| Hủy kết bạn | `DELETE … WHERE cặp AND status = 'accepted'` | Idempotent: 0 dòng vẫn **204** |
| Theo dõi | `INSERT … ON CONFLICT DO NOTHING` | Idempotent: **204** |
| Bỏ theo dõi | `DELETE` | Idempotent: **204** |

Gửi lời mời khi **đã có** quan hệ theo bất kỳ chiều nào (kể cả người kia đã gửi cho mình) → **409** "Đã có lời mời hoặc
quan hệ bạn bè giữa hai người." Không tự chấp nhận hộ: server không quyết định thay người dùng (cùng tinh thần Đ-3.4).
FE đọc `GET /relationships/{userId}` nên thường hiện nút "Chấp nhận" chứ không để người dùng bấm "Kết bạn" ở tình huống đó.

Gửi lời mời / theo dõi một `userId` không có hồ sơ → **404** (tra `IUserDirectory`): người chưa onboarding không tồn tại
với phần còn lại của hệ thống (Đ-2.4).

### Đ-4.15 Event trong tiến trình sau `COMMIT`, chỉ log; xóa cache cũng sau `COMMIT`

`FriendRequestSent`, `FriendRequestAccepted` phát sau `COMMIT`, **chỉ log** ở GĐ4 — GĐ6 nối notification vào (UC-10
bước 2, 4). GĐ3 chép đúng luật này (Đ-3.12).

Xóa khóa `sg:feed-sources:*` cũng **sau** `COMMIT`: xóa trước thì một request đọc chen vào giữa, nạp lại cache từ dữ liệu
**cũ**, và cache cũ sống thêm 60s.

### Đ-4.16 Frontend: hai feature mới, ghép bằng slot ở `app/`; nút quan hệ không optimistic

- `features/feed/` (danh sách feed, cuộn vô hạn, trạng thái rỗng, nhãn gợi ý, 503) và `features/friend/` (nút quan hệ, màn
  lời mời, danh sách bạn). Tên theo **màn** (luật frontend Mục 2).
- **Feed không import `PostCard`** của `features/post/` — luật frontend Mục 12 ghi đích danh ví dụ này là *không đạt*. Và
  `PostCard` biết nghiệp vụ nên cũng không đẩy xuống `components/` được. Cách giải: `FeedList` nhận prop
  `renderPost(post)`; `app/(app)/(with-profile)/page.tsx` (trang chủ) ráp `FeedList` với `PostCard` — **chỉ `app/` biết cả
  hai**, đúng khuôn slot `actions` của `PostCard` (GĐ2 E6). GĐ3 cắm thanh cảm xúc vào đúng chỗ ráp này (Đ-3.13).
- Nút quan hệ trên trang hồ sơ người khác: `PublicProfile` (của `features/profile/`) nhận slot `actions`, `app/users/[userId]`
  truyền `RelationshipButtons` của `features/friend/` vào.
- *Lệch Đ-4.16 (nhóm chốt 2026-09-23, Q-E6, L4):* trang chủ ráp `FeedList` với **`PostItem`**, không `PostCard` — feed có bài
  của chính mình (Đ-4.5), cần nút Sửa/Xóa theo `canEdit` và xóa xong phải biến khỏi feed (để lại card là để lại liên kết chết,
  cùng lý do `PostList` của GĐ2 dùng `PostItem`). `renderPost(post, onChanged)`; `FeedList` vẫn không biết `PostItem` là gì.
- *Lệch Đ-4.16 (nhóm chốt 2026-09-23, E5):* slot `actions` của `PublicProfile` là **hàm** `(profile) => ReactNode`, chỉ gọi khi
  hồ sơ đã nạp — `app/` lấy tên người kia cho hộp thoại Hủy kết bạn mà không nạp hồ sơ lần hai. `FeedList` thêm slot `action`
  (nút Đăng bài của trang chủ).
- **Nút kết bạn / theo dõi không optimistic**: bấm → nút khóa + spinner → cập nhật theo phản hồi. Thao tác hiếm, và trạng
  thái có bốn nhánh (không có · đã gửi · nhận được · là bạn) — đoán trước sai là hiện "Đã là bạn" trong khi server trả 409.
- `IntersectionObserver` của cuộn vô hạn tạo **trong effect**, ref chỉ là hộp đựng (luật frontend Mục 1 #14). `feed-list` sở
  hữu tài nguyên hủy được → **đúng một** ca `<StrictMode>` (luật frontend Mục 9).

---

## 4. Mô hình dữ liệu

Hai migration: **đầu tiên** của SocialGraph, và **một** migration nhỏ của Content cho feed gợi ý. DDL là đích đến; hiện
thực qua EF Core migration. Quy ước thời gian, UUID v7, `updated_at` giữ nguyên xi GĐ1/GĐ2.

```sql
-- ================= schema "socialgraph" =================
CREATE SCHEMA IF NOT EXISTS socialgraph;

-- ENT-04 · friendships — PK cặp chuẩn hóa CHÍNH LÀ BR-03 (một quan hệ mỗi cặp, mọi chiều)
CREATE TABLE socialgraph.friendships (
    user_min_id  uuid        NOT NULL,             -- KHÔNG FK sang identity (Đ-4.1, Đ-2.2)
    user_max_id  uuid        NOT NULL,
    requester_id uuid        NOT NULL,
    status       varchar(10) NOT NULL DEFAULT 'pending',
    created_at   timestamptz NOT NULL DEFAULT now(),
    updated_at   timestamptz NOT NULL DEFAULT now(),
    accepted_at  timestamptz,
    PRIMARY KEY (user_min_id, user_max_id),
    CONSTRAINT ck_friendships_order     CHECK (user_min_id < user_max_id),            -- BR-03: không tự kết bạn
    CONSTRAINT ck_friendships_requester CHECK (requester_id IN (user_min_id, user_max_id)),
    CONSTRAINT ck_friendships_status    CHECK (status IN ('pending','accepted')),
    CONSTRAINT ck_friendships_accepted  CHECK ((status = 'accepted') = (accepted_at IS NOT NULL))
);
-- PK phục vụ tra theo user_min_id. Tra theo user_max_id (nửa còn lại của "bạn của tôi") cần index riêng —
-- thiếu nó thì mọi truy vấn nguồn feed quét tuần tự nửa bảng.
CREATE INDEX idx_friendships_user_max ON socialgraph.friendships (user_max_id);

-- follows — PK cặp có hướng
CREATE TABLE socialgraph.follows (
    follower_id uuid        NOT NULL,
    followee_id uuid        NOT NULL,
    created_at  timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (follower_id, followee_id),
    CONSTRAINT ck_follows_not_self CHECK (follower_id <> followee_id)
);
-- "Ai theo dõi tôi" chưa có người đọc ở GĐ4 → KHÔNG tạo index theo followee_id. GĐ6 (thông báo) thêm nếu cần.

-- ================= schema "content" — thay đổi của GĐ4 =================
-- Feed gợi ý (Đ-4.6): bài công khai mới nhất toàn hệ thống, keyset DESC đọc thẳng từ index
CREATE INDEX idx_posts_public_recent ON content.posts (created_at DESC, post_id DESC)
    WHERE status = 'published' AND privacy = 'public';
```

**Bốn chỗ dễ sai:**

1. **Chuẩn hóa cặp ở MỘT chỗ.** `FriendPair.Of(a, b)` trong `Domain/` trả `(min, max)` — thứ tự phải khớp cách Postgres
   so `uuid` (so byte theo thứ tự chuỗi hex). `Guid.CompareTo` của .NET khớp thứ tự đó; **`ToByteArray()` thì không** (ba nhóm
   đầu bị đảo byte, little-endian) — ai "tối ưu" bằng cách so mảng byte sẽ chuẩn hóa sai chiều với khoảng một nửa số cặp,
   **ngẫu nhiên theo id**, và `ck_friendships_order` nổ. Unit test lấy cặp id mà hai cách so cho kết quả **ngược nhau**
   (khác nhau ở nhóm đầu) và khẳng định `FriendPair.Of` cho đúng thứ tự Postgres; integration test INSERT đúng cặp đó.
2. **`ck_friendships_accepted`** buộc `accepted_at` đi cùng trạng thái. Câu `UPDATE` của Đ-4.14 phải gán cả hai cột.
3. **Migration Content thứ hai trong đời module.** GĐ3 sẽ thêm migration thứ ba **sau** migration này. Không sửa migration
   của GĐ4 sau khi nó đã lên staging — thiếu gì thì thêm migration mới (lịch sử migration đã áp là bất biến).
4. **`CREATE INDEX` trên `posts` khi staging đã có dữ liệu** khóa ghi trong lúc dựng. Ở quy mô staging (vài trăm bài) là vô
   hại; ghi chú cho GĐ7 (production): index mới trên bảng lớn dùng `CONCURRENTLY`, và migration EF phải tắt transaction cho
   câu đó.

---

## 5. Dữ liệu nền

**GĐ4 không seed gì cho ứng dụng.** `friend.request`, `friend.respond`, `post.read.public` đã có cho `USER` từ GĐ1.

| Việc | Vì sao vẫn phải làm |
|---|---|
| `MigrateSocialGraphModuleAsync` nối vào hook `--migrate`, thứ tự Identity → Profile → Content → SocialGraph | Không nối thì staging chạy code mới trên schema chưa có; `IFriendshipReader` thật đỏ ngay request đầu tiên đọc bài `friends` |
| Dòng `socialgraph-v1` trong `apiGroups` + `AddApplicationPart` + `AddSocialGraphModule` ở `Program.cs` | Host liệt kê tường minh từng module (khuôn GĐ2 B.1) |
| Seed tải `tests/load/feed/seed.sql` | **Chỉ** cho môi trường đo (Đ-4.13). Script có chốt chặn: dừng ngay nếu `identity.users` đã có dòng — không bao giờ chạy nhầm lên staging |

---

## 6. Ba tầng kiểm soát truy cập áp vào GĐ4

### 6.1 Bảng đầy đủ: endpoint × tầng 2 × tầng 3 × mã lỗi

| Endpoint | Tầng 2 | Tầng 3 (service) | Không đạt |
|---|---|---|---|
| `POST /friends/requests` | `friend.request` | người nhận có hồ sơ · khác mình · chưa có quan hệ | **404** không có người · **400** tự gửi · **409** đã có |
| `POST /friends/requests/{userId}/accept` | `friend.respond` | lời mời `pending` từ `userId` tới người gọi | **403** (một phản hồi cho mọi lý do) |
| `DELETE /friends/requests/{userId}` | `[Authorize]` | — | 204 kể cả khi không có gì |
| `DELETE /friends/{userId}` | `[Authorize]` | — | 204 kể cả khi không có gì |
| `PUT /follows/{userId}` | `friend.request` | có hồ sơ · khác mình | **404** · **400** |
| `DELETE /follows/{userId}` | `[Authorize]` | — | 204 |
| `GET /friends`, `GET /friends/requests`, `GET /relationships/{userId}` | `[Authorize]` | — (của chính người gọi) | — |
| `GET /feed` | `post.read.public` | BR-02 trong truy vấn (Đ-4.7) + kiểm lại lúc hydrate (Đ-4.9) | — (bài không thấy thì vắng mặt) |

**Tự gửi lời mời cho mình là 400, không phải 403** (US-010 AC-03): đó là lỗi dữ liệu đầu vào, không phải thiếu quyền, và
không có tài nguyên nào để che giấu. Khẳng định **trước** khi chạm DB — để nó rơi tới `ck_friendships_order` là 500.

### 6.2 Tầng 3 không có "chủ sở hữu tài nguyên" — người gọi là một nửa của khóa

Khác GĐ2/GĐ3: quan hệ bạn bè không có cột `owner_id`. Tầng 3 ở đây có dạng: **cặp luôn dựng từ `(actorId, userId của
route)`**. Không endpoint nào nhận **hai** id người dùng từ client — không có đường để A thao tác trên quan hệ giữa B và C.
Đó là lý do bảng 6.1 ít nhánh 403: phần lớn thao tác gỡ quan hệ không thể chạm vào quan hệ của người khác ngay từ
hình dạng API.

`actorId` luôn từ `User.GetUserId()`, **không** từ route/body (luật GĐ2 Mục 6.2) — tự rà trước PR (B.9), test không phân biệt được.

### 6.3 Năm dòng AuthZ matrix mới — chỉ thêm dòng vào `AuthZMatrix.cs`

| Id | Kịch bản | Người gọi | Gọi gì | Kỳ vọng |
|---|---|---|---|---|
| `TC-A03-friend-accept` | C chấp nhận lời mời mà A gửi cho B (US-010 AC-04) | `Caller.User` (C) | `POST /api/v1/friends/requests/{A}/accept` | **403** |
| `TC-A03-friend-self-accept` | A tự chấp nhận lời mời chính A gửi cho B | `Caller.User` (A) | `POST /api/v1/friends/requests/{B}/accept` | **403** |
| `READ-06` | Người lạ đọc bài `friends` của B **sau khi** `IFriendshipReader` là hiện thực thật | `Caller.User` | `GET /api/v1/posts/{id của B}` | **404** |
| `TC-A01-feed` | Feed không kèm JWT | `Caller.Anonymous` | `GET /api/v1/feed` | **401** |
| `TC-A01-friends` | Gửi lời mời không kèm JWT | `Caller.Anonymous` | `POST /api/v1/friends/requests` | **401** |

`TC-A03-friend-self-accept` là dòng hay bị quên nhất: câu `UPDATE` thiếu vế `requester_id = @other` vẫn qua mọi test
"happy path" và cho phép ai cũng tự biến lời mời của mình thành tình bạn.

`READ-06` cần một dòng **đối chứng** (`READ-06b`, `Caller.User` là bạn của B → 200) giống `RBAC-02b` của GĐ1: một matrix chỉ
có dòng "bị chặn" thì xanh cả khi `FriendshipReader` luôn trả `false` — tức là khi bước đổi DI chưa làm gì cả.

---

## 7. Luồng nghiệp vụ

### 7.1 Kết bạn (UC-10/11, FR-010, FR-011)

```
A mở hồ sơ B   → GET /relationships/{B}            → { friendship: "none", following: false }
A bấm Kết bạn  → POST /friends/requests {userId:B}
                 API: friend.request → B có hồ sơ? (404) → B ≠ A? (400) → INSERT cặp chuẩn hóa, requester=A
                      đụng PK → 409 · COMMIT → xóa sg:feed-sources:{A},{B} → phát FriendRequestSent (log)
               ← 201 RelationshipResponse { friendship: "outgoing", … }
B mở /friends  → GET /friends/requests?direction=incoming   → thấy A
B bấm Chấp nhận → POST /friends/requests/{A}/accept
                 API: friend.respond → UPDATE có điều kiện (Đ-4.14) → 0 dòng: 403
                      COMMIT → xóa cache nguồn của cả hai → phát FriendRequestAccepted (log)
               ← 200 RelationshipResponse { friendship: "friends" }
```

`RelationshipResponse { userId, friendship: "none" | "outgoing" | "incoming" | "friends", following: bool }` — trả từ mọi
thao tác ghi để FE vẽ lại nút mà không gọi thêm `GET`.

### 7.2 SEQ-03 — đọc feed (FR-009, UC-08)

```
GET /feed?cursor=&limit=20
 1. tầng 2 post.read.public
 2. nguồn    ← IFeedSourceReader.GetAsync(me)          cache sg:feed-sources:{me} 60s · Redis lỗi → đọc DB
 3. nguồn rỗng (không bạn, không theo dõi) → mode = suggested → truy vấn idx_posts_public_recent
 4. không cursor + limit mặc định → thử cache feed:p1:{me} (30s) → trúng VÀ fp == dấu nguồn hiện tại: lấy ids, next
 5. trượt → truy vấn LATERAL (Đ-4.7), CommandTimeout 5s → quá hạn: 503 + Retry-After
            → nextCursor từ dòng thứ limit trong danh sách GỐC · null nếu ≤ limit dòng
            → ghi { ids, mode, next, fp } vào feed:p1:{me} nếu là trang đầu
 6. HYDRATE (Đ-4.9) — MỌI lần, kể cả khi trúng cache:
            trúng cache: nạp bài theo PK (status=published) · trượt: dùng luôn các dòng LATERAL vừa trả
            kiểm lại BR-02 với nguồn hiện tại (trong bộ nhớ)
            1 lô ảnh (ký URL mới) · 1 lô IUserDirectory · canEdit   (GĐ3 thêm 1 lô myReaction ở đây)
← 200 FeedPage { items, nextCursor, mode }
```

Số truy vấn DB khi trượt cache: nguồn (0–2) + feed (1) + ảnh (1) + tác giả (1) — **5** khi cả cache nguồn cũng trượt; GĐ3
thêm `myReaction` (1) — cố định, **không** phụ thuộc số bài hay số nguồn. Đây là thứ `B4` đo bằng một test đếm truy vấn
(Mục 10.2). Trúng cache trang đầu thì thay câu feed bằng một câu nạp bài theo PK.

*Sửa ngày 2026-09-22:* bản trước tính cả "bài theo PK" khi trượt cache. Truy vấn LATERAL đã trả nguyên dòng `posts` — nạp
lại theo PK ngay sau đó là một câu thừa trên đường nóng nhất.

### 7.3 Hủy kết bạn và hiệu lực lên feed

Hủy kết bạn → `DELETE` → **sau** `COMMIT` xóa `sg:feed-sources` của cả hai → request feed kế tiếp của hai người dùng
nguồn mới. Trang đầu đã cache của họ (≤ 30s) mang dấu nguồn **cũ** nên bị coi là trượt (Đ-4.8, sửa 2026-09-22); kể cả khi
trúng — đổi `privacy` của bài không đổi nguồn — hydrate vẫn kiểm lại BR-02 bằng nguồn hiện tại và loại bài không còn được
thấy (Đ-4.9). `GET /posts/{id}` thì đã đúng ngay vì `AreFriendsAsync` không cache (Đ-4.3).

Kết quả: **không có cửa sổ nào** bài `friends` của người vừa hủy kết bạn còn hiện ra — chặt hơn "chấp nhận trễ 60s" của
PTTK Mục 5.6, và không tốn thêm truy vấn nào (kiểm lại chạy trong bộ nhớ trên tập nguồn đã nạp).

---

## 8. Hợp đồng API

Base `/api/v1`. Mọi lỗi RFC 7807 kèm `traceId`. Rate limit chung 100 req/phút/user.

### 8.1 SocialGraph — `socialgraph-v1.yaml` (file mới)

| Method | Path | Auth | Thành công | Lỗi |
|---|---|---|---|---|
| POST | `/friends/requests` | Bearer + `friend.request` | 201 `RelationshipResponse` | 400 tự gửi / id sai · 401 · 404 · 409 |
| GET | `/friends/requests?direction=incoming\|outgoing&cursor=&limit=` | Bearer | 200 `FriendRequestPage` | 400 · 401 |
| POST | `/friends/requests/{userId}/accept` | Bearer + `friend.respond` | 200 `RelationshipResponse` | 400 id sai · 401 · 403 |
| DELETE | `/friends/requests/{userId}` | Bearer | 204 | 400 · 401 |
| GET | `/friends?cursor=&limit=` | Bearer | 200 `FriendPage` | 400 · 401 |
| DELETE | `/friends/{userId}` | Bearer | 204 | 400 · 401 |
| PUT | `/follows/{userId}` | Bearer + `friend.request` | 204 | 400 tự theo dõi / id sai · 401 · 404 |
| DELETE | `/follows/{userId}` | Bearer | 204 | 400 · 401 |
| GET | `/relationships/{userId}` | Bearer | 200 `RelationshipResponse` | 400 · 401 |

```
CreateFriendRequest   { userId (bắt buộc) }
RelationshipResponse  { userId, friendship: "none" | "outgoing" | "incoming" | "friends", following: boolean }
FriendCard            { user: { userId, displayName, avatarUrl? }, since }       // since = accepted_at hoặc created_at
FriendPage            { items: [FriendCard], nextCursor: string | null }         // bạn bè: accepted_at DESC
FriendRequestPage     { items: [FriendCard], nextCursor: string | null }         // lời mời: created_at DESC
```

- `user` của `FriendCard` dùng cùng hình dạng `PostAuthor` của `content-v1` nhưng **định nghĩa lại trong `socialgraph-v1`**
  (tên `UserCard`): hai file hợp đồng độc lập, không `$ref` chéo file — `ContractTestsBase` đọc từng file riêng, và `$ref`
  chéo file là một cổng CI phải hiểu thêm một thứ.
- Cursor của `FriendPage` dùng cùng bộ mã hóa keyset của GĐ2 (`thời điểm|id của người kia`), mờ với client.
  Cùng cách mã hóa, không cùng kiểu: `FriendCursor` là bản chép của `PostCursor` (lệch L13, ghi ở B.6 `D5`).
- `GET /relationships/{userId}` với chính mình → 400 (không có quan hệ nào với chính mình để hỏi).

**Chốt lúc viết hợp đồng (2026-09-22, cổng mở).** Bảng trên để ngỏ năm chỗ; `socialgraph-v1.yaml` chốt như sau:

- `POST /friends/requests` và `PUT /follows/{userId}` có thêm **403**: cả hai mang `friend.request` (`[RequirePermission]`),
  thiếu quyền là 403 — cùng cách `content-v1` ghi 403 cho `POST /posts`.
- `direction` của `GET /friends/requests` mặc định `incoming`; giá trị lạ → 400 `errors.direction`.
- `limit` của hai danh sách: mặc định 20, tối đa 50 (AGENTS.md Mục 9, cùng `content-v1`).
- Tự gửi lời mời / tự theo dõi → 400 `errors.userId`, thông điệp *"Không thể gửi lời mời kết bạn cho chính mình."* /
  *"Không thể theo dõi chính mình."*
- `GET /relationships/{userId}` với `userId` không tồn tại → **200** `none` / `false`, không 404: endpoint đọc quan hệ
  không phải chỗ để dò ai có tài khoản.

### 8.2 Content — thêm vào `content-v1.yaml` (chỉ-thêm)

> **Vào file ở commit của `D7`, không ở cổng mở** (lệch Mục 9.2, chốt 2026-09-22 — lý do ở đó). Đặc tả dưới đây là
> hình dạng đã chốt; `D7` chép nó vào yaml cùng lúc với controller.

| Method | Path | Auth | Thành công | Lỗi |
|---|---|---|---|---|
| GET | `/feed?cursor=&limit=` | Bearer + `post.read.public` | 200 `FeedPage` | 400 cursor sai · 401 · **503** + `Retry-After`, `type` `feed-overloaded` (Q-E4) |

```
FeedPage { items: [PostResponse], nextCursor: string | null, mode: "network" | "suggested" }
FeedOverloadedProblem = ProblemDetails & { type: "urn:socialapp:problem:feed-overloaded" }   // 503 — thêm ở Q-E4
```

- `items` dùng lại **đúng** `PostResponse` (`$ref`). Khi GĐ3 thêm `myReaction`, feed có trường đó **tự động**.
- Mô tả của `nextCursor` ghi nguyên văn: *"`items` có thể ít hơn `limit`. Hết dữ liệu khi và chỉ khi `nextCursor` là
  `null`."* (Đ-4.9)
- `info.version` của `content-v1` → `1.0.0-gd4`; Q-E4 (2026-09-23, sau `D7`) → `1.0.1-gd4`.
- *Lệch đã biết (Q-E4):* Swagger sinh từ code vẫn khai 503 là `ProblemDetails` — thêm một lớp DTO chỉ để schema mang tên
  `FeedOverloadedProblem` không đáng. Dữ liệu trên dây giống hệt; yaml là nguồn sự thật, `ContentContractTests` không so
  schema response.

### 8.3 Codegen frontend

Không thêm script: `pnpm gen:api` tự sinh `lib/api/socialgraph/schema.d.ts` ngay khi `socialgraph-v1.yaml` được commit
(luật frontend Mục 7, glob `Modules/*/Presentation/*-v1.yaml`). Alias thêm vào `lib/api/types.ts`; client mới
`lib/api/socialgraph-api.ts`. Cổng CI `API types khop hop dong` không phải sửa.

---

## 9. Kế hoạch thi công (một người, tuần tự)

### 9.1 Thứ tự với GĐ3 — GĐ4 trước, và ba chỗ để GĐ3 cắm vào

GĐ4 làm trước vì nó nằm trên đường găng (GĐ5 cần `friendships` cho BR-09) và chứa mục tiêu không cắt được (GOAL-01); GĐ3
thì cắt được và không giai đoạn nào chờ nó. GĐ4 không làm việc nào của GĐ3, nhưng dựng sẵn ba chỗ để GĐ3 **không phải
sửa feed**:

| # | GĐ4 dựng sẵn | GĐ3 dùng thế nào |
|---|---|---|
| 1 | **Một hàm hydrate** batch cho mọi đường trả `PostResponse` (C3) | Thêm một lô `myReaction` vào đúng hàm đó → feed, trang cá nhân, chi tiết bài có trường mới cùng lúc |
| 2 | Cache chỉ lưu `post_id` (Đ-4.9) + test `FEED-13` hai người xem | `myReaction` không bao giờ lọt vào cache; GĐ3 mở rộng `FEED-13` thêm khẳng định `myReaction` |
| 3 | Trang chủ ráp `FeedList` + `PostCard` ở `app/` qua `renderPost` (Đ-4.16) | GĐ3 cắm thanh cảm xúc vào chỗ ráp đó, không chạm `features/feed/` |

Việc GĐ3 phải làm lại khi tới lượt (đã ghi vào `giai-doan-3.md`): chạy lại k6 lượt (2) sau khi `myReaction` vào đường
hydrate, và chạy `READ-CMT-*` / `READ-REACT-*` với bài `friends` giữa hai người là **bạn thật** — việc mà GĐ4 làm cho
khả thi.

### 9.2 Cổng mở — nửa ngày đầu

Làm một mình thì không có buổi họp, nhưng **sản phẩm của cổng mở vẫn bắt buộc** (Mục 0C): hợp đồng commit trước, code
sau. Hợp đồng viết trước còn có một lợi ích riêng cho người làm một mình — lane frontend dựng trên `msw/node` từ hợp đồng
được ngay, không phải chờ backend chạy.

1. Tự rà **Đ-4.1 → Đ-4.16**; sửa cái nào thì ghi ngày và lý do dưới quyết định đó.
2. Viết `socialgraph-v1.yaml` + phần `/feed` của `content-v1.yaml` → `pnpm gen:api` → **commit cả yaml lẫn `schema.d.ts`**.
   Cổng `API contract` đỏ có chủ đích tới khi `D*` có controller — ghi rõ trong commit, như GĐ2 để matrix đỏ chờ `D5`–`D8`.

   **Lệch Mục 9.2 (chốt 2026-09-22): phần `/feed` của `content-v1.yaml` HOÃN tới commit có controller feed (`D7`).**
   Bước này viết khi ngầm coi cổng hợp đồng chưa canh `content-v1` — đúng ở GĐ2 (`ContentContractTests` ra đời ở `B4`
   GĐ2), sai ở GĐ4: nó đã chạy trong CI, nên thêm `GET /feed` khi chưa có action là `Contract_must_be_fully_implemented`
   đỏ liên tục qua cả khối A, B, C — và PR của từng khối không merge được vào `develop`. `socialgraph-v1.yaml` thì
   **đã** commit ở cổng mở như kế hoạch: `SocialGraphContractTests` là việc của `B5` nên không có gì đỏ.
   Cái giá, chấp nhận vì một người làm tuần tự (FE feed vốn đứng sau `D7`): type `FeedPage` chưa sinh được trước `D7`.
   Hình dạng `/feed` không đổi — vẫn đúng Mục 8.2, `info.version` → `1.0.0-gd4` ở chính commit đó, `pnpm gen:api` cùng
   commit (luật frontend Mục 7).
3. Chốt **máy chạy k6 và môi trường đo** (Đ-4.13): máy nào, giới hạn tài nguyên bao nhiêu. Không chốt ở đây thì tới lúc
   cần đo mới đi tìm máy.

### 9.3 Thứ tự thi công và ước lượng

Kế hoạch gốc giao GĐ4 cho **2 backend + 1 frontend trong 4 ngày**. Một người làm cả hai lane thì tổng khối lượng không đổi,
chỉ bỏ được phần phối hợp — ước lượng thật là **khoảng 8 ngày làm việc**. Ghi thẳng con số này ra để lịch tổng được sửa
theo, thay vì âm thầm trượt (Mục 13).

| Bước | Việc | Ước lượng | Xong khi | Hướng dẫn thi công |
|---|---|---|---|---|
| 1 | Cổng mở (Mục 9.2) | 0,5 ngày | Hợp đồng + `schema.d.ts` đã commit | Mục 9.2 |
| 2 | **A1–A5** nền dữ liệu + đổi DI · **B1–B2** harness + matrix đỏ có chủ đích | 1 ngày | `READ-01..05` xanh không sửa khẳng định; năm dòng matrix mới đỏ đúng lý do, `READ-06` xanh | [khối A](huong-dan-khoi-a-nen-du-lieu.md) · [B+C+D](huong-dan-khoi-b-c-d-test-feed-endpoint.md) Phần I |
| 3 | **C5** môi trường đo + seed 1M bài | 0,5 ngày | `seed.sql` chạy xong trên môi trường đo | [B+C+D](huong-dan-khoi-b-c-d-test-feed-endpoint.md) Phần II |
| 4 | **D0** nền chung · **C1** cache nguồn feed + `InvalidateAsync` · **D1–D6** endpoint quan hệ · **B3** test quan hệ + nửa bảng đột biến phần quan hệ · **B5** cổng hợp đồng `socialgraph-v1` | 1,2 ngày | `FRD-*`, `FOL-*` xanh; matrix 24/24 từ `D3` (`TC-A01-feed` đã xanh 401 từ `B2` — sửa 2026-09-23, trước ghi 23/24 chờ `D7`); cổng `API contract` canh bốn module | [B+C+D](huong-dan-khoi-b-c-d-test-feed-endpoint.md) Phần III |
| 5 | **C2–C4** LATERAL, hydrate, cache trang đầu, degrade · **D7** `GET /feed` · **B4** test feed + nửa bảng đột biến phần feed | 1,3 ngày | `EXPLAIN` trên bộ dữ liệu tải đúng hình dạng Đ-4.7; `FEED-*`, `FEED-Q1` xanh; matrix 24/24 | [B+C+D](huong-dan-khoi-b-c-d-test-feed-endpoint.md) Phần IV |
| 6 | **C6** k6 ba lượt + báo cáo sơ bộ | 1 ngày | Báo cáo có số, kể cả khi không đạt — nửa ngày còn lại để sửa theo `EXPLAIN` | [B+C+D](huong-dan-khoi-b-c-d-test-feed-endpoint.md) Phần V |
| 7 | **E1–E6** toàn bộ lane frontend | 2 ngày | Vitest + Playwright xanh ở local | [E+F](huong-dan-khoi-e-f-frontend-va-cong-dong.md) Mục 2–7 |
| 8 | **F1–F4** cổng đóng | 0,5 ngày | Mục 11, 12 đã tick hoặc ghi "chờ server" | [E+F](huong-dan-khoi-e-f-frontend-va-cong-dong.md) Mục 8–11 |

**Lệch bảng bước (chốt 2026-09-22, lúc viết hướng dẫn B+C+D):**

- **`B5` dời từ bước 7 lên bước 4**, ngay sau `D6`. `socialgraph-v1.yaml` đã commit từ cổng mở và bảy endpoint quan hệ xong ở
  cuối bước 4; để `B5` ở bước 7 là suốt bước 5–6 — đúng lúc `D7` rà RFC 7807 cho cả hai nhóm — không có cổng nào so hợp đồng
  `socialgraph-v1` với code. Cùng lượng việc, chỉ đổi chỗ; khối E (bước 7) dựng trên hợp đồng CI đã chứng nhận.
- **`C1` dời từ bước 5 lên bước 4**, ngay sau `D0`. `D2`–`D6` phải gọi `InvalidateAsync` sau `COMMIT` (Đ-4.15); có `C1`
  trước thì chúng gọi hàm thật ngay từ đầu, thay vì đăng ký tạm một bản rỗng rồi thay ở bước 5 — đúng loại "đăng ký tạm"
  đã sinh ra bẫy `AlwaysStrangers` (Đ-4.3). `C1` chỉ cần `A5` và Redis thật trong harness (`B1`), cả hai có từ bước 2. Tổng
  thời lượng bước 4 + 5 không đổi (2,5 ngày).
- **Bảng đột biến của `B3` tách làm hai nửa**: nửa quan hệ ở bước 4, nửa feed ở bước 5 — năm dòng của bảng cần `FEED-*`.
- **Hướng dẫn khối B, C, D gộp làm một file**, sắp theo **bước** chứ không theo khối: ở bước 4 và bước 5, việc của ba khối
  đan vào nhau từng giờ (`FRD-*` viết trước từng `D*`; `D7` là vỏ mỏng của `C1`–`C4`; `B4` cần cả hai). Ba file riêng là
  ba file trỏ chéo nhau liên tục.
- Cột "Xong khi" của bước 2 bỏ "`READ-06b` xanh": dòng đó cần `D2`+`D3` (lệch L2 của khối A), xanh ở bước 4.

**Vì sao backend và k6 đứng trước frontend** — ngược với nếp "hai lane song song" của GĐ1–GĐ2: làm một mình thì không có
song song, và rủi ro lớn nhất của giai đoạn là GOAL-01. Biết p95 ở bước 6 (khoảng ngày thứ 6) còn thời gian sửa; biết ở
cổng đóng thì không. Frontend dựng trên hợp đồng đã chốt ở bước 1 nên không bị backend kéo lùi.

**Bước 3 đứng trước bước 4–5 có chủ đích:** có bộ dữ liệu tải sớm thì `EXPLAIN` của truy vấn feed chạy được **ngay khi
viết** ở bước 5, không phải chờ tới k6 mới biết truy vấn sai hình dạng.

**Nhánh và PR:** làm trên `loveart1210`, commit theo mã việc (scope `gd4-<khối>`), một PR vào `develop` khi cả giai đoạn xong
(luật PR Mục 2 — một PR một khối). Không có người review chéo: danh sách "tự rà" ở B.9 thay chỗ đó — chạy nó **trước khi
mở PR**, và đưa nó cho người duyệt PR nếu có.

### 9.4 Cổng đóng

Frontend trỏ staging HTTPS thật; E2E hai tài khoản (một người điều khiển hai trình duyệt / hai profile Chrome); báo cáo k6
sơ bộ; **đóng băng `socialgraph-v1`** và phần `/feed` của `content-v1`; tick Mục 11 và Mục 12. GĐ3 khởi động ngay sau đó.

### 9.5 Thư viện cần thêm

| Gói | Ở đâu | Ghi chú |
|---|---|---|
| — | backend | Không. Redis, EF, Npgsql đã có |
| — | frontend | Không. `IntersectionObserver` có sẵn trong trình duyệt |
| **k6** | máy chạy đo | Công cụ, **không** vào repo, không vào CI. Ghi bản đã dùng vào báo cáo |

---

## 10. Chiến lược test

### 10.1 Nghiệm thu chức năng — quan hệ (integration, Postgres thật)

| Mã | Kịch bản | Kỳ vọng |
|---|---|---|
| `FRD-01` | A gửi lời mời cho B (US-010 AC-01 nửa đầu) | 201, `outgoing`; B thấy A trong `incoming` |
| `FRD-02` | A gửi lại lần hai (AC-02) | 409 |
| `FRD-03` | A gửi cho chính mình (AC-03) | 400, **không** có dòng nào |
| `FRD-04` | A gửi cho id không có hồ sơ | 404 |
| `FRD-05` | B chấp nhận (AC-01 nửa sau) | 200 `friends`; `accepted_at` khác null; hai bên thấy nhau trong `GET /friends` |
| `FRD-06` | A→B và B→A gửi **song song** | một 201, một 409; đúng một dòng; không 500 |
| `FRD-07` | B từ chối | 204, dòng biến mất; A gửi lại được |
| `FRD-08` | A hủy lời mời đã gửi | 204, dòng biến mất |
| `FRD-09` | Hủy kết bạn | 204; `AreFriendsAsync` = false ngay lập tức |
| `FRD-10` | Chấp nhận khi lời mời đã bị hủy (race) | 403 |
| `FOL-01..04` | Theo dõi · theo dõi lần hai (204, một dòng) · tự theo dõi (400) · bỏ theo dõi | như bảng Đ-4.14 |

### 10.2 Nghiệm thu chức năng — feed

| Mã | Kịch bản | Kỳ vọng |
|---|---|---|
| `FEED-01` | 25 bài từ bạn bè, `limit=20` (US-008 AC-01) | 20 bài mới trước + `nextCursor`; trang 2 đủ 5, `nextCursor=null` |
| `FEED-02` | Bài `friends` của người lạ (AC-02) | **không** xuất hiện |
| `FEED-03` | Bài `friends` của người chỉ-theo-dõi | **không** xuất hiện; bài `public` của họ **có**; `mode = network` |
| `FEED-04` | Bài `friends` của bạn | xuất hiện |
| `FEED-05` | Bài `private` của bạn · bài `private` của mình | của bạn: **không** · của mình: **có** (Đ-4.5) |
| `FEED-06` | Bài `hidden` của bạn (AC-03, INSERT thẳng trạng thái vì GĐ6 chưa có endpoint) | **không** xuất hiện |
| `FEED-07` | Người chưa có kết nối | `mode = suggested`, bài `public` của người khác, **không** có bài của mình |
| `FEED-07b` | Người mới đọc feed (gợi ý, đã cache) → có kết nối đầu tiên → đọc lại trong 30s | `mode = network`, có bài của người vừa kết nối (canh dấu nguồn, Đ-4.8) |
| `FEED-08` | Có một bạn nhưng bạn chưa đăng gì | `mode = network`, `items` rỗng, `nextCursor=null` — **không** trộn gợi ý |
| `FEED-09` | Hủy kết bạn khi trang đầu đang nằm trong cache | request kế tiếp **không** còn bài `friends` của người kia; trang có thể ngắn hơn `limit`, `nextCursor` vẫn đúng |
| `FEED-09b` | Tác giả đổi bài `public` → `friends` khi trang đầu của người **chỉ theo dõi** đang cache | bài **không** còn — lưới duy nhất của bước kiểm lại BR-02 ở hydrate (dấu nguồn không đổi nên cache vẫn trúng) |
| `FEED-10` | Trúng cache (`FakeObjectStorage` ký mỗi lần một URL khác) | URL ảnh lần 2 **khác** lần 1; giá trị thô trong Redis không chứa URL |
| `FEED-11` | Redis dừng | 200, cùng nội dung, log cảnh báo |
| `FEED-12` | Truy vấn feed chậm quá 5s (khóa `content.posts` từ kết nối khác) | 503 + `Retry-After: 5` + `title` + `type` `urn:socialapp:problem:feed-overloaded` (Q-E4); `feed.unavailable` không lên dây (Đ-4.10) |
| `FEED-13` | Hai người xem cùng trang đầu đã cache của **mỗi người** | `canEdit`/`myReaction` của ai đúng người đó; giá trị thô chỉ `{ids, mode, next, fp}` (canh Đ-4.9) |

*Sửa ngày 2026-09-22 (hướng dẫn B+C+D, L2/L3/L5/L9/L11/L15):* thêm `FEED-07b`, `FEED-09b`; `FEED-10` bỏ đồng hồ giả vì
`R2ObjectStorage` tính hạn bằng `DateTime.UtcNow` và fake trả URL hằng — đồng hồ giả không phân biệt được gì; `FEED-12` bỏ
`pg_sleep` vì không có chỗ chèn nó vào truy vấn của app mà không thêm hook vào code sản phẩm.
| `PAGE-04` | Cursor rác | 400 `errors.cursor` |

**Test đếm truy vấn** (`FEED-Q1`): bắt lệnh SQL qua `DbCommandInterceptor` của test; trang đầu trượt cache với 50 nguồn
và 20 bài có ảnh → số lệnh là **hằng số** của Mục 7.2, và **không đổi** khi tăng lên 200 nguồn. Đây là lưới cho N+1 ở đúng
endpoint trọng điểm hiệu năng — k6 thấy N+1 muộn và mơ hồ, test này thấy sớm và chỉ đích danh.

### 10.3 Đổi DI của `IFriendshipReader`

- Test khởi động (Đ-4.3): resolve từ host thật → **không** phải `AlwaysStrangers`, đúng một đăng ký.
- `READ-01..05` của GĐ2 chạy lại **không sửa khẳng định nào**; `READ-06` + `READ-06b` mới.
- Thử cho đỏ: khôi phục dòng `AlwaysStrangers` trong `AddContentModule` → test khởi động phải đỏ (hai đăng ký).
  *Sửa 2026-09-23 (thử thật ở B3, B4):* `READ-06b` **vẫn xanh** — `AddContentModule` chạy trước `AddSocialGraphModule` nên
  đăng ký sau thắng và host vẫn dùng `FriendshipReader`. `READ-06b` chỉ đỏ khi `AlwaysStrangers` đứng **sau**; lưới của
  đột biến này là test khởi động.

### 10.4 Unit test

`FriendPair.Of` (chuẩn hóa, và cặp id mà so `ToByteArray()` cho thứ tự ngược Postgres — Mục 4 cạm bẫy 1) · luật
chuyển trạng thái quan hệ thành `FriendshipView` qua `RelationshipState` (bốn nhánh; L1) · luật mức nhìn theo loại nguồn
(bảng Đ-4.5) dạng hàm thuần — và **một test đối chiếu** hàm thuần đó với mệnh đề `WHERE` của truy vấn LATERAL trên cùng
bộ dữ liệu, cùng nếp `PostVisibilityTests` của GĐ2 (hai bản của một luật là chỗ lệch được).

### 10.5 Cổng CI phải mở rộng

1. `IntegrationTests.csproj`: thêm dòng `Content Include … Link=Contracts/socialgraph-v1.yaml`.
2. `SocialGraphContractTests` (chép `ContentContractTests`) mang `[Trait("Category","Contract")]` — bước `API contract (CI
   GATE)` tự chạy nó. Thử cho đỏ: thêm một mã trả về vào controller mà không sửa yaml.
3. `AuthZMatrix.cs`: năm dòng Mục 6.3 + `READ-06b`.
4. `SocialGraphPermissionsTests` ở ArchitectureTests.
   Lệch B.4/B.5/B.6 (nhóm chốt): viết trong `D0` cùng hằng `SocialGraphPermissions` (nếp `Q-D5` GĐ2); `B5` chỉ thử đỏ.
5. `ModuleBoundaryTests` / `PresentationBoundaryTests` / `PersistenceBoundaryTests`: **không** sửa danh sách module —
   chúng đã liệt kê SocialGraph từ GĐ0.
   Lệch B.3 (nhóm chốt): không có `Skip` nào để gỡ. Chỉ **thêm** `SocialGraph_Domain_namespace_must_not_be_empty` trong
   commit `A1` (cùng entity đầu tiên), đúng nếp `A7` của GĐ2.
6. Cổng codegen FE: không sửa (đã tự suy danh sách hợp đồng).
7. **k6 không vào CI** (cần 1.000 VU và bộ dữ liệu 1M bài). Báo cáo k6 là bằng chứng kiểm tay, dán vào PR — cùng nếp
   Playwright.

### 10.6 Frontend

| Công cụ | Kiểm |
|---|---|
| Vitest + `msw/node` | Nút quan hệ đủ bốn trạng thái + 409/403/404 · màn lời mời chấp nhận / từ chối · feed: trang đầu, cuộn thêm theo `nextCursor`, **trang ngắn hơn `limit` mà `nextCursor` khác null thì vẫn cuộn tiếp**, nhãn gợi ý khi `mode=suggested`, 503 hiện nút Thử lại |
| Vitest `<StrictMode>` | **Đúng một ca** cho `feed-list` (sở hữu `IntersectionObserver` + request hủy được). Khẳng định trạng thái cuối, **không** đếm request |
| Playwright (local, `workers: 1`) | Hai tài khoản: A mới tinh thấy feed gợi ý → bấm tên B → Kết bạn → B chấp nhận → A thấy bài `friends` của B trên trang chủ → A hủy kết bạn → bài đó biến mất |

---

## 11. Definition of Done

Theo Mục 3.5 của PTTK, áp cho **từng** UC (UC-08, UC-10/11, UC-13). Tick ở `F4`, kèm bằng chứng.

- [ ] Đủ AC: US-008 AC-01..03, US-010 AC-01..04, FR-012 — Mục 10.1, 10.2 xanh
- [ ] **US-008 AC-04 (p95 ≤ 500ms @ 1.000 CCU) — sơ bộ**: báo cáo k6 ba lượt (Đ-4.13), lượt lạnh đạt ngưỡng; hoặc **không
  đạt** kèm phân tích `EXPLAIN` và việc cụ thể chuyển sang GĐ8 — không tick khi chưa có báo cáo
- [ ] Có kiểm RBAC (tầng 2) **và** tầng 3; năm dòng matrix + `READ-06b` xanh trên CI; bảng đột biến `B3`
- [ ] Lỗi theo RFC 7807, `errors` đúng key; 403 của chấp nhận lời mời không phân biệt lý do
- [ ] Đã chạy thử trên **staging** bằng hai tài khoản thật (`F3`)
- [ ] Hai hợp đồng khớp Swagger runtime; `pnpm gen:api` chạy lại thì worktree sạch
- [ ] Không lộ secret/PII: khóa ký JWT của môi trường đo **không** có trong repo; log feed không chứa URL ký

## 12. Checklist nghiệm thu cuối GĐ4

**Dữ liệu và ranh giới**

- [ ] Thấy schema `socialgraph` với `__EFMigrationsHistory` riêng; `--migrate` chạy hai lần, lần hai không đổi gì
- [ ] Không FK nào đi qua ranh giới schema (`information_schema.referential_constraints`), kể cả trong `socialgraph`
- [ ] `EXPLAIN (ANALYZE)` truy vấn feed trên bộ dữ liệu tải: `Nested Loop` + `Index Scan using idx_posts_author_created`,
  **không** `Sort` trên toàn bộ bài — kế hoạch dán vào hướng dẫn khối C
- [ ] `EXPLAIN` danh sách bài theo tác giả trước và sau Đ-4.11 — ghi vào commit

**Bảo mật và đúng quyền**

- [ ] Test khởi động: `IFriendshipReader` không phải `AlwaysStrangers`, đúng một đăng ký
- [ ] Thử cho đỏ: bỏ vế `requester_id = @other` → `TC-A03-friend-self-accept` đỏ; khôi phục `AlwaysStrangers` → test khởi
  động đỏ (`READ-06b` vẫn xanh vì đăng ký của SocialGraph đứng sau — Mục 10.3, sửa 2026-09-23)
- [ ] Redis trên môi trường đo: không khóa `feed:p1:*` nào chứa chuỗi `X-Amz-Signature` hay `myReaction` (lệnh `redis-cli
  --scan --pattern 'feed:*'` + `GET` vài khóa)

**Hiệu năng**

- [ ] Báo cáo k6 sơ bộ: máy chạy, bản k6, bộ dữ liệu, ba lượt, p50/p95/p99, tỷ lệ lỗi, số kết nối DB đỉnh — lưu ở
  `docs/giai-doan-4/bao-cao-k6-so-bo.md`
- [ ] Lượt Redis-dừng: tỷ lệ lỗi < 1%

**Lát cắt dọc**

- [ ] E2E trên staging, hai tài khoản: feed gợi ý → kết bạn → chấp nhận → bài `friends` hiện trên trang chủ → hủy kết bạn →
  biến mất (`F3`)

## 13. Sai khác so với kế hoạch gốc và báo cáo v5.0

| # | Kế hoạch gốc / v5.0 | GĐ4 thực hiện | Lý do |
|---|---|---|---|
| 1 | ENT-04: `user_min_id`, `user_max_id`, `requester_id` là `FK users` | `uuid` trần, không FK (Đ-4.1) | Đ-2.2 áp cho mọi module từ GĐ2 |
| 2 | FR-009: feed = bạn bè + đang theo dõi | Thêm **bài của chính mình** (Đ-4.5) | Người vừa đăng bài không thấy bài mình trên trang chủ trông như đăng thất bại |
| 3 | Kế hoạch gốc: "trạng thái rỗng cho tài khoản mới" | **Feed gợi ý** bài công khai khi chưa có kết nối (Đ-4.6) | Chính UC-08 A1 / FR-009 yêu cầu; và trước GĐ6 đây là đường duy nhất để gặp người khác |
| 4 | Mục 5.6: `author_id IN (…)` trên `idx_posts_author_created` | Một `LATERAL` mỗi nguồn trên cùng index (Đ-4.7) | `IN` nhiều giá trị buộc Postgres sắp toàn bộ bài của mọi nguồn; `LATERAL` chặn chi phí theo `số nguồn × limit` |
| 5 | Mục 5.6: kiểm quan hệ theo cặp cache 60s, chấp nhận trễ 60s khi hủy kết bạn | `AreFriendsAsync` đọc thẳng DB; cache 60s chỉ cho nguồn feed, xóa khi đổi quan hệ; hydrate kiểm lại BR-02 (Đ-4.3, Đ-4.9) | Chặt hơn, không tốn thêm truy vấn: không còn cửa sổ lộ bài `friends` |
| 6 | Kế hoạch gốc: "đổi đúng một dòng DI" trong `ContentModuleExtensions` | Xóa dòng đó ở Content, đăng ký hiện thực ở SocialGraph (Đ-4.3) | Content không được thấy kiểu của SocialGraph; đổi tại chỗ không biên dịch được qua `ModuleBoundaryTests` |
| 7 | Ma trận 6.7.2 không có mã quyền cho theo dõi | Theo dõi dùng `friend.request`; gỡ quan hệ chỉ `[Authorize]` (Đ-4.12) | Giữ 17 mã; gỡ quan hệ của mình là quyền của chủ dữ liệu |
| 8 | US-010 AC-04: người thứ ba chấp nhận → 403 | 403 cho **mọi** lý do chấp nhận thất bại, kể cả tự chấp nhận lời mời của mình (Đ-4.14) | Quy ước 3b của GĐ1 cho thao tác ghi cần sở hữu |
| 9 | "Trả 20 bài" | Trang có thể ngắn hơn `limit`; hết khi `nextCursor = null` (Đ-4.9) | Hydrate kiểm lại BR-02 trên danh sách đã cache |
| 10 | k6 đo "@1.000 CCU" (không nói ở đâu) | Môi trường đo riêng, dữ liệu sinh bằng SQL, token ký bằng khóa riêng, ba lượt (Đ-4.13) | Không làm bẩn staging; không đụng BCrypt và rate limit auth; con số lạnh không phụ thuộc tỷ lệ trúng cache |
| 11 | GĐ4 do 2 backend + 1 frontend làm trong 4 ngày (Ngày 9–13) | **Một người** làm cả hai lane, tuần tự, ước lượng ~8 ngày làm việc (Mục 9.3) | Nhân lực thực tế. Thứ tự GĐ4 → GĐ3 giữ nguyên lịch gốc; lịch tổng phải dời theo, không nén GĐ4 |

Mỗi dòng phải được nhắc lại trong commit tương ứng, mở bằng "Lệch …" theo luật commit Mục 5.3.

## 14. Rủi ro cần theo dõi

| Mã | Rủi ro | Dấu hiệu sớm | Ứng phó |
|---|---|---|---|
| **PERF-02** | k6 trượt 500ms | Lượt (2) p95 > 400ms ở bước 6 (Mục 9.3) | Theo thứ tự Đ-4.13: `EXPLAIN` → pool kết nối → người dùng ở đuôi. Nếu vẫn trượt: ghi vào DoD là **không đạt sơ bộ**, chuyển việc cụ thể sang GĐ8 — **không** giảm số VU hay tăng thời gian nghỉ cho đẹp số |
| **PERF-03** | Pool kết nối cạn trước khi CPU cạn | p95 cao nhưng CPU Postgres thấp; log Npgsql "pool exhausted" | Đặt `Maximum Pool Size` tường minh, ≤ `max_connections` trừ dự phòng cho `migrate`/backup; ghi con số vào báo cáo. *Đã xảy ra ở k6 sơ bộ (lượt Redis dừng: pool 100 chiếm hết `max_connections` 100) — sửa 2026-09-23: mặc định 80 trong code (`PostgresPool`), báo cáo k6 Mục 5* |
| **CACHE-01** | Cache chứa trường theo người xem / URL đã ký | `FEED-10`, `FEED-13` đỏ; khóa Redis chứa `X-Amz-Signature` | Đ-4.9: cache chỉ lưu `post_id`. Tự rà mọi chỗ `StringSet` của feed (B.9) |
| **DI-01** | `AlwaysStrangers` còn đăng ký, BR-02 chạy giả | Test khởi động đỏ (hai đăng ký); `READ-06b` chỉ đỏ khi `AlwaysStrangers` đăng ký **sau** SocialGraph; bài `friends` của bạn không hiện trên staging | Test khởi động (Đ-4.3) |
| **GUID-01** | Chuẩn hóa cặp lệch thứ tự `uuid` của Postgres (so mảng byte thay vì `Guid.CompareTo`) | `23514` (vi phạm CHECK) ngẫu nhiên trên khoảng một nửa số cặp | Một hàm `FriendPair.Of` duy nhất + unit test và integration test với cặp id đối nghịch (Mục 4 cạm bẫy 1) |
| **STAFF-01** | Một người làm cả giai đoạn: lịch trượt âm thầm | Hết bước 5 (Mục 9.3) mà `EXPLAIN` chưa đúng hình dạng — đã tiêu ~4,5/8 ngày | Kiểm tiến độ theo **bước**, không theo cảm giác; trễ thì cắt theo B.9 ngay, không đợi cổng đóng |
| **REV-01** | Không ai review chéo — lỗi mà chỉ con mắt thứ hai bắt được sẽ lọt | Năm mục "tự rà" ở B.9 chưa chạy trước khi mở PR | Chạy danh sách B.9 trước PR; bảng đột biến `B3` thay một phần việc review: mỗi luật quan trọng đã từng thấy test đỏ |

---

# Phần B — Kế hoạch triển khai

## B.0 Cách đọc phần này

Phần A nói *cái gì* và *vì sao*. Phần B chia việc thành **sáu khối A–F**, mỗi khối một chuỗi đầu việc có mã (`A1`, `C3`,
…). Một người làm hết; **thứ tự làm** nằm ở Mục 9.3, không phải thứ tự chữ cái của khối. Mã việc đi vào **tiêu đề commit** (luật commit Mục 4) với scope `gd4-<khối>` — ví dụ
`feat(gd4-c): C2 — feed một LATERAL mỗi nguồn trên idx_posts_author_created, BR-02 trong truy vấn`.

Một đầu việc xong khi: code chạy · có test · tài liệu/hợp đồng sửa **trong cùng commit** · `detect-changes` sạch.

## B.1 Điểm xuất phát — cái gì đã có sẵn

Kiểm ngày 2026-09-21 trên nhánh `loveart1210` (sau PR #20):

| Đã có | Ở đâu | GĐ4 dùng để làm gì |
|---|---|---|
| Project `SocialApp.Modules.SocialGraph` rỗng (chỉ `.gitkeep`), đã tham chiếu SharedKernel | `src/backend/Modules/SocialGraph/` | Vỏ của module — thêm `Presentation/`, `DependencyInjection/` |
| `IFriendshipReader` + `AlwaysStrangers` | `SharedKernel/Contracts/IFriendshipReader.cs` | Đổi hiện thực (Đ-4.3) |
| Dòng DI `AddSingleton<IFriendshipReader, AlwaysStrangers>()` | `ContentModuleExtensions.cs:41` | **Xóa** (Đ-4.3) |
| `PostReadService` hỏi `AreFriendsAsync` có điều kiện | `Content/Application/Posts/PostReadService.cs` | Không đổi — BR-02 tự đúng khi DI đổi |
| `PostResponseMapper` batch (ảnh, tác giả) | `Content/Application/Posts/` | Hàm hydrate của feed (Đ-4.9) |
| `idx_posts_author_created (author_id, created_at DESC, post_id DESC) WHERE status='published'` | migration `InitialContent` | Index của truy vấn LATERAL |
| `PostCursor` keyset | `Content/Application/Posts/PostCursor.cs` | Cursor của feed — dùng nguyên |
| `IUserDirectory` batch | SharedKernel + Profile | Tồn tại người dùng (404), `FriendCard`, tác giả trong feed |
| Kết nối Redis dùng chung + health check | `SharedKernel/Redis/` | Hai cache (Đ-4.8) |
| Khuôn module 4 tầng, `<Module>DbContextOptions`, hook `--migrate`, nhóm Swagger | Identity, Profile, Content | Khuôn cho SocialGraph |
| Khung AuthZ matrix (có `CallerUserId` từ `Q-B2`) | `tests/…/AuthZ/` | Năm dòng mới + `READ-06b`, không sửa khung |
| `ContractTestsBase`, `ContentPermissionsTests` | `tests/` | Chép cho SocialGraph |
| `tests/load/` rỗng | `tests/load/` | Chỗ đặt seed + kịch bản k6 |
| `PostCard` slot `actions`, `use-post-page.ts` (cuộn theo cursor) | `features/post/` | Tiền lệ slot; khuôn logic cuộn cho feed |

**Ba chỗ có sẵn nhưng phải sửa:**

| Chỗ | Sửa gì | Vì sao |
|---|---|---|
| `Program.cs` | `AddApplicationPart` · mục `apiGroups` · `AddSocialGraphModule` · dòng migrate | Host liệt kê tường minh từng module |
| `PostStore.ListByAuthorAsync` | Thêm `Status == Published` (Đ-4.11) | Index một phần + BR-07 |
| Test pin `AlwaysStrangers` ở host (A6 của GĐ2) | Đổi khẳng định (Đ-4.3) | Hành vi đã chốt của GĐ2 hết hiệu lực — có chủ đích |

## B.2 Bản đồ công việc

| Khối | Nội dung | Số việc | Cần trước | Chặn | Bước (Mục 9.3) |
|---|---|---|---|---|---|
| **A. Nền dữ liệu** | Module SocialGraph, schema, migration, đổi DI | 5 | cổng mở | C, D | 2 |
| **B. Test + cổng CI** | Harness, matrix, test quan hệ và feed, cổng hợp đồng mới | 5 | A (một phần) | F | 2, 4, 5 |
| **C. Feed + hiệu năng** | Nguồn feed, truy vấn LATERAL, hydrate, cache, degrade, k6 | 6 | A | D7, F | 3, 4, 5, 6 |
| **D. Endpoint** | 6 nhóm endpoint quan hệ + `GET /feed` | 7 | A, C | E (ráp thật), F | 4, 5 |
| **E. Lane frontend** | Nút quan hệ, màn lời mời / bạn bè, feed trang chủ | 6 | chỉ cần hợp đồng | F | 7 |
| **F. Cổng đóng** | Staging, E2E, báo cáo k6, đóng băng | 4 | D, E, C6 | GĐ3, GĐ5 | 8 |

**Khối E không phụ thuộc backend** — nó chỉ cần hợp đồng đã commit ở bước 1 và dựng trên `msw/node`. Nếu backend kẹt (chờ
máy đo, chờ `EXPLAIN`), chuyển sang một việc của khối E thay vì ngồi chờ.

---

## B.3 Khối A — Nền dữ liệu

> **Mục tiêu khối:** module thứ tư đứng độc lập với schema riêng, và BR-02 chạy thật mà Content không đổi một dòng logic.

### A1 — Entity trong `Domain/`

`Friendship` (cặp chuẩn hóa, `RequesterId`, `Status`, `AcceptedAt`), `Follow`, `FriendshipStatus` (enum chữ thường khớp
CHECK), và `FriendPair.Of(a, b)` — **chỗ duy nhất** chuẩn hóa cặp, theo đúng thứ tự `uuid` của Postgres bằng `Guid.CompareTo`,
**không** so `ToByteArray()` (Mục 4 cạm bẫy 1).

Lệch B.3 (nhóm chốt): B.3 viết `Relationship.From(…)` → `RelationshipResponse`. Domain trả **trạng thái miền**
`RelationshipState` / `FriendshipView` (`None · Outgoing · Incoming · Friends`); ánh xạ sang DTO `RelationshipResponse`
là việc của `D0`/`D1` — Domain không tham chiếu Application.

### A2 — `SocialGraphDbContext` + configuration + options + design-time factory

Chép hình dạng `ContentDbContext`: `HasDefaultSchema("socialgraph")`, bảng lịch sử trong schema của mình, override
`SaveChanges` đóng dấu `updated_at`. Bốn CHECK và index `idx_friendships_user_max` theo Mục 4.

Lệch B.3 (nhóm chốt): B.3 viết "chép hình dạng `ContentDbContext`" — ngầm hiểu dùng lại `LowercaseEnum`. Bản ở Content
là `internal` và `ModuleBoundaryTests` chặn import chéo; đưa lên SharedKernel sẽ kéo EF Core vào SharedKernel. **Chép**
`LowercaseEnum` sang `SocialGraph/Infrastructure/Configurations/` (bản `internal` của riêng module).

### A3 — Migration đầu tiên + `AddSocialGraphModule` + `MigrateSocialGraphModuleAsync` + nối `Program.cs`

Nghiệm thu: `--migrate` hai lần trên DB sạch, lần hai không đổi gì; `\dn` thấy bốn schema.

Lệch B.3 (nhóm chốt): harness migrate `socialgraph` vào **A3** (hai chỗ: `ModulesApiFactory.CreateMigratedDatabaseAsync`
và `PostgresFixture.SeededContentDatabaseAsync`), không chờ `B1`. Từ `A5` Content đọc `socialgraph.friendships` —
thiếu dòng migrate ở harness thì `READ_02_05` nhận 500 `relation does not exist`.

### A4 — Migration nhỏ của Content: `idx_posts_public_recent`

Một index (Mục 4). Migration thứ hai của `ContentDbContext`; GĐ3 thêm migration thứ ba sau nó (Mục 4 cạm bẫy 3).

### A5 — `FriendshipReader` + `FeedSourceReader` (chưa cache) + **đổi DI**

Hiện thực hai contract ở `SocialGraph.Infrastructure`; xóa dòng `AlwaysStrangers` ở Content (Đ-4.3); test khởi động; đổi
test pin của GĐ2. Cache của `FeedSourceReader` là việc của `C1`.

Lệch B.3 (nhóm chốt): `READ-06b` **không** xanh ở khối A. Matrix dựng cảnh qua API thật — "A và B là bạn" cần
`POST /friends/requests` + accept (`D2`+`D3`). Khối A chứng minh BR-02 thật bằng `FriendshipReaderTests` (qua DI của
module, Postgres thật) + test khởi động. `READ-06`/`READ-06b` vào matrix ở `B2` (đỏ có chủ đích), xanh khi `D3` xong.

**Kết quả khối A:** `READ-01..05` xanh không sửa khẳng định; ArchUnitNET xanh với type thật trong SocialGraph;
BR-02 thật chứng minh bằng `FriendshipReaderTests` + test khởi động. `READ-06b` vào `B2`/`D3` (L2).

---

## B.4 Khối B — Test và cổng CI

> **Mục tiêu khối:** biến BR-03, BR-02 thật và Đ-4.9 thành thứ **chặn merge**.

### B1 — Harness

`PostgresFixture` migrate thêm schema `socialgraph` (đã xong ở A3 — lệch L1). B1 còn: `ModulesApiFactory.UseRedis`,
`FakeObjectStorage.DistinctGetUrls` (tắt mặc định, L2), `SqlCommandCounter` qua ActivitySource `"Npgsql"` (L4), đo giờ.
Giữ luật chọn hàm của GĐ2 (`CreateDatabaseAsync` cho test sửa dữ liệu). Đo thời gian bộ integration trước và sau; vượt
~3 phút thì tách collection. **Thi công 2026-09-22:** trước 1 m 24 s → sau 1 m 21 s — chưa tách.

### B2 — Năm dòng AuthZ matrix + `READ-06b`, viết cho đỏ trước

Thêm dòng trước khi có endpoint → đỏ có chủ đích → `D*` làm xanh (nếp `B2`/`B3` của GĐ1, GĐ2).

Lệch lúc thi công B2 (2026-09-22): `TC-A01-feed` và `TC-A01-friends` **xanh ngay** với 401 — FallbackPolicy /
anti-enumeration trả 401 cho route chưa khớp khi ẩn danh (AGENTS.md Mục 9), không 404 như bảng hướng dẫn B+C+D giả định.
Ba dòng còn lại (`TC-A03-friend-accept`, `TC-A03-friend-self-accept`, `READ-06b`) đỏ đúng `ArrangePath … 404`; `READ-06`
xanh (L8).

### B3 — Test quan hệ `FRD-*`, `FOL-*` + bảng đột biến

| Đột biến | Test phải đỏ |
|---|---|
| Bỏ vế `requester_id = @other` trong câu chấp nhận | `TC-A03-friend-self-accept` |
| Khôi phục `AlwaysStrangers` ở Content | Test khởi động · `READ-06b` · `FEED-04` |
| Không bắt `23505` khi gửi lời mời | `FRD-06` (500 thay vì 409) |
| Xóa cache nguồn **trước** `COMMIT` | `FEED-09` (có thể không tái hiện ổn định — ghi rõ nếu vậy) |
| Bỏ kiểm lại BR-02 ở hydrate | `FEED-09` |
| Cache lưu `PostResponse` thay vì `post_id` | `FEED-10` · `FEED-13` |

### B4 — Test feed `FEED-*` + test đếm truy vấn `FEED-Q1`

Mục 10.2. `FEED-10` dùng `TimeProvider` giả (đã đăng ký `TryAddSingleton(TimeProvider.System)` ở Content) để "đi tới"
16 phút sau mà không ngủ.

### B5 — Cổng hợp đồng `socialgraph-v1` + thử cho đỏ

Mục 10.5 điểm 1, 2 + thử đỏ. Lệch B.4/B.5/B.6 (nhóm chốt): điểm 3 là `B2`; điểm 5 đã xong ở `A1`; điểm 4
(`SocialGraphPermissionsTests`) viết trong `D0`, `B5` chỉ thử đỏ.

---

## B.5 Khối C — Feed và hiệu năng

> **Mục tiêu khối:** feed đúng quyền, có chi phí bị chặn trên, chịu được Redis chết — và có một con số k6 để chứng minh.

### C1 — Cache nguồn feed trong `FeedSourceReader` (Đ-4.8)

Khóa `sg:feed-sources:{userId}`, TTL 60s, fail-open. Xóa khóa của **cả hai** người **sau** `COMMIT` ở mọi thao tác đổi quan
hệ (Đ-4.15) — một hàm `InvalidateAsync(a, b)` gọi từ một chỗ trong service, không rải khắp controller.

Lệch B.4/B.5/B.6 (nhóm chốt, L12): `FeedSourceReaderTests` thêm `AddSharedKernelRedis(ApiFactory.UnreachableRedis)` —
`FeedSourceReader` giờ cần `RedisConnection`; cổng 1 là đường fail-open. Q-C2 (bind công tắc có điều kiện) đã ghi ở Đ-4.8.

### C2 — Truy vấn LATERAL + feed gợi ý (Đ-4.6, Đ-4.7)

`IFeedStore` ở `Content.Application`, hiện thực `FromSql` ở `Content.Infrastructure`. `EXPLAIN (ANALYZE, BUFFERS)` trên bộ
dữ liệu tải → dán vào hướng dẫn khối C. Cùng commit: Đ-4.11 (`Status == Published` ở `ListByAuthorAsync`) kèm `EXPLAIN`
trước/sau — **chạy impact analysis trên `ListByAuthorAsync` trước khi sửa** (người gọi: `ListByUserAsync`).

### C3 — Hydrate dùng chung + kiểm lại BR-02 (Đ-4.9)

Tách phần "danh sách `Post` → `PostResponse`" hiện đang nằm trong `PostReadService` thành **một** hàm batch mà cả
`GET /users/{id}/posts` và feed gọi. Đây là chỗ GĐ3 sẽ cắm `myReaction` vào (Mục 9.1 #1) — **chạy impact analysis trên
`PostReadService` / `PostResponseMapper` trước khi tách**, và ghi chữ ký hàm vào hướng dẫn khối C để GĐ3 tìm thấy.

### C4 — Cache trang đầu + degrade + 503 (Đ-4.8, Đ-4.10)

Khóa `feed:p1:{userId}` chỉ chứa `ids` + `mode` + `next` + dấu nguồn `fp` (Đ-4.8 sửa 2026-09-22); tác giả đăng / sửa /
xóa bài thì Content xóa khóa của tác giả.
`CommandTimeout` 5s riêng cho truy vấn feed; timeout → `Result` 503 + header `Retry-After`. Hai công tắc cấu hình.

### C5 — Môi trường đo + bộ dữ liệu tải (Đ-4.13)

`tests/load/feed/`: `seed.sql` (hai chốt chặn), `docker-compose.perf.yml` (`name: socialapp-perf`, API `cpus: 2` /
`mem_limit: 12g`, cổng 15432/16379/18080), `.env.example` (`PERF_JWT_KEY`, `POSTGRES_PASSWORD`), `README.md`.
**Thi công 2026-09-22:** seed ~28 s → 10k hồ sơ, 1M bài (70/20/10, ~1% hidden), avg 120 bạn / 20 follows; hai chốt
đã thử đỏ. Khóa JWT đo không commit.

### C6 — Kịch bản k6 + ba lượt + báo cáo sơ bộ

`tests/load/feed/feed.js` (ký JWT trong `setup()`, 1.000 VU, ngưỡng p95/lỗi). Chạy ở bước 6 (Mục 9.3), còn nửa ngày để sửa. Báo cáo ở
`docs/giai-doan-4/bao-cao-k6-so-bo.md`: máy, bản k6, cấu hình giới hạn, bộ dữ liệu, ba lượt, p50/p95/p99, lỗi, số kết nối
DB đỉnh, `EXPLAIN` của truy vấn chậm nhất, việc chuyển sang GĐ8 (nếu có).

---

## B.6 Khối D — Endpoint

> **Mục tiêu khối:** hai hợp đồng thành hệ thống chạy thật, khớp từng mã lỗi.

### D0 — Nền chung của SocialGraph

`SocialGraphApiGroup`; controller khai `[ApiExplorerSettings(GroupName = …)]` từ file đầu tiên; `SocialGraphPermissions`;
`SocialGraphErrors` ở một chỗ (nếp `ContentErrors`); validator đăng ký trong `AddSocialGraphModule`.
`SocialGraphPermissionsTests` đi **cùng commit này** (nếp `Q-D5` GĐ2).

Lệch B.4/B.5/B.6 (nhóm chốt): Mục 10.5 điểm 4 viết trong `D0`, không để tới `B5` — để `D2`–`D6` gõ mã quyền mà không
ai canh thì Admin vẫn qua. `B5` chỉ thử đỏ lại.

### D1 — `GET /relationships/{userId}`

Endpoint đọc đầu tiên, và là thứ FE cần sớm nhất (nút trên hồ sơ). Chính mình → 400.

### D2 — `POST /friends/requests`

Thứ tự: tầng 2 → khác mình (400, **trước** DB) → có hồ sơ (404) → INSERT → `23505` → 409. Cache + event sau `COMMIT`.

### D3 — `POST /friends/requests/{userId}/accept`

Câu `UPDATE` có điều kiện (Đ-4.14); 0 dòng → 403.

### D4 — `DELETE /friends/requests/{userId}` + `DELETE /friends/{userId}`

Idempotent, 204. Xóa cache sau `COMMIT` chỉ khi có dòng bị xóa.

### D5 — `GET /friends` + `GET /friends/requests?direction=`

Keyset theo `accepted_at` / `created_at` + id người kia; một lô `IUserDirectory` cho cả trang. `direction` thiếu hoặc lạ → 400.

Lệch B.4/B.5/B.6 (nhóm chốt, L13): cursor dùng cùng **cách mã hóa** keyset của GĐ2 (Mục 8.1), nhưng không dùng lại **kiểu**
`PostCursor` — `FriendCursor` là bản chép trong `SocialGraph/Application/Relationships/`. `PostCursor` thuộc Content; import
nó là import chéo module (`ModuleBoundaryTests` chặn). Cùng lập luận L5 khối A (`LowercaseEnum`).

### D6 — `PUT` + `DELETE /follows/{userId}`

`ON CONFLICT DO NOTHING`; tự theo dõi → 400 trước DB; không có hồ sơ → 404.

### D7 — `GET /feed` + rà RFC 7807 cho cả hai nhóm

Controller mỏng gọi `FeedService` (C1–C4). Rà như `D9` của GĐ2: từng mã lỗi trong hai hợp đồng đối chiếu với code thật;
503 có `Retry-After`; không thông điệp nào chứa id hay tên kiểu.

**Cùng commit với controller** (lệch Mục 9.2, chốt 2026-09-22): thêm `GET /feed` + `FeedPage` vào `content-v1.yaml` đúng
Mục 8.2, `info.version` → `1.0.0-gd4`, chạy `pnpm gen:api`, commit `lib/api/content/schema.d.ts`. `ContentContractTests`
đỏ nếu thiếu một trong hai vế — đó là cổng canh việc này.

---

## B.7 Khối E — Lane frontend

> **Mục tiêu khối:** trang chủ là feed thật; người mới có đường để gặp người khác; không màn trắng khi server quá tải.

Luật đặt file theo `frontend-rules.md` Mục 2: `features/feed/`, `features/friend/`. Không `features/` nào import chéo;
ghép ở `app/` (Đ-4.16).

### E1 — Codegen, client, ngữ cảnh lỗi

`pnpm gen:api` sinh `lib/api/socialgraph/` → alias ở `lib/api/types.ts` → `lib/api/socialgraph-api.ts` → `errorMessage` thêm
ngữ cảnh `friend-request`, `friend-respond`, `follow`, `feed`. Bốn ngữ cảnh vì 403/404/409 mang nghĩa khác nhau trên từng
endpoint (nếp `Q-E4` của GĐ2). Fixture `msw/node` chép `example` của hợp đồng, gắn kiểu bằng `satisfies`.

Lệch B.7 (nhóm chốt 2026-09-23, Q-E3, L3): **năm** ngữ cảnh — thêm `relationship` cho `GET /relationships/{id}`,
`GET /friends`, `GET /friends/requests` và ba `DELETE` (lời mời, bạn bè, theo dõi). Các lời gọi đó không có mã riêng; mượn
`friend-request` cho một lời gọi `GET` là mời người sau thêm câu 409 "đã có lời mời" vào một màn đọc. `FieldErrorKey` thêm
`userId`, `direction` (hai key `errors` mới của `socialgraph-v1`).

Lệch B.7 (nhóm chốt 2026-09-23, Q-E8): mở tầng `hooks/` — hook React dùng lại, không biết nghiệp vụ, tầng ngang
`components/` (`frontend-rules.md` Mục 2, ESLint cấm import `@/features/*`, `@/app/*`, đã thử đỏ). `E4` và `E3` dùng **một**
`hooks/use-cursor-pages.ts` thay vì chép logic cuộn của `use-post-page.ts` thành hai bản; `use-post-page.ts` giữ nguyên ở GĐ4
(nợ có địa chỉ: chuyển khi GĐ5 thêm người dùng thứ ba của khuôn).

### E2 — Nút quan hệ trên hồ sơ người khác

`features/friend/relationship-buttons.tsx`: bốn trạng thái kết bạn (Kết bạn · Đã gửi – Hủy · Chấp nhận / Từ chối · Bạn bè –
Hủy kết bạn) + nút Theo dõi / Bỏ theo dõi. Không optimistic (Đ-4.16). Hủy kết bạn có `AlertDialog` xác nhận.

### E3 — Màn `/friends`: lời mời đến, lời mời đi, danh sách bạn

Ba danh sách theo cursor; chấp nhận / từ chối / hủy tại chỗ.

### E4 — Feed trang chủ

`features/feed/feed-list.tsx`: cuộn vô hạn theo `nextCursor` (**không** suy "hết" từ độ dài trang — Đ-4.9), skeleton, trạng
thái rỗng của `mode=network` ("Chưa có bài nào — kết bạn hoặc theo dõi để thấy bài"), nhãn gợi ý khi `mode=suggested`, 503
→ thẻ Thử lại. Chép logic cuộn của `use-post-page.ts` **thành bản của feed** trong `features/feed/` — không import từ
`features/post/`.

Trang chủ nằm ở `/` trong nhóm `(with-profile)`; `app/page.tsx` hiện có phải nhường chỗ — xử lý trùng route là điểm đầu
tiên hướng dẫn khối E phải chốt.

### E5 — Slot và ráp ở `app/`

Trang chủ ráp `FeedList` + `PostCard` (qua `renderPost`) — chỗ GĐ3 sẽ cắm thanh cảm xúc vào. `PublicProfile` nhận slot
`actions`; `app/users/[userId]/page.tsx` truyền `RelationshipButtons`. Liên kết "Bạn bè" trong `AppHeader`.

### E6 — Vitest + Playwright

Theo Mục 10.6. Playwright local, `workers: 1`, kết quả dán vào PR kèm bản Chrome (Đ-E8).

---

## B.8 Khối F — Cổng đóng

### F1 — Deploy staging qua CD

**Không có key `.env` mới** — hai công tắc cache mặc định bật, khóa ký JWT của môi trường đo không bao giờ lên server.
Sau deploy: service `migrate` xanh cho **bốn** module; `/health/ready` = 200.

### F2 — E2E lát cắt trên staging, hai tài khoản

Theo Mục 12 "Lát cắt dọc". Bằng chứng: ảnh feed gợi ý của tài khoản mới; ảnh bài `friends` hiện rồi biến mất sau khi hủy
kết bạn; tab Network chỉ có `/bff/*` và `GET` ảnh R2.

### F3 — Checklist Mục 12 + Definition of Done Mục 11

Tick từng dòng có bằng chứng. Dòng chờ thao tác trên server thì ghi "chờ server", **không xóa dòng** (nếp `F4` của GĐ2).

### F4 — Đóng băng + bàn giao

Đóng băng `socialgraph-v1` và phần `/feed` của `content-v1`; liệt kê phần hoãn có địa chỉ (Mục 2); bàn giao cho GĐ5 ba thứ
nó cần: `IFriendshipReader` thật (BR-09 chat chỉ giữa bạn bè), khuôn event sau `COMMIT`, và báo cáo k6 làm mốc so sánh.

---

## B.9 Thứ tự thực thi, đường găng, và thứ tự cắt

**Đường găng:** `A1 → A2 → A3 → A5` · `C2 → C3 → C4 → C6` · `C5` (làm sớm ở bước 3, phải xong trước `C2` được `EXPLAIN`) · `F1 → F2 → F3`

`C2` phải chạy được trên bộ dữ liệu tải **ngay khi viết** (bước 5, nhờ seed đã có từ bước 3) — `EXPLAIN` sai hình dạng lộ
ra ở đây, không phải ở k6.

**Thứ tự cắt khi trễ** — từ trên xuống, dừng khi kịp:

| Thứ tự | Cắt gì | Còn lại vẫn đạt |
|---|---|---|
| 1 | Màn "lời mời đã gửi" trên UI (giữ API `direction=outgoing`) | FR-010/011 đủ qua nút trên hồ sơ |
| 2 | Cache trang đầu (C4 phần 1) — **chỉ khi** k6 lượt lạnh đã đạt | SEQ-03 còn cache nguồn; p95 đã chứng minh không cần nó |
| 3 | Nút Theo dõi trên UI (giữ API) | FR-012 đủ ở mức API test |
| **Không cắt** | Feed + BR-02 trong truy vấn · đổi DI + `READ-06b` · kết bạn / chấp nhận · **báo cáo k6** (kể cả khi không đạt) | Đây là "Lõi" và "Bắt buộc phi chức năng" của bảng ưu tiên |

**Năm thứ không test tự động nào bắt được — tự rà trước khi mở PR** (không có người review chéo, Mục 9.3):

1. `actorId` từ `User.GetUserId()`, không từ route/body — cặp quan hệ luôn là `(actorId, userId của route)` (Mục 6.2).
2. Xóa cache và phát event **sau** `COMMIT` (Đ-4.15).
3. Truy vấn feed tham số hóa — không nối chuỗi mảng id vào SQL.
4. Không `StringSet` nào của feed chứa `PostResponse` đã dựng hay URL đã ký (Đ-4.9).
5. Khóa ký JWT của môi trường đo không nằm trong repo, không nằm trong `deploy/.env`.

---

## B.10 Mục tiêu từng khối — chúng cộng lại thành cái gì

| Khối | Mục tiêu | Thiếu nó thì mất gì |
|---|---|---|
| **A** | Module thứ tư độc lập; BR-02 chạy thật mà Content không đổi logic | Bài `friends` vẫn chỉ tác giả thấy — mức riêng tư thứ hai của sản phẩm không tồn tại |
| **B** | BR-03, BR-02 thật và luật cache thành cổng chặn merge | Lỗ lộ bài `friends` sau khi hủy kết bạn chỉ thấy khi người dùng kêu |
| **C** | Feed đúng, chi phí bị chặn trên, có con số k6 | GOAL-01 thành lời hứa; GĐ8 phát hiện trượt khi không còn ngày để sửa |
| **D** | Hợp đồng thành hệ thống chạy thật | Không có sản phẩm |
| **E** | Trang chủ là feed thật; người mới có đường gặp người khác | Người dùng mới mở app thấy một trang trống và không có nút nào để đi tiếp |
| **F** | "Xong" thành sự kiện kiểm chứng được, kể cả con số hiệu năng | "Xong" thành cảm giác |

## B.11 Mục tiêu của GĐ4

### Ba điều kiện để tuyên bố GĐ4 xong

1. **Trên staging, hai tài khoản thật đi hết vòng:** feed gợi ý → kết bạn → chấp nhận → bài `friends` hiện trên trang chủ →
   hủy kết bạn → bài đó biến mất ngay.
2. **Có báo cáo k6 sơ bộ ba lượt** trên bộ dữ liệu 1M bài — đạt p95 ≤ 500ms ở lượt lạnh, **hoặc** không đạt kèm `EXPLAIN` và
   việc cụ thể chuyển GĐ8. Không có báo cáo thì chưa xong, bất kể con số.
3. **CI xanh cả năm nhóm**, với cổng hợp đồng `socialgraph-v1` mới đã từng thấy đỏ một lần, và năm dòng matrix + `READ-06b`
   đã từng thấy đỏ theo bảng đột biến `B3`.

### GĐ4 để lại gì cho GĐ5–GĐ8

| Di sản | Ai thừa hưởng |
|---|---|
| `IFriendshipReader` thật | GĐ5 — BR-09 (chỉ nhắn tin giữa bạn bè), TC-A07 |
| `IFeedSourceReader` + cache do chủ dữ liệu xóa | GĐ6 — thông báo có thể dùng "ai theo dõi ai" |
| Khuôn "cache chỉ lưu id, hydrate lúc trả" | GĐ5 (danh sách hội thoại), GĐ6 (danh sách thông báo) |
| Event `FriendRequestSent`, `FriendRequestAccepted` sau `COMMIT` | GĐ6 — notification loại `friend` |
| Môi trường đo + seed + kịch bản k6 + báo cáo sơ bộ | GĐ8 — chạy lại **chính thức**, so với mốc GĐ4 |
| `idx_posts_public_recent` + feed gợi ý | GĐ6 — tìm kiếm mở thêm đường khám phá, feed gợi ý vẫn giữ cho tài khoản mới |
| Nợ có địa chỉ: dọn `friendships`/`follows` khi xóa tài khoản | GĐ8 (NĐ 13/2023) |

**Một câu để nhớ:** GĐ2 cho mỗi người một thứ thuộc về họ, GĐ3 cho nhiều người cùng chạm vào một thứ; GĐ4 là lần đầu hệ
thống phải trả lời **"ai nhìn thấy gì"** cho hàng nghìn người cùng lúc — và trả lời đó phải đúng *và* nhanh, không được đổi
cái này lấy cái kia.
