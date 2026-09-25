# Hướng dẫn thực hiện — Khối E. Lane frontend + Khối F. Cổng đóng (GĐ6)

> Bản triển khai chi tiết của **B.8 Khối E** và **B.9 Khối F** trong [giai-doan-6.md](giai-doan-6.md). Tài liệu gốc trả lời *cái
> gì* và *vì sao*; tài liệu này trả lời *gõ vào file nào, chốt câu nào, và nhìn vào đâu để biết đã xong thật*. Viết khi khối E
> bắt đầu (2026-09-25), chốt câu hỏi `Q-E*` ở đầu — nếp GĐ2/GĐ4.
>
> **Nguồn sự thật, theo thứ tự ưu tiên khi mâu thuẫn:**
>
> 1. Hợp đồng — `moderation-v1.yaml`, `admin-v1.yaml`, `notification-v1.yaml` và ba hợp đồng mở lại chỉ-thêm (`identity-v1`,
>    `profile-v1`, `content-v1`) — sinh sẵn vào `lib/api/*/schema.d.ts` ở khối D
> 2. `giai-doan-6.md` — Đ-6.11, Đ-6.14, Đ-6.16–6.21, Mục 7 (luồng), Mục 8 (hợp đồng), Mục 10.5 (test FE)
> 3. `.claude/rules/frontend-rules.md` và `Đ-E1`–`Đ-E18`
> 4. File này
>
> Chỗ nào file này lệch 1–3 thì **sửa file này**. Lệch kế hoạch đã ghi ngược vào `giai-doan-6.md` B.8 (mở bằng "Lệch B.8").

|  |  |
|---|---|
| **Người làm** | Một người (Mục 9 — cả hai lane) |
| **Cần trước** | Khối C, D xong trên `loveart1210` (tới `49c0c65`, C6 hub đã có). Ba `schema.d.ts` mới đã nằm trong repo; `pnpm gen:api` sạch |
| **Khối này chặn** | F1–F4 (staging) |
| **Không thuộc khối này** | Sửa `src/backend/**` · route BFF mới (mọi endpoint đi qua proxy `/bff/api/*`) · nối hub `/hubs/notifications` phía FE (L3) |

---

## 0. Danh sách công việc — trạng thái

