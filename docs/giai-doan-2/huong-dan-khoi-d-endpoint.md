# Hướng dẫn thực hiện — Khối D. Endpoint (GĐ2)

> Bản triển khai chi tiết của **B.6 Khối D** trong [giai-doan-2.md](giai-doan-2.md). Tài liệu gốc trả lời *cái gì*
> và *vì sao*; tài liệu này trả lời *gõ vào file nào, theo thứ tự nào, và nhìn vào đâu để biết đã xong thật*.
>
> **Nguồn sự thật vẫn là `giai-doan-2.md`** (Mục 3 quyết định `Đ-2.1`–`Đ-2.15`, Mục 6 ba tầng kiểm soát, Mục 7 luồng
> nghiệp vụ, Mục 8 hợp đồng, Mục 10.1 mã test), hai file hợp đồng
> [`profile-v1.yaml`](../../src/backend/Modules/Profile/Presentation/profile-v1.yaml) và
> [`content-v1.yaml`](../../src/backend/Modules/Content/Presentation/content-v1.yaml), và `AGENTS.md`. Chỗ nào tài
> liệu này lệch các nguồn đó thì sửa ở đây — trừ các mục `Q-D*` ở Mục 1.4 được đánh dấu **"ghi ngược"**: đó là chỗ
> tài liệu gốc đang thiếu hoặc mâu thuẫn, phải sửa `giai-doan-2.md` (hoặc file `.yaml`) **trong cùng commit** với
> code của việc đó.
>
> Khuôn để chép lại nằm ở khối D của GĐ1: [huong-dan-khoi-d-endpoint.md](../giai-doan-1/huong-dan-khoi-d-endpoint.md)
> — controller mỏng → service trả `Result` → store SQL nguyên tử; harness test theo lớp; `[ApiExplorerSettings]` ngay
> từ controller đầu tiên. GĐ2 **không phát minh khuôn mới** — làm lại đúng hình dạng đó cho hai module.

| | |
|---|---|
| **Người làm** | B.2 chia BE-1 (Profile: `D1`–`D3`) + BE-2 (Content: `D4`–`D8`). **Một người** thì đi tuần tự theo Mục 0.1 |
| **Thời lượng** | Ngày 7 (`D0`–`D4`) → Ngày 8 (`D5`–`D8`) → Ngày 9 sáng (`D9`, rồi `B4`, `B3`) |
| **Khối này chặn** | `B3` (bảng đột biến cần `D5`/`D7`/`D8`), `B4` (cần `D0`), **toàn bộ khối F**, và việc lane E bỏ mock ở dev |
| **Khối này cần trước** | `A3` + `A5` + `A6` (**đã xong**), `C1`–`C5` (**đã xong**), hai file `.yaml` của cổng mở (**đã commit**, `084982b`), và **`B2` đã push + đã thấy run đỏ** trước khi gõ `D5` (nếp `B3`) |
| **Không thuộc khối này** | Sáu dòng matrix (`B2`), bảng đột biến + bốn test BR-01 (`B3`), `ContractTests` (`B4`), presign/HEAD/worker (khối C, xong), nới CSP (`E7`), nghiệm thu trên trình duyệt (`F3`). Xem Mục 15 |

**Trạng thái lúc viết (2026-09-19):** khối A, C, `B1`, `B5`, cổng mở xong; `Q-D1` (hình dạng `mediaKeys`) đã chốt và ghi
vào Mục 8.2. Hai module **chưa có** file nào trong `Application/` và `Presentation/*.cs` — khối D bắt đầu từ trang trắng
đúng nghĩa, chỉ có hai file `.yaml` và hai `.gitkeep`. Chưa có test nào của Mục 10.1 tồn tại.

---

## 0. Danh sách công việc — mục tiêu và kết quả mong đợi

Mười đầu việc, `D0`–`D9`. `D0` là nền chung; `D1`–`D3` là module Profile (4 endpoint); `D4`–`D8` là module Content
(6 endpoint); `D9` là rà soát cuối. Mọi "kết quả mong đợi" dưới đây là thứ **chạy được hoặc nhìn thấy được**, không phải
cảm giác. Mã test (`PROF-01`, `AC-01`, `PAGE-02`…) lấy nguyên từ Mục 10.1 của `giai-doan-2.md`; bốn test `AC-02`,
`AC-03`, `BR01-05`, `BR01-06` thuộc `B3` (chốt `Q-B3`), không lặp lại ở đây.

| Mã | Đầu việc | Mục tiêu — việc này tồn tại để làm gì | Kết quả mong đợi — thứ kiểm chứng được |
|---|---|---|---|
| **D0** | Nền chung của hai module: `ProfileApiGroup` / `ContentApiGroup`, nạp hai assembly + hai `SwaggerDoc` vào `Program.cs`, FluentValidation + `AddValidatorsFromAssembly` trong `Add<Module>Module`, `ProfileErrors` / `ContentErrors`, `Error.Validation` (Q-D4), enum chữ thường qua JSON (Q-D2), harness `ModulesApiFactory` | Mười endpoint dùng chung **một** bộ gạch — một cách trả 400 có `errors` từ service, một cách trả enum, một factory test — thay vì mỗi endpoint tự chế một kiểu rồi lệch nhau ở D9 | `dotnet build SocialApp.sln` xanh; `/swagger/profile-v1/swagger.json` và `/swagger/content-v1/swagger.json` trả **200** (paths rỗng); `PresentationBoundaryTests` + `PermissionCodeUsageTests` xanh; unit test mới của `ResultTests`: `Error.Validation("x", "m")` qua `ToActionResult` ra **400** có `errors.x`; test khung trong `Profile/ProfileHarnessTests`: token USER gọi `PUT /api/v1/users/me/profile` nhận **404** (chưa có controller), ẩn danh nhận **401**; chạy `ContractTestsBase` của `B4` **ở local** thấy chiều 2 đỏ liệt kê **đủ 10 operation** |
| **D1** | `GET /users/{userId}/profile` | Endpoint đọc đầu tiên, và là **tín hiệu onboarding** của FE: 404 = chưa có hồ sơ → `/onboarding` (Đ-2.4, Mục 7.1) | `PROF-03` xanh: 404 `title` "Không tìm thấy tài nguyên", `detail` **không chứa** `userId`; `userId` không phải UUID → **400** `errors.userId`; sau `D2`: 200 đúng hình dạng `ProfileResponse`, `avatarUrl: null` khi chưa đặt avatar; `bio` vắng → `null` |
| **D2** | `PUT /users/me/profile` (upsert) | Bước onboarding của Đ-2.4 — cùng một mã 200 cho tạo lẫn sửa, FE không cần biết trạng thái server; trả `ProfileResponse` để FE không gọi lại `GET` | `PROF-01`, `PROF-02` xanh (`PROF-02`: vẫn **một** dòng, `updated_at` đổi, `created_at` giữ nguyên); `displayName` 1 ký tự / 51 ký tự / toàn khoảng trắng / `"  An  "` → **400** `errors.displayName` "Tên hiển thị phải có từ 2 đến 50 ký tự."; `bio` 501 ký tự → `errors.bio`; field lạ → 400; **hai `PUT` lần đầu song song → cả hai 200, một dòng** (`ON CONFLICT`); `TC-A01-profile` (B2) xanh |
| **D3** | `PUT` + `DELETE /users/me/avatar` | Đóng luồng avatar của E3 với đủ ba lớp của Đ-2.7/Đ-2.8: tiền tố người gọi → `HEAD` object có thật → đúng loại; `DELETE` chỉ gỡ liên kết (Đ-2.10) | `PROF-04` xanh (key `avatars/{id người khác}/…` → **403**, cùng phản hồi `Error.Forbidden`); key sai dạng → **400** `errors.mediaKey`; object **chưa `Put`** vào fake → 400 "Ảnh chưa được tải lên xong…"; object có nhưng `text/html` → 400; hợp lệ → 200, `avatarUrl` bắt đầu `https://fake.invalid/get/avatars/`; `DELETE` → 204, `GET` thấy `avatarUrl: null`; `DELETE` lần hai → 204; **`fake.Deleted` rỗng** (không xóa object trong request) |
| **D4** | `POST /media/uploads` (presign theo lô, Đ-2.15) | Một request cho tối đa 10 file; hai mức quyền theo `purpose` (Đ-2.6); trả `requiredHeaders` để FE `PUT` đúng header đã ký; **không log `uploadUrl`** | 201 mảng **cùng thứ tự** với `files`; `mediaKey` khớp `^posts/{actorId}/[0-9a-f]{32}\.(jpg\|png\|webp)$`; `expiresIn == 600`; `requiredHeaders` có đúng hai key `Content-Type`, `Content-Length`; `purpose=post` với token role **không có `post.create`** (vd `TestJwt.Create("GUEST")`) → **403**, cùng token `purpose=avatar` → 201; 11 file / `image/gif` / `sizeBytes` 0 / 10485761 → **400** `errors.files`; `purpose` lạ → 400 `errors.purpose`; `CapturingLogSink` **không** có dòng nào chứa `fake.invalid/put` |
| **D5** | `POST /posts` | Đường dài nhất của giai đoạn (SEQ-01 bước 6–7): hồ sơ → tiền tố key → BR-01 → HEAD **trước** transaction → một transaction INSERT post + media với `media_count` đúng ngay từ đầu; UNIQUE `storage_key` → **409** | `AC-01` xanh: 201, DB `media_count = 1`, `media[0].url` ký, `canEdit: true`, `author.displayName` đúng, `reactionCounts: {}`, `commentCount: 0`; `AC-04` (token hết hạn) → 401; **chưa có hồ sơ → 403**; `POST-08` → 409 và **số dòng `posts` không tăng**; `fake.HeadCalls` tăng **đúng bằng** số key; hai key trùng nhau trong một request → 400 `errors.mediaKeys`; thiếu `privacy` → 400 `errors.privacy` (Q-D2); `TC-A03-media` (B2) xanh; `BR01-05`/`BR01-06` (B3) xanh khi tới lượt |
| **D6** | `GET /posts/{postId}` + `GET /users/{userId}/posts` | BR-02 **tại thời điểm đọc** (Mục 7.4) và cursor keyset (Đ-2.11) — hình dạng mà GĐ4 dùng lại cho feed; `IUserDirectory` gọi **một lần** cho cả trang (Đ-2.3) | `READ-02..05` xanh theo ma trận Mục 7.4 (`friends` với người lạ → 404, với tác giả → 200); `READ-01` (B2) xanh; `PAGE-01` (25 bài, `limit=20` → 20 + `nextCursor`; trang 2 → 5 + `nextCursor: null`), `PAGE-02` (`cursor=abc` → 400 `errors.cursor`), `PAGE-03` (chèn bài giữa hai lần gọi: không nhân đôi, không nhảy cóc) xanh; `limit=0`/`51`/`abc` → 400 `errors.limit`; cursor mang offset `+07:00` → **400, không 500**; user không tồn tại → `items: []`, `nextCursor: null`; log EF của một trang có **đúng một** câu `SELECT … profile.profiles` |
| **D7** | `PATCH /posts/{postId}` | Sửa `body`/`privacy` của bài mình theo khuôn tầng 3 Mục 6.2; đóng dấu `edited_at`; **không** sửa ảnh ở GĐ2 | `POST-06` xanh (`editedAt` khác null, `updatedAt` đổi); body `{}` → 400; gửi `mediaKeys` → 400 (field lạ, `UnmappedMemberHandling`); bài không ảnh mà `body: ""` → 400 `errors.body` "Bài đăng phải có nội dung hoặc ít nhất một ảnh."; bài đã xóa mềm → **403**; `TC-A03` (B2) xanh |
| **D8** | `DELETE /posts/{postId}` | Xóa mềm (Đ-2.10); sau đó chính tác giả `GET` cũng 404; object R2 để worker dọn | `POST-07` xanh; `DELETE` lần hai → **403** (không 404); DB: `status = 'deleted'`, `deleted_at` khác null, dòng `media_attachments` **còn nguyên**, `fake.Deleted` rỗng; `GET /users/{tác giả}/posts` không còn bài đó; `TC-A03-delete` (B2) xanh |
| **D9** | Rà RFC 7807 + `[ProducesResponseType]` + `[ApiExplorerSettings]` cho cả hai nhóm | Một hình dạng lỗi cho mười endpoint, và làm cho cổng hợp đồng chiều 2 (`B4`) xanh được — Swagger chỉ thấy mã nào action khai | Bảng mã ở Mục 11 khớp từng action, **không khai thừa**; `ContractTestsBase` chạy local **xanh cả hai chiều** cho `profile-v1` và `content-v1`; bảng rà thông điệp trong PR: không `Error` nào chứa id, key, email, tên kiểu; `ProblemDetailsTests` thêm case 400 sinh từ `Error.Validation` có `title` "Dữ liệu không hợp lệ" + `errors` |

> **Lệch B.6 (không cần họp):** B.6 mô tả `D0` là "`ProfileApiGroup`/`ContentApiGroup` + `[ApiExplorerSettings]` +
> `[ProducesResponseType]` + validator + DTO không `required`". Ở đây `D0` **ôm thêm** harness test và hai viên gạch
> dùng chung (`Error.Validation`, enum chữ thường) — cùng lý do GĐ1 tách `D0`: cả mười endpoint đứng trên chúng, dựng dần
> trong lúc làm `D3`/`D5` là mỗi endpoint một kiểu. `[ProducesResponseType]` **không** làm ở `D0` mà làm cùng từng
> controller (không có action nào ở `D0` để khai), và `D9` rà lại.

### 0.1 Thứ tự thực thi

```
 B2 (sáu dòng matrix — đã push, ĐÃ THẤY run đỏ) ──────────────────────────┐
                                                                          ▼
 D0 ─→ D1 ─→ D2 ─→ D3 ─→ D4 ─→ D5 ─→ D6 ─→ D7 ─→ D8 ─→ D9 ─→ [B4] ─→ [B3]
  │                                                    ▲
  └── viết ContractTestsBase (B4) ở LOCAL ngay sau D0, chạy sau mỗi endpoint,
      nhưng CHỈ COMMIT sau D9 (chiều 2 đỏ tới lúc đủ 10 operation; repo không nhận Skip mới)
```

Bốn phụ thuộc **thật**, không phải sở thích sắp xếp:

- **`B2` trước `D5`/`D7`/`D8`, và phải thấy đỏ.** Đó là toàn bộ nội dung của `B3` bước 1 (hướng dẫn B/C Mục 9): dòng
  matrix có trước, code ownership có sau, để chứng minh dòng bắt được lỗi thật. `D0`–`D4` không chạm endpoint nào của
  matrix nên làm được **trước hoặc sau** `B2` — nhưng `B2` **phải** đã push và run đỏ đã chụp trước khi gõ dòng đầu
  của `D5`. Push `D5` sớm là `cancel-in-progress` hủy run đỏ và mất bằng chứng.
- **`D0` trước tất cả.** `Program.cs` không nạp assembly thì controller viết xong vẫn 404; `Error.Validation` không có
  thì `D3`/`D5`/`D7` mỗi chỗ trả 400 một kiểu.
- **`D2` trước `D3`, `D5`.** Avatar gắn vào dòng `profiles` có thật; `POST /posts` từ chối người chưa có hồ sơ (Đ-2.4).
  Không có `D2` thì test của hai việc đó không dựng được dữ liệu qua API thật.
- **`D5` trước `D6`–`D8`.** Test đọc/sửa/xóa đều tạo bài **qua `POST /posts`** (không INSERT thẳng DB — cùng luật với
  `ArrangePath` của `B2`), và `D7`/`D8` nghiệm thu bằng cách `GET` lại.

Hai chỗ **không** phải phụ thuộc, đừng xếp hàng cho gọn:

- `D4` không chặn `D5`: test `D5` dựng key bằng `StorageKeys.ForPost(actorId, contentType)` + `fake.Put(...)`, không
  cần đi qua `/media/uploads`. Xếp `D4` trước chỉ vì nó ngắn và là chỗ đầu tiên chạm `IObjectStorage` trong module.
- `D1` và `D2` đổi chỗ được — nhưng `D1` trước cho rẻ: một action, không body, không validator, đủ để biết
  `Program.cs`/`ApiGroup`/route của `D0` đúng trước khi viết thứ dài hơn.

