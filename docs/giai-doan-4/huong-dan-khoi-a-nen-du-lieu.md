# Hướng dẫn thực hiện — Khối A. Nền dữ liệu (GĐ4)

> Bản triển khai chi tiết của **B.3 Khối A** trong [giai-doan-4.md](giai-doan-4.md). Tài liệu gốc trả lời *cái gì* và
> *vì sao*; tài liệu này trả lời *gõ vào file nào, theo thứ tự nào, và nhìn vào đâu để biết đã xong thật*.
>
> **Nguồn sự thật vẫn là** `giai-doan-4.md` (Mục 3 quyết định `Đ-4.1`–`Đ-4.16`, Mục 4 mô hình dữ liệu, Mục 5 dữ liệu
> nền, Mục 10 chiến lược test) và `AGENTS.md`. Chỗ nào tài liệu này lệch với hai file đó thì sửa ở đây — không sửa ngược.
> Muốn đổi một `Đ-4.*` thì đó là **quyết định mới**, có ngày tháng, ghi vào `giai-doan-4.md` trong cùng commit.
>
> Khuôn để chép nằm ở khối A của GĐ2: [huong-dan-khoi-a-nen-du-lieu.md](../giai-doan-2/huong-dan-khoi-a-nen-du-lieu.md).
> GĐ4 **không phát minh khuôn mới** — module SocialGraph là lần thứ tư dựng đúng hình dạng Identity/Profile/Content.
> Cái mới duy nhất của khối này là `A5`: lần đầu một contract chéo module **đổi hiện thực** trên host đang chạy.


|                          |                                                                                                                                                                                    |
| ------------------------ | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Người làm**            | Một người (không chia lane — `giai-doan-4.md` Mục 9)                                                                                                                               |
| **Thời lượng**           | Nằm trong **bước 2** của Mục 9.3 (1 ngày, chung với `B1`–`B2`). Riêng khối A: khoảng 0,6 ngày                                                                                      |
| **Khối này cần trước**   | Cổng mở (Mục 9.2) — hợp đồng `socialgraph-v1.yaml` + phần `/feed` đã commit. Về kỹ thuật khối A không đọc yaml, nhưng luật "hợp đồng trước, code sau" không có ngoại lệ            |
| **Khối này chặn**        | Toàn bộ khối `C` (nguồn feed, truy vấn LATERAL cần `idx_posts_public_recent`), toàn bộ khối `D`, `B1` (harness dựng cảnh quan hệ), `F1` (deploy staging chạy `migrate` bốn module) |
| **Không thuộc khối này** | Controller, DTO, validator, `SocialGraphPermissions`, cache Redis, event, hai hợp đồng, sáu dòng AuthZ matrix. Xem Mục 11                                                          |


---



## 0. Danh sách công việc — mục tiêu và kết quả mong đợi

Năm đầu việc của B.3, cộng một việc chuẩn bị `[0]`. `A1→A2→A3` dựng module SocialGraph, `A4` là migration nhỏ của
Content, `A5` nối hai module bằng contract và **bật BR-02 thật**. Toàn bộ khối nằm trên đường găng `A1 → A2 → A3 → A5`
(B.9).


| Mã      | Đầu việc                                                                                                                   | Mục tiêu — việc này tồn tại để làm gì                                                                                                                                                                                                                | Kết quả mong đợi — thứ kiểm chứng được                                                                                                                                                                                                                                                                                                                    |
| ------- | -------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **[0]** | Thêm gói EF Core vào `SocialApp.Modules.SocialGraph.csproj`                                                                | Để `A2` biên dịch được và module tự làm startup project cho `dotnet ef` (ADR-001) — csproj hiện **chỉ** có `ProjectReference` tới SharedKernel                                                                                                       | Ba `PackageReference` 8.0.10 chép nguyên từ `SocialApp.Modules.Content.csproj`, gói `.Design` có `PrivateAssets=all`; `dotnet build SocialApp.sln` xanh                                                                                                                                                                                                   |
| **A1**  | Entity trong `Modules/SocialGraph/Domain/` + `FriendPair` + luật trạng thái quan hệ + test canh gác namespace              | Có mô hình nghiệp vụ cho quan hệ bạn bè / theo dõi; đặt **một** chỗ duy nhất chuẩn hóa cặp đúng thứ tự `uuid` của Postgres (BR-03, GUID-01); và đưa `SocialGraph.Domain` ra khỏi tình trạng rỗng — nơi mọi rule kiến trúc đang chạy trong chân không | `Friendship`, `Follow`, `FriendshipStatus`, `FriendPair`, `RelationshipState` trong `SocialApp.Modules.SocialGraph.Domain`, sạch EF; unit test `FriendPair.Of` với **cặp id mà so byte cho kết quả ngược** xanh; unit test bốn nhánh trạng thái xanh; `SocialGraph_Domain_namespace_must_not_be_empty` xanh và **đã từng thấy đỏ**                        |
| **A2**  | `SocialGraphDbContext` + hai configuration + options + design-time factory                                                 | Đưa module vào đúng khuôn `Đ-4.1`/`Đ-2.1`: schema `socialgraph`, bảng lịch sử migration trong schema của mình, một chỗ cấu hình Npgsql cho cả DI lẫn design-time; và giao BR-03 cho **DB** giữ (PK cặp + bốn CHECK)                                  | 4 file + thư mục `Configurations/` trong `SocialGraph/Infrastructure/`; `HasDefaultSchema("socialgraph")`; đủ `ck_friendships_order`, `_requester`, `_status`, `_accepted`, `ck_follows_not_self`, `idx_friendships_user_max`; **không** `HasPostgresExtension`; build xanh                                                                               |
| **A3**  | Migration đầu của SocialGraph + `AddSocialGraphModule` + `MigrateSocialGraphModuleAsync` + nối `Program.cs` + harness test | Biến schema `socialgraph` thành artifact có version, chạy được bằng **một lệnh** ở bước deploy; và để mọi DB của bộ integration test có schema đó **trước khi** `A5` bắt Content đọc nó                                                              | `<timestamp>_InitialSocialGraph.cs` + `.Designer.cs` + snapshot đã commit; `--migrate` trên DB sạch exit 0 hai lần, lần hai không đổi gì, `\dn` thấy **bốn** schema; `SocialGraphDbContextSchemaTests` xanh và **có test chứng minh CHECK chặn thật**; bộ integration cũ vẫn xanh                                                                         |
| **A4**  | Migration thứ hai của Content: `idx_posts_public_recent`                                                                   | Cho feed gợi ý (`Đ-4.6`) một index đọc keyset thẳng theo thời gian trên **toàn hệ thống** — `idx_posts_author_created` không phục vụ được truy vấn không có `author_id`                                                                              | Migration `<timestamp>_PublicRecentIndex` chỉ chứa **một** `CreateIndex`; filter sinh từ `LowercaseEnum`, không gõ tay; `migrations add Tmp` sau đó ra rỗng; `ContentDbContextSchemaTests` có khẳng định index tồn tại đúng định nghĩa                                                                                                                    |
| **A5**  | `FriendshipReader` + `IFeedSourceReader` / `FeedSourceReader` (chưa cache) + **đổi DI**                                    | Bật BR-02 thật mà Content **không đổi một dòng logic** (`Đ-4.3`); dựng sẵn contract batch cho feed (`Đ-4.4`) để khối C chỉ việc thêm cache                                                                                                           | Dòng `AddSingleton<IFriendshipReader, AlwaysStrangers>()` **biến mất** khỏi `ContentModuleExtensions.cs`; host resolve `IFriendshipReader` ra `FriendshipReader`, **đúng một** đăng ký; `READ-01..05` xanh **không sửa khẳng định**; `FriendshipReaderTests` + `FeedSourceReaderTests` xanh trên Postgres thật; thử khôi phục dòng cũ → test khởi động đỏ |




### Thứ tự thực thi

```
 [0] ─→ A1 ─→ A2 ─→ A3 ─→ A5
                    │      ▲
                    └→ A4 ─┘      A4 đứng đâu cũng được sau [0]; đặt trước A5 để một lượt test cuối phủ cả hai migration

 [0] = gói EF cho csproj SocialGraph — commit riêng, trước mọi thứ
```

- `[0]` **trước tiên.** Kiểm ngày 2026-09-21: `src/backend/Modules/SocialGraph/SocialApp.Modules.SocialGraph.csproj`
không có gói nào; ba thư mục `Application/`, `Domain/`, `Infrastructure/` chỉ có `.gitkeep`. Chưa có `DependencyInjection/`.
- `A1 → A2 → A3` **là phụ thuộc thật** — không entity thì không có gì để cấu hình, không configuration thì migration sinh
ra thiếu CHECK.
- `A3 → A5` **là phụ thuộc thật, và có một cái bẫy nằm giữa.** Từ lúc `A5` gỡ `AlwaysStrangers`, mọi request đọc bài
`friends` của người khác chạy một câu SQL vào `socialgraph.friendships`. DB của bộ integration test được migrate bằng
**danh sách module viết tay** trong hai chỗ harness (Mục 4 Bước 5) — thiếu dòng SocialGraph ở đó thì
`READ_02_05_ma_tran_BR02("friends", false, 404)` nhận **500** `relation "socialgraph.friendships" does not exist`. Nên
harness đổi ở `A3`, không chờ tới `B1`.
- `A4` **độc lập** với SocialGraph — chỉ chạm Content. Đặt nó trước `A5` để lượt chạy test cuối của khối (sau `A5`) phủ
cả hai migration mới cùng lúc.

**Mỗi mốc mở khóa việc gì** — lý do thứ tự trên không đổi được cho tiện:


| Xong việc này | Việc khác bắt đầu được                                                                                                                          |
| ------------- | ----------------------------------------------------------------------------------------------------------------------------------------------- |
| `A1`          | Unit test luật mức nhìn theo nguồn (Mục 10.4) — chúng chỉ cần `FriendPair` và `RelationshipState`                                               |
| `A3`          | `B1` (helper dựng cảnh quan hệ) · `D0` (nền chung, `AddApplicationPart`, `apiGroups`) · `C5` chạy `seed.sql` trên DB đã có schema `socialgraph` |
| `A4`          | `C2` phần feed gợi ý + `EXPLAIN` của nó trên bộ dữ liệu tải                                                                                     |
| `A5`          | `C1` (thêm cache vào `FeedSourceReader`) · `D1`–`D6` (endpoint quan hệ) · `B2` dòng `READ-06`/`READ-06b`                                        |