| Mã | Đầu việc | Kết quả (2026-09-25) |
|---|---|---|
| **E1** | Codegen, client, ngữ cảnh lỗi, quyền | ✅ `lib/api/{moderation,admin,notification}-api.ts`, `profileApi.search`; alias GĐ6 ở `types.ts`; **mười** ngữ cảnh lỗi (L1); 12 `type` mới trong `PROBLEM_TYPES` + `confirmationOf`; `lib/auth/permissions.ts` (`hasPermission`, `hasAnyPermission`), store `/me` (`me-store.ts`, `use-me.ts`); fixture `satisfies`. `messages.gd6.test.ts` 36 ca |
| **E2** | Header + điều hướng theo quyền + guard mềm | ✅ `RequirePermission` + trang "Bạn không có quyền xem trang này"; `ModerationNavLink`, `AdminNavLink`, `AdminSectionNav`; nạp lại `/me` khi focus/tab hiện lại và khi vào route cần quyền. Vitest: đổi `permissions` giả → liên kết hiện/ẩn không tải lại |
| **E3** | Chuông + danh sách thông báo | ✅ `NotificationBell` (popover 10 nhóm, badge "9+", hỏi lại 30 s dừng khi tab ẩn, nạp ngay khi focus), `/notifications` (cursor, khử trùng), đánh dấu đã đọc optimistic + rollback, "đánh dấu tất cả" (L2). Câu theo `type` × `actorCount`. Hub FE **chưa** (L3) |
| **E4** | Tìm kiếm | ✅ `SearchBox` (≤ 8 gợi ý, debounce 300 ms, `AbortController` trong effect, < 2 ký tự không gọi), `/search?q=` (≤ 20), trạng thái rỗng |
| **E5** | Hộp thoại báo cáo + ráp qua slot | ✅ `ReportButton` (5 lý do, "Khác" bắt buộc mô tả, 201/200 cùng câu, 404 "Nội dung này không còn nữa"); ráp ở `app/` vào bài, bình luận, hồ sơ (L4) |
| **E6** | Màn kiểm duyệt | ✅ `/moderation` (hàng đợi theo đối tượng), `/moderation/[reportId]` (ảnh chụp, báo cáo mở, lịch sử, quyết định theo loại, khôi phục). Không optimistic; 409 `already-decided` → về hàng đợi nạp mới + câu |
| **E7** | Màn quản trị tài khoản | ✅ `/admin/users` (email tiền tố, lọc trạng thái/vai trò), `/admin/users/[userId]` (khóa có lý do, mở khóa, đổi vai trò). `deferred` → cảnh báo vàng (token `--warning`, L8). 409 `last-admin` → câu server |
| **E8** | Màn vai trò + ma trận quyền | ✅ `/admin/roles`, `/admin/roles/[roleId]`: tạo, đổi tên, ma trận 18 quyền (ADMIN chỉ đọc, `role.manage` luôn khóa), hộp thoại xác nhận hiện **đúng** `removed`/`added`/`affectedUsers`, xóa (ẩn với vai trò hệ thống) |
| **E9** | Nhật ký + biểu ngữ bài bị ẩn | ✅ `/admin/audit` (lọc người thao tác / hành động / đối tượng, cursor, `metadata` khóa–giá trị); biểu ngữ trong `PostItem`, nút Sửa ẩn |
| **E10** | Vitest + Playwright | ✅ Vitest 626 → 742; `e2e/gd6.spec.ts` sáu ca E2E-01..06 xanh trên API dev thật (Chrome 154.0.8037.57). Cả bộ Playwright: xem "Thực tế thi công" |
| **F1–F4** | Cổng đóng | ⏳ **Chờ** — PR vào `develop` khi được bảo, deploy staging, chạy lại `e2e/gd6.spec.ts` với `E2E_ADMIN_*` thật |

### 0.1 Chỗ lệch — đã ghi ngược vào `giai-doan-6.md` B.8

| # | Kế hoạch ghi | Thực tế | Vì sao |
|---|---|---|---|
| L1 | E1: bảy ngữ cảnh lỗi | **Mười** — thêm `moderation-read`, `moderation-restore`, `admin-read` | Lời gọi ĐỌC không mượn ngữ cảnh ghi (nếp `relationship` của GĐ4 Q-E3); khôi phục có 403/404 riêng |
| L2 | E3: "Đánh dấu tất cả" `upTo` = lúc mở | `upTo` = `updatedAt` của nhóm **mới nhất đang hiển thị** | Hợp đồng `notification-v1` (nguồn 1) ghi đúng như vậy; "lúc mở" theo đồng hồ máy khách có thể nuốt nhóm tới sau lúc server trả trang |
| L3 | Đ-6.18 / Mục 10.5: "kết nối hub" | E3 chỉ **hỏi lại 30 s** (đúng chữ B.8 E3); nối `/hubs/notifications` phía FE là PR nhỏ sau | Luật FE #16 (Đ-5.17) hiện cấm kết nối hub thứ hai — nối hub thông báo cần sửa luật đó có ngày (Đ-6.18 đã chốt hai WebSocket/tab). Hợp đồng dữ liệu không đổi giữa hai chế độ, không màn nào phải sửa |
| L4 | Đ-6.20: nút "Báo cáo" trong "menu …" của bài | Không có menu nào — nút nằm ở **slot footer** của `PostCard` (bài), hàng cảm xúc của bình luận (`renderReactions`), slot `actions` của hồ sơ | Không sửa `features/post`/`comment`/`profile`; chỉ `app/(app)/(with-profile)/_interactions/` ráp. Ẩn trên nội dung của chính mình theo `canEdit`/`canDelete` của server |
| L5 | Đ-6.20: "Quản trị" khi có `user.lock \| role.assign \| role.manage \| audit.read` | Thêm `user.unlock` | Cùng policy any-of của `GET /admin/users` (Mục 6.1) — vai trò chỉ có `user.unlock` vào được danh sách thì phải có đường vào |
| L6 | E7: "select các vai trò có thật" | Có `role.manage` → `GET /admin/roles`; không có → hai vai trò hệ thống USER/MODERATOR (Q-E5) | `admin-v1` không có danh sách vai trò nào cho người chỉ có `role.assign` |
| L7 | E2E-01: người báo mở lại link → "Nội dung này không còn nữa" | Màn bài 404 giữ câu GĐ2 "Không tìm thấy bài viết." (`post-not-found`) | Một câu cho ba nghĩa của 404 (GĐ2 Mục 7.4). Câu "không còn nữa" dùng ở 404 của **báo cáo** |
| L8 | — | Token màu mới `--warning` (`globals.css`, sáng + tối) + biến thể `Alert` `warning` | "Cảnh báo vàng" của E7 không có token nào; luật UI Mục 3: token chỉ ở `globals.css`, biến thể bằng `cva` trong file kit |
| L9 | Mục 9.4: không đổi bố cục header mà không báo B | `AppHeader` cho **xuống dòng** khi chật (chữ ký không đổi) | Thêm "Kiểm duyệt", "Quản trị", ô tìm, chuông vào hàng `max-w-2xl` — báo B trong mô tả PR |
| L10 | Mục 10.5: StrictMode cho chuông và ô tìm | Thêm cho **mọi** màn GĐ6 tạo `AbortController` lúc mount (10 màn) | Luật FE Mục 9 áp cho mọi màn sở hữu tài nguyên hủy được — danh sách 9 → 19 màn |

