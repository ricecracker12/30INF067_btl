# Hướng dẫn thực hiện — Khối B. Hạ tầng test + Khối C. SharedKernel AuthZ (GĐ1)

> Bản triển khai chi tiết của **B.4 Khối B** và **B.5 Khối C** trong [giai-doan-1.md](giai-doan-1.md).
> Tài liệu gốc trả lời *cái gì* và *vì sao*; tài liệu này trả lời *gõ vào file nào, theo thứ tự nào,
> và nhìn vào đâu để biết đã xong thật*.
>
> **Nguồn sự thật vẫn là `giai-doan-1.md`** (Mục 3.1–3.2 quyết định thiết kế, Mục 6 ba tầng kiểm soát,
> Mục 10.1–10.2 mã test) và `AGENTS.md`. Chỗ nào tài liệu này lệch với hai file đó thì sửa ở đây —
> không sửa ngược. Năm quyết định ở Mục 1 đã được chốt và ghi ngược vào `giai-doan-1.md`.

| | |
|---|---|
| **Người làm** | BE-2 (cả hai khối — một người, một lane) |
| **Thời lượng** | Ngày 3 → sáng Ngày 5 (xem mốc ở Mục 0) |
| **Hai khối này chặn** | Toàn bộ khối **D** (cần `C4` + `C1`–`C3`, `C5`); `D7`/`D11` dùng khung matrix của `B2` |
| **Hai khối này cần trước** | Không gì. **Khối A đã xong** (commit `105077c`, `b4639ca`), nên `C5` và `B5` không còn bị chặn — và cũng vì thế **không dùng stub nguồn quyền trong integration test** (Đ3) |

**Vì sao gộp B và C vào một file.** Hai khối cùng một người làm và **đan xen nhau**: `B3` cần `C1` để
compile, `C4` và `C2` là thứ làm `B3` chuyển xanh, `C6` cần khung của `B2`. Tách thành hai file thì
người đọc phải tự ráp thứ tự từ hai nơi — đúng chỗ dễ làm sai nhất.

---

## 0. Danh sách công việc — mục tiêu và kết quả mong đợi

### Khối B — Hạ tầng test

| Mã | Đầu việc | Mục tiêu — việc này tồn tại để làm gì | Kết quả mong đợi — thứ kiểm chứng được |
|---|---|---|---|
| **B1** | Harness Testcontainers dùng chung | Mọi integration test chạy trên Postgres **thật** mà không phải mỗi test dựng một container — để số test tăng tới GĐ8 mà CI vẫn ≤ 10 phút | `PostgresFixture` + `[CollectionDefinition]` trong `tests/SocialApp.IntegrationTests/Harness/`; `IdentityDbContextSchemaTests` và `IdentitySeederTests` chuyển sang dùng nó; nhóm test Postgres dựng **đúng 1 container** (trước đó 6); thời gian chạy trước/sau ghi vào mô tả PR |
| **B2** | Khung AuthZ matrix data-driven | Mỗi giai đoạn sau **chỉ thêm dòng dữ liệu**, không sửa khung — cách duy nhất để GOAL-03 mở rộng tới GĐ8 mà không viết lại | `AuthZMatrix.cs` (bảng) tách khỏi `AuthZMatrixTests.cs` (khung); mỗi dòng hiện thành **một test riêng mang tên mã** trong Test Explorer và log CI; gắn `[Trait("Category", "AuthZ")]`; matrix chạy trên DB **thật** đã migrate + seed ngay từ đầu; thêm tạm một dòng → số test tăng 1 mà không sửa file khung |
| **B3** | TC-A01, TC-A02, RBAC-01, RBAC-02 — **viết cho đỏ trước** | Bốn dòng đầu của ma trận Mục 10.2, và bằng chứng cổng chặn thật sự chặn được | **7 dòng**: 4 mã gốc, TC-A02 tách đôi, hai dòng đối chứng `RBAC-02b`/`RBAC-02c`; **link CI run đỏ** trong PR; cuối cùng xanh cả 7; **bảng đột biến 6 dòng** đã thử, mỗi đột biến làm đúng dòng dự kiến đỏ |
| **B4** | Siết cổng AuthZ trong CI | Đóng cái bẫy "cổng chặn xanh với 0 test" — tệ hơn không có cổng vì tạo cảm giác an toàn giả | Bước `AuthZ matrix (CI GATE)` trong `ci.yml` nhắm vào **project** kèm `TreatNoTestsAsError=true`; đã cố tình gõ sai trait một lần → CI **đỏ** (link run đỏ trong PR) → hoàn tác → CI xanh |
| **B5** | Test dữ liệu nền: SEED-01/02/03, FK-01 | Khóa ba tính chất của khối A mà chỉ Postgres thật mới kiểm được | SEED-01/02/03 **đã có** (chỉ chuyển sang harness ở `B1`); thêm **FK-01**: xóa vai trò đang có user → `PostgresException` với `SqlState = 23503`; cả 4 xanh |

### Khối C — SharedKernel AuthZ

| Mã | Đầu việc | Mục tiêu — việc này tồn tại để làm gì | Kết quả mong đợi — thứ kiểm chứng được |
|---|---|---|---|
| **C1** | `RequirePermissionAttribute` + policy provider | Khai báo quyền ngay trên endpoint bằng một dòng đọc được, thay vì `if` rải rác trong service; không phải đăng ký tay 17 policy | 3 file trong `SharedKernel/Authorization/`; unit test: `perm:post.hide` → policy mang `PermissionRequirement("post.hide")`, tên policy khác → rơi về provider mặc định, **fallback policy vẫn đọc được** qua provider mới |
| **C4** | JwtBearer + fallback policy default deny + thứ tự middleware | Dựng tầng 1 và **mặc định từ chối** — endpoint quên khai quyền thì bị chặn chứ không lọt | `JwtOptions`/`JwtClaims` ở SharedKernel; TC-A01, TC-A02 (2 dòng), `DEFAULT-DENY` xanh; 401/403 là `application/problem+json`; `/health/ready` không token trả **503 chứ không phải 401**; rate limiter phân vùng theo claim `sub` (unit test); thiếu `Jwt:SigningKey` → app từ chối khởi động (test); smoke + cổng hợp đồng API vẫn xanh |
| **C3** | `IPermissionCache` (TTL 60s) | Ma trận quyền đọc từ DB nhưng không phải mỗi request một truy vấn | `IRolePermissionSource` + `IPermissionCache` + `PermissionCache` ở SharedKernel; unit test với đồng hồ giả: giây 59 vẫn trả dữ liệu cũ, giây 61 đọc lại nguồn, nguồn được gọi đúng 2 lần, lỗi không bị cache. Nguồn giả **chỉ** trong unit test |
| **C5** | Nối nguồn quyền vào repository thật | Ma trận quyền đọc thật từ `role_permissions` **ngay từ lần xanh đầu tiên** của matrix — không có giai đoạn nào xanh trên dữ liệu viết tay | `RolePermissionSource` trong `Identity/Infrastructure/Authorization/`, đăng ký ở `AddIdentityModule`; `RolePermissionSourceTests` xanh trên Postgres thật: USER 11 mã, MODERATOR 13 mã có `post.hide`, ADMIN **tập rỗng**, `ROOT` tập rỗng |
| **C2** | `PermissionHandler` với Admin short-circuit | Hiện thực Mục 3.2 **và đặt đúng tầng** — một dòng `if` nằm nhầm ở tầng 3 là Admin đọc được tin nhắn riêng của bất kỳ ai | `SystemRoles.Admin` + `RoleCodes.Admin = SystemRoles.Admin` (unit test khóa giá trị `"ADMIN"`); handler ~10 dòng; 5 unit test; RBAC-01, RBAC-02b, RBAC-02c đỏ → xanh **trên dữ liệu seed thật**; `PermissionDataDrivenTests` xanh (gỡ `post.hide` của MODERATOR → 403 sau 61 giây, không sửa code) |
| **C6** | Khuôn kiểm tra ownership (tầng 3) | Đặt khuôn tầng 3 ngay bây giờ để từ GĐ2 không module nào tự chế cách riêng — IDOR lọt qua đúng những khe đó. Bắt buộc cho DoD Mục 11 | `Result.Forbidden()` + `result.ToActionResult(this)` ở SharedKernel (test: 403, `problem+json`, có `traceId`, không lộ id); `ClaimsPrincipal.GetUserId()`; dòng `OWN-00` trong matrix chạy qua đủ 3 tầng; luật "endpoint chạm tài nguyên có chủ mà không có dòng matrix thì chưa xong" nằm trong `AGENTS.md` |

> Bảng khối C xếp theo **thứ tự làm**, không theo số: `C3 → C5 → C2`.

### Thứ tự thực thi

```
B1 ─→ B2 ─→ C1 ─→ B3 (push ĐỎ) ─→ C4 ─→ C3 ─→ C5 ─→ C2 ─→ B4 ─→ C6
 └─────────────────────────────→ B5 (bất cứ lúc nào sau B1)
```

- `C1` đứng trước `B3` vì dòng RBAC cần `[RequirePermission]` để **compile**. Nhưng `B3` phải chạy
  **trước** `C4` và `C2` — sau hai việc đó thì không còn cơ hội thấy đỏ nữa.
- `C4` được ưu tiên ngay sau `B3`: nó là thứ khối D chờ nhiều nhất (`JwtOptions`, `JwtClaims`).
- `C5` đứng **trước** `C2` (khác tài liệu gốc): khối A đã xong nên không cần stub — handler vừa có là
  chạy ngay trên dữ liệu thật (Đ3).
- `B4` chỉ làm khi test AuthZ đã **xanh** — siết cổng trong lúc test còn đỏ thì CI đỏ vì lý do sai.

**Mốc đo tiến độ:** hết Ngày 3 xong `B1` → `C4` (khối D bắt đầu viết được); hết Ngày 4 xong `C3`,
`C5`, `C2`, `B4`; sáng Ngày 5 xong `C6`, `B5`.

---

## 1. Trước khi gõ dòng đầu tiên

### Bốn điều kiện cần

```bash
# 1. Docker daemon chạy được — Testcontainers cần nó
docker ps

# 2. Solution build sạch
dotnet build SocialApp.sln

# 3. Toàn bộ test hiện có xanh — đây là mốc so sánh. Đỏ sẵn từ trước thì đừng bắt đầu
dotnet test SocialApp.sln

# 4. Ghi lại thời gian chạy nhóm test Postgres TRƯỚC khi đụng vào (B1 cần con số này)
dotnet test tests/SocialApp.IntegrationTests --filter "FullyQualifiedName~IdentityDbContextSchemaTests|FullyQualifiedName~IdentitySeederTests"
```

**Package: không cần thêm gì.**

| Cần | Đã có ở | Dùng cho |
|---|---|---|
| `Microsoft.AspNetCore.Authentication.JwtBearer` 8.0.10 | `SocialApp.Api` | `C4`; kéo theo `Microsoft.IdentityModel.JsonWebTokens` mà test dùng để ký token |
| `Testcontainers.PostgreSql` 4.0.0 | `SocialApp.IntegrationTests` | `B1` — **giữ 4.0.0, không hạ về 3.x** |
| `TimeProvider` | BCL .NET 8 | Đồng hồ giả cho test TTL ở `C3`/`C2` — không cần gói `TimeProvider.Testing` |
| ASP.NET Core Authorization | `SharedKernel` đã có `FrameworkReference Microsoft.AspNetCore.App` | `C1`–`C3` |

### Luật của repo áp thẳng vào hai khối này

1. **Chạy impact analysis trước khi sửa symbol có sẵn** (`CLAUDE.md`). Hai khối này sửa đúng những
   symbol nhiều người gọi nhất: `Program.cs`, `SharedKernelExtensions` (cả `PartitionKey`), `ApiFactory`,
   `Result`/`Error`, `RoleCodes`, `AddIdentityModule`. HIGH/CRITICAL thì dừng lại báo nhóm trước khi sửa.
   ```bash
   node .gitnexus/run.cjs impact "ApiFactory" --direction upstream --repo .
   ```
   Trước mỗi commit: `node .gitnexus/run.cjs detect-changes --scope all --repo .`
