# Hướng dẫn thực hiện — Khối A. Nền dữ liệu + Khối C. Hạ tầng chéo module (GĐ6)

> Bản triển khai chi tiết của **B.4 Khối A** và **B.5 Khối C** trong [giai-doan-6.md](giai-doan-6.md). Tài liệu gốc trả lời
> *cái gì* và *vì sao*; tài liệu này trả lời *gõ vào file nào, theo thứ tự nào, và nhìn vào đâu để biết đã xong thật*.
>
> **Nguồn sự thật vẫn là** `giai-doan-6.md`: Đ-6.1 (chia module), Đ-6.3 (hai hợp đồng ghi), Đ-6.5–Đ-6.10 (tài khoản, thu hồi,
> bất biến, fail-closed, vai trò, cache quyền), Đ-6.12–Đ-6.19 (báo cáo, ẩn, audit, thông báo, tìm kiếm), **Mục 4** (DDL đích), Mục 5
> (dữ liệu nền), Mục 6 (ba tầng kiểm soát), Mục 9.4 (chỗ đụng A, B), Mục 10 (test) — và `AGENTS.md`. Chỗ nào tài liệu này lệch
> với hai file đó thì sửa ở đây, không sửa ngược. Muốn đổi một `Đ-6.*` thì đó là **quyết định mới**, có ngày, ghi vào
> `giai-doan-6.md` trong cùng commit.
>
> Hai khối viết chung một file vì **C đứng thẳng trên A** (C1 ghi vào bảng của A1, C3 đọc bảng của A3) và cùng nằm trên đường găng
> `A1 → A3 → C1 → C4` (B.10). Khối C0 (đường ray) đã xong — xem [huong-dan-khoi-c0-duong-ray.md](huong-dan-khoi-c0-duong-ray.md).
>
> Cái mới của hai khối này so với mọi giai đoạn trước: **luật "không được" nằm ở DB** (trigger append-only, trigger vai trò hệ
> thống) và **lần đầu một transaction đi qua hai module** (C1, C2). Cả hai đều là thứ code review nhìn không ra — chỉ test trên
> Postgres thật và thử cho đỏ mới chứng minh được.

|                          |                                                                                                                                                                                                                                                                                  |
| ------------------------ | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **Người làm**            | Một người (không chia lane)                                                                                                                                                                                                                                                      |
| **Thời lượng**           | Khối A: **bước 3** của Mục 9.3 (1 ngày, chung với phần migrate của `B1`). Khối C: **bước 4** (1,5 ngày, chung với `B5`) + `C6` ở **bước 9** khi B merge vé                                                                                                                       |
| **Khối này cần trước**   | `C0` trên `develop` (đã xong, `60ac7ee`). Cổng mở Mục 9.2 bước 3 (ba yaml mới) **không** chặn A, C về kỹ thuật — không đầu việc nào ở đây có controller — nhưng luật "hợp đồng trước, code sau" giữ nguyên                                                                         |
| **Khối này chặn**        | Toàn bộ khối `D` (D1–D13), `B2`–`B5`, `F1` (deploy staging chạy `migrate` sáu module + kiểm extension)                                                                                                                                                                           |
| **Không thuộc hai khối** | Controller, DTO, validator, endpoint nào (khối D) · ba file hợp đồng (cổng mở) · dòng AuthZ matrix (B2) · test đồng thời (B3) · helper "dựng Admin thứ hai / vai trò / báo cáo" (B1) · handler thông báo (D10) · `HideAsync` cho **bình luận** (sau khi A merge). Xem Mục 15 |

---

## 0. Danh sách công việc — mục tiêu và kết quả mong đợi

Mười một đầu việc của B.4 + B.5, cộng một việc chuẩn bị `[0]`. Mỗi mã (`A1`…`C6`) là **một commit** (commit-rules Mục 8).

### 0.1 Khối A — Nền dữ liệu

> **Mục tiêu khối:** hai module mới đứng độc lập, Identity và Profile mở rộng không đổi dòng dữ liệu nào, và mọi luật "không
> được" (append-only, vai trò hệ thống) nằm ở **DB**, không chỉ ở code.

| Mã      | Đầu việc                                                                                                                  | Mục tiêu — việc này tồn tại để làm gì                                                                                                                                                                                                                                             | Kết quả mong đợi — thứ kiểm chứng được                                                                                                                                                                                                                                                                                                                                  |
| ------- | ------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| **[0]** | Gói EF cho hai csproj mới + impact analysis                                                                               | Để A1, A2 biên dịch được và mỗi module tự làm startup project cho `dotnet ef` (ADR-001) — hai csproj hiện **chỉ** có `ProjectReference` tới SharedKernel                                                                                                                         | Bốn `PackageReference` chép nguyên từ `SocialApp.Modules.SocialGraph.csproj` vào `…Moderation.csproj` và `…Notification.csproj` (`.Design` có `PrivateAssets=all`); `dotnet build SocialApp.sln` xanh; bảng impact Mục 1.2 đã chạy lại                                                                                                                                  |
| **A1**  | Module Moderation: entity, `ModerationDbContext`, migration đầu, trigger append-only, nối `--migrate`                     | Có chỗ chứa báo cáo (ENT-12) và nhật ký kiểm toán (ENT-13); và giao luật **"audit không sửa, không xóa được"** cho Postgres giữ — không phải cho quy ước code                                                                                                                      | `Report`, `AuditLog` trong `Moderation.Domain`; migration `InitialModeration` sinh **đúng từng CHECK và index một phần** của Mục 4; trigger chặn `UPDATE`, `DELETE`, **`TRUNCATE`**; `AUD-02` xanh; `--migrate` hai lần trên DB sạch: lần hai không đổi gì, `\dn` thấy `moderation`; `Moderation_Domain_namespace_must_not_be_empty` xanh                                   |
| **A2**  | Module Notification: entity, `NotificationDbContext`, migration đầu, `NotificationTypes`, `GroupKey`, nối `--migrate`     | Có chỗ chứa thông báo đã gộp (ENT-09) với đúng ràng buộc mà upsert của D9 dựa vào (`uq_notifications_group`), và **một** chỗ duy nhất sinh `group_key` cho tám loại (Đ-6.17)                                                                                                     | `Notification`, `NotificationActor` trong `Notification.Domain`; migration `InitialNotification` khớp DDL (UQ, hai index, FK cascade **trong** schema, CHECK `type` sinh từ `NotificationTypes.All`); unit test `GroupKey` cho **tám** loại xanh; `\dn` thấy `notification`; `Notification_Domain_namespace_must_not_be_empty` xanh                                          |
| **A3**  | Identity: mã quyền 18 `role.manage`, mô tả 18 quyền, trigger vai trò hệ thống, sequence cho vai trò tự tạo                | Trả ba món nợ GĐ1 Mục 2 ở tầng dữ liệu: mã quyền "định nghĩa vai trò" tách khỏi "gán vai trò" (Đ-6.9), mô tả quyền cho màn ma trận, và **lớp chặn thứ ba** cho ADMIN/USER/MODERATOR — chặn cả `psql` gõ tay                                                                        | `PermissionCodes.All` có 18 mã; mọi DB (mới **và** đã seed từ GĐ1) có `description` cho đủ 18 dòng; `UPDATE roles SET code` / `DELETE` trên ba vai trò hệ thống bị Postgres từ chối, `UPDATE display_name` thì được (`ROLE-05`); `ROLE-07` + `SEED-01/02/03`, `FK-01` xanh (ba test GĐ1 **sửa trong cùng commit**, Mục 5)                                          |
| **A4**  | Profile: extension `unaccent` + `pg_trgm`, hàm `IMMUTABLE` `profile.search_norm`, index GIN                               | Để tìm "nguyen" ra "Nguyễn …" mà **không** Seq Scan cả bảng hồ sơ (FR-017, p95 ≤ 700 ms) — bẫy `unaccent` là `STABLE` phải gỡ ở tầng DB, trước khi D12 viết truy vấn                                                                                                               | Migration `AddDisplayNameSearch` đúng khối SQL Đ-6.19; `\dx` có hai extension; `\df+ profile.search_norm` là `immutable`; `EXPLAIN (ANALYZE, BUFFERS)` trên **20.000** hồ sơ tên Việt có `Bitmap Index Scan on idx_profiles_display_name_search` cho ba loại `q` — kết quả dán vào "Thực tế thi công"                                                             |
| **A5**  | Hằng quyền cục bộ `ModerationPermissions` + `ModerationPermissionsTests`                                                  | Moderation dùng bốn mã quyền mà không `using` Identity (luật 4, `ModuleBoundaryTests`) — và lỗi gõ sai mã, thứ Admin vẫn qua nên kiểm tay không thấy, bị **CI** bắt                                                                                                               | `ModerationPermissions` (`report.create`, `report.resolve`, `post.hide`, `audit.read`) + `All`; hai ca của `ModerationPermissionsTests` xanh; đổi tạm thành `"report.reslove"` → đỏ đúng mã đó                                                                                                                                                                             |

**Kết quả khối A:** hai schema mới migrate được; `audit_logs` và ba vai trò hệ thống được DB bảo vệ; index tìm kiếm tồn tại và
được planner dùng.

### 0.2 Khối C — Hạ tầng chéo module

> **Mục tiêu khối:** những thứ **mọi** endpoint GĐ6 đứng lên trên: ghi audit cùng số phận với thao tác, kiểm duyệt ghi hộ đúng
> luật, quyền đổi là thấy ngay, endpoint đặc quyền không bao giờ fail-open.

| Mã     | Đầu việc                                                                                                                                  | Mục tiêu — việc này tồn tại để làm gì                                                                                                                                                                                                                                                              | Kết quả mong đợi — thứ kiểm chứng được                                                                                                                                                                                                                                                                                                                                   |
| ------ | ----------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------ |
| **C1** | `IAuditTrail` + `AuditEntry` + `AuditActions` (SharedKernel) · `SqlAuditTrail` (Moderation)                                               | Cho Identity và Moderation ghi audit **trong chính transaction của thao tác** (Đ-6.3) — thao tác rollback thì audit rollback theo, thao tác commit thì audit chắc chắn có. Đây là nền của mốc 3                                                                                                      | `AppendAsync(tx, entry, ct)` chạy `INSERT` tham số hóa trên **`tx.Connection`**; `tx == null` ghi trên kết nối riêng; `ip` lấy sau `ForwardedHeaders`; test "thao tác Identity + audit, ném sau audit → **cả hai** rollback" xanh; đột biến "mở kết nối riêng dù có `tx`" → test đó **đỏ**                                                                                |
| **C3** | `IPermissionCache.Invalidate/InvalidateAll` · `IsAllowedAsync` dùng chung · `IPermissionChangeNotifier` + subscriber pub/sub              | Sửa quyền của một vai trò có hiệu lực **ở request kế tiếp**, trên **mọi** instance — không phải sau 60 giây trông như bug (Đ-6.10); và gói "Admin short-circuit + tra cache" vào **một** hàm để tầng 2 kép của D7 không tự so `"ADMIN"` (Mục 6.2)                                                  | `PermissionHandler` gọi `IsAllowedAsync`, năm ca `PermissionHandlerTests` vẫn xanh; `PERM-01` (bản hạ tầng: sửa bảng + notify → request kế tiếp 403, không tua đồng hồ) và `PERM-02` (hai factory chung Redis, ≤ 1 giây) xanh; Redis rớt rồi hồi → instance gọi `InvalidateAll`                                                                                          |
| **C4** | `[PrivilegedEndpoint]` · `ITokenRevocationStore.CheckAsync` · fail-closed có chọn lọc · `AuditingAuthorizationResultHandler` · `[RequireAnyPermission]` | Endpoint quản trị/kiểm duyệt **không bao giờ** fail-open khi Redis chết (Đ-6.8 C1), mọi lần tầng 2 từ chối ở đó để lại **một** dòng `access.denied` (US-019 AC-03, Đ-6.15), và một attribute gánh cả hai việc để không ai gắn thiếu một nửa                                                 | `FC-01` (probe đặc quyền → 503 `revocation-unavailable`; `/feed` → 200) và `AUD-03` (5 lần bị từ chối/phút → **đúng 1** dòng) xanh; `PermissionCodeUsageTests` đọc cả `[RequireAnyPermission]`; `Privileged_controllers_carry_the_attribute` có mặt (canh gác chân không có địa chỉ gỡ ở D2)                                                                            |
| **C5** | `IAccountStatusReader.GetInactiveAsync(ids)` (SharedKernel) + hiện thực ở Identity                                                         | Cho Profile (tìm kiếm D12, ảnh chụp người dùng C2) biết tài khoản nào bị khóa **mà không đọc bảng của Identity** — hợp đồng đọc, batch, đúng Đ-2.3                                                                                                                                                 | Một câu `SELECT … WHERE user_id = ANY(@ids) AND status <> 'active'`; test tích hợp: 3 id (active, disabled, không tồn tại) → chỉ id `disabled` trả về, **một** câu SQL cho cả lô (`SqlCommandCounter`)                                                                                                                                                                  |
| **C2** | `IModerationTargets` + `ModerationTarget`/`TargetSnapshot`/`HideOutcome` (SharedKernel) · provider bài (Content) · provider người dùng (Profile) · composite | Cho Moderation **ẩn bài trong transaction của mình** mà Content vẫn là module **duy nhất** viết SQL vào `content.posts` (Đ-6.3); và cho D6 luật "thấy được mới báo được" (Đ-6.12) mà không nhân bản BR-02                                                                                          | `HideAsync` = một `UPDATE … WHERE status='published' RETURNING` trên `tx.Connection`, phân biệt `Hidden/AlreadyHidden/NotFound`; `TX-02` (bản hạ tầng) và phần store của `HID-*` xanh; `WriteContracts_are_only_the_two_named` xanh và **đã từng đỏ** khi thêm tạm một interface nhận `DbTransaction` ở Content                                                           |
| **C6** | `NotificationHub` + `NotificationPusher` + hợp đồng hub *(chờ B merge vé)*                                                                | Đẩy thông báo tới tab đang mở ngay khi upsert commit (Đ-6.18) — **dùng lại** vé, filter thu hồi, `IUserIdProvider` của B, không viết lại gì                                                                                                                                                      | `/hubs/notifications` map cạnh `/hubs/chat`; không vé → 401; vé của A chỉ nhận thông báo của A (khuôn `HUB-09` của B); `NotificationHubContractTests` xanh. **Trước khi B merge: không làm** — FE hỏi lại 30 giây là đường chính (Đ-6.18)                                                                                                                                  |

**Kết quả khối C:** mọi thứ Mục 6 đòi ở tầng hạ tầng đã có test; D chỉ còn nghiệp vụ.

> Thứ tự trong bảng C là **thứ tự làm** (C1 → C3 → C4 → C5 → C2 → C6), không phải thứ tự mã — lý do ở Mục 0.3 và lệch L-C8.

### 0.3 Thứ tự thực thi

```
 [0] ─→ A1 ─→ A3 ─→ C1 ─→ C3 ─→ C4          ← đường găng B.10 (A1 → A3 → C1 → C4)
         │      │                  │
         ├→ A5  ├→ A2 ─┐           └→ D1–D5 (bước 5)
         │      └→ A4 ─┤
         │             └──────→ C5 ─→ C2 ─→ D6–D7 (bước 6)
         └─ (C6 đợi B merge vé — bước 9)
```

- **`A1` trước tiên, `A3` ngay sau:** hai migration mà C1 (ghi `audit_logs`) và D3–D5 (vai trò) đứng lên. `A2`, `A4`, `A5` không
  nằm trên đường găng — làm xen khi chờ build/test.
- **`C1 → C3 → C4`:** C4 gọi `IAuditTrail` (C1) cho `access.denied` và handler `[RequireAnyPermission]` gọi `IsAllowedAsync` (C3).
- **`C5 → C2`:** ảnh chụp người dùng của C2 cần trạng thái `active|disabled`, mà Profile không biết — đọc qua C5 (L-C8).
- **`C6` tách hẳn:** phụ thuộc `SharedKernel/Realtime/` của B (chưa có trên `develop` ngày 2026-09-23 — `grep -r SignalR src/` rỗng).
  Không dựng tạm vé riêng, không sửa thư mục của B (Mục 9.4).

**Mỗi mốc mở khóa việc gì:**

| Mốc             | Mở khóa                                                                                                         |
| --------------- | --------------------------------------------------------------------------------------------------------------- |
| A1 + A3         | C1 (bảng audit), D3–D5 (vai trò, sequence id), B1 (helper dựng cảnh trên schema thật)                           |
| A2              | D9 (upsert gộp), D10 (handler)                                                                                  |
| A4              | D12 (truy vấn chỉ việc dùng đúng biểu thức `profile.search_norm(display_name)`)                                  |
| C1 + C4         | D1–D5 viết được controller admin đầy đủ tầng 2 + audit; B2 viết matrix `TC-A05*` cho đỏ trước                    |
| C3              | D5 gọi một dòng `notifier.NotifyAsync(roleCode)` sau `COMMIT`                                                    |
| C2 + C5         | D6 (`CanViewAsync`), D7 (`HideAsync` trong transaction Đ-6.13), D12 (lọc tài khoản bị khóa)                      |

### 0.4 Hai mươi chỗ lệch B.4/B.5 — đã chốt 2026-09-23

