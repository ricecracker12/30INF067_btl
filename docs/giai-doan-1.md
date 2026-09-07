# GĐ 1 — Identity & Access (UC-01, UC-02) · Ngày 3–5

> Tài liệu thi công chi tiết cho Giai đoạn 1. Đọc kèm `ke-hoach-trien-khai.md` (lộ trình tổng)
> và `BaoCao_Nhom4_v5.pdf` (Mục 5.5 schema, 6.7 security design, 7.2 verification plan).
>
> **Nguyên tắc:** GĐ1 là nền móng — không phụ thuộc gì, nhưng GĐ2→GĐ8 đều đứng trên nó.
> Làm ẩu ở đây thì mọi giai đoạn sau đều trả giá. "Xong" nghĩa là đạt Definition of Done
> (Mục 11), không phải "chạy được trên máy local".

---

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
- **Một trang HTML tối giản (~1 giờ)** kiểm chứng luồng refresh trong trình duyệt thật — công cụ
  đo, không phải sản phẩm; xóa khi frontend thật thay thế
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
| Frontend Next.js (màn đăng ký/đăng nhập, app shell, interceptor 401→refresh) | **GĐ2** | GĐ1 cả 3 người làm backend. *CORS và quyết định lưu token đã kéo về GĐ1* vì thuộc hợp đồng API. GĐ1 nghiệm thu bằng integration test + Swagger + trang HTML tối giản |
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

> **Ai làm:** GĐ1 **không** có frontend (cả 3 người làm backend). Interceptor này thuộc nhánh
> frontend, khởi động ở **GĐ2** — xem `ke-hoach-trien-khai.md` Mục 0C. Nghĩa là toàn bộ thiết kế
> thu hồi token ở mục này chỉ được kiểm chứng trong trình duyệt thật từ GĐ2; ở GĐ1 nghiệm thu
> bằng integration test (RV-01…RV-04) và Swagger.

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

> **Đóng băng hợp đồng này trước cuối Ngày 4.** GĐ2 khởi động Ngày 5 và người làm Content
> không thể chờ. Sau khi đóng băng, mọi thay đổi phải báo cả nhóm.

---

## 9. Kế hoạch thi công (3 người · Ngày 3–5)

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

### Ngày 3

**Sáng — cả nhóm, ~1 giờ:** chốt 7 quyết định Mục 3 + `ke-hoach-trien-khai.md` GĐ1, thống nhất
hợp đồng token và danh sách endpoint. Không ai code trước khi xong việc này.

**Tiếp theo — dọn nợ kỹ thuật (Mục 9.0), nửa ngày.** Xong mới sang ba khối bên dưới.

Sau đó chia ba khối — chỉ khối A là chặn, B và C chạy song song ngay:

| Khối | Nội dung | Phụ thuộc |
|---|---|---|
| **A. Nền dữ liệu** | Entity + EF config + migration đầu + seeder idempotent | chặn B, C |
| **B. Hạ tầng test** | Testcontainers harness + bảng AuthZ matrix (viết TC-A01/A02 cho **đỏ** trước) | không chặn — làm ngay |
| **C. SharedKernel AuthZ** | `RequirePermissionAttribute` + policy provider + handler + `IPermissionCache` | chỉ cần interface, stub repository |

### Ngày 4

- **A:** `POST /auth/register` + luồng xác minh email qua Mailpit
- **C:** `POST /auth/login` + lockout + phát JWT; nối handler tầng 2 vào repository thật
- **B:** integration test AC-01 → AC-04 chạy trên Postgres thật
- **Cuối ngày: ĐÓNG BĂNG hợp đồng API** và thông báo cho người làm GĐ2

### Ngày 5

- **Refresh rotation + reuse detection + logout** — phần khó nhất, giao cho người chắc tay nhất
- `ITokenRevocationStore` + hook `OnTokenValidated` (Mục 7.5) — khoảng 2 giờ; nối trigger ghi
  cho reuse detection. Nếu Ngày 5 quá tải thì đây là phần cắt được, đẩy sang GĐ6 cùng bên ghi
- Hoàn thiện RFC 7807 cho toàn bộ nhóm auth + cập nhật Swagger
- CORS + cookie refresh + **trang HTML tối giản** kiểm chứng luồng refresh trong trình duyệt
- Deploy staging, **test bằng tài khoản thật** qua domain HTTPS
- Rà Definition of Done (Mục 11), tick checklist nghiệm thu (Mục 12)

> **Lưu ý lịch:** kế hoạch tổng ghi GĐ1 Ngày 3–5 nhưng GĐ2 cũng bắt đầu Ngày 5 → có **1 ngày
> chồng lấn**. Đây là lý do hợp đồng API phải đóng băng từ cuối Ngày 4.

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
- [ ] Swagger cập nhật đầy đủ cho cả 6 endpoint
- [ ] Không lộ secret/PII trong log, response, hay image

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