2. **SharedKernel không được tham chiếu module nào.** Mọi thứ của khối C nằm ở SharedKernel.
3. **Không commit secret, kể cả khóa test.** Khóa ký JWT trong test sinh ngẫu nhiên mỗi lần chạy.
4. **Không dùng `Skip` để né đỏ.** Repo đã có hai `Skip` chờ gỡ (`A7`, `D11`) — không thêm cái thứ ba.

### Năm quyết định đã chốt

Tài liệu gốc chưa nói đủ để gõ code ở năm chỗ dưới đây. Đã chốt, và đã ghi ngược vào `giai-doan-1.md`.

**Đ1 — Hằng số `ADMIN`: `SystemRoles.Admin` ở SharedKernel.**

- **Quyết định:** `SharedKernel/Authorization/SystemRoles.cs` chứa **đúng một** hằng số `Admin = "ADMIN"`.
  Identity đổi thành `RoleCodes.Admin = SystemRoles.Admin`. Unit test khóa giá trị bằng chuỗi viết tay.
- **Vì sao:** handler ở SharedKernel không tham chiếu được `RoleCodes` của Identity. Cách này giữ chuỗi
  `"ADMIN"` được gõ **một lần** trong repo, nên A5 (kiểm tra khởi động) và short-circuit không thể lệch.
- **Đã loại:** gõ lại `"ADMIN"` trong handler (hai nguồn sự thật — A5 xanh mà Admin mất quyền); chuyển cả
  `RoleCodes` sang SharedKernel (SharedKernel biết danh sách vai trò — kiến thức của Identity, và từ GĐ6
  vai trò là dữ liệu); cấu hình vai trò short-circuit qua options (biến bất biến thành cấu hình — sai một
  biến môi trường là mất quyền quản trị).
- **Ghi ở:** `giai-doan-1.md` Mục 3.2, 6.2, B.5/`C2`.

**Đ2 — `JwtOptions` và `JwtClaims` ở SharedKernel.**

- **Quyết định:** cả hai ở `SharedKernel/Authentication/`. `Jwt:AccessTokenSeconds` là nguồn **duy nhất**
  cho TTL access token (`D3`) và TTL key `revoked:user` (`D8`).
- **Vì sao — lý do quyết định là `JwtClaims`:** từ GĐ2, Content/Messaging… phải đọc claim `sub` để kiểm
  ownership mà không được tham chiếu Identity → tên claim chỉ có một chỗ hợp lệ. `JwtOptions` đi cùng vì
  Api (validate) và Identity (phát token) đều đã tham chiếu SharedKernel.
- **Đã loại:** `JwtOptions` trong Identity — compile được (Api có tham chiếu Identity), nhưng cấu hình
  validate của host phụ thuộc kiểu nội bộ của một module, và tên claim vẫn phải tách đi nơi khác.
- **Làm thêm:** `D8` có test so `TTL revoked:user:<id>` trong Redis với `AccessTokenSeconds`.
- **Ghi ở:** `giai-doan-1.md` Mục 6.2, B.6/`D8`, B.9.

**Đ3 — Không có stub nguồn quyền trong integration test.**

- **Quyết định:** nguồn giả **chỉ** trong unit test (`PermissionHandlerTests`, `PermissionCacheTests`).
  AuthZ matrix dùng nguồn thật ngay từ đầu — `C5` kéo lên trước `C2`.
- **Vì sao:** tài liệu gốc cần stub vì *"C viết được khi A chưa xong"* — **A đã xong**, lý do đó hết. Bỏ
  stub thì: không còn bước "gỡ stub" dễ quên; không có giai đoạn matrix xanh trên dữ liệu viết tay; matrix
  đỏ thì không mơ hồ handler hay SQL, vì logic handler đã có unit test cô lập.
- **Không đổi:** không stub nào trong `src/`.
- **Ghi ở:** `giai-doan-1.md` B.2, B.5/`C3`, `C5`, `C2`.

**Đ4 — "Không tồn tại" và "không được phép thấy" trả cùng một phản hồi (quy ước 3b).**

- **Quyết định:**

  | Loại endpoint | Cả hai trường hợp trả | Ví dụ |
  |---|---|---|
  | Ghi / thao tác cần ownership hoặc membership | **403** | TC-A03 `PATCH /posts/{id}`, TC-A04, TC-A06 |
  | Đọc nội dung có mức hiển thị (BR-02) | **404** | `GET /posts/{id}` bài friends-only với người lạ |

- **Vì sao:** một cái 404 một cái 403 thì **status code đã lộ**, giấu thông điệp là vô ích. Không thể một
  mã cho tất cả: `GET /posts/{id}` phục vụ cả bài public, bài không tồn tại trả 404 là tự nhiên nên bài
  không được xem cũng phải 404; còn Mục 10.2 đã chốt 403 cho TC-A03/A04/A06.
- **Đã loại:** 404 cho mọi thứ (kiểu GitHub) — giấu tốt nhất nhưng phải mở lại TC-A03/A04/A06 vốn khớp
  báo cáo.
- **Việc ở GĐ1:** chỉ `Result.Forbidden()` + ghi quy ước. Mỗi endpoint GĐ2+ chọn mã khi viết, ghi vào file
  hợp đồng yaml, có dòng matrix tương ứng.
- **Ghi ở:** `giai-doan-1.md` Mục 6.3.

**Đ5 — `Result` → HTTP bằng extension method, không phải middleware.**

- **Quyết định:** `result.ToActionResult(this)` ở `SharedKernel/Http/`, ánh xạ **tổng quát** theo
  `Error.Status` — 404/409 của khối D dùng chung, không riêng 403.
- **Đã loại:**
  - Ném `AppException.Forbidden` để `GlobalExceptionHandler` bắt — trái quy ước 2; mỗi lần có người dò IDOR
    là một exception kèm stack trace vào log; chữ ký service không cho thấy nó có thể từ chối.
  - Filter tự bắt `Result<T>` — Swagger sinh schema `Result<T>` thay DTO → cổng hợp đồng đỏ; "phép màu"
    ngầm, đọc controller không thấy.
  - Controller base class — tương đương, nhưng ép kế thừa.
- **Không làm:** thêm `Error.Code` vào ProblemDetails — hợp đồng đã chốt `{type,title,status,errors,traceId}`.
- **Ghi ở:** `giai-doan-1.md` Mục 6.3, B.5/`C6`.

---

## 2. B1 — Harness Testcontainers dùng chung

**Mục tiêu.** Mọi integration test chạy trên Postgres **thật** — InMemory provider không có FK, không
có CHECK, không có `ON CONFLICT`, tức không kiểm được đúng những thứ khối A vừa dựng. Nhưng hiện mỗi
test dựng một container riêng: `IdentitySeederTests` và `IdentityDbContextSchemaTests` khởi tạo
container trong `IAsyncLifetime` của **lớp test**, mà xUnit tạo **một instance lớp cho mỗi test** →
6 test = 6 container. Tới khối D thêm AC-01…RV-04 là vượt ngân sách CI 10 phút.

**Kết quả mong đợi.**
- `tests/SocialApp.IntegrationTests/Harness/PostgresFixture.cs` + `PostgresCollection.cs`.
- Hai lớp test hiện có dùng fixture, **không viết lại nội dung test** — chỉ đổi phần dựng DB.
- Chạy lại lệnh ở Mục 1 bước 4: vẫn 6 test xanh, 1 container, thời gian giảm. Ghi hai con số
  trước/sau vào mô tả PR — đó là bằng chứng "không tăng tuyến tính theo số test".

### Các bước

**Bước 1 — một container, mỗi test một database mới.** Chia container thì rẻ; chia **database** thì
không được: SEED-01 cần DB rỗng, test schema cần DB vừa migrate. Tạo database mới trong container có
sẵn mất vài chục mili giây, dựng container mất vài giây.

```csharp
using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SocialApp.Modules.Identity.DependencyInjection;
using Testcontainers.PostgreSql;

namespace SocialApp.IntegrationTests.Harness;

/// <summary>
/// MỘT container Postgres cho cả collection; mỗi test tự xin một database riêng.
/// Chia container để nhanh, KHÔNG chia database để test không nhìn thấy dữ liệu của nhau.
/// </summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine")
        .Build();

    private readonly ConcurrentDictionary<string, Lazy<Task<string>>> _shared = new();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    /// <summary>Database mới, rỗng hoàn toàn — chưa migrate. Dùng cho test cần kiểm lần đầu tiên.</summary>
    public async Task<string> CreateDatabaseAsync()
    {
        var name = $"t_{Guid.NewGuid():N}";
        await using (var conn = new NpgsqlConnection(_container.GetConnectionString()))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", conn);
            await cmd.ExecuteNonQueryAsync();
        }

        return new NpgsqlConnectionStringBuilder(_container.GetConnectionString()) { Database = name }
            .ConnectionString;
    }

    /// <summary>
    /// Database đã migrate + seed, tạo MỘT lần cho mỗi <paramref name="key"/> rồi dùng lại.
    /// Dành cho test chỉ ĐỌC dữ liệu nền (AuthZ matrix) — test nào sửa dữ liệu thì dùng CreateDatabaseAsync.
    /// </summary>
    public Task<string> SeededIdentityDatabaseAsync(string key) =>
        _shared.GetOrAdd(key, _ => new Lazy<Task<string>>(async () =>
        {
            var cs = await CreateDatabaseAsync();
            await using var services = new ServiceCollection().AddIdentityModule(cs).BuildServiceProvider();
            await services.MigrateIdentityModuleAsync();   // migrate → seed → kiểm tra vai trò (A6)
            return cs;
        })).Value;
}
```

```csharp
namespace SocialApp.IntegrationTests.Harness;

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
    public const string Name = "postgres";
}
```

**Bước 2 — chuyển `IdentitySeederTests`.** Chỉ đổi phần đầu lớp; toàn bộ `[Fact]` giữ nguyên:

```csharp
[Collection(PostgresCollection.Name)]
public sealed class IdentitySeederTests(PostgresFixture postgres) : IAsyncLifetime
{
    private ServiceProvider _services = null!;

    public async Task InitializeAsync()
    {
        _services = new ServiceCollection()
            .AddIdentityModule(await postgres.CreateDatabaseAsync())
            .BuildServiceProvider();

        await using var scope = _services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IdentityDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _services.DisposeAsync();
    // ... các [Fact] giữ nguyên
}
```

Sửa luôn docstring của lớp — đoạn "Khi B1 dựng harness … thì chuyển lớp này sang đó" giờ đã thành sự thật.

**Bước 3 — chuyển `IdentityDbContextSchemaTests`** theo cùng khuôn: `_postgres.GetConnectionString()`
→ `await postgres.CreateDatabaseAsync()`. Cập nhật docstring "khối B GĐ1 xây tiếp … trên khuôn này"
thành trỏ tới `PostgresFixture`.

### Cạm bẫy đã biết

- **Quên `[Collection(PostgresCollection.Name)]`** → xUnit báo *"The following constructor parameters
  did not have matching fixture data: PostgresFixture"*. Lỗi to, dễ sửa.
- **Tưởng test trong một collection chạy song song.** Không: xUnit chạy **tuần tự** các test cùng
  collection. Đó là cái giá của chia container. Với GĐ1 vẫn nhanh hơn nhiều so với mỗi test một
  container. Khi tổng thời gian nhóm này vượt ~3 phút thì tách thành 2–3 collection, mỗi collection một
  container — đừng quay về mỗi test một container.
- **Dùng chung một database giữa các test sửa dữ liệu.** SEED-02 xóa một dòng `role_permissions`;
  chạy chung DB với AuthZ matrix là RBAC-02b đỏ ngẫu nhiên tùy thứ tự. Luật: **sửa dữ liệu →
  `CreateDatabaseAsync()`; chỉ đọc → `SeededIdentityDatabaseAsync(key)`**.
- **`CREATE DATABASE` trong transaction** → Postgres từ chối. Đừng bọc nó vào `BeginTransaction`.
- **Harness cho Redis.** `D8` (RV-01…RV-04) sẽ cần. **Chưa thêm ở B1** — thêm khi `D8` thật sự viết
  test, theo đúng khuôn này.

---

