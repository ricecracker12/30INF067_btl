# Hướng dẫn thực hiện — Khối E. Lane frontend (GĐ1)

> Bản triển khai chi tiết của **B.7 Khối E** trong [giai-doan-1.md](giai-doan-1.md). Tài liệu gốc trả lời *cái gì* và
> *vì sao*; tài liệu này trả lời *tạo file nào, theo thứ tự nào, và nhìn vào đâu để biết đã xong thật*.
>
> **Nguồn sự thật vẫn là** hợp đồng [`identity-v1.yaml`](../../src/backend/Modules/Identity/Presentation/identity-v1.yaml),
> `giai-doan-1.md` (Mục 7 luồng nghiệp vụ, Mục 8 cookie + CORS, Mục 10.1 E2E-01/02, Mục 12 checklist) và `AGENTS.md`.
> Chỗ nào tài liệu này lệch ba nguồn đó thì sửa ở đây — trừ các quyết định ở Mục 1 đánh dấu **"cần ghi ngược"**: tài
> liệu gốc đang thiếu hoặc mâu thuẫn ở những chỗ đó, phải sửa `giai-doan-1.md` trong cùng commit với việc tương ứng.

> **Trạng thái: E1–E7 xong + đổi sang BFF (Đ-E14), E8 là việc tiếp theo (cập nhật 2026-09-17)** — Đ-E14: trình duyệt
> không còn cầm JWT; Next server giữ token trong Redis (mã hóa), trình duyệt chỉ có cookie `__Host-sid` HttpOnly.
> **Vitest 214/214**, **Playwright** 8 xanh + 1 bỏ qua trên API dev, `single-flight.spec.ts` xanh trên API token 10 giây
> (API thật nhận đúng 1 refresh cho 9 request đồng thời); thử cho đỏ 25 đột biến Vitest + 2 đột biến E2E; chi tiết ở Mục
> 8A. E7: interceptor 401→refresh single-flight hai
> lớp, **Vitest 177/177**, **Playwright** `single-flight.spec.ts` xanh trên API `Jwt__AccessTokenSeconds=10` (bộ thường 7/7
> + 1 bỏ qua), thử cho đỏ 16 đột biến + 1 đột biến E2E; Đ-E4 đã ghi ngược vào `giai-doan-1.md`; chỗ lệch ở "Thực tế thi
> công" của Mục 8. E6: guard phía client
> của `(app)`, `/me`, đăng xuất trên API dev thật, **Vitest 163/163**, **Playwright 7/7** (thêm `guard.spec.ts`), thử cho đỏ
> 16 đột biến + 2 đột biến E2E; coordinator **lớp 1** đã có (E7 thêm lớp giữa tab + interceptor); chỗ lệch ở "Thực tế thi
> công" của Mục 7. E5: màn `/verify-email` chạy
> trên API dev thật dưới StrictMode (đúng 1 POST), **Vitest 136/136**, **Playwright 5/5** (thêm `verify-email.spec.ts`), thử
> cho đỏ 11 đột biến + 1 đột biến E2E; chỗ lệch ở "Thực tế thi công" của Mục 6. E3: màn `/register` và
> `/register/check-email` chạy trên API dev thật, **Vitest 109/109**, **Playwright 4/4** (thêm `register.spec.ts`), thử cho
> đỏ 14 đột biến (1 tương đương); chỗ lệch ở "Thực tế thi công" của Mục 5. E4: màn `/login` chạy trên API dev
> thật, **Vitest 67/67**, **Playwright 3/3** (có `login-storage.spec.ts`), thử cho đỏ 10 đột biến + 1 đột biến E2E; chỗ lệch ở
> "Thực tế thi công" của Mục 4. Phần dưới là trạng thái lúc xong E1 + E2 — `src/frontend/` dựng bằng preset
> **`b50KEhMiu`** (thay `b2C6hQKDg`, lý do ở Đ-E9). `pnpm lint`, `typecheck`, `test`, `build` xanh; **Vitest 28/28**;
> **Playwright 2/2** trên Chrome đã cài, cả lượt bật mock lẫn lượt không mock; `lib/api/schema.d.ts` sinh từ hợp đồng;
> job `frontend` trong `ci.yml` có **hai cổng** (codegen, bundle sạch MSW). Đã thử cho đỏ: sáu luật ESLint, hai cổng CI,
> và sáu đột biến trên api client / token store / config. **Node đổi 22 → 24** (đổi Đ-E9 ngày 2026-09-17).
> Chỗ lệch và chỗ còn treo: "Thực tế thi công" của Mục 2 và Mục 3.
> **Khối D đã xong** (`37b6b76` → `14e843f`): sáu endpoint
> chạy thật trên dev, cookie + CORS đã kiểm bằng test. Hệ quả: E3–E7 không phải chờ ai — kiểm được trên **API dev thật**
> ngay khi xong trên mock, và `E7` (vốn cần `D4`) làm được luôn.

| | |
|---|---|
| **Người làm** | Frontend (1 người); một người backend review chéo PR (bus factor — `ke-hoach-trien-khai.md` Mục 0C) |
| **Khối này chặn** | `F2` (bỏ mock, trỏ staging), `F3` (E2E-01), `F4` (E2E-02) |
| **Khối này cần trước** | Hợp đồng `identity-v1.yaml` (đã có từ cổng mở); `E7` cần `D4` — **đã xong** |

---

## 0. Danh sách công việc — mục tiêu và kết quả mong đợi

| Mã | Đầu việc | Mục tiêu — việc này tồn tại để làm gì | Kết quả mong đợi — thứ kiểm chứng được |
|---|---|---|---|
| **E1** | Scaffold Next.js 16 + shadcn/ui preset `b50KEhMiu` | Có nền để dựng màn, và bộ primitive dùng lại cho GĐ2–GĐ8 lấy từ **một** kit — dựng màn trước thì mỗi màn một kiểu | `src/frontend/` sinh bằng `shadcn init --preset b50KEhMiu --template next`, không repo lồng; `pnpm dev`/`lint`/`typecheck`/`build` xanh; kit `button input label field alert card skeleton spinner sonner`; composite `TextField`; font có subset `vietnamese`; ESLint chặn màu thô, import Base UI trực tiếp, icon ngoài lucide (đã thử cho đỏ); layout `(auth)` dùng kit |
| **E2** | Sinh type từ hợp đồng + api client + mock MSW | Biến "đổi hợp đồng mà quên sửa FE" thành **lỗi compile** trên máy và **đỏ CI**, thay vì lỗi runtime ở staging | `pnpm gen:api` sinh `lib/api/schema.d.ts` (commit vào repo); type test `RoleCode` = `'USER' \| 'MODERATOR' \| 'ADMIN'`; `fetch` chỉ xuất hiện trong `http.ts` (ESLint chặn); mọi lời gọi `credentials: 'include'` (unit test); `ApiError` đọc được Problem Details kể cả khi body không phải JSON; job CI `frontend` có cổng **codegen lệch → đỏ** (đã thử cho đỏ một lần) |
| **E3** | Màn đăng ký | FR-001 phía người dùng; validation client **không chặt hơn** server, riêng mật khẩu **khớp đúng** | `/register` trên API dev (nhánh lỗi dựng bằng Vitest + `msw/node`): 201 → `/register/check-email`; 400 hiện lỗi **theo trường** từ `errors`; 409 có thông điệp + link đăng nhập; 429/500/mất mạng có giao diện. Unit test bảng ngưỡng mật khẩu: 7 ký tự đỏ, 8 xanh, 72 byte xanh, 73 byte đỏ, chuỗi tiếng Việt 40 ký tự > 72 byte đỏ |
| **E4** | Màn đăng nhập | Dịch mã lỗi thành thông điệp người đọc hiểu **mà không lộ email có tồn tại hay không**; token chỉ ở memory | `/login` trên mock: 401/403/423/429 mỗi mã một thông điệp (401 **một** thông điệp duy nhất); 200 → về `next` (đã lọc open redirect) hoặc `/me`; ESLint cấm `localStorage`/`sessionStorage`; Playwright trên API dev: sau đăng nhập `localStorage` + `sessionStorage` rỗng, `document.cookie` không có `refresh_token` |
| **E5** | Màn xác minh email | Khép vòng đăng ký từ link trong mail | `/verify-email?token=…` có 3 trạng thái thành công / 400 / 410 (+ đang xử lý); token sai dạng → 400 **không gọi API**; dưới React StrictMode chỉ **một** `POST /auth/verify-email` (unit test đếm request); trên dev: bấm link trong Mailpit → 200 |
| **E6** | App shell + route guard + trang `/me` | Có khu vực cần đăng nhập để E7 có chỗ chứng minh tác dụng | Guard **phía client** (không `proxy.ts`); vào `/me` khi chưa đăng nhập → về `/login?next=%2Fme`, **không nháy nội dung**; tải lại trang khi còn phiên → khôi phục bằng một lần refresh; `/me` hiện `roleDisplayName`, không hiện `role`; nút Đăng xuất → 204 → về `/login`. Playwright xanh cho cả ba |
| **E7** | Interceptor 401→refresh **single-flight** | Giữ phiên mượt, và **không để chính FE kích hoạt reuse detection** của server | Unit test: 3 request 401 đồng thời → **1** `POST /auth/refresh`, cả 3 được gọi lại với token mới; 401 từ chính `/auth/refresh` → xóa token, về `/login`, **không thử lại**; `/auth/login` 401 **không** kích hoạt refresh; điều phối giữa tab (Web Locks + BroadcastChannel) có unit test. Playwright trên API dev (`Jwt__AccessTokenSeconds=10`): 3 tab, token hết hạn, bấm đồng thời → **đúng 1** request `/auth/refresh`, không tab nào về `/login` |
| **E8** | Đóng gói frontend cho staging *(bổ sung)* | `F2`/`F3` đòi FE chạy **cùng domain** `mxh.banhgao.net` — link xác minh trỏ về đó, và cookie `SameSite=Lax` không đi nếu FE khác site. Hiện CD chỉ build image `api` | `output: 'standalone'`; `src/frontend/Dockerfile` build được `linux/arm64`; `docker run` image mở `/login` được, gọi API qua đường dẫn tương đối `/api/v1`; không route nào của FE nằm dưới `/api`, `/health`, `/swagger`. Việc sửa compose/apache/CD **chuyển cho F1** (Mục 9) |

> `E8` không có trong B.7 của tài liệu gốc — tách ra vì không có nó thì `F2` không thực hiện được, và người biết Next.js
> build ra sao là người lane FE. **Cần ghi ngược** vào B.7 và B.8/F1 (Mục 1, Đ-E11).

### Thứ tự thực thi

```
E1 ─→ E2 ─→ E4 ─→ E3 ─→ E5 ─→ E6 ─→ E7 ─→ E8
            └──── E3, E5 đổi chỗ được cho nhau ────┘
```

- **`E2` ngay sau `E1`** — mọi màn gọi API qua client của E2; viết màn trước là mỗi màn một kiểu `fetch`.
- **`E4` trước `E3`** — E6 cần E4, và đăng nhập là màn nhiều nhánh lỗi nhất; làm trước thì bộ xử lý lỗi dùng chung
  (`errorMessage`, hiển thị lỗi theo trường) thành hình sớm, E3/E5 chỉ việc dùng.
- **`E7` sau `E6`** — interceptor cần một trang có bảo vệ để chứng minh; nhưng **thiết kế** token store của E7 (Đ-E2,
  Đ-E4) phải có từ E2, nếu không E6 phải viết lại.
- **`E8` cuối** nhưng **chậm nhất trước F1** — F1 cần image để thêm vào compose.

**Ước lượng** (một người, đã quen Next.js): E1 ½ ngày · E2 ½–1 ngày · E3+E4+E5 1 ngày · E6 ½ ngày · E7 1 ngày · E8 ½ ngày.
E7 là chỗ dễ trượt nhất — đừng cắt thời gian của nó.

---

## 1. Trước khi gõ dòng đầu tiên

### Điều kiện cần

```bash
node -v          # 22.x LTS (Đ-E9). Máy đang có 24.x thì cài 22 bằng nvm-windows/fnm — Next 16 đòi ≥ 20.9; CI dùng 22
docker compose -f deploy/docker-compose.dev.yml up -d postgres redis mailpit
dotnet run --project src/backend/SocialApp.Api          # API dev: http://localhost:5259 — link mail trỏ http://localhost:3000
```

- `deploy/.env` ở máy dev có `POSTGRES_PASSWORD` và `Jwt__SigningKey` (bắt buộc từ khối A/C) — thiếu thì API từ chối chạy.
- Mailpit: `http://localhost:8025`. Chạy API trong compose thay vì `dotnet run` thì cổng là **8080**, không phải 5259.
- **Rate limit nhóm `/auth/*` là 10 req/phút theo IP** — kể cả `/auth/refresh`. Thử tay trên API thật sẽ gặp 429 rất nhanh
  (đăng ký + xác minh + đăng nhập + một lần tải lại trang đã là 4). Đó là server làm đúng việc, không phải lỗi FE.

### Luật của repo áp thẳng vào khối này

1. **Hợp đồng là `identity-v1.yaml`, không phải Swagger và không phải trí nhớ.** FE cần API khác hợp đồng → báo cả nhóm,
   sửa yaml + code backend **cùng commit** (cổng `Category=Contract` canh). FE không tự "đoán" field.
2. **Không commit secret.** FE GĐ1 không có secret nào; `NEXT_PUBLIC_*` bị nhúng vào bundle nên **không bao giờ** chứa khóa.
   `.gitignore` đã chặn `.env.*` trừ `.env.example`.
3. **Docs sống cùng code** — lệch thì sửa cùng commit; cập nhật `README.md` mục trạng thái khi khối xong (`AGENTS.md` 14.7).
4. **Trước mỗi commit:** `node .gitnexus/run.cjs detect-changes --scope all --repo .` (`CLAUDE.md`). Sửa file dùng chung
   ngoài `src/frontend/` (`ci.yml`, `giai-doan-1.md`) thì đọc kỹ phần báo cáo.
5. **Không nghiệm thu trên mock** (Mục 12). Dev chạy đủ FE + BE (không còn mock trình duyệt — đổi Đ-E7); mock chỉ dựng nhánh
   lỗi khó tái hiện trong Vitest. "Xong" của E3–E7 luôn có một lượt trên API dev thật, và cổng đóng F2 là trên staging.

### Mười bốn quyết định đã chốt

Tài liệu gốc chưa nói đủ để gõ code ở những chỗ dưới đây.

**Đ-E1 — Dev gọi thẳng API local qua CORS; staging gọi cùng origin.**

> **Thay bởi Đ-E14 (2026-09-17, nhóm chốt).** Trình duyệt không còn gọi API: chỉ gọi `/bff/*` cùng origin với trang.
> `NEXT_PUBLIC_API_BASE_URL` bị bỏ; gốc API là cấu hình server `API_INTERNAL_URL`. Phần dưới giữ để truy nguồn.

| Môi trường | FE | `NEXT_PUBLIC_API_BASE_URL` | Cookie `refresh_token` đi được vì |
|---|---|---|---|
| Dev | `http://localhost:3000` | `http://localhost:5259/api/v1` (mặc định trong code khi `NODE_ENV=development`) | Cùng **site** `localhost` (SameSite bỏ qua cổng); CORS có `AllowCredentials` cho `http://localhost:3000` |
| Staging | `https://mxh.banhgao.net` (E8 + F1) | `/api/v1` (tương đối, nhúng lúc build) | Cùng origin — apache định tuyến `/api` về API, `/` về FE |

- **Không dùng `rewrites` proxy ở dev.** Proxy làm mọi thứ cùng origin nên CORS + `credentials: 'include'` — quyết định 7 của
  hợp đồng — không bao giờ được thử trên trình duyệt trước cổng đóng; và checklist khối D (Mục 15) chờ bằng chứng cookie +
  preflight từ `localhost:3000` (E4 đóng bằng `login-storage.spec.ts`).
- **Cấm FE local trỏ API staging.** `localhost` → `mxh.banhgao.net` là **khác site** → `SameSite=Lax` chặn cookie trên `fetch`
  → refresh luôn 401, không lỗi nào nói lý do (hướng dẫn D, D4 bẫy 4).
- Build production **thiếu** biến → `next build` ném lỗi nêu tên biến (cùng tinh thần "thiếu cấu hình = từ chối chạy").

**Đ-E2 — Access token nằm trong một module store, không trong Web Storage; chỉ `http.ts` được gọi `fetch`.**

> **Đổi bởi Đ-E14 (2026-09-17, nhóm chốt).** Không còn token nào ở trình duyệt, kể cả trong memory: `token-store.ts` chỉ
> giữ trạng thái phiên. Giữ nguyên: cấm Web Storage / `document.cookie`, `fetch` phía trình duyệt chỉ ở
> `lib/api/http.ts`; phía server chỉ ở `lib/bff/upstream.ts`.

- `lib/auth/token-store.ts`: biến module + `subscribe` (React đọc qua `useSyncExternalStore`). Không để token **chỉ** trong
  React state: api client không phải component, và interceptor phải đọc token hiện tại tại thời điểm gọi lại.
- ESLint `no-restricted-globals` / `no-restricted-properties` cấm `localStorage`, `sessionStorage`, `document.cookie`, và cấm
  `fetch` ngoài `lib/api/http.ts`. Cấm bằng máy chứ không bằng trí nhớ — Mục 12 có dòng riêng cho `localStorage`.
- `react/no-danger` bật: token trong memory vẫn đọc được bằng XSS; không `dangerouslySetInnerHTML` là lớp chặn rẻ nhất.

**Đ-E3 — Route guard ở phía client, không dùng `proxy.ts` (tên mới của `middleware.ts` từ Next 16).**

> **Đổi bởi Đ-E14 (2026-09-17).** Guard vẫn ở client, nhưng khởi động phiên bằng `GET /bff/auth/session` thay cho
> `POST /auth/refresh` — tải lại trang không còn tốn lượt `/auth/*` của API. Server giờ THẤY cookie phiên, nhưng guard
> vẫn không đặt ở `proxy.ts`: kiểm phiên ở đó là thêm một lượt Redis cho mọi request trang tĩnh, và API vẫn tự chặn
> dữ liệu (tầng 1–3).

- `proxy.ts` chạy trên server, **không thấy** access token (nằm trong memory của tab) và **không thấy** cookie refresh (`Path=
  /api/v1/auth`; ở dev còn nằm trên origin khác). Guard ở `proxy.ts` chỉ có thể đoán — hoặc chặn nhầm, hoặc cho qua hết.
- Guard là component `RequireAuth` trong layout `(app)`: trạng thái `unknown` → gọi refresh (qua **cùng** hàm single-flight của
  E7) → `authenticated` hoặc `anonymous` → `router.replace('/login?next=…')`. Lúc `unknown` chỉ hiện khung chờ.
- Hệ quả chấp nhận: mỗi lần **tải lại trang** tốn một `POST /auth/refresh` (tính vào hạn mức 10/phút). Điều hướng trong app
  không tải lại trang nên không tốn.

**Đ-E4 — Single-flight hai lớp: trong tab (promise chia sẻ) và giữa các tab (Web Locks + BroadcastChannel).** *(cần ghi ngược: B.7/E7, Mục 10.1 E2E-02)*

> **Thay bởi Đ-E14 (2026-09-17, nhóm chốt).** Refresh chuyển về BFF ở server: một khóa Redis theo phiên gộp mọi 401
> đồng thời — trong tab, giữa các tab, giữa các instance Next — thành MỘT lần refresh. Web Locks và coordinator phía
> trình duyệt đã gỡ; BroadcastChannel chỉ còn báo đăng xuất sang tab khác. Phần dưới giữ để truy nguồn.

- Tài liệu gốc mô tả hai kiểm chứng khác nhau: Mục 10.1 "**3 request** nhận 401 cùng lúc → refresh một lần", còn F4 và Mục 12
  "**mở 3 tab** … quan sát chỉ một lời gọi". Promise chia sẻ chỉ gộp được trong **một** tab — ba tab là ba vùng nhớ, mỗi tab một
  lời gọi refresh cùng cookie. Server không đăng xuất ai (ân hạn 10 giây, RT-04) nhưng F4 sẽ thấy **3** lời gọi, không phải 1.
- **Quyết định:** refresh chạy trong `navigator.locks.request('socialapp:auth-refresh')`. Tab thắng khóa gọi refresh rồi phát
  token mới qua `BroadcastChannel('socialapp:auth')`; tab đang chờ khóa, khi tới lượt, thấy token hiện tại **khác** token đã làm
  request của nó nhận 401 → dùng luôn, không gọi refresh. Refresh hỏng → phát `logout` → mọi tab về `/login`.
- BroadcastChannel chỉ truyền trong bộ nhớ giữa các tab **cùng origin**, không ghi xuống đĩa — không trái quyết định 6.
- Trình duyệt thiếu Web Locks (Safari < 15.4) → rơi về lớp trong tab; ân hạn 10 giây của server đỡ phần còn lại.
- Ghi ngược: E7 "Cách thực thi" thêm lớp giữa tab; E2E-02 ghi rõ cả hai kịch bản (3 request trong 1 tab — unit test; 3 tab —
  Playwright/F4).

**Đ-E5 — Validation client: mật khẩu khớp đúng từng ngưỡng; email không chặt hơn server; lỗi server luôn thắng.**

| Trường | Server (nguồn: validator trong `Application/`) | Client |
|---|---|---|
| Đăng ký · email | bắt buộc · ≤ 254 · `MailAddress.TryCreate` (không tên hiển thị, sau `Trim`) | bắt buộc · ≤ 254 sau `trim` · đúng **một** `@`, hai phía không rỗng, không khoảng trắng, không `<>` — **nới hơn** server, không bao giờ chặt hơn |
| Đăng ký · mật khẩu | bắt buộc · ≥ 8 (`string.Length`) · ≤ **72 byte UTF-8** | y hệt: `pw.length >= 8` (JS cũng đếm UTF-16) · `new TextEncoder().encode(pw).length <= 72` |
| Đăng nhập · email | bắt buộc · ≤ 254 — **không kiểm định dạng** | bắt buộc · ≤ 254 |
| Đăng nhập · mật khẩu | bắt buộc · ≤ 72 byte — **không có tối thiểu 8** | bắt buộc · ≤ 72 byte. Thêm tối thiểu 8 ở đây là chặn người dùng trước khi server kịp nói gì |
| Xác minh · token | đúng 64 ký tự `[0-9a-f]` | y hệt; sai dạng → hiện trạng thái 400, **không gọi API** |

- Vì sao email không bắt chước tuyệt đối: không tái tạo được bộ phân tích `MailAddress` của .NET trong JS. Client chặt hơn là
  **chặn nhầm người dùng hợp lệ** — không có đường thoát; client nới hơn thì server trả 400 và FE hiện đúng lỗi dưới trường.
- **Mọi 400 từ server hiển thị theo key của `errors`** (`email`, `password`, `token`, `body`) — kể cả khi client đã kiểm.
- Thông điệp client **dùng đúng câu của server** ("Mật khẩu phải có ít nhất 8 ký tự.", "Mật khẩu tối đa 72 byte."…) để người
  dùng không thấy hai câu khác nhau cho cùng một lỗi. Không `trim` mật khẩu.

**Đ-E6 — Thông điệp lỗi do FE sở hữu, ánh xạ theo (endpoint, status); `detail` của server chỉ là dự phòng.**

- Một hàm `errorMessage(context, error)` trong `lib/api/messages.ts` — bảng đầy đủ ở E4, E5, E3. Ánh xạ theo status giữ
  AC-02 **bất kể** server đổi chữ: 401 của login luôn ra đúng một câu.
- 500: "Đã xảy ra lỗi không mong muốn. Mã tra cứu: `<traceId>`" — `traceId` là thứ duy nhất backend cần để tìm log.
- `fetch` ném `TypeError` (mất mạng, CORS sai, API chưa chạy): "Không kết nối được máy chủ." — không đoán nguyên nhân.
- 429 **không có** `Retry-After` (limiter không đặt, và CORS không expose header đó): "Bạn thao tác quá nhanh. Vui lòng thử lại
  sau ít phút." — không hiện đồng hồ đếm ngược.