### 0.2 Đường đi khi một người làm cả khối

| # | Việc | Cần trước | Ghi chú |
|---|---|---|---|
| 0 | Chốt `Q-D2`–`Q-D9` (Mục 1.4), ghi ngược chỗ nào cần | — | Một người thì "nhóm chốt" = bạn chốt; vẫn ghi, vì đó là thứ người đọc sau lật lại |
| 1 | `D0` | — | Commit riêng. Sau đó viết `ContractTestsBase` + hai lớp con của `B4` **ở local**, không commit |
| 2 | `D1` → `D2` → `D3` | `D0` | Module Profile xong. Chạy `B4` local: chiều 2 còn thiếu đúng 6 operation của Content |
| 3 | `D4` | `D0` | Chỗ đầu tiên chạm `IObjectStorage` từ module; kiểm `UnconfiguredObjectStorage` không nổ lúc khởi động |
| 4 | **Kiểm `B2` đã push và run đỏ đã chụp** | `B2` | Chưa có thì làm `B2` **ngay bây giờ**, chờ CI xong, rồi mới sang #5 |
| 5 | `D5` | `D2`, `D4`, #4 | `TC-A03-media` chuyển đỏ → xanh ở đây |
| 6 | `D6` | `D5` | `READ-01` chuyển đỏ → xanh |
| 7 | `D7` → `D8` | `D6` | `TC-A03`, `TC-A03-delete` chuyển đỏ → xanh; 19 dòng matrix xanh |
| 8 | `D9` | tất cả | `B4` local xanh cả hai chiều → **commit `B4`** ngay sau |
| 9 | `B3` | `D5`, `D7`, `D8` | Bảng đột biến + bốn test BR-01 — kết thúc khối B lẫn D |

### 0.3 Mốc để đo tiến độ

| Mốc | Xong cái gì | Mở khóa gì |
|---|---|---|
| **Cuối Ngày 7** | `D0`–`D4` | Lane E bỏ mock được cho onboarding + avatar (`E2`, `E3`) ở dev; `B4` local báo thiếu đúng 5 operation của `posts` |
| **Cuối Ngày 8** | `D5`–`D8`, 19 dòng matrix xanh | `E4`–`E6` chạy trên API thật; `B3` bắt đầu được |
| **Trưa Ngày 9** | `D9` + `B4` commit + `B3` | Năm cổng CI xanh; khối F bắt đầu |

### 0.4 Phần cắt được nếu trễ

**Gần như không có.** Cả mười endpoint đều có một màn của lane E đứng lên (`E2`–`E6`), và cắt endpoint nào là màn đó
trở về mock — đúng thứ cổng đóng cấm. Ba thứ cắt được mà không đổi hợp đồng:

| Cắt được | Cái giá | Cắt **không** được |
|---|---|---|
| `PAGE-03` (chèn bài giữa hai lần gọi) | Tính đúng của keyset chỉ còn được chứng minh bằng lý luận, chưa bằng test | `PAGE-01`, `PAGE-02` |
| Đếm câu SQL `profile.profiles` trong test `D6` | N+1 khi lấy tác giả chỉ lộ ra ở k6 của GĐ4 (`PERF-01`) | `IUserDirectory.GetManyAsync` gọi một lần — code review vẫn phải nhìn |
| Test "hai `PUT` song song" của `D2` | Upsert bằng `ON CONFLICT` vẫn đúng, chỉ không có lưới | Bản thân `ON CONFLICT` — thay bằng đọc-rồi-ghi là 500 ngẫu nhiên khi hai tab cùng onboarding |

Cắt thì **ghi rõ vào PR và vào `giai-doan-2.md`**, không lặng lẽ bỏ.

---

## 1. Trước khi gõ dòng đầu tiên

### 1.1 Điều kiện cần

```bash
docker ps                                                   # Testcontainers cần Docker
docker compose -f deploy/docker-compose.dev.yml up -d       # Postgres + Redis cho kiểm tay bằng Swagger
dotnet build SocialApp.sln
dotnet test SocialApp.sln                                   # mốc so sánh — Unit 113, Integration 191, Arch 11 (2026-09-19)

# Khóa R2 dev nằm trong user-secrets (Mục 9.0 của giai-doan-2.md) — kiểm tay bằng Swagger cần nó; test KHÔNG cần
dotnet user-secrets list -p src/backend/SocialApp.Api | grep "^R2:" | sed 's/=.*/=<đã đặt>/'

# Hai hợp đồng có mặt và type FE đã sinh (cổng mở, 084982b)
ls src/backend/Modules/Profile/Presentation/profile-v1.yaml src/backend/Modules/Content/Presentation/content-v1.yaml
ls src/frontend/lib/api/profile/schema.d.ts src/frontend/lib/api/content/schema.d.ts
```

Đỏ sẵn từ trước thì đừng bắt đầu. Riêng khi `B2` đã push thì Integration có **6 dòng matrix đỏ** — đó là mốc đúng.

### 1.2 Điểm xuất phát — có sẵn gì, phải sửa gì

**Dùng nguyên, không viết lại:**

| Đã có | Ở đâu | Khối D dùng để làm gì |
|---|---|---|
| `Result` / `Error` / `ToActionResult`, `Error.Forbidden` | `SharedKernel/Results/`, `Http/ResultHttpExtensions.cs` | Mọi 403/404/409 của khối D. `ResultHttpExtensions` đã chép sẵn khuôn tầng 3 trong XML doc |
| `ProblemTitles`, `SharedKernelProblemDetailsFactory`, `ValidationErrors` | `SharedKernel/Errors/`, `Http/` | Title/type/`errors` tự đúng cho mọi 400/404/409; module **không** tự dựng `ProblemDetails` |
| `User.GetUserId()` | `SharedKernel/Authentication/ClaimsPrincipalExtensions.cs` | Nguồn **duy nhất** của `actorId` |
| `[RequirePermission]` + `PermissionPolicyProvider` (`perm:<mã>`) | `SharedKernel/Authorization/` | Tầng 2 của `POST/GET/PATCH/DELETE /posts`; và chính policy đó cho `purpose=post` của `D4` (Q-D5) |
| `IObjectStorage` (5 thao tác), `StorageKeys`, `R2Options.PutUrlMinutes/GetUrlMinutes` | `SharedKernel/Storage/` | Presign PUT (`D4`), presign GET cho `avatarUrl`/`media[].url` (`D1`–`D3`, `D5`–`D7`), HEAD (`D3`, `D5`), kiểm tiền tố (`D3`, `D5`) |
| `IUserDirectory` + `UserCard`, `IFriendshipReader` + `AlwaysStrangers` | `SharedKernel/Contracts/`, đăng ký ở `AddProfileModule` / `AddContentModule` | Tác giả của bài (`D5`–`D7`), **kiểm hồ sơ tồn tại** cho Đ-2.4 (`D5`), nhánh `friends` của BR-02 (`D6`) |
| `PostContentPolicy`, `MediaHeadPolicy`, `MediaDeclaration` | `Modules/Content/Domain/` | BR-01 trong validator và service; đối chiếu HEAD (`D5`) |
| `UserProfile.DisplayNameMinLength/MaxLength`, `MediaAttachment.MaxSizeBytes/AllowedContentTypes` | `Domain/` hai module | Hằng số cho validator — không gõ lại `2`, `50`, `10485760` |
| `ContentDbContext` (query filter loại `deleted`), `ProfileDbContext`, `StampUpdatedAt` | `Infrastructure/` hai module | Store của khối D. `IgnoreQueryFilters()` **cấm** ngoài `MediaCleanupWorker` |
| `FakeObjectStorage` (`Put`, `HeadCalls`, `Deleted`), `TestJwt.Create(role, userId)`, `PostgresFixture.CreateDatabaseAsync`, `FakeRemoteIpStartupFilter`, `CapturingLogSink` | `tests/SocialApp.IntegrationTests/Harness/` | Harness của khối D — chỉ ráp lại, không viết fake mới |
| `UnmappedMemberHandling = Disallow`, `JsonStringEnumConverter`, `AddFluentValidationAutoValidation` (một lần), `PropertyNameResolver` camelCase | `Program.cs` | Field lạ → 400 (kể cả `mediaKeys` ở `PATCH`); key `errors` camelCase; module **không gọi lại** auto-validation |

**Có sẵn nhưng phải sửa** (chạy impact analysis trước — Mục 1.3 luật 1):

| Chỗ | Sửa gì | Vì sao |
|---|---|---|
| `Program.cs` — `AddControllers()` | `.AddApplicationPart(typeof(ProfileApiGroup).Assembly)` + `.AddApplicationPart(typeof(ContentApiGroup).Assembly)` | Host cố ý liệt kê tường minh từng module; thiếu là controller viết xong vẫn 404 |
| `Program.cs` — `apiGroups` | Thêm `(ProfileApiGroup.Name, ProfileApiGroup.Title)` và `(ContentApiGroup.Name, ContentApiGroup.Title)` | Tên nhóm phải khớp **ba chỗ**; thiếu là `/swagger/profile-v1/swagger.json` 404 và `B4` đỏ "không parse được JSON" |
| `Program.cs` — `JsonStringEnumConverter()` | → `new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)` (Q-D2) | Hợp đồng ghi `privacy: public`; converter hiện tại trả `"Public"` |
| `SocialApp.Modules.Profile.csproj`, `SocialApp.Modules.Content.csproj` | Thêm `FluentValidation.AspNetCore` **11.3.0** (đúng version Identity đang dùng) | Hai csproj hiện chỉ có EF; không có gói thì `AbstractValidator` không compile |
| `AddProfileModule` / `AddContentModule` | `AddValidatorsFromAssembly(..., Singleton)` + đăng ký service/store của khối D | Hai hàm này đang chỉ có DbContext + contract; `PostgresFixture.SeededContentDatabaseAsync` dựng chúng **trần** (không host) nên mọi thứ thêm vào phải dựng được mà không cần `IConfiguration`/MVC |
| `SharedKernel/Results/Result.cs` — `Error` | Thêm `Errors` + `Error.Validation(field, message)` (Q-D4) | Service chưa có cách trả 400 kèm `errors`; `BR01-05`, avatar HEAD lệch, BR-01 ở `PATCH` đều cần |
| `SharedKernel/Http/ResultHttpExtensions.cs` | Nhánh `error.Errors is not null` → `ValidationProblem` | Cùng Q-D4. Kiểu trả về của `Problem` đổi `ObjectResult` → `ActionResult`; ba chỗ gọi ở `AuthController` vẫn compile |
| `content-v1.yaml` — `DELETE /posts/{postId}` | Thêm `400` (Q-D7), chạy lại `pnpm gen:api` | `postId` sai dạng chắc chắn ra 400 ở runtime mà hợp đồng chưa ghi |
| `giai-doan-2.md` Mục 8.1 (`bio`), Đ-2.6 (một mệnh đề) | Ghi ngược Q-D3, Q-D5 | Mục 1.4 |

### 1.3 Luật của repo áp thẳng vào khối này

1. **Impact analysis trước khi sửa symbol có sẵn** (`CLAUDE.md`). Khối D chạm những symbol nhiều người gọi:
   ```bash
   node .gitnexus/run.cjs impact "Error" --direction upstream --repo .                 # Q-D4
   node .gitnexus/run.cjs impact "ToActionResult" --direction upstream --repo .        # Q-D4
   node .gitnexus/run.cjs impact "AddProfileModule" --direction upstream --repo .      # D0
   node .gitnexus/run.cjs impact "AddContentModule" --direction upstream --repo .      # D0
   ```
   `AddContentModule`/`AddProfileModule` được dựng **trần** (`new ServiceCollection()`, không host) ở `PostgresFixture`
   (`SeededContentDatabaseAsync`), `ProfileDbContextSchemaTests`, `ContentDbContextSchemaTests`, `UserDirectoryTests` — thêm gì
   vào đó cũng phải dựng được mà không có `IConfiguration`/MVC. HIGH/CRITICAL thì dừng lại
   báo nhóm. Trước **mỗi** commit: `node .gitnexus/run.cjs detect-changes --scope all --repo .` (`partial`/`truncated` → chạy lại).
2. **Controller chỉ ở `Modules/<Module>/Presentation/`**, khai `[ApiExplorerSettings(GroupName = <Module>ApiGroup.Name)]`
   **ngay trong commit tạo controller** — `Every_controller_must_declare_a_swagger_group` đỏ nếu quên, và B.6 nói rõ:
   thiếu là endpoint biến mất khỏi Swagger trong im lặng.
3. **Chỉ `Infrastructure` chạm EF, chỉ `Presentation` chạm MVC** (`PersistenceBoundaryTests`, `PresentationBoundaryTests`).
   Service ở `Application` nhận `IProfileStore`/`IPostStore`, không nhận `DbContext`. `IObjectStorage`, `IUserDirectory`,
   `IFriendshipReader`, `TimeProvider` là của SharedKernel/BCL nên `Application` inject được.
4. **Không import chéo module.** Content cần "người này có hồ sơ chưa?" → `IUserDirectory.GetManyAsync([actorId])`,
   **không** phải `ProfileDbContext`. Content cần hằng số của Identity (`PermissionCodes`) → **không được**; dùng chuỗi
   hợp đồng `"post.create"` trong `[RequirePermission]` (Q-D5 nói cách canh gõ sai).
5. **`actorId` LUÔN từ `User.GetUserId()`** ở controller, truyền xuống service làm tham số. Không route, không body,
   không query. Đây là điều 1 trong "năm thứ không test tự động nào bắt được" của B.9 — code review đọc từng action.
6. **Không có nhánh `role == ADMIN` ở tầng 3.** `PostService.UpdateAsync/DeleteAsync` không nhìn vai trò. Grep
   `SystemRoles.Admin|"ADMIN"` trong `src/backend/Modules/Profile|Content` phải ra **0 kết quả** khi khối D xong.
7. **`IgnoreQueryFilters()` cấm** trong mọi file khối D chạm vào. Bài đã xóa "biến mất" là hành vi đúng của `D6`/`D8`.
8. **Đổi hình dạng API → sửa `.yaml` cùng commit + `pnpm gen:api` + commit `schema.d.ts`.** Mục tiêu của khối D là
   **không phải sửa** hai file đó ngoài hai chỗ đã biết trước (Q-D3 sửa mô tả `bio`, Q-D7 thêm 400 cho `DELETE`).
   Sửa `description` thôi cũng đổi `schema.d.ts` (JSDoc sinh từ mô tả) → vẫn phải chạy lại codegen.
9. **Không log `uploadUrl`, không log presigned GET, không log `storage_key` kèm id người dùng.** Điều 4 của B.9.
   `CapturingLogSink` có sẵn — test `D4` khẳng định điều này bằng máy.
10. **DTO request không dùng từ khóa C# `required`** (AGENTS.md Mục 9; thi công D9 của GĐ1): `[Required]` cho Swagger +
    giá trị mặc định; thiếu trường thì validator bắt với thông điệp tiếng Việt. Riêng `privacy` xem Q-D2.
11. **Không `Skip`.** `B4` viết sớm thì để local; không commit test đỏ có `Skip` để né.

### 1.4 Tám quyết định phải chốt **trước** khi gõ — không tự quyết một mình

Tài liệu gốc chưa nói đủ, hoặc nói mâu thuẫn, ở tám chỗ dưới đây. Mỗi mục có **đề xuất**; chốt khác thì sửa mục
tương ứng của hướng dẫn này. **Ghi ngược** = phải sửa `giai-doan-2.md` / file `.yaml` trong cùng commit với code.
`Q-D1` (hình dạng `mediaKeys`) đã chốt ở cổng mở và nằm trong Mục 8.2 — không lặp lại.

#### Q-D2 — Enum ra JSON phải là chữ thường, và `privacy` vắng mặt không được âm thầm thành `public` ✅ **chốt 2026-09-19 theo đề xuất** — ghi ngược: comment cạnh converter trong `Program.cs`, commit `D0`

