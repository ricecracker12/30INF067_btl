# Hướng dẫn thực hiện — Khối A. Nền dữ liệu (GĐ1)

> Bản triển khai chi tiết của **B.3 Khối A** trong [giai-doan-1.md](giai-doan-1.md). Tài liệu gốc trả
> lời *cái gì* và *vì sao*; tài liệu này trả lời *gõ vào file nào, theo thứ tự nào, và nhìn vào đâu
> để biết đã xong thật*.
>
> **Nguồn sự thật vẫn là `giai-doan-1.md`** (Mục 3.2–3.5 quyết định thiết kế, Mục 4 schema, Mục 5 dữ
> liệu seed, Mục 10.1 mã test) và `AGENTS.md`. Chỗ nào tài liệu này lệch với hai file đó thì sửa ở
> đây — không sửa ngược.

| | |
|---|---|
| **Người làm** | BE-1 (một người, lane độc lập) |
| **Thời lượng** | Ngày 3 cả ngày → sáng Ngày 4 |
| **Khối này chặn** | `C5` (nối handler vào repository thật), toàn bộ khối **D**, `B5` (test dữ liệu nền), `F1` (deploy staging) |
| **Khối này cần trước** | Không gì cả — bắt đầu được ngay từ giờ đầu tiên của Ngày 3 |

---

## 0. Danh sách công việc — mục tiêu và kết quả mong đợi

Bảy đầu việc, thứ tự tuyến tính. Đây là **đường găng của cả GĐ1**: mỗi giờ trượt ở đây là một giờ
trượt của khối D.

| Mã | Đầu việc | Mục tiêu — việc này tồn tại để làm gì | Kết quả mong đợi — thứ kiểm chứng được |
|---|---|---|---|
| **A1** | Sáu entity trong `Domain/` | Có mô hình nghiệp vụ để mọi tầng khác bám vào; đồng thời làm cho rule `PersistenceBoundaryTests` hết chạy trong chân không | 6 file entity + 3 file hằng số trong `SocialApp.Modules.Identity.Domain`, không file nào `using Microsoft.EntityFrameworkCore`; `dotnet build` xanh; `A7` gỡ được `Skip` và xanh |
| **A2** | `IEntityTypeConfiguration` trong `Infrastructure/` | Đẩy bất biến quan trọng xuống cho **DB** giữ, thay vì trông chờ code nhớ giữ | 6 file config + 6 `DbSet` trong `IdentityDbContext`; `IdentityDbContextSchemaTests` mở rộng và xanh: 6 bảng nằm trong schema `identity`, FK `users.role_id` là RESTRICT, extension `citext` tồn tại |
| **A3** | Migration đầu tiên | Biến schema thành artifact có version, tái lập y hệt ở mọi môi trường — không ai "sửa tay trên staging" | Thư mục `Infrastructure/Migrations/` có `<timestamp>_InitialIdentity.cs` + `.Designer.cs` + `IdentityDbContextModelSnapshot.cs` đã commit; `dotnet ef database update` trên compose dev chạy sạch; test schema xanh trên Postgres thật |
| **A4** | Seeder idempotent | Ma trận quyền là **dữ liệu**, không phải code; và CD deploy lại 10 lần/ngày cũng không nhân bản hay ghi đè cấu hình mà Admin đã sửa | `IdentitySeeder.SeedAsync` seed đúng 3 vai trò + 17 quyền + 24 dòng gán quyền; `SEED-01` và `SEED-02` xanh trên Postgres thật |
| **A5** | Kiểm tra vai trò hệ thống lúc khởi động | Bắt kịch bản nguy hiểm nhất của GĐ1: ai đó đổi `roles.code` bằng tay → short-circuit `role == "ADMIN"` không khớp → mất sạch quyền quản trị, im lặng, không đường phục hồi | ~5 dòng nằm **trong** seeder, ném `InvalidOperationException` nêu đúng tên vai trò thiếu; `SEED-03` xanh |
| **A6** | Nối vào hook `--migrate` | Một lệnh duy nhất ở bước deploy làm trọn ba việc: nâng schema → nạp dữ liệu nền → tự kiểm tra | `dotnet run --project src/SocialApp.Api -- --migrate` trên DB sạch cho exit code 0 và in dòng xác nhận; chạy lần hai vẫn 0, dữ liệu không đổi |
| **A7** | Gỡ `Skip` của `Identity_Domain_namespace_must_not_be_empty` | Đóng "lưới giả" mà `PersistenceBoundaryTests` tự cảnh báo về chính nó: rule dùng `WithoutRequiringPositiveResults` nên gõ sai namespace là xanh vĩnh viễn | Thuộc tính `Skip` biến mất khỏi [PersistenceBoundaryTests.cs](../tests/SocialApp.ArchitectureTests/PersistenceBoundaryTests.cs); test chạy thật và xanh |

**Thứ tự bắt buộc:** `A1 → A2 → A3 → A4 → A5 → A6`. Riêng **`A7` đi kèm `A1` trong cùng một commit** —
để sang commit sau là nó thành nợ, và nợ loại này không ai nhớ trả.

**Ba cột mốc để đo tiến độ trong ngày:** hết buổi sáng Ngày 3 xong `A1`+`A2`; đầu giờ chiều xong
`A3` (từ lúc này khối D bắt đầu viết được); cuối Ngày 3 xong `A4`+`A5`; sáng Ngày 4 xong `A6`.

---

## 1. Trước khi gõ dòng đầu tiên — bốn điều kiện cần

Kiểm đủ bốn cái này trước, mỗi cái mất chưa tới một phút. Phát hiện thiếu ở giữa `A3` thì đắt hơn
nhiều.

```bash
# 1. Postgres dev đang chạy (seeder và migration đều cần DB thật, không dùng InMemory)
#    compose tự nạp deploy/.env (gitignore, chứa POSTGRES_PASSWORD) — chưa có thì chép file mẫu rồi
#    ĐIỀN POSTGRES_PASSWORD (file mẫu để trống; thiếu thì compose từ chối chạy):
#    cp deploy/.env.example deploy/.env
docker compose -f deploy/docker-compose.dev.yml up -d
docker compose -f deploy/docker-compose.dev.yml ps        # postgres phải healthy

# 2. EF tools có sẵn và đúng dòng 8.x (khớp Microsoft.EntityFrameworkCore 8.0.10)
dotnet ef --version
# chưa có thì: dotnet tool install --global dotnet-ef --version 8.0.10

# 3. Solution build sạch từ điểm xuất phát
dotnet build SocialApp.sln

# 4. Docker daemon chạy được — IntegrationTests dùng Testcontainers
docker ps
```

