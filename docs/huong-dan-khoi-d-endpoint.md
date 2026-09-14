# Hướng dẫn thực hiện — Khối D. Endpoint (GĐ1)

> Bản triển khai chi tiết của **B.6 Khối D** trong [giai-doan-1.md](giai-doan-1.md). Tài liệu gốc trả lời
> *cái gì* và *vì sao*; tài liệu này trả lời *gõ vào file nào, theo thứ tự nào, và nhìn vào đâu để biết
> đã xong thật*.
>
> **Nguồn sự thật vẫn là `giai-doan-1.md`** (Mục 7 luồng nghiệp vụ, Mục 8 hợp đồng, Mục 10.1 mã test),
> hợp đồng [`identity-v1.yaml`](../src/Modules/Identity/Presentation/identity-v1.yaml) và `AGENTS.md`. Chỗ
> nào tài liệu này lệch ba nguồn đó thì sửa ở đây — trừ các quyết định ở Mục 1 được đánh dấu **"ghi
> ngược"**: những chỗ đó tài liệu gốc đang thiếu hoặc sai, phải sửa `giai-doan-1.md` trong cùng commit.

> **Trạng thái: đang làm — `D0`–`D3` và `D7` xong (xem "Thực tế thi công" cuối Mục 2, 3, 4, 5, 9). `D0`–`D2` đã
> commit + push lên `loveart1210` (`37b6b76`, CI xanh — run 34837717169); `D3`, `D7` chưa commit. Tiếp theo `D4`.** Khối A, B, C đã xong
> (commit `105077c` → `2d25ae6`, merge ở `455b597`).

| | |
|---|---|
| **Người làm** | Backend — làm tuần tự theo thứ tự ở Mục 0 |
| **Thời lượng** | Ngày 4 → Ngày 5 |
| **Khối này chặn** | Khối **F** toàn bộ; `E7` (interceptor) cần `D4` mới kiểm chứng thật được |
| **Khối này cần trước** | A (dữ liệu nền), C (JWT, fallback policy, `Result`, `GetUserId`), B (harness Postgres) — **cả ba đã xong** |

---

## 0. Danh sách công việc — mục tiêu và kết quả mong đợi

| Mã | Đầu việc | Mục tiêu — việc này tồn tại để làm gì | Kết quả mong đợi — thứ kiểm chứng được |
|---|---|---|---|
| **D0** | Nền chung của khối: tầng Application, harness test auth | Sáu endpoint dùng chung một bộ viên gạch (băm token, băm mật khẩu, phát JWT, factory test) — không để mỗi endpoint tự chế một kiểu | `SecureToken`, `IPasswordHasher`, `IAccessTokenIssuer` + unit test; `IdentityApiFactory` + `AuthTestClient` trong project test; một test "khung" gọi được `POST /api/v1/auth/register` và nhận 404 (chưa có controller) → chứng minh harness chạy |
| **D1** | `POST /auth/register` + gửi mail xác minh | FR-001 nửa đầu — người thật tạo được tài khoản | `RegisterTests` xanh: 201 đúng hình dạng; hash trong DB bắt đầu `$2` và cost `12`; token xác minh lưu **băm**, hạn 24h; email trùng (kể cả khác hoa thường) → 409; hai request trùng email **song song** → một 201 + một 409, **không 500**; mật khẩu 7 và 73 ký tự → 400 có `errors` + `traceId`. Trên dev: mail hiện trong Mailpit (ảnh chụp trong PR) |
| **D2** | `POST /auth/verify-email` | FR-001 nửa sau — tách rõ 400 và 410 để FE hiển thị khác nhau | `VerifyEmailTests` xanh: token đúng → 200 + `email_verified_at` có giá trị; token lạ → 400; token hết hạn → 410; dùng lần hai → 410; hai request cùng token song song → đúng **một** 200 |
| **D3** | `POST /auth/login` + lockout | FR-002 + FR-003, và **không rò rỉ email nào có thật** (AC-02) | AC-01 → AC-04 xanh trên Postgres thật; 401 "sai mật khẩu" và 401 "email không tồn tại" **giống hệt nhau** sau khi bỏ `traceId`/`instance`; unit test khẳng định nhánh email không tồn tại **vẫn gọi** `Verify` của hasher; **đúng 5** request sai mật khẩu song song → tài khoản **bị khóa** (10 request không bắt được đọc-rồi-ghi — thi công D3) |
| **D4** | Cookie refresh + CORS | Hiện thực quyết định 6 và 7 của cổng mở — thiếu là lane FE đứng im | `RefreshCookie` là chỗ **duy nhất** set/xóa cookie; test đọc `Set-Cookie` thấy đủ 5 thuộc tính; test preflight `OPTIONS` từ `http://localhost:3000` nhận `Access-Control-Allow-Origin` đúng origin + `Allow-Credentials: true`, origin lạ thì không; thiếu `Cors:AllowedOrigins` ngoài Development → từ chối khởi động (test). Kiểm DevTools: cookie đi và về từ `localhost:3000` |
| **D5** | `POST /auth/refresh`: rotation + reuse detection | NFR-SEC-03. **Phần khó nhất của cả giai đoạn** | RT-01 → RT-04 xanh; thêm RT-05 (reuse **sau** 10 giây → cả family bị thu hồi, token kế nhiệm cũng chết); mọi nhánh hỏng trả **cùng một** 401 + xóa cookie; không action nào nhận body |
| **D6** | `POST /auth/logout` | "Đăng xuất thu hồi toàn bộ phiên" (báo cáo Mục 6.7.3) | `LogoutTests` xanh: 204 + cookie bị xóa; sau logout refresh token cũ **và** mọi token cùng family → 401; family của phiên khác (thiết bị khác) không bị ảnh hưởng; **cookie của user B đi kèm bearer của user A → family của B vẫn sống** (tầng 3) |
| **D7** | `GET /me` | Smoke test rẻ nhất chứng minh tầng 1 thông tới DB | 200 đúng hình dạng `MeResponse` với cả `role` lẫn `roleDisplayName` đọc từ DB; không token → 401 (dòng matrix `TC-A01-me`); token hợp lệ của user đã bị xóa → 401 |
| **D8** | `ITokenRevocationStore` + hook `OnTokenValidated` | Dựng **bên đọc** của thu hồi access token để GĐ6 chỉ việc gọi; không có nó thì hạ quyền trễ tới 15 phút | RV-01 → RV-04 xanh trên Redis thật (Testcontainers); test đọc `TTL revoked:user:<id>` khớp `AccessTokenSeconds + ClockSkewSeconds` (Đ-D4); RV-03 nối với reuse detection của D5; AuthZ matrix **không chậm đi** khi Redis không tới được (fail-open nhanh) |
| **D9** | RFC 7807 cho cả nhóm + `[ProducesResponseType]` | Một hình dạng lỗi duy nhất, và làm cho cổng hợp đồng chiều 2 xanh được | Mọi mã trong hợp đồng (trừ 429/500) được khai trên action; chạy `Contract_must_be_fully_implemented` **cục bộ không Skip** → xanh; rà thông điệp lỗi không chứa email, id, stack trace |
| **D10** | `[ApiExplorerSettings]` cho mọi controller | Endpoint không biến mất khỏi Swagger trong im lặng | `PresentationBoundaryTests.Every_controller_must_declare_a_swagger_group` xanh sau **mỗi** commit thêm controller — không phải việc dồn cuối |
| **D11** | Gỡ `Skip` của `Contract_must_be_fully_implemented` | Bật nốt chiều 2 của cổng hợp đồng — từ đây hợp đồng được canh hai chiều | Cổng CI `Category=Contract` chạy **2** test, cả hai xanh. **Điều kiện vào cổng đóng** |

> `D0` không có trong B.6 của tài liệu gốc — tách ra ở đây vì cả sáu endpoint cùng đứng trên nó; dựng dần trong
> lúc làm D1/D3 là mỗi endpoint một cách băm token và một kiểu factory test.

### Thứ tự thực thi

```
D0 ─→ D1 ─→ D2 ─→ D3 ─→ D7 ─→ D4 ─→ D5 ─→ D6 ─→ D8 ─→ D9 ─→ D11

D10: làm cùng lúc với mỗi controller, không phải một bước riêng
```

- **`D0` trước tiên.** Nó quyết định hình dạng mọi file phía sau.
- **`D7` ngay sau `D3`**: `/me` là cách rẻ nhất xác nhận token vừa phát dùng được, và các test phía sau (RV-03,
  RT-01) gọi nó để kiểm access token còn sống hay không.
- **`D4` trước `D5`**: `/auth/refresh` đọc refresh token từ cookie — chưa có `RefreshCookie` thì không test được.
- **`D8` sau `D5`**: RV-03 kiểm reuse detection của D5 ghi `revoked:user`. Khi làm D5, nhánh `ReuseDetected` chỉ
  thu hồi family ở DB; D8 thêm lời gọi `RevokeUserAsync` vào đúng chỗ đó.
- **`D9` cuối cùng nhưng chạy thử sớm** — xem mẹo ở Mục 11.

**Mốc đo tiến độ:** hết Ngày 4 xong `D0`–`D3` + `D7` (FE bỏ mock được cho đăng ký/đăng nhập ở dev); trưa Ngày 5
xong `D4`, `D5`; hết Ngày 5 xong `D6`, `D8`, `D9`, `D11`.

**Phần cắt được nếu trễ:** chỉ `D8`, dời sang GĐ6 — nhưng khi đó RV-01→04 và hai dòng checklist Mục 12 dời
theo, **ghi rõ trong PR và trong `giai-doan-1.md`**, không lặng lẽ bỏ.

---

## 1. Trước khi gõ dòng đầu tiên

### Điều kiện cần

```bash
docker ps                                                  # Testcontainers cần Docker
docker compose -f deploy/docker-compose.dev.yml up -d      # Postgres + Redis + Mailpit cho kiểm tay
dotnet build SocialApp.sln
dotnet test SocialApp.sln                                  # mốc so sánh — đỏ sẵn thì đừng bắt đầu
```

`deploy/.env` ở máy dev phải có `POSTGRES_PASSWORD` và `Jwt__SigningKey` (đã bắt buộc từ khối A/C).

### Package

| Cần | Trạng thái | Dùng cho |
|---|---|---|
| `BCrypt.Net-Next` 4.0.3 | **Đã có** ở Identity | `D1`, `D3` |
| `FluentValidation.AspNetCore` 11.3.0 | **Đã có** ở Identity | `D1`–`D3` |
| `StackExchange.Redis` 2.8.16 | Đã có ở **Api** → **chuyển sang SharedKernel** (Đ-D8) | `D8` |
| `Microsoft.IdentityModel.JsonWebTokens` | **Thêm vào Identity, ghim `7.1.2`** — đúng version JwtBearer 8.0.10 đang kéo về (`project.assets.json` của Api). Ghim khác là NU1605 downgrade hoặc hai bản thư viện ký/validate lệch nhau | `D3`, `D5` — phát access token |
| `Testcontainers.Redis` | **Thêm vào IntegrationTests, ghim `4.0.0`** — cùng version `Testcontainers.PostgreSql` | `D8` |
| SMTP | **Không thêm gói** — `System.Net.Mail.SmtpClient` của BCL (Đ-D9) | `D1` |

### Luật của repo áp thẳng vào khối này

1. **Impact analysis trước khi sửa symbol có sẵn** (`CLAUDE.md`). Khối D sửa những symbol nhiều người gọi:
   `AddIdentityModule` (Api, `PostgresFixture`, mọi test Postgres), `JwtOptions` + `RequireJwtOptions`,
   `AddSharedKernel`, `ApiFactory`/`AuthZApiFactory`. HIGH/CRITICAL thì dừng lại báo nhóm.
   ```bash
   node .gitnexus/run.cjs impact "AddIdentityModule" --direction upstream --repo .
   ```
   Trước **mỗi** commit: `node .gitnexus/run.cjs detect-changes --scope all --repo .` (`partial`/`truncated`
   thì chạy lại).
2. **Controller chỉ ở `Modules/Identity/Presentation/`**, khai `[ApiExplorerSettings(GroupName = IdentityApiGroup.Name)]`.
3. **Chỉ `Infrastructure` chạm EF, chỉ `Presentation` chạm MVC** (`PersistenceBoundaryTests`,
   `PresentationBoundaryTests`). Service ở `Application` không được inject `IdentityDbContext`.
4. **Đổi hình dạng API → sửa `identity-v1.yaml` cùng commit.** Mục tiêu của khối D là **không phải sửa** file
   đó: hợp đồng đã chốt, code chạy theo hợp đồng.
5. **Không commit secret.** `Smtp__Password`, `Jwt__SigningKey` chỉ ở `.env`.
6. **Không dùng `Skip` để né đỏ.** `D11` gỡ cái `Skip` cuối cùng; không thêm cái mới.
7. **Cấu hình JWT lấy `IOptions<JwtOptions>` từ DI**, không bind lại section `Jwt` — bind lại thì mất khóa
   fallback đọc từ `deploy/.env` ở Development (hướng dẫn B+C, Mục 13.1).

### Mười quyết định đã chốt

Tài liệu gốc chưa nói đủ để gõ code ở mười chỗ dưới đây. **Ghi ngược** = phải sửa `giai-doan-1.md` trong
cùng commit với code của việc đó.

**Đ-D1 — Chia tầng: controller mỏng, service ở `Application`, SQL ở `Infrastructure`.**

| Tầng | File | Chạm gì |
|---|---|---|
| `Presentation/` | `AuthController` (5 action), `MeController`, `RefreshCookie` | MVC, cookie, `Result → HTTP` |
| `Application/` | DTO + validator; `RegistrationService`, `LoginService`, `SessionService`, `MeQuery`; interface `IIdentityUserStore`, `IEmailVerificationStore`, `IRefreshTokenStore`, `IPasswordHasher`, `IAccessTokenIssuer`, `IEmailSender`; `SecureToken`; `IdentityErrors` | Không EF, không MVC |
| `Infrastructure/` | `Persistence/*Store` (EF + SQL `RETURNING`/`FOR UPDATE`), `Security/BCryptPasswordHasher`, `Security/JwtAccessTokenIssuer`, `Email/SmtpEmailSender` | EF, Npgsql, BCrypt, IdentityModel |

- **Vì sao:** hai lưới ArchUnit đã có ép đúng hình này. Logic "sai 5 lần thì khóa", "trong 10 giây thì ân hạn"
  unit test được mà không dựng DB; phần phải nguyên tử (`UPDATE … RETURNING`, `FOR UPDATE`) nằm gọn trong store
  và được integration test trên Postgres thật.
- **Rotation (D5) là ngoại lệ có ý thức:** cả chuỗi khóa dòng → quyết định → ghi phải nằm trong **một**
  transaction, nên `IRefreshTokenStore.RotateAsync` giữ trọn thuật toán, service chỉ phát access token và gọi
  revocation. Tách quyết định ra service là phải kéo transaction qua ranh giới tầng.

**Đ-D2 — Token bản rõ là 32 byte ngẫu nhiên dạng hex thường (64 ký tự); lưu SHA-256 hex của chuỗi đó.**

- Một helper duy nhất `Application/Security/SecureToken.cs`: `Generate()` và `Hash(string)`. Dùng cho **cả**
  token xác minh email lẫn refresh token.
- **Vì sao hex:** an toàn trong URL và cookie không cần mã hóa thêm; khớp ví dụ cookie trong hợp đồng; thỏa
  `minLength: 32` của `VerifyEmailRequest`. .NET 8 chưa có `Base64Url`.
- **Bẫy:** băm **byte UTF-8 của chuỗi nhận được**, không băm 32 byte gốc — lúc verify server chỉ có chuỗi.

**Đ-D3 — Ân hạn 10 giây phát token anh em cùng family, không "trả lại token kế nhiệm".** *(đã ghi ngược: Mục 7.3, B.6/D5, `identity-v1.yaml`)*

- Mục 7.3 viết "trả lại token kế nhiệm". **Không làm được:** DB chỉ giữ băm của token kế nhiệm, bản rõ đã đi
  mất trong response của tab thắng.
- **Quyết định:** trong ân hạn, sinh token **mới** cùng `family_id`, không đụng token kế nhiệm. Family tạm có hai
  lá còn sống; cookie jar của trình duyệt giữ cái đến sau, cái kia hết hạn tự nhiên.