## 3. B2 — Khung AuthZ matrix data-driven

**Mục tiêu.** Mỗi giai đoạn sau **chỉ thêm dòng dữ liệu**, không sửa khung. TC-A03 (GĐ2), TC-A04/A07
(GĐ5), TC-A05/A06 (GĐ6) đều phải thêm được bằng một dòng — kể cả dòng cần dựng dữ liệu trước (bài của
người khác, hội thoại không phải thành viên).

**Kết quả mong đợi.**
- Bốn file dưới `tests/SocialApp.IntegrationTests/AuthZ/` + một file dưới `Harness/`:

  | File | Vai trò | Ai sửa về sau |
  |---|---|---|
  | `AuthZ/AuthZCase.cs` | Hình dạng một dòng | Không ai (trừ khi đổi khung) |
  | `AuthZ/AuthZMatrix.cs` | **Bảng** — danh sách dòng | Mọi giai đoạn, chỉ thêm dòng |
  | `AuthZ/AuthZMatrixTests.cs` | **Khung** — chạy từng dòng | Không ai |
  | `AuthZ/AuthZApiFactory.cs` | Dựng app cho matrix: Postgres thật đã seed, JWT test, probe controller | Không ai |
  | `Harness/TestJwt.cs` | Ký token test bằng khóa sinh ngẫu nhiên | Không ai |

- Mỗi dòng là **một test riêng** mang đúng mã (`Ma_tran_phan_quyen(id: "TC-A01")`), fail thì log nói
  thẳng mã nào, tình huống gì, mong đợi gì, nhận gì.
- Kiểm chứng "thêm dòng không sửa khung": thêm tạm một dòng `TMP-01` → `dotnet test` đếm thêm đúng 1
  test → xóa dòng đó. Không file nào khác thay đổi.

### Các bước

**Bước 1 — hình dạng một dòng.**

```csharp
using System.Net;

namespace SocialApp.IntegrationTests.AuthZ;

/// <summary>Người gọi của một dòng matrix. Vai trò ghi bằng chuỗi hợp đồng, không đọc RoleCodes.</summary>
public enum Caller { Anonymous, ExpiredToken, WrongSignature, User, Moderator, Admin }

/// <summary>
/// Một dòng của AuthZ matrix (Mục 10.2).
/// <paramref name="ArrangePath"/>: với dòng cần dựng dữ liệu trước (TC-A03: bài của user B), hàm này tạo
/// dữ liệu và trả về path cụ thể; <paramref name="Path"/> khi đó chỉ để đọc cho người.
/// </summary>
public sealed record AuthZCase(
    string Id,
    string Scenario,
    string AddedIn,
    Caller Caller,
    HttpMethod Method,
    string Path,
    HttpStatusCode Expected,
    Func<AuthZArrange, Task<string>>? ArrangePath = null)
{
    public override string ToString() => Id;
}

/// <summary>Thứ một dòng được dùng khi dựng dữ liệu: HttpClient của app + chuỗi kết nối DB của matrix.</summary>
public sealed record AuthZArrange(HttpClient Client, string PostgresConnectionString);
```

**Bước 2 — tách bảng khỏi khung.** Kỳ vọng viết tay, **không đọc lại `PermissionCodes`/`RoleCodes`** —
cùng lý do với `IdentitySeederTests`: test dùng chung nguồn với code thì code sai kiểu gì test cũng
sai theo và vẫn xanh.

```csharp
namespace SocialApp.IntegrationTests.AuthZ;

public static class AuthZMatrix
{
    public static readonly AuthZCase[] Cases =
    [
        // B3 điền các dòng GĐ1 vào đây. GĐ2 trở đi CHỈ thêm dòng, không sửa file nào khác.
    ];

    /// <summary>
    /// Chỉ đưa Id vào MemberData, không đưa cả record: xUnit 2 không serialize được record chứa delegate
    /// nên sẽ gộp mọi dòng thành MỘT test trong Test Explorer. Đưa chuỗi thì mỗi dòng là một test riêng.
    /// </summary>
    public static IEnumerable<object[]> Ids => Cases.Select(c => new object[] { c.Id });
}
```

**Bước 3 — khung.**

```csharp
using System.Net;
using System.Net.Http.Headers;
using SocialApp.IntegrationTests.Harness;

namespace SocialApp.IntegrationTests.AuthZ;

[Trait("Category", "AuthZ")]
[Collection(PostgresCollection.Name)]
public sealed class AuthZMatrixTests(PostgresFixture postgres, AuthZApiFactory factory)
    : IClassFixture<AuthZApiFactory>, IAsyncLifetime
{
    private string _db = null!;

    public async Task InitializeAsync()
    {
        _db = await postgres.SeededIdentityDatabaseAsync("authz");
        factory.UseDatabase(_db);   // phải trước CreateClient đầu tiên — host dựng lúc đó
    }

    public Task DisposeAsync() => Task.CompletedTask;

    [Theory]
    [MemberData(nameof(AuthZMatrix.Ids), MemberType = typeof(AuthZMatrix))]
    public async Task Ma_tran_phan_quyen(string id)
    {
        var c = AuthZMatrix.Cases.Single(x => x.Id == id);
        var client = factory.CreateClient();
        var path = c.ArrangePath is null ? c.Path : await c.ArrangePath(new AuthZArrange(client, _db));

        using var request = new HttpRequestMessage(c.Method, path);
        if (TestJwt.ForCaller(c.Caller) is { } token)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await client.SendAsync(request);

        Assert.True(response.StatusCode == c.Expected,
            $"{c.Id} — {c.Scenario}: mong đợi {(int)c.Expected}, nhận {(int)response.StatusCode}.");

        // Lỗi 401/403 cũng phải là RFC 7807 (AGENTS.md Mục 9) — không phải body rỗng.
        if (c.Expected is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public void Ma_tran_khong_rong_va_ma_khong_trung()
    {
        Assert.NotEmpty(AuthZMatrix.Cases);
        var duplicated = AuthZMatrix.Cases.GroupBy(c => c.Id).Where(g => g.Count() > 1).Select(g => g.Key);
        Assert.Empty(duplicated);
    }
}
```

**Bước 4 — `TestJwt`.** Token test phải được ký **đúng cách app sẽ validate ở `C4`**: HS256, khóa là
byte UTF-8 của chuỗi `Jwt:SigningKey`, claim tên ngắn.

```csharp
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using SocialApp.IntegrationTests.AuthZ;

namespace SocialApp.IntegrationTests.Harness;

/// <summary>
/// Ký JWT cho test. Khóa sinh NGẪU NHIÊN mỗi lần chạy: repo không giữ khóa nào, kể cả khóa test
/// (AGENTS.md Mục 14.3). Vai trò và tên claim ghi bằng chuỗi hợp đồng, cố ý không đọc RoleCodes/JwtClaims.
/// </summary>
public static class TestJwt
{
    public const string Issuer = "https://test.socialapp.local";
    public const string Audience = "socialapp-api-test";
    public static readonly string SigningKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));

    public static void Configure(IWebHostBuilder builder)
    {
        builder.UseSetting("Jwt:SigningKey", SigningKey);
        builder.UseSetting("Jwt:Issuer", Issuer);
        builder.UseSetting("Jwt:Audience", Audience);
        builder.UseSetting("Jwt:AccessTokenSeconds", "900");
    }

    public static string? ForCaller(Caller caller) => caller switch
    {
        Caller.Anonymous => null,
        Caller.User => Create("USER"),
        Caller.Moderator => Create("MODERATOR"),
        Caller.Admin => Create("ADMIN"),
        // Hết hạn từ 45 phút trước — vượt xa mọi ClockSkew, không phụ thuộc cấu hình lệch giờ.
        Caller.ExpiredToken => Create("USER", issuedAt: DateTimeOffset.UtcNow.AddHours(-1)),
        Caller.WrongSignature => Create("USER", signingKey: Convert.ToBase64String(RandomNumberGenerator.GetBytes(48))),
        _ => throw new ArgumentOutOfRangeException(nameof(caller)),
    };

    public static string Create(string role, Guid? userId = null, DateTimeOffset? issuedAt = null, string? signingKey = null)
    {
        var iat = issuedAt ?? DateTimeOffset.UtcNow;
        return new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
        {
            Issuer = Issuer,
            Audience = Audience,
            IssuedAt = iat.UtcDateTime,
            NotBefore = iat.UtcDateTime,
            Expires = iat.AddMinutes(15).UtcDateTime,
            Claims = new Dictionary<string, object>
            {
                ["sub"] = (userId ?? Guid.NewGuid()).ToString(),
                ["role"] = role,
                ["jti"] = Guid.NewGuid().ToString(),
            },
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey ?? SigningKey)),
                SecurityAlgorithms.HmacSha256),
        });
    }
}
```

**Bước 5 — `AuthZApiFactory`.** Tách khỏi `ApiFactory` có chủ ý: `ApiFactory` phục vụ smoke + cổng hợp
đồng API và **cố ý không chạm DB**; matrix cần DB thật **ngay từ đầu** (Đ3) và cần probe controller.

```csharp
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SocialApp.IntegrationTests.Harness;

namespace SocialApp.IntegrationTests.AuthZ;

public sealed class AuthZApiFactory : WebApplicationFactory<Program>
{
    private string? _postgres;

    /// <summary>Gọi trước CreateClient đầu tiên. Gọi lại với cùng giá trị là vô hại.</summary>
    public void UseDatabase(string connectionString) => _postgres ??= connectionString;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Environments.Development);
        builder.UseSetting("ConnectionStrings:Postgres",
            _postgres ?? throw new InvalidOperationException("Gọi UseDatabase trước CreateClient."));
        builder.UseSetting("ConnectionStrings:Redis", ApiFactory.UnreachableRedis);
        TestJwt.Configure(builder);

        builder.ConfigureTestServices(services =>
        {
            // Probe controller sống trong assembly TEST: không có trong image, không có trong Swagger,
            // không bị PresentationBoundaryTests quét (nó chỉ quét module + Api).
            services.AddControllers().AddApplicationPart(typeof(AuthZApiFactory).Assembly);
        });
    }
}
```

> `TestJwt.Configure` ghi `Jwt:*` trước khi `C4` có người đọc — vô hại, và nhờ vậy `C4` không phải sửa
> factory này. Không có dòng thay thế `IRolePermissionSource` nào ở đây, và sẽ không bao giờ có (Đ3).

### Cạm bẫy đã biết

- **Đưa cả `AuthZCase` vào `MemberData`.** Chạy vẫn đủ dòng, nhưng Test Explorer và log CI chỉ hiện
  **một** test — dòng nào đỏ phải đọc message mới biết. Đưa `Id` thôi.
- **Probe controller lọt vào `ApiFactory`.** Để nó sống riêng trong `AuthZApiFactory` — `ApiFactory` giữ
  đúng lời hứa "không chạm gì ngoài pipeline thật".
- **Dùng `Assert.Equal(c.Expected, response.StatusCode)`.** Fail thì chỉ in `Expected: Forbidden,
  Actual: OK` — không biết dòng nào. Message tự viết như trên in mã + tình huống.

---

## 4. C1 — `RequirePermissionAttribute` + policy provider

**Mục tiêu.** Khai quyền trên endpoint bằng một dòng `[RequirePermission("post.hide")]`, và không phải
đăng ký tay 17 policy cùng mọi quyền thêm ở GĐ sau.

**Kết quả mong đợi.**
- `src/SocialApp.SharedKernel/Authorization/`: `RequirePermissionAttribute.cs`, `PermissionRequirement.cs`,
  `PermissionPolicyProvider.cs`, `AuthorizationExtensions.cs`.
- Unit test `tests/SocialApp.UnitTests/Authorization/PermissionPolicyProviderTests.cs` xanh, ba khẳng
  định: (1) `perm:post.hide` → policy có đúng một `PermissionRequirement("post.hide")` và yêu cầu đã
  đăng nhập; (2) tên policy không có tiền tố → trả đúng cái provider mặc định trả; (3) đặt
  `AuthorizationOptions.FallbackPolicy` rồi hỏi provider mới → **nhận lại đúng policy đó**.
- Probe `[RequirePermission("post.hide")]` compile được (dùng ở `B3`).