**Package: không cần thêm gì.** Đây là điểm hay bị hiểu nhầm vì Mục 9.0 "Nợ 3" liệt kê 4 gói cần bổ
sung. Cả 4 **đã được thêm rồi**, khối A không phải cài gì nữa:

| Gói | Version | Ở đâu | Khối A dùng để |
|---|---|---|---|
| `Microsoft.EntityFrameworkCore` | 8.0.10 | `SocialApp.Modules.Identity` | `IEntityTypeConfiguration`, `DbSet` |
| `Microsoft.EntityFrameworkCore.Design` | 8.0.10 | `SocialApp.Modules.Identity` | `dotnet ef migrations add` với startup project là chính module |
| `Npgsql.EntityFrameworkCore.PostgreSQL` | 8.0.10 | `SocialApp.Modules.Identity` | `citext`, `inet`, `HasPostgresExtension` |
| `UUIDNext` | 4.2.4 | `SocialApp.SharedKernel` | Sinh PK UUID v7 (`A1`) |
| `Testcontainers.PostgreSql` | 4.0.0 | `SocialApp.IntegrationTests` | Nghiệm thu `A2`, `A3`, `A4`, `A5` |

**Ba luật của repo áp thẳng vào khối A** — vi phạm cái nào cũng có test bắt, nhưng biết trước thì
không phải sửa lại:

1. **Domain tuyệt đối không chạm EF Core / Npgsql** (ADR-001). `PersistenceBoundaryTests` bắt. Hệ quả
   thực tế: entity là POCO thuần, không attribute `[Column]`, không `[Table]`, không navigation nào
   cần kiểu của EF.
2. **Không auto-migrate lúc app start** (AGENTS.md Mục 13). Mọi thứ khối A dựng chỉ chạy ở hook
   `--migrate`, tức `A6`.
3. **Không tự bịa schema** (AGENTS.md Mục 14.1). Cột nào không có trong Mục 4 thì không thêm — kể cả
   khi "thấy cần". Thiếu cột thật thì sửa Mục 4 trước, trong cùng commit.

---

## 2. A1 — Sáu entity trong `Domain/`

**Mục tiêu.** Có mô hình nghiệp vụ để cả khối C lẫn khối D bám vào, và đóng lỗ hổng "rule persistence
chạy trong chân không" mà `PersistenceBoundaryTests` đang tự cảnh báo bằng một `Skip`.

**Kết quả mong đợi.**
- 6 file entity + 3 file hằng số dưới `src/Modules/Identity/Domain/`, đúng namespace
  `SocialApp.Modules.Identity.Domain`.
- Grep `using Microsoft.EntityFrameworkCore` trong `Domain/` ra **0 kết quả**.
- `dotnet build SocialApp.sln` xanh.
- `dotnet test tests/SocialApp.ArchitectureTests` xanh **sau khi đã gỡ `Skip` ở `A7`** — nghĩa là
  rule persistence từ giờ chạy trên tập type có thật.

### Các bước

**Bước 1 — ba file hằng số trước, entity sau.** Làm ngược lại thì `RoleCodes.Admin` ở Mục 5.5 không
có chỗ để trỏ tới, và mỗi người sẽ tự gõ chuỗi `"ADMIN"` ở một chỗ khác nhau.

| File | Nội dung |
|---|---|
| `Domain/RoleCodes.cs` | `public const string User = "USER"; Moderator = "MODERATOR"; Admin = "ADMIN";` + mảng `All` |
| `Domain/PermissionCodes.cs` | 17 hằng số `resource.action` đúng Mục 5.2 |
| `Domain/UserStatus.cs` | `Active/Locked/Disabled/Deleted` dạng `const string` — **không dùng `enum`**, vì cột là `varchar(20)` có `CHECK`, và enum sẽ kéo theo chuyện ánh xạ int/string không cần thiết |

`RoleCodes` chính là hiện thân của quyết định 3.1 ("`code` chuỗi là bất biến theo hợp đồng API"). Ba
nơi sẽ đọc nó: seeder (`A4`), kiểm tra khởi động (`A5`), và Admin short-circuit của khối C.

**Bước 2 — một hàm sinh UUID v7 dùng chung, đặt ở SharedKernel.** `Guid.CreateVersion7()` chỉ có từ
.NET 9; dự án đang net8.0. `Guid.NewGuid()` là v4 ngẫu nhiên → phân mảnh index B-tree, đúng thứ GĐ4
sẽ trả giá khi feed cần index tốt.

Tạo `src/SocialApp.SharedKernel/Ids/Uuid7.cs` — một hàm tĩnh gói lời gọi UUIDNext lại:

```csharp
namespace SocialApp.SharedKernel.Ids;

/// <summary>
/// Sinh UUID v7 (có tiền tố timestamp -> tuần tự -> index B-tree không phân mảnh).
/// Gói UUIDNext lại một lớp: đổi thư viện hoặc lên .NET 9 (Guid.CreateVersion7) chỉ sửa ở đây.
/// </summary>
public static class Uuid7
{
    public static Guid New() => UUIDNext.Uuid.NewDatabaseFriendly(UUIDNext.Database.PostgreSql);
}
```

> Tên chính xác của enum (`Database.PostgreSql` / `PostgreSQL`) xác nhận lại bằng IntelliSense lúc gõ
> — UUIDNext đổi cách đặt tên giữa các bản. Cái quan trọng là **chỉ một chỗ trong repo gọi thẳng vào
> UUIDNext**, và đó là file này.

Đặt ở SharedKernel chứ không ở Identity vì mọi module GĐ2–GĐ6 đều cần; Identity đã tham chiếu
SharedKernel sẵn.

**Bước 3 — sáu entity, đúng cột theo Mục 4, không thêm không bớt.**