- **"Chuỗi chưa bị thu hồi"** định nghĩa bằng dữ liệu: `EXISTS (… WHERE family_id = @f AND revoked_at IS NULL)`
  — trúng index một phần `idx_refresh_family`. Không suy từ `revoked_at` của token đang cầm: token bị **xoay**
  cũng có `revoked_at`.
- **Đánh đổi:** trong 10 giây, kẻ cầm token cũ xin được một lá mới — đúng cửa sổ Mục 7.3 đã chấp nhận.

**Đ-D4 — TTL của `revoked:user` = `AccessTokenSeconds + ClockSkewSeconds`.** *(đã ghi ngược: Mục 7.5, 12, 14, B.6/D8, B.9; `identity-v1.yaml`; `ke-hoach-trien-khai.md`; hướng dẫn B+C)*

- Tài liệu gốc viết "bằng **đúng** TTL access token". Nhưng `Program.cs` đặt `ClockSkew = 30s`: token phát lúc
  `iat < T` được chấp nhận tới `iat + 900 + 30`, còn key đặt lúc `T` hết hạn lúc `T + 900` → **cửa sổ tới 30
  giây** token đã thu hồi sống lại. Đúng loại lỗ hổng câm Mục 7.5 cảnh báo.
- **Quyết định:** thêm `JwtOptions.ClockSkewSeconds = 30` (hằng số), `Program.cs` đọc nó thay cho số `30` ghi
  tay; revocation store đặt `EX AccessTokenSeconds + ClockSkewSeconds`. Test so TTL với **tổng hai hằng số**.
- Tinh thần Mục 7.5 giữ nguyên — *cùng nguồn cấu hình*; chỉ con số được sửa cho đúng.

**Đ-D5 — Đăng ký: INSERT trong transaction → gửi mail → COMMIT.**

- **Vấn đề:** GĐ1 không có endpoint gửi lại mail. Commit trước rồi gửi mail hỏng → tài khoản kẹt vĩnh viễn:
  không xác minh được, đăng ký lại thì 409.
- **Quyết định:** gửi mail **trước** commit. SMTP hỏng → rollback → 500, người dùng thử lại được. Commit hỏng
  sau khi mail đã đi → link trỏ vào token không tồn tại → 400 ở verify, đăng ký lại vẫn được.
- **Đánh đổi:** giữ transaction qua một lời gọi SMTP (vài trăm ms) — chấp nhận ở quy mô đồ án. Hàng đợi
  outbox để GĐ7 nếu cần.

**Đ-D6 — Logout: cookie thiếu, lạ, hoặc của người khác → vẫn 204 + xóa cookie, không thu hồi gì.**

- Hợp đồng chỉ có 204/401; 401 dành cho bearer hỏng. Logout idempotent là hành vi FE mong đợi.
- **Tầng 3:** chỉ thu hồi khi `refresh_tokens.user_id == User.GetUserId()`. Bỏ điều kiện này thì ai cầm cookie
  của người khác (máy dùng chung) đăng xuất được họ — nhỏ, nhưng đúng là IDOR.
- **AuthZ matrix:** khung hiện tại chỉ so status code và không gửi cookie, nên kiểm chứng tầng 3 nằm ở
  `LogoutTests.Cookie_cua_nguoi_khac_khong_bi_thu_hoi` (quan sát hệ quả: family của B vẫn refresh được). Ghi
  một comment trong `AuthZMatrix.cs` trỏ tới test đó để luật AGENTS.md Mục 10 vẫn truy được.

**Đ-D7 — Test không đụng hạn mức 10 req/phút: `IStartupFilter` gán IP giả cho từng request.**

- Policy `auth` phân vùng theo IP. Trong `TestServer` mọi request có `RemoteIpAddress = null` → chung vùng
  `"anon"` → test thứ 11 của cả lớp nhận **429**, đỏ ngẫu nhiên theo thứ tự chạy.
- **Quyết định:** `Harness/FakeRemoteIpStartupFilter` (chỉ trong assembly test) đứng đầu pipeline: có header
  `X-Test-Remote-Ip` thì dùng, không thì sinh IP ngẫu nhiên. Test rate limit riêng (`Auth_rate_limit_429`) gửi
  cùng một IP 11 lần. **Không** thêm cờ tắt rate limit vào `src/`.

**Đ-D8 — `ITokenRevocationStore` và hiện thực Redis ở SharedKernel.**

- Bên đọc ở host (`OnTokenValidated`), bên ghi ở Identity (D5), GĐ6 thêm bên ghi ở Moderation → chỗ hợp lệ duy
  nhất là SharedKernel. Chuyển `StackExchange.Redis` sang SharedKernel; Api nhận lại qua project reference.
- `IConnectionMultiplexer` singleton, `AbortOnConnectFail = false`, timeout ngắn (`ConnectTimeout` 2000,
  `SyncTimeout`/`AsyncTimeout` 250 ms). **Chưa kết nối thì fail-open ngay**, không chờ timeout — nếu không,
  `ApiFactory`/`AuthZApiFactory` (Redis cố ý không tới được) sẽ chậm vài giây **mỗi request có token**.

**Đ-D9 — SMTP bằng `System.Net.Mail.SmtpClient`, cấu hình `Smtp:*` đã có sẵn trong `.env.example`.**

- Đủ cho Mailpit (1025, không TLS) và Brevo (587, STARTTLS qua `EnableSsl = true`). Không thêm MailKit ở GĐ1.
- Development không đặt `Smtp:Host` → mặc định `localhost:1025` (Mailpit của compose dev). **Ngoài
  Development thiếu `Smtp:Host`/`Smtp:From` → từ chối khởi động**, cùng tinh thần `RequireConnectionString`.
- Link trong mail: `{Frontend:BaseUrl}/verify-email?token=<hex>` — màn `E5`. `Frontend:BaseUrl` là key mới.
- **`deploy/.env` của nhóm chính là file chép lên server staging**, nên nó mang giá trị **staging**
  (`Frontend__BaseUrl=https://mxh.banhgao.net`, `Smtp__Host=mailpit`). Quy tắc cho `Smtp:*` và `Frontend:BaseUrl`:
  - **Development:** không đặt biến thì dùng mặc định trong code — `Frontend:BaseUrl = http://localhost:3000`,
    `Smtp:Host = localhost`, `Smtp:Port = 1025`. **Không** mở rộng `DevEnvFile` để đọc các key này từ
    `deploy/.env`: đọc vào thì mail đăng ký ở máy dev trỏ về frontend staging, trong khi token nằm trong DB local
    → bấm link nhận 400; và `mailpit` không phân giải được ngoài mạng compose.
  - **Ngoài Development:** bắt buộc đặt qua biến môi trường (`env_file: ./.env` của compose staging), thiếu thì
    từ chối khởi động.
  - Test: `StartupConfigurationTests` thêm case Development không đặt gì → link trong mail bắt đầu bằng
    `http://localhost:3000/verify-email?token=`.
- **Staging GĐ1 gửi qua Mailpit — đã chốt.** Service `mailpit` đã có trong `docker-compose.staging.yml` (và bản
  `.apache.yml`), mạng `internal`, UI chỉ bind `127.0.0.1:8025` — xem qua SSH tunnel, **không** route qua
  Caddy vì UI chứa link xác minh của mọi tài khoản. `.env` staging: `Smtp__Host=mailpit`, `Smtp__Port=1025`,
  `Smtp__From`, `Frontend__BaseUrl` (mẫu ở `.env.example`, bảng theo môi trường ở `oci-setup.md` mục vi).
  Lý do: khớp Mục 12 và E2E-01 ("nhận mail Mailpit"), không cần xác thực tên miền. Chuyển sang SMTP thật
  sau GĐ1 chỉ sửa `.env`.

**Đ-D10 — Thời gian: app dùng `TimeProvider`; test lùi mốc thời gian trong DB thay vì làm giả đồng hồ.**

- Service/store nhận `TimeProvider` (SharedKernel đã `TryAddSingleton(TimeProvider.System)`), truyền `now` vào
  SQL làm tham số — không dùng `now()` của Postgres, cùng nguồn thời gian Mục 4.
- **Test AC-03 "mở lại sau 15 phút", RT-03 "hết hạn", D2 "410 hết hạn"**: `UPDATE … SET locked_until/expires_at
  = now() - interval '1 second'` rồi gọi lại. **Không** thay `TimeProvider` bằng đồng hồ giả trong test HTTP:
  JwtBearer validate bằng giờ thật, token phát theo giờ giả sẽ bị coi là hết hạn hoặc chưa hiệu lực.
- Ân hạn 10 giây (RT-04) chạy bằng giờ thật; RT-05 (quá 10 giây) lùi `revoked_at` của token cũ 11 giây.

---

## 2. D0 — Nền chung của khối

**Mục tiêu.** Sáu endpoint dùng chung một bộ viên gạch. Viết một lần, có unit test một lần, trước endpoint đầu
tiên — nếu không mỗi endpoint dựng dần một cách băm token và một kiểu factory test.

**Kết quả mong đợi.**
- `src/Modules/Identity/Application/Security/`: `SecureToken.cs`, `IPasswordHasher.cs`, `IAccessTokenIssuer.cs`.
- `src/Modules/Identity/Application/IdentityErrors.cs` — mọi `Error` của nhóm auth ở một chỗ.
- `src/Modules/Identity/Infrastructure/Security/`: `BCryptPasswordHasher.cs`, `JwtAccessTokenIssuer.cs`.
- `JwtOptions` thêm `RefreshTokenDays` (mặc định 7) và hằng số `ClockSkewSeconds = 30`; `RequireJwtOptions`
  kiểm `RefreshTokenDays > 0`; `Program.cs` dùng `JwtOptions.ClockSkewSeconds` thay số `30`.
- Unit test xanh: `SecureTokenTests`, `BCryptPasswordHasherTests`, `JwtAccessTokenIssuerTests`.
- `tests/SocialApp.IntegrationTests/Auth/`: `IdentityApiFactory.cs`, `AuthTestClient.cs`,
  `CapturingEmailSender.cs`; `Harness/FakeRemoteIpStartupFilter.cs`.
- `AddIdentityModule` đăng ký service/store (rỗng dần theo từng D), Program.cs gọi
  `AddFluentValidationAutoValidation()` **một lần** ở host.

### Các bước

**Bước 1 — `SecureToken` (Đ-D2).**

```csharp
using System.Security.Cryptography;
using System.Text;

namespace SocialApp.Modules.Identity.Application.Security;

/// <summary>
/// Token bản rõ gửi cho client (link xác minh, cookie refresh) và băm lưu DB. MỘT chỗ cho cả hai loại token:
/// hai cách băm là hai nguồn sự thật — verify-email so băm lệch thì mọi link đều 400 mà không có lỗi nào.
/// Bản rõ không bao giờ vào DB hay log (NFR-SEC-01).
/// </summary>
public static class SecureToken
{
    public const int ByteLength = 32;

    /// <summary>32 byte ngẫu nhiên, hex thường — 64 ký tự, an toàn trong URL và cookie.</summary>
    public static string Generate() =>
        Convert.ToHexString(RandomNumberGenerator.GetBytes(ByteLength)).ToLowerInvariant();

    /// <summary>SHA-256 hex thường của byte UTF-8 của chuỗi nhận được — khớp cột varchar(64).</summary>
    public static string Hash(string token) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();
}
```

Unit test: `Generate()` dài 64, chỉ `[0-9a-f]`, hai lần gọi khác nhau; `Hash("abc")` bằng chuỗi SHA-256 **viết tay**
(`ba7816bf…`) — không so với chính `Hash`.

**Bước 2 — hasher mật khẩu.**

```csharp
namespace SocialApp.Modules.Identity.Application.Security;

public interface IPasswordHasher
{
    string Hash(string password);
    bool Verify(string password, string hash);

    /// <summary>
    /// Chạy một phép Verify tốn đúng chi phí như thật, trên hash giả — cho nhánh "email không tồn tại" của
    /// login (Mục 7.2 bước 2). Là method riêng để unit test khẳng định được là nó ĐÃ bị gọi.
    /// </summary>
    void VerifyAgainstDummy(string password);
}
```

```csharp
namespace SocialApp.Modules.Identity.Infrastructure.Security;

internal sealed class BCryptPasswordHasher : IPasswordHasher
{
    public const int WorkFactor = 12;   // NFR-SEC-01. Đổi số này là đổi thời gian của CẢ HAI nhánh login.

    // Hash giả sinh lúc khởi động bằng CÙNG WorkFactor. Hash giả cost 10 ghi cứng thì nhánh email không tồn tại
    // nhanh hơn 4 lần — đúng thứ AC-02 cấm, và không test nội dung nào bắt được.
    private static readonly Lazy<string> DummyHash =
        new(() => BCrypt.Net.BCrypt.HashPassword(SecureToken.Generate(), WorkFactor));

    public string Hash(string password) => BCrypt.Net.BCrypt.HashPassword(password, WorkFactor);
    public bool Verify(string password, string hash) => BCrypt.Net.BCrypt.Verify(password, hash);
    public void VerifyAgainstDummy(string password) => BCrypt.Net.BCrypt.Verify(password, DummyHash.Value);
}
```

Unit test: hash có đoạn cost `$12$` (tách theo `$`, phần tử thứ 2 là `"12"` — viết tay); verify đúng/sai.

**Bước 3 — phát access token.** Thêm `Microsoft.IdentityModel.JsonWebTokens` 7.1.2 vào Identity.

```csharp
namespace SocialApp.Modules.Identity.Application.Security;

public sealed record AccessToken(string Token, int ExpiresIn);

public interface IAccessTokenIssuer
{
    /// <summary><paramref name="roleCode"/> là roles.code CHUỖI đọc từ DB — không bao giờ role_id (Mục 3.1).</summary>
    AccessToken Issue(Guid userId, string roleCode);
}
```

```csharp
internal sealed class JwtAccessTokenIssuer(IOptions<JwtOptions> options, TimeProvider time) : IAccessTokenIssuer
{
    private readonly JwtOptions _jwt = options.Value;
    private readonly JsonWebTokenHandler _handler = new();

    public AccessToken Issue(Guid userId, string roleCode)
    {
        var now = time.GetUtcNow().UtcDateTime;
        var token = _handler.CreateToken(new SecurityTokenDescriptor
        {
            Issuer = _jwt.Issuer,
            Audience = _jwt.Audience,
            IssuedAt = now,
            NotBefore = now,
            Expires = now.AddSeconds(_jwt.AccessTokenSeconds),   // CÙNG hằng số với TTL revoked:user (D8)
            Claims = new Dictionary<string, object>
            {
                [JwtClaims.Sub] = userId.ToString(),
                [JwtClaims.Role] = roleCode,
                [JwtClaims.Jti] = Guid.NewGuid().ToString(),
            },
            SigningCredentials = new SigningCredentials(
                new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.SigningKey)), SecurityAlgorithms.HmacSha256),
        });
        return new AccessToken(token, _jwt.AccessTokenSeconds);
    }
}
```

Unit test giải mã token vừa phát bằng `JsonWebTokenHandler.ReadJsonWebToken`: có `sub`, `role`, `iat`, `jti`
(tên claim **viết tay**), `exp - iat == AccessTokenSeconds`, header `alg == HS256`. Thêm một test validate token
bằng đúng `TokenValidationParameters` như `Program.cs` → hợp lệ — bắt lệch khóa/issuer giữa hai phía.

> `iat` do handler tự ghi từ `IssuedAt` dưới dạng số giây Unix — `OnTokenValidated` của D8 đọc đúng dạng đó.

**Bước 4 — `IdentityErrors`.** Một chỗ cho mọi thông điệp — dễ rà PII ở D9, và AC-02 cần **một** đối tượng lỗi
duy nhất cho hai nhánh 401.