### Các bước

**Bước 1 — attribute + requirement.**

```csharp
using Microsoft.AspNetCore.Authorization;

namespace SocialApp.SharedKernel.Authorization;

/// <summary>
/// Tầng 2 (Mục 6.2): khai quyền trên endpoint. Tên policy sinh động = tiền tố + mã quyền, được
/// <see cref="PermissionPolicyProvider"/> dựng lúc cần. Mã quyền dạng resource.action (Mục 5.2).
/// </summary>
public sealed class RequirePermissionAttribute(string permission)
    : AuthorizeAttribute(PolicyPrefix + permission)
{
    public const string PolicyPrefix = "perm:";
    public string Permission { get; } = permission;
}

public sealed record PermissionRequirement(string Permission) : IAuthorizationRequirement;
```

**Bước 2 — provider, bắt buộc rơi về provider mặc định.**

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Options;

namespace SocialApp.SharedKernel.Authorization;

public sealed class PermissionPolicyProvider(IOptions<AuthorizationOptions> options) : IAuthorizationPolicyProvider
{
    private readonly DefaultAuthorizationPolicyProvider _default = new(options);

    public Task<AuthorizationPolicy?> GetPolicyAsync(string policyName)
    {
        if (!policyName.StartsWith(RequirePermissionAttribute.PolicyPrefix, StringComparison.Ordinal))
            return _default.GetPolicyAsync(policyName);

        var permission = policyName[RequirePermissionAttribute.PolicyPrefix.Length..];
        return Task.FromResult<AuthorizationPolicy?>(new AuthorizationPolicyBuilder()
            .RequireAuthenticatedUser()
            .AddRequirements(new PermissionRequirement(permission))
            .Build());
    }

    // Hai dòng dưới KHÔNG phải thủ tục: provider thay thế provider mặc định trong DI, nên nếu tự trả
    // null ở đây thì FallbackPolicy của C4 biến mất — default deny tắt câm, không lỗi, không log.
    public Task<AuthorizationPolicy> GetDefaultPolicyAsync() => _default.GetDefaultPolicyAsync();
    public Task<AuthorizationPolicy?> GetFallbackPolicyAsync() => _default.GetFallbackPolicyAsync();
}
```

**Bước 3 — điểm ráp DI.** `AuthorizationExtensions.AddSharedKernelAuthorization(this IServiceCollection)`.
Ở `C1` nó mới chỉ có:

```csharp
services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
```

`C4` thêm fallback policy, `C3` thêm cache, `C2` thêm handler vào **cùng hàm này** — Api chỉ gọi một dòng.

### Nên làm kèm — lưới chống gõ sai mã quyền

`[RequirePermission("post.hid")]` compile được, và **Admin vẫn qua** (short-circuit không nhìn mã
quyền). Người test tay bằng tài khoản Admin sẽ thấy "chạy tốt", còn Moderator bị chặn vĩnh viễn. Thêm
một test vào `tests/SocialApp.ArchitectureTests/`: quét mọi `RequirePermissionAttribute` trên
controller/action của 7 module bằng reflection, so với tập giá trị `public const string` trong
`SocialApp.Modules.Identity.Domain.PermissionCodes`; lệch thì đỏ, liệt kê mã sai.

Ở GĐ1 chưa module nào dùng attribute nên test này **chạy trong chân không** — cùng bài học với
`PersistenceBoundaryTests`. Kiểm một lần là nó đỏ được: thêm tạm `[RequirePermission("post.hid")]` vào
một action của module Identity → đỏ đúng mã → gỡ.

### Cạm bẫy đã biết

- **Không rơi về `DefaultAuthorizationPolicyProvider`** → xem comment trong code. Unit test khẳng định
  (3) tồn tại để bắt đúng nó; dòng `DEFAULT-DENY` ở `C4` bắt nó lần nữa ở tầng HTTP.
- **Đặt attribute trong module Identity** "vì quyền là của Identity". Mọi module từ GĐ2 đều cần nó, và
  module không được tham chiếu chéo → nó chỉ có một chỗ hợp lệ: SharedKernel.

---

## 5. B3 — TC-A01, TC-A02, RBAC-01, RBAC-02 — viết cho đỏ trước

**Mục tiêu.** Bốn dòng đầu của ma trận Mục 10.2, và bằng chứng cổng chặn thật sự chặn được. Test
chưa bao giờ đỏ thì không chứng minh được gì.

**Kết quả mong đợi.**
- `AuthZ/AuthZProbeController.cs` trong project test.
- 7 dòng trong `AuthZMatrix.Cases` (bảng dưới).
- **Link CI run đỏ** trong mô tả PR, kèm bảng "đỏ lúc nào, nhận mã gì, vì sao" đối chiếu với log.
- Sau `C2` cả 7 dòng xanh, và **bảng đột biến** (bước 5) đã thử xong, kết quả ghi vào PR.

### Các bước

**Bước 1 — probe controller.** `/me` (D7) thay được cho TC-A01/A02 sau này, nhưng **không có endpoint
thật nào đòi `post.hide` hay `user.lock` trước GĐ6**, nên probe RBAC sống lâu dài.

```csharp
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using SocialApp.SharedKernel.Authorization;

namespace SocialApp.IntegrationTests.AuthZ;

/// <summary>Endpoint thử cho AuthZ matrix. Chỉ tồn tại trong assembly test, không bao giờ vào image.</summary>
[ApiController]
[Route("__test/authz")]
public sealed class AuthZProbeController : ControllerBase
{
    /// <summary>KHÔNG khai gì — fallback policy (C4) phải chặn nó.</summary>
    [HttpGet("no-attribute")]
    public IActionResult NoAttribute() => Ok();

    [Authorize]
    [HttpGet("authenticated")]
    public IActionResult Authenticated() => Ok();

    [RequirePermission("post.hide")]
    [HttpGet("post-hide")]
    public IActionResult PostHide() => Ok();

    /// <summary>MODERATOR KHÔNG có user.lock (Mục 5.3) — dùng cho dòng đối chứng RBAC-02c.</summary>
    [RequirePermission("user.lock")]
    [HttpGet("user-lock")]
    public IActionResult UserLock() => Ok();
}
```

**Bước 2 — bảy dòng.**

```csharp
public static readonly AuthZCase[] Cases =
[
    new("TC-A01", "Gọi endpoint bảo vệ, không kèm JWT", "GĐ1",
        Caller.Anonymous, HttpMethod.Get, "/__test/authz/authenticated", HttpStatusCode.Unauthorized),

    new("TC-A02-expired", "Token đã hết hạn", "GĐ1",
        Caller.ExpiredToken, HttpMethod.Get, "/__test/authz/authenticated", HttpStatusCode.Unauthorized),

    new("TC-A02-signature", "Token sai chữ ký", "GĐ1",
        Caller.WrongSignature, HttpMethod.Get, "/__test/authz/authenticated", HttpStatusCode.Unauthorized),

    new("RBAC-01", "ADMIN gọi endpoint đòi quyền bất kỳ — qua dù không có dòng role_permissions", "GĐ1",
        Caller.Admin, HttpMethod.Get, "/__test/authz/post-hide", HttpStatusCode.OK),

    new("RBAC-02", "USER gọi endpoint đòi post.hide", "GĐ1",
        Caller.User, HttpMethod.Get, "/__test/authz/post-hide", HttpStatusCode.Forbidden),

    new("RBAC-02b", "Đối chứng: MODERATOR gọi endpoint đòi post.hide — vai trò thường CÓ quyền thì phải qua", "GĐ1",
        Caller.Moderator, HttpMethod.Get, "/__test/authz/post-hide", HttpStatusCode.OK),

    new("RBAC-02c", "Đối chứng: MODERATOR gọi endpoint đòi user.lock — handler phải xét MÃ QUYỀN, không chỉ vai trò", "GĐ1",
        Caller.Moderator, HttpMethod.Get, "/__test/authz/user-lock", HttpStatusCode.Forbidden),
];
```

**Vì sao thêm ba dòng ngoài bốn mã của Mục 10.2** (đã ghi vào bảng Mục 10.2):

- **TC-A02 tách đôi.** "Hết hạn" và "sai chữ ký" là hai nhánh validate khác nhau; một dòng chỉ kiểm được
  một nhánh.
- **`RBAC-02b`.** Bốn mã gốc **xanh được với handler hỏng**: handler từ chối mọi vai trò trừ Admin vẫn làm
  RBAC-01 xanh (nhờ short-circuit) và RBAC-02 xanh (vì từ chối tất). Cache hỏng, nguồn trả tập rỗng, join
  sai cột — cả bốn vẫn xanh. `RBAC-02b` bắt đúng loại hỏng này.
- **`RBAC-02c`.** `RBAC-02b` vẫn để lọt một handler hỏng kiểu "vai trò khác USER thì cho qua" — nó không
  hề nhìn mã quyền mà 6 dòng kia đều xanh. `RBAC-02c` bắt nó.

**Bước 3 — chạy và đối chiếu màu đỏ.**

```bash
dotnet test tests/SocialApp.IntegrationTests --filter "Category=AuthZ"
```

Đỏ theo từng giai đoạn — **đây là bảng đối chiếu với log CI**:

| Dòng | Ngay sau `C1` | Sau `C4` (và `C3`, `C5` — chưa có handler) | Sau `C2` |
|---|---|---|---|
| TC-A01 | 🔴 500 — endpoint có metadata `[Authorize]` mà pipeline chưa có middleware authorization, ASP.NET Core ném | 🟢 401 | 🟢 |
| TC-A02-expired / signature | 🔴 500 — cùng lý do | 🟢 401 | 🟢 |
| RBAC-01 | 🔴 500 | 🔴 403 — requirement không có handler nào nhận | 🟢 200 |
| RBAC-02 | 🔴 500 | 🟢 403 — nhưng **xanh vì từ chối tất cả**, chưa chứng minh gì | 🟢 |
| RBAC-02b | 🔴 500 | 🔴 403 | 🟢 200 |
| RBAC-02c | 🔴 500 | 🟢 403 — xanh vì từ chối tất cả | 🟢 |

Nếu một dòng **không** đỏ đúng như bảng thì dừng lại tìm hiểu vì sao trước khi đi tiếp — test đang
không kiểm cái nó tưởng là đang kiểm.

**Bước 4 — push commit đỏ có chủ đích.** `loveart1210` là nhánh cá nhân: CI trên nhánh này tồn tại để
chính người làm thấy đỏ/xanh, và nhánh không được merge khi đỏ nên không ai bị ảnh hưởng. Link CI run
đỏ là bằng chứng mạnh hơn output dán tay.

1. Commit message nói rõ: `test(gd1-b): TC-A01/A02, RBAC-01/02/02b/02c — DO CO CHU DICH, xanh o C4/C2`.
2. Push, rồi **chờ run chạy xong** mới push commit tiếp theo:
   ```bash
   gh run watch        # hoặc theo dõi trên tab Actions
   ```
   `ci.yml` có `cancel-in-progress: true` — push `C4` sớm thì run đỏ bị hủy và mất bằng chứng.
3. Dán link run đỏ vào mô tả PR.

**Ngoại lệ:** nếu PR `loveart1210 → develop` đang được review và nhóm dựa vào trạng thái xanh của nhánh,
giữ commit đỏ ở local, chép output vào PR thay cho link.

**Bước 5 — bảng đột biến (làm sau `C2`, khi cả 7 dòng đã xanh).** Mỗi dòng thử bằng tay một lần: sửa
tạm → chạy matrix → thấy **đúng** dòng dự kiến đỏ → hoàn tác. Ghi kết quả vào PR.

| Đột biến | Dòng phải đỏ |
|---|---|
| Comment dòng short-circuit Admin trong `PermissionHandler` | RBAC-01 |
| `RolePermissionSource` trả tập rỗng (hoặc join sai cột) | RBAC-02b |
| Handler bỏ qua mã quyền: `if (role != "USER") ctx.Succeed(req);` | RBAC-02c |
| Xóa `FallbackPolicy`, hoặc provider trả `null` ở `GetFallbackPolicyAsync` | DEFAULT-DENY |
| `ValidateLifetime = false` | TC-A02-expired |
| Bỏ kiểm chữ ký: `SignatureValidator = (token, _) => new JsonWebToken(token)` | TC-A02-signature |

### Cạm bẫy đã biết

- **Push commit tiếp theo trước khi run đỏ chạy xong** → run bị hủy, mất link bằng chứng.
- **Né đỏ bằng `Skip`.** Không — xem luật 4 ở Mục 1.
- **Sửa kỳ vọng cho khớp kết quả.** Nhận 403 mà dòng ghi 401 thì câu hỏi là "vì sao ra 403", không
  phải "sửa thành 403". Mã kỳ vọng lấy từ Mục 10.2 và hợp đồng, không lấy từ output.

---

## 6. C4 — JwtBearer + fallback policy default deny + thứ tự middleware

**Mục tiêu.** Dựng tầng 1 và **mặc định từ chối** — endpoint quên khai quyền thì bị chặn chứ không lọt.

**Kết quả mong đợi.**
- `SharedKernel/Authentication/JwtOptions.cs` + `JwtClaims.cs` (Đ2).
- `Program.cs`: đọc + validate `JwtOptions` **sau** hai dòng `RequireConnectionString`;
  `AddAuthentication().AddJwtBearer(...)`; Swagger chuyển lên trước; `UseAuthentication()` +
  `UseAuthorization()` ở đúng chỗ đánh dấu, **trước** `UseSharedKernelRateLimiter()`.
- `SharedKernelExtensions`: `UseStatusCodePages()` trong `UseSharedKernel`; `PartitionKey` thành
  `RateLimitPartitionKey` public, đọc **thẳng** claim `sub`.
- `AddSharedKernelAuthorization` thêm fallback policy.
- Matrix: TC-A01, TC-A02-expired, TC-A02-signature xanh; dòng `DEFAULT-DENY` thêm vào và xanh.
- Test mới, tất cả xanh:
  - `StartupConfigurationTests`: thiếu `Jwt:SigningKey` / khóa ngắn hơn 32 byte → từ chối khởi động.
  - `RateLimitPartitionKeyTests` (unit): có `sub` → `user:<sub>`; hai user → hai key; ẩn danh → `ip:…`.
  - `SmokeEndpointsTests.Health_ready_khong_can_token`: `/health/ready` trả **503**, không phải 401.
  - `JwtAuthenticationTests.Principal_giu_ten_claim_ngan`: `whoami` trả `name == sub` và có claim `role`.
- **Toàn bộ test cũ vẫn xanh** — smoke test và cổng hợp đồng API là hai thứ `C4` dễ làm đỏ nhất.

### Các bước

**Bước 1 — `JwtOptions` và `JwtClaims`.**

```csharp
namespace SocialApp.SharedKernel.Authentication;