| File | Bảng | Khóa chính | Điểm phải đúng |
|---|---|---|---|
| `Domain/Role.cs` | `roles` | `short RoleId` | Tách **`Code`** (bất biến, `varchar(30)`) và **`DisplayName`** (đổi thoải mái, `varchar(50)`) — quyết định 3.3. `RoleId` do seeder gán tay (1/2/3), **không tự sinh** |
| `Domain/Permission.cs` | `permissions` | `short PermissionId` | `Code` dạng `resource.action`, `varchar(40)`. Cũng gán tay 1→17 |
| `Domain/RolePermission.cs` | `role_permissions` | `(RoleId, PermissionId)` | Bảng nối thuần, không có cột nào khác. Mỗi dòng = một dấu tick trong ma trận 6.7.2 |
| `Domain/User.cs` | `users` | `Guid UserId` (v7) | `PasswordHash` `varchar(72)`; `RoleId` là **`short`**, không phải chuỗi; `FailedLoginCount` là `short`; `LockedUntil`, `EmailVerifiedAt` nullable `DateTimeOffset?` |
| `Domain/RefreshToken.cs` | `refresh_tokens` | `Guid Id` (v7) | Có **cả** `FamilyId` **lẫn** `ReplacedById` — quyết định 3.5, không bỏ cái nào. `TokenHash` là SHA-256 hex `varchar(64)`, **không bao giờ lưu bản rõ**. `CreatedIp` kiểu `System.Net.IPAddress?` (BCL, Npgsql ánh xạ thẳng sang `inet`) |
| `Domain/EmailVerificationToken.cs` | `email_verification_tokens` | `Guid Id` (v7) | `TokenHash` `varchar(64)`, `ConsumedAt` nullable — token dùng một lần |

Khuôn chung, lấy `RefreshToken` làm ví dụ vì nó là cái nhiều bẫy nhất:

```csharp
using System.Net;
using SocialApp.SharedKernel.Ids;

namespace SocialApp.Modules.Identity.Domain;

/// <summary>
/// Một refresh token đã phát. TokenHash là SHA-256 hex của bản rõ — bản rõ chỉ tồn tại trong
/// response và trong cookie của client, KHÔNG bao giờ nằm trong DB hay log.
///
/// FamilyId: mọi token sinh ra từ một lần đăng nhập mang cùng FamilyId. Reuse detection thu hồi
/// nguyên family chứ không chỉ token bị dùng lại (Mục 3.5) — nếu chỉ thu hồi một cái, kẻ trộm vẫn
/// giữ được nhánh còn lại.
/// ReplacedById: dấu vết ai thay ai, để điều tra sau sự cố. Hai cột phục vụ hai mục đích khác nhau,
/// giữ cả hai.
/// </summary>
public sealed class RefreshToken
{
    public Guid Id { get; init; } = Uuid7.New();
    public required Guid UserId { get; init; }
    public required Guid FamilyId { get; init; }
    public required string TokenHash { get; init; }
    public required DateTimeOffset ExpiresAt { get; init; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid? ReplacedById { get; set; }
    public IPAddress? CreatedIp { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}
```

### Cạm bẫy đã biết

- **`DateTime` thay vì `DateTimeOffset`.** Cột là `timestamptz`. Npgsql 8 ánh xạ `timestamptz` ↔
  `DateTimeOffset` (hoặc `DateTime` có `Kind = Utc`); dùng `DateTime` mặc định `Kind = Unspecified`
  sẽ ném lúc lưu. Thống nhất `DateTimeOffset` cho toàn bộ khối A.
- **`Guid.NewGuid()` lọt vào vì quen tay.** Sau khi viết xong 6 entity, grep một lượt:
  `grep -rn "Guid.NewGuid" src/` phải ra rỗng.
- **`RoleId` để EF tự sinh.** `roles.role_id` và `permissions.permission_id` là số cố định do seeder
  gán. Chưa cấu hình `ValueGeneratedNever()` (việc của `A2`) thì EF sẽ sinh cột `identity` và
  migration ra sai — bắt được ở bước đọc lại file migration của `A3`.
- **Thêm navigation property `Role.Users`, `User.Role`.** Chưa cần cho GĐ1 và mỗi navigation là một
  đường để code sau này vô tình `.Include()` cả bảng. Thêm khi có nơi thực sự dùng.

---

## 3. A2 — `IEntityTypeConfiguration` trong `Infrastructure/`

**Mục tiêu.** Ánh xạ entity xuống Postgres đúng ràng buộc, để những bất biến quan trọng được **DB**
giữ chứ không phải code nhớ giữ. Cụ thể: FK RESTRICT trên `users.role_id` chính là biện pháp #1 thay
cho cột `is_system` đã bị loại bỏ (Mục 3.4) — nó làm cho `DELETE FROM roles` khi còn user là **không
thể**, ở tầng dưới cùng, không phụ thuộc tầng app nhớ kiểm.

**Kết quả mong đợi.**
- 6 file dưới `src/Modules/Identity/Infrastructure/Configurations/`.
- 6 `DbSet<>` trong `IdentityDbContext`, và `HasPostgresExtension("citext")` trong `OnModelCreating`.
- Override `SaveChanges`/`SaveChangesAsync` trong `IdentityDbContext` gán `UpdatedAt` cho entity bị
  sửa — đồng hồ app, không trigger (Mục 4 "Nguồn thời gian").
- `IdentityDbContextSchemaTests` mở rộng thêm 4 khẳng định và xanh trên Postgres thật: đủ 6
  bảng trong schema `identity`; FK `users.role_id` có `delete_rule = 'RESTRICT'`; extension
  `citext` có mặt trong `pg_extension`; `DEFAULT now()` trên đủ 5 cột `created_at`/`updated_at`.

### Các bước

**Bước 1 — khai `DbSet` và extension trong `IdentityDbContext`.** File đã có sẵn
`ApplyConfigurationsFromAssembly`, nên các config sẽ tự được nạp; nhưng `DbSet` vẫn cần vì `A5` viết
`db.Roles.Select(...)`, và `HasPostgresExtension` **bắt buộc phải nằm ở `ModelBuilder`** — không đặt
được trong `IEntityTypeConfiguration`. Đây là chỗ hay mất 20 phút để tìm ra.

```csharp
public DbSet<Role> Roles => Set<Role>();
public DbSet<Permission> Permissions => Set<Permission>();
public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
public DbSet<User> Users => Set<User>();
public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
public DbSet<EmailVerificationToken> EmailVerificationTokens => Set<EmailVerificationToken>();

protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.HasDefaultSchema(Schema);
    modelBuilder.HasPostgresExtension("citext");   // email không phân biệt hoa thường
    modelBuilder.ApplyConfigurationsFromAssembly(typeof(IdentityDbContext).Assembly);
    base.OnModelCreating(modelBuilder);
}
```

Cũng trong `IdentityDbContext`: override `SaveChanges(bool)` và `SaveChangesAsync(bool, CancellationToken)`
— hai overload này là đích cuối của cả bốn đường lưu — để gán `UpdatedAt` cho mọi entry `Modified`
có property đó. Đây là nửa còn lại của quyết định "Nguồn thời gian" (Mục 4): `DEFAULT now()` chỉ chạy
lúc INSERT và repo không có trigger, nên thiếu override thì `updated_at` đứng im ở thời điểm tạo.