### 0.2 Nợ có địa chỉ phát hiện khi làm

| Nợ | Ở đâu | Địa chỉ |
|---|---|---|
| ~~`identity-v1` khai 403 `account-disabled` chỉ trong mô tả, không có schema~~ | `lib/api/problem.ts` | ✅ **Trả 2026-09-25:** `AccountDisabledProblem` vào `identity-v1` (`1.2.0-gd6`, chỉ-thêm); `PROBLEM_TYPES.accountDisabled` ràng `satisfies` |
| ~~`POST /posts` 403 hai nghĩa không `type` riêng — FE nói "hãy hoàn tất hồ sơ" cả khi mất quyền (lộ ở E2E-05)~~ | `messages.ts` ngữ cảnh `post-create` | ✅ **Trả 2026-09-25:** 403 **chưa có hồ sơ** mang `type` `profile-required` (`ContentErrors.ProfileRequired`, `content-v1` `1.3.0-gd6`); 403 không `type` = thiếu quyền → "Tài khoản của bạn chưa được phép đăng bài." (cùng câu `upload`). Chọn gắn `type` cho nhánh hồ sơ chứ không cho nhánh thiếu quyền: 403 thiếu quyền do tầng 2 chung của SharedKernel sinh cho MỌI endpoint — gắn `type` ở đó là đổi hợp đồng của cả dự án. Bình luận (`POST /posts/{id}/comments`) có cùng cặp 403 nhưng không đổi — ngoài phạm vi nợ này |
| Hub thông báo FE (L3) | `lib/realtime/` | PR nhỏ sau GĐ6, kèm sửa luật FE #16 |

---

## 1. Câu hỏi chốt trước khi gõ code

#### Q-E1 — `/me` sống ở đâu, nạp lại khi nào

Header, guard, nút theo quyền cùng cần `permissions`. **Chốt:** store module `lib/auth/me-store.ts` (khuôn `token-store`, single-flight,
nạp lại hỏng thì giữ `me` cũ — không đẩy người đang dùng ra trang "không có quyền" vì một request rớt) + hook `useMe()` đếm người dùng,
nghe `focus` + `visibilitychange`, xóa khi phiên `anonymous`. Guard gọi `refreshMe()` mỗi lần vào route; màn nhận 403 cũng gọi nó (Mục 7.3).
*✅ chốt 2026-09-25.*