**Đ-E7 — MSW là tùy chọn bật tay; mặc định dev dùng API thật.**

> **Đổi Đ-E7 (2026-09-17, nhóm chốt): gỡ mock trình duyệt, MSW chỉ còn trong Vitest.** Nhóm chạy app đầy đủ FE + BE ở mọi
> lượt dựng màn — khối D đã xong nên backend dev luôn sẵn, và chế độ mock chỉ để lại một đường chạy thứ hai phải giữ cho
> khớp (điều kiện `NODE_ENV` + cờ ở hai chỗ trong `providers.tsx`, lượt Playwright bật mock, cảnh báo script tag phải bỏ
> qua trong smoke test). Đã gỡ: cờ `NEXT_PUBLIC_API_MOCKING`, `mocks/browser.ts`, `public/mockServiceWorker.js`, khối
> `msw.workerDirectory` trong `package.json`, nhánh bật mock của `providers.tsx` và hai spec e2e; `mocks/session.ts` nhớ
> phiên giả **trong bộ nhớ** (bỏ `sessionStorage` — mất luôn ngoại lệ Đ-E2 duy nhất). **Giữ nguyên:** gói `msw`,
> `mocks/fixtures.ts` + `handlers.ts` + `node.ts` cho Vitest — nhánh 423/410/429/500 vẫn dựng bằng `msw/node`; cổng CI
> "bundle sạch MSW" giữ làm lưới an toàn. Nhánh lỗi khó tạo trên màn thật (423 cần 5 lần sai…) thì kiểm bằng Vitest, hoặc
> tái hiện tay trên API dev. Các gạch dưới đây là quyết định **gốc**, giữ để truy nguồn; mục "Thực tế thi công" của E2/E4 là
> lịch sử lúc còn mock.

- `NEXT_PUBLIC_API_MOCKING=enabled` mới khởi động service worker, qua `import()` động nên bundle production không chứa MSW.
  Không bật → gọi API dev thật (khối D đã xong nên đây là chế độ mặc định).
- Mock tồn tại vì hai lý do còn nguyên giá trị: dựng màn khi không muốn chạy backend, và **tái hiện nhánh lỗi khó tạo trên API
  thật** (423 cần 5 lần sai; 410 cần token hết hạn; 429; 500).
- Fixture trong `mocks/fixtures.ts` chép **giá trị** từ `example` của hợp đồng và **gắn kiểu** bằng
  `satisfies components['schemas'][...]` — hợp đồng đổi hình dạng thì mock đỏ compile. Không parse yaml lúc chạy: phải tự
  giải `$ref` của `components/responses`, tốn công mà chỉ bảo vệ thêm được chữ trong ví dụ.
- Cùng bộ handler dùng cho Vitest (`msw/node`) — một nguồn cho cả dựng màn lẫn unit test.

**Đ-E8 — Kiểm tự động hai tầng: Vitest cho logic, Playwright cho những gì chỉ trình duyệt thật mới có.**

| Công cụ | Kiểm | Chạy ở |
|---|---|---|
| Vitest + Testing Library + MSW node + `expectTypeOf` | validation, `ApiError`, client (`credentials`, bearer), single-flight trong tab + điều phối giữa tab (qua adapter giả), form E3–E5 (nhánh lỗi qua `msw/node`), type `RoleCode` | local + **CI** |
| Playwright (Chromium) | guard E6, không token trong Web Storage (E4), E2E-02 ba tab (E7), lượt E2E-01 trên dev (đọc link qua API REST của Mailpit) | local, **`workers: 1`** (rate limit theo IP); F3/F4 chạy lại trên staging bằng `BASE_URL` |

- Playwright **không vào CI ở GĐ1**: nó cần API + Postgres + Redis + Mailpit chạy. Kết quả chạy local dán vào PR — cùng nếp "kiểm
  tay ghi bằng chứng" của khối D.
- Job CI `frontend`: `pnpm install --frozen-lockfile` → cổng codegen → lint → typecheck → Vitest → build (E2).

> **Lệch Đ-E8 (2026-09-16, nhóm chốt): Playwright chạy trên Chrome đã cài trên máy, không tải Chromium.**
> `playwright.config.ts` đặt `channel: "chrome"`. Playwright 1.63.0 ghim Chromium bản 1243 (153.0.8010.12); máy làm E1 chỉ có
> bản 1234 của dự án khác (không dùng được) và Chrome hệ thống 152.0.7977.84 — đã thử mở trang bằng Chrome đó, chạy được.
> **Giá phải trả:** Chrome tự cập nhật và khác bản giữa các máy trong nhóm, nên kết quả E2E của người này có thể không tái
> hiện ở người kia. Dán kết quả Playwright vào PR thì **ghi kèm bản Chrome đã chạy**. Muốn quay về Chromium ghim: bỏ
> `channel`, chạy `pnpm exec playwright install chromium`.

**Đ-E9 — Stack FE: Next.js 16 + pnpm + shadcn/ui preset `b50KEhMiu`; ghim phiên bản chính xác.** *(chốt 2026-09-15 — thay Next.js 14 + npm; đã ghi ngược `AGENTS.md` Mục 3, `giai-doan-1.md` B.7/E1, `ke-hoach-trien-khai.md`, `README.md`)* *(sửa 2026-09-16 — mã preset `b2C6hQKDg` → `b50KEhMiu`, xem ghi chú ngay dưới)*

> **Đổi preset 2026-09-16 (nhóm chốt).** Mã cũ `b2C6hQKDg` được mô tả trong tài liệu là theme `emerald`, nhưng scaffold thật
> trên đĩa lại ra theme `rose` + font `dm-sans` + `Noto_Serif` cho heading (`shadcn info` báo `b5EiQOC4fY`) — tài liệu và mã
> nguồn đã lệch nhau từ bước 1. Chốt lấy `b50KEhMiu` làm mã thật: cùng `maia` / `neutral` / `lucide`, theme `rose`, nhưng
> **font `inter`, `fontHeading: inherit`, radius `medium`** — tức preset làm sẵn ba dòng đầu của bảng "Bước 2" bên dưới,
> không phải sửa tay nữa. Đã chạy lại `init` với mã mới (2026-09-16): `lint`/`typecheck`/`build` xanh.

- **Vì sao đổi:** kit shadcn/ui của preset (Base UI, style `base-maia`) dựng trên Tailwind v4 + React 19 — ép về Next 14 là
  không dùng được kit. Đã chạy `init --preset b50KEhMiu --template next` (2026-09-16): ra Next 16.3.4, React 19.2.8,
  Tailwind v4, pnpm, `next-themes`.
- **Template ghim `eslint: "^10"` nhưng ESLint 10 chạy không được ở đây.** `eslint-config-next@16.3.4` kéo
  `eslint-plugin-react@7.37.5`, bản này gọi API cũ của ESLint → `pnpm lint` ném
  `TypeError: contextOrFilename.getFilename is not a function` ngay ở `app/page.tsx`. Sau init phải hạ:
  `pnpm add -D "eslint@^9"` → giải ra `9.39.5`. Lưu ý `--save-exact` **không** thắng được spec `^9`: `package.json` vẫn ghi
  `"^9.39.5"`. Muốn ghim cứng thì sửa tay lúc làm nốt phần ghim của Đ-E9. Gỡ chốt này khi `eslint-config-next` lên bản hỗ
  trợ ESLint 10.
- Ba thay đổi của Next 16 chạm tới tài liệu này: `middleware.ts` **đổi tên thành `proxy.ts`**; `next lint` bị bỏ (script
  `lint` gọi thẳng `eslint`, cấu hình flat `eslint.config.mjs`); Node **≥ 20.9**. Template không dùng `src/` — mã FE nằm ở
  `src/frontend/app`, `src/frontend/components`, `src/frontend/lib`.

| Gói | Phiên bản | Ghi chú |
|---|---|---|
| Node | **24 LTS** (`src/frontend/.nvmrc`, `engines`) | *(đổi 2026-09-17, xem ghi chú dưới bảng)* Next 16 đòi ≥ 20.9; Node 20 hết hỗ trợ 04/2026, Node 22 đã sang bảo trì |
| pnpm | 10.x (`"packageManager"` trong `package.json`) | CI dùng `pnpm/action-setup` đọc đúng trường này |
| `next`, `react`, `react-dom` | bản do template sinh (16.3.x / 19.2.x), ghim chính xác | Không tự nâng major giữa giai đoạn |
| `tailwindcss`, `@tailwindcss/postcss` | 4.x | Không có `tailwind.config.ts` — token ở `app/globals.css` |
| `shadcn` | bản do template sinh, ghim chính xác | Mọi `shadcn add` chạy bằng **bản ghim này** (`pnpm exec shadcn`), không `dlx @latest` (Đ-E12) |
| `@base-ui/react`, `lucide-react`, `class-variance-authority`, `cn`, `tw-animate-css`, `next-themes` | bản do template sinh, ghim chính xác | Kit — chỉ đổi khi đổi kit |
| `openapi-typescript` | `7.13.0` | Bản đã kiểm chứng ở cổng mở (`Presentation/README.md`). Đổi version là đổi file sinh ra → cổng codegen đỏ |
| `msw` | 2.x | Chỉ `msw/node` cho Vitest — không có `public/mockServiceWorker.js` (đổi Đ-E7 2026-09-17); `allowBuilds: msw: false` giữ nguyên |
| `vitest`, `@testing-library/react`, `@playwright/test` | bản ổn định hiện hành | `pnpm-lock.yaml` commit |

> **Đổi Đ-E9 (2026-09-17, nhóm chốt): Node 22 → Node 24.** Ba lý do: (1) máy thi công lane FE đang chạy 24.15.0, và toàn
> bộ E1 + E2 đã chạy xanh trên đó — giữ `.nvmrc` ở 22 nghĩa là **máy, CI và image staging ba bản khác nhau**, và bản đầu
> tiên chạy 22 sẽ là CI, chỗ khó sửa nhất khi nó đỏ; (2) theo lịch phát hành của Node, 24 là bản LTS đang hoạt động, 22 đã
> sang giai đoạn bảo trì; (3) Next 16 chỉ đòi ≥ 20.9 nên không có ràng buộc nào ngăn. Đã sửa cùng lúc: `.nvmrc` = `24`,
> `engines` = `>=24`, `@types/node` = `24.13.5`, Dockerfile của E8 dùng `node:24-alpine`, `frontend-rules.md` Mục 10,
> `src/frontend/README.md`. `ci.yml` **không** phải sửa — nó đọc `node-version-file: src/frontend/.nvmrc`.
> **Chưa kiểm được:** `node:24-alpine` trên `linux/arm64` — chỉ biết chắc khi E8 build image thật (ảnh chính thức có
> arm64, rủi ro thấp).

Template ghi `^` cho gần hết gói: sau init, **xóa mọi `^`/`~`** trong `package.json` rồi `pnpm install`; đặt
`save-exact=true` trong `src/frontend/.npmrc` để các lần `pnpm add` sau tự ghim.

**Đ-E10 — Màn xác minh tự gửi POST một lần khi mở, chặn gọi đôi; xong thì xóa token khỏi URL.**

- Tự gửi thay vì bắt bấm nút: link trong mail là một cú bấm, thêm cú thứ hai là thêm chỗ bỏ cuộc.
- **React StrictMode chạy effect hai lần ở dev** (Next bật mặc định cho App Router). Lần hai gửi cùng token đã tiêu thụ → **410** → màn
  báo "đã hết hiệu lực" dù vừa xác minh thành công. Chặn bằng một `Map<token, Promise>` cấp module, không bằng `useRef`
  (StrictMode dựng lại component nên ref cũng mất).
- Sau khi có kết quả: `router.replace('/verify-email')` — token không nằm lại trong lịch sử trình duyệt.
- **Rủi ro chấp nhận:** bộ quét link của một số hộp thư có chạy JS sẽ tiêu thụ token trước người dùng → họ thấy 410. Hiếm với
  Gmail/Outlook cá nhân; nếu gặp ở F3 thì đổi sang nút bấm, không đổi server.

**Đ-E11 — FE staging là container Next.js `standalone` sau apache, cùng domain với API.** *(cần ghi ngược: B.7 thêm E8; B.8/F1)*

> **Đổi bởi Đ-E14 (2026-09-17).** Route Handler giờ CÓ: dưới `app/bff/**` (không dưới `/api` — vẫn là đường của
> backend). Container FE cần Redis và biến server `API_INTERNAL_URL`, `REDIS_URL`, `APP_ORIGIN`,
> `SESSION_ENCRYPTION_KEY`, `TRUSTED_PROXY_HOPS`; image không còn nhúng biến nào lúc build.

- `deploy/apache-socialapp.conf.example` **đã chừa sẵn** `ProxyPass / http://127.0.0.1:3000/` (đang comment) sau `/api`,
  `/swagger`, `/health`. Staging dùng biến thể apache (`deploy-staging.yml`).
- Hệ quả cho code FE, **có hiệu lực ngay từ E1**:
  - không dùng Route Handler `app/api/**` — apache chuyển mọi `/api` về backend, route đó không bao giờ tới được Next;
  - không route FE nào bắt đầu bằng `/api`, `/health`, `/swagger`;
  - mọi lời gọi API chạy **phía client** (token ở memory của trình duyệt); GĐ1 không có Server Component nào gọi API.
- `.dockerignore` gốc đang loại `src/frontend/` khỏi context của image backend — đúng, giữ nguyên; FE có `.dockerignore` riêng.

**Đ-E12 — Mọi UI theo kit shadcn/ui của preset `b50KEhMiu`; lệch kit là lỗi, không phải phong cách.** *(chốt 2026-09-15; sửa mã preset 2026-09-16 — xem ghi chú ở Đ-E9)*

Preset (đã giải mã bằng `shadcn preset decode b50KEhMiu`): Base UI · style `maia` · base color `neutral` · theme `rose` ·
chart color `rose` · icon `lucide` · font `inter` · fontHeading `inherit` · radius `medium` · có dark mode (`next-themes`,
mặc định theo hệ thống). Token thật sau init: `--primary: oklch(0.514 0.222 16.935)`, `--radius: 0.625rem`.

1. **`components.json` là nguồn sự thật của kit**, commit vào repo. Không sửa tay `style`, `baseColor` — shadcn ghi rõ hai trường
   này không đổi được sau init.
2. **Thêm component chỉ bằng CLI ghim trong `src/frontend/`:** `pnpm exec shadcn add <tên>`. Không `pnpm dlx shadcn@latest add` —
   CLI/registry mới hơn sinh component lệch style các cái đã có. So bản trong repo với bản gốc: `pnpm exec shadcn add <tên> --diff`.
   Nâng bản CLI là một commit riêng, có lý do.
3. **Bốn tầng** *(sửa 2026-09-16 — xem Đ-E13)*: `components/ui/` là kit — chỉ sửa khi thay đổi áp cho **toàn app**, thêm biến
   thể bằng `cva` ngay trong file đó · `components/form/`, `components/shell/` ghép từ `ui`, **không biết nghiệp vụ** ·
   `features/<màn>/` ghép từ hai tầng trên + `lib/` · `app/**` chỉ ráp, không chứa logic.
4. **Token chỉ ở `app/globals.css`** (`:root`, `.dark`, `@theme inline`). Màn dùng tên token (`bg-primary`,
   `text-muted-foreground`, `text-destructive`, `border-border`); không màu thô, không mã màu tùy ý, không đặt radius/bóng riêng
   theo màn — ESLint chặn (E1 bước 5).
5. **Base UI, không Radix:** ghép bằng prop `render`, không có `asChild`; không import `@base-ui/react` ngoài `components/ui/`.
   Mẫu trên mạng phần lớn là Radix — tra docs bản Base UI bằng `pnpm exec shadcn docs <component>`.
6. **Icon chỉ `lucide-react`; font chỉ Inter** (subset `latin` + `vietnamese`) nạp bằng `next/font` ở `app/layout.tsx`.
7. **Không `shadcn eject`** (cắt đường cập nhật kit). **Không chạy `shadcn apply` tùy tiện** — nó đảo thứ tự dòng trong
   `globals.css`, sinh diff thừa. Chỉ khi đổi preset thật: `pnpm exec shadcn apply --preset <mã mới> --only theme,font`, rồi đọc
   diff `globals.css` và `layout.tsx` trước khi commit.
8. **Luật nằm ở `src/frontend/AGENTS.md`** (mục "UI kit", E1 bước 6) cho người và agent đọc; `AGENTS.md` gốc Mục 3 trỏ về đây.

**Đ-E13 — Nghiệp vụ nằm ở `features/<màn>/`; `components/` chỉ giữ UI không biết nghiệp vụ.** *(chốt 2026-09-16 — thay tầng `components/<tính-năng>/` ở Đ-E12 mục 3; cần ghi ngược: B.7/E1, `AGENTS.md` Mục 4)*

- **Vì sao đổi:** để chung một tầng thì `components/` trộn hai loại đổi với nhịp khác hẳn nhau — hạ tầng UI (`ui/`, `form/`,
  `shell/`, đổi hiếm, có luật kit bảo vệ) và nghiệp vụ (`auth/`, `post/`, `feed/`, `chat/`… đổi liên tục). Đến GĐ6 là ~12 thư
  mục cùng cấp và mắt phải tự phân loại mỗi lần nhìn. Tách ra thì `components/` đứng yên ở ba thư mục, `features/` là chỗ duy
  nhất phình theo giai đoạn.
- **Bốn tầng, phụ thuộc một chiều:** `app/` → `features/` → `components/` + `lib/`. `lib/` **không** import ngược lên
  `features/` hay `app/`; `components/` không import `features/`.
- **`features/` không import chéo nhau.** Cái gì hai feature cùng cần thì đẩy xuống `components/` (nếu là UI) hoặc `lib/`
  (nếu là logic) — không `import '../post/…'` từ `features/feed/`.
- **`lib/api/` ở ngoài `features/`, vĩnh viễn.** Sẽ có lúc muốn gom `features/post/api/`. Không — `schema.d.ts` là file **sinh
  tự động**, script `gen:api` và cổng CI codegen đều trỏ `lib/api/`; chuyển vào feature là mỗi module một đường dẫn output và
  cổng CI phải liệt kê tay. GĐ1 giữ nguyên `lib/api/schema.d.ts` phẳng; module thứ hai (GĐ2) mới tách
  `lib/api/<module>/schema.d.ts` như Mục 13 đã ghi.
- **Tên `features/` là tên màn, không phải tên module backend.** Ánh xạ không 1-1 theo cả hai chiều: `Content` (CMP-04 =
  posts, comments, reactions, media, feed) đẻ ra bốn feature qua ba giai đoạn; còn `/feed` một màn thì gọi `content` +
  `profile` + `social-graph`. Chỉ `lib/api/` bám tên module backend.

  | GĐ | Module backend | `lib/api/` | Route | `features/` |
  |---|---|---|---|---|
  | 1 | Identity | *(phẳng)* | `(auth)/login` `register` `verify-email` · `(app)/me` | `auth/` |
  | 2 | Profile, Content | `profile/` `content/` | `(app)/profile/[handle]` · `(app)/posts/[id]` | `profile/` `post/` |
  | 4 | SocialGraph, Content | `social-graph/` | `(app)/feed` | `feed/` `follow/` |
  | 3 | Content | *(đã có)* | *không route mới* | `comment/` `reaction/` |
  | 5 | Messaging | `messaging/` | `(app)/messages/[conversationId]` | `chat/` |
  | 6 | Notification, Moderation | `notification/` `moderation/` | `(app)/notifications` · `admin/reports` | `notification/` `moderation/` |

- **Hình dạng bên trong một feature — chốt luôn, nếu không sáu feature sẽ có sáu kiểu bày.** Để **phẳng** cho tới khi một
  *loại* file chạm 2 cái thì mới mở thư mục con:

  ```
  features/auth/            # GĐ1 — phẳng
  ├─ login-form.tsx · register-form.tsx · verify-email.tsx

  features/post/            # GĐ2 — đã có 2 loại
  ├─ components/  post-card.tsx · post-composer.tsx
  ├─ hooks/       use-post-draft.ts
  └─ api.ts                 # gọi lib/api/http.ts, kiểu lấy từ lib/api/content/
  ```

- **Không tạo sẵn thư mục rỗng cho giai đoạn chưa tới.** Git không theo dõi thư mục rỗng, và tên feature còn phụ thuộc màn
  thiết kế ra sao. GĐ1 chỉ có `features/auth/`.
- **Không dùng feature-sliced "đầy đủ"** (mỗi feature có `ui/` và `api/` riêng): kit phải nằm ở `components/ui/` để
  `shadcn add` ghi đúng chỗ theo alias `components.json`, và luật ESLint theo đường dẫn (`components/ui/**`,
  `lib/api/http.ts`) sẽ phải nhân lên theo số feature — thêm một feature là phải sửa `eslint.config.mjs`.

**Đ-E14 — Backend-for-Frontend: trình duyệt không bao giờ cầm JWT; Next server giữ token trong Redis.** *(chốt 2026-09-17 —
thay Đ-E1, Đ-E4; đổi Đ-E2, Đ-E3, Đ-E11; ghi ngược `giai-doan-1.md` quyết định 6, B.7/E7, Mục 10.1 E2E-02)*

- **Vì sao đổi.** Sau E4, header `Authorization: Bearer <JWT>` hiện nguyên trong tab Network của DevTools cho mọi request
  `/me`. Nhóm chốt: token không được xuất hiện ở trình duyệt dưới bất kỳ dạng nào. Token trong memory không lộ ra ngoài
  máy, nhưng một đoạn script lạ chạy trên trang (XSS) đọc được và **mang đi dùng ở nơi khác** tới khi hết hạn; sau BFF,
  XSS chỉ còn hành động được trong chính tab đó.
- **Luồng.** Trình duyệt → `/bff/*` (cùng origin, cookie `__Host-sid`) → Next server (Route Handler) → API .NET
  (`Authorization: Bearer`, cookie `refresh_token` gửi server-to-server). Hợp đồng API **không đổi** — BFF chỉ là một
  client khác của nó.

  | Trình duyệt gọi | BFF làm | Trình duyệt nhận |
  |---|---|---|
  | `POST /bff/auth/login` | gọi `/auth/login`, cất access + refresh token vào Redis | **204**, `Set-Cookie: __Host-sid` |
  | `POST /bff/auth/register`, `/verify-email` | chuyển tiếp | status + body của API |
  | `GET /bff/auth/session` | tra Redis | `{ authenticated }` |
  | `/bff/api/<path>` | gắn bearer; 401 → refresh (khóa Redis) → gọi lại **một** lần | status + body + `X-Correlation-ID` |
  | `POST /bff/auth/logout` | gọi `/auth/logout` (thu hồi family), xóa Redis | **204**, xóa cookie |