**Bước 2 — snake_case phải khai tay.** Repo không có `EFCore.NamingConventions`, nên EF sẽ đặt tên
bảng theo tên `DbSet` (`Roles`) và cột theo tên property (`RoleId`). Mục 4 quy định `roles` /
`role_id`. **Mỗi config phải gọi `.ToTable("...")` và `.HasColumnName("...")` cho từng cột.** Dài
dòng nhưng làm một lần; đừng thêm package chỉ để tránh việc này giữa GĐ1.

**Bước 3 — sáu file config.** Danh sách ràng buộc bắt buộc, không cái nào là tùy chọn:

| Config | Bắt buộc có |
|---|---|
| `RoleConfiguration` | `ToTable("roles")`; `HasKey(RoleId)` + **`ValueGeneratedNever()`**; `Code` `varchar(30)` required + **unique index**; `DisplayName` `varchar(50)` required; `Description` `varchar(120)` nullable; `CreatedAt`/`UpdatedAt` `HasDefaultValueSql("now()")` |
| `PermissionConfiguration` | `ToTable("permissions")`; `HasKey(PermissionId)` + `ValueGeneratedNever()`; `Code` `varchar(40)` + **unique index**; `Description` `varchar(120)` |
| `RolePermissionConfiguration` | `ToTable("role_permissions")`; `HasKey(RoleId, PermissionId)`; hai FK **`DeleteBehavior.Cascade`** |
| `UserConfiguration` | `ToTable("users", t => t.HasCheckConstraint("ck_users_status", "status IN ('active','locked','disabled','deleted')"))`; `Email` **`HasColumnType("citext")`** + unique index; `PasswordHash` `varchar(72)`; `Status` `varchar(20)` `HasDefaultValue("active")`; FK `RoleId` → `roles` **`DeleteBehavior.Restrict`**; `FailedLoginCount` `HasDefaultValue((short)0)`; `CreatedAt`/`UpdatedAt` `HasDefaultValueSql("now()")` |
| `RefreshTokenConfiguration` | `ToTable("refresh_tokens")`; `TokenHash` `varchar(64)` + unique index; FK `UserId` **Cascade**; **self-FK** `ReplacedById` → `refresh_tokens.id` với `DeleteBehavior.NoAction` — DDL Mục 4 ghi `REFERENCES` không kèm `ON DELETE`, tức `NO ACTION` (bản trước ghi `Restrict`; đã thử trên Postgres 16: xóa user cascade xuống chuỗi token xoay vòng chạy được với **cả hai**, nên đổi chỉ để khớp DDL, không phải vì lỗi chức năng); index `idx_refresh_user` trên `UserId`; **index một phần** `idx_refresh_family`: `.HasIndex(x => x.FamilyId).HasDatabaseName("idx_refresh_family").HasFilter("revoked_at IS NULL")`; `CreatedAt` `HasDefaultValueSql("now()")` |
| `EmailVerificationTokenConfiguration` | `ToTable("email_verification_tokens")`; `TokenHash` `varchar(64)` + unique index; FK `UserId` **Cascade**; `ConsumedAt` nullable |

**Bước 4 — mở rộng `IdentityDbContextSchemaTests`.** Test hiện tại mới chỉ khẳng định bảng
`__EFMigrationsHistory` nằm đúng schema. Thêm bốn khẳng định, dùng đúng khuôn `SqlQuery<T>` đã có:

```sql
-- đủ 6 bảng nghiệp vụ trong schema identity
select table_name as "Value" from information_schema.tables
where table_schema = 'identity' and table_name <> '__EFMigrationsHistory'

-- FK users.role_id phải là RESTRICT (biện pháp #1 thay cho is_system)
select rc.delete_rule as "Value"
from information_schema.referential_constraints rc
join information_schema.table_constraints tc on tc.constraint_name = rc.constraint_name
where tc.table_schema = 'identity' and tc.table_name = 'users'

-- extension citext đã bật
select extname as "Value" from pg_extension where extname = 'citext'

-- DEFAULT now() trên đủ 5 cột created_at/updated_at (Mục 4 "Nguồn thời gian")
select table_name || '.' || column_name || '=' || coalesce(column_default, '<none>') as "Value"
from information_schema.columns
where table_schema = 'identity' and column_name in ('created_at', 'updated_at')
```

### Cạm bẫy đã biết

- **Tưởng override `SaveChanges` phủ cả `ExecuteUpdateAsync`.** Override chỉ thấy entity trong
  ChangeTracker; `ExecuteUpdateAsync` và SQL thô đi thẳng xuống DB, nên `updated_at` đứng im mà không
  có lỗi gì. Chỗ nào dùng chúng — ví dụ tăng `failed_login_count` nguyên tử ở D3 — phải tự
  `SetProperty(x => x.UpdatedAt, DateTimeOffset.UtcNow)`.
- **Đặt `HasPostgresExtension` trong entity config** → không compile hoặc bị bỏ qua, rồi
  `citext` không có trong migration, rồi `dotnet ef database update` chết ở dòng tạo cột. Nó thuộc
  `ModelBuilder`.
- **`DeleteBehavior.Restrict` viết nhầm thành `ClientSetNull`** (mặc định của EF cho FK optional).
  `users.role_id` là **required** nên mặc định của EF là `Cascade` — tức là xóa role sẽ xóa sạch
  user. Ngược hoàn toàn với ý định. Phải khai tường minh.
- **Index một phần bị EF bỏ qua** nếu quên `.HasFilter(...)`, và khi đó nó vẫn là index hợp lệ nên
  không có lỗi nào — chỉ là index to hơn cần thiết. Bắt bằng cách đọc lại file migration ở `A3`.
- **`citext` cần extension được tạo trước bảng.** EF xếp `CREATE EXTENSION` lên đầu migration khi
  dùng `HasPostgresExtension`; nếu tự viết SQL tay thì thứ tự này là của mình.

---

## 4. A3 — Migration đầu tiên

**Mục tiêu.** Schema trở thành artifact có version, tái lập được y hệt ở mọi môi trường. Đây là thứ
biến "sửa tay trên staging" từ một thói quen thành một chuyện không cần làm.

**Kết quả mong đợi.**
- Ba file đã commit: `Infrastructure/Migrations/<timestamp>_InitialIdentity.cs`, `.Designer.cs`,
  `IdentityDbContextModelSnapshot.cs`.
- `dotnet ef database update` trên compose dev chạy sạch, không warning về model khác snapshot.
- `IdentityDbContextSchemaTests` (đã mở rộng ở `A2`) xanh trên Postgres thật qua Testcontainers.

### Các bước