```csharp
using SocialApp.SharedKernel.Results;

namespace SocialApp.Modules.Identity.Application;

public static class IdentityErrors
{
    public static readonly Error EmailTaken = new("identity.email_taken", "Email này đã được đăng ký.", 409);
    public static readonly Error VerifyTokenInvalid = new("identity.verify_invalid", "Liên kết xác minh không hợp lệ.", 400);
    public static readonly Error VerifyTokenGone = new("identity.verify_gone", "Liên kết xác minh đã hết hạn hoặc đã được sử dụng.", 410);

    /// <summary>AC-02: DÙNG CHUNG cho email không tồn tại và sai mật khẩu. Không tạo lỗi thứ hai "cho rõ".</summary>
    public static readonly Error InvalidCredentials = new("identity.invalid_credentials", "Email hoặc mật khẩu không đúng.", 401);
    public static readonly Error EmailNotVerified = new("identity.email_not_verified", "Tài khoản chưa xác minh email. Vui lòng kiểm tra hộp thư.", 403);
    public static readonly Error Locked = new("identity.locked", "Tài khoản tạm khóa do đăng nhập sai nhiều lần. Vui lòng thử lại sau 15 phút.", 423);

    /// <summary>MỌI nhánh hỏng của /auth/refresh (D5) — không phân biệt hết hạn/thu hồi/reuse.</summary>
    public static readonly Error SessionInvalid = new("identity.session_invalid", "Phiên đăng nhập không còn hiệu lực. Vui lòng đăng nhập lại.", 401);
}
```

`ToActionResult` của SharedKernel ánh xạ theo `Error.Status` nên 400/401/409/410/423 đi thẳng, không sửa
SharedKernel (Đ5 của hướng dẫn B+C).

**Bước 5 — validation ở host, validator ở module.**

```csharp
// Program.cs, ngay sau AddControllers()...AddJsonOptions(...)
builder.Services.AddFluentValidationAutoValidation();   // MỘT lần cho cả host — GĐ2 không gọi lại

// AddIdentityModule
services.AddValidatorsFromAssemblyContaining<RegisterRequestValidator>(ServiceLifetime.Singleton);
```

- Không gọi `AddFluentValidationAutoValidation` trong `AddIdentityModule`: đó là cấu hình MVC toàn cục; bảy
  module mỗi module gọi một lần là lỗi validate bị lặp.
- Tên trường trong `errors` phải là camelCase như hợp đồng (`email`, không `Email`). Đặt một lần ở host:
  `ValidatorOptions.Global.PropertyNameResolver = (_, member, _) => member is null ? null : JsonNamingPolicy.CamelCase.ConvertName(member.Name);`
  — test D1 khẳng định key `"password"`.

**Bước 6 — harness test auth.**

`Harness/FakeRemoteIpStartupFilter.cs` (Đ-D7):

```csharp
/// <summary>
/// TestServer để RemoteIpAddress = null → mọi request chung vùng rate limit "anon" → test thứ 11 nhận 429.
/// Đứng ĐẦU pipeline. Chỉ tồn tại trong assembly test.
/// </summary>
public sealed class FakeRemoteIpStartupFilter : IStartupFilter
{
    public const string Header = "X-Test-Remote-Ip";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use((ctx, nextMw) =>
        {
            ctx.Connection.RemoteIpAddress = ctx.Request.Headers.TryGetValue(Header, out var ip)
                ? IPAddress.Parse(ip!)
                : new IPAddress(RandomNumberGenerator.GetBytes(4));
            return nextMw(ctx);
        });
        next(app);
    };
}
```

`Auth/IdentityApiFactory.cs` — theo khuôn `AuthZApiFactory`, khác ở ba chỗ:

```csharp
builder.ConfigureTestServices(services =>
{
    services.AddSingleton<IStartupFilter, FakeRemoteIpStartupFilter>();
    services.AddSingleton<CapturingEmailSender>();
    services.AddSingleton<IEmailSender>(sp => sp.GetRequiredService<CapturingEmailSender>());
});
// + UseSetting("ConnectionStrings:Redis", _redis ?? ApiFactory.UnreachableRedis) — D8 truyền Redis thật
```

Mỗi **lớp** test auth dùng `postgres.CreateDatabaseAsync()` + `MigrateIdentityModuleAsync()` (test **sửa** dữ
liệu — luật B1), email mỗi test sinh ngẫu nhiên nên các test trong lớp không giẫm nhau.

`Auth/AuthTestClient.cs` — bọc `HttpClient`, **tự quản cookie bằng tay**:

```csharp
var client = factory.CreateClient(new WebApplicationFactoryClientOptions
{
    HandleCookies = false,                          // RT-02 phải gửi lại cookie CŨ — cookie jar tự động không cho
    BaseAddress = new Uri("https://localhost"),     // cookie Secure không đi qua http:// (nếu có dùng cookie jar)
});
```

Helper cần có: `RegisterAndVerifyAsync(email, password)` (lấy token từ `CapturingEmailSender`),
`LoginAsync` → `(accessToken, refreshCookie)`, `RefreshAsync(refreshCookie)`, `LogoutAsync(access, cookie)`,
`ReadSetCookie(response)` (parse **không phân biệt hoa thường** — ASP.NET ghi `path=`, `httponly`, `samesite=lax`),
và `ExecuteSqlAsync` để lùi mốc thời gian (Đ-D10).

### Cạm bẫy đã biết

- **Hash giả ghi cứng với cost khác 12** → xem comment bước 2.
- **Tự phát token bằng `TestJwt` trong test D-block.** `TestJwt` sinh `sub` ngẫu nhiên không có trong DB. Test
  endpoint thật lấy token bằng `LoginAsync`; `TestJwt` chỉ dùng cho D8 (cần `iat` tùy ý).
- **Quên `services.AddSingleton<IStartupFilter, …>` ở factory** → test đỏ 429 ngẫu nhiên, trông như lỗi flaky.

### Thực tế thi công

**Bằng chứng.** `dotnet test SocialApp.sln`: Unit 32 → 44, Architecture 9, Integration 40 → 46 (+1 Skip vẫn là
`Contract_must_be_fully_implemented`, gỡ ở `D11`). Thử cho đỏ ở local rồi khôi phục:

| Đột biến | Test đỏ |
|---|---|
| Hash giả của `BCryptPasswordHasher` dùng cost 10 | `VerifyAgainstDummy_ton_chi_phi_ngang_Verify_that` — 63 ms so với 264 ms |
| `JwtAccessTokenIssuer` phát `aud` lệch | 2 test của `JwtAccessTokenIssuerTests` + `AuthHarnessTests.Register_chua_co_controller_token_cua_app_qua_tang_1_nhan_404` (nhận 401) |

**Chỗ lệch so với các bước trên — đã làm như sau:**

- Test "khung" **không** nhận 404 khi ẩn danh: fallback policy chặn cả route chưa tồn tại → **401**. Tách thành hai
  test trong `Auth/AuthHarnessTests`: có bearer do `IAccessTokenIssuer` **của app** phát → 404 (đồng thời chứng minh
  phía phát khớp JwtBearer của `Program.cs`); ẩn danh → 401. `D1` đã xóa hai test này nhưng giữ lưới bắt lệch token trên route không tồn tại (xem thi công `D1`).
- `Application/Email/IEmailSender` khai ngay ở `D0` (tài liệu để ở `D1` bước 3): `CapturingEmailSender` và
  `IdentityApiFactory` cần nó để compile. `SmtpEmailSender` vẫn là việc của `D1`.
- `AddIdentityModule` đăng ký validator bằng `AddValidatorsFromAssembly(typeof(IdentityModuleExtensions).Assembly, Singleton)`
  thay cho `…Containing<RegisterRequestValidator>` — chưa có validator nào, và `D1`–`D3` không phải sửa dòng này. Thêm
  `TryAddSingleton(TimeProvider.System)` để module tự đủ khi dựng ngoài host.
- Unit test lấy `IPasswordHasher`/`IAccessTokenIssuer` qua `AddIdentityModule` (`UnitTests/Identity/IdentityServices`)
  thay vì `InternalsVisibleTo` — cùng khuôn `RolePermissionSourceTests`, test luôn dòng đăng ký DI.
- Thêm ngoài tài liệu:
  - `appsettings.json` khai `Jwt:RefreshTokenDays: 7`; `StartupConfigurationTests.Non_positive_refresh_token_days_must_fail_fast`.
  - `BCryptPasswordHasherTests` có test thời gian cho hash giả (lấy min của 3 lần đo, ngưỡng 0,5).
  - `JwtAccessTokenIssuerTests` cấu hình `AccessTokenSeconds = 600` để bắt lỗi ghi cứng 900, và kiểm `iat` lấy từ `TimeProvider`.
  - `AuthTestClient` có thêm `GetMeAsync` (`D7`); `ReadSetCookie` dùng `SetCookieHeaderValue` của ASP.NET Core.
- `FakeRemoteIpStartupFilter` **chưa có test riêng**: chưa endpoint nào mang policy `auth`. `Auth_rate_limit_429` kiểm
  nó từ `D1`.

---

## 3. D1 — `POST /auth/register` + gửi mail xác minh

**Mục tiêu.** FR-001 nửa đầu — người thật tạo được tài khoản, và mật khẩu lẫn token xác minh không bao giờ nằm
trong DB ở dạng bản rõ.

**Kết quả mong đợi.**
- `Application/Registration/`: `RegisterRequest`, `RegisterResponse`, `RegisterRequestValidator`,
  `RegistrationService.RegisterAsync`; `Application/Email/IEmailSender`.
- `Infrastructure/Persistence/IdentityUserStore` (thêm user + token trong một transaction),
  `Infrastructure/Email/SmtpEmailSender` + `SmtpOptions`.
- `Presentation/AuthController` với action `Register` — `[AllowAnonymous]`, `[EnableRateLimiting("auth")]`,
  `[ApiExplorerSettings]` (D10) ở mức class.
- `Auth/RegisterTests` xanh (bảng dưới). Ảnh chụp Mailpit trên dev trong PR.

### Các bước

**Bước 1 — DTO và validator** (khớp `RegisterRequest` của hợp đồng: `email` ≤ 254, `password` 8–72).

```csharp
public sealed record RegisterRequest([property: Required] string Email, [property: Required] string Password);
public sealed record RegisterResponse(Guid UserId, string Email);

public sealed class RegisterRequestValidator : AbstractValidator<RegisterRequest>
{
    public RegisterRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().MaximumLength(254).EmailAddress().WithMessage("Email không đúng định dạng.");
        // 72 là trần CỨNG của BCrypt: ký tự thứ 73 bị bỏ qua âm thầm. Đếm BYTE UTF-8, không đếm ký tự —
        // "mật khẩu" tiếng Việt 40 ký tự có thể quá 72 byte.
        RuleFor(x => x.Password).NotEmpty()
            .MinimumLength(8).WithMessage("Mật khẩu phải có ít nhất 8 ký tự.")
            .Must(p => Encoding.UTF8.GetByteCount(p ?? "") <= 72).WithMessage("Mật khẩu tối đa 72 byte.");
    }
}
```

`[Required]` có mặt để Swagger đánh dấu `required: [email, password]` — cổng hợp đồng chiều 2 so đúng tập này
(Mục 11). Nó thuộc `System.ComponentModel.DataAnnotations`, không phải MVC → hợp lệ ở `Application`.

**Bước 2 — service (Đ-D5).**

```csharp
public async Task<Result<RegisterResponse>> RegisterAsync(RegisterRequest req, CancellationToken ct)
{
    var now = time.GetUtcNow();
    var user = new User
    {
        Email = req.Email.Trim(),
        PasswordHash = hasher.Hash(req.Password),
        RoleId = await users.GetRoleIdAsync(RoleCodes.User, ct),   // dịch code → id trong store (Mục 3.1)
    };
    var plain = SecureToken.Generate();
    var token = new EmailVerificationToken { UserId = user.UserId, TokenHash = SecureToken.Hash(plain), ExpiresAt = now.AddHours(24) };

    // Store mở transaction, INSERT user + token, gọi callback gửi mail, rồi COMMIT. Email trùng → false.
    var created = await users.AddWithVerificationAsync(user, token,
        beforeCommit: () => email.SendVerificationAsync(user.Email, plain, ct), ct);

    return created ? new RegisterResponse(user.UserId, user.Email) : IdentityErrors.EmailTaken;
}
```

- **Email trùng phát hiện bằng unique violation, không bằng `SELECT` trước.** Kiểm trước rồi insert là
  đọc-rồi-ghi: hai request song song cùng qua bước kiểm, request thứ hai chết bằng `23505` → **500**. Store bắt
  `PostgresException { SqlState: PostgresErrorCodes.UniqueViolation }` (EF bọc trong `DbUpdateException.InnerException`)
  và trả `false`. Có thể giữ thêm một `AnyAsync` trước để khỏi tốn BCrypt cho email trùng — nhưng unique
  violation vẫn là lưới cuối.
- Cột `citext` lo phần hoa thường; **không** `ToLowerInvariant()` email trước khi lưu — người dùng thấy lại đúng
  cách họ gõ.

**Bước 3 — gửi mail.**

```csharp
public interface IEmailSender
{
    Task SendVerificationAsync(string toEmail, string plainToken, CancellationToken ct);
}
```

`SmtpEmailSender` dựng link `{Frontend:BaseUrl}/verify-email?token={plainToken}`. **Không log link hay token**
— log `"Đã gửi mail xác minh"` kèm `UserId` là đủ. Cấu hình (Đ-D9) đăng ký trong `AddIdentityModule`, đọc từ
`IConfiguration`, fail-fast ngoài Development.

**Bước 4 — controller.**

```csharp
[ApiController]
[Route("api/v1/auth")]
[EnableRateLimiting(SharedKernelExtensions.AuthRateLimitPolicy)]        // cả nhóm auth, 10 req/phút/IP
[ApiExplorerSettings(GroupName = IdentityApiGroup.Name)]
// KHÔNG [Produces("application/json")] ở class: nó ép cả response lỗi thành application/json (đã kiểm — thi công D1)
public sealed class AuthController(RegistrationService registration /* , ... */) : ControllerBase
{
    [AllowAnonymous]                                                   // TỪNG action công khai, không ở class
    [HttpPost("register")]
    [Consumes("application/json")]                                     // không đặt ở class: refresh/logout không có body
    [ProducesResponseType<RegisterResponse>(StatusCodes.Status201Created)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict, "application/problem+json")]
    public async Task<ActionResult<RegisterResponse>> Register(RegisterRequest request, CancellationToken ct)
    {
        var result = await registration.RegisterAsync(request, ct);
        return result.IsSuccess ? StatusCode(StatusCodes.Status201Created, result.Value) : result.ToActionResult(this);
    }
}
```

- `ToActionResult<T>` trả **200** khi thành công — register cần **201**, nên controller rẽ nhánh như trên.
  Không dùng `CreatedAtAction`: không có endpoint `GET /users/{id}` để trỏ tới.
- **`[AllowAnonymous]` đặt ở từng action, không ở class.** Đặt ở class thì `logout` thêm `[Authorize]` cũng vô
  ích — `[AllowAnonymous]` thắng mọi `[Authorize]` — và endpoint cần token mở toang mà không test nào của D6
  đỏ nếu test chỉ thử đường có token. D6 có test "không bearer → 401" chính vì thế.

### Test — `Auth/RegisterTests`

| Test | Kỳ vọng |
|---|---|
| Đăng ký hợp lệ | 201, body có `userId` (UUID v7: ký tự thứ 13 là `7`) + `email`; `CapturingEmailSender` nhận đúng 1 mail |
| Đọc DB sau đăng ký | `password_hash` bắt đầu `$2`, cost `12`, **khác** mật khẩu; `role_id` là role có `code = 'USER'`; `email_verified_at IS NULL` |
| Token trong DB | `token_hash` = SHA-256 của token trong mail (tính trong test bằng `SHA256` BCL, không gọi `SecureToken`); **không** cột nào chứa token bản rõ; `expires_at` ≈ now + 24h (sai số 1 phút) |
| Email trùng, khác hoa thường | 409 `problem+json` có `traceId` |
| Hai request cùng email song song (`Task.WhenAll`) | Tập status = {201, 409}; **không có 500** |
| Mật khẩu 7 ký tự / 73 byte / email sai | 400, `errors` có key `password` / `email` (camelCase), có `traceId` |
| Body có field lạ `{"email":…,"password":…,"role":"ADMIN"}` | 400 (`UnmappedMemberHandling.Disallow`) — và **không** user nào được tạo |
| `IEmailSender` ném exception (factory thay bằng sender hỏng) | 500 **và** không có dòng `users` nào với email đó (Đ-D5) |