/// <summary>
/// Cấu hình JWT — MỘT nguồn cho ba nơi: validate (Api, C4), phát token (Identity, D3), TTL của key
/// revoked:user (D8). AccessTokenSeconds đổi ở đây là cả ba đổi theo (Mục 7.5 "Vì sao TTL đúng 900 giây").
/// Chỉ SigningKey là bí mật: nằm ở biến môi trường / deploy/.env, không bao giờ trong appsettings.
/// </summary>
public sealed class JwtOptions
{
    public const string Section = "Jwt";
    public const int MinSigningKeyBytes = 32;   // HS256 cần khóa ≥ 256 bit

    public string SigningKey { get; init; } = "";
    public string Issuer { get; init; } = "";
    public string Audience { get; init; } = "";
    public int AccessTokenSeconds { get; init; } = 900;
}

/// <summary>
/// Tên claim theo Mục 6.1 / 6.7.3. Token mang tên NGẮN, không phải URI của ClaimTypes. Đặt ở SharedKernel
/// vì từ GĐ2 mọi module đọc "sub" để kiểm ownership mà không được tham chiếu Identity (Đ2).
/// </summary>
public static class JwtClaims
{
    public const string Sub = "sub";
    public const string Role = "role";
    public const string Iat = "iat";
    public const string Jti = "jti";
}
```

`Issuer`, `Audience`, `AccessTokenSeconds` không bí mật → khai trong `appsettings.json` cạnh
`ConnectionStrings`, biến môi trường `Jwt__*` ghi đè như `deploy/.env.example` đang làm.

**Bước 2 — đọc và validate, đặt SAU kiểm tra chuỗi kết nối.**

```csharp
var postgres = RequireConnectionString("Postgres", ...);   // có sẵn
var redis = RequireConnectionString("Redis", ...);         // có sẵn
var jwt = RequireJwtOptions();                             // MỚI — phải đứng sau hai dòng trên
```

Thứ tự **không phải tùy ý**: `StartupConfigurationTests.Missing_connection_string_must_fail_fast_outside_development`
dựng app không khai JWT và khẳng định thông báo nêu `ConnectionStrings:Postgres`. Kiểm JWT trước thì test
đó nhận thông báo về JWT và đỏ, dù hành vi hoàn toàn đúng.

`RequireJwtOptions` theo đúng tinh thần `RequireConnectionString`:
- Bind section `Jwt`. Development mà `SigningKey` trống → đọc `Jwt__SigningKey` từ `deploy/.env` (mở rộng
  `DevEnvFile` theo cơ chế đang đọc `POSTGRES_PASSWORD`, thêm case vào `DevEnvFileTests`). **Không có khóa
  mặc định** ở bất kỳ môi trường nào.
- `Encoding.UTF8.GetByteCount(SigningKey) < JwtOptions.MinSigningKeyBytes`, hoặc `Issuer`/`Audience` trống
  → ném `InvalidOperationException` nêu `Jwt:SigningKey`, `Jwt__SigningKey`, `deploy/.env` và tên môi trường.

Test mới trong `StartupConfigurationTests`: khai chuỗi kết nối hợp lệ (dùng hằng số của `ApiFactory`),
**bỏ trống khóa** → khẳng định thông báo; lặp lại với khóa 16 ký tự.

**Bước 3 — `AddJwtBearer`.**

```csharp
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        // Giữ nguyên tên claim ngắn "sub"/"role". Mặc định handler đổi "role" thành URI của
        // ClaimTypes.Role → FindFirstValue("role") trả null → PermissionHandler từ chối cả Admin.
        // KHÔNG dùng cách xóa DefaultInboundClaimTypeMap: đó là static toàn cục, ảnh hưởng mọi handler.
        o.MapInboundClaims = false;

        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidIssuer = jwt.Issuer,
            ValidAudience = jwt.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SigningKey)),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],   // chặn đổi thuật toán trong header
            ClockSkew = TimeSpan.FromSeconds(30),                // mặc định 5 phút = access sống 20 phút
            NameClaimType = JwtClaims.Sub,
            RoleClaimType = JwtClaims.Role,
        };

        // D8 gắn OnTokenValidated (ITokenRevocationStore) vào đúng chỗ này.
    });

builder.Services.AddSharedKernelAuthorization();
```

**Bước 4 — fallback policy trong `AddSharedKernelAuthorization`.**

```csharp
services.AddAuthorization(o => o.FallbackPolicy = new AuthorizationPolicyBuilder()
    .RequireAuthenticatedUser()
    .Build());
```

**Bước 5 — pipeline: Swagger lên trước, auth trước rate limiter.**

```csharp
app.UseSerilogRequestLogging();
app.UseSharedKernel();

// Swagger PHẢI đứng trước UseAuthorization: fallback policy áp cho mọi request mà middleware
// authorization nhìn thấy, kể cả request do middleware đứng sau phục vụ (Swagger không phải endpoint).
if (app.Environment.IsDevelopment() || app.Environment.IsStaging())
{
    app.UseSwagger();
    app.UseSwaggerUI(/* giữ nguyên */);
}

app.UseAuthentication();
app.UseAuthorization();
app.UseSharedKernelRateLimiter();   // SAU UseAuthentication — xem docstring của hàm
```

Xóa comment "GĐ1 chèn … vào ĐÂY" sau khi chèn xong.

**Bước 6 — rate limiter đọc thẳng `sub`, để bẫy biến mất chứ không chỉ có test canh.** Hiện
`PartitionKey` đọc `ctx.User.Identity.Name` — chỉ có giá trị khi JwtBearer đặt `NameClaimType = "sub"`.
Quên dòng đó thì `Name` là `null` với **mọi** user → cả hệ thống chung một vùng `"user:"` → chung 100
req/phút. Hỏng câm. Sửa (chạy impact analysis cho `PartitionKey` trước):

```csharp
/// <summary>
/// Khóa phân vùng rate limit: đã đăng nhập → theo claim sub; chưa → theo IP.
/// Đọc THẲNG claim sub, không qua Identity.Name: Name phụ thuộc NameClaimType của JwtBearer, quên cấu
/// hình là mọi user chung một hạn mức mà không có lỗi nào.
/// </summary>
public static string RateLimitPartitionKey(HttpContext ctx) =>
    ctx.User.Identity?.IsAuthenticated == true && ctx.User.FindFirstValue(JwtClaims.Sub) is { } sub
        ? $"user:{sub}"
        : $"ip:{ctx.Connection.RemoteIpAddress?.ToString() ?? "anon"}";
```

Cập nhật docstring của `UseSharedKernelRateLimiter` cho khớp. Unit test với `DefaultHttpContext`:

```csharp
var ctx = new DefaultHttpContext
{
    User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "u1")], authenticationType: "Bearer")),
};
Assert.Equal("user:u1", SharedKernelExtensions.RateLimitPartitionKey(ctx));
```

Vẫn giữ `NameClaimType = "sub"` ở bước 3 cho nhất quán — `User.Identity.Name` là thứ dev sẽ tự nhiên dùng.

**Bước 7 — mở đường cho những gì phải công khai.**

| Chỗ | Sửa |
|---|---|
| `MapHealthChecks("/health/live", ...)` và `/health/ready` | `.AllowAnonymous()` — healthcheck của Docker/Caddy không có token |
| `PingController` | `[AllowAnonymous]` ở mức class |
| 6 endpoint auth của khối D | `register`, `verify-email`, `login`, `refresh` khai `[AllowAnonymous]`; `logout`, `/me` thì không |

Thêm vào `SmokeEndpointsTests`:

```csharp
/// <summary>
/// /health/ready phải công khai. ApiFactory trỏ Postgres/Redis vào cổng không có gì nên kỳ vọng 503 —
/// nhận 401 nghĩa là fallback policy đang chặn healthcheck, và container staging sẽ bị báo unhealthy.
/// </summary>
[Fact]
public async Task Health_ready_khong_can_token()
{
    var response = await Client.GetAsync("/health/ready");
    Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
}
```

> Test này có thể mất vài giây vì client Redis chờ timeout kết nối — chấp nhận được.

**Bước 8 — 401/403 phải có body RFC 7807.** JwtBearer challenge trả 401 **body rỗng**; authorization
trả 403 body rỗng. Thêm `app.UseStatusCodePages();` vào `UseSharedKernel` ngay sau
`UseExceptionHandler()` — khi đã `AddProblemDetails` (SharedKernel đã có), middleware này sinh
ProblemDetails cho response 4xx/5xx chưa có body, `traceId` đi qua `CustomizeProblemDetails` sẵn có.
Matrix khẳng định `problem+json` cho mọi dòng 401/403 — chỗ này sai thì matrix đỏ ngay.

**Bước 9 — `ApiFactory` khai JWT trong cùng commit.** Thêm `TestJwt.Configure(builder);` vào
`ApiFactory.ConfigureWebHost`. Đổi tên `Development_boots_without_deploy_env_when_connection_strings_are_explicit`
thành `..._when_connection_strings_and_jwt_are_explicit` cho khớp nghĩa mới.

**Bước 10 — dòng matrix và test claim.**

```csharp
// AuthZMatrix.Cases
new("DEFAULT-DENY", "Endpoint KHÔNG khai [Authorize] hay [RequirePermission], không kèm JWT", "GĐ1",
    Caller.Anonymous, HttpMethod.Get, "/__test/authz/no-attribute", HttpStatusCode.Unauthorized),