**Vấn đề.** Hợp đồng ghi `privacy: public | friends | private`, `purpose: post | avatar`. `Program.cs` dùng
`new JsonStringEnumConverter()` không có naming policy → ghi ra `"Public"`, `"Post"`. Cổng hợp đồng **không so schema
response** nên CI vẫn xanh, nhưng FE (type sinh từ yaml) nhận `"Public"` là lệch hợp đồng lúc chạy. Thêm nữa: nếu DTO
khai `PostPrivacy Privacy` (không nullable) thì body thiếu `privacy` → `default(PostPrivacy)` = `Public` → **bài công
khai ngoài ý muốn** — đúng cái Q-D1 vừa nói phải tránh khi chốt `privacy` bắt buộc.

**Đề xuất.**

- `Program.cs`: `new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)`. An toàn với Identity: **không enum nào** của
  Identity đi qua HTTP (`MeResponse.Status`/`Role` là `string`; kiểm bằng `grep -rn "enum " src/backend/Modules/Identity/Application`
  → 0). Đọc vào vẫn không phân biệt hoa thường (`"PUBLIC"` được nhận — nới hơn hợp đồng, chấp nhận).
- DTO: `PostPrivacy? Privacy { get; init; }` **nullable** + `[Required]` (Swagger) + validator `NotNull().WithMessage("Mức
  riêng tư là bắt buộc.")`. `UpdatePostRequest.Privacy` cũng `PostPrivacy?` (tùy chọn thật). `UploadPurpose? Purpose`
  tương tự.
- Giá trị lạ (`"everyone"`) → System.Text.Json ném → 400 `errors.privacy` với câu cố định của `ValidationErrors` — đúng key,
  không cần làm gì thêm.
- Kiểm bằng mắt sau `D5`: `/swagger/content-v1/swagger.json` phải hiện `enum: ["public","friends","private"]`. Nếu
  Swashbuckle vẫn hiện PascalCase thì đó là chuyện tài liệu, không phải runtime — ghi vào PR, không đổi cách làm.

**Phương án loại:** DTO dùng `string` + validator `Must(v => v is "public" or ...)` — mất kiểu ở service, và `LowercaseEnum`
(nguồn chữ thường) là `internal` của Infrastructure nên `Application` không dùng lại được.

**Ghi ngược:** không (không phải `Đ-2.*`). Ghi lý do vào comment cạnh converter trong `Program.cs`.

#### Q-D3 — `bio`: "bỏ trường = giữ nguyên" hay "bỏ trường = xóa"? Hai nguồn đang mâu thuẫn ✅ **chốt 2026-09-19 theo đề xuất** — Mục 8.1 của `giai-doan-2.md` **đã sửa ngay** (mâu thuẫn sống thì không để qua đêm)

**Vấn đề.** `giai-doan-2.md` Mục 8.1 (chốt cùng Q-D1): *"`bio: null` = XÓA bio, bỏ trường = giữ nguyên"*.
`profile-v1.yaml` (`UpsertProfileRequest.bio`): *"Bỏ trống hoặc `null` để xóa bio"*. Luật frontend xếp file `.yaml` là
nguồn sự thật số 1, tài liệu giai đoạn số 2 — nhưng không ai được để hai nguồn lệch nhau.

**Đề xuất: theo yaml — `PUT` là thay thế toàn phần, `bio` vắng mặt hay `null` đều xóa.** Ba lý do: (1) đúng ngữ nghĩa
`PUT`; (2) System.Text.Json **không phân biệt** "vắng mặt" với `null` cho `string?` — phân biệt được thì phải tự viết
kiểu `Optional<T>` + converter, một khối code chỉ để phục vụ một trường; (3) FE của `E2` gửi cả form một lần, không có
màn nào chỉ sửa `displayName` mà muốn giữ `bio` cũ. Ai muốn giữ `bio` thì gửi lại `bio`.

**Ghi ngược:** sửa dòng `UpsertProfileRequest` ở Mục 8.1 `giai-doan-2.md` thành *"bio? (≤ 500; vắng mặt hoặc null =
xóa — PUT thay thế toàn phần)"*, trong commit `D2`. Yaml **không đổi**.

#### Q-D4 — Service trả 400 kèm `errors` bằng cách nào? Đề xuất `Error.Validation` ✅ **chốt 2026-09-19 theo đề xuất** — ghi ngược: XML doc `ResultHttpExtensions`, commit `D0`

**Vấn đề.** Ba chỗ của khối D phải trả 400 có `errors` theo tên trường **sau khi đã qua FluentValidation** — vì cần I/O:
`D3` (HEAD avatar: chưa tải xong / sai loại → `errors.mediaKey`), `D5` (`MediaHeadPolicy` sau HEAD → `errors.mediaKeys`,
`BR01-05`), `D7` (BR-01 với `media_count` đọc từ DB → `errors.body`). `Error` hiện chỉ có `Code, Message, Status, Title`;
`ToActionResult` gọi `Problem(...)` không có `errors`. Hai đường có sẵn đều sai chỗ: `AppException.Validation` là ném
exception cho luồng nghiệp vụ (quy ước 2 của GĐ1 cấm), còn controller tự `ModelState.AddModelError` theo từng mã lỗi là
đem logic ánh xạ ra khỏi "một chỗ duy nhất" của `ResultHttpExtensions`.

**Đề xuất.** Mở rộng `Error` một cách tương thích ngược:

```csharp
// SharedKernel/Results/Result.cs — thêm tham số cuối, có mặc định, để mọi lời gọi cũ không đổi
public readonly record struct Error(
    string Code, string Message, int Status, string? Title = null,
    IReadOnlyDictionary<string, string[]>? Errors = null)
{
    public static readonly Error Forbidden = new("auth.forbidden", "Bạn không có quyền thực hiện thao tác này.", 403);

    /// <summary>400 theo TRƯỜNG, cùng hình dạng với 400 của FluentValidation: title "Dữ liệu không hợp lệ", detail
    /// ValidationDetail, errors {field: [message]}. Cho lỗi chỉ biết được sau I/O (HEAD lệch, BR-01 với dữ liệu DB).</summary>
    public static Error Validation(string field, string message) =>
        new("validation", ProblemTitles.ValidationDetail, 400, null,
            new Dictionary<string, string[]> { [field] = [message] });
}
```

```csharp
// SharedKernel/Http/ResultHttpExtensions.cs — MỘT nhánh mới, cùng factory với 400 của MVC
private static ActionResult Problem(ControllerBase controller, Error error)
{
    if (error.Errors is { } errors)
    {
        var modelState = new ModelStateDictionary();
        foreach (var (field, messages) in errors)
            foreach (var message in messages)
                modelState.AddModelError(field, message);
        return controller.ValidationProblem(modelState);   // → SharedKernelProblemDetailsFactory → ValidationErrors.From
    }

    return controller.Problem(statusCode: error.Status, detail: error.Message, title: error.Title);
}
```

`ValidationProblem(ModelStateDictionary)` đi qua `SharedKernelProblemDetailsFactory.CreateValidationProblemDetails` nên
title/type/detail/traceId **giống hệt** 400 do `[ApiController]` sinh — FE không thấy hai hình dạng. Unit test trong
`ResultTests`; integration qua chính test `D3`/`D5`/`D7` (khẳng định `title` và key).

**Ghi ngược:** không. Cập nhật XML doc của `ResultHttpExtensions` (khuôn tầng 3 cho GĐ2+) trong cùng commit `D0`.

#### Q-D5 — `purpose=post` kiểm `post.create` bằng gì? `IPermissionCache` như Đ-2.6 viết sẽ chặn nhầm Admin ✅ **chốt 2026-09-19 theo đề xuất** — ghi ngược: sửa một mệnh đề của Đ-2.6, commit `D4`

**Vấn đề.** Đ-2.6: *"`[Authorize]` ở attribute + kiểm `post.create` trong service khi `purpose=post`, bằng chính
`IPermissionCache`"*. Nhưng `IPermissionCache.GetAsync("ADMIN")` trả **rỗng** — Admin không có dòng `role_permissions`
(`RBAC-01`: *"ADMIN qua dù không có dòng role_permissions"*), short-circuit nằm trong `PermissionHandler`. Gọi thẳng
cache là Admin **không xin được URL tải ảnh bài** — và cách vá là lặp lại `if role == ADMIN` trong service, thứ Mục 3.2
của GĐ1 cấm lặp ở bất kỳ đâu ngoài `PermissionHandler`.

**Đề xuất: dùng `IAuthorizationService` với chính policy `perm:post.create`, ở controller.** Cùng `PermissionHandler`,
cùng short-circuit, cùng cache, không lặp logic:

```csharp
// Modules/Content/Presentation/MediaController.cs
[Authorize]                                   // tầng 2 mức 1: mọi purpose
[HttpPost]
public async Task<ActionResult<IReadOnlyList<UploadTicket>>> Create(
    CreateUploadsRequest request, [FromServices] IAuthorizationService authorization, CancellationToken ct)
{
    if (request.Purpose == UploadPurpose.Post)
    {
        // tầng 2 mức 2: đúng policy mà [RequirePermission("post.create")] dựng — PermissionPolicyProvider hiểu tiền tố perm:
        var check = await authorization.AuthorizeAsync(User, null, RequirePermissionAttribute.PolicyPrefix + ContentPermissions.PostCreate);
        if (!check.Succeeded)
            return Error.Forbidden.ToActionResult(this);
    }
    ...
}
```

`ContentPermissions` là lớp hằng chuỗi **trong module Content** (`Application/ContentPermissions.cs`: `PostCreate`,
`PostReadPublic`, `PostUpdate`, `PostDelete`) — Content không được import `PermissionCodes` của Identity. Để không gõ
sai: thêm **một test** vào `ArchitectureTests` (project test tham chiếu được cả hai module) khẳng định mọi hằng của
`ContentPermissions` nằm trong `PermissionCodes.All`. `[RequirePermission(ContentPermissions.X)]` trên các action còn
lại vẫn được `PermissionCodeUsageTests` canh như cũ.

Đặt ở controller chứ không ở service: kiểm này cần `ClaimsPrincipal` — từ vựng của HTTP; service chỉ nhận `actorId` và
`purpose`.

**Ghi ngược:** sửa một mệnh đề trong Đ-2.6 của `giai-doan-2.md`: *"…kiểm `post.create` trong service bằng chính
`IPermissionCache`"* → *"…kiểm `post.create` ở controller bằng `IAuthorizationService` với policy `perm:post.create` —
cùng handler và short-circuit Admin của tầng 2 (chốt Q-D5, 2026-09-19: gọi thẳng `IPermissionCache` chặn nhầm Admin)"*.
Trong commit `D4`.

#### Q-D6 — Câu truy vấn keyset viết bằng LINQ hay SQL nội suy? ✅ **chốt 2026-09-19 theo đề xuất** — ghi ngược: "Thực tế thi công" của `D6`

**Vấn đề.** Đ-2.11 đọc `WHERE (created_at, post_id) < (@at, @id)`. LINQ không có so sánh bộ; viết tách
`p.CreatedAt < at || (p.CreatedAt == at && p.PostId < id)` thì C# **không có** `<` cho `Guid`, còn `p.PostId.CompareTo(id) < 0`
có dịch được sang SQL hay không tùy provider — không dịch được thì EF ném **lúc chạy** (client evaluation bị cấm), tức
là đỏ ở `PAGE-01` chứ không ở compile.

**Đề xuất.** Thử LINQ với `CompareTo` **trước**; đọc câu SQL trong log của test `PAGE-01` (EF log `Executed DbCommand`
ở Information, `CapturingLogSink` bắt được). Dịch được → giữ. Không dịch được → `FromSql` **nội suy** (tham số hóa, không
phải `FromSqlRaw` ghép chuỗi) chỉ cho mệnh đề keyset, phần còn lại vẫn LINQ:

```csharp
// Modules/Content/Infrastructure/Persistence/PostStore.cs — chỉ khi CompareTo không dịch được
var query = db.Posts.FromSql($"""
    SELECT * FROM content.posts
     WHERE author_id = {authorId} AND (created_at, post_id) < ({cursor.CreatedAt}, {cursor.PostId})
    """);
// Query filter (status <> 'deleted') VẪN áp lên FromSql; BR-02, OrderBy, Take nối tiếp bằng LINQ.
```

SQL này chỉ chạm **một schema của chính module** — không phải `BOUND-01`. So sánh bộ của Postgres đọc thẳng
`idx_posts_author_created` (đúng lý do A5 đặt `post_id` vào index).

**Ghi ngược:** không. Ghi cách đã chọn vào "Thực tế thi công" của `D6`.

#### Q-D7 — `DELETE /posts/{postId}` với id sai dạng trả 400 mà hợp đồng chưa ghi ✅ **chốt 2026-09-19 theo đề xuất** — ghi ngược: thêm `400` vào `content-v1.yaml` + `pnpm gen:api`, commit `D8`

**Vấn đề.** `GET`/`PATCH /posts/{postId}` và `GET /users/{userId}/…` đều có `400` trong yaml (tham số sai dạng).
`DELETE /posts/{postId}` thì không: hợp đồng chỉ có 204/401/403. Nhưng route không dùng ràng buộc `:guid` (Mục 1.5 nói vì
sao) nên `DELETE /posts/abc` **chắc chắn** ra 400 `errors.postId` ở runtime. Cổng hợp đồng không bắt (nó so mã action
*khai*, không so mã runtime *trả*), nhưng đó là một mã lệch hợp đồng có thật.

**Đề xuất.** Thêm `'400': $ref ValidationProblem` vào `DELETE /posts/{postId}` trong `content-v1.yaml`, khai
`[ProducesResponseType<ValidationProblemDetails>(400)]` trên action, chạy `pnpm gen:api`, commit `schema.d.ts` — tất cả
trong commit `D8`. Báo lane E một dòng (type `paths["/posts/{postId}"]["delete"]["responses"]` có thêm `400`).

**Phương án loại:** ràng buộc route `{postId:guid}` để "không bao giờ ra 400" — khi đó id sai dạng ra **404** (không
khớp route), lệch hợp đồng của `GET`/`PATCH` vốn đã hứa 400, và lệch `ProblemDetailsTests` (route lạ có token → 404 title
"Không tìm thấy tài nguyên" là dành cho route **không tồn tại**, không phải cho tham số hỏng).

**Ghi ngược:** file `.yaml` (là hợp đồng — cần cả nhóm gật, không phải quyết định của một lane).

#### Q-D8 — `IUserDirectory` không trả về tác giả thì `PostResponse.author` là gì? ✅ **chốt 2026-09-19 theo đề xuất** — ghi ngược: XML doc của mapper

**Vấn đề.** `GetManyAsync` ghi rõ *"id nào không có hồ sơ thì vắng mặt — người gọi phải xử lý"*. Đ-2.4 làm cho ca này gần
như không xảy ra (không có hồ sơ thì không đăng được bài, và GĐ2 không có đường xóa hồ sơ), nhưng "gần như" không phải
"không": GĐ8 sẽ có xóa tài khoản, và một bài mồ côi tác giả **không được** làm 500 cả trang feed của GĐ4.

**Đề xuất.** `PostResponseMapper` dùng `UserCard` dự phòng `(authorId, "Người dùng", null)` khi vắng mặt và
`LogWarning` một dòng (chỉ `postId`, không id người dùng). Không ném. Không thêm nhánh nào khác.

**Ghi ngược:** không. Ghi vào XML doc của mapper, trỏ GĐ8.

#### Q-D9 — `PUT /users/me/avatar` khi người gọi chưa có hồ sơ ✅ **chốt 2026-09-19 theo đề xuất** — ghi ngược: mô tả `403` trong `profile-v1.yaml` + `pnpm gen:api`, commit `D3`

**Vấn đề.** Mục 7.1 bắt onboarding trước, nên FE không bao giờ gọi đặt avatar khi chưa có hồ sơ — nhưng API phải trả
**một mã nào đó**, và hợp đồng chỉ có 200/400/401/403. `UPDATE … WHERE user_id = @me` trúng 0 dòng thì không có gì để
trả 200.

**Đề xuất: 403, cùng `Error.Forbidden`** — nhất quán với Đ-2.4 ("không có hồ sơ thì không đăng được bài" → không có hồ sơ
thì không gắn được avatar), không thêm mã mới vào hợp đồng, không lộ gì. `DELETE` thì **204 bất kể** (idempotent như yaml
đã ghi).

