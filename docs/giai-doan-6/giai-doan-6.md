# GĐ 6 — Thông báo + Tìm kiếm + Kiểm duyệt/Quản trị (UC-16 → UC-20) · lịch gốc Ngày 19–21

> Nguồn: [`ke-hoach-trien-khai.md`](../ke-hoach-trien-khai.md) mục "GĐ 6 — Notification + Search + Moderation/Admin", nhịp
> cổng mở / cổng đóng ở Mục 0C, và báo cáo PTTK v5.0 (UC-16..20 ở Mục 2.2, UC-19 ở Mục 2.3, US-019 ở Mục 3.4, FR-017..020
> ở Mục 4, BR-07, ENT-09/12/13 ở Mục 5.2–5.5, Mục 5.6 index, Mục 5.7 audit/retention, API-Search/API-Admin-Users/API-Reports
> ở Mục 6.6, ma trận 6.7.2, TC-A05/A06 ở Mục 6.7.5).
> Việc **hoãn có địa chỉ** mà GĐ6 phải trả: [`giai-doan-1.md`](../giai-doan-1/giai-doan-1.md) Mục 2 (CRUD vai trò, bất biến
> "≥ 1 Admin", invalidate cache quyền, bên ghi `revoked:user`, trigger vai trò hệ thống, `permissions.description`), Mục 3.4,
> 5.4, 7.5; ghi chú C1/C2 cuối D8 ở [`huong-dan-khoi-d-endpoint.md`](../giai-doan-1/huong-dan-khoi-d-endpoint.md); BR-07 của
> GĐ2 (`hidden_reason` chờ sẵn); event của GĐ3 (Đ-3.12), GĐ4 (Đ-4.15), GĐ5 (Đ-5.15).
>
> **Người thi công: một người**, làm cả backend lẫn frontend, **chạy song song** với hai thành viên khác: **A làm GĐ3**
> (bình luận/cảm xúc), **B làm GĐ5** (chat realtime). Lúc viết tài liệu này cả GĐ3 lẫn GĐ5 **chưa có dòng code nào**, mà GĐ6
> lại là giai đoạn **tiêu thụ** event của cả hai. Cách chia việc để không ai chờ ai ở Mục 9.
>
> **Ba mốc không lùi được của giai đoạn này:**
> 1. **Thay đổi quyền có hiệu lực ở request kế tiếp, không cần đăng nhập lại** — nâng/hạ vai trò, khóa tài khoản, sửa quyền
>    của một vai trò. Chứng minh **trên UI thật, trên staging** (kế hoạch gốc bắt buộc). Đây là lần đầu bên ghi của
>    `revoked:user` (dựng bên đọc từ GĐ1) chạy thật.
> 2. **Không đường nào đưa hệ thống về 0 Admin hoạt động, và không ai ngoài người có quyền chạm được hàng đợi kiểm duyệt
>    hay màn quản trị** — TC-A05, TC-A06 xanh trên CI, kể cả dưới hai Admin thao tác đồng thời.
> 3. **Ẩn bài = bài `hidden` + báo cáo `resolved` + một dòng `audit_logs`, trong cùng một transaction** (PTTK ENT-12). Lỗi
>    giữa chừng thì không còn gì trong ba thứ đó; `audit_logs` không sửa, không xóa được.

## Tài liệu này có hai phần

| Phần | Trả lời câu hỏi | Đọc khi |
|---|---|---|
| **A — Thiết kế và quyết định** | *Cái gì* và *vì sao* | Trước khi gõ dòng đầu tiên; lúc review PR |
| **B — Kế hoạch triển khai** | *Làm gì, theo thứ tự nào, xong thì trông ra sao* | Lúc lên lịch; lúc kiểm tiến độ |

Hướng dẫn thi công từng bước (lệnh nào, file nào, cạm bẫy nào) nằm ở các file `huong-dan-khoi-*.md` cùng thư mục,
**viết khi khối đó bắt đầu** — đúng nếp GĐ1, GĐ2, GĐ4.

> **Trạng thái các quyết định:** hai mươi mốt quyết định ở Mục 3 là **đề xuất**, viết ngày 2026-09-23 trên nền code nhánh
> `loveart1210` (commit `ac509e4`, GĐ4 đã đóng; module `Notification`, `Moderation`, `Messaging` chỉ có `.gitkeep`). Cổng
> mở chốt hoặc sửa từng cái; cái nào sửa thì ghi ngày và lý do ngay dưới quyết định đó. Làm một mình thì "cổng mở" là một
> buổi tự rà **có sản phẩm**: hợp đồng commit trước, code sau (Mục 0C).
>
> **Sáu quyết định chạm tới người khác — phải báo trước khi chốt:** Đ-6.2 và Đ-6.4 (event bus A và B sẽ phát vào),
> Đ-6.3 (hai hợp đồng **ghi** ở SharedKernel — lệch Đ-2.3), Đ-6.14 (`comments.status` thêm `hidden` — bảng của A),
> Đ-6.18 (hub thông báo dùng vé của B), Đ-6.11 (`GET /me` thêm trường — hợp đồng Identity đã đóng băng).

---

# Phần A — Thiết kế và quyết định

## 0. Thuật ngữ

| Từ | Nghĩa trong tài liệu này |
|---|---|
| **báo cáo** | ENT-12 `reports` — một người dùng báo một **đối tượng** (bài, bình luận, người dùng) vi phạm, kèm `reason_code` |
| **hàng đợi** | Các báo cáo `open`, gom theo đối tượng — thứ Moderator mở ra đầu tiên (UC-19 bước 1) |
| **quyết định** | Kết luận **một lần duy nhất** cho một báo cáo: `hide` (ẩn đối tượng), `dismiss` (bỏ qua), `resolve` (đã xử lý ngoài luồng) |
| **ẩn (hidden)** | BR-07: đối tượng chuyển `status = hidden`. Tác giả vẫn thấy kèm lý do; người khác không thấy |
| **nhật ký kiểm toán** | ENT-13 `audit_logs` — append-only: ai, làm gì, trên cái gì, lúc nào, từ IP nào |
| **hợp đồng ghi** | Interface ở SharedKernel mà module khác **gọi để ghi** vào bảng của module chủ, trong transaction của người gọi (Đ-6.3) |
| **event** | Sự kiện trong tiến trình, phát **sau `COMMIT`** bởi module chủ dữ liệu (`CommentCreated`, `FriendRequestSent`…) |
| **nhóm thông báo** | Một dòng `notifications` gom mọi sự kiện cùng `group_key` cho cùng một người nhận (FR-018 "gộp cùng loại") |
| **đợt** | Chuỗi sự kiện dồn vào một nhóm kể từ lần người nhận **đọc** nhóm đó gần nhất (Đ-6.16) |
| **quyền hiệu lực** | Tập mã quyền thật sự áp cho một vai trò lúc này: dòng `role_permissions`, hoặc **toàn bộ** mã nếu là ADMIN |
| **vai trò hệ thống** | Đúng ba vai trò `ADMIN`, `USER`, `MODERATOR` — không xóa, không đổi `code` (GĐ1 Mục 3.4) |
| **Admin hoạt động** | Tài khoản vai trò `ADMIN` có `status = 'active'` — đại lượng mà bất biến "≥ 1" canh (Đ-6.7) |
| **thu hồi (revoke)** | Ghi `revoked:user:{id} = mốc` trên Redis → mọi access token phát trước mốc bị 401 (GĐ1 Mục 7.5) |

## 1. Mục tiêu giai đoạn

### Phát biểu một câu

> **Người dùng được báo cho biết ai vừa tương tác với mình, tìm được nhau bằng tên gõ không dấu, và báo được nội dung
> xấu; Moderator ẩn được nội dung đó mà mọi bước đều để lại dấu vết không xóa được; Admin khóa tài khoản, đổi vai trò, sửa
> quyền của cả một vai trò — và mọi thay đổi quyền có hiệu lực ngay ở request kế tiếp, trên hệ thống thật, mà không bao giờ
> khóa được chính cửa quản trị.**

### Mục tiêu chính thức và khối nào gánh

| Mã | Mục tiêu | Đạt bằng | Kiểm bằng |
|---|---|---|---|
| **FR-017** | Tìm người theo tên hiển thị, khớp không dấu, tiền tố | Đ-6.19, A4, D12 | `SRCH-*`, F2 |
| **FR-018** | Thông báo khi có bình luận, cảm xúc, gắn thẻ, lời mời, chấp nhận, tin nhắn mới; gộp cùng loại | Đ-6.16–6.18, C1, D9–D11, E3 | `NOTIF-*`, `EVT-*`, F2 |
| **FR-019** | Tiếp nhận báo cáo với lý do chuẩn hóa, đưa vào hàng đợi | Đ-6.12, D6 | `REP-*`, `REP-C1` |
| **FR-020** | Moderator ẩn/khôi phục nội dung; Admin khóa/mở, gán vai trò; ghi audit | Đ-6.5–6.9, 6.13–6.15, D1–D5, D7–D8 | `MOD-*`, `ADM-*`, `AUD-*` |
| **BR-07** | Nội dung bị gỡ → `hidden`: tác giả thấy kèm lý do, người khác không thấy | Đ-6.14, D7 | `HID-*` |
| **US-019 AC-01..04** | Ẩn → Hidden+Resolved+audit · bỏ qua → Dismissed · user thường → 403 + audit · xử lý lại → 409 | D7, C4 | `MOD-01..04` |
| **TC-A05 / TC-A06** | User gọi `/admin/*` → 403 · User gọi `PATCH /reports/{id}` → 403 | Tầng 2 + matrix | Mục 6.3 |
| **GĐ1 Mục 2 (hoãn)** | CRUD vai trò · bất biến ≥ 1 Admin · invalidate cache quyền · bên ghi `revoked:user` · trigger vai trò hệ thống | Đ-6.5–6.10, D1–D5 | `ROLE-*`, `ADM-C1`, `PERM-*`, `REV-*` |
| **GOAL-03** | 0 IDOR — hai dòng TC cuối cùng của matrix | Toàn giai đoạn | Mục 6.3 |

### Vì sao GĐ6 khó hơn danh sách endpoint của nó

Nhìn danh sách thì GĐ6 là "CRUD mấy bảng quản trị + một ô tìm kiếm + cái chuông". Năm thứ làm nó khác mọi giai đoạn trước:

1. **Lần đầu cấu hình quyền là dữ liệu sống.** GĐ1 dựng RBAC trên giả định "quyền không đổi lúc chạy": cache 60 giây không
   xóa, vai trò đóng dấu vào JWT 15 phút. Từ GĐ6, Admin đổi vai trò lúc 10:07:30 thì lúc 10:07:31 hệ thống **phải** đã khác.
   Ba cơ chế cũ (JWT, cache quyền, kết nối hub sống lâu) đều phải được dạy cách quên (Đ-6.6, Đ-6.10).
2. **Lần đầu một thao tác ghi vào hai module trong một transaction.** "Ẩn bài + đóng báo cáo + ghi audit" chạm schema
   `content` và `moderation`; "khóa tài khoản + ghi audit" chạm `identity` và `moderation`. Mọi giai đoạn trước, mỗi request
   chỉ ghi schema của một module — và Đ-2.3 cấm hợp đồng ghi (Đ-6.3).
3. **Lần đầu có thao tác có thể tự khóa hệ thống.** Hạ quyền Admin cuối cùng, gỡ `post.create` của `USER`, xóa nhầm vai trò —
   mỗi cái một câu SQL, và không có đường phục hồi nào ngoài psql. Guard phải đúng **dưới đồng thời**, không chỉ tuần tự.
4. **Lần đầu một module sống bằng event của ba module khác** — trong khi hai trong số đó (GĐ3, GĐ5) đang được viết song song
   bởi người khác. Nếu không dựng "đường ray" trước (Đ-6.4), GĐ6 hoặc ngồi chờ, hoặc sửa code của người khác sau lưng họ.
5. **Nhiều module nhất:** hai module mới (Notification, Moderation) và bốn module cũ phải sửa (Identity, Profile, Content,
   SocialGraph), ba hợp đồng mới + ba hợp đồng cũ mở lại theo kiểu chỉ-thêm.

---

## 2. Phạm vi

### Trong phạm vi

| Nhóm | Nội dung |
|---|---|
| **Module Moderation** | Schema `moderation`; bảng `reports`, `audit_logs` (append-only bằng trigger); `ModerationDbContext`; nối `--migrate` |
| **Module Notification** | Schema `notification`; bảng `notifications`, `notification_actors`; `NotificationDbContext`; nối `--migrate` |
| **Hạ tầng chéo module (SharedKernel)** | Event bus trong tiến trình (Đ-6.2) · hai hợp đồng ghi `IAuditTrail`, `IModerationTargets` (Đ-6.3) · `IAccountStatusReader` · invalidate cache quyền qua Redis pub/sub (Đ-6.10) · fail-closed cho endpoint quản trị (Đ-6.8) · ghi audit khi bị từ chối (Đ-6.15) |
| **Quản trị tài khoản (Identity)** | Danh sách/chi tiết tài khoản · khóa/mở · gán vai trò · bất biến ≥ 1 Admin · bên ghi `revoked:user` · login/refresh từ chối tài khoản `disabled` |
| **Quản trị vai trò (Identity)** | Liệt kê, tạo, đổi tên, sửa tập quyền, xóa vai trò · mã quyền mới `role.manage` · `permissions.description` · trigger chặn đổi/xóa vai trò hệ thống |
| **Báo cáo + kiểm duyệt (Moderation)** | Gửi báo cáo · hàng đợi · chi tiết + ảnh chụp đối tượng · quyết định `hide`/`dismiss`/`resolve` · khôi phục · đọc audit |
| **BR-07 phía Content** | `GET /posts/{id}` cho tác giả xem bài bị ẩn kèm lý do; người khác 404; bình luận `hidden` (khi GĐ3 đã có) |
| **Thông báo** | Nhận event → gộp theo nhóm → danh sách, số chưa đọc, đánh dấu đã đọc · hub `/hubs/notifications` (khi GĐ5 có vé) · hỏi lại 30 giây khi chưa có hub |
| **Tìm kiếm (Profile)** | `GET /search?q=&type=user` · `unaccent` + `pg_trgm` + index GIN · lọc tài khoản không hoạt động |
| **Hợp đồng** | Mới: `moderation-v1.yaml`, `notification-v1.yaml`, `admin-v1.yaml`, `notification-hub-v1.md` (+ ví dụ). Mở lại chỉ-thêm: `profile-v1` (tìm kiếm), `identity-v1` (`/me.permissions`, 403 `account-disabled`), `content-v1` (`PostResponse.moderation`) |
| **Lane frontend** | Chuông + danh sách thông báo · ô tìm kiếm + trang kết quả · hộp thoại báo cáo · màn kiểm duyệt · màn quản trị tài khoản · màn vai trò + ma trận quyền · màn nhật ký · biểu ngữ "bài bị ẩn" cho tác giả · điều hướng theo quyền |
| **Test** | AuthZ matrix (TC-A05, TC-A06 + đối chứng) · test transaction xuyên module · test đồng thời (bất biến Admin, xử lý báo cáo, gộp thông báo) · cổng hợp đồng cho ba file mới · E2E trên staging |

### Ngoài phạm vi — hoãn có địa chỉ

| Việc | Hoãn tới | Lý do |
|---|---|---|
| Áp bất biến "≥ 1 Admin" cho đường **tự xóa tài khoản** | **GĐ8** | Đường tự xóa chưa tồn tại. GĐ6 để sẵn **một** hàm `AdminInvariant.EnsureRemainsAsync` để GĐ8 gọi lại (Đ-6.7) |
| Xóa audit quá 12 tháng (NFR-COMP-01) | **GĐ8** | Trigger append-only (Đ-6.15) chừa sẵn cửa xóa có kiểm soát bằng cờ phiên `socialapp.audit_purge`; job xóa là việc của GĐ8 |
| Dọn thông báo đã đọc quá 90 ngày (30M dòng/năm theo PTTK Mục 5.6) | **GĐ8** / Roadmap | Chưa đủ dữ liệu để đau; index một phần (Đ-6.16) giữ badge rẻ tới lúc đó |
| Gắn thẻ `@tên` (loại thông báo `tag` của FR-018) | **Cắt được** — xem thứ tự cắt B.10 | Cần cú pháp nhắc tên trong bình luận (hợp đồng của A) + gợi ý tên lúc gõ. GĐ6 thiết kế sẵn (Đ-6.17) nhưng làm **sau cùng** |
| Cảnh cáo người dùng (UC-19 "ẩn/gỡ, cảnh cáo") | **Ngoài MVP** | Không FR, không bảng, không AC nào đặc tả; `resolve` + ghi chú là đủ để lưu dấu |
| Kháng nghị của tác giả bị ẩn bài | **Ngoài MVP** | Không nguồn nào trong PTTK |
| Tìm kiếm bài viết (`type=post`) | **Ngoài MVP** | FR-017 chỉ nói người dùng; tham số `type` giữ chỗ để thêm sau không đổi hình dạng |
| Thông báo đẩy ra ngoài app (email, web push) | **Ngoài MVP** | FR-018 chỉ đòi thông báo trong app |
| Grafana cho kiểm duyệt (báo cáo mở, thời gian xử lý) | **GĐ7** | GĐ6 để sẵn hai metric (C1, D7); GĐ7 vẽ |
| Rút lại thông báo khi người kia gỡ cảm xúc / xóa bình luận | **Ngoài MVP** | Đ-6.16 — một lần báo đã xảy ra; bấm vào đối tượng đã mất thì ra 404 có câu dễ hiểu |

---

## 3. Quyết định thiết kế

Hai mươi mốt quyết định, sáu nhóm. **Nhóm I (Đ-6.1 → Đ-6.4)** là kiến trúc chéo module — đắt nhất nếu sai và chạm tới A,
B; chốt đầu tiên ở cổng mở. **Nhóm II (Đ-6.5 → Đ-6.11)** là quyền và tài khoản — chỗ tự khóa được hệ thống. **Nhóm III
(Đ-6.12 → Đ-6.15)** là kiểm duyệt. **Nhóm IV (Đ-6.16 → Đ-6.18)** là thông báo. **Nhóm V (Đ-6.19)** là tìm kiếm. **Nhóm VI
(Đ-6.20, Đ-6.21)** là frontend.

### Nhóm I — Kiến trúc chéo module

### Đ-6.1 Hai module mới giữ đúng tên PTTK; quản trị tài khoản và vai trò ở **Identity**, không ở Moderation — lệch bảng thành phần PTTK

PTTK Mục 6.4 giao `/api/v1/reports/*` **và** `/admin/*` cho CMP-07 Moderation. Chia lại:

| Endpoint | Module | Vì sao |
|---|---|---|
| `/reports*`, `/moderation/*`, `/admin/audit-logs` | **Moderation** (schema `moderation`) | Đúng PTTK: báo cáo, quyết định, nhật ký là dữ liệu của Moderation |
| `/admin/users*`, `/admin/roles*`, `/admin/permissions` | **Identity** (schema `identity`) | Mọi thứ các endpoint này ghi — `users.status`, `users.role_id`, `roles`, `role_permissions`, `refresh_tokens` — là bảng của Identity; mọi guard (bất biến Admin, vai trò hệ thống, `revoked:user`) cần nội tạng của Identity |
| `/notifications*`, `/hubs/notifications` | **Notification** (schema `notification`) | Đúng PTTK |
| `/search` | **Profile** | `display_name` là cột của Profile; index nằm cạnh cột (Đ-6.19) |

Nếu để `/admin/users` ở Moderation như PTTK thì Moderation phải gọi Identity **ghi hộ** năm thứ qua hợp đồng — tức là mở
năm hợp đồng ghi thay vì một (Đ-6.3). Swagger vẫn tách được theo nhóm: `admin-v1` là **nhóm thứ hai của Identity** (file
`Identity/Presentation/admin-v1.yaml`), nên người đọc vẫn thấy "trang quản trị" là một chỗ. Ghi vào Mục 13.

Cả hai module mới chép đúng khuôn Đ-2.1/Đ-2.2: một schema, một `DbContext` + `Options` + design-time factory, bảng lịch sử
migration trong schema riêng, **không FK sang schema khác** (`reporter_id`, `actor_id`, `recipient_id` là `uuid` trần).

### Đ-6.2 Event bus trong tiến trình ở SharedKernel — dựng ở GĐ6, theo đúng địa chỉ hoãn của GĐ2 và GĐ5

GĐ2 (bảng "phương án loại bỏ"): *"nếu GĐ5/GĐ6 cần event thật thì dựng ở đó"*. GĐ5 Đ-5.15: *"dựng khung event chung là quyết
định cấp dự án, không phải của một giai đoạn"* — và GĐ5 không dựng. GĐ6 là người tiêu thụ event đầu tiên, nên GĐ6 dựng:

```
SharedKernel/Events/
  IIntegrationEvent                         -- đánh dấu; mọi event là record bất biến, chỉ mang id + enum, KHÔNG mang nội dung
  IEventPublisher.Publish(IIntegrationEvent)          -- producer gọi SAU COMMIT, không await handler
  IIntegrationEventHandler<TEvent>.HandleAsync(e, ct) -- consumer đăng ký trong Add<X>Module bằng AddIntegrationEventHandler
  InProcessEventBus                          -- Channel<T> có giới hạn + BackgroundService tiêu thụ, mỗi handler một scope DI
  EventBusMetrics                            -- Meter("SocialApp.Events"): published_total, dropped_total, tag event
  ContentEvents.cs / SocialGraphEvents.cs / MessagingEvents.cs / ModerationEvents.cs   -- các record event (Đ-6.17)
```

Bốn luật, mỗi luật chặn một lỗi cụ thể:

1. **Phát sau `COMMIT`, không bao giờ trong transaction** — nếp Đ-3.12, Đ-4.15, Đ-5.15. Phát trong transaction là thông báo
   cho một bình luận có thể bị rollback một giây sau.
2. **`Publish` không chờ handler.** Ghi vào `Channel` rồi trả về ngay. Handler chậm hay ném lỗi **không** làm request của
   người thả cảm xúc chậm hay 500 — thông báo là tính năng "Should", thả cảm xúc là tương tác lõi.
