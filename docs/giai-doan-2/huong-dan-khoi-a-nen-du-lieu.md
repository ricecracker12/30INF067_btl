# Hướng dẫn thực hiện — Khối A. Nền dữ liệu (GĐ2)

> Bản triển khai chi tiết của **B.3 Khối A** trong [giai-doan-2.md](giai-doan-2.md). Tài liệu gốc trả
> lời *cái gì* và *vì sao*; tài liệu này trả lời *gõ vào file nào, theo thứ tự nào, và nhìn vào đâu để
> biết đã xong thật*.
>
> **Nguồn sự thật vẫn là `giai-doan-2.md`** (Mục 3 quyết định `Đ-2.1`–`Đ-2.15`, Mục 4 schema, Mục 5 dữ
> liệu nền, Mục 10 chiến lược test) và `AGENTS.md`. Chỗ nào tài liệu này lệch với hai file đó thì sửa ở
> đây — không sửa ngược. Muốn đổi một `Đ-2.*` thì đó là **quyết định mới**, có ngày tháng, ghi vào
> `giai-doan-2.md` trong cùng commit.
>
> Khuôn để chép lại nằm ở khối A của GĐ1:
> [huong-dan-khoi-a-nen-du-lieu.md](../giai-doan-1/huong-dan-khoi-a-nen-du-lieu.md). GĐ2 **không phát
> minh khuôn mới** — mọi thứ dưới đây là "làm lại đúng hình dạng đó, hai lần, cho hai module".

| | |
|---|---|
| **Người làm** | BE-1 (một người, lane độc lập) |
| **Thời lượng** | Ngày 6 (A1–A3) → Ngày 7 sáng (A4–A6), A7 đi kèm A1/A4 |
| **Khối này chặn** | `C3` (HEAD lúc commit cần `media_attachments`), **toàn bộ khối D**, `B1` (harness hai context mới), `F1` (deploy staging) |
| **Khối này cần trước** | Chỉ cổng mở (Mục 9 "Cổng mở"). **Không** cần khối C — lưu trữ đối tượng không chạm DB (B.2) |
| **Không thuộc khối này** | Controller, DTO, validator, presign/R2, worker dọn rác, sáu dòng AuthZ matrix, hai file `.yaml` hợp đồng. Xem Mục 12 bên dưới |

---

## 0. Danh sách công việc — mục tiêu và kết quả mong đợi

Bảy đầu việc. `A1→A2→A3` là module Profile, `A4→A5` là module Content, `A6` nối hai module lại bằng
contract chỉ-đọc, `A7` đóng lỗ "test chạy trong chân không". Đây là **đường găng của lane BE-1**: mọi
endpoint của khối D đều đứng trên `A3` và `A5`.

| Mã | Đầu việc | Mục tiêu — việc này tồn tại để làm gì | Kết quả mong đợi — thứ kiểm chứng được |
|---|---|---|---|
| **A1** | Entity hồ sơ trong `Modules/Profile/Domain/` | Có mô hình nghiệp vụ cho Profile, và làm cho `PersistenceBoundaryTests` hết chạy trong chân không trên namespace `Profile.Domain` | 1 file entity trong `SocialApp.Modules.Profile.Domain`, không file nào `using Microsoft.EntityFrameworkCore`; `dotnet build SocialApp.sln` xanh; test canh gác của `A7` (bản Profile) xanh |
| **A2** | `ProfileDbContext` + configuration + `ProfileDbContextOptions` + design-time factory | Đưa module Profile vào đúng khuôn `Đ-2.1`: schema riêng, bảng lịch sử migration riêng, một chỗ duy nhất cấu hình Npgsql cho cả DI lẫn design-time | 4 file trong `Profile/Infrastructure/`; `HasDefaultSchema("profile")`; **không** có `HasPostgresExtension("citext")`; `SaveChanges` đóng dấu `updated_at`; build xanh |
| **A3** | Migration đầu của Profile + `AddProfileModule` + `MigrateProfileModuleAsync` + nối `Program.cs` | Biến schema `profile` thành artifact có version, chạy được bằng **một lệnh** ở bước deploy — không ai "sửa tay trên staging" | `Profile/Infrastructure/Migrations/` có `<timestamp>_InitialProfile.cs` + `.Designer.cs` + snapshot đã commit; `--migrate` trên DB sạch exit 0, **chạy lần hai vẫn exit 0 và không đổi gì**; `ProfileDbContextSchemaTests` xanh trên Postgres thật |
| **A4** | Entity Content trong `Modules/Content/Domain/` + `PostContentPolicy` | Có `Post`/`MediaAttachment` để khối D bám vào, có khung `Comment`/`Reaction` để GĐ3 không phải đổi hình dạng DTO lần thứ hai (`Đ-2.12`), và có BR-01 dạng **hàm thuần** để unit test không cần DB | Entity + enum + `PostContentPolicy` trong `SocialApp.Modules.Content.Domain`, sạch EF; unit test BR-01 xanh (bài chỉ ảnh: hợp lệ · body rỗng + 0 ảnh: không hợp lệ · 11 ảnh: không hợp lệ); test canh gác `A7` (bản Content) xanh |
| **A5** | `ContentDbContext` + configuration + migration | Giao bốn bất biến khó nhất của Mục 4 cho **DB** giữ (`ck_posts_not_empty`, `storage_key` UNIQUE, `uq_media_owner_position`, PK ba cột của `reactions`), và loại bài xóa mềm khỏi mọi truy vấn đọc bằng global query filter (`Đ-2.10`) | 4 bảng trong schema `content` + đủ CHECK/index của Mục 4; `ContentDbContextSchemaTests` xanh và **có test chứng minh CHECK thật sự chặn**, không chỉ "bảng tồn tại"; `--migrate` idempotent cho cả ba module |
| **A6** | Hai contract chéo module ở SharedKernel | Cho Content đọc được tên + avatar tác giả mà **không** import module Profile (`Đ-2.3`), và để GĐ4 bật kết bạn thật bằng **một dòng DI** thay vì sửa module Content (`Đ-2.9`) | `IUserDirectory` + `UserCard` + `IFriendshipReader` + `AlwaysStrangers` trong `SharedKernel/Directory/`; `UserDirectory` trong `Profile/Infrastructure/Directory/`; `ModuleBoundaryTests` xanh với **type thật ở cả hai module**; test pin `AlwaysStrangers` luôn trả `false` |
| **A7** | Test canh gác namespace `Profile.Domain` và `Content.Domain` | Đóng "lưới giả": `ModuleBoundaryTests` và `PersistenceBoundaryTests` dùng `WithoutRequiringPositiveResults()` nên gõ sai namespace là **xanh vĩnh viễn** | Hai `[Fact]` mới trong `PersistenceBoundaryTests.cs` theo đúng khuôn `Identity_Domain_namespace_must_not_be_empty`; thử đổi một namespace cho sai → đỏ |

### Thứ tự thực thi

```
      lane Profile              lane Content            nối hai lane
 [0] ─→ A1 ─→ A2 ─→ A3 ──────────→ A4 ─→ A5 ──────────────→ A6
         │                          │
         └─ A7(Profile)             └─ A7(Content)
            cùng commit A1             cùng commit A4

 [0] = thêm gói EF vào hai csproj (Mục 1.2) — commit riêng, trước mọi thứ
```

- **`[0]` trước tiên.** Hai csproj hiện không có gói EF nào (Mục 1.2). Phát hiện chuyện này giữa `A2` là
  mất một nhịp; làm trước mất hai phút.
- **`A1 → A2 → A3` là phụ thuộc thật.** Không có entity thì không có gì để `IEntityTypeConfiguration` cấu
  hình; không có configuration thì migration sinh ra một bảng thiếu CHECK.
- **`A3 → A4` là phụ thuộc *có chủ đích*, không phải phụ thuộc kỹ thuật.** Về mặt biên dịch, module Content
  không cần gì của module Profile — hai lane chạy song song được nếu có hai người. Với **một** người thì làm
  Profile trước, vì nó là bản diễn tập rẻ của đúng cái khuôn sẽ lặp lại ở Content: một bảng, không enum,
  không jsonb, không query filter. Sai khuôn ở đây sửa mất 10 phút; sai khuôn lần đầu ở `A5` thì phải sinh
  lại migration của bốn bảng.
- **`A5` sau `A4`** vì cùng lý do như `A2` sau `A1` — và thêm một lý do riêng: converter enum→chuỗi
  (`A4` Bước 1) là thứ `A5` dùng để sinh câu CHECK. Làm ngược thì CHECK gõ tay và lệch enum lúc nào không hay.
- **`A6` cuối cùng.** Nó cần `ProfileDbContext` để đọc `profile.profiles` (`A2`) và cần `AddContentModule`
  để đăng ký `AlwaysStrangers` (`A5` Bước 1). Làm sớm hơn là viết interface cho hai thứ chưa tồn tại.

**`A7` không đi một mình.** Bản Profile của test canh gác đi cùng commit `A1`; bản Content đi cùng
commit `A4` — để sang commit sau là nó thành nợ, và nợ loại này không ai nhớ trả. Đây là nếp đã chạy ở
GĐ1 (`A7` của khối A GĐ1 đi kèm `A1`).