- **Các lớp chặn — mỗi lớp có test riêng (Mục 8A):**
  1. Cookie `__Host-sid`: `HttpOnly`, `Secure`, `SameSite=Lax`, `Path=/`, không `Domain`; giá trị 32 byte ngẫu nhiên, không
     phải JWT. Cookie sai dạng không bao giờ chạm tới Redis.
  2. Redis: key là **SHA-256** của session ID; giá trị **AES-256-GCM** với `SESSION_ENCRYPTION_KEY`, AAD gắn với key — dump
     Redis không ra token, không chép được phiên sang key khác.
  3. **CSRF:** mọi method thay đổi dữ liệu phải có `Origin` đúng `APP_ORIGIN`; `Sec-Fetch-Site` khác `same-origin` bị chặn.
  4. **Session fixation:** đăng nhập luôn cấp ID mới và xóa phiên cũ.
  5. **Không rò token:** header từ API ra trình duyệt theo danh sách trắng (`content-type`, `x-correlation-id`) —
     `Set-Cookie` của API không bao giờ đi qua. Proxy chung chặn nhóm `auth/*` (body của `/auth/refresh` chứa token),
     segment `.`/`..`/`/`/`\`, và mã hóa lại từng segment (không chèn được query string).
  6. **Single-flight ở server:** khóa Redis `SET NX PX` theo phiên, nhả bằng so-chủ (Lua); trong khóa so token hiện tại
     với token vừa hỏng (`stale`) trước khi refresh. Refresh 401 → xóa phiên; 429/500 → giữ phiên.
  7. `Cache-Control: no-store` cho mọi response BFF; header bảo mật cho mọi trang (`X-Frame-Options: DENY`,
     `nosniff`, `Referrer-Policy: same-origin`, `Permissions-Policy`, HSTS khi production); `poweredByHeader: false`.
  8. Code server trong `lib/bff/**` có `import "server-only"` — import từ client component là **build đỏ** (đã thử); cổng CI
     "bundle sạch" chặn `ioredis`, tên biến server, tiền tố key Redis trong `.next/static`.
  9. IP người dùng gửi cho API bằng `X-Forwarded-For` (phần tử thứ `TRUSTED_PROXY_HOPS` tính từ cuối); API chỉ tin header
     này từ proxy đã khai (`ForwardedClientIpTests`) — rate limit vẫn theo từng người.
- **Cấu hình (server, đọc lười ở request đầu):** `API_INTERNAL_URL`, `REDIS_URL`, `APP_ORIGIN`, `SESSION_ENCRYPTION_KEY`,
  `TRUSTED_PROXY_HOPS`. Development có mặc định local và khóa ngẫu nhiên theo tiến trình; production thiếu thì request
  đầu ném lỗi nêu tên biến. `next build` không cần biến nào.
- **Giá phải trả:** container FE phụ thuộc Redis (F1); mỗi lời gọi API thêm một chặng Next server; access token 15 phút
  không còn "chết theo tab" — nó sống trong Redis tới khi hết hạn hoặc đăng xuất (bù lại: đăng xuất xóa ngay ở server).
- **Chưa làm:** Content-Security-Policy (cần nonce theo request qua `proxy.ts` — việc riêng). SignalR GĐ5: trình duyệt
  không có token để gắn vào hub — phải đi qua BFF hoặc vé ngắn hạn; quyết định ở GĐ5.

---

## 2. E1 — Scaffold Next.js 16 + shadcn/ui preset `b50KEhMiu`

**Mục tiêu.** Có nền để dựng màn, và bộ primitive dùng lại cho GĐ2–GĐ8 — lấy từ **một** kit (Đ-E9, Đ-E12) thay vì tự viết.
Kit có trước màn thì mọi màn cùng một cách hiển thị nhãn, lỗi, trạng thái đang gửi — thứ GĐ2 (composer), GĐ5 (chat), GĐ6
(admin) đều cần.

**Kết quả mong đợi.**
- `src/frontend/` sinh bằng lệnh init của preset; **không** còn `src/frontend/.git` lồng; `components.json` commit
  (`style: base-maia`, `baseColor: neutral`, `iconLibrary: lucide`).
- `package.json`: `"packageManager": "pnpm@10.x"`, **không** còn `^`/`~`; script `dev`, `build`, `start`, `lint`, `typecheck`,
  `format`, `test` (`vitest run`), `test:e2e` (`playwright test`) — `gen:api` thêm ở E2.
- Đã sửa sau init (bước 2): font Inter có subset `vietnamese`, `lang="vi"`, bỏ phím tắt `d` đổi giao diện.
- Kit component: `button` (có sẵn) + `input`, `label`, `field`, `alert`, `card`, `skeleton`, `spinner`, `sonner`.
- `components/form/text-field.tsx` — composite dùng chung cho E3–E5, ghép từ `Field` + `Input`.
- `eslint.config.mjs` có luật Đ-E2 + Đ-E12; `src/frontend/AGENTS.md` có mục "UI kit" (Đ-E12) dưới khối luật Next.
- `.nvmrc` (`24`), `.npmrc` (`save-exact=true`), `.env.example`, `.dockerignore`.
- Layout `app/(auth)/layout.tsx` + khung tĩnh `/login` dùng `Card`, `TextField`, `Button` — chính là màn E4 sau này.
- `pnpm lint`, `pnpm typecheck`, `pnpm test`, `pnpm build` xanh; Vitest `text-field.test.tsx` xanh; đã thử cho đỏ luật màu thô.

### Các bước

**Bước 1 — sinh dự án** (PowerShell, chạy từ `src/` — `init --name` tạo thư mục **anh em** với `backend/`):

```powershell
cd src
Remove-Item -Recurse -Force frontend              # hiện chỉ có .gitkeep; init --name tạo thư mục mới
pnpm dlx shadcn@latest init --preset b50KEhMiu --template next --name frontend
Remove-Item -Recurse -Force frontend\.git         # template tự `git init` → repo lồng trong repo
cd frontend
pnpm exec shadcn add input label field alert card separator skeleton spinner sonner
pnpm add -D --save-exact vitest @vitejs/plugin-react jsdom @testing-library/react @testing-library/user-event @testing-library/jest-dom @playwright/test
pnpm add -D "eslint@^9"                           # template ghim ^10, ESLint 10 làm `pnpm lint` nổ — xem Đ-E9
```

Sau đó xóa mọi `^`/`~` trong `package.json` (Đ-E9), thêm `"packageManager"` đúng bản `pnpm -v`, chạy `pnpm install`.
Kiểm: `pnpm exec shadcn info` ra `style base-maia`, `base base`, `iconLibrary lucide`, `theme rose`, `font inter`.

> `shadcn info` báo mã preset `b50KEhMfo` thay vì `b50KEhMiu` — **vô hại**. Hai mã chỉ khác `radius` (`medium` / `default`),
> hai giá trị cho cùng `--radius: 0.625rem` (kiểm trên scaffold thật 2026-09-16). Đừng "sửa" bằng `shadcn apply` — xem Đ-E12
> mục 7.

**Bước 2 — sửa ngay sau init.**

| File | Template sinh | Sửa thành | Vì sao |
|---|---|---|---|
| `app/layout.tsx` | `Inter({ subsets: ['latin'] })` cho `--font-sans` | thêm subset: `Inter({ subsets: ['latin', 'vietnamese'], … })` | Thiếu subset `vietnamese` thì dấu vẽ bằng font dự phòng, lệch nét |
| `app/layout.tsx` | `<html lang="en">` | `lang="vi"` | Trình đọc màn hình và gạch chân chính tả |
| `app/layout.tsx` | `Geist` import nhưng không dùng (chỉ dùng `Geist_Mono`) | bỏ `Geist` | Lint |
| `components/theme-provider.tsx` | `ThemeHotkey` — phím `d` đổi sáng/tối ở mọi nơi ngoài ô nhập | xóa `ThemeHotkey` | Mạng xã hội có nhiều phím tắt, `d` bấm nhầm là đổi giao diện |
| `app/layout.tsx` | chưa có `metadata` | `title: 'SocialApp'` | |

> **Mã preset cũ `b2C6hQKDg` từng sinh ra `DM_Sans` + `Noto_Serif` dù khai `font inter`** — đó là một trong hai lý do đổi sang
> `b50KEhMiu` (Đ-E9, 2026-09-16). Với mã mới, `layout.tsx` ra thẳng `Inter` và `globals.css` có `--font-heading:
> var(--font-sans)`, nên chỉ còn thiếu subset `vietnamese`. Lưu ý `shadcn info` đọc `components.json` chứ không đọc
> `layout.tsx` — font phải kiểm bằng mắt trong `app/layout.tsx`. Muốn giữ một font serif riêng cho heading thì đó là **sửa
> Đ-E12 mục 6**, làm thành quyết định có ngày tháng, không sửa lặng ở `layout.tsx`.

**Bước 3 — cấu trúc thư mục** (template **không** dùng `src/` của riêng nó; bốn tầng của Đ-E13, chốt ngay để GĐ2+ không phải
dời file):

```
src/frontend/
├─ AGENTS.md · components.json · eslint.config.mjs · next.config.ts · vitest.config.ts · playwright.config.ts
├─ app/                    # ROUTE — ráp trang từ features/ + components/, không chứa logic nghiệp vụ
│  ├─ (auth)/  layout.tsx · login/ · register/ · register/check-email/ · verify-email/
│  ├─ (app)/   layout.tsx (shell + RequireAuth) · me/
│  ├─ layout.tsx · page.tsx (redirect → /me) · providers.tsx ('use client': Theme + AuthProvider) · globals.css
├─ components/             # UI KHÔNG biết nghiệp vụ — ba thư mục này đứng yên qua GĐ2–GĐ8
│  ├─ ui/                  # KIT — sinh bởi `shadcn add`. Chỉ sửa khi đổi cho TOÀN app (Đ-E12)
│  ├─ form/                # ghép từ ui: text-field.tsx, form-alert.tsx
│  ├─ shell/               # header + nút Đăng xuất của nhóm (app) — E6
│  └─ theme-provider.tsx
├─ features/               # NGHIỆP VỤ (Đ-E13) — mọc dần theo giai đoạn, không tạo sẵn thư mục rỗng
│  └─ auth/                # GĐ1, phẳng: login-form.tsx · register-form.tsx · verify-email.tsx
├─ lib/                    # HẠ TẦNG — không import ngược lên features/ hay app/
│  ├─ utils.ts (cn)
│  ├─ api/                 # schema.d.ts (sinh) · types.ts · config.ts · http.ts · problem.ts · auth-api.ts · messages.ts
│  ├─ auth/                # token-store.ts · refresh-coordinator.ts · session.ts · auth-context.tsx · require-auth.tsx · safe-next.ts · verify-once.ts
│  └─ validation/          # auth.ts
├─ hooks/ · mocks/ (MSW cho Vitest) · test/ · e2e/
└─ public/
```

Hướng phụ thuộc một chiều: `app/` → `features/` → `components/` + `lib/`. Đặt file mới thì hỏi hai câu theo thứ tự:
*component này có biết nghiệp vụ không?* (không → `components/`) · *nó là logic không phải React không?* (đúng → `lib/`).
Còn lại vào `features/<màn>/`.

**Bước 4 — composite `TextField`.** Màn E3–E5 dùng cái này, không tự ráp `Field` mỗi nơi:

```tsx
// components/form/text-field.tsx
"use client"
import { useId, type ComponentProps } from "react"
import { Field, FieldDescription, FieldError, FieldLabel } from "@/components/ui/field"
import { Input } from "@/components/ui/input"

type Props = Omit<ComponentProps<typeof Input>, "id"> & { label: string; description?: string; error?: string }

export function TextField({ label, description, error, ...inputProps }: Props) {
  const id = useId()
  return (
    <Field data-invalid={error ? true : undefined}>
      <FieldLabel htmlFor={id}>{label}</FieldLabel>
      <Input id={id} aria-invalid={error ? true : undefined} {...inputProps} />
      {description && <FieldDescription>{description}</FieldDescription>}
      <FieldError errors={error ? [{ message: error }] : undefined} />
    </Field>
  )
}
```

- Lỗi cả form (401, 423, 500…) dùng `Alert` của kit — `components/form/form-alert.tsx` bọc `Alert variant="destructive"`.
- Nút đang gửi: `<Button type="submit" disabled={pending}>{pending && <Spinner data-icon="inline-start" />}Đăng nhập</Button>`
  — `data-icon` là quy ước canh lề icon của `button` trong kit. **Luôn ghi `type` rõ ràng.**

**Bước 5 — ESLint** (`eslint.config.mjs`, thêm sau `...nextTs`). Luật Đ-E2 và Đ-E12 bật ngay từ E1 để không ai kịp viết sai:

```js
// Flat config: override cùng tên rule THAY hẳn options, không cộng dồn — nên pattern gốc phải
// tách ra hằng số và spread lại ở mọi override, nếu không features/** mất lệnh cấm Đ-E12.
const KIT = [
  { group: ["@base-ui/react", "@base-ui/react/*"], message: "Đ-E12: dùng components/ui, không gọi Base UI trực tiếp." },
  { group: ["react-icons", "react-icons/*", "@tabler/*", "@heroicons/*", "@phosphor-icons/*"], message: "Đ-E12: icon chỉ lucide-react." },
];

{
  files: ["**/*.{ts,tsx}"],
  rules: {
    "no-restricted-globals": ["error",
      { name: "localStorage",   message: "Đ-E2: token chỉ ở memory (Mục 12)." },
      { name: "sessionStorage", message: "Đ-E2: token chỉ ở memory (Mục 12)." },
      { name: "fetch",          message: "Đ-E2: gọi API qua lib/api/http.ts." }],
    "no-restricted-properties": ["error",
      { object: "window",   property: "localStorage",   message: "Đ-E2" },
      { object: "window",   property: "sessionStorage", message: "Đ-E2" },
      { object: "document", property: "cookie",         message: "Đ-E2: cookie refresh là HttpOnly, JS không đụng" }],
    "react/no-danger": "error",
    "no-restricted-imports": ["error", { patterns: KIT }],
    "no-restricted-syntax": ["error",
      { selector: "Literal[value=/\\b(bg|text|border|ring|fill|stroke|from|via|to|outline|divide)-(slate|gray|zinc|neutral|stone|red|orange|amber|yellow|lime|green|emerald|teal|cyan|sky|blue|indigo|violet|purple|fuchsia|pink|rose)-\\d{2,3}\\b/]",
        message: "Đ-E12: dùng token (bg-primary, text-muted-foreground, text-destructive…), không màu thô." },
      { selector: "Literal[value=/-\\[#[0-9a-fA-F]{3,8}\\]/]", message: "Đ-E12: không mã màu tùy ý." }],
  },
},
// Đ-E13 — ranh giới bốn tầng. Mỗi feature tự import trong thư mục mình bằng đường dẫn tương đối;
// đụng tới feature khác thì phải đi qua alias và bị chặn ở đây.
{ files: ["features/**"], rules: { "no-restricted-imports": ["error", { patterns: [...KIT,
    { group: ["@/features/*"], message: "Đ-E13: features/ không import chéo nhau — đẩy phần dùng chung xuống components/ hoặc lib/." },
] }] } },
{ files: ["lib/**", "components/**"], rules: { "no-restricted-imports": ["error", { patterns: [...KIT,
    { group: ["@/features", "@/features/*", "@/app", "@/app/*"], message: "Đ-E13: lib/ và components/ không biết nghiệp vụ, không import ngược lên." },
] }] } },

// PHẢI đứng cuối: `components/ui/**` khớp cả `components/**` ở trên, mà flat config lấy khối SAU.
// Đảo lên trước là kit bị cấm import `@base-ui/react` — tức là cấm chính thứ nó được phép dùng.
{ files: ["components/ui/**"], rules: { "no-restricted-imports": "off", "no-restricted-syntax": "off" } },
```

- Luật Đ-E13 chỉ chặn **import chéo qua alias `@/features/…`**; `features/post/` tự import file của chính nó bằng
  `./…` nên không vướng. Đổi lại, một feature cố tình dùng `../post/post-card` (tương đối, vượt thư mục) thì luật không bắt —
  chỗ đó review bằng mắt, nhưng nó hiếm và nhìn là thấy ngay.
- `globalIgnores` thêm `lib/api/schema.d.ts` (E2). *(`public/mockServiceWorker.js` từng nằm đây — gỡ cùng mock trình duyệt, đổi Đ-E7.)*
- Chỗ **duy nhất** được gọi `fetch` (`lib/api/http.ts`) tắt luật bằng
  `// eslint-disable-next-line no-restricted-globals -- <lý do, trỏ Đ-E2/Đ-E7>` ngay tại dòng — lý do nằm cạnh ngoại lệ.
- Luật màu chỉ bắt chuỗi literal (kể cả trong `cn("…")`), không bắt template literal — review vẫn cần, nhưng phần lớn lỗi
  thật là chuỗi literal.

**Bước 6 — `src/frontend/AGENTS.md`.** Giữ khối `nextjs-agent-rules` template sinh, thêm mục "UI kit" chép 8 luật của Đ-E12 —
người và agent sửa FE đọc file này trước.

**Bước 7 — Vitest** (`vitest.config.ts`: plugin react, `environment: "jsdom"`, `setupFiles: ["test/setup.ts"]`, alias
`@` → thư mục `frontend`, `exclude: ["e2e/**", "node_modules/**"]`, `typecheck: { enabled: true, include: ["**/*.test-d.ts"] }`).
`test/setup.ts` import `@testing-library/jest-dom/vitest`. Base UI báo thiếu `ResizeObserver`/`PointerEvent` trong jsdom thì
thêm polyfill vào chính file này, không rải trong từng test.

### Test

| Test | Kỳ vọng |
|---|---|
| `text-field.test.tsx` — có `error` | `getByLabelText("Email")` trả đúng input; input `aria-invalid="true"`; thấy chữ lỗi; `toHaveAccessibleDescription(/Email không đúng định dạng/)` — **đỏ thì** kit chưa tự nối `aria-describedby`: thêm id cho `FieldDescription`/`FieldError` và nối tay trong `TextField` |
| cùng file — không `error` | không `aria-invalid`, không có phần tử lỗi |
| **Thử cho đỏ luật kit** (local, rồi hoàn tác) | Tạm thêm `className="bg-blue-600"` vào `app/(auth)/layout.tsx` và `import { Button } from "@base-ui/react/button"` vào `app/page.tsx` → `pnpm lint` đỏ đúng hai thông điệp Đ-E12; hoàn tác, `git status` sạch |

### Cạm bẫy đã biết

| # | Bẫy | Hệ quả | Chặn bằng |
|---|---|---|---|
| 1 | Quên xóa `src/frontend/.git` | Git gốc coi `frontend` là repo lồng (gitlink) — commit **không chứa file nào** của FE, CI checkout ra thư mục rỗng | Bước 1; kiểm `git ls-files src/frontend \| Measure-Object` > 0 sau commit |
| 2 | `pnpm dlx shadcn@latest add …` về sau | CLI/registry mới hơn bản ghim → component mới lệch style các component đã có | Luôn `pnpm exec shadcn add` (Đ-E12) |
| 3 | Chép mẫu Radix trên mạng (`asChild`) | Kit là **Base UI**: ghép bằng prop `render`, không có `asChild` — TS báo prop lạ | Tra `pnpm exec shadcn docs <component>` (docs bản Base UI) |
| 4 | Font chỉ subset `latin` | Chữ có dấu vẽ bằng font dự phòng | Bước 2 |
| 5 | Sửa màu/radius trong từng màn | Kit trôi dần, mỗi màn một kiểu | ESLint bước 5; đổi ở `app/globals.css` hoặc biến thể trong `components/ui` |
| 6 | Chạy `shadcn apply` "cho chắc" | Đảo thứ tự dòng trong `globals.css` → diff thừa trong PR | Chỉ `apply` khi đổi preset thật (Đ-E12) |
| 7 | Giữ `ThemeHotkey` | Bấm `d` ở bất kỳ đâu ngoài ô nhập là đổi giao diện | Bước 2 |

### Thực tế thi công — 2026-09-16

Cả tám bước đã chạy. Những chỗ **khác** hướng dẫn ở trên, và những thứ chỉ lộ ra khi gõ thật:

**`@types/node` bám theo `.nvmrc`, không theo template.** Template ghim `@types/node@^20` — để nguyên là kiểu Node lệch
một major so với bản chạy thật. E1 nâng lên 22 cho khớp Đ-E9 lúc đó; sau khi đổi Đ-E9 sang Node 24 (2026-09-17) thì ghim
`24.13.5`, `pnpm typecheck` vẫn xanh. `engines` đặt `{"node": ">=24"}` chứ không `>=24 <25`: chặn trần chỉ làm
`pnpm install` cằn nhằn trên máy chạy Node mới hơn mà không đổi được gì về hành vi build.

**`components/form/form-alert.tsx` chuyển sang E4.** Bước 4 có nhắc nó, nhưng E1 không có chỗ nào hiện lỗi cả form —
tạo sẵn là thêm một component không người dùng, không test. E4 dựng nó cùng lúc với nhánh 401/423/429/500.

**`aria-describedby` phải nối tay — đúng như bước 4 đã ngờ.** `FieldDescription` và `FieldError` của kit không tự sinh
`id`, nên `toHaveAccessibleDescription` đỏ nếu chỉ ráp theo mẫu. `TextField` tự sinh `id` cho hai phần tử đó và ghép
vào `aria-describedby` của `Input`. **Không** sửa `components/ui/field.tsx` — kit giữ nguyên bản gốc (Đ-E12 mục 1).

**`vitest.config.ts` để `globals: false` nên phải tự gọi `cleanup`.** Testing Library chỉ tự dọn DOM khi có
`afterEach` toàn cục. Thiếu nó thì DOM của test trước còn lại, `getByLabelText("Email")` thấy **hai** phần tử và đỏ vô
cớ — mất 10 phút để thấy đó không phải lỗi của `TextField`. `test/setup.ts` gọi `afterEach(cleanup)` một lần cho cả bộ.

**`typecheck: { enabled: true }` không đỏ khi chưa có file `*.test-d.ts`** — Vitest báo `Type Errors  no errors` và đi
tiếp, nên bật sẵn từ E1 được, E2 thả type test `RoleCode` vào là chạy ngay.

**`e2e/` có `.gitkeep`.** `playwright.config.ts` trỏ `testDir: "./e2e"`; git không theo dõi thư mục rỗng nên thiếu
`.gitkeep` là người clone về không có thư mục đó. Spec đầu tiên vào ở E4. **Chưa** chạy `playwright install chromium` —
để lại cho E4, khi đã có spec để chạy.

**`.gitignore` của template chặn `.env*` không chừa ngoại lệ** — đã thêm `!.env.example`, nếu không `.env.example` mà
E1 phải giao không bao giờ vào được commit.

**`app/page.tsx`** thành trang tĩnh tiếng Việt trỏ sang `/login` (E6 đổi thành redirect → `/me`). Trang mẫu của template
có dòng "Press `d` to toggle dark mode" — bỏ `ThemeHotkey` mà giữ dòng đó là nói dối người đọc.

**Kit ra khỏi phạm vi Prettier — `components/ui/` trong `.prettierignore` (nhóm chốt 2026-09-16).** CLI shadcn không chạy
Prettier của dự án: `sonner.tsx` và `spinner.tsx` ra đúng kiểu của registry (cả thẻ `<Loader2Icon …/>` trên một dòng), nên
`pnpm format` sẽ viết lại chúng. Hệ quả là diff chỉ khác cách xuống dòng lọt vào PR không liên quan, `shadcn add --overwrite`
sinh diff đảo ngược, và `shadcn add <tên> --diff` (Đ-E12 mục 2) mất tác dụng vì mọi dòng đều "khác". Bỏ kit khỏi Prettier
thì kit trong repo giống hệt bản shadcn sinh ra. Kiểm: `prettier --check "**/*.{ts,tsx}"` — đúng glob của `pnpm format` —
xanh toàn bộ.

**Ghi nhận, không phải lệch:** `pnpm exec shadcn info` in ra `preset b50KEhMfo`, `radius default` — đúng như cảnh báo ở
bước 1, vô hại. `style base-maia`, `base base`, `iconLibrary lucide`, `theme rose`, `font inter` khớp Đ-E12.

**Bằng chứng.**

| Cổng | Kết quả |
|---|---|
| `pnpm lint` | xanh, 0 lỗi |
| `pnpm typecheck` | xanh |
| `pnpm test` | **3/3** xanh (`components/form/text-field.test.tsx`), `Type Errors no errors` |
| `pnpm build` | xanh — 3 route: `/`, `/_not-found`, `/login`, đều static |
| `pnpm dev` + `GET /login` | `200`, `<html lang="vi">`, có "Đăng nhập" và "Mật khẩu" |

Thử cho đỏ ESLint — thêm vi phạm, `pnpm lint` đỏ đúng thông điệp, rồi khôi phục (`git status` sạch trước/sau):