3. **Event chỉ mang id và enum** — không `body`, không `displayName`. Người tiêu thụ tự hydrate lúc cần (luật "cache chỉ
   lưu id" của GĐ4). Event nằm trong hàng đợi bộ nhớ và có thể lọt vào log lỗi: không được mang dữ liệu người dùng (Đ-5.18).
4. **Hàng đợi có giới hạn** (`BoundedChannelFullMode.DropWrite`, 10.000 event): tràn thì **rơi event + đếm metric
   `socialapp_events_dropped_total` + log cảnh báo có ngưỡng** (khuôn `FailOpenLogThrottle` của GĐ4), không chặn producer.

**Mất event khi process chết giữa `COMMIT` và lúc handler chạy — chấp nhận, có ghi lại.** Phương án chắc chắn là
*transactional outbox* (event ghi vào bảng trong cùng transaction, worker đọc ra). Đã cân nhắc rồi loại: thêm một bảng + một
worker + xử lý trùng cho **mỗi** module phát event, trong khi cái mất được chỉ là một dòng "An đã thích bài của bạn" lúc
deploy. Chỗ **không được** mất — quyết định kiểm duyệt, audit — thì **không** đi qua event mà đi qua transaction (Đ-6.3).
Ghi vào Mục 13.

**Test không chờ bằng `Task.Delay`:** `InProcessEventBus` có `DrainAsync(timeout)` chờ tới khi hàng đợi rỗng và mọi handler
đang chạy xong. Test `EVT-*` gọi nó (qua harness `DrainEventsAsync`) thay vì ngủ.

*Sửa 2026-09-23 khi thi công C0* (chi tiết ở `huong-dan-khoi-c0-duong-ray.md`, L2/L5/L6): `DrainAsync` là phương thức public của
bus chứ không "chỉ đăng ký trong test harness" — số event dở dang phải nằm trong bus; `IEventPublisher` không có nó nên code
sản phẩm không thấy. Handler đăng ký **chỉ** qua `AddIntegrationEventHandler<TEvent, THandler>()` (bus cần biết kiểu handler
trước khi mở scope riêng cho nó). Metric dùng `System.Diagnostics.Metrics` có sẵn trong .NET 8, GĐ7 C2 kiểm tên ở `/metrics`.
Luật 3 có cổng CI: `IntegrationEventShapeTests` (`EVT-07`).

### Đ-6.3 Ghi xuyên module trong một transaction bằng cách **truyền `DbTransaction`** — hai hợp đồng ghi có tên, lệch Đ-2.3 luật 1 có chủ đích

PTTK ENT-12: *"Cập nhật kết luận + ghi AuditLog cùng transaction"*; UC-19: *"post.status = Hidden; report = Resolved; ghi
audit_log (cùng transaction)"*. Ba bảng, hai schema, hai module. Đ-2.3 luật 1: *"hợp đồng ở SharedKernel chỉ đọc — module
muốn module khác ghi hộ là dấu hiệu chia module sai"*.

Ở đây không phải chia sai: PTTK **cố ý** tách Moderation khỏi Content **và** đòi nguyên tử. Bốn phương án:

| Phương án | Loại vì |
|---|---|
| Moderation viết thẳng `UPDATE content.posts` bằng SQL | Hai module cùng ghi một bảng — đúng thứ Đ-2.1 dựng ra để chặn; ArchUnitNET không bắt được vì là chuỗi SQL |
| Hai transaction tuần tự (ẩn bài, rồi đóng báo cáo) + làm lại khi lỗi | Có trạng thái "bài đã ẩn, báo cáo vẫn mở, không audit" — đúng thứ PTTK cấm; và audit là thứ **không được** thiếu |
| `TransactionScope` trên hai kết nối | Npgsql leo thang thành giao dịch hai pha (prepared transaction), cần `max_prepared_transactions > 0` trên server — cấu hình mới cho một nhu cầu nhỏ |
| **Truyền `DbTransaction` của người gọi vào hợp đồng** | **Chọn.** Cùng database, cùng kết nối → một transaction Postgres thật; mỗi module vẫn là **người duy nhất viết SQL vào bảng của mình** |

Hai hợp đồng ghi, **đúng hai**, mỗi cái có lý do tồn tại riêng:

```csharp
// SharedKernel/Audit/IAuditTrail.cs — hiện thực ở Moderation. Chỉ THÊM, không sửa/xóa/đọc.
Task AppendAsync(DbTransaction? tx, AuditEntry entry, CancellationToken ct);
    // tx != null → INSERT trên đúng kết nối + transaction đó (cùng số phận với thao tác chính)
    // tx == null → tự mở kết nối riêng (chỉ dùng cho audit "bị từ chối" của Đ-6.15)

// SharedKernel/Moderation/IModerationTargets.cs — hiện thực ở Content (bài, bình luận) và Profile (người dùng).
Task<IReadOnlyDictionary<ModerationTarget, TargetSnapshot>> GetSnapshotsAsync(IReadOnlyCollection<ModerationTarget> t, CancellationToken ct);  // đọc, batch
Task<HideOutcome> HideAsync(DbTransaction tx, ModerationTarget t, string reasonCode, CancellationToken ct);   // ghi, bắt buộc có tx
Task<HideOutcome> RestoreAsync(DbTransaction tx, ModerationTarget t, CancellationToken ct);
```

- Người gọi lấy `DbTransaction` bằng `db.Database.CurrentTransaction!.GetDbTransaction()` sau `BeginTransactionAsync`. Hiện
  thực chạy `NpgsqlCommand` **tham số hóa** trên `tx.Connection` — không `DbContext` thứ hai, không `SetDbConnection`.
- `HideAsync` chỉ đổi `published → hidden` (một câu `UPDATE … WHERE status = 'published' RETURNING …`) và báo lại
  `Hidden | AlreadyHidden | NotFound`. Người gọi quyết định 409 hay 200 — hợp đồng không biết HTTP.
- **Không mở thêm hợp đồng ghi nào khác.** Quản trị tài khoản ở Identity (Đ-6.1) chính là để khỏi cần cái thứ ba. Ai muốn
  thêm cái thứ ba phải viết một quyết định mới, có ngày.
- ArchUnitNET: `SharedKernel.Audit` và `SharedKernel.Moderation` là hai namespace **duy nhất** được có phương thức nhận
  `DbTransaction` — thêm một test canh điều đó (`WriteContracts_are_only_the_two_named`).

Ghi lệch Đ-2.3 vào Mục 13 và vào chính comment đầu hai interface.

### Đ-6.4 "Đường ray" ra `develop` trước, trong hai ngày đầu — để A và B phát event thẳng, không "chỉ log" rồi đợi GĐ6 sửa

GĐ3 (Đ-3.12) và GĐ5 (Đ-5.15) được thiết kế khi GĐ6 còn ở sau: *"chỉ log, GĐ6 thay thân hàm"*. Nay ba giai đoạn chạy song
song — nếu GĐ6 đợi A, B merge xong rồi mới "thay thân hàm", GĐ6 phải sửa code của người khác sau lưng họ, và cổng đóng của
GĐ6 trễ theo người chậm nhất.

Chốt: sau cổng mở, GĐ6 tách ngay **PR mỏng thứ nhất** (`gd6-c` khối C0, Mục B.3) vào `develop`, chỉ gồm:

- `SharedKernel/Events/` đầy đủ (Đ-6.2) + **toàn bộ record event** của GĐ3, GĐ4, GĐ5, GĐ6 (Đ-6.17) + đăng ký DI.
- `SocialGraphEvents` của GĐ4 đổi thân hàm sang `Publish` (code của chính người thi công GĐ6 — GĐ4 xong rồi).
- Không bảng, không endpoint, không hợp đồng API — nên không đụng cổng hợp đồng, không cần cổng mở của ai.

A và B **rebase lên `develop`** và gọi `IEventPublisher.Publish(new CommentCreated(…))` thay cho dòng log. Nếu A hoặc B đã
viết lớp "chỉ log" trước khi đường ray tới thì chỉ thay thân hàm — chữ ký lớp của họ giữ nguyên. Chưa có handler nào đăng ký
thì `Publish` là no-op có đếm metric — A, B không phải chờ gì.

**Hai thứ khác cũng đi trong PR đường ray**, vì A và B cũng cần: giá trị `hidden` trong `comments.status` (Đ-6.14) chỉ là
**thỏa thuận** — A đưa vào CHECK từ migration đầu tiên của A; và `IAccountStatusReader` (Đ-6.19) nếu B cần lọc tài khoản bị
khóa khỏi danh sách hội thoại. Cả hai báo ở cổng mở.

### Nhóm II — Quyền và tài khoản

### Đ-6.5 Khóa tài khoản = `status = 'disabled'`; không đụng `locked_until` của FR-003; login **và** refresh từ chối

`UserStatus` đã có bốn giá trị từ GĐ1 (`ck_users_status`), và `identity-v1.yaml` đã ghi: *"FR-003 là trạng thái tạm nằm ở
cột `locked_until`, còn `disabled`/`deleted` chờ GĐ6/GĐ8"*. Nên:

| Khái niệm | Cột | Ai ghi | Hết khi nào |
|---|---|---|---|
| Khóa tạm vì sai mật khẩu 5 lần (FR-003) | `locked_until` | Login | Tự hết sau 15 phút |
| **Khóa bởi Admin (UC-20)** | `status = 'disabled'` | `POST /admin/users/{id}/lock` | Admin mở (`unlock`) |

Giá trị `locked` của `UserStatus` **không dùng** — giữ trong CHECK để không phải migration, ghi comment "không ai ghi".

**Login:** kiểm `disabled` **sau** khi mật khẩu đúng (bước 5 hiện tại), trả **403** `auth.account-disabled` *"Tài khoản đã bị
khóa. Liên hệ quản trị viên."*. Đặt sau bước kiểm mật khẩu là cố ý: người không biết mật khẩu không được biết tài khoản
tồn tại và bị khóa (khác 423 của FR-003, hợp đồng GĐ1 đã chấp nhận lộ). Mật khẩu sai trên tài khoản bị khóa vẫn 401 như mọi
tài khoản khác.

**Refresh:** `RotateAsync` đã join `users` để đọc vai trò (GĐ1 cạm bẫy 7) — thêm điều kiện `status = 'active'`; không đạt →
401 như refresh hỏng. Đây là **lưới thứ hai**: khóa tài khoản đã thu hồi mọi refresh family (Đ-6.6), nhưng nếu một ngày ai đó
quên bước đó thì refresh vẫn không cấp được token cho tài khoản bị khóa.

**Mở khóa** (`unlock`) đưa `status` về `active` **và** xóa `locked_until`, `failed_login_count` — Admin mở khóa thì người dùng
vào được ngay, không phải đợi hết 15 phút của FR-003.

### Đ-6.6 Mọi thay đổi quyền: **một** transaction DB → `COMMIT` → ghi Redis; Redis hỏng sau `COMMIT` thì nói thật trong phản hồi

Bảng GĐ1 Mục 7.5 đã chốt *cái gì* bị thu hồi; GĐ6 chốt *thứ tự* và *khi hỏng thì sao*:

| Thao tác | Trong transaction (DB) | Sau `COMMIT` (Redis) | Người bị đổi thấy gì |
|---|---|---|---|
| Gán vai trò (nâng **hoặc** hạ) | `UPDATE users.role_id` · audit | `revoked:user:{id} = now` | Request kế tiếp 401 → BFF tự refresh → token mang vai trò mới. **Không** bị đăng xuất |
| Khóa | `UPDATE users.status = 'disabled'` · `UPDATE refresh_tokens SET revoked_at` (mọi family) · audit | `revoked:user:{id} = now` | Request kế tiếp 401 → refresh 401 → văng về `/login` |
| Mở khóa | `UPDATE users` · audit | — (không có token nào để thu hồi) | Đăng nhập lại được |
| Sửa tập quyền của một vai trò | `DELETE/INSERT role_permissions` · audit | Phát `authz:permissions-changed` (Đ-6.10) | Request kế tiếp dùng tập quyền mới — **không** cần thu hồi token: token mang `code` vai trò, không mang quyền |
| Đổi tên hiển thị vai trò | `UPDATE roles.display_name` · audit | — | Lần gọi `/me` kế tiếp thấy tên mới |

**DB trước, Redis sau** — GĐ1 Mục 7.5 cạm bẫy 1 (đảo lại thì token phát chen giữa mang vai trò cũ mà qua được mốc thu hồi).
Không test tự động nào bắt được thứ tự này → nằm trong danh sách tự rà (B.10).

**Redis hỏng đúng lúc sau `COMMIT`:** DB đã đổi, không rollback được. Không trả 503 (nói dối — thay đổi đã lưu), không nuốt
lỗi (thu hồi mất âm thầm — `ITokenRevocationStore.RevokeUserAsync` cố ý ném để người gọi thấy). Chốt:

- Thử lại 3 lần trong ~1 giây; vẫn hỏng → log **Error** + metric `socialapp_revocation_failures_total`.
- Phản hồi 200 mang `revocation: "applied" | "deferred"`. `deferred` → UI Admin hiện: *"Đã lưu. Phiên đang mở của người
  này có thể giữ quyền cũ tối đa 15 phút."* — đúng cửa sổ phơi nhiễm mà fail-open của GĐ1 đã chấp nhận.
- Với **khóa**, refresh family đã bị thu hồi **trong DB** nên cửa sổ đó chỉ còn là access token đang sống, và lưới refresh
  của Đ-6.5 chặn việc gia hạn.

### Đ-6.7 Bất biến "luôn còn ≥ 1 Admin hoạt động" — kiểm **trong transaction, dưới khóa tư vấn**, không phải bằng một câu `SELECT COUNT` trước

Đường vi phạm ở GĐ6: hạ vai trò của Admin cuối cùng; khóa Admin cuối cùng. (Xóa vai trò ADMIN bị chặn riêng — Đ-6.9. Tự xóa
tài khoản là GĐ8.)

Kiểm "đếm trước rồi ghi" **sai dưới đồng thời**: có đúng hai Admin X, Y; X hạ Y và Y hạ X cùng lúc; cả hai lần đếm đều thấy
2 → cả hai qua → còn 0. Chốt:

```
BEGIN
  SELECT pg_advisory_xact_lock(<namespace Identity>, <hằng "admin-invariant">)   -- tuần tự hóa MỌI thao tác chạm tập Admin
  UPDATE identity.users SET …  WHERE user_id = @target
  SELECT count(*) FROM identity.users u JOIN identity.roles r ON r.role_id = u.role_id
   WHERE r.code = 'ADMIN' AND u.status = 'active'                               -- đếm SAU khi ghi, trong cùng transaction
  = 0 → ROLLBACK, 409 admin.last-admin  "Hệ thống phải còn ít nhất một quản trị viên đang hoạt động."
COMMIT
```

- **Đếm sau khi ghi** chứ không trước: không phải tự suy "thao tác này có làm giảm số Admin không" — cho DB trả lời.
- Khóa tư vấn chỉ lấy khi thao tác **có thể** chạm tập Admin (người bị đổi đang là ADMIN, hoặc vai trò đích là ADMIN) — gán
  vai trò giữa USER và MODERATOR không xếp hàng sau ai.
- Một hàm duy nhất `AdminInvariant.EnsureRemainsAsync(db, ct)` ở `Identity.Infrastructure` — GĐ8 gọi lại cho đường tự xóa.
- **Không tự khóa chính mình:** `lock` với `target == actor` → 400 *"Không thể tự khóa tài khoản của mình."* (khóa xong thì
  không còn phiên để mở lại). Tự hạ vai trò thì được, miễn còn Admin khác — bất biến lo phần còn lại.
- Test `ADM-C1` (Mục 10.2) chạy tình huống X hạ Y ‖ Y hạ X **20 lần liền**.

### Đ-6.8 Endpoint quản trị và kiểm duyệt **fail-closed** khi không kiểm được thu hồi; mọi endpoint khác giữ fail-open. Giữ `iat`, không đổi sang "phiên bản bảo mật"

Hai việc GĐ1 D8 ghi lại *"chốt trước khi GĐ6 viết bên ghi"*:

**C1 — fail-closed có chọn lọc.** GĐ1 chọn fail-open khi Redis chết (uptime quan trọng hơn, cửa sổ ≤ 15 phút). Chấp nhận được
cho "đọc feed", **không** chấp nhận được cho "Admin vừa bị hạ quyền vẫn khóa được tài khoản người khác trong 15 phút Redis
hỏng". Chốt:

- Controller của `admin-v1`, `moderation-v1` (trừ `POST /reports`) mang metadata `[PrivilegedEndpoint]` — **một** attribute gộp
  cả fail-closed lẫn "ghi audit khi bị từ chối" (Đ-6.15), để không ai gắn thiếu một nửa (Mục 6.1).
- `OnTokenValidated` đọc metadata của endpoint: có metadata **và** kho thu hồi không trả lời được → `503` Problem Details type
  `urn:socialapp:problem:revocation-unavailable`. Không metadata → fail-open như cũ.
- `ITokenRevocationStore` thêm `CheckAsync(sub, iat) → Revoked | NotRevoked | Unknown` (chỉ-thêm); `IsRevokedAsync` giữ nguyên
  hành vi để không đụng code GĐ1 và filter hub của GĐ5.

**C2 — "phiên bản bảo mật" thay `iat`: cân nhắc rồi loại (cho GĐ6).** Ưu điểm có thật (bỏ độ phân giải giây, bỏ phụ thuộc đồng
hồ giữa các instance). Nhưng: đổi hợp đồng token giữa lúc B đang dựng vé realtime **lưu `iat`** (Đ-5.9) — đổi là đụng code
đang viết của người khác; staging một VM một đồng hồ nên rủi ro lệch giờ chưa tồn tại; khe dưới 1 giây GĐ1 đã chấp nhận.
Ghi lại "xem lại khi GĐ7 lên hai instance trên hai máy" ở Mục 14.

### Đ-6.9 Vai trò: `code` bất biến và **không bao giờ** nhận qua API; thêm đúng một mã quyền `role.manage`; ba lớp chặn cho vai trò hệ thống

Thiết kế GĐ1 đã hứa (PTTK 6.7.2 "nâng cấp là thay dữ liệu, không thay code"); GĐ6 là lúc hứa thành thật.

| Thao tác | Endpoint | Tầng 2 | Luật |
|---|---|---|---|
| Liệt kê vai trò + quyền + số người mang | `GET /admin/roles` | `role.manage` | ADMIN hiện `effectivePermissions` = tất cả, `editable: false` |
| Tạo | `POST /admin/roles` | `role.manage` | `code` `^[A-Z][A-Z0-9_]{2,29}$`, duy nhất, không trùng ba mã hệ thống; `permissions` là tập mã có thật |
| Đổi tên | `PATCH /admin/roles/{roleId}` | `role.manage` | **Chỉ** nhận `displayName`; body có `code` → 400 (không phải lờ đi — GĐ1 Mục 3.1) |
| Sửa tập quyền | `PUT /admin/roles/{roleId}/permissions` | `role.manage` | Thay **cả tập** (idempotent); ADMIN → 409; USER/MODERATOR → cần `confirm: true` và **không được về 0 quyền** |
| Xóa | `DELETE /admin/roles/{roleId}` | `role.manage` | Ba vai trò hệ thống → 409; còn người mang → 409 (dịch từ FK `RESTRICT`, không để 500) |
| Liệt kê quyền | `GET /admin/permissions` | `role.manage` | 18 mã + `description` |

**Mã quyền thứ 18 `role.manage` — lệch ma trận PTTK (17 mã).** "Gán vai trò cho người" (`role.assign`) và "định nghĩa vai trò
là gì" là hai quyền khác bậc: một vai trò "Nhân sự" gán được người vào MODERATOR không có nghĩa được sửa MODERATOR có những
quyền gì. Chỉ ADMIN có (short-circuit — không dòng `role_permissions` nào). Seeder tự chèn dòng `permissions` thứ 18 vì đọc
`PermissionCodes.All` (`DO NOTHING`); **không** thêm vào bộ bootstrap của USER/MODERATOR. *(Sửa 2026-09-23, A3: seeder chèn kèm
mô tả — xem Mục 4 phần Identity.)*

**Vì sao USER/MODERATOR không được về 0 quyền** — ranh giới GĐ1 Mục 5.4: seeder chỉ bootstrap vai trò **chưa có dòng nào**, nên
vai trò đã seed bị gỡ hết quyền trông y như vai trò chưa từng seed → lần deploy sau **được cấp lại đủ bộ mặc định**, âm thầm.
Chặn ở API (400 `admin.role-needs-permission`) rẻ hơn bảng `seed_history`. Vai trò tự tạo thì về 0 được (seeder không biết nó).

**Xác nhận khi sửa USER hoặc MODERATOR** (kế hoạch gốc: *"gỡ nhầm `post.create` là cả hệ thống thành read-only"*): không có
`confirm: true` → **409** `admin.confirmation-required` kèm `added[]`, `removed[]`, `affectedUsers` (số người mang vai trò). FE
hiện đúng các con số đó trong hộp thoại rồi gửi lại. Server là chỗ bắt buộc, không phải FE — gọi thẳng API cũng phải qua bước này.

**Ba lớp chặn cho vai trò hệ thống**, từ trên xuống: API không nhận `code` · service trả 409 khi xóa/sửa quyền ADMIN · **trigger
DB** `trg_roles_protect_system` (`BEFORE UPDATE OF code OR DELETE ON identity.roles`, `IF OLD.code IN ('ADMIN','USER','MODERATOR')
THEN RAISE`). GĐ1 Mục 5.5: trigger *"chỉ đáng làm khi GĐ6 đã có endpoint sửa vai trò thật"* — đã tới lúc. Nó chặn cả `psql` gõ tay,
thứ kiểm tra lúc khởi động chỉ bắt được ở lần restart sau.

### Đ-6.10 Cache quyền: xóa ngay ở instance xử lý + phát Redis pub/sub cho instance khác; TTL 60 giây là lưới cuối

`PermissionCache` (SharedKernel) là `ConcurrentDictionary` **trong bộ nhớ từng instance**, TTL 60 giây, *"không đẩy invalidate
(GĐ1 chưa sửa quyền lúc runtime — GĐ6)"*. Kế hoạch gốc: *"không có thì Admin sửa xong phải chờ TTL 60s, triệu chứng nhìn rất
giống bug"*.

- `IPermissionCache` thêm `Invalidate(string roleCode)` và `InvalidateAll()` (chỉ-thêm).
- Sau `COMMIT` của mọi thao tác sửa `role_permissions` hoặc xóa vai trò: gọi `Invalidate` tại chỗ **rồi** `PUBLISH
  socialapp:{env}:authz:permissions-changed {roleCode}`. Mỗi instance có một `BackgroundService` `SUBSCRIBE` kênh đó và gọi
  `Invalidate`.
- Redis chết → không phát được → instance khác thấy thay đổi sau ≤ 60 giây (TTL cũ). Không 503: đây là **thêm** quyền hoặc
  **bớt** quyền của cả một vai trò, không phải khẩn như hạ quyền một kẻ phá hoại (thứ đó đi qua `revoked:user`, Đ-6.6).
- Kênh có tiền tố môi trường, cùng lý do `ChannelPrefix` của backplane GĐ5 (Đ-5.13): staging và production chung một Redis
  không được nghe lẫn nhau.
- Test `PERM-01`: sửa quyền → request kế tiếp (không tua `TimeProvider`) thấy quyền mới. `PERM-02`: hai `WebApplicationFactory`
  dùng chung Redis thật → sửa ở factory 1, factory 2 thấy trong ≤ 1 giây.

### Đ-6.11 Frontend biết **quyền hiệu lực**, không chỉ vai trò: `GET /me` thêm `permissions` — mở lại `identity-v1` chỉ-thêm

Vai trò giờ là dữ liệu mở: FE không thể suy "hiện nút Kiểm duyệt" từ `role === "MODERATOR"` — một vai trò `REVIEWER` tự tạo
có `report.resolve` cũng phải thấy hàng đợi. Chốt:

- `MeResponse` thêm `permissions: string[]` — quyền hiệu lực đọc từ **DB** (cùng nguồn với `role`, không từ claim), ADMIN =
  cả 18 mã. Chỉ-thêm vào `identity-v1.yaml` (đóng băng từ GĐ1) ở cổng mở GĐ6.
- FE dùng nó **chỉ để vẽ**: ẩn/hiện liên kết, chặn route bằng guard mềm. Server vẫn là nơi chặn thật — test FE không bao giờ là
  bằng chứng phân quyền.
- FE nạp lại `/me` khi cửa sổ lấy lại focus và khi vào một route cần quyền. Đó là cách "Admin nâng quyền X" hiện ra trên màn
  của X mà X không phải tải lại trang — **bằng chứng UI** mà kế hoạch gốc đòi (mốc 1).

### Nhóm III — Kiểm duyệt

### Đ-6.12 Báo cáo: phải **thấy được** đối tượng mới báo được; mỗi người một báo cáo mở cho mỗi đối tượng; gửi trùng trả lại báo cáo cũ

`POST /reports { targetType: post|comment|user, targetId, reasonCode, detail? }` — tầng 2 `report.create`.

| Luật | Hiện thực | Vì sao |
|---|---|---|
| Không thấy được đối tượng → **404**, cùng phản hồi với "không tồn tại" | Bài: `PostVisibility` qua `IModerationTargets.GetSnapshotsAsync` + `IFriendshipReader`. Bình luận: BR-02 của bài chứa nó (`PostAccess` của GĐ3). Người dùng: có hồ sơ | Không thì `POST /reports` thành **máy dò** "bài riêng tư id X có tồn tại không" — IDOR theo chiều đọc (quy ước 3b GĐ1) |
| Không tự báo cáo nội dung / tài khoản của mình → 400 | So `authorId` trong snapshot | Vô nghĩa, và làm bẩn hàng đợi |
| Một báo cáo **mở** cho mỗi `(reporter, target_type, target_id)` | Index duy nhất **một phần** `WHERE status = 'open'` + `INSERT … ON CONFLICT DO NOTHING` rồi `SELECT` | Bấm hai lần, hai tab → một báo cáo. Gửi trùng → **200** + báo cáo cũ (PTTK API-Reports "201/200"); mới → 201 |
| Báo lại sau khi báo cáo cũ đã xử lý → báo cáo mới | Index một phần không chặn dòng `resolved`/`dismissed` | Nội dung bị khôi phục rồi vi phạm lại vẫn báo được |
| `reasonCode = other` bắt buộc `detail` (1–500 ký tự) | Validator | Moderator cần biết "khác" là gì |
| Rate limit riêng: 10 báo cáo / phút / người | Policy `report-create` cạnh `auth` | Chặn một người xả rác hàng đợi — rủi ro "Nội dung độc hại/spam" PTTK Mục 6.7.4 |

`reasonCode` là CHECK (`spam, harassment, nudity, violence, other` — đúng PTTK ENT-12), **không** bảng tham chiếu: PTTK Mục 5.7
nói "reason_code seed bằng migration", nhưng năm giá trị cố định mà FE hiển thị bằng nhãn tiếng Việt thì một bảng chỉ thêm một
join. Thêm lý do mới = migration đổi CHECK + chỉ-thêm enum trong hợp đồng.

Người báo cáo **không** được xem lại báo cáo của mình ở MVP (không endpoint `GET /reports/mine`) — không FR nào đòi.

### Đ-6.13 Xử lý báo cáo: một quyết định, một lần, gộp cả đối tượng; `hide` cần **thêm** `post.hide` ngoài `report.resolve`

`PATCH /reports/{reportId} { decision: "hide" | "dismiss" | "resolve", note? }` — tầng 2 `report.resolve` (TC-A06).

```
BEGIN                                                         -- ModerationDbContext
 1. SELECT … FROM moderation.reports WHERE id = @rid FOR UPDATE
      không có → 404 · status <> 'open' → 409 moderation.already-decided (US-019 AC-04)
 2. decision = hide:
      tầng 2 thứ hai: IPermissionCache có post.hide cho vai trò người gọi? không → 403   (Đ-6.9: REVIEWER chỉ có report.resolve)
      IModerationTargets.HideAsync(tx, target, reasonCode)     -- Content tự UPDATE bảng của mình, TRÊN tx này (Đ-6.3)
        NotFound → 409 moderation.target-gone · AlreadyHidden → vẫn đi tiếp (báo cáo thứ hai cho bài đã ẩn)
 3. UPDATE moderation.reports SET status = <resolved|dismissed>, resolver_id, resolved_at, resolution_note
     WHERE target_type = @t AND target_id = @id AND status = 'open'      -- ĐÓNG MỌI báo cáo mở của cùng đối tượng
 4. IAuditTrail.AppendAsync(tx, { action: report.hide|report.dismiss|report.resolve, target, metadata: {reportIds, reasonCode, note} })
COMMIT
 5. SAU COMMIT: Publish ContentHidden(…) → thông báo tác giả (UC-19 bước 4 "thông báo tác giả kèm lý do")
```

- **Gộp theo đối tượng ở bước 3**: mười người báo một bài → một quyết định đóng cả mười. Không thế thì Moderator bấm "ẩn" mười
  lần, lần hai trở đi `AlreadyHidden`, và hàng đợi trông như chưa ai làm gì.
- **Hàng đợi hiển thị theo đối tượng**, không theo từng báo cáo: `GET /reports?status=open` trả mỗi đối tượng một dòng kèm
  `reportCount`, `reasons` (đếm theo `reasonCode`), `firstReportedAt` — `reportId` đại diện là báo cáo mở cũ nhất.
- `decision` hợp lệ theo loại đối tượng: `hide` cho `post`/`comment`; `resolve` **chỉ** cho `user` và bắt buộc `note` (Moderator
  không có `user.lock`; báo cáo người dùng được đóng sau khi Admin đã khóa, hoặc kèm ghi chú đã xử lý ngoài luồng); `dismiss`
  cho mọi loại. Sai cặp → 400.
- **Khôi phục** (FR-020 "ẩn/khôi phục"): `POST /moderation/targets/{targetType}/{targetId}/restore { note }` — tầng 2 `post.hide`;
  `RestoreAsync(tx)` + audit `content.restore` trong một transaction. Không mở lại báo cáo đã đóng.
- Hai Moderator bấm cùng lúc: bước 1 khóa dòng → người sau thấy `resolved` → 409. Test `MOD-C1`.
- Người báo cáo **không** được thông báo kết quả ở MVP (không FR nào đòi; và nói "bài bạn báo đã bị ẩn" cho người khác là lộ
  quyết định kiểm duyệt về tác giả).

### Đ-6.14 BR-07 phía người đọc: tác giả thấy bài bị ẩn **kèm lý do**, người khác 404; bình luận `hidden` giữ nhánh như `deleted`

GĐ4 (Đ-4.11) đã chặn `hidden` ở feed và trang cá nhân, và để lại đúng một việc: *"đường đọc một bài (`GET /posts/{id}` cho tác
giả xem bài bị ẩn của mình) là việc của GĐ6"*.