### Cạm bẫy đã biết

- **Kiểm trùng bằng `SELECT` rồi mới insert** → 500 khi song song. Xem bước 2.
- **Validator đếm ký tự thay vì byte** → mật khẩu có dấu dài 60 ký tự qua validator nhưng bị BCrypt cắt.
- **Log request body** ở `UseSerilogRequestLogging` hoặc log thủ công khi debug → mật khẩu bản rõ vào log.
  Serilog request logging mặc định không log body; đừng bật.
- **Email mở `ProblemDetails.detail` bằng thông điệp có chứa email** ("an@x.com đã tồn tại") → PII trong
  response và log. Dùng đúng thông điệp của `IdentityErrors`.

### Thực tế thi công

**Bằng chứng.** `dotnet test SocialApp.sln`: Unit 44, Architecture 9, Integration 46 → 64 (+13 `RegisterTests`,
+6 `StartupConfigurationTests`, `AuthHarnessTests` −2 +1). Cổng hợp đồng chiều 1 xanh với `POST /auth/register`
201/400/409.

Kiểm tay trên dev (API chạy từ `bin/` với `Development`, Postgres + Mailpit của compose dev, chưa có ảnh chụp):
`--migrate` thoát 0 → `POST /api/v1/auth/register` trả 201 với `userId` UUID v7 → API Mailpit
(`/api/v1/search?query=to:<email>`) thấy đúng 1 thư, `From: no-reply@socialapp.local`, link
`http://localhost:3000/verify-email?token=<64 hex>` → log stdout của API **không** chứa token lẫn email.

Thử cho đỏ ở local rồi khôi phục:

| Đột biến | Test đỏ |
|---|---|
| Store bắt unique violation của index khác `IX_users_email` | `Email_trung_khac_hoa_thuong_…` (500), `Hai_request_cung_email_song_song_…` ({201, 500}) |
| COMMIT trước rồi mới gửi mail | `Gui_mail_hong_500_va_khong_con_dong_users_nao` (còn 1 dòng) |
| Validator đếm ký tự thay vì byte | `Du_lieu_sai_400_…` với 25 × "ấ" (nhận 201) |
| `Program.cs` quên `AddIdentityEmail` | 4 case `Missing_email_config_…`, `Development_khong_dat_cau_hinh_mail_…`, và cả `Development_boots_without_deploy_env_…` có sẵn (DI validate lúc build thấy `RegistrationService` thiếu `IEmailSender`) |

**Chỗ lệch so với các bước trên — đã làm như sau:**

- **DTO request là class có `[Required]` trên property**, không phải positional record. *Đã kiểm bằng controller thử
  tạm:* `[property: Required]` trên record thì Swagger ghi `required` nhưng validation của MVC **bỏ qua âm thầm** (body
  rỗng vẫn 200); `[Required]` gắn vào tham số constructor thì ngược lại — MVC validate mà Swagger không thấy. Class
  không có chỗ tách đôi đó. (Giả thuyết ban đầu "MVC ném lỗi" là **sai**. Vì host đã tắt DataAnnotations, record +
  `[property: Required]` cũng chạy được — chọn class để không phụ thuộc điều đó.)
- **Host tắt DataAnnotations validation** (`AddFluentValidationAutoValidation(o => o.DisableDataAnnotationsValidation = true)`);
  `[Required]` chỉ còn phục vụ Swagger. *Đã kiểm:* để bật thì `{"email":"","password":""}` trả `errors` với key
  **PascalCase** `Email`/`Password`, mỗi key gồm cả "The Email field is required." lẫn thông điệp tiếng Việt — hợp đồng
  cần `email`. (Giả thuyết ban đầu "hai key `Email` và `email`" sai chi tiết: ModelState không phân biệt hoa thường nên
  hai lỗi dồn vào một key.)
- **Validator email dùng `MailAddress.TryCreate`** thay cho `EmailAddress()` (chỉ kiểm có `@`): địa chỉ lọt validator
  mà `MailMessage` không nhận thì SMTP ném giữa transaction → 500. Chặn cả dạng `Tên <a@b.com>`.
- **Không khai `[Produces("application/json")]` ở class controller:** filter đó ép cả response lỗi thành
  `application/json`, trong khi 409 phải là `application/problem+json`. *Đã kiểm bằng controller thử tạm:* có
  `[Produces]` thì `Problem()` 409 và 400 tự sinh (JSON sai) đều ra `application/json`; bỏ đi thì `Problem()` ra
  `application/problem+json`.
- **Cấu hình mail đăng ký qua `AddIdentityEmail(configuration, environment)`**, không nằm trong `AddIdentityModule`:
  6 chỗ trong test dựng `AddIdentityModule(cs)` trần. Kiểm cấu hình ném ngay lúc đăng ký DI, sau `RequireJwtOptions`.
- **Đ-D9 cụ thể hóa:**
  - Ngoài Development bắt buộc cả `Smtp:Port` (không chỉ Host/From) và `Frontend:BaseUrl`.
  - Development mặc định thêm `Smtp:From = no-reply@socialapp.local`.
  - `Smtp:EnableSsl` là key tùy chọn — không đặt thì bật khi có `Smtp:User`.
  - Gửi mail tự cắt sau 15 giây (`SendMailAsync` bỏ qua `SmtpClient.Timeout`) để không giữ transaction đăng ký vô hạn.
  - Thân mail mã hóa base64: quoted-printable ngắt dòng và mã hóa `=` trong URL.
- **Store chỉ coi unique violation của `IX_users_email` là email trùng**; unique violation khác vẫn ra 500. COMMIT không
  nhận `ct`: mail đã đi thì hủy commit chỉ để lại một link chết.
- **Test link mail ở Development** gửi bằng `SmtpEmailSender` thật tới `Harness/FakeSmtpServer` (TCP loopback). Test
  chỉ đặt `Smtp:Port` vì Mailpit của compose dev giữ cổng 1025 trên máy chạy test — nên mặc định `localhost` và
  `http://localhost:3000` được kiểm, còn cổng 1025 thì không.
- **Thêm ngoài tài liệu:** `Staging_boots_when_email_config_is_complete` (lưới không phải "luôn ném"); case
  `Tên <a@b.com>` → 400; `docker-compose.dev.yml` đặt `Smtp__Host: mailpit` cho service `api` (trong mạng compose,
  Mailpit không phải `localhost`) và vẫn không nạp `env_file`; `AuthTestClient.QueryRowAsync` — SQL thô so cột `citext` phải ép `$1::citext`: Npgsql gửi tham số chuỗi dạng `text`,
  và `citext = text` **không lỗi mà phân biệt hoa thường** (đã kiểm: `'an@example.com'::citext = 'AN@EXAMPLE.COM'` →
  `false`; ép `::citext` → `true`).
- **`AuthHarnessTests`:** gỡ hai test "register chưa có controller" như đã hẹn ở `D0`, nhưng giữ lưới bắt lệch giữa phía
  phát token và JwtBearer trên một route vĩnh viễn không tồn tại (`/api/v1/__khong-ton-tai`: có token → 404, ẩn danh →
  401) — không để lưới đó biến mất tới `D3`.

---

## 4. D2 — `POST /auth/verify-email`

**Mục tiêu.** FR-001 nửa sau, và tách rõ **400** (link sai) với **410** (link hết hạn/đã dùng) để FE hiển thị
khác nhau.

**Kết quả mong đợi.**
- `VerifyEmailRequest` (`[Required] Token`), `VerifyEmailResponse(Email, VerifiedAt)`, validator (64 ký tự hex —
  sai định dạng thì 400 ngay, không chạm DB).
- `IEmailVerificationStore.ConsumeAsync(hash, now)` trả một trong `Verified(email, at) | NotFound | Gone`.
- Action `VerifyEmail`: 200 / 400 / 410, `[AllowAnonymous]`.
- `Auth/VerifyEmailTests` xanh (bảng dưới).

### Các bước

**Bước 1 — tiêu thụ token nguyên tử.** Đọc-rồi-ghi thì hai request cùng token song song đều thấy
`consumed_at IS NULL` và cùng trả 200. Làm bằng một câu `UPDATE … RETURNING`, trong transaction cùng với việc
set `users.email_verified_at`:

```csharp
// Infrastructure/Persistence/EmailVerificationStore.cs — trong BeginTransactionAsync
var userIds = await db.Database.SqlQuery<Guid>($"""
    UPDATE identity.email_verification_tokens
       SET consumed_at = {now}
     WHERE token_hash = {hash} AND consumed_at IS NULL AND expires_at > {now}
    RETURNING user_id AS "Value"
    """).ToListAsync(ct);

if (userIds.Count == 0)
{
    // Không tiêu thụ được: phân biệt "không tồn tại" (400) với "hết hạn / đã dùng" (410).
    var exists = await db.EmailVerificationTokens.AnyAsync(t => t.TokenHash == hash, ct);
    return exists ? VerifyOutcome.Gone : VerifyOutcome.NotFound;
}

await db.Users.Where(u => u.UserId == userIds[0])
    .ExecuteUpdateAsync(s => s
        .SetProperty(u => u.EmailVerifiedAt, now)
        .SetProperty(u => u.UpdatedAt, now), ct);   // ExecuteUpdate đi vòng ChangeTracker — tự set UpdatedAt (Mục 4)
```

**Bước 2 — controller** ánh xạ `NotFound → IdentityErrors.VerifyTokenInvalid`, `Gone → VerifyTokenGone`, rồi
`ToActionResult`.

### Test — `Auth/VerifyEmailTests`

| Test | Kỳ vọng |
|---|---|
| Token trong mail | 200, `verifiedAt` có giá trị; DB: `users.email_verified_at` và `consumed_at` được set |
| Token 64 hex nhưng không tồn tại | 400 |
| Token sai định dạng (`"abc"`) | 400 có `errors.token` |
| Lùi `expires_at` về quá khứ bằng SQL (Đ-D10) rồi verify | **410** |
| Verify lần hai | **410** |
| Hai request cùng token song song | Đúng **một** 200, một 410 |

> Chuỗi "verify xong thì login hết 403" kiểm ở `D3` (AC-01 đi đăng ký → verify → login): lúc làm `D2` chưa có
> `/auth/login`. `D2` chỉ kiểm hệ quả ở DB (dòng đầu bảng).

### Cạm bẫy đã biết

- **`SqlQuery<Guid>` thiếu alias `"Value"`** → EF 8 ném lúc chạy: truy vấn scalar bắt buộc cột tên `Value`.
- **Nối `.FirstOrDefaultAsync()` vào `SqlQuery` của câu `UPDATE`** → EF bọc thành
  `SELECT … FROM (UPDATE …) LIMIT 1` và Postgres báo lỗi cú pháp. Dùng `.ToListAsync()` — không compose.
- **Tính 410 trước 400**: token không tồn tại mà trả 410 thì FE nói "link hết hạn" cho link bị gõ sai.
- **`ExecuteUpdateAsync` quên `UpdatedAt`** → không test nào đỏ, `updated_at` đứng im. Ghi trong review.

### Thực tế thi công

**Bằng chứng.** `dotnet test SocialApp.sln`: Unit 44, Architecture 9, Integration 64 → 76 (+12 `VerifyEmailTests`).
Cổng hợp đồng chiều 1 xanh với `POST /auth/verify-email` 200/400/410.

Kiểm tay trên dev (API chạy từ `bin/` với `Development`, Postgres + Mailpit của compose dev): register 201 → lấy token
64 hex từ link trong mail qua API Mailpit → verify lần 1 **200** `{"email":…,"verifiedAt":…}` → lần 2 **410**
`application/problem+json` → log stdout của API **không** chứa token lẫn email.

Thử cho đỏ ở local (ba đột biến áp cùng lúc)
rồi khôi phục:

| Đột biến | Test đỏ |
|---|---|
| Bỏ `consumed_at IS NULL` khỏi câu `UPDATE` | `Verify_lan_hai_410` (nhận 200), `Hai_request_cung_token_song_song_…` (`[OK, OK]`) |
| Đảo nhánh `Gone` / `NotFound` | `Token_dung_dinh_dang_nhung_khong_ton_tai_400_…` (nhận 410), `Token_het_han_410_…` (nhận 400) |
| Validator nhận mọi token | 5/6 case `Token_sai_dinh_dang_400_co_errors_token` (case chuỗi rỗng vẫn bị `NotEmpty` chặn) |

**Chỗ lệch so với các bước trên — đã làm như sau:**

- **Đánh dấu user đã xác minh bằng câu `UPDATE identity.users … RETURNING email` (`SqlQuery<string>`)** thay cho
  `ExecuteUpdateAsync`: response cần `email`, mà `ExecuteUpdateAsync` không trả dữ liệu. Câu này tự set `updated_at`.
- **Kết quả store là `VerifyEmailOutcome`** (record lồng `Verified(UserId, Email, VerifiedAt)` · `NotFound` · `Gone`), đặt
  cạnh `IEmailVerificationStore` ở `Application/`. `UserId` có mặt chỉ để service log, không ra response.
- **Service là `RegistrationService.VerifyEmailAsync`** — tài liệu không nêu tên; cùng FR-001 với đăng ký.
- **Validator so từng ký tự** thay cho `Matches("^[0-9a-f]{64}$")`: `$` của .NET khớp cả trước `\n` cuối chuỗi. Chỉ nhận
  hex **thường** — đúng thứ `SecureToken.Generate` sinh ra.
- **Test thêm ngoài bảng:** 6 dạng token sai (rỗng, 63, 65, hex HOA, ký tự `g`, `"abc"`); token hết hạn thì user **vẫn chưa**
  xác minh và body 410 không chứa email; `verifiedAt` trong response khớp `email_verified_at` trong DB (lệch < 1 ms);
  `Helper_RegisterAndVerifyAsync_…` khóa helper mà D3 trở đi dựa vào.
- Dòng "sau verify, login hết 403" chuyển sang AC-01 của `D3` (đã ghi ở bảng test phía trên).
- **Phát hiện khi kiểm tay, chuyển cho `D9`:** body 410 **không có `title`** (409 của D1 thì có nhưng là `"Conflict"` tiếng
  Anh), trong khi `ProblemDetails` của hợp đồng bắt buộc `title`. Không sửa lẻ ở D2 — chi tiết ở Mục 11.

---

## 5. D3 — `POST /auth/login` + lockout

**Mục tiêu.** FR-002 (cấp JWT + refresh) và FR-003 (khóa sau 5 lần sai), **không rò rỉ email nào có thật**
(AC-02) — cả qua nội dung lẫn thời gian phản hồi.

> **Đã chốt (2026-09-14): khóa sau 5 lần sai LIÊN TIẾP**, không có cửa sổ thời gian — đúng thuật toán ở bước 2. Hợp
> đồng từng ghi "5 lần trong 15 phút"; đã sửa câu chữ ở `identity-v1.yaml` (mô tả mã 423), `giai-doan-1.md` (FR-003,
> phạm vi, bảng mục tiêu) và `ke-hoach-trien-khai.md`. Chỉ đổi chữ, không đổi hình dạng API — lane FE không phải sinh
> lại type. Hệ quả chấp nhận: sai 4 lần hôm qua + 1 lần hôm nay vẫn khóa; đăng nhập đúng một lần là bộ đếm về 0.

**Kết quả mong đợi.**
- `LoginRequest` (`[Required]` email, password ≤ 72 byte), validator.
- `LoginService.LoginAsync` đúng 6 bước Mục 7.2; trả `Result<LoginSuccess>` với `LoginSuccess(AccessToken, string RefreshPlain)`.
- `IIdentityUserStore`: `FindForLoginAsync(email)` (kèm `roles.code`), `RegisterFailedLoginAsync(userId, now)`
  (nguyên tử), `ResetFailedLoginAsync(userId, now)`.
- `IRefreshTokenStore.CreateAsync(userId, familyId: Uuid7.New(), hash, expiresAt, ip)`.
- Action `Login`: 200 `TokenResponse` + cookie (ráp `RefreshCookie` của D4 ngay khi có; trước đó tạm set
  cookie trong action và thay ở D4).
- `Auth/LoginTests` (AC-01 → AC-04 + phần mở rộng) xanh; `LoginServiceTests` (unit) xanh.

### Các bước