| Vi phạm cắm vào | Luật bắt | Thông điệp |
|---|---|---|
| `import { Button } from "@base-ui/react/button"` trong `app/page.tsx` | `no-restricted-imports` | Đ-E12: dùng components/ui… |
| `className="bg-blue-600"` trong `app/(auth)/layout.tsx` | `no-restricted-syntax` | Đ-E12: dùng token… |
| `className="text-[#ff0000]"` | `no-restricted-syntax` | Đ-E12: không mã màu tùy ý |
| `localStorage.setItem(…)` | `no-restricted-globals` | Đ-E2: token chỉ ở memory |
| `fetch("/api/v1/me")` ngoài `http.ts` | `no-restricted-globals` | Đ-E2: gọi API qua lib/api/http.ts |
| `document.cookie` | `no-restricted-properties` | Đ-E2: cookie refresh là HttpOnly… |
| `features/tmp-thu/` import `@/features/auth/…` | `no-restricted-imports` | Đ-E13: features/ không import chéo nhau |
| `components/form/` import `@/features/auth/…` | `no-restricted-imports` | Đ-E13: lib/ và components/ không import ngược lên |

Khối `components/ui/**` đứng cuối có tác dụng thật: kit vẫn `import … from "@base-ui/react/input"` mà lint xanh.

**Playwright chạy được, và bài smoke đầu tiên bắt được hai lỗi thật (2026-09-17).** `e2e/smoke.spec.ts` vào `/`, bấm sang
`/login`, đòi không có lỗi console và Web Storage rỗng. Hai thứ nó bắt được ngay lần chạy đầu:

1. **`<Button render={<Link/>}>` dán ngữ nghĩa nút lên thẻ `<a>`.** Base UI cảnh báo trong console
   ("expected a native `<button>`… can impact forms and accessibility"), và hạ `nativeButton={false}` thì thẻ `<a>` mất
   `role="link"` — `getByRole("link")` không thấy nữa. Điều hướng thì phải là **link thật**: dùng
   `<Link className={buttonVariants()}>`. `render` của Base UI hợp khi thứ được render **vẫn là nút**.
2. **`CardTitle` của kit render ra `div`, không phải thẻ heading.** Màn `/login` do đó chưa có heading thật; test bám
   `[data-slot="card-title"]`. **Để lại cho E4 quyết**: thêm heading thật cho ba màn `(auth)` hay chấp nhận như kit.

Bài test cũng đụng chính luật Đ-E2 của mình: đọc `window.localStorage` trong `page.evaluate` bị ESLint chặn. Mở ngoại lệ
**ngay tại dòng** kèm lý do ("đây là bài test chứng minh luật đó, code chạy trong trang đang kiểm") — không nới luật cho cả
`e2e/**`.

**Bốn cổng chạy trên Node 24.15.0.** Lúc làm E1 con số này lệch `.nvmrc` (khi đó là `22`); đã xử bằng cách đổi hẳn
Đ-E9 sang Node 24 ngày 2026-09-17, nên máy thi công, `.nvmrc` mà CI đọc, và image của E8 giờ cùng một major.

---

## 3. E2 — Sinh type từ hợp đồng + api client + mock MSW

**Mục tiêu.** Biến "đổi hợp đồng mà quên sửa FE" thành **lỗi compile** ngay trên máy và **CI đỏ** trong PR — thay vì lỗi runtime
phát hiện ở staging lúc cổng đóng. Và có **một** chỗ duy nhất nói chuyện với API, để `credentials: 'include'`, bearer, và
interceptor E7 không bị quên ở màn nào.

**Kết quả mong đợi.**
- `pnpm gen:api` → `lib/api/schema.d.ts`, **commit vào repo**; ESLint/Prettier bỏ qua file này.
- `lib/api/types.ts` — alias kiểu cho màn dùng (`RoleCode`, `TokenResponse`, `MeResponse`, `ProblemDetails`…).
- `lib/api/config.ts`, `problem.ts` (`ApiError`, `NetworkError`), `http.ts` (`request`), `auth-api.ts` (6 hàm).
- `lib/auth/token-store.ts` (Đ-E2) — E2 dựng, E6/E7 dùng.
- `mocks/` — fixture có kiểu, handler theo kịch bản, `browser.ts` + `node.ts`; `providers.tsx` bật MSW khi
  `NEXT_PUBLIC_API_MOCKING=enabled`.
- Test xanh: `schema.test-d.ts`, `http.test.ts`, `problem.test.ts`.
- `ci.yml` có job `frontend`; đã thử cho đỏ cổng codegen một lần (Mục Test).

### Các bước

**Bước 1 — codegen.**

```json
// package.json
"gen:api": "openapi-typescript ../backend/Modules/Identity/Presentation/identity-v1.yaml -o lib/api/schema.d.ts"
```

```bash
pnpm add -D --save-exact openapi-typescript@7.13.0
pnpm gen:api
```

- **Commit file sinh ra.** Không commit thì máy nào quên chạy lại codegen vẫn build xanh với type cũ; commit thì đổi hợp đồng mà
  quên codegen hiện thành **diff** trong PR — và cổng CI ở bước 6 biến diff đó thành đỏ.
- `.gitattributes` gốc đã ép `eol=lf` cho mọi file text → file sinh trên Windows và trên runner Linux giống nhau từng byte.

```ts
// lib/api/types.ts — màn KHÔNG import thẳng schema.d.ts, đi qua đây cho gọn
import type { components } from './schema';
type S = components['schemas'];
export type RoleCode = S['RoleCode'];
export type UserStatus = S['UserStatus'];
export type ProblemDetails = S['ProblemDetails'];
export type RegisterRequest = S['RegisterRequest'];   export type RegisterResponse = S['RegisterResponse'];
export type VerifyEmailRequest = S['VerifyEmailRequest']; export type VerifyEmailResponse = S['VerifyEmailResponse'];
export type LoginRequest = S['LoginRequest'];         export type TokenResponse = S['TokenResponse'];
export type MeResponse = S['MeResponse'];
```

```ts
// lib/api/schema.test-d.ts
import { expectTypeOf, test } from 'vitest';
import type { RoleCode, MeResponse } from './types';

test('RoleCode là union chuỗi đúng hợp đồng (quyết định 1)', () => {
  expectTypeOf<RoleCode>().toEqualTypeOf<'USER' | 'MODERATOR' | 'ADMIN'>();
});
test('MeResponse có cả role lẫn roleDisplayName (quyết định 3)', () => {
  expectTypeOf<MeResponse>().toHaveProperty('roleDisplayName').toEqualTypeOf<string>();
});
```

**Bước 2 — cấu hình (Đ-E1).**

```ts
// lib/api/config.ts
const fromEnv = process.env.NEXT_PUBLIC_API_BASE_URL;   // phải viết nguyên văn — Next chỉ thay chuỗi này lúc build

export const API_BASE_URL: string = (() => {
  if (fromEnv) return fromEnv.replace(/\/$/, '');
  if (process.env.NODE_ENV !== 'production') return 'http://localhost:5259/api/v1';
  throw new Error('Thiếu NEXT_PUBLIC_API_BASE_URL khi build production. Staging: /api/v1 (Đ-E1).');
})();
```

`src/frontend/.env.example` ghi hai dòng có chú thích: `NEXT_PUBLIC_API_BASE_URL=http://localhost:5259/api/v1` và
`# NEXT_PUBLIC_API_MOCKING=enabled`. Giá trị thật đặt trong `src/frontend/.env.local` (đã bị `.gitignore`).

**Bước 3 — lỗi.** Một kiểu lỗi cho mọi thứ không phải 2xx, đọc được cả khi body không phải Problem Details (apache trả trang
HTML 502 lúc API đang khởi động lại):

```ts
// lib/api/problem.ts
import type { ProblemDetails } from './types';

export class ApiError extends Error {
  constructor(readonly status: number, readonly problem: ProblemDetails | null) {
    super(problem?.detail ?? problem?.title ?? `HTTP ${status}`);
  }
  get traceId() { return this.problem?.traceId; }
  /** Lỗi theo trường của 400 — key là tên trường client gửi (email, password, token) hoặc "body". */
  get fieldErrors(): Record<string, string[]> { return this.problem?.errors ?? {}; }
}

/** fetch ném (mất mạng, CORS sai, API tắt). Không bao giờ kích hoạt refresh. */
export class NetworkError extends Error {}

export async function toApiError(res: Response): Promise<ApiError> {
  const isProblem = (res.headers.get('content-type') ?? '').includes('json');
  const body = isProblem ? await res.json().catch(() => null) : null;
  return new ApiError(res.status, body && typeof body.status === 'number' ? (body as ProblemDetails) : null);
}
```

**Bước 4 — client.** `request` là chỗ **duy nhất** gọi `fetch` (ESLint canh). E2 viết bản chưa có interceptor; E7 chèn nhánh
401 vào đúng chỗ đánh dấu, chữ ký hàm không đổi.

```ts
// lib/api/http.ts
import { API_BASE_URL } from './config';
import { ApiError, NetworkError, toApiError } from './problem';
import { tokenStore } from '../auth/token-store';

export type RequestOptions = {
  method?: 'GET' | 'POST';
  body?: unknown;              // giữ dạng object: E7 phải gửi lại được — ReadableStream chỉ đọc một lần
  auth?: boolean;              // gắn bearer; mặc định true
  signal?: AbortSignal;
};

export async function request<T>(path: string, opts: RequestOptions = {}): Promise<T> {
  const token = opts.auth === false ? null : tokenStore.get();
  let res: Response;
  try {
    res = await fetch(`${API_BASE_URL}${path}`, {
      method: opts.method ?? 'GET',
      credentials: 'include',  // MỌI lời gọi — thiếu là cookie refresh im lặng không đi (quyết định 7)
      headers: {
        ...(opts.body !== undefined && { 'Content-Type': 'application/json' }),
        ...(token && { Authorization: `Bearer ${token}` }),
      },
      body: opts.body === undefined ? undefined : JSON.stringify(opts.body),
      signal: opts.signal,
    });
  } catch (e) {
    if ((e as Error).name === 'AbortError') throw e;
    throw new NetworkError('Không kết nối được máy chủ.');
  }
  // E7: nhánh 401 → refresh → gọi lại, chèn tại đây.
  if (!res.ok) throw await toApiError(res);
  return (res.status === 204 ? undefined : await res.json()) as T;
}
```

```ts
// lib/api/auth-api.ts — kiểu lấy từ hợp đồng: yaml đổi field là các dòng dưới đỏ compile
import { request } from './http';
import type * as T from './types';

export const authApi = {
  register:    (b: T.RegisterRequest)    => request<T.RegisterResponse>('/auth/register', { method: 'POST', body: b, auth: false }),
  verifyEmail: (b: T.VerifyEmailRequest) => request<T.VerifyEmailResponse>('/auth/verify-email', { method: 'POST', body: b, auth: false }),
  login:       (b: T.LoginRequest)       => request<T.TokenResponse>('/auth/login', { method: 'POST', body: b, auth: false }),
  /** KHÔNG body (quyết định 6) — refresh token đi trong cookie. Chỉ refresh-coordinator (E7) được gọi hàm này. */
  refresh:     ()                        => request<T.TokenResponse>('/auth/refresh', { method: 'POST', auth: false }),
  logout:      ()                        => request<void>('/auth/logout', { method: 'POST' }),
  me:          (signal?: AbortSignal)    => request<T.MeResponse>('/me', { signal }),
};
```

`POST /auth/refresh` gửi **không body và không `Content-Type`** — hợp đồng ghi "không nhận body".

**Bước 5 — token store (Đ-E2).**

```ts
// lib/auth/token-store.ts
type Listener = () => void;
let accessToken: string | null = null;        // CHỈ ở đây. Không Web Storage, không cookie, không log.
const listeners = new Set<Listener>();

export const tokenStore = {
  get: () => accessToken,
  set(next: string | null) {
    if (next === accessToken) return;
    accessToken = next;
    listeners.forEach((l) => l());
  },
  subscribe(l: Listener) { listeners.add(l); return () => listeners.delete(l); },
};
```

Không lưu `expiresIn` để tự refresh theo giờ: GĐ1 refresh **phản ứng** theo 401 (E7). Hẹn giờ refresh chủ động là thêm lời
gọi `/auth/*` (tính vào hạn mức) và thêm một nguồn race — không có yêu cầu nào đòi.

**Bước 6 — mock MSW (Đ-E7).**

> *Lịch sử — mock trình duyệt (`browser.ts`, worker, cờ trong `providers.tsx`) đã gỡ ngày 2026-09-17, xem ghi chú đổi Đ-E7.
> Phần fixture + handler + `node.ts` dưới đây vẫn đúng.*

```bash
pnpm add -D --save-exact msw
pnpm exec msw init public --save
```

Kịch bản chọn bằng **dữ liệu nhập**, không bằng cờ ẩn — ai mở màn cũng tái hiện được:

| Endpoint | Nhập | Mock trả |
|---|---|---|
| `register` | `trung@example.com` | 409 |
| `register`, `login` | `loi400@example.com` | 400 với `errors` của ví dụ `ValidationProblem` |
| mọi `/auth/*` | `quanhanh@example.com` | 429 |
| mọi `/auth/*` | `loi500@example.com` | 500 (không `detail`, có `traceId`) |
| `login` | `sai@example.com` · `chuaxacminh@example.com` · `bikhoa@example.com` | 401 · 403 · 423 |
| `login` | email khác | 200 + bật phiên giả |
| `verify-email` | token `'a'.repeat(64)` · `'b'.repeat(64)` | 410 · 400 |
| `refresh` | — | 200 + token giả mới nếu phiên giả bật, không thì 401 |
| `logout` | — | 204, tắt phiên giả |
| `me` | — | 200 ví dụ `MeResponse` nếu bearer khớp token giả hiện tại, không thì 401 |