Đọc B.4, B.5, Mục 4 đối chiếu với code ngày 2026-09-23 (`60ac7ee`) thì thấy các chỗ dưới đây viết chưa đủ, tự mâu thuẫn, hoặc
**không chạy được** trên code hiện tại.

**Trạng thái (chốt 2026-09-23, người thi công):** cả hai mươi chỗ đi **theo cột "Đề xuất"**.
- Bảy chỗ là **lựa chọn thiết kế**, đã cân nhắc phương án khác rồi loại: L-A2 (mô tả đi hai đường, seeder có sửa — thay vì migration
  upsert đủ 18 dòng), L-A4 (sequence từ 100 — thay vì để D5 tự sinh id), L-A5 (`AuditActions` ở SharedKernel — thay vì bản sao hằng
  ở từng module), L-A6 (chặn `TRUNCATE`), L-A10 (script seed tìm kiếm riêng — thay vì mở rộng seed feed), L-C4 (một hàm
  `NotifyAsync` — thay vì hai lời gọi), L-C9 (composite ở SharedKernel — thay vì trong host).
- Mười ba chỗ còn lại là **ràng buộc kỹ thuật** (code hiện tại không cho làm khác) hoặc sắp xếp thứ tự — không có phương án thay thế
  để chọn.

Chốt rồi nhưng `giai-doan-6.md` **chưa** sửa: mỗi chỗ sửa B.4/B.5, Mục 4, Mục 10 **trong cùng commit** của đầu việc chạm nó, ghi
"sửa 2026-09-23 khi thi công …" — cùng nếp C0. Thi công mà phải đổi hướng chỗ nào thì ghi vào "Thực tế thi công", có ngày và lý do.

**Khối A**

| #    | Kế hoạch viết                                                                                  | Đề xuất                                                                                                                                                                  | Vì sao                                                                                                                                                                                                                                                                                                                                       |
| ---- | ---------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------------------------------ | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| L-A1 | A3: "`ADD COLUMN description`"; "thêm `description` vào entity `Permission` + configuration"   | **Bỏ** cả hai. Chỉ `UPDATE`                                                                                                                                              | Cột **đã có từ GĐ1**: `InitialIdentity.cs:28` (`description varchar(120) NULL`), `Permission.Description`, `PermissionConfiguration`. `ADD COLUMN` chạy là `42701 column already exists` — migration đỏ trên mọi DB                                                                                                                       |
| L-A2 | A3: "seeder **không** sửa"; mô tả đặt bằng migration `UPDATE`                                  | Mô tả đi **hai đường**: seeder chèn `(id, code, description)` với `DO NOTHING` (hằng `PermissionCodes.Descriptions`); migration `UPDATE` bằng chữ cho DB đã seed từ GĐ1  | `MigrateIdentityModuleAsync` chạy **migrate → seed**. DB mới (CI, test, máy mới) lúc migration chạy **chưa có dòng nào** → `UPDATE` chạm 0 dòng → 18 mô tả NULL vĩnh viễn. Trên staging, dòng 18 `role.manage` do seeder chèn **sau** migration → cũng NULL. `DO NOTHING` vẫn giữ luật GĐ1 Mục 5.4: không bao giờ ghi đè dòng đã có          |
| L-A3 | A3 xong khi "`SEED-01`, `SEED-02` vẫn xanh"                                                     | Sửa **bốn** test GĐ1 trong cùng commit A3 (Mục 5 bước 6)                                                                                                                 | Không thể "vẫn xanh": `ExpectedPermissions` liệt kê 17 dòng (`IdentitySeederTests.cs:36`); `PermissionCodes_doc_duoc_du_17_ma` khóa `17`; **`SEED_03`** đổi `code` ADMIN→ROOT bằng SQL — trigger mới chặn ngay câu `UPDATE`; **`FK_01`** xóa `role_id = 1` (USER) để lấy `23503` — trigger `BEFORE DELETE` ném `P0001` trước khi FK kịp kiểm |
| L-A4 | A3 không nói vai trò tự tạo lấy `role_id` ở đâu                                                | Migration A3 tạo `identity.roles_role_id_seq AS smallint START 100` (khai `HasSequence` để snapshot biết); D5 gọi `nextval`                                              | `role_id smallint ValueGeneratedNever()` (`RoleConfiguration`), seeder gán tay 1/2/3. `POST /admin/roles` (D5) không có cách sinh id: `max()+1` đua nhau dưới đồng thời. Bắt đầu từ 100 để id của vai trò hệ thống (và vai trò hệ thống thêm sau này nếu có) không bao giờ đụng                                                              |
| L-A5 | Đ-6.15, A1: `AuditActions` ở `Moderation.Domain`                                               | `AuditActions` ở **`SharedKernel/Audit/`**, cạnh `IAuditTrail` (tạo ở A1, C1 dùng)                                                                                       | Người **ghi** audit là Identity (`user.lock`, `role.*` — D3–D5) và **SharedKernel** (`access.denied` — handler C4). Cả hai không được tham chiếu Moderation (ADR-001, `ModuleBoundaryTests`). Để ở Moderation thì hoặc gõ chuỗi tự do ở ba nơi, hoặc ba bản sao hằng + ba test đối chiếu                                                           |
| L-A6 | Mục 4: trigger `BEFORE UPDATE OR DELETE … FOR EACH ROW`                                         | Thêm `BEFORE TRUNCATE … FOR EACH STATEMENT` cùng hàm                                                                                                                     | Trigger mức dòng **không** chạy khi `TRUNCATE`. Một lệnh dọn DB dev chép nhầm sang staging xóa sạch nhật ký mà trigger không hề biết — đúng loại "một dòng lẫn trong PR" mà Đ-6.15 muốn chặn                                                                                                                                                     |
| L-A7 | B1 (khối B) thêm Moderation, Notification vào thứ tự migrate của `PostgresFixture`, `ModulesApiFactory` | Dòng `Add<X>Module` + `Migrate<X>ModuleAsync` vào `Program.cs` **và** hai harness đi **cùng commit A1/A2**; B1 chỉ còn helper dựng cảnh                                   | Nếp GĐ4 A3 ("nối `Program.cs` + harness test" là một đầu việc). Tách ra thì giữa A1 và B1 có một khoảng `--migrate` không tạo schema `moderation`, và C1 không có DB test nào có bảng audit để chạy                                                                                                                                              |
| L-A8 | A5: "hai test namespace không rỗng"                                                             | Mỗi test đi cùng commit **entity đầu tiên** của module đó (A1, A2); A5 còn `ModerationPermissions`                                                                        | Nếp GĐ2 A7, GĐ4 A1: *"test đi cùng commit entity đầu tiên để không thành nợ"*. Để tới A5 thì A1, A2 commit trong lúc rule persistence chạy trong chân không trên hai module mới                                                                                                                                                                  |
| L-A9 | Mục 10.3: "`GroupKey.For(event)` cho mọi loại"                                                  | `GroupKey` là **một hàm cho mỗi loại** (`GroupKey.Comment(postId)`, `.Reply(parentCommentId)`, `.Reaction(kind, id)`…); không hàm nào nhận event                          | Một `CommentCreated` sinh tới **ba** nhóm (`comment` cho tác giả bài, `reply` cho tác giả bình luận cha, `tag` cho người được nhắc) — `For(event)` không có một giá trị trả về đúng. Chọn nhóm là việc của handler D10, dựng chuỗi là việc của `GroupKey`                                                                                            |
| L-A10 | A4 xong khi "`EXPLAIN` trên 20.000 hồ sơ giả"; không nói dữ liệu ở đâu                          | Script `tests/load/search/seed-profiles.sql` (tên Việt có dấu, chặn theo tên DB như `tests/load/feed/seed.sql`), chạy trên DB **vứt được**; k6 của 10.6 dùng lại            | Seed feed hiện có chỉ 10.000 hồ sơ tên `Perf User N` — không dấu, không họ, không kiểm được "đ" hay tiền tố từ thứ hai. Bảng vài chục dòng trong integration test thì planner **luôn** chọn Seq Scan — không chứng minh được gì                                                                                                                      |

**Khối C**

| #    | Kế hoạch viết                                                                                                 | Đề xuất                                                                                                                                                                                  | Vì sao                                                                                                                                                                                                                                                                                                                             |
| ---- | ------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| L-C1 | C1: "`tx == null` → mở kết nối riêng từ `NpgsqlDataSource`; lấy IP qua `IHttpContextAccessor`"                | `tx == null` → kết nối của **`ModerationDbContext`** (scoped, cùng chuỗi `postgres`, cùng pool); `AddHttpContextAccessor()` trong `AddModerationModule`                                   | Repo **không** đăng ký `NpgsqlDataSource` nào (`grep -r NpgsqlDataSource src/` rỗng) và chưa có `IHttpContextAccessor`. Dựng data source thứ hai là **pool thứ hai** — đúng thứ PERF-03 GĐ4 đã gỡ ("một biến cho bốn module + health check → một pool")                                                                                 |
| L-C2 | "Xong khi" của C1/C2/C3/C5 nêu `TX-01`, `AUD-01`, `TX-02`, `HID-*`, `PERM-01`, `SRCH-05`                      | Khối C viết **bản hạ tầng** của các ca đó (gọi thẳng hợp đồng, không qua endpoint); khối D mở rộng thành bản đầy đủ qua API. Bảng phân chia ở Mục 16                                    | Bản đầy đủ của cả sáu ca cần endpoint chưa tồn tại (`PATCH /reports` — D7, `PUT /admin/roles/…` — D5, `GET /search` — D12). "Xong khi" của C phải đạt được **trong** khối C, không phải "xanh sau này"                                                                                                                                 |
| L-C3 | Mục 6.2: `IsAllowedAsync` là hàm SharedKernel duy nhất gói Admin short-circuit — **không đầu việc nào nhận nó** | Làm ở **C3** (cùng file cache); `PermissionHandler` gọi nó                                                                                                                              | Chỉ B.1 nhắc tên. Không có chủ thì D7 (tầng 2 kép cho `hide`) viết `if (role == "ADMIN")` ngay trong service — đúng dòng GĐ1 Mục 3.2 cấm                                                                                                                                                                                          |
| L-C4 | C3: `PermissionsChangedPublisher` (sau `COMMIT`) — D5 gọi `Invalidate` tại chỗ **rồi** publish                 | Một `IPermissionChangeNotifier.NotifyAsync(roleCode)` làm cả hai việc; D5 chỉ gọi một dòng                                                                                               | Hai lời gọi thì một ngày có người gọi một: quên `Invalidate` → chính instance xử lý request vẫn cũ 60 giây (`PERM-01` bắt); quên publish → **instance khác** cũ 60 giây, và không test một-instance nào bắt được                                                                                                                  |
| L-C5 | C3 không nói về tranh chấp "đang nạp" với "vừa xóa"                                                           | `PermissionCache` giữ **thế hệ** theo vai trò; lần nạp bắt đầu trước `Invalidate` không được ghi đè entry; subscriber gọi `InvalidateAll()` khi Redis **nối lại**                          | (1) Request R đọc DB lúc T0 (quyền cũ), Admin commit + `Invalidate` lúc T1, R ghi entry lúc T2 → cache cũ 60 giây dù đã invalidate. (2) Tin pub/sub phát lúc Redis rớt thì **mất hẳn** — Redis không lưu tin cho subscriber vắng mặt                                                                                               |
| L-C6 | C4: `OnTokenValidated` có metadata + không kiểm được thu hồi → "503"                                           | `OnTokenValidated` chỉ `ctx.Fail()` + đặt dấu `HttpContext.Items`; `AuditingAuthorizationResultHandler` thấy `Challenged` + dấu → ghi **503** Problem Details                              | `TokenValidatedContext` không trả được status nào khác 401: `Fail()` → middleware authorization challenge → 401. Ghi thẳng response trong `OnTokenValidated` thì challenge sau đó ném "response đã bắt đầu"                                                                                                                         |
| L-C7 | C4: thử cho đỏ "bỏ attribute khỏi một controller → test reflection đỏ"                                         | C4 viết test + canh gác "có ít nhất một controller nhóm `admin-v1`/`moderation-v1`" **mang `Skip` có địa chỉ gỡ ở D2**; thử cho đỏ làm ở D2. `FC-01`, `AUD-03` chạy trên **probe** controller của test | Ngày C4 chưa có controller quản trị nào — test reflection xanh trong chân không, và không có gì để bỏ attribute. Probe là khuôn có sẵn (`AuthZProbeController`), không vào image                                                                                                                                                  |
| L-C8 | B.5 xếp C2 trước C5                                                                                            | **C5 trước C2**                                                                                                                                                                         | `TargetSnapshot.status` của đối tượng `user` là `active\|disabled` (Mục 8.1) — Profile không có cột đó; `ProfileModerationTargets` đọc qua `IAccountStatusReader`                                                                                                                                                                   |
| L-C9 | C2: "`CompositeModerationTargets` … đăng ký ở host"                                                            | Composite `ModerationTargets` sống ở `SharedKernel/Moderation/`, nhận `IEnumerable<IModerationTargetProvider>`; mỗi module đăng ký provider của mình trong `Add<X>Module`                   | Composite ở `Program.cs` là logic nằm trong host, và host phải biết kiểu `internal` của từng module. Provider + composite ở SharedKernel thì thêm bình luận (sau A merge) là **một dòng** trong `AddContentModule`, không đụng host                                                                                                |
| L-C10 | C3, C4 "chỉ-thêm" vào `IPermissionCache`, `ITokenRevocationStore`                                             | Sửa **ba fake** trong UnitTests cùng commit (`FixedCache`, `ThrowingCache` — `PermissionHandlerTests`; `FakeRevocation` — `SessionServiceTests`)                                        | Chỉ-thêm với **người gọi**, không chỉ-thêm với **người hiện thực**: thêm thành viên vào interface là ba lỗi compile `CS0535` ở project test (impact Mục 1.2)                                                                                                                                                                     |

---

## 1. Trước khi gõ dòng đầu tiên

### 1.1 Năm điều kiện cần

```bash
# 1. Postgres + Redis dev đang chạy (compose dev) — A1–A4 chạy `--migrate` thật, C3/C4 cần Redis thật
# 2. Docker daemon — IntegrationTests dùng Testcontainers (postgres:16-alpine có sẵn contrib: unaccent, pg_trgm)
# 3. dotnet-ef khớp EF 8.0.10
dotnet tool list -g | grep dotnet-ef
# 4. Solution build sạch từ điểm xuất phát
dotnet build SocialApp.sln
# 5. Nhánh: loveart1210 đã có C0; ba yaml của cổng mở (Mục 9.2 bước 3) KHÔNG nằm dưới commit nào của A, C nếu định mở PR sớm
git log --oneline -3
```

Ghi **số test trước** (Unit, Integration, Architecture) — mỗi commit cần dòng `Test: … → …`. Máy dev có **một đỏ nền**
(`StartupConfigurationTests.Development_boots_without_r2_config_and_first_use_names_the_variables` — user-secrets có khóa R2; CI xanh),
không tính là lỗi của khối này.

### 1.2 Impact analysis trước khi sửa symbol có sẵn

Chạy lại **ngay trước** đầu việc chạm symbol đó — index có thể đã cũ:

```bash
node .gitnexus/run.cjs impact "<Symbol>" --direction upstream --repo .
```

Kết quả chạy ngày 2026-09-23:

| Symbol                         | Đầu việc | Risk        | Ghi chú                                                                                                                                                                                                                                              |
| ------------------------------ | -------- | ----------- | ---------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
| `IdentitySeeder`               | A3       | **LOW**     | 15 nút: `IdentityModuleExtensions` (gọi trực tiếp), qua đó `Program.cs`, `PostgresFixture`, `ModulesApiFactory`, `IdentityApiFactory`, `IdentitySeederTests`. Mọi DB test đi qua seeder — sai một câu SQL là **cả bộ integration** đỏ, không phải một test |
| `PermissionCodes`              | A3       | **UNKNOWN** | Index không thấy người đọc hằng. Text search: `IdentitySeeder` (đọc `All`), `PermissionCodeUsageTests`, `ContentPermissionsTests`, `SocialGraphPermissionsTests`. Thêm **cuối** `All` không đổi id 1..17 nào                                           |
| `IPermissionCache`             | C3       | **LOW**     | 5 nút: `PermissionCache` + hai fake `FixedCache`, `ThrowingCache` + ca test của chúng. Thêm thành viên → hai fake đỏ compile (L-C10)                                                                                                                  |
| `PermissionCache`              | C3       | **UNKNOWN** | Đăng ký qua DI (`AddSharedKernelAuthorization`) nên index không có cạnh. Text search: chỉ `AuthorizationExtensions`, `PermissionCacheTests`                                                                                                           |
| `PermissionHandler`            | C3       | **MEDIUM**  | 5 nút, cả năm là ca của `PermissionHandlerTests` (Admin không chạm cache, Moderator qua, thiếu claim, USER thiếu quyền không `Fail`, `admin` chữ thường không short-circuit). Đổi thân sang `IsAllowedAsync` phải giữ **nguyên** năm hành vi đó       |
| `PermissionPolicyProvider`     | C4       | **LOW**     | 3 nút — `PermissionPolicyProviderTests`. Nhánh `perm-any:` thêm **trước** nhánh rơi về mặc định                                                                                                                                                      |
| `ITokenRevocationStore`        | C4       | **LOW**     | `RedisTokenRevocationStore` + `FakeRevocation` (`SessionServiceTests`) → fake đỏ compile (L-C10). Người gọi `IsRevokedAsync`: `OnTokenValidated` (Program.cs:376) — **và filter hub của B khi B merge**: giữ nguyên hành vi                               |
| `AddSharedKernelAuthorization` | C3, C4   | **UNKNOWN** | Text search: đúng một chỗ gọi — `Program.cs:383`. Mọi host test đi qua `Program`                                                                                                                                                                      |