> **Lệch B.3, cần nhóm xác nhận ở cổng mở:** `giai-doan-2.md` B.3 xếp `A7` thành **một** đầu việc đứng
> sau `A5`. Đề xuất ở đây là tách đôi và gộp vào `A1`/`A4`. Nếu nhóm không đồng ý thì giữ nguyên B.3 và
> làm `A7` thành commit riêng — nhưng **phải xong trong Ngày 7**, không để trôi sang khối D.

**Mỗi mốc mở khóa việc gì cho lane khác** — đây là lý do thứ tự trên không đổi được cho tiện:

| Xong việc này | Lane khác bắt đầu được việc gì |
|---|---|
| `A3` | `D1`–`D3` (ba endpoint Profile) · `B1` phần Profile của `PostgresFixture` |
| `A5` | `D5`–`D8` (đăng/đọc/sửa/xóa bài) · `C3` (HEAD lúc commit cần bảng `media_attachments`) · `B1` phần Content |
| `A6` | `D6` dựng được `PostResponse.author` và chạy đúng nhánh BR-02 của Mục 7.4 |

**Ba cột mốc để đo tiến độ:** cuối buổi sáng Ngày 6 xong `[0]`+`A1`+`A2`; cuối Ngày 6 xong `A3` (từ lúc
này `D1`–`D3` viết được); trưa Ngày 7 xong `A4`+`A5`+`A6` (từ lúc này `D5` viết được).

**Phần cắt được nếu trễ: gần như không có.** Khối A nằm trọn trên đường găng của lane BE-1 (B.9) — `A1`–`A6`
đều có ít nhất một endpoint của khối D đứng lên. Ứng viên duy nhất là **khung `Comment`/`Reaction`** trong
`A4`/`A5`: bỏ chúng thì GĐ2 vẫn chạy đủ UC-03/04/05, nhưng `PostResponse` mất `commentCount`/`reactionCounts`
và GĐ3 phải đổi hình dạng DTO **lần thứ hai** — đúng cái giá mà Đ-2.12 sinh ra để tránh, cộng thêm một
migration nữa. Cắt thì **ghi rõ vào PR và vào `giai-doan-2.md`**, không lặng lẽ bỏ.

---

## 1. Trước khi gõ dòng đầu tiên

### 1.1 Bốn điều kiện cần — giống hệt GĐ1

```bash
# 1. Postgres dev đang chạy (migration và schema test đều cần DB thật, không InMemory)
docker compose -f deploy/docker-compose.dev.yml up -d
docker compose -f deploy/docker-compose.dev.yml ps        # postgres phải healthy

# 2. EF tools 8.x TRỞ LÊN — xem ghi chú ngay dưới, không cần đúng 8.x
dotnet ef --version

# 3. Solution build sạch từ điểm xuất phát
dotnet build SocialApp.sln

# 4. Docker daemon chạy được — IntegrationTests dùng Testcontainers
docker ps
```

**Version của `dotnet ef` không quyết định hình dạng migration** (sửa ngày 2026-09-18, lúc thi công `A2`; câu cũ
đòi "đúng dòng 8.x" là chặt hơn mức cần và quy nhầm nguyên nhân). CLI chỉ build project rồi gọi vào gói
`Microsoft.EntityFrameworkCore.Design` **của chính project** — tức 8.0.10. Đã dựng thử để chắc: tool 10.0.10 sinh ra
migration nháp cho `ProfileDbContext` mang `.HasAnnotation("ProductVersion", "8.0.10")` ở cả `.Designer.cs` lẫn
snapshot, thân migration cùng hình dạng với `20260913040158_InitialIdentity.cs` của GĐ1. Ba hệ quả:

- Luật tương thích của EF đi **một chiều**: tool **mới hơn hoặc bằng** runtime thì được, tool cũ hơn runtime thì
  hỏng. Nên 8.x trở lên đều dùng được cho GĐ2.
- Tool dòng 10 là app `net10.0` nên máy phải có **.NET 10 runtime**; máy chỉ cài SDK 8 thì `dotnet ef` báo thiếu
  framework. `global.json` vẫn ghim SDK `8.0.424` nên bản build không đổi.
- CI/CD **không gọi `dotnet ef`** một lần nào — migration lên staging chạy bằng hook `--migrate` của app, tức
  runtime EF Core 8.0.10. Version tool vì thế chỉ ảnh hưởng trong phạm vi máy dev.

Thứ phải canh không phải `dotnet ef --version` mà là dòng `ProductVersion` trong `<Module>DbContextModelSnapshot.cs`:
nó ra `10.x` nghĩa là **gói EF của project** đã bị nâng, không phải do tool.

**`deploy/.env` phải tồn tại**, kể cả khi chỉ chạy `dotnet ef migrations add` (lệnh này không mở kết
nối): `DesignTimeIdentityDbContextFactory` đọc mật khẩu Postgres dev qua `DevEnvFile` và **từ chối chạy**
nếu thiếu file. Hai factory mới của GĐ2 chép đúng hành vi đó.

**Khóa R2 KHÔNG liên quan tới khối A.** Khối A không chạm R2 một dòng nào — `avatar_key` và `storage_key`
chỉ là `varchar`. Chưa có khóa R2 vẫn làm trọn khối A được (đó là lý do B.2 nói khối C không chặn khối A).

### 1.2 Khác GĐ1: hai csproj **chưa** có gói EF — phải thêm trước `A2` và `A5`

Đây là chỗ khác biệt đầu tiên và dễ mất 20 phút nếu phát hiện giữa chừng. Kiểm ngày 2026-09-18:
`src/backend/Modules/Profile/SocialApp.Modules.Profile.csproj` và
`src/backend/Modules/Content/SocialApp.Modules.Content.csproj` hiện **chỉ** có `ProjectReference` tới
SharedKernel, không có gói nào.

Thêm vào **cả hai** csproj, chép nguyên khối từ `SocialApp.Modules.Identity.csproj` để ba version không
lệch nhau:

```xml
  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore" Version="8.0.10" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="8.0.10">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="8.0.10" />
  </ItemGroup>
```

| Gói | Dùng để | Bỏ sót thì sao |
|---|---|---|
| `Microsoft.EntityFrameworkCore` | `DbContext`, `DbSet`, `IEntityTypeConfiguration` | Không compile |
| `Microsoft.EntityFrameworkCore.Design` | `IDesignTimeDbContextFactory` — cho phép **module tự làm startup project** cho `dotnet ef`, host không phải kéo EF vào (ADR-001) | `dotnet ef migrations add` đòi startup project khác, và đường duy nhất còn lại là `SocialApp.Api` — ngược ADR-001 |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | `UseNpgsql`, `jsonb`, `MigrationsHistoryTable` | Không compile |

`<PrivateAssets>all</PrivateAssets>` ở gói `.Design` là bắt buộc: thiếu nó thì EF Design chảy theo
`ProjectReference` sang `SocialApp.Api` và bị publish vào image production.

**Không thêm gói nào khác.** `AWSSDK.S3` là việc của `C1` và nằm ở **SharedKernel**, không nằm ở hai
module này (Đ-2.14).

### 1.3 Năm luật áp thẳng vào khối A

Vi phạm cái nào cũng có test bắt, nhưng biết trước thì không phải sửa lại:

1. **Domain tuyệt đối không chạm EF Core / Npgsql** (ADR-001, `PersistenceBoundaryTests`). Entity là POCO
   thuần: không `[Table]`, không `[Column]`, không navigation nào cần kiểu của EF.
2. **Không auto-migrate lúc app start** (`AGENTS.md` Mục 13). Mọi thứ khối A dựng chỉ chạy ở hook
   `--migrate`.
3. **Không tự bịa schema** (`AGENTS.md` Mục 14). Cột nào không có trong Mục 4 thì không thêm — kể cả khi
   "thấy cần". Thiếu cột thật thì sửa Mục 4 **trước**, trong cùng commit.
4. **Không khóa ngoại nào đi qua ranh giới schema** (Đ-2.2). `profiles.user_id`, `posts.author_id`,
   `reactions.user_id`, `media_attachments.owner_id` là `uuid` trần. FK **trong cùng** schema `content`
   (`comments.post_id`, `comments.parent_id`) thì giữ nguyên.
5. **Không module nào import module khác** (Đ-2.3, `ModuleBoundaryTests`). Chỗ duy nhất hai module gặp
   nhau là `A6`, và nó nằm ở SharedKernel.

---

## 2. A1 — Entity hồ sơ trong `Modules/Profile/Domain/`

**Mục tiêu.** Có mô hình nghiệp vụ cho Profile để `A2` và khối D bám vào, đồng thời đưa namespace
`SocialApp.Modules.Profile.Domain` ra khỏi tình trạng rỗng — nơi mọi rule kiến trúc đang chạy trong chân
không.

**Kết quả mong đợi.**
- 1 file entity dưới `src/backend/Modules/Profile/Domain/`, namespace `SocialApp.Modules.Profile.Domain`.
- `grep -rn "Microsoft.EntityFrameworkCore" src/backend/Modules/Profile/Domain/` ra **0 kết quả**.
- `dotnet build SocialApp.sln` xanh.
- `Profile_Domain_namespace_must_not_be_empty` (`A7`) xanh — nghĩa là rule persistence từ giờ chạy trên
  tập type có thật.

### ⚠ Một điểm phải chốt trước khi gõ code: tên của entity

`giai-doan-2.md` B.3 gọi entity là `Profile`. **Tên đó không compile được ngoài `Domain/`.** Đã dựng thử
để chắc, không suy đoán:

