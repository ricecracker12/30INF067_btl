# Hướng dẫn thực hiện — Khối D. Endpoint + nghiệp vụ (GĐ6)

> Bản triển khai chi tiết của **B.6 Khối D** trong [giai-doan-6.md](giai-doan-6.md). Tài liệu gốc trả lời *cái gì* và *vì sao*;
> tài liệu này trả lời *gõ vào file nào, theo thứ tự nào, và nhìn vào đâu để biết đã xong thật*.
>
> **Nguồn sự thật vẫn là** `giai-doan-6.md`: Đ-6.5–Đ-6.11 (tài khoản, thu hồi, bất biến Admin, fail-closed, vai trò, cache quyền,
> `/me`), Đ-6.12–Đ-6.15 (báo cáo, quyết định, BR-07, audit), Đ-6.16–Đ-6.18 (thông báo), Đ-6.19 (tìm kiếm), **Mục 6** (endpoint ×
> tầng 2 × tầng 3 × mã lỗi), **Mục 7** (luồng), **Mục 8** (hợp đồng), Mục 9.4 (chỗ đụng A, B), Mục 10 (test) — và `AGENTS.md`.
> Chỗ nào tài liệu này lệch với hai file đó thì sửa ở đây, không sửa ngược. Muốn đổi một `Đ-6.*` thì đó là **quyết định mới**,
> có ngày, ghi vào `giai-doan-6.md` trong cùng commit.
>
> Khối A (dữ liệu) và C (hạ tầng chéo module) đã xong, trừ C6 đang chờ GĐ5 — xem
> [huong-dan-khoi-a-c-nen-du-lieu-va-ha-tang.md](huong-dan-khoi-a-c-nen-du-lieu-va-ha-tang.md). Khối D **không dựng hạ tầng mới**. Nó
> gọi `IAuditTrail`, `IModerationTargets`, `IPermissionChangeNotifier`, `IsAllowedAsync`, `[PrivilegedEndpoint]`,
> `[RequireAnyPermission]`, `IAccountStatusReader` đã có sẵn, rồi biến hợp đồng Mục 8 thành endpoint chạy thật.
>
> Cái khó của khối này không nằm ở số endpoint. Nó nằm ở **ba mốc không lùi được** mà khối D là nơi chứng minh bằng test: đổi
> quyền có hiệu lực ở request kế tiếp (D1, D3–D5), không đường nào về 0 Admin **kể cả dưới đồng thời** (D3, D4), và ẩn bài + đóng
> báo cáo + audit trong **một** transaction (D7).