Không cái nào HIGH/CRITICAL. `UNKNOWN` **không** được đọc là "an toàn" — đã xác nhận bằng text search ở trên. `PermissionHandler`
là MEDIUM vì nằm trên đường của **mọi** `[RequirePermission]`: nó sai thì cả AuthZ matrix đỏ.

### 1.3 Tám luật áp thẳng vào hai khối

1. **Luật DB viết bằng `migrationBuilder.Sql`, kèm comment trỏ quyết định** (Mục 4). Trigger, hàm, extension, index biểu thức,
   sequence — EF không sinh; `dotnet ef migrations add` ra `Up()` rỗng hoặc thiếu là **đúng**, rồi viết tay.
2. **CHECK dựng từ hằng C#**, không gõ tay chuỗi — khuôn `UserConfiguration.StatusCheckSql` dựng từ `UserStatus.All`. Hằng và
   ràng buộc không lệch nhau được.
3. **SQL thô luôn ghi rõ schema.** `HasDefaultSchema` chỉ tác động LINQ; SQL thô đi với `search_path` mặc định (`public`).
4. **Không FK sang schema khác** (Đ-2.2). `reporter_id`, `actor_id`, `recipient_id`, `target_id` là `uuid` trần. FK **trong**
   schema thì giữ (`notification_actors → notifications`).
5. **Hai hợp đồng ghi, đúng hai** (Đ-6.3). Không phương thức nào khác trong repo nhận `DbTransaction` — test C2 canh.
6. **Hiện thực hợp đồng ghi nằm ở `Infrastructure/`**, không `Application/`: nó chạm Npgsql, và `PersistenceBoundaryTests` cấm
   Npgsql ở `Domain|Application`.
7. **Thêm thành viên vào interface có sẵn thì sửa mọi fake cùng commit** (L-C10) — "chỉ-thêm" là với người gọi.
8. **Thêm một cổng thì thử cho đỏ một lần**, `git status` sạch trước và sau (nếp repo).

---

## 2. [0] — Gói EF cho hai module mới

**Mục tiêu:** A1, A2 biên dịch được.

**Kết quả mong đợi:** hai csproj có bốn `PackageReference` giống hệt SocialGraph; build xanh.

### Các bước

1. Chép nguyên khối `<ItemGroup>` gói của `src/backend/Modules/SocialGraph/SocialApp.Modules.SocialGraph.csproj` (FluentValidation 11.3.0,
   EF Core 8.0.10, EF Design 8.0.10 `PrivateAssets=all`, Npgsql EF 8.0.10) vào `…Moderation.csproj` và `…Notification.csproj`,
   **giữ comment** — người đọc sau cần biết vì sao `.Design` có `PrivateAssets`.
2. Thư mục: ba thư mục `Application/`, `Domain/`, `Infrastructure/` đã có (`.gitkeep`); tạo `DependencyInjection/`. **Chưa** tạo
   `Presentation/` — D0 tạo khi có controller đầu tiên.
3. `dotnet build SocialApp.sln`.
4. Chạy impact Mục 1.2 cho `IdentitySeeder`, `PermissionCodes` (A3).

Việc này đi **chung commit A1** (với Moderation) và A2 (với Notification) — một csproj không có entity nào thì không có lý do
đứng riêng một commit.

---

## 3. A1 — Module Moderation

**Mục tiêu:** bảng báo cáo và bảng audit có mặt, đúng từng ràng buộc của Mục 4, và audit do DB giữ.

**Kết quả mong đợi:** migration `InitialModeration` đã commit; `AUD-02` xanh; `--migrate` tạo schema `moderation`; bộ integration
cũ vẫn xanh.

### Các bước

**1. Domain** (`Modules/Moderation/Domain/`, POCO, không EF):

```
Report.cs           Id (Uuid7), ReporterId, TargetType, TargetId, ReasonCode, Detail?, Status, ResolverId?, ResolvedAt?,
                    ResolutionNote?, CreatedAt, UpdatedAt
AuditLog.cs         Id (long), ActorId, Action, TargetType?, TargetId?, Metadata (string? — jsonb), Ip (IPAddress?), CreatedAt
                    MỌI thuộc tính `init` — không setter nào: không ai "sửa một dòng audit" được bằng C#
ReportStatus.cs     const "open" | "resolved" | "dismissed" + All
ReasonCodes.cs      const "spam" | "harassment" | "nudity" | "violence" | "other" + All       (Đ-6.12 — CHECK, không bảng)
ReportTargetTypes.cs const "post" | "comment" | "user" + All + From(ModerationTargetType)     (SharedKernel enum → chuỗi DB)
ReportDecision.cs   const "hide" | "dismiss" | "resolve" + All
```

Dùng **`const string`** như `UserStatus`, không enum + converter: cột là `varchar` có CHECK, và các chuỗi này đi thẳng vào hợp
đồng API (`reasonCode`, `decision`).

**2. `AuditActions` ở SharedKernel** (L-A5) — `SharedKernel/Audit/AuditActions.cs`, đúng bảng Đ-6.15:

```csharp
public static class AuditActions
{
    public const string ReportHide = "report.hide";        // … report.dismiss, report.resolve, content.restore
    public const string UserLock = "user.lock";            // … user.unlock, role.assign
    public const string RoleCreate = "role.create";        // … role.rename, role.permissions, role.delete
    public const string AccessDenied = "access.denied";
    public static readonly string[] All = [ … 12 mã … ];
}
```

Unit test `AuditActionsTests`: `All` không trùng chuỗi; `All` liệt kê đủ mọi hằng (khuôn `All_liet_ke_du_moi_hang_cua_ContentPermissions`);
mọi mã ≤ 50 ký tự (`varchar(50)`).

**3. Infrastructure** — chép khuôn SocialGraph, đổi tên:

- `ModerationDbContext` (`Schema = "moderation"`, `HasDefaultSchema`, `ApplyConfigurationsFromAssembly`, `StampUpdatedAt` y như cũ —
  vòng lặp đã bỏ qua entity không có `UpdatedAt`, nên `AuditLog` **tự** được bỏ qua; **không** thêm nhánh riêng).
- `ModerationDbContextOptions.UseModerationNpgsql` — một chỗ cho DI + design-time.
- `DesignTimeModerationDbContextFactory` — comment ghi lệnh `dotnet ef` với `--project`/`--startup-project` là chính csproj Moderation.
- `Configurations/ReportConfiguration.cs`:
  - bốn CHECK dựng từ `ReportTargetTypes.All`, `ReasonCodes.All`, `ReportStatus.All` + hai CHECK logic
    `ck_reports_other_detail`, `ck_reports_decided` — **nguyên văn** Mục 4;
  - `HasIndex(reporter_id, target_type, target_id).IsUnique().HasFilter("status = 'open'").HasDatabaseName("uq_reports_open_per_reporter")`;
  - `idx_reports_open_queue (created_at, id) WHERE status = 'open'`; `idx_reports_target (target_type, target_id, status)`.
- `Configurations/AuditLogConfiguration.cs`: `id` → `UseIdentityByDefaultColumn()` (xem cạm bẫy 4), `metadata` `HasColumnType("jsonb")`,
  `ip` kiểu `inet` (Npgsql ánh xạ `IPAddress` sẵn), `created_at DEFAULT now()`; hai index `(actor_id, id DESC)`, `(target_type, target_id, id DESC)`
  bằng `IsDescending(false, true)` / `(false, false, true)`.

**4. Migration:**

```bash
dotnet ef migrations add InitialModeration \
  --project src/backend/Modules/Moderation/SocialApp.Modules.Moderation.csproj \
  --startup-project src/backend/Modules/Moderation/SocialApp.Modules.Moderation.csproj \
  --output-dir Infrastructure/Migrations
```

Rồi **viết tay** cuối `Up()` (L-A6, Đ-6.15):

```csharp
// Đ-6.15: audit_logs append-only do DB giữ. Cửa duy nhất: DELETE khi phiên đặt SET LOCAL socialapp.audit_purge = 'on'
// (job xóa 12 tháng của GĐ8). Không bao giờ có cửa cho UPDATE. TRUNCATE chặn riêng — trigger mức dòng không thấy nó.
migrationBuilder.Sql("""
    CREATE FUNCTION moderation.audit_logs_append_only() RETURNS trigger LANGUAGE plpgsql AS $$
    BEGIN
        IF TG_OP = 'DELETE' AND current_setting('socialapp.audit_purge', true) = 'on' THEN
            RETURN OLD;
        END IF;
        RAISE EXCEPTION 'moderation.audit_logs là append-only (%)', TG_OP USING ERRCODE = 'P0001';
    END $$;
    CREATE TRIGGER trg_audit_logs_append_only BEFORE UPDATE OR DELETE ON moderation.audit_logs
        FOR EACH ROW EXECUTE FUNCTION moderation.audit_logs_append_only();
    CREATE TRIGGER trg_audit_logs_no_truncate BEFORE TRUNCATE ON moderation.audit_logs
        FOR EACH STATEMENT EXECUTE FUNCTION moderation.audit_logs_append_only();
    """);
```

`Down()`: `DROP TRIGGER` ×2, `DROP FUNCTION` **trước** các `DropTable` mà EF sinh.

**Đọc lại DDL sinh ra** (Mục 4 "năm chỗ dễ sai"): mỗi CHECK đúng chữ; index một phần có đúng `filter`; không có FK nào.

**5. DI + migrate** — `DependencyInjection/ModerationModuleExtensions.cs`: `AddModerationModule(services, connectionString)`
(`AddDbContext` + `TryAddSingleton(TimeProvider.System)` + validator của module), `MigrateModerationModuleAsync` (chỉ migrate,
**không** try/catch), `public const string Schema`.

**6. Nối host và harness** (L-A7) — chỉ **thêm dòng**, không sắp lại khối có sẵn (Mục 9.4):

| File                                                     | Thêm                                                                                                                        |
| -------------------------------------------------------- | --------------------------------------------------------------------------------------------------------------------------- |
| `Program.cs` (sau `AddSocialGraphModule`)                | `builder.Services.AddModerationModule(postgres);`                                                                            |
| `Program.cs` (nhánh `isMigrate`, sau SocialGraph, sau Messaging nếu B đã nối) | `await app.Services.MigrateModerationModuleAsync();` + thêm tên schema vào dòng log `[migrate]`                              |
| `PostgresFixture.SeededContentDatabaseAsync`             | `.AddModerationModule(cs)` + dòng migrate — **cùng thứ tự** `Program.cs`; sửa doc comment "cả bốn module"                    |
| `ModulesApiFactory.CreateMigratedDatabaseAsync`          | như trên                                                                                                                    |

**Không** thêm `AddApplicationPart`, `apiGroups` — chưa có controller (D0).

**7. Test** — `tests/SocialApp.IntegrationTests/ModerationDbContextSchemaTests.cs` (khuôn `SocialGraphDbContextSchemaTests`):

| Id / tên                                         | Kịch bản                                                                                                   | Kỳ vọng                                                                                   |
| ------------------------------------------------ | ---------------------------------------------------------------------------------------------------------- | ----------------------------------------------------------------------------------------- |
| `AUD-02`                                         | Chèn một dòng audit; `UPDATE` nó; `DELETE` nó; `TRUNCATE` bảng; `DELETE` với `SET LOCAL socialapp.audit_purge = 'on'` trong transaction | `PostgresException` `P0001` · `P0001` · `P0001` · **được**                                 |
| `AUD-02b` *(đề xuất)*                            | `UPDATE` với cờ purge bật                                                                                  | vẫn `P0001` — cửa purge chỉ mở cho `DELETE`                                               |
| `Report_CHECK_chan_that`                         | Chèn: `reason_code = 'abuse'`; `other` không `detail`; `status = 'resolved'` không `resolver_id`            | ba lần `23514` (check violation)                                                          |
| `Report_mot_bao_cao_mo_moi_nguoi_moi_doi_tuong`  | Hai dòng `open` cùng `(reporter, target)`; rồi đóng dòng 1, chèn lại                                       | lần 2 `23505`; lần 3 được (index một phần không chặn dòng đã đóng)                        |
| `Moderation_Domain_namespace_must_not_be_empty`  | Architecture — thêm vào `PersistenceBoundaryTests` (L-A8)                                                  | xanh; **thử cho đỏ**: đổi tạm namespace trong test → đỏ                                    |

Thêm lệnh kiểm tay (ghi vào "Thực tế thi công"): `dotnet run --project src/backend/SocialApp.Api -- --migrate` **hai lần** trên
DB dev → lần hai không migration nào; `psql -c '\dn'` thấy `moderation`.

### Cạm bẫy đã biết

1. **Chép `HasPostgresExtension("citext")` từ Identity** — Moderation không có cột citext. Comment của `ProfileDbContext` đã nói
   vì sao đừng.
2. **`ON CONFLICT` của D6 phải nêu đúng vế `WHERE`** của index một phần (Mục 4 chỗ dễ sai 1) — A1 đặt đúng tên + filter là nửa
   của việc đó; ghi comment trên `HasIndex` trỏ D6.
3. **`SET LOCAL` ngoài transaction** không có tác dụng (Postgres cảnh báo rồi bỏ qua) — ca "được xóa" của `AUD-02` phải mở
   transaction tường minh, nếu không ca đó **đỏ vì lý do sai** và người sửa "nới" trigger.
4. **`bigserial` vs identity:** Npgsql EF sinh `GENERATED BY DEFAULT AS IDENTITY` cho khóa `long` — tương đương `bigserial` của PTTK,
   là cách chuẩn hiện nay. Ghi vào "lệch PTTK ở mô hình" (Mục 4 cuối) một dòng; đừng ép `bigserial` bằng SQL tay.
5. **`CREATE FUNCTION` trong chuỗi C# thường** (không raw string) — `$$` và `'` phải thoát, dễ gãy. Dùng raw string `"""`.
6. **`AuditLog` có setter** → một ngày có người `db.AuditLogs.Update(x)`; trigger chặn ở runtime (500), nhưng tốt hơn là không
   compile được.
7. **Test schema mỗi ca một DB mà không trả kết nối** → pool Npgsql giữ kết nối rảnh 300 giây, cả collection chung một container
   `max_connections = 100`; bộ test vốn sát trần tràn sang `53300 too many clients already` ở **lớp khác** chạy sau (gặp thật ở
   A1: 59 ca Auth đỏ). Lớp test tạo DB riêng mỗi ca phải `IAsyncLifetime` + `NpgsqlConnection.ClearPool` ở `DisposeAsync` —
   khuôn ở `ModerationDbContextSchemaTests`. A2 chép đúng khuôn đó.

---

## 4. A2 — Module Notification

**Mục tiêu:** bảng thông báo đã gộp có mặt với đúng ràng buộc D9 dựa vào; `group_key` sinh ở một chỗ.

**Kết quả mong đợi:** migration `InitialNotification`; test `GroupKey` tám loại xanh; `--migrate` tạo schema `notification`.

### Các bước

1. **Domain:**
   - `Notification` (Id Uuid7, RecipientId, Type, GroupKey, TargetType, TargetId, PostId?, LastActorId?, ActorCount, ReasonCode?,
     IsRead, CreatedAt, UpdatedAt), `NotificationActor` (NotificationId, ActorId).
   - `NotificationTypes`: tám `const string` đúng Đ-6.17 + `All`. `NotificationTargetTypes`: `post|comment|user|conversation`.
   - `GroupKey` (L-A9) — một hàm cho mỗi loại, trả `string`:

     ```csharp
     public static string Comment(Guid postId) => $"comment:post:{postId:D}";
     public static string Reply(Guid parentCommentId) => $"reply:comment:{parentCommentId:D}";
     public static string Reaction(ReactionTargetKind kind, Guid targetId) => $"reaction:{Kind(kind)}:{targetId:D}";
     public static string FriendRequest(Guid requesterId) => …; FriendAccepted(Guid accepterId); Message(Guid conversationId);
     public static string Moderation(ModerationTargetType type, Guid targetId); Tag(Guid commentId);
     ```

     Định dạng `Guid` **`:D`** cố định — cùng bài học khóa Redis GĐ4 (`RedisFeedPageCache.Key`): hai chỗ gõ khóa khác format là
     gộp không bao giờ trúng.
2. **Infrastructure:** `NotificationDbContext` (`Schema = "notification"`), options, design-time factory, hai configuration:
   `uq_notifications_group UNIQUE (recipient_id, group_key)` (**constraint**, không chỉ index — D9 dùng `ON CONFLICT (recipient_id, group_key)`),
   `ck_notifications_type` dựng từ `NotificationTypes.All`, `ck_notifications_actor_count CHECK (actor_count >= 1)`,
   `idx_notifications_recent (recipient_id, updated_at DESC, id DESC)`, `idx_notifications_unread (recipient_id) WHERE is_read = false`;
   `NotificationActor` PK cặp + `HasOne<Notification>().WithMany().OnDelete(Cascade)`.