```
error CS0118: 'Profile' is a namespace but is used like a type
```

Lý do: từ trong `SocialApp.Modules.Profile.Infrastructure`, C# tra tên `Profile` theo thứ tự namespace lồng
từ trong ra ngoài. Nó gặp **namespace** `SocialApp.Modules.Profile` trước khi kịp xét `using` của file, nên
`DbSet<Profile>` trong `ProfileDbContext` là lỗi biên dịch. Hai đường thoát, cả hai đã thử build thật:

| Cách | Trông ra sao | Giá phải trả |
|---|---|---|
| **Đặt tên `UserProfile`** (khuyến nghị) | `public sealed class UserProfile`, ánh xạ `.ToTable("profiles")` | Lệch tên trong B.3 → **ghi ngược vào `giai-doan-2.md` B.3 trong cùng commit** |
| Giữ tên `Profile` | Mọi file ngoài `Domain/` phải có `using ProfileEntity = SocialApp.Modules.Profile.Domain.Profile;` | Một alias vĩnh viễn ở mọi file của cả GĐ2 lẫn GĐ4–GĐ8; quên một file là lỗi compile khó hiểu |

Chốt ở đầu `A1`, không chốt giữa `A2`. Tài liệu này viết theo phương án `UserProfile`.

### Các bước

**Bước 1 — `src/backend/Modules/Profile/Domain/UserProfile.cs`.** Cột lấy đúng Mục 4, không thêm không bớt:

| Thuộc tính C# | Cột | Kiểu DB | Ghi chú |
|---|---|---|---|
| `Guid UserId { get; init; }` | `user_id` | `uuid` PK | **= `identity.users.user_id`**, KHÔNG FK (Đ-2.2) |
| `string DisplayName { get; set; }` (`required`) | `display_name` | `varchar(50)` NOT NULL | CHECK `btrim(...) <> ''` |
| `string? Bio { get; set; }` | `bio` | `varchar(500)` | |
| `string? AvatarKey { get; set; }` | `avatar_key` | `varchar(200)` | `storage_key` trên R2; `null` = ảnh mặc định |
| `DateTimeOffset CreatedAt { get; init; }` | `created_at` | `timestamptz` | đồng hồ app gán, `DEFAULT now()` chỉ là lưới |
| `DateTimeOffset UpdatedAt { get; set; }` | `updated_at` | `timestamptz` | do override `SaveChanges` đóng dấu (`A2`) |

**Bước 2 — viết XML doc cho đúng hai chỗ dễ hiểu nhầm nhất**, theo nếp `User.cs` của GĐ1:

- `UserId` **không** sinh bằng `Uuid7.New()`. Đây là điểm khác `Post`: khóa chính của hồ sơ **là** id của
  tài khoản, luôn lấy từ `User.GetUserId()` ở tầng D. Đặt `= Uuid7.New()` làm giá trị mặc định là tạo ra
  một hồ sơ mồ côi không thuộc về ai, và không có test nào bắt được vì kiểu vẫn đúng.
- Không có navigation property trỏ sang `User` — **không có kiểu nào để trỏ** (Đ-2.2). Đây không phải "tạm
  thời chưa làm", đây là ranh giới module.

**Bước 3 — `A7` bản Profile, trong chính commit này.** Xem Mục 8.

### Cạm bẫy đã biết

1. **CS0118** — đã nói ở trên. Dấu hiệu: build đỏ ngay khi `A2` gõ `DbSet<Profile>`.
2. **Đừng thêm `Guid ProfileId` làm PK.** Mục 4 nói PK là `user_id`. Thêm khóa thứ hai là mở đường cho hai
   hồ sơ của cùng một người, và `PUT /users/me/profile` (upsert) mất tính idempotent.
3. **Đừng nhét quy tắc độ dài tên hiển thị vào entity dưới dạng property có logic.** BR của `displayName`
   (2–50 ký tự, Đ-2.4) là việc của validator ở `D2`. Nếu muốn một chỗ duy nhất giữ hai con số đó thì đặt
   hằng số `public const int DisplayNameMinLength = 2;` trong Domain và để `D2` dùng lại — đừng để `D2` gõ
   lại số.

---

## 3. A2 — `ProfileDbContext` + configuration + options + design-time factory

**Mục tiêu.** Đưa module Profile vào đúng khuôn `Đ-2.1`: schema riêng, **bảng lịch sử migration nằm trong
schema của chính nó**, và cấu hình Npgsql đặt ở **một** chỗ dùng chung cho cả DI lúc chạy lẫn design-time
lúc `dotnet ef`.

**Kết quả mong đợi.**
- Bốn file dưới `src/backend/Modules/Profile/Infrastructure/`:
  `ProfileDbContext.cs` · `ProfileDbContextOptions.cs` · `DesignTimeProfileDbContextFactory.cs` ·
  `Configurations/UserProfileConfiguration.cs`.
- `ProfileDbContext.Schema == "profile"`, `HasDefaultSchema(Schema)`, `MigrationsHistoryTable(..., Schema)`.
- **Không** có `HasPostgresExtension("citext")` — Profile không có cột citext nào.
- `dotnet build SocialApp.sln` xanh. (Nghiệm thu trên DB thật là việc của `A3`.)

### Các bước

**Bước 1 — `ProfileDbContextOptions.cs`.** Chép nguyên hình dạng `IdentityDbContextOptions`, đổi tên:

```csharp
public static class ProfileDbContextOptions
{
    public const string MigrationsHistoryTable = "__EFMigrationsHistory";

    public static DbContextOptionsBuilder UseProfileNpgsql(
        this DbContextOptionsBuilder builder, string connectionString) =>
        builder.UseNpgsql(connectionString, npgsql =>
            npgsql.MigrationsHistoryTable(MigrationsHistoryTable, ProfileDbContext.Schema));
}
```

Đây là chỗ **cạm bẫy của Đ-2.1** nằm: hai đường tạo context mà cấu hình lệch nhau là lỗi câm — migration
sinh ở design-time ghi lịch sử vào một bảng, runtime đọc bảng khác, EF tưởng chưa chạy và áp lại từ đầu.
Cùng tên bảng `__EFMigrationsHistory` ở **ba** schema khác nhau là đúng, không phải trùng.

**Bước 2 — `ProfileDbContext.cs`.** Khác `IdentityDbContext` đúng ba chỗ: `Schema = "profile"`, một `DbSet`,
và **bỏ dòng `HasPostgresExtension("citext")`**.

```csharp
public sealed class ProfileDbContext(DbContextOptions<ProfileDbContext> options) : DbContext(options)
{
    public const string Schema = "profile";

    private const string UpdatedAtProperty = nameof(UserProfile.UpdatedAt);

    public DbSet<UserProfile> Profiles => Set<UserProfile>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(ProfileDbContext).Assembly);
        base.OnModelCreating(modelBuilder);
    }

    // Hai overload nhận acceptAllChangesOnSuccess là đích cuối của cả bốn đường SaveChanges /
    // SaveChangesAsync — chép nguyên phần StampUpdatedAt của IdentityDbContext.
}
```

Phần `StampUpdatedAt()` chép **nguyên văn** từ `IdentityDbContext`, kể cả ghi chú về ranh giới
(`ExecuteUpdateAsync` và SQL thô đi vòng qua ChangeTracker nên không được bảo vệ).

**Bước 3 — `Configurations/UserProfileConfiguration.cs`.**

```csharp
internal sealed class UserProfileConfiguration : IEntityTypeConfiguration<UserProfile>
{
    public void Configure(EntityTypeBuilder<UserProfile> builder)
    {
        builder.ToTable("profiles", t =>
            t.HasCheckConstraint("ck_profiles_display_name_not_blank", "btrim(display_name) <> ''"));

        builder.HasKey(x => x.UserId);
        // KHÔNG ValueGeneratedOnAdd: user_id đến từ token, không do DB hay EF sinh.
        builder.Property(x => x.UserId).HasColumnName("user_id").ValueGeneratedNever();

        builder.Property(x => x.DisplayName).HasColumnName("display_name").HasMaxLength(50).IsRequired();
        builder.Property(x => x.Bio).HasColumnName("bio").HasMaxLength(500);
        builder.Property(x => x.AvatarKey).HasColumnName("avatar_key").HasMaxLength(200);

        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
    }
}
```

**Bước 4 — `DesignTimeProfileDbContextFactory.cs`.** Chép `DesignTimeIdentityDbContextFactory`, đổi ba
định danh. Giữ nguyên phần đọc `ConnectionStrings__Postgres` → `DevEnvFile.LocalPostgresConnectionString`,
và **giữ nguyên khối XML doc ghi lệnh `dotnet ef`** — đó là chỗ người sau tra lệnh.

### Cạm bẫy đã biết

1. **Quên bỏ `HasPostgresExtension("citext")`.** Chép nguyên `IdentityDbContext` thì migration của Profile
   sẽ mang `CREATE EXTENSION citext` — chạy được, nhưng nó biến một extension của schema Identity thành
   phụ thuộc ngầm của Profile, và khi tra "ai cần citext" thì có hai câu trả lời.
2. **`internal` cho configuration, `public` cho context.** Configuration chỉ được
   `ApplyConfigurationsFromAssembly` gọi; để `public` là mở bề mặt không ai cần.