#### Q-E2 — Guard mềm: trang riêng hay redirect

**Chốt:** trang `NoPermission` tại chỗ (Đ-6.20: không redirect im lặng), đặt ở `features/auth/require-permission.tsx`, nhận `anyOf` —
`/admin` gốc chuyển tới mục **đầu tiên** được phép; không mục nào → `NoPermission`. *✅ chốt 2026-09-25.*

#### Q-E3 — Nhãn lý do báo cáo dùng chung bốn feature

Hộp thoại báo cáo, hàng đợi, câu thông báo `moderation`, biểu ngữ bài bị ẩn — bốn feature không import chéo nhau. **Chốt:**
`lib/moderation/reasons.ts` (`Record<ReasonCode, string>` — hợp đồng thêm lý do là đỏ compile); thêm dòng vào bảng tầng của luật FE Mục 2.
*✅ chốt 2026-09-25.*

#### Q-E4 — 409 `already-decided` "làm mới hàng đợi" khi quyết định nằm ở màn chi tiết

**Chốt:** màn chi tiết `router.replace("/moderation?notice=decided")` — hàng đợi nạp mới mỗi lần mở (dòng vừa bị xử lý đã biến mất) và
hiện câu "Báo cáo này vừa được người khác xử lý.". Thành công: `?notice=done`. 409 `target-gone` thì ở lại (báo cáo còn mở để bỏ qua).
*✅ chốt 2026-09-25.*

#### Q-E5 — Danh sách vai trò khi người xem không có `role.manage`

`GET /admin/roles` cần `role.manage`; người chỉ có `role.assign` không đọc được vai trò nào. **Chốt:** lùi về USER/MODERATOR (tên seed của
`IdentitySeeder`) — gán giữa hai vai trò này chỉ cần `role.assign`; chạm ADMIN cần `role.manage` (L-D18) nên không liệt kê. Vai trò hiện tại
luôn có trong danh sách. Ghi L6. *✅ chốt 2026-09-25.*

#### Q-E6 — Lọc nhật ký theo hành động

**Chốt:** `Select` của kit (thêm bằng `pnpm exec shadcn add` cùng `dialog`, `checkbox`); các bộ lọc khác là ô chữ. `targetId` chỉ gửi khi có
`targetType` (server 400 nếu thiếu cặp). *✅ chốt 2026-09-25.*

---

## 2. Bản đồ file

| Tầng | File | Đầu việc |
|---|---|---|
| `lib/api/` | `moderation-api.ts`, `admin-api.ts`, `notification-api.ts`, `profile-api.ts` (`search`), `types.ts`, `problem.ts` (`PROBLEM_TYPES`, `confirmationOf`), `messages.ts` | E1 |
| `lib/auth/` | `permissions.ts`, `me-store.ts`, `use-me.ts` | E1, E2 |
| `lib/moderation/`, `lib/validation/` | `reasons.ts`, `moderation.ts` (câu validator của server) | E5–E8 |
| `components/` | `form/textarea-field.tsx` (bản nhiều dòng của `TextField`); kit: `dialog`, `select`, `checkbox` (CLI ghim), `alert` biến thể `warning`; `shell/app-header.tsx` xuống dòng | E5, E7 |
| `features/auth/` | `require-permission.tsx` | E2 |
| `features/notification/` | `notification-text.ts`, `unread-store.ts`, `use-unread-count.ts`, `use-read-marks.ts`, `notification-item.tsx`, `notification-bell.tsx`, `notification-list.tsx` | E3 |
| `features/search/` | `use-user-search.ts`, `search-box.tsx`, `search-results.tsx`, `search-result-item.tsx` | E4 |
| `features/report/` | `report-dialog.tsx` | E5 |
| `features/moderation/` | `permissions.ts`, `labels.ts`, `moderation-nav-link.tsx`, `moderation-queue.tsx`, `report-detail.tsx` | E2, E6 |
| `features/admin/` | `sections.ts`, `admin-nav.tsx`, `labels.ts`, `use-role-options.ts`, `admin-users.tsx`, `admin-user-detail.tsx`, `permission-matrix.tsx`, `roles-screen.tsx`, `role-editor.tsx`, `audit-log.tsx` | E2, E7–E9 |
| `features/post/` | `post-item.tsx` (biểu ngữ), `post-actions.tsx` (`canEditBody`) | E9 |
| `app/` | `(app)/header-extras.tsx`, `(app)/layout.tsx`, `_interactions/report-slot.tsx` + `post-interactions.tsx`, `users/[userId]/page.tsx`; route `notifications`, `search`, `moderation`, `moderation/[reportId]`, `admin` (+ `layout`), `admin/users`, `admin/users/[userId]`, `admin/roles`, `admin/roles/[roleId]`, `admin/audit` | E2–E9 |
| `e2e/` | `gd6-helpers.ts` (`taoAdmin`, `timBaoCao`, `focusLai`), `gd6.spec.ts` | E10 |