**Ba cột mốc đo tiến độ** (một người, không có ai báo trễ hộ — STAFF-01): giữa buổi sáng xong `[0]`+`A1`; trưa xong
`A2`+`A3` (từ lúc này `B1` viết được); đầu giờ chiều xong `A4`+`A5`. Hết buổi chiều mà `A5` chưa xanh là khối A đang ăn
vào nửa ngày của `C5` — ghi ngay vào lịch, không đợi cổng đóng.

**Phần cắt được nếu trễ: không có.** Cả năm việc đều có ít nhất một việc của khối C hoặc D đứng lên, và `A5` chính là
mốc không lùi được số 2 của giai đoạn. Thứ duy nhất lùi được là `FeedSourceReader` trong `A5` — sang `C1` — nhưng khi đó
contract `IFeedSourceReader` vẫn phải vào SharedKernel ở `A5` để `C2` viết được trên chữ ký đã chốt.

### Năm chỗ lệch B.3 — đề xuất, chốt khi thi công

Phát hiện lúc đối chiếu B.3 với code thật ngày 2026-09-21. Mỗi cái đi kèm việc ghi ngược vào `giai-doan-4.md` **trong
commit của đầu việc tương ứng**, mở bằng "Lệch B.3 (nhóm chốt): …".


| #   | B.3 / Mục 10 viết                                                                                          | Làm thế này                                                                                                                                                                                | Vì sao                                                                                                                                                                                                                          |
| --- | ---------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| L1  | `A1`: hàm thuần `Relationship.From(…)` → `RelationshipResponse`                                            | `A1` trả **trạng thái miền** `RelationshipState` (enum `None · Outgoing · Incoming · Friends`); ánh xạ sang DTO `RelationshipResponse` là việc của `D0`/`D1`                               | `RelationshipResponse` là DTO của hợp đồng, nằm ở `Application/`. `Domain/` tham chiếu `Application/` là ngược chiều phụ thuộc bốn tầng                                                                                         |
| L2  | "Kết quả khối A: … `READ-06b` xanh"                                                                        | Khối A chứng minh BR-02 thật bằng `FriendshipReaderTests` (qua DI của module, Postgres thật) + test khởi động. `READ-06`/`READ-06b` vào matrix ở `B2` (đỏ có chủ đích), xanh khi `D3` xong | AuthZ matrix dựng cảnh **qua API thật**, không INSERT thẳng DB (`AuthZMatrix.cs`, luật B.4 của GĐ2). "A và B là bạn" qua API cần `POST /friends/requests` + `/accept` — tức `D2`+`D3`                                           |
| L3  | Harness migrate `socialgraph` là việc của `B1`                                                             | Hai dòng migrate của harness vào `A3`                                                                                                                                                      | Xem "Thứ tự thực thi" — không có chúng thì `A5` làm đỏ `READ_02_05` bằng 500                                                                                                                                                    |
| L4  | Mục 10.5 điểm 5: "Gỡ `Skip` của test namespace rỗng cho SocialGraph và thêm test canh gác" (việc của `B5`) | Không có `Skip` nào để gỡ. Chỉ **thêm** `SocialGraph_Domain_namespace_must_not_be_empty`, đi cùng commit `A1`                                                                              | `PersistenceBoundaryTests.cs` hiện có đúng ba test canh gác (Identity, Profile, Content), không `Skip`. Nếp `A7` của GĐ2: test canh gác đi cùng entity đầu tiên, để sang commit sau là thành nợ                                 |
| L5  | `A2` "chép hình dạng `ContentDbContext`" — ngầm hiểu dùng lại `LowercaseEnum`                              | **Chép** `LowercaseEnum` sang `SocialGraph/Infrastructure/Configurations/` (bản `internal` của riêng module)                                                                               | Bản ở Content là `internal` và nằm trong module khác (`ModuleBoundaryTests` chặn). Đưa lên SharedKernel là kéo EF Core vào SharedKernel — hiện SharedKernel không có gói EF nào, và không nên có. Trùng một file 60 dòng rẻ hơn |


---



## 1. Trước khi gõ dòng đầu tiên



### 1.1 Bốn điều kiện cần — giống hệt GĐ2

```bash
# 1. Postgres dev đang chạy (migration và schema test đều cần DB thật)
docker compose -f deploy/docker-compose.dev.yml up -d
docker compose -f deploy/docker-compose.dev.yml ps        # postgres phải healthy

# 2. EF tools 8.x TRỞ LÊN (tool mới hơn runtime là được — xem GĐ2 khối A Mục 1.1)
dotnet ef --version

# 3. Solution build sạch từ điểm xuất phát
dotnet build SocialApp.sln

# 4. Docker daemon chạy được — IntegrationTests dùng Testcontainers
docker ps
```

`deploy/.env` phải tồn tại kể cả khi chỉ chạy `dotnet ef migrations add`: design-time factory đọc mật khẩu Postgres dev
qua `DevEnvFile` và từ chối chạy nếu thiếu file. Chạy `dotnet ef` **từ gốc repo**.

Thứ phải canh sau mỗi lần sinh migration là dòng `ProductVersion` trong `SocialGraphDbContextModelSnapshot.cs` — phải là
`8.0.10`. Ra `10.x` nghĩa là gói EF của project đã bị nâng, không phải do tool.

**Khối A không chạm Redis, R2, JWT.** Chưa có khóa R2 hay Redis dev vẫn làm trọn khối A.

### 1.2 Chạy impact analysis trước khi sửa symbol có sẵn

Luật `CLAUDE.md`: sửa một symbol đã tồn tại thì chạy `impact` hướng `upstream` trước, và báo rủi ro. Khối A sửa đúng năm
chỗ có sẵn — mọi thứ còn lại là file mới:


| Symbol                                              | Sửa ở                         | Người gọi đã biết (kiểm lại bằng `impact`)                                                         |
| --------------------------------------------------- | ----------------------------- | -------------------------------------------------------------------------------------------------- |
| `AddContentModule`                                  | `A5` — xóa một dòng DI        | `Program.cs`, `PostgresFixture.SeededContentDatabaseAsync`, `ModulesApiFactory`, các schema test   |
| `CreateMigratedDatabaseAsync` (`ModulesApiFactory`) | `A3` — thêm dòng migrate      | Mọi test dùng `ModulesApiFactory`                                                                  |
| `SeededContentDatabaseAsync` (`PostgresFixture`)    | `A3` — thêm dòng DI + migrate | AuthZ matrix, test đọc                                                                             |
| `Host_resolve_duoc_hai_contract_cheo_module_cua_A6` | `A5` — đổi khẳng định         | Không ai (là test)                                                                                 |
| `PostConfiguration.Configure`                       | `A4` — thêm một `HasIndex`    | Chỉ EF (`ApplyConfigurationsFromAssembly`) — `risk` có thể ra `UNKNOWN`; xác nhận bằng text search |


```bash
node .gitnexus/run.cjs impact "AddContentModule" --direction upstream --repo .
```

`risk: UNKNOWN` **không** phải `LOW` — nó nghĩa là đồ thị không trả lời được (gọi qua extension method, qua reflection của
EF). Xác nhận bằng `grep` rồi mới sửa.

### 1.3 Sáu luật áp thẳng vào khối A

Năm luật của GĐ2 giữ nguyên, thêm một:

1. **Domain không chạm EF Core / Npgsql** (ADR-001, `PersistenceBoundaryTests`). Entity là POCO thuần. Comment trong
  `Domain/` cũng không nhắc tên trình điều khiển Postgres — checklist `grep` đúng chữ đó (cạm bẫy 6 của `A4` GĐ2).
2. **Không auto-migrate lúc app start** (`AGENTS.md` Mục 13). Mọi thứ khối A dựng chỉ chạy ở hook `--migrate`.
3. **Không tự bịa schema** (`AGENTS.md` Mục 14). Cột nào không có trong `giai-doan-4.md` Mục 4 thì không thêm. Thiếu cột
  thật thì sửa Mục 4 **trước**, trong cùng commit.
4. **Không khóa ngoại nào** trong schema `socialgraph` — không chỉ không FK chéo schema (Đ-2.2), mà `friendships` và
  `follows` cũng không có FK sang nhau. Checklist Mục 12 kiểm bằng `pg_constraint`.
5. **Không module nào import module khác** (`ModuleBoundaryTests`). SocialGraph gặp Content đúng ở hai interface trong
  `SharedKernel/Contracts/` (`A5`).
6. **Mới:** migration đã lên staging là **bất biến**. `InitialContent` của GĐ2 đã chạy trên staging — `A4` là migration
  **mới**, không sửa migration cũ. Cùng luật cho `InitialSocialGraph` sau khi `F1` deploy (Mục 4 cạm bẫy 3 của gốc).

---



## 2. [0] — Gói EF Core cho module SocialGraph

**Mục tiêu.** Để `A2` biên dịch được, và để module tự làm startup project cho `dotnet ef` thay vì kéo EF vào host.

**Kết quả mong đợi.**

- `SocialApp.Modules.SocialGraph.csproj` có ba `PackageReference` — **chép nguyên khối** từ
`SocialApp.Modules.Content.csproj`, kể cả comment về `PrivateAssets` (bỏ comment và dòng `FluentValidation.AspNetCore`:
validator là việc của `D0`, thêm gói khi có validator đầu tiên).
- `dotnet build SocialApp.sln` xanh.

```xml
  <ItemGroup>
    <PackageReference Include="Microsoft.EntityFrameworkCore" Version="8.0.10" />
    <!-- IDesignTimeDbContextFactory: module tự làm startup project cho `dotnet ef` (ADR-001).
         PrivateAssets=all để EF Design không chảy theo ProjectReference sang SocialApp.Api và bị
         publish vào image production. -->
    <PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="8.0.10">
      <IncludeAssets>runtime; build; native; contentfiles; analyzers; buildtransitive</IncludeAssets>
      <PrivateAssets>all</PrivateAssets>
    </PackageReference>
    <PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="8.0.10" />
  </ItemGroup>
```

