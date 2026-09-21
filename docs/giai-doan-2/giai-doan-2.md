# GĐ 2 — Profile + Content: hồ sơ, đăng bài, ảnh (UC-03, UC-04, UC-05) · Ngày 6–9

> Nguồn: [`ke-hoach-trien-khai.md`](../ke-hoach-trien-khai.md) mục "GĐ 2 — Profile + Content", nhịp hai lane ở Mục 0C,
> và báo cáo PTTK v5.0 (Mục 5.5 schema, 5.6 index, 6.7.2 ma trận quyền, SEQ-01).
> Nền móng: [`giai-doan-1.md`](../giai-doan-1/giai-doan-1.md) — GĐ2 **tiêu thụ** hạ tầng của GĐ1, không dựng lại.
>
> **Ba mốc không lùi được của giai đoạn này:**
> 1. **Upload ảnh thật từ trình duyệt lên R2** — chỗ DUY NHẤT rủi ro ISS-02 (CORS) lộ ra. `curl` luôn xanh.
> 2. **TC-A03** (`PATCH /posts/{id của người khác}` → 403) — module đầu tiên tiêu thụ khuôn tầng 3 của GĐ1.
> 3. **Hình dạng cursor** — GĐ2 đã cần danh sách bài, nên cursor chốt ở đây, không chờ GĐ4 (xem Đ-2.11).

## Tài liệu này có hai phần

| Phần | Trả lời câu hỏi | Đọc khi |
|---|---|---|
| **A — Thiết kế và quyết định** | *Cái gì* và *vì sao* | Trước khi gõ dòng đầu tiên; lúc review PR |
| **B — Kế hoạch triển khai** | *Ai làm gì, theo thứ tự nào* | Lúc chia việc; lúc kiểm tiến độ |

Hướng dẫn thi công từng bước (file nào, lệnh nào, cạm bẫy nào) nằm ở các file `huong-dan-khoi-*.md` cùng thư mục,
**viết khi khối đó bắt đầu** — đúng nếp GĐ1, không viết trước cả sáu khối rồi để lệch với thực tế thi công.

---

# Phần A — Thiết kế và quyết định

## 0. Thuật ngữ

| Từ | Nghĩa trong tài liệu này |
|---|---|
| **presign** | URL có chữ ký hạn ngắn cho phép trình duyệt `PUT` thẳng một object lên R2 mà không cầm khóa R2 |
| **`storage_key`** | Đường dẫn của object trong bucket (`posts/{userId}/{uuid7}.jpg`). Đây là thứ DB lưu, không lưu URL |
| **commit** | Bước `POST /posts` gắn các `storage_key` đã upload vào một bài. Trước commit, object là **mồ côi** |
| **mồ côi (orphan)** | Object đã nằm trên R2 nhưng không có dòng `media_attachments` nào trỏ tới — worker dọn |
| **cursor keyset** | Phân trang theo giá trị cột `(created_at, post_id)` của bản ghi cuối, không dùng `OFFSET` |
| **directory contract** | Interface chỉ-đọc ở SharedKernel để module A xem dữ liệu chiếu của module B (xem Đ-2.3) |
| **tầng 1/2/3** | AuthN · RBAC `[RequirePermission]` · ownership ở service — `giai-doan-1.md` Mục 6 |

## 1. Mục tiêu giai đoạn

### Phát biểu một câu

> **Người dùng thật trên staging đặt được tên hiển thị và avatar, đăng một bài kèm ảnh mà ảnh đi thẳng từ trình duyệt
> lên R2, xem lại — sửa — xóa bài của mình; và không một đường nào cho họ chạm vào bài hay ảnh của người khác.**

### Mục tiêu chính thức và khối nào gánh

| Mã | Mục tiêu | Đạt bằng | Kiểm bằng |
|---|---|---|---|
| **FR-013** | Hồ sơ + avatar | A1, D1–D3 | PROF-01..04, E2E-03 |
| **FR-004** | Đăng bài văn bản + ≤ 10 ảnh, có mức riêng tư | A2, C1–C4, D4–D5 | AC US-004 (AC-01..04) |
| **FR-005** | Sửa / xóa mềm bài | D7, D8 | POST-06..09 |
| **BR-01** | ≤ 5000 ký tự **hoặc** ≥ 1 ảnh · ≤ 10 ảnh · ≤ 10MB/ảnh | A2 (CHECK) + D4 (validator) + C3 (HEAD) | BR01-01..06 (unit + integration) |
| **BR-02** | Quyền xem đánh giá **tại thời điểm đọc** — phần làm được ở GĐ2 | D6 | READ-01..05 |
| **ISS-02** | CORS + presigned PUT chạy thật trên trình duyệt | C2, E4, F3 | F3 — ảnh Network + object trên R2 |
| **GOAL-03** | Không IDOR ở module nội dung | D0 (khuôn tầng 3 của GĐ1) | TC-A03, TC-A03-delete, TC-A03-media |

### Vì sao GĐ2 nặng hơn vẻ ngoài

Nhìn danh sách endpoint thì GĐ2 chỉ có "CRUD bài viết". Ba thứ làm nó khác:

1. **Đây là module đầu tiên KHÔNG phải Identity.** Mọi ranh giới ADR-001 mà GĐ1 chỉ mô tả trên giấy (schema riêng,
   không tham chiếu chéo, contract ở đâu) đến GĐ2 mới phải trả lời bằng code. Trả lời sai ở đây thì năm module sau
   copy đúng cái sai đó.
2. **Đây là module đầu tiên có tài nguyên thuộc sở hữu.** Khuôn tầng 3 của GĐ1 (`C6`) tới giờ chỉ có một probe trong
   assembly test; GĐ2 là lần đầu nó chạy trên dữ liệu thật.
3. **Đây là chỗ hạ tầng bên thứ ba (R2) vào hệ thống.** Ba thứ chỉ hỏng ở môi trường thật: CORS, chữ ký, và
   dung lượng — cả ba đều không bắt được bằng integration test.

---

## 2. Phạm vi

### Trong phạm vi

| Nhóm | Nội dung |
|---|---|
| **Module Profile** | Entity `profiles`; đặt tên hiển thị + bio; avatar (gắn / gỡ); đọc hồ sơ theo `userId` |
| **Module Content** | Entity `posts`, `media_attachments`; **khung bảng** `comments`, `reactions` (không endpoint) |
| **Lưu trữ đối tượng** | `IObjectStorage` ở SharedKernel; presign PUT (10 phút) + presign GET (TTL ngắn); HEAD lúc commit |
| **Endpoint** | 4 của Profile + 6 của Content (Mục 8) |
| **Dọn rác** | Worker dọn object mồ côi + object của bài đã xóa mềm quá hạn |
| **Contract chéo module** | `IUserDirectory` (Profile cấp) và `IFriendshipReader` (null-object, GĐ4 thay) ở SharedKernel |
| **Lane frontend** | Onboarding hồ sơ · avatar · composer đăng bài + upload thật có tiến trình · danh sách + chi tiết bài · sửa/xóa |
| **Test** | BR-01 unit · 3 dòng AuthZ matrix mới · hai cổng hợp đồng mới · E2E lát cắt trên staging |

### Ngoài phạm vi — hoãn có địa chỉ

| Việc | Hoãn tới | Lý do |
|---|---|---|
| Bình luận, cảm xúc (endpoint + nghiệp vụ) | **GĐ3** | GĐ2 chỉ dựng bảng và cột bộ đếm để hình dạng DTO không đổi hai lần (Đ-2.12) |
| News feed, `GET /feed` | **GĐ4** | Cần `friendships`; GĐ2 đã chốt cursor nên GĐ4 dùng lại (Đ-2.11) |
| Đánh giá mức riêng tư `friends` | **GĐ4** | Chưa có bảng `friendships`. `IFriendshipReader` trả `false` — bài `friends` chỉ tác giả thấy (Đ-2.9) |
| Ẩn/gỡ bài của người khác (BR-07), `hidden_reason` | **GĐ6** | Cột có sẵn từ GĐ2, không ai ghi; quyền `post.hide` đã seed từ GĐ1 |
| Media của tin nhắn (`owner_type='message'`) | **GĐ5** | CK đã cho phép giá trị đó để GĐ5 không phải migration đổi CK (Đ-2.12) |
| Index tìm kiếm `unaccent(display_name)` GIN | **GĐ6** | Chỉ UC-16 cần; tạo sớm là trả giá ghi (~15% dung lượng) suốt 4 giai đoạn không ai đọc |
| Xóa cứng bài + xóa object theo NĐ 13/2023 | **GĐ8** | Đi cùng quyền tự xóa tài khoản và chính sách ẩn danh PII |
| Dọn `profiles`/`posts` khi user bị xóa | **GĐ8** | Hệ quả trực tiếp của Đ-2.2 (không FK chéo schema) — ghi vào nợ có địa chỉ, không để vô chủ |
| Cắt/nén ảnh phía client, thumbnail, EXIF strip | **GĐ7** | UI/UX; GĐ2 chỉ chặn loại và dung lượng |
| Sửa danh sách ảnh của bài đã đăng | **GĐ7** | Kéo theo một luồng rác nữa (object của ảnh bị gỡ); `PATCH /posts/{id}` gửi `mediaKeys` → 400 (Mục 7.3). *Thêm vào bảng 2026-09-21 ở `F5` — trước đó chỉ nằm trong thân Mục 7.3* |
| Dọn avatar mồ côi (object cũ khi đổi/gỡ avatar) | **GĐ8** | Worker của Content chỉ quét `posts/`, không được đọc `profile.profiles.avatar_key` (Đ-2.2, Đ-2.3); làm khi Profile có worker riêng hoặc khi xóa tài khoản chạm tới. *Thêm vào bảng 2026-09-21 ở `F5` — trước đó chỉ nằm trong Mục 7.5* |
| Video, GIF động, SVG | **Ngoài MVP** | SVG bị loại có chủ đích: script trong SVG chạy được nếu phục vụ từ domain R2 |
| Sửa hồ sơ của người khác (admin) | **Ngoài MVP** | Không có FR nào yêu cầu; nhờ vậy GĐ2 không phải thêm mã quyền mới (Đ-2.6) |

---

## 3. Quyết định thiết kế đã chốt

Mười lăm quyết định. Bốn cái đầu (**Đ-2.1 → Đ-2.4**) là *quyết định kiến trúc dùng cho cả năm module còn lại* —
chúng đắt hơn hẳn phần còn lại và phải được cả nhóm chốt ở **cổng mở**, không quyết trong lúc code.

### Đ-2.1 Hai module = hai schema = hai DbContext, mỗi context một bảng lịch sử migration

`profile.profiles` trong schema `profile`; `posts`/`comments`/`reactions`/`media_attachments` trong schema `content`.
Mỗi module một `DbContext` + một `IDesignTimeDbContextFactory` + `__EFMigrationsHistory` **trong schema của chính nó** —
đúng khuôn `IdentityDbContextOptions` đã có, và chính là lý do GĐ1 đặt bảng lịch sử vào schema `identity` ngay từ đầu.

Không có `AppDbContext` dùng chung. Một context chung là cửa hậu để `Content` `JOIN` thẳng vào bảng của `Identity`:
ArchUnitNET không bắt được vì về kỹ thuật vẫn hợp lệ, còn tách schema thì ranh giới hiện ra ngay trong câu SQL.

**Cạm bẫy:** cấu hình Npgsql phải nằm ở MỘT chỗ (`<Module>DbContextOptions`) dùng chung cho DI lúc chạy và cho
design-time. Lệch hai đường thì migration ghi lịch sử vào bảng khác với bảng runtime đọc → EF áp lại migration từ đầu.

### Đ-2.2 Không có khóa ngoại nào đi qua ranh giới schema

`profiles.user_id`, `posts.author_id`, `reactions.user_id` là `uuid` **trần** — không `REFERENCES identity.users`.
Toàn vẹn do ứng dụng giữ, không do DB.

Vì sao: FK chéo schema biến ranh giới module thành một thứ không tháo được — không deploy lệch pha được, không tách
database được, và `ON DELETE CASCADE` khiến xóa một user âm thầm xóa dữ liệu của module khác mà chủ module không biết.

**Cái giá phải trả, ghi ra để không quên:**

| Hệ quả | Xử ở đâu |
|---|---|
| Có thể tồn tại `posts.author_id` trỏ tới user đã bị xóa | **GĐ8** — đường xóa tài khoản phải fan-out sang Profile/Content |
| DB không chặn được `author_id` bịa | Không có đường vào: `author_id` LUÔN lấy từ `User.GetUserId()`, không từ body/route |
| Không `JOIN` được để lấy tên tác giả | Đ-2.3 — contract ở SharedKernel |

**Trong cùng một schema thì FK vẫn giữ nguyên:** `comments.post_id → posts.post_id`, `comments.parent_id` self-FK.
Ranh giới là *giữa các module*, không phải "bỏ FK cho tiện".

### Đ-2.3 Đọc chéo module bằng contract chỉ-đọc ở SharedKernel, không import module nhau

Danh sách bài phải hiện tên và avatar tác giả, nhưng `ModuleBoundaryTests` chặn **mọi** phụ thuộc giữa hai namespace
`SocialApp.Modules.*` — kể cả phụ thuộc vào một interface ở `Profile.Application`. Nên contract không đặt ở module chủ.

Khuôn (đã có tiền lệ chạy thật trong repo — `IRolePermissionSource` ở SharedKernel do `Identity.Infrastructure` hiện
thực và tầng 2 tiêu thụ, xem `C5` của GĐ1):

```
SocialApp.SharedKernel/Contracts/IUserDirectory.cs       // interface + record chiếu, KHÔNG entity
Modules/Profile/Infrastructure/UserDirectory.cs           // hiện thực, đọc profile.profiles
Modules/Content/Application/PostReadService.cs            // tiêu thụ qua DI
```

Ba luật cho mọi contract kiểu này, áp từ GĐ2 để SharedKernel không thành cái sọt:

1. **Chỉ đọc.** Không có phương thức ghi. Module muốn module khác ghi hộ là dấu hiệu chia module sai.
2. **Chỉ chiếu (projection), không entity.** `UserCard(Guid UserId, string DisplayName, string? AvatarKey)` —
   không bao giờ trả `Profile`.
3. **Batch trước, đơn sau.** Chữ ký nhận `IReadOnlyCollection<Guid>` và trả `IReadOnlyDictionary<Guid, UserCard>`.
   GĐ4 gọi hàm này cho 20 bài mỗi trang feed: có bản đơn thì N+1 xuất hiện đúng ở endpoint trọng điểm hiệu năng
   (GOAL-01) và không ai thấy nó cho tới lúc chạy k6.

### Đ-2.4 Hồ sơ tạo bằng bước onboarding của chính người dùng, không tạo lúc đăng ký

`POST /auth/register` của GĐ1 **đã đóng băng** (F7) và Identity không được phép ghi vào schema `profile` (Đ-2.2, Đ-2.3).
Nên hồ sơ không thể sinh ra cùng lúc tài khoản.

Chốt: **đăng nhập lần đầu mà chưa có dòng `profiles` → FE đưa vào màn `/onboarding`** đặt tên hiển thị (bắt buộc,
2–50 ký tự). `PUT /users/me/profile` là thao tác upsert: chưa có thì tạo.

Kèm theo một bất biến rẻ và rất có ích:

> **Không có hồ sơ thì không đăng được bài.** `POST /posts` trả 403 khi người gọi chưa có dòng `profiles`.

Nhờ nó, mọi `posts.author_id` chắc chắn tra được ra `UserCard` — không cần nhánh "tác giả không tên" ở mọi chỗ render,
và GĐ4 không phải xử lý bài thiếu tác giả trong feed.

**Hai phương án đã cân nhắc rồi loại:**

| Phương án | Vì sao loại |
|---|---|
| Thêm `displayName` vào `POST /auth/register` | Mở lại hợp đồng vừa đóng băng ở F7, và đặt dữ liệu của Profile vào tay Identity |
| Event bus trong tiến trình: `UserRegistered` → Profile tạo hồ sơ | Phải dựng hạ tầng event + xử lý thất bại + idempotency; ba ngày không đủ. Hoãn có địa chỉ: nếu GĐ5/GĐ6 cần event thật thì dựng ở đó, và onboarding vẫn đúng |

### Đ-2.5 Ảnh đi thẳng trình duyệt → R2; API không bao giờ nhận byte ảnh

Theo SEQ-01 và ADR gốc. `POST /media/uploads` chỉ trả URL đã ký; byte ảnh không đi qua api container, không đi qua BFF.

Hai lý do, thứ tự này: băng thông VPS OCI là tài nguyên khan nhất trong dự án; và api có 2 instance từ GĐ7 nên
upload nhiều phần qua api sẽ phải dính phiên vào một instance.

**Hệ quả cho BFF:** `MAX_PROXY_BODY` = 1MB trong `lib/bff/handlers.ts` giữ nguyên, vì body `POST /posts` chỉ là
JSON danh sách key. Nếu có ngày ai đó định đẩy ảnh qua BFF, con số này là chỗ nó sẽ vỡ — cố ý.

### Đ-2.6 Presign là hành động có quyền, nhưng GĐ2 KHÔNG thêm mã quyền mới

Ma trận 17 mã quyền của Mục 6.7.2 đã seed từ GĐ1 và seeder chạy `ON CONFLICT DO NOTHING`. GĐ2 không thêm dòng nào:

| Endpoint | Tầng 2 | Tầng 3 |
|---|---|---|
| `POST /media/uploads` với `purpose=post` | `[Authorize]` + `IAuthorizationService` với `perm:post.create` (Q-D5) | — (key sinh theo `userId` người gọi) |
| `POST /media/uploads` với `purpose=avatar` | `[Authorize]` | — |
| `PUT /users/me/profile`, `PUT/DELETE /users/me/avatar` | `[Authorize]` | không cần: route là `me`, id lấy từ token |
| `POST /posts` | `post.create` | hồ sơ tồn tại (Đ-2.4) + key thuộc tiền tố của người gọi (Đ-2.7) |
| `GET /posts/{id}` | `post.read.public` | BR-02 lúc đọc (Đ-2.9) |
| `PATCH /posts/{id}` | `post.update` | `author_id == actorId` → 403 |
| `DELETE /posts/{id}` | `post.delete` | `author_id == actorId` → 403 |

Một endpoint hai mức quyền theo `purpose` là chỗ dễ sai: **không** đặt `[RequirePermission("post.create")]` cho cả
endpoint (người chưa có quyền đăng bài vẫn phải đổi được avatar), và **không** bỏ trắng tầng 2 rồi kiểm trong service
(mất tính khai báo). Cách làm: `[Authorize]` ở attribute + kiểm `post.create` **ở controller bằng `IAuthorizationService`
với policy `perm:post.create`** — cùng handler và short-circuit Admin của tầng 2 (chốt Q-D5, 2026-09-19: gọi thẳng
`IPermissionCache` chặn nhầm Admin, vì `GetAsync("ADMIN")` trả rỗng — lối tắt ADMIN nằm trong `PermissionHandler`, không
trong cache; xem `huong-dan-khoi-d-endpoint.md` Q-D5). Ghi rõ ở hợp đồng: `purpose=post` mà thiếu quyền → **403**.

### Đ-2.7 `storage_key` mang tiền tố người dùng, và commit phải kiểm tiền tố đó

Dạng khóa: `posts/{userId}/{uuid7}.{ext}` và `avatars/{userId}/{uuid7}.{ext}`.

Lúc `POST /posts`, service kiểm **mọi** key trong body bắt đầu bằng `posts/{actorId}/`. Không kiểm là mở một IDOR
rất dễ bỏ sót: B upload ảnh, A đọc được key của B (hoặc đoán ra), A gắn ảnh của B vào bài của mình.

Dòng matrix bắt buộc: `TC-A03-media`.

### Đ-2.8 Dung lượng và loại ảnh: ký kèm, rồi HEAD lại — không tin client, cũng không tin chữ ký

Presigned `PUT` **không tự giới hạn dung lượng**. Ký URL cho `image/jpeg` 2MB rồi client `PUT` 400MB vẫn vào bucket
nếu `Content-Length` không nằm trong phần được ký. Ba lớp, cả ba đều cần:

1. **Lúc presign:** client khai `contentType` + `sizeBytes`; server từ chối ngay nếu ngoài allowlist
   (`image/jpeg`, `image/png`, `image/webp`) hoặc > 10MB (BR-01). Ký URL với `Content-Length` và `Content-Type`
   **nằm trong signed headers** → `PUT` lệch một byte hay lệch kiểu thì R2 trả 403.
2. **Lúc commit (`POST /posts`):** `HEAD object` từng key — đọc `ContentLength` và `ContentType` **thật** từ R2.
   Lệch với khai báo, hoặc object không tồn tại (client chưa `PUT` xong) → 400, **không tạo bài**.
3. **Ở DB:** `CHECK size_bytes <= 10485760` và `CHECK content_type IN (...)` — lưới cuối, theo PTTK Mục 5.5.

Lớp 2 là lớp không ai nghĩ tới và là lớp duy nhất nói được sự thật: nó đọc đúng thứ nằm trong bucket. Cái giá là
N lời gọi HEAD (≤ 10) trong đường commit; chấp nhận được vì `POST /posts` không phải đường nóng.

### Đ-2.9 Ảnh riêng tư không phục vụ bằng bucket công khai — URL đọc là presigned GET hạn ngắn

Bài có ba mức riêng tư, nên ảnh của bài cũng phải có ba mức. Bucket công khai + key khó đoán là *bảo mật bằng sự khó
đoán*: một link rò ra là ảnh riêng tư mở cho cả internet, vĩnh viễn, và không có cách nào thu hồi.

Chốt: API trả `mediaUrl` là **presigned GET, TTL 15 phút**, cấp **sau** khi đã qua BR-02. Ký là HMAC cục bộ
(không gọi mạng) nên chi phí không đáng kể cả khi ký 200 URL cho một trang feed.

**Hai hệ quả phải nhớ, cả hai đều là bẫy chéo giai đoạn:**

- **GĐ4 cache feed phải lưu `storage_key`, KHÔNG lưu URL đã ký.** Cache TTL 30s mà lưu URL ký 15 phút thì sau
  15 phút cache trả URL hết hạn → ảnh vỡ mà log không có lỗi nào. Ghi vào tài liệu GĐ4 ngay khi mở giai đoạn đó.
- **CSP phải mở `img-src` cho host R2** (`lib/security/csp.ts`), và `connect-src` cho chính host đó vì trình duyệt
  `PUT` trực tiếp. Nới CSP là **quyết định mới** theo luật frontend Mục 1 #13 → ghi vào Đ-E17 (Đ-2.14).

Phương án đã cân nhắc rồi loại: proxy ảnh qua BFF (bảo mật tốt nhất, nhưng dồn toàn bộ băng thông ảnh về VPS —
đúng thứ Đ-2.5 vừa tránh).

### Đ-2.10 Xóa bài là xóa mềm; object trên R2 xóa trễ, do worker

`DELETE /posts/{id}` đặt `status='deleted'` + `deleted_at`, không `DELETE` dòng. Global query filter của EF loại
bài đã xóa khỏi mọi truy vấn đọc — trừ một truy vấn của worker dọn rác (`IgnoreQueryFilters`).

Object trên R2 **không** xóa trong request: xóa ngay thì một lần bấm nhầm là mất ảnh vĩnh viễn, và nếu commit DB
rollback sau khi đã xóa object thì bài còn mà ảnh mất. Worker dọn object của bài có `deleted_at` cũ hơn **7 ngày**.

### Đ-2.11 Cursor chốt ở GĐ2, không chờ cổng mở GĐ4 — **lệch kế hoạch gốc, có chủ đích**

`ke-hoach-trien-khai.md` xếp việc "chốt hình dạng cursor" vào cổng mở GĐ4. Nhưng GĐ2 đã cần
`GET /users/{userId}/posts` có phân trang, nên hoặc chốt ở đây, hoặc GĐ2 tự bịa một dạng rồi GĐ4 viết lại cả phần
cuộn vô hạn của frontend — đúng cái kế hoạch gốc muốn tránh.

Dạng đã chốt:

```
cursor = base64url("{created_at:O}|{post_id}")       // opaque với client, FE không được tự dựng
sort   = (created_at DESC, post_id DESC)             // post_id là UUID v7 nên thứ tự PK = thứ tự thời gian
limit  : mặc định 20, tối đa 50 (AGENTS.md Mục 9)
đọc    : WHERE (created_at, post_id) < (@cursorAt, @cursorId)
trả    : { items: [...], nextCursor: string | null }  // hết dữ liệu thì nextCursor = null, KHÔNG phải ""
```

Cursor sai dạng → **400** với `errors.cursor`, không phải 500 và cũng không âm thầm trả trang đầu: trả trang đầu là
người dùng cuộn mãi không hết mà không ai biết đang có bug.

### Đ-2.12 Dựng khung `comments` + `reactions` ở GĐ2, nhưng không một endpoint nào

Theo kế hoạch gốc. Lý do thật sự đáng làm: `PostResponse` mang `commentCount` và `reactionCounts`, mà hai cột đó
nằm trên `posts`. Có chúng từ GĐ2 thì hình dạng DTO (và `schema.d.ts` của frontend) không phải đổi lần thứ hai ở GĐ3.

Giới hạn rõ ràng để khung không trôi thành nợ: hai bảng có **CHECK constraint đầy đủ** (depth ≤ 3 cho BR-08,
PK `(user_id, target_type, target_id)` cho BR-05) nhưng **không** entity có hành vi, **không** repository,
**không** service. GĐ3 được phép `ALTER` chúng — một migration nữa là cái giá đã biết trước.

`media_attachments.owner_type` nhận cả `'post'` và `'message'` theo PTTK, dù GĐ2 chỉ ghi `'post'`: để CK không phải
đổi ở GĐ5. Vì bảng đa hình nên **không có FK tới `posts`** — toàn vẹn do service giữ, và đây là lý do thứ hai
worker dọn rác tồn tại.

### Đ-2.13 Worker dọn rác chạy một instance, khóa bằng Redis

`IHostedService` trong `Content.Infrastructure`, chu kỳ 1 giờ, hai việc: xóa object mồ côi quá 24 giờ, xóa object
của bài xóa mềm quá 7 ngày.

GĐ7 chạy **2 container api** → không có khóa thì hai worker cùng quét cùng xóa. Dùng `SET key val NX EX` trên
kết nối Redis chung đã có ở SharedKernel. Viết khóa **ngay ở GĐ2**, đừng để GĐ7: lúc đó nó là lỗi chỉ xuất hiện
trên production và không tái hiện được ở dev một container.

Ngưỡng 24 giờ (không phải 10 phút bằng hạn presign): người dùng chọn ảnh rồi đi ăn cơm, quay lại bấm đăng vẫn phải
được — chỉ cần URL còn hạn lúc `PUT`. Xóa theo hạn presign là xóa ảnh của bài đang soạn.

### Đ-2.14 R2 vào SharedKernel; compose dev **không** thêm MinIO; nới CSP ghi thành Đ-E17

> **Chốt 2026-09-18** (chủ dự án), trước cổng mở. Hai điểm được chốt: (1) dev trỏ bucket `-dev` **thật**, không
> MinIO; (2) SharedKernel được phép mang thêm `AWSSDK.S3` — dependency bên thứ ba thứ ba của nó, sau Redis và
> UUIDNext. Không mở lại trong lúc thi công; muốn đổi thì đó là quyết định mới, có ngày, ghi vào đây.

- `SharedKernel/Storage/`: `IObjectStorage` + `R2ObjectStorage` + `R2Options`, theo đúng tiền lệ `SharedKernel/Redis/`.
  Hai module (Profile: avatar, Content: ảnh bài) và GĐ5 (media tin nhắn) dùng chung; không module nào gọi AWS SDK trực tiếp.
  Bề mặt đúng **năm** thao tác (`CreatePresignedPut`, `CreatePresignedGet`, `HeadAsync`, `DeleteAsync`, `ListAsync`) —
  giữ hẹp để nếu có ngày bỏ SDK thì đó là thay một class, không phải viết lại luồng.
- **"Đã có R2 rồi sao còn cần `AWSSDK.S3`?"** — R2 là *dịch vụ lưu trữ*, `AWSSDK.S3` là *thư viện client*, đúng quan
  hệ Postgres ↔ Npgsql. R2 không có giao thức riêng: nó **tương thích S3** một cách cố ý để dùng lại client có sẵn,
  và Cloudflare hướng dẫn chính bằng AWS SDK; không có gói `Cloudflare.R2` cho .NET. Tên gói dễ gây hiểu nhầm —
  **không** có traffic nào đi qua AWS (endpoint là `https://<account-id>.r2.cloudflarestorage.com`), **không** cần
  tài khoản AWS, **không** trả tiền cho AWS.
  Riêng presign thì tự viết được (~150 dòng HMAC, không chạm mạng); thứ khiến SDK đáng giá là ba lời gọi API **thật**:
  `HEAD` lúc commit (Đ-2.8) và `DELETE` + `LIST` của worker dọn rác (Đ-2.13) — `LIST` còn phải parse XML và phân trang
  bằng continuation token. Ký sai một byte ở canonical request thì R2 trả `403 SignatureDoesNotMatch`, mà trên trình
  duyệt triệu chứng **trông y hệt CORS sai**: đó là nửa ngày dò nhầm hướng, không phải 150 dòng code.
- **Không thêm MinIO vào `docker-compose.dev.yml`.** Dev trỏ vào bucket `-dev` thật trên R2. MinIO sẽ cho một cảm
  giác an toàn sai: CORS và chữ ký của nó không giống R2, và ISS-02 là rủi ro *đã đăng ký* — giả lập nó là tự bịt mắt.
  Cái giá: mỗi người dev cần một bộ khóa R2 trỏ vào bucket `-dev`. **Không đặt chúng vào `deploy/.env`** — file đó
  mang giá trị **staging** (cùng lý do `AddIdentityEmail` cố ý không đọc nó, và cùng lý do luật frontend cấm trỏ BFF
  local sang API staging). Dev đặt bằng `dotnet user-secrets` hoặc biến môi trường của shell; `DevEnvFile` **không**
  mở rộng cho `R2__*`. Lẫn hai bộ khóa là dev ghi ảnh rác thẳng vào bucket staging mà không ai thấy.
  CI **không** có khóa R2 → xem Mục 10 (fake in-memory + test chữ ký thuần).