**Bước 1 — service, đúng thứ tự Mục 7.2.**

```csharp
public async Task<Result<LoginSuccess>> LoginAsync(LoginRequest req, string? ip, CancellationToken ct)
{
    var now = time.GetUtcNow();
    var user = await users.FindForLoginAsync(req.Email.Trim(), ct);

    // 2. Không tồn tại → VẪN tốn một phép BCrypt rồi mới trả. Bỏ dòng này là thời gian phản hồi lộ email.
    if (user is null) { hasher.VerifyAgainstDummy(req.Password); return IdentityErrors.InvalidCredentials; }

    // 3. Đang khóa → 423 (hợp đồng chấp nhận 423 lộ tồn tại — người thật cần biết vì sao không vào được).
    if (user.LockedUntil > now) return IdentityErrors.Locked;

    // 4. Sai mật khẩu → tăng bộ đếm NGUYÊN TỬ trong DB; đạt 5 thì khóa 15 phút và reset. Vẫn 401, không 423:
    //    lần sai thứ 5 trả 401, lần thử KẾ TIẾP mới thấy 423.
    if (!hasher.Verify(req.Password, user.PasswordHash))
    {
        await users.RegisterFailedLoginAsync(user.UserId, now, ct);
        return IdentityErrors.InvalidCredentials;               // CÙNG đối tượng lỗi với nhánh 2
    }

    // 5. Chưa xác minh → 403. Mật khẩu đúng nhưng KHÔNG reset bộ đếm và KHÔNG phát token.
    if (user.EmailVerifiedAt is null) return IdentityErrors.EmailNotVerified;

    // 6. Thành công.
    await users.ResetFailedLoginAsync(user.UserId, now, ct);
    var refresh = SecureToken.Generate();
    await refreshTokens.CreateAsync(user.UserId, familyId: Uuid7.New(), SecureToken.Hash(refresh),
        expiresAt: now.AddDays(jwt.RefreshTokenDays), ip, ct);   // family MỚI: một lần login = một thiết bị
    return new LoginSuccess(tokens.Issue(user.UserId, user.RoleCode), refresh);
}
```

**Bước 2 — tăng bộ đếm nguyên tử.**

```csharp
// Infrastructure/Persistence/IdentityUserStore.cs
await db.Database.ExecuteSqlAsync($"""
    UPDATE identity.users
       SET failed_login_count = CASE WHEN failed_login_count + 1 >= {MaxFailed} THEN 0 ELSE failed_login_count + 1 END,
           locked_until       = CASE WHEN failed_login_count + 1 >= {MaxFailed} THEN {now.Add(LockDuration)} ELSE locked_until END,
           updated_at         = {now}
     WHERE user_id = {userId}
    """, ct);
```

`MaxFailed = 5`, `LockDuration = 15 phút` là hằng số trong `Domain/LockoutPolicy.cs` — unit test khóa giá trị
viết tay. Hai `CASE` đọc cùng một giá trị cũ của `failed_login_count` trong cùng câu lệnh nên không lệch nhau.
Không cần `RETURNING` vì service không dùng kết quả; tài liệu gốc ghi `UPDATE … RETURNING` — điều cốt lõi là
**một câu lệnh**, không phải `RETURNING`.

**Bước 3 — controller.** `ip = HttpContext.Connection.RemoteIpAddress` (sau Caddy là IP của Caddy — chấp nhận
ở GĐ1, `created_ip` chỉ để điều tra; bật `ForwardedHeaders` là việc của F1 nếu cần).

```csharp
[AllowAnonymous]
[HttpPost("login")]
[Consumes("application/json")]
[ProducesResponseType<TokenResponse>(StatusCodes.Status200OK)]
[ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden, "application/problem+json")]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status423Locked, "application/problem+json")]
public async Task<ActionResult<TokenResponse>> Login(LoginRequest request, CancellationToken ct)
{
    var result = await login.LoginAsync(request, HttpContext.Connection.RemoteIpAddress?.ToString(), ct);
    if (result.IsFailure) return result.Error!.Value.ToActionResult(this);
    RefreshCookie.Set(Response, result.Value!.RefreshPlain, jwt.RefreshTokenDays);
    return Ok(new TokenResponse(result.Value.Access.Token, result.Value.Access.ExpiresIn));
}
```

`ToActionResult<T>` của SharedKernel trả `ActionResult<LoginSuccess>` — không khớp kiểu trả về
`ActionResult<TokenResponse>` của action. Thêm vào `ResultHttpExtensions` một overload public
`Error.ToActionResult(ControllerBase)` bọc đúng hàm `Problem` private đang có (chạy impact analysis cho
`ResultHttpExtensions` trước). Login, refresh, logout đều cần nó; không tự gọi `Problem(...)` trong controller
— hai chỗ dựng ProblemDetails là hai chỗ lệch nhau.

### Test — `Auth/LoginTests` (integration) và `LoginServiceTests` (unit)

| Mã | Test | Kỳ vọng |
|---|---|---|
| AC-01 | Đăng ký → verify → login đúng | 200, `expiresIn == 900`; `accessToken` qua được tầng 1 — gọi `/api/v1/__khong-ton-tai` nhận **404** chứ không 401 (cùng lưới `AuthHarnessTests`; `/me` chưa có ở D3, `D7` đổi lời gọi này thành `GET /me` → 200); có `Set-Cookie: refresh_token=…`; DB có 1 dòng `refresh_tokens` với `token_hash` = SHA-256 của cookie |
| AC-01b | Login bằng email viết HOA của tài khoản vừa đăng ký | 200 — bắt so `citext` với tham số `text` (xem cạm bẫy) |
| AC-02 | Sai mật khẩu | 401; `failed_login_count` = 1 |
| AC-02b | Email không tồn tại vs sai mật khẩu | Body JSON **bằng nhau** sau khi xóa `traceId` và `instance`; header giống nhau (trừ `X-Correlation-ID`) |
| AC-02c | Thời gian (thô, chống quên BCrypt giả) | Trung vị 5 lần mỗi nhánh: nhánh không tồn tại ≥ 50% nhánh sai mật khẩu. **Không** so chặt — CI dao động |
| AC-02 unit | `LoginServiceTests.Email_khong_ton_tai_van_goi_hasher` | Hasher giả đếm được `VerifyAgainstDummy` gọi **1** lần. Đây là lưới chính; AC-02c chỉ là lưới phụ |
| AC-03 | Sai 5 lần liên tiếp rồi thử lần 6 **với mật khẩu đúng** | Lần 1–5: 401; lần 6: **423** |
| AC-03b | Lùi `locked_until` về quá khứ bằng SQL, login đúng | 200; `failed_login_count = 0`, `locked_until IS NULL` |
| AC-03c | **Đúng 5** request sai mật khẩu **song song** (mỗi request một IP ngẫu nhiên của `FakeRemoteIpStartupFilter`) | Cả 5 là 401; sau đó login đúng → **423**. Không dùng 10 request: 5 lần thừa bù được các lần đếm bị mất nên đọc-rồi-ghi vẫn xanh (đã thử) |
| AC-03d | Gọi thẳng `IIdentityUserStore.RegisterFailedLoginAsync` 5 lần đồng thời, mỗi lần một scope DI | `failed_login_count = 0`, `locked_until` có giá trị. Không có BCrypt đệm nên cửa sổ tranh chấp lớn nhất — lưới chính của Mục 7.2 "Concurrency" |
| AC-04 | Đăng ký, **không** verify, login đúng | 403 |
| AC-04b | Chưa verify, sai mật khẩu | 401 (bước 4 đứng trước bước 5) |
| — | Login thành công sau 3 lần sai | `failed_login_count` về 0 |
| — | Hai lần login thành công | Hai `family_id` khác nhau |

### Cạm bẫy đã biết

- **Đọc `FailedLoginCount` lên C#, `+1`, `SaveChanges`** → AC-03c (5 request) và AC-03d đỏ. Bước 2. Test với **10**
  request song song thì **vẫn xanh** dù đọc-rồi-ghi — đã thử ở thi công D3; đừng "tăng số request cho chắc".
- **Tạo `Error` 401 thứ hai "cho dễ debug"** (vd "User not found") → AC-02b đỏ. May mắn thì test bắt; dùng
  `IdentityErrors.InvalidCredentials` cho cả hai nhánh để khỏi phải may mắn.
- **Kiểm `email_verified_at` trước mật khẩu** → ai cũng dò được email nào đã đăng ký mà chưa verify (403 không
  cần biết mật khẩu). Thứ tự 6 bước là hợp đồng.
- **Test AC-03 với 6 request cùng một `HttpClient` không có `FakeRemoteIpStartupFilter`** → cộng với test khác
  trong lớp là quá 10/phút → 429. Xem Đ-D7.
- **`FindForLoginAsync` viết SQL thô `WHERE email = {email}`** → Npgsql gửi tham số dạng `text`, `citext = text`
  **phân biệt hoa thường mà không lỗi** (đã kiểm sau D1) → người đăng ký `An@x.com` gõ `an@x.com` nhận 401. Ép
  `::citext`, hoặc dùng LINQ `u.Email == email` và để AC-01b xác nhận.
- **Access token phát từ `role_id`** (`"1"`) → `PermissionHandler` không nhận ra vai trò nào. `FindForLoginAsync`
  join `roles` lấy `code`.

### Thực tế thi công

**Bằng chứng.** `dotnet test SocialApp.sln`: Unit 44 → 53 (+8 `LoginServiceTests`, +1 `LockoutPolicyTests`), Architecture
9, Integration 76 → 92 (+16 `LoginTests`). Cổng hợp đồng chiều 1 xanh với `POST /auth/login` 200/400/401/403/423. Sau khi
khôi phục, AC-03c + AC-03d chạy 5 lần liên tiếp trên code đúng: 5/5 xanh.

Kiểm tay trên dev (API chạy từ `bin/` với `Development`, Postgres + Mailpit của compose dev): register 201 → verify 200 →
login bằng email **viết HOA** 200, `expiresIn=900`, `Set-Cookie: refresh_token=<64 hex>; max-age=604800; path=/api/v1/auth;
secure; samesite=lax; httponly` → bearer vừa nhận gọi route không tồn tại 404 → sai mật khẩu và email không tồn tại đều 401,
body giống hệt sau khi bỏ `traceId` → log stdout của API **không** chứa mật khẩu, access token, cookie refresh lẫn email.
Body 401 có `title: "Unauthorized"` tiếng Anh — cùng việc `title` đã chuyển cho D9.

Thử cho đỏ ở local rồi khôi phục:

| Đột biến | Test đỏ |
|---|---|
| Bỏ `hasher.VerifyAgainstDummy` ở nhánh email không tồn tại | `LoginServiceTests.Email_khong_ton_tai_…` (0 lần gọi), `AC02c_…` (2 ms so với 249 ms — tỉ lệ 0,01) |
| Kiểm xác minh email **trước** mật khẩu | `LoginServiceTests.Chua_xac_minh_sai_mat_khau_…` và `AC04b_…` (nhận 403) |
| `FindForLoginAsync` bằng `FromSql … WHERE email = {email}` (tham số `text`) | `AC01b_dang_nhap_bang_email_khac_hoa_thuong_200` (nhận 401) |
| Bộ đếm đọc-rồi-ghi qua EF (`SingleAsync` → `++` → `SaveChanges`) | **Bản đầu của AC-03c (10 request song song) vẫn XANH.** Sửa thành đúng 5 request + thêm AC-03d ở tầng store; chạy lại 3 lần trên đột biến: cả hai đỏ cả 3 lần (AC-03d chỉ đếm được 4/5, AC-03c đăng nhập sau đó nhận 200) |

**Chỗ lệch so với các bước trên — đã làm như sau:**

- **AC-03c đổi từ 10 xuống đúng 5 request, thêm AC-03d** (gọi store đồng thời, không qua BCrypt). Câu "đọc-rồi-ghi sẽ đỏ
  ở đây" của tài liệu gốc là **sai** với 10 request: số lần thừa bù cho các lần đếm bị mất, còn BCrypt ~250 ms làm các
  request lệch pha nhau.
- **`RefreshCookie` tạo luôn ở D3** bằng đúng code của D4 bước 1, thay vì set cookie tạm trong action rồi D4 xóa đi.
- **`LoginService.LoginAsync` nhận `IPAddress?`** thay vì `string?`: cột `created_ip` là `inet`, entity là `IPAddress`.
- **Store trả `LoginCandidate(UserId, PasswordHash, RoleCode, EmailVerifiedAt, LockedUntil)`**; `FindForLoginAsync` dùng
  LINQ join `roles`, không SQL thô (bẫy `citext` — AC-01b chứng minh LINQ so không phân biệt hoa thường).
- **`ResetFailedLoginAsync` chỉ ghi khi có gì để reset** (`WHERE failed_login_count <> 0 OR locked_until IS NOT NULL`) — đa
  số lần đăng nhập không đụng tới `updated_at`.
- **Validator login không kiểm độ dài tối thiểu hay định dạng email**: email sai định dạng không khớp tài khoản nào nên đi
  đường 401 chung; luật mật khẩu đăng ký đổi thì tài khoản cũ vẫn đăng nhập được.
- **`Error.ToActionResult(ControllerBase)` thêm vào `ResultHttpExtensions`** đúng như bước 3 (impact: UNKNOWN, grep 3 chỗ
  gọi, chỉ thêm overload cho `Error`).
- **Thêm ngoài bảng:** AC-01 giải mã JWT kiểm `sub` = userId và `role` = `USER`, body không có `refreshToken`; 401/403
  không có `Set-Cookie`; AC-03 kiểm `locked_until` ≈ +15 phút; 3 case validation (400 + `errors` camelCase); unit test
  khóa hết hạn thì đăng nhập đúng thành công.
- `title` của 401/403/423 vẫn để `D9` (Mục 11).

---

## 6. D4 — Cookie refresh + CORS

**Mục tiêu.** Hiện thực quyết định 6 (refresh token trong cookie `HttpOnly`) và 7 (CORS có `AllowCredentials`)
của cổng mở. Thiếu một trong hai là `E7` không kiểm chứng được và lane FE đứng im ở màn sau đăng nhập.

**Kết quả mong đợi.**
- `Presentation/RefreshCookie.cs` — chỗ **duy nhất** tạo, đọc, xóa cookie. **Đã tạo ở `D3`** (login cần set cookie) —
  D4 còn lại `RefreshCookieTests` và CORS.
- `Program.cs`: `AddCors` với origin đọc từ `Cors:AllowedOrigins`; `UseCors` đặt **trước** `UseAuthentication()`.
  Thiếu origin ngoài Development → từ chối khởi động. `appsettings.Development.json` có `http://localhost:3000`.
- Test xanh: `RefreshCookieTests` (5 thuộc tính), `CorsTests` (preflight đúng/sai origin),
  `StartupConfigurationTests.Missing_cors_origins_must_fail_fast_outside_development`.
- Helper `StartupConfigurationTests.StagingWithEmailConfig` (thêm ở `D1`) đặt thêm `Cors:AllowedOrigins:0` — không thì
  `Staging_boots_when_email_config_is_complete` đỏ ngay khi fail-fast CORS vào.
- Kiểm tay trong PR: ảnh DevTools tab Application → Cookies khi FE `localhost:3000` gọi API dev.

### Các bước

**Bước 1 — `RefreshCookie`.**

```csharp
namespace SocialApp.Modules.Identity.Presentation;

/// <summary>
/// Cookie refresh token — cả 5 thuộc tính là HỢP ĐỒNG (identity-v1.yaml, SetRefreshCookie), không phải chi tiết.
/// Set và Clear dùng CÙNG Options: Path lệch một ký tự là trình duyệt giữ cookie cũ sau logout.
/// </summary>
public static class RefreshCookie
{
    public const string Name = "refresh_token";
    public const string Path = "/api/v1/auth";

    private static CookieOptions Options(TimeSpan maxAge) => new()
    {
        HttpOnly = true,
        Secure = true,                  // trình duyệt vẫn nhận trên http://localhost
        SameSite = SameSiteMode.Lax,
        Path = Path,
        MaxAge = maxAge,
        IsEssential = true,
    };

    public static void Set(HttpResponse response, string plainToken, int days) =>
        response.Cookies.Append(Name, plainToken, Options(TimeSpan.FromDays(days)));

    /// <summary>Max-Age=0 thay vì Cookies.Delete (ghi expires=1970) — khớp nguyên văn ClearRefreshCookie.</summary>
    public static void Clear(HttpResponse response) =>
        response.Cookies.Append(Name, "", Options(TimeSpan.Zero));

    public static string? Read(HttpRequest request) =>
        request.Cookies.TryGetValue(Name, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;
}
```