3. **`nameof(UserProfile.UpdatedAt)` chứ không phải chuỗi `"UpdatedAt"`.** Đổi tên thuộc tính mà quên chuỗi
   thì `StampUpdatedAt` im lặng không làm gì — `updated_at` đứng yên mãi mãi, và không test nào của `A2`
   bắt được. Nghiệm thu thật của nó là `PROF-02` ở khối D.
4. **Đừng đặt `HasDefaultSchema` trong configuration.** Nó là thuộc tính của model, phải ở
   `OnModelCreating`.

---

## 4. A3 — Migration đầu của Profile + DI + hook `--migrate`

**Mục tiêu.** Biến schema `profile` thành artifact có version, tái lập y hệt ở mọi môi trường bằng **một
lệnh** ở bước deploy, và để một dòng duy nhất ở `Program.cs` chịu trách nhiệm cho cả module.

**Kết quả mong đợi.**
- `src/backend/Modules/Profile/Infrastructure/Migrations/` có `<timestamp>_InitialProfile.cs`,
  `.Designer.cs` và `ProfileDbContextModelSnapshot.cs` — **đã commit**, không gitignore.
- `src/backend/Modules/Profile/DependencyInjection/ProfileModuleExtensions.cs` có `AddProfileModule` và
  `MigrateProfileModuleAsync`.
- `Program.cs` có đúng **hai** dòng mới (một DI, một trong nhánh `--migrate`).
- Trên DB sạch: `dotnet run --project src/backend/SocialApp.Api -- --migrate` exit 0; **chạy lần hai vẫn
  exit 0** và `\dn` vẫn thấy đúng hai schema `identity`, `profile`.
- `ProfileDbContextSchemaTests` xanh trên Postgres thật.

### Các bước

**Bước 1 — `ProfileModuleExtensions.cs`.** Mỏng hơn bản Identity rất nhiều: Profile **không có seeder**,
không có vai trò hệ thống để kiểm.

```csharp
public static class ProfileModuleExtensions
{
    public const string Schema = ProfileDbContext.Schema;

    public static IServiceCollection AddProfileModule(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<ProfileDbContext>(o => o.UseProfileNpgsql(connectionString));
        return services;
    }

    /// <summary>Chạy ở hook --migrate, KHÔNG chạy khi api khởi động. Profile không có dữ liệu nền (Mục 5).</summary>
    public static async Task MigrateProfileModuleAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<ProfileDbContext>();
        await db.Database.MigrateAsync(ct);
    }
}
```

`IUserDirectory` sẽ được đăng ký vào đúng hàm `AddProfileModule` này ở `A6` — chừa chỗ, đừng tạo hàm thứ hai.

**Bước 2 — sinh migration.** Chạy **từ gốc repo**:

```bash
dotnet ef migrations add InitialProfile \
  --project src/backend/Modules/Profile/SocialApp.Modules.Profile.csproj \
  --startup-project src/backend/Modules/Profile/SocialApp.Modules.Profile.csproj \
  --output-dir Infrastructure/Migrations
```

Mở file migration sinh ra và **đọc bằng mắt** trước khi chạy. Ba thứ phải thấy: `migrationBuilder.EnsureSchema(name: "profile")`, `schema: "profile"` ở `CreateTable`, và CHECK
`ck_profiles_display_name_not_blank`. Không thấy CHECK nghĩa là `A2` bước 3 chưa vào.

**Bước 3 — nối `Program.cs`, đúng hai dòng.**

Dòng DI, đặt ngay dưới `builder.Services.AddIdentityModule(postgres);`:

```csharp
builder.Services.AddProfileModule(postgres);
```

Dòng migrate, trong nhánh `if (isMigrate)`, **sau** Identity:

```csharp
await app.Services.MigrateIdentityModuleAsync();
await app.Services.MigrateProfileModuleAsync();
```

Và sửa dòng `Console.WriteLine` để nó nêu đủ các schema đã áp dụng. Thứ tự Identity → Profile → Content là
**cố định** (Mục 5): không có FK chéo nên DB không đòi thứ tự, nhưng log deploy phải đọc được.

Hình dạng câu log đã chốt lúc thi công `A3` (ngày 2026-09-18) — `A5` **nối tên schema vào vế đầu**, không viết
câu thứ hai:

```
[migrate] Đã áp dụng migration cho schema "identity", "profile"; nạp dữ liệu nền và kiểm tra vai trò hệ
thống cho schema "identity". Thoát 0.
```

Hai vế tách nhau vì hai việc không cùng phạm vi: migration áp cho **mọi** module, còn seed + kiểm vai trò hệ
thống chỉ có ở Identity (Mục 5 — GĐ2 không seed gì). Gộp một vế là nói dối trong log rằng Profile cũng có dữ
liệu nền.

**Không** đụng `AddApplicationPart` và `apiGroups` ở khối A — Profile chưa có controller nào. Hai chỗ đó là
việc của `D1`.

**Bước 4 — nghiệm thu bằng tay trên DB sạch.**

```bash
# DB sạch
docker compose -f deploy/docker-compose.dev.yml down -v
docker compose -f deploy/docker-compose.dev.yml up -d

# lần 1
dotnet run --project src/backend/SocialApp.Api -- --migrate; echo "exit=$?"

# lần 2 — phải vẫn exit 0 và không đổi gì
dotnet run --project src/backend/SocialApp.Api -- --migrate; echo "exit=$?"
```

Rồi vào psql kiểm ba thứ:

```sql
\dn
-- kỳ vọng: identity, profile (và public)

SELECT table_schema, count(*) FROM information_schema.tables
WHERE table_name = '__EFMigrationsHistory' GROUP BY table_schema;
-- kỳ vọng: identity 1, profile 1 — KHÔNG có dòng nào ở "public"
```

**Bước 5 — `tests/SocialApp.IntegrationTests/ProfileDbContextSchemaTests.cs`.** Chép
`IdentityDbContextSchemaTests`, giữ `[Collection(PostgresCollection.Name)]` và
`postgres.CreateDatabaseAsync()` (database mới, chưa migrate). Ba khẳng định tối thiểu:

| # | Khẳng định | Hỏng thì triệu chứng là gì |
|---|---|---|
| 1 | `__EFMigrationsHistory` của `ProfileDbContext` nằm trong schema `profile` | Ba context tranh một bảng lịch sử ở `public` — migration của module này "biến mất" với module kia |
| 2 | Bảng `profiles` nằm trong schema `profile`, có đủ 6 cột đúng tên snake_case | Cột PascalCase: mọi câu SQL viết tay và mọi bản dump sau này đều lệch |
| 3 | INSERT `display_name = '   '` bị CHECK chặn | CHECK chỉ "tồn tại trong migration" chứ chưa bao giờ được chứng minh là chặn thật |

Khẳng định #3 là thứ phân biệt test schema thật với test "bảng có tồn tại". Đừng bỏ.

### Cạm bẫy đã biết

1. **Chép luôn `await IdentitySeeder.SeedAsync(db, ct);` sang bản Profile.** Không compile (không có seeder
   nào) — nhưng nếu ai đó "sửa cho compile" bằng cách tạo một seeder rỗng thì GĐ2 vừa mọc thêm một chỗ
   trống không ai biết để làm gì. Mục 5 đã nói rõ: **GĐ2 không seed gì**.
2. **Bọc `try/catch` quanh `MigrateProfileModuleAsync` ở `Program.cs`.** Đừng. Lỗi migration phải làm
   process thoát khác 0 để `set -e` ở CD dừng **trước** `up -d`.
3. **Quên commit `ProfileDbContextModelSnapshot.cs`.** Migration kế tiếp (GĐ3+) sẽ được sinh ra từ một
   snapshot rỗng và chứa lại toàn bộ bảng đã có → `--migrate` trên staging đỏ vì bảng đã tồn tại.
4. **Chạy `dotnet ef` từ thư mục module thay vì từ gốc repo.** `DevEnvFile` đi ngược lên tìm `deploy/.env`
   từ thư mục hiện hành; đứng sai chỗ thì lệnh từ chối chạy với thông báo về `.env` — dễ bị hiểu nhầm
   thành "thiếu cấu hình".

---

## 5. A4 — Entity Content trong `Modules/Content/Domain/` + `PostContentPolicy`

**Mục tiêu.** Có `Post` và `MediaAttachment` để khối D bám vào; dựng **khung** `Comment`/`Reaction` để
`PostResponse` mang được `commentCount`/`reactionCounts` ngay từ GĐ2 và GĐ3 không phải đổi hình dạng DTO
lần thứ hai (Đ-2.12); và đặt BR-01 thành **hàm thuần** để unit test không cần DB.

**Kết quả mong đợi.**
- Entity + enum + policy dưới `src/backend/Modules/Content/Domain/`, namespace
  `SocialApp.Modules.Content.Domain`, `grep` EF Core ra **0 kết quả**.
- `PostContentPolicy` là `static`, không tham số nào là `DbContext`, và có unit test phủ đúng ba ca của
  Mục 10.1: `AC-02` (body rỗng + 0 ảnh → không hợp lệ), `AC-03` (11 ảnh → không hợp lệ), `BR01-06` (chỉ
  ảnh, không chữ → hợp lệ).
- `Comment` và `Reaction` **chỉ có thuộc tính**: không phương thức, không repository, không service.
- `Content_Domain_namespace_must_not_be_empty` (`A7`) xanh.

### Các bước

**Bước 1 — chốt cách biểu diễn `privacy` / `status`.** B.3 nói "dạng enum, ánh xạ sang chuỗi để khớp
CHECK". Khác GĐ1 (Identity dùng lớp hằng chuỗi `UserStatus`), nên nói rõ một lần ở đây:

```csharp
public enum PostPrivacy { Public, Friends, Private }
public enum PostStatus  { Published, Hidden, Deleted }
```

**Cạm bẫy số một của cả `A4`+`A5`:** `HasConversion<string>()` của EF lưu **tên C#** — `"Public"`, chữ P
hoa. CHECK của Mục 4 đòi `'public'`. Kết quả: mọi INSERT đều nổ `ck_posts_privacy`, và thông báo lỗi
Postgres không nói gì về hoa thường. Cách đúng là một converter tường minh, và CHECK **sinh ra từ chính
enum đó** để C# và DB không thể lệch nhau — đúng nếp `UserStatus.All` của GĐ1:

```csharp
// Trong PostConfiguration (A5), KHÔNG phải trong Domain.
private static readonly ValueConverter<PostPrivacy, string> PrivacyConverter = new(
    v => v.ToString().ToLowerInvariant(),
    s => Enum.Parse<PostPrivacy>(s, ignoreCase: true));

private static readonly string PrivacyCheckSql =
    $"privacy IN ({string.Join(",", Enum.GetNames<PostPrivacy>().Select(n => $"'{n.ToLowerInvariant()}'"))})";
```

**Bước 2 — `Post.cs`.** Đủ 14 cột của Mục 4, không hơn:

| Thuộc tính | Cột | Kiểu C# | Ghi chú |
|---|---|---|---|
| `PostId` | `post_id` | `Guid` = `Uuid7.New()` | Khác `UserProfile`: **ở đây** Uuid7 là đúng (Đ-2.11 dựa vào thứ tự PK = thứ tự thời gian) |
| `AuthorId` | `author_id` | `Guid` | KHÔNG FK; luôn từ `User.GetUserId()` |
| `Body` | `body` | `string?` | ≤ 5000 |
| `Privacy` | `privacy` | `PostPrivacy` | mặc định `Public` |
| `Status` | `status` | `PostStatus` | mặc định `Published` |
| `MediaCount` | `media_count` | `short` | **smallint** — đừng dùng `int` |
| `CommentCount` | `comment_count` | `int` | GĐ3 ghi |
| `ReactionCounts` | `reaction_counts` | xem Bước 4 | GĐ3 ghi |
| `HiddenReason` | `hidden_reason` | `string?` | GĐ6 ghi (BR-07) |
| `EditedAt` | `edited_at` | `DateTimeOffset?` | `null` = chưa sửa |
| `CreatedAt` / `UpdatedAt` / `DeletedAt` | | `DateTimeOffset` / `?` | |

**Bước 3 — `MediaAttachment.cs`.** `MediaId` (`Uuid7.New()`), `OwnerType` (`string` hoặc enum
`MediaOwnerType { Post, Message }` — cùng luật converter như Bước 1), `OwnerId` (`Guid`, **không FK** vì
bảng đa hình — Đ-2.12), `StorageKey`, `ContentType`, `SizeBytes` (`int`), `Width`/`Height` (`short?`),
`Position` (`short`), `CreatedAt`.

**Bước 4 — `reaction_counts` (jsonb): chọn một, ghi lý do vào XML doc.** GĐ2 **không bao giờ ghi** cột này,
chỉ đọc ra `{}` và trả nguyên vào DTO (`Đ-2.12`, Mục 8.2: `{}` chứ không `null`).

| Cách | Thuộc tính | Đổi lại |
|---|---|---|
| **Value converter tường minh** (khuyến nghị) | `Dictionary<string,int> ReactionCounts { get; set; } = new();` + `HasConversion` bằng `JsonSerializer` + `ValueComparer` | DTO dùng thẳng, nhưng phải viết cả `ValueComparer` (thiếu nó EF không phát hiện thay đổi trong dictionary) |
| JSON thô | `string ReactionCountsJson { get; set; } = "{}";` | Đơn giản nhất ở tầng EF, nhưng `D6` phải tự parse khi dựng `PostResponse` |

Đường **không** nên đi: dựa vào ánh xạ POCO/Dictionary động của Npgsql. Npgsql 8 chặn ánh xạ động trừ khi
bật `EnableDynamicJson()` trên data source, và triệu chứng là exception **lúc chạy câu truy vấn đầu tiên**,
không phải lúc build — tức là lộ ra ở khối D chứ không ở đây. Dù chọn cách nào, nghiệm thu là một test của
`A5`: đọc một bài vừa tạo và thấy `{}`, không thấy `null`.

**Bước 5 — `Comment.cs` và `Reaction.cs`, đúng mức "khung".** Chỉ thuộc tính theo Mục 4
(`Comment`: `CommentId`, `PostId`, `ParentId?`, `AuthorId`, `Depth` (`short`), `Body`, `Status`,
`CreatedAt`, `UpdatedAt`, `DeletedAt` · `Reaction`: `UserId`, `TargetType`, `TargetId`, `Type`,
`CreatedAt`, `UpdatedAt`). Không phương thức, không factory, không validate. Giới hạn này là **có chủ đích**
(Đ-2.12) — viết thêm ở GĐ2 là viết mã cho một hợp đồng chưa tồn tại.

**Bước 6 — `PostContentPolicy.cs`, hàm thuần.** Đúng nếp `LockoutPolicy`/`RefreshTokenPolicy` của GĐ1: đầu
vào là giá trị, đầu ra là kết quả, không I/O. BR-01 gồm ba mệnh đề — body ≤ 5000 · số ảnh ≤ 10 · (có ảnh
**hoặc** body không rỗng sau `trim`) — và kết quả phải nói được **key lỗi nào** (`body` hay `mediaKeys`,
Mục 10.1 `AC-02`/`AC-03`), vì `D5` ánh xạ thẳng sang `errors` của RFC 7807.

Hai thứ **không** thuộc `PostContentPolicy`: kiểm tiền tố `posts/{actorId}/` của key (Đ-2.7 — cần `actorId`,
là việc của `D5`) và kiểm dung lượng/loại ảnh thật (Đ-2.8 — cần `HEAD`, là việc của `C3`).

Hình dạng kết quả đã chốt lúc thi công `A4` (ngày 2026-09-18) — `D5` ánh xạ theo đúng cái này:

```csharp
PostContentValidation Validate(string? body, int mediaCount);   // readonly record struct (ErrorKey, Message)
```

Trả **đúng một** key lỗi chứ không gom nhiều: `errors` hiện dưới trường nào thì người dùng sửa trường đó, và
bài vừa quá dài vừa quá nhiều ảnh hiếm hơn nhiều so với cái giá của hai thông điệp cùng lúc. Thứ tự kiểm là
**ảnh → độ dài body → rỗng**; đảo hai cái đầu vẫn đúng với `AC-03`, nhưng 11 ảnh không kèm chữ sẽ báo nhầm
`body`. Cố ý **không** dùng lại `Error` của SharedKernel: `Error` mang `Status` HTTP, mà chọn mã HTTP là việc
của tầng D, không của Domain.

### Cạm bẫy đã biết

1. **`media_count` là `int` trong C#.** Cột là `smallint`. EF sẽ sinh `integer` và migration lệch Mục 4 —
   không ai thấy cho tới lúc so schema.
2. **Đặt `Uuid7.New()` cho `Comment.CommentId`/`Reaction`** thì không sai, nhưng nhớ rằng `Reaction` **không
   có** khóa đơn: PK là ba cột `(user_id, target_type, target_id)` — đó **chính là** BR-05 (Đ-2.12). Thêm
   một cột `ReactionId` là xóa mất BR-05.
3. **`body` nullable còn `varchar(5000)`**: `Body` phải là `string?`. Để `required string` thì bài chỉ có
   ảnh không tạo được, và `BR01-06` đỏ.
4. **Enum trong `Domain/` là đúng; converter trong `Infrastructure/` là đúng.** Đặt `ValueConverter` vào
   `Domain/` là kéo EF vào Domain → `PersistenceBoundaryTests` đỏ.
5. **Thông điệp lỗi khai `const string` rồi nội suy hằng số.** Gặp thật lúc thi công `A4`: C# chỉ cho nội suy
   hằng khi **mọi** phần đều là hằng **chuỗi**, nên `$"... {MaxBodyLength} ..."` là `CS0133`. Đừng chữa bằng
   cách viết tay `5000` vào câu — con số trong thông điệp và con số trong luật thành hai nguồn. Dùng
   `static readonly string`.
6. **Nhắc tên trình điều khiển Postgres trong comment của `Domain/`.** Checklist nghiệm thu khối A (Mục 10)
   `grep` đúng chữ đó trong hai thư mục `Domain` và đòi **0 kết quả** — comment cũng bị tính. Viết vòng, giữ
   nguyên tên API `EnableDynamicJson()` để vẫn tra được.

---

## 6. A5 — `ContentDbContext` + configuration + migration

**Mục tiêu.** Giao cho DB giữ bốn bất biến mà code không giữ nổi một mình, và loại bài xóa mềm khỏi **mọi**
truy vấn đọc bằng global query filter (Đ-2.10).