```ts
// mocks/fixtures.ts — giá trị chép từ example của identity-v1.yaml; satisfies bắt lệch hình dạng
import type * as T from '@/lib/api/types';

export const me = {
  userId: '0192f3c1-8a4e-7c31-9f2a-6b5d4e3c2a10', email: 'an.nguyen@example.com', role: 'USER',
  roleDisplayName: 'Người dùng', emailVerifiedAt: '2026-09-08T03:14:07Z', status: 'active',
  createdAt: '2026-09-08T03:10:22Z',
} satisfies T.MeResponse;

export const problem = (status: number, title: string, detail?: string) => ({
  type: `https://httpstatuses.io/${status}`, title, status, ...(detail && { detail }),
  traceId: crypto.randomUUID().replaceAll('-', ''),
}) satisfies T.ProblemDetails;
```

Handler trả lỗi bằng `HttpResponse.json(problem(...), { status, headers: { 'Content-Type': 'application/problem+json' } })`.
Phiên giả nhớ trong `sessionStorage` (được override ESLint riêng cho `mocks/**`) để tải lại trang vẫn "còn phiên" như
cookie thật. Thêm `mockControls.expireAccessToken()` (đổi token giả hiện tại) cho Vitest của E7.

```tsx
// app/providers.tsx
'use client';
import { useEffect, useState, type ReactNode } from 'react';

const mocking = process.env.NEXT_PUBLIC_API_MOCKING === 'enabled';

export function Providers({ children }: { children: ReactNode }) {
  const [ready, setReady] = useState(!mocking);
  useEffect(() => {
    if (!mocking) return;
    import('@/mocks/browser').then(({ worker }) => worker.start({ onUnhandledRequest: 'error' })).then(() => setReady(true));
  }, []);
  return ready ? <AuthProvider>{children}</AuthProvider> : null;   // AuthProvider thêm ở E6
}
```

Phải **chờ** `worker.start` trước khi render: màn `/me` gọi refresh ngay khi mount, chạy trước worker là đi thẳng ra mạng.

**Bước 7 — job CI.** Thêm vào `.github/workflows/ci.yml` một job song song với `build-test` (giữ nếp comment ASCII của file):

```yaml
  frontend:
    runs-on: ubuntu-latest
    defaults:
      run:
        working-directory: src/frontend
    steps:
      - uses: actions/checkout@v4
      - uses: pnpm/action-setup@v4          # doc phien ban tu "packageManager" trong src/frontend/package.json
        with:
          package_json_file: src/frontend/package.json
      - uses: actions/setup-node@v4
        with:
          node-version-file: src/frontend/.nvmrc
          cache: pnpm
          cache-dependency-path: src/frontend/pnpm-lock.yaml
      - run: pnpm install --frozen-lockfile
      # CONG CODEGEN: schema.d.ts commit trong repo phai khop identity-v1.yaml hien tai.
      # Doi hop dong ma quen `pnpm gen:api` -> diff -> DO. Doi version openapi-typescript cung do: dung y.
      - name: API types khop hop dong (CI GATE)
        run: pnpm gen:api && git diff --exit-code -- lib/api/schema.d.ts
      - run: pnpm lint
      - run: pnpm typecheck
      - run: pnpm test
      - name: Build
        run: pnpm build
        env:
          NEXT_PUBLIC_API_BASE_URL: /api/v1
```

Job riêng chạy song song nên không cộng vào thời gian job .NET (NFR CI ≤ 10 phút).

### Test

| Test | Kỳ vọng |
|---|---|
| `schema.test-d.ts` | Hai assert kiểu ở bước 1 |
| `http.test.ts` — mọi hàm của `authApi` (MSW node ghi lại request) | `credentials` là `include` ở **cả 6**; `refresh` không body, không `Content-Type`; `register`/`login`/`verifyEmail` **không** gắn `Authorization` dù store có token; `me`/`logout` gắn `Bearer <token>` |
| `http.test.ts` — fetch ném `TypeError` | `NetworkError`; abort → ném lại `AbortError`, không bọc |
| `problem.test.ts` | 409 `application/problem+json` → `status 409`, `traceId` đúng; 400 → `fieldErrors.password` là mảng; 502 `text/html` → `ApiError(502, null)`, không ném khi parse |
| **Thử cho đỏ cổng codegen** (local, rồi hoàn tác) | Sửa tạm `enum` của `RoleCode` trong yaml thành thêm `ROOT` → `pnpm gen:api` → `git diff --exit-code` exit 1 **và** `schema.test-d.ts` đỏ. Khôi phục yaml, chạy lại `gen:api`, `git status` sạch. Ghi kết quả vào PR |

### Cạm bẫy đã biết

| # | Bẫy | Chặn bằng |
|---|---|---|
| 1 | Đọc `process.env[name]` động → Next không thay lúc build → `undefined` trên trình duyệt | Viết nguyên văn `process.env.NEXT_PUBLIC_API_BASE_URL` (bước 2) |
| 2 | Parse JSON cho mọi response lỗi → trang HTML 502 của apache ném `SyntaxError` giữa luồng | `toApiError` kiểm `content-type` + `.catch` |
| 3 | Gửi `body: {}` "cho chắc" ở `refresh`/`logout` → trái hợp đồng ("không nhận body"), và cổng hợp đồng phía server **không** bắt được lỗi phía client | `authApi.refresh`/`logout` không truyền `body`; `http.test.ts` khẳng định request không body, không `Content-Type` |
| 4 | MSW `onUnhandledRequest: 'bypass'` → gõ sai path mock vẫn "chạy" bằng mạng thật, lỗi lộ muộn | `'error'` |
| 5 | Prettier/ESLint sửa `schema.d.ts` → cổng codegen đỏ vĩnh viễn | Thêm file vào `.prettierignore` và `globalIgnores` của `eslint.config.mjs` |

### Thực tế thi công — 2026-09-16

Bảy bước đã chạy. Những chỗ **khác** hướng dẫn ở trên, và những thứ chỉ lộ ra khi gõ thật:

**Lệch Đ-E7 (nhóm chốt): mock cần THÊM điều kiện `NODE_ENV === 'development'`, không chỉ
`NEXT_PUBLIC_API_MOCKING`.** Gác `import('@/mocks/browser')` bằng một mình cờ mocking là **không đủ**:
Turbopack vẫn sinh ra chunk động cho nó, và `pnpm build` để lại một file 285 KB chứa cả `setupWorker`
lẫn `mockServiceWorker` trong `.next/static/chunks/`. Chunk đó không bao giờ được tải, nhưng nó vẫn
nằm trong image staging — và cổng của E8 ("không dòng nào khớp `mockServiceWorker` trong
`.next/static`") sẽ đỏ. Thêm `process.env.NODE_ENV !== "development"` ngay trước `import()` thì
bundler gấp được thành hằng false và cắt cả cây MSW; sau khi sửa, grep `.next/static` ra rỗng.
**Hệ quả chấp nhận:** mock chỉ chạy ở `next dev`, không chạy ở `pnpm build && pnpm start`. Đúng với
tinh thần "cấm nghiệm thu trên mock", nhưng là **hai** điều kiện chứ không phải một — ai đọc Đ-E7 rồi
đi tìm một cờ duy nhất sẽ không thấy.

Hai điều kiện đó phải **giống hệt nhau** ở hai chỗ trong `app/providers.tsx` (biến `mocking` dùng cho
state `ready`, và chỗ gác `import()`). Lệch nhau là trang treo ở khung trắng: `ready` đợi một worker
không bao giờ khởi động.

**`git diff --exit-code` không bắt được file CHƯA COMMIT — đã đổi cổng.** Lần thử cho đỏ đầu tiên, hợp đồng đã cắm
`ROOT` vào `RoleCode` mà cổng vẫn **xanh**, vì `schema.d.ts` lúc đó còn untracked và `git diff` chỉ so index với worktree.
Cổng giờ dùng `git status --porcelain -- lib/api/schema.d.ts` phải rỗng, bắt cả `??` lẫn ` M`. Đã kiểm bốn trạng thái
trong một repo nháp: chưa commit → đỏ · đã commit không đổi → xanh · đã commit rồi hợp đồng đổi → đỏ · khôi phục → xanh.

**Vòng đời MSW đặt ở `test/setup.ts`, không lặp trong từng file test.** `server.listen` một lần cho cả
bộ, `resetHandlers` + `events.removeAllListeners` + `mockControls.reset()` sau mỗi test. Không làm vậy
thì phiên giả của mock rò rỉ từ test này sang test khác (nó nhớ trong `sessionStorage` của jsdom).

**`mocks/session.ts` tách riêng khỏi `handlers.ts`** — phiên giả là trạng thái, handler là ánh xạ; để
chung thì ba chỗ ngoại lệ ESLint cho `sessionStorage` nằm lẫn giữa logic kịch bản.

**`problem.test.ts` dựng `new Response(...)` trực tiếp, không đi qua MSW.** `toApiError` là hàm thuần
túy nhận một `Response` — dùng MSW ở đây chỉ thêm một lớp giữa bài test và thứ đang kiểm.

**Thêm ba test ngoài bảng:** 204 trả `undefined` (không cố parse JSON), body JSON nhưng không phải
Problem Details (không nhận bừa làm `problem`), và JSON hỏng giữa chừng (không ném, chỉ mất phần
`problem`).

**`.env.example` đổi giá trị.** E1 để `NEXT_PUBLIC_API_BASE_URL=` rỗng; E2 đặt thẳng
`http://localhost:5259/api/v1` theo bước 2, kèm một câu nói rõ bỏ trống cũng ra đúng giá trị đó vì
`config.ts` đã có mặc định.

**`onUnhandledRequest: 'error'` trần trụi là sai — đã đo bằng trình duyệt thật (2026-09-17).** Lượt
`e2e/smoke.spec.ts` bật mock cho ra bốn lỗi đỏ: Next dev tự gọi `POST /__nextjs_original-stack-frames` (cùng origin, không
liên quan gì tới API) và MSW báo "intercepted a request without a matching request handler" kèm 500. Không hạ xuống
`'bypass'` — gõ sai path API sẽ lặng lẽ đi ra mạng thật. Cách ở giữa nằm trong `mocks/browser.ts`: chỉ `print.error()` cho
request bắt đầu bằng **chính gốc API**. Lỗ hổng còn lại: gõ sai chính cái gốc (sai host) thì không bắt được — chấp nhận.

**`worker.start()` bị gọi hai lần vì StrictMode.** Triệu chứng: `Failed to call "configure()" on the network: cannot
configure an already enabled network.` Chặn bằng một promise cấp module trong `mocks/browser.ts`, **không** dùng `useRef`
— StrictMode dựng lại component nên ref cũng mất (cùng cạm bẫy với Đ-E10).

**Chờ worker rồi mới render thì React cảnh báo về thẻ `<script>`.** `Providers` trả `null` trên server nên thẻ `<script>`
Next chèn vào cây RSC bị render ở phía client: `Encountered a script tag while rendering React component`. Chỉ có ở lượt
bật mock, không đụng bản production. Smoke test **bỏ qua đúng một cảnh báo này, có ghi lý do**, thay vì tắt cả phép kiểm
console. **Để lại cho E6** — `RequireAuth` cũng có trạng thái chờ, xem lại cách chờ một thể.

**Bằng chứng.**

| Cổng | Kết quả |
|---|---|
| `pnpm lint` / `typecheck` | xanh |
| `pnpm test` | **19/19** xanh (4 file), `Type Errors no errors` |
| `pnpm build` | xanh; grep `.next/static` cho `setupWorker`, `mockServiceWorker`, `localhost:5259` — **rỗng** |
| `pnpm dev` + `GET /login` | `200`, còn nguyên sau khi bọc thêm `Providers` |
| `pnpm test:e2e` (Chrome 152) | 2/2 xanh, **cả hai lượt**: không mock, và `NEXT_PUBLIC_API_MOCKING=enabled` |
| CI | job `frontend` trong `ci.yml`: 10 bước, hai cổng `CI GATE` |

Thử cho đỏ cổng codegen (sau khi `git add` file sinh):

| Cắm vào | Kết quả |
|---|---|
| `RoleCode.enum` thêm `ROOT` trong `identity-v1.yaml` | `pnpm gen:api` + `git diff --exit-code` → **exit 1** |
| cùng đột biến đó | `schema.test-d.ts` **đỏ** ở `toEqualTypeOf` |
| khôi phục yaml + `gen:api` | exit 0, `git status` sạch |

Việc siết thêm sau khi rà lại E1 + E2 (2026-09-17) — chi tiết ở mục tương ứng phía trên:

| Việc | Thử cho đỏ |
|---|---|
| Cổng codegen đổi sang `git status --porcelain` | bốn trạng thái trong repo nháp, xem trên |
| Cổng CI mới: bundle production sạch MSW | bỏ điều kiện `NODE_ENV` ở `providers.tsx` → cổng đỏ đúng một chunk; khôi phục → xanh |
| `lib/auth/token-store.test.ts` (5 ca) | bỏ `if (next === accessToken) return` → đỏ |
| `lib/api/config.test.ts` (4 ca) | bỏ nhánh `throw` → đỏ; bỏ cắt `/` cuối → đỏ |
| Ghim env của Vitest (`test.env`) | trước khi ghim, `NEXT_PUBLIC_API_BASE_URL=/api/v1 pnpm test` làm **8** test của `http.test.ts` đỏ (Node không `fetch` được đường dẫn tương đối); sau khi ghim, cả hai kiểu chạy đều xanh |

Thử cho đỏ bốn đột biến trên chính code vừa viết (từng cái một, rồi khôi phục):

| Đột biến | Test bắt |
|---|---|
| Bỏ `credentials: "include"` khỏi `http.ts` | "gắn credentials: 'include' ở CẢ SÁU lời gọi" |
| `refresh` gửi `body: {}` | "refresh KHÔNG gửi body và KHÔNG gửi Content-Type" |
| `login` quên `auth: false` | "register/login/verifyEmail KHÔNG gắn Authorization" |
| `toApiError` parse JSON bất kể content-type | 2 test: 502 text/html, và JSON hỏng giữa chừng |

---

## 4. E4 — Màn đăng nhập

> Làm **trước** E3 (xem thứ tự ở Mục 0). Số thứ tự mục giữ theo mã việc cho dễ tra.

**Mục tiêu.** Dịch mã lỗi thành thông điệp người đọc hiểu **mà không tiết lộ email có tồn tại hay không** (AC-02), và giữ access
token **chỉ trong memory** (quyết định 6).

**Kết quả mong đợi.**
- `app/(auth)/login/page.tsx` — mỏng, bọc `<Suspense>` vì đọc `useSearchParams`; form thật ở `features/auth/login-form.tsx` (`'use client'`).
- `lib/validation/auth.ts`: `validateLogin`; `lib/api/messages.ts`: bảng thông điệp dưới đây; `lib/auth/safe-next.ts`.
- Vitest: `features/auth/login-form.test.tsx` (mọi nhánh trên mock), `safe-next.test.ts`, `messages.test.ts`, `auth.test.ts`.
- Playwright `e2e/login-storage.spec.ts` trên API dev: xanh — cookie `refresh_token` có `HttpOnly`, `Secure`, `SameSite=Lax`,
  `Path=/api/v1/auth` khi đăng nhập từ `localhost:3000`. **Đây chính là mục còn treo ở checklist khối D (Mục 15)**; không chụp
  ảnh DevTools (xem "Thực tế thi công").

### Bảng thông điệp (Đ-E6)

| Status | Thông điệp | Ghi chú |
|---|---|---|
| 400 | lỗi theo trường từ `errors`; không có key trường → "Dữ liệu không hợp lệ." | |
| **401** | **"Email hoặc mật khẩu không đúng."** | **Một** câu cho cả hai trường hợp. Không thêm "Bạn đã nhập sai lần thứ N" — FE không biết, và đoán là lộ thông tin |
| 403 | "Tài khoản chưa xác minh email. Vui lòng mở liên kết trong thư chúng tôi đã gửi." | GĐ1 không có endpoint gửi lại mail — **không** hiện nút "Gửi lại" |
| 423 | "Tài khoản tạm khóa do đăng nhập sai nhiều lần. Vui lòng thử lại sau 15 phút." | Không đếm ngược: FE không biết `locked_until` |
| 429 | "Bạn thao tác quá nhanh. Vui lòng thử lại sau ít phút." | |
| 500 | "Đã xảy ra lỗi không mong muốn. Mã tra cứu: `<traceId>`" | |
| `NetworkError` | "Không kết nối được máy chủ." | |

### Các bước

1. **Form:** `noValidate` (thông điệp nhất quán, không để trình duyệt tự hiện câu tiếng Anh); `autoComplete="email"` và
   `"current-password"`; nút gửi `disabled` + `Spinner` khi đang chờ — chặn gửi đôi, cũng là chặn tốn hạn mức 10/phút.
2. **Kiểm client** theo Đ-E5 (đăng nhập **không** có tối thiểu 8). Có lỗi client thì không gọi API.
3. **Gọi `authApi.login`** → 200: `tokenStore.set(accessToken)` → `router.replace(safeNext(searchParams.get('next')))`.
   **Không** xóa mật khẩu khỏi state sau lỗi 401 — người dùng sửa một ký tự thì tiện hơn gõ lại; xóa khỏi state khi rời màn.
4. **Lỗi:** 400 gắn lỗi dưới trường; mọi mã khác hiện `FormAlert` (kit `Alert variant="destructive"`, E1 bước 4) trên form, focus vào nó.

```ts
// lib/auth/safe-next.ts — chặn open redirect: ?next=https://evil.example hoặc //evil.example
export function safeNext(next: string | null, fallback = '/me'): string {
  if (!next || !next.startsWith('/') || next.startsWith('//') || next.startsWith('/\\')) return fallback;
  return next;
}
```

**Không đụng token ở chỗ nào khác ngoài `tokenStore.set`.** Không `console.log(res)`, không đưa token vào URL, không lưu vào
state của form.

### Test

| Test | Kỳ vọng |
|---|---|
| Vitest — `sai@example.com` (401) | đúng câu "Email hoặc mật khẩu không đúng."; `tokenStore.get()` vẫn `null`; **không** có request `/auth/refresh` (E7 sẽ giữ test này xanh) |
| Vitest — 403 / 423 / 429 / 500 / mất mạng | mỗi nhánh đúng câu ở bảng; 500 có `traceId` của fixture |
| Vitest — 400 từ `loi400@example.com` | lỗi hiện dưới đúng trường `email`, `password` |
| Vitest — mật khẩu 73 byte (`'ệ'.repeat(25)` = 75 byte) | lỗi client "Mật khẩu tối đa 72 byte.", **0** request |
| Vitest — 200 với `?next=/me` và `?next=//evil.example` | `router.replace('/me')` cả hai lần |
| Playwright `login-storage.spec.ts` (API dev, tài khoản đã xác minh) | sau đăng nhập: `localStorage.length === 0`, `sessionStorage.length === 0`, `document.cookie` không chứa `refresh_token`; cookie jar của context (`context.cookies()`) **có** `refresh_token` với `httpOnly: true`, `sameSite: 'Lax'`, `path: '/api/v1/auth'` |

### Cạm bẫy đã biết

- **`useSearchParams` không bọc `<Suspense>`** → `next build` báo lỗi/cảnh báo "should be wrapped in a suspense boundary" và cả
  trang bị render phía client. Bọc ngay từ đầu.
- **Hiện `problem.detail` thay vì bảng** → hôm nay khớp, nhưng chỉ cần server sửa một chữ ở nhánh "email không tồn tại" là AC-02
  thủng ở FE mà không test backend nào bắt. Bảng do FE giữ.
- **Tài khoản thử bị khóa 15 phút** sau 5 lần sai trên API dev — thử 423 bằng một tài khoản riêng; nhánh 423 của màn đã có Vitest trên `msw/node`.
- **Safari** không nhận cookie `Secure` trên `http://localhost` như Chrome/Firefox → refresh luôn 401 trên Safari dev. Kiểm ở
  Chrome/Firefox; Safari kiểm ở staging (HTTPS).

### Thực tế thi công — 2026-09-17

Bốn bước đã chạy. Những chỗ **khác** hướng dẫn ở trên, và những thứ chỉ lộ ra khi gõ thật:

**Lệch mẫu `safeNext` ở bước 4 (nhóm chốt): kiểm bằng bộ phân tích URL, không bằng tiền tố.** Mẫu chỉ chặn `//` và `/\`.
Trình duyệt **bỏ tab và xuống dòng** trong URL trước khi phân tích, nên `?next=/%09/evil.example` qua được cả ba phép kiểm
tiền tố rồi thành `//evil.example` khi điều hướng. `lib/auth/safe-next.ts` giờ giải `next` trên một origin giả
(`http://safe-next.invalid`) và chỉ nhận khi origin không đổi; trả lại `pathname + search + hash` đã chuẩn hóa. Test có
ca `/\t/…` và `/\n/…` — bỏ phép so origin thì bốn ca đỏ.

**Tên file test theo kebab-case: `features/auth/login-form.test.tsx`, không phải `LoginForm.test.tsx`.** Khớp tên file nguồn
và nếp đã có (`components/form/text-field.test.tsx`). E3/E5 theo cùng nếp: `register-form.test.tsx`, `verify-email.test.tsx`.

**`lib/api/messages.ts` có hai hàm, không phải một.** `errorMessage(context, error)` như Đ-E6 cho lỗi cấp form; thêm
`validationErrors(error, fields)` tách 400 thành lỗi theo trường **và** một `formMessage` khi còn key không thuộc màn
(`body`, key lạ) hoặc 400 không có `errors` — để không lỗi nào biến mất lặng lẽ. `ErrorContext` hiện chỉ có `"login"`; E3 thêm
`"register"`, E5 thêm `"verify-email"`. Status không có trong bảng thì mới dùng `detail` của server làm dự phòng.

**`components/form/form-alert.tsx` dựng ở E4** (đã hẹn ở "Thực tế thi công" của E1). Nó tự chuyển focus vào chính nó mỗi khi
thông điệp đổi. Hệ quả phải biết: **cùng một lỗi hai lần liên tiếp** thì thông điệp không đổi và focus không quay lại — form
xóa lỗi cũ (`setFormError(null)`) ngay khi bấm gửi để lần sau vẫn là `null → câu`. Có test riêng; bỏ dòng xóa là đỏ.

**`lib/validation/auth.ts` và `auth.test.ts` mở ở E4** với `validateLogin` + `utf8ByteLength`; E3 thêm `validateRegister` và
bảng ngưỡng đăng ký vào cùng hai file. Email gửi lên **nguyên văn người dùng gõ** — `LoginService` đã `Trim()`; client chỉ trim
để đo độ dài (nới hơn hoặc bằng server).

**Khung chờ của `<Suspense>` là ba `Skeleton` cùng dáng form.** `next build` vẫn prerender `/login` là trang tĩnh (`○`): Card
có trong HTML đầu, chỉ form đợi hydrate. Không có cảnh báo `useSearchParams` nào.

**Sửa lỗi theo trường thì lỗi đó biến mất ngay** khi gõ lại trường ấy — không chờ lần gửi sau. Không có trong hướng dẫn; thêm
vì để câu "Email là bắt buộc." đứng cạnh một email đã gõ đủ là giao diện nói sai.

**`/me` chưa tồn tại tới E6.** Đăng nhập thành công ở E4 điều hướng tới trang 404 của Next — đúng URL, chưa đúng màn.
`login-storage.spec.ts` chỉ khẳng định URL.

**`e2e/login-storage.spec.ts` tự dựng tài khoản đã xác minh**, không đòi tài khoản cố định: `POST /auth/register` → đọc link
qua API REST của Mailpit (`/api/v1/search`, `/api/v1/message/{ID}`) → `POST /auth/verify-email` → đăng nhập trên UI. Tốn 3
lượt trong hạn mức 10/phút. Gốc API và Mailpit đổi được bằng `PLAYWRIGHT_API_URL`, `PLAYWRIGHT_MAILPIT_URL`. Bật mock thì
spec **tự bỏ qua** (mock không đặt cookie). Spec khẳng định thêm `secure: true` ngoài ba thuộc tính của bảng Test.

**Lệch kế hoạch E4 và checklist khối D Mục 15 (2026-09-17, nhóm chốt): bỏ ảnh chụp DevTools, bằng chứng là
`login-storage.spec.ts`.** Ảnh chụp chỉ cho thấy một lần nhìn bằng mắt; spec kiểm **cùng** các thuộc tính bằng máy và chạy lại
được. Cụ thể: `context.cookies()` thấy `refresh_token` với `httpOnly`, `secure`, `sameSite: 'Lax'`, `path: '/api/v1/auth'`;
`document.cookie` không đọc được nó. Preflight: `POST /auth/login` từ `localhost:3000` sang `localhost:5259` với
`Content-Type: application/json` + `credentials: 'include'` bắt buộc qua preflight — trang chỉ sang được `/me` khi JS **đọc
được** response, tức CORS có `Allow-Credentials` đã đúng. Đã sửa cùng lúc Mục 11 ở đây và D4 + Mục 15 của hướng dẫn khối D.

**Bằng chứng.**

| Cổng | Kết quả |
|---|---|
| `pnpm lint` / `typecheck` | xanh |
| `pnpm test` | **67/67** xanh (10 file; trước E4 là 28/28), `Type Errors no errors` |
| `pnpm build` (`NEXT_PUBLIC_API_BASE_URL=/api/v1`) | xanh, `/login` prerender tĩnh; grep `.next/static` cho `setupWorker`, `mockServiceWorker`, `localhost:5259` — **rỗng** |
| `pnpm test:e2e` (Chrome 152.0.7977.84), API dev thật | **3/3** xanh |
| `pnpm test:e2e` với `NEXT_PUBLIC_API_MOCKING=enabled` | 2 xanh, 1 bỏ qua (`login-storage`, có chủ đích) |

Thử cho đỏ (từng đột biến một, rồi khôi phục — `git status` như trước):

| Đột biến | Test bắt |
|---|---|
| 401 hiện `detail` của server trước bảng | `messages.test.ts` "401 luôn ra ĐÚNG một câu"; `login-form` ca 403 |
| Bỏ `return` khi có lỗi client | `login-form` "75 byte: 0 request", "để trống cả hai: 0 request" |
| `safeNext` bỏ phép so origin | `safe-next.test.ts` 4 ca `//`, `/\`, `/\t/`, `/\n/` |
| Form bỏ qua `safeNext` | `login-form` `?next=//evil.example`, `?next=https://evil.example/` |
| Đếm ký tự thay vì byte | `auth.test.ts` 2 ca; `login-form` "75 byte" |
| Thêm tối thiểu 8 cho đăng nhập | `auth.test.ts` 3 ca; `login-form` "mật khẩu 1 ký tự KHÔNG bị chặn" |
| Không xóa lỗi cũ khi gửi lại | `login-form` "cùng một lỗi hai lần liên tiếp" |
| `FormAlert` không chuyển focus | `login-form` 401, "hai lần liên tiếp" |
| Nút không `disabled` khi chờ | `login-form` "bấm thêm không gửi lần hai" |
| 400 đẩy hết lên lỗi cấp form | `login-form` "400 từ server: lỗi dưới đúng trường" |
| **E2E:** form ghi token vào `sessionStorage` | `login-storage.spec.ts` — `Expected: 0, Received: 1` |

---

## 5. E3 — Màn đăng ký

**Mục tiêu.** FR-001 phía người dùng, với validation client **khớp** validator server — chặt bằng ở mật khẩu, không chặt hơn ở
email (Đ-E5) — để người dùng không bị server từ chối vì điều client đã có thể nói trước, và không bị client chặn vì điều server
chấp nhận.

**Kết quả mong đợi.**
- `app/(auth)/register/page.tsx`, `app/(auth)/register/check-email/page.tsx`; form ở `features/auth/register-form.tsx`.
- `validateRegister` trong `lib/validation/auth.ts` + `auth.test.ts` (bảng ngưỡng).
- Vitest `RegisterForm.test.tsx` xanh cho 201 / 400 / 409 / 429 / 500 / mất mạng.
- Kiểm tay trên API dev: đăng ký → mail hiện trong Mailpit, link dạng `http://localhost:3000/verify-email?token=<64 hex>`.

### Các bước

1. **Form:** email (`autoComplete="email"`), mật khẩu (`autoComplete="new-password"`, gợi ý "8–72 ký tự" bằng `description` của `TextField`). Không có
   ô "nhập lại mật khẩu" — hợp đồng không có, và nút "Hiện mật khẩu" giải quyết cùng vấn đề rẻ hơn.
2. **Kiểm client:**

```ts
// lib/validation/auth.ts
const encoder = new TextEncoder();
export const MAX_EMAIL = 254, MIN_PASSWORD = 8, MAX_PASSWORD_BYTES = 72;   // RegisterRequestValidator

export function passwordError(pw: string, { requireMin }: { requireMin: boolean }): string | undefined {
  if (pw.length === 0) return 'Mật khẩu là bắt buộc.';
  if (requireMin && pw.length < MIN_PASSWORD) return 'Mật khẩu phải có ít nhất 8 ký tự.';
  if (encoder.encode(pw).length > MAX_PASSWORD_BYTES) return 'Mật khẩu tối đa 72 byte.';
}

/** NỚI hơn MailAddress của server (Đ-E5) — chỉ bắt lỗi chắc chắn sai; phần còn lại để server nói. */
export function registerEmailError(raw: string): string | undefined {
  const email = raw.trim();
  if (email.length === 0) return 'Email là bắt buộc.';
  if (email.length > MAX_EMAIL) return 'Email tối đa 254 ký tự.';
  const at = email.indexOf('@');
  if (at <= 0 || at !== email.lastIndexOf('@') || at === email.length - 1 || /[\s<>]/.test(email))
    return 'Email không đúng định dạng.';
}
```

3. **Gửi** `{ email: email.trim(), password }`. 201 → ghi email vào một biến module `pendingEmail` (memory, không URL) →
   `router.push('/register/check-email')`.
4. **Màn kiểm tra hộp thư:** "Chúng tôi đã gửi liên kết xác minh tới `<pendingEmail>`" — tải lại trang mất biến thì hiện câu
   chung không có email. **Không** đặt email vào query string: nó nằm lại trong lịch sử trình duyệt và log truy cập của
   apache (PII). Nêu rõ liên kết hết hạn sau 24 giờ và nhắc kiểm thư mục spam.
5. **Lỗi:**

| Status | Hiển thị |
|---|---|
| 400 | lỗi dưới trường theo `errors` |
| 409 | dưới trường email: "Email này đã được đăng ký." + link "Đăng nhập" (`/login`). Đây là ngoại lệ có ý thức của hợp đồng — không che đi |
| 429 / 500 / mất mạng | như bảng E4 |

### Test — `auth.test.ts` (bảng ngưỡng, viết tay số liệu, không tính từ hằng số)

| Đầu vào | `requireMin` | Kỳ vọng |
|---|---|---|
| `'a'.repeat(7)` | true | "Mật khẩu phải có ít nhất 8 ký tự." |
| `'a'.repeat(8)` | true | hợp lệ |
| `'a'.repeat(72)` | true | hợp lệ |
| `'a'.repeat(73)` | true | "Mật khẩu tối đa 72 byte." |
| `'ệ'.repeat(24)` (72 byte) / `'ệ'.repeat(25)` (75 byte) | true | hợp lệ / "tối đa 72 byte" |
| `'mậtkhẩu'` (7 ký tự) | false | hợp lệ — đăng nhập không có tối thiểu |
| email `'an@example.com'`, `' An@Example.com '` | — | hợp lệ (trim) |
| email `'an.example.com'`, `'@x.com'`, `'a@'`, `'a@@b.com'`, `'Tên <a@b.com>'` | — | "Email không đúng định dạng." |

Kèm một test **đối chiếu với server** chạy tay một lần (không tự động): gửi `'ệ'.repeat(25)` tới API dev → 400
`errors.password` đúng câu client. Ghi vào PR — đó là bằng chứng "khớp đúng validator server".

### Cạm bẫy đã biết

- **Đếm ký tự cho trần 72** → mật khẩu tiếng Việt 40 ký tự qua client, server trả 400. Đếm **byte**.
- **Regex email chặt** (kiểu `^[a-z0-9._%+-]+@…$`) → chặn địa chỉ hợp lệ có ký tự `'` hay tên miền quốc tế; người dùng không có
  cách nào vượt qua. Đ-E5.
- **Bấm "Đăng ký" hai lần** → request thứ hai nhận 409 sau khi request thứ nhất đã 201, màn hiện lỗi cho một tài khoản vừa tạo
  thành công. `disabled` khi đang gửi chặn gửi đôi.

### Thực tế thi công — 2026-09-17

Năm bước đã chạy. Những chỗ **khác** hướng dẫn ở trên, và những thứ chỉ lộ ra khi gõ thật:

**Lệch mẫu `passwordError` ở bước 2: "bắt buộc" kiểm bằng `trim() === ""`, không bằng `length === 0`.** `NotEmpty()` của
FluentValidation coi chuỗi toàn khoảng trắng là rỗng — đã gửi 8 dấu cách tới API dev, server trả "Mật khẩu là bắt buộc.",
không phải lỗi độ dài. Giá trị gửi đi vẫn **không** trim. `validateLogin` giờ gọi lại `passwordError(…, { requireMin: false })`
thay vì tự kiểm — một chỗ cho ngưỡng 72 byte. Hằng số tên `MIN_PASSWORD_LENGTH` (cạnh `MAX_EMAIL_LENGTH` đã có từ E4), không
phải `MIN_PASSWORD`.

**Gợi ý dưới ô mật khẩu là "Ít nhất 8 ký tự, tối đa 72 byte — chữ có dấu tính 2–3 byte.", không phải "8–72 ký tự".** Câu
"8–72 ký tự" nói sai với đúng người mà trần byte nhắm tới: mật khẩu tiếng Việt 40 ký tự bị chặn trong khi gợi ý bảo 72.

**Nút "Hiện mật khẩu" (bước 1) cần một component kit mới: `input-group`**, thêm bằng `pnpm exec shadcn add input-group` — CLI
kéo theo `textarea` (phụ thuộc của `InputGroupTextarea`), để nguyên vì GĐ2 cần cho bài viết. `TextField` nhận thêm prop
`inputEnd`: có thì ô nhập nằm trong `InputGroup`, không thì như cũ — `/login` không đổi DOM. Composite mới
`components/form/password-field.tsx` gói `TextField` + nút (`type="button"`, `aria-pressed`, nhãn cố định "Hiện mật khẩu").
Màn đăng nhập **chưa** dùng nó. Playwright phải `getByLabel("Mật khẩu", { exact: true })` trên `/register` — không `exact` thì
khớp cả nhãn của nút.