`Max-Age` của cookie đọc từ **cùng** `JwtOptions.RefreshTokenDays` với `expires_at` trong DB (`D3`, `D5`) — lệch
thì cookie còn mà DB đã hết hạn (401 khó hiểu) hoặc ngược lại.

**Bước 2 — CORS.**

```csharp
// Program.cs — cạnh RequireJwtOptions
var corsOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
if (corsOrigins.Length == 0 && !builder.Environment.IsDevelopment())
    throw new InvalidOperationException(
        $"Thiếu Cors:AllowedOrigins ở môi trường '{builder.Environment.EnvironmentName}'. "
      + "Đặt Cors__AllowedOrigins__0=https://<domain frontend> trong deploy/.env rồi deploy lại.");

builder.Services.AddCors(o => o.AddPolicy(FrontendCorsPolicy, p => p
    .WithOrigins(corsOrigins)          // TƯỜNG MINH — chuẩn CORS cấm AllowCredentials kèm AllowAnyOrigin
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()                // thiếu dòng này: trình duyệt IM LẶNG không gửi cookie → refresh luôn 401
    .WithExposedHeaders(CorrelationIdMiddleware.HeaderName)));
```

```csharp
// Pipeline — sau khối Swagger, TRƯỚC auth
app.UseCors(FrontendCorsPolicy);
app.UseAuthentication();
app.UseAuthorization();
app.UseSharedKernelRateLimiter();
```

### Test

| Test | Kỳ vọng |
|---|---|
| `RefreshCookieTests` — login thành công | `Set-Cookie` chứa (không phân biệt hoa thường) `httponly`, `secure`, `samesite=lax`, `path=/api/v1/auth`, `max-age=604800` |
| Logout (D6) / refresh hỏng (D5) | `Set-Cookie: refresh_token=;` … `max-age=0`, **cùng** `path` |
| `CorsTests` — `OPTIONS /api/v1/auth/refresh`, `Origin: http://localhost:3000`, `Access-Control-Request-Method: POST` | 204; `Access-Control-Allow-Origin: http://localhost:3000`; `Access-Control-Allow-Credentials: true` |
| Cùng request, `Origin: https://evil.example` | Không có header `Access-Control-Allow-Origin` |
| Preflight không mang token | **Không** 401 (bắt `UseCors` đặt sau `UseAuthorization`) |
| Thiếu origin ở Staging | App từ chối khởi động, thông báo nêu `Cors:AllowedOrigins` |

### Cạm bẫy đã biết

| # | Bẫy | Chặn bằng |
|---|---|---|
| 1 | `UseCors` sau `UseAuthorization` → preflight bị fallback policy trả 401 → trình duyệt báo "CORS error", không nói gì về 401 | Test "preflight không mang token" |
| 2 | `AllowAnyOrigin().AllowCredentials()` → ASP.NET ném lúc chạy request đầu | Chỉ có `WithOrigins` |
| 3 | `Cookies.Delete(name)` không kèm options → xóa cookie `Path=/`, cookie thật ở `/api/v1/auth` còn nguyên | `RefreshCookie.Clear` dùng chung `Options` |
| 4 | **FE dev ở `localhost:3000` gọi thẳng API staging** → khác site → `SameSite=Lax` chặn cookie trên `fetch` → refresh luôn 401 | Báo lane FE: dev dùng API local, hoặc Next.js `rewrites` proxy `/api` về cùng origin. Staging: FE và API **cùng site** |
| 5 | Test đọc cookie bằng cookie jar của `HttpClient` với `BaseAddress` `http://` → cookie `Secure` không bao giờ gửi lại | `AuthTestClient` tự quản cookie (D0 bước 6) |
| 6 | Staging `.env` thiếu `Cors__AllowedOrigins__0` → api crash-loop sau merge | Ghi vào checklist F1 cạnh `Jwt__SigningKey` |

---

## 7. D5 — `POST /auth/refresh`: rotation + reuse detection

**Mục tiêu.** NFR-SEC-03: mỗi lần refresh đổi token mới; token cũ bị dùng lại là dấu hiệu bị đánh cắp → cắt
cả chuỗi. Và **không** đăng xuất oan người dùng mở hai tab.

**Kết quả mong đợi.**
- `IRefreshTokenStore.RotateAsync(hash, now, newHash, newExpiresAt, ip)` trả một trong:
  `Rotated(userId, roleCode)` · `Grace(userId, roleCode)` · `ReuseDetected(userId)` · `Invalid`.
- `SessionService.RefreshAsync` phát access token + cookie mới cho `Rotated`/`Grace`; với `ReuseDetected` gọi
  revocation **sau** khi store đã commit (lời gọi này thêm ở D8 — lúc làm D5 chưa có store Redis); mọi nhánh hỏng → `IdentityErrors.SessionInvalid`.
- Action `Refresh`: **không tham số body**, `[AllowAnonymous]` (xác thực bằng cookie, không bằng bearer), 200 / 401;
  401 luôn kèm `RefreshCookie.Clear`.
- `Auth/RefreshTests` xanh: RT-01 → RT-05.

### Thuật toán — toàn bộ trong MỘT transaction

```
BEGIN
 1. row = SELECT * FROM refresh_tokens WHERE token_hash = @hash FOR UPDATE
 2. row không có                                        → COMMIT, Invalid
 3. row.replaced_by_id IS NOT NULL  hoặc  row.revoked_at IS NOT NULL:
      a. row.replaced_by_id IS NOT NULL
         AND row.revoked_at > @now - 10s
         AND EXISTS (token cùng family_id có revoked_at IS NULL)  → INSERT token mới cùng family   → COMMIT, Grace
      b. còn lại → UPDATE refresh_tokens SET revoked_at = @now
                   WHERE family_id = row.family_id AND revoked_at IS NULL                        → COMMIT, ReuseDetected
 4. row.expires_at <= @now                              → COMMIT, Invalid
 5. INSERT token mới (cùng family_id); UPDATE row SET revoked_at = @now, replaced_by_id = <id mới>  → COMMIT, Rotated
```

- Bước 3a là Đ-D3. Bước 3 kiểm **trước** bước 4: token hết hạn mà bị dùng lại vẫn là reuse.
- Bước 5 phải **INSERT trước rồi UPDATE**: `replaced_by_id` là FK tới dòng mới.
- Vai trò cho access token mới đọc từ DB (join `users → roles`) — **không** chép từ token cũ. Đây là thứ làm
  cho hạ quyền ở GĐ6 có hiệu lực sau một lần refresh (Mục 7.5, ví dụ 10:07:31).

### Các bước

**Bước 1 — khóa dòng bằng EF.**

```csharp
await using var tx = await db.Database.BeginTransactionAsync(ct);

var rows = await db.RefreshTokens
    .FromSql($"SELECT * FROM identity.refresh_tokens WHERE token_hash = {hash} FOR UPDATE")
    .ToListAsync(ct);                      // KHÔNG compose (Single/First) — giữ nguyên câu SQL có FOR UPDATE
var row = rows.SingleOrDefault();
```

Mặc định READ COMMITTED của Postgres là đủ: tab thứ hai chờ ở `FOR UPDATE` tới khi tab thứ nhất commit, rồi
**đọc lại** dòng đã được cập nhật (`replaced_by_id` có giá trị) → rơi vào 3a.

**Bước 2 — nhánh reuse phải COMMIT.** Store trả `ReuseDetected` sau khi `await tx.CommitAsync(ct)`. Quen tay
dùng exception để "trả 401" thì transaction rollback và **family không bị thu hồi** — endpoint vẫn trả 401 nên
test chỉ kiểm status code sẽ xanh. RT-02 vì thế phải kiểm bằng **hệ quả**: token kế nhiệm cũng chết.

**Bước 3 — service.**

```csharp
public async Task<Result<RefreshSuccess>> RefreshAsync(string? cookie, string? ip, CancellationToken ct)
{
    if (cookie is null) return IdentityErrors.SessionInvalid;

    var now = time.GetUtcNow();
    var next = SecureToken.Generate();
    var outcome = await store.RotateAsync(SecureToken.Hash(cookie), now, SecureToken.Hash(next),
        now.AddDays(jwt.RefreshTokenDays), ip, ct);

    switch (outcome)
    {
        case RotateOutcome.Rotated r: return new RefreshSuccess(tokens.Issue(r.UserId, r.RoleCode), next);
        case RotateOutcome.Grace g:   return new RefreshSuccess(tokens.Issue(g.UserId, g.RoleCode), next);
        case RotateOutcome.ReuseDetected x:
            // DB đã commit ở store — Redis SAU (Mục 7.5 cạm bẫy 1). Dòng dưới thêm ở D8.
            await revocation.RevokeUserAsync(x.UserId, now, ct);
            logger.LogWarning("Refresh token reuse detected, family revoked for user {UserId}", x.UserId);
            return IdentityErrors.SessionInvalid;
        default:
            return IdentityErrors.SessionInvalid;
    }
}
```

Refresh lấy token từ cookie nên action gọi `RefreshCookie.Read(Request)`; thành công → `RefreshCookie.Set`,
thất bại → `RefreshCookie.Clear` rồi `Error.ToActionResult(this)`.

### Test — `Auth/RefreshTests`

| Mã | Test | Kỳ vọng |
|---|---|---|
| RT-01 | Login → refresh bằng cookie | 200, `accessToken` mới gọi được `/me`, cookie mới **khác** cookie cũ; DB: dòng cũ có `revoked_at` + `replaced_by_id` = id dòng mới, **cùng** `family_id` |
| RT-01b | Refresh bằng cookie mới thêm lần nữa | 200 — chuỗi xoay tiếp được |
| RT-02 | Login (T1) → refresh (T2) → **đợi 11 giây giả**: `UPDATE … SET revoked_at = revoked_at - interval '11 seconds'` cho T1 → dùng lại T1 | 401 + cookie bị xóa; **rồi** refresh bằng T2 → **401**; DB: mọi dòng của family có `revoked_at` |
| RT-03 | Lùi `expires_at` của token về quá khứ → refresh | 401; **family không bị thu hồi** (token hết hạn không phải reuse) |
| RT-04 | Login → gửi **hai** refresh với cùng T1 bằng `Task.WhenAll` | **Cả hai 200**; hai cookie khác nhau; cả hai refresh tiếp được; family còn token sống |
| RT-05 | Như RT-04 nhưng request thứ hai gửi sau khi lùi `revoked_at` 11 giây | Request thứ hai 401, family bị thu hồi (ranh giới ân hạn) |
| — | Không cookie / cookie rác 64 hex / cookie `"abc"` | Cùng một 401, body giống hệt nhau (bỏ `traceId`/`instance`) |
| — | Gửi body `{}` kèm cookie hợp lệ | Không 400 vì body — action không bind body, request vẫn được xử lý |

RT-04 là test quan trọng nhất của khối. Chạy nó **20 lần liên tiếp** ở local trước khi push
(`dotnet test --filter "FullyQualifiedName~RT_04"` trong vòng lặp) — race condition xanh 1 lần không chứng minh gì.

### Cạm bẫy đã biết

| # | Bẫy | Hệ quả | Chặn bằng |
|---|---|---|---|
| 1 | Đọc token không `FOR UPDATE` | Hai tab cùng qua bước 5, cùng xoay → hai nhánh sống, không ai bị phát hiện; hoặc tab sau ghi đè `replaced_by_id` | RT-04 kiểm DB: đúng một dòng có `replaced_by_id` trỏ tới token của lượt xoay chính |
| 2 | Nối `.SingleOrDefaultAsync()` vào `FromSql` | EF bọc subquery — không chắc khóa được đúng như câu SQL gốc | `.ToListAsync()` |
| 3 | Rollback nhánh reuse | Family không bị thu hồi, test status code vẫn xanh | RT-02 kiểm T2 chết |
| 4 | "Chuỗi chưa bị thu hồi" suy từ `row.revoked_at IS NULL` | Token đã xoay luôn có `revoked_at` → ân hạn không bao giờ kích hoạt → RT-04 đỏ | `EXISTS … revoked_at IS NULL` theo family (Đ-D3) |
| 5 | Trả 401 khác nhau cho hết hạn / reuse / không tồn tại | Kẻ tấn công biết token nó cầm ở trạng thái nào | Test "cùng một 401" |
| 6 | Gọi Redis **trong** transaction, hoặc trước commit | Redis chậm giữ khóa dòng; Redis ghi mà DB rollback là thu hồi access oan | Store commit rồi mới trả outcome |
| 7 | Access token mới lấy `role` từ token cũ | GĐ6 hạ quyền không có hiệu lực, chỉ lộ ra ở GĐ6 | Review: `RotateAsync` join `roles` |

---

## 8. D6 — `POST /auth/logout`

**Mục tiêu.** "Đăng xuất thu hồi toàn bộ phiên" (báo cáo Mục 6.7.3) — của **thiết bị đó**, không phải của mọi
thiết bị (bảng Mục 7.5: "Đăng xuất 1 thiết bị → chỉ family đó").

**Kết quả mong đợi.**
- `IRefreshTokenStore.RevokeFamilyAsync(hash, ownerUserId, now)` — một câu `UPDATE` theo `family_id`, **có điều
  kiện chủ sở hữu**.
- Action `Logout`: `[Authorize]` (bearer), đọc cookie, 204 + `RefreshCookie.Clear`; không bearer → 401.
- `Auth/LogoutTests` xanh (bảng dưới).

### Các bước

```csharp
// Một câu, tầng 3 nằm ngay trong WHERE: token phải thuộc người gọi.
await db.Database.ExecuteSqlAsync($"""
    UPDATE identity.refresh_tokens t
       SET revoked_at = {now}
      FROM identity.refresh_tokens me
     WHERE me.token_hash = {hash}
       AND me.user_id    = {ownerUserId}
       AND t.family_id   = me.family_id
       AND t.revoked_at IS NULL
    """, ct);
```

```csharp
[Authorize]
[HttpPost("logout")]
[ProducesResponseType(StatusCodes.Status204NoContent)]
[ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
public async Task<IActionResult> Logout(CancellationToken ct)
{
    if (RefreshCookie.Read(Request) is { } cookie)
        await sessions.LogoutAsync(cookie, User.GetUserId(), ct);   // actor từ token, KHÔNG từ cookie (Mục 6.3)
    RefreshCookie.Clear(Response);
    return NoContent();
}
```

Access token đang cầm **vẫn sống tối đa 15 phút** — bản chất JWT, hợp đồng đã ghi. **Không** ghi `revoked:user`
ở logout: key đó cắt access token của **mọi** thiết bị của user, trái với "chỉ family đó".

### Test — `Auth/LogoutTests`

| Test | Kỳ vọng |
|---|---|
| Login → logout | 204; `Set-Cookie` xóa cookie cùng `path` |
| Sau logout, refresh bằng cookie cũ | 401 |
| Login → refresh (T2) → logout bằng T2 → refresh bằng **T1** | 401. T1 đã xoay và family không còn lá sống nên rơi vào nhánh **reuse** (3b), kể cả trong 10 giây — đúng: token cũ bị dùng lại sau khi đăng xuất là đáng nghi. Kiểm DB: mọi dòng của family có `revoked_at` |
| Login hai lần (hai thiết bị A, B) → logout A | Refresh của B vẫn 200 |
| Không bearer, có cookie | **401** — bắt `[AllowAnonymous]` lọt lên class |
| Bearer hợp lệ, không cookie | 204 (Đ-D6) |
| `Cookie_cua_nguoi_khac_khong_bi_thu_hoi`: bearer của A + cookie của B | 204; refresh bằng cookie của B vẫn **200** |

Thêm comment vào `AuthZMatrix.cs` (Đ-D6):