| Người gọi | `GET /posts/{id}` của bài `hidden` | Trang cá nhân / feed |
|---|---|---|
| Tác giả | **200** + `moderation: { status: "hidden", reasonCode, hiddenAt }` | Không hiện (giữ Đ-4.11) |
| Người khác, kể cả bạn bè | **404** — cùng phản hồi với bài không tồn tại | Không hiện |
| Moderator / Admin | **404** ở endpoint này; xem qua `GET /reports/{id}` (ảnh chụp đối tượng) | Không hiện |

- `PostResponse` thêm `moderation?: PostModeration` — **chỉ-thêm** vào `content-v1`, `null`/vắng với mọi bài `published`. Cùng
  một mapper, nhánh `hidden` chỉ đi được khi `actorId == authorId` (luật "mapper là chỗ duy nhất" của GĐ3 Mục 8.1).
- Tác giả **không** sửa được bài bị ẩn (`PATCH` → 409 `content.post-hidden`): sửa nội dung rồi "tự gỡ ẩn" là lách kiểm duyệt.
  Xóa thì được — người dùng luôn có quyền xóa nội dung của mình.
- Moderator không đọc bài qua `GET /posts/{id}`: nhìn thấy nội dung vi phạm là việc của **luồng kiểm duyệt**, có ngữ cảnh báo
  cáo, không phải một đường vòng quanh BR-02.

**Bình luận bị ẩn** (khi GĐ3 đã có): `comments.status` thêm giá trị `hidden` — **A đưa vào CHECK từ migration đầu tiên** (thỏa
thuận ở cổng mở, Đ-6.4), và mapper của A đi chung nhánh với `deleted` nhưng hiện *"Bình luận đã bị ẩn do vi phạm tiêu chuẩn"*;
nhánh con giữ nguyên (BR-08). Tác giả bình luận thấy nội dung của mình kèm lý do — cùng luật với bài. Nếu A đã merge mà chưa có
`hidden`: GĐ6 viết migration `ALTER … DROP/ADD CONSTRAINT` (chỉ mở rộng tập giá trị, không đổi dòng nào).

### Đ-6.15 Nhật ký kiểm toán: append-only do **DB** giữ; ghi cả lần bị từ chối; không bao giờ chứa nội dung người dùng

PTTK ENT-13 "append-only; không UPDATE/DELETE", Mục 5.7 "mọi thao tác Mod/Admin ghi audit_logs (ai, hành động, đối tượng, IP,
thời điểm)", US-019 AC-03 "403 (default deny) + audit".

**Append-only bằng trigger, không bằng quy ước:** `trg_audit_logs_append_only BEFORE UPDATE OR DELETE` → `RAISE EXCEPTION`,
**trừ** khi phiên đặt `SET LOCAL socialapp.audit_purge = 'on'` (cửa cho job xóa 12 tháng của GĐ8 — chỉ xóa, không bao giờ
sửa). Ứng dụng dùng một user DB là chủ bảng nên `REVOKE` không có tác dụng; trigger chặn mọi code vô tình, và muốn lách thì phải
`DROP TRIGGER` — một thao tác lộ liễu, không phải một dòng code lẫn trong PR.

*Sửa 2026-09-23 khi thi công A1* (L-A6 của `huong-dan-khoi-a-c-nen-du-lieu-va-ha-tang.md`): thêm `trg_audit_logs_no_truncate BEFORE
TRUNCATE … FOR EACH STATEMENT` cùng hàm — trigger mức dòng không chạy khi `TRUNCATE`, nên thiếu nó thì một lệnh dọn DB chép nhầm
sang staging xóa sạch nhật ký.

**Danh sách `action`** (hằng trong `SharedKernel.Audit.AuditActions`, test canh không trùng):

*Sửa 2026-09-23 khi thi công A1* (L-A5): hằng ở **SharedKernel**, không ở `Moderation.Domain` — người ghi audit là Identity
(`user.*`, `role.*`) và SharedKernel (`access.denied`, handler C4), cả hai không được tham chiếu Moderation (ADR-001).

| Nhóm | `action` | `target_type` | `metadata` (không bao giờ có nội dung bài/bình luận/tin nhắn) |
|---|---|---|---|
| Kiểm duyệt | `report.hide` · `report.dismiss` · `report.resolve` · `content.restore` | `post` · `comment` · `user` | `reportIds[]`, `reasonCode`, `note` (≤ 500, của Moderator) |
| Tài khoản | `user.lock` · `user.unlock` · `role.assign` | `user` | `fromRole`, `toRole`, `revocation: applied\|deferred` |
| Vai trò | `role.create` · `role.rename` · `role.permissions` · `role.delete` | `role` | `code`, `added[]`, `removed[]`, `confirmed` |
| Truy cập | `access.denied` | `endpoint` | `method`, `routeTemplate` (không query string, không id trên đường) |

**Ghi cả lần bị từ chối (AC-03):** `IAuthorizationMiddlewareResultHandler` riêng (SharedKernel) — khi tầng 2 từ chối một endpoint
mang `[PrivilegedEndpoint]` (mọi controller của `admin-v1`, `moderation-v1` trừ `POST /reports` — Đ-6.8), gọi `IAuditTrail.AppendAsync(tx: null,
access.denied)` rồi trả 403 như cũ. Chống ngập: mỗi `(actor, routeTemplate)` tối đa **một dòng mỗi phút** (`SET NX EX 60` trên
Redis; Redis chết thì vẫn ghi — rate limit chung 100/phút là trần).

**IP** lấy từ `HttpContext.Connection.RemoteIpAddress` **sau** `ForwardedHeaders` (đã cấu hình từ GĐ1 — IP người dùng, không
phải của apache). IP là dữ liệu cá nhân → nằm trong danh sách che log của GĐ7 (Đ-7.14), giữ 12 tháng cùng bảng.

**Ai đọc:** `GET /admin/audit-logs` — `audit.read`, **chỉ ADMIN** theo ma trận PTTK. Kế hoạch gốc ghi "màn kiểm duyệt … xem
audit": hiểu là Moderator thấy **lịch sử xử lý của chính đối tượng đang xem** (ai đóng, lúc nào, quyết định gì — đọc từ
`reports`, không từ `audit_logs`), không phải nhật ký toàn hệ thống. Ghi vào Mục 13.

### Nhóm IV — Thông báo

### Đ-6.16 Gộp theo `group_key`; mỗi **đợt** đếm số người khác nhau; không lưu tên hay nội dung — hydrate lúc đọc

PTTK: `notifications` có `UQ(recipient, group_key)` để gộp, `is_read`, index một phần `WHERE is_read = false` cho badge.

```
Event tới (vd ReactionSet: Bình thả tim bài P của An)
  → handler: nếu actor == recipient → bỏ (không tự báo mình)
  → BEGIN
      INSERT INTO notification.notifications (recipient_id, group_key, type, target…, last_actor_id, actor_count = 1, is_read = false, …)
      ON CONFLICT (recipient_id, group_key) DO UPDATE
         SET last_actor_id = EXCLUDED.last_actor_id,
             updated_at    = EXCLUDED.updated_at,
             is_read       = false,
             -- ĐỢT MỚI khi nhóm đã đọc: đếm lại từ 1
             actor_count   = CASE WHEN notifications.is_read THEN 1 ELSE notifications.actor_count END
      RETURNING id, (xmax = 0) AS inserted, <is_read cũ>
      nếu nhóm vừa chuyển từ đã đọc sang đợt mới → DELETE notification_actors WHERE notification_id = id
      INSERT INTO notification.notification_actors (notification_id, actor_id) ON CONFLICT DO NOTHING
      nếu thật sự chèn được (người MỚI trong đợt) và không phải dòng vừa tạo → UPDATE … SET actor_count = actor_count + 1
    COMMIT
  → sau COMMIT: đẩy NotificationUpserted qua hub (Đ-6.18)
```

- **Đếm người khác nhau, không đếm lượt:** Bình thả tim, gỡ, thả lại → vẫn "Bình đã bày tỏ cảm xúc", không phải "Bình và 2
  người khác". Bảng `notification_actors` (PK cặp) là thứ làm được điều đó mà không phải phình một mảng không giới hạn.
- **Đợt:** đã đọc rồi mới có người thả tiếp → nhóm sáng lại và đếm lại từ người mới, đúng cách người dùng hiểu "có gì mới".
- **Khóa dòng khi gộp:** `ON CONFLICT DO UPDATE` đã khóa dòng xung đột; phần đếm người mới chạy trong cùng transaction → 20 người
  thả cùng lúc ra đúng một dòng `actor_count = 20` (`NOTIF-C1`). Khuôn "khóa dòng → SQL nguyên tử" GĐ3 để lại.
- **Không lưu `displayName`, không lưu trích đoạn nội dung.** Danh sách thông báo hydrate `last_actor_id` qua `IUserDirectory`
  **một lô** mỗi trang (luật batch Đ-2.3). Người đổi tên thì thông báo cũ hiện tên mới — đúng; và bảng 30 triệu dòng/năm không
  chứa dữ liệu cá nhân nào ngoài id.
- **Không rút lại** khi người kia gỡ cảm xúc hay xóa bình luận. Bấm vào thông báo mà đối tượng đã mất/bị ẩn → màn đích trả 404,
  FE hiện *"Nội dung này không còn nữa."*
- **Không kiểm BR-02 lúc tạo** thông báo cho tác giả (tác giả luôn thấy bài của mình). Có kiểm ở **thông báo trả lời bình
  luận** cho tác giả bình luận cha: người đó đã bình luận được thì đã thấy bài — nhưng bài có thể đổi `privacy` sau đó; bấm vào
  thì BR-02 lúc đọc lo (404). Không nhân bản luật xem vào module thông báo.

### Đ-6.17 Bảng loại thông báo — nguồn event, người nhận, khóa gộp

| `type` | Event (phát bởi) | Người nhận | `group_key` | Có từ khi |
|---|---|---|---|---|
| `comment` | `CommentCreated` (Content — **A**) | Tác giả bài | `comment:post:{postId}` | A merge GĐ3 |
| `reply` | `CommentCreated` có `parentAuthorId` (A) | Tác giả bình luận cha | `reply:comment:{parentId}` | A merge GĐ3 |
| `reaction` | `ReactionSet` với `isNew = true` (A) | Tác giả bài / bình luận | `reaction:{post\|comment}:{id}` | A merge GĐ3 |
| `friend_request` | `FriendRequestSent` (SocialGraph — **đã có**, GĐ4) | Người được mời | `friend_request:{requesterId}` | **Ngay** |
| `friend_accepted` | `FriendRequestAccepted` (SocialGraph — đã có) | Người gửi lời mời | `friend_accepted:{accepterId}` | **Ngay** |
| `message` | `MessageSent` (Messaging — **B**) — **chỉ khi người nhận offline** (`IPresenceReader` của GĐ5) | Người nhận | `message:{conversationId}` | B merge GĐ5 |
| `moderation` | `ContentHidden` (Moderation — GĐ6) | Tác giả nội dung bị ẩn | `moderation:{targetType}:{targetId}` | **Ngay** |
| `tag` | `CommentCreated.mentionedUserIds` (A) | Người được nhắc | `tag:comment:{commentId}` | **Cắt được** (B.10) |

- **Đổi loại cảm xúc không tạo thông báo** (`ReactionSet.isNew = false`): An đổi từ thích sang tim không phải "có gì mới".
- **`message` khi GĐ5 cắt presence** (GĐ5 cho phép cắt C5): handler **không** tạo thông báo `message` nào — badge chưa đọc của
  màn chat (GĐ5 Đ-5.14) đã phủ UC-15 A1. Ghi "hoãn theo GĐ5 C5". Tạo thông báo bất kể online là mỗi tin một cái chuông kêu
  trong khi người ta đang chat.
- Mọi event record nằm ở `SharedKernel/Events/` (Đ-6.2), **chữ ký chốt ở cổng mở của GĐ6**, đưa ra trong PR đường ray (Đ-6.4).
  Trường mà GĐ6 cần từ event của A và B — người nhận phải có sẵn trong event để handler không phải đọc bảng của module khác:

```csharp
record CommentCreated(Guid CommentId, Guid PostId, Guid PostAuthorId, Guid? ParentCommentId, Guid? ParentAuthorId,
                      Guid ActorId, IReadOnlyList<Guid> MentionedUserIds)   // MentionedUserIds rỗng tới khi làm tag
record ReactionSet(ReactionTargetKind TargetType, Guid TargetId, Guid PostId, Guid TargetAuthorId, Guid ActorId, bool IsNew)
record FriendRequestSent(Guid RequesterId, Guid AddresseeId)
record FriendRequestAccepted(Guid RequesterId, Guid AccepterId)
record MessageSent(Guid ConversationId, Guid MessageId, Guid SenderId, Guid RecipientId, long Seq)   // khớp Đ-5.15
record ContentHidden(ModerationTargetType TargetType, Guid TargetId, Guid? PostId, Guid AuthorId, string ReasonCode)
```

*Sửa 2026-09-23 khi thi công C0:* enum của `ReactionSet` tên **`ReactionTargetKind`** (ở `SharedKernel.Events`), không phải
`ReactionTargetType` — `SocialApp.Modules.Content.Domain.ReactionTargetType` đã có, service cảm xúc của A `using` cả hai
namespace là `CS0104`; SharedKernel không được dùng enum của Content (ADR-001), Content ánh xạ bằng một `switch`.
`ModerationTargetType { Post, Comment, User }` đặt ở **`SharedKernel/Moderation/`** — C2 dựng `ModerationTarget`,
`IModerationTargets` cạnh nó mà không phải dời enum. Mọi record là `sealed record … : IIntegrationEvent`.

### Đ-6.18 Realtime: hub `/hubs/notifications` dùng **vé của GĐ5**; trước khi có vé thì hỏi lại 30 giây — và đó cũng là đường lùi vĩnh viễn

GĐ5 Đ-5.9 đặt vé realtime ở `SharedKernel/Realtime/` *"vì GĐ6 dựng hub thông báo dùng đúng vé này"*. Chốt:

- `NotificationHub` ở `Notification/Presentation`, `MapHub<NotificationHub>("/hubs/notifications")`, cùng scheme
  `RealtimeTicket`, cùng `RevocationHubFilter`, cùng tuổi thọ 15 phút, cùng `IUserIdProvider` đọc `sub` — **dùng lại, không viết
  lại** thứ gì của B. apache đã chuyển cả `/hubs/` (GĐ5 Mục 9.6) nên không cần sửa hạ tầng.
- **Chỉ server → client**, không có phương thức client gọi: `NotificationUpserted(NotificationResponse, unreadTotal)` gửi tới
  `Clients.User(recipientId)`. Không phương thức client gọi = không cửa tầng 3 nào phải canh trên hub này.
- **Mỗi tab hai WebSocket** (chat + thông báo), mỗi cái một vé. Đã cân nhắc gộp một hub chung cho cả app: đổi quyết định đã
  chốt của B (`/hubs/chat`, Đ-5.8) và bắt hai module chung một lớp hub — một module sở hữu kết nối của module kia, hoặc hub phải
  lên host. Chi phí của phương án tách là một kết nối nhàn rỗi mỗi tab, trong khi rate limit vé 20/phút (Đ-5.9) còn thừa.
  Nêu lại với B ở cổng mở: nếu B muốn gộp thì B quyết, GĐ6 theo.
- **Trước khi B merge vé** (và mãi mãi khi hub không nối được): FE hỏi `GET /notifications/unread-count` mỗi 30 giây + khi tab
  lấy lại focus; mở chuông thì nạp danh sách. Hợp đồng dữ liệu **không đổi** giữa hai chế độ — chuyển sang hub chỉ là thêm
  một nguồn đẩy, không sửa màn nào.
- Hợp đồng hub viết ở `notification-hub-v1.md` + `notification-hub-v1.examples.json` và có cổng hợp đồng như GĐ5 Mục 8.3.

### Nhóm V — Tìm kiếm

### Đ-6.19 Tìm người: `unaccent` bọc trong hàm `IMMUTABLE` + `pg_trgm` GIN ở Profile; khớp **tiền tố của từng từ**; tối đa 20 kết quả, không cursor

PTTK: *"GIN pg_trgm trên unaccent(display_name), prefix match, q ≥ 2 ký tự"*, API-Search `GET /search?q=&type=` p95 ≤ 700 ms.

**Bẫy thứ nhất — `unaccent()` không dùng được trong index.** Hàm là `STABLE` (phụ thuộc từ điển), Postgres từ chối tạo
index biểu thức trên nó. Cách chuẩn: hàm bọc `IMMUTABLE` gọi **dạng hai tham số** với từ điển ghi rõ schema:

```sql
CREATE EXTENSION IF NOT EXISTS unaccent;      -- trusted extension từ PG13: chủ DB tạo được, không cần superuser
CREATE EXTENSION IF NOT EXISTS pg_trgm;
CREATE FUNCTION profile.search_norm(text) RETURNS text
  LANGUAGE sql IMMUTABLE PARALLEL SAFE STRICT
  AS $$ SELECT lower(public.unaccent('public.unaccent'::regdictionary, $1)) $$;
CREATE INDEX idx_profiles_display_name_search ON profile.profiles
  USING gin (profile.search_norm(display_name) gin_trgm_ops);
```

Truy vấn phải dùng **đúng** biểu thức `profile.search_norm(display_name)` thì mới trúng index — viết `lower(unaccent(…))` ở chỗ
khác là Seq Scan. Kiểm bằng `EXPLAIN` ở A4.

**Bẫy thứ hai — "đ".** `unaccent` bản chuẩn đổi `đ/Đ → d/D`; kiểm bằng test (`SRCH-03`: "duc" khớp "Đức") chứ không tin.

**Khớp gì:** `q` được chuẩn hóa cùng hàm, rồi khớp **tiền tố của bất kỳ từ nào** trong tên: `norm LIKE q || '%' OR norm LIKE
'% ' || q || '%'` — gõ "nguyen" ra "Nguyễn Văn An", gõ "van" cũng ra. Cả hai vế đều trúng GIN trigram. Ký tự `%`, `_`, `\` trong
`q` phải **escape** trước khi ghép (test `SRCH-06`: tìm "%" không trả mọi người).

**Xếp hạng:** tên **bắt đầu** bằng `q` trước, rồi `similarity(norm, q)` giảm dần, rồi `display_name`, rồi `user_id` (ổn định).

**Không cursor — lệch quy ước phân trang của dự án.** Kết quả xếp theo độ khớp thì keyset không có khóa ổn định; và đây là ô
gõ-tới-đâu-ra-tới-đó: người dùng gõ thêm chữ chứ không cuộn trang 3. `limit` mặc định 10, tối đa 20. Ghi vào Mục 13.

**Ràng buộc `q`:** sau `trim`, 2–50 ký tự (50 = độ dài tối đa `display_name`); ngắn hơn → 400 `errors.q` *"Nhập ít nhất 2 ký tự."*
FE chặn trước **cùng ngưỡng** (Đ-E5: client không chặt hơn server). `type` chỉ nhận `user` (mặc định) — giữ chỗ cho `post` sau này.

**Không hiện tài khoản bị khóa/xóa:** Profile không biết `users.status`. Thêm hợp đồng đọc `IAccountStatusReader.GetInactiveAsync(ids)`
ở SharedKernel (batch, chỉ đọc — đúng Đ-2.3), hiện thực ở Identity. Lấy `limit + 5` ứng viên, lọc, cắt còn `limit`. Kết quả thiếu một
hai dòng khi nhiều người bị khóa trùng tên là chấp nhận được với ô tìm kiếm top-20.

**Không trả trạng thái quan hệ** trong kết quả: `IFriendshipReader` chỉ có bản đơn → 20 kết quả là N+1 đúng ở ô gõ phím. Bấm vào
kết quả → trang hồ sơ đã có nút quan hệ (GĐ4).

### Nhóm VI — Frontend

### Đ-6.20 Năm feature mới, chia theo màn; điều hướng theo **quyền hiệu lực**; ghép vào màn của người khác qua slot ở `app/`

| Feature | Chứa | Route |
|---|---|---|
| `features/notification/` | chuông + popover, danh sách, badge, hook hỏi lại/hub | `app/(app)/(with-profile)/notifications/` |
| `features/search/` | ô tìm trong header, trang kết quả | `app/(app)/(with-profile)/search/` |
| `features/report/` | hộp thoại báo cáo (lý do + chi tiết) | — (mở từ menu của bài, bình luận, hồ sơ) |
| `features/moderation/` | hàng đợi, chi tiết báo cáo + ảnh chụp đối tượng, quyết định, khôi phục | `app/(app)/(with-profile)/moderation/`, `moderation/[reportId]/` |
| `features/admin/` | danh sách/chi tiết tài khoản, vai trò + ma trận quyền, nhật ký | `app/(app)/(with-profile)/admin/users/`, `admin/users/[userId]/`, `admin/roles/`, `admin/roles/[roleId]/`, `admin/audit/` |

- Client API: `lib/api/moderation-api.ts`, `notification-api.ts`, `admin-api.ts`; kiểu từ `lib/api/<nhóm>/schema.d.ts` sinh tự động
  (`pnpm gen:api` tự thấy ba file `*-v1.yaml` mới — không sửa script, luật frontend Mục 7).
- **Điều hướng theo quyền:** `lib/auth/permissions.ts` (không biết nghiệp vụ — chỉ `hasPermission(me, code)`); liên kết
  "Kiểm duyệt" hiện khi có `report.resolve`, "Quản trị" khi có bất kỳ `user.lock | role.assign | role.manage | audit.read`. Mỗi
  route quản trị có guard mềm: thiếu quyền → trang "Bạn không có quyền xem trang này" (không redirect im lặng — người vừa bị hạ
  quyền cần hiểu vì sao).
- **Nút "Báo cáo" trên bài của A/GĐ2:** `features/post` không được import `features/report` (Đ-E13). `PostCard` đã/ sẽ có slot
  hành động (GĐ3 Đ-3.13 ghép cảm xúc/bình luận qua slot) → `app/` truyền `ReportMenuItem` vào slot. Cùng cách cho bình luận (khi A
  merge) và hồ sơ (slot `actions` của `PublicProfile`, GĐ4 E5).
- **Biểu ngữ "bài bị ẩn"** trên `post-detail` khi `post.moderation` có mặt: *"Bài viết này đã bị ẩn vì vi phạm tiêu chuẩn cộng
  đồng (lý do: …). Chỉ bạn nhìn thấy."* Nút Sửa ẩn đi; nút Xóa giữ.
- **Header:** chuông + ô tìm vào `AppHeader` qua prop có sẵn (`nav`, `actions`) ở `(app)/layout.tsx` — B cũng thêm badge "Tin
  nhắn" vào cùng chỗ (Mục 9.4).

### Đ-6.21 Màn quản trị không optimistic; màn thông báo optimistic có rollback; mọi 409 phân nhánh theo `type`, không theo câu chữ

- **Quản trị và kiểm duyệt chờ server** rồi mới vẽ: một thao tác bị bất biến Admin chặn (409) hay bị hỏi xác nhận (409
  `confirmation-required`) mà UI đã vẽ "thành công" trước là UI nói dối về quyền.
- **Đánh dấu đã đọc là optimistic** (reducer tuần tự theo đối tượng — di sản GĐ3): chấm xanh tắt ngay, lỗi thì bật lại.
- 409 ở màn quản trị có ba nghĩa khác nhau (`admin.last-admin`, `admin.confirmation-required`, `admin.role-in-use`) → FE phân
  nhánh theo **`type` của Problem Details** — đúng luật frontend Mục 4 "cùng status hai nghĩa phân
  nhánh theo `type`". Mỗi mã có `type` riêng khai trong `admin-v1.yaml`.
- Hộp thoại xác nhận sửa quyền USER/MODERATOR hiện **đúng** số liệu server trả (`affectedUsers`, `removed[]`) — không tự tính.
- Ô tìm kiếm: debounce 300 ms, hủy request cũ bằng `AbortController` tạo **trong effect** (luật frontend #14), không gửi khi < 2 ký tự.

---

## 4. Mô hình dữ liệu

DDL dưới đây là **đích đến**; hiện thực qua EF Core migration trong **module chủ** của bảng. Chỗ EF không biểu diễn được
(trigger, hàm, extension, index biểu thức) viết bằng `migrationBuilder.Sql(...)` trong chính migration đó, kèm comment trỏ về
quyết định. Quy ước thời gian, UUID v7, `updated_at` giữ **nguyên xi** GĐ1/GĐ2.

```sql
------------------------------------------------------------------ Moderation (module mới)
CREATE SCHEMA IF NOT EXISTS moderation;