**Bước 1 — sinh migration.** Startup project là **chính project module**, nhờ
[DesignTimeIdentityDbContextFactory.cs](../src/Modules/Identity/Infrastructure/DesignTimeIdentityDbContextFactory.cs)
— Api không phải kéo EF vào (ADR-001). Chạy từ gốc repo:

```bash
dotnet ef migrations add InitialIdentity \
  --project src/Modules/Identity/SocialApp.Modules.Identity.csproj \
  --startup-project src/Modules/Identity/SocialApp.Modules.Identity.csproj \
  --output-dir Infrastructure/Migrations
```

**Bước 2 — đọc lại file migration trước khi commit.** Đây là lúc rẻ nhất để phát hiện ánh xạ sai:
sau khi migration đã chạy trên staging thì sửa phải bằng migration thứ hai. Soát đúng chín dòng này:

| Soát | Đúng thì thấy |
|---|---|
| Schema | `migrationBuilder.EnsureSchema(name: "identity")` và mọi `CreateTable` có `schema: "identity"` |
| Extension | `migrationBuilder.AlterDatabase().Annotation("Npgsql:PostgresExtension:citext", ...)` ở đầu |
| Tên bảng/cột | `roles`, `role_id`, `display_name`… — **không phải** `Roles`, `RoleId` |
| PK smallint | `role_id` và `permission_id` **không** có `.Annotation("Npgsql:ValueGenerationStrategy", ...)` |
| FK RESTRICT | dòng `users.role_id` ghi `onDelete: ReferentialAction.Restrict` |
| FK CASCADE | `role_permissions`, `refresh_tokens.user_id`, `email_verification_tokens.user_id` ghi `Cascade` |
| Index một phần | `CreateIndex(name: "idx_refresh_family", ..., filter: "revoked_at IS NULL")` |
| CHECK | `constraints: table => table.CheckConstraint("ck_users_status", ...)` |
| DEFAULT thời gian | `defaultValueSql: "now()"` trên `roles.created_at`, `roles.updated_at`, `users.created_at`, `users.updated_at`, `refresh_tokens.created_at`. Thiếu thì đường EF vẫn chạy bình thường vì app luôn gửi giá trị tường minh — chỉ INSERT bằng SQL thô (seeder A4) mới hỏng, và chỉ lộ ra lúc đó (Mục 4 "Nguồn thời gian") |

Sai bất cứ dòng nào: **xóa migration rồi sinh lại**, đừng sửa tay file sinh ra — sửa tay thì snapshot
lệch với migration và migration kế tiếp sẽ sinh ra rác.

```bash
dotnet ef migrations remove \
  --project src/Modules/Identity/SocialApp.Modules.Identity.csproj \
  --startup-project src/Modules/Identity/SocialApp.Modules.Identity.csproj
```

**Bước 3 — áp lên compose dev.** Lệnh này **chạm DB thật**. Ở máy dev **không cần đặt biến nào**:
`DesignTimeIdentityDbContextFactory` tự dựng chuỗi `Host=localhost;Port=5432` với mật khẩu đọc từ
`POSTGRES_PASSWORD` trong `deploy/.env` — cùng file mà compose dev dùng
(`src/SocialApp.SharedKernel/Configuration/DevEnvFile.cs`). Repo không giữ mật khẩu ghi cứng nào.

> Chỉ đặt `ConnectionStrings__Postgres` khi muốn trỏ vào **DB khác** (DB tạm, staging) — biến môi trường
> thắng `deploy/.env`. Khi đó **đừng chép nguyên** `ConnectionStrings__Postgres` trong `deploy/.env`: chuỗi
> đó ghi `Host=postgres`, tên chỉ phân giải được bên trong mạng compose.
>
> **Bắt buộc có `deploy/.env` với `POSTGRES_PASSWORD`.** Thiếu file, hoặc chép file mẫu mà chưa điền, thì
> **mọi** lệnh `dotnet ef` — kể cả `migrations add` — từ chối chạy, thông báo nêu đúng chỗ sửa.

```bash
dotnet ef database update \
  --project src/Modules/Identity/SocialApp.Modules.Identity.csproj \
  --startup-project src/Modules/Identity/SocialApp.Modules.Identity.csproj
```

Kiểm nhanh bằng mắt một lần (chỉ lần này, về sau để test lo):

```bash
docker compose -f deploy/docker-compose.dev.yml exec postgres \
  psql -U socialapp -d socialapp -c '\dt identity.*'
```

**Bước 4 — chạy test schema thật.**

```bash
dotnet test tests/SocialApp.IntegrationTests --filter "FullyQualifiedName~IdentityDbContextSchemaTests"
```

### Cạm bẫy đã biết

- **`migrations remove` khi migration đã áp lên DB** sẽ báo lỗi hoặc để DB lệch. Thứ tự an toàn:
  `database update <migration trước>` (hoặc `0`) → `migrations remove` → sửa config → `migrations add`.
- **Quên commit `IdentityDbContextModelSnapshot.cs`.** Thiếu nó thì migration thứ hai của bất kỳ ai
  sẽ sinh lại toàn bộ `CreateTable` từ đầu. Kiểm bằng `git status` trước khi commit.
- **Migration nằm nhầm chỗ.** Phải là `Infrastructure/Migrations/` của module, không phải thư mục
  `Migrations/` ở gốc project — `--output-dir` lo việc này, đừng bỏ cờ đó.

---

## 5. A4 — Seeder idempotent

**Mục tiêu.** Ma trận quyền là **dữ liệu**, không phải code (Mục 6.7.2) — đó là điều làm cho "thêm
vai trò = thêm dữ liệu, không sửa code" thành sự thật thay vì khẩu hiệu. Và CD deploy lại 10 lần/ngày
cũng không được nhân bản dữ liệu hay ghi đè cấu hình mà Admin đã cố ý sửa.

**Kết quả mong đợi.**
- `src/Modules/Identity/Infrastructure/Seed/IdentitySeeder.cs` với một hàm public
  `SeedAsync(IdentityDbContext db, CancellationToken ct)`.
- Sau khi chạy: đúng **3** dòng `roles`, **17** dòng `permissions`, **24** dòng `role_permissions`
  (USER 11 + MODERATOR 13 + ADMIN 0).
- `SEED-01` xanh: chạy seeder hai lần, dữ liệu không đổi, không nhân bản.
- `SEED-02` xanh: gỡ 1 quyền của MODERATOR rồi chạy lại → **không bị cấp lại**.

### ⚠ Một điểm phải chốt trước khi gõ code