```csharp
// D6 logout chạm refresh token có chủ: kiểm tầng 3 ở Auth/LogoutTests.Cookie_cua_nguoi_khac_khong_bi_thu_hoi
// (quan sát hệ quả, khung matrix không mang cookie). Dòng dưới chỉ canh tầng 1.
new("TC-A01-logout", "Logout không kèm JWT", "GĐ1",
    Caller.Anonymous, HttpMethod.Post, "/api/v1/auth/logout", HttpStatusCode.Unauthorized),
```

### Cạm bẫy đã biết

- **Thu hồi theo `token_hash` không kèm `user_id`** → xem test cuối.
- **Thu hồi chỉ một dòng** thay vì cả family → T1 đã xoay thì vô hại, nhưng nhánh Grace (Đ-D3) để lại lá thứ
  hai còn sống → refresh bằng lá đó sau logout vẫn 200. Test thứ ba bắt được một phần; review bắt phần còn lại.
- **`Response.Cookies.Delete`** không kèm options → bẫy 3 của D4.

---

## 9. D7 — `GET /me`

**Mục tiêu.** Smoke test rẻ nhất chứng minh token phát ở D3 đi qua tầng 1 và chạm tới DB. Giữ endpoint này qua
mọi giai đoạn sau.

**Kết quả mong đợi.**
- `MeQuery.GetAsync(userId)` → `MeResponse(UserId, Email, Role, RoleDisplayName, EmailVerifiedAt, Status, CreatedAt)`,
  store join `users → roles` lấy **cả** `code` lẫn `display_name`.
- `Presentation/MeController`: `[Route("api/v1/me")]`, `[Authorize]`, `[ApiExplorerSettings]`, 200 / 401.
  Không `[EnableRateLimiting("auth")]` — `/me` dùng hạn mức chung 100/phút/user.
- Dòng matrix `TC-A01-me`; `Auth/MeTests` xanh.
- AC-01 của `LoginTests` (D3) đổi lời gọi `/api/v1/__khong-ton-tai` → 404 thành `GET /me` → 200 — D3 làm trước khi có
  `/me` nên tạm dùng route không tồn tại.

### Các bước

```csharp
[ApiController]
[Route("api/v1/me")]
[Authorize]
[ApiExplorerSettings(GroupName = IdentityApiGroup.Name)]
// KHÔNG [Produces("application/json")]: nhánh 401 "user đã bị xóa" đi qua Problem() sẽ mất application/problem+json
public sealed class MeController(MeQuery me) : ControllerBase
{
    [HttpGet]
    [ProducesResponseType<MeResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized, "application/problem+json")]
    public async Task<ActionResult<MeResponse>> Get(CancellationToken ct)
    {
        // Danh tính lấy từ token (C6), không có tham số route/query nào. User không còn trong DB → 401:
        // token hợp lệ về chữ ký nhưng phiên không còn chủ — hợp đồng chỉ có 200/401.
        var result = await me.GetAsync(User.GetUserId(), ct);
        return result.ToActionResult(this);
    }
}
```

- **Không `[RequirePermission]`.** Không mã nào trong 17 quyền nghĩa là "đọc chính mình", và bịa thêm quyền là
  sửa ma trận Mục 5.2. Tầng 2 đã được matrix chứng minh trên probe (`RBAC-*`); `/me` chứng minh tầng 1 + dữ liệu.
  Ghi rõ điều này trong docstring để GĐ sau không "sửa" bằng cách gắn một quyền ngẫu nhiên.
- `role` đọc từ **DB**, không từ claim — hợp đồng nói rõ hai giá trị có thể lệch 15 phút ở GĐ6.
- `MeQuery` trả `Error` 401 riêng (`IdentityErrors.SessionInvalid` dùng lại được) khi không thấy user.

### Test

| Test | Kỳ vọng |
|---|---|
| `TC-A01-me` (matrix): không token | 401 `problem+json` |
| Login → `/me` | 200; `role == "USER"`, `roleDisplayName == "Người dùng"` (viết tay theo Mục 5.1); `emailVerifiedAt` khác null; `status == "active"` |
| Đổi `roles.display_name` của USER bằng SQL → `/me` | `roleDisplayName` mới, `role` vẫn `"USER"` — chứng minh tách hai cột (quyết định 3) |
| Login → `DELETE FROM identity.users` user đó → `/me` với token cũ | 401 (không 500, không 404) |

```csharp
// AuthZMatrix.Cases
new("TC-A01-me", "GET /me không kèm JWT — endpoint thật đầu tiên của matrix", "GĐ1",
    Caller.Anonymous, HttpMethod.Get, "/api/v1/me", HttpStatusCode.Unauthorized),
```

### Cạm bẫy đã biết

- **`User.GetUserId()` ném khi thiếu `sub`** → 500. Chỉ xảy ra nếu action lọt `[AllowAnonymous]`; dòng matrix bắt.
- **Trả `Result<User>` (entity) thay vì DTO** → Swagger sinh schema `User` có `passwordHash` → PII ra ngoài, và
  cổng hợp đồng chiều 1 đỏ ở `required`. Luôn trả `MeResponse`.

### Thực tế thi công

**Bằng chứng.** `dotnet test SocialApp.sln`: Unit 53 (không đổi), Architecture 9, Integration 92 → 97 (+4 `MeTests`, +1
dòng matrix `TC-A01-me`). Cổng hợp đồng chiều 1 xanh với `GET /me` 200/401. AC-01 của `LoginTests` đã đổi từ route
không tồn tại (404) sang `GET /me` → 200 như đã hẹn ở D3.

Thời gian: bộ integration 25 s (D3: 23–26 s); `MeTests` + toàn bộ AuthZ matrix (15 test) chạy 2 s — matrix không chậm đi.
Một lần chạy lên 45 s là máy bận, chạy lại về 25 s.

Kiểm tay trên dev (API chạy từ `bin/` với `Development`): register 201 → verify 200 → login 200 → `GET /me` **200**
`{"userId":…,"role":"USER","roleDisplayName":"Người dùng","emailVerifiedAt":…,"status":"active","createdAt":…}` → không
token **401** `application/problem+json` → log stdout của API **không** chứa access token lẫn email.

`detect-changes` báo **high** (7 file, 29 symbol, 6 luồng) vì gộp cả D3 chưa commit: 5 luồng `Login → …` là code mới có
chủ đích của D3. Luồng thứ sáu `AddWithVerificationAsync → StampUpdatedAt` (đăng ký, D1) bị liệt kê do dịch dòng —
`git diff` của `IdentityUserStore.cs` chỉ có một dòng `using` và khối method mới chèn sau `GetRoleIdAsync`.

Thử cho đỏ ở local (bốn đột biến áp cùng lúc) rồi khôi phục:

| Đột biến | Test đỏ |
|---|---|
| `[AllowAnonymous]` trên `MeController` | Matrix `TC-A01-me` — nhận **500** (`GetUserId` ném vì principal không có `sub`) |
| `[EnableRateLimiting("auth")]` trên `MeController` | `Me_khong_dung_han_muc_auth_…` — request thứ 11 cùng IP nhận 429 |
| Store ghi cứng `roleDisplayName = "Người dùng"` | `Doi_display_name_bang_SQL_…` — vẫn thấy "Người dùng" |
| `MeQuery` bỏ kiểm `null` | `User_da_bi_xoa_token_cu_401_…` — nhận **204** (MVC đổi `Ok(null)` thành 204) |

**Chỗ lệch so với các bước trên — đã làm như sau:**

- **`FindMeAsync` thêm vào `IIdentityUserStore`** và trả thẳng `MeResponse`, không tạo store riêng; class giả trong
  `LoginServiceTests` thêm stub (impact: LOW — hai hiện thực).
- **Không `[Produces("application/json")]`** — đoạn code mẫu ở trên đã được sửa sau đợt kiểm giả thuyết (thi công D1).
- **Thêm ngoài bảng:** response có **đúng 7 key** của hợp đồng (chặn cột khác của `users` lọt ra); `/me` **không** dùng
  policy `auth` — tài liệu chỉ ghi trong phần kết quả mong đợi, không có test; test đổi `display_name` khôi phục giá trị
  trong `finally` vì các test trong lớp dùng chung database.
- "Không token → 401" không lặp lại trong `MeTests` — dòng matrix `TC-A01-me` lo, kèm kiểm `application/problem+json`.

---

## 10. D8 — `ITokenRevocationStore` + hook `OnTokenValidated`

**Mục tiêu.** Dựng **bên đọc** của thu hồi access token (Mục 7.5) để GĐ6 chỉ việc gọi `RevokeUserAsync`; và nối
**một** bên ghi ở GĐ1: reuse detection của D5. Không có nó thì hạ quyền hay khóa tài khoản trễ tới 15 phút.

Làm sau D5 và D6: bên đọc (hook, RV-01/02/04) không phụ thuộc gì, nhưng RV-03 kiểm nhánh reuse detection của D5.

**Kết quả mong đợi.**
- `SharedKernel/Authentication/ITokenRevocationStore.cs`, `RedisTokenRevocationStore.cs`,
  `TokenRevocationExtensions.AddSharedKernelTokenRevocation(redisConnectionString)`.
- `StackExchange.Redis` 2.8.16 chuyển từ Api sang SharedKernel (Đ-D8).
- `Program.cs` gắn `OnTokenValidated`. (`JwtOptions.ClockSkewSeconds = 30` và `ClockSkew` đọc hằng số đó **đã làm ở
  `D0`** — chỉ kiểm lại.)
- `Harness/RedisFixture.cs` (Testcontainers.Redis 4.0.0, `redis:7-alpine`).
- `Auth/TokenRevocationTests` xanh: RV-01 → RV-04 + `Ttl_bang_access_cong_clock_skew`.
- Nhánh `ReuseDetected` của `SessionService` (D5) gọi `RevokeUserAsync` **sau** khi store đã commit; RV-03 xanh.
- Đ-D4 đã ghi ngược vào `giai-doan-1.md` từ trước — code chỉ cần khớp.

### Các bước

**Bước 1 — interface và hiện thực.**

```csharp
namespace SocialApp.SharedKernel.Authentication;

/// <summary>
/// Thu hồi access token theo user + iat (Mục 7.5). Bên đọc: OnTokenValidated (tầng 1). Bên ghi: reuse detection
/// (GĐ1), đổi vai trò / khóa tài khoản (GĐ6), xóa tài khoản (GĐ8). Thứ tự ghi luôn là DB TRƯỚC, gọi hàm này SAU.
/// </summary>
public interface ITokenRevocationStore
{
    Task RevokeUserAsync(Guid userId, DateTimeOffset at, CancellationToken ct = default);

    /// <summary>true nếu token phát TRƯỚC mốc thu hồi (iat &lt; mốc, so CHẶT — Mục 7.5 cạm bẫy 3).</summary>
    Task<bool> IsRevokedAsync(string userId, long issuedAtUnix, CancellationToken ct = default);
}
```

```csharp
internal sealed class RedisTokenRevocationStore(
    IConnectionMultiplexer redis, IOptions<JwtOptions> jwt, ILogger<RedisTokenRevocationStore> logger)
    : ITokenRevocationStore
{
    private static string Key(string userId) => $"revoked:user:{userId}";

    // Đ-D4: key phải sống bằng thời gian token phát trước mốc còn được JwtBearer chấp nhận = TTL + ClockSkew.
    private TimeSpan Ttl => TimeSpan.FromSeconds(jwt.Value.AccessTokenSeconds + JwtOptions.ClockSkewSeconds);

    public Task RevokeUserAsync(Guid userId, DateTimeOffset at, CancellationToken ct = default) =>
        redis.GetDatabase().StringSetAsync(Key(userId.ToString()), at.ToUnixTimeSeconds(), Ttl);

    public async Task<bool> IsRevokedAsync(string userId, long issuedAtUnix, CancellationToken ct = default)
    {
        // Chưa kết nối → fail-open NGAY, không chờ timeout (Đ-D8). Quyết định có ý thức, Mục 7.5 "Khi Redis chết".
        if (!redis.IsConnected) { LogFailOpen(null); return false; }
        try
        {
            var value = await redis.GetDatabase().StringGetAsync(Key(userId));
            return value.HasValue && issuedAtUnix < (long)value;
        }
        catch (RedisException ex) { LogFailOpen(ex); return false; }
    }

    private void LogFailOpen(Exception? ex) =>
        logger.LogWarning(ex, "Redis không sẵn sàng — bỏ qua kiểm tra thu hồi token (fail-open, Mục 7.5)");
}
```

Đăng ký: `ConfigurationOptions.Parse(cs)` với `AbortOnConnectFail = false`, `ConnectTimeout = 2000`,
`SyncTimeout = AsyncTimeout = 250`; `IConnectionMultiplexer` singleton dựng **lười** (factory lambda) để app
khởi động được khi Redis chết.

> ⚠️ **Chưa kiểm (ghi lúc rà sau D1):** dựng lười bằng `ConnectionMultiplexer.Connect` đồng bộ thì lần resolve đầu
> tiên — tức request có token đầu tiên — có thể chờ tới `ConnectTimeout` (2 giây), trái RV-04 "< 1 giây". RV-04 đỏ ở
> request đầu thì kết nối bằng `ConnectAsync` ở nền và để `IsConnected = false` tới khi xong.

> ⚠️ `RevokeUserAsync` **không** nuốt lỗi Redis. Bên ghi thất bại phải lộ ra (log Error ở D5) — nuốt đi thì thu
> hồi mất âm thầm. Chỉ bên **đọc** fail-open.

**Bước 2 — hook ở tầng 1** (chỗ `// D8 gắn OnTokenValidated` đã có trong `Program.cs`).

```csharp
o.Events = new JwtBearerEvents
{
    // Chạy SAU khi chữ ký + exp đã hợp lệ: token giả không tốn lượt Redis.
    OnTokenValidated = async ctx =>
    {
        var sub = ctx.Principal!.FindFirstValue(JwtClaims.Sub);
        if (sub is null || !long.TryParse(ctx.Principal.FindFirstValue(JwtClaims.Iat), out var iat))
        {
            ctx.Fail("token thiếu sub/iat");
            return;
        }

        var store = ctx.HttpContext.RequestServices.GetRequiredService<ITokenRevocationStore>();
        if (await store.IsRevokedAsync(sub, iat, ctx.HttpContext.RequestAborted))
            ctx.Fail("token revoked");                     // → 401 RFC 7807 qua UseStatusCodePages
    },
};
```

`ClockSkew = TimeSpan.FromSeconds(JwtOptions.ClockSkewSeconds)` đã có từ `D0` — không sửa lại.

**Bước 3 — nối vào reuse detection.** Thêm dòng `revocation.RevokeUserAsync(x.UserId, now, ct)` vào nhánh
`ReuseDetected` của `SessionService` (D5, mục 7 bước 3 có sẵn vị trí) và inject `ITokenRevocationStore`. Bọc `try/catch` **ở service**: Redis lỗi → log Error + vẫn trả 401 (family
đã thu hồi ở DB — refresh đã bị chặn, chỉ access còn sống tối đa 15 phút).

### Test — `Auth/TokenRevocationTests` (Postgres + Redis thật)

Token cho RV-01/02/04 ký bằng `TestJwt.Create(..., issuedAt: …)` — cần `iat` tùy ý, nên `sub` là id ngẫu nhiên không
có trong DB. Vì vậy **không** gọi `/api/v1/me`: `/me` trả 401 cho user không tồn tại (D7) dù token không bị thu hồi, và
RV-02 "cả hai qua" sẽ không bao giờ xanh. Probe `__test/authz/authenticated` cũng không dùng được thẳng: nó chỉ được
nạp trong `AuthZApiFactory`, nơi Redis cố định không tới được. Gọi route **không tồn tại** `/api/v1/__khong-ton-tai`
bằng `IdentityApiFactory` + `UseRedis`: token còn hiệu lực → **404**, bị thu hồi → **401** (fallback policy) — cùng
lưới `AuthHarnessTests` đang dùng.