**Không thêm gói nào khác.** StackExchange.Redis đã có qua SharedKernel (cache là việc của `C1`).

---



## 3. A1 — Entity trong `Domain/` + `FriendPair` + trạng thái quan hệ

**Mục tiêu.** Có mô hình nghiệp vụ cho hai quan hệ; đặt **một** chỗ duy nhất biến `(a, b)` thành cặp chuẩn hóa đúng thứ
tự Postgres; và làm cho namespace `SocialApp.Modules.SocialGraph.Domain` có type thật để rule kiến trúc có dây.

**Kết quả mong đợi.**

- Dưới `src/backend/Modules/SocialGraph/Domain/`: `Friendship.cs`, `Follow.cs`, `FriendshipStatus.cs`, `FriendPair.cs`,
`RelationshipState.cs`. Xóa `.gitkeep` của thư mục.
- `grep -rn "Microsoft.EntityFrameworkCore\|Npgsql" src/backend/Modules/SocialGraph/Domain/` → **0 kết quả**.
- Unit test (`tests/SocialApp.UnitTests/SocialGraph/`): `FriendPairTests`, `RelationshipStateTests` xanh.
- `SocialGraph_Domain_namespace_must_not_be_empty` trong `PersistenceBoundaryTests.cs` xanh — và đã thấy đỏ một lần.



### Các bước

**Bước 1 —** `FriendshipStatus.cs`**.**

```csharp
public enum FriendshipStatus { Pending, Accepted }
```

Chỉ hai giá trị. **Không** thêm `Declined` hay `Cancelled`: từ chối / hủy là **xóa dòng** (FR-011, Đ-4.14), không phải
chuyển trạng thái. Thêm giá trị thứ ba là mở đường cho dòng "đã từ chối" nằm lại mãi và chặn người kia gửi lại lời mời
(`FRD-07` đòi gửi lại được).

**Bước 2 —** `FriendPair.cs`**, chỗ duy nhất chuẩn hóa cặp.**

```csharp
/// <summary>
/// Cặp chuẩn hóa của BR-03: (Min, Max) với Min < Max theo ĐÚNG thứ tự so sánh uuid của Postgres.
/// Guid.CompareTo so từng trường như chuỗi hex hiển thị — khớp Postgres. ToByteArray() thì KHÔNG khớp
/// (ba nhóm đầu little-endian): so mảng byte là chuẩn hóa sai chiều với khoảng một nửa số cặp.
/// </summary>
public readonly record struct FriendPair          // KHÔNG positional — xem đoạn dưới khối code
{
    private FriendPair(Guid min, Guid max) { Min = min; Max = max; }
    public Guid Min { get; }
    public Guid Max { get; }

    public static FriendPair Of(Guid a, Guid b)
    {
        if (a == b)
            throw new ArgumentException("Không có cặp quan hệ nào của một người với chính mình.", nameof(b));
        return a.CompareTo(b) < 0 ? new(a, b) : new(b, a);
    }

    public bool Contains(Guid userId) => userId == Min || userId == Max;

    public Guid Other(Guid userId) => userId == Min ? Max : userId == Max ? Min : throw new ArgumentException(...);
}
```

`a == b` **ném**, không trả gì: tự gửi lời mời là **400** (US-010 AC-03) và phải được `D2` chặn **trước** khi dựng cặp.
Tới được `FriendPair.Of` với hai id bằng nhau là lỗi lập trình, không phải lỗi người dùng — để nó rơi xuống DB thì
`ck_friendships_order` nổ thành 500.

**Constructor** `private` **(sửa ngày 2026-09-22, lúc rà `A1`).** Bản đầu viết `record struct FriendPair(Guid Min, Guid Max)`
dạng positional — tức có constructor public, và `new FriendPair(a, b)` đi vòng qua `Of`. "Chỗ duy nhất chuẩn hóa" khi đó
chỉ là quy ước, không phải thứ trình biên dịch giữ. Có unit test `Khong_co_constructor_public` canh. `RelationshipState`
kiểm người ngoài cặp bằng so thẳng hai cột của dòng, không dựng `FriendPair`.

**Bước 3 —** `Friendship.cs` **và** `Follow.cs`**.** Cột lấy đúng Mục 4 của gốc, không thêm không bớt:


| Thuộc tính C#                              | Cột            | Kiểu DB       | Ghi chú                           |
| ------------------------------------------ | -------------- | ------------- | --------------------------------- |
| `Guid UserMinId { get; init; }`            | `user_min_id`  | `uuid` PK     | KHÔNG FK (Đ-4.1)                  |
| `Guid UserMaxId { get; init; }`            | `user_max_id`  | `uuid` PK     |                                   |
| `Guid RequesterId { get; init; }`          | `requester_id` | `uuid`        | ∈ cặp — CHECK ở `A2`              |
| `FriendshipStatus Status { get; set; }`    | `status`       | `varchar(10)` | mặc định `Pending`                |
| `DateTimeOffset CreatedAt { get; init; }`  | `created_at`   | `timestamptz` | đồng hồ app gán                   |
| `DateTimeOffset UpdatedAt { get; set; }`   | `updated_at`   | `timestamptz` |                                   |
| `DateTimeOffset? AcceptedAt { get; set; }` | `accepted_at`  | `timestamptz` | đi cùng `Accepted` — CHECK ở `A2` |


`Follow`: `FollowerId`, `FolloweeId`, `CreatedAt`. Không `UpdatedAt` — Mục 4 không có cột đó (theo dõi không sửa, chỉ tạo
và xóa).

Một factory để dòng mới **không thể** sai hình dạng ngay từ C#:

```csharp
public static Friendship Request(Guid requester, Guid recipient, DateTimeOffset now)
{
    var pair = FriendPair.Of(requester, recipient);
    return new Friendship
    {
        UserMinId = pair.Min, UserMaxId = pair.Max, RequesterId = requester,
        Status = FriendshipStatus.Pending, CreatedAt = now, UpdatedAt = now,
    };
}
```

**Không** viết phương thức `Accept()` trên entity. Chấp nhận lời mời là **một câu** `UPDATE` **có điều kiện** (Đ-4.14) chạy
bằng `ExecuteUpdateAsync` ở `D3`, không đi qua ChangeTracker — một `Accept()` trên entity mời người sau viết
`SELECT` → `Accept()` → `SaveChanges`, tức đúng cửa sổ race mà Đ-4.14 dựng ra để đóng.

**Bước 4 —** `RelationshipState.cs` **(lệch L1).** Hàm thuần, cùng nếp `PostVisibility` / `PostContentPolicy`:

```csharp
public enum FriendshipView { None, Outgoing, Incoming, Friends }

public static class RelationshipState
{
    /// <summary>Quan hệ nhìn từ phía <paramref name="actorId"/>. Dòng null = chưa có quan hệ.</summary>
    public static FriendshipView Of(Friendship? friendship, Guid actorId) => friendship switch
    {
        null => FriendshipView.None,
        { Status: FriendshipStatus.Accepted } => FriendshipView.Friends,
        _ when friendship.RequesterId == actorId => FriendshipView.Outgoing,
        _ => FriendshipView.Incoming,
    };
}
```

Bốn giá trị trùng tên chuỗi `"none" | "outgoing" | "incoming" | "friends"` của `RelationshipResponse.friendship`
(Mục 8.1 gốc) — `D1` ánh xạ bằng `LowercaseEnum.Name` hoặc bảng tường minh, không đổi chữ. `following: bool` **không**
nằm ở đây: nó là một sự thật độc lập (Đ-4.5), `D1` ghép hai thứ lại.

Kiểm `friendship` có chứa `actorId` không (`Contains`) — dòng của người khác lọt vào đây là lỗi tầng 3 (Mục 6.2 gốc), ném
luôn thay vì trả `Incoming` một cách lặng lẽ.

**Bước 5 — test canh gác, trong chính commit này (lệch L4).** Chép `Content_Domain_namespace_must_not_be_empty` trong
`tests/SocialApp.ArchitectureTests/PersistenceBoundaryTests.cs`, đổi namespace thành `SocialApp.Modules.SocialGraph.Domain`.
Thử cho đỏ: đổi chuỗi thành `...SocialGraph.Domainn` → chạy `dotnet test tests/SocialApp.ArchitectureTests` → đỏ → khôi
phục. `git status` sạch trước và sau.

**Bước 6 — unit test.**


| Test                          | Khẳng định                                                                                                                  | Vì sao phải có                                                                                                                                                               |
| ----------------------------- | --------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `FriendPair.Of` giao hoán     | `Of(a, b) == Of(b, a)`                                                                                                      | BR-03: một khóa cho mọi chiều                                                                                                                                                |
| **Cặp đối nghịch**            | `a = 00000001-0000-0000-0000-000000000000`, `b = 01000000-0000-0000-0000-000000000000`: `Of(a, b).Min == a`                 | So chuỗi hex (Postgres): `a < b`. So `ToByteArray()`: byte đầu của `a` là `01`, của `b` là `00` → `b < a`. Cặp này là lưới cho GUID-01 — cùng cặp đó được INSERT thật ở `A3` |
| Tự ghép với mình              | `Of(a, a)` ném `ArgumentException`                                                                                          |                                                                                                                                                                              |
| `RelationshipState` bốn nhánh | null → `None`; `Accepted` → `Friends` từ **cả hai** phía; `Pending` → `Outgoing` phía người gửi, `Incoming` phía người nhận | Bốn nhánh của nút trên hồ sơ (Đ-4.16)                                                                                                                                        |
| Người ngoài cặp               | `Of(dòng của B–C, A)` ném                                                                                                   | Mục 6.2 gốc                                                                                                                                                                  |




### Cạm bẫy đã biết