3. **Migration** `InitialNotification` (lệnh như A1, đổi project). Không SQL tay nào.
4. **DI + host + harness** — y như A1 bước 5–6, dòng migrate đứng **sau** Moderation.
5. **Test:**
   - Unit `GroupKeyTests`: tám loại ra đúng chuỗi mẫu; hai `Guid` khác nhau ra hai khóa khác nhau; khóa dài nhất ≤ 120 ký tự
     (`varchar(120)`); `Reaction(Post, x) ≠ Reaction(Comment, x)`.
   - Integration `NotificationDbContextSchemaTests`: `UNIQUE` chặn cặp trùng (`23505`); `actor_count = 0` bị chặn (`23514`);
     xóa `notifications` kéo theo `notification_actors`.
   - `Notification_Domain_namespace_must_not_be_empty` vào `PersistenceBoundaryTests`.

### Cạm bẫy đã biết

- **`HasIndex(...).IsUnique()` thay vì `HasAlternateKey`/unique constraint:** `ON CONFLICT (cols)` chạy được với cả unique index lẫn
  unique constraint, nhưng tên trong DDL Mục 4 là `CONSTRAINT uq_notifications_group` — chọn một và **ghi vào schema test** để D9
  không đoán.
- **`GroupKey` nhận `ReactionTargetKind` của SharedKernel**, không nhận enum của Content (ADR-001) — đúng lý do L3 của C0.
- **Không** thêm cột `display_name`, trích đoạn nội dung "cho tiện hiển thị" — Đ-6.16: bảng 30 triệu dòng/năm chỉ chứa id.

---

## 5. A3 — Identity: `role.manage`, mô tả quyền, trigger vai trò hệ thống, sequence

**Mục tiêu:** trả nợ GĐ1 Mục 2 ở tầng dữ liệu, không đổi dòng dữ liệu nào đang có.

**Kết quả mong đợi:** migration `SystemRoleGuardAndPermissionDescriptions` (đổi tên so với kế hoạch — không còn "Add…Description",
L-A1); 18 mã có mô tả trên **mọi** DB; ba vai trò hệ thống không đổi `code`/không xóa được kể cả bằng `psql`; bốn test GĐ1 đã sửa
và xanh.

### Các bước

1. **`PermissionCodes`:** thêm `RoleManage = "role.manage"` với doc comment *"18 — định nghĩa vai trò (tạo, đổi tên, sửa tập quyền,
   xóa). Chỉ ADMIN (short-circuit) — không vào bootstrap USER/MODERATOR (Đ-6.9)"*; thêm vào **cuối** `All` (`// 18`); sửa doc lớp
   "Mười bảy" → "Mười tám" và ghi lệch ma trận PTTK (Đ-6.9).
2. **`PermissionCodes.Descriptions`** (L-A2) — `IReadOnlyDictionary<string, string>` cạnh `All`, câu tiếng Việt ≤ 120 ký tự. Bản nháp:

   | id  | Mã                  | Mô tả                                                   |
   | --- | ------------------- | ------------------------------------------------------- |
   | 1   | `post.read.public`  | Xem bài viết công khai                                  |
   | 2   | `post.read.friends` | Xem bài viết chỉ dành cho bạn bè                        |
   | 3   | `post.create`       | Đăng bài viết                                           |
   | 4   | `post.update`       | Sửa bài viết của mình                                   |
   | 5   | `post.delete`       | Xóa bài viết của mình                                   |
   | 6   | `post.hide`         | Ẩn và khôi phục nội dung vi phạm của người khác         |
   | 7   | `comment.create`    | Bình luận                                               |
   | 8   | `reaction.set`      | Bày tỏ cảm xúc                                          |
   | 9   | `friend.request`    | Gửi lời mời kết bạn và theo dõi                         |
   | 10  | `friend.respond`    | Chấp nhận lời mời kết bạn                               |
   | 11  | `message.send`      | Nhắn tin                                                |
   | 12  | `report.create`     | Báo cáo nội dung hoặc tài khoản vi phạm                 |
   | 13  | `report.resolve`    | Xem hàng đợi và xử lý báo cáo vi phạm                   |
   | 14  | `user.lock`         | Khóa tài khoản                                          |
   | 15  | `user.unlock`       | Mở khóa tài khoản                                       |
   | 16  | `role.assign`       | Gán vai trò cho tài khoản                               |
   | 17  | `audit.read`        | Xem nhật ký kiểm toán toàn hệ thống                     |
   | 18  | `role.manage`       | Tạo, đổi tên, sửa quyền và xóa vai trò                  |

3. **`IdentitySeeder.PermissionsSql`:** `INSERT … (permission_id, code, description)` — `description` lấy từ `Descriptions`, qua
   `Literal()` như mọi chuỗi khác; vẫn `ON CONFLICT DO NOTHING` (luật 2 của seeder). Sửa comment "description để NULL: Mục 5.2 không
   định nghĩa mô tả" → trỏ L-A2. Chỗ đọc `All` (bootstrap) **không** đổi — `role.manage` không vào `UserGrants`/`ModeratorGrants`.
4. **Sequence** (L-A4) — `IdentityDbContext.OnModelCreating`:
   `modelBuilder.HasSequence<short>("roles_role_id_seq").StartsAt(100);` (schema mặc định `identity`). `Role.RoleId` giữ
   `ValueGeneratedNever()` — seeder vẫn chèn id tường minh; D5 lấy id bằng `SELECT nextval('identity.roles_role_id_seq')`.
5. **Migration:**

   ```bash
   dotnet ef migrations add SystemRoleGuardAndPermissionDescriptions \
     --project src/backend/Modules/Identity/SocialApp.Modules.Identity.csproj \
     --startup-project src/backend/Modules/Identity/SocialApp.Modules.Identity.csproj \
     --output-dir Infrastructure/Migrations
   ```

   EF chỉ sinh `CreateSequence` (nhờ bước 4). Viết tay phần còn lại:

   ```csharp
   // L-A2: chỉ cho DB đã seed TRƯỚC migration này (staging, dev đang chạy). DB mới: seeder chèn kèm mô tả.
   // Chữ viết thẳng, KHÔNG đọc PermissionCodes.Descriptions: migration là ảnh chụp bất biến — sửa câu mô tả sau này là
   // migration mới, không phải sửa ngược migration đã chạy trên staging.
   migrationBuilder.Sql("""
       UPDATE identity.permissions SET description = 'Xem bài viết công khai' WHERE code = 'post.read.public' AND description IS NULL;
       … 17 câu, mỗi mã một câu, có "AND description IS NULL" — không đè mô tả ai đó đã sửa tay …
       """);

   // Đ-6.9 lớp chặn 3: ba vai trò hệ thống không đổi code, không xóa — kể cả psql. display_name thì đổi được (ROLE-05).
   migrationBuilder.Sql("""
       CREATE FUNCTION identity.roles_protect_system() RETURNS trigger LANGUAGE plpgsql AS $$
       BEGIN
           IF TG_OP = 'DELETE' OR NEW.code IS DISTINCT FROM OLD.code THEN
               RAISE EXCEPTION 'Vai trò hệ thống % không đổi mã, không xóa được', OLD.code USING ERRCODE = 'P0001';
           END IF;
           RETURN NEW;
       END $$;
       CREATE TRIGGER trg_roles_protect_system BEFORE UPDATE OF code OR DELETE ON identity.roles
           FOR EACH ROW WHEN (OLD.code IN ('ADMIN', 'USER', 'MODERATOR'))
           EXECUTE FUNCTION identity.roles_protect_system();
       """);
   ```

   `Down()`: `DROP TRIGGER`, `DROP FUNCTION`, `DropSequence` — **không** xóa mô tả (dữ liệu, không phải schema).
   Chuỗi `'ADMIN','USER','MODERATOR'` gõ tay trong migration là **cố ý** — cùng lý do ảnh chụp bất biến; comment trỏ `RoleCodes.All`.
6. **Sửa bốn test GĐ1 cùng commit** (L-A3):

   | Test                                                             | Sửa                                                                                                                                                                                                                              |
   | ---------------------------------------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
   | `PermissionCodeUsageTests.PermissionCodes_doc_duoc_du_17_ma`     | Đổi tên `…_18_ma`, `Assert.Equal(18, …)`; doc comment "Khóa đúng 18 mã (Mục 5.2 + Đ-6.9)"                                                                                                                                         |
   | `IdentitySeederTests.ExpectedPermissions`                        | Thêm `"18\|role.manage"`; `ReadSeedDataAsync` đọc thêm `description` và `SEED-01` khẳng định **không dòng nào NULL** (bắt L-A2). `ExpectedGrants` **không** đổi (24 dòng — `role.manage` không vào bootstrap)                         |
   | `IdentitySeederTests.SEED_03_…`                                  | Trước `UPDATE … 'ROOT'`: `ALTER TABLE identity.roles DISABLE TRIGGER trg_roles_protect_system` — mô phỏng "ai đó đã `DROP TRIGGER`". Doc comment: kiểm tra lúc khởi động là **lớp 2**, vẫn phải bắt khi lớp 3 bị gỡ                     |
   | `IdentitySeederTests.FK_01_…`                                    | Dùng vai trò **tự tạo** (id 100, `REVIEWER`) có một người dùng thay cho `role_id = 1` — trigger không chạy với vai trò không phải hệ thống, FK `RESTRICT` trả đúng `23503`                                                            |

7. **Test mới** — `IdentitySeederTests` hoặc `IdentitySystemRoleGuardTests`:

   | Id                                        | Kịch bản                                                                                                     | Kỳ vọng                                                                  |
   | ----------------------------------------- | ------------------------------------------------------------------------------------------------------------ | ------------------------------------------------------------------------ |
   | `ROLE-05`                                 | SQL thẳng: đổi `code` USER; xóa MODERATOR; đổi `display_name` USER; `UPDATE roles SET code = code` trên ADMIN | `P0001` · `P0001` · được · **được** (không đổi giá trị thì không chặn)   |
   | `ROLE-07`                                 | Seed; gỡ `post.hide` của MODERATOR; seed lại hai lần                                                         | gỡ còn nguyên; dòng 18 có mặt **đúng một lần**, có mô tả                  |
   | `A3_mo_ta_cho_DB_da_seed_tu_GD1` *(đề xuất)* | Migrate tới `InitialIdentity` (`IMigrator.MigrateAsync("20260913040158_InitialIdentity")`), chèn 17 dòng **không** mô tả bằng SQL (giả staging), rồi `MigrateIdentityModuleAsync` | 17 dòng cũ có mô tả (từ migration); dòng 18 có mô tả (từ seeder)          |
   | `Sequence_vai_tro_tu_tao_bat_dau_tu_100`   | `SELECT nextval('identity.roles_role_id_seq')` trên DB mới                                                   | `100`                                                                    |

### Cạm bẫy đã biết

1. **Chạy `migrations add` rồi thấy `Up()` rỗng và tưởng hỏng** — rỗng (trừ sequence) là **đúng**: cột đã có, trigger EF không biết.
2. **`BEFORE UPDATE` không có `OF code`** → mọi `UPDATE roles SET display_name` trên vai trò hệ thống bị chặn; D5 đổi tên hỏng,
   `ROLE-05` vế 3 đỏ (Mục 4 chỗ dễ sai 4).
3. **Quên `WHEN (OLD.code IN …)`** → xóa vai trò **tự tạo** cũng bị chặn; D5 `DELETE` luôn 409 sai lý do.
4. **`DISABLE TRIGGER` trong `SEED_03` mà không bật lại** — không sao: mỗi test một DB mới (`CreateDatabaseAsync`). Nhưng **đừng**
   chuyển `SEED_03` sang DB dùng chung.
5. **`session_replication_role = replica`** để "lách trigger" trong test — nó tắt cả trigger FK hệ thống, `FK_01` xanh giả. Dùng
   `DISABLE TRIGGER <tên>`, chỉ tắt đúng trigger đó.
6. **Seeder chạy trên staging sau migration:** dòng 18 được chèn kèm mô tả; 17 dòng cũ đã có mô tả từ migration → không dòng nào
   NULL. Kiểm bằng `SELECT count(*) FROM identity.permissions WHERE description IS NULL` = 0 ở F1.

---

## 6. A4 — Profile: tìm kiếm không dấu

**Mục tiêu:** index biểu thức mà D12 chỉ việc dùng; chứng minh planner **chọn** nó ở quy mô thật.

**Kết quả mong đợi:** migration `AddDisplayNameSearch`; `EXPLAIN` ba loại `q` trúng GIN trên 20.000 hồ sơ; quyền tạo extension trên
staging đã kiểm.

### Các bước

1. **Migration** (Profile; EF sinh `Up()` rỗng — model không đổi):

   ```csharp
   // Đ-6.19. Thứ tự bắt buộc: extension → hàm → index. WITH SCHEMA public vì hàm gọi public.unaccent nguyên tên — không phụ
   // thuộc search_path của phiên nào. Cả hai là trusted extension (PG13+): chủ database tạo được, không cần superuser.
   migrationBuilder.Sql("""
       CREATE EXTENSION IF NOT EXISTS unaccent WITH SCHEMA public;
       CREATE EXTENSION IF NOT EXISTS pg_trgm WITH SCHEMA public;
       CREATE FUNCTION profile.search_norm(text) RETURNS text
           LANGUAGE sql IMMUTABLE PARALLEL SAFE STRICT
           AS $$ SELECT lower(public.unaccent('public.unaccent'::regdictionary, $1)) $$;
       CREATE INDEX idx_profiles_display_name_search ON profile.profiles
           USING gin (profile.search_norm(display_name) public.gin_trgm_ops);
       """);
   ```

   `Down()`: `DROP INDEX`, `DROP FUNCTION` — **giữ** extension (module khác có thể đã dùng; `DROP EXTENSION` ở `Down` của một module
   là phá module kia). Ghi comment.
2. **Test tích hợp** — `ProfileDbContextSchemaTests` thêm:
   - hàm tồn tại, `provolatile = 'i'` (`SELECT provolatile FROM pg_proc WHERE proname = 'search_norm'`);
   - `SELECT profile.search_norm('Nguyễn Đức Ánh')` = `'nguyen duc anh'` — bẫy "đ" (`SRCH-03` ở tầng hàm);
   - **index dùng được**: trong transaction `SET LOCAL enable_seqscan = off`, `EXPLAIN` câu `WHERE profile.search_norm(display_name)
     LIKE 'ng%'` chứa `idx_profiles_display_name_search`. Đây chỉ chứng minh **biểu thức khớp** index — không chứng minh planner
     **chọn** nó (bước 3). Ghi rõ trong doc comment để không ai tưởng đây là `SRCH-07`.
3. **`EXPLAIN` ở quy mô** (L-A10) — script `tests/load/search/seed-profiles.sql`:

   ```sql
   -- Chỉ chạy trên DB vứt được. Chặn theo tên như tests/load/feed/seed.sql.
   DO $$ BEGIN
     IF current_database() <> 'socialapp_search' THEN RAISE EXCEPTION 'chỉ chạy trên socialapp_search'; END IF;
   END $$;
   INSERT INTO profile.profiles (user_id, display_name, created_at, updated_at)
   SELECT gen_random_uuid(),
          (ARRAY['Nguyễn','Trần','Lê','Phạm','Hoàng','Huỳnh','Phan','Vũ','Võ','Đặng','Bùi','Đỗ','Hồ','Ngô','Dương','Lý'])[1 + floor(random()*16)::int]
          || ' ' || (ARRAY['Văn','Thị','Đức','Minh','Ngọc','Thu','Hữu','Quốc','Thanh','Gia'])[1 + floor(random()*10)::int]
          || ' ' || (ARRAY['An','Bình','Châu','Dũng','Đức','Giang','Hà','Hải','Hạnh','Hoa','Hùng','Khánh','Lan','Linh','Long',
                           'Mai','Nam','Nga','Phúc','Quân','Sơn','Tâm','Thảo','Trang','Tuấn','Việt','Yến'])[1 + floor(random()*27)::int],
          now(), now()
   FROM generate_series(1, 20000);
   ANALYZE profile.profiles;
   ```

   Quy trình: `CREATE DATABASE socialapp_search` trên Postgres dev → `ConnectionStrings__Postgres=<chuỗi tới DB đó> dotnet run --project
   src/backend/SocialApp.Api -- --migrate` → `psql -v ON_ERROR_STOP=1 -f tests/load/search/seed-profiles.sql` → ba câu:

   ```sql
   EXPLAIN (ANALYZE, BUFFERS) SELECT user_id FROM profile.profiles
    WHERE profile.search_norm(display_name) LIKE 'ng%' OR profile.search_norm(display_name) LIKE '% ng%';      -- 2 ký tự
   -- lặp lại với 'nguy', và 'van' (tiền tố từ thứ hai)
   ```

   Phải thấy `BitmapOr` của hai `Bitmap Index Scan on idx_profiles_display_name_search`, không `Seq Scan`. Dán nguyên kết quả ba câu
   + thời gian vào "Thực tế thi công" (nếp GĐ4 A5) → `DROP DATABASE socialapp_search`.