| Mã | Test | Kỳ vọng |
|---|---|---|
| RV-01 | `SET revoked:user:X = T`; token của X có `iat = T - 60` | 401 |
| RV-02 | Cùng key; token có `iat = T` (bằng mốc) và `iat = T + 5` | Cả hai **404** — qua tầng 1 (so **chặt**) |
| RV-03 | Login → refresh (T2) → lùi `revoked_at` T1 11 giây → dùng lại T1 | 401 ở refresh; **access token của lần login đầu** gọi `/me` → **401**; key `revoked:user:<id>` tồn tại. Login lại ngay sau đó → `/me` 200 |
| RV-04 | Factory trỏ Redis không tới được; token hợp lệ gọi `/api/v1/__khong-ton-tai` | 404 trong **< 1 giây**; log có warning fail-open (bắt bằng `ILoggerProvider` test) |
| TTL | Sau RV-03, `TTL revoked:user:<id>` | Trong khoảng `(AccessTokenSeconds + ClockSkewSeconds) - 5` … `+ 0`; kỳ vọng tính từ `IOptions<JwtOptions>` **và** hằng số `930` viết tay |
| Matrix | Chạy `Category=AuthZ` trước và sau D8 | Thời gian không tăng đáng kể (Redis cố ý không tới được ở `AuthZApiFactory`) |

### Cạm bẫy đã biết

| # | Bẫy | Chặn bằng |
|---|---|---|
| 1 | TTL = `AccessTokenSeconds` đúng như tài liệu gốc → cửa sổ 30 giây | Đ-D4 + test TTL |
| 2 | Hai chỗ đọc hai hằng số khác nhau cùng bằng 900 | **Không test nào bắt** (B.9 điều 2) — review: grep `900` và `AccessTokenSeconds` trong `src/` |
| 3 | Redis **trước** DB ở nhánh reuse | **Không test nào bắt** (B.9 điều 1) — review `SessionService` |
| 4 | `ConnectionMultiplexer.Connect` mặc định `abortConnect=true` → app không khởi động khi Redis chết | `AbortOnConnectFail = false`; quên thì smoke + cổng hợp đồng đỏ ngay trên CI (`ApiFactory` trỏ Redis không tới được) |
| 5 | Chờ timeout Redis ở mỗi request có token | Kiểm `IsConnected` trước; RV-04 đòi < 1 giây |
| 6 | `iat <= mốc` thay vì `<` | Login lại trong cùng giây thu hồi bị 401 oan; RV-02 bắt |
| 7 | Ghi `revoked:user` ở logout | Đăng xuất một thiết bị cắt access của mọi thiết bị — trái bảng Mục 7.5 |

---

## 11. D9 — RFC 7807 cho cả nhóm auth + `[ProducesResponseType]`

**Mục tiêu.** Một hình dạng lỗi duy nhất cho cả nhóm, **và** làm cho cổng hợp đồng chiều 2 xanh được — Swagger
chỉ thấy status code nào action khai ra.

**Kết quả mong đợi.**
- Mỗi action khai **đúng** tập mã của hợp đồng, trừ 429/500:

  | Operation | Mã phải khai |
  |---|---|
  | `POST /auth/register` | 201, 400, 409 |
  | `POST /auth/verify-email` | 200, 400, 410 |
  | `POST /auth/login` | 200, 400, 401, 403, 423 |
  | `POST /auth/refresh` | 200, 401 |
  | `POST /auth/logout` | 204, 401 |
  | `GET /me` | 200, 401 |

- Required field của request body khớp hợp đồng: register `[email, password]`, verify `[token]`, login
  `[email, password]`.
- `ApiBehaviorOptions.InvalidModelStateResponseFactory` (đặt ở SharedKernel, một lần) cho 400 validation title
  **"Dữ liệu không hợp lệ"** thay mặc định tiếng Anh — cùng title với `AppException.Validation`.
- **`title` cho mọi lỗi đi qua `ToActionResult`** (phát hiện lúc kiểm tay D2): `ControllerBase.Problem(statusCode, detail)`
  chỉ điền `title` cho mã có trong `ClientErrorMapping` — 409 ra `"Conflict"` tiếng Anh, **410 và 423 không có `title`**,
  trong khi `ProblemDetails` của hợp đồng `required: [title, status, traceId]`. Cổng hợp đồng không so schema response nên
  không đỏ. Sửa ở **một** chỗ — `ResultHttpExtensions` (chạy impact trước), title theo status như ví dụ trong hợp đồng
  ("Xung đột dữ liệu", "Liên kết không còn hiệu lực", "Tài khoản tạm khóa", …) — và thêm assert `title` vào test
  409 (`RegisterTests`), 410 (`VerifyEmailTests`), 401/403/423 (`LoginTests`).
- `Contract_must_be_fully_implemented` chạy **cục bộ, không Skip** → xanh.
- Bảng rà thông điệp trong PR: mỗi `Error` của `IdentityErrors` + mỗi đường 401/403 tự sinh — không chứa email,
  id, tên bảng, stack trace.

### Các bước

1. Rà từng action theo bảng trên. **Không khai thừa**: chiều 1 (đang xanh) đỏ ngay nếu action khai mã hợp đồng
   không có (vd `[ProducesResponseType(404)]` trên `/me`).
2. Kiểm required: mở `/swagger/identity-v1/swagger.json`, tìm `components.schemas.RegisterRequest.required`.
   Thiếu thì `[Required]` chưa có trên DTO. DTO của D1 là class nên `[Required]` đặt thẳng trên property. Nếu dùng
   record positional thì phải `[property: Required]` — không có `property:` thì attribute gắn vào **tham số constructor**
   và Swashbuckle không thấy (đã kiểm sau D1).
3. Chạy trọng tài:
   ```bash
   # Tạm xóa Skip (KHÔNG commit), chạy, đọc danh sách thiếu, khôi phục
   dotnet test tests/SocialApp.IntegrationTests --filter "FullyQualifiedName~Contract_must_be_fully_implemented"
   ```

> **Mẹo — chạy trọng tài từ D1, đừng đợi D9.** Sau mỗi controller, tạm gỡ `Skip` ở local và chạy: danh sách
> "thiếu" phải **ngắn dần đúng** những endpoint chưa làm. Operation vừa làm mà vẫn nằm trong danh sách là lỗi
> route (`api/v1/auth` gõ sai) hoặc thiếu `[ApiExplorerSettings]` — sửa lúc đó rẻ hơn nhiều so với cuối Ngày 5.

### Cạm bẫy đã biết

- **Khai `[ProducesResponseType(429)]` "cho đủ"** → không sai nhưng thừa: test đã trừ 429/500 khỏi hai vế.
- **Title validation lệch** giữa 400 do `[ApiController]` sinh và 400 do `AppException` sinh → FE nhận hai hình
  dạng. `RegisterTests` của D1 **chưa** so title — D9 thêm assert `title == "Dữ liệu không hợp lệ"` vào case dữ liệu sai.
- **Sửa `identity-v1.yaml` cho khớp code** khi trọng tài đỏ. Hợp đồng đã chốt ở cổng mở và FE đã sinh type từ
  nó — sửa code. Chỉ sửa yaml khi cả nhóm đồng ý đổi hợp đồng, và báo lane FE.

---

## 12. D10 — `[ApiExplorerSettings]` cho mọi controller

**Mục tiêu.** Endpoint không biến mất khỏi Swagger trong im lặng — mất khỏi Swagger là mất khỏi cổng hợp đồng
và khỏi type sinh ra cho FE.

**Kết quả mong đợi.** `AuthController` và `MeController` đều khai
`[ApiExplorerSettings(GroupName = IdentityApiGroup.Name)]` ở mức class;
`PresentationBoundaryTests.Every_controller_must_declare_a_swagger_group` xanh ở **mọi** commit của khối.

**Cách làm.** Không phải một bước riêng — là dòng đầu tiên gõ khi tạo file controller. Lưới đã có sẵn và CI
chạy nó ở bước `Test (unit + integration)`.

**Cạm bẫy.** Đặt attribute ở **action** thay vì class: test reflection chỉ đọc attribute của class → đỏ dù
Swagger vẫn hiện. Đặt ở class.

---

## 13. D11 — Gỡ `Skip` của `Contract_must_be_fully_implemented`

**Mục tiêu.** Bật chiều thứ hai của cổng hợp đồng. Từ đây hợp đồng được canh **hai chiều**: code không lộ ra
ngoài hợp đồng, và hợp đồng không có phần nào chưa hiện thực. **Điều kiện vào cổng đóng.**

**Kết quả mong đợi.**
- `[Fact(Skip = …)]` → `[Fact]` trong `tests/SocialApp.IntegrationTests/IdentityContractTests.cs`; docstring đổi
  từ "Đang Skip vì…" thành trạng thái thật (đã gỡ ở D11, cổng hai chiều).
- Comment `ci.yml` ở bước `API contract (CI GATE)` không phải sửa — nó đã mô tả đúng.
- CI run xanh, log bước `API contract (CI GATE)` ghi **`Total tests: 2`**, **`Passed: 2`**. Link run trong PR.

**Cách làm.** Commit riêng, **sau** D9 xanh cục bộ. Trước khi push, thử cho đỏ một lần: xóa tạm
`[ProducesResponseType(423)]` của login → test đỏ nêu đúng `POST /auth/login: 423` → khôi phục. Cổng chưa từng
đỏ thì không chứng minh được gì (cùng bài học với `B3`, `B4`).

---

## 14. Kế hoạch commit

| # | Nội dung | CI sau khi push | Thông điệp gợi ý |
|---|---|---|---|
| 1 | `D0` | 🟢 | `feat(gd1-d): nen chung — SecureToken, BCrypt, phat JWT, harness test auth` |
| 2 | `D1` + `D10` (AuthController) | 🟢 | `feat(gd1-d): POST /auth/register + mail xac minh` |
| 3 | `D2` | 🟢 | `feat(gd1-d): POST /auth/verify-email — 400/410, tieu thu nguyen tu` |
| 4 | `D3` | 🟢 | `feat(gd1-d): POST /auth/login + lockout nguyen tu, AC-01..04` |
| 5 | `D7` + dòng matrix `TC-A01-me` | 🟢 | `feat(gd1-d): GET /me` |
| 6 | `D4` | 🟢 | `feat(gd1-d): cookie refresh + CORS AllowCredentials` |
| 7 | `D5` | 🟢 | `feat(gd1-d): POST /auth/refresh — rotation, reuse detection, an han 10s` |
| 8 | `D6` + dòng matrix `TC-A01-logout` | 🟢 | `feat(gd1-d): POST /auth/logout — thu hoi ca family, kiem chu so huu` |
| 9 | `D8` + chuyển `StackExchange.Redis` (`ClockSkewSeconds` đã ở `D0`) | 🟢 | `feat(gd1-d): ITokenRevocationStore + OnTokenValidated, noi vao reuse detection` |
| 10 | `D9` | 🟢 | `feat(gd1-d): ProducesResponseType khop hop dong + title validation` |
| 11 | `D11` | 🟢 — `Contract` 2/2 | `test(gd1-d): go Skip Contract_must_be_fully_implemented — cong hop dong hai chieu` |

- Mọi commit **xanh** — khối D không có commit đỏ có chủ đích; bằng chứng "đỏ được" đã nằm ở khối B/C, còn ở D
  là bước thử cho đỏ ở local của `D11` và của RT-02 (mục 7, bẫy 3).
- Trước **mỗi** commit: `node .gitnexus/run.cjs detect-changes --scope all --repo .`.
- Đ-D3, Đ-D4, Đ-D9 **đã ghi ngược** vào `giai-doan-1.md`, `identity-v1.yaml`, compose staging và `oci-setup.md`
  trước khi khối bắt đầu — commit của D không phải sửa lại. Thi công lệch khỏi tài liệu này thì ghi vào mục
  "Thực tế thi công" (theo khuôn hướng dẫn B+C, Mục 13.1), sửa tài liệu gốc cùng commit.
- `ci.yml` có `cancel-in-progress: true`: push hai commit sát nhau thì run trước bị hủy — không sao vì không cần
  giữ run đỏ nào, nhưng **run cuối** của nhánh phải xanh cả ba bước.

---

## 15. Checklist nghiệm thu khối D

**Kiểm tự động**

- [ ] `dotnet build SocialApp.sln` xanh
- [ ] Unit: `SecureTokenTests`, `BCryptPasswordHasherTests`, `JwtAccessTokenIssuerTests`, `LoginServiceTests`, `LockoutPolicyTests`
- [ ] Integration: `RegisterTests`, `VerifyEmailTests`, `LoginTests` (AC-01→04), `RefreshTests` (RT-01→05),
      `LogoutTests`, `MeTests`, `TokenRevocationTests` (RV-01→04 + TTL), `CorsTests`, `RefreshCookieTests`
- [ ] `StartupConfigurationTests`: thiếu `Cors:AllowedOrigins` / `Smtp:Host` ngoài Development → từ chối khởi động
- [ ] AuthZ matrix xanh, thêm `TC-A01-me`, `TC-A01-logout`; thời gian chạy không tăng đáng kể
- [ ] Cổng hợp đồng `Category=Contract`: **2** test, cả hai xanh (D11)
- [ ] `ArchitectureTests` xanh — không EF trong `Application`, không MVC ngoài `Presentation`
- [ ] RT-04 chạy 20 lần liên tiếp ở local, 20/20 xanh (ghi trong PR)

**Kiểm tay — ghi bằng chứng vào PR**

- [ ] Dev: đăng ký → mail hiện trong Mailpit `http://localhost:8025` (ảnh chụp)
- [ ] psql: một dòng `users.password_hash` có cost 12; `refresh_tokens.token_hash` và
      `email_verification_tokens.token_hash` là 64 hex, không khớp giá trị cookie/link (Mục 12)
- [ ] DevTools từ `localhost:3000`: cookie `refresh_token` có `HttpOnly`, `Secure`, `SameSite=Lax`,
      `Path=/api/v1/auth`; preflight không lỗi (ảnh chụp) — cần lane FE có ít nhất màn đăng nhập

**Code review — không test tự động nào bắt được**

- [ ] Nhánh reuse: **DB commit trước**, `RevokeUserAsync` sau (B.9 điều 1)
- [ ] TTL `revoked:user` và `exp` của access token cùng đọc `JwtOptions` — grep `900`, `AccessTokenSeconds`,
      `ClockSkew` trong `src/` chỉ ra `JwtOptions`, `Program.cs`, `JwtAccessTokenIssuer`, `RedisTokenRevocationStore` (B.9 điều 2)
- [ ] `[AllowAnonymous]` chỉ ở 4 action công khai, không ở class
- [ ] Không log nào chứa mật khẩu, token bản rõ, link xác minh, giá trị cookie
- [ ] Grep `SystemRoles.Admin|RoleCodes.Admin|"ADMIN"` trong `src/` vẫn chỉ ra 4 file cho phép (khối D không thêm)
- [ ] Code khớp ba quyết định đã ghi ngược: ân hạn phát token mới cùng family (Đ-D3), TTL = access + `ClockSkew`
      (Đ-D4), gửi mail đọc `Smtp__*` không ghi cứng host (Đ-D9)

**Việc chuyển cho F1 trước khi merge `develop`** — thiếu là api crash-loop trên staging

- [ ] `.env` staging có `Cors__AllowedOrigins__0`, `Smtp__Host=mailpit`, `Smtp__Port=1025`, `Smtp__From`,
      `Frontend__BaseUrl` (mẫu: `deploy/.env.example`)
- [ ] Service `mailpit` lên cùng compose staging; UI xem được qua SSH tunnel `127.0.0.1:8025` và **không** mở
      từ Internet (thử `curl https://<domain>:8025` phải thất bại)

---

## 16. Khối D để lại gì

| Ai nhận | Nhận cái gì |
|---|---|
| **E3–E7** (lane FE) | Sáu endpoint chạy thật đúng hợp đồng; cookie + CORS đã kiểm; bỏ mock được ở dev |
| **F1–F7** (cổng đóng) | Cổng hợp đồng hai chiều; danh sách biến `.env` staging; RT-04 làm nền cho E2E-02 |
| **GĐ2–GĐ8** | `User.GetUserId()` có token thật để thử; `/me` làm smoke test; khuôn controller mỏng → service `Result` → store SQL nguyên tử |
| **GĐ6** | `ITokenRevocationStore.RevokeUserAsync` — chỉ việc gọi **sau** `UPDATE users` khi đổi vai trò/khóa tài khoản; refresh đã đọc vai trò mới từ DB |
| **GĐ8** | Cùng hàm đó cho xóa tài khoản; `ON DELETE CASCADE` của `refresh_tokens` lo phần refresh |