```

```csharp
// AuthZProbeController
[Authorize]
[HttpGet("whoami")]
public IActionResult WhoAmI() => Ok(new { name = User.Identity?.Name, role = User.FindFirst("role")?.Value });
```

`JwtAuthenticationTests` (**không** mang trait AuthZ — đây là wiring, không phải phân quyền): ký token với
`userId` và vai trò biết trước, gọi `whoami`, khẳng định `name == userId` và `role == "MODERATOR"` —
tên claim `"role"` viết tay.

### Cạm bẫy đã biết

| # | Bẫy | Đã chặn bằng |
|---|---|---|
| 1 | Swagger sau `UseAuthorization()` → `swagger.json` 401 → cổng hợp đồng đỏ | Bước 5; `IdentityContractTests` (tải swagger.json không token) |
| 2 | Quên `AllowAnonymous` ở `/health/ready` → healthcheck staging unhealthy | Bước 7; `Health_ready_khong_can_token` |
| 3 | `Identity.Name` null → mọi user chung một hạn mức | Bước 6 loại bỏ phụ thuộc; `RateLimitPartitionKeyTests` |
| 4 | Quên `MapInboundClaims = false` → claim `role` đổi tên → chặn cả Admin | RBAC-01 đỏ; `Principal_giu_ten_claim_ngan` |
| 5 | Kiểm JWT trước chuỗi kết nối → test khởi động cũ đỏ sai lý do | Bước 2 |
| 6 | `ApiFactory` thiếu JWT → smoke + cổng hợp đồng đỏ | Bước 9 — lỗi tự báo to, chỉ cần làm cùng commit |
| 7 | `UseAuthentication()` đặt sau rate limiter | **Không test nào bắt** (B.9 điều 3) — code review |

Hai lưu ý khác:
- **`--migrate` cũng cần `Jwt__SigningKey`** vì `Program.cs` đọc cấu hình trước nhánh `isMigrate`. Service
  `migrate` trong `docker-compose.staging.yml` đã nạp `env_file: ./.env` nên không vỡ. Không tách nhánh
  riêng cho `--migrate` — hai hình dạng app là thêm một chỗ lệch.
- **Khóa ký nằm trong `appsettings*.json`** "cho tiện dev". Không. Khóa ở `deploy/.env` (đã gitignore).

---

## 7. C3 — `IPermissionCache` (TTL 60s)

**Mục tiêu.** Ma trận quyền đọc từ DB nhưng không phải mỗi request một truy vấn.

**Kết quả mong đợi.**
- `SharedKernel/Authorization/IRolePermissionSource.cs`, `IPermissionCache.cs`, `PermissionCache.cs`.
- `tests/SocialApp.UnitTests/Authorization/PermissionCacheTests.cs` xanh: t=0 đọc nguồn; t=59s trả dữ liệu
  cũ dù nguồn đã đổi; t=61s trả dữ liệu mới; nguồn bị gọi **đúng 2 lần**; lỗi từ nguồn **không bị cache**.
- Nguồn giả chỉ nằm trong project UnitTests (Đ3).

### Các bước

```csharp
namespace SocialApp.SharedKernel.Authorization;

/// <summary>
/// Nguồn thật của ma trận quyền. Identity hiện thực ở C5 (join roles → role_permissions → permissions).
/// Nhận role CODE dạng chuỗi; phép dịch code → role_id nằm gọn trong hiện thực (Mục 3.1).
/// </summary>
public interface IRolePermissionSource
{
    Task<IReadOnlySet<string>> GetPermissionsAsync(string roleCode, CancellationToken ct = default);
}

public interface IPermissionCache
{
    ValueTask<IReadOnlySet<string>> GetAsync(string roleCode, CancellationToken ct = default);
}
```

```csharp
using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace SocialApp.SharedKernel.Authorization;

/// <summary>
/// Cache vai trò → tập quyền, TTL 60 giây, không đẩy invalidate (GĐ1 chưa sửa quyền lúc runtime — GĐ6).
/// KHÔNG liên quan tới thu hồi token khi đổi vai trò của user (Mục 7.5 "Đừng nhầm với cache quyền").
/// </summary>
public sealed class PermissionCache(IServiceScopeFactory scopes, TimeProvider time) : IPermissionCache
{
    public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);

    private readonly ConcurrentDictionary<string, Entry> _entries = new(StringComparer.Ordinal);

    public async ValueTask<IReadOnlySet<string>> GetAsync(string roleCode, CancellationToken ct = default)
    {
        var now = time.GetUtcNow();
        if (_entries.TryGetValue(roleCode, out var hit) && hit.ExpiresAt > now)
            return hit.Permissions;

        // Cache là singleton, nguồn thật dùng DbContext (scoped) → tự mở scope. Inject thẳng nguồn
        // vào đây là captive dependency: một DbContext sống suốt đời app, dùng chung giữa các thread.
        await using var scope = scopes.CreateAsyncScope();
        var permissions = await scope.ServiceProvider
            .GetRequiredService<IRolePermissionSource>()
            .GetPermissionsAsync(roleCode, ct);

        _entries[roleCode] = new Entry(permissions, now + Ttl);
        return permissions;
    }

    private sealed record Entry(IReadOnlySet<string> Permissions, DateTimeOffset ExpiresAt);
}
```

Đăng ký trong `AddSharedKernelAuthorization`:

```csharp
services.TryAddSingleton(TimeProvider.System);
services.AddSingleton<IPermissionCache, PermissionCache>();
```

Unit test dùng đồng hồ giả tự viết — `TimeProvider` là lớp trừu tượng có sẵn trong .NET 8 — và nguồn giả
đăng ký scoped vào một `ServiceCollection` nhỏ để có `IServiceScopeFactory` thật:

```csharp
private sealed class ManualTime : TimeProvider
{
    public DateTimeOffset Now { get; set; } = DateTimeOffset.UnixEpoch;
    public override DateTimeOffset GetUtcNow() => Now;
}
```

### Cạm bẫy đã biết

- **Cache cả lỗi.** Nguồn ném (DB chết) mà cache lưu tập rỗng thì suốt 60 giây mọi Moderator bị 403
  trong khi DB đã sống lại. Code trên không bắt exception → không ghi entry → lỗi thành 500 và lần sau
  thử lại. Đó là fail-closed đúng nghĩa cho tầng 2.
- **Key cache lấy từ input người dùng.** Ở đây key là claim `role` của token **đã verify chữ ký**, nên
  kẻ tấn công không đẻ ra được key tùy ý làm phình bộ nhớ. Đừng tái dùng lớp này với key lấy từ query string.
- **Dùng `IMemoryCache` rồi test TTL bằng `Task.Delay(61_000)`.** Test chậm một phút và vẫn chập chờn.

---

## 8. C5 — Nối nguồn quyền vào repository thật

**Mục tiêu.** Ma trận quyền đọc thật từ bảng `role_permissions` — điều làm cho "thêm vai trò = thêm dữ
liệu, không sửa code" (Mục 6.7.2) thành sự thật — và làm việc đó **trước** handler, để matrix xanh lần
đầu tiên đã là xanh trên dữ liệu thật (Đ3).

**Kết quả mong đợi.**
- `src/Modules/Identity/Infrastructure/Authorization/RolePermissionSource.cs`, đăng ký scoped trong
  `AddIdentityModule`.
- `RolePermissionSourceTests` (Postgres thật, `SeededIdentityDatabaseAsync`) xanh: USER → đúng 11 mã;
  MODERATOR → 13 mã, có `post.hide`, không có `user.lock`; ADMIN → **tập rỗng** (thiết kế 3.2); `ROOT` →
  tập rỗng. Kỳ vọng viết tay theo Mục 5.3.
- Matrix: các dòng RBAC **vẫn đỏ/xanh như cột "Sau C4"** của bảng ở `B3` — chưa có handler, đó là đúng.

### Các bước

**Bước 1 — hiện thực.** Chạy impact analysis cho `AddIdentityModule` trước (Api, test schema, test
seeder, `PostgresFixture` đều gọi nó).

```csharp
using Microsoft.EntityFrameworkCore;
using SocialApp.SharedKernel.Authorization;

namespace SocialApp.Modules.Identity.Infrastructure.Authorization;

/// <summary>
/// Dịch role CODE (chuỗi trong token) → role_id → tập mã quyền. Phép dịch nằm gọn ở đây — token và policy
/// handler từ đầu đến cuối chỉ biết chuỗi (Mục 3.1). ADMIN ra tập rỗng là ĐÚNG: tầng 2 không bao giờ hỏi
/// nguồn này cho ADMIN (Mục 3.2) — đừng "sửa" bằng cách seed quyền cho ADMIN.
/// </summary>
internal sealed class RolePermissionSource(IdentityDbContext db) : IRolePermissionSource
{
    public async Task<IReadOnlySet<string>> GetPermissionsAsync(string roleCode, CancellationToken ct = default)
    {
        var codes = await (
            from r in db.Roles
            where r.Code == roleCode
            join rp in db.RolePermissions on r.RoleId equals rp.RoleId
            join p in db.Permissions on rp.PermissionId equals p.PermissionId
            select p.Code).ToListAsync(ct);

        return codes.ToHashSet(StringComparer.Ordinal);
    }
}
```

```csharp
public static IServiceCollection AddIdentityModule(this IServiceCollection services, string connectionString)
{
    services.AddDbContext<IdentityDbContext>(options => options.UseIdentityNpgsql(connectionString));
    services.AddScoped<IRolePermissionSource, RolePermissionSource>();
    return services;
}
```

### Cạm bẫy đã biết

- **Inject `IRolePermissionSource` thẳng vào `PermissionCache`.** Ở Development, DI bật `ValidateScopes`
  nên app ném ngay lúc dựng — tốt, lỗi to. Ở Staging/Production thì không ném, mà một `DbContext` bị giữ
  suốt đời app. `PermissionCache` ở `C3` đã mở scope — đừng "tối giản" nó.
- **Kiểm bằng `SeededIdentityDatabaseAsync` rồi sửa dữ liệu trong test.** DB đó dùng chung với matrix —
  test sửa dữ liệu dùng `CreateDatabaseAsync()` (xem `B1`).

---

## 9. C2 — `PermissionHandler` với Admin short-circuit

**Mục tiêu.** Hiện thực Mục 3.2 **và đặt đúng tầng**. Dòng `if (role == ADMIN)` nằm ở đây là "Admin
được làm mọi *loại* hành động"; nằm ở tầng 3 là "Admin thao tác được trên tài nguyên của bất kỳ ai" —
đúng lỗ IDOR mà GOAL-03 muốn đóng, chỉ khác là nạn nhân đông hơn.

**Kết quả mong đợi.**
- `SharedKernel/Authorization/SystemRoles.cs` (Đ1) và `RoleCodes.Admin = SystemRoles.Admin`.
- `SharedKernel/Authorization/PermissionHandler.cs`, đăng ký trong `AddSharedKernelAuthorization`.
- Unit test xanh: 5 test handler (bảng dưới) + 1 test khóa giá trị `"ADMIN"`.
- Matrix: **cả 7 dòng xanh** trên dữ liệu seed thật — RBAC-01, RBAC-02b chuyển đỏ → xanh.
- `PermissionDataDrivenTests` xanh.
- `IdentitySeederTests` (SEED-03 đọc `RoleCodes`) vẫn xanh sau khi đổi `RoleCodes.Admin`.
- **Bảng đột biến ở `B3` bước 5 đã thử xong** — việc này làm ngay sau khi 7 dòng xanh.

### Các bước

**Bước 1 — `SystemRoles` và đổi `RoleCodes`.** Chạy impact analysis cho `RoleCodes` trước (seeder + A5
đọc nó).

```csharp
namespace SocialApp.SharedKernel.Authorization;