1. **"Tối ưu"** `FriendPair.Of` **bằng so mảng byte.** Xem Bước 6 — lỗi chỉ hiện ở khoảng một nửa số cặp, **ngẫu nhiên theo
  id**, dưới dạng `23514` (vi phạm CHECK). Trên staging trông như "thỉnh thoảng kết bạn bị 500".
2. **Đặt tên namespace hay thư mục trùng tên type phổ biến.** GĐ2 đã trả giá hai lần (`Profile` → `CS0118`, `Directory` →
  `CS0234`). Ở đây: **không** mở thư mục `Domain/Friendship/` (namespace `...Domain.Friendship` che type `Friendship`) và
   không đặt type nào tên `SocialGraph`. Để phẳng năm file trong `Domain/`.
3. **Thêm navigation** `User` **hay** `Profile`**.** Không có kiểu nào để trỏ (Đ-2.2) — đây là ranh giới module, không phải việc
  "tạm chưa làm".
4. `Uuid7.New()` **cho bất cứ khóa nào ở đây.** Cả hai bảng có PK **ghép từ id người dùng**, không có khóa sinh mới.

---



## 4. A2 — `SocialGraphDbContext` + configuration + options + design-time factory

**Mục tiêu.** Đưa module vào đúng khuôn `Đ-2.1`: schema riêng, bảng lịch sử migration trong schema của chính nó, cấu
hình Npgsql ở một chỗ dùng chung cho DI lẫn design-time. Và giao BR-03 cho Postgres giữ: PK cặp + bốn CHECK là thứ
**không đường code nào đi vòng được**, kể cả SQL thô của `D3`.

**Kết quả mong đợi.**

- Dưới `src/backend/Modules/SocialGraph/Infrastructure/`: `SocialGraphDbContext.cs` · `SocialGraphDbContextOptions.cs` ·
`DesignTimeSocialGraphDbContextFactory.cs` · `Configurations/FriendshipConfiguration.cs` ·
`Configurations/FollowConfiguration.cs` · `Configurations/LowercaseEnum.cs` (chép, lệch L5). Xóa `.gitkeep`.
- `SocialGraphDbContext.Schema == "socialgraph"`, `HasDefaultSchema(Schema)`, `MigrationsHistoryTable(..., Schema)`.
- **Không** có `HasPostgresExtension(...)`, **không** có `HasQueryFilter` (không có xóa mềm ở đây).
- `dotnet build SocialApp.sln` xanh. Nghiệm thu trên DB thật là việc của `A3`.



### Các bước

**Bước 1 —** `SocialGraphDbContextOptions.cs`**.** Chép `ContentDbContextOptions`, đổi tên thành `UseSocialGraphNpgsql`. Giữ
nguyên XML doc về "hai đường tạo context mà cấu hình lệch nhau là lỗi câm".

**Bước 2 —** `SocialGraphDbContext.cs`**.** Chép `ContentDbContext`: `Schema = "socialgraph"`, hai `DbSet`
(`Friendships`, `Follows`), bỏ đoạn doc về query filter và về khung Đ-2.12. Giữ nguyên `StampUpdatedAt()` — nó kiểm
`FindProperty(UpdatedAtProperty)` nên bảng `follows` (không có `updated_at`) tự được bỏ qua. `UpdatedAtProperty = nameof(Friendship.UpdatedAt)`.

Ghi vào doc của `StampUpdatedAt` một câu **riêng cho module này**: câu `UPDATE` chấp nhận lời mời (`D3`) đi bằng
`ExecuteUpdateAsync`, **không** qua ChangeTracker, nên phải tự gán `updated_at` và `accepted_at` — đúng như SQL của Đ-4.14.

**Bước 3 —** `Configurations/FriendshipConfiguration.cs`**.**

```csharp
internal sealed class FriendshipConfiguration : IEntityTypeConfiguration<Friendship>
{
    private const string StatusColumn = "status";

    public void Configure(EntityTypeBuilder<Friendship> builder)
    {
        var accepted = LowercaseEnum.EqualsSql(StatusColumn, FriendshipStatus.Accepted);   // "status = 'accepted'"

        builder.ToTable("friendships", t =>
        {
            t.HasCheckConstraint("ck_friendships_order", "user_min_id < user_max_id");          // BR-03: không tự kết bạn
            t.HasCheckConstraint("ck_friendships_requester", "requester_id IN (user_min_id, user_max_id)");
            t.HasCheckConstraint("ck_friendships_status", LowercaseEnum.CheckSql<FriendshipStatus>(StatusColumn));
            t.HasCheckConstraint("ck_friendships_accepted", $"({accepted}) = (accepted_at IS NOT NULL)");
        });

        builder.HasKey(x => new { x.UserMinId, x.UserMaxId });          // PK cặp chuẩn hóa CHÍNH LÀ BR-03

        builder.Property(x => x.UserMinId).HasColumnName("user_min_id").ValueGeneratedNever();
        builder.Property(x => x.UserMaxId).HasColumnName("user_max_id").ValueGeneratedNever();
        builder.Property(x => x.RequesterId).HasColumnName("requester_id");

        builder.Property(x => x.Status).HasColumnName(StatusColumn)
            .HasMaxLength(10)
            .HasConversion(LowercaseEnum.Converter<FriendshipStatus>())
            .HasDefaultValueSql("'pending'")
            .HasSentinel(LowercaseEnum.NotSet<FriendshipStatus>());      // Pending = 0 = CLR default — xem LowercaseEnum.NotSet

        builder.Property(x => x.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("now()");
        builder.Property(x => x.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("now()");
        builder.Property(x => x.AcceptedAt).HasColumnName("accepted_at");

        // PK phục vụ tra theo user_min_id. "Bạn của tôi" khi tôi là user_max_id cần index riêng — thiếu nó thì
        // FeedSourceReader (A5) quét tuần tự nửa bảng ở mỗi request feed trượt cache.
        builder.HasIndex(x => x.UserMaxId).HasDatabaseName("idx_friendships_user_max");
    }
}
```

`HasDefaultValueSql("'pending'")` là literal viết tay duy nhất — nếu muốn chặt tuyệt đối thì sinh bằng
`$"'{LowercaseEnum.Name(FriendshipStatus.Pending)}'"`.

**Bước 4 —** `Configurations/FollowConfiguration.cs`**.**

```csharp
builder.ToTable("follows", t =>
    t.HasCheckConstraint("ck_follows_not_self", "follower_id <> followee_id"));
builder.HasKey(x => new { x.FollowerId, x.FolloweeId });
// KHÔNG index theo followee_id: "ai theo dõi tôi" chưa có người đọc ở GĐ4 (Mục 4 gốc). GĐ6 thêm nếu cần.
```

**Bước 5 —** `DesignTimeSocialGraphDbContextFactory.cs`**.** Chép `DesignTimeContentDbContextFactory`, đổi ba định danh,
**giữ nguyên** khối XML doc ghi lệnh `dotnet ef` (sửa đường dẫn project trong lệnh).

### Cạm bẫy đã biết

1. `HasConversion<string>()` **có sẵn của EF** lưu `"Pending"` chữ hoa → mọi INSERT nổ `ck_friendships_status`. Dùng
  `LowercaseEnum.Converter`. Đây là cạm bẫy số một của `A4`+`A5` GĐ2 và nó không tự biến mất ở module mới.
2. **Gõ tay** `'accepted'` **trong** `ck_friendships_accepted`**.** Chuỗi SQL thô không đi qua converter. Sinh bằng `EqualsSql`.
3. **Quên** `HasSentinel`**.** Không sai dữ liệu, nhưng EF cảnh báo mỗi lần dựng model — một dòng Warning ở mọi lần app
  khởi động (đã gặp thật ở `A5` GĐ2).
4. `HasIndex(x => x.UserMinId)`**.** Thừa — PK `(user_min_id, user_max_id)` đã phục vụ tra theo cột đầu. Mục 4 chỉ có
  **một** index phụ.
5. **Đặt** `LowercaseEnum` **bản chép ở** `public`**.** Giữ `internal` như bản gốc; bề mặt module không cần nó.

---



## 5. A3 — Migration đầu + DI + hook `--migrate` + harness test

**Mục tiêu.** Biến schema `socialgraph` thành artifact có version, tái lập y hệt ở mọi môi trường bằng **một lệnh** ở
bước deploy; và cho mọi DB của bộ integration test có schema đó trước khi `A5` bắt Content đọc nó (lệch L3).

**Kết quả mong đợi.**

- `src/backend/Modules/SocialGraph/Infrastructure/Migrations/` có `<timestamp>_InitialSocialGraph.cs`, `.Designer.cs`,
`SocialGraphDbContextModelSnapshot.cs` — **đã commit**.
- `src/backend/Modules/SocialGraph/DependencyInjection/SocialGraphModuleExtensions.cs` có `Schema`,
`AddSocialGraphModule`, `MigrateSocialGraphModuleAsync`.
- `Program.cs`: đúng **hai** dòng mới + câu log `[migrate]` nêu đủ bốn schema.
- `ModulesApiFactory` và `PostgresFixture.SeededContentDatabaseAsync` migrate thêm SocialGraph.
- DB sạch: `--migrate` exit 0 **hai lần**, lần hai không đổi gì; `\dn` thấy `identity`, `profile`, `content`, `socialgraph`.
- `SocialGraphDbContextSchemaTests` xanh; **toàn bộ** bộ integration cũ vẫn xanh.



### Các bước

**Bước 1 —** `SocialGraphModuleExtensions.cs`**.** Chép `ContentModuleExtensions` rồi **cắt**, đừng chép rồi quên xóa:

```csharp
public static class SocialGraphModuleExtensions
{
    public const string Schema = SocialGraphDbContext.Schema;

    public static IServiceCollection AddSocialGraphModule(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<SocialGraphDbContext>(o => o.UseSocialGraphNpgsql(connectionString));
        // A5 đăng ký IFriendshipReader + IFeedSourceReader vào ĐÚNG hàm này. C1, D0 cũng vậy — đừng mở hàm thứ hai.
        return services;
    }

    /// <summary>Chạy ở hook --migrate, KHÔNG chạy khi api khởi động. SocialGraph không có dữ liệu nền (Mục 5 gốc).
    /// Ngoại lệ phải thoát ra ngoài để `set -e` của CD dừng trước `up -d`.</summary>
    public static async Task MigrateSocialGraphModuleAsync(this IServiceProvider services, CancellationToken ct = default)
    {
        await using var scope = services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SocialGraphDbContext>();
        await db.Database.MigrateAsync(ct);
    }
}
```