**Kết quả mong đợi.**
- Bốn bảng trong schema `content` + `__EFMigrationsHistory` trong `content`.
- Đủ CHECK/index của Mục 4: `ck_posts_privacy`, `ck_posts_status`, `ck_posts_media_count`,
  `ck_posts_not_empty`, `idx_posts_author_created` (có `WHERE status = 'published'`, hai cột DESC),
  `storage_key` UNIQUE, `uq_media_owner_position`, `ck_media_*`, `ck_comments_depth`,
  `ck_comments_status`, PK ba cột của `reactions`, `ck_reactions_*`.
- `ContentDbContextSchemaTests` xanh, và **có test chứng minh ràng buộc chặn thật**, không chỉ tồn tại.
- `--migrate` chạy hai lần liên tiếp trên DB sạch: cả hai exit 0, `\dn` thấy **ba** schema.

### Các bước

**Bước 1 — options + context + factory**, y hệt `A2` với `Schema = "content"`, bốn `DbSet`, và global query
filter. Đặt filter ở configuration của `Post`:

```csharp
builder.HasQueryFilter(p => p.Status != PostStatus.Deleted);
```

Ghi ngay cạnh nó, bằng chữ, cái ngoại lệ duy nhất: **repository của worker dọn rác (`C4`) là chỗ duy nhất
trong repo được gọi `IgnoreQueryFilters()`**. Không viết ra thì sáu tháng nữa sẽ có chỗ thứ hai.

**Bước 2 — `PostConfiguration`.** Bốn CHECK + một index:

```csharp
builder.ToTable("posts", t =>
{
    t.HasCheckConstraint("ck_posts_privacy", PrivacyCheckSql);
    t.HasCheckConstraint("ck_posts_status", StatusCheckSql);
    t.HasCheckConstraint("ck_posts_media_count", "media_count BETWEEN 0 AND 10");
    // BR-01 ở tầng DB. Hệ quả: INSERT phải mang media_count ĐÚNG ngay từ đầu (Mục 4, chỗ dễ sai #1).
    t.HasCheckConstraint("ck_posts_not_empty", "media_count > 0 OR btrim(coalesce(body,'')) <> ''");
});

builder.HasIndex(x => new { x.AuthorId, x.CreatedAt, x.PostId })
       .HasDatabaseName("idx_posts_author_created")
       .IsDescending(false, true, true)          // (author_id, created_at DESC, post_id DESC)
       .HasFilter("status = 'published'");       // index một phần — GĐ4 dùng lại cho feed

builder.Property(x => x.ReactionCounts)
       .HasColumnName("reaction_counts")
       .HasColumnType("jsonb")
       .HasDefaultValueSql("'{}'::jsonb");       // ::jsonb BẮT BUỘC — Mục 4, chỗ dễ sai #3
```

`IsDescending(false, true, true)` là thứ làm cursor keyset của `Đ-2.11` đọc thẳng từ index thay vì sort
lại. Bỏ nó thì `PAGE-01` vẫn xanh và `GET /users/{id}/posts` vẫn đúng — chỉ chậm, và chỉ lộ ra ở k6 của GĐ4.

**Bước 3 — `MediaAttachmentConfiguration`.**

```csharp
builder.HasIndex(x => x.StorageKey).IsUnique();   // chặn gắn CÙNG object vào hai bài (Mục 4, chỗ dễ sai #2)
builder.HasIndex(x => new { x.OwnerType, x.OwnerId, x.Position })
       .IsUnique().HasDatabaseName("uq_media_owner_position");
builder.HasIndex(x => new { x.OwnerType, x.OwnerId }).HasDatabaseName("idx_media_owner");
```

**Không** khai `HasOne<Post>()` cho `OwnerId`: bảng đa hình, không có FK (Đ-2.12). Đây chính là lý do thứ
hai worker dọn rác tồn tại — viết câu đó vào XML doc để `C4` không bị hiểu là "tối ưu hóa".

**Bước 4 — `CommentConfiguration` / `ReactionConfiguration`.** Đây là hai bảng **trong cùng** schema
`content`, nên FK **giữ nguyên** (Đ-2.2 chỉ cấm FK chéo schema):

```csharp
builder.HasOne<Post>().WithMany().HasForeignKey(x => x.PostId).OnDelete(DeleteBehavior.Cascade);
builder.HasOne<Comment>().WithMany().HasForeignKey(x => x.ParentId).OnDelete(DeleteBehavior.Cascade);
```

Dùng `HasOne<T>()` **không có navigation property** — Đ-2.12 nói khung không có hành vi, và mỗi navigation
là một đường để code sau này vô tình `.Include()` cả bảng (cùng lý do `User` của GĐ1 không có navigation
`Role`). Tên index mặc định EF sinh ra là `IX_comments_post_id` và `IX_comments_parent_id` — **đúng y** tên
trong DDL Mục 4, nên đừng đặt lại tên.

`Reaction`: `builder.HasKey(x => new { x.UserId, x.TargetType, x.TargetId });`

**Bước 5 — sinh migration**, cùng khuôn lệnh của `A3`, tên `InitialContent`, `--project` và
`--startup-project` đều trỏ `SocialApp.Modules.Content.csproj`.

**Bước 6 — nối `Program.cs`** (hai dòng, sau Profile) và **nghiệm thu `--migrate` hai lần**.

**Bước 7 — `ContentDbContextSchemaTests.cs`.** Đây là test đắt nhất của khối A và cũng là thứ đáng giá
nhất. Sáu khẳng định, mỗi cái ứng với một thứ mà **nếu cấu hình sai thì không có lỗi nào khác báo**:

| # | Khẳng định | Cách kiểm |
|---|---|---|
| 1 | `__EFMigrationsHistory` của Content nằm trong schema `content` | `information_schema.tables` |
| 2 | INSERT bài `body = NULL`, `media_count = 0` bị chặn | SQL thô, kỳ vọng `PostgresException` nêu `ck_posts_not_empty` |
| 3 | INSERT bài chỉ có ảnh (`media_count = 1`, `body = NULL`) **thành công** | Đây là mặt còn lại của #2 — thiếu nó thì một CHECK viết quá chặt vẫn xanh |
| 4 | INSERT hai `media_attachments` cùng `storage_key` bị chặn | Kỳ vọng vi phạm unique — `D5` sẽ dịch thành **409**, không phải 500 |
| 5 | `reaction_counts` của bài mới đọc ra `{}`, **không** `null` | Chốt Bước 4 của `A4` bằng hành vi thật |
| 6 | Bài `status = 'deleted'` **không** xuất hiện trong `db.Posts` | Global query filter chạy thật |

Khẳng định #6 dùng SQL thô để tạo dòng `deleted` (đi vòng qua ChangeTracker), rồi đọc qua `DbSet` — đọc qua
`DbSet` cả hai chiều thì filter tự loại và test không chứng minh được gì.

### Cạm bẫy đã biết

1. **Ba chỗ dễ sai đã được Mục 4 liệt kê sẵn** — `ck_posts_not_empty` + thứ tự INSERT, `storage_key` UNIQUE
   → 409, `'{}'::jsonb` thiếu cast. Đọc lại Mục 4 trước khi gõ Bước 2.
2. **`HasFilter("status = 'published'")` là chuỗi SQL thô** — nó không đi qua `ValueConverter`, nên nếu
   converter của `PostStatus` đổi cách viết thì index lọc sai **âm thầm**. Sinh chuỗi này từ cùng một chỗ
   với `StatusCheckSql` thay vì gõ tay.
3. **Query filter + entity có navigation bắt buộc** sinh cảnh báo `PossibleIncorrectRequiredNavigation...`
   lúc build model. Nếu thấy cảnh báo đó nghĩa là đã lỡ thêm navigation property vào khung `Comment` —
   gỡ ra, đừng tắt cảnh báo.
4. **Hai FK cascade trên cùng bảng `comments`** (`post_id` và `parent_id`) là hợp lệ với Postgres. Nếu thấy
   EF than phiền về "multiple cascade paths" thì đó là provider SQL Server — kiểm lại `UseNpgsql`.
5. **Migration thứ hai sinh ra khác rỗng dù không đổi gì** là dấu hiệu cấu hình không tất định (ví dụ
   `HasDefaultValue(DateTimeOffset.UtcNow)` — giá trị đóng băng lúc build model). Kiểm bằng
   `dotnet ef migrations add Tmp` → phải rỗng → `dotnet ef migrations remove`.

---

## 7. A6 — Hai contract chéo module ở SharedKernel

**Mục tiêu.** Cho Content đọc được tên + avatar tác giả mà **không** import module Profile (Đ-2.3), và để
GĐ4 bật kết bạn thật bằng **một dòng DI** thay vì sửa module Content (Đ-2.9, Mục 7.4).

**Kết quả mong đợi.**
- `src/backend/SocialApp.SharedKernel/Directory/IUserDirectory.cs` — interface + `record UserCard`.
- `src/backend/SocialApp.SharedKernel/Directory/IFriendshipReader.cs` — interface + `AlwaysStrangers`.
- `src/backend/Modules/Profile/Infrastructure/Directory/UserDirectory.cs` — hiện thực, đọc
  `profile.profiles` qua `ProfileDbContext`.
- `AddProfileModule` đăng ký `IUserDirectory` → `UserDirectory`; `AddContentModule` đăng ký
  `IFriendshipReader` → `AlwaysStrangers` **kèm comment trỏ thẳng tới GĐ4**.
- `ModuleBoundaryTests` xanh với type thật ở cả hai module (nhờ `A7`).
- **Test pin** `AlwaysStrangers` luôn trả `false` — đổi hành vi nó mà không đổi test là đỏ.