/// <summary>
/// Vai trò mà tầng 2 short-circuit (Mục 3.2). Chỉ ADMIN ở đây: SharedKernel cần biết vai trò nào được đi
/// lối tắt, không cần biết danh sách vai trò — danh sách thuộc Identity (RoleCodes.Admin trỏ về đây).
/// </summary>
public static class SystemRoles
{
    public const string Admin = "ADMIN";
}
```

```csharp
// src/Modules/Identity/Domain/RoleCodes.cs — cập nhật docstring: "trỏ về SystemRoles.Admin (Đ1)"
public const string Admin = SystemRoles.Admin;
```

```csharp
// tests/SocialApp.UnitTests/Authorization/SystemRolesTests.cs
[Fact]
public void Ma_ADMIN_la_hop_dong_va_Identity_tro_ve_cung_mot_hang_so()
{
    Assert.Equal("ADMIN", SystemRoles.Admin);            // viết tay — đổi chuỗi là phá mọi token đang lưu hành
    Assert.Equal(SystemRoles.Admin, RoleCodes.Admin);
}
```

**Bước 2 — handler.**

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using SocialApp.SharedKernel.Authentication;

namespace SocialApp.SharedKernel.Authorization;

public sealed class PermissionHandler(IPermissionCache cache) : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext ctx, PermissionRequirement req)
    {
        var role = ctx.User.FindFirstValue(JwtClaims.Role);   // luôn là CHUỖI, với mọi vai trò (Mục 3.1)
        if (role is null) return;

        // Short-circuit Admin — CHỈ Ở ĐÂY, tầng 2. Tuyệt đối không lặp lại ở kiểm tra ownership (Mục 3.2).
        if (role == SystemRoles.Admin) { ctx.Succeed(req); return; }

        var granted = await cache.GetAsync(role);
        if (granted.Contains(req.Permission)) ctx.Succeed(req);

        // KHÔNG gọi ctx.Fail(): để mặc định deny, tránh chặn nhầm handler khác cùng requirement.
    }
}
```

```csharp
services.AddSingleton<IAuthorizationHandler, PermissionHandler>();
```

**Bước 3 — năm unit test handler.**

| Test | Người gọi | Cache giả | Khẳng định |
|---|---|---|---|
| `Admin_qua_ma_khong_cham_cache` | `role=ADMIN` | **ném nếu bị gọi** | `HasSucceeded` |
| `Moderator_co_quyen_thi_qua` | `role=MODERATOR` | `{post.hide}` | `HasSucceeded` |
| `User_thieu_quyen_khong_qua_va_khong_Fail` | `role=USER` | `{post.create}` | `!HasSucceeded` **và** `!HasFailed` |
| `Thieu_claim_role_khong_qua` | không có claim | ném nếu bị gọi | `!HasSucceeded` |
| `admin_chu_thuong_khong_duoc_short_circuit` | `role=admin` | `{}` | `!HasSucceeded` |

```csharp
var ctx = new AuthorizationHandlerContext(
    [new PermissionRequirement("post.hide")],
    new ClaimsPrincipal(new ClaimsIdentity([new Claim("role", "ADMIN")], "test")),
    resource: null);
await new PermissionHandler(new ThrowingCache()).HandleAsync(ctx);
Assert.True(ctx.HasSucceeded);
```

**Bước 4 — `PermissionDataDrivenTests`: nghiệm thu `C3` trên dữ liệu thật và bằng chứng Mục 6.7.2.**
Dùng `CreateDatabaseAsync()` (test **sửa** dữ liệu), migrate + seed, dựng một `AuthZApiFactory` riêng thay
`TimeProvider` bằng `ManualTime` qua `ConfigureTestServices`:

1. Token MODERATOR gọi `/__test/authz/post-hide` → **200**.
2. `delete from identity.role_permissions where role_id = 2 and permission_id = 6` → gọi lại → **vẫn 200**
   (cache còn hạn).
3. Đẩy đồng hồ giả thêm 61 giây → gọi lại → **403**.

Không dòng code nào đổi giữa bước 1 và 3 — đó chính là "nâng cấp là thay dữ liệu, không thay code".

### Cạm bẫy đã biết

- **Tưởng ArchUnitNET bắt được `RoleCodes.Admin` lọt vào service tầng 3.** Không: `const` được compiler
  chép thẳng giá trị vào IL, nên trong assembly không còn dấu vết tham chiếu tới `SystemRoles` hay
  `RoleCodes`. Lưới duy nhất là **grep khi review**:
  ```bash
  grep -rn "SystemRoles.Admin\|RoleCodes.Admin\|\"ADMIN\"" src --include=*.cs
  ```
  Chỉ được ra: `SystemRoles.cs`, `RoleCodes.cs`, `PermissionHandler.cs`, seeder. Thêm dòng này vào
  checklist review của mọi PR từ GĐ2.
- **"Sửa lỗi" Admin bị 403 bằng cách seed thêm quyền cho ADMIN.** Nếu Admin bị 403 thì short-circuit
  đang hỏng (thường là bẫy 4 của `C4`) — tìm nguyên nhân đó, đừng che bằng dữ liệu.

---

## 10. B4 — Siết cổng AuthZ trong CI

**Mục tiêu.** Đóng cái bẫy vừa suýt dính ở cổng hợp đồng: cổng chặn xanh với 0 test tệ hơn không có
cổng, vì nó tạo cảm giác an toàn giả.

**Kết quả mong đợi.**
- Bước `AuthZ matrix (CI GATE)` trong [.github/workflows/ci.yml](../.github/workflows/ci.yml) nhắm vào
  project `SocialApp.IntegrationTests` kèm `TreatNoTestsAsError=true`; comment "chưa có test nào…" được
  thay bằng comment mô tả trạng thái thật.
- Link một CI run **đỏ** do cố tình gõ sai trait, và link run **xanh** sau khi hoàn tác — cả hai trong PR.

**Làm lúc nào.** Sau `C2` — khi 7 dòng đã xanh. Siết cổng lúc test còn đỏ thì không phân biệt được "đỏ vì
0 test" với "đỏ vì AuthZ hỏng".

### Các bước

**Bước 1 — sửa bước CI.**

```yaml
      # CI GATE (GOAL-03, Muc 10.2). Test AuthZ nam o IntegrationTests (AuthZ/AuthZMatrixTests.cs).
      # NHAM VAO PROJECT kem TreatNoTestsAsError: go sai trait -> 0 test -> buoc nay DO.
      # DUNG chay tren .sln voi co nay: co xet rieng tung assembly, UnitTests/ArchitectureTests
      # khong co test AuthZ se lam buoc nay do du AuthZ hoan toan dung.
      - name: AuthZ matrix (CI GATE)
        run: dotnet test tests/SocialApp.IntegrationTests/SocialApp.IntegrationTests.csproj -c Release --no-build --filter "Category=AuthZ" -- RunConfiguration.TreatNoTestsAsError=true
```

Giữ nguyên bước `Test (unit + integration)` với `Category!=AuthZ&Category!=Contract` — test AuthZ không
chạy hai lần.

**Bước 2 — thử cho đỏ ở local trước** (rẻ hơn một vòng CI):

```bash
dotnet build SocialApp.sln -c Release
dotnet test tests/SocialApp.IntegrationTests/SocialApp.IntegrationTests.csproj -c Release --no-build \
  --filter "Category=AuthZZ" -- RunConfiguration.TreatNoTestsAsError=true
echo $?        # phải KHÁC 0
```

**Bước 3 — thử cho đỏ trên CI một lần.** Commit tạm đổi `[Trait("Category", "AuthZ")]` thành
`"AuthZZ"`, push, **chờ run xong** và đỏ ở đúng bước `AuthZ matrix`, rồi `git revert` commit đó và push.

### Cạm bẫy đã biết

- **Thử bằng cách chỉ đổi hoa/thường** (`"authz"`). So khớp giá trị trong `--filter` có thể không phân
  biệt hoa thường, và khi đó CI vẫn xanh — bạn sẽ kết luận sai là cổng không chặn. Đổi hẳn chữ.
- **Thêm `TreatNoTestsAsError` vào lệnh chạy `.sln`.** Đã giải thích trong comment `ci.yml` hiện có.

---

## 11. C6 — Khuôn kiểm tra ownership (tầng 3)

**Mục tiêu.** Đặt khuôn tầng 3 ngay bây giờ, dù GĐ1 gần như chưa có tài nguyên nào để sở hữu. Không
đặt khuôn thì từ GĐ2 mỗi module tự nghĩ ra một kiểu, và IDOR lọt qua đúng những khe đó. **Bỏ việc này
thì GĐ1 không đạt DoD** (Mục 11), dù 6 endpoint đều chạy.

**Kết quả mong đợi.**
- `Error.Forbidden`, `Result.Forbidden()`, `Result<T>.Forbidden()` ở `SharedKernel/Results/Result.cs`.
- `SharedKernel/Http/ResultHttpExtensions.cs`: `ToActionResult` cho `Result` và `Result<T>`, ánh xạ tổng quát
  theo `Error.Status` (Đ5).
- `SharedKernel/Authentication/ClaimsPrincipalExtensions.cs`: `GetUserId()` đọc `sub`, ném nếu thiếu.
- Probe `owned/{ownerId}` + dòng `OWN-00` trong matrix: token của A gọi tài nguyên của B → 403
  `problem+json`; `[Fact]` kèm theo khẳng định có `traceId`, `detail` **không chứa** id đã gửi, và token của
  chính chủ → 200.
- Luật "endpoint chạm tài nguyên có chủ mà không có dòng matrix thì chưa xong" nằm trong `AGENTS.md`
  Mục 10 và nhắc lại ở Mục 14.2.
- Quy ước 3b (Đ4) đã có ở Mục 6.3 của `giai-doan-1.md` — `C6` không phải sửa tài liệu thiết kế.

### Các bước

**Bước 1 — `Result.Forbidden`.** Chạy impact analysis cho `Result` và `Error` trước.

```csharp
public readonly record struct Error(string Code, string Message, int Status)
{
    /// <summary>
    /// Một thông điệp duy nhất cho mọi 403 tầng 3 — không nêu id. Service trả CÙNG lỗi này cho "không tồn
    /// tại" và "không phải của bạn" ở thao tác cần ownership (Mục 6.3 quy ước 3, 3b).
    /// </summary>
    public static readonly Error Forbidden = new("auth.forbidden", "Bạn không có quyền thực hiện thao tác này.", 403);
}

// trong Result:    public static Result Forbidden() => Failure(Error.Forbidden);
// trong Result<T>: public static Result<T> Forbidden() => Failure(Error.Forbidden);
```

`Error.NotFound` cho endpoint đọc có mức hiển thị (quy ước 3b, dòng 404) **chưa thêm ở GĐ1** — GĐ2 thêm khi
viết endpoint đầu tiên cần nó. `ToActionResult` không phải sửa vì nó ánh xạ theo `Status`.

**Bước 2 — ánh xạ sang HTTP.**

```csharp
using Microsoft.AspNetCore.Mvc;
using SocialApp.SharedKernel.Results;

namespace SocialApp.SharedKernel.Http;

/// <summary>
/// Result → HTTP ở MỘT chỗ. Controller gọi <c>return result.ToActionResult(this);</c>. Không ném exception
/// cho luồng nghiệp vụ bình thường (Mục 6.3 quy ước 2). ControllerBase.Problem đi qua ProblemDetailsFactory
/// nên traceId được gắn như mọi lỗi khác. Không phải middleware: Result không phải exception nên middleware
/// không thấy nó; không phải filter: filter bắt Result&lt;T&gt; làm Swagger sinh sai schema (Đ5).
/// </summary>
public static class ResultHttpExtensions
{
    public static IActionResult ToActionResult(this Result result, ControllerBase controller) =>
        result.IsSuccess ? controller.NoContent() : Problem(controller, result.Error!.Value);

    public static ActionResult<T> ToActionResult<T>(this Result<T> result, ControllerBase controller) =>
        result.IsSuccess ? controller.Ok(result.Value) : Problem(controller, result.Error!.Value);

    private static ObjectResult Problem(ControllerBase controller, Error error) =>
        controller.Problem(statusCode: error.Status, detail: error.Message);
}
```

**Bước 3 — danh tính người gọi lấy từ token, không bao giờ từ input.**

```csharp
public static Guid GetUserId(this ClaimsPrincipal user) =>
    Guid.TryParse(user.FindFirstValue(JwtClaims.Sub), out var id)
        ? id
        : throw new InvalidOperationException("Principal không có claim sub hợp lệ — endpoint này thiếu tầng 1?");
```

**Bước 4 — khuôn service** (ghi vào docstring của `ResultHttpExtensions` để GĐ2 chép):