Hàm DI phải dựng được bằng `new ServiceCollection()` **không host** — hai chỗ harness ở Bước 5 làm đúng vậy. Thứ gì cần
`IConfiguration` thì bind lười (nếp `MediaCleanupOptions` của Content).

**Bước 2 — sinh migration**, từ gốc repo:

```bash
dotnet ef migrations add InitialSocialGraph \
  --project src/backend/Modules/SocialGraph/SocialApp.Modules.SocialGraph.csproj \
  --startup-project src/backend/Modules/SocialGraph/SocialApp.Modules.SocialGraph.csproj \
  --output-dir Infrastructure/Migrations
```

**Đọc file sinh ra bằng mắt** trước khi chạy. Phải thấy: `EnsureSchema(name: "socialgraph")`; `schema: "socialgraph"` ở
cả hai `CreateTable`; năm CHECK (`ck_friendships_order`, `_requester`, `_status`, `_accepted`, `ck_follows_not_self`);
`status` có `defaultValueSql: "'pending'"`; `idx_friendships_user_max`; **không** có `AddForeignKey` nào. Thiếu CHECK nghĩa
là `A2` Bước 3–4 chưa vào.

**Bước 3 — nối** `Program.cs`**, đúng hai dòng + câu log.**

Dòng DI, ngay dưới `builder.Services.AddContentModule(postgres);` (dòng 192), cùng khuôn comment:

```csharp
// --- Module SocialGraph: DbContext riêng, schema "socialgraph" (ADR-001, Đ-4.1) ---
builder.Services.AddSocialGraphModule(postgres);
```

Dòng migrate, trong nhánh `if (isMigrate)`, **sau** Content — thứ tự Identity → Profile → Content → SocialGraph là cố định
(Mục 5 gốc):

```csharp
await app.Services.MigrateContentModuleAsync();
await app.Services.MigrateSocialGraphModuleAsync();
```

Câu `Console.WriteLine` (dòng 392–395): **nối** `"\"{SocialGraphModuleExtensions.Schema}\""` vào vế đầu, không viết câu
thứ hai — đúng hình dạng đã chốt ở `A3`/`A5` GĐ2. Vế sau (seed + kiểm vai trò) vẫn chỉ nói về `identity`.

**Không** đụng `AddApplicationPart` và `apiGroups` — chưa có controller nào. Hai chỗ đó là `D0` (cùng cách GĐ2 để `D1` làm).

**Bước 4 — nghiệm thu bằng tay trên DB sạch.**

```bash
docker compose -f deploy/docker-compose.dev.yml down -v
docker compose -f deploy/docker-compose.dev.yml up -d

dotnet run --project src/backend/SocialApp.Api -- --migrate; echo "exit=$?"    # lần 1
dotnet run --project src/backend/SocialApp.Api -- --migrate; echo "exit=$?"    # lần 2 — vẫn 0, không đổi gì
```

```sql
\dn
-- kỳ vọng: content, identity, profile, socialgraph (và public)

SELECT table_schema, count(*) FROM information_schema.tables
WHERE table_name = '__EFMigrationsHistory' GROUP BY table_schema;
-- kỳ vọng: bốn dòng, mỗi dòng 1 — KHÔNG có dòng "public"

\d socialgraph.friendships
-- kỳ vọng: PK (user_min_id, user_max_id), 4 CHECK, idx_friendships_user_max, KHÔNG có "Foreign-key constraints"
```

**Bước 5 — harness test, hai chỗ (lệch L3).** Cả hai đang liệt kê module **bằng tay**:


| File                                                            | Hàm                           | Thêm                                                                                                         |
| --------------------------------------------------------------- | ----------------------------- | ------------------------------------------------------------------------------------------------------------ |
| `tests/SocialApp.IntegrationTests/Harness/ModulesApiFactory.cs` | `CreateMigratedDatabaseAsync` | `.AddSocialGraphModule(cs)` vào chuỗi DI + `await services.MigrateSocialGraphModuleAsync();` **sau** Content |
| `tests/SocialApp.IntegrationTests/Harness/PostgresFixture.cs`   | `SeededContentDatabaseAsync`  | như trên; sửa doc "CẢ BA module" → "cả bốn module"                                                           |