**409 dưới trường email kèm link: `TextField.error` nhận `ReactNode`, không chỉ chuỗi.** `FieldError` của kit render
`children` nếu có, nên chuỗi cũ vẫn đi đúng đường. Câu lấy từ `errorMessage("register", …)` — bảng `messages.ts` có context
`"register"` với duy nhất 409; 429/500/mất mạng rơi về nhánh chung như E4. Link nằm trong `aria-describedby` nên trình đọc màn
hình đọc "Email này đã được đăng ký. Đăng nhập".

**`pendingEmail` ở `features/auth/pending-email.ts`, đọc qua `useSyncExternalStore` với snapshot server `null`.** Đọc thẳng
biến module trong component là lệch HTML khi hydrate (server không có biến; client có sau `router.push`). Có test
`renderToString` — thay snapshot server bằng `get` là đỏ. Màn kiểm tra hộp thư ráp ở `features/auth/check-email.tsx`.

**Thêm link qua lại** "Chưa có tài khoản? Đăng ký" ở chân Card `/login` và "Đã có tài khoản? Đăng nhập" ở `/register` — hướng
dẫn không nói, nhưng thiếu thì không có đường vào `/register` ngoài gõ URL.

**Tên file test theo kebab-case** như đã chốt ở E4: `register-form.test.tsx`, `check-email.test.tsx`,
`password-field.test.tsx`.

**Đối chiếu server chạy tay (API dev, 2026-09-17)** — mỗi ca trả 400 với đúng câu client hiện:

| Gửi `POST /auth/register` | `errors` của server |
|---|---|
| mật khẩu `'ệ'.repeat(25)` (75 byte) | `password: ["Mật khẩu tối đa 72 byte."]` |
| mật khẩu `'a'.repeat(7)` | `password: ["Mật khẩu phải có ít nhất 8 ký tự."]` |
| mật khẩu 8 dấu cách | `password: ["Mật khẩu là bắt buộc."]` |
| email `Tên <a@b.com>` | `email: ["Email không đúng định dạng."]` |

**`e2e/register.spec.ts` trên API dev** thay cho kiểm tay mail: `/login` → link "Đăng ký" → điền, bấm "Hiện mật khẩu" → 201 →
`/register/check-email` hiện email, URL **không** chứa email → link trong Mailpit khớp
`http://localhost:3000/verify-email?token=<64 hex>` → đăng ký lại cùng email → 409 dưới trường, bấm link → `/login`. Tốn 2
lượt trong hạn mức 10/phút.

**Bằng chứng.**

| Cổng | Kết quả |
|---|---|
| `pnpm lint` / `typecheck` | xanh |
| `pnpm test` | **109/109** xanh (13 file; trước E3 là 67/67), `Type Errors no errors` |
| `pnpm build` (`NEXT_PUBLIC_API_BASE_URL=/api/v1`) | xanh; `/register`, `/register/check-email` prerender tĩnh (`○`); grep `.next/static` cho `setupWorker`, `mockServiceWorker`, `localhost:5259` — **rỗng** |
| `pnpm test:e2e` (Chrome 152.0.7977.84), API dev thật | **4/4** xanh |

Thử cho đỏ (từng đột biến một, rồi khôi phục — `git status` như trước):

| Đột biến | Test bắt |
|---|---|
| Đăng ký gọi `requireMin: false` | `auth.test.ts` `validateRegister`; `register-form` "mật khẩu aaaaaaa: 0 request" |
| Đếm ký tự thay vì byte | `auth.test.ts` 3 ca (có 40 ký tự = 100 byte); `login-form` + `register-form` ca 75 byte |
| Regex email chặt `^[a-z0-9._%+-]+@…$` | `auth.test.ts` `o'brien@…`, `an@ví-dụ.vn`; `register-form` "email lạ thì để server nói" |
| Bỏ `return` khi có lỗi client | `register-form` 3 ca "0 request" |
| Email vào query string của `/register/check-email` | `register-form` "201 … KHÔNG kèm email" |
| Không ghi `pendingEmail` sau 201 | `register-form` "201" |
| 409 đẩy lên lỗi cấp form | `register-form` "409: dưới trường email" |
| Nút không `disabled` khi chờ | `register-form` "bấm thêm không gửi lần hai" |
| Bỏ `if (pending) return` | `register-form` "form bị gửi lần hai KHÔNG qua nút" — **lần chạy đầu sống sót** (nút `disabled` đã chặn click), thêm test gửi thẳng `submit` |
| Snapshot server đọc biến module | `check-email` "HTML phía server không chứa email" |
| Bảng `messages.ts` thiếu 409 đăng ký | `messages.test.ts` "409 theo bảng" |
| Nút "Hiện mật khẩu" là `type="submit"` | `password-field` "không gửi form" |
| `TextField` chỉ nhận lỗi dạng chuỗi | `text-field` "lỗi có link"; `register-form` 409 |
| Không `trim` email trước khi gửi | **không test nào bắt — đột biến tương đương:** `input type="email"` tự bỏ khoảng trắng hai đầu theo chuẩn HTML (jsdom cũng thế). Giữ `trim()` làm lớp dự phòng |

---

## 6. E5 — Màn xác minh email

**Mục tiêu.** Khép vòng đăng ký từ link trong mail — và phân biệt 400 ("liên kết không hợp lệ") với 410 ("đã hết hiệu lực")
để người dùng biết nên làm gì tiếp.

**Kết quả mong đợi.**
- `app/(auth)/verify-email/page.tsx` (bọc `<Suspense>`), `features/auth/verify-email.tsx`, `lib/auth/verify-once.ts`.
- Bốn trạng thái có giao diện: đang xác minh · thành công · 400 · 410 (+ 429/500/mất mạng dùng bảng E4).
- Vitest `VerifyEmail.test.tsx` xanh, gồm test **StrictMode chỉ một request**.
- Kiểm tay dev: đăng ký ở E3 → mở link từ Mailpit → "thành công" → đăng nhập được (trước đó đăng nhập trả 403).

### Các bước

1. Đọc `token` từ `useSearchParams`. Không đúng `/^[0-9a-f]{64}$/` → trạng thái 400 ngay, **không gọi API** (Đ-E5).
2. Gọi qua `verifyOnce` — gộp theo token ở cấp module, sống qua lần dựng lại của StrictMode (Đ-E10):

```ts
// lib/auth/verify-once.ts
import { authApi } from '@/lib/api/auth-api';
import type { VerifyEmailResponse } from '@/lib/api/types';

const inflight = new Map<string, Promise<VerifyEmailResponse>>();

/** StrictMode chạy effect 2 lần: lần 2 gửi token đã tiêu thụ → 410 → báo "hết hiệu lực" dù vừa thành công. */
export function verifyOnce(token: string) {
  let p = inflight.get(token);
  if (!p) { p = authApi.verifyEmail({ token }); inflight.set(token, p); }
  return p;
}
```

3. Có kết quả → `router.replace('/verify-email')` (xóa token khỏi URL), giữ trạng thái trong state.
4. Nội dung từng trạng thái:

| Trạng thái | Nội dung | Hành động |
|---|---|---|
| Đang xác minh | "Đang xác minh email…" | — |
| 200 | "Email `<email>` đã được xác minh." (email lấy từ **response**) | nút "Đăng nhập" → `/login` |
| 400 | "Liên kết xác minh không hợp lệ. Hãy mở lại đúng liên kết trong thư, không sao chép thiếu ký tự." | link "Đăng ký" |
| 410 | "Liên kết xác minh đã hết hạn hoặc đã được sử dụng. Nếu bạn đã xác minh trước đó, hãy đăng nhập." | nút "Đăng nhập" + link "Đăng ký" — **không** có nút "Gửi lại mail" (GĐ1 chưa có endpoint) |

   410 có hai nghĩa (hết hạn **hoặc** đã dùng) nên câu phải đúng cho cả hai — người vừa bấm link lần hai trong ngày là trường hợp
   phổ biến nhất, và họ **đăng nhập được**.

### Test

| Test | Kỳ vọng |
|---|---|
| `render(<StrictMode><VerifyEmailPage/></StrictMode>)` với token hợp lệ, đếm request trong MSW | **đúng 1** `POST /auth/verify-email`; hiện thành công, không hiện 410 |
| token `'a'.repeat(64)` | 410 đúng câu; có nút "Đăng nhập"; không có chữ "Gửi lại" |
| token `'b'.repeat(64)` | 400 đúng câu |
| không có `token` / `'XYZ'` / 64 ký tự in hoa | 400, **0** request |
| sau kết quả | `router.replace` được gọi với `/verify-email` (không query) |

### Cạm bẫy đã biết

- **Chặn gọi đôi bằng `useRef`** → StrictMode dựng lại component, ref mới là `false` → vẫn 2 request. Map cấp module.
- **Gọi API trong Server Component** để "khỏi nháy" → token đi qua server Next, và trên staging Next server gọi `/api/v1` tương
  đối không có host. Đ-E11: gọi API chỉ ở client.
- **Dev: API chạy với `Frontend__BaseUrl` của staging** (vd lỡ đặt biến trong shell) → link trong Mailpit trỏ
  `https://mxh.banhgao.net`, token lại nằm trong DB local → 400. Không đặt biến đó ở dev (Đ-D9).

### Thực tế thi công — 2026-09-17

Bốn bước đã chạy. Những chỗ **khác** hướng dẫn ở trên, và những thứ chỉ lộ ra khi gõ thật:

**Lệch bước 3 (nhóm chốt): chỉ xóa token khỏi URL khi có kết quả CUỐI (200 / 400 / 410).** 429, 5xx và mất mạng **giữ**
`?token=` và hiện nút "Thử lại" — xóa đi thì cả "Thử lại" lẫn tải lại trang đều không còn gì để gửi, người dùng phải quay
về mail. Để "Thử lại" gửi được request mới, `verifyOnce` **bỏ promise hỏng khỏi `Map`** khi lỗi không phải 400/410; 200,
400, 410 vẫn nhớ suốt đời tab (gọi lại token đã tiêu thụ chỉ ra 410). Token sai dạng bị chặn ở client cũng **không**
`router.replace` — không có kết quả nào từ server để giữ, và `setState` trong effect chỉ để đổi URL là thừa.

**Câu 400 và 410 nằm trong bảng `messages.ts` (context `"verify-email"`), không viết cứng trong màn.** Server có **hai** kiểu
400: validator (có `errors.token`) và token đúng dạng nhưng không tồn tại (`IdentityErrors.VerifyTokenInvalid`, không có
`errors`) — màn gộp cả hai vào một câu. Trạng thái 400 của token sai dạng hỏi bảng bằng `new ApiError(400, null)` để một lỗi
không có hai câu.

**Sau `router.replace` URL không còn token — trạng thái phải sống trong state, không tính lại từ URL.** Render: có kết quả thì
hiện kết quả; chưa có mà token sai dạng thì 400; còn lại "Đang xác minh…". Effect có cờ `ignore` ở cleanup nên dưới
StrictMode chỉ lần chạy thứ hai cập nhật state và gọi `replace` — test đếm `replace` đúng **một** lần (bỏ cờ là đỏ).

**Bẫy mock khi test: `useRouter` phải trả CÙNG một object.** `router` nằm trong deps của effect; mock `() => ({ replace })`
tạo object mới mỗi render → effect chạy lại sau mỗi render → `replace` hai lần và lỗi tạm thời tự thử lại. Next thật trả
router ổn định. Test của E4/E3 không có effect nên không lộ.

**`isVerifyToken` ở `lib/validation/auth.ts`** (cạnh các hàm Đ-E5 khác), chữ ký `token is string`. `$` của JS không cờ `m`
không khớp trước `\n` cuối chuỗi — có ca test riêng, khớp chú thích bên `VerifyEmailRequestValidator`.

**Mock 400 sửa câu cho đúng server:** `mocks/handlers.ts` ghi "Liên kết không hợp lệ.", server thật là "Liên kết xác minh
không hợp lệ.". Không ảnh hưởng giao diện (màn dùng bảng), nhưng fixture phải chép đúng.

**Tên file theo kebab-case:** `features/auth/verify-email.test.tsx`, `lib/auth/verify-once.test.ts`.

**`e2e/mailpit.ts` gom helper Mailpit** (E6 đổi tên thành `e2e/dev-api.ts`)
(`API`, `MAILPIT`, `emailMoi`, `linkXacMinh`) đang lặp ở `login-storage.spec.ts` và
`register.spec.ts`; hai spec cũ đổi sang dùng nó. Không khớp `testMatch` của Playwright nên không bị chạy như spec.

**`e2e/verify-email.spec.ts` trên `pnpm dev`** (App Router bật StrictMode — lượt thật của Đ-E10): đăng ký qua API → mở link
lấy từ Mailpit → đúng **1** `POST /auth/verify-email` (chỉ đếm POST, bỏ preflight), chữ "đã được xác minh", URL còn
`/verify-email` → mở lại **cùng** link → 410 đúng câu, không có "Gửi lại" → bấm "Đăng nhập" → đăng nhập UI 200. Tốn 4 lượt
`/auth/*`; cả bộ Playwright là 9 lượt, vừa dưới hạn mức 10/phút. **Bỏ** bước "trước khi xác minh đăng nhập trả 403" của kiểm
tay — thêm một lượt là cả bộ chạm 10/phút, và 403 đã có integration test ở khối D cùng Vitest ở E4.

**Bằng chứng.**

| Cổng | Kết quả |
|---|---|
| `pnpm lint` / `typecheck` | xanh |
| `pnpm test` | **136/136** xanh (15 file; trước E5 là 109/109), `Type Errors no errors` |
| `pnpm build` (`NEXT_PUBLIC_API_BASE_URL=/api/v1`) | xanh; `/verify-email` prerender tĩnh (`○`), không cảnh báo `useSearchParams`; grep `.next/static` cho `setupWorker`, `mockServiceWorker`, `localhost:5259` — **rỗng** |
| `pnpm test:e2e` (Chrome 152.0.7977.84), API dev thật | **5/5** xanh |

Thử cho đỏ (từng đột biến một, rồi khôi phục — `git status` như trước):

| Đột biến | Test bắt |
|---|---|
| `verifyOnce` không gộp | `verify-once` 2 ca; `verify-email` "StrictMode: ĐÚNG 1 POST", "URL đổi sau replace", "500 … Thử lại" |
| Bỏ cờ `ignore` ở nhánh thành công | `verify-email` "router.replace đúng một lần" |
| Không chặn token sai dạng trước khi gọi API | `verify-email` 4 ca "→ 400, 0 request" |
| Token nhận cả hex in hoa (cờ `i`) | `auth.test.ts` "in hoa"; `verify-email` "64 ký tự in hoa" |
| Không `router.replace` sau thành công | `verify-email` "router.replace", "500 … Thử lại" |
| Lỗi tạm thời cũng xóa token khỏi URL | `verify-email` 500, 429, mất mạng |
| Lỗi tạm thời bị nhớ mãi | `verify-once` "mất mạng … request mới"; `verify-email` "500 … Thử lại" |
| 410 hiện như 400 | `verify-email` "410: đúng câu" |
| Bảng thiếu 410 `verify-email` | `messages.test.ts`; `verify-email` "410" |
| "Thử lại" không chạy lại effect | `verify-email` "500 … Thử lại" |
| 400/410 không được nhớ | `verify-once` "410 là kết quả cuối"; `verify-email` "500 … Thử lại" |
| **E2E:** `verifyOnce` không gộp, trên `pnpm dev` | `verify-email.spec.ts` — lần chạy thứ hai của StrictMode nhận 410, không thấy "đã được xác minh" |

---

## 7. E6 — App shell + route guard + trang `/me`

**Mục tiêu.** Có khu vực cần đăng nhập để interceptor E7 có chỗ chứng minh tác dụng — và khuôn "trang có bảo vệ" mà mọi trang
GĐ2–GĐ8 đặt vào (`(app)/…`) mà không phải nghĩ lại cách chặn.

**Kết quả mong đợi.**
- Token store mở rộng thành **phiên**: `status: 'unknown' | 'authenticated' | 'anonymous' | 'error'` cạnh `token`.
- `lib/auth/session.ts` (nối coordinator E7 với `authApi` và `http.ts`), `auth-context.tsx` (`useSession`),
  `require-auth.tsx`; `components/shell/app-header.tsx` (tên ứng dụng + nút "Đăng xuất") dùng ở `app/(app)/layout.tsx`,
  `app/(app)/me/page.tsx`,
  `app/page.tsx` → `redirect('/me')`.
- Vitest `RequireAuth.test.tsx` xanh; Playwright `e2e/guard.spec.ts` xanh trên API dev.

### Các bước

**Bước 1 — trạng thái phiên.** Mở rộng `token-store.ts` (E2) thay vì thêm store thứ hai — hai store là hai nguồn sự thật:

| Chuyển | Khi |
|---|---|
| `unknown` → `authenticated` | refresh khởi động thành công, hoặc đăng nhập 200 |
| `unknown` → `anonymous` | refresh khởi động 401 |
| `unknown` → `error` | refresh khởi động 429 / 500 / mất mạng — **không** đẩy về `/login`: phiên có thể vẫn còn, đẩy đi là bắt đăng nhập lại oan |
| `authenticated` → `anonymous` | đăng xuất, refresh 401 (E7), nhận `logout` từ tab khác (E7) |

**Bước 2 — guard (Đ-E3).**

```tsx
// lib/auth/require-auth.tsx
'use client';
export function RequireAuth({ children }: { children: ReactNode }) {
  const { status } = useSession();
  const router = useRouter();
  const pathname = usePathname();

  useEffect(() => { if (status === 'unknown') void bootstrapSession(); }, [status]);   // gộp bởi coordinator — StrictMode vẫn 1 request
  useEffect(() => {
    if (status === 'anonymous') router.replace(`/login?next=${encodeURIComponent(pathname)}`);
  }, [status, pathname, router]);

  if (status === 'authenticated') return <>{children}</>;
  if (status === 'error') return <SessionError onRetry={bootstrapSession} />;   // "Không kiểm tra được phiên đăng nhập." + nút Thử lại
  return <PageSkeleton />;                                                       // unknown | anonymous: KHÔNG render children
}
```

`bootstrapSession()` = `coordinator.getFreshToken(null)` rồi cập nhật `status` theo bảng bước 1. Nó dùng **đúng** hàm refresh của
E7 — hai đường refresh (một cho khởi động, một cho interceptor) là hai lời gọi song song lúc trang vừa mở và có 401 đầu tiên.

**Bước 3 — shell + đăng xuất.**

```ts
// lib/auth/session.ts
export async function logout() {
  try { await authApi.logout(); }            // 401 → interceptor refresh → gọi lại; refresh cũng hỏng thì thôi
  catch { /* server luôn 204 khi bearer hợp lệ (Đ-D6); lỗi còn lại không giữ người dùng lại */ }
  finally { endSession({ broadcast: true }); }   // token = null, status = anonymous, báo các tab khác (E7)
}
```

Nút "Đăng xuất" gọi `logout()` rồi `router.replace('/login')`. Access token đã phát vẫn sống tới 15 phút phía server (hợp đồng
ghi rõ) — FE xóa khỏi memory là phần việc của FE.

**Bước 4 — `/me`.**
- Gọi `authApi.me(signal)` trong effect có `AbortController` (hủy khi rời trang).
- Hiện trong `<dl>`: Email · Vai trò = **`roleDisplayName`** · Xác minh lúc (`emailVerifiedAt`) · Tham gia lúc (`createdAt`),
  định dạng `Intl.DateTimeFormat('vi-VN', { dateStyle: 'medium', timeStyle: 'short', timeZone: 'Asia/Ho_Chi_Minh' })`.
- **Không** hiện `role`. `role` dành cho so logic — GĐ1 chưa có chỗ nào cần so.
- Nút **"Tải lại"** gọi lại `/me` — rẻ, và là cách E7 (Playwright) và F4 (bằng tay) tạo request đồng thời mà không cần DevTools.

### Test

| Test | Kỳ vọng |
|---|---|
| Vitest — `RequireAuth` với refresh 401 | children **không bao giờ** được render (spy); `router.replace('/login?next=%2Fme')` |
| Vitest — `RequireAuth` bọc `<StrictMode>`, refresh 200 | **1** request `/auth/refresh`; children render |
| Vitest — refresh 500 | hiện "Không kiểm tra được phiên đăng nhập."; **không** điều hướng; bấm "Thử lại" gọi refresh lần 2 |
| Playwright — context mới, `goto('/me')` | URL cuối là `/login?next=%2Fme`; không lúc nào thấy `data-testid="me-profile"` |
| Playwright — đăng nhập → `/me` | thấy "Người dùng"; **không** thấy chữ `USER` trong `main` |
| Playwright — tại `/me`, `page.reload()` | vẫn ở `/me`; đúng **1** request `/auth/refresh` trong lần tải lại |
| Playwright — bấm "Đăng xuất" | response `/auth/logout` 204; về `/login`; `goto('/me')` → lại về `/login` |

### Cạm bẫy đã biết

- **Render children khi `status === 'unknown'`** rồi mới đẩy đi → nội dung trang có bảo vệ nháy lên một khung hình, và các
  request của trang con bắn ra không có token. Chỉ render khi `authenticated`.
- **`status === 'error'` đẩy về `/login`** → API khởi động lại 30 giây là mọi người đang dùng bị đăng xuất.
- **Gắn thêm kiểm tra ở `proxy.ts` "cho chắc"** → `proxy.ts` không thấy token lẫn cookie (Đ-E3), chặn cả người đã đăng nhập.
- **`next` chứa cả query** (`/me?tab=x`) — `encodeURIComponent(pathname)` bỏ query; GĐ1 không cần, GĐ2 thêm `searchParams` nếu có.

### Thực tế thi công — 2026-09-17

Bốn bước đã chạy. Những chỗ **khác** hướng dẫn ở trên, và những thứ chỉ lộ ra khi gõ thật:

**Lệch thứ tự E6/E7 (nhóm chốt): E6 dựng coordinator LỚP 1, không đợi E7.** Bước 2 dùng `coordinator.getFreshToken(null)` của
E7. `lib/auth/refresh-coordinator.ts` có ở E6 với phần trong tab: promise chia sẻ, so `stale`, 401 → `endSession` +
`SessionExpiredError`, 429/500/mạng ném nguyên lỗi. **Còn lại cho E7:** `locks`/`channel` (lớp 2 giữa các tab) và nhánh 401
trong `http.ts` (`configureRefresh`). Hai thứ đó thêm phụ thuộc tiêm vào, không đổi chữ ký `getFreshToken`.

**Token store mở rộng bằng hàm chuyển trạng thái, bỏ `set`.** `tokenStore.startSession(token)`, `endSession(endedBy)`,
`markError()`, `markUnknown()`, `reset()` (chỉ test dùng); snapshot `getSession()` là object bất biến để
`useSyncExternalStore` so bằng `Object.is`. `set(token)` cũ cho phép đặt token mà không đặt trạng thái — hai nửa lệch nhau.
Đã chuyển `login-form.tsx` và các test sang hàm mới.

**Lệch bước 3 (nhóm chốt): nút "Đăng xuất" KHÔNG tự `router.replace('/login')`; guard là chỗ duy nhất điều hướng.** Theo
mẫu, `endSession` đổi `status` → guard chạy effect **sau** `router.replace('/login')` của nút → URL cuối là
`/login?next=%2Fme` cho người vừa tự đăng xuất. Phiên giờ mang `endedBy: 'logout' | 'expired'`: guard đưa `logout` về
`/login` trơn, `expired` (refresh 401 lúc khởi động hoặc của E7) kèm `next`. Test Vitest khẳng định `replace` đúng **một**
lần với `/login`.

**Không có `auth-context.tsx`.** `useSession()` ở `lib/auth/use-session.ts` đọc thẳng store qua `useSyncExternalStore`
(snapshot server = `unknown`) — store là biến module, Context/Provider không thêm gì. `providers.tsx` không đổi (E7 thêm import `session.ts` cho `configureRefresh`).