```csharp
// Tầng Application của module. actorId do controller truyền vào từ User.GetUserId().
public async Task<Result> UpdateAsync(Guid postId, Guid actorId, UpdatePostRequest req, CancellationToken ct)
{
    var post = await _posts.FindAsync(postId, ct);

    // Tầng 3. KHÔNG có nhánh "if role == ADMIN" ở đây (Mục 3.2).
    // Thao tác ghi cần ownership → CÙNG 403 cho "không tồn tại" và "không phải của bạn" (quy ước 3b).
    if (post is null || post.AuthorId != actorId)
        return Result.Forbidden();

    // ...
}
```

**Bước 5 — dòng matrix chạy qua đủ ba tầng.** Probe:

```csharp
[Authorize]
[HttpGet("owned/{ownerId:guid}")]
public IActionResult Owned(Guid ownerId) =>
    (ownerId == User.GetUserId() ? Result.Success() : Result.Forbidden()).ToActionResult(this);
```

```csharp
new("OWN-00", "Khuôn tầng 3: user A đọc tài nguyên (probe) của user B", "GĐ1",
    Caller.User, HttpMethod.Get, "/__test/authz/owned/{id của người khác}", HttpStatusCode.Forbidden,
    ArrangePath: _ => Task.FromResult($"/__test/authz/owned/{Guid.NewGuid()}")),
```

Khi GĐ2 viết TC-A03, dòng mới **cùng hình dạng**: `ArrangePath` tạo bài của user B rồi trả
`/api/v1/posts/{id}`, `Caller.User`, `HttpMethod.Patch`, `Forbidden`. Không sửa khung.

**Bước 6 — luật trong `AGENTS.md`.** Thêm vào Mục 10 (và nhắc lại một dòng ở Mục 14.2):

> **Endpoint chạm tài nguyên có chủ sở hữu mà không có dòng tương ứng trong
> `tests/SocialApp.IntegrationTests/AuthZ/AuthZMatrix.cs` thì coi như CHƯA XONG.** Kiểm ownership ở tầng
> Application, trả `Result.Forbidden()`, danh tính người gọi lấy từ `User.GetUserId()` — không bao giờ từ
> route/body. Không có nhánh Admin ở tầng 3. "Không tồn tại" và "không được phép thấy" trả cùng một
> phản hồi (`docs/giai-doan-1.md` Mục 6.3 quy ước 3b).

### Cạm bẫy đã biết

- **Nhận `actorId` từ body hoặc route** "vì client đã gửi sẵn". Đó chính là IDOR. `GetUserId()` là
  nguồn duy nhất.
- **`post is null` trả 404, `AuthorId != actorId` trả 403.** Status code lộ tài nguyên có tồn tại — trái
  quy ước 3b. Gộp hai điều kiện như khuôn ở bước 4.
- **Ném `AppException.Forbidden` trong service** vì nó đã có sẵn. Xem Đ5.
- **Ownership check trong controller hoặc attribute.** Nó cần truy vấn dữ liệu thật → thuộc tầng
  Application, nơi unit test được mà không dựng pipeline.

---

## 12. B5 — Test dữ liệu nền: SEED-01/02/03, FK-01

**Mục tiêu.** Khóa ba tính chất của khối A mà chỉ Postgres thật mới kiểm được.

**Kết quả mong đợi.**
- SEED-01, SEED-02, SEED-03 (tầng seeder) và "seed lại không ghi đè `display_name`" — **đã có** trong
  `IdentitySeederTests`, đã chuyển sang harness ở `B1`. Không viết lại.
- Phần "app từ chối khởi động" của SEED-03 (exit code khác 0 ở `--migrate`) đã nghiệm thu bằng tay ở
  `A6` — không tự động hóa thêm ở GĐ1.
- **Thêm FK-01**, xanh trên Postgres thật.

### Các bước

Thêm vào `IdentitySeederTests` (cùng khuôn DB mới mỗi test, dùng lại `SeedAsync`/`ExecuteAsync`):

```csharp
/// <summary>
/// FK-01 (Mục 10.1): xóa vai trò đang có người dùng bị DB từ chối. IdentityDbContextSchemaTests đã khóa
/// delete_rule = RESTRICT ở tầng schema; test này khóa HÀNH VI — biện pháp #1 thay cho is_system (Mục 3.4).
/// </summary>
[Fact]
public async Task FK_01_xoa_vai_tro_dang_co_nguoi_dung_bi_tu_choi()
{
    await SeedAsync();
    await ExecuteAsync($"""
        insert into identity.users (user_id, email, password_hash, role_id)
        values ({Guid.NewGuid()}, 'fk01@test.local', 'khong-phai-hash-that', 1)
        """);

    var ex = await Assert.ThrowsAsync<PostgresException>(
        () => ExecuteAsync($"delete from identity.roles where role_id = 1"));

    Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, ex.SqlState);   // 23503

    var (roles, _, _) = await ReadSeedDataAsync();
    Assert.Contains("1|USER|Người dùng", roles);
}
```

### Cạm bẫy đã biết

- **So message thay vì `SqlState`.** Message của Postgres đổi theo locale và version; `23503` thì không.
- **Thiếu cột NOT NULL khi insert user.** Câu trên khai đúng các cột không có default theo Mục 4
  (`status`, `failed_login_count`, `created_at`, `updated_at` có default). Mục 4 đổi thì sửa theo.
- **Kỳ vọng `DbUpdateException`.** Đó là lỗi của `SaveChanges`; SQL thô qua `ExecuteSqlAsync` nhận
  thẳng `PostgresException`. Nếu thực tế EF bọc lại thì đổi sang `ThrowsAnyAsync` + tìm
  `PostgresException` trong `InnerException` — vẫn so `SqlState`.

---

## 13. Kế hoạch commit

| # | Nội dung | CI sau khi push | Thông điệp gợi ý |
|---|---|---|---|
| 1 | `B1` | 🟢 | `test(gd1-b): harness Testcontainers dung chung, 1 container cho nhom test Postgres` |
| 2 | `B2` + `C1` | 🟢 | `feat(gd1-c): RequirePermission + policy provider; test(gd1-b): khung AuthZ matrix` |
| 3 | `B3` | 🔴 **có chủ đích** — chờ run xong mới push tiếp | `test(gd1-b): TC-A01/A02, RBAC-01/02/02b/02c — DO CO CHU DICH, xanh o C4/C2` |
| 4 | `C4` + `ApiFactory` + `StartupConfigurationTests` + `DevEnvFile` + `RateLimitPartitionKey` + test `/health/ready` | 🔴 chỉ RBAC-01, RBAC-02b — đối chiếu cột "Sau C4" | `feat(gd1-c): JwtBearer + default deny + 401/403 RFC 7807` |
| 5 | `C3` + `C5` | 🔴 như commit 4 | `feat(gd1-c): cache quyen TTL 60s + nguon quyen that tu role_permissions` |
| 6 | `C2` + `SystemRoles` + `PermissionDataDrivenTests` | 🟢 | `feat(gd1-c): PermissionHandler Admin short-circuit` |
| 7 | `B4` | 🟢 (sau một run đỏ thử trait rồi revert) | `ci(gd1-b): cong AuthZ nham vao project + TreatNoTestsAsError` |
| 8 | `C6` + `AGENTS.md` | 🟢 | `feat(gd1-c): khuon ownership tang 3 — Result.Forbidden -> 403 RFC 7807` |
| 9 | `B5` | 🟢 | `test(gd1-b): FK-01 xoa vai tro dang co user bi RESTRICT chan` |

Commit 4 push được ngay dù CI còn đỏ: khối D cần `JwtOptions`/`JwtClaims` sớm, và log đỏ của commit 4–5 là
lần đối chiếu thứ hai với bảng ở `B3` — đỏ **đúng hai dòng** RBAC-01, RBAC-02b thì đúng; đỏ thêm dòng nào
là có vấn đề. Ngoại lệ PR đang review: xem `B3` bước 4.

Trước **mỗi** commit: `node .gitnexus/run.cjs detect-changes --scope all --repo .` — kết quả `partial`
hoặc `truncated` thì chạy lại, không coi là sạch.

---

## 14. Checklist nghiệm thu khối B + C

**Kiểm tự động**

- [ ] `dotnet build SocialApp.sln` xanh
- [ ] `dotnet test SocialApp.sln` xanh — gồm smoke test (có `/health/ready` 503), `StartupConfigurationTests`, `DevEnvFileTests`
- [ ] Cổng hợp đồng API (`Category=Contract`) xanh — Swagger không bị fallback policy chặn
- [ ] Cổng AuthZ nhắm vào project, xanh với 9 dòng: `TC-A01`, `TC-A02-expired`, `TC-A02-signature`,
      `RBAC-01`, `RBAC-02`, `RBAC-02b`, `RBAC-02c`, `DEFAULT-DENY`, `OWN-00`
- [ ] Unit: `PermissionHandlerTests` (5), `SystemRolesTests`, `PermissionCacheTests`, `PermissionPolicyProviderTests`, `RateLimitPartitionKeyTests`
- [ ] Integration: `RolePermissionSourceTests`, `PermissionDataDrivenTests`, `JwtAuthenticationTests`, FK-01 xanh trên Postgres thật
- [ ] `ArchitectureTests` xanh (không rò MVC/EF vào tầng trong, không tham chiếu chéo module)

**Bằng chứng "đã thấy đỏ" — nằm trong mô tả PR**

- [ ] Link CI run đỏ của commit `B3`, đối chiếu khớp cột "Ngay sau C1"
- [ ] Log CI của commit `C4`/`C5` đỏ đúng RBAC-01 và RBAC-02b
- [ ] Bảng đột biến 6 dòng (`B3` bước 5): mỗi đột biến làm đúng dòng dự kiến đỏ
- [ ] `B4`: link CI run đỏ do gõ sai trait + link run xanh sau hoàn tác
- [ ] Số liệu thời gian nhóm test Postgres trước/sau `B1`

**Code review — không test tự động nào bắt được**

- [ ] `UseAuthentication()` đứng **trước** `UseSharedKernelRateLimiter()` (B.9 điều 3)
- [ ] Grep `SystemRoles.Admin|RoleCodes.Admin|"ADMIN"` trong `src/` chỉ ra 4 file cho phép
- [ ] Không có khóa ký JWT nào trong repo: `git grep -n "SigningKey" -- ':!*.md'` chỉ ra tên cấu hình, không ra giá trị
- [ ] `D3` và `D8` đọc `JwtOptions.AccessTokenSeconds`, không tự khai 900 (test TTL của `D8` chỉ bắt lệch giá trị)

---

## 15. Khối B + C để lại gì

| Ai nhận | Nhận cái gì |
|---|---|
| **D1–D6** | `JwtOptions`, `JwtClaims`, `[AllowAnonymous]` cho 4 endpoint công khai, 401/403 đã là RFC 7807, `ToActionResult` cho 404/409 |
| **D3** — phát token | Cùng `JwtOptions` + `JwtClaims` với phía validate; `TestJwt` là hình mẫu claim cần có; unit test giải mã token vừa phát, kỳ vọng tên claim viết tay `sub`/`role`/`iat`/`jti` |
| **D7** — `GET /me` | `User.GetUserId()`; thêm dòng matrix `TC-A01-me` (không token → 401) cho endpoint thật |
| **D8** — thu hồi token | Chỗ gắn `OnTokenValidated` trong `AddJwtBearer`; `AccessTokenSeconds` làm TTL key `revoked:user` **kèm test so TTL trong Redis** (Đ2); `ClockSkew` 30 giây đã tính sẵn |
| **D block — test** | `PostgresFixture` cho AC-01…RT-04; `AuthZApiFactory` làm mẫu cho factory có DB thật |
| **F** — cổng đóng | Cổng CI `Category=AuthZ` chặn thật, đã chứng minh đỏ được |
| **GĐ2** | TC-A03 = **một dòng** trong `AuthZMatrix.cs` với `ArrangePath`; khuôn `Result.Forbidden()` + `GetUserId()` + quy ước 3b |
| **GĐ2–GĐ8** | `[RequirePermission]` dùng nguyên xi; thêm quyền = thêm dữ liệu seed, không sửa `PermissionHandler` |