Đổi tên `SeededContentDatabaseAsync` thì **không** — tên nói về mục đích (DB có bảng Content cho test đọc), không nói số
module; đổi tên là sửa hàng chục test cho một khác biệt không ai cần. Doc của nó đã báo trước đúng chuyện này ("thêm
module thứ tư").

Chạy cả bộ integration: **phải xanh không đổi gì khác**. Ở `A3` Content còn dùng `AlwaysStrangers` nên chưa ai đọc
`socialgraph` — lượt chạy này chỉ chứng minh migrate thêm một schema không phá gì. Cái giá thời gian đo ở `B1`.

**Bước 6 —** `tests/SocialApp.IntegrationTests/SocialGraphDbContextSchemaTests.cs`**.** Chép `ContentDbContextSchemaTests`,
`[Collection(PostgresCollection.Name)]`, `postgres.CreateDatabaseAsync()`. Mỗi khẳng định ứng với một thứ mà nếu cấu hình
sai thì **không lỗi nào khác báo**:


| #   | Khẳng định                                                                                                                                 | Cách kiểm                                                                                      |
| --- | ------------------------------------------------------------------------------------------------------------------------------------------ | ---------------------------------------------------------------------------------------------- |
| 1   | `__EFMigrationsHistory` của SocialGraph nằm trong schema `socialgraph`                                                                     | `information_schema.tables`                                                                    |
| 2   | INSERT `user_min_id > user_max_id` bị chặn                                                                                                 | SQL thô, kỳ vọng `PostgresException` nêu `ck_friendships_order`                                |
| 3   | INSERT `requester_id` là người thứ ba bị chặn                                                                                              | `ck_friendships_requester`                                                                     |
| 4   | INSERT `status = 'accepted'` mà `accepted_at IS NULL` bị chặn **và** `status = 'pending'` có `accepted_at` bị chặn                         | `ck_friendships_accepted` — cả hai chiều của phép `=`                                          |
| 5   | INSERT lần hai **cùng cặp** (kể cả `requester_id` khác) bị chặn                                                                            | Vi phạm PK `23505` — đây là nguồn của **409** ở `D2` và của `FRD-06`                           |
| 6   | Qua EF: `Friendship.Request(a, b, now)` với **cặp đối nghịch** của `A1` Bước 6 lưu được, rồi đọc lại đúng `Min`/`Max`/`status = 'pending'` | Chứng minh `FriendPair.Of` khớp thứ tự Postgres **bằng Postgres**, và converter ghi chữ thường |
| 7   | INSERT `follows` với `follower_id = followee_id` bị chặn                                                                                   | `ck_follows_not_self`                                                                          |


Khẳng định #6 là thứ unit test không thay được: unit test chỉ nói C# nhất quán với chính nó.

### Cạm bẫy đã biết

1. **Chép luôn phần đăng ký** `MediaCleanupWorker`**,** `TimeProvider`**, validator** từ `AddContentModule`. SocialGraph ở `A3`
  chưa cần gì ngoài `DbContext`. `TimeProvider` (`TryAddSingleton`) thêm khi `D2` cần đồng hồ.
2. **Bọc** `try/catch` **quanh** `MigrateSocialGraphModuleAsync` — không. Lỗi migration phải làm process thoát khác 0.
3. **Quên commit snapshot.** Migration kế tiếp sinh từ snapshot rỗng → chứa lại cả hai bảng → `--migrate` staging đỏ.
4. **Chỉ sửa một trong hai chỗ harness.** Test dùng `ModulesApiFactory` xanh, AuthZ matrix (đi qua `SeededContentDatabaseAsync`)
  đỏ ở `A5` — hoặc ngược lại. Triệu chứng là 500 `relation "socialgraph.friendships" does not exist` ở **một nửa** bộ test.
5. **Chạy** `dotnet ef` **từ thư mục module** — `DevEnvFile` không tìm thấy `deploy/.env`, thông báo trông như "thiếu cấu hình".

---



## 6. A4 — Migration thứ hai của Content: `idx_posts_public_recent`

**Mục tiêu.** Cho feed gợi ý (`Đ-4.6`) một index đọc thẳng "bài công khai mới nhất toàn hệ thống" theo keyset
`(created_at DESC, post_id DESC)` — cùng thứ tự với `PostCursor`, nên trang 2 cũng đọc từ index, không sort.

**Kết quả mong đợi.**

- `PostConfiguration` có thêm **một** `HasIndex`; filter sinh từ `LowercaseEnum`.
- Migration `<timestamp>_PublicRecentIndex.cs` (+ `.Designer.cs`, snapshot cập nhật) chỉ chứa **một** `CreateIndex` và
`Down` là **một** `DropIndex`.
- `dotnet ef migrations add Tmp` ngay sau đó ra **rỗng** (rồi `migrations remove`).
- `ContentDbContextSchemaTests` có thêm khẳng định index tồn tại với đúng định nghĩa.
- Migration `InitialContent` **không đổi một byte** (`git diff` của file đó rỗng).



### Các bước

**Bước 1 —** `PostConfiguration.cs`, ngay dưới `idx_posts_author_created` (dòng 81–85):

```csharp
// Feed gợi ý (Đ-4.6, GĐ4): bài công khai mới nhất TOÀN HỆ THỐNG. idx_posts_author_created không phục vụ được
// truy vấn không có author_id. Hai literal lấy từ LowercaseEnum — chuỗi HasFilter không đi qua converter.
builder.HasIndex(x => new { x.CreatedAt, x.PostId })
    .HasDatabaseName("idx_posts_public_recent")
    .IsDescending(true, true)
    .HasFilter(
        $"{LowercaseEnum.EqualsSql(StatusColumn, PostStatus.Published)} AND " +
        $"{LowercaseEnum.EqualsSql(PrivacyColumn, PostPrivacy.Public)}");
```

Nếu `PostConfiguration` chưa có hằng `PrivacyColumn` thì thêm cạnh `StatusColumn`, không gõ `"privacy"` tại chỗ.

**Bước 2 — sinh migration**, cùng khuôn lệnh `A3` với project Content, tên `PublicRecentIndex`. Đọc bằng mắt: một
`CreateIndex` trong schema `content`, `descending: new[] { true, true }`, `filter:` đúng
`status = 'published' AND privacy = 'public'`.

**Bước 3 —** `--migrate` **hai lần trên DB dev** (DB đã có `InitialContent`): lần 1 áp đúng một migration, lần 2 không đổi gì.

**Bước 4 — khẳng định trong** `ContentDbContextSchemaTests`**.** Đọc `pg_indexes.indexdef` của `idx_posts_public_recent` và
khẳng định chứa `(created_at DESC, post_id DESC)` và `WHERE` có cả hai điều kiện. Index một phần mà filter sai thì
truy vấn vẫn **đúng** kết quả — chỉ không dùng index, và chỉ lộ ra ở `EXPLAIN` của `C2`. Test này bắt sớm hơn.

### Cạm bẫy đã biết

1. **Sửa** `InitialContent` **thay vì sinh migration mới.** Staging đã áp `InitialContent`; sửa nó là lịch sử migration lệch
  giữa repo và DB, và `--migrate` không bao giờ tạo index trên staging (luật 6, Mục 1.3).
2. **Migration sinh ra có thêm thay đổi ngoài index** (ví dụ `AlterColumn`). Dấu hiệu model Content đã trôi so với
  snapshot từ trước — **dừng**, tìm nguyên nhân, đừng commit lẫn vào đây.
3. `CREATE INDEX` **khóa ghi** `posts` **trong lúc dựng.** Vô hại ở quy mô staging. Ghi chú cho GĐ7 đã có ở Mục 4 gốc
  (`CONCURRENTLY` + tắt transaction) — **không** làm ở đây: `CONCURRENTLY` trong migration EF đòi
   `suppressTransaction`, thêm độ phức tạp cho một bảng vài trăm dòng.
4. **Nghĩ rằng cần index riêng cho** `privacy = 'public'` **trong feed mạng lưới.** Không — truy vấn LATERAL (Đ-4.7) đi theo
  `author_id` trên `idx_posts_author_created`; `privacy` lọc trong vòng lặp. Index này **chỉ** cho feed gợi ý.

---



## 7. A5 — `FriendshipReader` + `FeedSourceReader` + đổi DI

**Mục tiêu.** Bật BR-02 thật — bài `friends` hiện với bạn, và chỉ với bạn — mà Content **không đổi một dòng logic**
(mốc không lùi được số 2, `Đ-4.3`). Dựng contract batch `IFeedSourceReader` (`Đ-4.4`) để khối C viết trên chữ ký đã chốt.

**Kết quả mong đợi.**

- `src/backend/SocialApp.SharedKernel/Contracts/IFeedSourceReader.cs` — interface + `record FeedSources`.
- `src/backend/Modules/SocialGraph/Infrastructure/FriendshipReader.cs` và `FeedSourceReader.cs` (phẳng, không thư mục con).
- `AddSocialGraphModule` đăng ký cả hai, **scoped**.
- `ContentModuleExtensions.cs`: dòng `services.AddSingleton<IFriendshipReader, AlwaysStrangers>();` và khối comment trên
nó **đã xóa**; `using SocialApp.SharedKernel.Contracts;` còn hay không tùy chỗ khác còn dùng.
- Test khởi động: host resolve `IFriendshipReader` → **không** phải `AlwaysStrangers`, `GetServices` có **đúng một**.
- `READ-01` và `READ_02_05_ma_tran_BR02` xanh **không sửa khẳng định nào**.
- `FriendshipReaderTests`, `FeedSourceReaderTests` xanh trên Postgres thật.
- Thử cho đỏ: khôi phục dòng `AlwaysStrangers` → test khởi động đỏ. `git status` sạch trước và sau.



### Các bước

**Bước 1 —** `IFeedSourceReader` **ở SharedKernel.** Ba luật contract của Đ-2.3 (chỉ đọc, chỉ chiếu, batch trước):

```csharp
namespace SocialApp.SharedKernel.Contracts;

/// <summary>
/// Tập tác giả đổ bài vào feed của một người (Đ-4.4, Đ-4.5). Hai tập RỜI NHAU: FollowingOnly = đang theo dõi
/// TRỪ bạn bè — để mỗi tác giả có đúng một mức nhìn. "Chính mình" KHÔNG nằm trong đây; FeedService tự thêm.
///
/// Khác IFriendshipReader ở chính sách tươi: hiện thực được phép cache ≤ 60s (C1), xóa bởi module chủ dữ liệu.
/// </summary>
public sealed record FeedSources(IReadOnlySet<Guid> Friends, IReadOnlySet<Guid> FollowingOnly)
{
    public bool IsEmpty => Friends.Count == 0 && FollowingOnly.Count == 0;    // điều kiện feed gợi ý, Đ-4.6
}

public interface IFeedSourceReader
{
    Task<FeedSources> GetAsync(Guid userId, CancellationToken ct = default);
}
```

**Bước 2 —** `FriendshipReader`**.** Một lần tra PK, **không cache** (Đ-4.3 — lệch PTTK theo hướng chặt hơn):

```csharp
internal sealed class FriendshipReader(SocialGraphDbContext db) : IFriendshipReader
{
    public Task<bool> AreFriendsAsync(Guid userId, Guid otherUserId, CancellationToken ct = default)
    {
        // Mình không phải "bạn" của mình — cùng hợp đồng với AlwaysStrangers, và FriendPair.Of sẽ ném nếu đi tiếp.
        if (userId == otherUserId) return Task.FromResult(false);

        var pair = FriendPair.Of(userId, otherUserId);
        return db.Friendships.AsNoTracking().AnyAsync(f =>
            f.UserMinId == pair.Min && f.UserMaxId == pair.Max && f.Status == FriendshipStatus.Accepted, ct);
    }
}
```

`Pending` **không** là bạn: lời mời chưa chấp nhận không mở bài `friends`.

**Bước 3 —** `FeedSourceReader` **(chưa cache).** Hai truy vấn, không vòng lặp:

```csharp
// Bạn bè: tôi ở một trong hai đầu cặp. Nửa "tôi là min" đi PK, nửa "tôi là max" đi idx_friendships_user_max.
var friends = await db.Friendships.AsNoTracking()
    .Where(f => f.Status == FriendshipStatus.Accepted && (f.UserMinId == userId || f.UserMaxId == userId))
    .Select(f => f.UserMinId == userId ? f.UserMaxId : f.UserMinId)
    .ToListAsync(ct);

var following = await db.Follows.AsNoTracking()
    .Where(f => f.FollowerId == userId).Select(f => f.FolloweeId).ToListAsync(ct);

var friendSet = friends.ToHashSet();
return new FeedSources(friendSet, following.Where(id => !friendSet.Contains(id)).ToHashSet());
```

Cache Redis `sg:feed-sources:{userId}` **không** thêm ở đây — đó là `C1`. Viết cache trước khi có người tiêu thụ là viết
chính sách xóa cache cho những thao tác (kết bạn, hủy) chưa tồn tại.

`EXPLAIN` truy vấn bạn bè để chắc `OR` hai cột dùng được cả PK lẫn `idx_friendships_user_max` (`BitmapOr`); nếu Postgres
chọn quét tuần tự trên bộ dữ liệu tải (`C5`) thì tách thành `UNION ALL` hai nhánh — ghi kết quả vào "Thực tế thi công".

**Bước 4 — đăng ký DI ở** `AddSocialGraphModule`**.**

```csharp
// Đ-4.3: hiện thực THẬT của BR-02. Dòng AlwaysStrangers ở AddContentModule đã bị XÓA — không phải bị đè. Đăng ký
// hai lần thì cái sau thắng im lặng, và thứ tự hai dòng Add*Module trong Program.cs quyết định BR-02 thật hay giả.
// Scoped vì đọc qua SocialGraphDbContext.
services.AddScoped<IFriendshipReader, FriendshipReader>();
services.AddScoped<IFeedSourceReader, FeedSourceReader>();
```

Hệ quả vòng đời cần kiểm: `PostReadService` (scoped) nhận `IFriendshipReader` — scoped vào scoped, hợp lệ. Nếu có service
**singleton** nào của Content nhận `IFriendshipReader` thì host sẽ ném lúc dựng (captive dependency, `ValidateScopes` bật
ở Development). Kiểm bằng `grep IFriendshipReader src/backend` — hiện chỉ `PostReadService`.

**Bước 5 — xóa dòng ở** `AddContentModule` (`ContentModuleExtensions.cs:35–41`): xóa cả khối comment "GĐ4 ĐỔI ĐÚNG DÒNG
NÀY…" lẫn dòng `AddSingleton`. Chạy `impact` trên `AddContentModule` **trước** khi xóa (Mục 1.2).

**Không** thay bằng comment "IFriendshipReader do SocialGraph đăng ký" ở đây — thêm một dòng vào doc tổng của
`ContentModuleExtensions` là đủ: *"Content tiêu thụ* `IUserDirectory` *(Profile đăng ký) và* `IFriendshipReader` *(SocialGraph
đăng ký) — thiếu module chủ trong host thì request đầu tiên đọc bài nổ lúc resolve."*

**Bước 6 — đổi test khởi động, không xóa.** `StartupConfigurationTests.Host_resolve_duoc_hai_contract_cheo_module_cua_A6`
(dòng ~357–368) đang `Assert.IsType<AlwaysStrangers>(…)`. Đổi thành:

```csharp
var readers = scope.ServiceProvider.GetServices<IFriendshipReader>().ToList();
Assert.Single(readers);                                  // đăng ký hai lần là thứ tự Add*Module quyết định BR-02
Assert.IsNotType<AlwaysStrangers>(readers[0]);           // Đ-4.3
Assert.NotNull(scope.ServiceProvider.GetRequiredService<IFeedSourceReader>());
```

Sửa XML doc của test: bỏ câu "GĐ4 đổi dòng DI thì test này đỏ…", thay bằng lý do của hai khẳng định mới. Cân nhắc đổi
tên test thành `..._cua_A6_va_GD4` — đổi tên test là an toàn (không ai gọi nó), nhưng dùng `rename` của GitNexus chứ không
find-and-replace.

`ApiFactory` trỏ Postgres **không tới được** — resolve `FriendshipReader` chỉ dựng object, không mở kết nối, nên test vẫn
chạy không cần DB. Nếu nó đỏ vì kết nối thì hiện thực đang làm I/O trong constructor — sửa hiện thực, không sửa test.

**Test pin** `AlwaysStrangersTests` **(unit) giữ nguyên** — nó nói về `AlwaysStrangers`, không nói về hệ thống. Chỉ sửa doc
nếu doc hứa "GĐ4 đổi một dòng DI trong `AddContentModule`".

**Bước 7 — docs sống cùng code.** Năm chỗ comment đang mô tả GĐ2 như hiện tại, sửa **trong commit này**:


| Chỗ                                                                   | Đang nói                                                         | Sửa thành                                                                                            |
| --------------------------------------------------------------------- | ---------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------- |
| `SharedKernel/Contracts/IFriendshipReader.cs` — doc `AlwaysStrangers` | "GĐ4 bật kết bạn thật bằng một dòng DI trong `AddContentModule`" | Hiện thực thật ở SocialGraph (Đ-4.3); `AlwaysStrangers` còn lại cho test dựng cảnh "không ai là bạn" |
| `Content/Application/Posts/PostVisibility.cs:21`                      | "Ở GĐ2 LUÔN `false`"                                             | Kết quả tra thật, đọc thẳng DB                                                                       |
| `Content/Application/Posts/PostReadService.cs:26`                     | "GĐ4 là một lượt đi DB (hoặc cache)"                             | Một lượt tra PK, không cache (Đ-4.3)                                                                 |
| `tests/…/Content/ReadPostTests.cs:34`                                 | "Ở GĐ2 `AlwaysStrangers` luôn trả `false`…"                      | Người đọc là người lạ ngẫu nhiên — `false` vì chưa kết bạn, không vì null-object                     |
| `giai-doan-4.md` B.3                                                  | năm chỗ lệch L1–L5                                               | Ghi ngược, mở bằng "Lệch B.3 (nhóm chốt): …" — mỗi cái ở commit của đầu việc tương ứng               |


Đây là "comment mô tả" theo luật commit Mục 2 — đi cùng code, không tách commit `docs`.

**Bước 8 —** `tests/SocialApp.IntegrationTests/FriendshipReaderTests.cs`**.** Chép khuôn `UserDirectoryTests`: đi qua **chính
DI** của `AddSocialGraphModule` (hiện thực là `internal`), dựng dữ liệu bằng `SocialGraphDbContext`.


| Ca                                              | Kỳ vọng                                   |
| ----------------------------------------------- | ----------------------------------------- |
| Hai người `accepted`                            | `true` **theo cả hai thứ tự đối số**      |
| Hai người `pending`                             | `false` theo cả hai thứ tự                |
| Không có dòng                                   | `false`                                   |
| Cùng một id                                     | `false`, **không** ném, **không** chạm DB |
| Cặp đối nghịch của `A1` ở trạng thái `accepted` | `true` — lưới GUID-01 trên đường đọc      |


**Bước 9 —** `tests/SocialApp.IntegrationTests/FeedSourceReaderTests.cs`**.**


| Cảnh                                       | Kỳ vọng                                                               |
| ------------------------------------------ | --------------------------------------------------------------------- |
| A bạn B (A là `min`), A bạn C (A là `max`) | `Friends = {B, C}` — cả hai nửa của cặp                               |
| A có lời mời `pending` với D               | D **không** trong `Friends`                                           |
| A theo dõi E                               | `FollowingOnly = {E}`                                                 |
| A vừa là bạn vừa theo dõi B                | B trong `Friends`, **không** trong `FollowingOnly` — hai tập rời nhau |
| E theo dõi A (chiều ngược)                 | E **không** trong nguồn của A                                         |
| Người mới                                  | `IsEmpty == true`                                                     |


**Bước 10 — chạy cả bộ và thử cho đỏ.**

```bash
dotnet test SocialApp.sln
```

`READ-01`, `READ_02_05_ma_tran_BR02` (sáu ca), `PostVisibilityTests`, `ListPostsTests` phải xanh **mà không sửa khẳng định
nào**. Rồi thử đỏ:


| Đột biến                                                                                           | Test phải đỏ                            |
| -------------------------------------------------------------------------------------------------- | --------------------------------------- |
| Khôi phục `AddSingleton<IFriendshipReader, AlwaysStrangers>()` **sau** dòng `AddSocialGraphModule` | Test khởi động (`Assert.Single`)        |
| Khôi phục dòng đó và **xóa** đăng ký ở SocialGraph                                                 | Test khởi động (`IsNotType`)            |
| `FriendshipReader` bỏ điều kiện `Status == Accepted`                                               | `FriendshipReaderTests` ca `pending`    |
| `FeedSourceReader` bỏ nhánh `UserMaxId == userId`                                                  | `FeedSourceReaderTests` ca "A là `max`" |




Ghi số đột biến vào dòng `Test:` của commit. Đột biến `READ-06b` là việc của `B3`, sau `D3`.

### Cạm bẫy đã biết

1. **Chỉ thêm đăng ký ở SocialGraph mà quên xóa ở Content.** Hai đăng ký, cái sau thắng **im lặng**. Hôm nay
  `AddSocialGraphModule` đứng sau nên BR-02 chạy thật và mọi test xanh — cho tới ngày ai đó sắp lại `Program.cs`. Test
   khởi động với `Assert.Single` tồn tại đúng cho ngày đó.