**`RequireAuth` ở `features/auth/require-auth.tsx`, không phải `lib/auth/`.** Nó render UI (khung chờ, lỗi + nút) và biết
nghiệp vụ phiên; `lib/` theo Đ-E13 là logic không cần render. Header nằm **trong** guard (`app/(app)/layout.tsx`) — chưa biết
phiên thì không hiện cả nút "Đăng xuất". `components/shell/app-header.tsx` nhận `actions` (shell không biết nghiệp vụ);
`features/auth/logout-button.tsx`, `features/auth/me-profile.tsx` ráp vào. Khung chờ ở `components/shell/page-skeleton.tsx`.

**`app/page.tsx` → `redirect('/me')` vẫn prerender tĩnh (`○`)**; `next start` trả `307 location: /me` — đã kiểm bằng `curl`.

**`/me`: nút "Tải lại" không `disabled` khi đang chờ** — bấm lần nữa hủy request cũ (`AbortController`) và gửi request mới.
Lỗi hiện qua `FormAlert` + `errorMessage("me", …)` (context mới, bảng rỗng). **Chưa có interceptor:** access token quá 15
phút thì "Tải lại" báo lỗi, F5 mới lấy lại token — ghi ở `README.md` Mục 4.7 tới khi E7 xong.

**Test giờ Việt Nam ép `TZ=UTC`** (`vi.hoisted` trong `me-profile.test.tsx`): máy nhóm ở UTC+7, không ép thì bỏ
`timeZone: 'Asia/Ho_Chi_Minh'` vẫn ra 10:14 và test xanh vô nghĩa (bảng đột biến có dòng này).

**Playwright: bộ đếm hạn mức `/auth/*` ở `e2e/dev-api.ts`.** Cả bộ spec tốn 17 lượt (smoke 1, guard 7, login-storage 3,
register 2, verify-email 4) — quá 10/phút. Mỗi test gọi `giuHanMucAuth(n)` trước khi chạy; vượt thì chờ 75 giây (60 giây cửa
sổ cố định của server + 15 giây cho request của test trước) và nới timeout của test đó. Lượt chạy đủ ~1,5 phút.
`e2e/mailpit.ts` của E5 đổi tên thành `dev-api.ts`, thêm `taoTaiKhoanDaXacMinh(request, prefix)` (trước nằm riêng trong
`login-storage.spec.ts`).

**`smoke.spec.ts` đổi theo trang gốc mới:** `/` → `/me` → `/login?next=%2Fme`. Trình duyệt tự in response 401 ra console —
riêng 401 của `/auth/refresh` khởi động được bỏ qua (đúng thiết kế Đ-E3), mọi lỗi console khác vẫn làm đỏ.

**`guard.spec.ts` theo dõi `<main>`, không chỉ `me-profile`.** Chưa đăng nhập thì `/me` trả 401 nên hồ sơ không bao giờ hiện
**kể cả khi guard hỏng** — theo dõi mỗi `me-profile` là test không bắt được gì (đã thấy khi viết). `<main>` chỉ có trong layout
`(app)`, bên trong guard; `MutationObserver` cài bằng `addInitScript` ghi lại nếu nó từng xuất hiện.

**Bằng chứng.**

| Cổng | Kết quả |
|---|---|
| `pnpm lint` / `typecheck` | xanh |
| `pnpm test` | **163/163** xanh (19 file; trước E6 là 136/136), `Type Errors no errors` |
| `pnpm build` (`NEXT_PUBLIC_API_BASE_URL=/api/v1`) | xanh; `/`, `/me` prerender tĩnh; grep `.next/static` cho `setupWorker`, `mockServiceWorker`, `localhost:5259` — **rỗng** |
| `pnpm test:e2e` (Chrome 152.0.7977.84), API dev thật | **7/7** xanh (thêm 2 ca `guard.spec.ts`), 1,5 phút |

Thử cho đỏ (từng đột biến một, rồi khôi phục — checksum năm file nguồn khớp trước/sau):

| Đột biến | Test bắt |
|---|---|
| Guard render children khi `unknown` | `require-auth` 401 (spy render), 200, 500 |
| `error` cũng đẩy về `/login` | `require-auth` "refresh 500 … KHÔNG điều hướng" |
| Đăng xuất vẫn kèm `next` | `require-auth` "bấm Đăng xuất … KHÔNG kèm next" |
| Coordinator `=` thay `??=` (không gộp) | `refresh-coordinator` "3 lời gọi → 1"; `session` "2 lần đồng thời"; `require-auth` StrictMode 200, 500 |
| Bỏ so token khác `stale` | `refresh-coordinator` "đã có token khác stale"; `session` "đã đăng nhập sẵn" |
| Refresh 401 không `endSession` | `refresh-coordinator`; `session` 401; `require-auth` 401 |
| Refresh 500 cũng kết thúc phiên | `refresh-coordinator` 429/500; `session` 500/429; `require-auth` 500 |
| Bootstrap lỗi tạm thời → `anonymous` | `session` 500/429/mất mạng; `require-auth` 500 |
| "Thử lại" không về `unknown` | `session` 3 ca |
| `logout` không xóa phiên | `session` 2 ca; `require-auth` "bấm Đăng xuất" |
| `logout` ném lỗi server ra ngoài | `session` "server lỗi … không ném" |
| `/me` hiện `role` | `me-profile` "KHÔNG hiện role" |
| Bỏ `timeZone: 'Asia/Ho_Chi_Minh'` | `me-profile` "giờ Việt Nam" (nhờ `TZ=UTC`) |
| "Tải lại" không gọi lại `/me` | `me-profile` 2 ca |
| Không hủy request khi rời trang | `me-profile` "rời trang … hủy request" |
| Store sửa snapshot tại chỗ | `require-auth` 4 ca; `token-store` "tab mới mở" |
| **E2E:** guard render children khi `unknown` | `guard.spec.ts` ca 1 — `__thayHoSo` `Expected: false, Received: true` |
| **E2E:** coordinator không gộp, `pnpm dev` (StrictMode thật) | `guard.spec.ts` ca 2 — tải lại thấy **2** `POST /auth/refresh` |

---

## 8. E7 — Interceptor 401→refresh **single-flight**

> **Đã có từ E6** (xem "Thực tế thi công" Mục 7): `lib/auth/refresh-coordinator.ts` lớp 1 + test, `lib/auth/session.ts`
> (`coordinator`, `bootstrapSession`, `logout`), `tokenStore.startSession/endSession(endedBy)`. E7 thêm `locks` + `channel`
> vào `Deps` của coordinator và nhánh 401 trong `http.ts` — mẫu dưới dùng `d.endSession()` không tham số, ở code thật là
> `endSession: () => tokenStore.endSession("expired")` trong `session.ts`.

**Mục tiêu.** Giữ phiên đăng nhập mượt khi access token 15 phút hết hạn — và **không để chính frontend kích hoạt reuse detection
của server** (Mục 7.3). Làm sai ở đây thì người dùng bị đăng xuất ngẫu nhiên, triệu chứng trông hệt lỗi backend, và cả nhóm sẽ
debug nhầm chỗ rất lâu.

**Kết quả mong đợi.**
- `lib/auth/refresh-coordinator.ts` — `createRefreshCoordinator(deps)`, phụ thuộc **tiêm vào** (hàm refresh, store, Web Locks,
  BroadcastChannel) để unit test được mà không cần trình duyệt.
- `http.ts` có nhánh 401 → refresh → gọi lại **một lần**; `session.ts` nối coordinator với `authApi.refresh` (tránh import vòng
  `http → coordinator → authApi → http`).
- Vitest `refresh-coordinator.test.ts` + `http.interceptor.test.ts` xanh — bảng Test.
- Playwright `e2e/single-flight.spec.ts` xanh trên API dev chạy `Jwt__AccessTokenSeconds=10`.
- Bảng "thử cho đỏ" đã chạy, kết quả trong PR.

### Luật của interceptor

| Tình huống | Làm gì |
|---|---|
| Request có `auth !== false` nhận **401**, chưa gọi lại lần nào | `getFreshToken(tokenĐãDùng)` → gọi lại **một** lần với token mới |
| 401 từ `/auth/login`, `/auth/register`, `/auth/verify-email` | **Không** refresh — đó là lỗi nghiệp vụ (sai mật khẩu…). Trả lỗi cho màn |
| 401 từ **`/auth/refresh`** | Xóa token, `status = anonymous`, báo các tab → guard đưa về `/login`. **Không thử refresh lại** |
| Request đã gọi lại mà vẫn 401 | Trả lỗi; **không** refresh lần hai (tránh vòng lặp khi server từ chối token mới — vd `revoked:user`) |
| 403 | Trả lỗi. 403 là quyết định cuối của tầng 2/3, refresh không đổi được gì |
| Refresh nhận 429 / 500 / mất mạng | Request gốc thất bại với lỗi đó; **không** đăng xuất |

### Các bước

**Bước 1 — coordinator (Đ-E4).**

```ts
// lib/auth/refresh-coordinator.ts
import { ApiError } from '@/lib/api/problem';

export class SessionExpiredError extends Error {}
export type AuthMessage = { type: 'token'; token: string } | { type: 'logout' };

type Deps = {
  refresh: () => Promise<{ accessToken: string }>;
  getToken: () => string | null;
  setToken: (t: string) => void;
  endSession: () => void;                                   // token = null, status = anonymous (không broadcast)
  locks?: { request<T>(name: string, cb: () => Promise<T>): Promise<T> };   // navigator.locks
  channel?: { postMessage(m: AuthMessage): void; addEventListener(t: 'message', l: (e: MessageEvent<AuthMessage>) => void): void };
};

export function createRefreshCoordinator(d: Deps) {
  let inflight: Promise<string> | null = null;               // LỚP 1: trong tab — mọi 401 đồng thời chờ cùng một promise

  d.channel?.addEventListener('message', ({ data }) => {
    if (data.type === 'token') d.setToken(data.token);        // tab khác vừa refresh xong
    else d.endSession();                                      // tab khác nhận 401 từ /auth/refresh hoặc đăng xuất
  });

  async function refreshUnderLock(stale: string | null): Promise<string> {
    const current = d.getToken();
    if (current && current !== stale) return current;         // trong lúc chờ khóa, tab khác đã refresh — KHÔNG gọi nữa
    try {
      const { accessToken } = await d.refresh();
      d.setToken(accessToken);
      d.channel?.postMessage({ type: 'token', token: accessToken });
      return accessToken;
    } catch (e) {
      if (e instanceof ApiError && e.status === 401) {
        d.endSession();
        d.channel?.postMessage({ type: 'logout' });
        throw new SessionExpiredError();
      }
      throw e;                                                // 429/500/mạng: không kết luận là hết phiên
    }
  }

  return {
    /** @param stale token đã dùng cho request vừa nhận 401 — null khi khởi động phiên (E6). */
    getFreshToken(stale: string | null): Promise<string> {
      inflight ??= (d.locks
        ? d.locks.request('socialapp:auth-refresh', () => refreshUnderLock(stale))   // LỚP 2: giữa các tab
        : refreshUnderLock(stale)
      ).finally(() => { inflight = null; });
      return inflight;
    },
  };
}
```

- **Vì sao truyền `stale`:** tab B nhận 401 cho token T0 đúng lúc tab A phát T1. Nếu B so với "token lúc vào hàm" thì B đã có T1
  và vẫn gọi refresh thừa. So với token **đã dùng** cho request hỏng thì B thấy T1 ≠ T0 và dùng luôn.
- `navigator.locks` giữ khóa tới khi callback xong và **tự nhả khi tab đóng** — tab bị đóng giữa chừng không treo các tab khác.

**Bước 2 — nối vào `http.ts`.** Thay dòng đánh dấu ở E2:

```ts
// lib/api/http.ts
type Internal = RequestOptions & { _retried?: boolean };
const NEVER_REFRESH = new Set(['/auth/login', '/auth/register', '/auth/verify-email', '/auth/refresh']);

let getFreshToken: ((stale: string | null) => Promise<string>) | null = null;
export const configureRefresh = (fn: typeof getFreshToken) => { getFreshToken = fn; };   // gọi một lần ở session.ts

// ... trong request(), sau khi có res:
if (res.status === 401 && getFreshToken && opts.auth !== false && !NEVER_REFRESH.has(path) && !(opts as Internal)._retried) {
  await getFreshToken(token);                                // ném SessionExpiredError nếu refresh 401
  return request<T>(path, { ...opts, _retried: true } as Internal);
}
```

`NEVER_REFRESH` trùng với `auth: false` ở `authApi` — cố ý hai lớp: ai đó GĐ2 quên `auth: false` cho một endpoint công khai
mới thì login 401 vẫn không kích hoạt refresh.

**Bước 3 — dựng ở `session.ts`:**

```ts
const channel = typeof BroadcastChannel !== 'undefined' ? new BroadcastChannel('socialapp:auth') : undefined;
export const coordinator = createRefreshCoordinator({
  refresh: authApi.refresh, getToken: tokenStore.get, setToken: startSession, endSession,
  locks: typeof navigator !== 'undefined' ? navigator.locks : undefined, channel,
});
configureRefresh(coordinator.getFreshToken);
```

Chỉ import `session.ts` từ client component (`providers.tsx`) — `BroadcastChannel`/`navigator` không có khi Next prerender.

### Test

| Test | Kỳ vọng |
|---|---|
| Vitest — 3 lời gọi `authApi.me()` đồng thời, MSW trả 401 cho token cũ | **1** request `/auth/refresh`; cả 3 request `/me` gọi lại với token mới, cả 3 resolve |
| Vitest — refresh trả 401 | cả 3 reject `SessionExpiredError`; `tokenStore.get() === null`; `status === 'anonymous'`; **1** request refresh (không thử lại) |
| Vitest — refresh trả 500 | request gốc reject `ApiError(500)`; token **không** bị xóa |
| Vitest — `/me` 401, refresh 200, `/me` gọi lại **vẫn** 401 | reject `ApiError(401)`; tổng **1** refresh (không vòng lặp) |
| Vitest — `authApi.login` 401 | **0** request refresh (giữ test của E4 xanh) |
| Vitest — 403 | **0** request refresh |
| Vitest — **hai coordinator** (giả hai tab) dùng chung khóa giả (mutex) + kênh giả nối nhau, cùng nhận 401 với cùng token | tổng **1** lần gọi `refresh`; coordinator thứ hai trả token do cái thứ nhất phát |
| Vitest — kênh nhận `{ type: 'logout' }` | token `null`, `status === 'anonymous'` |
| **Playwright `single-flight.spec.ts`** — API dev chạy `$env:Jwt__AccessTokenSeconds='10'; dotnet run --project src/backend/SocialApp.Api`; một context, đăng nhập ở tab 1, mở `/me` ở tab 2, 3; chờ 45 giây (10 + `ClockSkew` 30 + dư); **đặt lại bộ đếm** request; bấm "Tải lại" ở cả 3 tab bằng `Promise.all` | đúng **1** request `POST /auth/refresh` (đếm bằng `context.on('request')`); cả 3 tab hiện hồ sơ; không tab nào ở `/login`; bấm "Tải lại" lần nữa → **0** refresh |

Playwright chạy `workers: 1`, và nên chạy spec này **riêng** — cả spec tốn khoảng 6 request `/auth/*`, gần nửa hạn mức 10/phút.

### Thử cho đỏ (local, từng đột biến một, rồi khôi phục — `git status` sạch trước/sau)

| Đột biến | Test phải đỏ |
|---|---|
| Bỏ `inflight` (mỗi 401 tự gọi refresh) | Vitest "3 lời gọi đồng thời" — 3 request refresh |
| Bỏ `locks` ở `session.ts` | Playwright — **3** request refresh (không ai bị đăng xuất nhờ ân hạn 10 giây — chính vì thế lỗi này **không lộ ra** nếu chỉ nhìn "có bị đăng xuất không") |
| Bỏ điều kiện `current !== stale` | Vitest "hai coordinator" — 2 lần gọi refresh |
| Bỏ `/auth/login` khỏi `NEVER_REFRESH` **và** bỏ `auth: false` của `login` | Vitest E4 "401 không refresh" |
| Bỏ `_retried` | Vitest "gọi lại vẫn 401" — treo/nhiều refresh |
| Refresh 401 mà vẫn thử lại | Vitest "refresh trả 401" — 2 request refresh |

### Cạm bẫy đã biết

| # | Bẫy | Hệ quả | Chặn bằng |
|---|---|---|---|
| 1 | Gọi `authApi.refresh()` trực tiếp ở chỗ khác (guard, nút "Tải lại") | Đường refresh thứ hai không qua khóa — đúng loại race phải tránh | Chỉ `session.ts` import `authApi.refresh`; grep trong review |
| 2 | Hẹn giờ refresh theo `expiresIn` "cho mượt" | Thêm lời gọi `/auth/*`, thêm nguồn race, và lỗi single-flight bị che vì hiếm khi có 401 | Không làm ở GĐ1 (E2 bước 5) |
| 3 | Kiểm E7 bằng cách chờ 15 phút thật | Mỗi lần thử mất 15 phút, không ai chạy lại lần hai | `Jwt__AccessTokenSeconds=10` ở dev — một hằng số `JwtOptions` đổi đồng bộ cả `exp` lẫn TTL `revoked:user` (Mục 7.5), không phải cờ tắt bảo vệ |
| 4 | Nhầm "không ai bị đăng xuất" với "single-flight đúng" | Ân hạn 10 giây của server che mất lỗi; lộ ra khi request cách nhau > 10 giây hoặc khi reuse thật xảy ra | Đếm **số request refresh**, không chỉ nhìn trạng thái đăng nhập |
| 5 | Đọc `tokenStore.get()` **sau** `await` rồi mới gắn vào request gọi lại, nhưng truyền `stale` là token đọc lại | `stale` luôn bằng token mới → coordinator tưởng "đã có token mới" và không refresh | `stale` là biến `token` chụp **trước** `fetch` (bước 2) |

### Thực tế thi công — 2026-09-17

Ba bước đã chạy (lớp 1 của bước 1 có từ E6). Những chỗ **khác** hướng dẫn ở trên, và những thứ chỉ lộ ra khi gõ thật:

**Thêm một nhánh vào coordinator (nhóm chốt): request bay đi với token, giờ token đã bị xóa → `SessionExpiredError`, KHÔNG
refresh.** Mẫu bước 1 chỉ có "token khác `stale` → dùng luôn". Thiếu nhánh này thì: 3 request `/me` cùng 401, refresh trả 401
→ phiên kết thúc; request thứ hai, thứ ba tới sau khi promise chung đã xong, thấy `current = null` và **gọi refresh lần nữa**
— bảng Test đòi đúng 1. Giữa tab cũng vậy: tab A refresh 401 và phát `logout`, tab B đang chờ khóa không được "hồi sinh" phiên.
Khởi động phiên (`stale = null`) không đi nhánh này.

**`announceLogout()` trên coordinator; `logout()` báo các tab.** Mẫu bước 3 của E6 ghi `endSession({ broadcast: true })`;
store không biết kênh, nên kênh ở coordinator: `logout()` gọi `tokenStore.endSession('logout')` rồi
`coordinator.announceLogout()`. Tab nhận `logout` kết thúc phiên do **hết hạn** → guard kèm `?next=` (người ở tab đó không tự
bấm đăng xuất).

**`session.ts` chỉ tạo `BroadcastChannel`/`navigator.locks` khi có `window`.** Next prerender chạy module của client
component trên server; Node 24 có sẵn `BroadcastChannel` (mở kênh là giữ tiến trình sống) và `navigator`. `navigator.locks`
bọc lại thành `{ request(name, cb) }` — kiểu `LockManager.request` (callback nhận `Lock | null`) không gán thẳng vào `Deps`.

**`providers.tsx` import `@/lib/auth/session`** để `configureRefresh` chạy ở **mọi** trang — không chỉ trang đã import
`session.ts` qua guard.

**Tên hằng khóa `REFRESH_LOCK` export từ coordinator.** Không đổi hành vi, để test và code dùng chung một chuỗi.

**Lệch cách chạy `single-flight.spec.ts` (nhóm chốt): API + FE RIÊNG, không khởi động lại API dev đang dùng.**
- API thứ hai cổng **5260** với `Jwt__AccessTokenSeconds=10`, `Cors__AllowedOrigins__0=http://localhost:3001`,
  `dotnet run --no-build --no-launch-profile` (API 5259 đang giữ file trong `bin/` nên không build lại được). Instance riêng
  còn có bộ đếm rate limit riêng.
- FE cổng **3001** là bản **build** (`next start`), không `next dev`: Next 16 từ chối dev server thứ hai trên cùng thư mục
  ("Another next dev server is already running"). Bản build nhúng `NEXT_PUBLIC_API_BASE_URL=http://localhost:5260/api/v1`
  — xong phải `pnpm build` lại với `/api/v1` (đã làm; grep `.next/static` không còn `5259`/`5260`). Bản production không có
  StrictMode — không ảnh hưởng: kịch bản là ba vùng nhớ, không phải effect chạy hai lần.
- Lệnh đầy đủ ở đầu file spec. Spec **tự bỏ qua** khi không đặt `PLAYWRIGHT_API_URL` (lượt `pnpm test:e2e` thường, không tốn
  request), và bỏ qua kèm `expiresIn` đọc được nếu API vẫn phát token dài.
- Tốn **7** lượt `/auth/*` (thêm một lần đăng nhập qua API để đọc `expiresIn`), không phải 6. Chạy ~55 giây.

**Hai lỗ hổng test lộ ra khi thử đột biến, đã vá:**
- **Bẫy 5 (đọc `stale` sau `await`) sống sót lần đầu.** Ở ca "3 request đồng thời" cả ba 401 về **trước** khi refresh xong nên
  `tokenStore.get()` lúc đó vẫn là token cũ — không phân biệt được. Thêm ca "401 về MUỘN": request đầu bị giữ lại tới khi
  request thứ hai đã refresh + gọi lại xong, rồi mới nhận 401 → phải dùng token mới, tổng vẫn 1 refresh.
- **Bỏ `_retried` làm treo cả tiến trình Vitest** thay vì đỏ (vòng lặp vô hạn, `--testTimeout` không cắt được). Ca "gọi lại vẫn
  401" giờ từ chối 5 lần rồi mới cho qua: code đúng dừng ở lần 2; code lặp thì lần 6 resolve và test đỏ gọn.

**Bằng chứng.**

| Cổng | Kết quả |
|---|---|
| `pnpm lint` / `typecheck` | xanh |
| `pnpm test` | **177/177** xanh (20 file; trước E7 là 163/163), `Type Errors no errors` |
| `pnpm build` (`NEXT_PUBLIC_API_BASE_URL=/api/v1`) | xanh; grep `.next/static` cho `setupWorker`, `mockServiceWorker`, `localhost:5259`, `localhost:5260` — **rỗng** |
| `single-flight.spec.ts` (Chrome 152.0.7977.84), API 5260 `Jwt__AccessTokenSeconds=10` + FE build 3001 | **1/1** xanh, 55,5 giây — đúng 1 `POST /auth/refresh` khi 3 tab bấm đồng thời; bấm lần hai: 0 |
| `pnpm test:e2e` thường (API 5259) | **7** xanh, **1** bỏ qua (`single-flight`, có chủ đích), 1,6 phút |

Thử cho đỏ (từng đột biến một, rồi khôi phục — checksum bốn file nguồn khớp trước/sau):