Mục 5.4 và B.3/A4 đều nói *"dùng `ON CONFLICT ... DO NOTHING`, tuyệt đối không `DO UPDATE`"*. Điều đó
**đúng cho `roles` và `permissions`** — hai bảng danh mục có cột `code` unique và có payload
(`display_name`, `description`) để mà ghi đè.

Nhưng **`role_permissions` không có payload: bản thân sự tồn tại của dòng chính là quyền.** Nếu Admin
gỡ quyền `post.hide` của MODERATOR ở GĐ6, dòng đó **biến mất**. Lần seed sau, câu
`INSERT ... ON CONFLICT DO NOTHING` không thấy xung đột nào cả → **chèn lại đúng cái quyền vừa bị gỡ**.
Đó chính là kịch bản `SEED-02` cấm, và `DO NOTHING` một mình không chặn được nó.

Cách chặn: **seed bảng gán quyền theo kiểu bootstrap-một-lần cho mỗi vai trò** — chỉ chèn khi vai trò
đó chưa có dòng nào:

```sql
INSERT INTO identity.role_permissions (role_id, permission_id)
SELECT 2, p FROM unnest(ARRAY[1,2,3,4,5,6,7,8,9,10,11,12,13]) AS p
WHERE NOT EXISTS (SELECT 1 FROM identity.role_permissions WHERE role_id = 2)
ON CONFLICT DO NOTHING;
```

- `WHERE NOT EXISTS` → MODERATOR đã có cấu hình (dù đã bị sửa) thì seeder **không đụng vào**. `SEED-02` xanh.
- `ON CONFLICT DO NOTHING` giữ lại để hai instance khởi động cùng lúc không đâm nhau. `SEED-01` xanh.
- Vẫn đúng tinh thần Mục 5.4: quyết định của Admin lúc runtime thắng seeder.
- **Ranh giới:** vai trò bị gỡ **hết** quyền trông giống vai trò chưa từng seed → lần deploy sau được
  bootstrap lại đủ bộ mặc định. GĐ1 chấp nhận (chưa có endpoint sửa quyền); GĐ6 cần một vai trò không
  quyền nào lâu dài thì phải thêm dấu vết seed, ví dụ bảng `seed_history`.

> **Việc phải làm kèm:** cập nhật Mục 5.4 và B.3/`A4` của `giai-doan-1.md` bằng đoạn này **trong cùng
> commit** với seeder (AGENTS.md Mục 14.7 — docs sống cùng code). Nếu nhóm muốn cách khác (ví dụ một
> bảng `seed_history` ghi phiên bản), chốt trước khi gõ, đừng để hai người hiện thực hai kiểu.

Điểm thứ hai, nhỏ hơn: với `roles`, dùng `ON CONFLICT DO NOTHING` **không nêu cột** thay vì
`ON CONFLICT (code)`. Lý do: kịch bản ADMIN→ROOT ở Mục 3.1 làm cho `code` không còn xung đột nhưng
`role_id = 3` thì vẫn — chỉ định `(code)` sẽ khiến seeder chết bằng một `Npgsql.PostgresException` khó đọc (`23505: duplicate key value
violates unique constraint "PK_roles"` — đã kiểm bằng cách đổi thử seeder, SEED-03 đỏ đúng lỗi này)
*trước khi* `A5` kịp ném ra thông báo nói thẳng nguyên nhân. Bỏ trống cột thì Postgres bỏ qua xung
đột ở **mọi** ràng buộc unique, và `A5` được chạy để báo đúng chuyện đã xảy ra.

### Các bước

**Bước 1 — ba câu SQL, chạy tuần tự, đúng thứ tự phụ thuộc khóa ngoại:** `roles` → `permissions` →
`role_permissions`.

**Bước 2 — luôn ghi rõ schema trong SQL thô.** `HasDefaultSchema("identity")` chỉ tác động tới LINQ
của EF; `ExecuteSqlRawAsync` đi thẳng xuống Postgres và `search_path` mặc định là `public`. Viết
`identity.roles`, không viết `roles`. Dùng `IdentityDbContext.Schema` để nội suy thay vì gõ chuỗi.

**Bước 3 — đẩy tính idempotent xuống tầng DB, không đọc-rồi-ghi ở tầng app.** Hai instance khởi
động cùng lúc thì "đọc thấy chưa có → ghi" sẽ chèn trùng; `ON CONFLICT` thì không.

Khung hàm:

```csharp
public static class IdentitySeeder
{
    private const string S = IdentityDbContext.Schema;

    public static async Task SeedAsync(IdentityDbContext db, CancellationToken ct = default)
    {
        // 1. Vai trò (Mục 5.1) — 3 dòng, role_id gán tay
        await db.Database.ExecuteSqlRawAsync($"""
            INSERT INTO {S}.roles (role_id, code, display_name, description) VALUES
              (1, 'USER',      'Người dùng',      NULL),
              (2, 'MODERATOR', 'Kiểm duyệt viên', NULL),
              (3, 'ADMIN',     'Quản trị viên',   NULL)
            ON CONFLICT DO NOTHING;
            """, ct);

        // 2. Quyền (Mục 5.2) — 17 dòng, permission_id gán tay 1..17
        // 3. Gán quyền (Mục 5.3) — USER 11 dòng, MODERATOR 13 dòng, ADMIN KHÔNG DÒNG NÀO
        //    (bootstrap-một-lần theo vai trò, xem phần cảnh báo phía trên)

        // 4. A5 — kiểm tra vai trò hệ thống, chạy ngay sau khi seed xong
        await EnsureSystemRolesAsync(db, ct);
    }
}
```

**Bước 4 — ADMIN không có dòng `role_permissions` nào.** Ghi comment ngay tại chỗ đó. Đây là **thiết
kế** (quyết định 3.2: short-circuit ở tầng 2), không phải dữ liệu thiếu — nếu không ghi, sẽ có người
ở GĐ6 "sửa lỗi" bằng cách seed thêm quyền cho ADMIN và làm hỏng cả cơ chế.

### Cạm bẫy đã biết

- **`DO UPDATE` vì thấy `DO NOTHING` "không cập nhật được mô tả".** Đúng là không cập nhật được, và
  đó là chủ ý. Muốn đổi `description` của một quyền thì làm bằng migration, không bằng seeder.
- **Seed `role_permissions` cho ADMIN "cho chắc".** Xem trên.
- **Encoding tiếng Việt trong SQL thô.** File `.cs` phải là UTF-8; `display_name` là
  `'Người dùng'`. Kiểm bằng `SEED-01` đọc lại giá trị chứ đừng chỉ nhìn count.

---

## 6. A5 — Kiểm tra vai trò hệ thống lúc khởi động