-- ENT-12 · reports
CREATE TABLE moderation.reports (
    id              uuid         PRIMARY KEY,                           -- UUID v7
    reporter_id     uuid         NOT NULL,                              -- KHÔNG FK (Đ-2.2)
    target_type     varchar(10)  NOT NULL,
    target_id       uuid         NOT NULL,                              -- KHÔNG FK: đa hình, nhiều schema
    reason_code     varchar(20)  NOT NULL,
    detail          varchar(500),
    status          varchar(10)  NOT NULL DEFAULT 'open',
    resolver_id     uuid,                                               -- KHÔNG FK
    resolved_at     timestamptz,
    resolution_note varchar(500),
    created_at      timestamptz  NOT NULL DEFAULT now(),
    updated_at      timestamptz  NOT NULL DEFAULT now(),
    CONSTRAINT ck_reports_target_type CHECK (target_type IN ('post','comment','user')),
    CONSTRAINT ck_reports_reason      CHECK (reason_code IN ('spam','harassment','nudity','violence','other')),
    CONSTRAINT ck_reports_status      CHECK (status IN ('open','resolved','dismissed')),
    CONSTRAINT ck_reports_other_detail CHECK (reason_code <> 'other' OR detail IS NOT NULL),
    CONSTRAINT ck_reports_decided     CHECK ((status = 'open') = (resolved_at IS NULL AND resolver_id IS NULL))  -- một chiều, có người quyết
);
-- Một báo cáo MỞ mỗi (người báo, đối tượng) — Đ-6.12
CREATE UNIQUE INDEX uq_reports_open_per_reporter ON moderation.reports (reporter_id, target_type, target_id) WHERE status = 'open';
-- Hàng đợi: gom theo đối tượng, cũ nhất trước — Đ-6.13
CREATE INDEX idx_reports_open_queue ON moderation.reports (created_at, id) WHERE status = 'open';
-- Đóng mọi báo cáo mở của một đối tượng (bước 3 Đ-6.13) + lịch sử xử lý của đối tượng
CREATE INDEX idx_reports_target ON moderation.reports (target_type, target_id, status);

-- ENT-13 · audit_logs (append-only)
CREATE TABLE moderation.audit_logs (
    id          bigint GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY,    -- PTTK: bigserial (identity = cách chuẩn tương đương,
                                                                        --   Npgsql EF sinh ra — sửa 2026-09-23, A1); khóa keyset
    actor_id    uuid         NOT NULL,                                  -- KHÔNG FK
    action      varchar(50)  NOT NULL,
    target_type varchar(20),
    target_id   uuid,
    metadata    jsonb,
    ip          inet,
    created_at  timestamptz  NOT NULL DEFAULT now()
);
CREATE INDEX idx_audit_logs_actor  ON moderation.audit_logs (actor_id, id DESC);
CREATE INDEX idx_audit_logs_target ON moderation.audit_logs (target_type, target_id, id DESC);
-- Không index theo action: lọc theo action đi kèm khoảng id, bảng nhỏ (thao tác quản trị là "tần suất thấp" — PTTK UC-19)
-- Trigger append-only (Đ-6.15):
--   CREATE FUNCTION moderation.audit_logs_append_only() … IF current_setting('socialapp.audit_purge', true) = 'on'
--        AND TG_OP = 'DELETE' THEN RETURN OLD; END IF; RAISE EXCEPTION 'audit_logs is append-only';
--   CREATE TRIGGER trg_audit_logs_append_only BEFORE UPDATE OR DELETE ON moderation.audit_logs FOR EACH ROW …
--   CREATE TRIGGER trg_audit_logs_no_truncate BEFORE TRUNCATE ON moderation.audit_logs FOR EACH STATEMENT …  (thêm 2026-09-23, A1)

------------------------------------------------------------------ Notification (module mới)
CREATE SCHEMA IF NOT EXISTS notification;

-- ENT-09 · notifications
CREATE TABLE notification.notifications (
    id            uuid        PRIMARY KEY,                              -- UUID v7
    recipient_id  uuid        NOT NULL,                                 -- KHÔNG FK
    type          varchar(20) NOT NULL,
    group_key     varchar(120) NOT NULL,
    target_type   varchar(10) NOT NULL,                                 -- post | comment | user | conversation
    target_id     uuid        NOT NULL,
    post_id       uuid,                                                 -- để FE dẫn tới bài khi đích là bình luận
    last_actor_id uuid,                                                 -- NULL cho type = moderation (không lộ ai kiểm duyệt)
    actor_count   integer     NOT NULL DEFAULT 1,
    reason_code   varchar(20),                                          -- chỉ type = moderation
    is_read       boolean     NOT NULL DEFAULT false,
    created_at    timestamptz NOT NULL DEFAULT now(),
    updated_at    timestamptz NOT NULL DEFAULT now(),                  -- = lúc sự kiện mới nhất dồn vào; khóa sắp danh sách
    CONSTRAINT uq_notifications_group UNIQUE (recipient_id, group_key),                                   -- gộp (FR-018)
    CONSTRAINT ck_notifications_type  CHECK (type IN ('comment','reply','reaction','friend_request','friend_accepted',
                                                      'message','moderation','tag')),
    CONSTRAINT ck_notifications_actor_count CHECK (actor_count >= 1)
);
CREATE INDEX idx_notifications_recent ON notification.notifications (recipient_id, updated_at DESC, id DESC);
CREATE INDEX idx_notifications_unread ON notification.notifications (recipient_id) WHERE is_read = false;     -- badge (PTTK 5.6)

CREATE TABLE notification.notification_actors (
    notification_id uuid NOT NULL REFERENCES notification.notifications(id) ON DELETE CASCADE,   -- cùng schema: FK giữ
    actor_id        uuid NOT NULL,
    PRIMARY KEY (notification_id, actor_id)
);

------------------------------------------------------------------ Identity (migration mới, chỉ mở rộng)
-- permissions.description: cột ĐÃ CÓ từ InitialIdentity (GĐ1) — KHÔNG ADD COLUMN (sửa 2026-09-23 khi thi công A3, L-A1)
UPDATE identity.permissions SET description = … WHERE code = … AND description IS NULL;   -- cho DB đã seed từ GĐ1 (staging)
-- DB MỚI: seeder chèn (permission_id, code, description) từ PermissionCodes.Descriptions, DO NOTHING — migration chạy TRƯỚC
--   seeder nên UPDATE ở trên chạm 0 dòng trên DB mới (sửa 2026-09-23, L-A2 — lệch GĐ1 Mục 2 "không sửa seeder")
-- Mã quyền 18 'role.manage': seeder tự chèn (đọc PermissionCodes.All, DO NOTHING), kèm mô tả
-- Sequence cho vai trò tự tạo (thêm 2026-09-23, L-A4): CREATE SEQUENCE identity.roles_role_id_seq AS smallint START 100
--   (HasSequence trong IdentityDbContext); roles.role_id vẫn gán tay, D5 lấy id bằng nextval
-- Trigger vai trò hệ thống (Đ-6.9):
--   BEFORE UPDATE OF code OR DELETE ON identity.roles FOR EACH ROW
--   WHEN (OLD.code IN ('ADMIN','USER','MODERATOR')) → RAISE EXCEPTION (P0001) khi DELETE hoặc NEW.code IS DISTINCT FROM OLD.code
-- KHÔNG thêm cột nào vào users: status 'disabled' đã có trong ck_users_status từ GĐ1 (Đ-6.5)

------------------------------------------------------------------ Profile (migration mới)
-- extension + hàm IMMUTABLE + index GIN — nguyên văn Đ-6.19

------------------------------------------------------------------ Content — KHÔNG đổi schema posts
-- posts.status 'hidden' + hidden_reason đã có từ GĐ2. comments.status 'hidden' do A đưa vào CHECK (Đ-6.14).
-- hidden_at: KHÔNG thêm cột — PostModeration.hiddenAt đọc updated_at của lần UPDATE ẩn (HideAsync không chạm gì khác);
--   lịch sử đầy đủ nằm ở audit_logs.
```

**Năm chỗ dễ sai trong các migration này:**

1. **`ON CONFLICT` của `reports` phải nêu đúng index một phần:** `ON CONFLICT (reporter_id, target_type, target_id) WHERE status =
   'open' DO NOTHING`. Thiếu vế `WHERE` thì Postgres không suy ra được index nào → lỗi `42P10` lúc chạy, không phải lúc build.
2. **Trigger append-only chặn cả EF:** override `SaveChanges` đóng dấu `updated_at` phải bỏ qua `AuditLog` (bảng không có cột
   đó), và **không** entity nào được `Update(auditLog)`. Test `AUD-02` chứng minh `UPDATE` và `DELETE` đều bị Postgres từ chối.
3. **Hàm `search_norm` phải nằm trong migration của Profile** và extension tạo **trước** hàm. Staging: user DB phải là chủ
   database để tạo trusted extension — kiểm bằng `\dx` sau `--migrate` lần đầu; nếu không, chủ dự án tạo tay một lần (Mục 9.1).
4. **Trigger `roles` phải cho phép `UPDATE display_name`** — chỉ `UPDATE OF code`, không phải mọi `UPDATE`. Test `ROLE-05`
   đổi tên USER được, đổi `code` USER bằng SQL thẳng bị chặn.
5. **Hai migration Identity/Profile chạy trên staging đang có dữ liệu:** chỉ `ADD COLUMN` nullable, `CREATE INDEX`, `CREATE
   FUNCTION/TRIGGER` — không khóa bảng lâu. `CREATE INDEX` GIN trên vài nghìn hồ sơ mất dưới một giây; **không** cần
   `CONCURRENTLY` (EF bọc migration trong transaction, `CONCURRENTLY` không chạy được trong transaction).

**Lệch PTTK ở mô hình (ghi vào Mục 13):** `reports` thêm `resolution_note`, `updated_at`; `notifications` thêm `type`, `target_*`,
`post_id`, `last_actor_id`, `actor_count`, `reason_code` và bảng phụ `notification_actors` (PTTK chỉ tóm tắt "UQ + is_read");
không FK sang `users` (Đ-2.2); `reason_code` là CHECK không bảng tham chiếu (Đ-6.12); `permissions` thêm mã 18.

## 5. Dữ liệu nền

| Việc | Ở đâu | Vì sao |
|---|---|---|
| `PermissionCodes.RoleManage = "role.manage"` thêm vào cuối `PermissionCodes.All` (id 18) | Identity | Seeder chèn dòng `permissions` (`DO NOTHING`); `PermissionCodeUsageTests` canh chính tả mọi `[RequirePermission]`. **Không** thêm vào bootstrap USER/MODERATOR |
| `description` cho 18 mã | Migration Identity (`UPDATE … AND description IS NULL`) **và** seeder (`PermissionCodes.Descriptions`, `DO NOTHING`) | GĐ1 Mục 2: seeder `DO NOTHING` không chạm DB đã seed (staging) — migration điền cho nó. Nhưng migration chạy trước seeder, nên DB mới nhận mô tả từ seeder (*sửa 2026-09-23 khi thi công A3, L-A2*) |
| Hằng quyền cục bộ `ModerationPermissions` (`report.create`, `report.resolve`, `post.hide`, `audit.read`) + `ModerationPermissionsTests` | Moderation + ArchitectureTests | Module không import `Identity.Domain` — chép khuôn `ContentPermissionsTests` |
| `MigrateModerationModuleAsync`, `MigrateNotificationModuleAsync` nối `--migrate`, **sau** SocialGraph và sau Messaging (nếu B đã nối) | `Program.cs` | Thứ tự cố định để log deploy đọc được; hai module mới không phụ thuộc thứ tự dữ liệu với ai |
| **Không seed Admin mới.** Tài khoản ADMIN trên staging đã có từ GĐ1 | — | Test bất biến ≥ 1 Admin tự dựng Admin trong fixture |

---

## 6. Ba tầng kiểm soát truy cập áp vào GĐ6

### 6.1 Bảng đầy đủ: endpoint × tầng 2 × tầng 3 × mã lỗi

| Endpoint | Tầng 2 | Tầng 3 / guard nghiệp vụ | Không đạt |
|---|---|---|---|
| `GET /search` | `[Authorize]` | lọc tài khoản không hoạt động | 400 `q` |
| `GET /notifications`, `GET /notifications/unread-count` | `[Authorize]` | luôn của người gọi | — |
| `POST /notifications/{id}/read` | `[Authorize]` | `recipient_id == actor` | **403** (cùng phản hồi cho "không tồn tại") |
| `POST /notifications/read-all` | `[Authorize]` | chỉ dòng của người gọi | — |
| `POST /reports` | `report.create` | thấy được đối tượng · không phải của mình | **404** · 400 · 429 |
| `GET /reports`, `GET /reports/{id}` | `report.resolve` + ◆ | — | **403** + audit |
| `PATCH /reports/{id}` | `report.resolve` + ◆ | `hide` cần thêm `post.hide` (Đ-6.13) · báo cáo còn `open` | 403 + audit · 404 · **409** |
| `POST /moderation/targets/{type}/{id}/restore` | `post.hide` + ◆ | đối tượng đang `hidden` | 403 + audit · 404 · 409 |
| `GET /admin/audit-logs` | `audit.read` + ◆ | — | 403 + audit |
| `GET /admin/users`, `GET /admin/users/{id}` | `user.lock` **hoặc** `user.unlock` **hoặc** `role.assign` (policy "any-of" — dưới bảng) + ◆ | — | 403 + audit · 404 |
| `POST /admin/users/{id}/lock` | `user.lock` + ◆ | không tự khóa · bất biến Admin | 403 + audit · 400 · 404 · **409** |
| `POST /admin/users/{id}/unlock` | `user.unlock` + ◆ | — | 403 + audit · 404 |
| `PUT /admin/users/{id}/role` | `role.assign` + ◆ | vai trò đích tồn tại · bất biến Admin | 403 + audit · 400 · 404 · **409** |
| `/admin/roles*`, `GET /admin/permissions` | `role.manage` + ◆ | vai trò hệ thống · xác nhận · không về 0 quyền | 403 + audit · 400 · 404 · **409** |
| Bắt tay `/hubs/notifications` | scheme `RealtimeTicket` (GĐ5) | vé hợp lệ · chưa bị thu hồi | 401 |

◆ = `[PrivilegedEndpoint]`: fail-closed khi không kiểm được thu hồi (Đ-6.8) **và** ghi `access.denied` khi tầng 2 từ chối
(Đ-6.15) — một attribute cho hai việc, để không ai gắn thiếu một nửa. Cổng 10.4 #6 canh mọi controller đặc quyền đều có nó.

**Policy "any-of" cho màn danh sách tài khoản:** `[RequirePermission]` hiện nhận **một** mã. Người chỉ có `role.assign` cũng phải
xem được danh sách để gán vai trò. Thêm `[RequireAnyPermission("user.lock","user.unlock","role.assign")]` vào SharedKernel —
cùng `PermissionPolicyProvider`, tên policy mã hóa danh sách (`perm-any:a|b|c`). Chỉ-thêm; `PermissionCodeUsageTests` mở rộng
để đọc cả attribute mới.

### 6.2 Khuôn tầng 2 kép — chép nguyên hình dạng

```csharp
// Moderation/Application/Reports/DecideReportService.cs — tầng 2 thứ HAI, trong service, cho decision = hide
if (request.Decision == ReportDecision.Hide)
{
    // report.resolve đã qua ở attribute. post.hide là quyền RIÊNG (Đ-6.9: vai trò REVIEWER chỉ có report.resolve).
    // Admin short-circuit giữ đúng chỗ của nó: tầng 2 — cùng một hàm IsAllowed của SharedKernel, không tự so "ADMIN" ở đây.
    if (!await _permissions.IsAllowedAsync(actorRole, ModerationPermissions.PostHide, ct))
        return Result.Forbidden();
}
```

`IsAllowedAsync` là hàm SharedKernel **duy nhất** gói "Admin short-circuit + tra cache" — `PermissionHandler` cũng gọi nó. Không
module nào tự viết `role == "ADMIN"` (luật GĐ1 Mục 3.2: đặt nhầm dòng đó xuống tầng 3 là IDOR toàn hệ thống).

### 6.3 Dòng AuthZ matrix mới — chỉ thêm dòng vào `AuthZMatrix.cs`

Khung có sẵn (GĐ1 `B2`/`B3`, `CallerUserId` từ GĐ4). Thêm một khối `// --- GĐ6 ---`, **không** sửa `AuthZMatrixTests`,
`AuthZCase`, `AuthZApiFactory`.

| Id | Kịch bản | Người gọi | Gọi gì | Kỳ vọng |
|---|---|---|---|---|
| `TC-A05` | User thường gọi `/admin/*` | `Caller.User` | `GET /api/v1/admin/users` | **403** |
| `TC-A05-roles` | User sửa quyền một vai trò | `Caller.User` | `PUT /api/v1/admin/roles/{USER}/permissions` | **403** |
| `TC-A05-mod-lock` | Moderator khóa tài khoản | `Caller.Moderator` | `POST /api/v1/admin/users/{id}/lock` | **403** |
| `TC-A05-mod-audit` | Moderator đọc nhật ký | `Caller.Moderator` | `GET /api/v1/admin/audit-logs` | **403** |
| `TC-A05b` | *(đối chứng)* Admin đọc danh sách tài khoản | `Caller.Admin` | `GET /api/v1/admin/users` | **200** |
| `TC-A06` | User thường xử lý báo cáo | `Caller.User` | `PATCH /api/v1/reports/{id}` `{decision: dismiss}` | **403** |
| `TC-A06-queue` | User thường đọc hàng đợi | `Caller.User` | `GET /api/v1/reports` | **403** |
| `TC-A06b` | *(đối chứng)* Moderator bỏ qua báo cáo | `Caller.Moderator` | `PATCH /api/v1/reports/{id}` `{decision: dismiss}` | **200** |
| `REP-IDOR` | A báo cáo bài `private` của B | `Caller.User` | `POST /api/v1/reports` `{post, id bài B}` | **404** |
| `NOTIF-IDOR` | A đánh dấu đã đọc thông báo của B | `Caller.User` | `POST /api/v1/notifications/{id của B}/read` | **403** |
| `TC-A01-notifications` | Thông báo không kèm JWT | `Caller.Anonymous` | `GET /api/v1/notifications` | **401** |
| `TC-A01-search` | Tìm kiếm không kèm JWT | `Caller.Anonymous` | `GET /api/v1/search?q=an` | **401** |

- `TC-A05b`, `TC-A06b` là dòng **đối chứng bắt buộc** (nếp `RBAC-02b`, `READ-06b`, `TC-A07b`): matrix chỉ có dòng "bị chặn" thì
  xanh cả khi handler "chặn mọi người trừ Admin".
- **Vai trò tự tạo không có trong `Caller`** (enum cố định). Test `ROLE-01` (Mục 10.1) dựng vai trò `REVIEWER` qua API rồi kiểm
  "có `report.resolve`, không `post.hide`" — ngoài matrix, cùng category `AuthZ` để nằm trong cổng CI.
- Kiểm **audit khi bị từ chối** (AC-03) không đặt trong matrix (matrix chỉ so status): test `AUD-03` gọi `TC-A06` rồi đọc
  `audit_logs` thấy đúng một dòng `access.denied`.

---

## 7. Luồng nghiệp vụ

### 7.1 Báo cáo một bài (UC-18, FR-019)

```
Menu "…" trên bài → "Báo cáo" → hộp thoại: chọn lý do (5 nút) · "Khác" thì bắt buộc mô tả
  → POST /reports { targetType: post, targetId, reasonCode, detail? }
       201 (mới) | 200 (đã báo, còn mở) → "Cảm ơn bạn. Chúng tôi sẽ xem xét báo cáo này."   (cùng câu cho cả hai)
       404 → "Nội dung này không còn nữa."   · 429 → "Bạn đã gửi quá nhiều báo cáo. Thử lại sau ít phút."
```

Cùng câu cho 201 và 200: người báo không cần biết mình đã báo rồi hay chưa.

### 7.2 UC-19 — Moderator ẩn bài (US-019 AC-01)

```
 1. Moderator mở /moderation → GET /reports?status=open         → mỗi đối tượng một dòng, reportCount, reasons, cũ nhất trước
 2. Chọn một dòng → GET /reports/{id}                              → ảnh chụp đối tượng (IModerationTargets.GetSnapshots)
                                                                     + các báo cáo mở + lịch sử xử lý của đối tượng
 3. "Ẩn nội dung" → chọn lý do (mặc định lý do được báo nhiều nhất) + ghi chú tùy chọn
 4. PATCH /reports/{id} { decision: hide, note }                   → transaction Đ-6.13 (hidden + mọi báo cáo mở → resolved + audit)
 5. 200 → dòng biến khỏi hàng đợi
 6. Sau COMMIT: ContentHidden → thông báo `moderation` cho tác giả (không kèm tên Moderator)
 7. Tác giả bấm thông báo → GET /posts/{id} → 200 + moderation{reasonCode} → biểu ngữ "đã bị ẩn"; người khác → 404
```

AC-02 (bỏ qua) cùng đường, `decision: dismiss`, không bước 6–7. AC-04: bước 4 lần hai → 409. AC-03: User gọi bước 1 hoặc 4 → 403 +
`access.denied` trong audit.

### 7.3 Admin hạ quyền một Moderator — ví dụ đầy đủ (mốc 1)

```
10:00:00  M (MODERATOR) đăng nhập → access (role=MODERATOR, iat 10:00:00) + refresh family F1 — trong Redis phiên BFF
10:07:30  Admin: PUT /admin/users/M/role { roleCode: USER }
            BEGIN · advisory lock (M đang không phải ADMIN, vai trò đích không phải ADMIN → KHÔNG lấy khóa bất biến)
                  · UPDATE users SET role_id = USER · IAuditTrail(tx, role.assign {fromRole, toRole}) · COMMIT
            SET revoked:user:M = 10:07:30 EX 930                              ← SAU COMMIT (Đ-6.6)
            ← 200 { …, revocation: "applied" }
10:07:31  M bấm "Ẩn nội dung" ở tab đang mở → BFF gắn access cũ → API 401 (iat < mốc)
            → BFF refresh single-flight (GĐ1) → RotateAsync đọc vai trò TỪ DB → access mới role=USER
            → BFF gửi lại → API tầng 2: USER không có report.resolve → 403 + audit access.denied
10:07:31  FE nhận 403 trên route /moderation → nạp lại /me → permissions không còn report.resolve
            → liên kết "Kiểm duyệt" biến khỏi header, trang hiện "Bạn không có quyền xem trang này"
          M KHÔNG bị đăng xuất: feed, hồ sơ, chat vẫn chạy.
```

Nâng quyền là cùng đường ngược lại: bước 10:07:31 của người vừa được nâng là **focus lại tab** → `/me` thấy `report.resolve` →
liên kết "Kiểm duyệt" hiện ra, không tải lại trang. **E2E-06 (Mục 10.5) quay đúng cảnh này bằng hai trình duyệt trên staging.**

### 7.4 Admin khóa tài khoản

```
POST /admin/users/X/lock { reason }
  BEGIN · advisory lock nếu X là ADMIN · UPDATE users SET status='disabled'
        · UPDATE refresh_tokens SET revoked_at = now WHERE user_id = X AND revoked_at IS NULL     -- mọi family, mọi thiết bị
        · kiểm bất biến Admin · IAuditTrail(tx, user.lock) · COMMIT
  SET revoked:user:X · (kết nối hub của X chết ở lời gọi kế tiếp hoặc sau ≤ 15 phút — filter + tuổi thọ của GĐ5 Đ-5.10)
X, request kế tiếp: 401 → BFF refresh → 401 (family đã thu hồi + status ≠ active) → BFF xóa phiên → FE anonymous → /login
X đăng nhập lại: mật khẩu đúng → 403 auth.account-disabled "Tài khoản đã bị khóa. Liên hệ quản trị viên."
```

### 7.5 Admin sửa quyền của vai trò USER

```
PUT /admin/roles/1/permissions { permissions: [ …bỏ post.create… ] }
  → 409 admin.confirmation-required { added: [], removed: ["post.create"], affectedUsers: 8421 }
FE: "Gỡ quyền Đăng bài khỏi vai trò Người dùng. 8.421 tài khoản mất quyền này ngay lập tức. Tiếp tục?"
  → PUT … { permissions: […], confirm: true }
  → BEGIN · DELETE/INSERT role_permissions · IAuditTrail(tx, role.permissions {added, removed, confirmed:true}) · COMMIT
  → Invalidate("USER") tại chỗ + PUBLISH authz:permissions-changed USER
  → request kế tiếp của bất kỳ USER nào: tầng 2 tra lại DB → không có post.create → 403
```

### 7.6 Thông báo từ event (FR-018)