4. **Staging — quyền tạo extension** (Mục 9.1, R6-07). *Kiểm trên repo 2026-09-23:* `deploy/docker-compose.staging.yml` dựng
   `postgres:16` với `POSTGRES_USER: socialapp`, `POSTGRES_DB: socialapp`; chuỗi kết nối của api/`migrate` trong `deploy/.env` dùng
   `Username=socialapp`. Image chính thức tạo `POSTGRES_USER` là **superuser** và chủ DB `POSTGRES_DB` → `CREATE EXTENSION` chạy được,
   **không** cần ai tạo tay trước. Điều kiện duy nhất: `.env` trên server khớp bản trong repo (cùng user). Kiểm lại **sau** deploy ở
   F1, không phải trước:

   ```sql
   SELECT current_user, pg_get_userbyid(datdba) AS owner FROM pg_database WHERE datname = current_database();
   \dx
   ```

   Thấy `unaccent`, `pg_trgm` trong `\dx` là xong. Chỉ khi `--migrate` đỏ ở migration Profile (user trên server khác bản repo, không
   phải chủ DB) mới cần chủ dự án chạy tay một lần `CREATE EXTENSION unaccent WITH SCHEMA public; CREATE EXTENSION pg_trgm WITH SCHEMA
   public;` — migration dùng `IF NOT EXISTS` nên chạy lại đi qua. Ghi kết quả vào "Thực tế thi công".

### Cạm bẫy đã biết

1. **`unaccent(display_name)` một tham số trong hàm** → dựa vào `search_path` để tìm từ điển; migrate chạy với `search_path` khác
   là hàm lỗi ở runtime. Luôn dạng hai tham số, từ điển ghi schema.
2. **`CREATE INDEX CONCURRENTLY`** — EF bọc migration trong transaction, `CONCURRENTLY` không chạy được (Mục 4 chỗ dễ sai 5).
3. **Tin `EXPLAIN` trên DB test vài dòng** — planner chọn Seq Scan với bảng nhỏ, đúng; kết luận "index hỏng" từ đó là sai. Quy mô
   mới là bằng chứng (bước 3).
4. **Viết `lower(unaccent(…))` ở D12** thay vì `profile.search_norm(…)` → Seq Scan. Comment trên hàm ghi: *"D12 phải gọi đúng hàm
   này ở cả hai vế"*.
5. **Đổi từ điển `unaccent` sau này** (nâng Postgres) mà không `REINDEX` — hàm "khai man" `IMMUTABLE`, index giữ giá trị cũ. Ghi
   vào comment hàm; không làm gì thêm ở GĐ6.

---

## 7. A5 — `ModerationPermissions`

**Mục tiêu:** bốn mã quyền của Moderation là chuỗi của chính module, có lưới bắt gõ sai.

**Kết quả mong đợi:** `Modules/Moderation/Application/ModerationPermissions.cs` + `tests/SocialApp.ArchitectureTests/ModerationPermissionsTests.cs`.

### Các bước

1. Chép khuôn `SocialGraphPermissions` (doc comment giải thích vì sao không `using` Identity, và vì sao Admin vẫn qua khi gõ sai):
   `ReportCreate`, `ReportResolve`, `PostHide`, `AuditRead` + `All`.
2. Chép `ContentPermissionsTests` thành `ModerationPermissionsTests` — hai ca: mọi mã có trong `PermissionCodes.All`; `All` liệt kê
   đủ mọi hằng.
3. **Thử cho đỏ:** đổi tạm `ReportResolve = "report.reslove"` → ca 1 đỏ, nêu đúng mã → khôi phục, `git status` sạch.

`role.manage`, `user.*`, `role.assign` **không** vào đây — chúng thuộc controller của Identity (`admin-v1`), đọc thẳng
`PermissionCodes`.

---

## 8. C1 — `IAuditTrail` + `SqlAuditTrail`

**Mục tiêu:** audit cùng số phận với thao tác — nền của mốc 3.

**Kết quả mong đợi:** `SharedKernel/Audit/{IAuditTrail,AuditEntry}.cs` (+ `AuditActions` từ A1); `Moderation/Infrastructure/Audit/SqlAuditTrail.cs`;
test transaction + test hình dạng xanh; đột biến "kết nối riêng" bị bắt.

### Các bước

1. **Hợp đồng** — comment đầu file ghi **lệch Đ-2.3 có chủ đích** + trỏ Đ-6.3 (Đ-6.3 đòi ghi ở chính interface):

   ```csharp
   public sealed record AuditEntry(Guid ActorId, string Action, string? TargetType, Guid? TargetId,
       IReadOnlyDictionary<string, object?>? Metadata = null);

   public interface IAuditTrail
   {
       /// tx != null → INSERT trên đúng kết nối + transaction đó. tx == null → kết nối riêng, CHỈ cho access.denied (Đ-6.15).
       Task AppendAsync(DbTransaction? tx, AuditEntry entry, CancellationToken ct);
   }
   ```

   `Metadata` là **khóa–giá trị**, không phải `object` tự do: người gọi không có đường nào đẩy cả entity (có `Body`) vào audit.
2. **Hiện thực** — `SqlAuditTrail(ModerationDbContext db, IHttpContextAccessor http, TimeProvider clock)`, **`internal sealed`**, ở
   `Infrastructure/`:

   ```csharp
   const string Sql = """
       INSERT INTO moderation.audit_logs (actor_id, action, target_type, target_id, metadata, ip, created_at)
       VALUES ($1, $2, $3, $4, $5, $6, $7)
       """;

   if (tx is not null)
   {
       var conn = tx.Connection as NpgsqlConnection
           ?? throw new InvalidOperationException("Transaction đã đóng hoặc không phải Npgsql.");
       await using var cmd = new NpgsqlCommand(Sql, conn, (NpgsqlTransaction)tx);   // KHÔNG new NpgsqlConnection (R6-06)
       …
   }
   else   // L-C1: kết nối của ModerationDbContext, cùng pool
   {
       await db.Database.OpenConnectionAsync(ct);
       try { await using var cmd = new NpgsqlCommand(Sql, (NpgsqlConnection)db.Database.GetDbConnection()); … }
       finally { await db.Database.CloseConnectionAsync(); }
   }
   ```

   Tham số: `metadata` → `NpgsqlDbType.Jsonb` với `JsonSerializer.Serialize(entry.Metadata)`; `ip` → `RemoteIpAddress`, đổi
   `IsIPv4MappedToIPv6` về IPv4 (`MapToIPv4()`); không có `HttpContext` (job nền) → `NULL`; `created_at` từ `TimeProvider` (nguồn thời
   gian của dự án).
3. **DI** — `AddModerationModule`: `services.AddHttpContextAccessor(); services.AddScoped<IAuditTrail, SqlAuditTrail>();`.
4. **Test** — `tests/SocialApp.IntegrationTests/Moderation/AuditTrailTests.cs`, DB có cả `identity` và `moderation`:

   | Id                                  | Kịch bản                                                                                                                                                                         | Kỳ vọng                                                                                                   |
   | ----------------------------------- | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------- |
   | `TX-01` *(bản hạ tầng — L-C2)*      | `IdentityDbContext.BeginTransactionAsync` → `UPDATE identity.users SET status='disabled'` → `AppendAsync(GetDbTransaction(), user.lock)` → **ném** trước `CommitAsync`            | user vẫn `active`; **0** dòng `audit_logs`                                                                |
   | `TX-01-commit`                      | Như trên, `CommitAsync`                                                                                                                                                           | user `disabled`; **1** dòng audit, đúng `actor_id`, `action`, `target_id`                                  |
   | `AUD-01` *(bản hạ tầng)*            | `AppendAsync(null, …)` trong một request giả có `RemoteIpAddress = ::ffff:203.0.113.7`, metadata `{ reportIds: [..], note: "x" }`                                                  | `ip = 203.0.113.7`; `metadata` đọc lại đúng khóa; `created_at` từ đồng hồ giả                               |

   **Đột biến bắt buộc** (B5 ghi vào bảng đột biến PR): nhánh `tx != null` mở `new NpgsqlConnection(chuỗi)` riêng → `TX-01` **đỏ**
   (dòng audit sống sót sau rollback). Đây là bằng chứng transaction là **một**.

### Cạm bẫy đã biết

1. **`db.AuditLogs.Add(x); await db.SaveChangesAsync()`** ở nhánh `tx != null` — `ModerationDbContext` **không** ở trong transaction
   của Identity; nó ghi trên kết nối riêng → đúng đột biến trên, chạy "tốt" mọi lúc trừ lúc lỗi.
2. **`db.Database.UseTransaction(tx)`** "cho gọn" — gắn `DbContext` của Moderation vào transaction trên kết nối của Identity: EF đòi
   **cùng** `DbConnection` instance; ở đây là hai `DbContext` hai kết nối → ném. Và nếu chạy được thì đó là `DbContext` thứ hai
   Đ-6.3 cấm. `NpgsqlCommand` trần là đúng.