### Các bước

**Bước 1 — `IUserDirectory` + `UserCard`.** Ba luật của Đ-2.3, áp từ đây để SharedKernel không thành cái sọt:

```csharp
namespace SocialApp.SharedKernel.Directory;

/// <summary>Chiếu (projection) của hồ sơ — KHÔNG phải entity. Xem Đ-2.3 luật 2.</summary>
public sealed record UserCard(Guid UserId, string DisplayName, string? AvatarKey);

public interface IUserDirectory
{
    /// <summary>Luật 3 của Đ-2.3: batch trước, đơn sau. GĐ4 gọi hàm này cho 20 bài mỗi trang feed.</summary>
    Task<IReadOnlyDictionary<Guid, UserCard>> GetManyAsync(
        IReadOnlyCollection<Guid> userIds, CancellationToken ct = default);
}
```

- **Chỉ đọc** — không phương thức ghi. Module muốn module khác ghi hộ là dấu hiệu chia module sai.
- **Chỉ chiếu** — `UserCard` mang `AvatarKey` (khóa), không mang URL: ký presigned GET là việc của khối D,
  và SharedKernel không biết gì về R2 ở tầng này.
- **Batch, không có bản đơn.** Thêm `GetAsync(Guid)` "cho tiện" là mở lại đường N+1 đúng ở endpoint trọng
  điểm hiệu năng của GĐ4 (GOAL-01), và không ai thấy cho tới lúc chạy k6.

**Bước 2 — `IFriendshipReader` + `AlwaysStrangers`.**

```csharp
public interface IFriendshipReader
{
    Task<bool> AreFriendsAsync(Guid userId, Guid otherUserId, CancellationToken ct = default);
}

/// <summary>
/// Null-object CÓ CHỦ ĐÍCH (Đ-2.9, Mục 7.4), không phải chỗ trống: GĐ2 chưa có module SocialGraph,
/// nên bài "friends" chỉ tác giả xem được. GĐ4 đổi MỘT dòng DI sang hiện thực thật và KHÔNG chạm
/// module Content. Có test pin giữ hành vi này — đổi nó mà không đổi test là đỏ.
/// </summary>
public sealed class AlwaysStrangers : IFriendshipReader
{
    public Task<bool> AreFriendsAsync(Guid userId, Guid otherUserId, CancellationToken ct = default) =>
        Task.FromResult(false);
}
```

**Bước 3 — `UserDirectory` trong `Profile/Infrastructure/Directory/`.** Một truy vấn, không vòng lặp:

```csharp
public async Task<IReadOnlyDictionary<Guid, UserCard>> GetManyAsync(
    IReadOnlyCollection<Guid> userIds, CancellationToken ct = default)
{
    if (userIds.Count == 0) return new Dictionary<Guid, UserCard>();   // không chạm DB khi rỗng

    return await db.Profiles
        .Where(p => userIds.Contains(p.UserId))
        .Select(p => new UserCard(p.UserId, p.DisplayName, p.AvatarKey))
        .ToDictionaryAsync(c => c.UserId, ct);
}
```

`Select` trước `ToDictionary` là cố ý: nó sinh `SELECT user_id, display_name, avatar_key` chứ không kéo cả
dòng. Id không có hồ sơ thì **vắng mặt** trong dictionary — không phải giá trị `null`. Người gọi ở `D6`
phải xử lý trường hợp vắng mặt, dù Đ-2.4 (không có hồ sơ thì không đăng được bài) làm cho nó gần như không
xảy ra.

**Bước 4 — đăng ký DI.** `IUserDirectory` đăng ký ở **`AddProfileModule`** (module chủ đăng ký hiện thực của
mình — đúng tiền lệ `IRolePermissionSource` → `RolePermissionSource` trong `AddIdentityModule`, xem `C5` của
GĐ1). `IFriendshipReader` → `AlwaysStrangers` đăng ký ở **`AddContentModule`**, kèm comment nêu đích danh GĐ4.

Hệ quả cần biết: từ giờ **Content chạy được chỉ khi host đã gọi `AddProfileModule`**. Không có gì bắt lỗi
lúc build — nó nổ lúc resolve service của request đầu tiên. Thêm một khẳng định vào
`StartupConfigurationTests`: resolve được `IUserDirectory` và `IFriendshipReader` từ container của host.

**Bước 5 — test pin.** Unit test (`tests/SocialApp.UnitTests/`), hai khẳng định: hai id khác nhau → `false`;
**cùng một id** → cũng `false` (bản thân mình không phải "bạn" của mình; đường tác giả xem bài của chính
mình đi qua nhánh `author_id == actorId` ở `D6`, không đi qua đây). Viết vào XML doc của test câu "đây là
test pin, GĐ4 thay hiện thực thì sửa DI chứ không sửa test này".

### Cạm bẫy đã biết

1. **Đặt `IUserDirectory` vào `Profile/Application/`.** `ModuleBoundaryTests` chặn **mọi** phụ thuộc giữa
   hai namespace `SocialApp.Modules.*`, kể cả phụ thuộc vào một interface. Đó chính là lý do contract nằm ở
   SharedKernel chứ không ở module chủ.
2. **Trả `UserProfile` thay vì `UserCard`.** Compile được (SharedKernel không tham chiếu module... nhưng
   module tham chiếu SharedKernel, nên kiểu `UserProfile` không đặt vào chữ ký ở SharedKernel được) — nếu
   ai đó "sửa cho chạy" bằng cách chuyển interface sang module thì xem cạm bẫy 1.
3. **`AlwaysStrangers` trả `true` "cho dễ test".** Đó là mở toàn bộ bài `friends` cho mọi người. Test pin
   tồn tại đúng để chặn chuyện này.
4. **ArchUnitNET không đọc được chuỗi SQL.** Một câu `FromSql("select * from profile.profiles")` trong
   module Content sẽ **xanh** ở mọi test. Đây là một trong bốn chỗ B.9 giao cho **code review**, không giao
   cho máy.

---

## 8. A7 — Test canh gác hai namespace `Domain`

**Mục tiêu.** Đóng "lưới giả". `ModuleBoundaryTests` và `PersistenceBoundaryTests` đều dùng
`WithoutRequiringPositiveResults()`: khi namespace không khớp type nào, rule **xanh vĩnh viễn**. Trước `A1`
thì `SocialApp.Modules.Profile.Domain` và `SocialApp.Modules.Content.Domain` đúng là rỗng — mọi luật ranh
giới của GĐ2 đang được canh bởi một cái lưới không có dây.

**Kết quả mong đợi.**
- Hai `[Fact]` mới trong
  [PersistenceBoundaryTests.cs](../../tests/SocialApp.ArchitectureTests/PersistenceBoundaryTests.cs), chép
  đúng khuôn `Identity_Domain_namespace_must_not_be_empty` (đã có, không `Skip`).
- Thử đổi một namespace trong test cho sai → **đỏ**; khôi phục → xanh. `git status` sạch trước và sau.

### Các bước

**Bước 1 — bản Profile, đi cùng commit `A1`:**

```csharp
[Fact]
public void Profile_Domain_namespace_must_not_be_empty()
{
    var types = Architecture.Types
        .Where(t => t.FullName.StartsWith("SocialApp.Modules.Profile.Domain", StringComparison.Ordinal))
        .ToList();

    Assert.True(types.Count > 0,
        "Không có type nào trong SocialApp.Modules.Profile.Domain — rule persistence boundary "
      + "đang chạy trong chân không. Kiểm tra lại namespace trong PersistenceBoundaryTests.");
}
```

**Bước 2 — bản Content, đi cùng commit `A4`**, y hệt với `SocialApp.Modules.Content.Domain`.

**Bước 3 — thử cho đỏ một lần.** Đổi `"SocialApp.Modules.Profile.Domain"` thành
`"SocialApp.Modules.Profile.Domainn"`, chạy `dotnet test tests/SocialApp.ArchitectureTests`, thấy đỏ, khôi
phục. Đây là luật chung của repo cho mọi cổng mới (luật frontend Mục 9, áp tương tự cho backend): **cổng
chưa từng đỏ là cổng chưa biết có chặn được không**.

### Cạm bẫy đã biết

1. **Không phải sửa `ModuleNames` trong hai file test.** Cả `ModuleBoundaryTests` lẫn
   `PersistenceBoundaryTests` đã liệt kê đủ bảy module từ GĐ0. Mục 10.4 điểm 5 nói rõ: nếu **phải** sửa
   chúng để code xanh thì code đang phá ranh giới, không phải test sai.
2. **Không phải thêm `ProjectReference`.** `SocialApp.ArchitectureTests` tham chiếu `SocialApp.Api`, mà Api
   đã tham chiếu cả bảy module — nên `Assembly.Load("SocialApp.Modules.Profile")` tìm thấy assembly trong
   thư mục output.
3. **Hai test này kiểm "có type", không kiểm "type đúng".** Chúng chỉ nói cái lưới có dây, không nói lưới
   đúng chỗ. Thứ kiểm đúng chỗ là `ModuleBoundaryTests` + code review (Mục 7 cạm bẫy 4).

---

## 9. Kế hoạch commit

Sáu commit, tách theo mã việc (luật commit Mục 8), scope `gd2-a` (luật commit Mục 3). Mỗi commit chạm code
**phải** có dòng `Test:` và dòng `detect-changes:` (Mục 5.5, 5.6), và footer **sạch bút ký** (Mục 6).

