# GĐ 1 — Identity & Access (UC-01, UC-02) · Ngày 3–6

> Tài liệu thi công chi tiết cho Giai đoạn 1. Đọc kèm `ke-hoach-trien-khai.md` (lộ trình tổng)
> và `BaoCao_Nhom4_v5.pdf` (Mục 5.5 schema, 6.7 security design, 7.2 verification plan).
>
> **Nguyên tắc:** GĐ1 là nền móng — không phụ thuộc gì, nhưng GĐ2→GĐ8 đều đứng trên nó.
> Làm ẩu ở đây thì mọi giai đoạn sau đều trả giá. "Xong" nghĩa là đạt Definition of Done
> (Mục 11), không phải "chạy được trên máy local".

## Tài liệu này có hai phần

| | Trả lời câu hỏi | Đọc khi nào |
|---|---|---|
| **[Phần A](#phần-a--thiết-kế-và-quyết-định)** — Thiết kế và quyết định (Mục 0–14) | *Hệ thống phải trở thành cái gì, và vì sao chọn như vậy* | Trước khi bắt tay; khi cần tra một quyết định hoặc giải thích nó lúc bảo vệ |
| **[Phần B](#phần-b--kế-hoạch-triển-khai)** — Kế hoạch triển khai (Mục B.0–B.11) | *Phải làm những việc gì, làm thế nào, và biết là xong bằng cách nào* | Hằng ngày, khi nhận việc và khi đánh dấu việc đã xong |

Phần B **không lặp lại** thiết kế của Phần A — nó trỏ ngược về. Gặp câu "vì sao lại làm thế" trong
Phần B thì câu trả lời nằm ở mục tương ứng của Phần A.

---

# Phần A — Thiết kế và quyết định

## 0. Thuật ngữ

Đọc mục này trước nếu bạn không phải người làm phần auth — các thuật ngữ dưới đây xuất hiện
xuyên suốt tài liệu và cả GĐ2–GĐ8.

| Thuật ngữ | Nghĩa | Chi tiết |
|---|---|---|
| **AuthN** (Authentication) | Xác thực — "người gọi **là ai**?" | Mục 6.1 |
| **AuthZ** (Authorization) | Phân quyền — "người đó **được làm gì**?" Gồm tầng 2 + tầng 3 | Mục 6.0 |
| **RBAC** | Role-Based Access Control — phân quyền theo vai trò (tầng 2) | Mục 6.2 |
| **Ownership** | Kiểm tra "đúng tài nguyên NÀY" (tầng 3). Bỏ sót = lỗ hổng IDOR | Mục 6.3 |
| **IDOR** | Insecure Direct Object Reference — sửa/đọc được dữ liệu của người khác chỉ bằng cách đổi `id` trên URL. Lỗi phổ biến nhất của loại app này | Mục 6.0 |
| **claim** | Một trường dữ liệu bên trong JWT (`sub`, `role`, `exp`, `jti`…) | Mục 3.1 |
| **`code` (role code)** | Định danh vai trò dạng **chuỗi** — `'USER'`, `'MODERATOR'`, `'ADMIN'`. **Mọi** vai trò đều dùng chuỗi này trong token, không riêng Admin. `role_id` kiểu số chỉ sống trong DB | Mục 3.1 |
| **short-circuit** | "Đoản mạch" — gặp điều kiện đủ thì trả kết quả ngay, bỏ qua phần còn lại. Ở đây: `role == "ADMIN"` thì qua tầng 2 luôn, không tra bảng quyền | Mục 3.2 |
| **default deny** | Mặc định từ chối — endpoint nào chưa khai báo quyền thì bị chặn, thay vì cho qua | Mục 6.1 |
| **idempotent** | Chạy 1 lần hay 100 lần đều cho cùng kết quả. Bắt buộc với seeder vì CD deploy lại liên tục | Mục 5.4 |
| **access token** | JWT ngắn hạn (15 phút), đi kèm **mọi** request. Server **không lưu**. Không phải mã định danh của user — nó *chứa* mã định danh ở claim `sub` | Mục 7.0 |
| **refresh token** | Chuỗi ngẫu nhiên 32 byte (**không phải JWT**), sống vài ngày, chỉ dùng ở `/auth/refresh` để xin access token mới. Server lưu **băm** trong DB nên thu hồi được | Mục 7.0 |
| **rotation / reuse detection** | Mỗi lần refresh thì cấp token mới và vô hiệu token cũ; nếu token cũ bị dùng lại → coi là bị đánh cắp → thu hồi cả chuỗi | Mục 7.3 |
| **`family_id`** | "Họ" chung của một dòng refresh token sinh ra từ **một lần đăng nhập** (không phải của cả user — mỗi thiết bị một family). Thu hồi cả dòng bằng 1 câu `UPDATE` | Mục 3.5 |
| **`revoked:user` + `iat`** | Cơ chế thu hồi access token: ghi mốc thời gian vào Redis → mọi token của user phát trước mốc đó bị từ chối. Dùng khi nâng/hạ vai trò, khóa/xóa tài khoản | Mục 7.5 |

---

## 1. Mục tiêu giai đoạn

| Mã | Mục tiêu | Nguồn |
|---|---|---|
| FR-001 | Đăng ký tài khoản + xác minh email | US-001 |
| FR-002 | Đăng nhập cấp JWT (access 15 phút) + refresh token rotation | US-002, ADR-002 |
| FR-003 | Khóa tài khoản sau 5 lần đăng nhập sai trong 15 phút | US-002 AC-03 |
| Mục 6.7.1 | Ba tầng kiểm soát truy cập hoạt động đầy đủ (AuthN → RBAC → Ownership) | Báo cáo 6.7.1 |
| Mục 6.7.2 | RBAC **dữ liệu hóa**: ma trận Role–Permission nằm trong DB, không hard-code | ENT-10/10a/10b |
| NFR-SEC-01 | BCrypt cost 12; refresh token lưu dạng băm, không lưu bản rõ | Báo cáo 6.7 |
| NFR-SEC-03 | Refresh rotation + reuse detection → thu hồi cả chuỗi | ADR-002 |
| GOAL-03 | Nền chống IDOR: TC-A01, TC-A02 xanh và trở thành CI gate | Mục 7.2 |

**Kết quả bàn giao cuối GĐ1:** người dùng thật đăng ký được trên staging, nhận mail xác minh
qua Mailpit, đăng nhập lấy token, gọi được endpoint có bảo vệ, và mọi truy cập sai quyền đều
bị chặn đúng mã lỗi.

---

## 2. Phạm vi

### Trong phạm vi

- **Dọn 4 khoản nợ kỹ thuật GĐ0 để lại** (EF cho Identity, DbContext + schema, 5 package,
  tách CI gate) — Mục 9.0
- Bảng `roles`, `permissions`, `role_permissions`, `users`, `refresh_tokens`,
  `email_verification_tokens` + migration đầu tiên
- Seeder idempotent cho 3 vai trò và toàn bộ ma trận quyền + kiểm tra vai trò hệ thống lúc khởi
  động (Mục 5.5)
- Đăng ký, xác minh email, đăng nhập, refresh, đăng xuất
- **CORS** cho origin frontend + refresh token trong `httpOnly` cookie (Mục 8)
- **Lane frontend chạy song song từ Ngày 3** (1 người — `ke-hoach-trien-khai.md` Mục 0C): scaffold
  Next.js 14, màn đăng ký/đăng nhập/xác minh email, app shell + route guard, **interceptor
  401→refresh**. Đây là thay đổi so với bản A của kế hoạch tổng, nơi frontend hoãn tới GĐ2
- **Cổng mở / cổng đóng hợp đồng API** — OpenAPI stub chốt ở đầu giai đoạn, ráp thật trên staging ở
  cuối giai đoạn (Mục 9)
- Lockout 5 lần / 15 phút
- `[RequirePermission]` + policy handler trong SharedKernel (dùng chung cho mọi module sau)
- Quy ước kiểm tra ownership (tầng 3) + khuôn test AuthZ matrix
- Harness integration test trên Postgres thật (Testcontainers)
- `ITokenRevocationStore` (Redis) — **bên đọc**: hook `OnTokenValidated` kiểm tra `revoked:user`
  + `iat` ở tầng 1 (Mục 7.5). Bên ghi ở GĐ1 chỉ nối **một** trigger: reuse detection

### Ngoài phạm vi — hoãn có địa chỉ

| Việc | Hoãn tới | Lý do |
|---|---|---|
| Cột `roles.is_system` | **Loại bỏ hẳn** | Tập vai trò cần bảo vệ vĩnh viễn là 3 cái nên trigger gọi thẳng tên là đủ — cột chỉ lặp lại thông tin đã cố định. Thay bằng FK RESTRICT + kiểm tra lúc khởi động + bất biến "≥ 1 Admin" (Mục 3.4) |
| Trigger DB chặn sửa/xóa vai trò hệ thống | **GĐ6** (tùy chọn) | Chỉ đáng làm khi đã có endpoint sửa vai trò thật; GĐ1 dùng kiểm tra lúc khởi động (Mục 5.5) |
| CRUD vai trò (`role.create/update/delete`) | **GĐ6** | Thuộc nhóm admin endpoints |
| Bất biến "luôn còn ≥ 1 Admin hoạt động" | **GĐ6** + **GĐ8** | Chỉ có đường vi phạm khi đã có `role.assign`, `user.lock` (GĐ6) và quyền tự xóa tài khoản (GĐ8) |
| Đẩy invalidate cache quyền khi Admin sửa role | **GĐ6** | GĐ1 quyền chưa sửa được lúc runtime; cache TTL 60s là đủ |
| Trigger **ghi** vào `revoked:user` khi nâng/hạ vai trò, khóa tài khoản | **GĐ6** | GĐ1 chưa có endpoint nào đổi vai trò hay khóa tài khoản. Bên đọc đã sẵn sàng từ GĐ1 nên GĐ6 chỉ việc gọi (Mục 7.5) |
| Trigger ghi khi user tự xóa tài khoản | **GĐ8** | Đi kèm quyền xóa tài khoản theo NĐ 13/2023 |
| ~~Frontend Next.js~~ | **Đã kéo về GĐ1** | Kế hoạch chuyển sang **lát cắt dọc**: mỗi giai đoạn giao trọn cả backend lẫn frontend của cùng một tính năng. Nhân lực GĐ1 thành **2 backend + 1 frontend**, và **trang HTML tối giản của bản A bị loại bỏ** — interceptor 401→refresh thật làm đúng việc đó mà không phải xóa đi sau. Chi tiết và bảng đối chiếu bản A/bản B: `ke-hoach-trien-khai.md` Mục 0C |
| Đổi mật khẩu / quên mật khẩu | Sau MVP | Không nằm trong FR-001..003 |
| Đăng nhập mạng xã hội (OAuth) | Ngoài MVP | — |

---

## 3. Quyết định thiết kế đã chốt

Chốt trước khi gõ dòng code nào. Bốn điểm đầu **lệch so với báo cáo v5.0** — xem Mục 13.

### 3.1 JWT claim `role` mang `code` dạng chuỗi

#### `code` chuỗi là gì

Mỗi vai trò có hai cách gọi tên trong DB:

```
role_id = 3        ← số, khóa chính, dùng để nối bảng (FK)
code    = 'ADMIN'  ← chuỗi, định danh cho người đọc và cho code so sánh
```

Token mang **chuỗi**, không mang số. Điều này áp dụng cho **mọi vai trò**, không riêng Admin:

```json
// An — người dùng thường     // Bình — kiểm duyệt viên     // Cường — quản trị
{ "sub":  "0192f3c1-...",     { "sub":  "0192a7b4-...",     { "sub":  "0192d8e5-...",
  "role": "USER",               "role": "MODERATOR",          "role": "ADMIN",
  "iat":  1757116800,           "iat":  1757116800,           "iat":  1757116800,
  "exp":  1757117700,           "exp":  1757117700,           "exp":  1757117700,
  "jti":  "3f9a..." }           "jti":  "7c2b..." }           "jti":  "d41e..." }
```

Cả ba đều là chuỗi. Không có vai trò nào mang `"role": 3`.

#### `role_id` kiểu số sống ở đâu

Chỉ bên trong database, không bao giờ ra khỏi hệ thống:

| Nơi | Kiểu |
|---|---|
| `roles.role_id` (PK) | **int** — 1, 2, 3 |
| `users.role_id` (FK) | **int** |
| `role_permissions.role_id` (FK) | **int** |
| **JWT claim `role`** | **chuỗi** — `'USER'`, `'MODERATOR'`, `'ADMIN'` |
| So sánh trong policy handler | **chuỗi** |

Phép dịch `'MODERATOR'` → `role_id = 2` (để join sang `role_permissions`) nằm gọn trong
repository. Token và policy handler từ đầu đến cuối chỉ làm việc với chuỗi.

**Vì sao chuỗi chứ không phải số:** token tự mô tả — đọc log là hiểu ngay, không phải tra bảng.
Và `role_id` có thể lệch giữa các môi trường nếu seed chạy khác thứ tự, còn `'ADMIN'` thì ở đâu
cũng là `'ADMIN'`.

#### "Bất biến theo hợp đồng API" nghĩa là gì

Nghĩa là **không endpoint nào cho phép sửa `code`**. Endpoint sửa vai trò (GĐ6) chỉ nhận
`display_name`:

```json
PATCH /api/v1/admin/roles/2
{ "displayName": "Điều hành viên" }   ← được
{ "code": "MOD" }                      ← API không nhận trường này
```

Nói cho chính xác về mức độ đảm bảo: đây là **quy ước ở tầng API**, không phải ràng buộc cứng —
chạy `UPDATE roles SET code='MOD'` bằng tay trong psql thì DB vẫn cho. Cái bắt được chuyện đó là
**kiểm tra lúc khởi động** (Mục 5.5): lần deploy hoặc restart kế tiếp, app từ chối chạy kèm thông
báo rõ ràng thay vì hỏng âm thầm.

Vì sao phải bất biến: `code` là chuỗi mà JWT, policy, seeder và test đều bám vào. Hai mức hậu quả
rất khác nhau:

**Đổi `MODERATOR` → `MOD`** — nặng nhưng hồi phục được. Token đang lưu hành mang
`"role": "MODERATOR"` tra không ra vai trò nào → toàn bộ kiểm duyệt viên mất quyền. Đăng nhập lại
thì token mới ghi `"MOD"`, tra ra `role_id = 2`, các dòng `role_permissions` vẫn nguyên → quyền
trở lại.

**Đổi `ADMIN` → `ROOT`** — nghiêm trọng hơn hẳn. Phép so `role == "ADMIN"` trong short-circuit
(Mục 3.2) **không bao giờ khớp nữa**, kể cả sau khi đăng nhập lại. Và vì Admin cố ý không có dòng
nào trong `role_permissions`, không có gì để rơi về — nhánh tra bảng trả về tập rỗng. Kết quả:
**mất sạch quyền quản trị, im lặng, không có đường phục hồi** ngoài việc lại vào psql sửa ngược.

Đây chính là kịch bản mà biện pháp ở Mục 5.5 sinh ra để bắt.

#### Hệ quả: đổi vai trò của user không có hiệu lực ngay

Vai trò được **đóng dấu** vào token lúc phát hành. Token cũ mang `"role": "USER"` sẽ giữ nguyên
chữ đó cho tới khi hết hạn — server không tra lại DB ở tầng 2 để xem vai trò có đổi không.
Đây là cái giá của thiết kế stateless (ADR-002), không phải bug. **Đến GĐ6 mà không nhớ điều này
thì sẽ tưởng là bug.**

Cách làm cho thay đổi có hiệu lực, theo thứ tự chi phí:

Bốn cách khả dĩ, đã cân nhắc:

| Cách | Hiệu lực | Chi phí | Chọn? |
|---|---|---|---|
| User tự đăng xuất rồi đăng nhập lại | **Ngay** — server phát token mới, đọc vai trò mới từ DB | 0 — nhưng **không ép được** | Bổ trợ |
| Thu hồi `family_id` refresh token | Access token cũ vẫn sống **tối đa 15 phút**, sau đó không refresh được nữa → văng ra | 1 `UPDATE`, đã có sẵn | Dùng cho khóa/xóa tài khoản |
| Denylist theo `jti` | Ngay lập tức, nhưng **chỉ cho 1 token biết trước** | 1 lookup Redis/request | ❌ — xem bên dưới |
| **Thu hồi theo `user` + `iat`** | **Ngay lập tức, mọi thiết bị** | 1 `GET` Redis/request (~0,3ms) | ✅ **ĐÃ CHỐT** |

**Vì sao không dùng denylist theo `jti`.** `jti` định danh **một** access token. Nhưng một người
dùng có thể đang có nhiều access token còn sống cùng lúc — điện thoại, laptop, máy ở trường.
Hạ quyền thì phải chặn **tất cả**, mà server lại không lưu danh sách `jti` nào đang lưu hành —
đó chính là bản chất stateless. Không biết `jti` thì không `SET` được key nào. `jti` chỉ hợp cho
việc khác: cắt đúng **một** phiên đã biết (nút "Đăng xuất khỏi thiết bị này").

**Phương án đã chốt — thu hồi theo `user` + `iat`.** Thay vì liệt kê từng token, tuyên bố một mốc
thời gian: *"mọi token của user X phát trước thời điểm T đều vô hiệu"*. Một key duy nhất cho mỗi
user, không cần biết trước có bao nhiêu token đang sống.

**Áp cho cả nâng quyền lẫn hạ quyền** — cơ chế giống nhau, chỉ khác ở chỗ có thu hồi kèm refresh
family hay không:

- **Nâng quyền** (User → Moderator): thu hồi access, **giữ** refresh family → client tự refresh
  trong nền, nhận quyền mới ngay, **không bị đăng xuất**.
- **Hạ quyền** (Moderator → User): y hệt — mất quyền ngay, vẫn giữ phiên.
- **Khóa / xóa tài khoản**: thu hồi **cả hai** → văng ra hoàn toàn.

> 📖 Cơ chế đầy đủ, ví dụ theo mốc thời gian, thứ tự thao tác và các cạm bẫy: **Mục 7.5**.

### 3.2 Admin short-circuit ở tầng 2, tuyệt đối không ở tầng 3

#### Short-circuit là gì

Là "đoản mạch" — gặp điều kiện đủ thì trả kết quả ngay, bỏ qua phần còn lại. Giống hệt `a || b`
trong lập trình: `a` đúng rồi thì không cần tính `b` nữa.

Ở đây: handler tầng 2 thấy `role == "ADMIN"` thì cho qua luôn, **không** tra bảng
`role_permissions`.

```csharp
var role = ctx.User.FindFirstValue("role");   // luôn là chuỗi, với MỌI vai trò

if (role == RoleCodes.Admin) { ctx.Succeed(req); return; }   // "ADMIN"    → đi lối tắt
var granted = await _permissionCache.GetAsync(role);         // "USER", "MODERATOR" → tra bảng
if (granted.Contains(req.Permission)) ctx.Succeed(req);
```

Cả hai nhánh đều so sánh chuỗi với chuỗi. **Admin khác biệt ở cách xử lý, không phải ở kiểu dữ
liệu** — token của Admin và của User cùng một dạng, cùng mang `role` là chuỗi (Mục 3.1).

> Ví như soát vé: mọi hành khách đều cầm vé ghi hạng bằng chữ. Nhân viên thấy chữ "VIP" thì cho
> vào thẳng, thấy chữ "Thường" thì mở danh sách ra tra xem ghế nào. Hai tấm vé cùng loại giấy,
> cùng in chữ — chỉ quy trình xử lý khác nhau.

Đó là lý do Admin **không cần dòng nào** trong `role_permissions`: không có ai đi tra bảng cho
Admin cả.

#### Đánh đổi

- **Được:** thêm permission mới về sau, Admin tự động có — không bao giờ xảy ra cảnh deploy xong
  Admin bị chặn khỏi chính tính năng mới vì quên một dòng `INSERT`.
- **Mất:** ma trận trong DB không phản ánh Admin. Bù lại bằng audit — mọi thao tác Admin ghi
  `audit_logs` (DoD đã yêu cầu sẵn).

#### Ranh giới cứng: chỉ tầng 2, không bao giờ tầng 3

Viết đúng chỗ (tầng 2 — "vai trò này có được làm *loại* hành động này không"):

```csharp
if (role == RoleCodes.Admin) { ctx.Succeed(req); return; }   // ✅ trong PermissionHandler
```

Viết nhầm chỗ (tầng 3 — "có được thao tác trên *đúng tài nguyên NÀY* không"):

```csharp
if (role == RoleCodes.Admin) return true;                    // ❌ trong service kiểm ownership
```

Một dòng đặt nhầm chỗ là Admin sửa được nội dung bài của bất kỳ ai, đọc được tin nhắn riêng của
bất kỳ ai — không phải vì có ai quyết định như vậy, mà vì `if` nằm sai tầng. Đúng cái lỗ IDOR mà
GOAL-03 muốn đóng, chỉ khác là nạn nhân đông hơn.

Admin ẩn bài vi phạm thì được — nhưng đó là quyền `post.hide`, một use case riêng có kiểm tra
riêng và có ghi `audit_logs`, chứ không phải hệ quả phụ của việc bỏ qua tầng 3. Mọi ngoại lệ kiểu
"Admin được xem cái này của người khác" phải là **quyết định có ý thức cho từng use case**.

### 3.3 Bảng `roles` tách `code` và `display_name`

```
code            display_name        đối tượng phục vụ
──────────────────────────────────────────────────────────────────────
ADMIN           Quản trị viên       code → cho máy (JWT, policy, seeder, test) — BẤT BIẾN
USER            Người dùng          display_name → cho người (UI, audit log) — sửa thoải mái
MODERATOR       Kiểm duyệt viên
```

Báo cáo v5.0 chỉ có một cột `name`. Không tách thì kẹt giữa hai lựa chọn đều dở: hoặc hiện thẳng
`MODERATOR` cho người dùng cuối đọc, hoặc cho phép sửa tên vai trò và thế là chuỗi dùng để kiểm
tra quyền cũng đổi theo.

> **Lưu ý trùng tên:** `profiles.display_name` (GĐ2) là tên hiển thị của *người dùng* — chính là
> trường mà tìm kiếm không dấu ở GĐ6 đánh index GIN. Hai cột cùng tên, khác bảng, khác ý nghĩa.

### 3.4 Không dùng cột `is_system` — đã cân nhắc và loại bỏ

Phương án từng được xét: thêm `roles.is_system boolean` để đánh dấu vai trò hệ thống, rồi dựng
trigger chặn xóa/đổi `code` dựa trên cột đó. **Đã loại bỏ hẳn, không phải hoãn.**

#### Lý do 1 — cột này thừa

`is_system` chỉ có nghĩa nếu **tập vai trò cần bảo vệ có thể thay đổi**, tức là sau này còn muốn
đánh dấu thêm vai trò thứ 4, thứ 5 là "hệ thống". Nhưng theo thiết kế đã chốt thì không bao giờ:
vai trò tạo sau GĐ6 đều là dữ liệu mở, xóa được sửa được — đó là toàn bộ mục đích của chúng.
Tập cần bảo vệ **vĩnh viễn là đúng ba cái**: `ADMIN`, `USER`, `MODERATOR`.

Tập đã cố định thì thứ gì bảo vệ chúng cũng gọi thẳng tên được, không cần cột cờ để tra:

```sql
-- Nếu GĐ6 muốn chặn cứng ở tầng DB, trigger nêu thẳng tên là đủ
IF OLD.code IN ('ADMIN','USER','MODERATOR') THEN
    RAISE EXCEPTION 'system role is immutable';
```

Thêm một cột chỉ để lặp lại thông tin đã cố định là dữ liệu thừa. Nếu mai kia thật sự cần bảo vệ
vai trò thứ 4, vẫn phải sửa seeder cho nó — thêm một dòng trong trigger là cùng một lượt việc,
không đắt hơn bật một boolean.

#### Lý do 2 — nó canh sai bất biến

Điều thực sự làm hệ thống chết không phải "có người xóa role Admin" mà là **"hệ thống còn 0 người
có quyền Admin"**. Hai bất biến khác nhau, và `is_system` chỉ chặn cái đầu. Xóa sạch các *user*
Admin trong khi *role* Admin vẫn nằm đó nguyên vẹn thì hệ thống hỏng y hệt — cái đáng canh nằm ở
`users`, không ở `roles`.

#### Thay bằng ba thứ, không cái nào cần cột mới

| # | Biện pháp | GĐ | Chặn được gì |
|---|---|---|---|
| 1 | `users.role_id` FK `ON DELETE RESTRICT` | **GĐ1** | Xóa vai trò còn người mang — tự động phủ `ADMIN` và `USER` vì hai vai trò này luôn có người |
| 2 | **Kiểm tra lúc khởi động, ngay trong seeder** (~5 dòng) | **GĐ1** | Ba `code` kỳ vọng bị đổi tên hoặc biến mất → app **từ chối khởi động** kèm thông báo rõ (Mục 5.5) |
| 3 | Bất biến **"luôn còn ≥ 1 Admin đang hoạt động"** | GĐ6 + GĐ8 | Hạ quyền / khóa / tự xóa Admin cuối cùng |

*(Tùy chọn ở GĐ6: nếu muốn chặn cứng ngay tại tầng DB thì thêm trigger nêu thẳng ba tên như đoạn
SQL trên. Vẫn không cần cột.)*

Biện pháp 2 là thứ bắt được kịch bản nguy hiểm nhất — xem Mục 5.5.

### 3.5 Bổ sung `refresh_tokens.family_id`

#### "Family" là gì và vì sao gọi là family

Refresh token **xoay vòng** (rotation): mỗi lần dùng thì token cũ chết và **sinh ra** token mới.
Cứ thế thành một dòng dõi, mà tổ tiên là token phát ra lúc đăng nhập:

```
Đăng nhập 10:00 ──→ r1   ← tổ tiên: MỘT lần đăng nhập
                     │ replaced_by_id
        refresh ──→ r2   ← con
                     │
        refresh ──→ r3   ← cháu
                     │
        refresh ──→ r4   ← chắt (đang sống)
```

`family_id` là **họ chung** của cả dòng dõi. Sinh ra một lần lúc đăng nhập, mọi đời con cháu
mang y nguyên: `r1.family_id = r2.family_id = r3.family_id = r4.family_id = F1`.

Tên "family" đến từ đó: chúng là một gia đình vì **cùng một gốc đăng nhập**, *không phải* vì cùng
một người dùng. X đăng nhập trên hai máy thì có **hai** family khác nhau, dù cùng một `user_id`:

```
User X (user_id cố định)
├── Đăng nhập laptop 10:00     → family F1 → r1 → r2 → r3 …
└── Đăng nhập điện thoại 11:00 → family F2 → s1 → s2 …
```

Nhờ vậy thu hồi F1 chỉ đá laptop ra, điện thoại vẫn dùng bình thường — đây là cách làm nút
"Đăng xuất khỏi thiết bị này".

#### Vì sao cần nó — câu chuyện đánh cắp

```
10:00  X đăng nhập                          → r1 (F1)
10:15  X refresh                            → r2 (F1),  r1 chết
10:20  Kẻ trộm chép được r2 từ máy X        (X không hề biết)
10:30  X refresh bằng r2                    → r3 (F1),  r2 chết
       X vẫn dùng bình thường, không có gì lạ.
10:35  Kẻ trộm đem r2 đi dùng
       → server tra: r2 đã chết (replaced_by_id đã có giá trị)
       → REUSE! Có kẻ đang dùng token đã bị thay thế.
```

Đến đây server đứng trước tình thế: **nó không biết ai là thật, ai là trộm**. Cả hai đều cầm
token thuộc dòng F1 — người thật giữ `r3`, kẻ trộm giữ `r2` — và không có cách nào phân biệt.

Cách xử lý duy nhất an toàn: **giết cả dòng họ**. Cả X lẫn kẻ trộm đều văng ra; X đăng nhập lại
bằng **mật khẩu** — thứ kẻ trộm không có.

```sql
UPDATE refresh_tokens SET revoked_at = now()
WHERE family_id = @family AND revoked_at IS NULL;
```

Một lần `UPDATE`, bất kể dòng họ dài bao nhiêu đời. Schema báo cáo chỉ có `replaced_by_id` — đủ
để *phát hiện* reuse, nhưng để *thu hồi cả chuỗi* thì phải bám theo linked list `r1→r2→r3→r4`,
mỗi bước một truy vấn.

Cột này có việc làm **ngay trong GĐ1** — reuse detection là tiêu chí nghiệm thu của chính giai
đoạn này — nên nó được thêm vào migration đầu, khác với cột `is_system` đã bị loại bỏ ở Mục 3.4.

---

## 4. Mô hình dữ liệu

Migration đầu tiên của module Identity. DDL dưới đây là đích đến; hiện thực qua EF Core migration,
không viết SQL tay vào repo.

```sql
CREATE EXTENSION IF NOT EXISTS citext;   -- email không phân biệt hoa thường

-- ENT-10 · roles
CREATE TABLE roles (
    role_id      smallint    PRIMARY KEY,
    code         varchar(30) NOT NULL UNIQUE,   -- BẤT BIẾN: JWT/policy/seeder/test bám vào
    display_name varchar(50) NOT NULL,
    description  varchar(120),
    created_at   timestamptz NOT NULL DEFAULT now(),
    updated_at   timestamptz NOT NULL DEFAULT now()
);

-- ENT-10a · permissions
CREATE TABLE permissions (
    permission_id smallint    PRIMARY KEY,
    code          varchar(40) NOT NULL UNIQUE,  -- dạng resource.action
    description   varchar(120)
);

-- ENT-10b · role_permissions — mỗi dòng = một dấu tick trong ma trận 6.7.2
CREATE TABLE role_permissions (
    role_id       smallint NOT NULL REFERENCES roles(role_id)             ON DELETE CASCADE,
    permission_id smallint NOT NULL REFERENCES permissions(permission_id) ON DELETE CASCADE,
    PRIMARY KEY (role_id, permission_id)
);

-- ENT-01 · users
CREATE TABLE users (
    user_id            uuid        PRIMARY KEY,              -- UUID v7
    email              citext      NOT NULL UNIQUE,
    password_hash      varchar(72) NOT NULL,                 -- BCrypt cost 12 (chuỗi 60 ký tự)
    role_id            smallint    NOT NULL REFERENCES roles(role_id) ON DELETE RESTRICT,
    email_verified_at  timestamptz,
    failed_login_count smallint    NOT NULL DEFAULT 0,
    locked_until       timestamptz,
    status             varchar(20) NOT NULL DEFAULT 'active',
    created_at         timestamptz NOT NULL DEFAULT now(),
    updated_at         timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT ck_users_status CHECK (status IN ('active','locked','disabled','deleted'))
);

-- ENT-11 · refresh_tokens
CREATE TABLE refresh_tokens (
    id             uuid        PRIMARY KEY,
    user_id        uuid        NOT NULL REFERENCES users(user_id) ON DELETE CASCADE,
    family_id      uuid        NOT NULL,                     -- BỔ SUNG (Mục 3.5)
    token_hash     varchar(64) NOT NULL UNIQUE,              -- SHA-256 hex, KHÔNG lưu bản rõ
    expires_at     timestamptz NOT NULL,
    revoked_at     timestamptz,
    replaced_by_id uuid        REFERENCES refresh_tokens(id),
    created_ip     inet,
    created_at     timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX idx_refresh_family ON refresh_tokens(family_id) WHERE revoked_at IS NULL;
CREATE INDEX idx_refresh_user   ON refresh_tokens(user_id);

-- Token xác minh email
CREATE TABLE email_verification_tokens (
    id          uuid        PRIMARY KEY,
    user_id     uuid        NOT NULL REFERENCES users(user_id) ON DELETE CASCADE,
    token_hash  varchar(64) NOT NULL UNIQUE,
    expires_at  timestamptz NOT NULL,
    consumed_at timestamptz
);
```

**Gotcha .NET 8:** `Guid.CreateVersion7()` chỉ có từ .NET 9. Với .NET 8 phải dùng thư viện
(`UUIDNext`) hoặc tự sinh. Đừng dùng `Guid.NewGuid()` (v4 ngẫu nhiên) — mất tính tuần tự,
gây phân mảnh index B-tree, đúng thứ mà GĐ4 sẽ trả giá khi feed cần index tốt.

---

## 5. Dữ liệu seed

### 5.1 Vai trò

| role_id | code | display_name |
|---|---|---|
| 1 | `USER` | Người dùng |
| 2 | `MODERATOR` | Kiểm duyệt viên |
| 3 | `ADMIN` | Quản trị viên |

`MODERATOR` vẫn được seed dù không phải "vai trò hệ thống", để AC của US-019 (GĐ6) luôn có vai
trò để bám. Nếu không seed, integration test phải tự dựng role và tự gán quyền trong fixture —
làm được, nhưng khi đó test tự định nghĩa lấy quyền của Moderator và không còn kiểm chứng cấu
hình thật nữa.

### 5.2 Quyền — toàn bộ ma trận Mục 6.7.2

| id | code | id | code |
|---|---|---|---|
| 1 | `post.read.public` | 10 | `friend.respond` |
| 2 | `post.read.friends` | 11 | `message.send` |
| 3 | `post.create` | 12 | `report.create` |
| 4 | `post.update` | 13 | `report.resolve` |
| 5 | `post.delete` | 14 | `user.lock` |
| 6 | `post.hide` | 15 | `user.unlock` |
| 7 | `comment.create` | 16 | `role.assign` |
| 8 | `reaction.set` | 17 | `audit.read` |
| 9 | `friend.request` | | |

### 5.3 Gán quyền

| Vai trò | Quyền |
|---|---|
| `USER` | 1, 2, 3, 4, 5, 7, 8, 9, 10, 11, 12 |
| `MODERATOR` | tất cả của USER + 6 (`post.hide`) + 13 (`report.resolve`) |
| `ADMIN` | **không dòng nào** — short-circuit tầng 2 (Mục 3.2) |

Điều kiện `(own)` và `(bạn)` trong ma trận gốc **không** biểu diễn được bằng dòng
`role_permissions`. Chúng là logic nghiệp vụ ở tầng 3 (Mục 6.3).

### 5.4 Seeder phải idempotent

Seeder chạy mỗi lần app khởi động, kể cả khi CD deploy lại staging 10 lần/ngày.

```sql
INSERT INTO permissions (permission_id, code, description)
VALUES (6, 'post.hide', 'Ẩn bài vi phạm (BR-07)')
ON CONFLICT (code) DO NOTHING;
```

**Dùng `DO NOTHING`, không dùng `DO UPDATE`.** Đây là điểm dễ sai nhất: từ GĐ6 Admin sẽ sửa được
`role_permissions` lúc runtime. Nếu seeder dùng `DO UPDATE` thì lần deploy kế tiếp sẽ lặng lẽ
cấp lại đúng cái quyền mà Admin vừa cố tình gỡ đi. Viết test cho chuyện này ngay từ bây giờ
(SEED-02 ở Mục 10).

### 5.5 Kiểm tra lúc khởi động — thay cho `is_system`

Chạy ngay sau seeder, khoảng 5 dòng. Đây là biện pháp #2 ở Mục 3.4, thay thế cho cột `is_system`
đã bị loại bỏ.

```csharp
// Sau khi seed xong
var expected = new[] { RoleCodes.Admin, RoleCodes.User, RoleCodes.Moderator };
var actual   = await db.Roles.Select(r => r.Code).ToListAsync(ct);
var missing  = expected.Except(actual).ToArray();

if (missing.Length > 0)
    throw new InvalidOperationException(
        $"Thiếu vai trò hệ thống: {string.Join(", ", missing)}. " +
        "Có thể ai đó đã đổi roles.code bằng tay. App từ chối khởi động.");
```

**Nó bắt được gì:** kịch bản `ADMIN → ROOT` ở Mục 3.1 — mất sạch quyền quản trị, im lặng, không
đường phục hồi. Với kiểm tra này thì lần deploy hoặc restart kế tiếp app **từ chối chạy** kèm
thông báo chỉ thẳng nguyên nhân.

**Nó không bắt được gì:** thời điểm ai đó gõ `UPDATE` trong psql. Ứng dụng đang chạy vẫn hỏng cho
tới lần restart. Đây là đánh đổi có ý thức — chặn đúng thời điểm ghi cần trigger ở tầng DB, mà
trigger chỉ đáng làm khi GĐ6 đã có endpoint sửa vai trò thật.

**Vì sao đặt trong seeder** chứ không phải một `IHostedService` riêng: seeder vốn đã chạy mỗi lần
khởi động và vốn đã đọc bảng `roles`. Thêm việc vào đó không tốn thêm truy vấn nào và không thể
quên gọi.

---

## 6. Ba tầng kiểm soát truy cập

Đây là phần có giá trị dài hạn nhất của GĐ1: GĐ2–GĐ6 chỉ việc dùng lại, không module nào được
tự chế cách kiểm tra riêng.

### 6.0 Tổng quan — có đúng ba tầng

Theo Mục 6.7.1 của báo cáo. **Mọi request đều đi qua cả ba**; thiếu một tầng là thủng.

| Tầng | Câu hỏi cần trả lời | Cách kiểm tra | Nằm ở đâu |
|---|---|---|---|
| **1. AuthN** — Xác thực | Người gọi **là ai**? | Verify chữ ký JWT + `exp`. Không truy vấn DB | Middleware |
| **2. RBAC** — Phân quyền vai trò | **Vai trò này** có được làm *loại* hành động này không? | `role` trong token + bảng `role_permissions` (cache) | `[RequirePermission]` + policy handler |
| **3. Ownership** — Quyền trên tài nguyên | Người này có được thao tác trên **đúng tài nguyên NÀY** không? | Quan hệ sở hữu / bạn bè — **có truy vấn dữ liệu thật** | Tầng service của module |

> Báo cáo nói thẳng: *"Bỏ sót tầng 3 là lỗ hổng IDOR — nhóm lỗi phổ biến nhất với loại ứng dụng
> này."* RBAC một mình không bảo vệ được tài nguyên thuộc sở hữu người dùng.

#### Ví dụ chạy qua đủ ba tầng

**Tình huống: An gửi `PATCH /api/v1/posts/{id bài của Bình}`** — An là `USER`:

```
Tầng 1: Token hợp lệ, chữ ký đúng, chưa hết hạn?   → OK, sub = An, role = "USER"
Tầng 2: Vai trò USER có quyền post.update không?   → CÓ (có dòng trong role_permissions)
Tầng 3: Bài này có phải của An không?              → KHÔNG, của Bình
                                                   → 403   ← chặn ở đây
```

Chú ý **tầng 2 cho qua**. An đúng là có quyền sửa bài — chỉ là sửa bài *của mình*. Chữ `(own)`
trong ma trận quyền không biểu diễn được bằng dòng `role_permissions`, nó buộc phải là logic ở
tầng 3. Thiếu tầng 3 thì An sửa được bài của bất kỳ ai — đó chính là IDOR (TC-A03 ở GĐ2).

**Cùng request đó, người gọi là Cường (`ADMIN`):**

```
Tầng 1: Token hợp lệ                               → OK, role = "ADMIN"
Tầng 2: role == "ADMIN"                            → SHORT-CIRCUIT, qua ngay, không tra bảng
Tầng 3: VẪN CHẠY BÌNH THƯỜNG                       → ...
```

Tầng 3 không có ngoại lệ cho Admin (Mục 3.2).

**`PATCH /api/v1/posts/{id}/hide`, người gọi là Bình (`MODERATOR`):**

```
Tầng 1: Token hợp lệ                               → OK, role = "MODERATOR"
Tầng 2: KHÔNG short-circuit (chuỗi ≠ "ADMIN")
        → tra cache/DB: MODERATOR có post.hide?    → CÓ
Tầng 3: kiểm tra nghiệp vụ (bài tồn tại, chưa bị ẩn…) + ghi audit_log
```

Bước "tra cache/DB" bên trong mới dịch `'MODERATOR'` → `role_id = 2` để join sang
`role_permissions`. Phép dịch đó nằm gọn trong repository, không lộ ra ngoài.

### 6.1 Tầng 1 — AuthN (xác thực)

JWT Bearer middleware, HS256, verify chữ ký + `exp`. Không truy vấn DB.

Claims tối thiểu theo Mục 6.7.3: `sub` (user_id), `role` (code), `iat`, `exp`, `jti`.

Bật **fallback policy** để mặc định mọi endpoint đều yêu cầu đăng nhập — endpoint công khai
phải khai báo `[AllowAnonymous]` tường minh. Đây là cách rẻ nhất để hiện thực "default deny":

```csharp
options.FallbackPolicy = new AuthorizationPolicyBuilder()
    .RequireAuthenticatedUser()
    .Build();
```

### 6.2 Tầng 2 — RBAC (`[RequirePermission]`)

Đặt trong **SharedKernel**, viết một lần, mọi module dùng chung. ArchUnitNET đã chặn tham chiếu
chéo nên đây là chỗ duy nhất hợp lệ để đặt nó.

```csharp
// SharedKernel/Authorization/RequirePermissionAttribute.cs
public sealed class RequirePermissionAttribute : AuthorizeAttribute
{
    public const string PolicyPrefix = "perm:";
    public RequirePermissionAttribute(string permission) => Policy = PolicyPrefix + permission;
}
```

Vì tên policy sinh động, cần cài `IAuthorizationPolicyProvider` riêng để dựng policy theo yêu
cầu, thay vì đăng ký sẵn từng cái trong `Program.cs`.

```csharp
public sealed class PermissionHandler : AuthorizationHandler<PermissionRequirement>
{
    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext ctx, PermissionRequirement req)
    {
        var role = ctx.User.FindFirstValue("role");
        if (role is null) return;                                   // tầng 1 chưa qua -> deny

        if (role == RoleCodes.Admin) { ctx.Succeed(req); return; }  // short-circuit (Mục 3.2)

        var granted = await _permissionCache.GetAsync(role);        // đọc DB, cache TTL 60s
        if (granted.Contains(req.Permission)) ctx.Succeed(req);
        // KHÔNG gọi ctx.Fail() -> để mặc định deny, tránh chặn nhầm handler khác
    }
}
```

Cách dùng ở mọi module về sau:

```csharp
[HttpPatch("{id}/hide")]
[RequirePermission("post.hide")]
public async Task<IActionResult> Hide(Guid id) { ... }
```

### 6.3 Tầng 3 — Ownership / quan hệ

GĐ1 gần như chưa có tài nguyên nào để sở hữu, **nhưng phải đặt khuôn ngay**, vì GĐ2 cần nó lập
tức (TC-A03: user A `PATCH` bài của B → 403). Không đặt khuôn thì mỗi module tự nghĩ ra một kiểu
và IDOR lọt qua đúng những khe đó — đây là lỗi phổ biến nhất với loại ứng dụng này (Mục 6.7.1).

Quy ước bắt buộc:

1. Kiểm tra ownership nằm ở **tầng service**, không ở controller, không ở attribute — vì nó cần
   truy vấn dữ liệu thật.
2. Service trả `Result.Forbidden(...)`; middleware SharedKernel map sang **403 RFC 7807**.
   Không ném exception cho luồng nghiệp vụ bình thường.
3. Thông điệp lỗi 403 **không được tiết lộ tài nguyên có tồn tại hay không**.
4. **Mỗi endpoint chạm tới tài nguyên có chủ sở hữu phải có một dòng tương ứng trong bảng
   AuthZ matrix** (Mục 10.2). Không có dòng test thì coi như endpoint chưa xong.

---

## 7. Luồng nghiệp vụ

### 7.0 Nền tảng — vì sao có hai loại token

Đọc mục này trước 7.2–7.5 nếu chưa quen với mô hình access + refresh.

#### Vấn đề

Access token phải **ngắn hạn** (15 phút) vì JWT là stateless — không thu hồi được. Token sống
càng lâu thì kẻ đánh cắp dùng được càng lâu. Nhưng ngắn hạn thì phiền: cứ 15 phút bắt người dùng
gõ lại mật khẩu là không ai chịu nổi.

```
Token dài hạn  → tiện, nhưng bị đánh cắp là toi, không cắt được
Token ngắn hạn → an toàn, nhưng 15 phút đăng nhập lại một lần
```

#### Cách giải: tách làm hai loại

Cái **đi khắp nơi** thì cho chết nhanh; cái **sống lâu** thì cất kỹ và giữ quyền cắt.

| | Access token (JWT) | Refresh token |
|---|---|---|
| Dùng để | Chứng minh danh tính ở **mỗi** request | Xin access token mới khi cái cũ hết hạn |
| Đi kèm request nào | **Mọi** request | Chỉ `/auth/refresh`, ~1 lần/15 phút |
| Chứa gì | `sub`, `role`, `iat`, `exp`, `jti` — đọc được | Chuỗi ngẫu nhiên 32 byte, **không chứa thông tin gì** |
| Server có lưu không | **Không** (stateless) | **Có** — bảng `refresh_tokens`, dạng băm |
| Sống bao lâu | 15 phút | Vài ngày |
| Có `family_id` không | Không | **Có** |
| Thu hồi bằng | `revoked:user:<id>` + `iat` (Redis) — Mục 7.5 | `UPDATE ... SET revoked_at` (DB) |

Ví như đi xe buýt: **vé tháng** cất trong ví, chỉ móc ra ở quầy để đổi vé lượt — mất thì báo quầy
hủy. **Vé lượt** cầm tay đưa cho tài xế mỗi chuyến, hết hạn trong ngày nên rơi mất cũng chẳng ai
dùng được.

#### Hai điều dễ hiểu nhầm

**JWT không phải mã định danh của người dùng.** Nó là *giấy thông hành có hạn*, bên trong có
*chứa* mã định danh ở claim `sub`:

```
user_id  = 0192f3c1-…      ← mã định danh. Cố định vĩnh viễn. PK của bảng users
JWT      = abcd… → efgh…   ← giấy thông hành. Đổi mới mỗi 15 phút
```

Một user trong đời đi qua hàng nghìn JWT khác nhau; `user_id` thì chỉ có một.

**Refresh token của chúng ta không phải JWT.** Nó chỉ là 32 byte ngẫu nhiên. Đúng ra phải thế:
nó cần được tra trong DB mỗi lần dùng, nên không cần tự mang thông tin — và nhờ lưu được ở DB
mới thu hồi được.

### 7.1 Đăng ký + xác minh email (FR-001)

1. `POST /auth/register` → validate (FluentValidation): email đúng định dạng, mật khẩu ≥ 8 ký tự
2. Email đã tồn tại → **409**
3. Hash mật khẩu BCrypt cost 12
4. `INSERT users` với `role_id = 1 (USER)`, `email_verified_at = NULL`
5. Sinh token xác minh (ngẫu nhiên 32 byte), lưu **băm**, hạn 24 giờ
6. Gửi mail qua `IEmailSender` → Mailpit ở dev/staging
7. `POST /auth/verify-email` với token → đối chiếu băm → set `email_verified_at`, `consumed_at`

Token xác minh cũng lưu băm, cùng lý do với refresh token: rò rỉ DB không được kéo theo rò rỉ
tài khoản.

### 7.2 Đăng nhập + lockout (FR-002, FR-003)

```
1. Tra user theo email
2. Không tồn tại      -> CHẠY MỘT PHÉP BCRYPT GIẢ rồi trả 401
                         (không bỏ qua — nếu không, thời gian phản hồi lộ ra
                          email nào có thật)
3. locked_until > now()          -> 423 Locked
4. Sai mật khẩu                  -> failed_login_count++ ; nếu đạt 5 thì
                                    locked_until = now() + 15 phút, reset count -> 401
5. email_verified_at IS NULL     -> 403
6. Thành công                    -> reset count + clear locked_until
                                 -> phát access (15') + refresh (family_id mới)
```

**AC-02 nói rõ: 401 không được lộ email tồn tại hay không.** Thông điệp duy nhất cho cả hai
trường hợp: *"Email hoặc mật khẩu không đúng."*

**Concurrency:** cập nhật `failed_login_count` bằng `UPDATE ... RETURNING` nguyên tử, không
đọc-rồi-ghi. Tấn công dò mật khẩu song song sẽ bắn nhiều request cùng lúc — read-modify-write
sẽ đếm sót và lockout không bao giờ kích hoạt.

### 7.3 Refresh rotation + reuse detection (NFR-SEC-03)

Toàn bộ trong **một transaction**, `SELECT ... FOR UPDATE` trên dòng token.

```
1. Băm SHA-256 token nhận được -> tra theo token_hash
2. Không thấy                                  -> 401
3. revoked_at IS NOT NULL  hoặc  replaced_by_id IS NOT NULL
       -> REUSE -> thu hồi toàn bộ family_id   -> 401
4. expires_at < now()                          -> 401
5. Hợp lệ -> sinh token mới CÙNG family_id
          -> token cũ: revoked_at = now(), replaced_by_id = <id mới>
          -> trả cặp token mới
```

> **Cạm bẫy thực tế — hai tab refresh cùng lúc.** Cả hai gửi cùng một refresh token; một cái
> thắng, cái còn lại rơi vào bước 3 và bị coi là reuse → thu hồi cả chuỗi → người dùng bị đăng
> xuất đột ngột mà không hiểu vì sao. Đây là lỗi kinh điển của refresh rotation.
>
> **Cách xử lý khuyến nghị:** cho phép một khoảng ân hạn ngắn (~10 giây) — nếu token đã bị thay
> thế trong vòng 10 giây **và chuỗi chưa bị thu hồi**, trả lại token kế nhiệm thay vì coi là
> reuse. Đánh đổi: có cửa sổ 10 giây mà token bị đánh cắp vẫn dùng được. Chấp nhận được, nhưng
> phải ghi vào tài liệu để lúc bảo vệ giải thích được lựa chọn này.

### 7.4 Đăng xuất

`POST /auth/logout` → thu hồi toàn bộ `family_id` của refresh token gửi lên. Báo cáo yêu cầu
"đổi mật khẩu/đăng xuất thu hồi toàn bộ phiên" (Mục 6.7.3) nên endpoint này thuộc GĐ1.

### 7.5 Thu hồi token khi đổi vai trò / khóa tài khoản (`user` + `iat`)

Phương án đã chốt ở Mục 3.1. Bên **đọc** (kiểm tra mỗi request) làm ở GĐ1; bên **ghi** đầy đủ
(hạ quyền, khóa tài khoản) ở GĐ6 — GĐ1 chỉ nối một trigger duy nhất là reuse detection.

#### Cơ chế

**Khi thu hồi** — ghi một mốc thời gian cho user đó:

```
SET  revoked:user:0192f3c1-…   1757116800   EX 900
                               └─ Unix timestamp lúc thu hồi
```

**Mỗi request**, sau khi verify chữ ký:

```
GET revoked:user:<sub>
   → không có key         → cho qua
   → có, và iat <  value  → 401  (token phát TRƯỚC mốc thu hồi)
   → có, và iat >= value  → cho qua (token phát SAU, hợp lệ)
```

Một key duy nhất cho mỗi user, bất kể họ đang đăng nhập trên bao nhiêu thiết bị. Đây là lý do
claim `iat` có mặt trong token — không phải để trang trí.

#### Vì sao TTL đúng 900 giây

Không phải con số tùy tiện: **bằng đúng TTL của access token**. Sau 15 phút, mọi token phát trước
mốc thu hồi đều đã tự hết hạn theo `exp` — key không còn tác dụng, giữ lại chỉ tốn RAM. Ngắn hơn
thì thủng: key hết hạn ở phút thứ 10, token bị thu hồi lại được chấp nhận trong 5 phút cuối.

> ⚠️ Hai con số này **phải trỏ về cùng một hằng số cấu hình**. Đổi access TTL thành 10 phút mà
> quên đổi TTL denylist là tự tạo một lỗ hổng câm, không test nào bắt được nếu không nghĩ tới.

#### Đặt kiểm tra ở đâu

Tầng 1 (AuthN), **không phải** tầng 2 — câu hỏi là "token này còn hiệu lực không", chứ không phải
"vai trò này có quyền gì":

```csharp
options.Events = new JwtBearerEvents
{
    OnTokenValidated = async ctx =>          // chạy SAU khi chữ ký đã hợp lệ
    {
        var sub = ctx.Principal.FindFirstValue("sub");
        var iat = long.Parse(ctx.Principal.FindFirstValue("iat"));

        var store = ctx.HttpContext.RequestServices
                       .GetRequiredService<ITokenRevocationStore>();

        if (await store.IsRevokedAsync(sub, iat))
            ctx.Fail("token revoked");        // → 401
    }
};
```

Đặt sau bước verify chữ ký để token giả không tốn một lượt truy vấn Redis.

#### Ví dụ: hạ quyền X từ MODERATOR xuống USER

```
10:00:00  X đăng nhập
          → access abcd (role=MODERATOR, iat=10:00:00, exp=10:15:00) + refresh family F1

10:07:30  Admin hạ quyền X
          ① UPDATE users SET role_id = 1 WHERE user_id = X      (DB  — TRƯỚC)
          ② SET revoked:user:X = 10:07:30  EX 900               (Redis — SAU)

10:07:31  X bấm "Ẩn bài", gửi kèm abcd
          Tầng 1: chữ ký OK → sub=X, iat=10:00:00
                  GET revoked:user:X → 10:07:30
                  10:00:00 < 10:07:30 → TOKEN ĐÃ THU HỒI → 401

10:07:31  Client thấy 401 → tự gọi POST /auth/refresh
          (family F1 KHÔNG bị thu hồi — hạ quyền không đụng tới nó)
          → server đọc vai trò MỚI từ DB → phát access efgh (role=USER, iat=10:07:31)

10:07:31  Client thử lại "Ẩn bài" với efgh
          Tầng 1: iat=10:07:31 >= 10:07:30 → cho qua
          Tầng 2: USER có post.hide không? → KHÔNG → 403
```

Độ trễ thực tế: **một round-trip phụ, dưới một giây**. X **không bị đăng xuất** — phiên vẫn còn,
chỉ là token được thay bằng cái mang vai trò mới. Vai trò và quyền đổi **cùng lúc**, không có
trạng thái "quyền đã hạ nhưng role chưa đổi".

Không có cơ chế này thì X giữ nguyên quyền mod thêm **tối đa 15 phút** — với người đang phá hoại
thì đó là quá nhiều.

#### Thu hồi access và refresh là hai việc khác nhau

| Tình huống | Thu hồi access (`iat`) | Thu hồi refresh (`family_id`) | Người dùng thấy gì | GĐ |
|---|---|---|---|---|
| **Nâng quyền** User→Mod | ✔ | ✖ | Client tự refresh → có quyền mới ngay, **không phải đăng nhập lại** | GĐ6 |
| **Hạ quyền** Mod→User | ✔ | ✖ | Mất quyền ngay, vẫn giữ phiên | GĐ6 |
| **Khóa tài khoản** | ✔ | ✔ | Văng ra hoàn toàn | GĐ6 |
| **Xóa tài khoản** | ✔ | ✔ | Văng ra hoàn toàn | GĐ8 |
| **Phát hiện reuse** | ✔ | ✔ | Nghi bị đánh cắp → cắt sạch | **GĐ1** |
| Đăng xuất 1 thiết bị | ✖ | ✔ (chỉ family đó) | Thiết bị khác không ảnh hưởng | GĐ1 |

#### Ba cạm bẫy

**1. Thứ tự ① ② không được đảo.** Nếu `SET` Redis trước rồi mới `UPDATE` DB:

```
10:07:30.000  SET revoked:user:X = 10:07:30
10:07:30.020  X refresh → server đọc DB, role VẪN là MODERATOR (chưa update)
              → phát token mới role=MODERATOR, iat=10:07:30.020
10:07:30.050  UPDATE users SET role_id = 1        ← quá muộn
```

Token mới có `iat` **sau** mốc thu hồi nên qua được denylist, mà lại mang vai trò cũ → X giữ
quyền mod thêm 15 phút và lần này không gì chặn được. **DB trước, Redis sau** thì an toàn.

**2. Client phải biết tự refresh khi gặp 401.** Frontend Next.js cần interceptor: gặp 401 thì gọi
`/auth/refresh` một lần rồi thử lại request. Không có nó thì người dùng chỉ thấy màn hình lỗi và
phải đăng nhập tay — vẫn an toàn, chỉ là trải nghiệm tệ.

> **Ai làm:** lane frontend của **chính GĐ1** (Mục 9, Ngày 5) — kế hoạch đã chuyển sang lát cắt
> dọc nên interceptor xong cùng giai đoạn với backend, không còn hoãn tới GĐ2. Nghĩa là thiết kế
> thu hồi token ở mục này được kiểm chứng **trong trình duyệt thật ngay ở cổng đóng GĐ1** (E2E-01),
> bên cạnh integration test RV-01…RV-04.
>
> **Bắt buộc single-flight:** nhiều request nhận 401 cùng lúc chỉ được gọi `/auth/refresh` **một
> lần**, số còn lại xếp hàng chờ. Gọi song song là tự kích hoạt reuse detection ở Mục 7.3 và người
> dùng bị đăng xuất oan — ân hạn 10 giây che được phần lớn nhưng không phải tất cả (test E2E-02).

**3. `iat` có độ phân giải giây.** Thu hồi lúc `T` rồi user đăng nhập lại ngay trong cùng giây đó
thì token mới cũng có `iat = T`. Dùng so sánh **chặt** `iat < revoked_at` (không phải `<=`) thì
token mới được chấp nhận đúng như mong muốn. Đổi lại có khe hở dưới 1 giây cho token phát ra ngay
trước lúc thu hồi trong cùng giây — chấp nhận được ở quy mô này.

#### Khi Redis chết

- **Fail-open** (bỏ qua kiểm tra, cho request đi tiếp): token đã thu hồi sống lại trong lúc Redis
  hỏng, nhưng cửa sổ phơi nhiễm **tối đa 15 phút**, và refresh token vẫn bị chặn ở DB nên sau đó
  họ vẫn văng ra.
- **Fail-closed** (chặn hết): Redis hỏng là cả trang web sập.

**Chọn fail-open**, kèm log mức warning và alert — nhất quán với cách GĐ4 xử lý Redis chết (đọc
thẳng DB) và phù hợp GOAL-04 uptime ≥ 99%. Đây là **quyết định có ý thức**, không phải chuyện bỏ
quên, nên phải ghi vào tài liệu vận hành.

#### Đừng nhầm với cache quyền TTL 60s

Cache đó lưu ánh xạ *vai trò → danh sách quyền* (`MODERATOR` có những quyền gì). Hạ quyền X là đổi
*user → vai trò*, không đụng tới `role_permissions`, nên 60 giây đó **không liên quan** ở đây.
Nó chỉ có vai trò khi Admin sửa quyền của cả một vai trò (GĐ6).

---

## 8. Hợp đồng API

Base: `/api/v1`. Mọi lỗi trả **RFC 7807 Problem Details** kèm `traceId`.
Rate limit **10 req/phút** cho toàn nhóm `auth` (ISS-04).

| Method | Path | Auth | Thành công | Lỗi |
|---|---|---|---|---|
| POST | `/auth/register` | — | 201 | 400 validation · 409 email đã tồn tại |
| POST | `/auth/verify-email` | — | 200 | 400 token sai · 410 token hết hạn/đã dùng |
| POST | `/auth/login` | — | 200 `{accessToken, expiresIn}` + **`Set-Cookie` refresh token** | 401 sai thông tin · 403 chưa verify · 423 bị khóa |
| POST | `/auth/refresh` | Cookie | 200 `{accessToken, expiresIn}` + `Set-Cookie` mới | 401 (gồm cả reuse) · **không nhận body** |
| POST | `/auth/logout` | Bearer + Cookie | 204 + xóa cookie | 401 |
| GET | `/me` | Bearer | 200 hồ sơ rút gọn + role | 401 |

`GET /me` là smoke test rẻ nhất chứng minh tầng 1 + tầng 2 chạy thông từ đầu đến cuối — giữ nó.

#### Refresh token đi trong cookie, không đi trong body

Quyết định đã chốt (`ke-hoach-trien-khai.md` GĐ1, quyết định 6):

```
Set-Cookie: refresh_token=<32 byte ngẫu nhiên>;
            HttpOnly; Secure; SameSite=Lax; Path=/api/v1/auth; Max-Age=604800
```

- **`HttpOnly`** — JS không đọc được. Refresh token là thứ sống lâu nhất và nguy hiểm nhất nếu bị
  XSS lấy mất; để trong `localStorage` là đặt nó ngay tầm tay kẻ tấn công.
- **`Path=/api/v1/auth`** — cookie chỉ được gửi kèm các endpoint auth, không đính vào mọi request.
- **`SameSite=Lax`** — chặn CSRF cơ bản. Access token vẫn đi bằng header `Authorization: Bearer`
  nên các endpoint nghiệp vụ không phụ thuộc cookie.
- **Access token client giữ trong memory**, không `localStorage` — mất khi refresh trang là chấp
  nhận được, vì interceptor sẽ tự gọi `/auth/refresh` lấy cái mới.

> Đây là **quyết định hợp đồng API, không phải quyết định frontend** — nó đổi chữ ký của cả ba
> endpoint auth. Chốt ở GĐ1 chính vì thế: để muộn là phải mở lại hợp đồng vừa đóng băng.

#### CORS — cấu hình ngay ở GĐ1

Nhánh frontend khởi động ngày đầu GĐ2 (`ke-hoach-trien-khai.md` Mục 0C); thiếu CORS là họ ngồi chờ.

```csharp
policy.WithOrigins(allowedOrigins)   // localhost:3000 (dev) + domain thật (staging)
      .AllowAnyHeader()
      .AllowAnyMethod()
      .AllowCredentials();           // BẮT BUỘC — không có thì cookie refresh không đi kèm
```

`AllowCredentials()` là điểm dễ sót: thiếu nó thì trình duyệt **im lặng không gửi cookie**, và
`/auth/refresh` luôn trả 401 mà không có thông báo lỗi nào chỉ ra nguyên nhân. Kèm theo ràng buộc
của chuẩn CORS: dùng `AllowCredentials()` thì **không được** dùng `AllowAnyOrigin()` — phải liệt
kê origin cụ thể.

> **Hợp đồng này được chốt ở cổng mở (Ngày 3 sáng) dưới dạng OpenAPI stub commit vào repo**, không
> phải đóng băng dần tới cuối giai đoạn. Người làm frontend dựng mock từ chính stub đó và bắt đầu
> ngay Ngày 3 — không có stub thì không ai code. Mọi thay đổi sau đó phải báo cả nhóm và **cập nhật
> stub trong cùng commit**. Cổng đóng (Ngày 6) là lúc hợp đồng đóng băng thật sự cho GĐ2.

---

## 9. Kế hoạch thi công (2 backend + 1 frontend · Ngày 3–6)

### 9.0 Dọn nợ kỹ thuật trước — nửa ngày đầu Ngày 3

GĐ0 đã giao lại một khung chắc chắn (RFC 7807, correlation ID, rate limit policy `"auth"`,
health check Postgres + Redis, hook `--migrate`, `public partial class Program`). Nhưng còn **bốn
khoản nợ** phải dọn trước khi viết dòng entity đầu tiên. Cả bốn nằm gọn trong khối A.

> **Trạng thái: đã dọn xong trước khi khối A bắt đầu.** Mục này giữ lại làm hồ sơ quyết định; phần
> ghi bằng chữ nghiêng *(thực tế)* là chỗ hiện thực khác với dự kiến ban đầu.

#### Nợ 1 — Identity module chưa có EF Core *(chặn mọi thứ)*

Hiện `SocialApp.Modules.Identity.csproj` chỉ tham chiếu SharedKernel.

```xml
<PackageReference Include="Microsoft.EntityFrameworkCore" Version="8.0.10" />
<PackageReference Include="Npgsql.EntityFrameworkCore.PostgreSQL" Version="8.0.10" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Design" Version="8.0.10" />
```

`Design` là gói riêng, chỉ phục vụ `dotnet ef migrations add` — thiếu nó lệnh báo lỗi rất khó đoán.

*(thực tế)* **Đặt `Design` ở module là chưa đủ.** EF tools đòi gói này ở **startup project**, mà
`Design` luôn đi kèm `<PrivateAssets>all</PrivateAssets>` nên **không chảy** từ Identity sang Api.
Hệ quả: lệnh `migrations add` với `--startup-project src/SocialApp.Api` báo *"Your startup project
'SocialApp.Api' doesn't reference Microsoft.EntityFrameworkCore.Design"* — đúng cái lỗi khó đoán vừa
nói tới.

Cách gỡ đã chọn: thêm `DesignTimeIdentityDbContextFactory` (`IDesignTimeDbContextFactory<>`) trong
`Identity/Infrastructure/`, rồi lấy **chính project Identity làm startup project**. Api nhờ vậy
không phải kéo EF vào — đúng ADR-001 — và GĐ2–GĐ6 mỗi module lặp lại đúng khuôn này cho context của
mình. Lệnh đầy đủ nằm ở `AGENTS.md` Mục 13.

Cấu hình Npgsql (chuỗi kết nối + bảng lịch sử migration) để ở **một chỗ duy nhất**
(`IdentityDbContextOptions.UseIdentityNpgsql`) vì giờ có hai đường dựng context — DI lúc chạy và
factory lúc design-time. Hai đường lệch nhau là lỗi câm: migration ghi lịch sử vào bảng khác với
bảng runtime đọc, EF tưởng chưa chạy và áp lại từ đầu.

> **Ràng buộc kiến trúc:** chỉ tầng `Infrastructure` được chạm EF. `Domain` và `Application` phải
> sạch, nếu không ArchUnitNET bắt (ADR-001) — luật này đã có test giữ:
> `PersistenceBoundaryTests`.

#### Nợ 2 — Chưa có `DbContext` nào trong toàn solution

**Quyết định: mỗi module một `DbContext` riêng, chung một database, tách bằng schema.**

```
IdentityDbContext → schema "identity"
    users · roles · permissions · role_permissions
    refresh_tokens · email_verification_tokens
```

Vì sao không dùng một `AppDbContext` chung: modular monolith cấm module tham chiếu chéo (ADR-001).
Một context chung là cửa hậu để module nào cũng query được bảng của module khác — **ArchUnitNET
không bắt được**, vì về mặt kỹ thuật vẫn hợp lệ. Tách schema thì ranh giới hiện ra ngay trong SQL,
ai vượt rào là thấy liền.

Bảng lịch sử migration cũng để riêng mỗi schema, tránh hai module tranh nhau
`__EFMigrationsHistory` khi GĐ2 thêm context thứ hai.

**Nối vào hook `--migrate`** đã có sẵn ở `Program.cs` (hiện là no-op): apply migration → chạy
seeder → kiểm tra vai trò hệ thống (Mục 5.5) → thoát 0.

#### Nợ 3 — Thiếu 5 package *(thực tế: 4)*

Ghim phiên bản chính xác, **không dùng dải** — CI phải dựng lại được y hệt.

*(thực tế)* `Testcontainers.PostgreSql` đã có sẵn từ GĐ0 ở **4.0.0**, nên chỉ thêm 4 gói. Giữ 4.0.0
chứ không hạ về 3.10.0: 4.x đổi API, hạ version là phải sửa lại harness đang chạy tốt.

| Gói | Version | Project | Dùng cho |
|---|---|---|---|
| `Microsoft.AspNetCore.Authentication.JwtBearer` | 8.0.10 | Api | Tầng 1 (AuthN) |
| `BCrypt.Net-Next` | 4.0.3 | Identity | Băm mật khẩu cost 12 |
| `FluentValidation.AspNetCore` | 11.3.0 | Identity | Validator → RFC 7807 |
| `UUIDNext` | 4.x | **SharedKernel** | UUID v7 — .NET 8 chưa có `Guid.CreateVersion7()`; đặt ở SharedKernel vì mọi module đều cần |
| `Testcontainers.PostgreSql` | ~~3.10.0~~ **4.0.0** | IntegrationTests | Test trên Postgres thật |

#### Nợ 4 — CI chưa tách AuthZ matrix thành cổng chặn

Hiện `dotnet test` gộp tất cả. Tài liệu yêu cầu AuthZ matrix là cổng chặn merge riêng (GOAL-03,
Mục 10.2). Cách rẻ nhất — gắn trait rồi tách thành **hai bước trong cùng một job**:

```yaml
- name: Test (unit + integration)
  run: dotnet test SocialApp.sln -c Release --no-build --filter "Category!=AuthZ"

- name: AuthZ matrix (CI GATE)     # tách riêng để log chỉ thẳng nguyên nhân fail
  run: dotnet test SocialApp.sln -c Release --no-build --filter "Category=AuthZ"
```

Chưa cần job riêng: job riêng phải build lại từ đầu, tốn thêm 2–3 phút mỗi lần push, mà NFR-MAINT
giới hạn CI ≤ 10 phút.

> **Kiểm tra ngay ở lần push đầu tiên của GĐ1:** integration test dùng Testcontainers cần Docker
> daemon trên runner. `ubuntu-latest` có sẵn, nhưng phải xác nhận sớm — phát hiện lúc gần deadline
> thì không còn đường lùi.
>
> *(thực tế)* **Đã xác nhận, rủi ro này đóng.** `IdentityDbContextSchemaTests` chạy thật trên CI:
> log có `Docker image postgres:16-alpine created` và ryuk khởi động, cả job dưới 60 giây. Cổng
> AuthZ của GOAL-03 đứng được trên nền này.

### Cổng mở — Ngày 3 sáng, cả nhóm, ~2 giờ

Chốt 7 quyết định ở Mục 3 + thống nhất hợp đồng token và danh sách endpoint, rồi **viết ra thành
OpenAPI stub cho 6 endpoint auth** (kèm mã lỗi 400/401/403/409/410/423) và **commit vào repo**.

Đây là sản phẩm bắt buộc, không phải thủ tục: người làm frontend sinh type TypeScript và mock MSW
từ chính stub này để bắt đầu ngay trong ngày, thay vì chờ backend chạy được. **Không có stub thì
không ai gõ dòng code nào.**

> **Trạng thái: đã xong.** Stub nằm ở
> [`src/Modules/Identity/Presentation/identity-v1.yaml`](../src/Modules/Identity/Presentation/identity-v1.yaml)
> (OpenAPI 3.0.3, 6 endpoint, đủ mã lỗi 400/401/403/409/410/423 + 429/500, `redocly lint` không
> error). Biên bản cổng mở — 7 quyết định, hợp đồng token, luật sửa hợp đồng — ở
> [`Presentation/README.md`](../src/Modules/Identity/Presentation/README.md).
>
> **Hợp đồng nằm trong module vì module sở hữu tầng HTTP của mình** (Mục 9.1). Và nó không còn được
> đối chiếu bằng mắt: `IdentityContractTests` so file này với `/swagger/identity-v1/swagger.json`
> sinh từ code, lệch là CI đỏ ở cổng `Category=Contract`.
>
> Đã kiểm chứng bằng `openapi-typescript`: stub sinh ra type dùng được, `RoleCode` thành union
> `'USER' | 'MODERATOR' | 'ADMIN'` nên đổi hợp đồng mà quên sửa FE là **compile lỗi**, không phải
> lỗi runtime phát hiện muộn ở staging.

**Tiếp theo — dọn nợ kỹ thuật (Mục 9.0).** *(Đã xong trước khi khối A bắt đầu.)*

### 9.1 Module sở hữu tầng HTTP — làm trước khối A

> **Trạng thái: đã xong.** Làm trước khối A vì nó quyết định **chỗ** mọi người gõ code; để sau thì
> khối D viết controller vào chỗ sai rồi mới chuyển.

Bản đầu của tài liệu này để controller ở `SocialApp.Api` và hợp đồng API ở một thư mục tài liệu
riêng. Cả hai đã đổi:

| | Trước | Sau |
|---|---|---|
| Controller | `SocialApp.Api/Controllers/` | `Modules/<Module>/Presentation/` |
| Hợp đồng API | thư mục docs riêng | `Modules/<Module>/Presentation/<nhóm>.yaml` |
| Swagger | một trang gộp 7 module | một nhóm mỗi module (`identity-v1`, `platform-v1`…) |
| "Swagger khớp stub" ở cổng đóng | đối chiếu **bằng mắt** | `IdentityContractTests` — cổng CI `Category=Contract` |

**Vì sao:** mã lỗi, hợp đồng và hiện thực của một module là một khối kiến thức; tách chúng ra ba chỗ
thì sửa một thứ mà quên hai thứ kia là chuyện sớm muộn. Module tự khai nhóm Swagger của mình, host
chỉ nạp assembly qua `AddApplicationPart` — một dòng mỗi module ở `Program.cs`.

**Cái giá đã trả để việc này an toàn** — ASP.NET Core vốn đã có sẵn trong mọi module (kéo qua
`SharedKernel` và `FluentValidation.AspNetCore`), nên `return NotFound();` trong một domain service
compile được từ trước tới nay mà không rule nào bắt. Ba lưới mới đóng lỗ đó:

| Lưới | Bắt gì |
|---|---|
| `PresentationBoundaryTests.Inner_layers_must_not_depend_on_AspNetCore_Mvc` | HTTP rò vào Domain/Application/Infrastructure/DependencyInjection |
| `PresentationBoundaryTests.Every_controller_must_declare_a_swagger_group` | Controller quên `[ApiExplorerSettings]` → biến mất khỏi **mọi** trang Swagger, im lặng, không lỗi |
| `IdentityContractTests` | Hợp đồng và code lệch nhau (tập `path × method`, tập status code, required field) |

Hai rule đầu đã được kiểm chứng là **đỏ được**: chèn tạm một file vi phạm thì test đỏ đúng chỗ, gỡ
ra thì xanh lại. Lưới không bao giờ đỏ được là lưới giả — đúng bài học mà `PersistenceBoundaryTests`
đã ghi sẵn trong comment.

`IdentityContractTests` có hai chiều, cố ý tách:

- **Chiều "code không được lộ ra ngoài hợp đồng"** — xanh ngay từ bây giờ, bắt lỗi thêm endpoint mà
  quên cập nhật yaml.
- **Chiều "hợp đồng phải được hiện thực đủ"** — đang `Skip`, đã chạy thử một lần không Skip để xác
  nhận nó đỏ đúng lý do (liệt kê đủ 6 operation còn thiếu). **Gỡ Skip khi khối D ráp xong 6 endpoint.**

### Bốn khối — chỉ A là chặn

| Khối | Nội dung | Người | Phụ thuộc |
|---|---|---|---|
| **A. Nền dữ liệu** | Entity + EF config + migration đầu + seeder idempotent + kiểm tra khởi động | BE | chặn C, D |
| **B. Hạ tầng test** | Testcontainers harness + bảng AuthZ matrix (viết TC-A01/A02 cho **đỏ** trước) | BE | không chặn — làm ngay |
| **C. SharedKernel AuthZ** | `RequirePermissionAttribute` + policy provider + handler + `IPermissionCache` | BE | chỉ cần interface, stub repository |
| **E. Lane frontend** | Scaffold Next.js + màn auth + interceptor 401→refresh | FE | chỉ cần **OpenAPI stub**, không cần backend chạy |

Khối **D (endpoint)** ghép sau khi A và C xong.

### Ngày 3

- **BE — khối A:** 6 entity + `IEntityTypeConfiguration` + migration đầu + seeder + kiểm tra vai
  trò hệ thống + nối hook `--migrate`.
- **BE — khối C:** `RequirePermissionAttribute` + policy provider + `PermissionHandler` +
  `IPermissionCache` (dùng stub repository, nối repo thật ở Ngày 4) + JwtBearer + fallback policy.
- **BE — khối B:** bảng AuthZ matrix data-driven, viết TC-A01/A02/RBAC-01/RBAC-02 **cho đỏ trước**.
- **FE — khối E:** scaffold Next.js 14 App Router · design token + primitive · sinh type từ
  OpenAPI stub · api client bọc `fetch` với `credentials: 'include'` · mock MSW.

### Ngày 4

- **BE:** `POST /auth/register` + luồng xác minh email qua Mailpit · `POST /auth/login` + lockout +
  phát JWT · nối handler tầng 2 vào repository thật.
- **BE — test:** integration AC-01 → AC-04 chạy trên Postgres thật.
- **FE:** màn đăng ký (validation client **khớp đúng validator server**) · màn đăng nhập (dịch
  401/403/423 thành thông điệp người đọc hiểu, **không tiết lộ email có tồn tại hay không**) · màn
  xác minh email nhận token từ link Mailpit.

### Ngày 5

- **BE:** **refresh rotation + reuse detection + logout** — phần khó nhất, giao cho người chắc tay
  nhất.
- **BE:** `ITokenRevocationStore` + hook `OnTokenValidated` (Mục 7.5) — khoảng 2 giờ; nối trigger
  ghi cho reuse detection. Nếu Ngày 5 quá tải thì đây là phần cắt được, đẩy sang GĐ6 cùng bên ghi.
- **BE:** hoàn thiện RFC 7807 cho toàn bộ nhóm auth + cập nhật Swagger cho khớp stub · CORS +
  cookie refresh.
- **FE:** app shell + route guard · **access token giữ trong memory, không `localStorage`** ·
  **interceptor 401→refresh** · trang `/me`.

> ⚠️ **Interceptor phải single-flight.** Nhiều request nhận 401 cùng lúc chỉ được gọi
> `/auth/refresh` **một lần**, số còn lại xếp hàng chờ kết quả. Không làm vậy thì chính frontend tự
> kích hoạt reuse detection của Ngày 5 và người dùng bị đăng xuất oan — triệu chứng trông hệt như
> lỗi backend. Ân hạn 10 giây (Mục 7.3) che được phần lớn nhưng không phải tất cả.

### Ngày 6 — cổng đóng

- Deploy staging; **frontend bỏ mock, trỏ thẳng domain HTTPS thật**.
- **E2E lát cắt:** đăng ký → nhận mail Mailpit → xác minh → đăng nhập → `GET /me` → ép hết hạn
  access token → interceptor refresh → gọi lại thành công.
  Đây là chỗ cookie `httpOnly`, `SameSite`, `Path=/api/v1/auth` và CORS preflight được kiểm chứng —
  integration test không chạm tới được. *(Bản A của kế hoạch tổng dùng một trang HTML tạm cho đúng
  việc này; bản B không cần vì UI thật đã có.)*
- Rà Definition of Done (Mục 11), tick checklist nghiệm thu (Mục 12).
- **Đóng băng hợp đồng API** và thông báo cho cả nhóm — GĐ2 khởi động ngay sau đây.

> **Lưu ý lịch:** GĐ1 kéo dài 1 ngày so với bản A (Ngày 3–5 → Ngày 3–6) để hấp thụ việc backend chỉ
> còn 2 người trong khi khối A vẫn đang chặn C và D. Mọi giai đoạn sau dịch theo, tổng 26 ngày —
> nhưng GĐ7 nhẹ đi vì không còn backlog frontend nên nhiều khả năng vẫn về đúng 25.

### Thư viện cần thêm

Xem bảng đầy đủ (kèm version ghim và project đích) ở **Mục 9.0 — Nợ 3**.

---

## 10. Chiến lược test

### 10.1 Nghiệm thu chức năng (integration, Postgres thật)

| Mã | Tình huống | Kỳ vọng |
|---|---|---|
| AC-01 | Đăng nhập đúng | 200 + cặp access/refresh token |
| AC-02 | Sai mật khẩu | 401, **không lộ email tồn tại**, `failed_login_count++` |
| AC-03 | Sai 5 lần liên tiếp | 423 Locked; mở lại sau 15 phút |
| AC-04 | Chưa xác minh email | 403 |
| RT-01 | Refresh hợp lệ | Cặp token mới; token cũ hết hiệu lực |
| RT-02 | **Refresh reuse** | Thu hồi toàn bộ `family_id` → 401 |
| RT-03 | Refresh đã hết hạn | 401 |
| RT-04 | Hai tab refresh trong 10 giây | Cả hai thành công, chuỗi không bị thu hồi (Mục 7.3) |
| RV-01 | `SET revoked:user:X = T`, gọi API bằng token có `iat < T` | 401 |
| RV-02 | Cùng key, token có `iat >= T` (phát sau khi thu hồi) | Cho qua |
| RV-03 | Reuse detection kích hoạt | Thu hồi **cả** refresh family **và** access (`revoked:user`) |
| RV-04 | Redis tắt, gọi API bằng token hợp lệ | Vẫn phục vụ (fail-open) + ghi log warning |
| SEED-01 | Chạy seeder hai lần | Dữ liệu không đổi, không nhân bản |
| SEED-02 | Gỡ 1 quyền của MODERATOR rồi chạy lại seeder | **Không bị cấp lại** |
| SEED-03 | `UPDATE roles SET code='ROOT' WHERE code='ADMIN'` rồi khởi động lại app | **App từ chối khởi động**, thông báo nêu tên vai trò thiếu (Mục 5.5) |
| FK-01 | Xóa vai trò đang có user | Lỗi RESTRICT |
| E2E-01 | Đăng ký → Mailpit → verify → login → `/me` → ép 401 → refresh → gọi lại, **trên trình duyệt thật qua HTTPS** | Xuyên suốt không lỗi. Kiểm chứng cookie `httpOnly`, `SameSite`, `Path` scoping, CORS preflight — integration test không chạm tới |
| E2E-02 | 3 request nhận 401 cùng lúc | Interceptor chỉ gọi `/auth/refresh` **một lần**; không kích hoạt reuse detection; không ai bị đăng xuất |

### 10.2 AuthZ matrix — CI gate từ GĐ1

Dựng dạng **data-driven** ngay từ bây giờ: một bảng dữ liệu, mỗi dòng một tình huống. Mỗi giai
đoạn sau chỉ thêm dòng, không sửa khung.

| Mã | Tình huống | Kỳ vọng | Thêm ở |
|---|---|---|---|
| TC-A01 | Gọi endpoint bảo vệ, không kèm JWT | 401 | **GĐ1** |
| TC-A02 | Token hết hạn hoặc sai chữ ký | 401 | **GĐ1** |
| RBAC-01 | ADMIN gọi endpoint đòi quyền bất kỳ | Qua, dù không có dòng `role_permissions` | **GĐ1** |
| RBAC-02 | USER gọi endpoint đòi `post.hide` | 403 | **GĐ1** |
| TC-A03 | User A `PATCH /posts/{id của B}` | 403 (IDOR) | GĐ2 |
| TC-A04 | Đọc conversation không phải thành viên | 403 | GĐ5 |
| TC-A05 | User thường gọi `/admin/*` | 403 | GĐ6 |
| TC-A06 | User thường gọi `PATCH /reports/{id}` | 403 | GĐ6 |
| TC-A07 | Gửi tin nhắn cho người không phải bạn | 403 | GĐ5 |

Đây là cách GOAL-03 (0 lỗ hổng IDOR) thực sự đạt được — bằng một cổng chặn merge từ ngày đầu,
không phải bằng một đợt rà soát ở GĐ8 khi đã quá muộn để sửa rẻ.

### 10.3 Unit test

BCrypt (cost đúng 12, verify đúng/sai), sinh & xác thực JWT (claims đủ, hết hạn, sai chữ ký),
quy tắc lockout (ngưỡng 5, cửa sổ 15 phút, reset sau đăng nhập thành công), validator đăng ký.

### 10.4 Architecture test

ArchUnitNET: module `Identity` không lộ tầng `Infrastructure` ra ngoài; module khác chỉ được
tham chiếu qua interface ở `Application` (ADR-001).

---

## 11. Definition of Done

Theo Mục 3.5 của tài liệu PTTK — cả 6 mục phải tick:

- [ ] Đủ AC (AC-01 → AC-04 của US-002; FR-001/002/003)
- [ ] Có kiểm tra RBAC **và** ownership (tầng 2 + tầng 3, kể cả khi tầng 3 mới chỉ là khuôn)
- [ ] Validation trả đúng RFC 7807 Problem Details có `traceId`
- [ ] **Đã chạy thử trên staging bằng tài khoản thật** — không phải chỉ trên máy local
- [ ] Swagger cập nhật đầy đủ cho cả 6 endpoint **và khớp OpenAPI stub đã chốt ở cổng mở**
- [ ] Không lộ secret/PII trong log, response, hay image
- [ ] **Lát cắt chạy được đầu-cuối trên trình duyệt thật** — frontend đã bỏ mock, trỏ staging

---

## 12. Checklist nghiệm thu cuối GĐ1

**Bảo mật**
- [ ] Mật khẩu băm BCrypt cost 12 — kiểm tra bằng cách đọc trực tiếp một dòng trong DB
- [ ] `refresh_tokens.token_hash` là băm; không cột nào chứa token bản rõ
- [ ] Token xác minh email cũng lưu băm
- [ ] Email không tồn tại và mật khẩu sai cho **cùng** thông điệp lỗi, thời gian phản hồi tương đương
- [ ] JWT key đọc từ biến môi trường / CI protected variable — không có trong repo, không trong image

**Phân quyền**
- [ ] `GET /me` không kèm token → 401
- [ ] Endpoint chưa khai báo policy vẫn bị chặn (fallback policy hoạt động)
- [ ] Tài khoản ADMIN qua được tầng 2 dù `role_permissions` không có dòng nào
- [ ] Ma trận quyền đọc từ DB — thử `DELETE` một dòng `role_permissions` của MODERATOR và xác nhận
      hành vi đổi theo sau khi cache hết hạn
- [ ] TTL của `revoked:user` **bằng đúng** TTL access token, và cả hai đọc từ **cùng một** hằng số
      cấu hình (Mục 7.5)
- [ ] Thứ tự thu hồi là **DB trước, Redis sau** — kiểm bằng code review, không có test nào bắt được

**Dữ liệu**
- [ ] Seeder chạy 3 lần liên tiếp cho kết quả giống hệt
- [ ] Sửa `role_permissions` rồi restart app → thay đổi **không** bị ghi đè
- [ ] `DELETE FROM roles WHERE code='USER'` bị chặn bởi FK RESTRICT
- [ ] Đổi `roles.code` của ADMIN bằng tay rồi restart → app từ chối khởi động (Mục 5.5)

**Vận hành**
- [ ] Deploy lên staging qua CD tự động, không thao tác tay
- [ ] Đăng ký → nhận mail Mailpit → xác minh → đăng nhập, toàn bộ trên domain HTTPS thật
- [ ] **Frontend đã bỏ mock MSW, trỏ staging thật** — không giai đoạn nào được nghiệm thu trên mock
- [ ] **Interceptor 401→refresh single-flight** — mở 3 tab, ép hết hạn token, không ai bị đăng xuất
- [ ] Access token **không** nằm trong `localStorage` — kiểm bằng DevTools
- [ ] CI xanh: unit + integration + AuthZ matrix + ArchUnitNET
- [ ] **AuthZ matrix chạy thành bước riêng** trong CI, fail là chặn merge (Mục 9.0 — Nợ 4)
- [ ] Testcontainers chạy được trên CI runner (Docker daemon sẵn sàng)
- [ ] `dotnet run --migrate` apply migration + seed + kiểm tra vai trò hệ thống rồi thoát 0

---

## 13. Sai khác so với báo cáo v5.0

Ghi lại để lúc bảo vệ giải thích được — chắc chắn sẽ có người đối chiếu với bản đã chốt.

| # | Báo cáo v5.0 | Thực hiện | Lý do |
|---|---|---|---|
| 1 | `roles(role_id, name)` | Tách `code` (bất biến) + `display_name` (sửa được) | `code` là thứ JWT/policy/test bám vào nên phải bất biến; tên hiển thị thì cần sửa được. Một cột không gánh nổi hai vai trò |
| 2 | Admin có đủ dòng trong ma trận `role_permissions` | Admin **không có dòng nào**, short-circuit ở tầng 2 | Thêm permission mới về sau, Admin tự động có — loại bỏ hẳn rủi ro quên `INSERT` khiến Admin bị chặn khỏi tính năng mới. Bù bằng audit đầy đủ |
| 3 | "Seed cố định 3 vai trò" | Vẫn seed 3 vai trò, nhưng vai trò là **dữ liệu mở** — thêm role mới lúc runtime không cần sửa code | Chính Mục 6.7.2 đã hứa "nâng cấp là thay dữ liệu, không thay code"; đây là hiện thực đúng lời hứa đó |
| 4 | `refresh_tokens` có `replaced_by_id` | Bổ sung `family_id` | Thu hồi cả chuỗi bằng một `UPDATE` thay vì lần theo linked list qua N truy vấn |
| 5 | Không đề cập cách thu hồi access token | Bổ sung cơ chế `revoked:user` + `iat` trên Redis (Mục 7.5) | JWT stateless không thu hồi được; không có cơ chế này thì hạ quyền/khóa tài khoản trễ tới 15 phút. Denylist theo `jti` không dùng được vì server không biết `jti` nào đang lưu hành |
| 6 | — | Kiểm tra vai trò hệ thống lúc khởi động (Mục 5.5) | Bắt được kịch bản đổi `roles.code` bằng tay — nguy hiểm nhất là `ADMIN` bị đổi tên: short-circuit không khớp nữa, Admin lại không có dòng `role_permissions` nào để rơi về → mất sạch quyền quản trị âm thầm |
| 7 | Api host giữ toàn bộ controller; module chỉ có Domain/Application/Infrastructure | **Module sở hữu tầng HTTP của mình** (`Presentation/`), host chỉ nạp assembly; Swagger tách nhóm theo module; hợp đồng API nằm cạnh controller và có test CI canh | Mã lỗi, hợp đồng và hiện thực của một module là một khối kiến thức — để ba chỗ thì sửa một mà quên hai là chuyện sớm muộn. Kèm theo là lưới ArchUnitNET chặn HTTP rò vào tầng trong, thứ trước đây **không có** dù ASP.NET Core đã sẵn trong mọi module (Mục 9.1) |

**Phương án đã cân nhắc rồi loại bỏ — không phải bỏ sót:** cột `roles.is_system` + trigger dựa
trên cột đó. Lý do loại: tập vai trò cần bảo vệ vĩnh viễn chỉ có 3 cái nên trigger gọi thẳng tên
là đủ, cột chỉ lặp lại thông tin đã cố định; và nó canh sai bất biến — thứ đáng canh là "còn ≥ 1
*user* Admin", không phải "*role* Admin còn tồn tại". Chi tiết ở Mục 3.4.

---

## 14. Rủi ro cần theo dõi

| Rủi ro | Ảnh hưởng | Giảm thiểu |
|---|---|---|
| Hai tab refresh đồng thời → đăng xuất oan | Trải nghiệm tệ, khó tái hiện khi debug | Ân hạn 10 giây (Mục 7.3) + test RT-04 |
| Thời gian phản hồi lộ email nào có thật | Rò rỉ thông tin, vi phạm AC-02 | Luôn chạy BCrypt giả cho email không tồn tại |
| Seeder ghi đè cấu hình quyền trên production | Admin gỡ quyền xong bị cấp lại âm thầm | `ON CONFLICT DO NOTHING` + test SEED-02 |
| BCrypt cost 12 ≈ 250ms/lần → login tốn CPU | Chậm khi tải cao | Rate limit 10 req/phút nhóm auth đã có; theo dõi ở GĐ7 |
| Quên tầng 3 khi sang GĐ2 | IDOR — hỏng GOAL-03 | Quy ước Mục 6.3: endpoint không có dòng AuthZ matrix = chưa xong |
| Đặt `UseAuthentication()` **sau** rate limiter | Limiter phân vùng theo IP thay vì theo user: nhiều user sau cùng một NAT ăn chung hạn mức. Hỏng câm, không log | Tách sẵn `UseSharedKernelRateLimiter()` thành lệnh riêng để thứ tự hiện ra ở `Program.cs`; comment tại chỗ chèn |
| Đọc `role` từ token nên đổi vai trò trễ 15 phút | Dễ hiểu nhầm thành bug ở GĐ6 | Cơ chế `revoked:user` + `iat` (Mục 7.5); ghi rõ trong Swagger và tài liệu bàn giao |
| Đảo thứ tự thu hồi (Redis trước DB) | User giữ vai trò cũ thêm 15 phút, **không gì chặn được** | Code review bắt buộc; ghi rõ ở Mục 7.5. Không test tự động nào bắt được lỗi này |
| TTL `revoked:user` lệch TTL access token | Lỗ hổng câm — token đã thu hồi được chấp nhận lại | Cùng một hằng số cấu hình cho cả hai; có dòng trong checklist Mục 12 |
| Redis chết → bỏ qua kiểm tra thu hồi (fail-open) | Token đã thu hồi sống lại, cửa sổ ≤ 15 phút | Quyết định có ý thức (Mục 7.5); alert khi Redis mất kết nối; refresh token vẫn bị chặn ở DB |
| **Interceptor 401→refresh không single-flight** | Nhiều tab refresh cùng lúc tự kích hoạt reuse detection → người dùng bị đăng xuất oan. Triệu chứng trông hệt lỗi backend, debug nhầm chỗ rất tốn thời gian | Single-flight bắt buộc ở lane frontend (Mục 9, Ngày 5) + ân hạn 10 giây phía server (Mục 7.3) + test E2E-02 |
| **Backend chỉ còn 2 người** trong khi khối A vẫn chặn C và D | Trễ ngay ở giai đoạn nền, kéo theo mọi giai đoạn sau | GĐ1 kéo dài thêm 1 ngày (Ngày 3–6); khối B và E không phụ thuộc A nên vẫn chạy hết công suất |
| **Bỏ qua cổng mở khi gấp** | Frontend dựng trên API đang viết dở → phải sửa lại, mất sạch lợi ích của lát cắt dọc | OpenAPI stub là artifact bắt buộc; không có stub thì không ai bắt đầu (Mục 9) |
| **Mock MSW trôi xa khỏi hiện thực thật** | Frontend xanh trên mock, đỏ trên staging, phát hiện muộn ở cổng đóng | Sinh type từ stub nên đổi hợp đồng là compile lỗi; cổng đóng cấm nghiệm thu trên mock |

---

# Phần B — Kế hoạch triển khai

## B.0 Cách đọc phần này

Mỗi công việc được mô tả bằng bốn dòng cố định:

| Dòng | Nghĩa |
|---|---|
| **Mục tiêu** | Việc này tồn tại để đạt điều gì. Không phải mô tả thao tác — mà là thứ sẽ mất đi nếu bỏ việc này |
| **Cách thực thi** | Làm cụ thể ra sao: file nào, lệnh nào, quyết định kỹ thuật nào đã chốt sẵn |
| **Xong là** | Điều **kiểm chứng được** chứng minh việc đã xong. Không có dòng nào là "chạy thử thấy được" |
| **Chặn / Cần** | Việc này chặn ai, và cần gì trước đó |

**"Xong" luôn là thứ máy kiểm được**, trừ ba trường hợp đã biết là máy không kiểm được (ghi rõ ở
B.9). Đây không phải hình thức: GOAL-03 (0 lỗ hổng IDOR) chỉ đạt được bằng cổng chặn tự động từ
ngày đầu, không phải bằng một đợt rà soát ở GĐ8 khi đã quá muộn để sửa rẻ.

Mã công việc dùng chữ cái khối + số thứ tự trong khối (`A3`, `D7`…). Khối giữ nguyên tên đã đặt ở
Mục 9 của Phần A.

---

## B.1 Điểm xuất phát — cái gì đã xong

Ba mốc dưới đây đã hoàn thành và đã đẩy lên nhánh; CI xanh cả ba bước (test, cổng hợp đồng API,
cổng AuthZ).

| Mốc | Kết quả để lại | Bằng chứng |
|---|---|---|
| **Cổng mở** (Mục 9) | Hợp đồng API 6 endpoint đủ mã lỗi, đã chốt 7 quyết định thiết kế | `src/Modules/Identity/Presentation/identity-v1.yaml`; `redocly lint` 0 error; `openapi-typescript` sinh type dùng được |
| **Dọn nợ kỹ thuật** (Mục 9.0) | EF Core cho Identity, `IdentityDbContext` schema riêng, 4 package ghim version, CI tách cổng AuthZ | `IdentityDbContextSchemaTests` chạy thật trên Postgres qua Testcontainers |
| **Module sở hữu tầng HTTP** (Mục 9.1) | Tầng `Presentation/`, Swagger tách nhóm theo module, ba lưới chặn mới | `PresentationBoundaryTests` (4 test), `IdentityContractTests` (cổng CI `Category=Contract`) |

**Hệ quả cho mọi việc phía sau — đọc kỹ ba dòng này:**

1. **Controller viết vào `src/Modules/Identity/Presentation/`**, không viết vào `SocialApp.Api/Controllers/`.
2. **Mọi controller phải khai `[ApiExplorerSettings(GroupName = IdentityApiGroup.Name)]`** — thiếu là
   endpoint biến mất khỏi Swagger trong im lặng, và `PresentationBoundaryTests` sẽ đỏ.
3. **Đổi hình dạng API là phải sửa `identity-v1.yaml` trong cùng commit** — `IdentityContractTests`
   chiều 1 đang xanh và sẽ đỏ ngay khi code lộ ra thứ hợp đồng chưa ghi.

Còn hai `Skip` đang chờ được gỡ, mỗi cái là một dòng việc cụ thể trong Phần B: `A7` và `D11`.

---

## B.2 Bản đồ công việc

| Khối | Nội dung | Người | Số việc | Cần trước | Chặn |
|---|---|---|---|---|---|
| **A. Nền dữ liệu** | Entity → migration → seeder → kiểm tra khởi động | BE-1 | 7 | — | C5·, D, B5 |
| **B. Hạ tầng test** | Harness + khung AuthZ matrix + test seeder | BE-2 | 5 | — | B4 siết cổng CI |
| **C. SharedKernel AuthZ** | `[RequirePermission]` + handler + JwtBearer + default deny | BE-2 | 5 | — (dùng stub) | D |
| **D. Endpoint** | 6 endpoint auth + revocation + CORS | BE-1 + BE-2 | 11 | A, C | F |
| **E. Lane frontend** | Next.js + 3 màn auth + interceptor single-flight | FE | 7 | chỉ cần hợp đồng | F |
| **F. Cổng đóng** | Staging + E2E + checklist + đóng băng hợp đồng | cả nhóm | 7 | D, E | GĐ2 |

*· Khối C viết được ngay với repository stub; chỉ bước `C5` nối vào dữ liệu thật mới cần A xong.

**Ba lane chạy song song ngay từ đầu:** A (BE-1) · B rồi C (BE-2) · E (FE). Chỉ D mới cần chờ.

---

## B.3 Khối A — Nền dữ liệu

> **Mục tiêu khối:** biến schema trên giấy (Mục 4) thành database chạy được, có dữ liệu phân quyền
> đúng, và **tự từ chối khởi động** nếu dữ liệu nền bị sửa sai.

### A1 — Sáu entity trong `Domain/`

- **Mục tiêu:** có mô hình nghiệp vụ để mọi tầng khác bám vào, và đóng luôn lỗ hổng "rule persistence
  chạy trong chân không" mà `PersistenceBoundaryTests` đang cảnh báo bằng một `Skip`.
- **Cách thực thi:** `Role`, `Permission`, `RolePermission`, `User`, `RefreshToken`,
  `EmailVerificationToken` trong `src/Modules/Identity/Domain/`. Đúng cột theo Mục 4 — **không tự bịa
  thêm cột**. `roles` tách `code` + `display_name` (Mục 3.3); `refresh_tokens` có **cả**
  `family_id` lẫn `replaced_by_id` (Mục 3.5). PK sinh bằng `UUIDNext` (UUID v7), **không dùng**
  `Guid.NewGuid()`. Tầng này tuyệt đối không `using Microsoft.EntityFrameworkCore`.
- **Xong là:** `PersistenceBoundaryTests` xanh **và** `A7` gỡ được `Skip`.
- **Chặn / Cần:** chặn A2. Cần: không.

### A2 — `IEntityTypeConfiguration` trong `Infrastructure/`

- **Mục tiêu:** ánh xạ entity xuống Postgres đúng ràng buộc, để những bất biến quan trọng được **DB**
  giữ chứ không phải code nhớ giữ.
- **Cách thực thi:** một file cấu hình cho mỗi entity. Bắt buộc có:
  `modelBuilder.HasPostgresExtension("citext")` cho `users.email`; `users.role_id` FK
  `.OnDelete(DeleteBehavior.Restrict)` (biện pháp #1 thay cho `is_system` — Mục 3.4); unique index cho
  `roles.code`, `permissions.code`, `users.email`, `refresh_tokens.token_hash`,
  `email_verification_tokens.token_hash`; index một phần `idx_refresh_family` dùng
  `.HasFilter("revoked_at IS NULL")`; `CHECK` cho `users.status`.
- **Xong là:** `IdentityDbContextSchemaTests` mở rộng, khẳng định 6 bảng nằm trong schema `identity`
  và FK RESTRICT tồn tại.
- **Chặn / Cần:** chặn A3. Cần A1.

### A3 — Migration đầu tiên

- **Mục tiêu:** schema thành artifact có version, tái lập được y hệt ở mọi môi trường — không ai
  "sửa tay trên staging".
- **Cách thực thi:** startup project là **chính project module** (nhờ `DesignTimeIdentityDbContextFactory`,
  Api không phải kéo EF vào — ADR-001):

  ```bash
  dotnet ef migrations add InitialIdentity \
    --project src/Modules/Identity/SocialApp.Modules.Identity.csproj \
    --startup-project src/Modules/Identity/SocialApp.Modules.Identity.csproj \
    --output-dir Infrastructure/Migrations
  ```

  Đọc lại file migration sinh ra **trước khi commit** — đây là lúc rẻ nhất để phát hiện ánh xạ sai.
- **Xong là:** `dotnet ef database update` trên compose dev chạy sạch; `IdentityDbContextSchemaTests`
  xanh trên Postgres thật.
- **Chặn / Cần:** chặn A4. Cần A2.

### A4 — Seeder idempotent

- **Mục tiêu:** ma trận quyền là **dữ liệu**, không phải code (Mục 6.7.2) — và CD deploy lại 10
  lần/ngày cũng không nhân bản hay ghi đè cấu hình.
- **Cách thực thi:** seed 3 vai trò (Mục 5.1), 17 permission (Mục 5.2), gán quyền theo Mục 5.3 —
  **ADMIN không có dòng nào**, đó là thiết kế (Mục 3.2), không phải thiếu dữ liệu. Dùng
  `ExecuteSqlRawAsync` với `ON CONFLICT (code) DO NOTHING`, **tuyệt đối không `DO UPDATE`**: từ GĐ6
  Admin sửa được `role_permissions` lúc runtime, `DO UPDATE` sẽ lặng lẽ cấp lại đúng cái quyền Admin
  vừa cố tình gỡ. Đẩy tính idempotent xuống **tầng DB** chứ không đọc-rồi-ghi ở tầng app — hai
  instance khởi động cùng lúc thì đọc-rồi-ghi sẽ chèn trùng.
- **Xong là:** `SEED-01` (chạy 2 lần, dữ liệu không đổi) và `SEED-02` (gỡ 1 quyền của MODERATOR rồi
  chạy lại, **không bị cấp lại**) xanh trên Postgres thật.
- **Chặn / Cần:** chặn A5. Cần A3.

### A5 — Kiểm tra vai trò hệ thống lúc khởi động

- **Mục tiêu:** bắt kịch bản nguy hiểm nhất của toàn GĐ1 — ai đó `UPDATE roles SET code='ROOT'` bằng
  tay. Khi đó short-circuit `role == "ADMIN"` không khớp nữa, mà Admin lại cố ý không có dòng
  `role_permissions` nào để rơi về → **mất sạch quyền quản trị, im lặng, không đường phục hồi**.
- **Cách thực thi:** ~5 dòng theo Mục 5.5, đặt **ngay trong seeder** (nó vốn đã chạy mỗi lần khởi
  động và vốn đã đọc bảng `roles` — không tốn thêm truy vấn nào và không thể quên gọi). Thiếu bất kỳ
  `code` nào trong ba cái thì ném `InvalidOperationException` nêu **tên vai trò bị thiếu**.
- **Xong là:** `SEED-03` xanh — đổi `roles.code` của ADMIN bằng tay rồi khởi động lại thì app **từ
  chối chạy**, thông báo nêu đúng tên vai trò thiếu.
- **Chặn / Cần:** chặn A6. Cần A4.

### A6 — Nối vào hook `--migrate`

- **Mục tiêu:** một lệnh duy nhất ở bước deploy làm trọn: nâng schema, nạp dữ liệu nền, tự kiểm tra.
  Không auto-migrate lúc app start (AGENTS.md Mục 13).
- **Cách thực thi:** mở rộng `MigrateIdentityModuleAsync` theo đúng thứ tự **apply migration → seed →
  kiểm tra vai trò → thoát 0**. Service `migrate` trong compose staging đã gọi sẵn
  `dotnet SocialApp.Api.dll --migrate`.
- **Xong là:** `dotnet run --project src/SocialApp.Api -- --migrate` trên máy sạch cho exit code 0 và
  in dòng xác nhận; chạy lần hai vẫn 0 và dữ liệu không đổi.
- **Chặn / Cần:** chặn F1. Cần A5.

### A7 — Gỡ `Skip` của `Identity_Domain_namespace_must_not_be_empty`

- **Mục tiêu:** đóng cái "lưới giả" mà `PersistenceBoundaryTests` tự cảnh báo về chính nó — rule dùng
  `WithoutRequiringPositiveResults` nên gõ sai namespace là nó xanh vĩnh viễn mà không kiểm gì cả.
- **Cách thực thi:** xóa thuộc tính `Skip` trong `tests/SocialApp.ArchitectureTests/PersistenceBoundaryTests.cs`.
  File đã ghi sẵn điều kiện gỡ.
- **Xong là:** test chạy thật và xanh (namespace `Identity.Domain` có ≥ 1 type).
- **Chặn / Cần:** cần A1. **Làm ngay trong cùng commit với A1**, đừng để thành nợ.

---

## B.4 Khối B — Hạ tầng test

> **Mục tiêu khối:** dựng cái khung mà GĐ2–GĐ8 chỉ việc thêm dòng vào, và biến GOAL-03 từ một lời
> hứa thành cổng chặn merge. **Không phụ thuộc khối A — bắt đầu ngay từ giờ đầu tiên.**

### B1 — Harness Testcontainers dùng chung

- **Mục tiêu:** mọi integration test chạy trên Postgres **thật**, không phải InMemory provider —
  InMemory không có FK, không có CHECK, không có `ON CONFLICT`, tức là không kiểm được đúng những
  thứ khối A vừa dựng.
- **Cách thực thi:** tách phần dựng container trong `IdentityDbContextSchemaTests` thành fixture dùng
  chung (`ICollectionFixture`), để N test chia một container thay vì mỗi test một cái. Giữ
  `postgres:16-alpine` và `Testcontainers.PostgreSql` 4.0.0 — **không hạ về 3.x**, 4.x đổi API và
  harness hiện tại đang chạy tốt.
- **Xong là:** ít nhất hai test class dùng chung một container; thời gian chạy cả nhóm không tăng
  tuyến tính theo số test.
- **Chặn / Cần:** chặn B3, B5, D-test. Cần: Docker daemon.

### B2 — Khung AuthZ matrix data-driven

- **Mục tiêu:** mỗi giai đoạn sau **chỉ thêm dòng dữ liệu**, không sửa khung. Đây là cách GOAL-03 mở
  rộng được tới GĐ8 mà không phải viết lại.
- **Cách thực thi:** một bảng dữ liệu (`[Theory]` + `MemberData`), mỗi dòng là một tình huống
  `(endpoint, vai trò, token, kỳ vọng)`. Gắn `[Trait("Category", "AuthZ")]`.
- **Xong là:** thêm một dòng mới vào bảng là có thêm một test chạy, không đụng tới code khung.
- **Chặn / Cần:** chặn B3. Cần B1.

### B3 — TC-A01, TC-A02, RBAC-01, RBAC-02 — **viết cho đỏ trước**

- **Mục tiêu:** bốn dòng đầu của ma trận ở Mục 10.2, và là bằng chứng cổng chặn thật sự chặn được.
- **Cách thực thi:** TC-A01 (không kèm JWT → 401), TC-A02 (token hết hạn/sai chữ ký → 401), RBAC-01
  (ADMIN qua được dù `role_permissions` rỗng), RBAC-02 (USER gọi endpoint đòi `post.hide` → 403).
  Viết **trước** khi có endpoint, để chúng đỏ; xanh dần khi C và D xong. Dùng một endpoint thử
  nghiệm có `[RequirePermission]` nếu `/me` chưa có.
- **Xong là:** bốn test tồn tại, và đã **quan sát thấy chúng đỏ** trước khi làm chúng xanh. Test chưa
  bao giờ đỏ thì không chứng minh được gì.
- **Chặn / Cần:** chặn B4. Cần B2, C.

### B4 — Siết cổng AuthZ trong CI

- **Mục tiêu:** đóng đúng cái bẫy vừa suýt dính ở cổng hợp đồng — cổng chặn **xanh với 0 test** thì
  tệ hơn không có cổng, vì nó tạo cảm giác an toàn giả.
- **Cách thực thi:** khi B3 có test đầu tiên, đổi bước `AuthZ matrix (CI GATE)` sang dạng
  **nhắm vào project** kèm `-- RunConfiguration.TreatNoTestsAsError=true`, giống bước cổng hợp đồng.
  **Không** thêm cờ đó vào lệnh chạy `.sln`: cờ xét riêng từng test assembly, nên `.sln` sẽ đỏ vì các
  project không có test AuthZ chứ không phải vì AuthZ hỏng. `.github/workflows/ci.yml` đã ghi sẵn
  lệnh mẫu và cảnh báo này ngay tại chỗ.
- **Xong là:** cố tình gõ sai trait → CI **đỏ**. Đây là bước phải thử tay một lần rồi hoàn tác.
- **Chặn / Cần:** cần B3.

### B5 — Test dữ liệu nền: SEED-01/02/03, FK-01

- **Mục tiêu:** khóa ba tính chất của khối A mà chỉ Postgres thật mới kiểm được.
- **Cách thực thi:** SEED-01 (chạy seeder 2 lần), SEED-02 (gỡ quyền rồi seed lại), SEED-03 (đổi
  `roles.code` rồi khởi động lại → app từ chối chạy), FK-01 (`DELETE FROM roles` khi còn user → lỗi
  RESTRICT).
- **Xong là:** bốn test xanh trên Postgres thật.
- **Chặn / Cần:** cần B1, A4, A5.

---

## B.5 Khối C — SharedKernel AuthZ

> **Mục tiêu khối:** viết **một lần** cho cả dự án cơ chế phân quyền tầng 2, đúng nghĩa "nâng cấp là
> thay dữ liệu, không thay code". GĐ2–GĐ8 dùng lại nguyên xi, không module nào tự chế cách riêng.

### C1 — `RequirePermissionAttribute` + policy provider

- **Mục tiêu:** khai báo quyền ngay trên endpoint bằng một dòng đọc được, thay vì `if` rải rác trong
  service.
- **Cách thực thi:** đặt trong `SocialApp.SharedKernel` (không đặt trong module — mọi module đều
  dùng). `IAuthorizationPolicyProvider` dựng policy động từ tên quyền trong attribute, tránh phải
  đăng ký tay 17 policy và mọi quyền thêm ở các giai đoạn sau.
- **Xong là:** gắn `[RequirePermission("post.hide")]` lên một endpoint thử là policy được tạo và chạy.
- **Chặn / Cần:** chặn C2.

### C2 — `PermissionHandler` với Admin short-circuit

- **Mục tiêu:** hiện thực đúng Mục 3.2 — và **đặt đúng tầng**. Một dòng `if` này nằm nhầm ở tầng
  3 là Admin đọc được tin nhắn riêng của bất kỳ ai; đúng cái lỗ IDOR mà GOAL-03 muốn đóng, chỉ khác
  là nạn nhân đông hơn.
- **Cách thực thi:** đọc claim `role` (luôn là **chuỗi**, với mọi vai trò — Mục 3.1);
  `if (role == RoleCodes.Admin) { ctx.Succeed(req); return; }` **chỉ ở đây**; còn lại tra
  `IPermissionCache`. Mã mẫu ở Mục 6.2.
- **Xong là:** RBAC-01 và RBAC-02 chuyển từ đỏ sang xanh.
- **Chặn / Cần:** chặn C5. Cần C1.

### C3 — `IPermissionCache` (TTL 60s)

- **Mục tiêu:** ma trận quyền đọc từ DB nhưng không phải mỗi request một truy vấn.
- **Cách thực thi:** interface ở SharedKernel, cache theo `role code`, TTL 60 giây. GĐ1 chưa cần đẩy
  invalidate (quyền chưa sửa được lúc runtime — việc đó ở GĐ6); TTL là đủ và đơn giản hơn.
- **Xong là:** `DELETE` một dòng `role_permissions` của MODERATOR → hành vi đổi theo **sau khi cache
  hết hạn**, có test hoặc kiểm tay ghi lại kết quả.
- **Chặn / Cần:** cần C1.

### C4 — JwtBearer + fallback policy default deny + thứ tự middleware

- **Mục tiêu:** dựng tầng 1, và **mặc định từ chối** — endpoint nào quên khai quyền thì bị chặn chứ
  không lọt.
- **Cách thực thi:** `AddAuthentication().AddJwtBearer(...)`, khóa ký đọc từ biến môi trường (**không
  bao giờ nằm trong repo hay trong image**). `options.FallbackPolicy = new AuthorizationPolicyBuilder()
  .RequireAuthenticatedUser().Build()`. Chèn `UseAuthentication()` + `UseAuthorization()` vào **đúng
  chỗ đã đánh dấu sẵn** trong `Program.cs` — **trước** `UseSharedKernelRateLimiter()`. Đặt sai thứ tự
  là limiter phân vùng theo IP thay vì theo user: nhiều người sau cùng một NAT ăn chung hạn mức, hỏng
  câm, không log, không test nào bắt.
- **Xong là:** TC-A01 và TC-A02 chuyển sang xanh; một endpoint không khai policy vẫn bị chặn.
- **Chặn / Cần:** chặn D. Cần: không (làm song song A).

### C5 — Nối handler vào repository thật

- **Mục tiêu:** bỏ stub, để ma trận quyền thật sự đọc từ bảng `role_permissions`.
- **Cách thực thi:** repository trong `Identity/Infrastructure/` dịch `role code` (chuỗi) → `role_id`
  (số) rồi join `role_permissions`. **Phép dịch này nằm gọn trong repository** — token và policy
  handler từ đầu đến cuối chỉ làm việc với chuỗi (Mục 3.1).
- **Xong là:** RBAC-02 xanh với dữ liệu seed thật, không phải dữ liệu dựng trong fixture.
- **Chặn / Cần:** cần A4, C2.

---

## B.6 Khối D — Endpoint

> **Mục tiêu khối:** biến hợp đồng đã chốt ở cổng mở thành 6 endpoint chạy thật, khớp từng mã lỗi.
> **Ghép sau khi A và C xong.** Mọi controller vào `Modules/Identity/Presentation/`.

### D1 — `POST /auth/register` + gửi mail xác minh

- **Mục tiêu:** FR-001 nửa đầu — người thật tạo được tài khoản.
- **Cách thực thi:** validator FluentValidation (email đúng định dạng, mật khẩu 8–72 ký tự — trần 72
  là giới hạn cứng của BCrypt, ký tự thứ 73 bị bỏ qua âm thầm). Băm `BCrypt.HashPassword(pw, workFactor: 12)`.
  `INSERT users` với `role_id = 1`, `email_verified_at = NULL`. Sinh token 32 byte ngẫu nhiên, lưu
  **băm SHA-256**, hạn 24 giờ. Gửi qua `IEmailSender` → Mailpit ở dev/staging. Email trùng → **409**
  (ngoại lệ có ý thức so với quy tắc không lộ email của `/auth/login` — lý do ghi trong hợp đồng).
- **Xong là:** đăng ký trên dev → mail hiện trong Mailpit; validator trả RFC 7807 có `errors` và `traceId`.
- **Chặn / Cần:** chặn D2. Cần A, C4.

### D2 — `POST /auth/verify-email`

- **Mục tiêu:** FR-001 nửa sau — tách rõ hai mã lỗi để frontend hiển thị khác nhau.
- **Cách thực thi:** băm token nhận được, đối chiếu `token_hash`, set `email_verified_at` +
  `consumed_at`. Token sai/không tồn tại → **400**; hết hạn **hoặc đã dùng** → **410**.
- **Xong là:** cả hai nhánh lỗi có test; token đã dùng lần hai trả đúng 410.
- **Chặn / Cần:** chặn D3 (test AC-04). Cần D1.

### D3 — `POST /auth/login` + lockout

- **Mục tiêu:** FR-002 + FR-003, và **không rò rỉ email nào có thật** (AC-02).
- **Cách thực thi:** đúng thứ tự 6 bước ở Mục 7.2. Hai điểm không được bỏ:
  (a) email không tồn tại → **vẫn chạy một phép BCrypt giả** rồi mới trả 401, nếu không thời gian
  phản hồi lộ ra email nào có thật; (b) cập nhật `failed_login_count` bằng `UPDATE ... RETURNING`
  **nguyên tử**, không đọc-rồi-ghi — tấn công dò mật khẩu bắn song song sẽ làm đọc-rồi-ghi đếm sót và
  lockout không bao giờ kích hoạt.
- **Xong là:** AC-01 → AC-04 xanh trên Postgres thật; 401 của "sai mật khẩu" và của "email không tồn
  tại" giống **hệt** nhau cả nội dung lẫn thời gian phản hồi.
- **Chặn / Cần:** chặn D5, D7. Cần D2, C.

### D4 — Cookie refresh + CORS

- **Mục tiêu:** hiện thực hai quyết định về cookie refresh và CORS ở Mục 8 — thiếu là lane FE đứng im.
- **Cách thực thi:** `Set-Cookie: refresh_token=…; HttpOnly; Secure; SameSite=Lax; Path=/api/v1/auth; Max-Age=604800`
  — **cả năm thuộc tính đều là một phần hợp đồng**, không phải chi tiết cài đặt. CORS:
  `.WithOrigins(<liệt kê tường minh>).AllowAnyHeader().AllowAnyMethod().AllowCredentials()`. Thiếu
  `AllowCredentials()` thì trình duyệt **im lặng** không gửi cookie và `/auth/refresh` luôn 401 mà
  không có thông báo nào chỉ ra nguyên nhân. Chuẩn CORS cấm dùng `AllowCredentials()` kèm
  `AllowAnyOrigin()`.
- **Xong là:** gọi từ `localhost:3000` sang API dev, cookie đi và về đúng; kiểm bằng DevTools.
- **Chặn / Cần:** chặn D5, E7. Cần D3.

### D5 — `POST /auth/refresh`: rotation + reuse detection

- **Mục tiêu:** NFR-SEC-03. **Phần khó nhất của cả giai đoạn — giao cho người chắc tay nhất.**
- **Cách thực thi:** toàn bộ trong **một transaction**, `SELECT ... FOR UPDATE` trên dòng token (EF:
  `FromSqlRaw` + `FOR UPDATE`). Năm bước ở Mục 7.3. Thêm **ân hạn 10 giây**: token đã bị thay thế
  trong vòng 10 giây và chuỗi chưa bị thu hồi thì trả token kế nhiệm thay vì coi là reuse — nếu không,
  hai tab refresh cùng lúc sẽ tự kích hoạt reuse detection và người dùng bị đăng xuất oan. **Mọi
  nhánh hỏng đều trả 401** — phân biệt "hết hạn" với "bị thu hồi" là nói cho kẻ tấn công biết token
  nó đang cầm ở trạng thái nào. Không nhận body: refresh token đọc từ cookie.
- **Xong là:** RT-01 → RT-04 xanh, đặc biệt **RT-04** (hai tab refresh trong 10 giây, cả hai thành
  công, chuỗi không bị thu hồi).
- **Chặn / Cần:** chặn D6, D8. Cần D4.

### D6 — `POST /auth/logout`

- **Mục tiêu:** đáp ứng yêu cầu "đăng xuất thu hồi toàn bộ phiên" (báo cáo Mục 6.7.3).
- **Cách thực thi:** thu hồi **cả `family_id`**, không chỉ một dòng — một `UPDATE` nhờ `family_id` (Mục 3.5).
  Xóa cookie trong cùng response (`Path` phải khớp lúc set, nếu không trình duyệt không xóa). Access
  token đang cầm **vẫn sống tối đa 15 phút** — bản chất stateless của JWT, không phải bug.
- **Xong là:** sau logout, refresh token cũ **và** mọi token cùng family đều trả 401.
- **Chặn / Cần:** cần D5.

### D7 — `GET /me`

- **Mục tiêu:** smoke test rẻ nhất chứng minh tầng 1 + tầng 2 chạy thông từ đầu đến cuối. Giữ endpoint
  này qua mọi giai đoạn sau.
- **Cách thực thi:** trả `role` (chuỗi `code`, để so logic) **và** `roleDisplayName` (để hiển thị) —
  đây chính là lý do bảng `roles` tách hai cột.
- **Xong là:** gọi có token → 200 đúng hình dạng hợp đồng; không token → 401 (TC-A01).
- **Chặn / Cần:** cần D3, C4.

### D8 — `ITokenRevocationStore` + hook `OnTokenValidated`

- **Mục tiêu:** dựng sẵn **bên đọc** của cơ chế thu hồi access token, để GĐ6 chỉ việc gọi. Không có
  nó thì hạ quyền / khóa tài khoản trễ tới 15 phút.
- **Cách thực thi:** store trên Redis; hook `OnTokenValidated` ở tầng 1 kiểm `revoked:user:<id>` so
  với claim `iat`. **TTL của key phải bằng đúng TTL access token, và cả hai đọc từ cùng một hằng số
  cấu hình** — lệch nhau là lỗ hổng câm: token đã thu hồi được chấp nhận lại. Redis chết thì
  **fail-open** + log warning (quyết định có ý thức, Mục 7.5). GĐ1 nối **đúng một** trigger ghi:
  reuse detection ở D5. Thứ tự thu hồi luôn là **DB trước, Redis sau**.
- **Xong là:** RV-01 → RV-04 xanh.
- **Chặn / Cần:** cần D5. **Đây là phần cắt được** nếu phải cắt — đẩy sang GĐ6 cùng bên ghi, nhưng
  khi đó RV-01→04 và hai dòng trong checklist Mục 12 cũng dời theo, **phải ghi rõ chứ không lặng lẽ bỏ**.

### D9 — RFC 7807 cho toàn nhóm auth + `[ProducesResponseType]`

- **Mục tiêu:** một hình dạng lỗi duy nhất cho cả nhóm, **và** làm cho cổng hợp đồng chiều 2 có thể
  xanh — Swagger chỉ thấy status code nào action khai ra.
- **Cách thực thi:** rà từng endpoint, khai `[ProducesResponseType]` cho **mọi** mã trong hợp đồng
  trừ 429/500 (hai mã cross-cutting do middleware sinh, `IdentityContractTests` đã trừ khỏi cả hai vế).
  Kiểm lại thông điệp không lộ PII hay chi tiết nội bộ.
- **Xong là:** `/swagger/identity-v1/swagger.json` khớp `identity-v1.yaml` ở tập status code.
- **Chặn / Cần:** chặn D11. Cần D1–D7.

### D10 — `[ApiExplorerSettings]` cho mọi controller

- **Mục tiêu:** endpoint không biến mất khỏi Swagger trong im lặng.
- **Cách thực thi:** `[ApiExplorerSettings(GroupName = IdentityApiGroup.Name)]` trên mỗi controller
  của module.
- **Xong là:** `PresentationBoundaryTests.Every_controller_must_declare_a_swagger_group` xanh.
- **Chặn / Cần:** làm cùng lúc với từng controller, không để dồn.

### D11 — Gỡ `Skip` của `Contract_must_be_fully_implemented`

- **Mục tiêu:** bật nốt chiều thứ hai của cổng hợp đồng — từ lúc này hợp đồng được canh **hai chiều**:
  code không lộ ra ngoài hợp đồng, và hợp đồng không có phần nào chưa hiện thực.
- **Cách thực thi:** xóa `Skip` trong `tests/SocialApp.IntegrationTests/IdentityContractTests.cs`.
- **Xong là:** cổng CI `Category=Contract` chạy 2 test, cả hai xanh.
- **Chặn / Cần:** cần D9. **Đây là điều kiện vào cổng đóng.**

---

## B.7 Khối E — Lane frontend

> **Mục tiêu khối:** giao trọn lát cắt dọc — người dùng thật thao tác được trên trình duyệt thật.
> **Không chờ backend:** chỉ cần hợp đồng API, đã có từ cổng mở.

### E1 — Scaffold Next.js 14 + design token

- **Mục tiêu:** có nền để dựng màn, và bộ primitive dùng lại được cho GĐ2–GĐ8.
- **Cách thực thi:** App Router + TypeScript + Tailwind trong `frontend/`. Design token + primitive
  (button, input, form field, alert) trước khi dựng màn — dựng màn trước thì mỗi màn một kiểu.
- **Xong là:** `npm run dev` lên được, có ít nhất một trang dùng primitive.
- **Chặn / Cần:** chặn E2.

### E2 — Sinh type từ hợp đồng + api client + mock MSW

- **Mục tiêu:** biến "đổi hợp đồng mà quên sửa FE" từ lỗi runtime phát hiện muộn ở staging thành
  **lỗi compile** ngay trên máy.
- **Cách thực thi:**

  ```bash
  # chạy từ frontend/
  npx openapi-typescript ../src/Modules/Identity/Presentation/identity-v1.yaml \
      -o src/lib/api/schema.d.ts
  ```

  Đặt thành script `gen:api` trong `package.json` và **commit file sinh ra vào repo** — nghe ngược
  đời, nhưng đó là thứ khiến việc đổi hợp đồng mà quên chạy lại codegen hiện ra thành diff trong PR
  thay vì im lặng tới lúc build ở máy khác. Api client bọc `fetch` với **`credentials: 'include'` ở
  mọi lời gọi**. Mock MSW dựng từ chính các `example` trong hợp đồng.
- **Xong là:** `RoleCode` sinh ra là union `'USER' | 'MODERATOR' | 'ADMIN'`; ba màn E3–E5 chạy được
  hoàn toàn trên mock.
- **Chặn / Cần:** chặn E3–E7. Cần E1.

### E3 — Màn đăng ký

- **Mục tiêu:** FR-001 phía người dùng, và validation client **khớp đúng** validator server.
- **Cách thực thi:** cùng ngưỡng với server (8–72 ký tự). Xử lý 409 thành thông điệp rõ ràng. Sau khi
  thành công, điều hướng sang màn "kiểm tra hộp thư".
- **Xong là:** chạy được trọn trên mock, mọi nhánh lỗi 400/409 có giao diện.
- **Chặn / Cần:** cần E2.

### E4 — Màn đăng nhập

- **Mục tiêu:** dịch bốn mã lỗi thành thông điệp người đọc hiểu, **mà không tiết lộ email có tồn tại
  hay không**.
- **Cách thực thi:** 401 → "Email hoặc mật khẩu không đúng" (đúng một thông điệp cho cả hai trường
  hợp); 403 → "Chưa xác minh email"; 423 → "Tạm khóa, thử lại sau 15 phút"; 429 → "Quá nhiều yêu cầu".
  **Access token giữ trong memory**, không `localStorage`.
- **Xong là:** bốn nhánh có giao diện; DevTools xác nhận không có token trong `localStorage`.
- **Chặn / Cần:** cần E2.

### E5 — Màn xác minh email

- **Mục tiêu:** khép vòng đăng ký từ link trong mail.
- **Cách thực thi:** đọc token từ query string, gọi `/auth/verify-email`. Phân biệt 400 ("liên kết
  không hợp lệ") với 410 ("liên kết đã hết hiệu lực") — GĐ1 **chưa có** endpoint gửi lại mail nên màn
  410 chỉ hướng dẫn, không hứa nút gửi lại.
- **Xong là:** cả ba trạng thái (thành công / 400 / 410) có giao diện.
- **Chặn / Cần:** cần E2.

### E6 — App shell + route guard + trang `/me`

- **Mục tiêu:** có khu vực cần đăng nhập để interceptor ở E7 có chỗ chứng minh tác dụng.
- **Cách thực thi:** shell + guard chuyển hướng khi chưa đăng nhập; trang `/me` hiển thị
  `roleDisplayName` (không hiển thị `role`).
- **Xong là:** vào `/me` khi chưa đăng nhập thì bị đẩy về màn đăng nhập.
- **Chặn / Cần:** chặn E7. Cần E4.

### E7 — Interceptor 401→refresh **single-flight**

- **Mục tiêu:** giữ phiên đăng nhập mượt, và **không để chính frontend kích hoạt reuse detection của
  server**.
- **Cách thực thi:** nhiều request nhận 401 cùng lúc chỉ được gọi `/auth/refresh` **một lần**, số còn
  lại xếp hàng chờ kết quả của lần gọi đó (một promise chia sẻ). Nhận 401 **từ chính `/auth/refresh`**
  thì xóa access token trong memory và chuyển về màn đăng nhập — **không thử refresh lại**.
- **Xong là:** E2E-02 xanh (3 tab, ép hết hạn token đồng thời, không ai bị đăng xuất).
- **Chặn / Cần:** cần E6, D4. **Không làm đúng ở đây thì triệu chứng trông hệt lỗi backend và cả
  nhóm sẽ debug nhầm chỗ rất lâu.**

---

## B.8 Khối F — Cổng đóng

> **Mục tiêu khối:** chứng minh GĐ1 xong **trên hệ thống thật**, không phải trên máy local và không
> phải trên mock. Đây là ranh giới giữa "code chạy" và "giai đoạn hoàn thành".

### F1 — Deploy staging qua CD tự động

- **Mục tiêu:** loại bỏ "chạy được trên máy tôi" khỏi định nghĩa xong.
- **Cách thực thi:** merge vào `develop` → CD build arm64 → GHCR → deploy. **Không thao tác tay trên
  VPS.** Service `migrate` chạy trước `api`.
- **Xong là:** `/health/ready` xanh trên domain HTTPS thật.
- **Chặn / Cần:** cần A6, D. ⚠️ Kiểm `deploy/.env` có đủ `ConnectionStrings__Postgres` và `__Redis`
  **trước khi merge** — app hiện fail-fast khi thiếu, sẽ crash-loop chỗ image cũ vẫn boot được.

### F2 — Frontend bỏ mock, trỏ staging thật

- **Mục tiêu:** đóng rủi ro "mock trôi xa khỏi hiện thực" — xanh trên mock, đỏ trên staging.
- **Cách thực thi:** tắt MSW, trỏ base URL sang domain HTTPS thật.
- **Xong là:** ba màn auth chạy trên dữ liệu thật. **Không giai đoạn nào được nghiệm thu trên mock.**
- **Chặn / Cần:** cần F1, E.

### F3 — E2E-01: lát cắt dọc trên trình duyệt thật

- **Mục tiêu:** kiểm chứng những thứ **integration test không chạm tới được**: cookie `HttpOnly`,
  `SameSite`, `Path` scoping, CORS preflight.
- **Cách thực thi:** đăng ký → nhận mail Mailpit → xác minh → đăng nhập → `GET /me` → ép hết hạn
  access token → interceptor refresh → gọi lại thành công. Trên trình duyệt thật, qua HTTPS.
- **Xong là:** chạy xuyên suốt không lỗi, có ghi lại kết quả.
- **Chặn / Cần:** cần F2.

### F4 — E2E-02: single-flight dưới tải đồng thời

- **Mục tiêu:** đóng rủi ro đăng xuất oan — thứ khó tái hiện nhất khi debug.
- **Cách thực thi:** mở 3 tab, ép token hết hạn, quan sát chỉ **một** lời gọi `/auth/refresh`.
- **Xong là:** không tab nào bị đăng xuất; reuse detection không kích hoạt.
- **Chặn / Cần:** cần F2, E7.

### F5 — Checklist nghiệm thu (Mục 12)

- **Mục tiêu:** rà bốn nhóm — Bảo mật / Phân quyền / Dữ liệu / Vận hành — bằng cách **kiểm tận nơi**,
  không suy đoán.
- **Cách thực thi:** tick từng dòng ở Mục 12. Có dòng phải mở psql đọc trực tiếp (BCrypt cost 12,
  không cột nào chứa token bản rõ), có dòng phải mở DevTools (access token không nằm trong
  `localStorage`).
- **Xong là:** mọi dòng được tick hoặc được ghi lý do hoãn kèm địa chỉ hoãn tới đâu.
- **Chặn / Cần:** cần F3, F4.

### F6 — Definition of Done (Mục 11)

- **Mục tiêu:** chốt bằng tiêu chuẩn chung của dự án, không phải cảm giác "chắc xong rồi".
- **Cách thực thi:** rà đủ 7 mục ở Mục 11.
- **Xong là:** cả 7 tick.
- **Chặn / Cần:** cần F5.

### F7 — Đóng băng hợp đồng API + bàn giao

- **Mục tiêu:** GĐ2 khởi động trên nền ổn định, không phải trên hợp đồng còn đang đổi.
- **Cách thực thi:** thông báo cả nhóm hợp đồng `identity-v1.yaml` đã đóng băng. Ghi lại phần hoãn có
  địa chỉ (bên ghi `revoked:user` → GĐ6; bất biến "≥ 1 Admin" → GĐ6/GĐ8) và điều dễ hiểu nhầm nhất:
  **vai trò được đóng dấu vào token nên đổi vai trò không có hiệu lực ngay** — đến GĐ6 mà không nhớ
  điều này thì sẽ tưởng là bug.
- **Xong là:** GĐ2 bắt đầu được.
- **Chặn / Cần:** cần F6.

---

## B.9 Thứ tự thực thi và đường găng

**Đường găng:** `A1 → A2 → A3 → A4 → A5 → A6` · `C5` · `D3 → D4 → D5 → D8` · `D9 → D11` ·
`F1 → F2 → F3 → F6 → F7`

Khối **B** và **E** không nằm trên đường găng — nên chúng phải chạy **hết công suất song song ngay
từ giờ đầu**, đó là cách hấp thụ việc backend chỉ có 2 người trong khi khối A đang chặn C và D.

Lịch theo ngày (Ngày 3–6) giữ nguyên như Mục 9 của Phần A; Phần B mô tả **thứ tự phụ thuộc**, thứ
không đổi kể cả khi lịch trượt.

**Ba điểm dễ mất dứt điểm nhất — kiểm riêng, đừng tin là mặc nhiên:**

| Nguy cơ | Việc canh |
|---|---|
| Cổng CI xanh giả với 0 test | `B4` — phải thử gõ sai trait một lần và thấy CI đỏ |
| Interceptor không single-flight — triệu chứng trông hệt lỗi backend | `E7` + `F4` |
| Nghiệm thu trên mock hoặc trên máy local | `F2` — Mục 12 cấm |

**Ba thứ không test tự động nào bắt được — bắt buộc code review:**

1. Thứ tự thu hồi **DB trước, Redis sau** (D8). Đảo lại thì user giữ vai trò cũ thêm 15 phút, không
   gì chặn được.
2. TTL `revoked:user` **bằng đúng** TTL access token, và cả hai đọc từ **cùng một** hằng số (D8).
3. `UseAuthentication()` đặt **trước** rate limiter (C4).

---

## B.10 Mục tiêu từng khối — chúng cộng lại thành cái gì

| Khối | Mục tiêu | Thiếu nó thì mất gì |
|---|---|---|
| **A** | Dữ liệu nền đúng, tự bảo vệ, tái lập được ở mọi môi trường | Không có gì để phân quyền; và mất khả năng phát hiện dữ liệu nền bị sửa sai |
| **B** | GOAL-03 thành cổng chặn merge, có khung để GĐ2–GĐ8 thêm dòng | IDOR chỉ được phát hiện ở GĐ8, khi sửa đã đắt |
| **C** | Cơ chế phân quyền viết **một lần** cho cả dự án, dữ liệu hóa | Mỗi module tự chế cách riêng; RBAC hard-code, sai lời hứa Mục 6.7.2 |
| **D** | Hợp đồng thành hệ thống chạy thật, khớp từng mã lỗi | Không có sản phẩm |
| **E** | Lát cắt dọc chạm tới người dùng thật | Backend đúng nhưng không ai dùng được; và GĐ7 gánh toàn bộ backlog frontend |
| **F** | Chứng minh trên hệ thống thật, đóng băng nền cho GĐ2 | "Xong" thành cảm giác chứ không phải sự kiện kiểm chứng được |

---

## B.11 Mục tiêu của GĐ1

### Phát biểu một câu

> **Người dùng thật đăng ký được trên staging, nhận mail xác minh, đăng nhập lấy token, gọi được
> endpoint có bảo vệ — và mọi truy cập sai quyền đều bị chặn đúng mã lỗi.**

### Mục tiêu chính thức và khối nào gánh

| Mã | Mục tiêu | Đạt bằng | Kiểm bằng |
|---|---|---|---|
| **FR-001** | Đăng ký + xác minh email | D1, D2 | AC-04, E2E-01 |
| **FR-002** | Đăng nhập cấp JWT + refresh rotation | D3, D5 | AC-01, RT-01→04 |
| **FR-003** | Khóa tài khoản sau 5 lần sai trong 15 phút | D3 | AC-03 |
| **Mục 6.7.1** | Ba tầng kiểm soát truy cập chạy đủ | C4 (tầng 1), C1–C3 (tầng 2), khuôn tầng 3 | TC-A01/A02, RBAC-01/02 |
| **Mục 6.7.2** | RBAC **dữ liệu hóa** — ma trận trong DB, không hard-code | A4, C5 | SEED-02, kiểm tay ở Mục 12 |
| **NFR-SEC-01** | BCrypt cost 12; refresh token lưu băm | D1, D5 | Đọc trực tiếp DB (Mục 12) |
| **NFR-SEC-03** | Rotation + reuse detection → thu hồi cả chuỗi | D5 | RT-02, RT-04 |
| **GOAL-03** | Nền chống IDOR — cổng chặn merge từ ngày đầu | B2, B3, B4 | Cổng CI `Category=AuthZ` |

### Ba điều kiện để tuyên bố GĐ1 xong

Thiếu bất kỳ điều nào thì **chưa xong**, dù code đã chạy:

1. **Chạy trên staging bằng tài khoản thật**, qua domain HTTPS, deploy bằng CD tự động — không phải
   trên máy local (F1, F3).
2. **Frontend đã bỏ mock**, thao tác trên dữ liệu thật (F2).
3. **CI xanh cả bốn nhóm**: unit + integration, AuthZ matrix, cổng hợp đồng API, ArchUnitNET.

### GĐ1 để lại gì cho GĐ2–GĐ8

Đây mới là lý do GĐ1 đáng làm kỹ — nó là nền móng, không phải một tính năng:

| Di sản | Ai thừa hưởng |
|---|---|
| `[RequirePermission]` + policy handler + `IPermissionCache` ở SharedKernel | Mọi module từ GĐ2 |
| Khung AuthZ matrix data-driven — thêm dòng, không sửa khung | TC-A03 (GĐ2) → TC-A07 (GĐ5, GĐ6) |
| Khuôn module 4 tầng + hợp đồng API + cổng CI đối chiếu | Bảy module còn lại |
| Harness Testcontainers trên Postgres thật | Mọi integration test sau này |
| `ITokenRevocationStore` **bên đọc** đã sẵn sàng | GĐ6 chỉ việc nối bên ghi; GĐ8 nối trigger xóa tài khoản |
| Hợp đồng token + cookie + CORS đã đóng băng | Lane frontend mọi giai đoạn |

**Nguyên tắc mở đầu tài liệu này áp dụng cho đúng chỗ đó:** GĐ1 không phụ thuộc gì, nhưng GĐ2→GĐ8
đều đứng trên nó. Làm ẩu ở đây thì mọi giai đoạn sau đều trả giá.