3. **Log `entry`** khi lỗi — `record` tự in mọi thuộc tính, kể cả `note` của Moderator (luật "không nội dung vào log", B.10 #5).
   Log `Action` + tên lỗi, không log entry.
4. **Nuốt lỗi ở nhánh `tx != null`** — lỗi audit phải làm **cả** thao tác rollback. Không try/catch ở đó. (Nhánh `tx == null` thì
   người gọi C4 quyết — xem Mục 10.)

---

## 9. C3 — Cache quyền: invalidate, `IsAllowedAsync`, pub/sub

**Mục tiêu:** sửa quyền là thấy ngay, trên mọi instance; một hàm duy nhất gói Admin short-circuit.

**Kết quả mong đợi:** `IPermissionCache` + `PermissionCache` có `Invalidate`, `InvalidateAll`, `IsAllowedAsync`; `IPermissionChangeNotifier`
+ `PermissionsChangedSubscriber`; `PERM-01` (bản hạ tầng), `PERM-02` xanh.

### Các bước

1. **`IPermissionCache`** — chỉ-thêm:

   ```csharp
   ValueTask<bool> IsAllowedAsync(string? roleCode, string permission, CancellationToken ct = default);  // Mục 6.2 — L-C3
   void Invalidate(string roleCode);
   void InvalidateAll();
   ```

   `IsAllowedAsync`: `null` → false; `== SystemRoles.Admin` → true **không** chạm cache; còn lại → `GetAsync(role).Contains(permission)`.
   `PermissionHandler` đổi thân thành một lời gọi `IsAllowedAsync` — năm ca `PermissionHandlerTests` phải xanh **không sửa khẳng định**
   (impact MEDIUM, Mục 1.2). Sửa hai fake `FixedCache`, `ThrowingCache` (L-C10).
2. **Chống ghi đè sau invalidate** (L-C5) — `PermissionCache` giữ `ConcurrentDictionary<string, long> _generation`: `GetAsync` đọc
   thế hệ **trước** khi nạp, chỉ ghi entry nếu thế hệ chưa đổi; `Invalidate` tăng thế hệ rồi xóa entry; `InvalidateAll` tăng một
   thế hệ toàn cục. Unit test: nguồn giả chặn trên `TaskCompletionSource`; bắt đầu `GetAsync` → `Invalidate` → mở chặn → lần
   `GetAsync` kế tiếp **gọi lại nguồn** (không dùng kết quả cũ).
3. **`IPermissionChangeNotifier`** (L-C4) — SharedKernel, singleton:

   ```csharp
   /// Gọi SAU COMMIT của mọi thao tác đổi role_permissions hoặc xóa vai trò (D5). Không ném khi Redis chết (Đ-6.10:
   /// instance khác thấy sau ≤ 60 giây — TTL là lưới cuối).
   Task NotifyAsync(string roleCode, CancellationToken ct = default);
   ```

   Thân: `cache.Invalidate(roleCode)` **trước**, rồi `PUBLISH socialapp:{env}:authz:permissions-changed {roleCode}` (`env` =
   `IHostEnvironment.EnvironmentName` viết thường). Redis lỗi → log Warning qua `FailOpenLogThrottle` (`"permissions-publish"`), không ném.
4. **`PermissionsChangedSubscriber : BackgroundService`** — `SUBSCRIBE` kênh trên `RedisConnection.GetAsync()`, mỗi tin gọi
   `Invalidate(roleCode)`; bỏ qua tin do **chính** instance phát cũng được (invalidate hai lần vô hại). Móc
   `ConnectionRestored` của multiplexer → `InvalidateAll()` (L-C5: tin phát lúc rớt đã mất). Lần `SUBSCRIBE` đầu thất bại (Redis chết lúc
   khởi động) → thử lại có giãn cách, **không** làm chết host.
5. **DI** — trong `AddSharedKernelAuthorization` (điểm ráp duy nhất của tầng 2): notifier + subscriber. Subscriber cần `RedisConnection`:
   đăng ký **có điều kiện** (`services.Any(d => d.ServiceType == typeof(RedisConnection))`) như `AddSharedKernelTokenRevocation` kiểm —
   test trần không có Redis không bị kéo theo.
6. **Test:**

   | Id                         | Kịch bản                                                                                                                                         | Kỳ vọng                                                    |
   | -------------------------- | ------------------------------------------------------------------------------------------------------------------------------------------------ | ---------------------------------------------------------- |
   | `PERM-01` *(bản hạ tầng)*  | `ModulesApiFactory`; USER `POST /posts` → 201; SQL xóa `post.create` của USER; `notifier.NotifyAsync("USER")`; `POST /posts` ngay (**không** tua `TimeProvider`) | 403 ở request kế tiếp                                      |
   | `PERM-01-doi-chung`        | Như trên nhưng **không** gọi notifier                                                                                                            | vẫn 201 (cache còn) — chứng minh ca trên đo đúng thứ cần đo |
   | `PERM-02`                  | Hai `ModulesApiFactory` chung DB + chung Redis thật (`UseRedis`); nạp cache ở factory 2; sửa bảng + notify ở factory 1                            | factory 2 trả 403 trong ≤ 1 giây (chờ bằng vòng hỏi có hạn, không `Task.Delay` cố định) |
   | `PERM-03` *(đề xuất)*      | Unit — tranh chấp nạp/invalidate (bước 2)                                                                                                        | lần đọc sau gọi lại nguồn                                  |

   Bản đầy đủ `PERM-01` (qua `PUT /admin/roles/{id}/permissions`) là của D5.

### Cạm bẫy đã biết

1. **Kênh không có tiền tố môi trường** — staging và production chung một Redis thì production xóa cache vì staging sửa quyền (vô
   hại) và ngược lại (Đ-6.10). Không tiền tố thì không test nào đỏ; kiểm bằng code review + một unit test tên kênh.
2. **`IsAllowedAsync` tự viết lại ở D7** thay vì gọi hàm này → hai bản short-circuit. `grep -rn '"ADMIN"' src/` chỉ được ra
   `SystemRoles.cs` (B.10 tự rà #3).
3. **`AsyncTimeout = 250 ms`** của kết nối chung áp cả cho `PUBLISH` — đủ; đừng tăng nó "cho pub/sub" (nó đang giữ trần độ trễ của bên
   đọc thu hồi token trên **mọi** request).
4. **Subscriber resolve `IPermissionCache` là singleton** — không mở scope; nhưng đừng inject `IRolePermissionSource` (scoped) vào nó.

---

## 10. C4 — `[PrivilegedEndpoint]`: fail-closed, audit khi bị từ chối, any-of

**Mục tiêu:** endpoint quản trị không fail-open; mọi lần bị từ chối để lại dấu vết; một attribute cho cả hai.

**Kết quả mong đợi:** năm mảnh dưới đây + `FC-01`, `AUD-03` xanh trên probe; `PermissionCodeUsageTests` đọc attribute mới.

### Các bước

1. **Attribute** — `SharedKernel/Authorization/PrivilegedEndpointAttribute.cs`: `[AttributeUsage(Class | Method)]`, **chỉ metadata**
   (không kế thừa `AuthorizeAttribute` — tầng 2 vẫn do `[RequirePermission]` khai). Doc comment: *"gộp fail-closed (Đ-6.8) và audit
   khi bị từ chối (Đ-6.15). Mọi controller `admin-v1`, `moderation-v1` trừ `POST /reports`"*.
2. **`ITokenRevocationStore.CheckAsync(sub, iat) → RevocationCheck { NotRevoked, Revoked, Unknown }`** — chỉ-thêm. Hiện thực Redis:
   chưa kết nối / `RedisException` / `RedisTimeoutException` → `Unknown` (không log ở đây); `IsRevokedAsync` viết lại thành
   `CheckAsync` + `Unknown → false + LogFailOpen` — **một** chỗ đọc Redis, hành vi `IsRevokedAsync` giữ nguyên cho filter hub của B.
   Sửa `FakeRevocation` (L-C10).
3. **`OnTokenValidated`** (Program.cs) — thay `IsRevokedAsync` bằng `CheckAsync`:

   ```csharp
   var privileged = ctx.HttpContext.GetEndpoint()?.Metadata.GetMetadata<PrivilegedEndpointAttribute>() is not null;
   switch (await revocation.CheckAsync(sub, iat, ct))
   {
       case RevocationCheck.Revoked: ctx.Fail("token đã bị thu hồi"); break;
       case RevocationCheck.Unknown when privileged:                      // L-C6
           ctx.HttpContext.Items[PrivilegedEndpointAttribute.RevocationUnavailableKey] = true;
           ctx.Fail("không kiểm được thu hồi trên endpoint đặc quyền"); break;
       case RevocationCheck.Unknown: /* fail-open như GĐ1 — log đã có trong store */ break;
   }
   ```

   `GetEndpoint()` có giá trị ở đây vì `WebApplication` tự chèn `UseRouting` **đầu** pipeline khi không ai gọi tường minh — comment
   dòng đó: *"đừng thêm `app.UseRouting()` sau `UseAuthentication`"*.
4. **`AuditingAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler`** — SharedKernel, bọc
   `AuthorizationMiddlewareResultHandler` mặc định:

   | Kết quả                                                                  | Làm gì                                                                                                                                                                                 |
   | ------------------------------------------------------------------------ | -------------------------------------------------------------------------------------------------------------------------------------------------------------------------------------- |
   | `Challenged` + dấu `RevocationUnavailable`                               | Ghi **503** Problem Details `type = urn:socialapp:problem:revocation-unavailable`, không gọi handler mặc định                                                                            |
   | `Forbidden` + endpoint có `[PrivilegedEndpoint]` + có `sub`              | `SET audit:denied:{sub}:{routeTemplate} 1 NX EX 60` → đặt được (hoặc Redis lỗi) thì `IAuditTrail.AppendAsync(null, access.denied { method, routeTemplate })`; rồi handler mặc định (403) |
   | Còn lại                                                                  | Handler mặc định                                                                                                                                                                       |

   `routeTemplate` lấy từ `RouteEndpoint.RoutePattern.RawText` — **không** `Request.Path` (có id trên đường) và không query string
   (Đ-6.15). `IAuditTrail` resolve từ `RequestServices` (scoped). Ghi audit **lỗi** → log Error, **vẫn 403** — không biến 403 thành 500.
5. **`[RequireAnyPermission(params string[])]`** — tên policy `perm-any:a|b|c`; `PermissionPolicyProvider` thêm nhánh tiền tố đó
   dựng `AnyPermissionRequirement`; handler mới gọi `IsAllowedAsync` từng mã (C3). `PermissionCodeUsageTests` đọc cả attribute mới
   (Mục 10.4 #4); `PermissionPolicyProviderTests` thêm ca `perm-any:`.
6. **DI** — `AddSharedKernelAuthorization`: `AddSingleton<IAuthorizationMiddlewareResultHandler, AuditingAuthorizationResultHandler>()`
   + handler any-of. 503 dùng `ProblemDetailsFactory` có sẵn (`SharedKernelProblemDetailsFactory`) để có `traceId` như mọi lỗi.
7. **Probe** — thêm vào `tests/…/AuthZ/AuthZProbeController.cs` (L-C7):

   ```csharp
   [PrivilegedEndpoint, RequirePermission("report.resolve")] [HttpGet("privileged/{id:guid}")] …
   [PrivilegedEndpoint, RequireAnyPermission("user.lock", "user.unlock", "role.assign")] [HttpGet("privileged-any")] …
   ```

8. **Test:**

   | Id                                          | Kịch bản                                                                                                            | Kỳ vọng                                                                                                   |
   | ------------------------------------------- | ------------------------------------------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------------------------- |
   | `FC-01` *(trên probe)*                      | Redis trỏ cổng không ai nghe; MODERATOR gọi probe đặc quyền; gọi `GET /feed`                                        | 503, `type` đúng URN, có `traceId` · 200                                                                   |
   | `AUD-03`                                    | USER gọi probe đặc quyền 5 lần trong 1 phút, **mỗi lần một id khác** trên đường                                       | 5 × 403; **đúng 1** dòng `access.denied`, `metadata.routeTemplate` = `__test/authz/privileged/{id:guid}`, không chứa id |
   | `AUD-03b` *(đề xuất)*                       | USER gọi probe **không** đặc quyền (`user-lock`) bị 403                                                              | 0 dòng audit                                                                                              |
   | `ANY-01`                                    | Vai trò chỉ có `role.assign` gọi `privileged-any`; USER gọi                                                          | 200 · 403                                                                                                 |
   | `Privileged_controllers_carry_the_attribute` | Reflection trên assembly module: mọi action của controller có `GroupName` `admin-v1`/`moderation-v1` — trừ đúng `POST /reports` — có attribute (action hoặc class) | xanh. Ca canh gác "≥ 1 controller như vậy" mang `Skip = "Gỡ ở D2 — chưa có controller admin-v1 nào"` |

### Cạm bẫy đã biết

1. **Ghi 503 ngay trong `OnTokenValidated`** — L-C6. Test `FC-01` so status **và** `type`: 401 thì đỏ đúng chỗ.
2. **Audit cả 401** (chưa đăng nhập gọi `/admin/*`) — không có `actor_id` (cột `NOT NULL`), và là cửa cho người lạ làm ngập bảng.
   Chỉ `Forbidden`.
3. **Khóa chống ngập theo `Request.Path`** → mỗi id một khóa → `AUD-03` ra 5 dòng. Theo `routeTemplate`.
4. **`AddSingleton<IAuthorizationMiddlewareResultHandler>` hai lần** (một ở SharedKernel, một "thêm cho chắc" ở Program) → cái sau
   thắng im lặng. Một chỗ đăng ký.
5. **Sửa `IsRevokedAsync` đổi hành vi** (ví dụ ném khi `Unknown`) — filter hub của B gọi nó; B không biết GĐ6 sửa (Mục 9.4 hàng
   `TokenRevocationExtensions`).
6. **`PermissionPolicyProvider` trả `null` cho `perm-any:`** vì quên nhánh → rơi về mặc định → policy không tồn tại → 500 lúc chạy,
   không phải lúc build. `ANY-01` bắt.

---

## 11. C5 — `IAccountStatusReader`

**Mục tiêu:** module khác biết tài khoản nào không hoạt động mà không đọc bảng của Identity.

**Kết quả mong đợi:** `SharedKernel/Contracts/IAccountStatusReader.cs` + `Identity/Infrastructure/AccountStatusReader.cs`; test tích hợp xanh.

### Các bước

1. Hợp đồng — cùng ba luật Đ-2.3 ghi ở `IUserDirectory` (chỉ đọc · chỉ chiếu · batch, không bản đơn):

   ```csharp
   /// Tập con của userIds có status <> 'active' (disabled, deleted, và 'locked' — giá trị không ai ghi, Đ-6.5).
   /// Id không tồn tại KHÔNG có trong kết quả — người gọi không suy "không tồn tại" từ hàm này.
   Task<IReadOnlySet<Guid>> GetInactiveAsync(IReadOnlyCollection<Guid> userIds, CancellationToken ct = default);
   ```

2. Hiện thực scoped trên `IdentityDbContext`: LINQ `Where(u => ids.Contains(u.UserId) && u.Status != UserStatus.Active)` (Npgsql dịch
   thành `= ANY(@p)`); danh sách rỗng → trả rỗng **không** chạm DB. Đăng ký trong `AddIdentityModule`.
3. Test `AccountStatusReaderTests` (khuôn `UserDirectoryTests`): 3 id (active, disabled, không tồn tại) → `{disabled}`; `SqlCommandCounter`
   đếm **1** câu cho 50 id. `SRCH-05` đầy đủ là của D12.

---

## 12. C2 — `IModerationTargets`

**Mục tiêu:** ẩn/khôi phục đối tượng trong transaction của Moderation, mà mỗi module vẫn là người duy nhất viết SQL vào bảng của mình.

**Kết quả mong đợi:** hợp đồng + composite ở SharedKernel; provider bài (Content), provider người dùng (Profile); `TX-02` (bản hạ
tầng), `HID-*` phần store, `WriteContracts_are_only_the_two_named` xanh.

### Các bước

1. **SharedKernel/Moderation/** (cạnh `ModerationTargetType` của C0):

   ```csharp
   public readonly record struct ModerationTarget(ModerationTargetType Type, Guid Id);
   public sealed record TargetSnapshot(ModerationTarget Target, string Status, Guid? AuthorId, string? Body,
       IReadOnlyList<string> MediaKeys, Guid? PostId, DateTimeOffset CreatedAt, DateTimeOffset? EditedAt);
   public enum HideOutcome { Hidden, AlreadyHidden, NotFound }     // Restore: Restored, NotHidden, NotFound — enum riêng
   public interface IModerationTargets { GetSnapshotsAsync · CanViewAsync(actorId, target) · HideAsync(tx, …) · RestoreAsync(tx, …) }
   public interface IModerationTargetProvider { ModerationTargetType Type { get; } + cùng bốn phương thức cho MỘT loại }
   internal sealed class ModerationTargets(IEnumerable<IModerationTargetProvider> providers) : IModerationTargets   // L-C9
   ```

   Snapshot mang `AuthorId` + `MediaKeys`, **không** `UserCard` + URL: hydrate tên và ký URL là việc của D7 (một lô `IUserDirectory`,
   `IObjectStorage`) — cùng luật "SharedKernel không biết R2" của `UserCard`. Composite: gom `targets` theo `Type` → mỗi provider một
   lời gọi batch; loại không có provider (bình luận trước khi A merge) → `GetSnapshots` vắng mặt, `CanView` false, `Hide` ném
   `NotSupportedException` (D6 map thành 404 trước khi tới đây). Đăng ký composite `TryAddScoped` trong `AddSharedKernel`.
   Comment đầu `IModerationTargets` ghi lệch Đ-2.3 (Đ-6.3).
2. **Content** — `Modules/Content/Infrastructure/Moderation/ContentModerationTargets.cs` (`Type = Post`), đăng ký trong
   `AddContentModule` là `IModerationTargetProvider`:
   - `HideAsync(tx, t, reasonCode)`:
     `UPDATE content.posts SET status='hidden', hidden_reason=$2, updated_at=$3 WHERE post_id=$1 AND status='published' RETURNING post_id`
     trên `tx.Connection`; 0 dòng → `SELECT status … WHERE post_id=$1`: `hidden` → `AlreadyHidden`; `deleted` hoặc không có →
     `NotFound`. `updated_at` từ `TimeProvider` — nó chính là `hiddenAt` (Mục 4, không có cột `hidden_at`).
   - `RestoreAsync`: đối xứng (`hidden → published`, `hidden_reason = NULL`).
   - `GetSnapshotsAsync`: SQL thô một câu `= ANY(@ids)` **kể cả `deleted`** — Moderator phải thấy (Mục 8.1); đi vòng global query
     filter một cách tường minh, comment ghi lý do.
   - `CanViewAsync(actor, post)`: `status = 'published'` **và** `PostVisibility.CanView(privacy, author, actor, areFriends)` với
     `IFriendshipReader` — tái dùng đúng hàm thuần, không viết lại BR-02.
3. **Profile** — `Modules/Profile/Infrastructure/Moderation/ProfileModerationTargets.cs` (`Type = User`): snapshot từ `profiles` +
   `IAccountStatusReader` (C5) → `Status = active|disabled`; `CanView` = có hồ sơ; `Hide`/`Restore` ném `NotSupportedException`
   (người dùng không "ẩn" — D7 chặn `hide` cho `user` bằng bảng hợp lệ `decision × targetType` trước).
4. **Test:**

   | Id                                         | Kịch bản                                                                                                                                                             | Kỳ vọng                                                              |
   | ------------------------------------------ | -------------------------------------------------------------------------------------------------------------------------------------------------------------------- | -------------------------------------------------------------------- |
   | `HID-store-01`                             | `HideAsync` bài `published` · lần hai · bài `deleted` · id lạ                                                                                                         | `Hidden` (+ `hidden_reason`, `updated_at` đổi) · `AlreadyHidden` · `NotFound` · `NotFound` |
   | `HID-store-02`                             | `RestoreAsync` bài `hidden` · bài `published`                                                                                                                         | `Restored`, `hidden_reason` NULL · `NotHidden`                       |
   | `TX-02` *(bản hạ tầng)*                    | `ModerationDbContext.BeginTransaction` → chèn một báo cáo → `HideAsync(tx)` → `AppendAsync(tx)` → ném                                                               | bài vẫn `published`; không báo cáo; không audit                      |
   | `CanView-01`                               | Bài `private` của B, người gọi A · bài `friends`, A là bạn · bài `hidden`, A là tác giả khác                                                                          | false · true · false                                                 |
   | `Feed-sau-khi-an` *(đề xuất)*              | Nạp feed (cache trang đầu), ẩn một bài trong đó, nạp lại trong 30 giây                                                                                                | bài không còn — kiểm 2026-09-23: cache chỉ giữ id, `FindManyPublishedAsync` lọc lúc đọc; ca này khóa hành vi đó |
   | `WriteContracts_are_only_the_two_named`    | Architecture: (a) mọi **interface** có phương thức nhận `DbTransaction` nằm trong `SharedKernel.Audit` hoặc `SharedKernel.Moderation`; (b) mọi lớp trong module có phương thức public nhận `DbTransaction` phải hiện thực một interface ở (a) | xanh; **thử cho đỏ**: thêm tạm `interface IFoo { Task X(DbTransaction t); }` vào Content → đỏ nêu tên |

### Cạm bẫy đã biết

1. **Hiện thực ở `Application/`** — `PersistenceBoundaryTests` đỏ vì Npgsql. Đúng chỗ: `Infrastructure/Moderation/`.
2. **`WriteContracts` viết ngây thơ "chỉ hai namespace có phương thức nhận `DbTransaction`"** → đỏ ngay với chính hai provider hợp lệ
   ở Content/Profile. Luật đúng là hai vế (a) + (b).
3. **Phân biệt `AlreadyHidden`/`NotFound` bằng một `SELECT` trước `UPDATE`** — đua với Moderator thứ hai; `UPDATE … RETURNING` trước,
   `SELECT` chỉ để **giải thích** vì sao 0 dòng.
4. **Quên `status = 'published'` trong `CanView`** — `PostVisibility.CanView` không xét trạng thái; bài bị ẩn thành "báo lại được"
   bởi người thấy nó trước khi ẩn, và `POST /reports` thành máy dò bài bị ẩn.
5. **Provider giữ `ContentDbContext` rồi `SaveChanges` trên nó** trong `HideAsync` — `DbContext` thứ hai, kết nối thứ hai: đúng lỗi
   R6-06. Không đụng `ContentDbContext` ở hai phương thức ghi.

---

## 13. C6 — `NotificationHub` *(chờ B merge vé)*

**Mục tiêu:** đẩy thông báo tới tab đang mở; không viết lại gì của B.

**Kết quả mong đợi:** hub map được, hai ca auth xanh, cổng hợp đồng hub xanh.

**Điều kiện bắt đầu** — cả bốn phải có trên `develop`: `SharedKernel/Realtime/` (scheme `RealtimeTicket`, `RevocationHubFilter`,
`IUserIdProvider` đọc `sub`), `POST /realtime/tickets`, `MapHub` của `/hubs/chat`, khuôn `HubAuthZTests` + `ChatHubContractTests`.
Thiếu một → **không làm**, ghi "chờ GĐ5" vào checklist (R6-01), FE chạy chế độ hỏi lại 30 giây (Đ-6.18 — đó là đường lùi vĩnh
viễn, không phải tạm). Theo quyết định ở C0 (2026-09-23): **không chờ, không hỏi** người làm GĐ5 — kiểm `develop` là đủ biết.

### Các bước (khi đủ điều kiện)

1. `Notification/Presentation/NotificationHub.cs` — `[Authorize(AuthenticationSchemes = RealtimeTicketDefaults.Scheme)]`, **không
   phương thức nào** (Đ-6.18: không cửa tầng 3 nào để canh).
2. `NotificationPusher` (Notification/Infrastructure) — `IHubContext<NotificationHub>`; D9 gọi **sau `COMMIT`** của upsert:
   `Clients.User(recipient).SendAsync("NotificationUpserted", item, unreadTotal)`. Lỗi đẩy → log, không ném (thông báo đã lưu).
3. `Program.cs`: `app.MapHub<NotificationHub>("/hubs/notifications")` cạnh dòng của B; cùng `RevocationHubFilter`.
4. `notification-hub-v1.md` + `.examples.json` (Mục 8.4) + `NotificationHubContractTests` + `Content Include` trong csproj test (khuôn
   `ChatHubContractTests`).
5. Test (khuôn `HubAuthZTests`): không vé → 401; vé đã dùng → 401; hai người A, B nối; upsert cho A → chỉ A nhận.

### Cạm bẫy đã biết

- **Sửa `SharedKernel/Realtime/`** "cho vừa" — cấm (Mục 9.4). Thiếu gì thì PR riêng, nhỏ, **sau khi** GĐ5 đã merge — không sửa
  thư mục đó trong PR khối GĐ6.
- **Gộp chung hub với chat** — Đ-6.18 đã cân nhắc rồi loại: hub thông báo riêng. Nếu lúc thi công GĐ5 đã gộp sẵn một hub chung thì
  theo thứ đã có trên `develop` và ghi vào "Thực tế thi công".

---

## 14. Kế hoạch commit

Một mã việc một commit (commit-rules Mục 8). Mỗi commit: build sạch, bốn bộ test xanh (trừ đỏ nền R2), `detect-changes` chạy và ghi
vào thân, **không** dòng ghi công.

| #   | Tiêu đề (≤ 95 ký tự)                                                                                  | Đi kèm trong cùng commit                                                                         |
| --- | ----------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------ |
| 1   | `feat(gd6-a): A1 — module Moderation: báo cáo, nhật ký append-only do trigger giữ, nối migrate`        | [0] cho csproj Moderation; `AuditActions` (SharedKernel); Program.cs + harness; namespace guard   |
| 2   | `feat(gd6-a): A3 — mã quyền role.manage, mô tả 18 quyền, trigger chặn sửa vai trò hệ thống`            | sequence `roles_role_id_seq`; bốn test GĐ1 sửa theo; `giai-doan-6.md` A3/Mục 4/Mục 5 theo L-A1..A4 |
| 3   | `feat(gd6-a): A2 — module Notification: thông báo gộp theo group_key, nối migrate`                     | [0] cho csproj Notification; Program.cs + harness; namespace guard                                |
| 4   | `feat(gd6-a): A4 — tìm tên không dấu: unaccent, pg_trgm, hàm IMMUTABLE, index GIN`                     | script `tests/load/search/seed-profiles.sql`; kết quả `EXPLAIN` vào "Thực tế thi công"             |
| 5   | `feat(gd6-a): A5 — hằng quyền cục bộ của Moderation, có test đối chiếu PermissionCodes`                 | —                                                                                                |
| 6   | `feat(gd6-c): C1 — IAuditTrail ghi audit trong transaction của người gọi`                             | —                                                                                                |
| 7   | `feat(gd6-c): C3 — sửa quyền có hiệu lực ngay: invalidate tại chỗ, pub/sub, IsAllowedAsync dùng chung` | hai fake `IPermissionCache`                                                                      |
| 8   | `feat(gd6-c): C4 — endpoint đặc quyền fail-closed, audit khi bị từ chối, policy any-of`               | fake `ITokenRevocationStore`; probe controller                                                   |
| 9   | `feat(gd6-c): C5 — IAccountStatusReader: lọc tài khoản không hoạt động theo lô`                        | —                                                                                                |
| 10  | `feat(gd6-c): C2 — IModerationTargets: ẩn/khôi phục bài trong transaction của Moderation`             | test `WriteContracts_are_only_the_two_named`                                                      |
| 11  | `feat(gd6-c): C6 — hub thông báo trên vé realtime của GĐ5` *(khi B merge)*                            | hợp đồng hub + cổng                                                                              |

Thân commit theo commit-rules Mục 5 — mục **"Lệch …"** là bắt buộc với commit 1, 2, 4, 6, 7, 8, 10 (có chỗ lệch ở Mục 0.4). Ví dụ:

```
feat(gd6-a): A3 — mã quyền role.manage, mô tả 18 quyền, trigger chặn sửa vai trò hệ thống

Đ-6.9 lớp chặn 3 + nợ GĐ1 Mục 2 (permissions.description) ở tầng dữ liệu.
- PermissionCodes: role.manage (18) cuối All, không vào bootstrap USER/MODERATOR
- Trigger trg_roles_protect_system: BEFORE UPDATE OF code OR DELETE, chỉ ba vai trò hệ thống
- Sequence identity.roles_role_id_seq START 100 cho vai trò tự tạo (D5)
Lệch B.4 A3 (chốt 2026-09-…): không ADD COLUMN description — cột có từ InitialIdentity; mô tả đi hai đường (seeder chèn
kèm cho DB mới, migration UPDATE cho DB đã seed) vì migration chạy trước seeder.
Sửa bốn test GĐ1 bị trigger/mã 18 làm đỏ: …

Test: Unit … → …, Integration … → … (+ROLE-05, ROLE-07, …), Architecture … → …. Thử cho đỏ N đột biến đều bị bắt.
detect-changes: …
```

PR: hai khối đi chung **một PR khối GĐ6** cùng D, B, E (pull-request-rules Mục 2 — "một PR = một khối"; GĐ6 chỉ tách đường ray C0).
**Không** mở PR khi chưa được bảo; **không** tự merge.

---

## 15. Ranh giới — cái gì **không** thuộc hai khối

| Việc                                                                                       | Thuộc                     | Ghi chú                                                                                          |
| ------------------------------------------------------------------------------------------ | ------------------------- | ------------------------------------------------------------------------------------------------ |
| Controller, DTO, validator, `[ApiExplorerSettings]`, `AddApplicationPart`, `apiGroups`     | **D0–D13**                | A, C không có endpoint thật nào; probe của C4 chỉ sống trong assembly test                      |
| `AdminInvariant.EnsureRemainsAsync`, khóa tư vấn, thứ tự DB → Redis                        | **D3**                    | C4 chỉ dựng bên đọc `CheckAsync`; bên ghi `RevokeUserAsync` đã có từ GĐ1                         |
| `LoginService` kiểm `disabled`, `RotateAsync` thêm `status = 'active'`, `/me.permissions`  | **D1**                    | Dữ liệu (`status 'disabled'`) đã có từ GĐ1 — không migration nào                                 |
| `RolePermissionDiff`, 409 `confirmation-required`, dịch `P0001`/`23503` thành 409          | **D5**                    | A3 chỉ dựng trigger + sequence                                                                   |
| `INSERT … ON CONFLICT` của báo cáo, upsert gộp thông báo                                   | **D6**, **D9**            | A1, A2 chỉ dựng index/constraint mà hai câu đó dựa vào                                           |
| `PostResponse.moderation`, nhánh tác giả ở `PostReadService`, 409 `post-hidden`             | **D7**                    | C2 chỉ ghi, không đọc cho người dùng                                                             |
| `HideAsync` cho **bình luận**, `hidden` trong `ck_comments_status`                          | **A (GĐ3)** + bước 9      | `ck_comments_status` hiện `('visible','deleted')` (InitialContent); A mở rộng. GĐ6 thêm provider `Comment` sau khi A merge |
| Helper dựng cảnh (Admin thứ hai, vai trò tự tạo, báo cáo mở), dòng AuthZ matrix, test đồng thời | **B1**, **B2**, **B3** | L-A7: dòng migrate trong harness đã đi cùng A1/A2                                                |
| Ba file hợp đồng yaml, `notification-hub-v1.md`                                            | Cổng mở / **C6**          | —                                                                                                |
| Job xóa audit 12 tháng, dọn thông báo 90 ngày                                              | **GĐ8**                   | A1 chừa cửa `socialapp.audit_purge`                                                              |
| Sửa `SharedKernel/Realtime/`                                                               | Không bao giờ             | Mục 9.4                                                                                          |

---

## 16. Hai khối để lại gì

| Cho ai               | Để lại                                                                                                   | Còn nợ — bản đầy đủ của ca test (L-C2)                                                                  |
| -------------------- | -------------------------------------------------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------- |
| **D1**               | `permissions.description`, `role.manage`                                                                 | `ME-01`                                                                                                 |
| **D3–D5**            | `IAuditTrail`, `[PrivilegedEndpoint]`, `[RequireAnyPermission]`, `IPermissionChangeNotifier`, trigger + sequence | `PERM-01` qua API; `ROLE-01..04, 06`; gỡ `Skip` của `Privileged_controllers_carry_the_attribute` ở D2 + thử cho đỏ |
| **D6–D7**            | `IModerationTargets`, bảng `reports`, `IsAllowedAsync`                                                   | `TX-01`/`TX-02` qua `PATCH /reports` (Đ-6.13); `AUD-01` có chuỗi `SECRET-xyz`; `HID-01..06` qua `GET /posts` |
| **D8**               | `audit_logs` + hai index                                                                                 | `AUD-04`                                                                                                |
| **D9–D11**           | Bảng `notifications`, `notification_actors`, `GroupKey`, `NotificationTypes`                              | `NOTIF-*`                                                                                               |
| **D12**              | `profile.search_norm`, index GIN, `IAccountStatusReader`, script seed tên Việt                            | `SRCH-05`, `SRCH-07` (dùng script A4 cho `EXPLAIN` ở quy mô)                                            |
| **B5**               | Đột biến "kết nối riêng" đã chạy ở C1                                                                    | Bảng đột biến trong PR                                                                                  |
| **GĐ7**              | Kênh pub/sub có tiền tố môi trường                                                                       | Hai bản sao API thấy thay đổi quyền cùng lúc (khối E GĐ7)                                               |
| **GĐ8**              | Cửa `socialapp.audit_purge`; `IAccountStatusReader`                                                      | Job xóa audit 12 tháng; lọc tài khoản đã xóa ở mọi danh sách công khai                                  |

---

## 17. Checklist nghiệm thu

**Khối A — dữ liệu** (khớp Mục 12 "Dữ liệu (A)")

- [ ] `--migrate` hai lần liên tiếp trên DB sạch: lần hai không đổi gì, exit 0; `\dn` thấy `moderation`, `notification`
- [ ] Mọi CHECK, index một phần của Mục 4 có mặt đúng chữ (đọc lại DDL, có schema test)
- [ ] `UPDATE`, `DELETE`, `TRUNCATE` trên `moderation.audit_logs` bị từ chối; `DELETE` với cờ purge thì được (`AUD-02`)
- [ ] Đổi `code` / xóa ba vai trò hệ thống bị từ chối; đổi `display_name` được (`ROLE-05`)
- [ ] `SELECT count(*) FROM identity.permissions WHERE description IS NULL` = 0 trên DB mới **và** DB đã seed từ GĐ1
- [ ] `\dx` có `unaccent`, `pg_trgm`; `\df+ profile.search_norm` là `immutable`; `EXPLAIN` 20.000 hồ sơ trúng GIN ×3 (dán kết quả)
- [ ] Sau deploy staging (F1): `\dx` có `unaccent`, `pg_trgm` — ghi vào "Thực tế thi công" (repo cho thấy user migrate là superuser, Mục 6 bước 4)
- [ ] Bốn test GĐ1 (`…_18_ma`, `SEED-01`, `SEED-03`, `FK-01`) sửa trong commit A3, xanh
- [ ] `Moderation_Domain_…`, `Notification_Domain_…` namespace guard xanh, đã thử cho đỏ

**Khối C — hạ tầng**

- [ ] Hai hiện thực hợp đồng ghi dùng `tx.Connection` + tham số hóa; không `new NpgsqlConnection` khi có `tx` (B.10 #6)
- [ ] `TX-01` (hạ tầng) đỏ khi `IAuditTrail` mở kết nối riêng — đã chạy đột biến
- [ ] `PermissionHandler` gọi `IsAllowedAsync`; `grep -rn '"ADMIN"' src/` chỉ ra `SystemRoles.cs`
- [ ] `PERM-01` (hạ tầng) + đối chứng, `PERM-02` xanh; kênh có tiền tố môi trường
- [ ] `FC-01`, `AUD-03`, `ANY-01` xanh; `IsRevokedAsync` giữ nguyên hành vi
- [ ] `Privileged_controllers_carry_the_attribute` có mặt, `Skip` ghi "gỡ ở D2"
- [ ] `WriteContracts_are_only_the_two_named` xanh, đã thử cho đỏ
- [ ] Không log nào chứa `AuditEntry`, `note`, `ip`, `email` (B.10 #5)
- [ ] C6: làm **hoặc** ghi "chờ B" — không xóa dòng

**Quy trình**

- [ ] Mỗi đầu việc một commit, có `Test:` và `detect-changes:`, không dòng ghi công
- [ ] `giai-doan-6.md` (B.4, B.5, Mục 4, Mục 5, Mục 10.1, Mục 13) sửa theo các L đã chốt **trong cùng commit**, có ngày
- [ ] Impact Mục 1.2 chạy lại trước mỗi đầu việc chạm symbol có sẵn

**Bảng đột biến — mỗi dòng phải bị đúng ca tương ứng bắt**, file khôi phục nguyên byte sau mỗi lượt:

| Đột biến                                                              | Ca đỏ                                        |
| --------------------------------------------------------------------- | -------------------------------------------- |
| Bỏ trigger `BEFORE TRUNCATE` khỏi migration A1                        | `AUD-02` vế `TRUNCATE`                       |
| Trigger `roles` bỏ `OF code`                                          | `ROLE-05` vế `display_name`                  |
| Seeder chèn không kèm `description`                                   | `SEED-01` (khẳng định không NULL)            |
| `ModerationPermissions.ReportResolve = "report.reslove"`              | `ModerationPermissionsTests`                 |
| `SqlAuditTrail` mở kết nối riêng dù có `tx`                           | `TX-01` (hạ tầng)                            |
| `NotifyAsync` bỏ `Invalidate` tại chỗ                                 | `PERM-01`                                    |
| `PermissionCache` ghi entry bất kể thế hệ                             | `PERM-03`                                    |
| `OnTokenValidated` bỏ nhánh `Unknown when privileged`                 | `FC-01`                                      |
| Khóa chống ngập dùng `Request.Path`                                   | `AUD-03`                                     |
| `CanViewAsync` bỏ điều kiện `status = 'published'`                    | `CanView-01` vế 3                            |
| Thêm interface nhận `DbTransaction` ở Content                         | `WriteContracts_are_only_the_two_named`      |

---

## Thực tế thi công

**2026-09-23 — chốt trước khi thi công:** hai mươi chỗ lệch Mục 0.4 đi theo đề xuất (chi tiết ở đó). C6 không chờ, không hỏi người
GĐ5 — theo quyết định ở C0.

### A1 — 2026-09-23

Làm đúng Mục 3; L-A5 (`AuditActions` ở SharedKernel), L-A6 (chặn `TRUNCATE`), L-A7 (Program.cs + hai harness), L-A8 (namespace
guard) áp như chốt. `giai-doan-6.md` sửa cùng commit: Đ-6.15 (vị trí `AuditActions`, trigger `TRUNCATE`), Mục 4 (`id` identity,
trigger thứ hai), B.4 A1 và A5, B.7 B1 — mỗi chỗ ghi "sửa 2026-09-23".

**Lệch so với chính tài liệu này:**
- **`.gitkeep` giữ nguyên** ở `Moderation/Domain`, `Infrastructure` — Identity, Content, Profile đều còn `.gitkeep` cạnh file thật;
  xóa là đổi nếp, không có lý do.
- **Thêm ca ngoài bảng:** `Report_on_conflict_voi_index_mot_phan_khong_chen_trung` — chạy đúng câu `ON CONFLICT … WHERE status =
  'open' DO NOTHING` mà D6 sẽ viết, để hình dạng câu được khóa từ tầng dữ liệu. CHECK có thêm ca `ck_reports_status`.
- **`ClearPool` trong test schema** — cạm bẫy 7 Mục 3, gặp thật: lượt đầu Integration 435/495, 59 ca đỏ `53300 too many clients
  already`; thêm `ClearPool` → 494/495.

**Kiểm tay trên DB dev:** `dotnet run --project src/backend/SocialApp.Api -- --migrate` ba lần, đều exit 0; `moderation."__EFMigrationsHistory"`
đúng một dòng `20260923151519_InitialModeration`; `\dn` thấy `moderation`; hai trigger `trg_audit_logs_append_only`,
`trg_audit_logs_no_truncate` có mặt.

**Test:** Unit 303 → 306 (+3 `AuditActionsTests`), Integration 484 → 495 (+11 `ModerationDbContextSchemaTests`), Architecture 16 → 17
(+`Moderation_Domain_namespace_must_not_be_empty`). Integration còn **một đỏ nền trên máy dev**, có từ trước:
`StartupConfigurationTests.Development_boots_without_r2_config_and_first_use_names_the_variables` (user-secrets có khóa R2; CI xanh).

**Thử cho đỏ — 3/3 đột biến bị bắt**, file khôi phục nguyên byte (`cmp`):

| Đột biến                                                          | Ca đỏ thực tế                                         |
| ----------------------------------------------------------------- | ----------------------------------------------------- |
| Bỏ trigger `BEFORE TRUNCATE` khỏi migration                       | `AUD_02_update_delete_truncate_audit_deu_bi_tu_choi`  |
| Đổi namespace trong test guard thành `…Moderation.Domainx`        | `Moderation_Domain_namespace_must_not_be_empty`       |
| `RoleDelete = "role.rename"` (trùng chuỗi)                        | `AuditActionsTests.Khong_hai_hang_nao_trung_chuoi`    |

**detect-changes:** low, 0 luồng (24 file, 17 symbol — `ModulesApiFactory.CreateMigratedDatabaseAsync`, `PersistenceBoundaryTests`,
các mục tài liệu). Impact trước khi sửa: `PostgresFixture.SeededContentDatabaseAsync` MEDIUM (5 lớp test đọc DB chung — chỉ thêm
một lượt migrate, không đổi hành vi), `ModulesApiFactory.CreateMigratedDatabaseAsync` LOW.

### A3 — 2026-09-23

Làm đúng Mục 5; L-A1 (không `ADD COLUMN`), L-A2 (mô tả hai đường, seeder có sửa), L-A3 (bốn test GĐ1), L-A4 (sequence từ 100) áp
như chốt. `dotnet ef migrations add` chỉ sinh `CreateSequence` — xác nhận L-A1 tại chỗ. `giai-doan-6.md` sửa cùng commit: Đ-6.9,
Mục 4 phần Identity, Mục 5 hàng `description`, B.4 A3.

**Lệch so với chính tài liệu này:**
- **Migration `UPDATE` cả 18 mã**, không 17: dòng `role.manage` chưa có trên DB đã seed nên câu thứ 18 chạm 0 dòng — vô hại, và DB
  nào lỡ chạy seeder mới trước (không có đường nào như vậy, nhưng) cũng được điền.
- **Test mới gom ở `IdentitySystemRoleGuardTests`** (không rải vào `IdentitySeederTests`): `ROLE-05` ba ca bị chặn (Theory) + ca
  được phép (`display_name`, `SET code = code`) + ca vai trò tự tạo đổi/xóa tự do; `Mo_ta_quyen_duoc_dien_cho_DB_da_seed_tu_GD1`
  (migrate tới `InitialIdentity` bằng `IMigrator`, chèn 17 dòng như seeder GĐ1, rồi `MigrateIdentityModuleAsync`);
  `Mo_ta_da_co_khong_bi_migration_de` *(thêm ngoài bảng — canh `AND description IS NULL`)*; `Sequence_vai_tro_tu_tao_bat_dau_tu_100`.
  `ROLE-07` ở `IdentitySeederTests`. Unit `PermissionCodesTests` (mọi mã có mô tả, ≤ 120 ký tự, `role.manage` ở cuối).
- **`IdentitySeederTests` thêm `ClearPool` ở `DisposeAsync`** — cùng khuôn A1; lớp này cũng một DB mỗi ca.

**Kiểm tay trên DB dev** (đã seed từ GĐ1 — đúng hình dạng staging): `--migrate` hai lần, exit 0; 18 quyền, **0** mô tả NULL; lịch sử
Identity có `20260923153649_SystemRoleGuardAndPermissionDescriptions`; `trg_roles_protect_system` có mặt; `roles_role_id_seq`
`last_value = 100, is_called = false`; `psql` `UPDATE identity.roles SET code = 'X' WHERE code = 'USER'` → *"Vai trò hệ thống USER
không đổi mã, không xóa được"*.

**Test:** Unit 306 → 309 (+3 `PermissionCodesTests`), Integration 495 → 504 (+5 `ROLE-05`, +2 mô tả, +1 sequence, +1 `ROLE-07`),
Architecture 17 → 17 (đổi tên `…_17_ma` → `…_18_ma`). Còn đỏ nền R2 trên máy dev như A1.

**Thử cho đỏ — 4/4 đột biến bị bắt**, file khôi phục nguyên byte (`cmp`):

| Đột biến                                                          | Ca đỏ thực tế                                                                                 |
| ----------------------------------------------------------------- | --------------------------------------------------------------------------------------------- |
| Hàm trigger chặn mọi `UPDATE` (bỏ so `IS DISTINCT FROM`)          | `ROLE_05_doi_display_name_va_update_giu_nguyen_code_thi_duoc`                                 |
| Bỏ `WHEN (OLD.code IN …)`                                         | `ROLE_05_vai_tro_tu_tao_doi_code_va_xoa_duoc`                                                 |
| Seeder chèn `NULL` thay mô tả                                     | `SEED_01`, `ROLE_07`, `Mo_ta_quyen_duoc_dien_cho_DB_da_seed_tu_GD1`                           |
| Migration bỏ `AND description IS NULL`                            | `Mo_ta_da_co_khong_bi_migration_de`                                                           |

Lượt đầu của đột biến 3 viết sai (bỏ cột `description` khỏi danh sách cột nhưng giữ giá trị → lệch số cột, cả lớp đỏ vì lỗi SQL
chứ không vì mô tả) — không tính, làm lại bằng `NULL`.

**detect-changes:** medium, 1 luồng — `MigrateIdentityModuleAsync → Literal`, đổi `PermissionsSql`: có chủ đích (seeder chèn kèm
mô tả, L-A2). Impact trước khi sửa: `IdentitySeeder` LOW (15), `PermissionsSql` LOW (8), `IdentityDbContext` LOW (1),
`PermissionCodes` UNKNOWN — text search: seeder + ba test đối chiếu.

### C1 — 2026-09-23

Làm đúng Mục 8; L-C1 (kết nối của `ModerationDbContext` cho `tx == null`, `AddHttpContextAccessor` trong module) và L-C2 (bản hạ
tầng của `TX-01`, `AUD-01`) áp như chốt. `giai-doan-6.md` B.5 C1 và `AGENTS.md` Mục 5 (dòng "hai hợp đồng ghi") sửa cùng commit.

**Lệch so với chính tài liệu này:**
- Tham số SQL khai **kiểu tường minh** (`NpgsqlDbType.Uuid`, `Jsonb`, `Inet`, `TimestampTz`) — `metadata` là chuỗi JSON, không
  khai `Jsonb` thì Npgsql gửi `text` và Postgres từ chối gán vào cột `jsonb`.
- **Thêm hai ca ngoài bảng:** `Khong_request_thi_ip_null_va_khong_metadata_thi_null` (job nền không có `HttpContext`) và
  `Transaction_da_dong_thi_nem` (tx đã commit → `InvalidOperationException`, không lặng lẽ ghi trên kết nối khác).
- Test dùng **đồng hồ giả** (`TimeProvider` con) và **`IHttpContextAccessor` giả**, đăng ký TRƯỚC `AddModerationModule` — cả hai
  dòng của module là `TryAdd`.

**Test:** Unit 309 → 309, Integration 504 → 509 (+5 `AuditTrailTests`), Architecture 17 → 17. Còn đỏ nền R2 trên máy dev.

**Thử cho đỏ — 2/2 đột biến bị bắt**, file khôi phục nguyên byte (`cmp`):

| Đột biến                                                                            | Ca đỏ thực tế                                                                           |
| ----------------------------------------------------------------------------------- | --------------------------------------------------------------------------------------- |
| Có `tx` nhưng ghi trên kết nối của `ModerationDbContext` (đột biến bắt buộc của B5) | `TX_01_thao_tac_Identity_nem_sau_audit_thi_ca_hai_rollback` — *Expected "0", Actual "1"*: dòng audit sống sót sau rollback, đúng lý do |
| Bỏ `MapToIPv4`                                                                      | `AUD_01_khong_tx_ghi_ip_metadata_va_dong_ho_cua_app`                                    |

Ca `TX_01_commit_…` **vẫn xanh** dưới đột biến 1 — đúng như dự đoán ở cạm bẫy 1 Mục 8: ghi sai kết nối "chạy tốt mọi lúc trừ lúc
lỗi". Vì vậy ca rollback mới là bằng chứng, không phải ca commit.

### C3 + C4 — 2026-09-23

Làm cùng một lượt vì C4 dùng `IsAllowedAsync` của C3 (handler any-of). L-C3..L-C7, L-C10 áp như chốt. `giai-doan-6.md` (B.5 C3, C4;
Mục 10.1 thêm `PERM-03`, `ANY-01`, `AUD-03b`) và `AGENTS.md` (khung tầng 3: short-circuit ở `PermissionChecks.IsAllowedAsync`,
`[PrivilegedEndpoint]`) sửa cùng lượt.

**Lệch so với chính tài liệu này:**
- **`IsAllowedAsync` là extension method** (`PermissionChecks`) trên `IPermissionCache`, không phải thành viên interface như Mục 9
  bước 1 viết: đúng MỘT hiện thực short-circuit, và fake trong test không phải chép nó — ca `Admin_qua_ma_khong_cham_cache` vẫn
  đúng nghĩa. Interface chỉ thêm `Invalidate`, `InvalidateAll`.
- **Gỡ entry bằng ghi-rồi-kiểm** thay cho so-rồi-ghi của Mục 9 bước 2: `Invalidate` chen giữa "so thế hệ" và "ghi" vẫn để lại entry
  cũ; ghi trước rồi kiểm thế hệ, đổi thì `TryRemove` đúng entry vừa ghi.
- **Subscriber đăng ký vô điều kiện**, tự thoát khi không có `RedisConnection` — thay cho đăng ký có điều kiện ở Mục 9 bước 5
  (thứ tự `AddSharedKernelAuthorization` / `AddSharedKernelRedis` trong Program.cs không còn quan trọng).
- **`CheckAsync` vẫn tự ghi cảnh báo fail-open** (Mục 10 bước 2 ghi "không log ở đây"): `OnTokenValidated` giờ gọi `CheckAsync`, và
  `RV04` khẳng định có log khi Redis chết. `IsRevokedAsync` = `CheckAsync == Revoked`.
- Test đặt tên theo lớp: `PermissionInvalidationTests` (`PERM-01` gộp cả đối chứng "chưa notify vẫn 201" trong một ca, `PERM-02`),
  `FailClosedTests` (`FC-01` + ca không token vẫn 401), `PrivilegedEndpointAuditTests` (`AUD-03`, `AUD-03b`, `ANY-01`), Architecture
  `PrivilegedEndpointTests`. `ModulesApiFactory` thêm hook `UseDatabase(cs)` cho `PERM-02` (hai host chung DB). `PERM-02` chờ bằng
  trạng thái: `PUBSUB NUMSUB` ≥ 2 trước khi báo.

**Lỗi tìm ra khi chạy test, đã sửa:** lượt đầu cả bộ Integration mất **5 phút 31 giây** (trước đó ~2 phút) —
`StartupConfigurationTests` 4 → 41 giây, `ForwardedClientIpTests` 1 → 27 giây. Nguyên nhân: `PermissionsChangedSubscriber` chờ
`RedisConnection.GetAsync()` và `SubscribeAsync` — không nhận token — khi Redis không tới được; host dừng phải chờ `ExecuteAsync`, nên
MỌI lần dừng host chậm theo timeout của thư viện. Sửa: `.WaitAsync(stoppingToken)` ở cả hai lượt chờ. Đo lại hai lớp đó: 40 → 3 giây;
cả bộ: 1 phút 48 giây. So thời gian theo lớp giữa hai file trx là cách tìm ra — không test nào đỏ vì lỗi này.

**Test:** Unit 309 → 314 (+3 `PermissionCacheTests`: invalidate, invalidate một/tất cả, `PERM-03`; +2 `PermissionPolicyProviderTests`),
Integration 509 → 516 (+2 `PERM-*`, +2 `FailClosedTests`, +3 `PrivilegedEndpointAuditTests`), Architecture 17 → 18 xanh + 1 Skip có địa
chỉ (`Privileged_groups_are_not_empty` — gỡ ở D2). Năm ca `PermissionHandlerTests` và bộ `TokenRevocationTests` (gồm `RV04`) xanh
không sửa khẳng định. Còn đỏ nền R2 trên máy dev.

**Thử cho đỏ — 6/6 đột biến bị bắt**, file khôi phục nguyên byte (`cmp`):

| Đột biến                                                           | Ca đỏ thực tế                                                                 |
| ------------------------------------------------------------------ | ----------------------------------------------------------------------------- |
| `NotifyAsync` bỏ `Invalidate` tại chỗ                              | `PERM_01_…`                                                                   |
| `PermissionCache` giữ entry bất kể thế hệ                          | `PERM_03_…` (unit)                                                            |
| `OnTokenValidated` bỏ nhánh `Unknown` khi đặc quyền                 | `FC_01_…`                                                                     |
| Khóa chống ngập theo `Request.Path`                                | `AUD_03_…`                                                                    |
| Subscriber nhận tin mà không xóa cache                             | `PERM_02_…`                                                                   |
| Provider bỏ nhánh `perm-any:`                                      | `ANY_01_…`                                                                    |

Hai lượt đầu của đột biến 5 viết sai (lượt 1 xóa câu lệnh làm vỡ cú pháp; lượt 2 dùng `_ =` mà `_` là tham số lambda) — build lỗi,
test chạy trên DLL cũ và "xanh" — không tính. Lượt 3 dùng `GC.KeepAlive(message)`. Bài học: đọc dòng `Error(s)` của build trước khi
đọc kết quả test đột biến.

**detect-changes:** medium, 2 luồng — `HandleRequirementAsync → Entry` (tầng 2 qua `IsAllowedAsync`, cache có thế hệ) và
`IsRevokedAsync → Slot` (giờ qua `CheckAsync`, hành vi giữ nguyên — `RV04` xanh): cả hai có chủ đích. Impact trước khi sửa:
`PermissionHandler` MEDIUM (5 ca `PermissionHandlerTests` — xanh không sửa khẳng định), còn lại LOW/UNKNOWN đã xác nhận bằng text search.

### A5 — 2026-09-24

Làm đúng Mục 7: `ModerationPermissions` (bốn mã + `All`) ở `Moderation/Application/`, `ModerationPermissionsTests` hai ca chép
khuôn `ContentPermissionsTests`. Không lệch.

**Test:** Architecture 18 → 20 (+2). **Thử cho đỏ — 1/1:** `ReportResolve = "report.reslove"` →
`Moi_ma_cua_ModerationPermissions_deu_co_trong_PermissionCodes` đỏ, thông điệp nêu đúng `report.reslove`; khôi phục nguyên byte.

### A2 — 2026-09-24

Làm theo Mục 4; L-A7 (Program.cs + hai harness), L-A8 (namespace guard cùng commit), L-A9 (`GroupKey` một hàm mỗi loại) áp như chốt.
`giai-doan-6.md` sửa cùng commit: Mục 4 (`target_type`), Mục 10.3 (`GroupKey`), B.4 A2 — mỗi chỗ ghi "sửa 2026-09-24".

**Lệch so với chính tài liệu này:**
- **Entity tên `UserNotification`**, không `Notification` như Mục 4 bước 1: trong namespace `SocialApp.Modules.Notification.*`, tên
  `Notification` được tra ra **namespace** `SocialApp.Modules.Notification` trước khi tới type của `using` — `CS0118`. Đã thử bằng
  một file probe trước khi đổi tên (đỏ đúng `CS0118`). Cùng lý do GĐ2 đặt `UserProfile`. `NotificationActor` giữ tên.
- **`target_type` là `varchar(20)`**, không `varchar(10)` như DDL Mục 4: `conversation` dài 12 ký tự — `varchar(10)` chặn mọi thông
  báo `message` bằng `22001`, đúng lúc B merge. 20 cho cùng cỡ `audit_logs.target_type`. Có ca canh
  (`Tam_loai_va_dich_conversation_deu_chen_duoc`) và hằng `NotificationTargetTypes.MaxLength` mà configuration đọc.
- **`NotificationDbContext` KHÔNG override `SaveChanges` đóng dấu `updated_at`** (Mục 4 bước 2 bảo "chép khuôn"): ở bảng này
  `updated_at` là "lúc sự kiện mới nhất dồn vào nhóm" — khóa sắp `NotificationPage`. Đóng dấu tự động thì đánh dấu đã đọc qua EF (D11)
  làm thông báo cũ nhảy lên đầu danh sách. Upsert D9 tự gán cột trong SQL. Ghi ở doc comment của context và của `UpdatedAt`.
- **`uq_notifications_group` là UNIQUE CONSTRAINT** (`HasAlternateKey`), đúng chữ `CONSTRAINT` của DDL — cạm bẫy Mục 4 bảo "chọn một
  và ghi vào schema test": `UNIQUE_recipient_group_key_la_constraint_va_chan_cap_trung` đọc `pg_constraint contype = 'u'` +
  `pg_get_constraintdef`.
- **Thêm `NotificationTargetTypes.From(ReactionTargetKind)` / `From(ModerationTargetType)`** — `GroupKey` và handler D10 dịch enum
  SharedKernel → chuỗi DB ở một chỗ (khuôn `ReportTargetTypes.From` của A1). Tiền tố của mọi khóa lấy từ hằng `NotificationTypes`.
- **Thêm ca ngoài danh sách:** `On_conflict_do_update_gop_vao_mot_dong` (chạy đúng hình dạng `ON CONFLICT (recipient_id, group_key)
  DO UPDATE … RETURNING (xmax = 0)` của D9), `Hai_index_dung_hinh_dang` (đọc `pg_indexes.indexdef`: `updated_at DESC, id DESC` và
  `WHERE (is_read = false)`); unit: tiền tố khóa = loại, `Guid` chữ hoa ra cùng khóa, enum lạ thì ném, `All` đủ hằng.

**Kiểm tay trên DB dev** (chung lượt với A4): `--migrate` hai lần, exit 0, dòng log có `"notification"`; `\dn` thấy `notification`;
`notification."__EFMigrationsHistory"` đúng một dòng `20260923171431_InitialNotification`.

**Test:** Unit 314 → 334 (+20 `GroupKeyTests`), Integration 516 → 524 (+8 `NotificationDbContextSchemaTests`), Architecture 20 → 21
(+`Notification_Domain_namespace_must_not_be_empty`). Còn đỏ nền R2 trên máy dev.

**Thử cho đỏ — 4/4 đột biến bị bắt**, file khôi phục nguyên byte (`cmp`):

| Đột biến                                                        | Ca đỏ thực tế                                                                                          |
| --------------------------------------------------------------- | ------------------------------------------------------------------------------------------------------ |
| Đổi namespace trong test guard thành `…Notification.Domainx`    | `Notification_Domain_namespace_must_not_be_empty`                                                      |
| `GroupKey.Reaction` bỏ qua `kind` (luôn `post`)                 | `Moi_loai_ra_dung_chuoi_mau` (reaction:comment), `Khoa_khac_nhau_theo_id_va_theo_loai`, `Enum_la_thi_nem` |
| Migration `target_type` về `varchar(10)`                        | `Tam_loai_va_dich_conversation_deu_chen_duoc`                                                          |
| Migration bỏ `UniqueConstraint("uq_notifications_group")`       | `UNIQUE_recipient_group_key_la_constraint_…`, `On_conflict_do_update_gop_vao_mot_dong`                 |

Lượt đầu đột biến 3 không khớp chuỗi (mẫu hai dòng, file migration CRLF) — script dừng ở "0 khớp", không tính; làm lại bằng mẫu
một dòng.

### Các đầu việc còn lại

*Chưa thi công.* Điền khi làm, theo khuôn của C0: chỗ nào phải đổi hướng so với Mục 0.4 và vì sao; lệch so với chính tài liệu này;
kết quả `EXPLAIN` của A4; kết quả kiểm extension trên staging; số test trước → sau; bảng đột biến thực tế; `detect-changes` của
từng commit.