- Nới `img-src`/`connect-src` cho host R2 là thay đổi CSP ⇒ **quyết định mới**: ghi thành **Đ-E17** trong
  `huong-dan-khoi-e-frontend.md` *trong cùng commit* với code (luật frontend Mục 11 #4).

### Đ-2.15 Presign theo lô, không gọi 10 lần

`POST /media/uploads` nhận **mảng** tối đa 10 file và trả mảng URL. Một bài 10 ảnh = **1** request.

Không phải chuyện tiết kiệm: hạn mức chung là 100 req/phút/user, nên 10 request presign + 10 `PUT` + 1 commit cho
mỗi bài khiến người dùng đăng 4–5 bài liên tiếp ăn 429 — và 429 lúc đang đăng bài trông y như lỗi hệ thống.

---

## 4. Mô hình dữ liệu

Hai migration đầu tiên của hai module. DDL dưới đây là **đích đến**; hiện thực qua EF Core migration, không viết SQL
tay vào repo. Quy ước thời gian, UUID v7 và `updated_at` giữ **nguyên xi** GĐ1 (`giai-doan-1.md` Mục 4): giá trị do
đồng hồ app gán trong entity, `DEFAULT now()` chỉ là lưới cho SQL thô, `updated_at` do override `SaveChanges` đóng dấu.

```sql
-- ================= schema "profile" =================
CREATE SCHEMA IF NOT EXISTS profile;

-- ENT-01a · profiles
CREATE TABLE profile.profiles (
    user_id      uuid        PRIMARY KEY,            -- = identity.users.user_id · KHÔNG FK (Đ-2.2)
    display_name varchar(50) NOT NULL,
    bio          varchar(500),
    avatar_key   varchar(200),                       -- storage_key trên R2; NULL = ảnh mặc định
    created_at   timestamptz NOT NULL DEFAULT now(),
    updated_at   timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_profiles_display_name_not_blank CHECK (btrim(display_name) <> '')
);
-- Index GIN pg_trgm trên unaccent(display_name): HOÃN tới GĐ6 (UC-16), không tạo ở đây.

-- ================= schema "content" =================
CREATE SCHEMA IF NOT EXISTS content;

-- ENT-02 · posts
CREATE TABLE content.posts (
    post_id         uuid         PRIMARY KEY,        -- UUID v7
    author_id       uuid         NOT NULL,           -- = identity.users.user_id · KHÔNG FK (Đ-2.2)
    body            varchar(5000),                   -- BR-01: rỗng thì phải có ≥ 1 ảnh
    privacy         varchar(10)  NOT NULL DEFAULT 'public',
    status          varchar(10)  NOT NULL DEFAULT 'published',
    media_count     smallint     NOT NULL DEFAULT 0, -- giữ ĐỒNG BỘ với media_attachments trong 1 transaction
    comment_count   integer      NOT NULL DEFAULT 0, -- GĐ3 ghi
    reaction_counts jsonb        NOT NULL DEFAULT '{}'::jsonb,   -- GĐ3 ghi
    hidden_reason   varchar(200),                    -- GĐ6 ghi (BR-07)
    edited_at       timestamptz,                     -- NULL = chưa sửa lần nào
    created_at      timestamptz  NOT NULL DEFAULT now(),
    updated_at      timestamptz  NOT NULL DEFAULT now(),
    deleted_at      timestamptz,
    CONSTRAINT ck_posts_privacy     CHECK (privacy IN ('public','friends','private')),
    CONSTRAINT ck_posts_status      CHECK (status  IN ('published','hidden','deleted')),
    CONSTRAINT ck_posts_media_count CHECK (media_count BETWEEN 0 AND 10),
    -- BR-01 ở tầng DB. Hệ quả: INSERT phải mang media_count ĐÚNG ngay từ đầu, không được
    -- INSERT 0 rồi UPDATE sau — CHECK nổ ngay ở câu INSERT với bài chỉ có ảnh.
    CONSTRAINT ck_posts_not_empty   CHECK (media_count > 0 OR btrim(coalesce(body,'')) <> '')
);

-- Index của UC-09 (trang cá nhân) và là index GĐ4 dùng lại cho feed (PTTK Mục 5.6).
-- post_id trong khóa sắp để cursor keyset (Đ-2.11) đọc thẳng từ index, không phải sort lại.
CREATE INDEX idx_posts_author_created ON content.posts (author_id, created_at DESC, post_id DESC)
    WHERE status = 'published';

-- ENT-08 · media_attachments — đa hình theo PTTK: owner_type post|message (Đ-2.12)
CREATE TABLE content.media_attachments (
    media_id     uuid        PRIMARY KEY,
    owner_type   varchar(10) NOT NULL,
    owner_id     uuid        NOT NULL,               -- post_id (GĐ2) hoặc message_id (GĐ5)
    storage_key  varchar(200) NOT NULL UNIQUE,       -- UNIQUE: chặn gắn CÙNG object vào hai bài
    content_type varchar(40) NOT NULL,
    size_bytes   integer     NOT NULL,
    width        smallint,
    height       smallint,
    position     smallint    NOT NULL,               -- thứ tự hiển thị 0..9
    created_at   timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_media_owner_type   CHECK (owner_type IN ('post','message')),
    CONSTRAINT ck_media_size         CHECK (size_bytes > 0 AND size_bytes <= 10485760),
    CONSTRAINT ck_media_content_type CHECK (content_type IN ('image/jpeg','image/png','image/webp')),
    CONSTRAINT ck_media_position     CHECK (position BETWEEN 0 AND 9)
);
CREATE UNIQUE INDEX uq_media_owner_position ON content.media_attachments (owner_type, owner_id, position);
CREATE INDEX        idx_media_owner         ON content.media_attachments (owner_type, owner_id);

-- ENT-03 · comments — KHUNG (Đ-2.12): có constraint đầy đủ, không có endpoint nào ở GĐ2
CREATE TABLE content.comments (
    comment_id uuid          PRIMARY KEY,
    post_id    uuid          NOT NULL REFERENCES content.posts(post_id) ON DELETE CASCADE,
    parent_id  uuid          REFERENCES content.comments(comment_id) ON DELETE CASCADE,
    author_id  uuid          NOT NULL,
    depth      smallint      NOT NULL DEFAULT 1,
    body       varchar(1000) NOT NULL,
    status     varchar(10)   NOT NULL DEFAULT 'visible',
    created_at timestamptz   NOT NULL DEFAULT now(),
    updated_at timestamptz   NOT NULL DEFAULT now(),
    deleted_at timestamptz,
    CONSTRAINT ck_comments_depth  CHECK (depth BETWEEN 1 AND 3),          -- BR-08
    CONSTRAINT ck_comments_status CHECK (status IN ('visible','deleted')) -- xóa giữ nhánh
);
CREATE INDEX "IX_comments_post_id"   ON content.comments (post_id, created_at);
CREATE INDEX "IX_comments_parent_id" ON content.comments (parent_id);

-- ENT-05 · reactions — KHUNG (Đ-2.12). PK ba cột CHÍNH LÀ BR-05: 1 cảm xúc / người / đối tượng
CREATE TABLE content.reactions (
    user_id     uuid        NOT NULL,
    target_type varchar(10) NOT NULL,
    target_id   uuid        NOT NULL,
    type        varchar(10) NOT NULL,
    created_at  timestamptz NOT NULL DEFAULT now(),
    updated_at  timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (user_id, target_type, target_id),
    CONSTRAINT ck_reactions_target CHECK (target_type IN ('post','comment')),
    CONSTRAINT ck_reactions_type   CHECK (type IN ('like','love','haha','wow','sad','angry'))
);
CREATE INDEX idx_reactions_target ON content.reactions (target_type, target_id);
```

**Ba chỗ dễ sai trong migration này:**

1. **`ck_posts_not_empty` và thứ tự INSERT.** Bài chỉ có ảnh mà INSERT `media_count = 0` rồi UPDATE sau là đỏ ngay ở
   INSERT. Service phải đếm ảnh **trước**, rồi INSERT post với con số đúng, rồi INSERT media — tất cả trong một
   transaction. Có test `BR01-06` canh đúng đường này.
2. **`storage_key` UNIQUE** là thứ chặn "gắn một object vào hai bài". Nó cũng có nghĩa là retry commit với cùng
   danh sách key sẽ nhận `DbUpdateException` — service phải dịch thành **409**, không để rơi thành 500.
3. **`reaction_counts jsonb DEFAULT '{}'`** phải có `::jsonb`; thiếu cast thì Npgsql sinh cột `jsonb` với default
   kiểu `text` và migration đỏ trên Postgres 16.

---

## 5. Dữ liệu nền

**GĐ2 không seed gì.** Cả 17 mã quyền và 3 vai trò đã có từ GĐ1, và mọi quyền GĐ2 cần (`post.read.public`,
`post.read.friends`, `post.create`, `post.update`, `post.delete`) đều đã nằm trong ma trận.

Ba hệ quả cần nói rõ, vì "không seed" dễ bị hiểu thành "không phải làm gì":

| Việc | Vì sao vẫn phải làm |
|---|---|
| `MigrateProfileModuleAsync` / `MigrateContentModuleAsync` nối vào hook `--migrate` | Không nối thì staging chạy code mới trên schema cũ; api khởi động được nhưng mọi truy vấn đỏ |
| Thứ tự trong hook: Identity → Profile → Content | Không có FK chéo nên thứ tự không bắt buộc về mặt DB, nhưng cố định thứ tự để log deploy đọc được |
| `PermissionCodeUsageTests` (đã có từ GĐ1) | Nó canh việc `[RequirePermission]` chỉ dùng mã có trong `PermissionCodes` — GĐ2 gõ sai chuỗi `"post.craete"` sẽ bị bắt ở đây, không phải ở runtime 403 |

---

## 6. Ba tầng kiểm soát truy cập áp vào GĐ2

GĐ1 dựng cơ chế; GĐ2 là **người tiêu thụ đầu tiên**. Không module nào được tự chế cách kiểm tra riêng
(`giai-doan-1.md` Mục 6, `AGENTS.md` Mục 10).

### 6.1 Bảng đầy đủ: endpoint × tầng 2 × tầng 3 × mã lỗi

| Endpoint | Tầng 2 | Tầng 3 (ở service) | Không đạt tầng 3 |
|---|---|---|---|
| `PUT /users/me/profile` | `[Authorize]` | — (`me`, id từ token) | — |
| `PUT /users/me/avatar` | `[Authorize]` | key thuộc `avatars/{actorId}/` + object tồn tại | **400** (key sai dạng) / **403** (key của người khác) |
| `DELETE /users/me/avatar` | `[Authorize]` | — | — |
| `GET /users/{id}/profile` | `[Authorize]` | — (hồ sơ là dữ liệu công khai trong MVP) | **404** khi chưa có hồ sơ |
| `POST /media/uploads` | `[Authorize]` (+ `post.create` khi `purpose=post`) | — | **403** khi thiếu `post.create` |
| `POST /posts` | `post.create` | có hồ sơ (Đ-2.4) · mọi key thuộc `posts/{actorId}/` · HEAD khớp | **403** hồ sơ/khóa · **400** HEAD lệch |
| `GET /posts/{id}` | `post.read.public` | BR-02 lúc đọc | **404** (quy ước 3b: không tồn tại và không được thấy trả CÙNG một thứ) |
| `GET /users/{id}/posts` | `post.read.public` | lọc BR-02 ngay trong truy vấn | danh sách rỗng, **không** 403 |
| `PATCH /posts/{id}` | `post.update` | `author_id == actorId` | **403** |
| `DELETE /posts/{id}` | `post.delete` | `author_id == actorId` | **403** |

Hai mã khác nhau cho hai loại endpoint là **cố ý**, đúng quy ước 3b của GĐ1: thao tác ghi cần ownership trả **403**;
đọc nội dung có mức hiển thị trả **404**. Trộn lẫn hai cái thì status code tự nó tố cáo tài nguyên có tồn tại.

### 6.2 Khuôn tầng 3 — chép nguyên hình dạng, không sáng tạo

```csharp
// Modules/Content/Application/Posts/PostService.cs
public async Task<Result> UpdateAsync(Guid postId, Guid actorId, UpdatePostRequest req, CancellationToken ct)
{
    var post = await _posts.FindAsync(postId, ct);

    // Tầng 3. KHÔNG có nhánh "if role == ADMIN" ở đây — Admin short-circuit CHỈ ở tầng 2 (Mục 3.2 của GĐ1).
    // "Không tồn tại" và "không phải của bạn" trả CÙNG một Result — status code cũng không được lộ.
    if (post is null || post.AuthorId != actorId || post.Status == PostStatus.Deleted)
        return Result.Forbidden();
    ...
}
```

`actorId` **luôn** do controller truyền vào từ `User.GetUserId()`. Không có đường nào cho nó đến từ route, body hay
query — đây là loại lỗi mà code review phải bắt, vì không test tự động nào phân biệt được `actorId` lấy từ đâu.

### 6.3 Sáu dòng AuthZ matrix mới — chỉ thêm dòng vào `AuthZMatrix.cs`

Khung đã có từ `B2`/`B3` của GĐ1: thêm dòng, **không** sửa `AuthZMatrixTests`, `AuthZCase` hay `AuthZApiFactory`.

| Id | Kịch bản | Người gọi | Gọi gì | Kỳ vọng |
|---|---|---|---|---|
| `TC-A03` | A `PATCH` bài của B | `Caller.User` | `PATCH /api/v1/posts/{id của B}` | **403** |
| `TC-A03-delete` | A `DELETE` bài của B | `Caller.User` | `DELETE /api/v1/posts/{id của B}` | **403** |
| `TC-A03-media` | A tạo bài gắn ảnh nằm dưới tiền tố của B | `Caller.User` | `POST /api/v1/posts` | **403** |
| `TC-A01-posts` | Đăng bài không kèm JWT | `Caller.Anonymous` | `POST /api/v1/posts` | **401** |
| `TC-A01-profile` | Sửa hồ sơ không kèm JWT | `Caller.Anonymous` | `PUT /api/v1/users/me/profile` | **401** |
| `READ-01` | A đọc bài `private` của B | `Caller.User` | `GET /api/v1/posts/{id của B}` | **404** |

`TC-A03` là dòng mà `OWN-00` của GĐ1 đã chừa chỗ sẵn (xem chú thích ngay trong `AuthZMatrix.cs`): dùng `ArrangePath`
để tạo bài của B rồi trả về path thật. Nếu phải sửa khung mới thêm được dòng nào ở bảng trên thì **dừng lại** — đó là
dấu hiệu khung thiết kế sai, và sửa khung một lần ở GĐ2 rẻ hơn nhiều so với sửa ở GĐ5.

> **Chốt 2026-09-19 (`Q-B2`, hướng dẫn khối B/C Mục 1.3):** đúng trường hợp đoạn trên đã chừa — `TC-A03-media` **không**
> thêm được mà không chạm khung. `Ma_tran_phan_quyen` sinh token **sau** `ArrangePath` nên `ArrangePath` không biết id
> người gọi; người gọi chưa có hồ sơ nên 403 đến từ Đ-2.4, không từ Đ-2.7 — dòng xanh vì lý do sai. Sửa khung **một lần,
> tối thiểu**: `AuthZArrange` mang thêm `CallerUserId`, token sinh trước `ArrangePath`, `TestJwt.ForCaller` nhận `userId`
> tùy chọn. Năm dòng còn lại không cần gì thêm.

---

## 7. Luồng nghiệp vụ

### 7.1 Onboarding hồ sơ (FR-013)

```
đăng nhập (GĐ1)  →  FE gọi GET /users/{me}/profile
                       ├─ 200  → vào app bình thường
                       └─ 404  → chuyển /onboarding (không cho bỏ qua)
                                   → PUT /users/me/profile {displayName, bio?}
                                   → 200 ProfileResponse → vào app
```

`PUT` là **upsert**: chưa có dòng thì tạo, có rồi thì sửa. Không tách `POST`/`PUT` — hai endpoint cho một thao tác
buộc FE phải biết trạng thái server trước khi gọi, và đúng lúc hai tab đua nhau thì một cái nhận 409 vô nghĩa.

### 7.2 SEQ-01 — upload ảnh và đăng bài (FR-004, BR-01)

```
 1. FE  : người dùng chọn 1..10 ảnh; kiểm ngay ở client loại + dung lượng (cùng ngưỡng server, Đ-E5)
 2. FE  → BFF → API : POST /media/uploads { purpose:"post", files:[{contentType,sizeBytes}, ...] }
 3. API : kiểm quyền (post.create) · kiểm allowlist + <=10MB + <=10 file
          sinh key posts/{userId}/{uuid7}.{ext}
          ký PUT hạn 10 phút, Content-Type + Content-Length NẰM TRONG signed headers
       <- 201 [{ mediaKey, uploadUrl, expiresIn, requiredHeaders }]
 4. FE  → R2 (THẲNG, không qua BFF): PUT uploadUrl, body = file, đúng headers đã ký
          dùng XHR để có tiến trình từng ảnh; ảnh nào lỗi thì báo riêng ảnh đó, không hủy cả lô
 5. FE  → BFF → API : POST /posts { body, privacy, mediaKeys:[...] }
 6. API : tầng 3 — có hồ sơ? mọi key thuộc posts/{actorId}/?
          HEAD từng object trên R2 → size/type THẬT khớp khai báo? (Đ-2.8)
          BR-01: body rỗng thì mediaKeys phải >= 1; <= 10 ảnh
 7. API : MỘT transaction — INSERT posts (media_count đúng ngay từ đầu) + INSERT media_attachments
       <- 201 PostResponse (mediaUrl là presigned GET 15 phút)
 8. API : phát event PostCreated (in-process, GĐ2 chỉ log — GĐ6 nối notification vào đúng chỗ này)
```

**Bốn chỗ hỏng đã biết trước:**

| Hỏng | Triệu chứng | Chặn bằng |
|---|---|---|
| Thiếu CORS trên bucket | Bước 4 đỏ **chỉ trên trình duyệt**; `curl` xanh | F3 — bắt buộc thử trên trình duyệt thật (ISS-02) |
| `PUT` thiếu header đã ký | R2 trả 403 `SignatureDoesNotMatch`, dễ tưởng sai khóa | `requiredHeaders` trả kèm ở bước 3; FE gửi đúng từng cái |
| Ảnh `PUT` xong rồi người dùng bỏ đi | Object mồ côi nằm lại vĩnh viễn | Worker dọn sau 24 giờ (Đ-2.13) |
| Commit hỏng giữa chừng | Bài có 3/5 ảnh | Một transaction ở bước 7; HEAD ở bước 6 đứng **trước** transaction |

### 7.3 Sửa và xóa bài (FR-005)

- `PATCH /posts/{id}`: sửa được `body` và `privacy`. **Không sửa được danh sách ảnh ở GĐ2** — sửa ảnh kéo theo việc
  dọn object của ảnh bị gỡ, tức là thêm một luồng rác nữa; hoãn có địa chỉ tới GĐ7 nếu UI thật sự cần. Mỗi lần sửa
  đóng dấu `edited_at` để FE hiện nhãn "đã chỉnh sửa".
- `DELETE /posts/{id}`: xóa mềm (Đ-2.10). Sau khi xóa, `GET /posts/{id}` của **chính tác giả** cũng trả **404** —
  bài đã xóa không còn là tài nguyên; giữ cho nó nhìn thấy được là mở ra một trạng thái thứ tư không ai đặc tả.

### 7.4 Đọc bài và BR-02 tại thời điểm đọc

```
GET /posts/{id}  →  tra post (status='published')
                    privacy = 'public'   → cho xem
                    privacy = 'private'  → chỉ khi author_id == actorId
                    privacy = 'friends'  → IFriendshipReader.AreFriendsAsync(actorId, author_id)
                                           GĐ2: luôn false ⇒ chỉ tác giả xem được (Đ-2.9)
                    không đạt            → 404, CÙNG phản hồi với "không tồn tại"
```

**"Tại thời điểm đọc"** nghĩa là không có bản sao quyền xem nào bị đóng băng lúc ghi: đổi `privacy` của bài cũ có
hiệu lực ngay ở request kế tiếp, và sang GĐ4 khi hủy kết bạn thì bài `friends` biến mất ngay (trừ độ trễ cache 60s
đã đăng ký ở PTTK Mục 5.6).

`IFriendshipReader` là **null-object có chủ đích**, không phải chỗ trống: có interface ở SharedKernel, có hiện thực
`AlwaysStrangers` đăng ký ở GĐ2, có test pin hành vi. GĐ4 đổi **một dòng DI** sang hiện thực thật của SocialGraph và
không phải chạm vào module Content.

### 7.5 Dọn rác (worker)

```
mỗi 1 giờ · SET lock NX EX 3000 trên Redis · chỉ instance lấy được khóa mới chạy
  (1) object mồ côi : key trong bucket cũ hơn 24h mà KHÔNG có dòng media_attachments → xóa object
  (2) bài xóa mềm   : post.deleted_at < now() - 7 ngày → xóa object + xóa dòng media_attachments
log: số object đã xóa, số byte thu hồi. Redis chết → BỎ lượt này, KHÔNG chạy khi không có khóa.
```

Nhánh (1) phải liệt kê bucket theo `continuation token` và giới hạn mỗi lượt (ví dụ 1000 object) để một bucket lớn
không giữ khóa suốt cả tiếng.

> **Chốt 2026-09-19 (lúc thi công C4):** nhánh (1) chỉ quét tiền tố **`posts/`**, không quét cả bucket. Lý do: avatar
> **không** có dòng `media_attachments` — nó sống ở `profile.profiles.avatar_key`, schema mà module Content không được đọc
> (Đ-2.2, Đ-2.3). Áp luật "cũ hơn 24 giờ mà không có dòng `media_attachments`" lên `avatars/` là **xóa nhầm avatar đang
> dùng**. Avatar mồ côi (đổi avatar để lại object cũ; `DELETE /users/me/avatar` chỉ gỡ liên kết — D3) là việc của module
> Profile, **hoãn có địa chỉ**: dung lượng không đáng kể ở quy mô đồ án, làm khi Profile có worker riêng hoặc khi GĐ8
> (xóa tài khoản) chạm tới. Hai điểm thi công khác: lượt đầu chạy **sau** một chu kỳ, không chạy lúc khởi động (deploy
> hai instance cùng lúc không tranh khóa khi còn warm-up); không nhả khóa sau lượt — `EX 3000` tự nhả trước chu kỳ kế.

---

## 8. Hợp đồng API

Base `/api/v1`. Mọi lỗi RFC 7807 kèm `traceId`. Rate limit chung 100 req/phút/user.
**Hai file hợp đồng mới**, mỗi file là nguồn sự thật của một module và là một cổng CI:

- `src/backend/Modules/Profile/Presentation/profile-v1.yaml` — nhóm Swagger `profile-v1`
- `src/backend/Modules/Content/Presentation/content-v1.yaml` — nhóm Swagger `content-v1`

Tên nhóm phải khớp ở đúng ba chỗ như GĐ1: `[ApiExplorerSettings(GroupName=...)]` trên controller, `SwaggerDoc` trong
`Program.cs`, và tên file yaml. Lệch một chỗ thì endpoint biến mất khỏi Swagger mà không có lỗi nào báo.

### 8.1 Profile

| Method | Path | Auth | Thành công | Lỗi |
|---|---|---|---|---|
| GET | `/users/{userId}/profile` | Bearer | 200 `ProfileResponse` | 400 id sai dạng · 401 · 404 chưa có hồ sơ |
| PUT | `/users/me/profile` | Bearer | 200 `ProfileResponse` (upsert) | 400 validation · 401 |
| PUT | `/users/me/avatar` | Bearer | 200 `ProfileResponse` | 400 key sai dạng / object không tồn tại · 401 · 403 key của người khác |
| DELETE | `/users/me/avatar` | Bearer | 204 | 401 |

```
ProfileResponse { userId, displayName, bio?, avatarUrl?, createdAt, updatedAt }
```

`avatarUrl` là presigned GET 15 phút (Đ-2.9), **không** phải `avatar_key`. Client không bao giờ thấy key của người
khác — đó cũng là lý do `PUT /users/me/avatar` nhận `mediaKey` chứ không nhận URL.

**Request body (chốt 2026-09-19 lúc viết hợp đồng — Mục 8 bản gốc chỉ đặc tả response):**

```
UpsertProfileRequest { displayName (bắt buộc, 2–50 ký tự sau trim), bio? (≤ 500; vắng mặt HOẶC null = xóa — PUT là thay thế toàn phần, chốt Q-D3) }
SetAvatarRequest     { mediaKey (bắt buộc, dạng avatars/{userId}/{uuid7}.{ext}) }
```

### 8.2 Content

| Method | Path | Auth | Thành công | Lỗi |
|---|---|---|---|---|
| POST | `/media/uploads` | Bearer (+ `post.create` khi `purpose=post`) | 201 `[UploadTicket]` | 400 loại/dung lượng/số lượng · 401 · 403 |
| POST | `/posts` | Bearer + `post.create` | 201 `PostResponse` | 400 BR-01 hoặc HEAD lệch · 401 · 403 hồ sơ/khóa · 409 ảnh đã dùng |
| GET | `/posts/{postId}` | Bearer + `post.read.public` | 200 `PostResponse` | 400 · 401 · 404 |
| GET | `/users/{userId}/posts` | Bearer + `post.read.public` | 200 `PostPage` | 400 cursor sai · 401 |
| PATCH | `/posts/{postId}` | Bearer + `post.update` | 200 `PostResponse` | 400 · 401 · 403 |
| DELETE | `/posts/{postId}` | Bearer + `post.delete` | 204 | 401 · 403 |

```
UploadTicket  { mediaKey, uploadUrl, expiresIn, requiredHeaders: { "Content-Type": ..., "Content-Length": ... } }
PostResponse  { postId, author: { userId, displayName, avatarUrl? }, body?, privacy,
                media: [{ url, contentType, width?, height?, position }],
                commentCount, reactionCounts, createdAt, editedAt?, canEdit }
PostPage      { items: [PostResponse], nextCursor: string | null }
```

- `canEdit` do **server** tính (`author_id == actorId`) — FE không tự so id để quyết định hiện nút Sửa/Xóa; FE so id
  thì mỗi chỗ render lại lặp một bản logic quyền, và sớm muộn chúng lệch nhau.
- `reactionCounts` là object rỗng `{}` ở GĐ2 (Đ-2.12), **không** phải `null` — FE viết một lần, GĐ3 không phải sửa.
- **Không có** `mediaKey` trong `PostResponse`: key là chi tiết nội bộ, chỉ đi ra ngoài trong `UploadTicket` của
  chính người vừa xin upload.

**Request body (chốt 2026-09-19 lúc viết hợp đồng — Mục 8 bản gốc chỉ đặc tả response):**

```
CreateUploadsRequest { purpose: "post" | "avatar" (bắt buộc), files: [{ contentType, sizeBytes }] 1..10 (bắt buộc) }
CreatePostRequest    { body? (≤ 5000), privacy (BẮT BUỘC), mediaKeys?: [{ mediaKey, contentType, sizeBytes }] ≤ 10 }
UpdatePostRequest    { body?, privacy? }   — body {} rỗng → 400; có mediaKeys → 400 (field lạ, GĐ2 không sửa ảnh)
```

> **Q-D1 (chốt 2026-09-19):** `mediaKeys` của `POST /posts` là **mảng object** `{mediaKey, contentType, sizeBytes}`, không phải
> mảng chuỗi. Đ-2.8 lớp 2 đối chiếu HEAD với *"khai báo lúc presign"*, mà server không giữ trạng thái giữa presign và commit
> — mảng chuỗi thì hoặc thêm bảng `media_uploads` tạm (một migration, một luồng dọn nữa), hoặc bỏ luôn lớp 2. Client đang cầm
> `File` nên gửi lại khai báo là rẻ nhất, và không có gì để giả: HEAD vẫn là thứ quyết định. Tên trường giữ `mediaKeys` để
> `errors.mediaKeys` (AC-03) đúng như Mục 10.1. Ba điểm nhỏ chốt cùng lúc: `privacy` **bắt buộc** (tùy chọn + mặc định
> `public` là composer quên gửi thành bài công khai ngoài ý muốn); `PATCH` với body `{}` → 400 (OpenAPI không diễn đạt gọn
> "ít nhất một trường", ContractTests chỉ so mảng `required`); `bio: null` = xóa, bỏ trường = giữ nguyên.

### 8.3 Codegen frontend

**Không thêm script nào** (đổi 2026-09-19, luật frontend Mục 7). `pnpm gen:api` gọi `scripts/gen-api.mjs`, script
này **suy ra** danh sách module từ glob `../backend/Modules/*/Presentation/*-v1.yaml` và ghi ra
`lib/api/<module>/schema.d.ts`. Hai hợp đồng của GĐ2 vì vậy được sinh **ngay khi file `.yaml` được commit ở cổng mở**,
không phải chờ ai thêm dòng.

Đích suy từ **tên nhóm Swagger** (tên file bỏ hậu tố `-v1`), không từ tên thư mục module — tên nhóm đã buộc phải duy nhất
toàn app (mỗi nhóm một `SwaggerDoc` trong `Program.cs`), nên không có lớp va chạm "một module hai nhóm". **Không có ngoại
lệ đường dẫn nào**: Identity đã dời từ `lib/api/schema.d.ts` vào `lib/api/identity/` cùng ngày, đúng như GĐ1 khối E Mục 13
đã hẹn ("module thứ hai mới tách"). Phần lập kế hoạch của script là hàm thuần có unit test (`scripts/gen-api.test.ts`).

Cổng CI `API types khop hop dong` **cũng không liệt kê file nào**: nó chạy `pnpm gen:api` rồi đòi worktree sạch. Lý do
bỏ danh sách — `git status --porcelain -- <đường dẫn không tồn tại>` trả **rỗng và exit 0**, nên mọi danh sách gõ tay
(trong `package.json` lẫn trong `ci.yml`) đều có thể **xanh giả**: gõ sai một ký tự, hay thêm module thứ ba mà quên
thêm dòng, đều không ai biết. Cùng luật với `TreatNoTestsAsError=true` của cổng Contract/AuthZ: **một số không không
phải là một lần qua**.

---

## 9. Kế hoạch thi công (2 backend + 1 frontend · Ngày 6–9)

### 9.0 Chuẩn bị hạ tầng R2 — xong **trước** sáng Ngày 6

Hệ quả trực tiếp của Đ-2.14 (chốt 2026-09-18). Đây là việc thao tác trên dashboard Cloudflare, không phải việc code —
làm trước, để buổi cổng mở không biến thành buổi ngồi chờ tạo bucket. Người làm: chủ dự án (chủ tài khoản Cloudflare).

1. **Hai bucket:** `socialmedia-dev` và `socialmedia-staging`. Tách bucket, không tách bằng thư mục trong một bucket —
   token phạm vi theo bucket mới chặn được dev ghi nhầm sang staging.
2. **CORS cho từng bucket.** Đây là thứ duy nhất chỉ trình duyệt mới kiểm chứng được (ISS-02):

   | Bucket | `AllowedOrigins` | `AllowedMethods` | `AllowedHeaders` | `ExposeHeaders` |
   |---|---|---|---|---|
   | `-dev` | `http://localhost:3000` | `PUT`, `GET` | `content-type` | `etag` |
   | `-staging` | `https://mxh.banhgao.net` | `PUT`, `GET` | `content-type` | `etag` |

   Origin phải đúng dạng `scheme://host[:port]` — không path, không dấu `/` cuối. Lệch một ký tự thì trình duyệt
   chặn im lặng và triệu chứng trông hệt như ký sai chữ ký (cùng cạm bẫy với `Cors:AllowedOrigins` của GĐ1).
3. **Hai API token R2, phạm vi tối thiểu** — mỗi token chỉ đọc/ghi **một** bucket. Token của `-dev` phát cho 3 người;
   token của `-staging` chỉ nằm trên server.
4. **Đặt khóa đúng chỗ** (Đ-2.14 — đây là chỗ dễ làm sai nhất):

   ```bash
   # STAGING: 4 dòng R2__* trong deploy/.env trên server (file đã có sẵn 4 key trống ở .env.example)

   # DEV, trên máy mỗi người — user-secrets của project host, KHÔNG phải deploy/.env:
   dotnet user-secrets init -p src/backend/SocialApp.Api
   dotnet user-secrets set "R2:Endpoint"  "https://<account-id>.r2.cloudflarestorage.com" -p src/backend/SocialApp.Api
   dotnet user-secrets set "R2:Bucket"    "socialmedia-dev"  -p src/backend/SocialApp.Api
   dotnet user-secrets set "R2:AccessKey" "<khóa của bucket -dev>" -p src/backend/SocialApp.Api
   dotnet user-secrets set "R2:SecretKey" "<khóa của bucket -dev>" -p src/backend/SocialApp.Api
   ```

   `user-secrets` nằm ngoài thư mục repo nên không có đường lọt vào commit. Ai lỡ đặt khóa `-dev` vào `deploy/.env`
   thì lần chạy `docker compose -f deploy/docker-compose.staging.yml` kế tiếp sẽ đẩy ảnh dev vào bucket staging.

**Điều kiện coi là xong bước 9.0:** một ảnh `PUT` được lên `socialmedia-dev` **từ tab Network của trình duyệt** bằng URL
ký tay (hoặc bằng `C2` nếu đã có code). Chưa làm được việc này thì ISS-02 vẫn đang mở, dù code có xanh.

### Cổng mở — Ngày 6 sáng, cả nhóm, ~2 giờ

Không có sản phẩm của cổng mở thì **không ai gõ dòng code nào** (Mục 0C của kế hoạch gốc).

1. Chốt **Đ-2.1 → Đ-2.4** — bốn quyết định kiến trúc dùng cho cả năm module còn lại. Đây là phần đắt nhất của buổi họp.
2. Chốt hình dạng **cursor** (Đ-2.11) và hình dạng **`PostResponse`** — hai thứ frontend bám chặt nhất.
3. Viết **`profile-v1.yaml` + `content-v1.yaml`** đủ path, status code, schema → **commit**.
4. Xác nhận **Mục 9.0 đã xong** — hai bucket, CORS, hai token, khóa nằm đúng chỗ. Chưa xong thì khối C khởi động
   trong chân không và ISS-02 lùi tới cổng đóng, đúng thứ giai đoạn này sinh ra để tránh.
5. Chia lane: BE-1 khối A + D(Profile) · BE-2 khối B + C + D(Content) · FE khối E.

### Lịch theo ngày

| Ngày | BE-1 | BE-2 | FE |
|---|---|---|---|
| **6** | Cổng mở · A1–A3 (entity + context + migration Profile) | Cổng mở · C1–C2 (`IObjectStorage` + presign R2) | Cổng mở · E1 (codegen + api client + mở rộng `request()`) |
| **7** | A4–A6 (entity + migration Content) · D1–D3 (Profile) | C3–C4 (HEAD + worker dọn) · D4 (`POST /media/uploads`) | E2 onboarding hồ sơ · E3 avatar |
| **8** | D5–D6 (`POST /posts`, đọc bài + BR-02) | D7–D8 (PATCH/DELETE) · B1–B3 (matrix + BR-01 + contract) | E4 composer + upload thật · E5 danh sách/chi tiết |
| **9** | F1–F3 (staging, E2E, ISS-02) | B4–B5 (mở rộng cổng CI) · F4 | E6 sửa/xóa · E7 CSP + Đ-E17 · F2 |

Ngày 9 là **cổng đóng**, không phải ngày code. Việc chưa xong đến Ngày 9 thì cắt theo bảng ưu tiên của
`ke-hoach-trien-khai.md`, **không** kéo dài giai đoạn — GĐ4 là trọng điểm hiệu năng và không có chỗ để trượt.

### Cổng đóng — Ngày 9

Frontend bỏ mock, trỏ staging HTTPS thật; chạy E2E lát cắt; **đóng băng `profile-v1` + `content-v1`**; tick Mục 11 và
Mục 12. GĐ4 khởi động ngay sau đó.

### Thư viện cần thêm

| Gói | Ở đâu | Ghi chú |
|---|---|---|
| `AWSSDK.S3` | `SocialApp.SharedKernel` | **Đã duyệt 2026-09-18** (Đ-2.14). Ghim chính xác version. Chỉ presign + HEAD + Delete + List; **không** `TransferUtility` |
| — | frontend | Không thêm gói nào: upload dùng `XMLHttpRequest` có sẵn (fetch không báo được tiến trình upload) |

MinIO, ImageSharp, thư viện resize: **không** — đều ngoài phạm vi GĐ2 (Đ-2.14, Mục 2).

---

## 10. Chiến lược test

### 10.1 Nghiệm thu chức năng (integration, Postgres thật qua Testcontainers)

| Mã | Kịch bản | Kỳ vọng |
|---|---|---|
| `PROF-01` | `PUT /users/me/profile` lần đầu | 200, tạo dòng mới |
| `PROF-02` | `PUT` lần hai đổi `displayName` | 200, vẫn một dòng, `updated_at` đổi |
| `PROF-03` | `GET /users/{id}/profile` của người chưa onboarding | 404 |
| `PROF-04` | `PUT /users/me/avatar` với key `avatars/{id người khác}/...` | 403 |
| `AC-01` | `POST /posts` bài công khai có 1 ảnh | 201, `media_count=1`, ảnh có URL đã ký |
| `AC-02` | `POST /posts` body rỗng, không ảnh | 400 (BR-01), `errors.body` |
| `AC-03` | `POST /posts` 11 ảnh | 400 (BR-01), `errors.mediaKeys` |
| `AC-04` | `POST /posts` token hết hạn | 401 |
| `BR01-05` | Ảnh khai 1MB nhưng object thật 12MB | 400, và **không** có bài nào được tạo |
| `BR01-06` | Bài chỉ có ảnh, không có chữ | 201 — canh `ck_posts_not_empty` và thứ tự INSERT |
| `POST-06` | `PATCH` bài của mình | 200, `edited_at` khác null |
| `POST-07` | `DELETE` rồi `GET` lại bằng chính tác giả | 404 |
| `POST-08` | Commit hai lần cùng `mediaKeys` | 409, không tạo bài thứ hai |
| `READ-02..05` | Ma trận BR-02: public/private/friends × tác giả/người lạ | theo Mục 7.4 |
| `PAGE-01` | `GET /users/{id}/posts` 25 bài, `limit=20` | 20 item + `nextCursor`; trang 2 đủ 5 và `nextCursor=null` |
| `PAGE-02` | Cursor rác (`"abc"`) | 400 `errors.cursor` |
| `PAGE-03` | Bài mới chèn vào giữa hai lần gọi | Không nhân đôi, không nhảy cóc (bản chất keyset) |

### 10.2 Test hạ tầng R2 mà **không** có khóa R2 trên CI

Đây là chỗ dễ làm bậy nhất. Ba mức, tách bạch:

| Mức | Kiểm gì | Chạy ở |
|---|---|---|
| Unit (thuần) | Chuỗi ký SigV4, dạng key, allowlist, hạn 10 phút | CI — không chạm mạng |
| Integration | `FakeObjectStorage` in-memory cài `IObjectStorage`: HEAD trả size/type dựng sẵn | CI |
| **Kiểm tay trên trình duyệt** | CORS, chữ ký thật, `PUT` thật lên bucket `-staging` | **F3** — không tự động hóa được |

**Cấm** viết integration test gọi R2 thật rồi `Skip` khi thiếu khóa: test bị skip trên CI là test không tồn tại, mà
lại tạo cảm giác đã có lưới. Mức 3 là **kiểm tay có bằng chứng** (ảnh Network + ảnh object trong bucket), đúng nếp
Playwright của GĐ1.

### 10.3 Unit test

BR-01 ở validator (body ≤ 5000 hoặc ≥ 1 ảnh, ≤ 10 ảnh, ≤ 10MB), mã hóa/giải mã cursor (round-trip, và rác vào thì
phải ném), dạng `storage_key`, `AlwaysStrangers` luôn trả `false`.

### 10.4 Cổng CI phải mở rộng — năm chỗ, quên chỗ nào là cổng xanh giả

1. `tests/SocialApp.IntegrationTests/SocialApp.IntegrationTests.csproj`: thêm **hai** dòng `Content Include=…`
   `Link=Contracts/profile-v1.yaml` và `Link=Contracts/content-v1.yaml` (đúng dạng đã có cho `identity-v1.yaml`).
2. `ProfileContractTests` + `ContentContractTests` mang `[Trait("Category","Contract")]` — cùng project nên bước
   `API contract (CI GATE)` tự chạy, không phải sửa workflow.
3. `AuthZMatrix.cs`: sáu dòng mới (Mục 6.3).
4. `.github/workflows/ci.yml` bước `API types khop hop dong`: kiểm cả `lib/api/profile/schema.d.ts` và
   `lib/api/content/schema.d.ts`.
5. `ModuleBoundaryTests` / `PresentationBoundaryTests`: **không** phải sửa — chúng đã liệt kê đủ bảy module từ GĐ0.
   Nếu phải sửa chúng để code xanh thì code đang phá ranh giới, không phải test sai.

### 10.5 Frontend

| Công cụ | Kiểm |
|---|---|
| Vitest + `msw/node` | Validate BR-01 phía client · nhánh lỗi 400/403/409 của composer · một ảnh lỗi không hủy cả lô · nối trang theo cursor |
| Playwright (local, `workers: 1`) | Onboarding bắt buộc · đăng bài kèm 2 ảnh thật · sửa · xóa · 403 không lộ tài nguyên · **CSP không chặn PUT lên R2** |

E2E Playwright cần ảnh thật trong `e2e/fixtures/` (một JPEG nhỏ, một PNG). Không sinh ảnh lúc chạy: ảnh sinh động dễ
không phải JPEG hợp lệ, và lỗi khi đó trông hệt lỗi CORS.

---

## 11. Definition of Done

Theo Mục 3.5 của PTTK, áp cho **từng** UC của giai đoạn. *Rà ở `F4` ngày 2026-09-21. Chi tiết và lệnh đã chạy nằm ở
mục "Thực tế thi công" của `F4` trong [huong-dan-khoi-e-f-frontend-va-cong-dong.md](huong-dan-khoi-e-f-frontend-va-cong-dong.md).*

- [x] Đủ AC (US-004 AC-01..04; FR-005; FR-013). Integration 320 ca (319 xanh + 1 đỏ nền R2 của máy dev, CI xanh), E2E
  staging `F3`
- [x] Có kiểm RBAC (tầng 2) **và** ownership (tầng 3), có dòng trong `AuthZMatrix.cs`. `AuthZ matrix` 18/18 trên CI
  run 35561152514; bảng đột biến `B3` (bảy đột biến đều bị bắt)
- [x] Lỗi theo RFC 7807, `errors` đúng key hợp đồng, không lộ tài nguyên có tồn tại hay không (`TC-A03*` 403,
  `READ-01` 404, 403 của FE không lộ, `E6`)
- [x] Đã chạy thử trên **staging** bằng tài khoản thật, qua domain HTTPS (`F2`, `F3`, 2026-09-21)
- [x] Hợp đồng `.yaml` khớp Swagger runtime (`API contract` 6/6 trên CI), `pnpm gen:api` chạy lại thì worktree sạch
- [ ] Không lộ secret/PII: log **không** chứa presigned URL (nó mang chữ ký, là thông tin nhạy cảm có hạn), không chứa email
  — **chờ lệnh trên server** (`docker compose logs api | grep -c "X-Amz-Signature"` → 0, và đếm email). Máy dev không
  thay được: container api dev là bản 2026-09-18, không chạy lát cắt GĐ2

## 12. Checklist nghiệm thu cuối GĐ2

*Rà ở `F4` ngày 2026-09-21. Dòng chưa tick là dòng **chờ thao tác trên server staging hoặc dashboard R2**, không phải
dòng bỏ. Lệnh cho từng dòng ở "Thực tế thi công" của `F4`.*

**Dữ liệu và ranh giới**

- [x] Thấy đủ ba schema `identity`, `profile`, `content`; mỗi schema có `__EFMigrationsHistory` riêng. Log CD run
  35561152520: `[migrate] Đã áp dụng migration cho schema "identity", "profile", "content" … Thoát 0`; psql trên
  Postgres dev: ba bảng lịch sử
- [ ] Không có FK nào đi qua ranh giới schema — chứng minh bằng truy vấn `information_schema.referential_constraints`.
  Dev: **0** FK chéo (8 FK, đều cùng schema). **Chờ chạy trên Postgres staging**
- [ ] `--migrate` chạy hai lần liên tiếp: lần hai không đổi gì, exit 0 — **chờ chạy trên server**
- [x] ArchUnitNET xanh, và **không** ai nới rule để code chạy được. 13/13; lịch sử `tests/SocialApp.ArchitectureTests`
  từ GĐ2 chỉ **thêm** rule (A1, A4+A7, D0) và gỡ `Skip`, không có dòng nới

**Bảo mật**

- [x] Sáu dòng matrix mới xanh; thử cho đỏ một lần bằng cách bỏ kiểm ownership rồi khôi phục (`B3`: bỏ
  `post.AuthorId != actorId` → đỏ đúng `TC-A03` / `TC-A03-delete`)
- [ ] Đọc thẳng DB: không có `posts.author_id` nào khác `sub` của người đã tạo bài. Dev: 77 bài, 0 `author_id` không
  có trong `identity.users`, 0 không có hồ sơ; body gửi `authorId` → 400 (`Field_la_trong_body_tra_400`). **Chờ psql
  trên staging**
- [x] Trình duyệt không bao giờ thấy `storage_key` của người khác (Network tab ở `F2`: response của `/bff/api/posts/*`
  không có key nào)
- [x] Bucket **không** để public; mở một URL ảnh đã hết hạn → R2 trả 403 (`F3` #5: **403 `ExpiredRequest`**; bỏ hẳn
  chữ ký → `400 InvalidArgument`, không trả object)

**Vận hành**

- [x] `R2__Endpoint`, `R2__Bucket`, `R2__AccessKey`, `R2__SecretKey` đã có trên staging **trước khi merge**. Bằng chứng
  gián tiếp: `F3` `PUT`/`GET` 200 vào đúng bucket `socialmedia-staging`, CSP có host R2
- [ ] Worker dọn rác chạy đúng một lượt trên staging, log số object đã xóa — **chờ server**: worker mặc định TẮT, phải
  đặt `Media__Cleanup__Enabled=true`, và lượt đầu chạy **sau 60 phút** (không chạy lúc khởi động)
- [ ] Tắt Redis → worker bỏ lượt và **không** chạy khi không có khóa; api vẫn phục vụ bình thường — **chờ server**
  (Integration `Redis_khong_toi_duoc_thi_bo_luot_khong_xoa_gi` xanh ở local)

**Lát cắt dọc**

- [x] E2E trên staging: đăng nhập → onboarding → đăng bài 2 ảnh → xem lại → sửa → xóa (`F3`, 2026-09-21)
- [ ] Ảnh có mặt thật trong bucket `-staging` (ảnh chụp dashboard R2) — **chờ người có quyền Cloudflare**. Đường dẫn
  object đã biết: `avatars/{userId}/…` và `posts/{userId}/…` (`F3`)
- [x] Frontend đã bỏ mock (`mocks/` chỉ còn phục vụ Vitest — `F2` bước 1, lệnh bản Q-E7)

## 13. Sai khác so với kế hoạch gốc và báo cáo v5.0

| # | Kế hoạch gốc / v5.0 | GĐ2 thực hiện | Lý do |
|---|---|---|---|
| 1 | Cursor chốt ở cổng mở **GĐ4** | Chốt ở **GĐ2** (Đ-2.11) | GĐ2 đã cần danh sách bài; chốt muộn là viết lại phần cuộn vô hạn |
| 2 | "Hồ sơ" không nói ai tạo dòng | **Onboarding của chính người dùng** (Đ-2.4) | Hợp đồng `register` đã đóng băng, và Identity không được ghi sang schema khác |
| 3 | Không nói mức riêng tư của **ảnh** | Presigned GET hạn ngắn, không bucket công khai (Đ-2.9) | Bài `private` mà ảnh public là BR-02 thủng ở chỗ không ai nhìn |
| 4 | "≤10MB/ảnh" như một luật validation | Ba lớp: ký kèm size → **HEAD** lúc commit → CHECK ở DB (Đ-2.8) | Presigned PUT không tự giới hạn dung lượng; chỉ HEAD mới nói được sự thật |
| 5 | Mức riêng tư `friends` làm ở GĐ2 | Hoãn hành vi, giữ **null-object** `IFriendshipReader` (Đ-2.9) | Chưa có bảng `friendships`; GĐ4 đổi một dòng DI là xong |
| 6 | Compose dev có MinIO (AGENTS.md Mục 13) | **Không** thêm MinIO; dev dùng bucket `-dev` thật (Đ-2.14) | ISS-02 là rủi ro đã đăng ký; giả lập nó là tự bịt mắt |
| 7 | — | Presign **theo lô** ≤ 10 file (Đ-2.15) | 10 presign + 10 PUT + 1 commit mỗi bài sẽ đụng hạn mức 100 req/phút |

Mỗi dòng trong bảng này phải được nhắc lại trong commit tương ứng, mở bằng "Lệch …" theo luật commit Mục 5.3.

## 14. Rủi ro cần theo dõi

| Mã | Rủi ro | Dấu hiệu sớm | Ứng phó |
|---|---|---|---|
| **ISS-02** | CORS / presign R2 trục trặc | F3 đỏ trên trình duyệt trong khi `curl` xanh | Đã đăng ký từ đầu: đổi `IObjectStorage` sang hiện thực lưu volume VPS, giữ nguyên bảng metadata để chuyển lại R2 sau. Nhờ có interface, đây là thay một class chứ không phải viết lại luồng |
| **R2-01** | Khóa R2 lọt vào log hoặc vào bundle | Grep thấy `R2__` trong `.next/static` | Cổng bundle của CI đã grep sẵn — **thêm `R2__` và `X-Amz-Signature` vào danh sách grep** |
| **BOUND-01** | Ai đó "tạm" `JOIN` chéo schema cho nhanh | PR có `FromSqlRaw` chạm hai schema | ArchUnitNET không bắt được (SQL là chuỗi) → code review; cân nhắc thêm test grep chuỗi ở GĐ4 |
| **PERF-01** | N+1 khi lấy tên tác giả | Danh sách 20 bài sinh 21 truy vấn | Đ-2.3 batch-first; thêm test đếm số truy vấn ở khối B nếu còn nghi ngờ |
| **SCOPE-01** | Khung `comments`/`reactions` bị "tiện tay làm luôn" | PR khối A có service bình luận | Mục 2 đã nói rõ ngoài phạm vi; GĐ3 mới là chỗ của nó |
| **FE-01** | Bus factor 1 ở frontend (giữ nguyên từ GĐ1) | Không ai ngoài FE đọc PR khối E | Một người backend review chéo PR frontend, như GĐ1 |

---

# Phần B — Kế hoạch triển khai

## B.0 Cách đọc phần này

Phần A nói *cái gì* và *vì sao*. Phần B chia việc thành **sáu khối A–F**, mỗi khối một chuỗi đầu việc có mã
(`A1`, `D5`, …). Mã việc là thứ đi vào **tiêu đề commit** (luật commit Mục 4) và vào bảng theo dõi tiến độ.

Một đầu việc được coi là xong khi: code chạy · có test tương ứng · tài liệu/hợp đồng liên quan đã sửa **trong cùng
commit** · `detect-changes` sạch. Không có "xong 90%".

Thứ tự trong mỗi khối là **thứ tự phụ thuộc**, không phải thứ tự ưu tiên — B.9 nói rõ cái nào nằm trên đường găng.

## B.1 Điểm xuất phát — cái gì đã có sẵn

Kiểm ngày 2026-09-18 trên nhánh `loveart1210`. GĐ2 **không** dựng lại thứ nào trong bảng dưới:

| Đã có | Ở đâu | GĐ2 dùng để làm gì |
|---|---|---|
| `[RequirePermission]` + `PermissionHandler` + `IPermissionCache` | `SharedKernel/Authorization/` | Tầng 2 của mọi endpoint mới |
| `Result` / `Error` / `ToActionResult` + khuôn tầng 3 | `SharedKernel/Results/`, `Http/ResultHttpExtensions.cs` | Tầng 3 và toàn bộ ánh xạ lỗi |
| `ProblemTitles` + `SharedKernelProblemDetailsFactory` + `ValidationErrors` | `SharedKernel/Errors/`, `Http/` | RFC 7807, module **không tự chế** |
| `Uuid7.New()` | `SharedKernel/Ids/Uuid7.cs` | PK của `posts`, `media_attachments` |
| Kết nối Redis dùng chung + health check | `SharedKernel/Redis/` | Khóa của worker dọn rác (Đ-2.13) |
| Khuôn module 4 tầng + `AddApplicationPart` + nhóm Swagger | `Modules/Identity/`, `Program.cs` | Khuôn cho Profile và Content |
| `<Module>DbContextOptions` + design-time factory + hook `--migrate` | `Modules/Identity/Infrastructure/`, `Program.cs` | Khuôn cho hai context mới |
| Khung AuthZ matrix data-driven (`AuthZCase.ArrangePath` đã chừa sẵn cho TC-A03) | `tests/…/AuthZ/` | Sáu dòng mới, **không sửa khung** |
| Harness Testcontainers (`PostgresFixture`, `RedisFixture`) | `tests/…/Harness/` | Integration test hai module mới |
| Cổng CI: gitlink · API contract · AuthZ matrix · codegen FE · bundle sạch | `.github/workflows/ci.yml` | Mở rộng, không dựng lại (Mục 10.4) |
| BFF + proxy chung `/bff/api/[...path]` gắn bearer ở server | `src/frontend/app/bff/`, `lib/bff/` | Mọi endpoint GĐ2 dùng ngay, **không thêm route BFF** |
| CSP có nonce | `src/frontend/lib/security/csp.ts`, `proxy.ts` | Nới `img-src`/`connect-src` cho R2 (Đ-E17) |

**Bốn chỗ có sẵn nhưng phải sửa** (không phải viết mới, nhưng cũng không phải "dùng nguyên"):

| Chỗ | Sửa gì | Vì sao |
|---|---|---|
| `Program.cs` | 2 dòng `AddApplicationPart` · 2 mục `apiGroups` · 2 dòng `Add<Module>Module` · 2 dòng trong nhánh `--migrate` | Host cố ý liệt kê tường minh từng module |
| `IntegrationTests.csproj` | 2 dòng `Content Include` cho hai file hợp đồng | Không có thì `ContractTests` không tìm thấy file |
| `lib/api/http.ts` | `RequestOptions.method` hiện chỉ có `"GET" \| "POST"` | GĐ2 cần `PATCH`, `PUT`, `DELETE` |
| `.github/workflows/ci.yml` | Cổng codegen kiểm thêm 2 file · cổng bundle grep thêm `R2__` | Mục 10.4 và R2-01 |

## B.2 Bản đồ công việc

| Khối | Nội dung | Người | Số việc | Cần trước | Chặn |
|---|---|---|---|---|---|
| **A. Nền dữ liệu** | 2 module, 2 context, 2 migration, contract chéo module | BE-1 | 7 | cổng mở | C3, D |
| **B. Test + cổng CI** | Matrix, BR-01, contract test, mở rộng cổng CI | BE-2 | 5 | D (một phần) | F |
| **C. Lưu trữ đối tượng** | `IObjectStorage`, presign, HEAD, worker dọn | BE-2 | 5 | cổng mở | D4, D5 |
| **D. Endpoint** | 4 của Profile + 6 của Content | BE-1 + BE-2 | 9 | A, C | E(ráp thật), F |
| **E. Lane frontend** | Onboarding, avatar, composer, danh sách, sửa/xóa, CSP | FE | 8 | chỉ cần hợp đồng | F |
| **F. Cổng đóng** | Staging + E2E + ISS-02 + đóng băng hợp đồng | cả nhóm | 5 | D, E | GĐ4 |

**Ba lane chạy song song từ giờ đầu:** A (BE-1) · C (BE-2) · E (FE). Khối D là chỗ hợp lưu; khối B bám theo D.

Khác GĐ1 ở một điểm đáng chú ý: **khối C không phụ thuộc khối A**. Lưu trữ đối tượng không chạm DB, nên BE-2 có thể
đóng xong toàn bộ rủi ro R2 trong Ngày 6–7, tức là ISS-02 lộ ra sớm nhất có thể — đúng mục tiêu mà bản B của kế
hoạch gốc đặt cho GĐ2.

---

## B.3 Khối A — Nền dữ liệu

> **Mục tiêu khối:** hai module mới có schema riêng, migrate được, và **không có một tham chiếu nào** giữa chúng với
> nhau hay với Identity ngoài hai contract đã khai báo ở SharedKernel.

> **Hướng dẫn thi công từng bước:** [huong-dan-khoi-a-nen-du-lieu.md](huong-dan-khoi-a-nen-du-lieu.md)
> — mục tiêu và kết quả mong đợi của từng đầu việc, file nào, lệnh nào, cạm bẫy nào, checklist nghiệm thu.
> Mục B.3 dưới đây giữ nguyên vai trò "cái gì và vì sao".

### A1 — Entity của Profile trong `Domain/`

`UserProfile` (user_id, display_name, bio, avatar_key, created_at, updated_at). Không dùng navigation property trỏ
sang `User` — không có kiểu nào để trỏ (Đ-2.2). `user_id` là `Guid` trần.

> **Lệch B.3 bản gốc (chốt 2026-09-18, lúc thi công A1):** tên kiểu là `UserProfile`, không phải `Profile`. Tên
> `Profile` không biên dịch được ngoài `Domain/`: từ trong `SocialApp.Modules.Profile.Infrastructure`, C# tra tên
> theo thứ tự namespace lồng từ trong ra ngoài nên gặp namespace `SocialApp.Modules.Profile` trước khi xét `using`
> của file, và `DbSet<Profile>` thành `CS0118: 'Profile' is a namespace but is used like a type`. Đường thoát còn
> lại — alias `using ProfileEntity = SocialApp.Modules.Profile.Domain.Profile;` ở **mọi** file ngoài `Domain/` của
> GĐ2 lẫn GĐ4–GĐ8 — đắt hơn hẳn. Bảng DB vẫn là `profile.profiles` (Mục 4 không đổi), chỉ tên kiểu C# đổi.

### A2 — `ProfileDbContext` + `IEntityTypeConfiguration` + `ProfileDbContextOptions` + design-time factory

Chép đúng hình dạng của `IdentityDbContext`: `HasDefaultSchema("profile")`, bảng lịch sử migration trong schema
`profile`, override `SaveChanges` đóng dấu `updated_at`. **Không** `HasPostgresExtension("citext")` — Profile không
có cột citext nào.

### A3 — Migration đầu tiên của Profile + `AddProfileModule` + `MigrateProfileModuleAsync`

Nối vào `Program.cs`: một dòng DI, một dòng trong nhánh `--migrate`. Nghiệm thu bằng cách chạy `--migrate` hai lần
liên tiếp trên DB sạch: lần hai không đổi gì, exit 0.

### A4 — Entity của Content trong `Domain/`

`Post` (có `PostStatus`, `PostPrivacy` dạng enum, ánh xạ sang chuỗi để khớp CHECK), `MediaAttachment`,
và hai entity **khung** `Comment`, `Reaction` (chỉ thuộc tính, không hành vi — Đ-2.12).

Business rule BR-01 đặt trong `Domain/` dạng hàm thuần (`PostContentPolicy`) để unit test không cần DB, đúng nếp
`LockoutPolicy`/`RefreshTokenPolicy` của GĐ1.

### A5 — `ContentDbContext` + configuration + migration

Bốn bảng theo DDL Mục 4, kèm ba chỗ dễ sai đã liệt kê ở đó (`ck_posts_not_empty`, `storage_key` UNIQUE,
`jsonb DEFAULT '{}'::jsonb`). Global query filter loại `status = 'deleted'`; repository của worker dọn rác dùng
`IgnoreQueryFilters()` — chỗ **duy nhất** được phép.

### A6 — Hai contract chéo module ở SharedKernel

```
SharedKernel/Contracts/IUserDirectory.cs      → record UserCard; GetManyAsync(IReadOnlyCollection<Guid>)
SharedKernel/Contracts/IFriendshipReader.cs   → AreFriendsAsync(Guid, Guid) + AlwaysStrangers (Đ-2.9)
Modules/Profile/Infrastructure/UserDirectory.cs
```

`AlwaysStrangers` đăng ký ở `AddContentModule` với một comment trỏ thẳng tới GĐ4. Có test pin: đổi hành vi nó mà
không đổi test là đỏ — để GĐ4 không âm thầm thay bằng thứ khác.

### A7 — Gỡ `Skip` của các test namespace rỗng cho Profile/Content

`ModuleBoundaryTests` và bạn của nó dùng `WithoutRequiringPositiveResults()` nên hiện đang xanh trong chân không.
Sau A5, hai module đã có type thật: thêm một test canh gác giống
`Mvc_namespace_must_be_present_in_the_architecture` — khẳng định `SocialApp.Modules.Content.Domain` có > 0 type.

**Kết quả khối A:** `\dn` thấy ba schema; `--migrate` idempotent; ArchUnitNET xanh với type thật, không phải chân không.

---

## B.4 Khối B — Test và cổng CI

> **Mục tiêu khối:** biến mọi luật của Phần A thành thứ **chặn merge**, và giữ nguyên tinh thần GĐ1: khung không sửa,
> chỉ thêm dòng.

> **Hướng dẫn thi công từng bước:** [huong-dan-khoi-b-c-test-va-luu-tru.md](huong-dan-khoi-b-c-test-va-luu-tru.md)
> — mục tiêu và kết quả mong đợi của từng đầu việc, file nào, lệnh nào, cạm bẫy nào, checklist nghiệm thu.
> Gộp chung với khối C vì B.2 giao cả hai cho cùng một người (BE-2) và hai khối gặp nhau ở `C5`.
> Mục B.4 dưới đây giữ nguyên vai trò "cái gì và vì sao".

### B1 — Harness cho hai context mới

`PostgresFixture` thêm `SeededContentDatabaseAsync(key)` chạy migrate cả ba module. Giữ nguyên luật chọn hàm của
GĐ1: test **sửa** dữ liệu dùng `CreateDatabaseAsync`, test chỉ **đọc** dùng bản seed dùng chung.

Cảnh báo về thời gian: nhóm test Postgres của GĐ1 đã chạy tuần tự trong một collection. Thêm hai module là thêm
migration mỗi lần dựng DB — đo lại thời gian **trước** khi thêm; vượt ~3 phút thì tách collection (GĐ1 đã ghi ngưỡng này).

> **Chốt 2026-09-19 (`Q-B1`):** thêm `SeededContentDatabaseAsync` **bên cạnh**, giữ nguyên hàm cũ. Hàm cũ có **bốn** chỗ
> gọi cùng `key = "authz"` (`AuthZMatrixTests`, `OwnershipTemplateTests`, `JwtAuthenticationTests`,
> `RolePermissionSourceTests`) — phát hiện bằng impact analysis, không phải hai như bản phác của hướng dẫn. Vì cache
> `_shared` khóa theo `key`, đổi lẻ vài chỗ là database dùng chung phụ thuộc thứ tự xUnit → đỏ ngẫu nhiên. Xử lý: **đổi
> cả bốn** sang hàm mới, và cache khóa theo `<hàm>:<key>` để trộn hai hàm cùng key không bao giờ thành một database.
> Đổi một dòng gọi ở các file test đó **không** tính là "sửa khung" — khung là hình dạng `AuthZCase` và khẳng định của
> `Ma_tran_phan_quyen`, không phải nguồn dữ liệu.

### B2 — Sáu dòng AuthZ matrix (Mục 6.3)

Chỉ sửa `AuthZMatrix.cs`. `TC-A03`/`TC-A03-delete` dùng `ArrangePath` tạo bài của B qua API thật (không INSERT thẳng
DB — INSERT thẳng thì test không đi qua đúng đường mà người dùng đi).

### B3 — Viết cho đỏ trước, rồi mới có D7/D8

Đúng nếp `B3` của GĐ1: thêm dòng matrix **trước** khi viết kiểm ownership, thấy đỏ, rồi mới viết `PATCH`/`DELETE`.
Ghi lại bảng đột biến: bỏ `post.AuthorId != actorId` → `TC-A03` đỏ; đổi 403 thành 404 → đỏ; bỏ kiểm tiền tố key →
`TC-A03-media` đỏ.

> **Chốt 2026-09-19 (`Q-B3`):** `B3` nhận thêm bốn test BR-01 **mức integration** đi qua `POST /posts` — `AC-02`, `AC-03`,
> `BR01-05`, `BR01-06` của Mục 10.1. Unit test BR-01 dạng hàm thuần vẫn thuộc `A4` (đã xong); phần còn lại của Mục 10.1
> đi cùng endpoint sinh ra chúng (khối D). `BR01-05` còn canh thứ mà `D5` không tự canh được: HEAD đứng **trước** transaction.

### B4 — Hai `ContractTests` mới + hai dòng trong csproj

Chép `IdentityContractTests` cho từng module (nó đã được viết theo tham số hóa được: `BasePath`, `ContractPath`,
tên nhóm). Thử cho đỏ một lần: thêm một status code vào controller mà không sửa yaml → cổng `API contract` phải đỏ.

### B5 — Mở rộng cổng CI (Mục 10.4 điểm 4) và thử cho đỏ

Cổng codegen kiểm thêm hai file `schema.d.ts`; cổng bundle grep thêm `R2__` và `X-Amz-Signature`. Theo luật frontend
Mục 9: thêm cổng thì **phải thử cho đỏ một lần rồi khôi phục**, `git status` sạch trước và sau.

---

## B.5 Khối C — Lưu trữ đối tượng (R2)

> **Mục tiêu khối:** đóng rủi ro ISS-02 sớm nhất có thể, và để lại một interface mà GĐ5 dùng lại được cho media
> tin nhắn mà không phải sửa gì.

> **Hướng dẫn thi công từng bước:** [huong-dan-khoi-b-c-test-va-luu-tru.md](huong-dan-khoi-b-c-test-va-luu-tru.md)
> Phần I — mục tiêu và kết quả mong đợi của từng đầu việc, file nào, lệnh nào, cạm bẫy nào, checklist nghiệm thu.
> Mục B.5 dưới đây giữ nguyên vai trò "cái gì và vì sao".

### C1 — `IObjectStorage` + `R2Options` ở SharedKernel

Bề mặt tối thiểu, không hơn: `CreatePresignedPut`, `CreatePresignedGet`, `HeadAsync`, `DeleteAsync`, `ListAsync`.
`R2Options` fail-fast như `JwtOptions` của GĐ1: ngoài Development thiếu `R2__Endpoint|Bucket|AccessKey|SecretKey`
thì **app từ chối khởi động**, thông báo nêu đúng tên biến và đúng chỗ sửa.

> **Chốt 2026-09-19 (`Q-C1`):** fail-fast **chỉ ngoài Development**, chép nguyên khuôn cấu hình email của GĐ1. Lý do:
> `ApiFactory` (smoke + **cổng hợp đồng API**) chạy Development và cố ý không có khóa R2; fail-fast ở mọi môi trường là cổng
> hợp đồng đỏ vì lý do không liên quan hợp đồng. Ở Development thiếu khóa thì app khởi động, lời gọi `IObjectStorage`
> đầu tiên mới ném với thông điệp nêu bốn tên biến và lệnh `user-secrets`. `StartupConfigurationTests` có thêm hai
> khẳng định: Staging thiếu từng key → ném nêu tên; `ApiFactory` khởi động được **không** có `R2__*`.

### C2 — `R2ObjectStorage` + kiểm chứng presign PUT bằng tay

Hiện thực bằng `AWSSDK.S3` trỏ vào endpoint R2 (`ForcePathStyle` theo yêu cầu của R2). Ký kèm `Content-Type` và
`Content-Length` (Đ-2.8).

**Nghiệm thu C2 không phải bằng test**: dựng một trang tạm hay dùng luôn DevTools của FE để `PUT` một ảnh thật lên
bucket `-dev` từ **trình duyệt**. Đây là lần chạm đầu tiên với CORS — làm ở Ngày 6, không để tới F3.

> **Thi công 2026-09-19 — xong cả code lẫn nghiệm thu trình duyệt; ISS-02 đóng trên dev** (PUT 200 từ `http://localhost:3000`
> lên `socialmedia-dev`, preflight 204, `Access-Control-Allow-Origin` đúng, `X-Amz-SignedHeaders=content-length;content-type;host`).
> Trang probe phục vụ bằng server tĩnh trần vì CSP của `proxy.ts` chưa mở `connect-src` cho R2 — đó là việc `E7`/`Đ-E17`, và
> là thứ `F3` phải kiểm lại trên staging qua chính app. Hai điều lộ ra khi làm: (1) `AWSSDK.S3`
> **v4** mặc định `RequestChecksumCalculation`/`ResponseChecksumValidation = WHEN_SUPPORTED`, gửi thêm header checksum CRC mà
> R2 không hiểu và lỗi trả về không nói gì về checksum — phải đặt cả hai về `WHEN_REQUIRED`; (2) sinh key (`posts/{userId}/…`,
> allowlist, kiểm tiền tố cho `TC-A03-media`) đặt ở `SharedKernel/Storage/StorageKeys.cs` vì cả Profile lẫn Content dùng;
> allowlist ở đó trùng `MediaAttachment.AllowedContentTypes` và có unit test canh hai danh sách không lệch. Unit test đã
> khẳng định URL PUT có `content-length` + `content-type` trong `X-Amz-SignedHeaders`, hạn 600 s; GET 900 s, chỉ ký `host`.

### C3 — Kiểm lúc commit: `HeadAsync` + đối chiếu khai báo

Hàm thuần nhận (khai báo, kết quả HEAD) → `Result`, để unit test được không cần mạng. Lệch size, lệch content type,
object không tồn tại: ba nhánh, ba thông điệp, cùng một mã 400.

### C4 — Worker dọn rác + khóa Redis (Đ-2.13)

`IHostedService` trong `Content.Infrastructure`, đăng ký trong `AddContentModule`. Có công tắc cấu hình để tắt
(`Media:Cleanup:Enabled`) — test và môi trường dev không cần nó chạy nền.

> **Chốt 2026-09-19 (`Q-C2`):** công tắc mặc định **tắt**; staging bật tường minh bằng `Media__Cleanup__Enabled=true`
> trong `deploy/.env` (key này phải có mặt trong mục "Trước khi merge" của PR khối C). Vì worker đăng ký trong
> `AddContentModule` nên nó chạy trong **mọi** host kể cả `WebApplicationFactory` của test — mặc định bật là gọi R2 thật
> từ CI không có khóa, hoặc xóa object trong lúc test khác đang dùng. "Quên bật trên staging" nhìn thấy được (bucket tích
> rác); "quên tắt trên CI" thì không.

### C5 — `FakeObjectStorage` cho test

In-memory, cài cùng interface, cho phép test dựng sẵn kết quả HEAD. Đăng ký trong `ConfigureTestServices`, **không**
đặt `#if DEBUG` trong code sản phẩm.

---

## B.6 Khối D — Endpoint

> **Mục tiêu khối:** hợp đồng ở Mục 8 thành hệ thống chạy thật, khớp từng mã lỗi, và mỗi endpoint chạm tài nguyên có
> chủ đều có dòng matrix.

> **Hướng dẫn thi công từng bước:** [huong-dan-khoi-d-endpoint.md](huong-dan-khoi-d-endpoint.md)
> — mục tiêu và kết quả mong đợi của từng đầu việc `D0`–`D9`, file nào, lệnh nào, cạm bẫy nào, checklist nghiệm thu.
> Mục 1.4 của file đó liệt kê tám điểm (`Q-D2`–`Q-D9`) mà Phần A chưa nói đủ hoặc nói lệch nhau (casing enum, ngữ nghĩa
> `bio`, 400 kèm `errors` từ service, kiểm `post.create` theo `purpose`, keyset, mã 400 của `DELETE /posts`, tác giả vắng
> mặt, avatar khi chưa có hồ sơ) — chốt ở đầu khối, ghi ngược vào đây khi chốt. Mục B.6 dưới đây giữ nguyên vai trò
> "cái gì và vì sao".

### D0 — Nền chung của hai module

`ProfileApiGroup` / `ContentApiGroup`; controller khai `[ApiExplorerSettings(GroupName = …)]` ngay từ file đầu tiên
(thiếu là endpoint biến mất khỏi Swagger, im lặng); `[ProducesResponseType]` đủ mã theo hợp đồng; validator
FluentValidation đăng ký trong `Add<Module>Module` (auto-validation đã bật **một lần** ở host — module không gọi lại);
DTO request **không** dùng từ khóa `required` của C#.

### D1 — `GET /users/{userId}/profile`

Endpoint đọc đầu tiên. 404 khi chưa onboarding — và đây chính là tín hiệu FE dùng để chuyển sang `/onboarding`.

### D2 — `PUT /users/me/profile` (upsert)

Validator: `displayName` 2–50 ký tự sau khi `Trim`, không rỗng; `bio` ≤ 500. Trả `ProfileResponse` để FE không phải
gọi lại `GET`.

### D3 — `PUT` + `DELETE /users/me/avatar`

Nhận `mediaKey`; kiểm tiền tố `avatars/{actorId}/` (Đ-2.7) và `HeadAsync` xác nhận object có thật + đúng loại.
`DELETE` chỉ gỡ liên kết (`avatar_key = NULL`), object để worker dọn.

### D4 — `POST /media/uploads` (presign theo lô, Đ-2.15)

Hai mức quyền theo `purpose` (Đ-2.6). Giới hạn 10 file/lô. Trả `requiredHeaders` — thiếu nó thì FE `PUT` sai header
đã ký và nhận 403 từ R2 với thông báo không nói lên điều gì.

**Không bao giờ log `uploadUrl`.** Nó mang chữ ký; log nó là để lại một URL ghi được vào bucket trong hệ thống log.

### D5 — `POST /posts`

Đường dài nhất của giai đoạn, theo đúng bước 6–7 của SEQ-01: kiểm hồ sơ → kiểm tiền tố key → HEAD từng object →
một transaction INSERT post + media với `media_count` đúng ngay từ đầu. `DbUpdateException` do đụng `storage_key`
UNIQUE dịch thành **409**, không để rơi thành 500.

### D6 — `GET /posts/{postId}` + `GET /users/{userId}/posts`

BR-02 lúc đọc (Mục 7.4) và cursor keyset (Đ-2.11). Điểm dễ sai: lọc BR-02 phải nằm **trong câu truy vấn** của endpoint
danh sách, không phải lọc sau khi đã lấy 20 dòng — lọc sau thì trang trả về ít hơn `limit` một cách ngẫu nhiên và
FE tưởng đã hết dữ liệu.

`IUserDirectory.GetManyAsync` gọi **một lần** cho cả trang (Đ-2.3).

### D7 — `PATCH /posts/{postId}`

Tầng 3 theo khuôn Mục 6.2. Chỉ `body` và `privacy`; đóng dấu `edited_at`. `UnmappedMemberHandling = Disallow` đã bật
ở host nên client gửi `mediaKeys` vào đây sẽ nhận 400 — đúng ý, vì GĐ2 không cho sửa ảnh.

### D8 — `DELETE /posts/{postId}`

Xóa mềm; 204. Gọi lại lần hai trên bài đã xóa → **403** (cùng phản hồi với "không phải của bạn"), không phải 404 —
theo bảng ở Mục 6.1, thao tác ghi cần ownership dùng 403.

### D9 — Rà RFC 7807 cho cả hai nhóm + `[ApiExplorerSettings]` cho mọi controller mới

Giống `D9`/`D10` của GĐ1: đối chiếu từng mã lỗi trong hợp đồng với thứ code thật sự trả, kiểm `title`/`type` mặc
định có đúng bảng `ProblemTitles`, và không một thông điệp nào chứa id, email hay tên kiểu.

---

## B.7 Khối E — Lane frontend

> **Mục tiêu khối:** lát cắt dọc chạm tới người dùng thật, và chứng minh CORS bằng trình duyệt — thứ backend không tự
> chứng minh được.

> **Hướng dẫn thi công từng bước:** [huong-dan-khoi-e-f-frontend-va-cong-dong.md](huong-dan-khoi-e-f-frontend-va-cong-dong.md)
> — mục tiêu và kết quả mong đợi của từng đầu việc `E1`–`E8` và `F1`–`F5`, file nào, lệnh nào, cạm bẫy nào, checklist
> nghiệm thu. Gộp khối E và khối F vào một file vì tới lúc chúng bắt đầu thì A–D đã xong, không còn lane để chạy song
> song. Mục 1.4 của file đó liệt kê tám điểm (`Q-E1`–`Q-E8`) mà Phần B chưa nói đủ hoặc nói lệch nhau (host R2 vào CSP
> lấy từ biến nào, chỗ đặt `/onboarding`, `XMLHttpRequest` trong luật ESLint, số ngữ cảnh lỗi, kit cần thêm, bản đồ
> route, nội dung thật của `F2`, Playwright có vào CI không).

Luật đặt file theo `frontend-rules.md` Mục 2: màn nào thì `features/<màn>/`. Tên theo **màn**, không theo module
backend: `features/profile/`, `features/post/`. Không `features/` nào import chéo `features/` khác.

### E1 — Codegen + api client + mở rộng `request()`

**Không thêm script `gen:api:*` nào** — `pnpm gen:api` tự sinh `lib/api/profile/` và `lib/api/content/` ngay khi hai file
`.yaml` được commit ở cổng mở (Mục 8.3, đổi 2026-09-19). `E1` chỉ còn: kiểu lấy từ file sinh (**không khai lại tay**);
`RequestOptions.method` thêm `PUT | PATCH | DELETE`; `errorMessage` thêm ngữ cảnh `profile`, `post`, `upload`.

Mọi lời gọi đi qua proxy chung `/bff/api/...` — **không thêm route BFF nào** (Đ-E14, luật frontend Mục 4).

> **Lệch B.7 bản gốc (Q-E4, chốt 2026-09-20, trước khi thi công `E1`):** `errorMessage` nhận **bảy** ngữ cảnh —
> `profile-read`, `profile-write`, `avatar`, `upload`, `post-create`, `post-read`, `post-write` — không phải ba
> (`profile`, `post`, `upload`) như câu trên. Lý do: bảng ánh xạ theo `(ngữ cảnh, status)`, mà **403 mang nghĩa khác
> nhau trên các endpoint của cùng một module** — `POST /posts` 403 là "chưa có hồ sơ hoặc thiếu quyền đăng bài",
> `PATCH /posts/{id}` 403 là "không phải bài của bạn", `PUT /users/me/avatar` 403 là "khóa ảnh không phải của bạn".
> Gộp ba endpoint vào một ngữ cảnh thì hoặc mất hai câu, hoặc phải đoán nghĩa 403 ở chỗ hiển thị — đúng thứ Đ-E6
> sinh ra để tránh.

### E2 — Onboarding hồ sơ

Guard: `(app)` layout gọi `GET /users/{me}/profile`; 404 → chuyển `/onboarding`, và **không cho bỏ qua**. Trạng thái
`unknown` hiện khung chờ, không nháy nội dung rồi mới chuyển (cùng luật với `RequireAuth` của GĐ1).

### E3 — Avatar

Chọn ảnh → presign (`purpose=avatar`) → `PUT` R2 → `PUT /users/me/avatar`. Ba bước, ba trạng thái lỗi riêng: lỗi ở
bước 2 là lỗi mạng/CORS, lỗi ở bước 3 là hợp đồng — gộp chúng thành một câu "tải ảnh thất bại" là tự bịt đường sửa.

### E4 — Composer đăng bài + upload thật có tiến trình

Kiểm BR-01 phía client **cùng ngưỡng với server**, không chặt hơn (Đ-E5). Upload bằng `XMLHttpRequest` để có tiến
trình; mỗi ảnh một dòng trạng thái, ảnh lỗi thì thử lại **riêng ảnh đó**. Upload song song tối đa 3 để không làm
nghẽn đường lên của người dùng.

Đây là chỗ **duy nhất** trong code trình duyệt được phép gọi ra ngoài origin — viết thành module riêng
`lib/upload/r2.ts` với `eslint-disable` có chú thích trỏ tới Đ-E17, đúng cách `lib/api/http.ts` đang làm với `fetch`.

### E5 — Danh sách + chi tiết bài

Cuộn theo `nextCursor`; skeleton; trạng thái rỗng cho tài khoản mới. Cursor là chuỗi mờ — FE **không** tự dựng, chỉ
truyền lại thứ server trả.

### E6 — Sửa / xóa bài của mình

Hiện nút theo `canEdit` của server (Mục 8.2), không tự so id. 403 hiển thị một câu **không tiết lộ** bài có tồn tại
hay không.

### E7 — Nới CSP cho R2 + ghi **Đ-E17**

`connect-src` và `img-src` thêm host R2 trong `lib/security/csp.ts`; test Vitest cho từng chỉ thị (file đã có sẵn
khuôn). Ghi Đ-E17 vào `huong-dan-khoi-e-frontend.md` **trong cùng commit** — luật frontend Mục 11 #4.

### E8 — Vitest + Playwright cho lát cắt mới

Theo Mục 10.5. Playwright chạy local, `workers: 1`, kết quả dán vào PR kèm bản Chrome đã chạy (Đ-E8).

---

## B.8 Khối F — Cổng đóng

> **Mục tiêu khối:** chứng minh trên hệ thống thật, không phải trên máy local và không phải trên mock.

> **Hướng dẫn thi công từng bước:** [huong-dan-khoi-e-f-frontend-va-cong-dong.md](huong-dan-khoi-e-f-frontend-va-cong-dong.md)
> Mục 10–14 — cùng file với khối E.

### F1 — Deploy staging qua CD tự động

Trước khi merge: `R2__*` đã có trong `.env` trên server. Sau deploy: service `migrate` chạy xanh cho **cả ba** module;
`/health/ready` = 200.

> **Bổ sung B.8 bản gốc (Q-E1, ĐẢO lại 2026-09-20 sau khi thi công `E7`):** **KHÔNG có biến mới nào** cho `.env` trên
> server. Frontend dựng CSP cho R2 từ **chính `R2__Endpoint`** mà API đã dùng — container frontend thấy nó sẵn nhờ
> `env_file: [./.env]`. Bản chốt đầu buổi sinh thêm `R2_PUBLIC_HOST` vì sợ cổng CI bundle (`B5`, grep chuỗi `R2__` trên
> `.next/static`) đỏ; **đã đo và cổng không đỏ** — `proxy.ts` là middleware, chuỗi đó chỉ nằm trong `.next/server`.
> Bỏ biến thứ hai là bỏ luôn cái giá "hai biến một giá trị, có thể lệch". Ràng buộc còn lại: hằng tên biến phải nằm
> trong `proxy.ts`, không trong `lib/security/csp.ts` (xem Đ-E17). Dòng kiểm ở `F1` giữ nguyên: header
> `Content-Security-Policy` của `/login` trên staging phải có host R2 — thiếu thì upload **chết trên staging** dù dev
> xanh, và `R2__Endpoint` sai dạng (có path, có `/` cuối) thì Next **từ chối phục vụ**, không chỉ chặn upload.

### F2 — Frontend trỏ staging thật, bỏ mock

`mocks/` chỉ còn phục vụ Vitest (luật frontend Mục 8). Kiểm Network: chỉ thấy `/bff/*` và các `PUT` thẳng tới R2 —
không có JWT nào, không có `storage_key` của người khác.

> **Lệch B.8 bản gốc (Q-E7, chốt 2026-09-20):** `F2` **không còn việc "bỏ mock"** — mock trình duyệt đã bị bỏ từ GĐ1
> (Đ-E7, đổi 2026-09-17): không cờ `NEXT_PUBLIC_API_MOCKING`, không `public/mockServiceWorker.js`, code app không
> import `@/mocks/*`. Giữ nguyên mã việc, đổi nội dung thành hai phần: (a) **xác nhận** bằng lệnh —
> `grep -rn "@/mocks" app features components lib` không ra dòng nào; (b) phần chính là **kiểm tab Network trên
> staging** theo bốn điều ở câu trên. Để nguyên câu cũ thì `F2` giao một việc không còn tồn tại, và người tick
> checklist sẽ tick một ô rỗng.

### F3 — E2E lát cắt + bằng chứng ISS-02

Trên trình duyệt thật, domain HTTPS thật: đăng nhập → onboarding → đăng bài 2 ảnh → xem → sửa → xóa.

Bằng chứng phải giữ: ảnh tab Network của lượt `PUT` lên R2 (thấy 200 và thấy header đã ký), ảnh dashboard R2 có
object, ảnh bài hiển thị ảnh. **Đây là lúc ISS-02 được đóng lại** — hoặc lộ ra, và khi đó dùng phương án ứng phó ở
Mục 14 ngay trong ngày, đừng để sang GĐ4.

### F4 — Checklist nghiệm thu (Mục 12) + Definition of Done (Mục 11)

Tick từng dòng, có bằng chứng kèm theo. Dòng nào không áp dụng thì ghi lý do, **không xóa dòng**.

### F5 — Đóng băng hợp đồng + bàn giao

Thông báo `profile-v1.yaml` và `content-v1.yaml` đóng băng cho phạm vi GĐ2; liệt kê phần **hoãn có địa chỉ** (Mục 2)
để không có nợ vô chủ; xác nhận ba điều kiện ở B.11 → mở GĐ4.

---

## B.9 Thứ tự thực thi và đường găng

**Đường găng:** `A4 → A5 → A6` · `C1 → C2 → C3` · `D4 → D5 → D6` · `F1 → F2 → F3 → F5`

Khối **B** và **E** không nằm trên đường găng — nên chúng phải chạy **hết công suất song song ngay từ giờ đầu**,
đúng cách GĐ1 hấp thụ việc backend chỉ có hai người.

`C2` (presign chạy thật trên trình duyệt) phải xong **trong Ngày 6**. Đây là đầu việc rủi ro nhất của giai đoạn và là
đầu việc duy nhất có thể buộc cả nhóm đổi phương án (ứng phó ISS-02) — biết muộn một ngày là mất một ngày.

**Bốn chỗ dễ mất dứt điểm nhất — kiểm riêng, đừng tin là mặc nhiên:**

| Nguy cơ | Việc canh |
|---|---|
| Cổng CI mới xanh giả với 0 test | `B4`, `B5` — thử gõ sai một lần và thấy CI đỏ |
| Matrix xanh dù kiểm ownership hỏng | `B3` — bảng đột biến, thử cho đỏ ba lần |
| Nghiệm thu upload trên `curl` thay vì trình duyệt | `C2` và `F3` — `curl` không bao giờ thấy CORS |
| Ranh giới module bị phá bằng SQL thô | Code review: ArchUnitNET không đọc được chuỗi SQL |

**Năm thứ không test tự động nào bắt được — bắt buộc code review:**

1. `actorId` lấy từ `User.GetUserId()`, **không** từ route/body (Mục 6.2). Test không phân biệt được nguồn.
2. **HEAD đứng trước transaction** ở `D5`. Đảo lại thì có bài rồi mới phát hiện ảnh sai, và phải rollback thủ công.
3. Không có nhánh `if role == ADMIN` ở tầng 3 — Admin short-circuit **chỉ** ở tầng 2 (luật kế thừa từ GĐ1 Mục 3.2).
4. **Không log `uploadUrl`, không log presigned GET.** Grep review từng PR khối C và D.
5. Cache của GĐ4 sẽ lưu `storage_key` chứ không lưu URL đã ký (Đ-2.9) — ghi sẵn vào tài liệu GĐ4 ngay khi mở giai đoạn.

---

## B.10 Mục tiêu từng khối — chúng cộng lại thành cái gì

| Khối | Mục tiêu | Thiếu nó thì mất gì |
|---|---|---|
| **A** | Hai module đứng độc lập, có schema riêng, không tham chiếu chéo | Modular monolith thành monolith; năm module sau copy đúng cái sai này |
| **B** | Luật của Phần A thành cổng chặn merge | IDOR ở module nội dung chỉ lộ ra ở GĐ8, lúc sửa đã đắt |
| **C** | Rủi ro bên thứ ba đóng sớm, interface dùng lại được cho GĐ5 | ISS-02 lộ ra ở cổng đóng, không còn ngày nào để ứng phó |
| **D** | Hợp đồng thành hệ thống chạy thật, khớp từng mã lỗi | Không có sản phẩm |
| **E** | Lát cắt dọc chạm người dùng thật + chứng minh CORS | Backend đúng nhưng không ai dùng được; và ISS-02 không có cách nghiệm thu |
| **F** | "Xong" thành sự kiện kiểm chứng được trên staging | "Xong" thành cảm giác |

## B.11 Mục tiêu của GĐ2

### Ba điều kiện để tuyên bố GĐ2 xong

Thiếu bất kỳ điều nào thì **chưa xong**, dù code đã chạy:

1. **Một bài viết có ảnh thật tồn tại trên staging**, ảnh nằm trong bucket R2, upload đi thẳng từ trình duyệt —
   không phải từ `curl`, không phải từ máy local.
2. **Sáu dòng AuthZ matrix mới xanh trên CI**, và đã từng thấy đỏ ít nhất một lần khi cố tình bỏ kiểm ownership.
3. **CI xanh cả năm nhóm**: unit + integration · AuthZ matrix · hai cổng hợp đồng · codegen FE · bundle sạch.

### GĐ2 để lại gì cho GĐ3–GĐ8

| Di sản | Ai thừa hưởng |
|---|---|
| Khuôn "module thứ hai": schema riêng, context riêng, contract chéo ở SharedKernel | SocialGraph (GĐ4), Messaging (GĐ5), Notification + Moderation (GĐ6) |
| `IObjectStorage` + presign + HEAD + worker dọn | GĐ5 (media tin nhắn) — dùng lại, không viết lại |
| `IUserDirectory` batch | GĐ4 (feed 20 bài/trang), GĐ6 (thông báo, tìm kiếm) |
| `IFriendshipReader` null-object | GĐ4 — đổi **một dòng DI**, không chạm module Content |
| Cursor keyset đã chốt + đã có test phân trang | GĐ4 (feed), GĐ5 (lịch sử hội thoại) |
| Bảng `comments`, `reactions` + hai cột bộ đếm trên `posts` | GĐ3 — không phải đổi hình dạng DTO lần thứ hai |
| Cột `hidden_reason` + trạng thái `hidden` | GĐ6 (BR-07) |
| Ba lớp kiểm dung lượng/loại ảnh | GĐ8 (security scan) — không còn đường tải file tùy ý lên hệ thống |
| Nợ có địa chỉ: dọn `profiles`/`posts` khi xóa tài khoản | GĐ8 (NĐ 13/2023) |

**Một câu để nhớ:** GĐ1 chứng minh *ai là ai*; GĐ2 là lần đầu hệ thống có **thứ thuộc về ai đó** — và đó là lúc mọi
quyết định về ranh giới, quyền sở hữu và dữ liệu bên thứ ba bắt đầu tính lãi.