**Mục tiêu.** Bắt kịch bản nguy hiểm nhất của toàn GĐ1: ai đó chạy `UPDATE roles SET code='ROOT'`
bằng tay. Khi đó short-circuit `role == "ADMIN"` ở tầng 2 không khớp nữa, mà ADMIN lại **cố ý** không
có dòng `role_permissions` nào để rơi về → **mất sạch quyền quản trị, im lặng, không đường phục hồi**.
Đây là biện pháp #2 thay cho cột `is_system` đã bị loại bỏ (Mục 3.4).

**Kết quả mong đợi.**
- ~5 dòng, đặt **trong** `IdentitySeeder`, chạy ngay sau khi seed xong.
- Thiếu bất kỳ `code` nào trong ba cái → ném `InvalidOperationException` **nêu đúng tên vai trò bị
  thiếu**, không phải một thông báo chung chung.
- `SEED-03` xanh: đổi `roles.code` của ADMIN bằng tay rồi khởi động lại → app **từ chối chạy**, thông
  báo nêu đúng tên vai trò thiếu.

### Các bước

Đúng như Mục 5.5, không thêm gì:

```csharp
private static async Task EnsureSystemRolesAsync(IdentityDbContext db, CancellationToken ct)
{
    var expected = new[] { RoleCodes.Admin, RoleCodes.User, RoleCodes.Moderator };
    var actual   = await db.Roles.Select(r => r.Code).ToListAsync(ct);
    var missing  = expected.Except(actual).ToArray();

    if (missing.Length > 0)
        throw new InvalidOperationException(
            $"Thiếu vai trò hệ thống: {string.Join(", ", missing)}. " +
            "Có thể ai đó đã đổi roles.code bằng tay. App từ chối khởi động.");
}
```

**Vì sao đặt trong seeder** chứ không phải một `IHostedService` riêng: seeder vốn đã chạy mỗi lần
deploy (hook `--migrate`), nên kiểm tra **không thể bị quên gọi**. Một `IHostedService` riêng thì ai đó
ở GĐ3 sẽ vô tình bỏ đăng ký nó. Giá phải trả là một câu `SELECT` trên 3 dòng — seeder chỉ `INSERT`,
không tự đọc bảng `roles`.

### Ranh giới — nói rõ để không ai kỳ vọng nhầm

- **Bắt được:** kịch bản ADMIN→ROOT, ở lần deploy hoặc restart kế tiếp.
- **Không bắt được:** thời điểm ai đó gõ `UPDATE` trong psql. Ứng dụng **đang chạy** vẫn hỏng cho tới
  lần restart. Đây là đánh đổi có ý thức — chặn đúng thời điểm ghi thì cần trigger ở tầng DB, mà
  trigger chỉ đáng làm khi GĐ6 đã có endpoint sửa vai trò thật.

Ghi hai gạch đầu dòng này vào docstring của hàm. Ranh giới không viết ra thì sáu tháng nữa sẽ có
người tưởng nó bảo vệ nhiều hơn thực tế.

---

## 7. A6 — Nối vào hook `--migrate`

**Mục tiêu.** Một lệnh duy nhất ở bước deploy làm trọn ba việc: nâng schema, nạp dữ liệu nền, tự kiểm
tra. Không auto-migrate lúc app start (AGENTS.md Mục 13) — vì hai instance cùng migrate lúc start là
một cách hỏng database rất khó dò.

**Kết quả mong đợi.**
- `MigrateIdentityModuleAsync` chạy **đúng thứ tự**: apply migration → seed → kiểm tra vai trò → thoát 0.
- `dotnet run --project src/SocialApp.Api -- --migrate` trên DB sạch: exit code **0**, in dòng xác nhận.
- Chạy **lần hai**: vẫn exit 0, số dòng trong 3 bảng không đổi.
- Đổi `roles.code` rồi chạy lại: exit code **khác 0**, thông báo nêu tên vai trò thiếu.

### Các bước

**Bước 1 — mở rộng
[IdentityModuleExtensions.cs](../src/Modules/Identity/DependencyInjection/IdentityModuleExtensions.cs).**
Chỗ nối đã được chuẩn bị sẵn từ trước, chỉ thêm một dòng:

```csharp
public static async Task MigrateIdentityModuleAsync(this IServiceProvider services, CancellationToken ct = default)
{
    await using var scope = services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
    await db.Database.MigrateAsync(ct);
    await IdentitySeeder.SeedAsync(db, ct);   // seed + kiểm tra vai trò hệ thống (A4, A5)
}
```

Thứ tự **migrate trước, seed sau** không được đảo: seed vào bảng chưa tồn tại thì chết bằng một lỗi
Postgres thô, không phải bằng thông báo của mình.

**Bước 2 — không phải sửa gì ở `Program.cs`.** Nhánh `--migrate` đã gọi
`MigrateIdentityModuleAsync()` rồi `return`. Chỉ cân nhắc sửa **một chuỗi**: dòng
`Console.WriteLine` hiện chỉ nói "đã áp dụng migration", nên bổ sung ý "và nạp dữ liệu nền" cho khớp
việc thật — người đọc log deploy chỉ có dòng này để biết seeder đã chạy.

**Bước 3 — cũng không phải sửa gì ở compose staging.** Service `migrate` đã gọi sẵn
`dotnet SocialApp.Api.dll --migrate`.

**Bước 4 — nghiệm thu bằng tay, hai lần liên tiếp.** Đây là một trong những thứ mà `B.9` xếp vào
nhóm phải xác nhận riêng, đừng tin là mặc nhiên.

> **Ở máy dev không cần đặt biến nào, nhưng bắt buộc có `deploy/.env`.** `dotnet run` chạy profile
> Development; khi chưa cấu hình chuỗi kết nối, `Program.cs` dựng chuỗi localhost với `POSTGRES_PASSWORD`
> đọc từ `deploy/.env` (cùng cơ chế với A3 Bước 3). Thiếu file hoặc biến để trống thì từ chối chạy.
>
> Muốn thấy nhánh thất bại (exit khác 0, thông báo nêu `ADMIN`) thì làm trên **database tạm**
> (`CREATE DATABASE socialapp_a6_check`, trỏ `ConnectionStrings__Postgres` vào đó, `--migrate`, rồi
> `UPDATE identity.roles SET code='ROOT' WHERE code='ADMIN'` và `--migrate` lần nữa), xong `DROP DATABASE`
> — khỏi phải sửa ngược DB dev. Con số exit code cụ thể tùy nền tảng (Git Bash trên Windows thấy `127`);
> `set -e` ở CD dừng với mọi mã khác 0.

Các lệnh:

```bash
# lần 1 — DB sạch
dotnet run --project src/SocialApp.Api -- --migrate
echo $?                # bash       -> phải là 0
```

```powershell
$LASTEXITCODE          # PowerShell -> phải là 0
```

```bash
# đếm số dòng
docker compose -f deploy/docker-compose.dev.yml exec postgres psql -U socialapp -d socialapp -c \
  "select (select count(*) from identity.roles) roles,
          (select count(*) from identity.permissions) perms,
          (select count(*) from identity.role_permissions) grants;"
#  -> 3 | 17 | 24

# lần 2 — chạy lại, ba con số phải y hệt và exit vẫn 0
dotnet run --project src/SocialApp.Api -- --migrate
```

### Cạm bẫy đã biết

- **Exit code 0 dù seeder ném.** Nếu ai đó bọc `try/catch` quanh lời gọi để "log cho đẹp" thì `A5`
  mất hết tác dụng: deploy tiếp tục chạy trên dữ liệu nền hỏng. Ngoại lệ ở bước migrate **phải** thoát
  khác 0.
- **Chạy `--migrate` mà không có `ConnectionStrings__Postgres` ở môi trường khác Development** → app
  từ chối khởi động kèm thông báo nêu đúng key (`StartupConfigurationTests` khóa hành vi này). Đó là
  đúng, không phải lỗi của khối A.

---

## 8. A7 — Gỡ `Skip` của `Identity_Domain_namespace_must_not_be_empty`

**Mục tiêu.** Đóng cái "lưới giả" mà [PersistenceBoundaryTests.cs](../tests/SocialApp.ArchitectureTests/PersistenceBoundaryTests.cs)
tự cảnh báo về chính nó: rule `Domain_and_Application_must_not_depend_on_EfCore` dùng
`WithoutRequiringPositiveResults`, nên nếu gõ sai namespace thì nó không khớp type nào và **xanh vĩnh
viễn mà không kiểm gì cả**. Test này bắt đúng chuyện đó.

**Kết quả mong đợi.** Thuộc tính `Skip` biến mất; test chạy thật và xanh (namespace
`SocialApp.Modules.Identity.Domain` có ≥ 1 type).

### Các bước

Xóa `Skip` trong khai báo `[Fact(...)]` của `Identity_Domain_namespace_must_not_be_empty` — file đã
ghi sẵn điều kiện gỡ trong docstring. Sửa luôn docstring cho khỏi mâu thuẫn.

```bash
dotnet test tests/SocialApp.ArchitectureTests
```

**Làm trong cùng commit với `A1`.** Đây không phải hình thức: nếu `A1` đặt nhầm namespace (ví dụ
`SocialApp.Identity.Domain` thay vì `SocialApp.Modules.Identity.Domain`), test này là thứ duy nhất
phát hiện ra — và nó chỉ phát hiện được nếu đã hết `Skip` ngay lúc đó.

---

## 9. Kế hoạch commit

Bốn commit, mỗi cái tự đứng được và CI xanh:

| # | Nội dung | Thông điệp gợi ý |
|---|---|---|
| 1 | `A1` + `A7` + `Uuid7` ở SharedKernel | `feat(gd1-a): 6 entity Identity trong Domain + gỡ Skip lưới persistence` |
| 2 | `A2` + `A3` + mở rộng `IdentityDbContextSchemaTests` | `feat(gd1-a): EF config + migration InitialIdentity cho schema identity` |
| 3 | `A4` + `A5` + cập nhật Mục 5.4 của `giai-doan-1.md` | `feat(gd1-a): seeder idempotent + kiểm tra vai trò hệ thống lúc khởi động` |
| 4 | `A6` | `feat(gd1-a): hook --migrate chạy migrate -> seed -> kiểm tra` |

Commit 3 **phải** kèm phần cập nhật tài liệu (xem cảnh báo ở Mục 5) — docs sống cùng code, lệch thì
sửa cùng commit.

---

## 10. Checklist nghiệm thu khối A

Tick đủ mới coi là bàn giao được cho khối C5/D/B5.

**Kiểm tự động**

- [ ] `dotnet build SocialApp.sln` xanh
- [ ] `dotnet test tests/SocialApp.ArchitectureTests` xanh, **và** `Identity_Domain_namespace_must_not_be_empty` không còn `Skip`
- [ ] `dotnet test tests/SocialApp.IntegrationTests --filter "FullyQualifiedName~IdentityDbContextSchemaTests"` xanh: 6 bảng đúng schema, FK RESTRICT, `citext`
- [ ] CI xanh cả ba bước (test, cổng hợp đồng API, cổng AuthZ)

**Kiểm bằng tay — ba thứ không test nào bắt được**

- [ ] Đã **đọc lại** file migration sinh ra, soát đủ chín dòng ở bảng Mục 4
- [ ] `--migrate` chạy hai lần liên tiếp: exit 0 cả hai lần, số dòng `3 | 17 | 24` không đổi
- [ ] `ADMIN` **không có** dòng `role_permissions` nào, và có comment giải thích tại chỗ

**Bàn giao cho khối B5** — bốn test này thuộc khối B nhưng chỉ chạy được khi khối A xong. Báo cho
BE-2 ngay khi commit 3 lên nhánh:

| Mã | Tình huống | Kỳ vọng |
|---|---|---|
| `SEED-01` | Chạy seeder hai lần | Dữ liệu không đổi, không nhân bản |
| `SEED-02` | Gỡ 1 quyền của MODERATOR rồi chạy lại seeder | **Không bị cấp lại** |
| `SEED-03` | `UPDATE roles SET code='ROOT' WHERE code='ADMIN'` rồi khởi động lại | **App từ chối khởi động**, nêu tên vai trò thiếu |
| `FK-01` | Xóa vai trò đang có user | Lỗi RESTRICT |

---

## 11. Khối A để lại gì

| Ai nhận | Nhận cái gì |
|---|---|
| **C5** — nối `PermissionHandler` vào repository thật | Bảng `permissions` + `role_permissions` có dữ liệu thật, thay cho stub |
| **Khối D** — 6 endpoint auth | `users`, `refresh_tokens`, `email_verification_tokens` cùng mọi ràng buộc; `RoleCodes` cho claim `role` |
| **B5** | Bốn test dữ liệu nền chạy được |
| **F1** — deploy staging | `--migrate` một lệnh, chạy lại được, tự từ chối khi dữ liệu nền hỏng |
| **GĐ2–GĐ6** | Khuôn mẫu để lặp lại: module → `Domain/` POCO → `Infrastructure/Configurations/` → migration trong module → seeder idempotent → hook `--migrate` |