Impact analysis trước khi sửa symbol có sẵn (`node .gitnexus/run.cjs impact … --direction upstream`): `errorMessage` LOW · `PostItem` LOW ·
`PostActions` LOW (thêm prop tùy chọn) · `PROBLEM_TYPES`, `postListFooter`, `postDetailComments` UNKNOWN → tìm chữ: chỉ thêm thành viên/nhánh,
người gọi không đổi.

## 3. Cạm bẫy đã gặp

1. **Radio của Base UI có hai phần tử cùng nhãn** (input ẩn + nút) — `getByLabelText` đỏ "multiple elements". Chọn bằng
   `getByRole("radio", { name })` (khuôn `post-composer.test.tsx`).
2. **Fake timer làm treo msw.** Ca hỏi lại 30 giây chỉ giả `setInterval`/`clearInterval` (`vi.useFakeTimers({ toFake: [...] })`) —
   msw và Testing Library cần `setTimeout` thật.
3. **Hạn mức `/auth/*` 10 lượt/phút theo IP:** khai `giuHanMucAuth(n)` với n > 10 là 429 **dù có chờ** — tách khai theo từng chặng
   (E2E-01 dùng 13 lượt: khai 9 rồi 4).
4. **Lượt biên dịch đầu của route mới trên `next dev`** vượt 30 giây mặc định của Playwright ở bước đăng nhập UI — `gd6.spec.ts` đặt
   `timeout: 120_000` cho cả file.
5. **Hai `BrowserContext` không tranh focus của hệ điều hành** — spec bắn đúng sự kiện `focus` app nghe (`focusLai`), vì đó là cơ chế
   thật của Đ-6.11, không phải đường tắt.
6. **Không có API tạo ADMIN** (đúng thiết kế). Dev: `taoAdmin` nâng bằng psql trong container Postgres dev; máy khác localhost bắt buộc
   `E2E_ADMIN_EMAIL`/`E2E_ADMIN_PASSWORD`.

---

## Thực tế thi công

### E1–E10 — 2026-09-25

- Mốc trước khi sửa: `pnpm gen:api` sạch; lint, typecheck xanh; Vitest **54 file / 626 ca**.
- Sau khối E: lint, typecheck, build xanh; Vitest **68 file / 742 ca** (+116, 14 file mới, trong đó 6 ca `<StrictMode>` phủ 10 màn).
  Build có đủ 11 route mới.
- `e2e/gd6.spec.ts` trên API dev chạy từ source (`dotnet run --migrate` áp đủ `moderation`, `notification`), Postgres/Redis/Mailpit dev,
  Chrome **154.0.8037.57**: E2E-02, 03, 04, 05 xanh lượt đầu; E2E-06, E2E-01 đỏ lượt đầu vì cạm bẫy 3–4 (lỗi của spec), sửa rồi xanh.
- Cả bộ Playwright: xem mục dưới.