```
Bình thả tim bài P của An (A — GĐ3): transaction cảm xúc COMMIT → Publish(ReactionSet{…, IsNew:true})   ← không chờ
  InProcessEventBus: Channel → BackgroundService → scope mới → ReactionNotificationHandler
    → actor ≠ recipient → upsert nhóm reaction:post:P (Đ-6.16) → COMMIT
    → hub có? Clients.User(An).NotificationUpserted(item, unreadTotal) : (FE của An tự thấy ở lượt hỏi lại ≤ 30s)
An mở chuông → GET /notifications → hydrate một lô IUserDirectory → "Bình và 3 người khác đã bày tỏ cảm xúc về bài viết của bạn."
An bấm → POST /notifications/{id}/read (optimistic) → điều hướng /posts/P
```

### 7.7 Tìm kiếm (UC-16)

```
Gõ "ngu" vào ô header → (debounce 300ms) GET /search?q=ngu&type=user&limit=8   → gợi ý dưới ô
Enter → /search?q=ngu → trang kết quả (limit 20)
Server: trim · 2–50 ký tự · escape %_\ · search_norm(q) · LIKE tiền tố từng từ trên GIN · xếp hạng
        · lấy limit+5 · IAccountStatusReader lọc tài khoản không active · cắt limit · ký URL avatar
```

---

## 8. Hợp đồng

Base `/api/v1`. Mọi lỗi REST là RFC 7807 kèm `traceId`. Rate limit chung 100 req/phút/user; `POST /reports` có policy riêng
10/phút/user (Đ-6.12).

**Sáu file hợp đồng chạm tới**, mỗi file mới là một nguồn sự thật **và** một cổng CI:

| File | Module / nhóm Swagger | Mới / mở lại |
|---|---|---|
| `Moderation/Presentation/moderation-v1.yaml` | Moderation · `moderation-v1` | **Mới** |
| `Notification/Presentation/notification-v1.yaml` | Notification · `notification-v1` | **Mới** |
| `Identity/Presentation/admin-v1.yaml` | Identity · `admin-v1` (nhóm thứ hai của Identity, Đ-6.1) | **Mới** |
| `Notification/Presentation/notification-hub-v1.md` + `.examples.json` | Hub, không Swagger | **Mới** |
| `Profile/Presentation/profile-v1.yaml` | thêm `GET /search` | Mở lại **chỉ-thêm** |
| `Identity/Presentation/identity-v1.yaml` | `MeResponse.permissions`; login 403 `auth.account-disabled` | Mở lại **chỉ-thêm** |
| `Content/Presentation/content-v1.yaml` | `PostResponse.moderation?`; `PATCH /posts/{id}` 409 `content.post-hidden` | Mở lại **chỉ-thêm** — **A cũng đang mở file này** (Mục 9.4) |

Tên nhóm phải khớp ở ba chỗ như mọi module: `[ApiExplorerSettings(GroupName=…)]`, `apiGroups` trong `Program.cs`, tên file yaml.
`ContractGateCoverageTests` (GĐ4) sẽ đỏ nếu thiếu `Content Include` hoặc lớp so cho file mới — đó là cổng canh chính nó.

### 8.1 `moderation-v1.yaml`

| Method | Path | Auth | Thành công | Lỗi |
|---|---|---|---|---|
| POST | `/reports` | `report.create` | 201 mới / 200 trùng `ReportReceipt` | 400 · 401 · 404 · 429 |
| GET | `/reports?status=open&cursor=&limit=` | `report.resolve` | 200 `ReportQueuePage` | 400 · 401 · 403 · 503 |
| GET | `/reports/{reportId}` | `report.resolve` | 200 `ReportDetail` | 400 · 401 · 403 · 404 · 503 |
| PATCH | `/reports/{reportId}` | `report.resolve` (+ `post.hide` khi `hide`) | 200 `ReportDecisionResult` | 400 · 401 · 403 · 404 · 409 · 503 |
| POST | `/moderation/targets/{targetType}/{targetId}/restore` | `post.hide` | 200 | 400 · 401 · 403 · 404 · 409 · 503 |
| GET | `/admin/audit-logs?actorId=&action=&targetType=&targetId=&cursor=&limit=` | `audit.read` | 200 `AuditLogPage` | 400 · 401 · 403 · 503 |

```
CreateReportRequest  { targetType: "post"|"comment"|"user", targetId, reasonCode: "spam"|"harassment"|"nudity"|"violence"|"other",
                       detail?: string (1–500; bắt buộc khi other) }
ReportReceipt        { reportId, status: "open", createdAt }                          // KHÔNG trả lại target — không xác nhận gì thêm
ReportQueueItem      { reportId, target: { type, id }, reportCount, reasons: { [reasonCode]: integer }, firstReportedAt }
ReportQueuePage      { items: [ReportQueueItem], nextCursor: string | null }           // firstReportedAt ASC — cũ nhất trước
TargetSnapshot       { type, id, status: "published"|"hidden"|"deleted"|"active"|"disabled",
                       author: UserCard | null, body?: string, media?: [ { url } ], postId?: uuid, createdAt, editedAt? }
ReportDetail         { reportId, target: TargetSnapshot, openReports: [ { reportId, reasonCode, detail?, createdAt } ],
                       history: [ { decision, resolverId, resolvedAt, note? } ] }      // reporterId KHÔNG trả — Moderator không cần biết ai báo
DecideReportRequest  { decision: "hide"|"dismiss"|"resolve", reasonCode?: …, note?: string (≤ 500; bắt buộc khi resolve) }
ReportDecisionResult { decision, closedReportIds: [uuid], targetStatus }
AuditLogItem         { id: integer, actorId, actor: UserCard | null, action, targetType?, targetId?, metadata?, ip?, createdAt }
AuditLogPage         { items: [AuditLogItem], nextCursor: string | null }              // id DESC
```

- `reporterId` **không** lên dây ở `ReportDetail`: Moderator quyết định theo nội dung, không theo người báo — và tránh trả thù.
  Admin cần thì đọc audit/DB.
- Snapshot trả `body` cả khi bài `private`/`friends` hay đã `deleted`: Moderator phải thấy nội dung mới quyết được. Đây là **đường
  duy nhất** Moderator đọc được nội dung không công khai — có dòng matrix `TC-A06-queue` canh cửa vào.
- 409 có ba `type`: `urn:socialapp:problem:report-already-decided`, `…:moderation-target-gone`, `…:moderation-not-hidden`.
- `info.version`: `1.0.0-gd6`.

### 8.2 `admin-v1.yaml` (nhóm thứ hai của Identity)

| Method | Path | Auth | Thành công | Lỗi |
|---|---|---|---|---|
| GET | `/admin/users?q=&status=&roleCode=&cursor=&limit=` | any-of `user.lock`/`user.unlock`/`role.assign` | 200 `AdminUserPage` | 400 · 401 · 403 · 503 |
| GET | `/admin/users/{userId}` | như trên | 200 `AdminUser` | 400 · 401 · 403 · 404 · 503 |
| POST | `/admin/users/{userId}/lock` | `user.lock` | 200 `AdminUserChange` | 400 tự khóa · 401 · 403 · 404 · 409 last-admin · 503 |
| POST | `/admin/users/{userId}/unlock` | `user.unlock` | 200 `AdminUserChange` | 400 · 401 · 403 · 404 · 503 |
| PUT | `/admin/users/{userId}/role` | `role.assign` | 200 `AdminUserChange` | 400 · 401 · 403 · 404 · 409 last-admin · 503 |
| GET | `/admin/roles` | `role.manage` | 200 `[RoleSummary]` | 401 · 403 · 503 |
| POST | `/admin/roles` | `role.manage` | 201 `RoleSummary` | 400 · 401 · 403 · 409 code trùng · 503 |
| PATCH | `/admin/roles/{roleId}` | `role.manage` | 200 `RoleSummary` | 400 · 401 · 403 · 404 · 503 |
| PUT | `/admin/roles/{roleId}/permissions` | `role.manage` | 200 `RoleSummary` | 400 · 401 · 403 · 404 · 409 system/confirm · 503 |
| DELETE | `/admin/roles/{roleId}` | `role.manage` | 204 | 401 · 403 · 404 · 409 system/in-use · 503 |
| GET | `/admin/permissions` | `role.manage` | 200 `[PermissionInfo]` | 401 · 403 · 503 |

```
AdminUser        { userId, email, displayName: string | null, roleCode, roleDisplayName,
                   status: "active"|"disabled", emailVerified: boolean, lockedUntil?: date-time, createdAt }
AdminUserPage    { items: [AdminUser], nextCursor }                  // created_at DESC, user_id DESC · q = tiền tố email (citext)
LockRequest      { reason: string (1–500) }                          // vào metadata audit, không lưu ở users
AssignRoleRequest{ roleCode: string }
AdminUserChange  { user: AdminUser, revocation: "applied" | "deferred" | "not-needed" }     // Đ-6.6
RoleSummary      { roleId, code, displayName, isSystem: boolean, editable: boolean, userCount: integer,
                   permissions: [string] }                           // ADMIN: permissions = cả 18 mã, editable = false
CreateRoleRequest{ code, displayName (1–50), permissions: [string] }
RenameRoleRequest{ displayName }                                     // additionalProperties: false → có `code` là 400
SetRolePermissionsRequest { permissions: [string], confirm?: boolean }
PermissionInfo   { code, description }
ConfirmationRequiredProblem  (409, type …:confirmation-required) { added: [string], removed: [string], affectedUsers: integer }
```

- `isSystem` là **thuộc tính tính ra** (`code IN (ADMIN, USER, MODERATOR)`), không phải cột — đúng quyết định loại bỏ `is_system`
  của GĐ1 Mục 3.4.
- `email` là PII: chỉ trả ở nhóm này, chỉ cho người có quyền quản trị, không bao giờ vào log.

### 8.3 `notification-v1.yaml`

| Method | Path | Auth | Thành công | Lỗi |
|---|---|---|---|---|
| GET | `/notifications?cursor=&limit=` | Bearer | 200 `NotificationPage` | 400 · 401 |
| GET | `/notifications/unread-count` | Bearer | 200 `{ total }` | 401 |
| POST | `/notifications/{notificationId}/read` | Bearer | 204 | 400 · 401 · 403 |
| POST | `/notifications/read-all` | Bearer | 204 | 400 · 401 |

```
NotificationResponse { notificationId, type, actor: UserCard | null, actorCount: integer,
                       target: { type: "post"|"comment"|"user"|"conversation", id, postId?: uuid },
                       reasonCode?: string,                           // chỉ type = moderation
                       isRead: boolean, createdAt, updatedAt }
NotificationPage     { items: [NotificationResponse], nextCursor }    // updated_at DESC, id DESC — nhóm vừa có sự kiện mới nhảy lên đầu
ReadAllRequest       { upTo: date-time }                             // chỉ đánh dấu nhóm có updated_at ≤ upTo — không nuốt thông báo tới sau khi mở
```

- **Câu hiển thị do FE ghép** từ `type` + `actor` + `actorCount` (*"An và 3 người khác đã bình luận về bài viết của bạn."*) — server
  không trả câu, để đổi chữ không phải đổi hợp đồng và không có tên người trong DB (Đ-6.16).
- `unread-count` đếm **nhóm** chưa đọc, không đếm sự kiện. FE hiện "9+" từ 10.
- Cursor keyset trên `(updated_at, id)` nhưng `updated_at` **đổi** khi nhóm có sự kiện mới → một nhóm có thể nhảy lên trang 1
  trong lúc người dùng đang ở trang 2 và không bao giờ hiện lại ở trang 2. Chấp nhận (danh sách thông báo, không phải sổ cái),
  và FE khử trùng theo `notificationId` khi nối trang (khuôn `useCursorPages` GĐ4).

### 8.4 `notification-hub-v1.md`

```
Đường       : /hubs/notifications     Transport : chỉ WebSockets, skipNegotiation (GĐ5 Đ-5.16)     Giao thức : JSON
Xác thực    : ?access_token=<vé> — vé xin qua POST /realtime/tickets (hợp đồng messaging-v1 của GĐ5), dùng một lần
Tuổi thọ    : server cắt sau 15 phút (GĐ5 Đ-5.10); client tự kết nối lại với vé mới
Client → server : KHÔNG có phương thức nào
Server → client : NotificationUpserted { notification: NotificationResponse, unreadTotal: integer }   → Clients.User(recipient)
```

Quy tắc: sự kiện không bảo đảm tới (mất kết nối là mất); `notificationId` là khóa khử trùng; `unreadTotal` là số **tuyệt đối** —
nhận hai lần hay sai thứ tự đều vô hại. Nối lại → nạp lại `unread-count` + trang đầu nếu chuông đang mở.

### 8.5 Mở lại ba hợp đồng cũ — chỉ-thêm

| File | Thêm | Không đổi |
|---|---|---|
| `profile-v1.yaml` | `GET /search?q=&type=user&limit=` → 200 `{ items: [{ userId, displayName, avatarUrl? }] }` · 400 `errors.q` · 401 | Mọi schema GĐ2 |
| `identity-v1.yaml` | `MeResponse.permissions: string[]` (required) · `POST /auth/login` thêm 403 type `…:account-disabled` | Mọi trường đã có; 403 `email-not-verified` giữ nguyên type riêng |
| `content-v1.yaml` | `PostResponse.moderation?: { status: "hidden", reasonCode, hiddenAt }` · `PATCH /posts/{id}` thêm 409 `…:post-hidden` | Mọi trường GĐ2/GĐ3/GĐ4 |

Mỗi lần mở: `info.version` → `…-gd6`, `pnpm gen:api`, commit `schema.d.ts` **cùng commit**, cổng `API contract` + codegen xanh.
Thêm trường `required` vào **response** là chỉ-thêm với client (client cũ bỏ qua trường lạ); vào **request** thì không.

---

## 9. Kế hoạch thi công (một người làm cả hai lane, song song với A — GĐ3 và B — GĐ5)

### 9.1 Điều kiện trước khi bắt đầu

| Cần | Trạng thái 2026-09-23 | Nếu chưa có |
|---|---|---|
| GĐ4 trên `develop` (`SocialGraphEvents`, `IFriendshipReader` thật, slot `actions` của hồ sơ) | **Có** — PR #21 đã merge | — |
| `posts.status 'hidden'` + `hidden_reason` (GĐ2), feed/trang cá nhân đã lọc `hidden` (GĐ4 Đ-4.11) | **Có** | — |
| `ITokenRevocationStore` bên đọc + `OnTokenValidated` (GĐ1 D8) | **Có** | — |
| Bảng `comments`, event `CommentCreated`/`ReactionSet` (A — GĐ3) | **Chưa** — A chưa bắt đầu | Làm mọi thứ khác trước; loại `comment`/`reply`/`reaction` + báo cáo bình luận nối sau khi A merge (Mục 9.3 bước 9) |
| Vé realtime, `RevocationHubFilter`, `IPresenceReader`, event `MessageSent` (B — GĐ5) | **Chưa** | Thông báo chạy bằng hỏi lại 30 giây (Đ-6.18); hub + loại `message` nối sau khi B merge |
| User DB staging là chủ database (tạo được `unaccent`, `pg_trgm`) | **Chưa kiểm** | Kiểm `\dx` + `SELECT current_user, pg_get_userbyid(datdba) FROM pg_database WHERE datname = current_database()` ở bước 2; không phải chủ thì chạy `CREATE EXTENSION` bằng tay một lần trên VM |
| Một tài khoản ADMIN thật trên staging để làm E2E | Có từ GĐ1 (kiểm lại trước F2) | Nâng quyền bằng SQL một lần, **ghi vào biên bản F2** |

### 9.2 Cổng mở — nửa ngày đầu

Làm một mình thì không có buổi họp, nhưng **sản phẩm của cổng mở vẫn bắt buộc** (Mục 0C):

1. Tự rà **Đ-6.1 → Đ-6.21**; sửa cái nào ghi ngày + lý do ngay dưới nó. Ưu tiên rà kỹ bốn chỗ lệch sẽ bị hỏi lúc bảo vệ:
   Đ-6.1 (admin ở Identity), Đ-6.3 (hợp đồng ghi), Đ-6.9 (mã quyền thứ 18), Đ-6.19 (tìm kiếm không cursor).
2. **Báo A và B — một tin nhắn, sáu mục** (chép thẳng từ khung "Sáu quyết định chạm tới người khác" ở đầu tài liệu), và chốt
   với từng người:
   - **A:** chữ ký `CommentCreated`, `ReactionSet` (Đ-6.17) · `hidden` nằm sẵn trong `ck_comments_status` (Đ-6.14) · thứ tự
     thêm trường vào `PostResponse` và `content-v1.yaml` (cả hai cùng mở file — Mục 9.4) · slot hành động trên thẻ bình luận.
   - **B:** chữ ký `MessageSent` (khớp Đ-5.15) · `IPresenceReader` có hay bị cắt · hub thông báo riêng hay gộp (Đ-6.18) ·
     `lib/realtime/` có tách được "tạo kết nối hub theo đường" để GĐ6 dùng lại không.
3. Viết **ba file hợp đồng mới** (`moderation-v1.yaml`, `notification-v1.yaml`, `admin-v1.yaml`) + `notification-hub-v1.md` +
   `.examples.json` → `pnpm gen:api` → commit cả yaml lẫn `schema.d.ts`. Cổng `API contract` **đỏ có chủ đích trên nhánh** tới khi
   có controller — ghi rõ trong commit (nếp GĐ2). Nhánh không vào `develop` cho tới cổng đóng nên không ai khác bị chặn.
4. **Ba hợp đồng cũ (`profile-v1`, `identity-v1`, `content-v1`) KHÔNG sửa ở cổng mở** — bài học GĐ4 Mục 9.2: các file này đã
   có cổng hợp đồng chạy, thêm operation/status chưa hiện thực là `Contract_must_be_fully_implemented` đỏ. Phần thêm của chúng
   đi **cùng commit với controller** (D1, D7, D12). Ghi lại ở đây để không ai "sửa trước cho FE có kiểu".

### 9.3 Thứ tự thi công và ước lượng

Kế hoạch gốc giao GĐ6 cho **2 backend + 1 frontend trong 2 ngày** (Ngày 19–21). Một người làm cả hai lane, năm UC, hai module mới:
khoảng **12–13 ngày làm việc**. Ghi thẳng con số để lịch tổng sửa theo (nếp GĐ4, GĐ5).

| Bước | Việc | Phụ thuộc người khác | Ước lượng | Xong khi |
|---|---|---|---|---|
| 1 | Cổng mở (Mục 9.2) | Chốt với A, B | 0,5 ngày | Hợp đồng mới đã commit; A, B đã trả lời sáu mục |
| 2 | **C0 — đường ray** ⭐ → PR mỏng vào `develop` | — | 0,5 ngày | A, B rebase được và gọi `Publish` (Đ-6.4) |
| 3 | **A1–A5** nền dữ liệu (hai module mới, migration Identity, migration Profile) · **B1** harness | — | 1 ngày | `--migrate` hai lần không đổi gì; `\dn` thấy `moderation`, `notification`; trigger + extension có trên DB test |
| 4 | **C1–C5** hạ tầng chéo module · **B5** test transaction + audit | — | 1,5 ngày | `TX-01/02`, `AUD-02`, `PERM-01/02`, `FC-01` xanh; đã thử cho đỏ |
| 5 | **D1–D5** tài khoản + vai trò · **B2** matrix phần `TC-A05*` (viết đỏ trước) · **B3** `ADM-C*` | — | 2 ngày | Mốc 1 và 2 chứng minh được bằng integration test |
| 6 | **D6–D8** báo cáo, kiểm duyệt, nhật ký + đường đọc `hidden` của Content · matrix `TC-A06*` · `MOD-C1`, `REP-C1` | Chạm `content-v1` cùng A | 1,5 ngày | Mốc 3 chứng minh được; AC-01..04 US-019 xanh |
| 7 | **D12** tìm kiếm | — | 0,5 ngày | `SRCH-*` xanh; `EXPLAIN` trúng GIN |
| 8 | **D9–D11** thông báo: store + gộp + endpoint + handler cho `friend_*` và `moderation` | — | 1 ngày | `NOTIF-*` (trừ loại của A, B) xanh |
| 9 | **Nối event của A và B** khi họ merge: handler `comment`/`reply`/`reaction`, báo cáo + ẩn bình luận, **C6** hub, handler `message` | **A merge GĐ3 · B merge GĐ5** | 1 ngày (chia hai lần) | `EVT-*` với event thật; bình luận ẩn giữ nhánh |
| 10 | **E1–E10** lane frontend | — (hub: B) | 3 ngày | Vitest + Playwright local xanh |
| 11 | **F1–F4** cổng đóng trên staging | Chủ dự án deploy | 1 ngày | Mục 12 tick đủ; E2E-06 có ảnh/quay màn |

**Vì sao đường ray (bước 2) đứng trước cả dữ liệu:** nó là thứ duy nhất trong GĐ6 mà **người khác** chờ. Trễ một ngày ở đây là
A và B viết lớp "chỉ log" rồi GĐ6 phải sửa lại code của họ — đúng thứ Đ-6.4 sinh ra để tránh.

**Vì sao quyền và tài khoản (bước 5) trước kiểm duyệt (bước 6):** bước 5 chứa hai trong ba mốc không lùi được, không phụ thuộc
ai, và dựng sẵn `[PrivilegedEndpoint]` + audit mà bước 6 dùng lại. Bước 6 thì đụng file với A.

**Vì sao backend trước frontend, dù hợp đồng đã chốt:** làm một mình không có song song thật, và rủi ro lớn nhất (đồng thời,
transaction xuyên module, thứ tự DB → Redis) nằm ở backend. Kẹt backend (chờ A/B merge, chờ staging) → chuyển sang một việc
của khối E dựng trên `msw/node`.

### 9.4 Làm song song với A (GĐ3) và B (GĐ5) — chỗ đụng nhau

| Chỗ | Ai đụng | Cách xử |
|---|---|---|
| **Nhánh** | cả ba | GĐ6 làm trên `loveart1210`, `rebase` lên `develop` mỗi khi A hoặc B merge. PR đường ray (bước 2) đi riêng, nhỏ, sớm; **PR khối GĐ6 mở sau** khi đường ray đã merge — không mang theo commit của ai |
| `SharedKernel/Events/` | **GĐ6 tạo** · A, B phát vào | Đường ray (Đ-6.4). Đổi chữ ký event sau khi đã merge = báo cả hai trước, chỉ-thêm tham số có mặc định |
| `SharedKernel/Realtime/` (vé, filter, `IUserIdProvider`, presence) | **B tạo** · GĐ6 dùng | GĐ6 **không sửa** thư mục này. Cần thêm gì → nhờ B, hoặc đợi B merge rồi PR riêng |
| `Program.cs` | cả ba | Mỗi người chỉ thêm dòng của mình (`AddApplicationPart`, `apiGroups`, `Add<X>Module`, dòng migrate, `MapHub`); không sắp lại khối có sẵn; ai merge sau thì rebase. Thứ tự migrate: … → SocialGraph → Messaging → Moderation → Notification |
| `content-v1.yaml` + `lib/api/content/schema.d.ts` | A (bình luận, cảm xúc, `myReaction`) · GĐ6 (`PostResponse.moderation`, 409 `post-hidden`) | GĐ6 thêm **một** commit nhỏ, trường mới đặt **cuối** schema; ai merge sau rebase rồi chạy lại `pnpm gen:api` — **không** sửa tay `schema.d.ts` để gỡ xung đột |
| `PostResponse` / mapper / `PostReadService` | A (`myReaction`) · GĐ6 (`Moderation`, nhánh `hidden`) | Cùng luật: thêm tham số cuối record; A đi trước thì GĐ6 rebase |
| `ck_comments_status` + mapper bình luận | **A** | A đưa `hidden` vào từ đầu (Đ-6.14); GĐ6 chỉ viết `HideAsync` cho bình luận **sau khi** A merge |
| Thẻ bài / thẻ bình luận (slot hành động) | A (GĐ3 Đ-3.13) · GĐ6 (menu "Báo cáo") | GĐ6 chỉ truyền `ReportMenuItem` qua slot ở `app/`; không sửa `features/post` hay `features/comment` |
| `SharedKernelExtensions.cs` (rate limit) | B (`realtime-ticket`) · GĐ6 (`report-create`) | Mỗi người một khối `AddFixedWindowLimiter`, không đổi policy có sẵn |
| `TokenRevocationExtensions` / `OnTokenValidated` | GĐ6 (fail-closed có chọn lọc, Đ-6.8) | Chỉ-thêm `CheckAsync`; `IsRevokedAsync` mà filter hub của B gọi **giữ nguyên hành vi** |
| `AuthZMatrix.cs` | cả ba | Mỗi người **một khối** `// --- GĐx ---` ở cuối mảng; không chèn giữa khối của người khác |
| `PostgresFixture` / `ModulesApiFactory` / `IntegrationTests.csproj` (`Content Include`) | cả ba | Chỉ thêm dòng; thứ tự migrate như `Program.cs` |
| `app/(app)/layout.tsx` (header) | B (badge "Tin nhắn") · GĐ6 (chuông, ô tìm, liên kết theo quyền) | Truyền qua `nav`/`actions` có sẵn; không đổi chữ ký `AppHeader`. Nếu hàng header chật → báo B trước khi đổi bố cục |
| `lib/api/messages.ts`, `lib/api/types.ts` | cả ba | Chỉ thêm ngữ cảnh/alias mới, không sửa cái có sẵn |
| `identity-v1.yaml`, `IdentitySeeder`, `PermissionCodes` | GĐ6 | A, B không đụng — không cần phối hợp |
| Grep log PII (GĐ7 Đ-7.14) | chủ dự án | GĐ6 báo thêm `email` (admin), `ip`, `detail`/`note` (báo cáo) vào danh sách che |