| Đột biến | Test bắt |
|---|---|
| Bỏ `inflight` (`=` thay `??=`) | `http.interceptor` "3 lời gọi /me" + "refresh 401"; `refresh-coordinator` "3 lời gọi"; `session`; `require-auth` 2 ca |
| Bỏ điều kiện `current !== stale` | `refresh-coordinator` "hai tab", "ba tab", "đã có token khác stale"; `session` "đã đăng nhập sẵn" |
| Chỉ bỏ `/auth/login` khỏi `NEVER_REFRESH` | **không test nào đỏ — đúng thiết kế:** `auth: false` của `authApi.login` là lớp thứ hai |
| Bỏ `/auth/login` khỏi `NEVER_REFRESH` **và** bỏ `auth: false` | `http.interceptor` "login 401 → 0 refresh"; `http.test` "không gắn Authorization" |
| Bỏ `_retried` | `http.interceptor` "gọi lại VẫN 401" (sau khi vá — xem trên) |
| Refresh hỏng mà vẫn gọi lại | `http.interceptor` "refresh 401", "refresh 500" |
| Đọc `stale` sau `await` (bẫy 5) | `http.interceptor` "401 về MUỘN" (sau khi vá) |
| 403 cũng refresh | `http.interceptor` "403 → 0 refresh" |
| `session.ts` không `configureRefresh` | `http.interceptor` 5 ca |
| Không phát token mới qua kênh | `refresh-coordinator` "hai tab", "ba tab" |
| Refresh 401 không phát `logout` | `refresh-coordinator` "tab đang chờ khóa kết thúc phiên" |
| Kênh nhận `logout` mà không `endSession` | `refresh-coordinator` 2 ca |
| Kênh nhận `token` mà không `setToken` | `refresh-coordinator` "hai tab", "ba tab" |
| Bỏ nhánh chặn hồi sinh phiên đã kết thúc | `refresh-coordinator` 2 ca |
| `logout()` không báo tab khác | `session` "báo các tab khác qua BroadcastChannel" |
| Refresh không chạy trong khóa | `refresh-coordinator` 3 ca lớp 2 |
| **E2E:** bỏ `locks` ở `session.ts` (FE build lại) | `single-flight.spec.ts` — `Expected: 1, Received: 3` |

## 8A. Đổi sang BFF (Đ-E14) — thực tế thi công 2026-09-17

**Gỡ khỏi trình duyệt:** `lib/auth/refresh-coordinator.ts` (+ test), `lib/api/http.interceptor.test.ts`,
`lib/api/config.test.ts`, bearer và nhánh 401→refresh trong `http.ts`, token trong `token-store.ts`,
`NEXT_PUBLIC_API_BASE_URL`. `http.ts` giờ chỉ gọi `/bff` (`credentials: 'same-origin'`); 401 từ `/bff/api/*` = phiên hết
thật (BFF đã thử refresh) → `tokenStore.endSession('expired')`.

**Thêm:** `lib/bff/` (`config`, `crypto`, `http`, `session-store`, `upstream`, `handlers`, `deps`), route mỏng dưới
`app/bff/**`, `lib/api/bff-contract.ts` (hình dạng endpoint BFF dùng chung client/server), `mocks/upstream.ts` (API .NET
giả cho test server), `mocks/redis.ts` (Redis giả đúng ngữ nghĩa TTL / `SET NX`), gói `ioredis` 6.0.0 và `server-only`
0.0.1 (ghim chính xác). Mock MSW phía trình duyệt đổi sang bề mặt `/bff/*`.

**Những thứ chỉ lộ ra khi gõ thật:**
- **msw trong Node tự giữ cookie** của response trước và gắn vào request sau (`leak=1` của API giả xuất hiện trong header
  `Cookie` gửi `/auth/refresh`). `fetch` của Next server không có cookie jar — test chỉ đọc đúng `refresh_token`.
- **`enableOfflineQueue: false` của ioredis** làm request đầu tiên sau khi tiến trình khởi động lỗi 503 (kết nối chưa mở
  xong). Giữ hàng đợi mặc định, `maxRetriesPerRequest: 1` để Redis chết thì vẫn lỗi nhanh.
- **Bấm "Tải lại" ở 3 tab — hay fetch từ trong trang — không tạo được tranh chấp thật:** đã build bản bỏ khóa Redis mà spec
  cũ vẫn xanh, log API chỉ 1 refresh. Gửi 9 request đồng thời từ Node (`context.request` dùng chung cookie của tab) thì
  bản bỏ khóa làm API nhận **9** refresh, 6 lần rơi vào ân hạn 10 giây và 3 request **429** → spec đỏ. Bản đúng: **1**
  refresh, 9/9 là 200.
- **Mutation "key Redis dạng rõ" viết lần đầu là tương đương** (không đổi gì); viết lại bằng cách bỏ băm ở mọi chỗ → bị bắt.
- `app/page.tsx` redirect và các trang vẫn prerender tĩnh; chỉ `/bff/*` là dynamic (`force-dynamic` — thiếu thì
  `GET /bff/auth/session` bị build thành file tĩnh, mọi người nhận cùng câu trả lời).
- Cổng Playwright rẻ hơn: tải lại trang và vào `/me` khi chưa đăng nhập không còn tốn lượt `/auth/*` của API.

**Bằng chứng.**

| Cổng | Kết quả |
|---|---|
| `pnpm lint` / `typecheck` | xanh |
| `pnpm test` | **214/214** (21 file; trước là 177 — bỏ 26 test của coordinator/interceptor/config phía trình duyệt, thêm 61 test `lib/bff`) |
| `pnpm build` (không biến nào) | xanh; `/bff/*` dynamic; grep `.next/static` cho `ioredis`, `SESSION_ENCRYPTION_KEY`, `API_INTERNAL_URL`, `bff:session`, `refresh_token`, `localhost:5259` — **rỗng** |
| Import `lib/bff/crypto` vào client component | build **đỏ** "'server-only' cannot be imported from a Client Component module" (rồi khôi phục) |
| `pnpm test:e2e` (Chrome 152.0.7977.84), API dev 5259 | **8** xanh, 1 bỏ qua (`single-flight`) — `login-storage.spec.ts`: không request nào mang `Authorization`, không request nào tới API trực tiếp, không body nào chứa JWT, không cookie `refresh_token`, `__Host-sid` HttpOnly/Secure/Lax, `document.cookie` rỗng; CSRF logout từ Origin lạ 403 |
| `single-flight.spec.ts`, API 5260 (`Jwt__AccessTokenSeconds=10`) + FE build 3001 (Redis thật) | xanh; log API: **1** `POST /auth/refresh` cho 9 request đồng thời, không lần nào trong ân hạn |
| Cổng CI bundle mở rộng | thử cho đỏ bằng file chứa `ioredis` trong `.next/static` rồi gỡ |

Thử cho đỏ (từng đột biến, rồi khôi phục — checksum `lib/bff/*.ts` + `lib/api/http.ts` khớp trước/sau):

| Đột biến | Test bắt |
|---|---|
| Login trả token ra body | `handlers` 24 ca (204 không body, Redis, fixation…) |
| `relay` chuyển cả `Set-Cookie` của API | `handlers` "KHÔNG chuyển Set-Cookie của API" |
| Bỏ kiểm `Origin` | `handlers` 5 ca CSRF (login, register/verify, proxy POST) |
| Bỏ kiểm `Sec-Fetch-Site` | `handlers` "Sec-Fetch-Site: cross-site" |
| Giữ session ID cũ khi đăng nhập | `handlers` "chống session fixation" |
| Proxy cho đi nhóm `auth` | `handlers` `["auth","refresh"]`, `["AUTH","login"]` |
| Proxy không chặn `..` | `handlers` 2 ca path traversal |
| Proxy không mã hóa lại segment | `handlers` "segment chứa '?'…" — **lần đầu sống sót**, thêm ca này |
| Refresh không so `stale` | `handlers` "3 request đồng thời", "401 về MUỘN" |
| Refresh 401 không xóa phiên | `handlers` "refresh 401 (bị thu hồi)" |
| Refresh 500 xóa phiên | `handlers` "refresh 500 → GIỮ phiên" |
| Gọi lại 401 thì refresh tiếp | `handlers` "gọi lại vẫn 401 → tổng 1 refresh" |
| Logout không gọi API thu hồi | `handlers` 2 ca logout |
| Logout không xóa phiên Redis | `handlers` "phiên bị xóa" |
| Không mã hóa token trong Redis | `handlers` 15 ca |
| Key Redis là session ID dạng rõ (bỏ băm) | `handlers` "key là băm của session ID" |
| AAD không gắn key | `crypto` + `handlers` 16 ca |
| Cookie thiếu `HttpOnly` | `handlers` "204, KHÔNG body, cookie __Host-sid HttpOnly…" |
| Khóa không nhả khi tác vụ ném | `session-store` + `handlers` 4 ca |
| Nhả khóa không kiểm chủ | `session-store` "KHÔNG xóa khóa của người mới" |
| `X-Forwarded-For` lấy phần tử đầu | `handlers` "203.0.113.9, 198.51.100.10 (hops=0)" |
| Cookie phiên không kiểm dạng | `handlers` "cookie sai dạng → không chạm Redis" — **lần đầu sống sót**, thêm đếm lần đọc Redis |
| Production không đòi `SESSION_ENCRYPTION_KEY` | `config` "production thiếu biến" |
| Client không báo phiên hết hạn khi 401 | `http` + `session` 2 ca |
| Client gửi `credentials: 'include'` | `http` "credentials: 'same-origin'" |
| **E2E:** login trả token ra body (dev server) | `login-storage.spec.ts` — `Expected: 204, Received: 200` |
| **E2E:** bỏ khóa Redis (FE build 3001) | `single-flight.spec.ts` — 3 × 429; log API 9 refresh |

---

## 9. E8 — Đóng gói frontend cho staging *(bổ sung)*

**Mục tiêu.** `F2` (bỏ mock, trỏ staging) và `F3` (E2E-01) đòi FE chạy **cùng domain** `mxh.banhgao.net` với API: link xác minh
trong mail trỏ về đó (`Frontend__BaseUrl`), và cookie `SameSite=Lax` không đi nếu FE nằm ở site khác (Đ-E1, Đ-E11). Hiện CD chỉ
build image `api` — không có image FE thì F1 không có gì để deploy.

**Kết quả mong đợi.**
- `next.config.ts`: `output: "standalone"`.
- `src/frontend/Dockerfile` (multi-stage, `node:24-alpine`, pnpm qua corepack, chạy non-root) + `src/frontend/.dockerignore`.
- `docker buildx build --platform linux/arm64 src/frontend` xanh; `docker run -p 3000:3000` → `/login` trả 200.
- Bundle production **không** chứa MSW và **không** chứa `localhost:5259` (bảng Test).
- Không route FE nào dưới `/api`, `/health`, `/swagger`; không `app/api/**`.
- Danh sách việc chuyển cho F1 (cuối mục) đã gửi cho người làm F1.

### Các bước

```ts
// next.config.ts
import type { NextConfig } from "next"

const nextConfig: NextConfig = {
  output: "standalone",   // E8: image chỉ mang file runtime cần — chạy `node server.js`, không cần pnpm trong image
}

export default nextConfig
```

```dockerfile
# src/frontend/Dockerfile — context = src/frontend/. Build cho OCI Ampere: --platform linux/arm64 (AGENTS.md luật 8)
FROM node:24-alpine AS deps
WORKDIR /app
RUN corepack enable
COPY package.json pnpm-lock.yaml pnpm-workspace.yaml .npmrc ./
RUN pnpm install --frozen-lockfile

FROM node:24-alpine AS build
WORKDIR /app
RUN corepack enable
COPY --from=deps /app/node_modules ./node_modules
COPY . .
# NEXT_PUBLIC_* bị NHÚNG lúc build. Đường dẫn tương đối → cùng origin với apache, một image cho mọi domain (Đ-E1).
ARG NEXT_PUBLIC_API_BASE_URL=/api/v1
ENV NEXT_PUBLIC_API_BASE_URL=$NEXT_PUBLIC_API_BASE_URL NEXT_TELEMETRY_DISABLED=1
RUN pnpm build

FROM node:24-alpine AS runner
WORKDIR /app
ENV NODE_ENV=production NEXT_TELEMETRY_DISABLED=1 PORT=3000 HOSTNAME=0.0.0.0
RUN addgroup -S app && adduser -S app -G app
COPY --from=build --chown=app:app /app/public ./public
COPY --from=build --chown=app:app /app/.next/standalone ./
COPY --from=build --chown=app:app /app/.next/static ./.next/static
USER app
EXPOSE 3000
HEALTHCHECK --interval=15s --timeout=3s --retries=3 CMD wget -qO- http://127.0.0.1:3000/login > /dev/null || exit 1
CMD ["node", "server.js"]
```

```gitignore
# src/frontend/.dockerignore
node_modules
.next
e2e
test-results
playwright-report
.env*
!.env.example
```

- MSW không vào bundle — app không còn mock trình duyệt (đổi Đ-E7 2026-09-17); cổng CI grep `.next/static` giữ làm lưới.
- Health check gọi `/login`, không gọi `/health`: apache chuyển `/health` về API (Đ-E11).

### Test

| Test | Kỳ vọng |
|---|---|
| `docker buildx build --platform linux/arm64 -t socialapp-frontend:local --load src/frontend` | exit 0 (dưới QEMU chậm vài phút — bình thường) |
| `docker run --rm -p 3000:3000 socialapp-frontend:local` rồi `curl.exe -s -o NUL -w "%{http_code}" http://localhost:3000/login` | `200` |
| Sau `pnpm build`: `Select-String -Path .next\static\**\*.js -Pattern 'mockServiceWorker','localhost:5259' -List` | **không** dòng nào |
| Build **không** đặt `NEXT_PUBLIC_API_BASE_URL` (bỏ `ARG` mặc định tạm thời) | `pnpm build` **đỏ**, thông điệp nêu tên biến (Đ-E1) — rồi khôi phục |
| `Get-ChildItem app -Recurse -Directory -Filter api` | rỗng |

### Cạm bẫy đã biết

| # | Bẫy | Hệ quả | Chặn bằng |
|---|---|---|---|
| 1 | Quên chép `.next/static` vào runner | Trang lên nhưng không có CSS/JS — trắng trơn | Dòng `COPY … .next/static` |
| 2 | Không đặt `HOSTNAME=0.0.0.0` | `server.js` chỉ nghe trong container, apache trên host không vào được | `ENV` ở stage runner |
| 3 | `NEXT_PUBLIC_API_BASE_URL` là URL tuyệt đối của staging | Đổi domain là phải build lại; và build nhầm URL dev là staging gọi `localhost` của người dùng | Đường dẫn tương đối `/api/v1` |
| 4 | Apache đặt `ProxyPass /` **trước** `/api` | Mọi request API rơi vào Next → 404 HTML, FE báo "không kết nối được" | File mẫu apache đã xếp `/api`, `/swagger`, `/health` trước — F1 giữ đúng thứ tự |
| 5 | Thiếu `.npmrc` hoặc `pnpm-workspace.yaml` trong context | `COPY` lỗi, hoặc `pnpm install` chạy postinstall mà template đã chặn | Cả hai file có từ E1 và không bị `.dockerignore` loại |

### Chuyển cho F1 — không làm trong khối E *(cần ghi ngược: B.8/F1)*

- [ ] `deploy-staging.yml`: thêm bước build + push `ghcr.io/ricecracker12/30inf067_btl/frontend:staging` (`context: src/frontend`,
      `platforms: linux/arm64`).
- [ ] `docker-compose.staging.apache.yml`: service `frontend`, `image` như trên, `ports: ["127.0.0.1:3000:3000"]`,
      `restart: unless-stopped`. Biến thể Caddy thì thêm `reverse_proxy /api/* /health/* /swagger* api:8080` trước
      `reverse_proxy frontend:3000`.
- [ ] Apache trên VM: bỏ comment `ProxyPass / http://127.0.0.1:3000/` + `ProxyPassReverse`, **giữ sau** ba khối API.
- [ ] Sau deploy: `https://mxh.banhgao.net/login` 200; `https://mxh.banhgao.net/api/v1/ping` vẫn trả JSON của API; `.env` staging đã có
      `Frontend__BaseUrl=https://mxh.banhgao.net` và `Cors__AllowedOrigins__0=https://mxh.banhgao.net` (không đổi).

---

## 10. Kế hoạch commit

| # | Nội dung | CI sau khi push | Thông điệp gợi ý |
|---|---|---|---|
| 1 | `E1` — scaffold preset, sửa sau init, `TextField`, ESLint, Vitest | 🟢 (chưa có job `frontend` — kiểm local) | `feat(gd1-e): E1 — Next.js 16 + shadcn/ui preset b50KEhMiu, luat UI kit` |
| 2 | `E2` — codegen, client, token store, MSW, job CI `frontend` | 🟢 job `frontend` chạy lần đầu | `feat(gd1-e): E2 — type tu hop dong, api client, mock MSW, cong codegen CI` |
| 3 | `E4` | 🟢 | `feat(gd1-e): E4 — man dang nhap, 401 mot thong diep, token chi o memory` |
| 4 | `E3` | 🟢 | `feat(gd1-e): E3 — man dang ky, nguong mat khau 8 ky tu / 72 byte` |
| 5 | `E5` | 🟢 | `feat(gd1-e): E5 — man xac minh email, chan goi doi duoi StrictMode` |
| 6 | `E6` | 🟢 | `feat(gd1-e): E6 — app shell, route guard phia client, trang /me` |
| 7 | `E7` + ghi ngược Đ-E4 vào `giai-doan-1.md` | 🟢 | `feat(gd1-e): E7 — interceptor 401 single-flight trong tab va giua cac tab` |
| 8 | `E8` + ghi ngược Đ-E11 vào `giai-doan-1.md` B.7/B.8 | 🟢 | `feat(gd1-e): E8 — dong goi frontend standalone arm64 cho staging` |

- Trước **mỗi** commit: `node .gitnexus/run.cjs detect-changes --scope all --repo .` (`CLAUDE.md`).
- Từ commit 2, job `frontend` phải xanh ở **run cuối** của nhánh; `cancel-in-progress` hủy run cũ khi push dồn.
- Thi công lệch tài liệu này thì ghi vào mục "Thực tế thi công" của việc đó (khuôn hướng dẫn D), sửa tài liệu gốc cùng commit.
- PR vào `develop` khi khối xong; một người backend review (bus factor, `ke-hoach-trien-khai.md` Mục 0C).

---

## 11. Checklist nghiệm thu khối E

**Kiểm tự động**

- [ ] `pnpm lint`, `pnpm typecheck`, `pnpm test`, `pnpm build` xanh
- [ ] Job CI `frontend` xanh, gồm cổng `API types khop hop dong (CI GATE)`; đã thử cho đỏ một lần (E2)
- [ ] Vitest: `text-field`, `schema.test-d`, `http`, `problem`, `messages`, `safe-next`, `auth` (bảng ngưỡng), `login-form`, `register-form`,
      `check-email`, `password-field`,
      `verify-email` (StrictMode 1 request), `verify-once`, `require-auth`, `session`, `me-profile`; BFF (Đ-E14):
      `lib/bff/handlers`, `crypto`, `config`, `session-store`
- [ ] Luật ESLint Đ-E2/Đ-E12 đã thử cho đỏ (E1)
- [ ] Bảng "thử cho đỏ" của E7 đã chạy

**Playwright trên API dev — dán kết quả vào PR**

- [ ] `login-storage.spec.ts` — Web Storage rỗng, cookie `refresh_token` `httpOnly`/`secure`/`Lax`/`/api/v1/auth`, đăng nhập
      cross-origin qua preflight — **đóng luôn mục còn treo ở checklist khối D (Mục 15)**, không chụp ảnh DevTools (E4)
- [ ] `guard.spec.ts` — chưa đăng nhập về `/login?next=%2Fme`; tải lại giữ phiên bằng 1 refresh; đăng xuất
- [ ] `single-flight.spec.ts` (`Jwt__AccessTokenSeconds=10`) — 3 tab, **đúng 1** `POST /auth/refresh`
- [ ] Lượt E2E-01 trên dev: đăng ký → link lấy từ API REST của Mailpit → xác minh → đăng nhập → `/me` → hết hạn → refresh → gọi lại

**Kiểm tay — ghi bằng chứng vào PR**

- [x] Mật khẩu `'ệ'.repeat(25)` gửi thẳng API dev → 400 `errors.password` đúng câu client hiện (E3 — bảng đối chiếu ở
      "Thực tế thi công" Mục 5)
- [ ] `docker run` image FE → `/login` 200 (E8)

**Code review — không test tự động nào bắt được**

- [ ] Không route BFF nào trả access/refresh token ra trình duyệt; mọi lời gọi API phía server đi qua `lib/bff/upstream.ts` (Đ-E14)
- [ ] Route Handler chỉ dưới `app/bff/**`; không route `app/api/**`, không route FE dưới `/api`, `/health`, `/swagger` (Đ-E11)
- [ ] Module server của BFF có `import "server-only"` (Đ-E14)
- [ ] `components/ui/**` chỉ đổi khi đổi cho toàn app; component mới thêm bằng `pnpm exec shadcn add` (Đ-E12)
- [ ] Bốn tầng đúng chiều (Đ-E13): `components/` và `lib/` không biết nghiệp vụ; `features/auth/` không bị `app/` hay `lib/`
      import ngược; không có thư mục `features/` rỗng cho giai đoạn chưa tới
- [ ] Không `^`/`~` trong `package.json`; `pnpm-lock.yaml` commit; `git ls-files src/frontend` có file (không gitlink)
- [ ] Không `console.log` token, response đăng nhập/refresh, hay link xác minh
- [ ] `safeNext` dùng ở **mọi** chỗ điều hướng theo `next`

**Ghi ngược vào tài liệu gốc**

- [x] Đ-E4 → `giai-doan-1.md` B.7/E7 và Mục 10.1 E2E-02 (single-flight giữa các tab) — 2026-09-17, cùng E7
- [ ] Đ-E11 → `giai-doan-1.md` B.7 (thêm E8) và B.8/F1 (service `frontend`, apache, CD)
- [ ] `README.md` mục trạng thái: khối E xong

---

## 12. Khối E để lại gì

| Ai nhận | Nhận cái gì |
|---|---|
| **F1** | Image FE `standalone` arm64 + danh sách việc compose/apache/CD (Mục 9). Từ Đ-E14 container FE cần Redis và `API_INTERNAL_URL`, `REDIS_URL`, `APP_ORIGIN`, `SESSION_ENCRYPTION_KEY`, `TRUSTED_PROXY_HOPS`; api cần `ReverseProxy__TrustedNetworks__0` = mạng compose (checklist F1 hướng dẫn khối D) |
| **F2** | Trình duyệt không cần base URL nào (Đ-E14 — chỉ gọi `/bff` cùng origin); mock trình duyệt đã gỡ (đổi Đ-E7) — F2 đặt biến server của BFF trên staging |
| **F3** | Playwright E2E-01 chạy lại được bằng `BASE_URL=https://mxh.banhgao.net` (bước mail đổi sang hộp thư thật) |
| **F4** | `single-flight.spec.ts` (9 request đồng thời cùng phiên sau khi token hết hạn) + đếm `POST /auth/refresh` trong log api — bấm tay trên 3 tab không tạo được tranh chấp thật (Mục 8A) |
| **GĐ2–GĐ8** | Kit shadcn/ui + luật Đ-E12 + composite `components/form`; `request()` tới BFF + proxy chung `/bff/api/*` (bearer, refresh single-flight ở server — module mới không phải thêm route BFF); nhóm route `(app)` có guard; bảng thông điệp lỗi theo status; codegen — module mới thêm một script `gen:api:<module>` ra `lib/api/<module>/schema.d.ts`, cổng CI so cả thư mục `lib/api`. **Thêm một màn (Đ-E13) = 4 chỗ:** route ở `app/(app)/<url>/` · nghiệp vụ ở `features/<màn>/` · `gen:api:<module>` → `lib/api/<module>/` · kiểm dữ liệu ở `lib/validation/<màn>.ts`. Không đụng `components/`, không sửa `eslint.config.mjs` |
| **GĐ5** | Trình duyệt KHÔNG có token cho SignalR (Đ-E14): hub phải đi qua BFF (proxy WebSocket ở Next server) hoặc API cấp vé ngắn hạn qua `/bff/api/*` — chốt ở cổng mở GĐ5 |
| **GĐ6** | Hạ quyền ghi `revoked:user` → request kế tiếp 401 → BFF refresh → token mới mang vai trò mới; FE **không phải sửa gì** |