| | |
|---|---|
| **Người làm** | Một người (không chia lane) |
| **Thời lượng** | Bước 5–8 của Mục 9.3: D1–D5 **2 ngày** · D6–D8 **1,5 ngày** · D12 **0,5 ngày** · D9–D11 **1 ngày**. Phần nối event của A, B (bước 9) tính riêng khi họ merge |
| **Khối này cần trước** | A1–A5, C1–C5 trên `loveart1210` (đã xong 2026-09-24). C0 trên `develop` (PR #24) |
| **Khối này chặn** | E (ráp thật thay `msw/node`), F1–F2 (deploy + E2E staging), `B2`/`B3`/`B5` ở dạng bảng đột biến tổng |
| **Không thuộc khối D** | Hub thông báo (C6 — chờ GĐ5) · handler `comment`/`reply`/`reaction` (chờ A merge GĐ3) · handler `message` (chờ B merge GĐ5) · `HideAsync` cho bình luận · mọi màn FE (E1–E10) · k6 tìm kiếm (Mục 10.6, cắt được). Xem Mục 19 |

---

## 0. Danh sách công việc — mục tiêu và kết quả mong đợi

Mười bốn đầu việc: D0 (nền chung, **không** thành commit riêng — L-D1) và D1–D13, trong đó D7 tách ba commit `D7a`/`D7b`/`D7c`
(L-D5). Mỗi mã còn lại là **một commit** (commit-rules Mục 8). Cột "Kết quả mong đợi" là thứ **kiểm chứng được**: tên ca test,
số liệu, hoặc lệnh chạy ra kết quả cụ thể.

### 0.1 Tài khoản và vai trò — D0 → D5 (bước 5 của Mục 9.3)

> **Mục tiêu nhóm:** Admin khóa/mở tài khoản, đổi vai trò, sửa quyền của cả một vai trò qua API. Mọi thay đổi có hiệu lực ở
> request kế tiếp, và không thao tác nào đưa hệ thống về 0 Admin hoạt động. Đây là mốc 1 và mốc 2, chứng minh bằng integration test.

| Mã | Đầu việc | Mục tiêu — việc này tồn tại để làm gì | Kết quả mong đợi — thứ kiểm chứng được |
|---|---|---|---|
| **D0** | Nền chung của ba nhóm Swagger mới: `AdminApiGroup`, `ModerationApiGroup`, `NotificationApiGroup`; `AddApplicationPart`; `apiGroups`; yaml khung; lớp `…ContractTests` + `Content Include` | Mỗi nhóm endpoint mới **có hợp đồng và có cổng** ngay từ endpoint đầu tiên. Không có khoảnh khắc nào mà một endpoint chạy thật lại nằm ngoài cổng `API contract` | Không có commit riêng. Checklist Mục 2 đi cùng endpoint đầu tiên của từng nhóm: `admin-v1` ở D2, `moderation-v1` ở D6, `notification-v1` ở D11. Swagger thấy nhóm mới; `ContractGateCoverageTests` và `Every_controller_must_declare_a_swagger_group` xanh |
| **D1** | Login và refresh từ chối tài khoản `disabled` · `GET /me` có `permissions` · mở `identity-v1.yaml` chỉ-thêm | Hai lưới cho khóa tài khoản (Đ-6.5): người bị khóa không lấy được token mới bằng mật khẩu lẫn bằng refresh. Và FE biết **quyền hiệu lực** để vẽ điều hướng theo quyền thay vì theo tên vai trò (Đ-6.11) | Login đúng mật khẩu trên tài khoản `disabled` → **403** `type …:account-disabled`, sai mật khẩu → 401. Refresh → 401, **không** sinh dòng `refresh_tokens` mới. `ME-01` xanh (ADMIN = đủ 18 mã). Cổng `API contract` Identity xanh; `pnpm gen:api` + `pnpm typecheck` xanh |
| **D2** | `GET /admin/users`, `GET /admin/users/{id}` — `admin-v1.yaml` ra đời | Màn quản trị có danh sách để chọn người. Người chỉ có `role.assign` cũng xem được (policy any-of, Mục 6.1). Đồng thời đây là controller đặc quyền **đầu tiên**, nên cổng reflection của C4 hết chạy trong chân không | `ADM-07` (45 tài khoản, `limit=20` → 20/20/5, keyset ổn định) · `TC-A05`, `TC-A05b` xanh · tên hiển thị hydrate **một** câu SQL cho cả trang. `Skip` của `Privileged_groups_are_not_empty` đã gỡ; bỏ `[PrivilegedEndpoint]` khỏi controller thì `Privileged_controllers_carry_the_attribute` đỏ |
| **D3** ⭐ | Khóa / mở khóa · `AdminInvariant.EnsureRemainsAsync` · bên ghi `revoked:user` theo thứ tự DB → Redis | Lần đầu bên ghi `revoked:user` (dựng bên đọc từ GĐ1) chạy thật. Khóa tài khoản phải thật sự **đá người đó ra** ở request kế tiếp, và không bao giờ khóa được Admin hoạt động cuối cùng (Đ-6.6, Đ-6.7) | `ADM-01`, `ADM-02`, `ADM-03`, `ADM-04` (vế khóa), `ADM-06` · `ADM-C2` xanh **20/20 lượt** · `TC-A05-mod-lock` xanh · `socialapp_revocation_failures_total` có trên `/metrics` từ lúc khởi động · tự rà B.10 #1 ghi tên hàm vào thân commit |
| **D4** | `PUT /admin/users/{id}/role` | Nâng hoặc hạ vai trò mà **không** đăng xuất người đó. Token cũ chết, refresh cùng family cấp token mang vai trò mới đọc từ DB (Mục 7.3) | `ADM-05` (token cũ 401 → refresh 200 → token mới `role = USER`, không mất phiên) · `ADM-04` (vế hạ quyền) · `ADM-C1` xanh **20/20** · `ROLE-01` phần gán |
| **D5** | CRUD vai trò + `GET /admin/permissions` · `RolePermissionDiff` · 409 `confirmation-required` | "Nâng cấp là thay dữ liệu, không thay code" (PTTK 6.7.2): tạo `REVIEWER` qua API là dùng được, không sửa dòng nghiệp vụ nào. Gỡ nhầm `post.create` của USER phải qua bước xác nhận **ở server** (Đ-6.9, Đ-6.10) | `ROLE-01..07` (vế hide của `ROLE-01` ở D7c) · `PERM-01` **qua API** · `TC-A05-roles` · unit `RolePermissionDiff` · 409 `confirmation-required` mang đúng `added`, `removed`, `affectedUsers` |

### 0.2 Báo cáo, kiểm duyệt, nhật ký — D6 → D8 (bước 6)

> **Mục tiêu nhóm:** người dùng báo được nội dung xấu. Moderator xử lý được nó, và mỗi bước để lại dấu vết không xóa được. Mốc 3
> và bốn AC của US-019 thành test xanh.

| Mã | Đầu việc | Mục tiêu — việc này tồn tại để làm gì | Kết quả mong đợi — thứ kiểm chứng được |
|---|---|---|---|
| **D6** | `POST /reports` — `moderation-v1.yaml` ra đời · policy `report-create` | Tiếp nhận báo cáo (FR-019), nhưng **không** để endpoint này thành máy dò "bài riêng tư id X có tồn tại không" (Đ-6.12). Bấm hai lần hay hai tab chỉ ra một báo cáo | `REP-01..06` · `REP-C1` 20/20 · `REP-IDOR` · báo cáo trùng trả **200 cùng `reportId`**, báo cáo mới trả **201** · báo cáo thứ 11 trong một phút → 429 |
| **D7a** | Đường đọc `hidden` của Content: `PostResponse.moderation`, nhánh tác giả, 404 cho người khác, 409 khi sửa bài bị ẩn · mở `content-v1.yaml` chỉ-thêm | BR-07 phía người đọc (Đ-6.14): tác giả thấy bài bị ẩn **kèm lý do**, người khác thấy như bài không tồn tại. Đóng hai lỗ BR-07 **đang mở** trên code (L-D4) | `HID-01..06` xanh · bài `hidden` qua `GET /posts/{id}`: tác giả 200 có `moderation`, bạn bè 404, Moderator 404 · `PATCH` → 409 `…:post-hidden`, `DELETE` → 204 · cổng hợp đồng Content xanh; `schema.d.ts` sinh lại cùng commit |
| **D7b** | `GET /reports` (hàng đợi gom theo đối tượng) · `GET /reports/{id}` (ảnh chụp + báo cáo mở + lịch sử) | Moderator thấy **mỗi đối tượng một dòng**, cũ nhất trước, kèm số báo cáo theo lý do. Mở một dòng thì thấy **nội dung thật** của đối tượng, kể cả bài riêng tư hay đã xóa, mà không biết ai báo (Đ-6.13, Mục 8.1) | Ba báo cáo cùng một bài → **một** dòng `reportCount = 3`, `reasons` đếm đúng · keyset không trùng không sót qua 3 trang · `ReportDetail` **không** có trường `reporterId` · `TC-A06-queue` 403 + một dòng `access.denied` · số câu SQL của chi tiết không đổi theo số ảnh |
| **D7c** ⭐ | `PATCH /reports/{id}` (transaction Đ-6.13) · `POST /moderation/targets/{type}/{id}/restore` · `ContentHidden` sau `COMMIT` · `socialapp_reports_decided_total{decision}` | Mốc 3: ẩn bài + đóng **mọi** báo cáo mở của đối tượng + một dòng audit, **cùng số phận**. Tầng 2 kép: `hide` cần thêm `post.hide` ngoài `report.resolve` (Đ-6.9, Mục 6.2) | `MOD-01..06` · `MOD-C1` 20/20 · `TX-01`, `TX-02` **qua API** (lỗi giữa chừng → bài vẫn `published`, báo cáo vẫn `open`, 0 dòng audit) · `AUD-01` (chuỗi `SECRET-xyz` không lọt vào audit) · `TC-A06`, `TC-A06b` · `ROLE-01` vế `hide` → 403 · metric có ba nhãn từ lúc khởi động |
| **D8** | `GET /admin/audit-logs` | Admin đọc được "ai làm gì, lúc nào, từ đâu" (ENT-13, Mục 5.7). Đây là **người đọc duy nhất** của bảng append-only | `AUD-04` (lọc theo `actorId`, `action`, `targetId`; 3 trang `id DESC` không trùng không sót) · `TC-A05-mod-audit` 403 · `actor` hydrate một lô |

### 0.3 Thông báo — D9 → D11 (bước 8)

> **Mục tiêu nhóm:** người dùng được báo khi có người tương tác với mình, gộp cùng loại, đếm **người** chứ không đếm lượt (FR-018,
> Đ-6.16). Chạy ở chế độ hỏi lại 30 giây. Hub (C6) sau này chỉ là thêm một nguồn đẩy, không sửa endpoint nào.

| Mã | Đầu việc | Mục tiêu — việc này tồn tại để làm gì | Kết quả mong đợi — thứ kiểm chứng được |
|---|---|---|---|
| **D9** | `INotificationStore.UpsertAsync` — upsert gộp theo `group_key`, đếm người khác nhau theo **đợt** | Một chỗ duy nhất biến "một sự kiện" thành "một dòng thông báo đã gộp". Đúng dưới đồng thời: 20 người thả cùng lúc ra **một** dòng `actorCount = 20` | `NOTIF-03`, `NOTIF-04`, `NOTIF-05` (ở tầng store) · `NOTIF-C1` 20/20 · tự báo mình (`actor == recipient`) → không dòng nào · thông báo `moderation` không có `last_actor_id` lẫn dòng `notification_actors` |
| **D10** | Handler event: `FriendRequestSent`, `FriendRequestAccepted`, `ContentHidden` **ngay** · `CommentCreated`, `ReactionSet`, `MessageSent` **khi A/B merge** | Nối ba module phát event vào module thông báo mà không module nào biết module kia tồn tại (Đ-6.2). Mỗi handler được chứng minh từ **API thật** của module phát, không `Publish` tay | `NOTIF-01` (mời qua `socialgraph-v1` → B có `friend_request`; chấp nhận → A có `friend_accepted`) · `NOTIF-09` (ẩn qua `PATCH /reports` → tác giả có thông báo `moderation`, `actor = null`, có `reasonCode`, không id Moderator ở trường nào) · `EVT-02` vẫn xanh |
| **D11** | `GET /notifications`, `GET /notifications/unread-count`, `POST /notifications/{id}/read`, `POST /notifications/read-all` — `notification-v1.yaml` ra đời | Chuông và danh sách của lane E có API thật. Đánh dấu đã đọc có tầng 3, **không** làm thông báo cũ nhảy lên đầu (bài học A2) | `NOTIF-06..08` · `NOTIF-10` (đề xuất: đánh dấu đã đọc không đổi thứ tự) · `NOTIF-IDOR` 403, cùng thân lỗi với id không tồn tại · `TC-A01-notifications` 401 · số câu SQL của danh sách không đổi giữa trang 1 nhóm và trang 20 nhóm |

### 0.4 Tìm kiếm và rà cuối — D12, D13 (bước 7, cuối khối)

| Mã | Đầu việc | Mục tiêu — việc này tồn tại để làm gì | Kết quả mong đợi — thứ kiểm chứng được |
|---|---|---|---|
| **D12** | `GET /search?q=&type=user&limit=` · mở `profile-v1.yaml` chỉ-thêm | Tìm "nguyen" ra "Nguyễn Văn An", "van" cũng ra, "duc" ra "Đức" (FR-017). Truy vấn phải dùng **đúng** biểu thức của index A4, không thì Seq Scan | `SRCH-01..08` · `TC-A01-search` · `SRCH-07` khẳng định kế hoạch có `idx_profiles_display_name_search` · chạy lại `tests/load/search/explain.sql` trên 20.000 hồ sơ: vẫn `Bitmap Index Scan`, dán vào "Thực tế thi công" |
| **D13** | Rà RFC 7807 và `type` trên sáu hợp đồng | Mọi 409/503 mà FE phải phân nhánh có `type` riêng khai trong hợp đồng (Đ-6.21). Không thông điệp nào chứa id, email, nội dung | Bảng Mục 17.1 khớp từng dòng với code và yaml · sáu cổng `API contract` xanh hai chiều · `grep` thông điệp lỗi không ra `{` hay email · không thay đổi code nào **hoặc** một commit `fix` ghi rõ chỗ lệch |

**Kết quả khối D:** hợp đồng Mục 8 thành hệ thống chạy thật, khớp từng mã lỗi. Ba mốc không lùi được là test xanh trên CI, và mỗi
mốc đã từng đỏ khi cố tình bỏ đúng thứ bảo vệ nó (bảng đột biến Mục 20).

### 0.5 Thứ tự thực thi

```
 D1 ─→ D2 ─→ D3 ─→ D4 ─→ D5                         ← bước 5 (đường găng B.10: C4 → D3 → D4 → D7)
  │     │ (admin-v1 ra đời, gỡ Skip C4)   │
  │     └─ B1: helper "dựng Admin thứ hai" └─→ ROLE-01 vế gán
  │
  └─(độc lập)─→ D6 ─→ D7a ─→ D7b ─→ D7c ─→ D8       ← bước 6 (moderation-v1 ra đời ở D6)
                                     │
                    D12 (bước 7)     └─→ D9 ─→ D10 ─→ D11   ← bước 8 (ContentHidden của D7c là event thật đầu tiên)
                                                        └─→ D13 (rà cuối khối)
```

- **D1 trước tiên:** nó sửa đường login/refresh mà D3 dựa vào ("khóa xong thì refresh 401" có hai lưới, D1 dựng lưới thứ hai).
  Và `permissions` của `/me` là thứ lane E cần sớm nhất.
- **D2 trước D3:** D2 dựng nền `admin-v1` (D0), controller đặc quyền đầu tiên, và gỡ `Skip` của C4. D3 chỉ còn thêm action.
- **D3 → D4:** cùng khuôn transaction + khóa tư vấn + thu hồi. D3 viết `AdminInvariant`, D4 dùng lại.
- **D7a trước D7b, D7c:** phần Content là commit nhỏ, dễ rebase khi A (GĐ3) cũng sửa `PostResponse`/`content-v1` (Mục 9.4). Nó
  cũng cho `MOD-01` của D7c khẳng định được "tác giả thấy biểu ngữ" qua API thật.
- **D12 không phụ thuộc ai trong khối:** làm xen lúc chờ test đồng thời chạy 20 lượt.
- **D9 → D10 → D11:** handler cần store; `ContentHiddenHandler` cần `PATCH /reports` của D7c để có test đi từ API thật.

**Mỗi mốc mở khóa việc gì:**

| Mốc | Mở khóa |
|---|---|
| D1 | E1 (codegen `permissions`), E2 (điều hướng theo quyền) |
| D2 + D3 + D4 | E7 (màn quản trị tài khoản); F2 `E2E-04`, `E2E-06` |
| D5 | E8 (màn vai trò + ma trận quyền); F2 `E2E-05` |
| D6 | E5 (hộp thoại báo cáo) |
| D7a | E9 (biểu ngữ "bài bị ẩn") |
| D7b + D7c | E6 (màn kiểm duyệt); F2 `E2E-01`; ba điều kiện B.12 #3 |
| D8 | E9 (màn nhật ký) |
| D9–D11 | E3 (chuông, danh sách); F2 `E2E-02` |
| D12 | E4 (ô tìm, trang kết quả); F2 `E2E-03` |

### 0.6 Mười lăm chỗ lệch B.6 — đã chốt 2026-09-24

Đọc B.6, Mục 6, Mục 8 đối chiếu với code ngày 2026-09-24 (`5c93f42`), thấy các chỗ dưới đây viết chưa đủ, tự mâu thuẫn, hoặc
**không chạy được** trên code hiện tại.

**Trạng thái (chốt 2026-09-24, người thi công):** cả mười lăm chỗ đi **theo cột "Đề xuất"**. L-D16 tìm ra khi thi công D1, L-D17 khi
thi công D2 — ràng buộc kỹ thuật, đi theo đề xuất, ghi ở "Thực tế thi công". L-D18 tìm ra khi thi công D4 — lỗ bảo mật trong thiết
kế, người thi công chốt theo đề xuất trước khi viết code. L-D19 (D5) đi theo đúng câu "chỉ ADMIN có" của Đ-6.9.
- Mười chỗ là **lựa chọn thiết kế**. Mỗi chỗ đã cân nhắc phương án khác rồi loại:
  - L-D5: tách D7 thay vì một commit lớn.
  - L-D6: khuôn hai bước thay vì truy vấn con trong `RETURNING`. Cách đó chạy được, nhưng dưới lượt đua nó trả `NULL`, và phải lập
    luận thêm mới thấy vẫn đúng. `RETURNING OLD.*` chỉ có từ Postgres 18, còn repo chạy 16.
  - L-D8: luôn khóa thay vì đọc lại sau `FOR UPDATE` rồi thử lại.
  - L-D9: phương án (a) — bỏ `revocation` khỏi audit. Phương án (b) là ghi thêm dòng `user.revocation-deferred` với `tx: null` khi
    thu hồi hỏng. (b) không vi phạm Đ-6.3, nhưng bị loại vì audit trả lời "ai đã làm gì", còn thu hồi hỏng là sự cố hạ tầng.
  - L-D10: idempotent thay vì 409.
  - L-D11: `Error.Extensions` thay vì dựng `ProblemDetails` trong controller.
  - L-D12: có ghi audit ở tầng 2 kép. Không ghi cũng không vi phạm AC-03, nhưng ghi thì đúng câu "mọi lần bị từ chối" của Đ-6.15.
  - L-D13: `Supports` + 404 thay vì validator chặn `comment`.
  - L-D14: cho lọc `action` đứng một mình, thay vì trả 400.
  - L-D15: đổi tên trường thay vì thêm cột.
- Năm chỗ còn lại là **ràng buộc kỹ thuật hoặc quy trình**: code hiện tại hoặc luồng PR không cho làm khác. Đó là L-D1, L-D2, L-D3,
  L-D4, L-D7.
  - Hệ quả của L-D7: khối B **không còn commit riêng**. B2, B3, B5 đi cùng commit D tương ứng; B1 đi cùng D đầu tiên dùng nó. Bằng
    chứng "đã đỏ trước" nằm ở bảng đột biến trong thân commit và trong mô tả PR, không ở một commit đỏ.

Chốt rồi nhưng `giai-doan-6.md` **chưa** sửa. Mỗi chỗ sửa B.6, B.7, Mục 6, Mục 8, Mục 10 **trong cùng commit** của đầu việc chạm nó,
ghi "sửa 2026-09-24 khi thi công D…", cùng nếp khối A+C. Nếu thi công phải đổi hướng chỗ nào thì ghi vào "Thực tế thi công", có
ngày và lý do.

| # | Kế hoạch viết | Đề xuất | Vì sao |
|---|---|---|---|
| L-D1 | D0 là một đầu việc riêng; B4 viết ba lớp `ContractTestsBase` + dòng csproj như một việc của khối B | D0 **không** thành commit. Nền của mỗi nhóm (ApiGroup, `AddApplicationPart`, `apiGroups`, yaml, lớp `…ContractTests`, `Content Include`) đi **cùng commit endpoint đầu tiên** của nhóm: `admin-v1` → D2, `moderation-v1` → D6, `notification-v1` → D11. B4 còn lại: `NotificationHubContractTests` (đi với C6) + thử cho đỏ | `ContractGateCoverageTests` quét **mọi** `Modules/*/Presentation/*-v1.yaml` trên đĩa. Yaml mới mà thiếu lớp test hoặc `Content Include` là **đỏ ngay**. Một nhóm Swagger không có endpoint nào là hợp đồng rỗng so với Swagger rỗng: cổng xanh mà không chứng minh gì. Hệ quả tự nhiên của quyết định 2026-09-24 "hợp đồng đi cùng controller" |
| L-D2 | D1 / Đ-6.5: `RotateAsync` "thêm điều kiện `status = 'active'` trong join lúc xoay" | Kiểm `status` **trước** bước 5 (xoay): sau khi khóa family và qua các kiểm hợp lệ, trước `INSERT` token kế nhiệm. Không `active` → `Invalid`, không xoay | Join đọc vai trò (`RoleCodeAsync`) chạy **sau** khi token kế nhiệm đã chèn. Thêm điều kiện vào đó thì `SingleAsync` ném (500), hoặc token mới đã nằm trong DB rồi mới biết tài khoản bị khóa |
| L-D3 | D1: "codegen FE xanh" | Sửa `src/frontend/mocks/fixtures.ts` (và `lib/api/identity/schema.test-d.ts` nếu đỏ) **trong commit D1**, chỉ thêm `permissions` vào fixture | `MeResponse.permissions` là trường **required** của response. Fixture `satisfies MeResponse` đỏ `pnpm typecheck` ngay sau `pnpm gen:api`. Luật frontend Mục 7: hợp đồng đổi thì sửa chỗ đỏ trong cùng commit |
| L-D4 | D7: "nhánh tác giả trong `PostReadService`" | D7a sửa **ba** chỗ, không một: (1) `GetAsync`: bài `hidden` và người gọi không phải tác giả → 404, (2) nhánh tác giả kèm `moderation`, (3) `UpdateAsync` → 409. Ghi mục "Lỗi tìm ra khi rà" trong thân commit | Hôm nay `PostStore.FindAsync` chỉ lọc `deleted` (query filter). `GET /posts/{id}` của bài `hidden` trả **200** cho mọi người qua BR-02, và `PATCH` sửa được bài `hidden`. Chưa lộ thật vì chưa endpoint nào ẩn được bài. D7c mở đường đó, nên D7a phải đi trước |
| L-D5 | D7 là một đầu việc, một commit | Tách **D7a** (Content + `content-v1`) · **D7b** (hàng đợi + chi tiết) · **D7c** (quyết định + khôi phục + event + metric) | Mục 9.4: phần của GĐ6 trong `content-v1.yaml` phải là "**một** commit nhỏ" để rebase với A. D7 gộp là commit chạm ba module, hai yaml, hơn 25 ca test. `detect-changes` và review của nó không đọc nổi |
| L-D6 | Đ-6.16: `INSERT … ON CONFLICT DO UPDATE … RETURNING id, (xmax = 0), <is_read cũ>` | Khuôn hai bước trong một transaction: `UPDATE … FROM (SELECT … FOR UPDATE) old … RETURNING old.is_read` → 0 dòng thì `INSERT … ON CONFLICT DO NOTHING RETURNING id` → vẫn 0 dòng (người khác vừa chèn) thì chạy lại `UPDATE` **một** lần | `RETURNING` của Postgres chỉ thấy giá trị **mới**. `<is_read cũ>` không lấy được từ câu đó, mà đó lại là thứ quyết định "đợt mới → đếm lại từ 1". Khuôn hai bước lấy được giá trị cũ một cách tường minh, khóa dòng rõ ràng, và test được `NOTIF-C1` |
| L-D7 | D3: "viết `ADM-04`, `ADM-C1/C2` **trước** (đỏ)"; B2: "viết cho đỏ trước"; B3/B5 là việc của khối B | Dòng matrix, ca đồng thời, ca transaction đi **cùng commit D** làm chúng xanh. "Đỏ trước" làm ở **local** và ghi trong thân commit ("đã đỏ trước khi có controller: …"). Khối B còn lại B1 (helper, đi cùng D đầu tiên dùng nó) và bảng đột biến tổng trong PR | PR #24 đã đưa GĐ6 vào `develop` giữa giai đoạn, và PR khối D cũng sẽ vậy. Commit đỏ trên nhánh là CI đỏ trên PR. Nếp khối A+C: test sống cùng code (L-A7, L-A8) |
| L-D8 | Đ-6.7: khóa tư vấn "chỉ lấy khi thao tác **có thể** chạm tập Admin" | **Luôn** lấy khóa tư vấn cho mọi thao tác ghi của `admin-v1` lên `users` (lock, unlock, gán vai trò) | Quyết định "có lấy khóa không" dựa trên lần đọc vai trò **trước** khi khóa dòng là một lỗ đua. X đang là USER, vừa được nâng lên ADMIN và commit xen giữa; một Admin khác đồng thời bị hạ. Cả hai lượt đếm đều thấy 1 → còn 0. Thao tác quản trị là "tần suất thấp" (PTTK UC-19), xếp hàng sau nhau không ai thấy |
| L-D9 | Đ-6.15: audit `user.lock`/`role.assign` có `metadata.revocation: applied\|deferred` | Audit **không** ghi `revocation`. `LockRequest.reason` vào `metadata.reason`; `role.assign` ghi `fromRole`, `toRole`. `deferred` để lại dấu bằng log Error + `socialapp_revocation_failures_total` | Audit ghi **trong** transaction (Đ-6.3), còn thu hồi chạy **sau** `COMMIT` (Đ-6.6). Lúc ghi audit chưa biết kết quả thu hồi, và audit append-only nên không sửa lại được. Phương án ghi thêm một dòng riêng khi `deferred` đã cân nhắc và loại (xem trạng thái phía trên) |
| L-D10 | Hợp đồng `AdminUserChange.revocation` có `not-needed` nhưng không nói khi nào | Thao tác **không đổi gì** thì idempotent: khóa tài khoản đã `disabled`, mở tài khoản đang `active` và không có `locked_until`, gán đúng vai trò đang có → **200** `revocation: not-needed`, không audit, không đụng Redis. `unlock` luôn `not-needed` | Bấm "Khóa" hai lần (hai tab Admin) không được thu hồi phiên người ta hai lần hay làm bẩn nhật ký. 409 cho trường hợp này là bắt UI xử lý một lỗi không có gì sai |
| L-D11 | Đ-6.9 409 `confirmation-required` "kèm `added[]`, `removed[]`, `affectedUsers`" | `Error` thêm tham số cuối có mặc định `IReadOnlyDictionary<string, object?>? Extensions = null`; `ResultHttpExtensions.Problem` chép nó vào `ProblemDetails.Extensions` | `Error` hiện chỉ có `Errors` (400 theo trường) và `Type`. `controller.Problem(...)` không nhận extensions. Tự dựng `ProblemDetails` trong controller là tạo chỗ dựng lỗi thứ hai, đúng thứ `ResultHttpExtensions` cấm. Chỉ-thêm như Q-D4, Q-E4: không lời gọi nào phải đổi |
| L-D12 | Đ-6.13 bước 2: kiểm `post.hide` **trong** transaction, sau `FOR UPDATE` | Kiểm `post.hide` **trước** `BEGIN` (chỉ phụ thuộc `decision` của request và vai trò). Bị từ chối thì ghi `access.denied` qua `IAuditTrail.AppendAsync(null, …)` với `metadata.permission = "post.hide"` | Không giữ khóa dòng báo cáo trong lúc tra cache quyền. `REVIEWER` luôn nhận 403, không 404/409 tùy báo cáo. `AuditingAuthorizationResultHandler` (C4) chỉ thấy từ chối ở **middleware**, không thấy tầng 2 kép trong service, mà Đ-6.15 nói "mọi lần bị từ chối" trên endpoint đặc quyền |
| L-D13 | D6: `targetType: comment` trong hợp đồng; C2 chưa có provider bình luận | `IModerationTargets` thêm `bool Supports(ModerationTargetType)` (chỉ-thêm). D6 trả **404** cho loại chưa hỗ trợ, cùng thân lỗi với "không tồn tại"; D7c chặn bằng bảng `decision × targetType` như C2 đã ghi | `ModerationTargets` ném `NotSupportedException` cho loại không có provider → **500**. Trước khi A merge, không có bình luận nào tồn tại qua API, nên 404 là câu trả lời đúng nghĩa đen. Khi A merge, thêm provider là `Supports` tự đúng, không sửa D6 |
| L-D14 | D8: "`action` chỉ đi kèm một trong hai hoặc khoảng id" | Cho phép lọc `action` đứng một mình (không 400). Truy vấn đi theo PK lùi + lọc, `LIMIT` dừng sớm | Câu đó là **ghi chú hiệu năng** của Mục 4 ("không index theo action"), không phải luật validation. 400 cho "xem mọi `user.lock`" là chặn một câu hỏi hợp lệ của Admin trên một bảng nhỏ |
| L-D15 | Mục 8.1: `ReportDetail.history: [{ decision, resolverId, resolvedAt, note? }]` | Đổi tên trường thành `outcome: "resolved" \| "dismissed"`, **trước** khi yaml có operation này (D7b), nên chưa client nào dùng | Bảng `reports` chỉ có `status` (`resolved\|dismissed`), không phân biệt `hide` với `resolve`. Trả `decision` thì hoặc bịa, hoặc thêm cột bằng một migration Moderation mới. "Đã ẩn hay xử lý ngoài luồng" đã có ở `target.status` và trong audit |
| L-D16 *(thêm 2026-09-24 khi thi công D1)* | Mục 8.5: `identity-v1` chỉ thêm `permissions` và 403 `account-disabled` | Nới `RoleCode` từ enum `[USER, MODERATOR, ADMIN]` thành chuỗi có pattern `^[A-Z][A-Z0-9_]{2,29}$`, cùng commit D1. Test kiểu của FE (`schema.test-d.ts`) đổi theo | `/me.role` đọc `roles.code` từ DB, nên từ lúc có vai trò tự tạo (D5, và ca `ME-01` vai trò tự tạo của D1) response nằm ngoài enum của hợp đồng. Cổng hợp đồng không so giá trị enum nên không đỏ: hợp đồng nói dối mà không ai biết. Không chỗ nào của FE so tên vai trò (grep) |
| L-D17 *(thêm 2026-09-24 khi thi công D2)* | Mục 6.3: "chỉ thêm dòng vào `AuthZMatrix.cs`, **không** sửa `AuthZMatrixTests`, `AuthZCase`, `AuthZApiFactory`" | Matrix chạy với **Redis thật** từ D2: `AuthZApiFactory.UseRedis` (mặc định giữ cổng 1, khuôn `ModulesApiFactory`/`IdentityApiFactory`) + `AuthZMatrixTests` nhận `RedisFixture` và chờ kết nối của app trước dòng đầu. `AuthZCase` không đổi | `AuthZApiFactory` trỏ Redis vào cổng 1. Endpoint `[PrivilegedEndpoint]` fail-closed (Đ-6.8), nên mọi người gọi có token nhận **503** trước khi tới tầng 2: `TC-A05` (403) và `TC-A05b` (200) không thể xanh — và mọi dòng `TC-A05*`/`TC-A06*` sau này cũng vậy. Stub `ITokenRevocationStore` thay vì Redis thật cũng chạy, nhưng là stub trên đúng đường mà matrix phải canh. Endpoint thường không đổi hành vi (Unknown và "không bị thu hồi" cùng cho qua). Đột biến M0 chứng minh |
| L-D18 *(thêm 2026-09-24 khi thi công D4)* | Đ-6.9, Mục 6.1, Mục 8.2: `PUT /admin/users/{id}/role` chỉ cần `role.assign` | Thao tác **chạm ADMIN** — vai trò đích là ADMIN, hoặc người bị đổi đang là ADMIN — cần **thêm** `role.manage`: tầng 2 kép, khuôn L-D12. Service tra `IsAllowedAsync(role.manage)` **trước** `BEGIN` (đích ADMIN → 403 ngay); store kiểm vế "đang là ADMIN" sau khi khóa dòng với cờ tra sẵn. Từ chối → 403 + `access.denied` (`tx: null`, target `user`, `metadata.permission = role.manage`) | Đ-6.9 lấy ví dụ vai trò "Nhân sự" có `role.assign` mà không phải ADMIN. Không chặn thì người đó tự gán mình lên ADMIN: `role.assign` tương đương toàn quyền, `role.manage` "chỉ ADMIN có" mất nghĩa. Vế "đang là ADMIN" chặn chiều ngược lại — "Nhân sự" hạ hết Admin. Phương án "chỉ ADMIN mới gán ADMIN" bị loại: phải so `role == ADMIN` ở Identity (luật 3 Mục 1.3); dùng quyền thì Admin short-circuit tự đúng |
| L-D19 *(thêm 2026-09-24 khi thi công D5)* | Đ-6.9: "`role.manage` … Chỉ ADMIN có" — nhưng `POST /admin/roles` và `PUT …/permissions` nhận "tập mã có thật" | API **không** gán `role.manage` cho vai trò nào: validator chỉ nhận 17 mã còn lại → 400 `errors.permissions`. `GET /admin/permissions` trả thêm `assignable` (sai với đúng `role.manage`) để FE khóa ô đó | Câu "chỉ ADMIN có" mất nghĩa nếu API gán được. Gắn nhầm vào USER là mọi người dùng sửa được vai trò, và với L-D18 nâng được bất kỳ ai lên ADMIN — bước xác nhận không cứu được một cú nhấp nhầm có hộp thoại đi kèm. Muốn trao toàn quyền thì gán vai trò ADMIN (D4), có bất biến và audit riêng |

---

## 1. Trước khi gõ dòng đầu tiên

### 1.1 Năm điều kiện cần

```bash
# 1. Postgres + Redis dev đang chạy (compose dev) — D3/D4 cần Redis thật cho revoked:user, D6 cho rate limit, D5 cho pub/sub
# 2. Docker daemon — IntegrationTests dùng Testcontainers
# 3. Solution build sạch; frontend lint/typecheck/test/build xanh (D1, D7a, D11, D12 chạy pnpm gen:api)
dotnet build SocialApp.sln
cd src/frontend && pnpm lint && pnpm typecheck && pnpm test && pnpm build && cd -
# 4. Nhánh loveart1210 đã có A1–A5, C1–C5 (5c93f42 trở lên)
git log --oneline -3
# 5. Chưa có yaml moderation/notification/admin nào trên đĩa — L-D1 (có sẵn là ContractGateCoverageTests đỏ)
ls src/backend/Modules/*/Presentation/*-v1.yaml
```

Ghi **số test trước** (Unit, Integration, Architecture, Vitest). Mỗi commit cần dòng `Test: … → …`. Máy dev có **một đỏ nền**
(`StartupConfigurationTests.Development_boots_without_r2_config_and_first_use_names_the_variables`, do user-secrets có khóa R2; CI
xanh). Nó không tính là lỗi của khối này.

### 1.2 Impact analysis trước khi sửa symbol có sẵn

Chạy lại **ngay trước** đầu việc chạm symbol đó, vì index có thể đã cũ:

```bash
node .gitnexus/run.cjs impact "<Symbol hoặc uid>" --direction upstream --repo .
```

Kết quả chạy ngày 2026-09-24:

| Symbol | Đầu việc | Risk | Ghi chú |
|---|---|---|---|
| `LoginService` | D1 | **UNKNOWN** | Index không có cạnh (đăng ký qua DI). Text search: `IdentityModuleExtensions`, `AuthController`, `LoginTests`, `LoginServiceTests`. Đổi `LoginCandidate` (thêm `Status`) thì fake `FakeUsers` trong `LoginServiceTests` đỏ compile, sửa cùng commit |
| `IRefreshTokenStore.RotateAsync` | D1 | **LOW** | 12 nút, 4 trực tiếp: `SessionService` + hai fake (`LoginServiceTests`, `SessionServiceTests`). Chữ ký **không đổi** (L-D2 chỉ đổi thân hiện thực) nên fake không đỏ |
| `MeResponse` (record) | D1 | **LOW** + UNKNOWN phía FE | 3 nút C#: `IdentityUserStore.FindMeAsync` (chỗ duy nhất `new MeResponse(`), `MeQuery`, `MeController`. Phía FE là alias `lib/api/types.ts`: index không thấy người dùng. Text search: `features/auth/me-profile.tsx`, `lib/api/auth-api.ts`, `mocks/fixtures.ts`, `mocks/handlers.ts` (L-D3) |
| `IIdentityUserStore.FindMeAsync` | D1 | **LOW** | 4 nút: hiện thực + `FakeUsers` + `MeQuery` |
| `Error` (record, SharedKernel) | D5 | **UNKNOWN** | Index không thấy `new Error(…)` theo vị trí. Text search: mọi `*Errors.cs` của năm module + `Error.Forbidden`/`Validation`. L-D11 thêm tham số **cuối có mặc định**, nên không lời gọi nào đổi. Kiểm `ProblemDetailsTests` + cả bộ Integration |
| `PostReadService.GetAsync` | D7a | **LOW** | 1 nút: `PostsController.Get` |
| `PostService.UpdateAsync` | D7a | **LOW** | 1 nút: `PostsController.Update` (4 luồng) |
| `PostResponseMapper` | D7a | **LOW** | 9 nút: `PostReadService`, `PostService`, `PostHydrator` (feed + trang cá nhân), `FeedServiceTests`. Nhánh `moderation` chỉ bật với `hidden` + tác giả, mà feed/trang cá nhân đã lọc `hidden` từ GĐ4, nên hai đường đó không đổi output |
| `PostResponse` (record) | D7a | **LOW** + UNKNOWN phía FE | 15 nút C# qua mapper. Phía FE: `features/post/*`, `features/feed/*`, `mocks/*`. `moderation` là trường **optional** nên fixture không đỏ |
| `IModerationTargets` | D6 | chạy lại trước D6 | L-D13 thêm thành viên. Sửa **mọi fake** hiện thực interface này cùng commit (L-C10). `grep -rn ": IModerationTargets" tests/` |
| `SharedKernelExtensions` (rate limiter) | D6 | chạy lại trước D6 | Thêm policy `report-create` cạnh `auth`, không đổi policy có sẵn (Mục 9.4: B cũng thêm `realtime-ticket`) |

Không cái nào HIGH/CRITICAL. `UNKNOWN` **không** được đọc là "an toàn": các dòng trên đã xác nhận bằng text search. Chạy impact
cho mọi symbol **mới** mà đầu việc sau sửa lại (ví dụ `AdminInvariant` ở D4, `NotificationStore` ở D10).

### 1.3 Mười luật áp thẳng vào khối D

1. **Hợp đồng đi cùng controller** (chốt 2026-09-24, L-D1). Operation nào có trong yaml thì đã có controller hiện thực nó, **trong
   cùng commit**. `pnpm gen:api` chạy lại và `schema.d.ts` commit cùng lúc.
2. **`actorId` từ token** (`User.GetUserId()`), không từ route hay body. Ở `/admin/users/{userId}/…`, id trên đường là **đích**,
   người thao tác là token (B.10 #4).
3. **Không `role == "ADMIN"`** ngoài `SystemRoles` và `PermissionChecks.IsAllowedAsync` (B.10 #3). Tầng 2 kép gọi
   `cache.IsAllowedAsync(User.FindFirstValue(JwtClaims.Role), …)`, đúng cách `PermissionHandler` gọi. Ngoại lệ **duy nhất**: hàm
   "quyền hiệu lực" cho **hiển thị** ở Identity (D1, D5), đặt ở một chỗ, so với `SystemRoles.Admin`.
4. **DB trước, Redis sau** ở mọi đường gọi `RevokeUserAsync` (B.10 #1). Không test nào bắt được thứ tự này, nên tự rà và ghi tên
   hàm vào thân commit.
5. **`Publish` sau `CommitAsync`**, không trong khối `await using var tx` (B.10 #2, cạm bẫy C0).
6. **Audit ghi trong transaction của thao tác**: `IAuditTrail.AppendAsync(tx, …)` với
   `tx = db.Database.CurrentTransaction!.GetDbTransaction()`. `tx: null` chỉ cho `access.denied`.
7. **`metadata` audit không bao giờ chứa nội dung** bài, bình luận hay tin nhắn. Log không chứa `email`, `ip`, `detail`, `note`,
   `reason` (B.10 #5, danh sách che log của GĐ7 Đ-7.14).
8. **Mọi 409/503 có `type` riêng** khai trong hợp đồng (Mục 17.1). Hằng `type` đặt cạnh `Error` của module, **không** gõ lại chuỗi.
9. **Chỉ số mới khai ở `BusinessMetrics`** (prometheus-net), tạo sẵn chuỗi có nhãn trong `Initialize()`, thêm ca vào
   `MetricsEndpointTests` (chốt 2026-09-24). **Không** `Meter`.
10. **Không commit đỏ lên nhánh** (L-D7). Đỏ trước ở local, ghi trong thân commit. Test đồng thời chạy **20 lượt liền** trước khi tin:
    `for i in $(seq 20); do dotnet test tests/SocialApp.IntegrationTests --no-build --filter "FullyQualifiedName~ADM_C1" || break; done`.
    Build trước, đọc dòng `Error(s)`. Build lỗi thì `--no-build` chạy DLL cũ và ra "xanh" giả.

---

## 2. D0 — Nền chung của ba nhóm Swagger *(không commit riêng — L-D1)*

**Mục tiêu:** mỗi nhóm endpoint mới có hợp đồng và cổng từ endpoint đầu tiên.

**Kết quả mong đợi:** checklist dưới đây tick đủ ở D2 (`admin-v1`), D6 (`moderation-v1`), D11 (`notification-v1`).

### Checklist cho mỗi nhóm (chép khuôn `IdentityApiGroup` + `IdentityContractTests`)

| # | Việc | `admin-v1` (D2) | `moderation-v1` (D6) | `notification-v1` (D11) |
|---|---|---|---|---|
| 1 | Lớp `<X>ApiGroup` (`Name`, `Title`) ở `Presentation/`, doc comment "ba chỗ phải khớp" | `Identity/Presentation/AdminApiGroup.cs` — `"admin-v1"`, `"Quản trị"` | `Moderation/Presentation/ModerationApiGroup.cs` — `"Kiểm duyệt"` | `Notification/Presentation/NotificationApiGroup.cs` — `"Thông báo"` |
| 2 | `.AddApplicationPart(typeof(<X>ApiGroup).Assembly)` trong `Program.cs` | **Không cần**, vì Identity đã có | Thêm | Thêm |
| 3 | Dòng `apiGroups` trong `Program.cs` | Thêm | Thêm | Thêm |
| 4 | File `<nhóm>.yaml` cạnh ApiGroup: `openapi`, `info.version: 1.0.0-gd6`, `servers`, `components` dùng chung (`ProblemDetails`, `ValidationProblemDetails`, `401`, `403`, `503 revocation-unavailable`), khối comment đầu file ghi "lớn dần theo đầu việc D" | `Identity/Presentation/admin-v1.yaml` | `Moderation/Presentation/moderation-v1.yaml` | `Notification/Presentation/notification-v1.yaml` (không có 503) |
| 5 | Lớp `<X>ContractTests : ContractTestsBase` `[Trait("Category","Contract")]` | `AdminContractTests` | `ModerationContractTests` | `NotificationContractTests` |
| 6 | `<Content Include=… Link="Contracts\<nhóm>.yaml" CopyToOutputDirectory="PreserveNewest" />` trong `SocialApp.IntegrationTests.csproj` | Thêm | Thêm | Thêm |
| 7 | Validator FluentValidation đăng ký trong `Add<X>Module` (khuôn `AddIdentityModule`) | Có sẵn cho Identity | Thêm | Thêm |
| 8 | Controller: `[ApiController]`, `[Route("api/v1/…")]`, `[ApiExplorerSettings(GroupName = …)]`, `[ProducesResponseType]` **đủ mã** của hợp đồng | ✓ | ✓ | ✓ |
| 9 | `[PrivilegedEndpoint]` **ở mức class** | Mọi controller | Mọi controller **trừ** controller chứa `POST /reports` (D6 tách controller) | Không, vì đây không phải endpoint đặc quyền |
| 10 | `pnpm gen:api` sinh `lib/api/<nhóm>/schema.d.ts` (glob tự thấy file mới, không sửa script — luật frontend Mục 7) | `lib/api/admin/` | `lib/api/moderation/` | `lib/api/notification/` |

### Cạm bẫy đã biết

1. **Yaml khung không có `paths`** rồi đợi "điền sau": `ContractTestsBase` so hai chiều. Swagger có operation mà yaml chưa có là
   đỏ, và ngược lại. Yaml ra đời với **đúng** các operation của commit đó.
2. **Tên nhóm lệch ở một trong ba chỗ** (attribute, `apiGroups`, tên file) thì endpoint biến khỏi Swagger mà không lỗi nào. Cổng
   hợp đồng đỏ đúng chỗ, đừng "sửa" bằng cách đổi tên file cho khớp nhầm.
3. **`[PrivilegedEndpoint]` gắn ở action thay vì class** thì action thứ hai của controller dễ quên. Gắn ở class; `POST /reports`
   là lý do duy nhất tách controller.
4. **`servers`/`info` chép từ `identity-v1.yaml` nguyên văn** kéo theo phần comment "Q-D4…" của Identity. Viết khối comment riêng
   cho file mới, trỏ về Mục 8.x của `giai-doan-6.md`.

---

## 3. D1 — Login/refresh từ chối `disabled` · `GET /me` có `permissions`

**Mục tiêu:** tài khoản bị Admin khóa không lấy được token mới bằng bất kỳ đường nào; FE biết quyền hiệu lực.

**Kết quả mong đợi:** ba nhánh login/refresh (Đ-6.5) và `ME-01` xanh; `identity-v1.yaml` mở lại chỉ-thêm; FE typecheck xanh.

### Các bước

1. **Lỗi mới** — `IdentityErrors.AccountDisabled`:

   ```csharp
   public const string AccountDisabledType = "urn:socialapp:problem:account-disabled";
   public static readonly Error AccountDisabled = new(
       "identity.account_disabled", "Tài khoản đã bị khóa. Liên hệ quản trị viên.", 403, "Bị từ chối", Type: AccountDisabledType);
   ```

   `type` riêng vì login giờ có **hai** 403 (chưa xác minh / bị khóa) mà FE phải hiện hai câu khác nhau (luật frontend Mục 4).
   403 `email-not-verified` **giữ nguyên** hình dạng cũ (Mục 8.5). Thứ tự: 4b đứng **trước** bước 5 — tài khoản vừa bị khóa vừa
   chưa xác minh thì báo "đã bị khóa".

2. **Login** — `LoginCandidate` thêm `Status` (cuối record); `FindForLoginAsync` chọn thêm `u.Status`. `LoginService`: chèn bước
   **4b** ngay sau bước 4 (mật khẩu đúng), **trước** bước 5 (chưa xác minh):

   ```csharp
   // 4b. Bị Admin khóa (Đ-6.5) → 403. Sau bước kiểm mật khẩu là cố ý: người không biết mật khẩu không được biết tài khoản bị
   //     khóa (khác 423 của FR-003). Không reset bộ đếm, không phát token. `locked` của UserStatus không ai ghi — chỉ so `disabled`.
   if (user.Status == UserStatus.Disabled)
       return IdentityErrors.AccountDisabled;
   ```

   Sửa `FakeUsers` trong `LoginServiceTests` (đổi `LoginCandidate`). Thêm hai ca unit: `disabled` + mật khẩu đúng → `AccountDisabled`
   và **không** gọi `CreateAsync`; `disabled` + mật khẩu sai → `InvalidCredentials` và bộ đếm vẫn tăng.

3. **Refresh** — `RefreshTokenStore.RotateAsync` (L-D2): kiểm **trước mỗi chỗ phát token** — nhánh ân hạn 3a (phát token anh em
   cùng family) và trước bước 5 "Xoay" (sau các kiểm hợp lệ: tồn tại, chưa hết hạn, chưa thu hồi, không reuse). Nhánh reuse 3b giữ
   nguyên: nó thu hồi, không phát. Khuôn ở bước 5:

   ```csharp
   // Lưới thứ hai của Đ-6.5: khóa tài khoản (D3) đã thu hồi mọi family, nhưng nếu một đường nào đó quên bước ấy thì refresh vẫn
   // không cấp được token cho tài khoản không hoạt động. Kiểm TRƯỚC khi chèn token kế nhiệm — RoleCodeAsync ở cuối là quá muộn.
   var active = await db.Users.AnyAsync(u => u.UserId == row.UserId && u.Status == UserStatus.Active, ct);
   if (!active)
   {
       await transaction.CommitAsync(ct);
       return RotateOutcome.Invalid.Instance;
   }
   ```

   `SessionService` đã ánh xạ `Invalid` → 401 `SessionInvalid`. Không sửa `SessionService`.

4. **`/me.permissions`** — `MeResponse` thêm `IReadOnlyList<string> Permissions` **cuối** record. Quyền hiệu lực tính ở **một** hàm
   dùng lại cho D5 (`RoleSummary.permissions`):

   ```csharp
   // Identity/Application/Roles/EffectivePermissions.cs — CHỈ để hiển thị (Đ-6.11). Tầng 2 vẫn là PermissionHandler/IsAllowedAsync.
   // Chỗ duy nhất ngoài SharedKernel so với SystemRoles.Admin (luật 3 Mục 1.3): ADMIN không có dòng role_permissions nào.
   public static IReadOnlyList<string> For(string roleCode, IEnumerable<string> granted) =>
       roleCode == SystemRoles.Admin ? PermissionCodes.All : [.. granted.Order(StringComparer.Ordinal)];
   ```

   `FindMeAsync` đọc thêm mã quyền của vai trò (một câu join `role_permissions → permissions`), **từ DB**, không từ claim. Thứ tự
   ổn định để test và FE so được. `PermissionCodes.All` đã có thứ tự id 1..18.

5. **`identity-v1.yaml`** chỉ-thêm: `MeResponse.permissions` (`type: array`, `items: string`, **required**, ví dụ cho USER),
   `POST /auth/login` 403 đổi `example` thành hai `examples` (chưa xác minh / bị khóa) kèm bảng `type` trong mô tả; `RoleCode` nới
   thành chuỗi có pattern (L-D16). `info.version` → `1.1.0-gd6`.
   Khối comment đầu file thêm dòng "GĐ6 D1 (2026-09-…)".

6. **FE:** `pnpm gen:api` → sửa `mocks/fixtures.ts` thêm `permissions` cho fixture `MeResponse` (L-D3), `schema.test-d.ts` đổi
   kỳ vọng `RoleCode` thành `string` + thêm ca `permissions` (L-D16) → `pnpm lint typecheck test build`. **Không** sửa màn nào, vì
   điều hướng theo quyền là E2.

7. **Test** (integration, `Auth/`):

   | Id | Kịch bản | Kỳ vọng |
   |---|---|---|
   | `ADM-01-login` | Đặt `status = 'disabled'` bằng SQL (endpoint khóa là D3 — ghi chú luật 9 ngoại lệ như `FEED-06`); login đúng mật khẩu · sai mật khẩu | 403 `type …:account-disabled`, không cookie refresh · 401 cùng thân với email không tồn tại |
   | `ADM-01-refresh` | Đăng nhập → đặt `disabled` bằng SQL (family **chưa** bị thu hồi — thử đúng lưới 2) → `POST /auth/refresh` | 401 `SessionInvalid`; `count(*)` của `refresh_tokens` **không tăng** |
   | `ME-01` | `/me` của USER, MODERATOR, ADMIN, vai trò tự tạo (chèn bằng SQL, D5 chưa có) | đúng tập quyền; ADMIN = 18 mã theo thứ tự `PermissionCodes.All`; vai trò tự tạo = đúng dòng `role_permissions` |

### Cạm bẫy đã biết

1. **Kiểm `disabled` trước bước mật khẩu** thì ai cũng dò được email nào bị khóa. Thứ tự là hợp đồng (doc comment đầu `LoginService`).
2. **Kiểm trạng thái ở `RoleCodeAsync`**: L-D2.
3. **Đọc `permissions` từ claim hay từ `IPermissionCache`**: claim không có quyền, còn cache là của **tầng 2** và có TTL. `/me` đọc DB
   như `role`, cùng nguồn, cùng lúc.
4. **Quên `required` trong yaml**: FE sinh `permissions?: string[]` và mọi chỗ dùng phải `?? []`. Response thêm trường required
   là chỉ-thêm với client (Mục 8.5).

---

## 4. D2 — `GET /admin/users`, `GET /admin/users/{id}` *(admin-v1 ra đời)*

**Mục tiêu:** danh sách tài khoản cho màn quản trị; controller đặc quyền đầu tiên.

**Kết quả mong đợi:** checklist D0 cho `admin-v1`; `ADM-07`, `TC-A05`, `TC-A05b` xanh; cổng reflection C4 hết `Skip`.

### Các bước

1. **Nền `admin-v1`** theo checklist Mục 2.
2. **`AdminUsersController`** — `Identity/Presentation/`, `[Route("api/v1/admin/users")]`, `[PrivilegedEndpoint]` ở class,
   `[ApiExplorerSettings(GroupName = AdminApiGroup.Name)]`. Hai action đọc mang
   `[RequireAnyPermission(PermissionCodes.UserLock, PermissionCodes.UserUnlock, PermissionCodes.RoleAssign)]`.
3. **Truy vấn danh sách** — `IAdminUserQueries` (Application) + hiện thực ở `Identity/Infrastructure/Persistence/`:
   - Keyset `(created_at DESC, user_id DESC)`; cursor mờ, chép khuôn `PostCursor` (base64 của `createdAt|userId`, hỏng → 400 `errors.cursor`).
   - `limit` mặc định 20, tối đa 50 (cùng mức các danh sách GĐ2/GĐ4; ghi vào yaml).
   - `q`: tiền tố email. `email` là `citext`, nên `EF.Functions.Like(u.Email, Escape(q) + "%", "\\")` đã không phân biệt hoa
     thường. **Escape** `\`, `%`, `_` trước khi ghép (cùng hàm escape với D12, đặt ở SharedKernel hoặc chép, xem cạm bẫy 3).
   - `status` ∈ `active|disabled`, `roleCode` = mã vai trò; sai → 400 theo trường.
   - `displayName` hydrate **một** lô qua `IUserDirectory.GetManyAsync` (Profile). Người chưa có hồ sơ thì `null`.
   - `lockedUntil` chỉ trả khi `> now` (FR-003 còn hiệu lực), không trả mốc đã qua.
4. **Chi tiết:** 404 khi không tồn tại. Endpoint đặc quyền nên không có mối lo IDOR "404 hay 403". Tầng 2 đã chặn người ngoài.
5. **`admin-v1.yaml`** hai operation: `AdminUser`, `AdminUserPage`, lỗi 400/401/403/404/503.
6. **Gỡ `Skip`** của `Privileged_groups_are_not_empty` (`tests/SocialApp.ArchitectureTests/PrivilegedEndpointTests.cs`). Thử cho
   đỏ: bỏ `[PrivilegedEndpoint]` khỏi `AdminUsersController` → `Privileged_controllers_carry_the_attribute` đỏ, nêu đúng tên action;
   khôi phục, `git status` sạch.
7. **B1 (đi cùng đây, L-D7):** helper harness `TaoAdminThuHaiAsync`, `DatVaiTroAsync(userId, roleCode)`, `TaoVaiTroAsync(code,
   quyền…)` bằng SQL, đặt cạnh helper GĐ4 trong `Harness/`. D3–D5 dùng lại.
8. **Test:**

   | Id | Kịch bản | Kỳ vọng |
   |---|---|---|
   | `ADM-07` | 45 tài khoản khớp `q`, `limit=20` | 20 / 20 / 5, không trùng không sót, `nextCursor` null ở trang cuối |
   | `ADM-07b` *(đề xuất)* | `q = "a%"` · `q = "A_"` · `q` chữ hoa | ký tự hiểu theo nghĩa đen · không phân biệt hoa thường |
   | `ADM-07c` *(đề xuất)* | Trang 20 dòng | số câu SQL = số câu của trang 1 dòng (`SqlCommandCounter`) |
   | `TC-A05`, `TC-A05b` | matrix (Mục 6.3) | 403 · 200 |
   | `ANY-01` qua endpoint thật *(đề xuất)* | Vai trò chỉ có `role.assign` gọi `GET /admin/users` | 200. Bản probe của C4 giữ nguyên |

### Cạm bẫy đã biết

1. **`email` vào log** (`LogInformation("… {Email}")`) — PII (Mục 8.2). Log `userId` nếu cần.
2. **Hydrate trong vòng lặp** là N+1 đúng ở màn Admin cuộn nhiều. Một lô cho cả trang.
3. **Escape hai lần, hai kiểu:** D2 và D12 cùng cần escape `LIKE`. Viết một hàm thuần có unit test (`LikePattern.Escape`), không
   chép hai bản lệch nhau.
4. **Sắp theo `created_at` mà thiếu `user_id`**: nhiều tài khoản seed cùng một mili giây ra trùng/sót giữa trang. Keyset luôn có
   khóa phụ.

---

## 5. D3 — Khóa / mở khóa + bất biến Admin + thu hồi ⭐

**Mục tiêu:** khóa tài khoản đá người đó ra ở request kế tiếp; không bao giờ khóa được Admin hoạt động cuối cùng; thứ tự DB → Redis.

**Kết quả mong đợi:** `ADM-01..04`, `ADM-06`, `ADM-C2` (20/20), `TC-A05-mod-lock` xanh; metric thu hồi hỏng có trên `/metrics`.

### Các bước

1. **`AdminInvariant`** — `Identity/Infrastructure/Administration/AdminInvariant.cs`:

   ```csharp
   /// Bất biến "luôn còn ≥ 1 Admin hoạt động" (Đ-6.7). GĐ8 gọi lại cho đường tự xóa tài khoản — đừng nhân bản câu đếm.
   internal static class AdminInvariant
   {
       // Namespace khóa tư vấn RIÊNG — khác FamilyLockNamespace = 0x5246 ("RF") của RefreshTokenStore, theo đúng luật ghi ở
       // comment đầu lớp đó. `grep -rn pg_advisory src/` trước khi chọn: hôm nay chỉ có "RF".
       private const int LockNamespace = 0x4144;   // "AD"
       private const int LockKey = 1;              // "admin-invariant"

       public static Task AcquireAsync(IdentityDbContext db, CancellationToken ct) => db.Database.ExecuteSqlAsync(
           $"SELECT pg_advisory_xact_lock({LockNamespace}, {LockKey})", ct);

       /// Đếm SAU khi ghi, trong CÙNG transaction — không tự suy "thao tác này có giảm số Admin không".
       public static async Task<bool> EnsureRemainsAsync(IdentityDbContext db, CancellationToken ct) => …count > 0;
   }
   ```

2. **Store thao tác** — `IAccountAdministrationStore` (Application) + `AccountAdministrationStore` (Infrastructure). Một phương
   thức một thao tác, mỗi cái **một** transaction Identity:

   ```
   LockAsync(targetId, actorId, reason, now):
     BEGIN
       AdminInvariant.AcquireAsync                                  -- LUÔN lấy (L-D8), TRƯỚC mọi khóa dòng
       SELECT u.status, r.code FROM users u JOIN roles r … WHERE user_id = @target FOR UPDATE OF u
         không có → NotFound · status = disabled → NoChange (L-D10)
       UPDATE identity.users SET status = 'disabled', updated_at = @now WHERE user_id = @target
       UPDATE identity.refresh_tokens SET revoked_at = @now WHERE user_id = @target AND revoked_at IS NULL   -- MỌI family
       EnsureRemainsAsync = false → ROLLBACK → LastAdmin
       IAuditTrail.AppendAsync(tx, { actor, user.lock, "user", target, { reason } })    -- L-D9: không `revocation`
     COMMIT → Changed

   UnlockAsync: cùng khung; UPDATE status = 'active', locked_until = NULL, failed_login_count = 0;
     đang active và không locked_until → NoChange; audit user.unlock. Bất biến không thể thủng khi mở khóa, nhưng
     vẫn lấy khóa tư vấn (L-D8 — một luật, không ngoại lệ để nhớ).
   ```

   `IAuditTrail` inject vào store (Infrastructure), vì store là chỗ có `tx`.

3. **Service** — `AccountAdministrationService` (Application), nơi thứ tự **DB → Redis** hiện ra trên màn hình:

   ```csharp
   public async Task<Result<AdminUserChange>> LockAsync(Guid targetId, Guid actorId, LockRequest req, CancellationToken ct)
   {
       if (targetId == actorId)
           return AdminErrors.SelfLock;                  // 400 — khóa xong thì không còn phiên để mở lại (Đ-6.7)

       var outcome = await store.LockAsync(targetId, actorId, req.Reason.Trim(), time.GetUtcNow(), ct);   // COMMIT xong ở đây
       if (outcome is AdminOutcome.NotFound) return AdminErrors.UserNotFound;
       if (outcome is AdminOutcome.LastAdmin) return AdminErrors.LastAdmin;

       var revocation = outcome is AdminOutcome.NoChange
           ? RevocationState.NotNeeded
           : await revoker.RevokeAsync(targetId, ct);    // SAU COMMIT (Đ-6.6) — không return nào chen giữa hai dòng này
       return new AdminUserChange(await queries.GetAsync(targetId, ct), revocation);
   }
   ```

   `revoker` là một lớp nhỏ `UserRevoker`: gọi `ITokenRevocationStore.RevokeUserAsync(userId, now)` tối đa **3 lần** trong ~1 giây
   (chờ 0 / 250 / 500 ms, qua `TimeProvider` để test không ngủ thật). Hỏng cả ba → `LogError("Không ghi được mốc thu hồi cho tài khoản
   {UserId}", …)` (không email) + `BusinessMetrics.RevocationFailed()` → `Deferred`.

4. **Lỗi** — `AdminErrors` (Identity/Application, khuôn `IdentityErrors`): `SelfLock` (400), `UserNotFound` (404),
   `LastAdmin` (409, `type urn:socialapp:problem:last-admin`, *"Hệ thống phải còn ít nhất một quản trị viên đang hoạt động."*).

5. **Metric** — `BusinessMetrics`: `socialapp_revocation_failures_total` (không nhãn), `_ = …` trong `Initialize()`,
   `RevocationFailed()`. `MetricsEndpointTests` thêm tên này vào danh sách phải có từ lúc khởi động.

6. **Controller** — hai action trên `AdminUsersController`: `POST {userId:guid}/lock` `[RequirePermission(PermissionCodes.UserLock)]`,
   `POST {userId:guid}/unlock` `[RequirePermission(PermissionCodes.UserUnlock)]`. `LockRequestValidator`: `reason` sau trim 1–500.
   Unlock không body.

7. **Yaml** — hai operation, `LockRequest`, `AdminUserChange` (`revocation` enum ba giá trị), 409 `type last-admin`.

8. **Test** (`Admin/AccountLockTests`, `Admin/AdminInvariantConcurrencyTests`):

   | Id | Kịch bản | Kỳ vọng |
   |---|---|---|
   | `ADM-01` | Admin khóa X (X đang đăng nhập hai thiết bị) | `status = disabled`; **cả hai** family có `revoked_at`; có key `revoked:user:{X}`; access cũ → 401; refresh → 401; login đúng mật khẩu → 403 `account-disabled`; sai mật khẩu → 401; audit `user.lock` có `metadata.reason`; 200 `revocation: applied` |
   | `ADM-01b` *(đề xuất)* | Khóa X lần hai | 200 `not-needed`; vẫn **một** dòng audit |
   | `ADM-02` | Mở khóa X đang `disabled` **và** `locked_until` tương lai, `failed_login_count = 4` | Đăng nhập được ngay; `locked_until` null, bộ đếm 0; audit `user.unlock`; `not-needed` |
   | `ADM-03` | Admin tự khóa mình | 400; DB không đổi; 0 dòng audit |
   | `ADM-04-lock` | Khóa Admin hoạt động **cuối cùng** (Admin kia đã `disabled`) | 409 `last-admin`; DB không đổi; **không** key `revoked:user`; 0 dòng audit |
   | `ADM-06` | Đăng ký `ITokenRevocationStore` giả luôn ném (sau `AddSharedKernelTokenRevocation`, thay bằng `Replace`) | 200 `deferred`; DB đã đổi; metric +1; log Error **không** chứa email |
   | `ADM-C2` ⭐ | Đúng hai Admin X, Y; X khóa Y ‖ Y khóa X (`Task.WhenAll`, hai client) | Luôn còn ≥ 1 Admin `active`; đúng một 200 và một **409** — **20/20 lượt** (một ca, vòng lặp 20 lần trong ca hoặc chạy lệnh luật 10) |
   | `TC-A05-mod-lock` | matrix | 403 |

   Thử cho đỏ trước khi commit (luật 10, ghi vào thân): bỏ `AcquireAsync` → `ADM-C2` đỏ ≥ 1/20; đếm **trước** `UPDATE` → `ADM-C2`
   đỏ; bỏ `UPDATE refresh_tokens` → `ADM-01` vế "cả hai family có `revoked_at`" đỏ.

**Tự rà trước commit** (B.10 #1, ghi vào thân commit): `RevokeAsync` nằm **sau** `store.LockAsync` trả về (đã `COMMIT`); không nhánh
nào `return` giữa hai bước mà bỏ quên thu hồi; `UnlockAsync` không gọi thu hồi.

### Cạm bẫy đã biết

1. **Lấy khóa tư vấn có điều kiện**: L-D8.
2. **Khóa tư vấn sau khóa dòng** (`FOR UPDATE` rồi mới `pg_advisory_xact_lock`): hai thao tác chờ nhau thì ra deadlock `40P01` → 500.
   Khóa tư vấn luôn **trước**, cùng luật "khóa family trước khóa dòng" của `RefreshTokenStore`.
3. **Đếm Admin bằng `role_id = 3`** thay vì join `roles.code = 'ADMIN'`: id là chi tiết seed. Dùng `SystemRoles.Admin`.
4. **`RevokeUserAsync` trong transaction**: Redis ghi xong, DB rollback (last-admin), và người bị "khóa hụt" vẫn bị 401 vô cớ 15 phút.
5. **Test đồng thời dùng chung một `HttpClient` và một token**: không đồng thời thật ở tầng DB. Hai client, hai Admin, `Task.WhenAll`.
6. **`ADM-06` thay store bằng fake rồi quên `OnTokenValidated` cũng đọc store đó** thì mọi request 401/503. Fake chỉ ném ở
   `RevokeUserAsync`, `CheckAsync` trả `NotRevoked`.

---

## 6. D4 — `PUT /admin/users/{id}/role`

**Mục tiêu:** nâng/hạ vai trò có hiệu lực ở request kế tiếp mà người đó không mất phiên.

**Kết quả mong đợi:** `ADM-05`, `ADM-04-role`, `ADM-C1` (20/20), phần gán của `ROLE-01` xanh.

### Các bước

1. **Store** `AssignRoleAsync(targetId, actorId, roleCode, now)`: cùng khung D3. `AcquireAsync` → `SELECT … FOR UPDATE` target
   (NotFound) → tra `role_id` theo `code` (không có → `UnknownRole`) → cùng vai trò → `NoChange` → `UPDATE users SET role_id` →
   `EnsureRemainsAsync` → audit `role.assign { fromRole, toRole }` → `COMMIT`. **Không** đụng `refresh_tokens`: refresh cùng family
   phải cấp được token mới mang vai trò mới (Mục 7.3).
2. **Service** — cùng khuôn `LockAsync`: tự hạ vai trò **được** (bất biến lo phần còn lại, Đ-6.7). `UnknownRole` → 400
   `errors.roleCode` *"Vai trò không tồn tại."*. `Changed` → thu hồi sau `COMMIT`.
3. **Controller** `PUT {userId:guid}/role` `[RequirePermission(PermissionCodes.RoleAssign)]`; `AssignRoleRequestValidator` (`roleCode`
   không rỗng, ≤ 30).
4. **Yaml** thêm operation + `AssignRoleRequest`.
5. **Test** (`Admin/AssignRoleTests` + ca vào `AdminInvariantConcurrencyTests`):

   | Id | Kịch bản | Kỳ vọng |
   |---|---|---|
   | `ADM-05` | Hạ X từ MODERATOR xuống USER | 200 `applied`; access cũ → 401; `POST /auth/refresh` cùng cookie → **200**, token mới `role = USER`; `/me` của X → `role: USER`, `permissions` không còn `report.resolve` |
   | `ADM-04-role` | Hạ Admin hoạt động cuối cùng | 409 `last-admin`; không key `revoked:user` |
   | `ADM-C1` ⭐ | Đúng hai Admin X, Y; X hạ Y ‖ Y hạ X | ≥ 1 Admin; một 200, một 409 — **20/20** |
   | `ROLE-01` (gán) | Gán vai trò tự tạo (SQL, D5 chưa có) cho R | R refresh → token `role` = mã vai trò mới; `/me.permissions` đúng tập |
   | `ADM-05b` *(đề xuất)* | Gán đúng vai trò đang có | 200 `not-needed`; không audit; không key `revoked:user` |

### Cạm bẫy đã biết

1. **Thu hồi family khi đổi vai trò** (chép nhầm từ D3): X bị đăng xuất khỏi mọi thiết bị vì một lần được nâng quyền. Mục 7.3
   ghi rõ "**Không** bị đăng xuất".
2. **`roleCode` so không phân biệt hoa thường** ở một chỗ mà chỗ khác thì có: `code` là `^[A-Z]…` (D5). So chính xác.
3. **Không lấy khóa tư vấn khi nâng ai đó lên ADMIN**: L-D8 bao luôn.

---

## 7. D5 — CRUD vai trò + `GET /admin/permissions`

**Mục tiêu:** vai trò là dữ liệu sống; sửa quyền có hiệu lực ở request kế tiếp trên mọi instance; không thao tác nào tự khóa hệ thống.

**Kết quả mong đợi:** `ROLE-01..07` (trừ vế `hide`), `PERM-01` qua API, `TC-A05-roles`, unit `RolePermissionDiff` xanh.

### Các bước

1. **`Error.Extensions`** (L-D11) — `SharedKernel/Results/Result.cs`: tham số cuối
   `IReadOnlyDictionary<string, object?>? Extensions = null`. `ResultHttpExtensions.Problem`: nhánh không-`Errors` dựng
   `ProblemDetails` qua `ProblemDetailsFactory` (để có `traceId`) rồi chép `Extensions`. Doc comment ghi "chỉ-thêm như Q-D4, Q-E4".
   `ProblemDetailsTests` thêm một ca: lỗi có `Extensions` → JSON có đúng key, vẫn có `traceId`.

2. **`RolePermissionDiff`** — hàm thuần ở `Identity/Application/Roles/`:

   ```csharp
   public sealed record RolePermissionDiff(IReadOnlyList<string> Added, IReadOnlyList<string> Removed, bool IsEmptyAfter)
   {
       public bool HasChanges => Added.Count > 0 || Removed.Count > 0;
       public static RolePermissionDiff Compute(IReadOnlySet<string> current, IReadOnlySet<string> requested) => …;   // sắp Ordinal
   }
   ```

   Unit: thêm/bớt/không đổi/về rỗng/trùng mã trong request.

3. **Năm endpoint** — `AdminRolesController` `[Route("api/v1/admin/roles")]` + `AdminPermissionsController`
   `[Route("api/v1/admin/permissions")]`, cả hai `[PrivilegedEndpoint]`, mọi action `[RequirePermission(PermissionCodes.RoleManage)]`:

   | Endpoint | Luật | Ghi |
   |---|---|---|
   | `GET /admin/roles` | Mỗi vai trò: `permissions` = `EffectivePermissions.For` (D1), `userCount` (một câu `GROUP BY`), `isSystem` = `code ∈ {ADMIN, USER, MODERATOR}` (tính, không cột), `editable = code != ADMIN` | — |
   | `POST /admin/roles` | Validator: `code` `^[A-Z][A-Z0-9_]{2,29}$`, không ∈ ba mã hệ thống; `displayName` 1–50; `permissions` ⊆ `PermissionCodes.All` (lạ → 400 `errors.permissions`). `role_id = nextval('identity.roles_role_id_seq')` (A3). `code` trùng: bắt `23505` → 409 `type …:role-code-taken` | audit `role.create { code, added }` |
   | `PATCH /admin/roles/{roleId}` | `RenameRoleRequest` mang `[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]`, nên body có `code` (hay trường lạ bất kỳ) → 400. `ValidationErrors.From` đã làm sạch thông điệp `JsonException` (không lộ tên kiểu) | audit `role.rename`. Đổi tên vai trò hệ thống **được** (trigger chỉ chặn `code`) |
   | `PUT /admin/roles/{roleId}/permissions` | ADMIN → 409 `type …:system-role`. Mã lạ → 400. USER/MODERATOR + `IsEmptyAfter` → 400 `errors.permissions` *"Vai trò hệ thống phải còn ít nhất một quyền."* (`admin.role-needs-permission`). USER/MODERATOR + `HasChanges` + `confirm != true` → **409** `type …:confirmation-required`, `Extensions { added, removed, affectedUsers }`. Không đổi gì → 200, không audit, không notify | Transaction: `SELECT role FOR UPDATE` → `DELETE`/`INSERT role_permissions` → audit `role.permissions { code, added, removed, confirmed }` → `COMMIT` → `notifier.NotifyAsync(code)` (C3, **một** lời gọi) |
   | `DELETE /admin/roles/{roleId}` | Ba vai trò hệ thống → 409 `system-role` (service chặn trước). `userCount > 0` → 409 `type …:role-in-use`. Lưới: bắt `23503` → `role-in-use`, bắt `P0001` (trigger A3) → `system-role` — **không** để 500 | audit `role.delete`; `COMMIT` → `NotifyAsync(code)` |
   | `GET /admin/permissions` | 18 dòng `{ code, description }` theo `permission_id` | — |

   `affectedUsers` = số tài khoản mang vai trò, **không** lọc `status`. Tài khoản bị khóa mở lại vẫn mang quyền mới. FE hiện đúng
   số server trả (Đ-6.21).

4. **Yaml** — sáu operation, `RoleSummary`, `CreateRoleRequest`, `RenameRoleRequest` (`additionalProperties: false`),
   `SetRolePermissionsRequest`, `PermissionInfo`, `ConfirmationRequiredProblem`.

5. **Test** (`Admin/RoleManagementTests`):

   | Id | Kịch bản | Kỳ vọng |
   |---|---|---|
   | `ROLE-01` ⭐ (tạo + gán) | `POST /admin/roles { REVIEWER, [report.resolve] }` → gán cho R (D4) → R refresh | 201; `/me` của R có đúng `report.resolve`. Vế `GET /reports` 200 / `hide` 403 ở D7c — **không sửa dòng nghiệp vụ nào** |
   | `ROLE-02` | `PATCH` có `code` · có trường lạ | 400 · 400; thân lỗi không chứa `SocialApp.` |
   | `ROLE-03` | Xóa ADMIN/USER/MODERATOR · vai trò còn người · vai trò tự tạo trống | 409 `system-role` · 409 `role-in-use` · 204 |
   | `ROLE-04` | Sửa quyền USER không `confirm` · có `confirm` · về rỗng | 409, `removed`/`affectedUsers` đúng · 200 · 400 |
   | `ROLE-06` | Sửa quyền ADMIN | 409 `system-role` |
   | `ROLE-07` | Sửa quyền MODERATOR qua API, chạy seeder lại | Chỉnh sửa còn nguyên (đã có bản A3; thêm vế "qua API") |
   | `PERM-01` (API) | Gỡ `post.create` của USER (`confirm: true`) rồi `POST /posts` ngay, không tua đồng hồ | 403 ở request kế tiếp |
   | `TC-A05-roles` | matrix | 403 |

### Cạm bẫy đã biết

1. **`JsonUnmappedMemberHandling` ở `JsonSerializerOptions` toàn cục** (Program.cs) thay vì trên đúng record: mọi endpoint từ chối
   trường lạ, và FE cũ gửi thừa trường là 400 khắp nơi. Chỉ đặt trên `RenameRoleRequest`.
2. **`NotifyAsync` trước `COMMIT`**: instance khác xóa cache, nạp lại từ DB **chưa commit**, và giữ quyền cũ 60 giây. Đúng bẫy L-C5
   của C3, nhưng từ phía người gọi.
3. **Bước xác nhận chỉ ở FE**: gọi thẳng API (Swagger trên staging) là gỡ được `post.create` không hỏi. Server là chỗ bắt buộc (Đ-6.9).
4. **Tính `affectedUsers` sau khi đã ghi** (trong transaction lần hai): ra đúng số nhưng thao tác đã xảy ra. 409 trả **trước** mọi ghi.
5. **Đếm "vai trò hệ thống" bằng `role_id ≤ 3`**: dùng mã. Sequence bắt đầu từ 100 chỉ là lưới phụ.

---

## 8. D6 — `POST /reports` *(moderation-v1 ra đời)*

**Mục tiêu:** tiếp nhận báo cáo mà không thành máy dò IDOR; một báo cáo mở mỗi (người, đối tượng).

**Kết quả mong đợi:** checklist D0 cho `moderation-v1`; `REP-01..06`, `REP-C1` (20/20), `REP-IDOR` xanh.

### Các bước

1. **Nền `moderation-v1`** theo checklist Mục 2.
2. **`IModerationTargets.Supports(type)`** (L-D13) — chỉ-thêm ở interface + `ModerationTargets` (composite: có provider cho loại đó
   không). Sửa fake nếu có (L-C10). Unit: `Supports(Comment) == false` khi chỉ có provider bài và người dùng.
3. **Hai controller** (cạm bẫy 3 Mục 2):
   - `ReportSubmissionController` `[Route("api/v1/reports")]`, **không** `[PrivilegedEndpoint]`: `POST` `[RequirePermission(ModerationPermissions.ReportCreate)]`
     `[EnableRateLimiting(SharedKernelExtensions.ReportCreateRateLimitPolicy)]`. Doc comment: *"endpoint duy nhất của moderation-v1
     không đặc quyền — B.10 #8, cố ý"*.
   - `ReportsController` `[Route("api/v1/reports")]` `[PrivilegedEndpoint]` sẽ nhận `GET`/`PATCH` ở D7b, D7c.
4. **`CreateReportRequestValidator`**: `targetType` ∈ `post|comment|user`, `reasonCode` ∈ `ReasonCodes.All` (A1), `detail` sau
   trim 1–500 khi có, **bắt buộc** khi `other` → `errors.detail` *"Vui lòng mô tả lý do."*.
5. **`ReportSubmissionService`**:

   ```
   target = new ModerationTarget(type, id)
   !targets.Supports(type)                                   → 404 ReportTargetNotFound   (L-D13)
   snapshot = GetSnapshotsAsync([target]) ; không có          → 404 (cùng Error)
   !CanViewAsync(actor, target)                              → 404 (cùng Error)            ← Đ-6.12, REP-IDOR
   snapshot.AuthorId == actor                                → 400 "Không thể báo cáo nội dung của chính mình."
   store.CreateOrGetOpenAsync(actor, target, reason, detail) → (receipt, created)
   ```

   Thứ tự "tồn tại → thấy được → của mình" và **cùng một** `Error` cho hai nhánh 404. Nếu "không thấy" trả khác "không tồn tại"
   thì status code lại lộ bài riêng tư có tồn tại.
6. **`ReportStore.CreateOrGetOpenAsync`** — SQL thô trên `ModerationDbContext`, đúng câu A1 đã khóa hình dạng
   (`Report_on_conflict_voi_index_mot_phan_khong_chen_trung`):

   ```sql
   INSERT INTO moderation.reports (id, reporter_id, target_type, target_id, reason_code, detail, status, created_at, updated_at)
   VALUES (@id, @reporter, @type, @target, @reason, @detail, 'open', @now, @now)
   ON CONFLICT (reporter_id, target_type, target_id) WHERE status = 'open' DO NOTHING
   RETURNING id, created_at;
   -- 0 dòng → SELECT id, created_at FROM moderation.reports
   --          WHERE reporter_id = @reporter AND target_type = @type AND target_id = @target AND status = 'open'
   ```

   Controller: `created` → `StatusCode(201, receipt)` (không `CreatedAtAction`, vì người báo không có `GET` nào để trỏ tới); còn lại → 200.
7. **Rate limit** `report-create` — `SharedKernelExtensions`: hằng `ReportCreateRateLimitPolicy = "report-create"`, một khối
   `AddPolicy` riêng (Mục 9.4). Fixed window 10/phút, **phân vùng theo `sub`** (người đã đăng nhập, vì middleware authorization chạy
   trước rate limiter, xem Program.cs), 429 đi qua cùng `OnRejected` có sẵn.
8. **Yaml** — `POST /reports`, `CreateReportRequest`, `ReportReceipt`, 201/200/400/401/404/429.
9. **Test** (`Moderation/ReportSubmissionTests`):

   | Id | Kịch bản | Kỳ vọng |
   |---|---|---|
   | `REP-01` | Báo bài công khai · báo lại lần hai | 201 · 200 **cùng `reportId`** |
   | `REP-02` | Báo bài `private` của người khác · id không tồn tại · `targetType: comment` | 404 · 404 · 404 — **cùng thân lỗi** (so `title`, `detail`, `type`) |
   | `REP-03` | Báo bài của chính mình · báo chính mình (`user`) | 400 · 400 |
   | `REP-04` | `other` không `detail` · `detail` 501 ký tự | 400 `errors.detail` · 400 |
   | `REP-05` | Báo cáo thứ 11 trong một phút (11 bài khác nhau) | 429 |
   | `REP-06` | Báo lại sau khi báo cáo cũ đã `dismissed` (đặt bằng SQL, D7c chưa có) | 201, báo cáo **mới** |
   | `REP-07` *(đề xuất)* | Báo bài `friends` của **bạn** · của người lạ | 201 · 404 |
   | `REP-C1` ⭐ | 10 lượt `POST` giống hệt từ một người, song song | 1 dòng; 1 × 201, 9 × 200; không 500 — 20/20 |
   | `REP-IDOR` | matrix | 404 |

### Cạm bẫy đã biết

1. **`ON CONFLICT` thiếu vế `WHERE status = 'open'`** ra `42P10` lúc **chạy** (Mục 4 chỗ dễ sai 1). Câu đã khóa ở A1, nên chép
   đúng câu đó.
2. **Rate limit phân vùng theo IP** (chép policy `auth`): mười người sau NAT trường học chung một hạn mức. Theo `sub`.
3. **`REP-05` đếm trúng hạn mức chung** 100/phút thay vì 10: dùng bài khác nhau cho mỗi lượt (báo trùng vẫn tính lượt), khẳng định
   lượt thứ **11** là 429, không phải "có 429 ở đâu đó".
4. **`detail` vào log** ("báo cáo {Detail}"): nội dung người dùng tự gõ, có thể chứa PII. Log `reportId`.
5. **Trả `target` trong `ReportReceipt`**: hợp đồng cố ý không trả (Mục 8.1), không xác nhận thêm gì.

---

## 9. D7a — Đường đọc `hidden` của Content *(mở content-v1 chỉ-thêm)*

**Mục tiêu:** BR-07 phía người đọc; đóng hai lỗ đang mở (L-D4).

**Kết quả mong đợi:** `HID-01..06` xanh; cổng hợp đồng Content xanh; `schema.d.ts` của Content sinh lại cùng commit.

### Các bước

1. **DTO** — `PostModeration(string Status, string ReasonCode, DateTimeOffset HiddenAt)`; `PostResponse` thêm
   `PostModeration? Moderation` **cuối** record (Mục 9.4: A cũng thêm trường, nên ai thêm cũng đặt cuối).
2. **Mapper** — `PostResponseMapper.ToResponse`: `Moderation` **chỉ** khác null khi `post.Status == Hidden && actorId == post.AuthorId`
   (`Status = "hidden"`, `ReasonCode = post.HiddenReason`, `HiddenAt = post.UpdatedAt`). Mục 4: không thêm cột `hidden_at`, vì
   `HideAsync` (C2) đặt `updated_at = now()` và tác giả không sửa được bài bị ẩn.
3. **`PostReadService.GetAsync`** — ngay sau khi tải bài:

   ```csharp
   // BR-07 (Đ-6.14): bài bị ẩn — người khác (kể cả bạn bè, Moderator) thấy như bài không tồn tại; tác giả thấy kèm lý do.
   // Moderator xem nội dung qua GET /reports/{id}, không qua đường vòng quanh BR-02.
   if (post.Status == PostStatus.Hidden && post.AuthorId != actorId)
       return ContentErrors.PostNotFound;
   ```

4. **`PostService.UpdateAsync`** — sau tầng 3 (vẫn 403 cho "không phải của bạn"), trước BR-01:
   `post.Status == Hidden` → `ContentErrors.PostHidden` (409, `type urn:socialapp:problem:post-hidden`, *"Bài viết đã bị ẩn do vi
   phạm tiêu chuẩn cộng đồng nên không sửa được."*). `DeleteAsync` **không** đổi: người dùng luôn xóa được nội dung của mình.
5. **`content-v1.yaml`** chỉ-thêm: `PostModeration`, `PostResponse.moderation` (**không** required), 409 `post-hidden` ở `PATCH
   /posts/{postId}`. `info.version` `1.0.2-gd4` → `1.1.0-gd6`. `pnpm gen:api`; FE không sửa màn (biểu ngữ là E9).
6. **Test** (`Content/HiddenPostTests`). Ẩn bài bằng `IModerationTargets.HideAsync` trong một transaction của test (dùng hợp đồng
   thật của C2, không SQL tay):

   | Id | Kịch bản | Kỳ vọng |
   |---|---|---|
   | `HID-01` | Tác giả `GET /posts/{id}` bài `hidden` · bài `published` | 200 + `moderation { status: hidden, reasonCode, hiddenAt }` · `moderation` vắng/null |
   | `HID-02` | Bạn bè đọc bài `hidden` (`privacy: friends`) | 404, cùng thân với id không tồn tại |
   | `HID-03` | Moderator đọc bài `hidden` công khai | 404 |
   | `HID-04` | Tác giả `PATCH` bài `hidden` | 409 `type …:post-hidden`; `body` không đổi |
   | `HID-05` | Tác giả `DELETE` bài `hidden` | 204 |
   | `HID-06` | Feed + trang cá nhân của tác giả | Không có bài đó (khẳng định lại Đ-4.11 qua API) |

### Cạm bẫy đã biết

1. **Chỉ thêm nhánh tác giả** mà quên nhánh người khác: L-D4. `HID-02` bắt.
2. **Gắn `moderation` trong `PostHydrator`/feed**: feed không bao giờ có bài `hidden` (Đ-4.11). Chỉ mapper, chỉ một điều kiện.
3. **Rebase với A** đỏ ở `schema.d.ts`: chạy lại `pnpm gen:api`, **không** sửa tay để gỡ xung đột (Mục 9.4, luật frontend #3).
4. **`hiddenAt` lấy `EditedAt`**: đó là lần sửa cuối của tác giả, không phải lúc bị ẩn.

---

## 10. D7b — Hàng đợi và chi tiết báo cáo

**Mục tiêu:** Moderator thấy mỗi đối tượng một dòng; mở ra thì thấy nội dung thật mà không biết ai báo.

**Kết quả mong đợi:** hai operation `GET` trong `moderation-v1`; ca gom nhóm, keyset, ảnh chụp xanh; `TC-A06-queue` 403 + audit.

### Các bước

1. **`GET /reports?status=open&cursor=&limit=`** — `ReportsController` `[RequirePermission(ModerationPermissions.ReportResolve)]`.
   `status` chỉ nhận `open` (giá trị khác → 400; giữ chỗ trong hợp đồng). Một câu SQL:

   ```sql
   SELECT target_type, target_id,
          min(created_at)                                   AS first_reported_at,
          (array_agg(id ORDER BY created_at, id))[1]        AS report_id,          -- đại diện = báo cáo mở CŨ NHẤT
          count(*)                                          AS report_count,
          count(*) FILTER (WHERE reason_code = 'spam')      AS spam, …             -- năm lý do, dựng từ ReasonCodes.All
   FROM moderation.reports
   WHERE status = 'open'
   GROUP BY target_type, target_id
   HAVING (min(created_at), target_id) > (@cursorAt, @cursorId)                   -- keyset (Mục B.6 D7)
   ORDER BY first_reported_at, target_id
   LIMIT @limit + 1;
   ```

   `reasons` chỉ gồm lý do có đếm > 0. Keyset trên `target_id` là đủ, không cần `target_type` (UUID v7 không trùng giữa bảng).
2. **`GET /reports/{reportId}`** — báo cáo không tồn tại (mọi `status`) → 404. Còn lại:
   - `GetSnapshotsAsync([target])` → `TargetSnapshot` của hợp đồng: `author` hydrate `IUserDirectory` (một lô), `media` ký
     `IObjectStorage.CreatePresignedGet` cho từng `MediaKeys`, `status` là chuỗi DB (`published|hidden|deleted|active|disabled`).
     Đối tượng biến mất khỏi mọi bảng (không có snapshot) → `target` với `status: "deleted"`, `author: null`, không `body`.
   - `openReports`: mọi báo cáo **mở** cùng đối tượng — `reportId`, `reasonCode`, `detail`, `createdAt`. **Không** `reporterId`.
   - `history`: báo cáo đã đóng của cùng đối tượng, gom theo `(status, resolver_id, resolved_at, resolution_note)`. Mỗi quyết
     định một dòng `{ outcome, resolverId, resolvedAt, note }`, với `outcome` là `resolved | dismissed` (L-D15).
3. **Yaml** — hai operation, `ReportQueueItem`, `ReportQueuePage`, `TargetSnapshot`, `ReportDetail` (`history[].outcome`), 403 + 503.
4. **Test** (`Moderation/ReportQueueTests`):

   | Id | Kịch bản | Kỳ vọng |
   |---|---|---|
   | `QUE-01` *(đề xuất)* | 3 người báo bài P (spam, spam, violence), 1 người báo bài Q | 2 dòng; P trước (cũ hơn), `reportCount = 3`, `reasons = { spam: 2, violence: 1 }`, `reportId` = báo cáo cũ nhất |
   | `QUE-02` | 45 đối tượng, `limit = 20` | 20/20/5 không trùng không sót |
   | `QUE-03` | Chi tiết bài `private` của người khác, bài đã `deleted` (bằng API xóa) | 200, có `body` (Moderator phải thấy mới quyết được) |
   | `QUE-04` | JSON của `ReportDetail` | không có key `reporterId` ở bất kỳ độ sâu nào |
   | `QUE-05` | Chi tiết bài 1 ảnh và bài 4 ảnh | số câu SQL bằng nhau |
   | `TC-A06-queue` + `AUD-03` qua endpoint thật | USER gọi `GET /reports` | 403; một dòng `access.denied`, `routeTemplate = api/v1/reports` |

### Cạm bẫy đã biết

1. **Trả `reporterId`** "vì Moderator có quyền": hợp đồng cố ý không trả (tránh trả thù, Mục 8.1).
2. **Snapshot đọc qua `GET /posts/{id}` nội bộ**: BR-02 và BR-07 chặn Moderator. `IModerationTargets` (C2) đã dùng
   `IgnoreQueryFilters` có chủ đích, và đây là đường **duy nhất** (Mục 8.1).
3. **Ký URL ảnh trước khi kiểm quyền**: tầng 2 ở attribute đã chặn trước khi vào action, nhưng đừng ký trong store.
4. **`array_agg(id)` không `ORDER BY`**: đại diện ngẫu nhiên, FE mở chi tiết ra báo cáo khác mỗi lần.

---

## 11. D7c — Quyết định, khôi phục, `ContentHidden` ⭐

**Mục tiêu:** mốc 3. Ẩn + đóng mọi báo cáo mở + audit trong **một** transaction; tầng 2 kép; bốn AC của US-019.

**Kết quả mong đợi:** `MOD-01..06`, `MOD-C1` (20/20), `TX-01`/`TX-02` qua API, `AUD-01`, `TC-A06`, `TC-A06b`, vế `hide` của `ROLE-01`
xanh; `socialapp_reports_decided_total` có ba nhãn từ lúc khởi động.

### Các bước

1. **Bảng hợp lệ** — hàm thuần `DecisionRules.IsAllowed(ReportDecision, ModerationTargetType)` + unit test đủ 9 ô:

   | | `post` | `comment` | `user` |
   |---|---|---|---|
   | `hide` | ✓ | ✓ (khi có provider) | ✗ 400 |
   | `dismiss` | ✓ | ✓ | ✓ |
   | `resolve` | ✗ 400 | ✗ 400 | ✓ + `note` bắt buộc |

2. **`DecideReportService.DecideAsync(reportId, actorId, actorRole, request)`**:

   ```
   0. request.Decision == Hide && !cache.IsAllowedAsync(actorRole, ModerationPermissions.PostHide)
        → IAuditTrail.AppendAsync(null, access.denied { method, routeTemplate, permission: "post.hide" }) → 403   (L-D12)
   1. đọc báo cáo (không khóa) → 404 · DecisionRules sai cặp → 400 · resolve thiếu note → 400
   2. hide: snapshot = GetSnapshotsAsync([target])  — lấy AuthorId, PostId cho event (tác giả không đổi)
   BEGIN (ModerationDbContext) ; tx = CurrentTransaction.GetDbTransaction()
   3. SELECT status FROM moderation.reports WHERE id = @rid FOR UPDATE → <> 'open' → ROLLBACK → 409 report-already-decided (AC-04)
   4. hide: outcome = targets.HideAsync(tx, target, reasonCode ?? report.ReasonCode)
        NotFound → ROLLBACK → 409 moderation-target-gone · AlreadyHidden → đi tiếp (báo cáo thứ hai cho bài đã ẩn)
   5. UPDATE moderation.reports SET status = <resolved|dismissed>, resolver_id = @actor, resolved_at = @now,
        resolution_note = @note, updated_at = @now
      WHERE target_type = @t AND target_id = @id AND status = 'open' RETURNING id          -- ĐÓNG MỌI báo cáo mở của đối tượng
   6. IAuditTrail.AppendAsync(tx, { actor, report.<decision>, targetType, targetId,
        { reportIds: [...RETURNING], reasonCode, note } })                                  -- không body, không detail
   COMMIT
   7. BusinessMetrics.ReportDecided(decision)
   8. hide && outcome == Hidden → publisher.Publish(new ContentHidden(type, id, snapshot.PostId, snapshot.AuthorId, reasonCode))
        (AlreadyHidden: tác giả đã được báo ở lần ẩn đầu — không phát lại)
   ```

   Trả `ReportDecisionResult { decision, closedReportIds, targetStatus }`. `hide` → `hidden`, còn lại → trạng thái hiện tại của snapshot.
   `actorRole` do controller đọc `User.FindFirstValue(JwtClaims.Role)`, không bao giờ từ body.

3. **Khôi phục** — `ModerationTargetsController` `[Route("api/v1/moderation/targets")]` `[PrivilegedEndpoint]`,
   `POST {targetType}/{targetId:guid}/restore` `[RequirePermission(ModerationPermissions.PostHide)]`, body `{ note }` (≤ 500, tùy chọn).
   Transaction: `RestoreAsync(tx)` → `NotHidden` → 409 `moderation-not-hidden` · `NotFound` → 404 → audit `content.restore
   { note }` → `COMMIT`. **Không** mở lại báo cáo đã đóng, **không** phát event (Mục 13 không có loại thông báo "được khôi phục").
   `targetType = user` → 400 (không có "ẩn người dùng").

4. **Lỗi** — `ModerationErrors` (Moderation/Application): `ReportNotFound` (404), `ReportAlreadyDecided`, `TargetGone`,
   `TargetNotHidden` (409, ba `type` Mục 8.1), `InvalidDecision` (400 `errors.decision`), `NoteRequired` (400 `errors.note`).

5. **Metric** — `socialapp_reports_decided_total{decision}`, `Initialize()` tạo sẵn `hide`, `dismiss`, `resolve`; `MetricsEndpointTests`.

6. **Yaml** — `PATCH /reports/{reportId}`, `POST /moderation/targets/{targetType}/{targetId}/restore`, `DecideReportRequest`,
   `ReportDecisionResult`, ba 409 có `type`.

7. **Test** (`Moderation/DecideReportTests`, `Moderation/ModerationTransactionTests`):

   | Id | Kịch bản | Kỳ vọng |
   |---|---|---|
   | `MOD-01` ⭐ | AC-01: ba người báo bài P → Moderator `hide` | P `hidden` + `hidden_reason`; **ba** báo cáo `resolved` cùng `resolver_id`; **một** dòng audit `report.hide` có đủ ba `reportIds`; tác giả `GET /posts/P` → 200 + `moderation`; người báo → 404 |
   | `MOD-02` | AC-02: `dismiss` | `dismissed`; P `published`; audit `report.dismiss`; không event |
   | `MOD-03` | AC-03: USER gọi `PATCH` | 403 + đúng một `access.denied` |
   | `MOD-04` | AC-04: quyết lần hai | 409 `report-already-decided` |
   | `MOD-05` | `resolve` cho báo cáo bài · `hide` cho báo cáo người dùng · `resolve` thiếu `note` | 400 · 400 · 400 |
   | `MOD-06` | Khôi phục P bị ẩn · khôi phục bài `published` | 200 + audit `content.restore`, P `published`, `hidden_reason` null · 409 `moderation-not-hidden` |
   | `MOD-07` *(đề xuất)* | `hide` báo cáo thứ hai của bài **đã** ẩn | 200; báo cáo đóng; **không** `ContentHidden` thứ hai (drain rồi đếm metric `published{event=ContentHidden}`) |
   | `ROLE-01` (hide) | R (`REVIEWER`) `GET /reports` · `dismiss` · `hide` | 200 · 200 · **403** + một `access.denied` có `permission: post.hide` |
   | `TX-01` ⭐ | `IAuditTrail` giả **ném** (decorator đăng ký trong factory) giữa bước 6 | 500; P **vẫn** `published`; báo cáo **vẫn** `open`; 0 dòng audit |
   | `TX-02` | Provider bài giả ném trong `HideAsync` | 500; không gì thay đổi |
   | `AUD-01` | Bài có chuỗi `SECRET-xyz` trong `body`, báo cáo có `SECRET-abc` trong `detail` → `hide` | mọi dòng audit có `actor_id`, `action`, `ip`; **không** dòng nào chứa `SECRET-` |
   | `MOD-C1` ⭐ | Hai Moderator quyết cùng một báo cáo | một 200, một 409; **một** dòng audit quyết định — 20/20 |
   | `TC-A06`, `TC-A06b` | matrix | 403 · 200 |

   Thử cho đỏ trước khi commit: bỏ `FOR UPDATE` → `MOD-C1` đỏ; bước 5 chỉ đóng `id = @rid` → `MOD-01` đỏ (còn 2 báo cáo mở); audit
   ghi `tx: null` → `TX-01` đỏ (đột biến bắt buộc của B5, bản qua API). *Sửa 2026-09-25 khi thi công D7c:* `tx: null` là đột biến
   TƯƠNG ĐƯƠNG ở đây (nhánh null của `SqlAuditTrail` dùng chính kết nối scoped đang giữ transaction) — thay bằng "audit ghi SAU `COMMIT`".
   *Sửa lần hai 2026-09-25 (fix(gd6-c)):* nhánh null nay mở kết nối riêng — `tx: null` lại là đột biến có nghĩa, `TX-01` bắt.

### Cạm bẫy đã biết

1. **`Publish` trong khối `await using var tx`**: cạm bẫy C0, luật 5. Đặt sau `CommitAsync`, ngoài khối.
2. **`HideAsync` trên kết nối khác**: đã canh ở C2, nhưng decorator/fake trong test phải **chuyển tiếp đúng `tx`**, không mở kết nối
   (không thì `TX-01` xanh giả, R6-06).
3. **Tầng 2 kép viết `role == "ADMIN"`**: luật 3. `IsAllowedAsync` đã short-circuit.
4. **Kiểm `status` báo cáo trước `FOR UPDATE`** rồi mới khóa: hai Moderator cùng thấy `open`. Kiểm **trong** câu khóa.
5. **`reasonCode` của quyết định lấy từ báo cáo đại diện** khi Moderator chọn lý do khác: FE mặc định lý do được báo nhiều nhất, còn
   server lấy **đúng** `request.reasonCode` nếu có (Mục 7.2 bước 3).
6. **Thông báo `moderation` lộ Moderator**: `ContentHidden` không có trường actor, D9 ghi `last_actor_id = NULL`. Đừng "tiện tay"
   thêm `ModeratorId` vào event (`EVT-07` sẽ không bắt, vì đó là `Guid`).

---

## 12. D8 — `GET /admin/audit-logs`

**Mục tiêu:** Admin đọc nhật ký; người đọc duy nhất của bảng append-only.

**Kết quả mong đợi:** `AUD-04`, `TC-A05-mod-audit` xanh; `actor` hydrate một lô.

### Các bước

1. **`AuditLogsController`** ở **Moderation** (dữ liệu của Moderation, Mục 8.1 xếp vào `moderation-v1`),
   `[Route("api/v1/admin/audit-logs")]`, `[PrivilegedEndpoint]`, `[RequirePermission(ModerationPermissions.AuditRead)]`.
2. **Truy vấn** — keyset `id DESC` (`WHERE id < @cursor`), cursor là `id` mã hóa mờ. Lọc: `actorId` (dùng `idx_audit_logs_actor`),
   `targetType` + `targetId` (dùng `idx_audit_logs_target`; `targetId` không kèm `targetType` → 400), `action` ∈ `AuditActions.All`
   (lạ → 400), được đứng một mình (L-D14). `limit` mặc định 50, tối đa 100.
3. **Hydrate** `actor` qua `IUserDirectory` một lô (không hồ sơ → `null`); `metadata` trả nguyên JSON (`JsonElement`); `ip` là chuỗi.
4. **Yaml** — operation + `AuditLogItem`, `AuditLogPage`.
5. **Test** (`Moderation/AuditLogQueryTests`):

   | Id | Kịch bản | Kỳ vọng |
   |---|---|---|
   | `AUD-04` | 120 dòng audit trộn ba actor, bốn action; lọc từng kiểu; 3 trang | keyset đúng `id DESC`, không trùng không sót; lọc đúng |
   | `AUD-04b` *(đề xuất)* | `targetId` không `targetType` · `action = "abc"` | 400 · 400 |
   | `TC-A05-mod-audit` | matrix | 403 |

### Cạm bẫy đã biết

1. **Đọc qua `DbSet<AuditLog>` có tracking** rồi `SaveChanges` ở đâu đó: trigger append-only ném. Đường đọc luôn `AsNoTracking`.
2. **Moderator cần "xem audit"**: kế hoạch gốc hiểu là lịch sử của đối tượng đang xem (D7b `history`), không phải endpoint này (Đ-6.15, Mục 13 #9).

---

## 13. D9 — Store thông báo + upsert gộp

**Mục tiêu:** một chỗ duy nhất biến sự kiện thành dòng thông báo đã gộp, đúng dưới đồng thời.

**Kết quả mong đợi:** `NOTIF-03..05` (tầng store), `NOTIF-C1` (20/20) xanh.

### Các bước

1. **Hợp đồng nội bộ** — `Notification/Application/INotificationStore.cs`:

   ```csharp
   public sealed record NotificationUpsert(
       Guid RecipientId, string Type, string GroupKey, string TargetType, Guid TargetId, Guid? PostId,
       Guid? ActorId,            // null cho type = moderation (không lộ ai kiểm duyệt)
       string? ReasonCode);      // chỉ type = moderation

   Task<UpsertResult> UpsertAsync(NotificationUpsert upsert, CancellationToken ct);   // Skipped | Created | Updated
   ```

   Tự báo mình (`ActorId == RecipientId`) → `Skipped` **ở store** (Đ-6.16). Một chỗ canh, không handler nào quên được.

2. **Hiện thực** — `NotificationStore` (Infrastructure), một transaction trên `NotificationDbContext`, khuôn hai bước (L-D6):

   ```sql
   -- (1) Nhóm đã có: khóa dòng, đọc is_read CŨ, cập nhật
   UPDATE notification.notifications n
      SET last_actor_id = @actor, updated_at = @now, is_read = false,
          actor_count = CASE WHEN old.is_read THEN 1 ELSE n.actor_count END      -- ĐỢT MỚI khi nhóm đã đọc
     FROM (SELECT id, is_read FROM notification.notifications
            WHERE recipient_id = @r AND group_key = @k FOR UPDATE) old
    WHERE n.id = old.id
   RETURNING n.id, old.is_read AS was_read;
   -- 0 dòng → (2) INSERT … (actor_count = 1, is_read = false, created_at = updated_at = @now)
   --            ON CONFLICT (recipient_id, group_key) DO NOTHING RETURNING id
   --          0 dòng (người khác vừa chèn, đã commit) → chạy lại (1) MỘT lần
   -- was_read = true → DELETE FROM notification.notification_actors WHERE notification_id = @id
   -- @actor không null:
   --   INSERT INTO notification.notification_actors (notification_id, actor_id) VALUES (@id, @actor) ON CONFLICT DO NOTHING
   --   chèn được 1 dòng VÀ nhánh (1) VÀ was_read = false → UPDATE … SET actor_count = actor_count + 1 WHERE id = @id
   ```

   `updated_at` gán **trong SQL** từ `TimeProvider` (A2: `NotificationDbContext` không tự đóng dấu). Id mới là `Uuid7.New()`.

   *Sửa 2026-09-25 khi thi công D9:* câu (1) tách hai — `SELECT id, is_read … FOR UPDATE` rồi `UPDATE … WHERE id = @id`. Xem
   "Thực tế thi công" D9.

3. **Unit/integration** (`Notification/NotificationStoreTests`), gọi store trực tiếp (handler là D10):

   | Id | Kịch bản | Kỳ vọng |
   |---|---|---|
   | `NOTIF-02-store` | `ActorId == RecipientId` | `Skipped`; 0 dòng |
   | `NOTIF-03` | 3 người khác nhau, cùng nhóm | 1 dòng, `actor_count = 3`, `last_actor_id` = người cuối |
   | `NOTIF-04` | Cùng một người 3 lần | `actor_count = 1` |
   | `NOTIF-05` | Đánh dấu đã đọc (SQL), rồi người mới | `is_read = false`, `actor_count = 1`, `notification_actors` chỉ còn người mới |
   | `NOTIF-05b` *(đề xuất)* | Đã đọc, rồi **người cũ** quay lại | đợt mới, `actor_count = 1` |
   | `NOTIF-09-store` | `ActorId = null`, `ReasonCode` có | `last_actor_id` null; 0 dòng `notification_actors`; `actor_count = 1` |
   | `NOTIF-C1` ⭐ | 20 người khác nhau song song vào nhóm **chưa có** | 1 dòng, `actor_count = 20`, 20 dòng `notification_actors`; không ngoại lệ — 20/20 |
   | `NOTIF-C2` *(đề xuất)* | Cùng một người 20 lượt song song | `actor_count = 1` |

### Cạm bẫy đã biết

1. **`RETURNING <is_read cũ>`**: không tồn tại (L-D6).
2. **Tăng `actor_count` bằng `+1` ở câu (1)**: đếm lượt, không đếm người, và `NOTIF-04` bắt. Chỉ tăng khi `notification_actors`
   **thật sự** chèn được một dòng.
3. **Không chạy lại (1) sau `DO NOTHING`**: sự kiện của người thứ hai trong lượt đua **mất lặng lẽ**, và `NOTIF-C1` ra 19.
4. **Đọc `SELECT … FOR UPDATE` rồi quyết định `INSERT` hay `UPDATE` bằng hai câu thường**: nhóm chưa có thì `FOR UPDATE` không khóa
   gì, hai lượt cùng `INSERT`, và một lượt `23505`.
5. **Không đặt `updated_at`** trong câu (1): thông báo có sự kiện mới không nhảy lên đầu danh sách.

---

## 14. D10 — Handler event

**Mục tiêu:** nối module phát event vào thông báo; mỗi handler chứng minh bằng API thật của module phát.

**Kết quả mong đợi:** `NOTIF-01`, `NOTIF-09` xanh qua API thật; `EVT-02` vẫn xanh; ba handler còn lại có "khi nào làm" rõ.

### Các bước

1. **Handler** ở `Notification/Application/Handlers/`, mỗi cái `IIntegrationEventHandler<TEvent>`, đăng ký trong
   `AddNotificationModule` bằng `services.AddIntegrationEventHandler<TEvent, THandler>()` (C0: **chỉ** đường này):

   | Handler | Người nhận | Actor | `type` / `GroupKey` | Đích | Khi nào |
   |---|---|---|---|---|---|
   | `FriendRequestSentHandler` | `AddresseeId` | `RequesterId` | `friend_request` / `GroupKey.FriendRequest(requesterId)` | `user` = requester | **Ngay** |
   | `FriendRequestAcceptedHandler` | `RequesterId` | `AccepterId` | `friend_accepted` / `GroupKey.FriendAccepted(accepterId)` | `user` = accepter | **Ngay** |
   | `ContentHiddenHandler` | `AuthorId` | **null** | `moderation` / `GroupKey.Moderation(type, id)` | `NotificationTargetTypes.From(TargetType)`, `PostId` | **Ngay** (sau D7c) |
   | `CommentCreatedHandler` | tác giả bài (`comment`), tác giả bình luận cha (`reply`), `MentionedUserIds` (`tag`, cắt được) | `ActorId` | ba nhóm từ một event (L-A9) | `post`/`comment` | **A merge GĐ3** |
   | `ReactionSetHandler` | `TargetAuthorId`; `IsNew = false` → bỏ | `ActorId` | `reaction` / `GroupKey.Reaction(kind, id)` | `NotificationTargetTypes.From(kind)` | **A merge GĐ3** |
   | `MessageSentHandler` | `RecipientId` **chỉ khi offline** (`IPresenceReader`); GĐ5 cắt presence → không tạo gì | `SenderId` | `message` / `GroupKey.Message(conversationId)` | `conversation` | **B merge GĐ5** |

   Handler **không** tự bắt mọi ngoại lệ. Bus (C0) đã bắt, log tên handler + tên event + số thứ tự phong bì, và chạy tiếp event
   sau (`EVT-02`). Handler bắt rồi nuốt là lỗi biến mất khỏi log.

2. **Test** (`Notification/NotificationHandlerTests`), đi từ **API thật** + `DrainEventsAsync` (harness C0), không `Publish` tay:

   | Id | Kịch bản | Kỳ vọng |
   |---|---|---|
   | `NOTIF-01` | A `POST` lời mời qua `socialgraph-v1` → drain → B chấp nhận → drain | B có `friend_request` (actor A); A có `friend_accepted` (actor B) |
   | `NOTIF-01b` *(đề xuất)* | Mời → hủy → mời lại | vẫn **một** dòng `friend_request`, `actor_count = 1` (cùng người) |
   | `NOTIF-09` | Báo bài của C → Moderator `hide` qua `PATCH /reports` → drain | C có `moderation`, `last_actor_id` null, `reason_code` đúng, 0 dòng `notification_actors`; id Moderator không xuất hiện ở cột nào |
   | `EVT-02` | (có từ C0) | vẫn xanh, không sửa khẳng định |

3. **Khi A hoặc B merge** (bước 9 của Mục 9.3): thêm handler theo bảng trên, mỗi cái **một** ca đi từ API thật của A/B, commit
   `feat(gd6-d): D10 — thông báo bình luận/cảm xúc từ event của GĐ3` riêng. `NOTIF-02` (tự thả cảm xúc bài mình) viết lúc đó.

### Cạm bẫy đã biết

1. **Handler đọc bảng của module khác** để tìm người nhận: event đã mang sẵn người nhận (Đ-6.17). Thiếu thì sửa event (báo A/B,
   chỉ-thêm), không đọc chéo schema.
2. **Test `Task.Delay` chờ handler**: dùng `DrainEventsAsync` (Đ-6.2).
3. **Hai handler cùng một scope**: bus mở scope riêng cho mỗi handler (C0). Đừng đăng ký handler bằng `AddScoped` tay.

---

## 15. D11 — Endpoint thông báo *(notification-v1 ra đời)*

**Mục tiêu:** API cho chuông và danh sách; tầng 3 ở đánh dấu đã đọc.

**Kết quả mong đợi:** checklist D0 cho `notification-v1`; `NOTIF-06..08`, `NOTIF-10`, `NOTIF-IDOR`, `TC-A01-notifications` xanh.

### Các bước

1. **Nền `notification-v1`** theo checklist Mục 2.
2. **`NotificationsController`** `[Route("api/v1/notifications")]` `[Authorize]`, không mã quyền nào (Mục 6.1, cùng lý do `/me`).
3. **Bốn action:**

   | Endpoint | Hiện thực |
   |---|---|
   | `GET /notifications?cursor=&limit=` | Keyset `(updated_at DESC, id DESC)` trên `idx_notifications_recent`; `limit` mặc định 20, tối đa 50. `actor` hydrate **một** lô `IUserDirectory` + ký avatar; `type = moderation` → `actor: null` |
   | `GET /notifications/unread-count` | `count(*) WHERE recipient_id = @me AND is_read = false` (index một phần) → `{ total }` |
   | `POST /notifications/{notificationId}/read` | `ExecuteUpdateAsync(SET is_read = true)` `WHERE id = @id AND recipient_id = @me`. 0 dòng → **403** `Error.Forbidden` (không tồn tại = không phải của bạn, quy ước 3b). Đã đọc rồi vẫn khớp 1 dòng → 204 (idempotent). **Không** chạm `updated_at` |
   | `POST /notifications/read-all` | Body `{ upTo }` bắt buộc; `UPDATE … SET is_read = true WHERE recipient_id = @me AND is_read = false AND updated_at <= @upTo` → 204 |

4. **Yaml** — bốn operation, `NotificationResponse`, `NotificationPage`, `ReadAllRequest`. `target.type` có `conversation` dù chưa
   ai tạo (A2 đã mở cột 20 ký tự).
5. **Test** (`Notification/NotificationEndpointTests`):

   | Id | Kịch bản | Kỳ vọng |
   |---|---|---|
   | `NOTIF-06` | 2 nhóm chưa đọc gồm 7 sự kiện | `unread-count = 2` |
   | `NOTIF-07` | `read-all { upTo }` rồi có sự kiện mới sau `upTo` | nhóm mới vẫn chưa đọc |
   | `NOTIF-08` | Trang có 1 nhóm và trang có 20 nhóm | số câu SQL bằng nhau (`SqlCommandCounter`, nếp `FEED-Q1`) |
   | `NOTIF-10` *(đề xuất)* | 3 nhóm; đánh dấu nhóm **cũ nhất** đã đọc | thứ tự danh sách **không đổi** |
   | `NOTIF-IDOR` | A đánh dấu thông báo của B · id không tồn tại | 403 · 403, **cùng thân lỗi** |
   | `TC-A01-notifications` | matrix | 401 |

### Cạm bẫy đã biết

1. **Đánh dấu đã đọc bằng tracking + `SaveChanges`** rồi ai đó thêm đóng dấu `updated_at` "cho đồng bộ": thông báo cũ nhảy lên đầu
   (bài học A2). `ExecuteUpdateAsync` chỉ đặt `is_read`; `NOTIF-10` bắt.
2. **404 cho id không tồn tại, 403 cho id của người khác**: status code lộ id nào có thật. Cùng 403.
3. **`read-all` không `upTo`**: nuốt thông báo tới sau lúc người dùng mở chuông (Mục 8.3).

---

## 16. D12 — `GET /search` *(mở profile-v1 chỉ-thêm)*

**Mục tiêu:** tìm người bằng tên gõ không dấu, tiền tố của từng từ, trúng index A4.

**Kết quả mong đợi:** `SRCH-01..08`, `TC-A01-search` xanh; `explain.sql` chạy lại trên 20.000 hồ sơ vẫn trúng GIN.

### Các bước

1. **`SearchController`** ở `Profile/Presentation/`, `[Route("api/v1/search")]`, `[Authorize]`, nhóm `profile-v1`.
2. **Validator** — `q` sau `Trim()` 2–50 ký tự → `errors.q` *"Nhập ít nhất 2 ký tự."* (ngắn) / *"Tối đa 50 ký tự."* (dài); `type`
   chỉ `user` (mặc định; khác → 400 `errors.type`); `limit` 1–20, mặc định 10.
3. **Hàm thuần** `SearchTerm.EscapeLike(q)`: escape `\` **trước**, rồi `%`, `_`. Dùng chung với D2 (cạm bẫy 3 Mục 4). Unit test.
4. **Truy vấn** — SQL thô trên `ProfileDbContext`. Chuẩn hóa `q` **ở DB bằng đúng hàm** (`unaccent`/`lower` không đụng `\ % _`
   nên escape ở C# trước là an toàn):

   ```sql
   WITH q AS (SELECT profile.search_norm(@escaped) AS p, profile.search_norm(@raw) AS raw)
   SELECT pr.user_id, pr.display_name, pr.avatar_key
   FROM profile.profiles pr, q
   WHERE profile.search_norm(pr.display_name) LIKE q.p || '%' ESCAPE '\'
      OR profile.search_norm(pr.display_name) LIKE '% ' || q.p || '%' ESCAPE '\'
   ORDER BY (profile.search_norm(pr.display_name) LIKE q.p || '%' ESCAPE '\') DESC,   -- tên BẮT ĐẦU bằng q trước
            public.similarity(profile.search_norm(pr.display_name), q.raw) DESC,
            pr.display_name, pr.user_id
   LIMIT @limit + 5;
   ```

   Rồi `IAccountStatusReader.GetInactiveAsync(ids)` (C5) lọc, cắt còn `limit`, ký avatar. Trả `{ items: [{ userId, displayName, avatarUrl? }] }`.
5. **`profile-v1.yaml`** chỉ-thêm: `GET /search`, `SearchResult`, `SearchPage`, 400 `errors.q`, 401. `info.version` → `1.1.0-gd6`.
6. **Test** (`Profile/SearchTests`):

   | Id | Kịch bản | Kỳ vọng |
   |---|---|---|
   | `SRCH-01..03` | "nguyen" · "van" · "duc" | "Nguyễn Văn An" · "Nguyễn **Văn** An" · "Đức" |
   | `SRCH-04` | `q` 1 ký tự · 51 ký tự · chỉ khoảng trắng | 400 `errors.q` |
   | `SRCH-05` | Người `disabled` tên khớp | không có trong kết quả; vẫn đủ `limit` nếu còn ứng viên |
   | `SRCH-06` | `q = "%%"` · `q = "a_"` | không trả mọi người; ký tự hiểu theo nghĩa đen |
   | `SRCH-07` | `EXPLAIN` **đúng câu SQL của store** với `SET LOCAL enable_seqscan = off` | kế hoạch có `idx_profiles_display_name_search`. Bảng vài chục dòng thì planner luôn chọn Seq Scan (L-A10), nên ca này chứng minh **biểu thức khớp index**; bằng chứng ở quy mô là `explain.sql` |
   | `SRCH-08` | "an" với "An Bình" và "Bảo An" | "An Bình" đứng trước |
   | `TC-A01-search` | matrix | 401 |

   Chạy lại `tests/load/search/seed-profiles.sql` + `explain.sql` trên DB vứt được (quy trình A4) **bằng câu SQL của D12** (sửa
   `explain.sql` cho khớp nếu câu D12 khác bản A4). Dán kết quả vào "Thực tế thi công". k6 và `bao-cao-tim-kiem.md` (Mục 10.6)
   **cắt được** (B.10 #2).

### Cạm bẫy đã biết

1. **Viết `lower(unaccent(display_name))`** ở truy vấn: không trúng index, Seq Scan (Đ-6.19). Chỉ `profile.search_norm(…)`.
2. **Chuẩn hóa `q` ở C#** (`RemoveDiacritics`): lệch từ điển `unaccent` ở chữ hiếm, "đ" xử lý khác. Cùng một hàm ở hai vế.
3. **Escape sau khi chuẩn hóa ở DB**: `search_norm('%')` vẫn là `%`, nhưng thứ tự ngược lại khó đọc. Escape C# trước, một chiều.
4. **Lọc tài khoản bị khóa trước khi `LIMIT`** bằng join sang `identity.users`: đọc chéo schema. Dùng `IAccountStatusReader` + `limit + 5`.
5. **Trả trạng thái quan hệ** trong kết quả: `IFriendshipReader` chỉ có bản đơn, tức N+1 ở ô gõ phím (Đ-6.19).

---

## 17. D13 — Rà RFC 7807 và `type`

**Mục tiêu:** mọi lỗi của GĐ6 khớp hợp đồng; FE phân nhánh được theo `type`; không lỗi nào lộ dữ liệu.

**Kết quả mong đợi:** bảng 17.1 khớp từng dòng; sáu cổng hợp đồng xanh; không commit **hoặc** một commit `fix(gd6-d): D13 — …`.

### 17.1 Danh mục `type` mới của GĐ6

| `type` | Status | Endpoint | Đầu việc | Hằng ở |
|---|---|---|---|---|
| `urn:socialapp:problem:account-disabled` | 403 | `POST /auth/login` | D1 | `IdentityErrors` |
| `urn:socialapp:problem:revocation-unavailable` | 503 | mọi endpoint `[PrivilegedEndpoint]` | C4 | `PrivilegedEndpointAttribute` |
| `urn:socialapp:problem:last-admin` | 409 | `lock`, `PUT …/role` | D3, D4 | `AdminErrors` |
| `urn:socialapp:problem:role-code-taken` | 409 | `POST /admin/roles` | D5 | `AdminErrors` |
| `urn:socialapp:problem:system-role` | 409 | `PUT …/permissions` (ADMIN), `DELETE` (hệ thống) | D5 | `AdminErrors` |
| `urn:socialapp:problem:confirmation-required` | 409 + `added`, `removed`, `affectedUsers` | `PUT …/permissions` | D5 | `AdminErrors` |
| `urn:socialapp:problem:role-in-use` | 409 | `DELETE /admin/roles/{id}` | D5 | `AdminErrors` |
| `urn:socialapp:problem:report-already-decided` | 409 | `PATCH /reports/{id}` | D7c | `ModerationErrors` |
| `urn:socialapp:problem:moderation-target-gone` | 409 | `PATCH /reports/{id}` | D7c | `ModerationErrors` |
| `urn:socialapp:problem:moderation-not-hidden` | 409 | `POST …/restore` | D7c | `ModerationErrors` |
| `urn:socialapp:problem:post-hidden` | 409 | `PATCH /posts/{id}` | D7a | `ContentErrors` |

400 theo trường, 404, 403 tầng 3 giữ hình dạng cũ (`https://httpstatuses.io/{status}`), vì FE không cần phân nhánh chúng.

### Các bước

1. `grep -rn "urn:socialapp:problem" src/backend --include=*.cs --include=*.yaml`: mỗi `type` xuất hiện ở **đúng một** hằng C# và
   trong yaml của mọi endpoint trả nó.
2. `grep` mọi `new Error(` thêm ở GĐ6: thông điệp không chứa `{`, id, email, tên kiểu; tiếng Việt có dấu.
3. Chạy sáu cổng `Category=Contract` + `ContractGateCoverageTests` + `ProblemDetailsTests`.
4. Đưa bảng 17.1 cho lane E (E1: `PROBLEM_TYPES` trong `lib/api/problem.ts`). Việc sửa FE thuộc E1, **không** thuộc D13.

---

## 18. Kế hoạch commit

Một mã việc một commit (commit-rules Mục 8). Mỗi commit: build sạch, bốn bộ test .NET xanh (trừ đỏ nền R2), FE bốn lệnh xanh khi
chạm hợp đồng, `detect-changes` chạy và ghi vào thân, **không** dòng ghi công.

| # | Tiêu đề (≤ 95 ký tự) | Đi kèm trong cùng commit |
|---|---|---|
| 1 | `feat(gd6-d): D1 — tài khoản bị khóa không đăng nhập, không refresh được; /me có quyền hiệu lực` | `identity-v1.yaml` 1.1.0-gd6; `schema.d.ts`; fixture FE (L-D3); `FakeUsers` |
| 2 | `feat(gd6-d): D2 — danh sách tài khoản cho quản trị, nhóm admin-v1 và cổng hợp đồng của nó` | D0 cho `admin-v1`; gỡ `Skip` C4; helper B1; matrix `TC-A05`, `TC-A05b` |
| 3 | `feat(gd6-d): D3 — khóa/mở tài khoản: thu hồi mọi phiên sau COMMIT, không khóa được Admin cuối` | `AdminInvariant`; metric; matrix `TC-A05-mod-lock`; `ADM-C2` |
| 4 | `feat(gd6-d): D4 — đổi vai trò có hiệu lực ở request kế tiếp, không đăng xuất người bị đổi` | `ADM-C1` |
| 5 | `feat(gd6-d): D5 — CRUD vai trò, xác nhận khi sửa USER/MODERATOR, xóa cache quyền mọi instance` | `Error.Extensions` (L-D11); matrix `TC-A05-roles` |
| 6 | `feat(gd6-d): D6 — gửi báo cáo: thấy được mới báo được, một báo cáo mở mỗi người, nhóm moderation-v1` | D0 cho `moderation-v1`; `Supports` (L-D13); policy `report-create`; `REP-IDOR`, `REP-C1` |
| 7 | `feat(gd6-d): D7a — bài bị ẩn: tác giả thấy kèm lý do, người khác 404, không sửa được` | `content-v1.yaml` 1.1.0-gd6; `schema.d.ts`; mục "Lỗi tìm ra khi rà" (L-D4) |
| 8 | `feat(gd6-d): D7b — hàng đợi kiểm duyệt gom theo đối tượng, chi tiết có ảnh chụp nội dung` | matrix `TC-A06-queue` |
| 9 | `feat(gd6-d): D7c — ẩn bài, đóng báo cáo, ghi audit trong một transaction; khôi phục` | metric; matrix `TC-A06`, `TC-A06b`; `MOD-C1`; `TX-01/02` qua API |
| 10 | `feat(gd6-d): D8 — nhật ký kiểm toán cho Admin, keyset id giảm dần, lọc theo người/đối tượng` | matrix `TC-A05-mod-audit` |
| 11 | `feat(gd6-d): D12 — tìm người theo tên không dấu, tiền tố từng từ, trúng index GIN` | `profile-v1.yaml` 1.1.0-gd6; `schema.d.ts`; kết quả `explain.sql` vào "Thực tế thi công" |
| 12 | `feat(gd6-d): D9 — thông báo gộp theo nhóm, đếm người khác nhau theo đợt, đúng dưới đồng thời` | `NOTIF-C1` |
| 13 | `feat(gd6-d): D10 — thông báo lời mời kết bạn và nội dung bị ẩn từ event` | — |
| 14 | `feat(gd6-d): D11 — endpoint thông báo, nhóm notification-v1, đánh dấu đã đọc có tầng 3` | D0 cho `notification-v1`; matrix `NOTIF-IDOR`, `TC-A01-notifications` |
| 15 | `fix(gd6-d): D13 — …` *(chỉ khi rà ra lệch)* | — |

Thân commit theo commit-rules Mục 5. Mục **"Lệch …"** bắt buộc với mọi commit chạm một chỗ lệch của Mục 0.6. Hiện là mọi commit
trừ 11 (D12) và 13 (D10). Ví dụ:

```
feat(gd6-d): D3 — khóa/mở tài khoản: thu hồi mọi phiên sau COMMIT, không khóa được Admin cuối

Đ-6.5–6.7: bên ghi revoked:user chạy thật lần đầu; bất biến ≥ 1 Admin dưới khóa tư vấn, đếm SAU khi ghi.
- AccountAdministrationStore: một transaction Identity — khóa tư vấn → UPDATE users → thu hồi mọi family → đếm Admin →
  audit(tx) → COMMIT; service gọi RevokeUserAsync SAU khi store trả về (thử lại 3 lần, hỏng → deferred + metric)
- AdminInvariant.EnsureRemainsAsync ở Identity.Infrastructure — GĐ8 gọi lại cho đường tự xóa
Lệch Đ-6.7 (chốt 2026-09-…): luôn lấy khóa tư vấn, không chỉ khi chạm tập Admin — quyết định dựa trên lần đọc chưa khóa
là lỗ đua (L-D8). Lệch Đ-6.15: audit user.lock không có metadata.revocation — audit ghi trong transaction, thu hồi chạy
sau COMMIT (L-D9).
Tự rà B.10 #1: RevokeAsync chỉ gọi ở AccountAdministrationService.LockAsync, sau store.LockAsync; UnlockAsync không thu hồi.
Đã đỏ trước khi có endpoint: ADM-01..04, ADM-C2.

Test: Unit … → …, Integration … → … (+ADM-01..04, ADM-06, ADM-C2 20/20), Architecture … → …. Thử cho đỏ 3 đột biến đều bị bắt.
detect-changes: …
```

**PR:** khối D đi chung **PR khối GĐ6** với phần còn lại (pull-request-rules Mục 2). **Không** mở PR khi chưa được bảo; **không**
tự merge. Mô tả PR mang danh sách tự rà B.10 (tám mục) và bảng đột biến Mục 20.

---

## 19. Ranh giới — cái gì **không** thuộc khối D

| Việc | Thuộc | Ghi chú |
|---|---|---|
| Hub `/hubs/notifications`, `NotificationPusher`, `NotificationHubContractTests` | **C6** | Chờ GĐ5. D9 thêm một lời gọi pusher sau `COMMIT` **khi** C6 làm, không dựng khung trước |
| Handler `comment`/`reply`/`reaction`/`tag`, báo cáo + ẩn **bình luận**, provider `Comment` | **Bước 9** (sau A merge) | Commit riêng, cùng scope `gd6-d`/`gd6-c` |
| Handler `message` | **Bước 9** (sau B merge) | GĐ5 cắt presence thì không làm (Đ-6.17) |
| Mọi màn FE, `PROBLEM_TYPES`, `hasPermission`, client `*-api.ts` | **E1–E10** | D chỉ sửa fixture bị đỏ vì hợp đồng (L-D3) |
| k6 tìm kiếm, `bao-cao-tim-kiem.md` | **Mục 10.6** — cắt được | `EXPLAIN` ở D12 là phần bắt buộc |
| Deploy staging, `\dx` trên staging, E2E | **F1, F2** | — |
| Áp bất biến ≥ 1 Admin cho tự xóa tài khoản; job xóa audit 12 tháng | **GĐ8** | D3 để sẵn `AdminInvariant` |
| Sửa `SharedKernel/Realtime/` | Không bao giờ | Mục 9.4 |

---

## 20. Checklist nghiệm thu khối D

**Mốc 1 — đổi quyền có hiệu lực ở request kế tiếp**

- [ ] `ADM-01`, `ADM-05`, `PERM-01` (API), `ME-01` xanh
- [ ] Mọi đường `RevokeUserAsync` sau `COMMIT`; tên hàm đã rà ghi trong thân commit D3, D4 (B.10 #1)
- [ ] `NotifyAsync` gọi sau `COMMIT` ở mọi đường sửa `role_permissions` và xóa vai trò

**Mốc 2 — không về 0 Admin; không ai ngoài người có quyền chạm màn quản trị/kiểm duyệt**

- [ ] `ADM-C1`, `ADM-C2` xanh **20/20**, đã đỏ khi bỏ khóa tư vấn
- [ ] Matrix `TC-A05*`, `TC-A06*` + hai dòng đối chứng xanh; đã đỏ khi bỏ `[RequirePermission]`
- [ ] `Privileged_controllers_carry_the_attribute` xanh **không** `Skip`; `POST /reports` là ngoại lệ duy nhất (B.10 #8)
- [ ] `grep -rn '"ADMIN"' src/backend` chỉ ra `SystemRoles.cs`; so CLAIM `role` của người gọi với ADMIN chỉ ở `PermissionChecks` (*sửa
  2026-09-24 khi thi công D5:* bản đầu ghi "so `SystemRoles.Admin` chỉ ở `PermissionChecks` + `EffectivePermissions`". Các chỗ so
  `RoleCodes.Admin` còn lại so vai trò của DỮ LIỆU, không phải quyền người gọi: `EffectivePermissions` (D1), `AdminInvariant` (D3),
  `AccountAdministrationService`/`Store` vế L-D18 (D4), `RoleStore` — ADMIN không sửa quyền, `editable` (D5))

**Mốc 3 — ẩn + đóng báo cáo + audit một transaction**

- [ ] `MOD-01..06`, `MOD-C1` (20/20), `TX-01`, `TX-02` qua API xanh; `TX-01` đã đỏ khi audit ghi `tx: null` VÀ khi audit ghi sau `COMMIT` (*sửa 2026-09-25:* `tx: null` từng tương đương tới khi
  fix(gd6-c) đổi nhánh null sang kết nối riêng — Thực tế thi công D7c)
- [ ] `AUD-01` (không `SECRET-` trong audit), `AUD-03` qua endpoint thật, `AUD-04` xanh
- [ ] `HID-01..06` xanh; hai lỗ BR-07 (L-D4) ghi trong thân commit D7a

**Thông báo, tìm kiếm**

- [ ] `NOTIF-01`, `-03..10`, `NOTIF-C1` (20/20), `NOTIF-IDOR` xanh; ba handler còn lại ghi "chờ GĐ3/GĐ5" (R6-01), không xóa dòng
- [ ] `SRCH-01..08` xanh; `explain.sql` 20.000 hồ sơ trúng GIN, kết quả dán vào "Thực tế thi công"

**Hợp đồng và quy trình**

- [ ] Ba yaml mới + ba yaml mở lại: cổng `API contract` xanh hai chiều; `info.version` `…-gd6`; `schema.d.ts` sinh lại cùng commit
- [ ] Bảng `type` Mục 17.1 khớp code + yaml; không thông điệp nào chứa id, email, nội dung
- [ ] Hai metric mới có trên `/metrics` từ lúc khởi động (`MetricsEndpointTests`)
- [ ] Không log `email`, `ip`, `detail`, `note`, `reason` (B.10 #5)
- [ ] Mỗi đầu việc một commit, có `Test:`, `detect-changes:`, không dòng ghi công
- [ ] `giai-doan-6.md` (B.6, Mục 6, 8, 10) sửa theo các L đã chốt **trong cùng commit**, có ngày

**Bảng đột biến — mỗi dòng phải bị đúng ca tương ứng bắt**, file khôi phục nguyên byte (`cmp`) sau mỗi lượt, build hợp lệ ở mọi lượt
(đọc dòng `Error(s)` trước khi đọc kết quả test):

| Đột biến | Ca đỏ |
|---|---|
| `LoginService` bỏ bước 4b | `ADM-01-login` |
| `RotateAsync` bỏ kiểm `status` | `ADM-01-refresh` |
| `EffectivePermissions` bỏ nhánh ADMIN | `ME-01` |
| Bỏ `[PrivilegedEndpoint]` khỏi `AdminUsersController` | `Privileged_controllers_carry_the_attribute` |
| Bỏ `[RequireAnyPermission]` khỏi `GET /admin/users` | `TC-A05` |
| Bỏ `AdminInvariant.AcquireAsync` | `ADM-C1` / `ADM-C2` (≥ 1/20) |
| Đếm Admin **trước** `UPDATE` | `ADM-C1` / `ADM-C2` |
| Khóa không thu hồi `refresh_tokens` | `ADM-01` (vế family) |
| Unlock không xóa `locked_until` | `ADM-02` |
| Bỏ nhánh `confirm` | `ROLE-04` |
| Cho USER về 0 quyền | `ROLE-04` |
| Bỏ `NotifyAsync` sau `PUT …/permissions` | `PERM-01` (API) |
| Bỏ `JsonUnmappedMemberHandling` | `ROLE-02` |
| Bỏ `CanViewAsync` ở `POST /reports` | `REP-IDOR`, `REP-02` |
| `ON CONFLICT` bỏ vế `WHERE` | `REP-01` (500 `42P10`) |
| Bỏ kiểm `post.hide` | `ROLE-01` (vế `hide`) |
| Bước 5 D7c chỉ đóng `id = @rid` | `MOD-01` |
| Bỏ `FOR UPDATE` ở D7c | `MOD-C1` |
| Audit D7c ghi `tx: null` · ghi SAU `COMMIT` (*sửa 2026-09-25:* `tx: null` tương đương lúc thi công D7c, có nghĩa lại sau fix(gd6-c) — xem Thực tế thi công D7c) | `TX-01` (API) |
| `GetAsync` bỏ nhánh `hidden` cho người khác | `HID-02`, `HID-03` |
| `UpdateAsync` bỏ 409 | `HID-04` |
| Upsert tăng `actor_count` mỗi lượt | `NOTIF-04` |
| Upsert không reset đợt | `NOTIF-05` |
| Upsert không chạy lại sau `DO NOTHING` | `NOTIF-C1b` (*sửa 2026-09-25, D9:* `NOTIF-C1` xanh với đột biến này — lượt đua không bảo đảm) |
| Upsert đọc `is_read` không `FOR UPDATE` *(thêm 2026-09-25, D9)* | `NOTIF-C3b` |
| `read` không kiểm `recipient_id` | `NOTIF-IDOR` |
| `read` đóng dấu `updated_at` | `NOTIF-10` |
| `read-all` bỏ `upTo` | `NOTIF-07` |
| Tìm kiếm bỏ escape | `SRCH-06` |
| Tìm kiếm bỏ lọc tài khoản không hoạt động | `SRCH-05` |
| Xếp hạng bỏ vế "bắt đầu bằng q" | `SRCH-08` |
| Truy vấn dùng `lower(unaccent(…))` | `SRCH-07` |
| Audit logs keyset `<=` thay `<` | `AUD-04` |

---

## 21. Khối D để lại gì

| Cho ai | Để lại |
|---|---|
| **E1–E10** | Ba nhóm API thật + `schema.d.ts`; `MeResponse.permissions`; bảng `type` Mục 17.1; fixture `msw/node` chép `example` từ ba yaml mới |
| **F2** | Endpoint cho `E2E-01..06`; tài khoản ADMIN thật trên staging đã có từ GĐ1 |
| **C6 (khi B merge)** | `INotificationStore` trả `Created`/`Updated`, nên pusher biết khi nào đẩy |
| **Bước 9 (A, B merge)** | Bảng handler Mục 14; `Supports(Comment)` tự đúng khi thêm provider |
| **GĐ7** | `socialapp_revocation_failures_total`, `socialapp_reports_decided_total{decision}` để vẽ và cảnh báo |
| **GĐ8** | `AdminInvariant.EnsureRemainsAsync`; đường khóa tài khoản (thu hồi mọi family) để xóa tài khoản dùng lại |

---

## Thực tế thi công

**2026-09-24 — chốt trước khi thi công:** mười lăm chỗ lệch Mục 0.6 đi theo đề xuất; L-D9 theo phương án (a). Chi tiết ở đó.

### D1 — 2026-09-24

Làm đúng Mục 3; L-D2, L-D3 áp như chốt. `giai-doan-6.md` sửa cùng lượt: Đ-6.5 (vị trí kiểm `status` ở refresh), Đ-6.11 (`RoleCode`),
Mục 8.5 (hàng `identity-v1`), B.6 D1, B.7 (L-D7 — D1 là commit D đầu tiên mang test) — mỗi chỗ ghi "sửa 2026-09-24".

**Lệch so với chính tài liệu này:**
- **L-D16 (mới):** nới `RoleCode` của `identity-v1.yaml` thành chuỗi có pattern — lý do ở bảng Mục 0.6. Test kiểu GĐ1
  `RoleCode là union chuỗi đúng hợp đồng` đổi thành `RoleCode là chuỗi mở từ GĐ6`; thêm ca `MeResponse có permissions bắt buộc`.
- **Lưới refresh đặt ở HAI chỗ**, không một như bản đầu của Mục 3 bước 3: nhánh ân hạn 3a cũng phát token (anh em cùng family).
  Có ca riêng `ADM_01_refresh_trong_an_han_…` và đột biến riêng (M3 dưới đây).
- **Quyền hiệu lực đọc bằng hai câu SQL** (user + vai trò, rồi mã quyền của vai trò theo `permission_id`), không projection lồng:
  câu thứ hai không chạy khi user không tồn tại, và thứ tự khớp `PermissionCodes.All` mà ADMIN nhận. `EffectivePermissions` so
  `RoleCodes.Admin` (trỏ về `SystemRoles.Admin`), không gõ chuỗi.
- **403 của login dùng `examples` (hai ví dụ có tên)** thay cho `example` — cổng hợp đồng không so ví dụ (`ContractTestsBase`), FE
  đọc bảng `type` trong mô tả.
- **Thêm ca ngoài bảng:** `Bi_Admin_khoa_va_chua_xac_minh_thi_bao_bi_khoa` (unit, thứ tự 4b trước 5), `…_sai_mat_khau_401_cung_than_voi_email_khong_ton_tai`
  (integration, AC-02 không thủng qua đường mới), `Tai_khoan_active_refresh_van_200` (đối chứng). Test GĐ1
  `Dang_nhap_roi_goi_me_200_dung_7_truong_…` đổi thành `…_8_truong_…` — tập trường của hợp đồng có thêm `permissions`, khẳng định
  giữ nguyên kiểu so cả tập.

**Lỗi tìm ra khi chạy test, đã sửa:** lượt đầu Integration đỏ 3 ca `PrivilegedEndpointAuditTests` trong 1 ms với `53300: sorry, too
many clients already` — không phải lỗi của lớp đó. `AccountDisabledTests` là lớp thứ mười dùng `IdentityApiFactory`, mỗi lớp một
database, một pool; pool giữ kết nối rỗi 5 phút sau khi host dừng, nên cả bộ vượt `max_connections` 100 của container ở lớp chạy
SAU. Sửa ở gốc: `IdentityApiFactory.DisposeAsync` gọi `ClearPool` (khuôn các lớp test schema của khối A, cạm bẫy 7). Lượt hai: xanh.
Impact `IdentityApiFactory`: UNKNOWN — text search: mười lớp `IClassFixture<IdentityApiFactory>` trong `Auth/`, không đổi hành vi
test nào ngoài việc trả kết nối sớm hơn.

**Test:** Unit 336 → 344 (+3 `LoginServiceTests`, +5 `EffectivePermissionsTests`), Integration 546 → 555 (+5 `AccountDisabledTests`,
+4 `ME-01`; đổi tên một ca GĐ1), Architecture 23 + 1 Skip → không đổi (Skip gỡ ở D2), Vitest 543 → 544 (+1 test kiểu). Còn đỏ nền
R2 trên máy dev. FE: `pnpm lint`, `typecheck`, `test`, `build` xanh.

**Thử cho đỏ — 4/4 đột biến bị bắt**, build hợp lệ ở mọi lượt (`0 Error(s)`), file khôi phục nguyên byte (`cmp`):

| Đột biến | Ca đỏ thực tế |
|---|---|
| M1 — `LoginService` bỏ bước 4b | `ADM_01_login_dung_mat_khau_tren_tai_khoan_bi_khoa_403_account_disabled_khong_phat_token` |
| M2 — `RotateAsync` bỏ kiểm trạng thái trước bước 5 | `ADM_01_refresh_tren_tai_khoan_bi_khoa_401_khong_xoay_khong_chen_token` |
| M3 — nhánh ân hạn 3a bỏ kiểm trạng thái | `ADM_01_refresh_trong_an_han_tren_tai_khoan_bi_khoa_401_khong_phat_anh_em` |
| M4 — `EffectivePermissions` bỏ nhánh ADMIN | `ME_01_permissions_la_quyen_hieu_luc_cua_vai_tro_doc_tu_DB(roleCode: "ADMIN", …)` |

**detect-changes:** high, 11 luồng. Chín luồng có chủ đích: Login (4 — bước 4b, `FindForLoginAsync` đọc thêm `status`), Refresh (3 —
lưới trạng thái trong `RotateAsync`), Get `/me` (1 — `permissions`), `LoginCandidate` (1). Hai luồng `Logout → LockFamilyAsync`
(`RevokeFamilyAsync`) và `AddWithVerificationAsync → StampUpdatedAt` **không** bị sửa thân hàm — cùng file với hàm đã sửa nên bị
gán theo dòng; `git diff -U0` chỉ có hunk ở `RotateAsync`, `FindForLoginAsync`, `FindMeAsync` và hàm mới `IsActiveAsync`. Impact trước
khi sửa: bảng Mục 1.2 (không HIGH/CRITICAL); `LoginCandidate` LOW (4 — store + fake `LoginServiceTests`).

### D2 — 2026-09-24

Làm đúng Mục 4 và checklist D0 cho `admin-v1` (Mục 2): `AdminApiGroup` (`"admin-v1"`, `"Quản trị"`), dòng `apiGroups` (không
`AddApplicationPart` — cùng assembly Identity), `admin-v1.yaml` `1.0.0-gd6` với **đúng** hai operation, `AdminContractTests` + dòng
`Content Include`, `pnpm gen:api` sinh `lib/api/admin/schema.d.ts` (glob tự thấy, không sửa script). `AdminUsersController` mang
`[PrivilegedEndpoint]` ở class, `[RequireAnyPermission(user.lock, user.unlock, role.assign)]` ở từng action. `IAdminUserQueries`
(Application) + `AdminUserQueries` (Infrastructure) + `AdminUserReadService` hydrate tên một lô qua `IUserDirectory`.
`LikePattern.Escape`/`StartsWith` ở `SharedKernel/Text/` (cạm bẫy 3 — D12 dùng lại). `Skip` của `Privileged_groups_are_not_empty`
đã gỡ. B1: `Harness/IdentitySql.cs` — `TaoTaiKhoanAsync`, `TaoAdminThuHaiAsync`, `DatVaiTroAsync`, `TaoVaiTroAsync`, `MaVaiTroMoi`.

**Lệch so với chính tài liệu này:**
- **L-D17 (mới):** matrix chạy với Redis thật — lý do ở bảng Mục 0.6. `giai-doan-6.md` Mục 6.3 sửa cùng lượt.
- **Hình dạng `AdminUser` so với Mục 8.2:** `status` dùng `UserStatus` bốn giá trị như `identity-v1` (quyết định 5 của GĐ1: giữ đủ
  bốn để GĐ8 không mở lại hợp đồng), không phải `active|disabled` — bộ lọc `status` thì đúng hai giá trị Admin ghi được.
  `lockedUntil` và `displayName` là trường **bắt buộc, nullable** (luôn có mặt, `null` khi không có), không phải `lockedUntil?`: app
  không bỏ trường null khi ghi JSON, và FE sinh `string | null` đúng với dây. `lockedUntil` chỉ có giá trị khi mốc còn ở tương lai.
- **Validation cụ thể hóa "sai → 400 theo trường":** `status` ngoài `active|disabled`, `roleCode` sai dạng `RoleCode` (so từng ký tự,
  không Regex — bẫy `$` của .NET), `q` dài hơn 254 ký tự. `roleCode` đúng dạng mà không tồn tại → trang rỗng 200 (vai trò là dữ liệu).
- **Thêm ca ngoài bảng:** `FC_01_admin_users_…` (fail-closed trên endpoint thật — bản probe của C4 giữ nguyên), `Moderator_403_va_co_dong_access_denied`,
  `ANY_01` chạy cả chi tiết (vai trò có quyền → 404 cho id lạ, không 403), đủ ba mã của policy + một mã ngoài policy.

**Test:** Unit 344 → 353 (+9 `LikePatternTests`), Integration 555 → 582 (+22 `AdminUsersTests`: `ADM-07`, `ADM-07b` ×3, `ADM-07c`,
`ANY-01` ×4…; +2 `AdminContractTests`; +2 matrix `TC-A05`, `TC-A05b`; +1 FC-01 thật), Architecture 23 + 1 Skip → 24, 0 Skip. Vitest
544 → 544. Còn đỏ nền R2 trên máy dev. FE: `pnpm lint`, `typecheck`, `test`, `build` xanh.

**Thử cho đỏ — 9/9 đột biến bị bắt**, build hợp lệ ở mọi lượt (`0 Error(s)`), file khôi phục nguyên byte (`cmp`):

| Đột biến | Ca đỏ thực tế |
|---|---|
| M0 — matrix không `UseRedis` (L-D17) | `TC-A05`, `TC-A05b` (503) |
| M1 — bỏ `[PrivilegedEndpoint]` khỏi `AdminUsersController` | `Privileged_controllers_carry_the_attribute`; `FC_01_admin_users_…` |
| M2 — bỏ `[RequireAnyPermission]` khỏi `GET /admin/users` | `TC-A05`; `ANY_01(report.resolve)`; `Moderator_403_…` |
| M3 — keyset `<` thành `<=` ở khóa phụ | `ADM_07_…` |
| M3b — bỏ `ThenByDescending(user_id)` | `ADM_07_…` |
| M4 — `StartsWith` không escape | `ADM_07b` ×3 (`x_`, `y%`, `w\`) |
| M5 — hydrate tên từng dòng (N+1) | `ADM_07c_…` |
| M6 — trả `lockedUntil` đã qua mốc | `Tung_truong_cua_AdminUser_…` |
| M7 — validator không giải mã cursor | `Tham_so_sai_dang_400_dung_truong` (hai ca `cursor`) |

"Đã đỏ trước khi có controller" (L-D7): `TC-A05`/`TC-A05b` gọi đường chưa có route → 401/404, và M0 cho thấy chúng đỏ 503 khi thiếu
Redis — hai lý do đỏ khác nhau, đều không phải 403/200.

**detect-changes:** low, 0 luồng (10 file đã theo dõi, 17 symbol: `IdentityErrors`, `AddIdentityModule`, `apiGroups`, khung matrix,
`PrivilegedEndpointTests`, tài liệu). Impact trước khi sửa: `AddIdentityModule`, `IdentityErrors`, `AuthZMatrixTests`,
`PrivilegedEndpointTests` — UNKNOWN, text search xác nhận (16 lời gọi `AddIdentityModule` không đổi chữ ký; `IdentityErrors` chỉ
thêm trường); `AuthZApiFactory` MEDIUM (6 nút trong assembly test; bốn lớp dùng làm fixture, `UseRedis` mặc định giữ cổng 1 nên ba
lớp không gọi nó không đổi hành vi).

### D3 — 2026-09-24

Làm đúng Mục 5; L-D7, L-D8, L-D9, L-D10 áp như chốt. `AdminInvariant` (`Infrastructure/Administration/`, namespace khóa `0x4144`
"AD") · `IAccountAdministrationStore` + `AccountAdministrationStore` (một transaction mỗi thao tác, `IAuditTrail` inject vào store) ·
`UserRevoker` (3 lần, chờ 0/250/500 ms qua `TimeProvider`) · `AccountAdministrationService` · `AdminErrors` (`SelfLock`, `LastAdmin`,
`type …:last-admin`) · `LockRequest` + validator · `BusinessMetrics.RevocationFailed` · hai action trên `AdminUsersController` ·
`admin-v1.yaml` `1.1.0-gd6`. `giai-doan-6.md` sửa cùng lượt: Đ-6.6 (`not-needed`), Đ-6.7 (khóa tư vấn luôn lấy), Đ-6.15 (metadata
tài khoản), Mục 7.3, 7.4, B.6 D3 — mỗi chỗ ghi "sửa 2026-09-24". Comment `AuditActions` (metadata tài khoản) sửa theo L-D9.

**Lệch so với chính tài liệu này:**
- **Route `{userId}` không ràng buộc `:guid`** (Mục 5 bước 6 ghi `{userId:guid}`): cùng nếp D2 — id sai dạng thành 400 `errors.userId`
  như hợp đồng, `:guid` thì thành 404 do không khớp route.
- **`UserNotFound` dùng lại `IdentityErrors.UserNotFound` của D2**, không tạo bản thứ hai trong `AdminErrors`. `SelfLock` là
  `Error.Validation("userId", …)` — cùng hình dạng `SelfFollow`.
- **Khóa chỉ đổi tài khoản `active`**: `disabled` (và `locked`/`deleted`, không ai ghi) → `NoChange`. **Mở khóa là thay đổi khi**
  `disabled` **hoặc** `active` mà còn khóa tạm FR-003 (`locked_until > now`) — thêm ca `Mo_khoa_tai_khoan_chi_bi_khoa_tam_FR003_…`.
  Mốc FR-003 đã qua coi như không khóa, khớp `lockedUntil` của D2.
- **`ADM-04` gọi bằng vai trò tự tạo chỉ có `user.lock`**, không bằng một Admin: "Admin kia đã `disabled`" thì Admin đó không gọi
  được API. Ca trả Admin của lớp về `active` trong `finally`.
- **`ADM-C2` chấp nhận bên thua 409 HOẶC 401**, không chỉ 409: request của bên thua tới tầng 1 sau khi bên thắng đã ghi
  `revoked:user` thì 401 — đúng thiết kế, vẫn giữ bất biến. Thứ bị cấm là hai 200. Ca đòi ≥ 10/20 lượt là 409 để chắc nó chạm
  nhánh bất biến. Chạy 6 lần × 20 lượt = 120 lượt xanh.
- **Đăng nhập thật qua `ModulesApiFactory`**, không `IdentityApiFactory`: factory đó chỉ migrate Identity, còn audit ghi vào
  `moderation.audit_logs`. `IdentitySql.TaoTaiKhoanAsync` thêm tham số `passwordHash` (hash BCrypt thật từ `IPasswordHasher`).
- **Seed không có tài khoản Admin nào** — database test không có Admin thì bất biến chặn MỌI lần khóa (409). Lớp test dựng Admin
  trước tiên; comment của `TaoAdminThuHaiAsync` ghi lại.
- **Một lỗ đua đã cân nhắc, để lưới D1 lo:** lượt refresh đang xoay (khóa family, chưa `COMMIT`) chèn token kế nhiệm sau khi câu
  `UPDATE refresh_tokens` của lệnh khóa chụp snapshot → token đó sống sót trong DB. Lần refresh kế tiếp của nó chạm lưới trạng thái
  của `RotateAsync` (Đ-6.5) → 401; access token phát kèm có `iat` trước mốc Redis → 401. Không lấy khóa family ở lệnh khóa (nhiều
  family, thứ tự khóa phức tạp) — ghi trong comment của store.

**Tự rà B.10 #1** (DB trước, Redis sau): `ITokenRevocationStore.RevokeUserAsync` chỉ gọi ở `UserRevoker.RevokeAsync` (mới) và
`SessionService.RevokeAccessTokensAsync` (GĐ1, sau khi store đã thu hồi family và `COMMIT`). `UserRevoker.RevokeAsync` chỉ gọi ở
`AccountAdministrationService.LockAsync`, **sau** `store.LockAsync` trả về (đã `COMMIT`); nhánh `NotFound`/`LastAdmin` return trước,
không chạm Redis; nhánh `Changed` luôn đi qua thu hồi; `UnlockAsync` không thu hồi. Unit `Khoa_thanh_cong_ghi_DB_truoc_Redis_sau…`
khẳng định thứ tự gọi. `grep '"ADMIN"' src/backend` vẫn chỉ ra `SystemRoles.cs` (+ comment).

**Test:** Unit 353 → 369 (+9 `AccountAdministrationServiceTests`, +7 `LockRequestValidatorTests`), Integration 582 → 598 (+14
`AccountLockTests`: `ADM-01`, `-01b`, `-02`, `-03`, `-04`, `-06`, mở khóa FR-003, 5 ca `reason`, 404/400, quyền riêng từng action; +1
`ADM-C2`; +1 matrix `TC-A05-mod-lock`; `MetricsEndpointTests` thêm một chuỗi vào ca có sẵn), Architecture 24 → 24. Vitest 544 → 544.
Còn đỏ nền R2 trên máy dev. FE: `pnpm lint`, `typecheck`, `test`, `build` xanh.

**Thử cho đỏ — 8/8 đột biến bị bắt, 1 đột biến tương đương**, build hợp lệ ở mọi lượt (`0 Error(s)`), file khôi phục nguyên byte (`cmp`):

| Đột biến | Ca đỏ thực tế |
|---|---|
| M1 — bỏ `AdminInvariant.AcquireAsync` ở `LockAsync` | `ADM_C2_…` |
| M2 — đếm Admin TRƯỚC `UPDATE` (thay vì sau) | `ADM_C2_…`; `ADM_04_…` |
| M3 — khóa không thu hồi `refresh_tokens` | `ADM_01_…` |
| M4 — mở khóa không xóa `locked_until`/bộ đếm | `ADM_02_…`; `Mo_khoa_tai_khoan_chi_bi_khoa_tam_FR003_…` |
| M5 — thu hồi Redis TRƯỚC store (đảo B.10 #1) | `ADM_04_…` (có key `revoked:user` sau 409) |
| M6 — bỏ `[RequirePermission(user.lock)]` | `TC-A05-mod-lock`; `Moi_action_mot_ma_quyen_rieng` |
| M7 — bỏ nhánh `NoChange` của khóa | `ADM_01b_…` |
| M8 — `UserRevoker` chỉ thử một lần | `ADM_06_…` (3 lần thử) |
| M9 — bỏ `_ = RevocationFailuresCounter` trong `Initialize()` | *không ca nào đỏ — tương đương:* counter không nhãn đã đăng ký khi khởi tạo static của lớp; dòng đó giữ cho đồng dạng với hai counter không nhãn có sẵn |

Lượt viết sai, không tính: M2 bản đầu chỉ THÊM một lần đếm trước mà giữ lần đếm sau — không phải "đếm trước thay vì sau", xanh là đúng.

**detect-changes:** low, 0 luồng (22 file, 37 symbol, đã `git add` để tính file mới). Impact trước khi sửa: `BusinessMetrics` MEDIUM
(16 nút, chỉ thêm counter), `AuditActions` MEDIUM (41 nút, chỉ sửa comment), `AdminUsersController`, `IdentitySql`,
`AddIdentityModule` UNKNOWN — text search: controller chỉ nối qua routing MVC; `IdentitySql` thêm tham số cuối có mặc định (ba lớp
gọi không đổi); `AddIdentityModule` 16 lời gọi, chữ ký không đổi.

### D4 — 2026-09-24

Làm đúng Mục 6 cộng **L-D18** (mới, chốt trước khi viết code — bảng Mục 0.6). `AccountAdministrationStore.AssignRoleAsync` cùng khung
D3, không đụng `refresh_tokens` · `AccountAdministrationService.AssignRoleAsync` (thêm `IPermissionCache`, `IAuditTrail` vào
constructor) · `AssignRoleRequest` + validator · `AdminErrors.UnknownRole` (400 `errors.roleCode`) · action `PUT {userId}/role` ·
`admin-v1.yaml` `1.2.0-gd6`. `giai-doan-6.md` sửa cùng lượt: Đ-6.9, Mục 6.1, Mục 8.2, B.6 D4. Comment `AuditActions` ghi hình dạng
`access.denied` của tầng 2 kép trong service.

**Lệch so với chính tài liệu này:**
- **L-D18 (mới):** chạm ADMIN cần thêm `role.manage`. `access.denied` của tầng 2 kép trong service ghi target là **tài khoản đích**
  (`user` + id) và `metadata.permission`, không phải `endpoint` + route như handler C4 — service không biết route, còn đối tượng thì
  biết. D7c (`post.hide`) dùng cùng hình dạng.
- **`LockTargetAsync` đổi** (D3): join `roles` để biết vai trò hiện tại, `FOR UPDATE OF u` chỉ khóa dòng tài khoản. Impact HIGH (ba
  luồng Lock/Unlock) — đã cảnh báo, chạy lại toàn bộ ca D3 xanh.
- **Route `{userId}` không `:guid`**, như D2/D3.
- **Validator không kiểm pattern `RoleCode`**: mã sai dạng thì cũng không tồn tại — một câu "Vai trò không tồn tại." cho cả hai.
- **`ADM-C1` và `ADM-C2` dùng chung một khung** (`HaiAdminDongThoiAsync`), cùng luật bên thua 409 hoặc 401 như D3.
- **B1 thêm `Admin/AdminTestClient`** (đăng nhập thật, refresh, đọc DB) cho D4 trở đi; `AccountLockTests` giữ bản riêng viết trước.
- **Thêm ca ngoài bảng:** `Tu_ha_vai_tro_khi_con_Admin_khac_200`, ba ca L-D18 (tự nâng lên ADMIN 403 + audit · hạ một ADMIN 403 ·
  đối chứng hai chiều), 5 ca `roleCode` sai (có `user` chữ thường — cạm bẫy 2), 404; matrix `TC-A05-mod-role` (ngoài bảng Mục 6.3,
  cùng lý do `TC-A05-mod-lock`).

**Tự rà B.10 #1:** `UserRevoker.RevokeAsync` giờ gọi ở hai chỗ — `LockAsync` (D3) và `AssignRoleAsync`, cả hai **sau** khi store trả
về (đã `COMMIT`); nhánh `NotFound`/`UnknownRole`/`LastAdmin`/`Forbidden` return trước, không chạm Redis. `AssignRoleAsync` không thu
hồi refresh family (cạm bẫy 1 — đột biến M3 bắt). Unit `Doi_vai_tro_ghi_DB_truoc_Redis_sau` khẳng định thứ tự.

**Test:** Unit 369 → 387 (+11 `AccountAdministrationServiceTests`, +7 `AssignRoleRequestValidatorTests`), Integration 598 → 614
(+14 `AssignRoleTests`, +1 `ADM-C1`, +1 matrix `TC-A05-mod-role`), Architecture 24 → 24. Vitest 544 → 544. `ADM-C1` + `ADM-C2` chạy
6 lần × 20 lượt, xanh. Còn đỏ nền R2 trên máy dev. FE: `pnpm lint`, `typecheck`, `test`, `build` xanh.

**Thử cho đỏ — 9/9 đột biến bị bắt**, build hợp lệ ở mọi lượt (`0 Error(s)`), file khôi phục nguyên byte (`cmp`):

| Đột biến | Ca đỏ thực tế |
|---|---|
| M1 — bỏ `AdminInvariant.AcquireAsync` ở `AssignRoleAsync` | `ADM_C1_…` |
| M2 — đếm Admin TRƯỚC `UPDATE role_id` (thay vì sau) | `ADM_C1_…`; `ADM_04_ha_Admin_hoat_dong_cuoi_cung_…` |
| M3 — đổi vai trò thu hồi refresh family (cạm bẫy 1) | `ADM_05_…` (refresh 401); `ROLE_01_…` |
| M4 — service bỏ vế "đích là ADMIN" của L-D18 | `L_D18_chi_co_role_assign_khong_tu_nang_len_ADMIN_…` |
| M5 — store bỏ vế "đang là ADMIN" của L-D18 | `L_D18_chi_co_role_assign_khong_ha_duoc_ADMIN_403` |
| M6 — tra vai trò không phân biệt hoa thường (cạm bẫy 2) | `Vai_tro_sai_400_errors_roleCode("user")` |
| M7 — bỏ nhánh `NoChange` | `ADM_05b_…` |
| M8 — bỏ `[RequirePermission(role.assign)]` | `TC-A05-mod-role` |
| M9 — không thu hồi sau `Changed` | `ADM_05_…` (access cũ vẫn 200) |

**detect-changes:** **critical, 18 luồng** — cả 18 là luồng Lock/Unlock của D3, có chủ đích. `git diff -U0`: thân `LockAsync`,
`UnlockAsync`, `ChangeAsync` của service không đổi — bị gán vì constructor thêm hai phụ thuộc và method mới chèn ngay cạnh; thay đổi
thật trên đường D3 là `LockTargetAsync` (join `roles`). Toàn bộ ca D3 (`AccountLockTests`, `ADM-C2`) chạy lại xanh. Không `partial`,
không `truncated`. Impact trước khi sửa: `AdminOutcome` HIGH, `LockTargetAsync` HIGH (ba luồng Lock/Unlock — đã cảnh báo người thi
công); `IAccountAdministrationStore`, `AccountAdministrationStore` LOW; `AccountAdministrationService` MEDIUM.

### D5 — 2026-09-24

Làm đúng Mục 7 cộng **L-D19** (bảng Mục 0.6). L-D11: `Error` thêm tham số cuối `Extensions`; `ResultHttpExtensions.Problem` dựng
qua `ProblemDetailsFactory` rồi chép extensions (nhánh không-extensions giữ nguyên `controller.Problem`). `RolePermissionDiff` (hàm
thuần) · `RoleModels` (`RoleSummary`, `PermissionInfo`, ba request + validator, `RoleRules`) · `IRoleStore` + `RoleStore` ·
`RoleAdministrationService` · `AdminRolesController` + `AdminPermissionsController` (`[RequirePermission(role.manage)]` ở CLASS —
cả năm action cùng một mã) · bốn `type` 409 mới trong `AdminErrors` · `admin-v1.yaml` `1.3.0-gd6`. `giai-doan-6.md` sửa: Đ-6.9
(L-D19), Mục 8.2 (`PermissionInfo.assignable`, lỗi 409), B.6 D5.

**Lệch so với chính tài liệu này:**
- **Cạm bẫy 1 giả định sai:** `UnmappedMemberHandling = Disallow` đã là cấu hình TOÀN CỤC của host từ GĐ1 (`Program.cs`), không phải
  lựa chọn "đặt sai chỗ". `PATCH` có `code` (hay trường lạ) ra 400 `errors.code` mà không cần attribute trên `RenameRoleRequest`.
  Bước 3 bảng dòng `PATCH` cũng vậy — không thêm attribute thừa.
- **L-D19 (mới):** `role.manage` không gán được qua API.
- **Phát invalidate cả khi TẠO vai trò** (hướng dẫn chỉ ghi sửa quyền + xóa): mã của vai trò đã xóa có thể còn trong cache dưới
  dạng tập rỗng — token cũ mang mã đó sống tới 15 phút. Tạo lại cùng mã thì 60 giây đầu người được gán không có quyền nào. Một lần
  PUBLISH, rẻ. Đổi tên không phát: cache giữ quyền, không giữ tên.
- **Audit vai trò `target_id = null`**: cột là `uuid`, `role_id` là `smallint` — mã nằm ở `metadata.code` như bảng Đ-6.15.
  `role.rename` ghi `from`/`to` (tên hiển thị, không phải nội dung người dùng).
- **Thứ tự kiểm của `PUT …/permissions`**: ADMIN → không đổi gì → về rỗng → chưa xác nhận. "Không đổi gì" đứng trước hai luật vai
  trò hệ thống: gửi lại đúng tập hiện có không cần `confirm`.
- **`[Trait("Category","AuthZ")]` cho cả `RoleManagementTests`** (Mục 10.4 #7 của `giai-doan-6.md` nói `ROLE-01` mang trait đó):
  lớp canh tầng 2 `role.manage` ở cả sáu operation.
- **Thêm ca ngoài bảng:** danh sách vai trò + `userCount` gồm tài khoản bị khóa, danh mục quyền (`assignable`), tạo trùng mã 409 /
  mã hệ thống 400 / `role.manage` 400, PUT không đổi 200 không audit, sáu operation × hai vai trò không `role.manage` → 403 (một vai
  trò tự tạo có ĐỦ 17 mã gán được vẫn 403), 404/400 `roleId`; `ProblemDetailsTests.Error_co_Extensions_…` qua probe
  `Harness/ProblemProbeController`; matrix `TC-A05-roles`.

**Tự rà Đ-6.10 (cạm bẫy 2):** `IPermissionChangeNotifier.NotifyAsync` gọi ở ba chỗ của `RoleAdministrationService` — `CreateAsync`,
`SetPermissionsAsync` (nhánh `Changed`), `DeleteAsync` — cả ba SAU khi store trả về (đã `COMMIT`); nhánh lỗi return trước. Mỗi
thao tác MỘT lời gọi (L-C4).

**Test:** Unit 387 → 412 (+25 `RoleRulesTests`: diff, hình dạng mã, ba validator, L-D19), Integration 614 → 629 (+13
`RoleManagementTests`, +1 `ProblemDetailsTests`, +1 matrix `TC-A05-roles`), Architecture 24 → 24. Vitest 544 → 544. Còn đỏ nền R2
trên máy dev. FE: `pnpm lint`, `typecheck`, `test`, `build` xanh.

**Thử cho đỏ — 10/10 đột biến bị bắt; 2 lượt kiểm lưới xanh đúng thiết kế**, build hợp lệ ở mọi lượt, file khôi phục nguyên byte:

| Đột biến | Ca đỏ thực tế |
|---|---|
| M1 — bỏ nhánh `confirm` | `ROLE_04_…` |
| M2 — cho USER về 0 quyền | `ROLE_04_…` |
| M3 — bỏ `NotifyAsync` sau `PUT …/permissions` | `PERM_01_…` (request kế tiếp vẫn 201) |
| M4 — bỏ chặn ADMIN ở `SetPermissionsAsync` | `ROLE_06_…` |
| M5b — bỏ kiểm vai trò hệ thống VÀ lưới `P0001` khi xóa | `ROLE_03_…` (500) |
| M6b — bỏ đếm người mang VÀ lưới FK `23503` khi xóa | `ROLE_03_…` (500) |
| M7 — cho gán `role.manage` (L-D19) | `Tao_vai_tro_…`, `Danh_muc_quyen_…`, `Sua_quyen_…` |
| M8 — `affectedUsers` chỉ đếm tài khoản `active` | `ROLE_04_…` |
| M9 — bỏ `[RequirePermission(role.manage)]` ở `AdminRolesController` | `TC-A05-roles`, `Moi_action_doi_role_manage_403` — và bốn dòng matrix GĐ4 đỏ LÂY: dòng `TC-A05-roles` (USER) thật sự ghi đè tập quyền USER trong database dùng chung của matrix |
| M10 — `ResultHttpExtensions` không chép `Extensions` | `Error_co_Extensions_…`, `ROLE_04_…` |

Kiểm lưới (xanh là ĐÚNG): M5 — chỉ bỏ kiểm vai trò hệ thống ở store → trigger A3 ném `P0001`, store dịch ra 409; M6 — chỉ bỏ đếm
người mang → FK RESTRICT ném `23503`, store dịch ra 409. Hai lưới dưới DB đỡ được khi lớp trên thủng, và không bao giờ thành 500.

Chưa thử: `NotifyAsync` TRƯỚC `COMMIT` (cạm bẫy 2) — một instance không phân biệt được với đúng thứ tự vì request kế tiếp chạy sau
`COMMIT`; bắt nó cần hai instance và một lần nạp chen giữa (PERM-02 của C3 dựng được khung, chưa dựng cảnh đua).

**detect-changes:** low, 0 luồng (19 file, 36 symbol, đã `git add`). Impact trước khi sửa: `Error` constructor LOW, record UNKNOWN — text search: mọi `*Errors.cs` gọi
theo vị trí tối đa 6 tham số, thêm tham số thứ 7 có mặc định không đổi lời gọi nào; `ResultHttpExtensions.Problem` LOW (3 nút);
`AdminErrors` UNKNOWN — chỉ thêm thành viên.

### D6 — 2026-09-24

Làm đúng Mục 8 và checklist D0 cho `moderation-v1` (Mục 2): `ModerationApiGroup` (`"moderation-v1"`, `"Kiểm duyệt"`),
`AddApplicationPart` + dòng `apiGroups`, `moderation-v1.yaml` `1.0.0-gd6` với **đúng** một operation, `ModerationContractTests` +
dòng `Content Include`, `pnpm gen:api` sinh `lib/api/moderation/schema.d.ts` (glob tự thấy). L-D13 áp như chốt:
`IModerationTargets.Supports` (chỉ-thêm, hiện thực duy nhất là composite — không fake nào phải sửa). `ReportSubmissionController`
(không `[PrivilegedEndpoint]`) · `CreateReportRequest` + validator · `ReportSubmissionService` · `IReportStore` + `ReportStore` (SQL thô
trên kết nối của `ModerationDbContext`, khuôn `SqlAuditTrail`) · `ModerationErrors` · policy `report-create` ·
`ReportTargetTypes.Parse` (chiều ngược của `From`). `giai-doan-6.md` sửa cùng lượt: Đ-6.12 (loại chưa hỗ trợ), Mục 8.1 (403, `detail`
chỉ khoảng trắng), Mục 10.1 (`REP-02` thêm vế bình luận, `REP-07` mới), B.6 D6 — mỗi chỗ ghi "sửa 2026-09-24".

**Lệch so với chính tài liệu này:**
- **`ReportsController` chưa tạo** (Mục 8 bước 3 ghi "hai controller"): D6 không có action nào cho nó, và một controller rỗng không
  chứng minh gì. Ra đời ở D7b cùng `GET /reports`, mang `[PrivilegedEndpoint]` ở class.
- **Hợp đồng có thêm 403** (Mục 8.1 bản đầu ghi 400 · 401 · 404 · 429): tầng 2 `report.create` chặn vai trò tự tạo không được gán mã
  đó. Có ca `Vai_tro_khong_co_report_create_403`.
- **`targetType`, `reasonCode` là `string`, không enum** trong DTO: `JsonStringEnumConverter` nhận cả `"Post"` lẫn số `0`; chuỗi thì
  validator so chính xác với đúng mảng mà CHECK của DB dựng từ đó, và báo lỗi tiếng Việt dưới đúng trường.
- **`detail` chỉ khoảng trắng coi như vắng mặt** (nới hơn "1–500 khi có"): `spam` lưu `null`, `other` ra cùng câu "Vui lòng mô tả lý
  do." như khi thiếu. Lưu bản đã trim.
- **Hai câu 400 cho "của chính mình"**, cùng key `targetId`: "Không thể báo cáo nội dung của chính mình." (bài/bình luận) và "Không
  thể báo cáo chính mình." (tài khoản).
- **Store có vòng hai lượt** `INSERT … DO NOTHING` → `SELECT`: khe hẹp khi báo cáo mở vừa bị đóng (D7) giữa hai câu thì chèn lại là
  đúng. Hết hai lượt vẫn rỗng → ném, không bịa biên nhận.
- **Dòng matrix `REP-IDOR` mang body mà `ArrangePath` điền id** (đối tượng `BaoCaoBody`, `JsonContent.Create` serialize lúc gửi) —
  không sửa `AuthZCase`/`AuthZMatrixTests`.
- **Thêm ca ngoài bảng:** `REP-04` thành Theory 10 biến thể (thiếu/sai từng trường, `"Post"`, `Guid.Empty`, `detail` chỉ khoảng
  trắng); `REP-04b` (trim khi lưu); `REP-02` thêm bài đã xóa, bài đã ẩn, người không có hồ sơ; `REP-05` thêm vế "người khác vẫn báo
  được" (theo người, không theo IP); báo tài khoản 201; 401 không token; unit thứ tự kiểm của service (6 ca).

**Test:** Unit 412 → 438 (+19 `CreateReportRequestValidatorTests`, +6 `ReportSubmissionServiceTests`, +1 `Supports`), Integration
629 → 653 (+21 `ReportSubmissionTests`, +2 `ModerationContractTests`, +1 matrix `REP-IDOR`), Architecture 24 → 24. Vitest 544 → 544.
`REP-C1` chạy 20 lượt liền, xanh 20/20. Còn đỏ nền R2 trên máy dev. FE: `pnpm lint`, `typecheck`, `test`, `build` xanh.

**Thử cho đỏ — 10/10 đột biến bị bắt; 1 lượt kiểm lưới xanh đúng thiết kế**, build hợp lệ ở mọi lượt (`0 Error(s)`), file khôi phục
nguyên byte (`md5sum -c`):

| Đột biến | Ca đỏ thực tế |
|---|---|
| M1 — bỏ `CanViewAsync` | `REP-IDOR` (matrix); `REP_02_…`; `REP_07_…` |
| M2 — `ON CONFLICT` bỏ vế `WHERE` (`42P10`) | `REP_01_…`, `REP_C1_…` và mọi ca có lượt chèn thành công (7 ca) |
| M3 — bỏ kiểm "của chính mình" | `REP_03_…` |
| M4 — bỏ `[EnableRateLimiting]` | `REP_05_…` |
| M5 — policy `report-create` một vùng chung cho mọi người | `REP_05_…` (vế người khác) |
| M6 — bỏ `[RequirePermission(report.create)]` | `Vai_tro_khong_co_report_create_403` |
| M7 — validator bỏ luật `other` cần `detail` | `REP_04_body_sai_400_dung_truong` (DB CHECK → 500) |
| M8 — luôn 200, bỏ nhánh 201 | `REP_01_…`, `REP_06_…`, `REP_C1_…` (7 ca) |
| M9 — không trim `detail` khi lưu | `REP_04b_…` |
| M11 — so tác giả TRƯỚC "thấy được" | `Khong_thay_duoc_thi_404_truoc_khi_so_tac_gia` (unit) |

Kiểm lưới (xanh là ĐÚNG): M10 — bỏ `Supports` ở service → composite trả ảnh chụp rỗng cho loại không có provider → vẫn 404 cùng thân,
không 500. `Supports` giữ vì nó dừng trước mọi I/O và vì `CanViewAsync`/`HideAsync` của composite không phải hợp đồng để dựa vào.

**detect-changes:** low, 0 luồng (28 file, đã `git add` để tính file mới). Impact trước khi sửa: `IModerationTargets` MEDIUM (32 nút, 14
trực tiếp — hiện thực duy nhất là composite, grep `: IModerationTargets` chỉ ra `ModerationTargets`, không fake nào); `ModerationTargets`,
`AddSharedKernel`, `AddModerationModule` UNKNOWN — text search: `AddSharedKernel` 2 lời gọi (Program.cs, `ResultTests`), chữ ký không
đổi, chỉ thêm policy; `AddModerationModule` 6 lời gọi, chữ ký không đổi, chỉ thêm hai đăng ký scoped mà container trần không resolve.

### D7a — 2026-09-25

Làm đúng Mục 9; L-D4, L-D5 áp như chốt. `PostModeration` + `PostResponse.Moderation` (cuối record) · `PostResponseMapper.ModerationOf`
(một điều kiện: `hidden` VÀ tác giả) · nhánh BR-07 trong `PostReadService.GetAsync` · 409 trong `PostService.UpdateAsync` ·
`ContentErrors.PostHidden` + `PostHiddenType` · `[ProducesResponseType(409)]` trên `PATCH` · `content-v1.yaml` `1.1.0-gd6` chỉ-thêm
(`PostModeration`, `PostResponse.moderation`, response `PostHidden`, schema `PostHiddenProblem`) · `pnpm gen:api` → chỉ
`lib/api/content/schema.d.ts` đổi, FE không sửa màn nào (biểu ngữ là E9). `giai-doan-6.md` sửa cùng lượt: Đ-6.14 (`type` thật +
thứ tự sau tầng 3), Mục 8.5 (hàng `content-v1`), Mục 10 (`HID-*`), B.6 D7 — mỗi chỗ ghi "sửa 2026-09-25".

**Lỗi tìm ra khi rà, đã sửa (L-D4):** trước D7a có hai lỗ BR-07 trên code — `GET /posts/{id}` của bài `hidden` trả **200** cho mọi
người qua BR-02 (`PostStore.FindAsync` chỉ lọc `deleted`), và `PATCH` sửa được bài `hidden`. Chưa lộ thật vì chưa endpoint nào ẩn
được bài; D7c mở đường đó.

**Lệch so với chính tài liệu này:**
- **`reasonCode` là enum năm giá trị** trong `content-v1` (chép tập `ReasonCode` của `moderation-v1`), không chuỗi trần: FE sinh
  union để map nhãn. Cổng hợp đồng không so schema response, nên tập này giữ khớp bằng tay khi thêm lý do (Đ-6.12 đã ghi "thêm lý
  do mới = migration đổi CHECK + chỉ-thêm enum" — giờ là hai yaml).
- **Lưới `hidden_reason` rỗng → `other`** (`PostResponseMapper.UnknownReasonCode`): DB không CHECK cặp (`status = hidden`,
  `hidden_reason` khác null), `HideAsync` ghi cả hai cùng câu nên API không sinh ca này, nhưng `reasonCode` là trường bắt buộc —
  trả `null` là nói dối hợp đồng. Gõ chuỗi `"other"` ở Content vì Content không tham chiếu Domain của Moderation.
- **`moderation` luôn có mặt, `null` với bài thường** (app không bỏ trường null khi ghi JSON — như D2): yaml `nullable: true`, không
  `required`; FE sinh `moderation?: PostModeration | null`.
- **Nhánh BR-07 của `GetAsync` đứng TRƯỚC BR-02**: bài ẩn của người khác trả 404 mà không tra bạn bè.
- **409 dùng `title` mặc định** "Xung đột dữ liệu" (`ProblemTitles`), không đặt riêng.
- **Thêm ca ngoài bảng:** `HID-03` là Theory ba vai trò (MODERATOR, ADMIN, USER lạ — bài `public` nên BR-02 cho qua, chỉ nhánh BR-07
  chặn được), mỗi vai trò đọc được bài **trước** khi ẩn; `HID-02` cũng đọc trước khi ẩn (200) để 404 sau đó chắc chắn đến từ BR-07;
  `HID-04` thêm vế người khác `PATCH` bài ẩn vẫn **403** (409 cho họ là lộ "bài này bị ẩn") và kiểm `privacy`/`edited_at` không
  đổi; `HID-05` thêm vế sau khi xóa tác giả đọc 404; unit `moderation_chi_co_khi_bai_an_va_nguoi_doc_la_tac_gia` (đặt `EditedAt` khác
  `UpdatedAt` — cạm bẫy 4) và `Bai_an_thieu_ly_do_thi_reasonCode_la_other`.
- **Đã xét, không cần làm:** cache trang đầu feed (Đ-4.8) có giữ bài vừa bị ẩn tới 30s không — không: cache chỉ lưu id, lượt trúng
  nạp lại bằng `FindManyPublishedAsync`, bài `hidden` vắng mặt ngay. D7c không phải xóa cache của ai.

"Đã đỏ trước khi có code" (L-D7): chạy `HiddenPostTests` trên code trước D7a → M1, M2 dưới đây là đúng hai lỗ L-D4.

**Test:** Unit 458 → 460 (+2 `PostResponseMapperTests`), Integration 736 → 744 (+8 `HiddenPostTests`: `HID-01`, `-02`,
`-03` ×3 vai trò, `-04`, `-05`, `-06`), Architecture 27 → 27. Vitest 591 → 591 (chỉ `schema.d.ts` đổi). Còn đỏ nền R2 trên
máy dev. FE: `pnpm lint`, `typecheck`, `test`, `build` xanh.

**Thử cho đỏ — 9/9 đột biến bị bắt**, build hợp lệ ở mọi lượt (`0 Error(s)`), file khôi phục nguyên byte (`md5`):

| Đột biến | Ca đỏ thực tế |
|---|---|
| M1 — `GetAsync` bỏ nhánh `hidden` cho người khác (lỗ L-D4 thứ nhất) | `HID_02_…`; `HID_03_…` |
| M2 — `UpdateAsync` bỏ 409 (lỗ L-D4 thứ hai) | `HID_04_…` |
| M3 — 409 đứng TRƯỚC tầng 3 | `HID_04_…` (người khác nhận 409 thay vì 403) |
| M4 — mapper bỏ điều kiện tác giả | `moderation_chi_co_khi_bai_an_va_nguoi_doc_la_tac_gia` (unit) |
| M5 — `hiddenAt` lấy `EditedAt` (cạm bẫy 4) | `HID_01_…`; `moderation_chi_co_khi_…` (unit) |
| M6 — `DeleteAsync` chặn bài ẩn | `HID_05_…` |
| M7 — 409 không có `type` riêng | `HID_04_…` |
| M8 — controller bỏ khai 409 của `PATCH` | `ContentContractTests.Contract_must_be_fully_implemented` |
| M9 — yaml bỏ 409 của `PATCH` | `ContentContractTests.Runtime_must_not_expose_anything_outside_the_contract` |

**detect-changes:** high, 8 luồng — cả tám là luồng `Get`/`Update` của `/posts/{postId}` (`GetAsync`: 4, `UpdateAsync` + action `Update`: 4),
có chủ đích; không luồng nào ngoài hai hàm D7a sửa. 13 file, 43 symbol (file test mới đã `git add -N` để được tính). Impact trước khi sửa: `PostReadService.GetAsync` LOW (1 nút — `PostsController.Get`),
`PostService.UpdateAsync` LOW (1 nút — `PostsController.Update`, 4 luồng), `PostResponseMapper` LOW (9 nút), record `PostResponse`
UNKNOWN — text search: `new PostResponse(` chỉ ở mapper; test đọc qua JSON; phía FE `moderation` là trường không required nên
fixture `satisfies` không đỏ (`pnpm typecheck` xanh).

### D7b — 2026-09-25

Làm đúng Mục 10; L-D15 áp như chốt. `ReportsController` (`[PrivilegedEndpoint]` ở class, `[RequirePermission(report.resolve)]` từng
action) · `ReportReadService` · `IReportQueries` + `ReportQueries` (SQL thô, một câu mỗi đường) · `ListReportsQuery` + validator ·
`ReportQueueCursor` · DTO `ReportQueueItem`/`ReportQueuePage`/`ReportDetail`/`ReportTargetSnapshot`/`OpenReport`/`ReportHistoryEntry` ·
`ModerationErrors.ReportNotFound` · `moderation-v1.yaml` `1.1.0-gd6` (hai operation, `UserCard`, `TargetSnapshot`, `ReportDetail`
với `history[].outcome`, 403 + 404 + 503) · `pnpm gen:api` → chỉ `lib/api/moderation/schema.d.ts` đổi. Matrix `TC-A06-queue`.
`giai-doan-6.md` sửa cùng lượt: Mục 8.1 (L-D15, hình dạng `TargetSnapshot`), B.6 D7 — ghi "sửa 2026-09-25".

**Lệch so với chính tài liệu này:**
- **Đường đọc là interface riêng `IReportQueries`**, không thêm vào `IReportStore` như comment D6 định: khuôn `IAdminUserQueries` /
  `IAccountAdministrationStore` của D2 — hai đường không chung phụ thuộc, và fake `IReportStore` của unit test D6 không phải hiện
  thực hàm nó không dùng. Comment của `IReportStore` sửa theo.
- **Chi tiết là MỘT câu SQL** (tự join `reports` với báo cáo neo) trả cả báo cáo mở lẫn đã đóng; service tách mở/lịch sử và gom lịch
  sử theo `(status, resolver_id, resolved_at, resolution_note)`, cũ nhất trước. Mục 10 bước 2 để ngỏ thứ tự lịch sử — chọn cũ nhất
  trước, cùng chiều `openReports`.
- **`TargetSnapshot` mọi trường luôn có mặt, `null` khi không áp dụng** (như `AdminUser` của D2) — `body`, `postId`, `createdAt`,
  `editedAt`, `author` nullable; `createdAt` chỉ null khi đối tượng không còn trong bảng nào. `UserCard` thêm vào `moderation-v1`
  (`userId`, `displayName`, `avatarUrl | null`).
- **`status` hàng đợi so chính xác chữ thường** (`OPEN` → 400), cùng luật chuỗi của CHECK.
- **`reasons` dựng từ `ReasonCodes.All`** (chính mảng dựng `ck_reports_reason`): thêm lý do là thêm cột đếm, không sửa câu.
- **Thêm ca ngoài bảng:** `QUE-01b` (báo cáo đã đóng rời hàng đợi, đối tượng còn báo cáo mở vẫn một dòng), `QUE-06` (mở chi tiết
  bằng báo cáo đã đóng: thấy báo cáo mở mới + một dòng lịch sử), `QUE-07` (đối tượng biến mất → `deleted`, nội dung null), chi tiết
  báo cáo tài khoản (ảnh chụp là hồ sơ), 404, sáu biến thể 400, MODERATOR + ADMIN 200; `QUE-03` thêm bài bị ẩn; `QUE-04` soi cả
  chuỗi id người báo trong thân (không chỉ tên trường); `AUD-03` năm lần bị chặn → đúng MỘT dòng, trên cả hai route; `QUE-02` ở
  lớp riêng (`ReportQueuePagingTests`, database riêng) để khẳng định đúng 20/20/5, với nhóm ba đối tượng cùng mốc để khóa phụ
  `target_id` thật sự được dùng. Unit: cursor, validator, gom lịch sử, đối tượng biến mất, ký URL, cursor neo dòng cuối.

"Đã đỏ trước" (L-D7): dòng `TC-A06-queue` và ca `AUD-03` đỏ khi bỏ tầng 2 (M1) hoặc `[PrivilegedEndpoint]` (M2) — xem bảng dưới.

**Test:** Unit 460 → 477 (+17 `ReportReadServiceTests`), Integration 744 → 765 (+19 `ReportQueueTests`, +1 `ReportQueuePagingTests`,
+1 matrix `TC-A06-queue`), Architecture 27 → 27. Vitest 591 → 591 (chỉ `schema.d.ts` đổi). Còn đỏ nền R2 trên máy dev. FE:
`pnpm lint`, `typecheck`, `test`, `build` xanh.

**Thử cho đỏ — 12/12 đột biến bị bắt**, build hợp lệ ở mọi lượt (`0 Error(s)`), file khôi phục nguyên byte (`md5`):

| Đột biến | Ca đỏ thực tế |
|---|---|
| M1 — bỏ `[RequirePermission]` của `GET /reports` | matrix `TC-A06-queue`; `AUD_03_…(/api/v1/reports)` |
| M2 — bỏ `[PrivilegedEndpoint]` khỏi `ReportsController` | `Privileged_controllers_carry_the_attribute`; `AUD_03_…` cả hai route |
| M3 — đại diện là báo cáo MỚI nhất | `QUE_01_…` |
| M4 — keyset bỏ khóa phụ `target_id` | `QUE_02_…` |
| M5 — hàng đợi không lọc `status = open` | `QUE_01b_…` |
| M6 — `reasons` giữ lý do đếm 0 | `QUE_01_…` |
| M7 — chi tiết chỉ đọc báo cáo được mở | `QUE_06_…` |
| M8 — lịch sử không gom theo lần quyết | `QUE_06_…`; `Lich_su_gom_theo_lan_quyet_…` (unit) |
| M9 — cursor neo vào dòng thừa `limit + 1` | `QUE_02_…`; `Cursor_trang_sau_neo_vao_dong_cuoi_trang_nay` (unit) |
| M10 — hydrate tác giả mỗi ảnh một lần (N+1) | `QUE_05_…` |
| M11 — validator bỏ luật `status` | `Tham_so_sai_400_…` ×2; `Validator_status_cursor_limit` ×2 (unit) |
| M12 — controller bỏ khai 503 của chi tiết | `ModerationContractTests.Contract_must_be_fully_implemented` |

Chưa thử: `array_agg` BỎ HẲN `ORDER BY` (cạm bẫy 4) — Postgres thường trả theo thứ tự chèn nên ca có thể xanh ngẫu nhiên; M3 (đảo
chiều) là bản bắt được một cách tất định.

**detect-changes:** low, 0 luồng (19 file, 24 symbol; file mới đã `git add -N`). Lưu ý: index báo có luồng bị cắt ở bước dựng process
(`truncation`) — "0 luồng" là số tối thiểu. Impact trước khi sửa: `ModerationErrors` LOW (3 nút — chỉ thêm thành viên), `IReportStore` LOW
(9 nút — chỉ sửa comment), `AddModerationModule` UNKNOWN — text search: 7 lời gọi, chữ ký không đổi, chỉ thêm hai đăng ký scoped.

### D7c — 2026-09-25

Làm đúng Mục 11; L-D12 áp như chốt. `DecisionRules` (bảng chín ô + ánh xạ trạng thái/`action`) · `DecideReportRequest` + validator ·
`ReportDecisionResult` · `DecideReportService` · `IModerationDecisionStore` + `ModerationDecisionStore` (transaction: khóa báo cáo →
ẩn → đóng mọi báo cáo mở → audit → `COMMIT`; khôi phục) · `RestoreTargetService` + `RestoreTargetRequest` · action `PATCH` trên
`ReportsController` · `ModerationTargetsController` (`[PrivilegedEndpoint]`, `[RequirePermission(post.hide)]`) · ba 409 có `type` +
`InvalidDecision`, `RestoreTypeInvalid`, `ModerationTargetNotFound` trong `ModerationErrors` · `BusinessMetrics.ReportDecided` +
ba nhãn trong `Initialize()` · `moderation-v1.yaml` `1.2.0-gd6` · `pnpm gen:api` → chỉ `lib/api/moderation/schema.d.ts` đổi · matrix
`TC-A06`, `TC-A06b`. `giai-doan-6.md` sửa cùng lượt: Mục 8.1 (version, hình dạng khôi phục, target của `access.denied`), B.6 D7.

**Lệch so với chính tài liệu này:**
- **Store ghi là interface mới `IModerationDecisionStore`**, không thêm vào `IReportStore` (comment D6/D7b định vậy): phụ thuộc khác
  hẳn (`IModerationTargets`, `IAuditTrail` inject vào store — khuôn `AccountAdministrationStore` của D3) và fake D6 không phải sửa.
  Comment `IReportStore` sửa theo.
- **`access.denied` của tầng 2 kép ghi target `report` + id báo cáo**, `metadata.permission = post.hide` — khuôn L-D18 (D4): service
  biết đối tượng, không biết route. Bước 0 của Mục 11 ghi `{ method, routeTemplate }` — bỏ, như D4.
- **`hide` mà ảnh chụp không có** (đối tượng biến mất khỏi mọi bảng, hoặc loại chưa có provider) → 409 `moderation-target-gone` TRƯỚC
  transaction, thay vì để `HideAsync` ném `NotSupportedException` (500). Đã xóa mềm thì vẫn có ảnh chụp → `HideAsync` trả `NotFound`
  trong transaction → cùng 409.
- **`targetStatus` của `dismiss`/`resolve` lấy từ ảnh chụp đọc cho MỌI quyết định** (bước 2 chỉ ghi ảnh chụp cho `hide`); ảnh chụp vắng
  → `deleted`.
- **Khôi phục trả 200 `ModerationTargetChange`**, body tùy chọn (`EmptyBodyBehavior.Allow`), route không `:guid` (Mục 11 bước 3 ghi
  `{targetId:guid}`) — cùng nếp D2/D3: id sai dạng 400 `errors.targetId`.
- **`note` bắt buộc khi `resolve` kiểm ở validator** (Mục 11 bước 1 đặt ở service): không cần báo cáo để biết, và 400 tới trước mọi I/O.
  Sai cặp `decision × targetType` vẫn ở service (cần báo cáo).
- **`MOD-07` đếm `ContentHidden` bằng handler ghi lại** (khuôn EVT-06), không bằng metric `published{event}`: metric là số của cả
  process — lớp khác chạy cùng làm lệch.
- **`TX-01`/`TX-02` dùng decorator GHI THẬT rồi mới ném**, không fake ném ngay: fake ném ngay xanh cả khi audit ghi sau `COMMIT` hay provider
  ghi trên kết nối khác — đúng hai đột biến mà hai ca này tồn tại để bắt. Có ca đối chứng không bật lỗi.
- **Thêm ca ngoài bảng:** `MOD-05` sáu biến thể 400 + đối chứng `resolve` có ghi chú 200; `MOD-06` thêm vế người khác đọc lại được bài,
  báo cáo đã đóng giữ nguyên, không event, gọi không body; khôi phục 400/404 năm biến thể; `ROLE-01` thêm `hide` trên id báo cáo lạ vẫn
  403 (không 404); `AUD-01` chạy cả `hide` lẫn khôi phục; unit: bảng chín ô, validator, thứ tự tầng 2 kép, event đúng vai và lý do
  của request, không phát lại khi `AlreadyHidden`, không phát khi 409.

**Tự rà luật 5 (Publish sau COMMIT):** `IEventPublisher.Publish` chỉ gọi ở `DecideReportService.DecideAsync`, SAU khi
`store.DecideAsync` trả về (store đã `CommitAsync`), ngoài mọi khối `await using var tx`; nhánh `AlreadyDecided`/`TargetGone` return
trước. Khôi phục không phát event.

**Test:** Unit 477 → 501 (+24 `DecideReportServiceTests`: bảng chín ô, validator, thứ tự tầng 2 kép, event), Integration 765 → 785
(+15 `DecideReportTests`, +3 `ModerationTransactionTests`, +2 matrix `TC-A06`/`TC-A06b`; `MetricsEndpointTests` thêm ba chuỗi vào ca
có sẵn), Architecture 27 → 27. Vitest 591 → 591 (chỉ `schema.d.ts` đổi). `MOD-C1` (20 lượt mỗi lần chạy) chạy thêm 5 lần liền:
5/5 xanh. Còn đỏ nền R2 trên máy dev. FE: `pnpm lint`, `typecheck`, `test`, `build` xanh.

**Thử cho đỏ — 13/13 đột biến có nghĩa bị bắt; 1 đột biến tương đương**, build hợp lệ ở mọi lượt (`0 Error(s)`), file khôi phục
nguyên byte (`md5`):

| Đột biến | Ca đỏ thực tế |
|---|---|
| M1 — bỏ `FOR UPDATE` | `MOD_C1_…` |
| M2 — bước đóng chỉ đóng báo cáo được mở | `MOD_01_…` |
| M3b — `COMMIT` trước khi ghi audit | `TX_01_…` (+ 11 ca khác: audit trên transaction đã đóng ném → 500 sau khi đã ghi) |
| M4 — bỏ kiểm `status = open` sau khóa | `MOD_04_…`; `MOD_C1_…` |
| M5 — bỏ tầng 2 kép `post.hide` | `ROLE_01_hide_…`; `Hide_thieu_post_hide_403_…` (unit) |
| M6 — phát `ContentHidden` cả khi `AlreadyHidden` | `MOD_07_…`; `Bai_da_an_tu_truoc_khong_phat_lai` (unit) |
| M7 — lý do luôn lấy của báo cáo (cạm bẫy 5) | `Hide_vua_an_phat_ContentHidden_dung_vai_ly_do_cua_request` (unit) |
| M8 — bảng cho `resolve` bài | `MOD_05_…`; `Bang_decision_x_targetType_chin_o` ×2 (unit) |
| M9 — khôi phục không ghi audit | `MOD_06_…`; `AUD_01_…` |
| M10 — bỏ `[RequirePermission]` của `PATCH` | matrix `TC-A06`; `MOD_03_…` |
| M11 — validator bỏ luật `note` cho `resolve` | `MOD_05_…`; `Validator_…` ×2 (unit) |
| M12 — `Initialize()` bỏ ba nhãn quyết định | `MetricsEndpointTests.Chi_so_nghiep_vu_co_mat_tu_luc_khoi_dong` |
| M13 — yaml bỏ 409 của `PATCH` | `ModerationContractTests.Runtime_must_not_expose_anything_outside_the_contract` |

**Tương đương, không tính:** M3 — audit quyết định ghi `tx: null` (đột biến bắt buộc của B5 ở Mục 20) → **không ca nào đỏ**, và đó là
hành vi đúng của code hiện tại, không phải test yếu: nhánh `tx: null` của `SqlAuditTrail` ghi trên kết nối của `ModerationDbContext`
**scoped** — chính kết nối đang giữ transaction của store — nên Postgres cho câu `INSERT` vào luôn transaction đó (Npgsql không bắt
gán `cmd.Transaction`). Thay bằng M3b (audit SAU `COMMIT`) để chứng minh `TX-01` canh đúng "audit cùng số phận". Dòng Mục 20 sửa theo.

*Cập nhật 2026-09-25 — ĐÃ SỬA ở commit `fix(gd6-c)` riêng (người thi công chọn đổi hiện thực):* nhánh `tx: null` nay mở kết nối RIÊNG
từ cùng pool; ca `AuditTrailTests.Khong_tx_song_sot_khi_transaction_cua_scope_rollback` đỏ với bản cũ. M3 hết tương đương: chạy lại,
`TX_01_audit_hong_giua_chung_khong_gi_thay_doi` đỏ. Đoạn dưới giữ nguyên làm dấu vết.

**Điểm tìm ra khi rà, CHƯA sửa (ngoài phạm vi D7c — code của C1):** `IAuditTrail` hứa "`tx` null → tự ghi trên kết nối riêng", nhưng
`SqlAuditTrail` dùng kết nối của context scoped. Ai gọi `AppendAsync(null, access.denied)` trong lúc CÙNG scope đang mở transaction
trên `ModerationDbContext` rồi rollback thì mất dòng từ chối. Hôm nay không đường nào làm vậy: D4 và D7c ghi `access.denied` trước khi
mở transaction hoặc sau khi store đã trả về (đã rollback/commit); handler C4 chạy ở middleware, trước action. Cần quyết định: sửa comment
cho đúng thực tế, hay đổi hiện thực sang kết nối riêng từ cùng pool (L-C1 cấm pool thứ hai, không cấm kết nối thứ hai).

**detect-changes:** low, 0 luồng (22 file, 31 symbol; file mới đã `git add -N`). Index báo luồng bị cắt ở bước dựng process — "0 luồng" là số
tối thiểu. Impact trước khi sửa: `BusinessMetrics` MEDIUM (18 nút — chỉ thêm counter + nhãn), `Initialize` LOW,
`ModerationErrors` MEDIUM (5 nút — chỉ thêm thành viên), `ReportsController`, `AddModerationModule` UNKNOWN — text search: controller
chỉ nối qua routing MVC, thêm một action và một tham số constructor (DI resolve); `AddModerationModule` 7 lời gọi, chữ ký không đổi.

### D8 — 2026-09-25

Làm đúng Mục 12; L-D14 áp như chốt. `AuditLogsController` (Moderation, `[Route("api/v1/admin/audit-logs")]`, `[PrivilegedEndpoint]`,
`[RequirePermission(audit.read)]`, nhóm `moderation-v1`) · `ListAuditLogsQuery` + validator · `AuditLogCursor` · `IAuditLogQueries` +
`AuditLogQueries` (LINQ `AsNoTracking`, keyset `id < @before ORDER BY id DESC`) · `AuditLogReadService` (hydrate người thao tác một lô,
ký avatar) · `moderation-v1.yaml` `1.3.0-gd6` (operation + `AuditAction`, `AuditLogItem`, `AuditLogPage`) · `pnpm gen:api` → chỉ
`lib/api/moderation/schema.d.ts` đổi · matrix `TC-A05-mod-audit`. `giai-doan-6.md` sửa: B.6 D8 (L-D14), Mục 8.1 (`AuditLogItem`).

**Lệch so với chính tài liệu này:**
- **`AuditLogItem` mọi trường luôn có mặt, `null` khi không có** (như `TargetSnapshot` của D7b); `actor` dùng lại `UserCard` của D7b;
  `action` là enum `AuditAction` (12 mã) trong hợp đồng.
- **`targetType` một mình lọc được** (index bắt đầu bằng `target_type`); chỉ `targetId` thiếu `targetType` mới 400. `targetType` không
  bị giới hạn tập giá trị (các module ghi `post`, `user`, `role`, `report`, `endpoint`…) — chỉ ≤ 20 ký tự như cột.
- **Cursor là `id` mã hóa base64url**, một khóa (identity không hòa).
- **Dòng dựng bằng `INSERT` thẳng** (bảng cho INSERT): 120 lần thao tác thật qua API chậm mà không chứng minh thêm gì cho đường đọc.
  Mỗi ca dựng người/đối tượng MỚI; ca không lọc khẳng định "chứa đủ", ca lọc theo id khẳng định "đúng bằng". Nhịp dựng: người
  `i % 3`, hành động `i % 4`, đối tượng `i / 3 % 3` — lượt đầu dùng cùng `i % 3` cho người lẫn đối tượng nên mọi dòng về bài đều của A và
  ca hydrate không tìm được dòng của B (lỗi dữ liệu test, không phải code — sửa trước khi đo).
- **Thêm ca ngoài bảng:** hình dạng dòng + số câu SQL trang 3 dòng = trang 60 dòng (hydrate một lô); `AUD-04b` bảy biến thể; MODERATOR
  và USER 403 **và** lần từ chối đó đọc lại được qua chính endpoint (`access.denied`, route template); unit cursor + validator.

**Test:** Unit 528 → 548 (+20 `AuditLogQueryValidatorTests`), Integration 829 → 843 (+13 `AuditLogQueryTests`: `AUD-04` ×3,
`AUD-04b` ×7, hình dạng + một lô, 403 ×2; +1 matrix `TC-A05-mod-audit`), Architecture 27 → 27. Vitest 626 → 626 (chỉ
`schema.d.ts` đổi). Số trước lấy sau lần merge GĐ3 (`02f1d63`). Còn đỏ nền R2 trên máy dev. FE: `pnpm lint`, `typecheck`,
`test`, `build` xanh.

**Thử cho đỏ — 11/11 đột biến bị bắt**, build hợp lệ ở mọi lượt (`0 Error(s)`), file khôi phục nguyên byte (`md5`):

| Đột biến | Ca đỏ thực tế |
|---|---|
| M1 — bỏ `[RequirePermission(audit.read)]` | matrix `TC-A05-mod-audit`; `Khong_phai_admin_403_…` ×2 |
| M2 — bỏ `[PrivilegedEndpoint]` | `Privileged_controllers_carry_the_attribute`; `Khong_phai_admin_403_…` ×2 (không còn dòng `access.denied`) |
| M3 — keyset `<` thành `<=` | `AUD_04_khong_loc_…`; `AUD_04_loc_theo_nguoi_…` |
| M4 — sắp `id` tăng | ba ca `AUD_04_…` |
| M5 — bỏ lọc `actorId` | `AUD_04_loc_theo_nguoi_…`; `AUD_04_loc_theo_doi_tuong_…`; `Khong_phai_admin_…(USER)` |
| M6 — bỏ lọc `targetId` | `AUD_04_loc_theo_doi_tuong_…` |
| M7 — validator bỏ luật `targetId` cần `targetType` | `AUD_04b_…(targetId)`; `Validator` (unit) |
| M8 — validator bỏ luật `action` | `AUD_04b_…` ×2; `Validator` ×2 (unit) |
| M9 — hydrate tên từng dòng (N+1) | `Hinh_dang_dong_va_actor_hydrate_mot_lo` |
| M10 — cursor neo vào dòng thừa | `AUD_04_khong_loc_…`; `AUD_04_loc_theo_nguoi_…` |
| M11 — yaml bỏ 503 | `ModerationContractTests.Runtime_must_not_expose_anything_outside_the_contract` |

Chưa thử: bỏ `AsNoTracking` (cạm bẫy 1) — đường đọc không `SaveChanges`, nên từ ngoài không phân biệt được; lưới thật là trigger
append-only (A1).

**detect-changes:** low, 0 luồng (12 file, 18 symbol; file mới đã `git add -N`). Index báo luồng bị cắt ở bước dựng process — "0 luồng" là
số tối thiểu. Impact trước khi sửa: `AddModerationModule` UNKNOWN — text search: 7 lời gọi, chữ ký không đổi, chỉ thêm
hai đăng ký scoped; `ModerationUserCard` chỉ dùng lại, không sửa.

### D9 — 2026-09-25

Làm đúng Mục 13; L-D6 áp như chốt. `INotificationStore` + `NotificationUpsert` + `UpsertResult` (Application) · `NotificationStore`
(Infrastructure, SQL thô trong một transaction của `NotificationDbContext`) · đăng ký scoped trong `AddNotificationModule`.
`giai-doan-6.md` sửa cùng lượt: Đ-6.16 (khối SQL thật thay câu `RETURNING <is_read cũ>` không chạy được), B.6 D9, Mục 10.2 (`NOTIF-C1b`,
`NOTIF-C3b`) — mỗi chỗ ghi "sửa 2026-09-25".

**Lệch so với chính tài liệu này:**
- **Câu (1) tách hai:** `SELECT id, is_read … FOR UPDATE` rồi `UPDATE … WHERE id = @id`, không `UPDATE … FROM (SELECT … FOR UPDATE) old`
  như bản vẽ Mục 13. Ở câu gộp, bảng đích có thể được quét bằng snapshot cũ trước khi truy vấn con chờ khóa, và đúng hay sai lúc đua
  phụ thuộc cách Postgres kiểm lại dòng (EvalPlanQual) theo thứ tự join planner chọn. Khóa trước rồi ghi thì câu `UPDATE` chạy trên
  dòng mình đang giữ. Thêm một vòng đi về trong transaction — chấp nhận được.
- **`reason_code` cập nhật ở nhánh nhóm cũ** (Mục 13 không nói): khôi phục rồi ẩn lại với lý do khác → hiện lý do mới.
- **Store canh cặp loại/người:** `moderation` ⇔ `ActorId` null, và `ReasonCode` chỉ cho `moderation` — sai → `ArgumentException` trước
  mọi I/O. Một handler viết nhầm không ghi được id Moderator vào `last_actor_id` (B.10 #7). Cùng chỗ với luật "không tự báo mình".
- **Hàm phụ SQL nằm trong lớp lồng `GroupTransaction`** giữ transaction ở trường: bản đầu truyền `NpgsqlTransaction` qua tham số hàm
  `private static` và `WriteContractTests.WriteContracts_are_only_the_two_named` đỏ — phương thức nhận `DbTransaction` là dấu hiệu của
  hợp đồng ghi xuyên module (Đ-6.3). Đây là đường ghi trong module, không phải hợp đồng thứ ba, nên đổi hình dạng code, không đổi luật.
- **`NOTIF-C1` không bảo đảm có lượt đua:** 20 lượt `Task.Run` thường chạy gần tuần tự — lượt đầu `COMMIT` trước khi lượt sau tới bước
  1. Đo được: bỏ hẳn bước chạy lại sau `DO NOTHING` (M4) hoặc bỏ `FOR UPDATE` (M5), cả `NOTIF-C1` lẫn `NOTIF-C3` vẫn xanh. Thêm hai ca
  **ép** lượt đua: test giữ một transaction chưa commit (chèn sẵn nhóm — `NOTIF-C1b`; khóa sẵn dòng đã đọc — `NOTIF-C3b`), hỏi
  `pg_stat_activity` tới khi đủ 5 lượt upsert đang chờ khóa, rồi mới commit. `NOTIF-C1`, `NOTIF-C3` giữ lại làm ca "lịch chạy thật".
- **Thêm ca ngoài bảng:** `NOTIF-02-store` thêm vế nhóm đã có của người khác; `NOTIF-03` khẳng định đích, `created_at` đứng yên,
  `updated_at` theo sự kiện cuối; `NOTIF-04` khẳng định `updated_at` vẫn nhảy; `NOTIF-05b` thêm vế người của đợt trước quay lại
  trong đợt mới được đếm; `NOTIF-09-store` thêm lần ẩn thứ hai (lý do mới, vẫn đếm 1); khóa gộp là cặp (người nhận, `group_key`); ba
  biến thể sai cặp loại/người.

**Test:** Unit 548 → 548, Integration 844 → 857 (+13 `NotificationStoreTests`), Architecture 27 → 27. Vitest không chạy (không chạm
FE). Năm ca `NOTIF_C*` chạy 20 lượt liền: 20/20 xanh (cả trước và sau khi dời hàm phụ vào `GroupTransaction`). Còn đỏ nền R2 trên
máy dev.

**Thử cho đỏ — 8/8 đột biến bị bắt**, build hợp lệ ở mọi lượt (`0 Error(s)`), file khôi phục nguyên byte (`md5`). Lượt đầu M4, M5
**xanh** — đó là lý do có `NOTIF-C1b`, `NOTIF-C3b` (xem trên); bảng dưới là lượt sau khi thêm hai ca đó:

| Đột biến | Ca đỏ thực tế |
|---|---|
| M1 — tăng `actor_count` mỗi lượt (bỏ vế "chèn được người") | `NOTIF_04_…`; `NOTIF_C2_…` |
| M2 — đợt mới không đếm lại từ 1 (`CASE` bỏ) | `NOTIF_05_…`; `NOTIF_05b_…` |
| M3 — đợt mới không xóa người của đợt cũ | `NOTIF_05_…`; `NOTIF_05b_…`; `NOTIF_C3_…`; `NOTIF_C3b_…` |
| M4 — không chạy lại bước 1 sau `DO NOTHING` | `NOTIF_C1b_…` |
| M5 — bước 1 không `FOR UPDATE` | `NOTIF_C3b_…` |
| M6 — bước 1 không đặt `updated_at` | `NOTIF_03_…`; `NOTIF_04_…`; `NOTIF_09_store_…` |
| M7 — bỏ `Skipped` khi tự báo mình | `NOTIF_02_store_…` |
| M8 — bỏ kiểm cặp loại/người | `Sai_cap_loai_va_nguoi_nem_truoc_khi_ghi` |

**detect-changes:** low, 0 luồng (6 file, 17 symbol; file mới đã `git add`). Impact trước khi sửa: `AddNotificationModule` UNKNOWN —
text search: 4 lời gọi (Program.cs, `PostgresFixture`, `ModulesApiFactory`, `NotificationDbContextSchemaTests`), chữ ký không đổi, chỉ
thêm một đăng ký scoped mà container trần của test schema không resolve.

### D10 — 2026-09-25

Làm đúng Mục 14 cho ba handler "làm ngay": `FriendRequestSentHandler`, `FriendRequestAcceptedHandler`, `ContentHiddenHandler` ở
`Notification/Application/Handlers/`, đăng ký bằng `AddIntegrationEventHandler` trong `AddNotificationModule`. Không chỗ lệch nào của
Mục 0.6 áp vào D10; `giai-doan-6.md` sửa B.6 D10 (trạng thái bước 9).

**Lệch so với chính tài liệu này:**
- **Mỗi handler có hàm tĩnh `For(event)`** trả `NotificationUpsert` — phép dịch tách khỏi I/O để unit test khẳng định đúng VAI từng id
  (hai `Guid` đổi chỗ vẫn compile). Handler không bắt ngoại lệ; unit `Loi_cua_store_thoat_ra_khoi_handler` canh.
- **`ContentHiddenHandler` không so tự báo mình:** không có actor để so — Moderator ẩn bài của chính mình vẫn nhận thông báo.
- **Bước 9 đã mở khóa:** GĐ3 (`02f1d63`) và GĐ5 đã merge vào `loveart1210` — `CommentCreated`, `ReactionSet` (Content) và `MessageSent`
  (Messaging) đều đã được phát. Ba handler đó KHÔNG làm trong D10: Mục 14 bước 3 đặt chúng ở commit riêng
  `feat(gd6-d): D10 — thông báo bình luận/cảm xúc từ event của GĐ3`, mỗi handler một ca đi từ API thật, và `MessageSentHandler` còn
  phải kiểm `IPresenceReader` (có trên nhánh: `SharedKernel/Realtime/Presence.cs`). `NOTIF-02` (tự thả cảm xúc bài mình) viết lúc đó.
- **Thêm ca ngoài bảng:** `NOTIF-01` khẳng định cả `group_key`, đích (`user` = người kia), người mời không nhận gì, người chấp nhận
  không nhận thêm; `NOTIF-01b` thêm vế B đọc trước khi A hủy → mời lại làm nhóm sáng lại, cùng id dòng; `NOTIF-09` hide với lý do
  của CHÍNH quyết định (khác lý do báo cáo) và soi id Moderator lẫn người báo trên dạng chữ của cả dòng (gồm `group_key`) ở cả hai
  bảng; unit: bốn phép dịch (kể cả ẩn tài khoản — đích `user`, không `postId`) + lỗi store thoát ra.

"Đã đỏ trước" (L-D7): `NOTIF-01`, `-01b`, `-09` đỏ khi bỏ đăng ký handler (M1, M2 dưới đây) — đúng trạng thái trước D10.

**Test:** Unit 548 → 553 (+5 `NotificationHandlerTests`), Integration 857 → 860 (+3 `NotificationHandlerTests`: `NOTIF-01`, `-01b`,
`-09`), Architecture 27 → 27. `EVT-02` xanh, không sửa. Vitest không chạy (không chạm FE). Còn đỏ nền R2 trên máy dev.

**Thử cho đỏ — 7/7 đột biến bị bắt**, build hợp lệ ở mọi lượt (`0 Error(s)`), file khôi phục nguyên byte (`md5`):

| Đột biến | Ca đỏ thực tế |
|---|---|
| M1 — bỏ đăng ký `FriendRequestSentHandler` | `NOTIF_01_…`; `NOTIF_01b_…` |
| M2 — đăng ký `ContentHiddenHandler` bằng `AddScoped<IIntegrationEventHandler<…>>` (cạm bẫy C0) | `NOTIF_09_…` |
| M3 — chấp nhận: người nhận là người bấm | `NOTIF_01_…`; `Chap_nhan_…` (unit) |
| M4 — lời mời: khóa gộp theo người được mời | `NOTIF_01_…`; `Loi_moi_ket_ban_…` (unit) |
| M5 — ẩn: đích luôn `post` | `Tai_khoan_bi_an_dich_la_user_khong_co_bai` (unit) |
| M6 — ẩn: lý do cố định `other` | `NOTIF_09_…`; `Noi_dung_bi_an_…`, `Tai_khoan_bi_an_…` (unit) |
| M7 — handler nuốt lỗi của store | `Loi_cua_store_thoat_ra_khoi_handler` (unit) |

**detect-changes:** low, 0 luồng (6 file, 1 symbol — so với index đang giữ D9; file mới đã `git add -N`). Impact trước khi sửa:
`AddNotificationModule` UNKNOWN — như D9 (4 lời gọi, chữ ký không đổi); nay thêm ba `EventHandlerRegistration` singleton mà chỉ bus
của host đọc.

### D11 — 2026-09-25

Làm đúng Mục 15 và checklist D0 cho `notification-v1` (Mục 2): `NotificationApiGroup` (`"notification-v1"`, `"Thông báo"`),
`AddApplicationPart` + dòng `apiGroups`, `notification-v1.yaml` `1.0.0-gd6` với **đúng** bốn operation, `NotificationContractTests` +
dòng `Content Include`, `pnpm gen:api` sinh `lib/api/notification/schema.d.ts` (glob tự thấy, không file sinh nào khác đổi).
`NotificationsController` (`[Authorize]`, không mã quyền, không `[PrivilegedEndpoint]`) · `NotificationService` · `INotificationQueries` +
`NotificationQueries` (LINQ `AsNoTracking`, `ExecuteUpdateAsync`) · DTO + `ListNotificationsQuery`/`ReadAllRequest` + validator +
`NotificationCursor` · matrix `NOTIF-IDOR`, `TC-A01-notifications`. `giai-doan-6.md` sửa cùng lượt: Mục 8.3 (version, hình dạng), B.6 D11.

**Lệch so với chính tài liệu này:**
- **Mọi trường của `NotificationResponse` luôn có mặt, `null` khi không áp dụng** (Mục 8.3 bản đầu ghi `postId?`, `reasonCode?`) — cùng
  nếp `TargetSnapshot` (D7b), `AuditLogItem` (D8): app không bỏ trường null khi ghi JSON. `type`, `target.type`, `reasonCode` là enum
  trong hợp đồng; tập `reasonCode` chép `ReasonCode` của `moderation-v1` — cổng hợp đồng không so schema response, giữ khớp bằng tay
  (như `content-v1` ở D7a).
- **Dòng matrix `NOTIF-IDOR` dựng thông báo của B bằng `INSERT`**, không qua API: thông báo sinh từ event chạy bất đồng bộ sau request, và
  `AuthZArrange` không có bus để chờ — thêm vào là sửa khung matrix (Mục 6.3 cấm). Đường event → thông báo có `NotificationHandlerTests`.
- **Test endpoint dựng thông báo bằng `INotificationStore`** (D9), không bằng event: tất định, và mỗi ca chỉ chứng minh một điều. Hai
  điều đó đã có lưới riêng (`NotificationStoreTests`, `NotificationHandlerTests`).
- **`NOTIF-08` so hai số đếm**, không so với một hằng viết tay như `FEED-Q1`: đúng câu "số câu SQL bằng nhau" của Mục 15; trang có
  người thật (hồ sơ qua API) nên lô `IUserDirectory` thật sự chạy ở cả hai lượt.
- **`MarkReadAsync` không có vế `is_read = false`**: đã đọc rồi vẫn khớp một dòng → 204 (idempotent, Mục 15). Có vế đó thì bấm lại
  thông báo đã đọc (hai tab) nhận 403 như thông báo của người khác — M6 dưới đây.
- **`read-all` luôn 204**, kể cả khi không nhóm nào khớp; `upTo` đổi về UTC trước khi so (Npgsql từ chối offset khác 0).
- **Thêm ca ngoài bảng:** `NOTIF-06` thêm người không có thông báo (0); `NOTIF-07` thêm vế thông báo của người khác không bị chạm và sự
  kiện mới vào nhóm đã đọc làm nó chưa đọc lại; `NOTIF-10` đánh dấu hai lần (204, 204) và khẳng định `updatedAt` không đổi;
  `NOTIF-IDOR` khẳng định thông báo của B vẫn chưa đọc; hình dạng (actor có ảnh → `avatarUrl` ký sẵn, actor không hồ sơ → `null` mà
  vẫn đếm, `moderation` không actor có lý do, đích `user` không `postId`, chỉ thấy thông báo của mình); phân trang 25 nhóm → 10/10/5;
  năm biến thể 400.

"Đã đỏ trước" (L-D7): dòng `NOTIF-IDOR` và ca `NOTIF_IDOR_…` đỏ khi bỏ vế `recipient_id` (M1) — xem bảng.

**Test:** Unit 553 → 553, Integration 860 → 876 (+12 `NotificationEndpointTests`, +2 `NotificationContractTests`, +2 matrix `NOTIF-IDOR`,
`TC-A01-notifications`), Architecture 27 → 27. Vitest 626 → 626 (chỉ thêm `schema.d.ts`). Còn đỏ nền R2 trên máy dev. FE:
`pnpm lint`, `typecheck`, `test`, `build` xanh.

**Thử cho đỏ — 11/11 đột biến bị bắt**, build hợp lệ ở mọi lượt (`0 Error(s)`), file khôi phục nguyên byte (`md5`):

| Đột biến | Ca đỏ thực tế |
|---|---|
| M1 — `read` không kiểm `recipient_id` | matrix `NOTIF-IDOR`; `NOTIF_IDOR_…` |
| M2 — `read` đóng dấu `updated_at` | `NOTIF_10_…` |
| M3 — `read-all` bỏ `upTo` | `NOTIF_07_…` |
| M4 — keyset `<` thành `<=` | `Phan_trang_keyset_khong_trung_khong_sot` |
| M5 — hydrate tên từng dòng (N+1) | `NOTIF_08_…` |
| M6 — `read` chỉ khớp dòng chưa đọc (mất idempotent) | `NOTIF_10_…` |
| M7 — `unread-count` không lọc `is_read` | `NOTIF_06_…`; `NOTIF_10_…` |
| M8 — controller bỏ khai 403 của `read` | `NotificationContractTests.Contract_must_be_fully_implemented` |
| M9 — validator bỏ luật `upTo` | `Tham_so_sai_400_dung_truong(…read-all…)` |
| M10 — danh sách không lọc người nhận | `Hinh_dang_…`; `NOTIF_07_…`; `NOTIF_08_…`; `NOTIF_10_…`; `Phan_trang_…` |
| M11 — yaml bỏ 403 của `read` | `NotificationContractTests.Runtime_must_not_expose_anything_outside_the_contract` |

**detect-changes:** low, 0 luồng (14 file, 10 symbol — so với index đang giữ D9 + D10; file mới đã `git add -N`). Impact:
`AddNotificationModule` UNKNOWN — như D9 (4 lời gọi, chữ ký không đổi, thêm hai đăng ký scoped). `AuthZMatrix` UNKNOWN, **chạy sau khi đã
thêm dòng** (quên chạy trước) — text search: chỉ `AuthZMatrixTests` đọc `Cases`; thêm dòng là đúng điểm mở rộng của khung.

### D12 — 2026-09-25

Làm đúng Mục 16. `SearchController` (Profile, `[Route("api/v1/search")]`, `[Authorize]`, nhóm `profile-v1`) · `SearchUsersQuery` +
validator · `SearchService` (lấy dư 5, lọc `IAccountStatusReader`, ký avatar) · `IProfileSearch` + `ProfileSearch` (một câu SQL thô trên
kết nối của `ProfileDbContext`) · `profile-v1.yaml` `1.1.0-gd6` chỉ-thêm (`GET /search`, `SearchResult`, `SearchPage`) · `pnpm gen:api` →
chỉ `lib/api/profile/schema.d.ts` đổi · matrix `TC-A01-search` · `tests/load/search/explain.sql` sửa theo câu D12. `giai-doan-6.md` sửa
cùng lượt: Mục 8.5 (hàng `profile-v1`), B.6 D12.

**Lệch so với chính tài liệu này:**
- **Không có `SearchTerm.EscapeLike`:** `LikePattern.Escape` ở `SharedKernel/Text/` đã có từ D2 (đúng câu "dùng chung với D2" của Mục 16
  bước 3), kèm `LikePatternTests`. Thêm hàm thứ hai là đúng lỗi hàm đó sinh ra để chặn.
- **Không CTE `WITH q AS (…)`:** câu thật viết `profile.search_norm($1)` thẳng trong `WHERE`/`ORDER BY`. Cột của một CTE ở vế phải `LIKE`
  là điều kiện join, không phải hằng lúc chạy — index GIN chỉ dùng được khi planner chọn đường tham số hóa. Hàm `IMMUTABLE` trên tham
  số thì là hằng chắc chắn. `similarity` so với `search_norm($2)` (từ khóa thô đã chuẩn hóa), không với chuỗi thô.
- **`type` so chính xác `user`** (`USER` → 400); vắng mặt = `user`. Thông điệp 400: `q` "Nhập ít nhất 2 ký tự." (thiếu, rỗng, chỉ khoảng
  trắng, 1 ký tự sau trim) / "Tối đa 50 ký tự."; `type` "Chỉ hỗ trợ tìm người dùng (type=user)."; `limit` "Số kết quả phải từ 1 đến 20.".
- **`avatarUrl` luôn có mặt, `null` khi không ảnh** (Mục 8.5 ghi `avatarUrl?`) — cùng nếp D7b/D8/D11.
- **`SRCH-07` EXPLAIN đúng câu endpoint vừa gửi:** bắt bằng `SqlCommandCounter` (lọc câu có `search_norm`) rồi `EXPLAIN` lại với cùng
  ba tham số — không chép câu SQL vào test (chép thì test xanh cả khi code đổi sang `lower(unaccent(…))`). Kèm: một lượt tìm đúng HAI
  câu SQL (tìm + một lô trạng thái tài khoản).
- **Thêm ca ngoài bảng:** `SRCH-01..03` gộp một ca, thêm vế chữ hoa ("NGUYEN", "Văn"), "Lê Đức Anh" (đ ở từ giữa) và đối chứng "guyen"
  KHÔNG khớp (tiền tố từ, không phải chuỗi con); `SRCH-04` tám biến thể (thêm `" a "`, thiếu `q`, `type=post`, `limit` 0/21) và
  `SRCH-04b` đối chứng (50 ký tự kèm khoảng trắng hai đầu 200, `type=user` 200); `SRCH-06` thêm "%%" chỉ ra tên có "%%"; kết quả có ảnh
  ký sẵn / không ảnh `null` / không khớp → `items` rỗng; unit: biên validator, escape + lấy dư, lọc rồi mới cắt, ký ảnh, không gọi
  `IAccountStatusReader` khi rỗng.

**`EXPLAIN (ANALYZE, BUFFERS)` trên 20.000 hồ sơ** — Postgres 16 của compose dev, DB `socialapp_search` tạo riêng, `--migrate` bảy
schema, `seed-profiles.sql` → `explain.sql` (câu D12, `LIMIT 15`) → `DROP DATABASE socialapp_search`. **Cả ba câu `BitmapOr` của hai
`Bitmap Index Scan on idx_profiles_display_name_search`, không Seq Scan:**

| `q` | Dòng khớp | Bitmap Index Scan (hai vế) | Execution Time |
|---|---|---|---|
| `ng` | 4.813 | 1,78 ms + 0,80 ms | 29,2 ms |
| `nguy` | 1.217 | 1,10 ms + 1,37 ms | 9,6 ms |
| `van` | 2.053 | 0,88 ms + 0,82 ms | 14,8 ms |

Phần lớn thời gian là `Bitmap Heap Scan` + recheck + `Sort` top-N (`similarity` trên mọi dòng khớp) — "ng" khớp 1/4 bảng. Hai vế
trả cùng số dòng ứng viên vì `pg_trgm` coi dấu cách là ranh giới từ: trigram của `ng%` và `% ng%` trùng nhau, recheck lọc lại.

**Test:** Unit 553 → 565 (+12 `SearchServiceTests`), Integration 876 → 892 (+15 `SearchTests`: `SRCH-01..03`, `-04` ×8, `-04b`, `-05`,
`-06`, `-07`, `-08`, ảnh/rỗng; +1 matrix `TC-A01-search`), Architecture 27 → 27. Vitest 626 → 626 (chỉ `schema.d.ts` đổi). Còn đỏ nền
R2 trên máy dev. FE: `pnpm lint`, `typecheck`, `test`, `build` xanh.

**Thử cho đỏ — 9/9 đột biến bị bắt**, build hợp lệ ở mọi lượt (`0 Error(s)`), file khôi phục nguyên byte (`md5`):

| Đột biến | Ca đỏ thực tế |
|---|---|
| M1 — bỏ escape | `SRCH_06_…`; `Gui_tu_khoa_da_escape_va_lay_du` (unit) |
| M2 — bỏ lọc tài khoản không hoạt động | `SRCH_05_…`; `Loc_tai_khoan_…` (unit) |
| M3 — xếp hạng bỏ vế "bắt đầu bằng q" | `SRCH_08_…` |
| M4 — vế cột dùng `lower(unaccent(…))` | `SRCH_07_…` |
| M5 — cắt `limit` trước khi lọc (không lấy dư) | `SRCH_05_…`; `Gui_tu_khoa_…` (unit) |
| M6 — bỏ vế "tiền tố của từ sau dấu cách" | `SRCH_01_03_…`; `SRCH_08_…` |
| M7 — khớp chuỗi con thay tiền tố | `SRCH_01_03_…` (vế "guyen") |
| M8 — validator đo trước khi trim | `SRCH_04_…`; `SRCH_04b_…`; `Tu_khoa_…` ×2 (unit) |
| M9 — yaml bỏ `GET /search` | `ProfileContractTests` cả hai chiều |

Lượt đầu M9 "bỏ qua" vì chuỗi đột biến viết `\n` mà yaml trong worktree dùng CRLF — sửa chuỗi rồi chạy lại riêng M9.

**detect-changes:** low, 0 luồng (11 file, 14 symbol; staged). Impact trước khi sửa: `AddProfileModule` UNKNOWN — text search: 9 lời gọi
(Program.cs, hai harness, bốn lớp test schema/directory/moderation), chữ ký không đổi, chỉ thêm hai đăng ký scoped mà container trần
không resolve.

### D13 — 2026-09-25

Rà theo Mục 17 trên `6d5d65f` (D11) + D12. **Không lệch nào — không commit `fix`**; chỉ commit tài liệu này.

**Bước 1 — `type` ↔ hằng ↔ yaml.** Mười một `type` của bảng 17.1, mỗi cái đúng MỘT hằng C# ở đúng chỗ bảng ghi (`IdentityErrors`,
`AdminErrors` ×5, `ModerationErrors` ×3, `ContentErrors`, `PrivilegedEndpointAttribute`), không chuỗi nào gõ lại ở chỗ khác. Mỗi endpoint
trả nó có khai trong yaml:

| `type` | Hằng | Yaml |
|---|---|---|
| `account-disabled` | `IdentityErrors.AccountDisabledType` | `identity-v1` `POST /auth/login` 403 |
| `revocation-unavailable` | `PrivilegedEndpointAttribute.RevocationUnavailableType` | 16 operation 503 (`admin-v1` 11, `moderation-v1` 5) trỏ `RevocationUnavailable` — khớp 16 action của controller `[PrivilegedEndpoint]`, action nào cũng khai 503 (đếm bằng script) |
| `last-admin` | `AdminErrors.LastAdminType` | `admin-v1` `lock`, `PUT …/role` → `LastAdmin` |
| `role-code-taken`, `system-role`, `role-in-use` | `AdminErrors` | `admin-v1` `POST /admin/roles`, `DELETE` → `RoleConflict` (enum ba URN của `RoleConflictProblem`) |
| `system-role`, `confirmation-required` (+ `added`, `removed`, `affectedUsers`) | `AdminErrors` | `admin-v1` `PUT …/permissions` → `RolePermissionsConflict` |
| `report-already-decided`, `moderation-target-gone` | `ModerationErrors` | `moderation-v1` `PATCH /reports/{id}` → `ReportDecisionConflict` |
| `moderation-not-hidden` | `ModerationErrors.TargetNotHiddenType` | `moderation-v1` `POST …/restore` → `TargetNotHidden` |
| `post-hidden` | `ContentErrors.PostHiddenType` | `content-v1` `PATCH /posts/{id}` → `PostHidden` |

Mọi `Error` 409/503 trong code GĐ6 (`Identity/Application/Admin`, `Moderation`, `Notification`, `Profile/Application/Search`) đều có
`Type:`. `notification-v1` và `GET /search` không có 409/503 nào.

**Bước 2 — thông điệp.** Mọi `Error` và thông điệp validator thêm ở GĐ6 là tiếng Việt có dấu, không `{`, không id, email, tên kiểu (các
`$"…{MaxLimit}"` là hằng số được nội suy lúc biên dịch). Log thêm ở GĐ6 chỉ mang `UserId`, route template, số giây — không `email`,
`ip`, `detail`, `note`, `reason` (luật 7, B.10 #5).

**Bước 3 — cổng.** `Category=Contract` + `ContractGateCoverageTests` + `ProblemDetailsTests`: **45/45 xanh** — tám cổng REST
(`identity`, `admin`, `profile`, `content`, `socialgraph`, `messaging`, `moderation`, `notification`) hai chiều, cổng hub chat, ba ca
phủ cổng, 18 ca Problem Details.

**Bước 4 — bàn giao cho E1.** `src/frontend/lib/api/problem.ts` (`PROBLEM_TYPES`) hôm nay chỉ có ba `type` trước GĐ6 (`feed-overloaded`,
`not-friends`, `realtime-unavailable`) + `bff-session-unavailable` của BFF; **chưa có** `type` nào trong mười một cái ở bảng 17.1. Thêm
chúng (mỗi cái `satisfies` schema tương ứng của `schema.d.ts`) là việc của E1, không của D13.

**detect-changes:** không chạm code (chỉ tài liệu).

### Các đầu việc còn lại

Bước 9 (handler `comment`/`reply`/`reaction`/`message` — đã mở khóa, xem D10). Mỗi đầu việc khi xong điền theo khuôn của hướng dẫn khối A+C:

- chỗ nào đi theo / không theo đề xuất Mục 0.6, và vì sao;
- lệch so với chính tài liệu này;
- kiểm tay (nếu có) trên DB dev;
- số test trước → sau;
- bảng đột biến **thực tế** (ca nào đỏ, lượt nào viết sai và không tính);
- `detect-changes` của commit, và impact đã chạy trước khi sửa.