### 9.5 Thư viện cần thêm

| Gói / thành phần | Ở đâu | Ghi chú |
|---|---|---|
| — | backend | **Không thêm gói nào.** `unaccent`, `pg_trgm` là contrib có sẵn trong image `postgres:16` (cả ARM64); Redis pub/sub có trong StackExchange.Redis; event bus dùng `System.Threading.Channels` |
| EF Core + Npgsql 8.0.10 | `SocialApp.Modules.Moderation.csproj`, `…Notification.csproj` | Chép đúng ba dòng của `SocialGraph.csproj` |
| Component shadcn còn thiếu (`dialog`, `popover`, `select`, `table`, `checkbox`, `badge`…) | frontend | `pnpm exec shadcn add <tên>` (bản ghim — luật frontend #4); kiểm `components/ui/` trước, cái nào có rồi thì dùng |

Luật vàng số 8: mọi thứ phải chạy trên ARM64 — không có native binary mới nào.

---

## 10. Chiến lược test

### 10.1 Nghiệm thu chức năng (integration, Postgres + Redis thật qua Testcontainers)

**Tài khoản và thu hồi**

| Id | Kịch bản | Kỳ vọng |
|---|---|---|
| `ADM-01` | Admin khóa X | `status = disabled`; mọi refresh family của X có `revoked_at`; có `revoked:user:X`; token cũ của X → 401; refresh → 401; login đúng mật khẩu → 403 `account-disabled`; login sai mật khẩu → 401 |
| `ADM-02` | Mở khóa X đang `disabled` và đang `locked_until` (FR-003) | Đăng nhập được ngay; `locked_until`, `failed_login_count` về rỗng/0 |
| `ADM-03` | Admin tự khóa mình | 400; không đổi gì |
| `ADM-04` | Hạ vai trò / khóa Admin **cuối cùng** | 409 `last-admin`; DB không đổi; không có key `revoked:user` |
| `ADM-05` | Hạ X từ MODERATOR xuống USER | Token cũ → 401; refresh (cùng family) → 200, token mới `role = USER`; X **không** mất phiên |
| `ADM-06` | Redis không ghi được sau `COMMIT` (store giả ném) | 200 `revocation: deferred`; DB đã đổi; log Error + metric |
| `ADM-07` | `GET /admin/users?q=` tiền tố email, 45 tài khoản, `limit=20` | 20/20/5, keyset ổn định |
| `ME-01` | `/me` của USER, MODERATOR, ADMIN, vai trò tự tạo | đúng tập quyền hiệu lực; ADMIN = 18 mã |
| `FC-01` | Redis dừng | `GET /admin/users` → 503 `revocation-unavailable`; `GET /feed` → 200 (fail-open giữ nguyên) |

**Vai trò và cache quyền**

| Id | Kịch bản | Kỳ vọng |
|---|---|---|
| `ROLE-01` ⭐ | Tạo `REVIEWER` chỉ có `report.resolve`, gán cho R | R: `GET /reports` 200 · `dismiss` 200 · `hide` **403** — **không sửa dòng code nghiệp vụ nào** (câu hỏi phản biện PTTK 6.7.2) |
| `ROLE-02` | `PATCH /admin/roles/{id}` có trường `code` | 400 |
| `ROLE-03` | Xóa ADMIN/USER/MODERATOR · xóa vai trò còn người mang · xóa vai trò tự tạo trống | 409 · 409 · 204 |
| `ROLE-04` | Sửa quyền USER không `confirm` · có `confirm` · về tập rỗng | 409 kèm `removed`, `affectedUsers` đúng · 200 · 400 |
| `ROLE-05` | SQL thẳng: đổi `code` USER · xóa MODERATOR · đổi `display_name` USER | exception · exception · được |
| `ROLE-06` | Sửa quyền ADMIN | 409 |
| `ROLE-07` | Chạy seeder hai lần sau khi đã sửa quyền MODERATOR | Chỉnh sửa còn nguyên; dòng `permissions` 18 có mặt đúng một lần |
| `PERM-01` | Gỡ `post.create` của USER rồi `POST /posts` ngay (không tua đồng hồ) | 403 ở request kế tiếp |
| `PERM-02` | Hai `WebApplicationFactory` chung Redis; sửa ở 1 | 2 thấy quyền mới trong ≤ 1 giây |
| `PERM-03` | *(unit, thêm 2026-09-23)* Lần nạp đang dở khi `Invalidate` | Lần đọc sau về nguồn, không dùng kết quả cũ |
| `ANY-01` | *(thêm 2026-09-23)* Vai trò chỉ có `role.assign` gọi `[RequireAnyPermission]` · USER · ADMIN | 200 · 403 · 200 |

**Báo cáo, kiểm duyệt, nhật ký**

| Id | Kịch bản | Kỳ vọng |
|---|---|---|
| `REP-01` | Báo bài công khai · báo lại lần hai | 201 · 200 cùng `reportId` |
| `REP-02` | Báo bài `private` của người khác · id không tồn tại | 404 · 404 (cùng thân lỗi) |
| `REP-03` | Báo bài của chính mình | 400 |
| `REP-04` | `other` không `detail` | 400 `errors.detail` |
| `REP-05` | Báo cáo thứ 11 trong một phút | 429 |
| `REP-06` | Báo lại sau khi báo cáo cũ đã `dismissed` | 201 báo cáo mới |
| `MOD-01` | AC-01: ba người báo cùng một bài → Moderator `hide` | bài `hidden` + `hidden_reason`; **ba** báo cáo `resolved`; **một** dòng audit `report.hide` có đủ ba `reportIds` |
| `MOD-02` | AC-02: `dismiss` | `dismissed`; bài `published`; có audit |
| `MOD-03` | AC-03: USER gọi `PATCH /reports/{id}` | 403 + một dòng `access.denied` |
| `MOD-04` | AC-04: quyết lần hai | 409 `already-decided` |
| `MOD-05` | `resolve` cho báo cáo bài · `hide` cho báo cáo người dùng · `resolve` thiếu `note` | 400 · 400 · 400 |
| `MOD-06` | Khôi phục bài bị ẩn · khôi phục bài đang `published` | 200 + audit `content.restore` · 409 |
| `TX-01` ⭐ | `IAuditTrail` ném lỗi giữa bước 4 (Đ-6.13) | Bài **vẫn** `published`; báo cáo **vẫn** `open`; không dòng audit |
| `TX-02` | `HideAsync` ném lỗi | Không gì thay đổi |
| `HID-01..06` | Tác giả / bạn bè / Moderator đọc bài `hidden`; tác giả `PATCH`; tác giả `DELETE`; feed + trang cá nhân | 200 + `moderation` · 404 · 404 · 409 · 204 · không có |
| `AUD-01` | Mọi dòng audit có `actor_id`, `action`, `ip` | Không dòng nào chứa chuỗi đánh dấu `SECRET-xyz` đã đặt trong thân bài bị ẩn |
| `AUD-02` | `UPDATE` / `DELETE` dòng audit bằng SQL · `DELETE` với `SET LOCAL socialapp.audit_purge = 'on'` | exception · exception · được |
| `AUD-03` | 5 lần bị từ chối cùng endpoint trong 1 phút | đúng **1** dòng `access.denied` |
| `AUD-03b` | *(thêm 2026-09-23)* 403 ở endpoint không đặc quyền · 401 ẩn danh · 200 được phép | 0 dòng audit |
| `AUD-04` | `GET /admin/audit-logs` lọc theo `actorId`, `action`, `targetId`; 3 trang | keyset `id DESC` đúng, không trùng không sót |

**Thông báo, event, tìm kiếm**

| Id | Kịch bản | Kỳ vọng |
|---|---|---|
| `NOTIF-01` | A mời B · B chấp nhận | B có `friend_request`; A có `friend_accepted` |
| `NOTIF-02` | Tự thả cảm xúc bài mình | Không thông báo |
| `NOTIF-03` | 3 người thả cảm xúc một bài | 1 dòng, `actorCount = 3`, `actor` = người cuối |
| `NOTIF-04` | Cùng một người thả, gỡ, thả lại | `actorCount` vẫn 1 |
| `NOTIF-05` | Đọc nhóm rồi có người mới | nhóm chưa đọc lại, `actorCount = 1` (đợt mới) |
| `NOTIF-06` | `unread-count` với 2 nhóm chưa đọc gồm 7 sự kiện | 2 |
| `NOTIF-07` | `read-all { upTo }` rồi có sự kiện mới sau `upTo` | nhóm mới vẫn chưa đọc |
| `NOTIF-08` | Danh sách không N+1 | số câu SQL không đổi khi trang có 1 hay 20 nhóm (nếp `FEED-Q1`) |
| `NOTIF-09` | Thông báo `moderation` | `actor = null`, có `reasonCode`; không lộ id Moderator ở bất kỳ trường nào |
| `EVT-01` | `Publish` khi chưa có handler | no-op, không lỗi; metric `published` vẫn đếm |
| `EVT-02` | Handler ném lỗi | `Publish` không ném (request gốc không đổi mã); log có tên handler + tên event + số thứ tự phong bì, **không** id hay payload (message lẫn property); event kế tiếp vẫn được xử lý |
| `EVT-03` | Hàng đợi đầy | event rơi, metric `dropped` tăng, **một** dòng cảnh báo (ngưỡng), producer không bị chặn; `DrainAsync` chưa xong khi handler còn chạy |
| `EVT-04` | Hai handler một event, handler 1 ném | handler 2 vẫn chạy; hai handler ở hai scope DI khác nhau |
| `EVT-05` | Dựng bằng `AddInProcessEventBus()` | `IEventPublisher`, `InProcessEventBus`, `IHostedService` là **một** instance |
| `EVT-06` | Gửi → chấp nhận lời mời → gửi lại (409), qua API thật | đúng hai event, **đúng vai** từng id; request 409 không phát gì |
| `EVT-07` | Reflection trên mọi `IIntegrationEvent` | `sealed`; thuộc tính chỉ id/enum/số/cờ, ngoại lệ duy nhất `ContentHidden.ReasonCode` |

*`EVT-01..05`, `EVT-07` là unit test (`InProcessEventBusTests`, `IntegrationEventShapeTests`); `EVT-06` là integration
(`SocialGraphEventsTests`). `EVT-04..07` thêm 2026-09-23 khi thi công C0.*
| `SRCH-01` | "nguyen" | ra "Nguyễn Văn An" |
| `SRCH-02` | "van" | ra "Nguyễn **Văn** An" (tiền tố của từ thứ hai) |
| `SRCH-03` | "duc" | ra "Đức" |
| `SRCH-04` | `q` một ký tự / 51 ký tự / chỉ khoảng trắng | 400 `errors.q` |
| `SRCH-05` | Người bị khóa tên khớp | không có trong kết quả |
| `SRCH-06` | `q = "%%"` · `q = "a_"` | không trả mọi người; ký tự được hiểu theo nghĩa đen |
| `SRCH-07` | `EXPLAIN` truy vấn tìm kiếm | `Bitmap Index Scan on idx_profiles_display_name_search` |
| `SRCH-08` | "an" với "An Bình" và "Bảo An" | "An Bình" đứng trước (tiền tố cả tên) |

### 10.2 Nghiệm thu đồng thời — chạy 20 lần liền trước khi tin

| Id | Kịch bản | Kỳ vọng |
|---|---|---|
| `ADM-C1` ⭐ | Đúng hai Admin X, Y; X hạ Y ‖ Y hạ X | Luôn còn ≥ 1 Admin hoạt động; đúng một 200, một 409 |
| `ADM-C2` | Hai Admin khóa nhau đồng thời | như trên |
| `MOD-C1` | Hai Moderator quyết cùng một báo cáo | một 200, một 409; **một** dòng audit quyết định |
| `REP-C1` | 10 báo cáo giống hệt từ một người, song song | 1 dòng; 1 × 201, 9 × 200; không 500 |
| `NOTIF-C1` | 20 người khác nhau thả cảm xúc một bài song song | 1 dòng, `actorCount = 20`, 20 dòng `notification_actors` |

### 10.3 Unit test

Chuẩn hóa + escape `q` · xếp hạng tìm kiếm (hàm thuần trên danh sách) · `GroupKey.For(event)` cho mọi loại · bảng hợp lệ
`decision × targetType` · `RolePermissionDiff` (added/removed, cần xác nhận hay không, về 0 hay không) · `AuditActions` không trùng
chuỗi · `InProcessEventBus` với handler giả (thứ tự, lỗi, tràn) · ghép câu thông báo phía FE (Vitest, không phải .NET).

### 10.4 Cổng CI phải mở rộng — quên chỗ nào là cổng xanh giả

1. `ModerationContractTests`, `NotificationContractTests`, `AdminContractTests : ContractTestsBase` + ba dòng `Content Include`.
   `ContractGateCoverageTests` (GĐ4) đỏ nếu thiếu — đó là lưới cho chính mục này.
2. `NotificationHubContractTests` + `Content Include` cho `.examples.json` (khuôn `ChatHubContractTests` của GĐ5).
3. `Moderation_Domain_namespace_must_not_be_empty`, `Notification_Domain_namespace_must_not_be_empty` ở `PersistenceBoundaryTests`
   — không có thì `ModuleBoundaryTests` xanh trong chân không.
4. `ModerationPermissionsTests` ở ArchitectureTests; `PermissionCodeUsageTests` đọc cả `[RequireAnyPermission]`.
5. **`WriteContracts_are_only_the_two_named`** (ArchUnitNET): chỉ `SharedKernel.Audit` và `SharedKernel.Moderation` có phương thức
   nhận `DbTransaction` (Đ-6.3).
6. **`Privileged_controllers_carry_the_attribute`** (reflection): mọi action thuộc nhóm `admin-v1`, `moderation-v1` — trừ đúng
   `POST /reports` — mang `[PrivilegedEndpoint]`. Quên một cái là một endpoint quản trị fail-open và không ghi audit khi bị chặn.
7. Test concurrency và `ROLE-01` mang `[Trait("Category","AuthZ")]` nếu chúng canh phân quyền — để nằm trong cổng "AuthZ matrix".

Mọi cổng mới: **thử cho đỏ một lần rồi khôi phục**, `git status` sạch trước và sau (luật frontend Mục 9).

### 10.5 Frontend

- **Vitest:** chuông hỏi lại 30 giây (fake timers) và dừng khi tab ẩn · đánh dấu đã đọc optimistic + rollback · ghép câu theo
  `type` × `actorCount` (1, 2, nhiều) · ô tìm: debounce, hủy request cũ, không gửi dưới 2 ký tự · hộp thoại báo cáo: `other` bắt
  buộc mô tả, 201 và 200 cùng câu · màn kiểm duyệt: 409 `already-decided` làm mới hàng đợi · màn quản trị: 409 phân nhánh theo
  `type` (last-admin / confirmation-required / role-in-use), hộp thoại xác nhận hiện đúng số server trả · điều hướng theo
  `permissions` · biểu ngữ bài bị ẩn · **đúng một ca `<StrictMode>`** cho mỗi màn sở hữu tài nguyên hủy được: chuông (timer hỏi
  lại / kết nối hub) và ô tìm (`AbortController`) — luật frontend Mục 9.
- **Playwright** (local `workers: 1`; dán kết quả vào PR kèm bản Chrome — Đ-E8; chạy lại trên staging ở F2):

| Id | Kịch bản |
|---|---|
| `E2E-01` | A báo bài của C → Moderator ẩn → C thấy thông báo + biểu ngữ; A mở lại link bài → "Nội dung này không còn nữa" |
| `E2E-02` | A mời B → chuông của B tăng (≤ 30 giây khi hỏi lại; ngay lập tức khi có hub) → bấm → tới `/friends` |
| `E2E-03` | Gõ "nguyen" → gợi ý → Enter → trang kết quả → bấm → hồ sơ |
| `E2E-04` | Admin khóa X đang mở tab → tab của X về `/login`; X đăng nhập → câu "Tài khoản đã bị khóa" |
| `E2E-05` | Admin gỡ `post.create` của USER → hộp thoại hiện số tài khoản → xác nhận → tab USER đăng bài → lỗi quyền; Admin trả lại quyền → đăng được, **không** tải lại trang |
| `E2E-06` ⭐ | Hai trình duyệt: Admin nâng X lên MODERATOR → X focus lại tab → liên kết "Kiểm duyệt" hiện, vào được hàng đợi, **không đăng nhập lại**; Admin hạ lại → X bấm một thao tác kiểm duyệt → trang "không có quyền", X vẫn đăng nhập |

### 10.6 Hiệu năng tìm kiếm (PTTK API-Search: p95 ≤ 700 ms)

- **Bắt buộc:** `EXPLAIN (ANALYZE, BUFFERS)` trên DB có **20.000 hồ sơ** giả (script seed dev, tên Việt sinh ngẫu nhiên có dấu) cho
  ba loại `q`: 2 ký tự, 4 ký tự, tiền tố từ thứ hai. Phải thấy `Bitmap Index Scan` trên index GIN, không `Seq Scan`.
- **Cắt được:** k6 50 VU gọi `GET /search` với tiền tố ngẫu nhiên trong 2 phút (khuôn `tests/load/feed/` của GĐ4) → p50/p95/p99.
- Lưu `docs/giai-doan-6/bao-cao-tim-kiem.md`: ngày, dữ liệu, kế hoạch truy vấn, số đo — **kể cả khi không đạt** (nếp báo cáo k6 GĐ4).
- Lưu ý đã biết: `q` 2 ký tự cho trigram rất ít chọn lọc (một trigram khớp nhiều dòng). Nếu p95 vượt ngưỡng ở `q` 2 ký tự: xếp hạng
  chỉ trên 200 ứng viên đầu (`LIMIT` trong CTE) trước khi `ORDER BY similarity` — ghi vào báo cáo, không đổi hợp đồng.

---

## 11. Definition of Done

Theo Mục 3.5 báo cáo:

- [ ] Đủ AC US-019 (AC-01..04) và đủ FR-017..020, mỗi cái có test hoặc bằng chứng E2E trỏ tới
- [ ] Mọi endpoint mới có tầng 2; mọi endpoint chạm tài nguyên có chủ (thông báo, báo cáo) có tầng 3 và dòng matrix; mọi endpoint
      quản trị/kiểm duyệt mang `[PrivilegedEndpoint]` (cổng 10.4 #6 xanh)
- [ ] Lỗi là RFC 7807; mọi 409 có `type` riêng khai trong hợp đồng; không thông điệp nào chứa id, email, nội dung hay tên kiểu
- [ ] Chạy thử trên staging bằng **ba tài khoản thật**: một ADMIN, một người được nâng/hạ, một người thường
- [ ] Swagger ba nhóm mới cập nhật; ba hợp đồng cũ mở lại chỉ-thêm; `notification-hub-v1.md` khớp code
- [ ] Không lộ secret/PII: `email`, `ip`, nội dung báo cáo không vào log; audit không chứa nội dung bài/bình luận/tin nhắn
- [ ] `README.md` Mục 1 cập nhật trạng thái GĐ6 (luật vàng 7); lệch quyết định đã ghi ngược vào tài liệu này

## 12. Checklist nghiệm thu cuối GĐ6

**Dữ liệu (A)**
- [ ] `--migrate` hai lần liên tiếp trên DB sạch: lần hai không đổi gì, exit 0; `\dn` thấy `moderation`, `notification`
- [ ] Staging: `\dx` có `unaccent`, `pg_trgm`; `\df profile.search_norm` là `IMMUTABLE`
- [ ] `UPDATE`/`DELETE` trên `moderation.audit_logs` bằng psql trên staging bị từ chối (chụp lỗi)
- [ ] Đổi `code` của `USER` bằng psql trên staging bị từ chối (chụp lỗi)

**Quyền và tài khoản (C, D)**
- [ ] Mốc 1 — E2E-06 trên staging có ảnh/quay màn: nâng và hạ quyền có hiệu lực ở request kế tiếp, không đăng nhập lại
- [ ] Khóa tài khoản trên staging: tab của người bị khóa về `/login` trong một request
- [ ] Mốc 2 — `ADM-C1`, `ADM-C2` xanh 20 lần liền; `ADM-04` xanh
- [ ] Sửa quyền một vai trò trên staging có hiệu lực ngay (không đợi 60 giây)
- [ ] Tắt Redis trên staging: `/admin/*` trả 503, feed vẫn chạy — **rồi bật lại** (ghi giờ tắt/bật vào biên bản)
- [ ] Tự rà "DB trước, Redis sau" ở **mọi** đường gọi `RevokeUserAsync` (B.10 mục 1) — ghi tên từng hàm đã rà

**Kiểm duyệt (D)**
- [ ] Mốc 3 — `TX-01`, `TX-02` xanh; đã thử cho đỏ (đảo thứ tự: `COMMIT` trước khi ghi audit → `TX-01` đỏ)
- [ ] AC-01..04 US-019 chạy trên staging bằng giao diện, có ảnh
- [ ] Matrix `TC-A05*`, `TC-A06*` xanh trên CI; đã từng đỏ khi bỏ `[RequirePermission]` khỏi một controller quản trị

**Thông báo và tìm kiếm (D, E)**
- [ ] Trên staging: lời mời kết bạn → chuông của người nhận sáng; ba người cùng thả cảm xúc → một dòng "… và 2 người khác" *(nếu A đã merge)*
- [ ] Hub thông báo nối được qua Cloudflare + apache, 1 lần xin vé mỗi lần kết nối *(hoặc ghi "chờ B — đang chạy chế độ hỏi lại")*
- [ ] Tìm "nguyen" trên staging ra tài khoản tên "Nguyễn …"; báo cáo tìm kiếm đã lưu

**Lát cắt dọc (E, F)**
- [ ] Tab Network ở màn quản trị: chỉ `/bff/*`, không `Authorization`, không email người khác trong URL
- [ ] Người thường gõ tay `/admin/users` và `/moderation`: trang "không có quyền", API 403, có dòng `access.denied`

## 13. Sai khác so với kế hoạch gốc và báo cáo v5.0

| # | Báo cáo v5.0 / kế hoạch gốc | Thực hiện | Lý do |
|---|---|---|---|
| 1 | CMP-07 Moderation cung cấp `/admin/*` | `/admin/users*`, `/admin/roles*` ở **Identity** (nhóm `admin-v1`); Moderation giữ báo cáo, kiểm duyệt, audit | Mọi thứ các endpoint đó ghi là bảng của Identity; để ở Moderation là năm hợp đồng ghi thay vì một (Đ-6.1) |
| 2 | Đ-2.3: hợp đồng SharedKernel chỉ đọc | Hai hợp đồng **ghi** có tên: `IAuditTrail`, `IModerationTargets`, nhận `DbTransaction` của người gọi | PTTK đòi ẩn + đóng báo cáo + audit **cùng transaction** qua hai module; mỗi module vẫn là người duy nhất viết SQL vào bảng của mình (Đ-6.3) |
| 3 | Ma trận 6.7.2 có 17 quyền | Thêm `role.manage` (18), chỉ ADMIN | "Gán người vào vai trò" ≠ "định nghĩa vai trò" (Đ-6.9) |
| 4 | API-Admin-Users `GET/PATCH /admin/users/{id}` | `POST …/lock`, `POST …/unlock`, `PUT …/role` | Mỗi thao tác một mã quyền ở tầng 2 bằng attribute; một `PATCH` nhiều trường phải kiểm quyền theo trường trong service |
| 5 | `notifications`: "UQ(recipient, group_key); is_read" | Thêm loại, đích, người cuối, `actor_count`, bảng `notification_actors`; không lưu tên/nội dung | Gộp phải đếm được **người khác nhau** theo đợt; tên hydrate lúc đọc (Đ-6.16) |
| 6 | UC-15 A1 "B offline → tạo notification" | Chỉ khi GĐ5 có presence; cắt presence thì không tạo | Tạo bất kể online là mỗi tin một tiếng chuông (Đ-6.17) |
| 7 | API chung: cursor pagination | Tìm kiếm trả top ≤ 20, không cursor | Xếp theo độ khớp không có khóa keyset ổn định; ô gõ-tới-đâu-ra-tới-đó (Đ-6.19) |
| 8 | `reason_code` "seed bằng migration" | CHECK constraint, không bảng tham chiếu | Năm giá trị cố định; bảng chỉ thêm một join (Đ-6.12) |
| 9 | Kế hoạch gốc: màn kiểm duyệt "xem audit" | Moderator xem **lịch sử xử lý của đối tượng**; nhật ký toàn hệ thống chỉ ADMIN (`audit.read`) | Ma trận PTTK chỉ cho Admin `audit.read` (Đ-6.15) |
| 10 | GĐ2/GĐ5: "không dựng event bus" | Dựng event bus trong tiến trình ở GĐ6; không outbox | Đúng địa chỉ hoãn của GĐ2; outbox đắt hơn cái nó bảo vệ — thứ không được mất đi qua transaction (Đ-6.2) |
| 11 | GĐ1 D8 C2: "cân nhắc phiên bản bảo mật thay `iat`" | Giữ `iat` | Đổi hợp đồng token giữa lúc GĐ5 dựng vé dựa trên `iat` (Đ-6.8) |
| 12 | GĐ1: fail-open khi Redis chết | Fail-closed **chỉ** cho endpoint quản trị/kiểm duyệt | Admin vừa bị hạ không được giữ quyền 15 phút vì Redis hỏng (Đ-6.8, GĐ1 D8 C1) |
| 13 | Kế hoạch gốc: 2 backend + 1 frontend, 2 ngày, sau GĐ5 | Một người, ~12–13 ngày, **song song** GĐ3 (A) và GĐ5 (B) | Nhóm chia GĐ3/GĐ5/GĐ6 cho ba người; GĐ6 tiêu thụ event của hai người kia qua đường ray (Đ-6.4) |

## 14. Rủi ro cần theo dõi

| Mã | Rủi ro | Dấu hiệu sớm | Ứng phó |
|---|---|---|---|
| **R6-01** | A hoặc B trễ → loại thông báo `comment`/`reaction`/`message`, hub, báo cáo bình luận không kịp cổng đóng | A/B chưa merge khi GĐ6 xong bước 8 | Đóng GĐ6 với phần không phụ thuộc; phần phụ thuộc ghi "chờ GĐ3/GĐ5" trong checklist, **không xóa dòng**; nối sau bằng PR nhỏ |
| **R6-02** | A, B không dùng đường ray, viết event riêng | PR của A/B có lớp event tự chế | Đường ray phải tới `develop` **trước** khi A/B viết tới chỗ phát event — bước 2 là việc đầu tiên |
| **R6-03** | Xung đột `content-v1.yaml` / `PostResponse` với A | Rebase đỏ ở `schema.d.ts` | Mục 9.4 — trường thêm ở cuối, sinh lại chứ không sửa tay |
| **R6-04** | Thứ tự DB → Redis bị đảo ở một đường nào đó | Không test nào bắt | Danh sách tự rà B.10 #1 + dòng checklist Mục 12 |
| **R6-05** | Bất biến Admin thủng dưới đồng thời | `ADM-C1` đỏ lúc có lúc không | Khóa tư vấn + đếm **sau** khi ghi (Đ-6.7); không bao giờ "đếm rồi ghi" |
| **R6-06** | Transaction xuyên module không thật sự chung | `TX-01` xanh giả vì hợp đồng tự mở kết nối | Test `TX-01` kiểm **cả ba** trạng thái sau lỗi; code review: hiện thực dùng `tx.Connection`, không `new NpgsqlConnection` |
| **R6-07** | Không tạo được extension trên staging | `--migrate` đỏ ở migration Profile | Kiểm quyền ở bước 3 (Mục 9.1); tạo tay một lần |
| **R6-08** | Sửa nhầm quyền USER làm cả hệ thống read-only | Người dùng báo không đăng bài được | Hộp thoại xác nhận có số tài khoản + audit có `removed[]` để hoàn tác ngay |
| **R6-09** | Ngập audit do bị từ chối liên tục | Bảng `audit_logs` tăng bất thường | Giới hạn 1 dòng/phút/(actor, route) (Đ-6.15) + rate limit chung |
| **R6-10** | Event bus rơi event dưới tải | Metric `dropped` > 0 | Chấp nhận có ý thức (Đ-6.2); nếu gặp ở GĐ8 k6 thì tăng dung lượng hàng đợi, không đổi thiết kế |
| **R6-11** | Mỗi tab hai WebSocket làm vượt rate limit vé khi mạng chập chờn | 429 ở `/realtime/tickets` | Hỏi lại 30 giây là đường lùi; báo B nâng hạn mức nếu cần |
| **R6-12** | `q` 2 ký tự chậm | `SRCH` k6 p95 > 700 ms | Giới hạn ứng viên trước khi xếp hạng (Mục 10.6) |
| **R6-13** | Ước lượng 12–13 ngày trượt | Bước 5 quá 2 ngày | Thứ tự cắt B.10 — quyết định sớm, đừng đợi tới cổng đóng |

---

# Phần B — Kế hoạch triển khai

## B.0 Cách đọc phần này

Phần A nói *cái gì* và *vì sao*. Phần B chia việc thành **sáu khối** — `C0` (đường ray), `A` (dữ liệu), `C` (hạ tầng chéo
module), `D` (endpoint + nghiệp vụ), `B` (test + cổng CI), `E` (frontend), `F` (cổng đóng) — mỗi khối một chuỗi đầu việc có
mã (`A1`, `D5`…). Mã việc đi vào **tiêu đề commit** — `feat(gd6-d): D3 — …` (`.claude/rules/commit-rules.md`: scope
`gd6-<khối>`, tiếng Việt có dấu, ≤ 95 ký tự, có `Test:` và `detect-changes:`, **không** dòng đồng tác giả/ghi công).

Mỗi đầu việc ghi ba thứ: **Làm gì** · **Làm như nào** · **Xong khi**. Một đầu việc xong khi: code chạy · có test tương ứng ·
tài liệu/hợp đồng liên quan sửa **trong cùng commit** · `detect-changes` sạch (hoặc mức rủi ro được giải thích). Không có "xong 90%".

Thứ tự trong mỗi khối là thứ tự phụ thuộc; thứ tự **giữa** các khối theo Mục 9.3.

## B.1 Điểm xuất phát — cái gì đã có sẵn

Kiểm ngày 2026-09-23 trên `loveart1210` (`ac509e4`). GĐ6 **không** dựng lại thứ nào dưới đây:

| Đã có | Ở đâu | GĐ6 dùng để làm gì |
|---|---|---|
| Project `SocialApp.Modules.Moderation`, `…Notification` rỗng (chỉ `.gitkeep`), đã tham chiếu SharedKernel | `src/backend/Modules/` | Vỏ module — thêm `Presentation/`, `DependencyInjection/` |
| 17 mã quyền; `post.hide`, `report.create`, `report.resolve`, `user.lock`, `user.unlock`, `role.assign`, `audit.read` đã seed; MODERATOR có 6 + 13 | `Identity/Domain/PermissionCodes.cs`, `IdentitySeeder` | Tầng 2 của mọi endpoint GĐ6 |
| `UserStatus` đủ bốn giá trị + `ck_users_status`; `locked_until` cho FR-003 | `Identity/Domain/` | `disabled` cho khóa bởi Admin (Đ-6.5) — không migration cột |
| `RefreshTokenStore.RotateAsync` đọc vai trò **từ DB** (join `roles`) | `Identity/Infrastructure/Persistence/` | Hạ/nâng quyền có hiệu lực sau một lần refresh — chỉ thêm điều kiện `status = 'active'` |
| `ITokenRevocationStore` (`RevokeUserAsync` ném khi Redis lỗi, `IsRevokedAsync` fail-open) + `OnTokenValidated` | `SharedKernel/Authentication/` | Bên ghi của Đ-6.6; thêm `CheckAsync` cho Đ-6.8 |
| `PermissionCache` (TTL 60 s, trong bộ nhớ), `IRolePermissionSource`, `PermissionHandler`, Admin short-circuit, `SystemRoles.Admin` | `SharedKernel/Authorization/` | Thêm `Invalidate` (Đ-6.10), `IsAllowedAsync` dùng chung (Mục 6.2), `[RequireAnyPermission]` |
| Kiểm vai trò hệ thống lúc khởi động | `IdentitySeeder` | Giữ nguyên; trigger Đ-6.9 bổ sung chặn lúc ghi |
| `posts.status 'hidden'` + `hidden_reason`; feed và trang cá nhân lọc `Status == Published` | Content (GĐ2, GĐ4 Đ-4.11) | `HideAsync` chỉ `UPDATE`; đường đọc tác giả thêm ở D7 |
| `PostVisibility.CanView` (hàm thuần BR-02) | `Content/Application/Posts/` | Kiểm "thấy được mới báo được" (Đ-6.12) trong hiện thực `IModerationTargets` |
| `SocialGraphEvents.FriendRequestSent/Accepted` — **chỉ log**, gọi sau `COMMIT` | `SocialGraph/Application/` | Đổi thân hàm sang `Publish` ở C0 |
| `IUserDirectory.GetManyAsync` (batch), `IObjectStorage.CreatePresignedGet` | SharedKernel | Hydrate người thao tác, ảnh chụp đối tượng, kết quả tìm kiếm |
| `RedisConnection` dùng chung, `FailOpenLogThrottle` | `SharedKernel/Redis/` | Pub/sub invalidate, chống ngập audit, log rơi event |
| Rate limiter (100/phút, policy `auth`) | `SharedKernelExtensions` | Thêm `report-create` |
| `Result`/`Error` (có `Type` cho Problem Details — Q-D4/Q-E4 GĐ4), `ToActionResult` | SharedKernel | Mọi mã 409 có `type` riêng |
| Khuôn module: `Add<X>Module`, `<X>DbContextOptions`, design-time factory, `Migrate<X>ModuleAsync` | Identity, Profile, Content, SocialGraph | Khuôn cho Moderation, Notification |
| Khung AuthZ matrix (`Caller` có `Moderator`, `Admin`), harness Testcontainers, `CapturingLogSink`, `ContractTestsBase`, `ContractGateCoverageTests` | `tests/` | Dòng mới + ba lớp cổng hợp đồng |
| BFF proxy chung `/bff/api/[...path]`, tự refresh single-flight | `src/frontend/app/bff/`, `lib/bff/` | Mọi endpoint mới chạy qua proxy — **không** route BFF mới |
| `hooks/use-cursor-pages.ts`, khuôn khử trùng theo id | frontend | Danh sách thông báo, hàng đợi, nhật ký, danh sách tài khoản |
| `AppHeader` nhận `nav`, `actions`; slot `actions` của `PublicProfile` | `components/shell/`, `features/profile/` | Chuông, ô tìm, liên kết theo quyền; nút "Báo cáo" trên hồ sơ |

**Chỗ có sẵn nhưng phải sửa:**

| Chỗ | Sửa gì | Vì sao |
|---|---|---|
| `LoginService` | Bước kiểm `disabled` sau khi mật khẩu đúng → 403 | Đ-6.5 |
| `RefreshTokenStore` | `status = 'active'` trong join lúc xoay | Đ-6.5 (lưới thứ hai) |
| `MeQuery`/`MeResponse` | Thêm `permissions` | Đ-6.11 |
| `PermissionCodes` | `RoleManage` ở cuối `All` | Đ-6.9 |
| `PermissionCache`, `IPermissionCache` | `Invalidate`, `InvalidateAll`; `IsAllowedAsync` dùng chung | Đ-6.10, Mục 6.2 |
| `ITokenRevocationStore` + `OnTokenValidated` | `CheckAsync`; nhánh fail-closed theo metadata | Đ-6.8 |
| `SocialGraphEvents` | Thân hàm → `Publish` | Đ-6.4 |
| `PostReadService` / mapper / `PostResponse` | Nhánh `hidden` cho tác giả; `Moderation?` | Đ-6.14 |
| `PostService.UpdateAsync` | Bài `hidden` → 409 | Đ-6.14 |
| `Program.cs` | `AddApplicationPart` ×2, `apiGroups` ×3, `Add<X>Module` ×2, dòng migrate ×2, `MapHub` (sau B), `IAuthorizationMiddlewareResultHandler` | Host liệt kê tường minh |
| `app/(app)/layout.tsx` | Chuông, ô tìm, liên kết "Kiểm duyệt"/"Quản trị" | Đ-6.20 |

## B.2 Bản đồ công việc

| Khối | Nội dung | Số việc | Cần trước | Chặn | Bước (Mục 9.3) |
|---|---|---|---|---|---|
| **C0. Đường ray** | Event bus + record event + `SocialGraphEvents` | 1 | cổng mở | A, B (người khác); D9–D10 | 2 |
| **A. Nền dữ liệu** | Hai module mới, migration Identity + Profile | 5 | cổng mở | C, D | 3 |
| **C. Hạ tầng chéo module** | Audit, hợp đồng kiểm duyệt, cache quyền, endpoint đặc quyền, trạng thái tài khoản, hub | 6 | A | D | 4, 9 |
| **D. Endpoint + nghiệp vụ** | Tài khoản, vai trò, báo cáo, kiểm duyệt, nhật ký, thông báo, tìm kiếm | 13 | A, C | E (ráp thật), F | 5–9 |
| **B. Test + cổng CI** | Harness, matrix, đồng thời, hợp đồng, transaction | 5 | A (một phần) | F | 3–6 |
| **E. Lane frontend** | Header, chuông, tìm kiếm, báo cáo, kiểm duyệt, quản trị | 10 | chỉ cần hợp đồng | F | 10 |
| **F. Cổng đóng** | Staging, E2E, checklist, bàn giao | 4 | D, E | GĐ7 khối F, GĐ8 | 11 |

**Khối E không phụ thuộc backend** — dựng trên hợp đồng (msw). Kẹt backend thì chuyển sang E.

---

## B.3 Khối C0 — Đường ray *(làm ngay sau cổng mở, PR riêng)*

> **Mục tiêu:** A và B phát event thật từ ngày đầu của họ — không ai viết lớp "chỉ log" để GĐ6 sửa lại sau lưng.

### C0 — Event bus trong tiến trình + toàn bộ record event

**Làm gì:** `SharedKernel/Events/` đúng Đ-6.2; sáu record của Đ-6.17 (kể cả của A, B); `AddInProcessEventBus()` trong
`AddSharedKernel`; `DrainAsync` cho test; metric `socialapp_events_published_total`, `…_dropped_total` trên
`Meter("SocialApp.Events")` của `System.Diagnostics.Metrics` (GĐ7 C2 kiểm tên ở `/metrics`); `SocialGraphEvents` gọi `Publish`.

**Làm như nào:** `Channel.CreateBounded(options { Capacity = 10_000, DropWrite }, itemDropped)` — `TryWrite` với `DropWrite` luôn
trả `true`, chỉ callback `itemDropped` biết có rơi; một `BackgroundService` đọc, với mỗi **handler** mở **một scope DI riêng**
(Đ-6.2 — hai handler chung scope là chung `DbContext`), gọi tuần tự theo danh sách `EventHandlerRegistration` do
`AddIntegrationEventHandler` đăng ký, bắt ngoại lệ từng handler, log tên handler + tên event + số thứ tự phong bì (không id, không
payload — id người dùng là PII). Không handler → bỏ qua; `published` đã đếm.

*Sửa 2026-09-23 khi thi công:* câu cũ "mỗi event một scope, tra bằng `GetServices`, log tên event + id, bộ đếm nội bộ" lệch
Đ-6.2 và không an toàn — lý do từng chỗ ở `huong-dan-khoi-c0-duong-ray.md` bảng L1–L8.

**Xong khi:** `EVT-01..07` xanh; test tích hợp GĐ4 (`FRD-*`) vẫn xanh; PR mỏng vào `develop` đã merge; tin nhắn cho A và B kèm
đoạn code mẫu một dòng `publisher.Publish(new CommentCreated(…))`.

**Cạm bẫy:** gọi `Publish` bên trong khối `await using var tx` — trông "sau `SaveChanges`" nhưng vẫn **trước** `COMMIT`. Luôn
đặt sau `await tx.CommitAsync()`.

---

## B.4 Khối A — Nền dữ liệu

> **Mục tiêu khối:** hai module mới đứng độc lập, Identity và Profile mở rộng không đổi dòng dữ liệu nào, và mọi luật "không
> được" (append-only, vai trò hệ thống) nằm ở **DB**, không chỉ ở code.

### A1 — Module Moderation: entity, `ModerationDbContext`, migration đầu tiên

**Làm gì:** `Report`, `AuditLog` (Domain, POCO); configuration đủ CHECK + index một phần của Mục 4; trigger append-only bằng
`migrationBuilder.Sql`; `AddModerationModule`, `MigrateModerationModuleAsync`; `AuditActions`, `ReportDecision`, `ReasonCodes`.

**Làm như nào:** chép hình dạng `SocialGraphDbContext` (schema mặc định, bảng lịch sử migration trong schema, `UseModerationNpgsql`
một chỗ cho DI + design-time). `AuditLog` không có `updated_at` → override `SaveChanges` bỏ qua. Không navigation sang bảng module
khác (không có kiểu nào để trỏ).

**Xong khi:** `dotnet ef migrations add InitialModeration` sinh đúng DDL (đọc lại từng CHECK, index một phần có đúng `WHERE`);
`AUD-02` xanh.

*Sửa 2026-09-23 khi thi công A1* (chi tiết ở `huong-dan-khoi-a-c-nen-du-lieu-va-ha-tang.md`, L-A5..L-A8): `AuditActions` ở
`SharedKernel/Audit/`, không ở Moderation; trigger chặn thêm `TRUNCATE`; dòng `AddModerationModule` + migrate vào `Program.cs`
**và** hai harness (`PostgresFixture`, `ModulesApiFactory`) đi cùng A1 — không đợi B1; test namespace
`Moderation_Domain_namespace_must_not_be_empty` đi cùng A1 — không đợi A5.

### A2 — Module Notification: entity, `NotificationDbContext`, migration đầu tiên

**Làm gì / như nào:** `Notification`, `NotificationActor`; `uq_notifications_group`, hai index Mục 4; FK trong schema
`notification_actors → notifications ON DELETE CASCADE`; `AddNotificationModule`, `MigrateNotificationModuleAsync`;
`NotificationTypes`, `GroupKey`.

**Xong khi:** migration khớp DDL; unit test `GroupKey.For` cho tám loại.

### A3 — Identity: migration `role.manage` + `description` + trigger vai trò hệ thống

**Làm gì:** `PermissionCodes.RoleManage` (cuối `All`); migration `AddPermissionDescriptionAndSystemRoleGuard`: `ADD COLUMN
description`, 18 câu `UPDATE`, hàm + trigger `trg_roles_protect_system`.

**Làm như nào:** seeder **không** sửa (nó tự chèn mã 18 vì đọc `All`). Thêm `description` vào entity `Permission` + configuration.

**Xong khi:** `ROLE-05`, `ROLE-07` xanh; test seeder GĐ1 (`SEED-01`, `SEED-02`) vẫn xanh.

*Sửa 2026-09-23 khi thi công A3* (chi tiết ở `huong-dan-khoi-a-c-nen-du-lieu-va-ha-tang.md`, L-A1..L-A4): migration tên
`SystemRoleGuardAndPermissionDescriptions`, **không** `ADD COLUMN` — cột, entity, configuration đã có từ GĐ1. Seeder **có** sửa:
chèn kèm mô tả từ `PermissionCodes.Descriptions` (DB mới — migration chạy trước seeder nên `UPDATE` chạm 0 dòng); migration chỉ
`UPDATE … AND description IS NULL` cho DB đã seed. Thêm sequence `identity.roles_role_id_seq` từ 100 cho vai trò tự tạo (D5).
Bốn test GĐ1 **không thể** "vẫn xanh" nếu không sửa, đã sửa cùng commit: `PermissionCodes_doc_duoc_du_17_ma` → `…_18_ma`;
`SEED-01` thêm mã 18 + khẳng định không mô tả nào NULL; `SEED-03` tắt đúng trigger mới trước khi đổi `code` (mô phỏng lớp 3 bị
gỡ — lớp 2 vẫn phải bắt); `FK-01` dùng vai trò tự tạo (với vai trò hệ thống trigger chặn trước FK, ra `P0001` không phải `23503`).

### A4 — Profile: extension, `search_norm`, index GIN

**Làm gì:** migration `AddDisplayNameSearch` đúng khối SQL của Đ-6.19. **Làm như nào:** extension trước hàm, hàm trước index; hàm
gọi `public.unaccent` dạng hai tham số. **Xong khi:** `EXPLAIN` trên 20.000 hồ sơ giả có `Bitmap Index Scan` (ghi kết quả vào hướng
dẫn khối A — nếp GĐ4 A5).

### A5 — Guard namespace + hằng quyền cục bộ

**Làm gì:** hai test namespace không rỗng; `ModerationPermissions` + `ModerationPermissionsTests`. **Xong khi:** ArchUnitNET xanh
**với type thật**; đổi hằng thành `"report.reslove"` → test đỏ.

*Sửa 2026-09-23* (L-A8): hai test namespace đi cùng commit entity đầu tiên của từng module (A1, A2) — nếp GĐ2 A7, GĐ4 A1. A5 còn
`ModerationPermissions` + test.

**Kết quả khối A:** hai schema mới migrate được; `audit_logs` và ba vai trò hệ thống được DB bảo vệ; index tìm kiếm tồn tại.

---

## B.5 Khối C — Hạ tầng chéo module

> **Mục tiêu khối:** những thứ **mọi** endpoint GĐ6 đứng lên trên: ghi audit cùng số phận với thao tác, kiểm duyệt ghi hộ đúng
> luật, quyền đổi là thấy ngay, endpoint đặc quyền không bao giờ fail-open.

### C1 — `IAuditTrail` + hiện thực ở Moderation (Đ-6.3, Đ-6.15)

**Làm gì:** interface + `AuditEntry` ở `SharedKernel/Audit/`; `SqlAuditTrail` ở `Moderation/Infrastructure` (một `INSERT` tham
số hóa trên `tx.Connection`; `tx == null` → mở kết nối riêng từ `NpgsqlDataSource`); lấy IP qua `IHttpContextAccessor`.

**Xong khi:** `TX-01` (dựng tạm một thao tác Identity + audit, ném sau audit → cả hai rollback), `AUD-01` xanh.

*Sửa 2026-09-23 khi thi công C1* (L-C1, L-C2 của `huong-dan-khoi-a-c-nen-du-lieu-va-ha-tang.md`): `tx == null` ghi trên kết nối
của `ModerationDbContext` — repo không đăng ký `NpgsqlDataSource` nào, dựng một cái là pool thứ hai (PERF-03 GĐ4 đã gỡ);
`AddHttpContextAccessor()` trong `AddModerationModule`. `TX-01`, `AUD-01` ở C1 là **bản hạ tầng** (`AuditTrailTests`, gọi thẳng
hợp đồng); bản đầy đủ qua `PATCH /reports` là của D7. Đột biến B5 "hiện thực ghi trên kết nối riêng dù có `tx`" đã chạy ở C1 →
`TX-01` đỏ (còn 1 dòng audit sau rollback).

### C2 — `IModerationTargets` + hiện thực (Đ-6.3, Đ-6.12, Đ-6.14)

**Làm gì:** interface ở `SharedKernel/Moderation/`; `ContentModerationTargets` (bài; bình luận **sau khi A merge**) ở Content;
`ProfileModerationTargets` (người dùng — snapshot hồ sơ, không có Hide) ở Profile; một `CompositeModerationTargets` chọn theo
`targetType` đăng ký ở host. `GetSnapshotsAsync` batch; `CanViewAsync(actor, target)` cho luật "thấy được mới báo được".

**Làm như nào:** `HideAsync` = `UPDATE content.posts SET status='hidden', hidden_reason=@r, updated_at=now() WHERE post_id=@id AND
status='published' RETURNING …`; không trả gì → `SELECT status` để phân biệt `AlreadyHidden`/`NotFound`. Bài `hidden` rồi thì
`CanView` của người khác trả false — tái dùng đúng nhánh `PostVisibility`.

**Xong khi:** `TX-02`, `HID-*` (phần store) xanh; ArchUnitNET `WriteContracts_are_only_the_two_named` xanh.

### C3 — Invalidate cache quyền (Đ-6.10)

**Làm gì:** `Invalidate`, `InvalidateAll`; `PermissionsChangedPublisher` (sau `COMMIT`) + `PermissionsChangedSubscriber`
(`BackgroundService`, `SUBSCRIBE` có tiền tố môi trường; nối lại khi Redis hồi). **Xong khi:** `PERM-01`, `PERM-02` xanh.

*Sửa 2026-09-23 khi thi công C3* (L-C3..L-C5, L-C10 của `huong-dan-khoi-a-c-nen-du-lieu-va-ha-tang.md`):
- `IsAllowedAsync` (Mục 6.2 — trước đó không đầu việc nào nhận) làm ở C3, dạng **extension method** `PermissionChecks.IsAllowedAsync`
  trên `IPermissionCache`: một hiện thực short-circuit duy nhất, fake trong test không phải chép lại. `PermissionHandler` gọi nó.
- Publisher là `IPermissionChangeNotifier.NotifyAsync(roleCode)`: xóa tại chỗ **rồi** `PUBLISH` — một lời gọi cho D5, không hai.
- `PermissionCache` có **thế hệ** theo vai trò: lần nạp bắt đầu trước `Invalidate` không để kết quả cũ nằm lại (`PERM-03`, unit).
  Subscriber gọi `InvalidateAll` khi Redis nối lại — tin phát lúc rớt đã mất.
- `PERM-01` là bản hạ tầng (SQL + notify); bản qua `PUT /admin/roles/…/permissions` là của D5.

### C4 — `[PrivilegedEndpoint]`: fail-closed + audit khi bị từ chối + policy any-of (Đ-6.8, Đ-6.15, Mục 6.1)

**Làm gì:** attribute (metadata) ở SharedKernel; `ITokenRevocationStore.CheckAsync`; nhánh trong `OnTokenValidated`;
`AuditingAuthorizationResultHandler : IAuthorizationMiddlewareResultHandler` (bọc handler mặc định); `[RequireAnyPermission]` + mở
rộng `PermissionPolicyProvider`; test reflection `Privileged_controllers_carry_the_attribute`.

**Làm như nào:** 503 dùng Problem Details type `urn:socialapp:problem:revocation-unavailable` khai trong cả `admin-v1` và
`moderation-v1`. Chống ngập bằng `SET audit:denied:{actor}:{route} 1 NX EX 60`.

**Xong khi:** `FC-01`, `AUD-03` xanh; thử cho đỏ: bỏ attribute khỏi một controller → test reflection đỏ.

*Sửa 2026-09-23 khi thi công C4* (L-C6, L-C7, L-C10 của `huong-dan-khoi-a-c-nen-du-lieu-va-ha-tang.md`):
- `OnTokenValidated` chỉ `Fail` được thành 401 → nó đặt dấu `HttpContext.Items` rồi `Fail`; `AuditingAuthorizationResultHandler` thấy
  `Challenged` + dấu trên endpoint đặc quyền thì ghi 503 qua `IProblemDetailsService` (có `traceId`).
- `CheckAsync` vẫn tự ghi cảnh báo fail-open có giới hạn tần suất (như `IsRevokedAsync` cũ); `IsRevokedAsync` = `CheckAsync == Revoked`
  — một chỗ đọc Redis, hành vi GĐ1 giữ nguyên cho filter hub của GĐ5 (`RV04` vẫn xanh).
- Chưa có controller admin/moderation thật: `FC-01`, `AUD-03`, `ANY-01` chạy trên probe controller của test; canh gác "có ít nhất một
  controller trong hai nhóm" của test reflection mang `Skip` — gỡ ở D2, và thử cho đỏ test reflection làm ở đó.
- Chỉ ghi audit khi `Forbidden`, không khi 401 (không có actor); ghi audit lỗi thì log Error, vẫn 403.

### C5 — `IAccountStatusReader` (Đ-6.19)

Interface batch ở SharedKernel, hiện thực ở Identity (`SELECT user_id FROM identity.users WHERE user_id = ANY(@ids) AND status <>
'active'`). **Xong khi:** `SRCH-05` xanh (dùng ở D12).

### C6 — `NotificationHub` *(sau khi B merge vé)*

**Làm gì:** hub rỗng phương thức ở `Notification/Presentation`, `[Authorize(AuthenticationSchemes = RealtimeTicketDefaults.Scheme)]`,
`MapHub` cạnh `/hubs/chat`; `NotificationPusher` (sau `COMMIT` của upsert) gọi `Clients.User(recipient)`; hợp đồng hub + cổng.
**Xong khi:** test hub (khuôn `HubAuthZTests` của B): không vé → 401; vé của A chỉ nhận thông báo của A (khuôn `HUB-09`).

**Kết quả khối C:** mọi thứ Mục 6 đòi hỏi ở tầng hạ tầng đã có test; D chỉ còn nghiệp vụ.

---

## B.6 Khối D — Endpoint và nghiệp vụ

> **Mục tiêu khối:** hợp đồng Mục 8 thành hệ thống chạy thật, khớp từng mã lỗi — và ba mốc không lùi được thành test xanh.

### D0 — Nền chung

`ModerationApiGroup`, `NotificationApiGroup`, `AdminApiGroup` (trong Identity); mọi controller khai `[ApiExplorerSettings]` từ file
đầu; `[ProducesResponseType]` đủ mã; validator FluentValidation đăng ký trong `Add<X>Module`. **Xong khi:**
`Every_controller_must_declare_a_swagger_group` xanh; Swagger thấy ba nhóm mới.

### D1 — Login/refresh từ chối `disabled` · `GET /me` có `permissions`

Đ-6.5, Đ-6.11; mở `identity-v1.yaml` chỉ-thêm **trong cùng commit**. **Xong khi:** `ADM-01` (phần login/refresh), `ME-01` xanh; cổng
hợp đồng Identity xanh; codegen FE xanh.

### D2 — `GET /admin/users`, `GET /admin/users/{id}`

Keyset `(created_at, user_id)`; `q` tiền tố email (citext `LIKE q || '%'` có escape); hydrate `displayName` một lô `IUserDirectory`.
**Xong khi:** `ADM-07`, `TC-A05`, `TC-A05b` xanh.

### D3 — Khóa / mở khóa + bất biến Admin + thu hồi ⭐

**Làm gì:** `AccountAdministrationService.LockAsync/UnlockAsync`; `AdminInvariant.EnsureRemainsAsync`; thứ tự Đ-6.6.

**Làm như nào:** viết `ADM-04`, `ADM-C1/C2` **trước** (đỏ). Một transaction Identity: khóa tư vấn (khi chạm tập Admin) → `UPDATE
users` → `UPDATE refresh_tokens` → bất biến → `IAuditTrail.AppendAsync(tx)` → `COMMIT` → `RevokeUserAsync` (thử lại 3 lần) →
`revocation`.

**Xong khi:** `ADM-01..04`, `ADM-06`, `ADM-C1/C2` (20 lần), `TC-A05-mod-lock` xanh.

**Tự rà trước commit:** `RevokeUserAsync` nằm **sau** `CommitAsync`; không có nhánh nào `return` giữa hai bước mà bỏ quên thu hồi.

### D4 — `PUT /admin/users/{id}/role`

Cùng khuôn D3, không đụng refresh family. **Xong khi:** `ADM-05`, `ROLE-01` (phần gán) xanh.

### D5 — CRUD vai trò + `GET /admin/permissions`

**Làm gì:** năm endpoint Đ-6.9; `RolePermissionDiff`; 409 `confirmation-required` có `added/removed/affectedUsers`; bắt `23503`
(FK RESTRICT) → 409 `role-in-use`; bắt `P0001` từ trigger → 409 (lưới — service đã chặn trước); sau `COMMIT` gọi C3.

**Xong khi:** `ROLE-01..07`, `PERM-01`, `TC-A05-roles` xanh.

### D6 — `POST /reports`

Đ-6.12: `CanViewAsync` qua C2; `INSERT … ON CONFLICT (…) WHERE status='open' DO NOTHING` rồi `SELECT`; policy `report-create`.
**Xong khi:** `REP-01..06`, `REP-C1`, `REP-IDOR` xanh.

### D7 — Hàng đợi, chi tiết, quyết định, khôi phục + đường đọc `hidden` của Content ⭐

**Làm gì:** `GET /reports` (gom theo đối tượng — một câu `GROUP BY target_type, target_id` trên `idx_reports_open_queue`, keyset
theo `(min(created_at), target_id)`), `GET /reports/{id}` (snapshot C2 + lịch sử), `PATCH /reports/{id}` (transaction Đ-6.13),
`POST …/restore`; phía Content: `PostResponse.moderation`, nhánh tác giả trong `PostReadService`, 409 khi sửa bài `hidden`; mở
`content-v1.yaml` chỉ-thêm **trong cùng commit**. Sau `COMMIT` phát `ContentHidden`. Counter `socialapp_reports_decided_total{decision}`.

**Làm như nào:** viết `TC-A06`, `TC-A06b`, `MOD-03` **trước** (đỏ). Tầng 2 kép theo Mục 6.2.

**Xong khi:** `MOD-01..06`, `MOD-C1`, `TX-01/02`, `HID-01..06`, matrix `TC-A06*` xanh.

### D8 — `GET /admin/audit-logs`

Keyset `id DESC`; lọc theo `actorId`/`targetType+targetId` (hai index), `action` chỉ đi kèm một trong hai hoặc khoảng id; hydrate
`actor`. **Xong khi:** `AUD-04`, `TC-A05-mod-audit` xanh.

### D9 — Store thông báo + upsert gộp

`INotificationStore.UpsertAsync(recipient, groupKey, …, actorId)` đúng khối SQL Đ-6.16 trong một transaction. **Xong khi:**
`NOTIF-03..05`, `NOTIF-C1` (20 lần) xanh.

### D10 — Handler event

**Làm gì:** `FriendRequestSentHandler`, `FriendRequestAcceptedHandler`, `ContentHiddenHandler` **ngay**; `CommentCreatedHandler`,
`ReactionSetHandler` khi A merge; `MessageSentHandler` khi B merge (kiểm `IPresenceReader`; không có → không tạo — Đ-6.17).

**Xong khi:** `NOTIF-01`, `NOTIF-02`, `NOTIF-09`, `EVT-02` xanh; mỗi handler mới có một test tích hợp đi từ **API thật của module
phát** (không `Publish` tay) tới dòng `notifications`.

### D11 — Endpoint thông báo

Bốn endpoint Mục 8.3; `read` tầng 3 cùng khuôn "không tồn tại = không phải của bạn = 403". **Xong khi:** `NOTIF-06..08`,
`NOTIF-IDOR`, `TC-A01-notifications` xanh.

### D12 — `GET /search`

Đ-6.19; mở `profile-v1.yaml` chỉ-thêm **trong cùng commit**. **Xong khi:** `SRCH-01..08`, `TC-A01-search` xanh.

### D13 — Rà RFC 7807 và `type`

Đối chiếu từng mã trong sáu file hợp đồng với thứ code trả; mọi 409/503 có `type` riêng; không thông điệp nào chứa id, email,
nội dung. **Xong khi:** ba lớp cổng hợp đồng mới + ba cũ xanh hai chiều.

---

## B.7 Khối B — Test và cổng CI

> **Mục tiêu khối:** biến mọi luật của Phần A thành thứ **chặn merge**, giữ tinh thần GĐ1: khung không sửa, chỉ thêm dòng.

### B1 — Harness

Thêm Moderation, Notification vào thứ tự migrate cố định của `PostgresFixture` và `ModulesApiFactory` (*đã đi cùng A1/A2 —
sửa 2026-09-23, L-A7*); helper "dựng Admin thứ
hai", "dựng vai trò tự tạo", "dựng báo cáo mở"; `DrainEventsAsync` của event bus (đã có từ C0 — `Harness/EventBusHarness.cs`); đo lại thời gian nhóm test Postgres — vượt ~3
phút thì tách collection (ngưỡng GĐ1).

### B2 — Dòng AuthZ matrix (Mục 6.3), viết cho đỏ trước

Chỉ sửa `AuthZMatrix.cs`, một khối `// --- GĐ6 ---`. Bảng đột biến trong commit: bỏ `[RequirePermission]` ở controller admin →
`TC-A05*` đỏ; handler "chặn mọi người trừ Admin" → `TC-A06b` đỏ; bỏ `CanViewAsync` ở `POST /reports` → `REP-IDOR` đỏ; bỏ tầng 3
ở `read` → `NOTIF-IDOR` đỏ.

### B3 — Test đồng thời (Mục 10.2)

`ADM-C1/C2`, `MOD-C1`, `REP-C1`, `NOTIF-C1`. Chạy **20 lần liền** trước khi tin (`for i in $(seq 20); do dotnet test --filter …;
done`). Đột biến: bỏ khóa tư vấn ở D3 → `ADM-C1` phải đỏ ít nhất một lần trong 20.

### B4 — Cổng hợp đồng (Mục 10.4 #1–2)

Ba lớp `ContractTestsBase` + `NotificationHubContractTests` + dòng csproj. Thử cho đỏ: thêm một status code vào controller không
sửa yaml → đỏ; đổi tên trường trong `notification-hub-v1.examples.json` → đỏ cả backend lẫn frontend.

### B5 — Transaction xuyên module và audit

`TX-01/02`, `AUD-01..04`, `WriteContracts_are_only_the_two_named`, `Privileged_controllers_carry_the_attribute`. Đột biến: hiện thực
`IAuditTrail` mở kết nối riêng thay vì dùng `tx.Connection` → `TX-01` đỏ (đây là thứ chứng minh transaction là **một**).

---

## B.8 Khối E — Lane frontend

> **Mục tiêu khối:** người dùng thấy thông báo và tìm được nhau; Moderator và Admin làm được việc của mình **chỉ bằng giao
> diện** — và giao diện là nơi mốc 1 được chứng minh.

Luật đặt file (luật frontend Mục 2, Đ-E13): năm feature của Đ-6.20; `lib/auth/permissions.ts`; không `features/` nào import chéo;
ghép ở `app/`. Viết `docs/giai-doan-6/huong-dan-khoi-e-f-frontend-va-cong-dong.md` khi bắt đầu khối, chốt câu hỏi `Q-E*` ở đầu
(nếp GĐ2/GĐ4).

### E1 — Codegen, client, ngữ cảnh lỗi, quyền

`pnpm gen:api` → ba `schema.d.ts` mới; alias ở `lib/api/types.ts`; `moderation-api.ts`, `notification-api.ts`, `admin-api.ts`;
`errorMessage` thêm ngữ cảnh `report-create`, `report-decide`, `admin-lock`, `admin-role`, `role-edit`, `search`, `notification-read`;
`PROBLEM_TYPES` thêm các `type` 409/503 mới; `hasPermission(me, code)`. Fixture msw chép `example` của yaml, gắn `satisfies`.
**Xong khi:** typecheck xanh; test `errorMessage` từng ngữ cảnh.

### E2 — Header + điều hướng theo quyền + guard mềm

Liên kết "Kiểm duyệt", "Quản trị" theo `permissions`; nạp lại `/me` khi focus và khi vào route cần quyền; trang "Bạn không có
quyền xem trang này". **Xong khi:** Vitest: đổi `permissions` giả → liên kết hiện/ẩn không tải lại trang.

### E3 — Chuông + danh sách thông báo

Popover 10 nhóm mới nhất + trang `/notifications` (cuộn theo cursor, khử trùng theo id); badge "9+"; hỏi lại 30 giây, dừng khi tab
ẩn, nạp ngay khi focus; bấm → đánh dấu đã đọc (optimistic) + điều hướng theo `target`; "Đánh dấu tất cả đã đọc" (`upTo` = lúc mở).
Câu theo `type`: *"An đã gửi lời mời kết bạn"*, *"An và 3 người khác đã bày tỏ cảm xúc về bài viết của bạn"*, *"Bài viết của bạn đã
bị ẩn vì vi phạm tiêu chuẩn cộng đồng"*… **Xong khi:** Vitest phần 10.5; một ca `<StrictMode>` không tạo hai timer.

### E4 — Tìm kiếm

Ô trong header (gợi ý ≤ 8) + trang `/search?q=` (≤ 20); debounce 300 ms, `AbortController` trong effect; dưới 2 ký tự hiện gợi ý
"Nhập ít nhất 2 ký tự" và **không** gọi API; trạng thái rỗng *"Không tìm thấy ai tên như vậy."* **Xong khi:** Vitest phần 10.5.

### E5 — Hộp thoại báo cáo + ráp qua slot

`features/report/report-dialog.tsx` (năm lý do bằng nhãn tiếng Việt, "Khác" bắt buộc mô tả); `ReportMenuItem` ráp ở `app/` vào menu
bài (GĐ2), hồ sơ (slot `actions` GĐ4), bình luận (slot của A — khi A merge). **Xong khi:** 201 và 200 cùng câu cảm ơn; 404 → "Nội
dung này không còn nữa".

### E6 — Màn kiểm duyệt

Hàng đợi (mỗi đối tượng một dòng: loại, số báo cáo, lý do nổi bật, thời gian chờ) · chi tiết (ảnh chụp đối tượng, danh sách báo
cáo, lịch sử xử lý) · ba nút quyết định theo loại đối tượng (ẩn chỉ hiện khi có `post.hide`) · khôi phục. Không optimistic; 409 → làm
mới hàng đợi + "Báo cáo này vừa được người khác xử lý." **Xong khi:** E2E-01 local xanh.

### E7 — Màn quản trị tài khoản

Danh sách (tìm theo email, lọc trạng thái/vai trò) · chi tiết · khóa (hộp thoại lý do) / mở khóa · đổi vai trò (select các vai trò
có thật) · hiện `revocation: deferred` bằng cảnh báo vàng. 409 `last-admin` → câu của server. **Xong khi:** E2E-04, E2E-06 local xanh.

### E8 — Màn vai trò + ma trận quyền

Danh sách vai trò (số người mang, nhãn "Hệ thống") · tạo · đổi tên · ma trận checkbox 18 quyền × vai trò đang sửa (ADMIN chỉ đọc) ·
hộp thoại xác nhận khi server trả `confirmation-required` (hiện **đúng** `removed` + `affectedUsers`) · xóa (ẩn nút với vai trò hệ
thống; vẫn xử lý 409). **Xong khi:** E2E-05 local xanh.

### E9 — Nhật ký + biểu ngữ bài bị ẩn

`/admin/audit` (lọc theo người thao tác/đối tượng, cursor, `metadata` hiện dạng khóa–giá trị); biểu ngữ trên `post-detail` khi có
`moderation`, ẩn nút Sửa. **Xong khi:** Vitest biểu ngữ; E2E-01 bước tác giả.

### E10 — Vitest + Playwright cho lát cắt

Theo Mục 10.5. Playwright local, `workers: 1`, kết quả dán vào PR kèm bản Chrome (Đ-E8).

---

## B.9 Khối F — Cổng đóng

> **Mục tiêu khối:** chứng minh trên hệ thống thật, với tài khoản thật, không trên máy local và không trên mock.

### F1 — Deploy staging

Trước merge: không key `.env` mới (event bus, pub/sub dùng Redis có sẵn); kiểm quyền tạo extension (Mục 9.1). Sau deploy: service
`migrate` xanh cho **mọi** module; `\dx`, `\dn`, trigger có mặt; `/health/ready` = 200.

### F2 — E2E lát cắt trên staging

Chạy `E2E-01..06` bằng ba tài khoản thật. Bằng chứng lưu `docs/giai-doan-6/bang-chung/`: ảnh/quay E2E-06 (mốc 1); ảnh hàng đợi trước
và sau quyết định; ảnh biểu ngữ tác giả + ảnh 404 của người khác; kết quả psql của hai dòng "bị từ chối" (audit, `roles.code`); tab
Network màn quản trị.

### F3 — Checklist Mục 12 + Definition of Done Mục 11

Tick từng dòng có bằng chứng. Dòng chờ A/B hay chờ server ghi "chờ …", **không xóa dòng** (nếp F4 GĐ2).

### F4 — Đóng băng + bàn giao

Đóng băng `moderation-v1`, `notification-v1`, `admin-v1`, `notification-hub-v1.md` cho phạm vi GĐ6; liệt kê phần hoãn có địa chỉ
(Mục 2) và phần "chờ A/B" nếu còn; cập nhật `README.md` Mục 1; bàn giao cho GĐ7 và GĐ8 (B.12).

---

## B.10 Thứ tự thực thi, đường găng, và thứ tự cắt

**Đường găng:** cổng mở → `C0` → `A1 → A3` → `C1 → C4` → `D3 → D4` → `D7` → `E2 → E7` → `F1 → F2 (E2E-06)`

`C0` phải xong **trong ngày thứ nhất sau cổng mở** — nó là thứ duy nhất người khác chờ.

**Thứ tự cắt khi trễ** — từ trên xuống, dừng khi kịp:

| Thứ tự | Cắt gì | Còn lại vẫn đạt |
|---|---|---|
| 1 | Loại thông báo `tag` (Đ-6.17) | FR-018 còn năm loại có thật |
| 2 | k6 tìm kiếm (giữ `EXPLAIN` bắt buộc) | Bằng chứng index vẫn có |
| 3 | Hub thông báo (C6) — nếu B trễ | Hỏi lại 30 giây (Đ-6.18) — thông báo vẫn tới, chậm hơn |
| 4 | Loại `message` — nếu B trễ hoặc B cắt presence | Badge chưa đọc của màn chat (GĐ5) phủ UC-15 A1 |
| 5 | Báo cáo + ẩn **bình luận** — nếu A trễ | Báo cáo bài và người dùng đủ AC US-019 |
| 6 | Màn nhật ký FE (E9 phần audit) | API + test vẫn đủ; Admin đọc qua Swagger trên staging |
| 7 | Đổi tên / xóa vai trò trên UI (API giữ) | Tạo vai trò + ma trận quyền — thứ câu hỏi phản biện PTTK hỏi |
| **Không cắt** | Bên ghi `revoked:user` + thứ tự DB → Redis · fail-closed · bất biến ≥ 1 Admin dưới đồng thời · TC-A05/A06 + đối chứng · ẩn + đóng báo cáo + audit **một transaction** · audit append-only + audit khi bị từ chối · CRUD vai trò qua API với đủ guard + invalidate cache · tìm kiếm không dấu cơ bản · thông báo `friend_*` + `moderation` · **E2E-06 trên staging** | "Bắt buộc phi chức năng" của bảng ưu tiên kế hoạch gốc (kiểm duyệt + audit + RBAC không cắt được — GOAL-03) |

**Tám thứ không test tự động nào bắt được — tự rà trước khi mở PR** (không có người review chéo cố định; đưa danh sách này
cho người duyệt PR):

1. **DB trước, Redis sau** ở mọi đường gọi `RevokeUserAsync` — liệt kê tên hàm đã rà trong mô tả PR.
2. `Publish` đặt **sau** `CommitAsync`, không trong khối transaction. (Phần "record event chỉ mang id và enum" đã có cổng CI:
   `EVT-07`.)
3. Không có `role == "ADMIN"` nào ngoài `SystemRoles` / `IsAllowedAsync` của SharedKernel; không nhánh Admin nào ở tầng 3.
4. `actorId` từ token (`GetUserId()`), không từ route hay body — kể cả ở `PUT /admin/users/{id}/role` (id trên đường là **đích**,
   người thao tác là token).
5. Không log `email`, `ip`, `detail`/`note` của báo cáo, nội dung bài; `metadata` audit không chứa nội dung.
6. Hai hiện thực hợp đồng ghi dùng `tx.Connection` + tham số hóa, không nối chuỗi, không mở kết nối riêng khi có `tx`.
7. Thông báo `moderation` và `ReportDetail` không lộ danh tính người báo cáo hay Moderator cho tác giả.
8. `POST /reports` là endpoint duy nhất của `moderation-v1` **không** mang `[PrivilegedEndpoint]` — và đó là cố ý.

## B.11 Mục tiêu từng khối — chúng cộng lại thành cái gì

| Khối | Mục tiêu | Thiếu nó thì mất gì |
|---|---|---|
| **C0** | Ba giai đoạn song song dùng chung một cách phát event | A, B viết event riêng; GĐ6 sửa code người khác, cổng đóng trễ theo người chậm nhất |
| **A** | Luật "không được" nằm ở DB | `audit_logs` sửa được bằng một dòng code vô tình; `roles.code` đổi được bằng psql |
| **C** | Audit cùng số phận thao tác; quyền đổi là thấy; endpoint đặc quyền không fail-open | Mốc 3 không có; "sửa quyền xong phải đợi 60 giây" trông như bug |
| **D** | Hợp đồng thành hệ thống chạy thật | Không có sản phẩm |
| **B** | Luật Phần A thành cổng chặn merge, kể cả dưới đồng thời | Hệ thống về 0 Admin lần đầu tiên hai Admin cùng thao tác |
| **E** | Moderator, Admin làm việc bằng giao diện; mốc 1 chứng minh trên UI | Kế hoạch gốc đòi bằng chứng UI — không có E là không nghiệm thu được |
| **F** | "Xong" thành sự kiện kiểm chứng được trên staging | "Xong" thành cảm giác |

## B.12 Mục tiêu của GĐ6

### Ba điều kiện để tuyên bố GĐ6 xong

Thiếu bất kỳ điều nào thì **chưa xong**, dù code đã chạy:

1. **E2E-06 trên staging có bằng chứng**: nâng rồi hạ quyền một tài khoản thật, có hiệu lực ở request kế tiếp, người đó không
   phải đăng nhập lại; và khóa một tài khoản thì tab của họ văng ra trong một request.
2. **Matrix `TC-A05*`/`TC-A06*`, `ADM-C1/C2`, `TX-01` xanh trên CI**, và đã từng đỏ khi cố tình bỏ `[RequirePermission]`, bỏ khóa
   tư vấn, và cho `IAuditTrail` mở kết nối riêng.
3. **Bốn AC của US-019 đã chạy trên staging bằng giao diện** với hai tài khoản thật (người báo + Moderator), có bằng chứng trong
   `bang-chung/`.

### GĐ6 để lại gì cho GĐ7–GĐ8

| Di sản | Ai thừa hưởng |
|---|---|
| Event bus trong tiến trình + record event | **GĐ7** (metric `dropped`, cảnh báo) · mọi tính năng sau MVP muốn phản ứng với việc xảy ra ở module khác |
| `IAuditTrail` + trigger append-only có cửa `socialapp.audit_purge` | **GĐ8** — job xóa audit quá 12 tháng (NFR-COMP-01), inspection "ai làm gì" |
| `AdminInvariant.EnsureRemainsAsync` | **GĐ8** — áp cho đường tự xóa tài khoản (kế hoạch gốc: "nối tiếp GĐ6") |
| Bên ghi `revoked:user` + khóa tài khoản thu hồi mọi family | **GĐ8** — xóa tài khoản dùng lại đúng đường khóa rồi ẩn danh PII |
| Invalidate cache quyền qua pub/sub | **GĐ7** khối E — hai bản sao API thấy thay đổi quyền cùng lúc, không phải đợi 60 giây |
| `[PrivilegedEndpoint]` (fail-closed + audit khi bị từ chối) | **GĐ8** — mọi endpoint quản trị mới (xóa tài khoản thay người dùng, xuất dữ liệu) gắn một attribute là đủ |
| Metric `socialapp_reports_decided_total`, `…_revocation_failures_total`, `…_events_dropped_total` | **GĐ7** (Grafana, cảnh báo) |
| `IAccountStatusReader` | **GĐ8** — mọi danh sách công khai phải lọc tài khoản đã xóa |
| Báo cáo tìm kiếm | **GĐ8** — chạy lại cùng kịch bản trên dữ liệu lớn hơn, so với mốc GĐ6 |
| Nợ có địa chỉ: dọn thông báo cũ, tag nếu bị cắt, xóa audit 12 tháng | **GĐ8** / Roadmap |

**Một câu để nhớ:** các giai đoạn trước dạy hệ thống trả lời *"người này được làm gì"*; GĐ6 dạy nó **đổi câu trả lời lúc đang
chạy** — và đổi xong thì mọi token, mọi cache, mọi kết nối đang mở đều phải biết, trong khi không một thao tác nào được phép
làm hệ thống mất người trả lời câu hỏi đó.