2. **Sửa** `PostReadService` **/** `PostVisibility` **"cho chắc".** Đ-4.3: Content không đổi logic. Thấy mình đang sửa logic
  Content để bật kết bạn nghĩa là contract đã bị đi vòng. Chỉ sửa **comment** (Bước 7).
3. **Cache trong** `FriendshipReader`**.** Đ-4.3 chốt đọc thẳng DB: hủy kết bạn phải có hiệu lực ngay với `GET /posts/{id}`
  (`FRD-09`). Cache 60s chỉ ở `FeedSourceReader`, và là việc của `C1`.
4. `FriendshipReader` **là** `public`**.** Để `internal`; host và test đi qua interface + DI. `public` là mời module khác
  `new FriendshipReader(...)` — tức import SocialGraph.
5. `FromSql("… socialgraph.friendships …")` **trong Content "cho nhanh".** ArchUnitNET không đọc chuỗi SQL, mọi test xanh.
  Đây là một trong những thứ chỉ tự rà bắt được (B.9 gốc) — Content đọc SocialGraph **chỉ** qua hai interface.
6. `ListByUserAsync` **giờ tốn một câu SQL mỗi lần gọi.** Nó gọi `AreFriendsAsync` khi `userId != actorId`, không xét
  `privacy` (khác `GetAsync`). Đúng về kết quả, và một câu tra PK là rẻ — ghi nhận, không sửa ở khối A. `C3` (hydrate
   dùng chung) là chỗ cân nhắc lại.

---



## 8. Kế hoạch commit

Scope `gd4-a` (luật commit Mục 3). Mỗi commit chạm code **phải** có dòng `Test:` và dòng `detect-changes:`; chạy
`node .gitnexus/run.cjs detect-changes --scope all --repo .` trước mỗi commit, `partial`/`truncated` thì chạy lại. Footer
**sạch bút ký** (Mục 6).


| #   | Tiêu đề                                                                                                       | Gồm                                                                                                                                                                              |
| --- | ------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| 1   | `chore(gd4-a): thêm gói EF Core 8.0.10 cho module SocialGraph`                                                | `[0]`. Tách riêng để diff của `A1` không lẫn thay đổi build                                                                                                                      |
| 2   | `feat(gd4-a): A1 — entity quan hệ, FriendPair theo thứ tự uuid của Postgres, canh gác SocialGraph.Domain`     | `A1` + test canh gác + unit test. Ghi ngược **L1**, **L4** vào `giai-doan-4.md`                                                                                                  |
| 3   | `feat(gd4-a): A2–A3 — schema socialgraph với BR-03 do DB giữ, --migrate bốn module chạy hai lần không đổi gì` | `A2` + `A3` + schema test + hai dòng `Program.cs` + hai chỗ harness. Gộp vì migration không tách khỏi configuration sinh ra nó — nói rõ trong thân bài. Ghi ngược **L3**, **L5** |
| 4   | `feat(gd4-a): A4 — idx_posts_public_recent cho feed gợi ý`                                                    | `A4` + khẳng định trong `ContentDbContextSchemaTests`                                                                                                                            |
| 5   | `feat(gd4-a): A5 — IFriendshipReader thật ở SocialGraph, gỡ AlwaysStrangers khỏi Content`                     | `A5` + `IFeedSourceReader` + test khởi động đổi khẳng định + hai lớp test + năm chỗ comment. Ghi ngược **L2**                                                                    |


Mẫu thân bài cho commit #5:

```
Đ-4.3: IFriendshipReader có hiện thực thật (FriendshipReader, một lần tra PK cặp chuẩn hóa, không cache) đăng ký ở
AddSocialGraphModule. Dòng AlwaysStrangers ở AddContentModule đã XÓA, không bị đè — test khởi động khẳng định đúng một
đăng ký và không phải AlwaysStrangers. Logic Content không đổi dòng nào; chỉ sửa bốn comment còn mô tả GĐ2 như hiện tại.

Thêm contract IFeedSourceReader (Đ-4.4) + FeedSourceReader chưa cache — cache là C1.

Lệch B.3 (nhóm chốt): READ-06b không xanh ở khối A. Matrix dựng cảnh qua API thật, "A và B là bạn" cần D2+D3. Khối A
chứng minh BR-02 thật bằng FriendshipReaderTests qua DI của module; READ-06/06b vào matrix ở B2, đỏ có chủ đích tới D3.

Test: Unit … → …, Integration … → … (+… FriendshipReaderTests, +… FeedSourceReaderTests). READ-01..05 xanh, không sửa
khẳng định nào. Thử cho đỏ 4 đột biến đều bị bắt.
detect-changes: …
```

---



## 9. Checklist nghiệm thu khối A

Tick từng dòng, có bằng chứng. Dòng không áp dụng thì ghi lý do, **không xóa dòng**.

**Schema và migration**

- [ ] `\dn` trên DB dev thấy **bốn** schema: `identity`, `profile`, `content`, `socialgraph`
- [ ] Mỗi schema có đúng **một** `__EFMigrationsHistory`; **không** có cái nào ở `public`
- [ ] `--migrate` trên DB sạch: lần 1 exit 0, lần 2 exit 0 và không đổi gì
- [ ] `dotnet ef migrations add Tmp` cho **SocialGraph và Content** đều ra migration **rỗng** (rồi `migrations remove`)
- [ ] `InitialContent` không đổi (`git diff` rỗng); `PublicRecentIndex` chỉ có một `CreateIndex`
- [ ] Ba file migration của `InitialSocialGraph` đã commit; `ProductVersion` trong snapshot là `8.0.10`
- [ ] **Không có FK nào trong schema** `socialgraph` và không FK nào qua ranh giới schema — cả hai câu ra 0 dòng:

```sql
SELECT conname FROM pg_constraint
WHERE contype = 'f' AND connamespace = 'socialgraph'::regnamespace;

SELECT con.conname
FROM pg_constraint con
JOIN pg_class src ON src.oid = con.conrelid
JOIN pg_class tgt ON tgt.oid = con.confrelid
WHERE con.contype = 'f' AND src.relnamespace <> tgt.relnamespace;
```

**Ràng buộc do DB giữ**

- [ ] `ck_friendships_order`, `_requester`, `_accepted` (cả hai chiều), `ck_follows_not_self` chặn thật
- [ ] Cùng cặp lần hai → `23505` (nguồn của 409 ở `D2`)
- [ ] Cặp đối nghịch (`00000001-…` / `01000000-…`) lưu được qua `Friendship.Request` — `FriendPair.Of` khớp Postgres
- [ ] `idx_friendships_user_max` và `idx_posts_public_recent` tồn tại đúng định nghĩa (`pg_indexes`)

**Ranh giới**

- [ ] `grep -rn "Microsoft.EntityFrameworkCore\|Npgsql" src/backend/Modules/SocialGraph/Domain` → 0 kết quả
- [ ] `grep -rn "SocialApp.Modules\." src/backend/Modules/SocialGraph` không thấy tên module khác; tương tự Content không thấy `SocialGraph`
- [ ] `dotnet test tests/SocialApp.ArchitectureTests` xanh; `SocialGraph_Domain_namespace_must_not_be_empty` **đã từng thấy đỏ**
- [ ] `FriendshipReader`, `FeedSourceReader`, các configuration là `internal`

**BR-02 thật (mốc không lùi được số 2)**

- [ ] Dòng `AlwaysStrangers` không còn trong `ContentModuleExtensions.cs` (`grep AlwaysStrangers src/backend` chỉ còn định nghĩa trong SharedKernel)
- [ ] Test khởi động: đúng một `IFriendshipReader`, không phải `AlwaysStrangers`; `IFeedSourceReader` resolve được
- [ ] `READ-01`, `READ_02_05_ma_tran_BR02` xanh **không sửa khẳng định**; `git diff` của hai file test đó chỉ chạm comment
- [ ] `FriendshipReaderTests`, `FeedSourceReaderTests` xanh; bốn đột biến Mục 7 Bước 10 đã từng đỏ
- [ ] `AlwaysStrangersTests` (unit) vẫn xanh, không đổi khẳng định

**Luật repo**

- [ ] Không có cột nào ngoài Mục 4 gốc; lệch thì Mục 4 đã sửa **trong cùng commit**
- [ ] Năm chỗ lệch L1–L5 đã ghi ngược vào `giai-doan-4.md` B.3 / Mục 10.5, mỗi cái ở commit của nó
- [ ] Năm chỗ comment ở Mục 7 Bước 7 đã sửa trong commit `A5`
- [ ] Không có secret trong diff; không có khóa nào (khối A không chạm Redis, R2, JWT)
- [ ] Mọi commit có `Test:` và `detect-changes:`; footer sạch bút ký

---



## 10. Khối A để lại gì


| Di sản                                                                                                   | Ai thừa hưởng ngay                                                       | Ai thừa hưởng về sau                          |
| -------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------ | --------------------------------------------- |
| Schema `socialgraph` + BR-03 do DB giữ (PK cặp, bốn CHECK)                                               | `D2`–`D6` (`23505` → 409 thay vì tự kiểm trước)                          | GĐ5 — BR-09 chat chỉ giữa bạn bè              |
| `FriendPair.Of` — chỗ duy nhất chuẩn hóa cặp, có lưới GUID-01                                            | `D2`–`D4`, `B1` helper dựng cảnh                                         | Mọi chỗ sau này tra quan hệ theo cặp          |
| `RelationshipState` hàm thuần                                                                            | `D1` (`GET /relationships`), mọi thao tác ghi trả `RelationshipResponse` | `E2` gián tiếp — bốn trạng thái nút           |
| `IFriendshipReader` thật, đúng một đăng ký, có test khởi động canh                                       | BR-02 của `GET /posts/{id}` và `/users/{id}/posts` ngay lập tức          | GĐ3 (`READ-CMT-*` với bạn thật), GĐ5 (TC-A07) |
| `IFeedSourceReader` + `FeedSources` (hai tập rời nhau)                                                   | `C1` thêm cache, `C2` dựng truy vấn LATERAL                              | GĐ6 — "ai theo dõi ai" cho thông báo          |
| `idx_posts_public_recent`                                                                                | `C2` feed gợi ý + `EXPLAIN`                                              | GĐ6 — feed gợi ý vẫn giữ cho tài khoản mới    |
| Harness migrate bốn module                                                                               | Mọi integration test từ `B1` trở đi                                      | GĐ5 thêm module thứ năm vào đúng hai chỗ đó   |
| Khuôn "đổi hiện thực contract chéo module": xóa ở người tiêu thụ, đăng ký ở chủ dữ liệu, `Assert.Single` | —                                                                        | Mọi null-object sau này (nếu có)              |


---



## 11. Ranh giới — cái gì **không** thuộc khối A


| Không thuộc khối A                                                                                | Thuộc về                                    | Vì sao dễ nhầm                                                      |
| ------------------------------------------------------------------------------------------------- | ------------------------------------------- | ------------------------------------------------------------------- |
| `AddApplicationPart`, mục `apiGroups`, `SocialGraphApiGroup`, controller, DTO, validator          | `D0`–`D7`                                   | Mục 5 gốc liệt kê chúng cùng dòng với `AddSocialGraphModule`        |
| `SocialGraphPermissions`, `SocialGraphPermissionsTests`, `SocialGraphErrors`                      | `D0`, `B5`                                  | Cũng là "nền của module"                                            |
| `socialgraph-v1.yaml`, phần `/feed` của `content-v1.yaml`                                         | **Cổng mở** (Mục 9.2), commit trước khối A  | Hợp đồng đi trước entity, không đi sau                              |
| Cache `sg:feed-sources:*`, `InvalidateAsync`, fail-open                                           | `C1`                                        | `FeedSourceReader` do `A5` viết — nhưng chưa cache                  |
| Cache `feed:p1:*`, `CommandTimeout` 5s, 503                                                       | `C4`                                        |                                                                     |
| Truy vấn LATERAL, `IFeedStore`, `FeedService`, Đ-4.11 `Status == Published` ở `ListByAuthorAsync` | `C2`                                        | `A4` cũng chạm `PostConfiguration` / migration Content              |
| Tách hàm hydrate dùng chung                                                                       | `C3`                                        |                                                                     |
| Câu `UPDATE` chấp nhận lời mời, `INSERT … ON CONFLICT`, bắt `23505`                               | `D2`, `D3`, `D6`                            | CHECK và PK do `A2` đặt là thứ các câu đó dựa vào                   |
| Event `FriendRequestSent` / `Accepted`                                                            | `D2`, `D3` (Đ-4.15)                         |                                                                     |
| Helper dựng cảnh "A và B là bạn" qua API, đo thời gian bộ integration                             | `B1`                                        | `A3` đã sửa harness — nhưng chỉ phần migrate (lệch L3)              |
| Sáu dòng AuthZ matrix (`READ-06`, `READ-06b`, …)                                                  | `B2`                                        | Kết quả khối A trong B.3 nhắc `READ-06b` (lệch L2)                  |
| `SocialGraphContractTests`, dòng `Content Include` trong csproj test                              | `B5`                                        | Cần controller có thật                                              |
| `tests/load/feed/seed.sql`, môi trường đo                                                         | `C5`                                        | Seed chèn thẳng vào `socialgraph.friendships` — cần schema của `A3` |
| Seeder ứng dụng                                                                                   | **Không ai** — Mục 5 gốc: GĐ4 không seed gì | Khuôn `MigrateIdentityModuleAsync` có gọi seeder, dễ chép nhầm      |


---



## Thực tế thi công

*Ghi khi làm, có ngày tháng: chỗ lệch tài liệu này, lỗi gặp thật, số liệu* `EXPLAIN` *của truy vấn nguồn feed. Nếp của
khối A GĐ2 — sửa ngay tại mục liên quan phía trên kèm "(sửa ngày …, lúc thi công* `A…`*)", và tóm tắt ở đây.*

**2026-09-22 — rà code trước khi commit.**

- `A1`: `FriendPair` đổi từ record positional sang constructor `private` (Mục 3 Bước 2). Thêm test `Khong_co_constructor_public`.