**Ghi ngược:** thêm một mệnh đề vào `description` của `403` trong `profile-v1.yaml` (`Forbidden`: *"…hoặc người gọi
chưa có hồ sơ (chốt Q-D9)"*) + `pnpm gen:api`, trong commit `D3`.

### 1.5 Khuôn thi công — file nào ở tầng nào

Chép đúng Đ-D1 của GĐ1 cho **hai** module. Tên file là đề xuất; tầng và ranh giới thì không.

**Module Profile**

| Tầng | File | Chạm gì |
|---|---|---|
| `Presentation/` | `ProfileApiGroup.cs` · `ProfilesController.cs` (`[Route("api/v1/users")]`: `GET {userId}/profile`, `PUT me/profile`, `PUT me/avatar`, `DELETE me/avatar`) | MVC, `User.GetUserId()`, `Result → HTTP` |
| `Application/` | `ProfileErrors.cs` · `Profiles/ProfileResponse.cs`, `UpsertProfileRequest.cs` + validator, `SetAvatarRequest.cs` + validator · `Profiles/ProfileService.cs` · `Profiles/IProfileStore.cs` | Không EF, không MVC. Inject `IObjectStorage` (ký `avatarUrl`), `TimeProvider` |
| `Infrastructure/` | `Persistence/ProfileStore.cs` (`UpsertAsync` bằng `INSERT … ON CONFLICT`, `FindAsync`, `SetAvatarKeyAsync`) | EF, Npgsql |

**Module Content**

| Tầng | File | Chạm gì |
|---|---|---|
| `Presentation/` | `ContentApiGroup.cs` · `MediaController.cs` (`[Route("api/v1/media")]`: `POST uploads`) · `PostsController.cs` (`[Route("api/v1")]`: `POST posts`, `GET posts/{postId}`, `PATCH posts/{postId}`, `DELETE posts/{postId}`, `GET users/{userId}/posts`) | MVC, `IAuthorizationService` (Q-D5), `Result → HTTP` |
| `Application/` | `ContentErrors.cs` · `ContentPermissions.cs` · `Media/CreateUploadsRequest.cs` + validator, `UploadTicket.cs`, `UploadTicketService.cs` · `Posts/CreatePostRequest.cs`, `UpdatePostRequest.cs`, `ListUserPostsQuery.cs` + ba validator, `PostResponse.cs` (+ `PostAuthor`, `PostMedia`, `PostPage`), `PostCursor.cs`, `PostResponseMapper.cs`, `PostService.cs`, `PostReadService.cs`, `IPostStore.cs` | Không EF, không MVC. Inject `IObjectStorage`, `IUserDirectory`, `IFriendshipReader`, `TimeProvider` |
| `Infrastructure/` | `Persistence/PostStore.cs` (`AddWithMediaAsync` một transaction + bắt UNIQUE, `FindAsync`, `ListByAuthorAsync` keyset, `MediaOfAsync`, `SaveAsync`) | EF, Npgsql, `FromSql` nếu Q-D6 rẽ nhánh đó |

**Ba quy ước chung cho cả hai module:**

- **Không ràng buộc route `:guid`.** `[HttpGet("{userId}/profile")]` với tham số `Guid userId`: sai dạng → model binding
  hỏng → `[ApiController]` trả **400** `errors.userId` "Giá trị không hợp lệ." — đúng hợp đồng. Có `:guid` thì sai dạng
  không khớp route → 404 — sai hợp đồng. `PUT me/profile` và `GET {userId}/profile` khác method nên không tranh nhau;
  `GET /users/me/profile` (không có trong hợp đồng) rơi vào `{userId}` → 400, không phải endpoint ẩn.
- **Response DTO là `record` riêng**, không trả entity (`UserProfile`, `Post` có cột nội bộ). Request DTO là **class
  `init`** với `[Required]` + giá trị mặc định, đúng thi công D9 của GĐ1 (record positional làm Swagger không thấy
  `[Required]`).
- **Thời gian từ `TimeProvider`** (đã `TryAddSingleton(TimeProvider.System)` — thêm dòng đó vào hai `Add<Module>Module`
  để dựng trần được). `CreatedAt`/`UpdatedAt` gán ở service; `StampUpdatedAt` của context vẫn là lưới cho `Modified`.

**Harness test** — `tests/SocialApp.IntegrationTests/Harness/ModulesApiFactory.cs`:

| Khác gì `IdentityApiFactory` | Vì sao |
|---|---|
| `UseFreshDatabaseAsync` migrate **cả ba** module (`AddIdentityModule + AddProfileModule + AddContentModule` rồi ba `Migrate*Async`) | Test khối D **sửa** dữ liệu → DB riêng mỗi lớp (luật B1); tầng 2 đọc `role_permissions` nên Identity phải seed |
| `Storage` = `FakeObjectStorage`, đăng ký `RemoveAll<IObjectStorage>()` + `AddSingleton(Storage)` | Chép nguyên `AuthZApiFactory` (C5) |
| **Không** `CapturingEmailSender` | Khối D không gửi mail |
| Token bằng `TestJwt.Create("USER", userId: id)` — **không** đăng ký/đăng nhập | Profile/Content không FK sang `users` (Đ-2.2); tầng 1 chỉ cần chữ ký, tầng 2 chỉ cần `role`. Đây là điểm khác GĐ1 (`/me` đọc `users` nên cần login thật) |

Kèm `Harness/ModulesTestClient.cs` với `Bearer(Guid userId, string role = "USER")`, `PutProfileAsync(userId, displayName, bio)`,
`PutObject(key, size, type)` (bọc `fake.Put`), `CreatePostAsync(userId, body, privacy, keys)` → `PostResponse`, và
`ReadProblemAsync(response)` → `(status, title, errors)`. Test đặt ở `tests/SocialApp.IntegrationTests/Profile/` và
`…/Content/`, `[Collection(PostgresCollection.Name)]`, `IClassFixture<ModulesApiFactory>`.

---

## 2. D0 — Nền chung của hai module

**Mục tiêu.** Mười endpoint dùng chung một bộ gạch. Viết một lần, có test một lần, trước endpoint đầu tiên.

**Kết quả mong đợi.** Xem bảng Mục 0. Cụ thể là các file/dòng sau tồn tại và build xanh:

- `Modules/Profile/Presentation/ProfileApiGroup.cs` (`Name = "profile-v1"`, `Title = "Profile"`),
  `Modules/Content/Presentation/ContentApiGroup.cs` (`Name = "content-v1"`, `Title = "Content"`) — chép `IdentityApiGroup`
  kể cả XML doc "ba chỗ phải khớp".
- `Program.cs`: hai `AddApplicationPart`, hai mục `apiGroups`, converter enum camelCase (Q-D2).
- Hai csproj có `FluentValidation.AspNetCore` 11.3.0.
- `AddProfileModule`/`AddContentModule`: `TryAddSingleton(TimeProvider.System)`, `AddValidatorsFromAssembly(typeof(<Module>ModuleExtensions).Assembly, ServiceLifetime.Singleton)`,
  và các `AddScoped` cho service/store (thêm dần theo từng D — dòng validator thì có ngay).
- `Application/ProfileErrors.cs`, `Application/ContentErrors.cs`, `Application/ContentPermissions.cs`.
- `Error.Errors` + `Error.Validation` + nhánh `ValidationProblem` (Q-D4) + unit test.
- `tests/SocialApp.IntegrationTests/Harness/ModulesApiFactory.cs`, `ModulesTestClient.cs`,
  `tests/SocialApp.IntegrationTests/Profile/ProfileHarnessTests.cs` (test khung).
- `tests/SocialApp.ArchitectureTests/ContentPermissionsTests.cs` (Q-D5): mọi hằng của `ContentPermissions` ∈ `PermissionCodes.All`.

### Các bước

**Bước 1 — hai `ApiGroup` + `Program.cs`.** Chạy impact cho `AddProfileModule`/`AddContentModule` trước. Sau khi nối, dựng
app bằng `ApiFactory` (không DB) và `GET /swagger/profile-v1/swagger.json` → 200 với `paths: {}`. 404 ở đây nghĩa là tên
nhóm lệch một trong ba chỗ.

**Bước 2 — `Error.Validation` (Q-D4).** Sửa `Result.cs` và `ResultHttpExtensions.cs` theo đoạn mã ở Q-D4. Cập nhật XML doc
của `ResultHttpExtensions` (khuôn tầng 3 cho GĐ2+ giờ có thêm dòng *"lỗi theo trường sau I/O → `Error.Validation`"*).
Unit test `ResultTests`: `Error.Validation("mediaKey", "m").Errors["mediaKey"]` = `["m"]`, `Status == 400`,
`Message == ProblemTitles.ValidationDetail`.

**Bước 3 — hai lớp lỗi.** Mọi thông điệp của khối D ở **hai** chỗ, để `D9` rà PII bằng một lần đọc. Thông điệp chép **đúng
câu trong ví dụ của yaml** — FE (luật frontend Mục 6) hiện đúng câu server, và một lỗi không được có hai cách nói:

```csharp
// Modules/Profile/Application/ProfileErrors.cs
public static class ProfileErrors
{
    public static readonly Error NotFound = new("profile.not_found", "Người dùng này chưa có hồ sơ.", 404);
    public static Error AvatarNotUploaded => Error.Validation("mediaKey", "Ảnh chưa được tải lên xong. Hãy chờ tải lên hoàn tất rồi thử lại.");
    public static Error AvatarTypeNotAllowed => Error.Validation("mediaKey", "Ảnh đại diện chỉ nhận JPEG, PNG hoặc WebP.");
    // 403 dùng Error.Forbidden của SharedKernel — không tạo bản thứ hai.
}

// Modules/Content/Application/ContentErrors.cs
public static class ContentErrors
{
    public static readonly Error PostNotFound = new("post.not_found", "Không tìm thấy bài viết.", 404);
    public static readonly Error MediaAlreadyUsed = new("post.media_conflict", "Ảnh này đã được dùng trong một bài khác.", 409);
    public static Error FromValidation(PostContentValidation v) => Error.Validation(v.ErrorKey!, v.Message!);   // BR-01, MediaHeadPolicy
    public static Error NothingToUpdate => Error.Validation("body", "Không có gì để sửa.");
    public static Error DuplicateMediaKeys => Error.Validation("mediaKeys", "Một ảnh không được đính kèm hai lần.");
}
```

`ContentPermissions`: bốn hằng `"post.create"`, `"post.read.public"`, `"post.update"`, `"post.delete"` + test kiến trúc ở Q-D5.

**Bước 4 — csproj + DI.** Thêm gói; thêm ba dòng vào mỗi `Add<Module>Module`. Chạy `dotnet test tests/SocialApp.IntegrationTests
--filter "FullyQualifiedName~DbContextSchemaTests|FullyQualifiedName~UserDirectoryTests"` — ba lớp này dựng module bằng
`new ServiceCollection()` không host, đỏ ở đây nghĩa là vừa thêm một phụ thuộc không dựng được ngoài host.

**Bước 5 — harness.** `ModulesApiFactory` theo bảng Mục 1.5. Test khung `ProfileHarnessTests`: (a) `PUT /api/v1/users/me/profile`
với bearer USER → **404** (route chưa có; đồng thời chứng minh token của `TestJwt` qua tầng 1 của app); (b) ẩn danh → **401**.
`D2` xóa (a) khi controller có thật (nếp `D1` GĐ1), giữ (b) hoặc để `TC-A01-profile` của `B2` thay.
**Thực tế: cả (a) lẫn (b) chết ở `D1`, không phải `D2`, và chết bằng `405`** — xem "Thực tế thi công" của `D1`.

**Bước 6 — `B4` ở local.** Viết `ContractTestsBase` + `ProfileContractTests` + `ContentContractTests` + hai dòng csproj theo
hướng dẫn B/C Mục 10. Chạy `--filter "Category=Contract"`: chiều 1 xanh (chưa có gì để lộ), chiều 2 đỏ liệt kê **đủ 10
operation**. Danh sách đó là bảng tiến độ của khối D. **Không commit** cho tới sau `D9`.

### Cạm bẫy đã biết

- **Quên `AddApplicationPart`** → mọi controller 404 (có token) / 401 (ẩn danh), Swagger rỗng, và không có lỗi nào. Từ
  `D1` trở đi, `ProfileTests` bắt chỗ này: thiếu dòng đó thì *mọi* nhánh của nó ra 404 — kể cả nhánh đáng lẽ 400 — nên
  `UserId_sai_dang_tra_400_kem_errors_userId` đỏ trước tiên.
- **Gọi `AddFluentValidationAutoValidation` trong module** → lỗi validate bị nhân đôi. Host đã gọi một lần; module chỉ
  `AddValidatorsFromAssembly`.
- **Đăng ký service cần `IConfiguration`/`IHostEnvironment` trong `Add<Module>Module`** → `PostgresFixture` dựng trần đỏ.
  Mọi thứ cần host thì nhận qua tham số như `AddIdentityEmail`, hoặc bind lười như `MediaCleanupOptions`.
- **Đổi converter enum mà không grep Identity** → nếu có enum nào đang đi qua HTTP thì casing đổi âm thầm. Grep trước, ghi
  kết quả (0) vào commit.

---

## 3. D1 — `GET /users/{userId}/profile`

**Mục tiêu.** Endpoint đọc đầu tiên, và là tín hiệu onboarding của FE: 404 → `/onboarding` (Mục 7.1).

**Kết quả mong đợi.** `ProfilesController.Get(Guid userId)` `[Authorize]`, không `[RequirePermission]` (hồ sơ công khai
trong MVP — Mục 6.1); `ProfileService.GetAsync(userId)` → `Result<ProfileResponse>`; `IProfileStore.FindAsync(userId)`;
`ProfileResponse(UserId, DisplayName, Bio, AvatarUrl, CreatedAt, UpdatedAt)`; `[ProducesResponseType]` cho 200/400/401/404.

### Các bước

1. `ProfileResponse` là `record` trong `Application/Profiles/`. `AvatarUrl` = `storage.CreatePresignedGet(avatarKey)` khi
   `avatarKey` khác null, ngược lại `null` — ký ở **service**, không ở store (store không biết R2) và không ở controller.
2. `ProfileService.GetAsync`: `FindAsync` null → `ProfileErrors.NotFound`. Không có tầng 3.
3. Controller: `return result.ToActionResult(this);`. Không `[Produces]` ở class (mất `application/problem+json` cho lỗi —
   bài học `MeController`).
4. Test `Profile/ProfileTests` (dùng `ModulesApiFactory`): `PROF-03`; `GET /users/khong-phai-uuid/profile` → 400 với
   `errors.userId`; ẩn danh → 401. Test 200 viết sau `D2` (cần dữ liệu qua API thật).

### Cạm bẫy đã biết

- **`detail` chứa `userId`** ("Người dùng 0192… chưa có hồ sơ") → lộ id vào response, và `D9` rà PII đỏ. Câu cố định, không nội suy.
- **FE hỏi "làm sao biết `userId` của mình để gọi?"** → từ `GET /me` của Identity (`MeResponse.userId`). Không thêm
  `GET /users/me/profile` — hợp đồng không có, thêm là `B4` chiều 1 đỏ.

### Thực tế thi công

**Bằng chứng.** `dotnet test SocialApp.sln`: Unit 117 (không đổi), Architecture 13 (không đổi), Integration 196 → 201
(+6 `ProfileTests`, −1 test khung của `D0`). Thử cho đỏ ở local rồi khôi phục:

| Đột biến | Test đỏ |
|---|---|
| Route đổi thành `[HttpGet("{userId:guid}/profile")]` | cả ba dòng `UserId_sai_dang_tra_400_kem_errors_userId` — nhận 404 thay vì 400 |
| `ProfileErrors.NotFound` nội suy id: `$"Người dùng {userId} chưa có hồ sơ."` | `PROF_03_nguoi_chua_onboarding_tra_404_va_detail_khong_neu_userId` |
| `[Authorize]` của controller đổi thành `[AllowAnonymous]` | `An_danh_tra_401` — nhận 404 thay vì 401 |

`Ho_so_cua_chinh_minh_khi_chua_onboarding_van_404` **không có đột biến riêng nào giết nó** mà không giết luôn `PROF-03`:
nó canh một cách hỏng cần người cố ý viết thêm nhánh (`if (userId == actorId) return ...`), không phải một dòng gõ sai.
Giữ vì cái nhánh đó là thứ dễ bị đề xuất thêm vào nhất khi `D2` tới, và giá của nó là một lời gọi HTTP.

**Chỗ lệch so với các bước trên — đã làm như sau:**

- **Test khung của `D0` chết ở `D1`, không phải `D2`, và chết bằng `405` chứ không phải 400/200.** Bước 5 của `D0` đoán
  `PUT /api/v1/users/me/profile` còn 404 cho tới khi `D2` nối action `PUT me/profile`. Sai: `ProfilesController` của `D1`
  đã nhận route `api/v1/users`, nên đường dẫn đó khớp template `{userId}/profile` ngay ở tầng **routing** rồi mới lệch
  method — ASP.NET Core chọn endpoint 405, và endpoint đó không mang metadata `[Authorize]` nên nhánh **ẩn danh cũng 405**,
  không còn 401. Tức là (b) cũng hỏng, không chỉ (a). Đã **xóa cả test** trong commit này (nếp `D1` GĐ1) thay vì "xóa (a),
  giữ (b)": hai khẳng định của nó có chỗ đứng thật hơn trong `ProfileTests` — token USER qua tầng 1 (404 chứ không 401) và
  ẩn danh → 401, cả hai trên endpoint có thật. Mục 2 bước 5 và cạm bẫy `AddApplicationPart` đã sửa theo trong cùng commit.
- **Thêm ngoài bốn nhánh của bước 4:** `Ho_so_cua_chinh_minh_khi_chua_onboarding_van_404`. Trông thừa cạnh `PROF-03`
  nhưng nó canh đúng thứ Đ-2.4 dựa vào: service "ưu ái" người gọi (trả 200 hồ sơ rỗng, hoặc tự tạo hồ sơ) thì FE mất tín
  hiệu onboarding mà không test nào khác đỏ. Và `me` vào danh sách id sai dạng, để `GET /users/me/profile` được chứng minh
  là **400** chứ không phải một endpoint ẩn.
- **`IProfileStore` chỉ có `FindAsync`.** Mục 1.5 liệt kê `UpsertAsync`/`SetAvatarKeyAsync` cùng file; chúng vào ở `D2`/`D3`
  cùng commit với endpoint gọi chúng — khai trước là một dòng không test nào chạm tới. Cùng lý do với `ModulesTestClient` ở `D0`.
- **`ToResponse` (entity → DTO, kèm ký `avatarUrl`) nằm ở `ProfileService`**, `private`. `D2`/`D3` cũng trả `ProfileResponse`
  nên dùng lại đúng hàm này; tách ra mapper riêng chỉ có nghĩa khi có người gọi thứ hai ngoài service.
- **`ProfileService` nhận `IObjectStorage` nhưng `AddProfileModule` KHÔNG đăng ký nó** — host làm việc đó (`Program.cs`,
  `R2StorageExtensions`). Bốn chỗ dựng module trần (`new ServiceCollection()`, Mục 1.3 luật 1) vẫn xanh vì chúng chỉ
  *đăng ký*, không resolve `ProfileService`.

**Lỗi tìm ra khi rà, CHƯA sửa ở đây vì không thuộc `D1`:**
`StartupConfigurationTests.Development_boots_without_r2_config_and_first_use_names_the_variables` (khối C) đỏ trên máy
**đã có khóa R2 trong user-secrets** — Development lúc đó nhận `R2ObjectStorage` thật chứ không phải
`UnconfiguredObjectStorage`, nên `HeadAsync` đi ra mạng thay vì ném `InvalidOperationException`. Đỏ **từ trước `D1`**
(kiểm bằng cách stash và chạy lại trên `7419cea`), xanh trên CI vì CI không có user-secrets. Là test tự ràng vào môi
trường máy chạy, thuộc khối C — sửa ở việc riêng.

---

## 4. D2 — `PUT /users/me/profile` (upsert)

**Mục tiêu.** Bước onboarding của Đ-2.4. Một mã 200 cho cả tạo lẫn sửa; hai tab cùng onboarding không tab nào 500.

**Kết quả mong đợi.** `UpsertProfileRequest { DisplayName = ""; Bio }` + `UpsertProfileRequestValidator`;
`ProfileService.UpsertAsync(actorId, request)` → `Result<ProfileResponse>`; `IProfileStore.UpsertAsync(profile)` là
**một** câu `INSERT … ON CONFLICT (user_id) DO UPDATE … RETURNING`; `PROF-01`, `PROF-02`, các case 400, test song song.

### Các bước

**Bước 1 — validator.** Dùng hằng số của entity, không gõ lại số:

```csharp
public sealed class UpsertProfileRequestValidator : AbstractValidator<UpsertProfileRequest>
{
    public const int MaxBioLength = 500;
    public static readonly string DisplayNameLength =
        $"Tên hiển thị phải có từ {UserProfile.DisplayNameMinLength} đến {UserProfile.DisplayNameMaxLength} ký tự.";

    public UpsertProfileRequestValidator()
    {
        // Trim TRƯỚC khi đo (hợp đồng: "2–50 ký tự sau khi trim"); "   " → 0 ký tự → cùng một thông điệp.
        RuleFor(x => x.DisplayName)
            .Must(d => d.Trim().Length is >= UserProfile.DisplayNameMinLength and <= UserProfile.DisplayNameMaxLength)
            .WithMessage(DisplayNameLength);
        RuleFor(x => x.Bio).MaximumLength(MaxBioLength).WithMessage($"Giới thiệu tối đa {MaxBioLength} ký tự.");
    }
}
```

**Bước 2 — store: upsert nguyên tử.** Đọc-rồi-ghi (`FindAsync` → null → `Add`) là hai tab onboarding cùng lúc: cả hai
thấy null, cả hai INSERT, tab sau ăn PK violation → 500. Một câu SQL:

```csharp
// Infrastructure/Persistence/ProfileStore.cs
public async Task<UserProfile> UpsertAsync(Guid userId, string displayName, string? bio, DateTimeOffset now, CancellationToken ct)
{
    // ON CONFLICT: created_at GIỮ NGUYÊN (không có trong SET), avatar_key GIỮ NGUYÊN (PUT profile không đụng avatar — D3 mới đụng).
    // updated_at gán tay: SQL thô đi vòng StampUpdatedAt. RETURNING để trả đúng dòng sau ghi, không SELECT lần hai.
    //
    // ToListAsync + Single() ở client, KHÔNG SingleAsync: EF xếp `INSERT … RETURNING` vào loại non-composable, mà
    // SingleAsync thêm LIMIT 2 tức là compose lên trên nó → InvalidOperationException trước khi chạm DB (500 trần ở
    // tầng HTTP). ToListAsync không sửa một ký tự nào của SQL. Bọc bằng CTE không cứu được — xem "Thực tế thi công".
    var rows = await db.Profiles.FromSql($"""
        INSERT INTO profile.profiles (user_id, display_name, bio, avatar_key, created_at, updated_at)
        VALUES ({userId}, {displayName}, {bio}, NULL, {now}, {now})
        ON CONFLICT (user_id) DO UPDATE
           SET display_name = EXCLUDED.display_name, bio = EXCLUDED.bio, updated_at = EXCLUDED.updated_at
        RETURNING *
        """).AsNoTracking().ToListAsync(ct);

    return rows.Single();
}
```

`displayName` đã `Trim()` ở service trước khi truyền xuống; `bio` rỗng/toàn khoảng trắng chuẩn hóa thành `null` (Q-D3:
vắng mặt hay null đều xóa).

**Bước 3 — controller.** `[HttpPut("me/profile")]`, `[Authorize]`, `service.UpsertAsync(User.GetUserId(), request, ct)`,
`ToActionResult`. Mã: 200/400/401.

**Bước 4 — test.** `PROF-01` (200, đọc lại bằng `GET` → cùng dữ liệu, `avatarUrl: null`); `PROF-02` (đổi tên → 200, đếm
dòng bằng Npgsql = 1, `updatedAt` mới > cũ, `createdAt` không đổi); bốn case 400 `errors.displayName` (1 ký tự, 51 ký tự,
`"   "`, và `"  An  "` — hai ký tự sau trim → **200**, không phải 400); `bio` 501 → `errors.bio`; `{ "displayName": "An",
"nickname": "x" }` → 400 (field lạ); **song song**: `Task.WhenAll` hai `PUT` lần đầu của cùng user → cả hai 200, một dòng.
Sau `D2`, xóa test khung (a) của `D0` và thêm test 200 cho `D1`.

**Bước 5 — ghi ngược Q-D3** vào Mục 8.1 `giai-doan-2.md` trong chính commit này.

### Cạm bẫy đã biết

- **`ON CONFLICT` thiếu `updated_at` trong `SET`** → `PROF-02` đỏ (`updated_at` đứng yên). `DEFAULT now()` chỉ chạy lúc INSERT.
- **Đưa `avatar_key` vào `SET` của `ON CONFLICT`** → sửa tên là mất avatar. Cột đó chỉ `D3` đụng.
- **Lưu `displayName` chưa trim** → hợp đồng đo "sau trim" nhưng DB giữ khoảng trắng; `GET` trả `"  An  "`. Trim ở service,
  một lần, trước validator lẫn store nhìn thấy nó… — không: validator chạy **trước** service (auto-validation), nên
  validator tự trim khi đo, service trim khi lưu. Hai lần `Trim()` là đúng, đừng gộp.
- **`RETURNING *` với `FromSql` cần `AsNoTracking`** khi không định sửa tiếp — tránh tracker giữ một entity mà transaction đã đóng.
- **`FromSql` + `SingleAsync()` trên `INSERT … RETURNING` là 500, không phải chạy được.** Xem "Thực tế thi công" bên dưới —
  đoạn mã ở Bước 2 đã sửa theo.

### Thực tế thi công

**Bằng chứng.** `dotnet test SocialApp.sln`: Unit 117 → 126 (+9 `UpsertProfileRequestValidatorTests`), Architecture 13
(không đổi), Integration 201 → 216 (+14 `UpsertProfileTests`, +1 nhánh 200 của `ProfileTests`). Thử cho đỏ ở local rồi
khôi phục:

| Đột biến | Test đỏ |
|---|---|
| Bỏ `updated_at` khỏi `SET` của `ON CONFLICT` | `PROF_02_…` |
| Thêm `created_at = EXCLUDED.created_at` vào `SET` | `PROF_02_…` |
| Bỏ `.Trim()` ở `ProfileService.UpsertAsync` | `DisplayName_thua_khoang_trang_van_200_va_duoc_luu_da_trim` |
| Bỏ chuẩn hóa `bio` rỗng → `null` | `Q_D3_bio_vang_mat_null_rong_hay_toan_khoang_trang_deu_xoa_bio` |
| Thêm `avatar_key = EXCLUDED.avatar_key` vào `SET` | `Sua_ho_so_khong_lam_mat_avatar_da_dat` — **lần thử đầu KHÔNG bắt được**, xem bên dưới |

**Chỗ lệch so với các bước trên — đã làm như sau:**

- **Bước 2 sai một dòng: `SingleAsync()` sau `FromSql` cho 500, không phải chạy được.** EF xếp `INSERT … RETURNING` vào
  loại SQL **non-composable**, mà `SingleAsync` thì thêm `LIMIT 2` — tức là *compose* lên trên nó — nên EF ném
  `InvalidOperationException` ("was called with non-composable SQL and with a query composing over it") **trước khi chạm
  DB**. Triệu chứng ở tầng HTTP chỉ là 500 trần, không chỉ vào nguyên nhân. Đã đổi sang `.AsNoTracking().ToListAsync(ct)`
  rồi `.Single()` ở client: `ToListAsync` không sửa một ký tự nào của SQL nên hợp lệ, vẫn đúng một round-trip, và
  `Single()` vẫn khẳng định "đúng một dòng". **Bọc bằng CTE không cứu được** — Postgres đòi CTE ghi dữ liệu phải ở top
  level, mà EF sẽ nhét nó vào subquery. Đoạn mã ở Bước 2 đã sửa trong cùng commit.
- **Đột biến `avatar_key` lọt qua toàn bộ bộ test viết theo Bước 4.** Cạm bẫy "đưa `avatar_key` vào `SET`" có trong danh
  sách nhưng không có test nào canh nó, vì D2 không có đường API nào đặt được `avatar_key` và D3 thì chưa tồn tại. Đã
  thêm `Sua_ho_so_khong_lam_mat_avatar_da_dat`, dựng trạng thái bằng **SQL trực tiếp** (`ModulesTestClient.ExecuteSqlAsync`,
  mới) — ngoại lệ có chủ đích với nếp "dựng dữ liệu qua API thật", vì ở đây không có API thật để dùng. D3 tới thì thay
  nhánh SQL đó bằng `PUT /users/me/avatar`.
- **`UserProfile.BioMaxLength` là hằng MỚI của entity** (Bước 1 đặt `MaxBioLength` trong validator). Lý do: cột DB
  `varchar(500)` và validator phải đọc **cùng một** hằng, đúng như `display_name` đã làm từ khối A —
  `UserProfileConfiguration` nay dùng `UserProfile.BioMaxLength` thay cho số 500 gõ tay. Impact analysis của `UserProfile`:
  **MEDIUM**, 5 phụ thuộc, 0 execution flow; thay đổi là *thêm* một hằng nên không chỗ nào phải sửa theo.
- **Một thông điệp cho cả ba nhánh độ dài của `displayName`** (quá ngắn / quá dài / toàn khoảng trắng), đúng đoạn mã mẫu.
  Ghi ra đây vì nó là lựa chọn, không phải mặc định: ba câu khác nhau chỉ làm FE phải nghĩ xem hiện câu nào.
- **`SocialApp.UnitTests` nay tham chiếu `SocialApp.Modules.Profile`** — validator là hàm thuần trên DTO nên biên độ dài
  kiểm ở tầng unit (9 ca) rẻ hơn 9 lượt HTTP; integration chỉ giữ ca chứng minh validator **được nối vào** đường request.
- **Bước 5 (ghi ngược Q-D3) đã xong từ trước**: dòng `UpsertProfileRequest` ở Mục 8.1 của `giai-doan-2.md` đã mang
  *"vắng mặt HOẶC null = xóa — PUT là thay thế toàn phần, chốt Q-D3"* ngay lúc chốt quyết định. Không sửa lại; thay vào
  đó Q-D3 được **canh bằng máy** ở `Q_D3_bio_…` với đủ bốn cách nói "không có bio".
- **`ModulesTestClient` thêm `PutProfileAsync`/`PutProfileOkAsync`** (gửi body ẩn danh chứ không phải DTO — hợp đồng phân
  biệt "bio vắng mặt" với "bio null" ở mức JSON, mà DTO thì luôn phát ra cả hai trường), `QueryRowAsync`, `ExecuteSqlAsync`.
- **Test khung (a) của `D0` không còn gì để xóa ở đây** — nó đã chết ở `D1`, xem "Thực tế thi công" của `D1`.

---

## 5. D3 — `PUT` + `DELETE /users/me/avatar`

**Mục tiêu.** Đóng luồng của E3 với đủ ba lớp: tiền tố người gọi (Đ-2.7, 403) → object có thật (HEAD, 400) → đúng loại
(400). `DELETE` chỉ gỡ liên kết; object để worker (Đ-2.10 — và Mục 7.5 đã ghi avatar mồ côi là nợ có địa chỉ của Profile).

**Kết quả mong đợi.** `SetAvatarRequest { MediaKey = "" }` + validator (regex của hợp đồng);
`ProfileService.SetAvatarAsync(actorId, mediaKey)` / `RemoveAvatarAsync(actorId)`; `IProfileStore.SetAvatarKeyAsync(userId, key?)`
trả `bool` (có dòng hay không); `PROF-04` + các case ở bảng Mục 0; ghi ngược Q-D9 vào `profile-v1.yaml` + `gen:api`.

### Các bước

**Bước 1 — validator.** `Matches(@"^avatars/[0-9a-f-]{36}/[0-9a-f]{32}\.(jpg|png|webp)$")` với thông điệp "Khóa ảnh không
đúng dạng." — chuỗi regex chép **nguyên** từ `SetAvatarRequest.mediaKey.pattern` của yaml. Đây là lớp "sai dạng → 400".

**Bước 2 — service, đúng thứ tự ba lớp:**

```csharp
public async Task<Result<ProfileResponse>> SetAvatarAsync(Guid actorId, string mediaKey, CancellationToken ct)
{
    // Tầng 3 (Đ-2.7): key của người khác → 403, CÙNG phản hồi với mọi 403 khác. Trước HEAD: không tốn một lời gọi R2 cho kẻ dò key.
    if (!StorageKeys.BelongsTo(mediaKey, StorageKeys.AvatarsPrefix, actorId))
        return Result<ProfileResponse>.Forbidden();

    // Đ-2.8 lớp 2: object có thật và đúng loại. Dung lượng KHÔNG kiểm ở đây: Content-Length nằm trong chữ ký PUT (lớp 1),
    // object to hơn khai báo không thể vào bucket qua URL ta ký.
    var head = await storage.HeadAsync(mediaKey, ct);
    if (head is null) return ProfileErrors.AvatarNotUploaded;
    if (!StorageKeys.IsAllowedContentType(head.ContentType)) return ProfileErrors.AvatarTypeNotAllowed;

    var profile = await profiles.SetAvatarKeyAsync(actorId, mediaKey, time.GetUtcNow(), ct);
    return profile is null ? Result<ProfileResponse>.Forbidden() : ToResponse(profile);   // null = chưa có hồ sơ (Q-D9)
}
```

`RemoveAvatarAsync`: `SetAvatarKeyAsync(actorId, null, now)` rồi **luôn** `Result.Success()` — không có hồ sơ cũng 204.

**Bước 3 — store.** `UPDATE profile.profiles SET avatar_key = {key}, updated_at = {now} WHERE user_id = {userId} RETURNING *`
qua `FromSql` (một câu, gán `updated_at` tay), `SingleOrDefaultAsync`. Không xóa object, không gọi `DeleteAsync`.

**Bước 4 — controller.** `[HttpPut("me/avatar")]` 200/400/401/403; `[HttpDelete("me/avatar")]` 204/401.

**Bước 5 — test.** `PROF-04` (key dưới id khác → 403, `title` "Bị từ chối", `detail` không chứa key); key sai dạng
(`avatars/abc.jpg`, `posts/{me}/….jpg`) → 400 `errors.mediaKey`; key đúng dạng của mình nhưng **không `Put`** → 400 với câu
"Ảnh chưa được tải lên xong…" và `title` "Dữ liệu không hợp lệ" (đây là integration test đầu tiên của `Error.Validation`);
`Put(key, 1024, "text/html")` → 400 câu loại ảnh; hợp lệ → 200 `avatarUrl` bắt đầu `https://fake.invalid/get/avatars/{me}/`;
đổi avatar lần hai → `fake.Deleted` **vẫn rỗng**; `DELETE` → 204 → `GET` `avatarUrl: null`; `DELETE` lần hai → 204;
chưa có hồ sơ mà `PUT` → 403 (Q-D9).

### Cạm bẫy đã biết

- **HEAD trước kiểm tiền tố** → mỗi lần kẻ dò gửi key của người khác là một lời gọi R2 thật, và R2 tính tiền theo lời gọi.
  Tiền tố trước, HEAD sau.
- **Xóa object cũ khi đổi avatar "cho sạch"** → Đ-2.10 nói không xóa trong request; và nếu `UPDATE` rollback sau khi đã xóa
  thì hồ sơ trỏ vào object không còn. Để worker (nợ có địa chỉ, Mục 7.5).
- **Dùng `MediaHeadPolicy` của Content cho avatar** → import chéo module, `ModuleBoundaryTests` đỏ. Profile có câu riêng
  trong `ProfileErrors` (câu của yaml khác một chữ: "…rồi thử lại" thay vì "…rồi đăng lại").
- **Quên `pnpm gen:api` sau khi sửa `description` của 403** → cổng codegen đòi worktree sạch → đỏ.

---

## 6. D4 — `POST /media/uploads`

**Mục tiêu.** Một request cho tối đa 10 file (Đ-2.15); hai mức quyền theo `purpose` (Đ-2.6, Q-D5); `requiredHeaders` để FE
`PUT` đúng header đã ký (Đ-2.8 lớp 1); **không log `uploadUrl`**. Endpoint này **không chạm DB**.

**Kết quả mong đợi.** `CreateUploadsRequest { UploadPurpose? Purpose; List<UploadFileDeclaration> Files }` + validator;
`UploadTicket(MediaKey, UploadUrl, ExpiresIn, RequiredHeaders)`; `UploadTicketService.Create(actorId, purpose, files)` (đồng bộ —
presign là HMAC cục bộ, không I/O); `MediaController` với kiểm quyền Q-D5; bảng test ở Mục 0; ghi ngược Q-D5 vào Đ-2.6.

### Các bước

**Bước 1 — DTO.** `UploadPurpose` là enum `{ Post, Avatar }` trong `Application/Media/` (camelCase → `post`/`avatar`).
`UploadFileDeclaration { ContentType = ""; long SizeBytes }`. `RequiredHeaders` là record với
`[JsonPropertyName("Content-Type")] string ContentType` và `[JsonPropertyName("Content-Length")] string ContentLength` —
tên key có gạch nối nên **không** để `Dictionary` mặc định camelCase hóa thành `contentType`.

**Bước 2 — validator.**

```csharp
RuleFor(x => x.Purpose).NotNull().WithMessage("Mục đích tải lên là bắt buộc.");
RuleFor(x => x.Files).NotEmpty().WithMessage("Cần ít nhất một file.")
    .Must(f => f.Count <= PostContentPolicy.MaxMediaCount).WithMessage($"Tối đa {PostContentPolicy.MaxMediaCount} file một lần.");
RuleForEach(x => x.Files).ChildRules(f =>
{
    f.RuleFor(d => d.ContentType).Must(StorageKeys.IsAllowedContentType).WithMessage(Allowlist);
    f.RuleFor(d => d.SizeBytes).InclusiveBetween(1, MediaAttachment.MaxSizeBytes).WithMessage(Allowlist);
}).OverridePropertyName("files");   // key phải là `files`, không phải `files[0].sizeBytes` — hợp đồng ghi `errors.files`
```

`Allowlist` = "Chỉ nhận ảnh JPEG, PNG hoặc WebP, tối đa 10 MB mỗi ảnh." (câu của yaml). Kiểm `OverridePropertyName` có cho
key đúng bằng cách đọc `errors` trong test — nếu FluentValidation vẫn sinh `files[2].sizeBytes` thì hạ về `Must` trên cả
danh sách với một thông điệp.

**Bước 3 — service.**

```csharp
public IReadOnlyList<UploadTicket> Create(Guid actorId, UploadPurpose purpose, IReadOnlyList<UploadFileDeclaration> files)
{
    var expiresIn = R2Options.PutUrlMinutes * 60;   // 600 — cùng hằng số C2 dùng để ký
    return files.Select(f =>
    {
        var key = purpose == UploadPurpose.Post ? StorageKeys.ForPost(actorId, f.ContentType) : StorageKeys.ForAvatar(actorId, f.ContentType);
        var url = storage.CreatePresignedPut(key, f.ContentType, f.SizeBytes);
        return new UploadTicket(key, url, expiresIn, new RequiredHeaders(f.ContentType, f.SizeBytes.ToString()));
    }).ToList();
}
```

Log: `LogInformation("Cấp {Count} ticket tải lên ({Purpose})", ...)` — **không** có key, **không** có URL.

**Bước 4 — controller** theo đoạn mã ở Q-D5. `StatusCode(201, tickets)` (như `Register`). Mã: 201/400/401/403.

**Bước 5 — test** `Content/MediaUploadsTests`: bảng Mục 0. Cho 403: `TestJwt.Create("GUEST", userId)` — vai trò không có
trong `role_permissions` → cache trả rỗng → `AuthorizeAsync` thất bại; **cùng token** `purpose=avatar` → 201 (chứng minh
hai mức quyền tách nhau). Cho log: `CapturingLogSink` không có dòng nào chứa `fake.invalid/put`.

**Bước 6 — ghi ngược Q-D5** vào Đ-2.6.

### Cạm bẫy đã biết

- **`[RequirePermission("post.create")]` lên cả endpoint** → người không có quyền đăng bài không đổi được avatar (Đ-2.6 nói rõ).
- **Gọi `IPermissionCache` thẳng** → Admin bị 403 (Q-D5).
- **`Content-Length` trả dạng số** → FE so sánh với `file.size` thì được, nhưng hợp đồng ghi `string` và header HTTP là chuỗi;
  giữ `string` để codegen khớp.
- **Test 403 bằng cách xóa dòng `role_permissions` của USER** → `PermissionCache` có TTL, và DB là của riêng lớp test nhưng
  cache là của app — đỏ/xanh tùy thứ tự. Dùng vai trò không tồn tại.

---

## 7. D5 — `POST /posts`

**Mục tiêu.** SEQ-01 bước 6–7. Thứ tự kiểm là **một phần của hợp đồng** vì nó quyết định mã lỗi (yaml đã ghi 6 bước):
quyền → hồ sơ → tiền tố → BR-01 → HEAD (**trước** transaction) → một transaction → 409 nếu đụng UNIQUE.

**Kết quả mong đợi.** `CreatePostRequest { string? Body; PostPrivacy? Privacy; List<MediaKeyDeclaration>? MediaKeys }` +
validator (BR-01 tĩnh, regex key, không trùng key); `PostService.CreateAsync(actorId, request)` → `Result<PostResponse>`;
`IPostStore.AddWithMediaAsync(post, attachments)` → `bool`; `PostResponseMapper`; bảng test Mục 0; `TC-A03-media` xanh.

### Các bước

**Bước 1 — validator** (BR-01 phần không cần I/O, dùng hàm thuần để một nguồn thông điệp):

```csharp
RuleFor(x => x.Privacy).NotNull().WithMessage("Mức riêng tư là bắt buộc.");
RuleFor(x => x).Custom((r, ctx) =>
{
    var v = PostContentPolicy.Validate(r.Body, r.MediaKeys?.Count ?? 0);   // AC-02 → body, AC-03 → mediaKeys
    if (!v.IsValid) ctx.AddFailure(v.ErrorKey!, v.Message!);
});
RuleFor(x => x.MediaKeys).Must(m => m is null || m.Select(k => k.MediaKey).Distinct(StringComparer.Ordinal).Count() == m.Count)
    .WithMessage("Một ảnh không được đính kèm hai lần.");
RuleForEach(x => x.MediaKeys).ChildRules(k =>
{
    k.RuleFor(d => d.MediaKey).Matches(@"^posts/[0-9a-f-]{36}/[0-9a-f]{32}\.(jpg|png|webp)$");
    k.RuleFor(d => d.ContentType).Must(StorageKeys.IsAllowedContentType);
    k.RuleFor(d => d.SizeBytes).InclusiveBetween(1, MediaAttachment.MaxSizeBytes);
}).OverridePropertyName("mediaKeys");
```

**Bước 2 — service.** Đọc cùng với bước 6 của yaml:

```csharp
public async Task<Result<PostResponse>> CreateAsync(Guid actorId, CreatePostRequest req, CancellationToken ct)
{
    // (2) Đ-2.4 — có hồ sơ? MỘT lời gọi batch, và UserCard này dùng lại cho response.
    var cards = await directory.GetManyAsync([actorId], ct);
    if (!cards.TryGetValue(actorId, out var author))
        return Result<PostResponse>.Forbidden();

    var media = req.MediaKeys ?? [];

    // (3) Đ-2.7 — MỌI key thuộc posts/{actorId}/ (TC-A03-media). Trước HEAD, như D3.
    if (media.Any(m => !StorageKeys.BelongsTo(m.MediaKey, StorageKeys.PostsPrefix, actorId)))
        return Result<PostResponse>.Forbidden();

    // (4) BR-01 lần nữa bằng hàm thuần — rẻ, và service không giả định mình luôn được gọi qua MVC.
    var br01 = PostContentPolicy.Validate(req.Body, media.Count);
    if (!br01.IsValid) return ContentErrors.FromValidation(br01);

    // (5) Đ-2.8 lớp 2 — HEAD từng key, TRƯỚC transaction. Dừng ở lỗi đầu tiên.
    foreach (var m in media)
    {
        var head = await storage.HeadAsync(m.MediaKey, ct);
        var check = MediaHeadPolicy.Check(new MediaDeclaration(m.ContentType, m.SizeBytes), head);
        if (!check.IsValid) return ContentErrors.FromValidation(check);
    }

    // (6) Một transaction. media_count ĐÚNG ngay từ INSERT (ck_posts_not_empty). position = chỉ số trong mảng.
    var now = time.GetUtcNow();
    var post = new Post { AuthorId = actorId, Body = NormalizeBody(req.Body), Privacy = req.Privacy!.Value,
                          MediaCount = (short)media.Count, CreatedAt = now, UpdatedAt = now };
    var attachments = media.Select((m, i) => new MediaAttachment
    {
        OwnerType = MediaOwnerType.Post, OwnerId = post.PostId, StorageKey = m.MediaKey, ContentType = m.ContentType,
        SizeBytes = (int)m.SizeBytes, Position = (short)i, CreatedAt = now,
    }).ToList();

    if (!await posts.AddWithMediaAsync(post, attachments, ct))
        return ContentErrors.MediaAlreadyUsed;   // 409, POST-08

    logger.LogInformation("Đã tạo bài {PostId} với {MediaCount} ảnh", post.PostId, post.MediaCount);   // không key, không URL
    return mapper.ToResponse(post, attachments, author, actorId);
}
```

`NormalizeBody`: rỗng/toàn khoảng trắng → `null` (để `body` của bài chỉ ảnh là `null` như ví dụ hợp đồng, và CHECK
`btrim(coalesce(body,''))` nhất quán với `PostContentPolicy`). Không `Trim()` nội dung có chữ — người dùng cố ý xuống dòng đầu.

**Bước 3 — store.** Một `SaveChangesAsync` cho `Add(post)` + `AddRange(attachments)` = một transaction ngầm của EF; bắt đúng
index:

```csharp
private const string StorageKeyUniqueIndex = "IX_media_attachments_storage_key";   // tên do migration InitialContent sinh

public async Task<bool> AddWithMediaAsync(Post post, IReadOnlyList<MediaAttachment> attachments, CancellationToken ct)
{
    db.Posts.Add(post);
    db.MediaAttachments.AddRange(attachments);
    try { await db.SaveChangesAsync(ct); return true; }
    catch (DbUpdateException ex) when (ex.InnerException is PostgresException
           { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: StorageKeyUniqueIndex })
    {
        db.ChangeTracker.Clear();   // entity đã Add mà không lưu được thì không được nằm lại trong tracker của scope này
        return false;               // chỉ ĐÚNG index này là 409; uq_media_owner_position hay PK vi phạm là lỗi thật → 500
    }
}
```

**Bước 4 — `PostResponseMapper`** (`Application/Posts/`): `ToResponse(post, attachments, UserCard? author, actorId)` →
`PostResponse` với `Media` sắp theo `Position`, mỗi `url = storage.CreatePresignedGet(key)`, `Author.AvatarUrl` ký từ
`author.AvatarKey`, `CanEdit = post.AuthorId == actorId`, `ReactionCounts = post.ReactionCounts` (dictionary — `{}` khi rỗng,
**không** null), `EditedAt = post.EditedAt`. Bản batch `ToResponses(posts, attachmentsByPost, cards, actorId)` cho `D6`. Dự
phòng tác giả theo Q-D8.

**Bước 5 — controller.** `[HttpPost("posts")]`, `[RequirePermission(ContentPermissions.PostCreate)]`,
`StatusCode(201, result.Value)` khi thành công. Mã: 201/400/401/403/409.

**Bước 6 — test** `Content/CreatePostTests`: bảng Mục 0. `AC-04`: token `TestJwt.Create("USER", userId, issuedAt: -1h)`.
`POST-08`: gọi hai lần cùng `mediaKeys` → lần hai 409 `title` "Xung đột dữ liệu", đếm `content.posts` = 1. Kiểm HEAD:
`fake.HeadCalls` trước/sau chênh đúng `mediaKeys.Count`. Đường 403 "chưa có hồ sơ": user mới, không `PUT profile`, `POST /posts`
→ 403; sau `PUT profile` → 201 (một test, hai bước — chứng minh đúng nguyên nhân).

### Cạm bẫy đã biết

- **HEAD sau khi đã `Add` vào tracker** hoặc trong cùng `using var tx` → "có bài rồi mới phát hiện ảnh sai". Điều 2 của B.9:
  không test nào bắt được, chỉ code review. Giữ HEAD ở service, transaction ở store, hai hàm khác nhau.
- **`media_count` gán sau `Add`** hoặc `INSERT 0 rồi UPDATE` → `ck_posts_not_empty` nổ ở INSERT với bài chỉ ảnh (`BR01-06`).
- **Bắt mọi `UniqueViolation` thành 409** → vi phạm `uq_media_owner_position` (lỗi của chính ta khi gán `position`) bị che
  thành "ảnh đã dùng". So `ConstraintName`.
- **`MediaAttachment.SizeBytes` là `int`, `sizeBytes` của hợp đồng là int64** → ép kiểu chỉ an toàn vì validator đã chặn
  ≤ 10 MB; đừng bỏ `InclusiveBetween` rồi tin `(int)`.
- **`ReactionCounts` trả `null`** khi ai đó đổi mapper sang `post.ReactionCounts.Count == 0 ? null : …` "cho gọn" → FE
  của `E5` vỡ ở GĐ3. Hợp đồng ghi `{}`.
- **Đường `403` cho "chưa có hồ sơ" và "key của người khác" trả hai `detail` khác nhau** → status code không lộ nhưng thông
  điệp lộ. Cả hai đều `Result.Forbidden()` — yaml ghi rõ *"cùng một phản hồi, không nêu lý do nào"*.

---

## 8. D6 — `GET /posts/{postId}` + `GET /users/{userId}/posts`

**Mục tiêu.** BR-02 tại thời điểm đọc (Mục 7.4), cursor keyset (Đ-2.11), tác giả lấy theo lô (Đ-2.3).

**Kết quả mong đợi.** `PostReadService.GetAsync(postId, actorId)`, `ListByUserAsync(userId, actorId, cursor?, limit)`;
`PostCursor.Encode/TryDecode`; `ListUserPostsQuery { string? Cursor; int? Limit }` `[FromQuery]` + validator;
`IPostStore.FindAsync`, `ListByAuthorAsync(authorId, actorId, areFriends, cursor?, take)`, `MediaOfAsync(postIds)`;
bảng test Mục 0; `READ-01` xanh; unit test `PostCursorTests`.

### Các bước

**Bước 1 — BR-02 là một hàm, dùng cho cả hai endpoint:**

```csharp
// Application/Posts/PostVisibility.cs — thuần, unit test được
public static bool CanView(PostPrivacy privacy, Guid authorId, Guid actorId, bool areFriends) => privacy switch
{
    PostPrivacy.Public => true,
    PostPrivacy.Private => authorId == actorId,
    PostPrivacy.Friends => authorId == actorId || areFriends,   // GĐ2: areFriends luôn false (AlwaysStrangers)
    _ => false,
};
```

**Bước 2 — đọc một bài.** `FindAsync` (query filter đã loại `deleted`) → null → `ContentErrors.PostNotFound`; tính
`areFriends` **chỉ khi** `privacy == Friends && authorId != actorId` (tiết kiệm một lời gọi ở GĐ4); `!CanView` → **cùng**
`PostNotFound` (quy ước 3b — 404, không 403); rồi `MediaOfAsync([postId])`, `directory.GetManyAsync([authorId])`, mapper.

**Bước 3 — cursor.**

```csharp
// Application/Posts/PostCursor.cs
public readonly record struct PostCursor(DateTimeOffset CreatedAt, Guid PostId)
{
    // .NET 8 chưa có Base64Url (GĐ1 đã ghi) — tự thay ký tự và bỏ '='.
    public string Encode() => ToBase64Url(Encoding.UTF8.GetBytes($"{CreatedAt:O}|{PostId:D}"));

    public static bool TryDecode(string? raw, out PostCursor cursor)
    {
        cursor = default;
        if (string.IsNullOrEmpty(raw) || !TryFromBase64Url(raw, out var bytes)) return false;
        var parts = Encoding.UTF8.GetString(bytes).Split('|');
        if (parts.Length != 2
            || !DateTimeOffset.TryParseExact(parts[0], "O", CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var at)
            || !Guid.TryParseExact(parts[1], "D", out var id))
            return false;
        cursor = new PostCursor(at.ToUniversalTime(), id);   // Npgsql đòi offset 0 cho timestamptz — cursor +07:00 phải về UTC, không được ném
        return true;
    }
}
```

Unit test: round-trip giữ nguyên tick; `"abc"`, `""`, base64 của `"x|y"`, `"2026-01-01T00:00:00+07:00|<guid>"` → decode
được và `CreatedAt.Offset == 0`; rác → `false` không ném.

**Bước 4 — validator của query.** `Limit` null → 20; `InclusiveBetween(1, 50)` "Số bài mỗi trang phải từ 1 đến 50.";
`Cursor` null hoặc `TryDecode` thành công, ngược lại "Cursor không hợp lệ." (câu của yaml). Auto-validation áp cho tham
số `[FromQuery]` phức hợp — không cần gọi tay.

**Bước 5 — danh sách.** `areFriends` tính **một lần** trước truy vấn (endpoint là bài của **một** người):

```csharp
var areFriends = userId != actorId && await friends.AreFriendsAsync(actorId, userId, ct);
var take = limit + 1;                                    // +1 để biết còn trang sau
var rows = await posts.ListByAuthorAsync(userId, actorId, areFriends, cursor, take, ct);
var page = rows.Take(limit).ToList();
var next = rows.Count > limit ? new PostCursor(page[^1].CreatedAt, page[^1].PostId).Encode() : null;   // từ bài CUỐI của trang, không phải bài thứ limit+1
```

Store: BR-02 **trong** `WHERE` (Mục 7.4, B.6 nhấn mạnh): `p.Privacy == Public || p.AuthorId == actorId || (areFriends && p.Privacy == Friends)`
— `areFriends` là hằng đã tính nên EF gấp thành `TRUE`/`FALSE`. Sắp `OrderByDescending(CreatedAt).ThenByDescending(PostId)`,
mệnh đề keyset theo Q-D6. Rồi `MediaOfAsync(page ids)` **một** câu, `GetManyAsync(distinct authorIds)` **một** câu (ở endpoint
này luôn là một id — nhưng viết theo lô để GĐ4 chép nguyên).

**Bước 6 — controller.** `[HttpGet("posts/{postId}")]`, `[HttpGet("users/{userId}/posts")]`, cả hai
`[RequirePermission(ContentPermissions.PostReadPublic)]`. Mã: 200/400/401/404 và 200/400/401.

**Bước 7 — test** `Content/ReadPostTests`, `Content/ListPostsTests`: bảng Mục 0. `PAGE-03`: tạo 25 bài, lấy trang 1, **tạo
thêm 1 bài**, lấy trang 2 bằng `nextCursor` → 5 bài, không bài nào trùng trang 1, bài mới **không** xuất hiện (nó đứng trước
cursor) — đúng bản chất keyset. Đếm SQL: lọc `CapturingLogSink` các dòng `Executed DbCommand` chứa `profile.profiles` trong
một request trang → **1**.

### Cạm bẫy đã biết

- **Lọc BR-02 sau khi `Take(limit)`** → trang thiếu hụt ngẫu nhiên, FE tưởng hết dữ liệu (B.6 nói thẳng).
- **`nextCursor = ""` khi hết** → Đ-2.11 nói `null`; FE kiểm `if (nextCursor)` thì `""` là falsy nên tình cờ đúng, nhưng
  codegen ghi `string | null` và `""` là lệch hợp đồng.
- **Cursor sai dạng trả trang đầu** → người dùng cuộn mãi không hết (Đ-2.11). Validator chặn thành 400.
- **Quên `ToUniversalTime()`** → Npgsql ném "Cannot write DateTimeOffset with Offset=07:00:00 to PostgreSQL type
  'timestamp with time zone'" → 500 từ một cursor do client sửa tay.
- **Dùng `IgnoreQueryFilters()` "để tác giả xem lại bài đã xóa"** → Mục 7.3 nói bài đã xóa không còn là tài nguyên; và đây là
  chỗ code review chặn (luật 7).
- **Nhánh `friends` gọi `AreFriendsAsync` cho từng bài** → N lời gọi ở GĐ4. Ở endpoint theo người dùng, tính một lần.

---

## 9. D7 — `PATCH /posts/{postId}`

**Mục tiêu.** Sửa `body`/`privacy` của bài mình theo **đúng khuôn Mục 6.2** — dòng `TC-A03` của matrix chuyển từ đỏ sang xanh
ở đây, và đó là bằng chứng của `B3`.

**Kết quả mong đợi.** `UpdatePostRequest { string? Body; PostPrivacy? Privacy }` + validator ("ít nhất một trường");
`PostService.UpdateAsync(postId, actorId, request)`; `IPostStore.FindForUpdateAsync` + `SaveAsync`; bảng test Mục 0.

### Các bước

**Bước 1 — validator.** `RuleFor(x => x).Must(r => r.Body is not null || r.Privacy is not null).WithName("body").WithMessage("Không có gì để sửa.")`;
`Body` `MaximumLength(PostContentPolicy.MaxBodyLength)`. `mediaKeys` không cần rule: `UnmappedMemberHandling = Disallow` đã trả 400.

**Bước 2 — service, chép nguyên khuôn Mục 6.2:**

```csharp
public async Task<Result<PostResponse>> UpdateAsync(Guid postId, Guid actorId, UpdatePostRequest req, CancellationToken ct)
{
    var post = await posts.FindForUpdateAsync(postId, ct);   // tracked; query filter đã loại deleted

    // Tầng 3. KHÔNG có nhánh Admin. "Không tồn tại", "không phải của bạn", "đã xóa" → CÙNG 403 (quy ước 3b, TC-A03).
    if (post is null || post.AuthorId != actorId)
        return Result<PostResponse>.Forbidden();

    if (req.Body is not null)
    {
        var body = NormalizeBody(req.Body);
        var br01 = PostContentPolicy.Validate(body, post.MediaCount);   // BR-01 với số ảnh THẬT của bài
        if (!br01.IsValid) return ContentErrors.FromValidation(br01);
        post.Body = body;
    }
    if (req.Privacy is { } privacy) post.Privacy = privacy;

    post.EditedAt = time.GetUtcNow();
    await posts.SaveAsync(ct);                                  // StampUpdatedAt đóng dấu updated_at

    var attachments = await posts.MediaOfAsync([post.PostId], ct);
    var cards = await directory.GetManyAsync([post.AuthorId], ct);
    return mapper.ToResponse(post, attachments, cards.GetValueOrDefault(post.AuthorId), actorId);
}
```

`body: null` trong PATCH nghĩa là "không gửi" (System.Text.Json không phân biệt — cùng lý do Q-D3); muốn xóa chữ thì gửi
`""`, và khi đó BR-01 quyết định: bài có ảnh → được, bài không ảnh → 400 `errors.body`.

**Bước 3 — controller.** `[HttpPatch("posts/{postId}")]`, `[RequirePermission(ContentPermissions.PostUpdate)]`. Mã: 200/400/401/403.

**Bước 4 — test** `Content/UpdatePostTests`: `POST-06` (200, `editedAt` khác null, `updatedAt` mới, `GET` lại thấy nội dung mới);
`{}` → 400 `errors.body` "Không có gì để sửa."; `{ "mediaKeys": [] }` → 400 (key `mediaKeys`, câu cố định của
`ValidationErrors`); bài chỉ chữ + `{ "body": "" }` → 400 `errors.body` câu `PostContentPolicy.Empty`; bài có ảnh + `{ "body": "" }`
→ 200 `body: null`; đổi `privacy` → người lạ `GET` bài vừa chuyển `private` → 404 (BR-02 tại thời điểm đọc, không phải lúc ghi);
bài **đã xóa** → 403; bài **không tồn tại** → 403 (không phải 404). Matrix: `TC-A03` xanh.

### Cạm bẫy đã biết

- **Trả 404 cho "không tồn tại" và 403 cho "của người khác"** → status code tự tố cáo bài có tồn tại. Một `Result.Forbidden()`.
  Đây là đột biến #3 của bảng `B3`.
- **BR-01 với `req.MediaKeys?.Count`** thay vì `post.MediaCount` → bài có 3 ảnh mà xóa chữ bị 400 oan.
- **Quên `EditedAt`** → `POST-06` đỏ; FE không hiện nhãn "đã chỉnh sửa".
- **`FindForUpdateAsync` dùng `AsNoTracking`** (chép từ `FindAsync` của đọc) → `SaveAsync` không ghi gì, 200 mà DB không đổi.
  Hai hàm store cho hai mục đích.

---

## 10. D8 — `DELETE /posts/{postId}`

**Mục tiêu.** Xóa mềm (Đ-2.10). Sau đó bài "không còn là tài nguyên": chính tác giả `GET` cũng 404, `DELETE` lần hai 403.

**Kết quả mong đợi.** `PostService.DeleteAsync(postId, actorId)` → `Result`; controller 204; ghi ngược Q-D7 vào `content-v1.yaml`
+ `gen:api`; bảng test Mục 0; `TC-A03-delete` xanh.

### Các bước

1. Service: `FindForUpdateAsync` → null hoặc `AuthorId != actorId` → `Result.Forbidden()`; `post.Status = Deleted;
   post.DeletedAt = now;` → `SaveAsync` → `Result.Success()`. **Không** gọi `storage.DeleteAsync`, **không** xóa dòng
   `media_attachments` — cả hai là việc của `MediaCleanupWorker` sau 7 ngày.
2. Controller: `[HttpDelete("posts/{postId}")]`, `[RequirePermission(ContentPermissions.PostDelete)]`,
   `return result.ToActionResult(this);` (thành công → 204). Mã: 204/**400** (Q-D7)/401/403.
3. Yaml: thêm `'400'` cho `delete` của `/posts/{postId}`; `pnpm gen:api`; commit `schema.d.ts`.
4. Test `Content/DeletePostTests`: `POST-07`; `DELETE` lần hai → 403; DB (Npgsql, **không** qua DbSet vì filter): `status =
   'deleted'`, `deleted_at IS NOT NULL`, `COUNT(media_attachments WHERE owner_id = …)` không đổi; `fake.Deleted` rỗng;
   `GET /users/{me}/posts` không còn bài; `DELETE /posts/khong-uuid` → 400 `errors.postId`. Matrix: `TC-A03-delete` xanh.

### Cạm bẫy đã biết

- **`DELETE` lần hai trả 404 "vì bài không còn"** → bảng Mục 6.1 nói thao tác ghi cần ownership dùng 403; và query filter làm
  điều đó xảy ra **tự nhiên** (`FindForUpdateAsync` trả null → Forbidden). Nếu thấy mình viết `if (post.Status == Deleted) return NotFound` thì đang đi vòng.
- **Xóa object "cho sạch bucket"** → bấm nhầm là mất ảnh vĩnh viễn (Đ-2.10), và `MediaCleanupWorkerTests` đang giả định
  dòng `media_attachments` còn để nhánh (2) tìm thấy.
- **Kiểm DB sau xóa bằng `db.Posts.FindAsync`** → filter che mất, test tưởng bài đã bị xóa cứng. Đọc bằng `NpgsqlCommand`.

---

## 11. D9 — Rà RFC 7807 + `[ProducesResponseType]` + `[ApiExplorerSettings]`

**Mục tiêu.** Một hình dạng lỗi cho mười endpoint, và làm cho `B4` chiều 2 xanh — Swagger chỉ thấy mã action khai.
`[ApiExplorerSettings]` đã có từ commit tạo mỗi controller (luật 2) — `D9` chỉ **xác nhận**.

**Kết quả mong đợi.**

- Mỗi action khai **đúng** tập mã của hợp đồng (trừ 429/500 — `CrossCuttingStatusCodes`):

  | Operation | Mã phải khai | Required của request |
  |---|---|---|
  | `GET /users/{userId}/profile` | 200, 400, 401, 404 | — |
  | `PUT /users/me/profile` | 200, 400, 401 | `[displayName]` |
  | `PUT /users/me/avatar` | 200, 400, 401, 403 | `[mediaKey]` |
  | `DELETE /users/me/avatar` | 204, 401 | — |
  | `POST /media/uploads` | 201, 400, 401, 403 | `[purpose, files]` |
  | `POST /posts` | 201, 400, 401, 403, 409 | `[privacy]` |
  | `GET /posts/{postId}` | 200, 400, 401, 404 | — |
  | `PATCH /posts/{postId}` | 200, 400, 401, 403 | *(không có)* |
  | `DELETE /posts/{postId}` | 204, 400 (Q-D7), 401, 403 | — |
  | `GET /users/{userId}/posts` | 200, 400, 401 | — |

- `ContractTestsBase` local: `--filter "Category=Contract"` **6/6 xanh** → **commit `B4`** ngay sau `D9`.
- Bảng rà thông điệp trong PR: từng `Error` của `ProfileErrors`, `ContentErrors`, từng câu validator — không id, key, email,
  tên kiểu, tên bảng. Grep nhanh: `grep -rn "\$\"" src/backend/Modules/Profile/Application src/backend/Modules/Content/Application`
  — mọi chuỗi nội suy phải chỉ chứa **hằng số** (`MaxBodyLength`, `MaxMediaCount`), không biến runtime.
- `ProblemDetailsTests` thêm một case: 400 sinh từ `Error.Validation` (qua `PUT /users/me/avatar` với key của mình chưa `Put`)
  có `title` "Dữ liệu không hợp lệ", `detail` "Dữ liệu đầu vào không hợp lệ", `errors.mediaKey`, `traceId` — chứng minh
  Q-D4 cho ra **cùng** hình dạng với 400 của FluentValidation. Test này cần DB → đặt trong `Profile/AvatarTests`, không
  trong `ProblemDetailsTests` (lớp đó dùng `ApiFactory` không DB); ghi chú chéo ở cả hai.
- Grep kiểm luật: `SystemRoles.Admin|"ADMIN"` trong hai module → 0; `IgnoreQueryFilters` trong hai module → chỉ
  `MediaCleanupWorker.cs`; `uploadUrl|UploadUrl|CreatePresigned` trong các lời gọi `Log*` → 0.

### Các bước

1. Rà từng action theo bảng. **Không khai thừa** — chiều 1 đỏ ngay nếu action khai mã hợp đồng không có.
2. Kiểm required: mở `/swagger/content-v1/swagger.json`, `components.schemas.CreatePostRequest.required` phải là `["privacy"]`
   — `[Required]` trên `PostPrivacy? Privacy` (Q-D2). `UpdatePostRequest` **không** có `required`.
3. Kiểm enum trong cùng file: `PostPrivacy.enum` = `["public","friends","private"]` (Q-D2). Lệch thì ghi vào PR, không đổi cách làm.
4. Chạy trọng tài (đã viết ở `D0` bước 6) — xanh cả hai chiều cho cả hai module → commit `B4`.

### Cạm bẫy đã biết

- **Khai `[ProducesResponseType(429)]` "cho đủ"** → không sai nhưng thừa; hai vế đã trừ 429/500.
- **Sửa yaml cho khớp code khi trọng tài đỏ** → hợp đồng đã chốt và FE đã sinh type. Sửa code; chỉ hai chỗ được sửa yaml
  là Q-D7 và Q-D9, đã báo trước.
- **`title` của 404/409 khác ví dụ yaml** → `ProblemTitles` mặc định đã đúng ("Không tìm thấy tài nguyên", "Xung đột dữ liệu");
  đừng đặt `Title` riêng trong `Error` trừ khi yaml đòi câu khác (khối D không có ca nào).

---

## 12. Kế hoạch commit

Scope `gd2-d` (luật commit Mục 3). Mỗi commit chạm code **phải** có dòng `Test:` và `detect-changes:`, footer **sạch bút ký**.
Mọi commit của khối D **xanh** — bằng chứng "đỏ được" nằm ở `B2`/`B3`.

| # | Tiêu đề | Gồm |
|---|---|---|
| 1 | `feat(gd2-d): D0 — nền chung hai module: nhóm Swagger, Error.Validation, enum chữ thường, harness test` | Mục 2 trọn vẹn; `ContractTestsBase` **không** commit |
| 2 | `feat(gd2-d): D1 — GET /users/{userId}/profile, 404 là tín hiệu onboarding` | + test khung xóa/thay |
| 3 | `feat(gd2-d): D2 — PUT /users/me/profile upsert một câu ON CONFLICT, bio thay thế toàn phần` | + ghi ngược Q-D3 vào Mục 8.1 |
| 4 | `feat(gd2-d): D3 — avatar: tiền tố người gọi, HEAD object thật, gỡ chỉ bỏ liên kết` | + Q-D9 vào `profile-v1.yaml` + `schema.d.ts` |
| 5 | `feat(gd2-d): D4 — POST /media/uploads presign theo lô, quyền theo purpose qua policy tầng 2` | + ghi ngược Q-D5 vào Đ-2.6 (`ContentPermissionsTests` đã vào cùng `ContentPermissions` ở `D0`, Mục 2) |
| — | **Kiểm `B2` đã push, run đỏ đã chụp** | không phải commit của D |
| 6 | `feat(gd2-d): D5 — POST /posts: hồ sơ → tiền tố → BR-01 → HEAD trước transaction → 409 theo UNIQUE` | `TC-A03-media` đỏ → xanh; nói rõ trong thân bài |
| 7 | `feat(gd2-d): D6 — đọc bài với BR-02 tại thời điểm đọc, danh sách keyset, tác giả một lô` | `READ-01` đỏ → xanh; ghi cách chọn Q-D6 |
| 8 | `feat(gd2-d): D7 — PATCH /posts/{postId} theo khuôn tầng 3, đóng dấu edited_at` | `TC-A03` đỏ → xanh |
| 9 | `feat(gd2-d): D8 — DELETE /posts/{postId} xóa mềm, gọi lại là 403, hợp đồng thêm 400 cho id sai dạng` | + Q-D7 vào `content-v1.yaml` + `schema.d.ts` |
| 10 | `feat(gd2-d): D9 — ProducesResponseType khớp mười operation, rà thông điệp không lộ dữ liệu` | + case 400 `Error.Validation` |
| 11 | `test(gd2-b): B4 — ContractTestsBase, hai cổng hợp đồng profile-v1 và content-v1, 6 test` | ngay sau #10 — thuộc khối B, ghi ở đây để không quên |

Gộp `D1`+`D2` hoặc `D7`+`D8` là **được** nếu nói rõ lý do trong thân bài (luật Mục 8); không gộp `D5` với gì cả — nó là
commit dài nhất và là chỗ `TC-A03-media` đổi màu.

Mẫu thân bài cho commit #6:

```
PostService.CreateAsync đi đúng sáu bước của hợp đồng: có hồ sơ (IUserDirectory, Đ-2.4) → mọi key thuộc posts/{actorId}/
(Đ-2.7) → BR-01 bằng PostContentPolicy → HEAD từng key và đối chiếu bằng MediaHeadPolicy TRƯỚC khi mở transaction (Đ-2.8
lớp 2) → một SaveChanges cho post + media với media_count đúng ngay từ INSERT → unique violation của đúng index
IX_media_attachments_storage_key dịch thành 409, index khác vẫn 500.

Lệch Đ-2.6 (đã chốt Q-D5 ở D4): không lệch thêm ở đây. Lệch B.6: không.

TC-A03-media chuyển từ đỏ (run <link> lúc B2) sang xanh ở commit này; TC-A03, TC-A03-delete, READ-01 vẫn đỏ đúng dự kiến
cho tới D6–D8.

Test: Unit 118 → 121 (+PostCursorTests chưa; +validator), Integration 197 → 206 (+CreatePostTests 9; AuthZ 16/19 xanh).
detect-changes: low, 0 luồng
```

---

## 13. Checklist nghiệm thu khối D

Tick từng dòng, có bằng chứng. Dòng nào không áp dụng thì ghi lý do, **không xóa dòng**.

**Kiểm tự động**

- [ ] `dotnet build SocialApp.sln` xanh; `dotnet test SocialApp.sln` xanh, số Unit/Integration/Arch ghi trong PR
- [ ] Integration mới: `ProfileTests` (`PROF-01..03`), `AvatarTests` (`PROF-04` + 400/204), `MediaUploadsTests`, `CreatePostTests`
      (`AC-01`, `AC-04`, `POST-08`, 403 hồ sơ, HEAD đếm), `ReadPostTests` (`READ-02..05`), `ListPostsTests` (`PAGE-01..03`, limit/cursor),
      `UpdatePostTests` (`POST-06` + 400/403), `DeletePostTests` (`POST-07` + 403/DB)
- [ ] Unit mới: `ResultTests` (`Error.Validation`), `PostCursorTests`, `PostVisibilityTests`, ba validator của Content, hai của Profile
- [ ] `ArchitectureTests`: `ContentPermissionsTests` mới xanh; `PresentationBoundaryTests`, `PersistenceBoundaryTests`,
      `PermissionCodeUsageTests` xanh **không sửa rule**
- [ ] AuthZ matrix **19/19** xanh — và sáu dòng GĐ2 **đã từng đỏ** (link run của `B2`)
- [ ] `--filter "Category=Contract"` **6/6** (sau commit `B4`)
- [ ] Nhóm test Postgres vẫn dưới ~3 phút (B1 đo 1 m 23 s trước khối D) — ghi số mới vào PR

**Kiểm tay — Swagger ở dev, ghi bằng chứng vào PR**

- [ ] Đăng nhập lấy token → `GET /users/{me}/profile` 404 → `PUT /users/me/profile` 200 → `GET` 200
- [ ] `POST /media/uploads` `purpose=avatar` → `PUT` file thật lên `uploadUrl` bằng DevTools/probe của `C2` → `PUT /users/me/avatar`
      200, `avatarUrl` mở được trong trình duyệt **trong 15 phút**, hết hạn thì R2 trả 403
- [ ] `POST /posts` với 2 ảnh thật đã `PUT` → 201; `GET /posts/{id}` thấy 2 URL ảnh mở được; đổi `privacy=private` bằng
      `PATCH` → token của user khác `GET` → 404
- [ ] psql: `SELECT media_count, status FROM content.posts` khớp; `SELECT COUNT(*) FROM content.media_attachments` = 2;
      sau `DELETE`: `status='deleted'`, dòng ảnh còn, object trên R2 còn

**Code review — không test tự động nào bắt được (B.9)**

- [ ] `actorId` trong **mọi** service đến từ `User.GetUserId()` ở controller — đọc từng action của `ProfilesController`,
      `MediaController`, `PostsController`
- [ ] `D5`: HEAD ở service, transaction ở store, HEAD **trước** — hai hàm khác nhau
- [ ] Grep `SystemRoles.Admin|"ADMIN"` trong `Modules/Profile`, `Modules/Content` → 0
- [ ] Grep `IgnoreQueryFilters` → chỉ `MediaCleanupWorker.cs`
- [ ] Không lời gọi `Log*` nào mang `uploadUrl`, URL đã ký, `storage_key` kèm id người dùng
- [ ] `IUserDirectory.GetManyAsync` gọi **một lần** mỗi request ở `D6` danh sách (đếm SQL trong test hoặc đọc code)
- [ ] Code khớp các quyết định đã chốt: Q-D2 (converter camelCase, `Privacy` nullable), Q-D3 (`bio` thay thế), Q-D4
      (`Error.Validation` — không `AppException`, không `ModelState` trong controller), Q-D5 (`IAuthorizationService`),
      Q-D7/Q-D9 (yaml + `schema.d.ts` cùng commit)

**Luật repo**

- [ ] Hợp đồng chỉ đổi ở hai chỗ đã báo (Q-D7, Q-D9); mỗi lần đổi có `schema.d.ts` sinh lại **trong cùng commit**
- [ ] `giai-doan-2.md` đã sửa: Mục 8.1 (Q-D3), Đ-2.6 (Q-D5); B.6 có blockquote trỏ tới hướng dẫn này
- [ ] Không secret trong diff (khối D không thêm key cấu hình nào); không khóa R2 ở đâu cả
- [ ] Mọi commit có `Test:` và `detect-changes:`; footer sạch bút ký

**Việc chuyển cho khối F** — khối D **không** thêm biến `.env` nào. `R2__*` và `Media__Cleanup__Enabled` là của khối C
(đã ghi ở PR khối C). Không có migration EF mới.

---

## 14. Khối D để lại gì

| Ai nhận | Nhận cái gì |
|---|---|
| **E2–E6** (lane FE) | Mười endpoint chạy thật đúng hợp đồng ở dev; `canEdit` do server tính; cursor opaque; `reactionCounts: {}` |
| **B3**, **B4** | `D5`/`D7`/`D8` để bảng đột biến có chỗ đột; `D0` (`ApiGroup`) để `ContractTests` có nhóm để so |
| **F1–F3** | Không biến `.env` mới; Swagger `profile-v1` + `content-v1` trên staging để kiểm tay; `GET /posts/{id}` làm smoke |
| **GĐ3** | `PostResponse` đã có `commentCount`/`reactionCounts` — bình luận/cảm xúc không đổi hình dạng DTO; `PostResponseMapper` là chỗ nối |
| **GĐ4** | `PostCursor`, `PostVisibility.CanView`, `ListByAuthorAsync` với BR-02 trong `WHERE`, mapper theo lô — feed chép nguyên và đổi **một dòng DI** cho `IFriendshipReader`; và lời nhắc: cache lưu `storage_key`, không lưu URL đã ký |
| **GĐ5** | `UploadTicketService` + `RequiredHeaders` dùng lại cho media tin nhắn với `purpose` mới |
| **GĐ6** | `post.hide` đã có mã quyền; `Status.Hidden` + `hidden_reason` chờ sẵn; `PostVisibility` là chỗ thêm nhánh BR-07 |
| **GĐ8** | Q-D8 (tác giả vắng mặt) là điểm phải quyết lại khi có xóa tài khoản; `Error.Validation` dùng cho mọi 400 sau I/O |
| **Mọi module sau** | `Error.Validation` + nhánh `ValidationProblem`; khuôn "kiểm quyền theo tham số body bằng `IAuthorizationService`" (Q-D5); `ModulesApiFactory` là factory chuẩn cho module có tài nguyên thuộc sở hữu |

---

## 15. Ranh giới — cái gì **không** thuộc khối D

| Không thuộc khối D | Thuộc về | Vì sao dễ nhầm |
|---|---|---|
| Sáu dòng `AuthZMatrix.cs`, sửa khung `AuthZArrange` (`CallerUserId`) | `B2` | `D5`/`D7`/`D8` là thứ làm chúng xanh, nhưng dòng phải có **trước** |
| Bảng đột biến, `AC-02`, `AC-03`, `BR01-05`, `BR01-06` | `B3` | Chúng gọi `POST /posts` của `D5` |
| `ContractTestsBase` + hai lớp con + hai dòng csproj | `B4` | Viết ở local từ `D0`, nhưng là commit của khối B |
| `R2ObjectStorage`, chữ ký, `HeadAsync`, `MediaHeadPolicy`, `FakeObjectStorage`, worker | Khối C (**xong**) | Khối D chỉ **gọi** chúng |
| Dọn avatar mồ côi | Nợ có địa chỉ của Profile (Mục 7.5) | `D3` gỡ liên kết mà không xóa object — cố ý |
| Kiểm `width`/`height`, sniff magic bytes | GĐ7 / ngoài phạm vi | Hai cột luôn `null` ở GĐ2; hợp đồng đã `nullable` |
| Nới CSP `connect-src`/`img-src` cho R2 | `E7` (Đ-E17) | `avatarUrl`/`media[].url` chỉ hiện được trên FE khi CSP mở |
| Kiểm CORS, `PUT` thật từ trình duyệt qua app | `F3` (và `C2` đã đóng ở dev) | Kiểm tay Mục 13 dùng probe/DevTools, chưa qua app |
| `RequestOptions.method` thêm `PUT/PATCH/DELETE`, `errorMessage` ngữ cảnh mới | `E1` | Lane E, không phải backend |
| Sửa bất kỳ `Đ-2.*` nào ngoài mệnh đề của Q-D5 | Quyết định mới, có ngày, ở `giai-doan-2.md` | Khối D tiêu thụ quyết định, không tạo quyết định |