| # | Tiêu đề | Gồm |
|---|---|---|
| 1 | `chore(gd2-a): thêm gói EF Core 8.0.10 cho hai module Profile và Content` | Mục 1.2 — hai csproj. Tách riêng để diff của `A1` không lẫn thay đổi hạ tầng build |
| 2 | `feat(gd2-a): A1 + A7 — entity hồ sơ và canh gác namespace Profile.Domain` | `A1` + `A7` bản Profile. Nếu chốt tên `UserProfile` thì **sửa B.3 của `giai-doan-2.md` trong chính commit này** |
| 3 | `feat(gd2-a): A2–A3 — schema profile migrate được, hook --migrate chạy hai lần không đổi gì` | `A2` + `A3` + `ProfileDbContextSchemaTests` + hai dòng `Program.cs` |
| 4 | `feat(gd2-a): A4 + A7 — entity nội dung, khung comment/reaction, BR-01 dạng hàm thuần` | `A4` + `A7` bản Content + unit test BR-01 |
| 5 | `feat(gd2-a): A5 — schema content với bốn bất biến do DB giữ` | `A5` + `ContentDbContextSchemaTests` + hai dòng `Program.cs` |
| 6 | `feat(gd2-a): A6 — hai contract chéo module ở SharedKernel, kết bạn là null-object` | `A6` + test pin + khẳng định mới trong `StartupConfigurationTests` |

Gộp `A2`+`A3` (và có thể `A4`+`A5`) là **được**, vì migration không tách khỏi configuration sinh ra nó —
nhưng phải nói rõ lý do trong thân bài (luật commit Mục 8).

Mẫu thân bài cho commit #5:

```
ContentDbContext đặt bốn bất biến của Mục 4 xuống cho Postgres giữ: ck_posts_not_empty (BR-01 ở tầng DB),
storage_key UNIQUE (chặn gắn một object vào hai bài), uq_media_owner_position, và PK ba cột của reactions
(chính là BR-05). Global query filter loại status='deleted' theo Đ-2.10; chỗ duy nhất được IgnoreQueryFilters
là repository của worker dọn rác (C4), đã ghi bằng chữ cạnh filter.

Lệch Đ-2.12 (không lệch): comments/reactions chỉ có thuộc tính, không hành vi, không repository — đúng giới hạn
đã chốt.

Test: Unit 78 → 82, Integration 155 → 161 (+6 ContentDbContextSchemaTests). Thử cho đỏ 3 đột biến đều bị bắt.
detect-changes: low, 0 luồng
```

---

## 10. Checklist nghiệm thu khối A

Tick từng dòng, có bằng chứng. Dòng nào không áp dụng thì ghi lý do, **không xóa dòng**.

**Schema và migration**

- [ ] `\dn` trên DB dev thấy **ba** schema: `identity`, `profile`, `content`
- [ ] Mỗi schema có đúng **một** `__EFMigrationsHistory` của riêng nó; **không** có cái nào ở `public`
- [ ] `--migrate` trên DB sạch: lần 1 exit 0, lần 2 exit 0 và không đổi gì
- [ ] `dotnet ef migrations add Tmp` cho **cả hai** module sinh ra migration **rỗng** (rồi `migrations remove`)
- [ ] Ba file migration của mỗi module (`.cs`, `.Designer.cs`, `ModelSnapshot.cs`) đã commit
- [ ] **Không có FK nào đi qua ranh giới schema** — chạy và thấy 0 dòng:

```sql
SELECT con.conname,
       src.relnamespace::regnamespace AS tu_schema,
       tgt.relnamespace::regnamespace AS toi_schema
FROM pg_constraint con
JOIN pg_class src ON src.oid = con.conrelid
JOIN pg_class tgt ON tgt.oid = con.confrelid
WHERE con.contype = 'f' AND src.relnamespace <> tgt.relnamespace;
```

**Ranh giới**

- [ ] `grep -rn "Microsoft.EntityFrameworkCore\|Npgsql" src/backend/Modules/Profile/Domain src/backend/Modules/Content/Domain` → 0 kết quả
- [ ] `grep -rn "SocialApp.Modules" src/backend/Modules/Profile src/backend/Modules/Content` không thấy tên module khác
- [ ] `dotnet test tests/SocialApp.ArchitectureTests` xanh, và hai test canh gác `A7` **đã từng thấy đỏ** một lần

**Ràng buộc do DB giữ**

- [ ] `ck_profiles_display_name_not_blank` chặn thật (`display_name = '   '` → lỗi)
- [ ] `ck_posts_not_empty` chặn bài rỗng **và** cho qua bài chỉ có ảnh
- [ ] `storage_key` trùng bị chặn (là nguồn của 409 ở `D5`)
- [ ] `reaction_counts` của bài mới đọc ra `{}`, không phải `null`
- [ ] Bài `status='deleted'` không xuất hiện qua `db.Posts`

**Contract chéo module**

- [ ] `IUserDirectory` chỉ có phương thức đọc, chỉ nhận/trả batch, trả `UserCard` chứ không trả entity
- [ ] Test pin `AlwaysStrangers` xanh, có XML doc nói rõ GĐ4 sửa DI chứ không sửa test
- [ ] Host resolve được `IUserDirectory` và `IFriendshipReader` (khẳng định mới trong `StartupConfigurationTests`)

**Luật repo**

- [ ] Không có cột nào ngoài Mục 4; có lệch thì Mục 4 đã sửa **trong cùng commit**
- [ ] Tên entity `UserProfile` (nếu chốt phương án đó) đã được ghi ngược vào B.3 của `giai-doan-2.md`
- [ ] Không có secret trong diff; không có khóa R2 ở đâu cả (khối A không chạm R2)
- [ ] Mọi commit có dòng `Test:` và `detect-changes:`; footer sạch bút ký

---

## 11. Khối A để lại gì

| Di sản | Ai thừa hưởng ngay | Ai thừa hưởng về sau |
|---|---|---|
| Khuôn "module thứ hai và thứ ba": schema riêng + context riêng + options dùng chung + design-time factory | `D1`–`D8` | SocialGraph (GĐ4), Messaging (GĐ5), Notification + Moderation (GĐ6) |
| `profile.profiles` migrate được | `D1`–`D3`, `E2`, `E3` | GĐ6 (tìm kiếm — index trgm thêm sau), GĐ8 (xóa tài khoản fan-out) |
| `content.posts` + `media_attachments` + index keyset | `D5`, `D6`, `C3` | GĐ4 (feed dùng lại `idx_posts_author_created`) |
| Khung `comments` + `reactions` + hai cột bộ đếm | `D6` (hình dạng `PostResponse` không phải đổi) | GĐ3 — `ALTER` thêm, không đổi DTO lần hai |
| `ck_posts_not_empty` và thứ tự INSERT đã có test canh | `D5`, `B3` | — |
| `storage_key` UNIQUE | `D5` (nguồn của 409) | `C4` (worker biết object nào mồ côi) |
| Global query filter loại bài xóa mềm | `D6`, `D8` | `C4` — chỗ duy nhất `IgnoreQueryFilters()` |
| `IUserDirectory` batch | `D6` | GĐ4 (feed 20 bài/trang), GĐ6 (thông báo, tìm kiếm) |
| `IFriendshipReader` null-object + test pin | `D6` (BR-02 ở Mục 7.4) | GĐ4 — đổi **một dòng DI**, không chạm module Content |
| Hai test canh gác namespace | Mọi rule ranh giới của GĐ2 | GĐ3–GĐ8 |

---

## 12. Ranh giới — cái gì **không** thuộc khối A

Nói rõ để không ai kỳ vọng nhầm, và để khối A không phình ra nuốt mất đường găng của lane khác:

| Không thuộc khối A | Thuộc về | Vì sao dễ nhầm |
|---|---|---|
| Controller, DTO, validator, `[ApiExplorerSettings]`, `AddApplicationPart`, `apiGroups` | `D0`–`D9` | Cũng là "code của module Profile/Content" |
| `profile-v1.yaml`, `content-v1.yaml` | **Cổng mở** (Mục 9), commit trước khi ai gõ code | Hợp đồng đi trước, không đi sau entity |
| `IObjectStorage`, presign, `HeadAsync`, `R2Options` | `C1`–`C3`, ở **SharedKernel** | `avatar_key`/`storage_key` là cột của khối A, nhưng chỉ là `varchar` |
| Worker dọn rác + khóa Redis | `C4` | Nó dùng `IgnoreQueryFilters()` mà filter do `A5` đặt |
| `SeededContentDatabaseAsync` trong `PostgresFixture` | `B1` | Khối A dùng `CreateDatabaseAsync` (DB mới, chưa migrate) — đã có sẵn từ GĐ1 |
| Sáu dòng AuthZ matrix | `B2` | Đ-2.6 nói GĐ2 **không** thêm mã quyền mới, nên khối A không chạm gì tới quyền |
| `ProfileContractTests`, `ContentContractTests`, hai dòng `Content Include` trong csproj test | `B4` | Chúng cần controller có thật |
| Mã hóa/giải mã cursor | `D6` | Đ-2.11 chốt **hình dạng** ở cổng mở; index hỗ trợ nó là `A5`; code là `D6` |
| Seeder | **Không ai** — Mục 5: GĐ2 không seed gì | Khuôn `MigrateIdentityModuleAsync` có gọi seeder, dễ chép nhầm |
