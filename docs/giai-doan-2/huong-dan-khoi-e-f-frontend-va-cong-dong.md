# Hướng dẫn thực hiện — Khối E. Lane frontend + Khối F. Cổng đóng (GĐ2)

> Bản triển khai chi tiết của **B.7 Khối E** và **B.8 Khối F** trong [giai-doan-2.md](giai-doan-2.md). Tài liệu gốc
> trả lời *cái gì* và *vì sao*; tài liệu này trả lời *gõ vào file nào, theo thứ tự nào, và nhìn vào đâu để biết đã
> xong thật*.
>
> Gộp hai khối vào một file, và **viết theo một luồng tuần tự, không chia lane**. Lý do: tới lúc hai khối này bắt
> đầu thì khối A–D đã xong trên nhánh `loveart1210` (tới `d5b0f49`), nên không còn lane nào để chạy song song; và bốn trong năm đầu việc của
> khối F (`F2`, `F3`, `F4`, `F5`) chỉ kiểm được **sau khi** khối E lên staging — tách hai file thì chỗ nối rơi vào
> khoảng giữa. Đây cũng là nếp GĐ1 đã chạy với
> [huong-dan-khoi-b-c-test-va-luu-tru.md](huong-dan-khoi-b-c-test-va-luu-tru.md).
>
> **Nguồn sự thật, theo thứ tự ưu tiên khi mâu thuẫn:**
>
> 1. Hai hợp đồng — `src/backend/Modules/Profile/Presentation/profile-v1.yaml`, `.../Content/Presentation/content-v1.yaml`
> 2. `giai-doan-2.md` — Mục 3 (`Đ-2.1`–`Đ-2.15`), Mục 7 (luồng), Mục 8 (hợp đồng), Mục 10.5 (test FE), Mục 11–12
> 3. `.claude/rules/frontend-rules.md` và mười sáu quyết định `Đ-E1`–`Đ-E16` trong
>    [huong-dan-khoi-e-frontend.md](../giai-doan-1/huong-dan-khoi-e-frontend.md)
> 4. File này
>
> Chỗ nào file này lệch với 1–3 thì **sửa file này**, không sửa ngược. Muốn đổi một `Đ-2.*` hay một `Đ-E*` thì đó là
> **quyết định mới**, có ngày tháng, ghi vào tài liệu gốc **trong cùng commit** (luật frontend Mục 11 #4).

|  |  |
|---|---|
| **Người làm** | Một luồng duy nhất. B.2 giao `E` cho FE và `F` cho cả nhóm; ở đây **không áp dụng chia lane** — xem đoạn mở đầu |
| **Thời lượng** | Ngày 8 (`E1`–`E4`) → Ngày 9 sáng (`E5`–`E8`) → Ngày 9 chiều (`F1`–`F5`, cổng đóng) |
| **Hai khối này chặn** | **GĐ4.** Không đóng băng hợp đồng (`F5`) thì GĐ4 dựng trên nền còn đang đổi |
| **Cần trước** | Khối `A`–`D` đã xong trên `loveart1210` (tới `d5b0f49`); Mục 9.0 (bucket + CORS + token R2) đã xong; `deploy/.env` trên server đã có `R2__*` |
| **Không thuộc hai khối này** | Bất kỳ thay đổi nào ở `src/backend/**` (trừ lỗi hợp đồng blocking — xem Mục 1.2 luật 3) · route BFF mới · bình luận/cảm xúc · feed · cắt/nén ảnh phía client · sửa danh sách ảnh của bài đã đăng |

---

## 0. Danh sách công việc — mục tiêu và kết quả mong đợi

Mười ba đầu việc: **tám** của khối E, **năm** của khối F. Ranh giới giữa hai khối là ranh giới giữa *"chạy trên máy
tôi"* và *"chứng minh được trên hệ thống thật"* — mọi thứ của khối E nghiệm thu bằng `pnpm test` và `localhost`, mọi
thứ của khối F nghiệm thu bằng domain HTTPS và trình duyệt thật.

### 0.1 Khối E — Lane frontend

> **Mục tiêu khối:** lát cắt dọc chạm tới người dùng thật, và **chứng minh CORS bằng trình duyệt** — thứ backend
> không tự chứng minh được, và là lý do ISS-02 còn mở cho tới `F3`.

| Mã | Đầu việc | Mục tiêu — việc này tồn tại để làm gì | Kết quả mong đợi — thứ kiểm chứng được |
|---|---|---|---|
| **E1** | Codegen + api client hai module + mở rộng `request()` | Cho bảy đầu việc sau **một** bề mặt gọi API: kiểu lấy từ file sinh (hợp đồng đổi = đỏ compile, không phải đỏ lúc chạy), một chỗ gọi `fetch`, một bảng thông điệp lỗi | `pnpm gen:api` chạy xong **worktree sạch** (file sinh đã có từ cổng mở); `lib/api/types.ts` re-export đủ kiểu của hai module, **không** khai tay dòng nào; `RequestOptions.method` có `PUT \| PATCH \| DELETE`; `lib/api/profile-api.ts` + `lib/api/content-api.ts` gọi qua `BFF_ROUTES.api`, **không** thêm route BFF nào; `errorMessage` có ngữ cảnh mới; `pnpm typecheck` + `pnpm lint` xanh |
| **E2** | Onboarding hồ sơ (FR-013, Đ-2.4) | Bảo đảm bất biến rẻ nhất của giai đoạn: **không có hồ sơ thì không đăng được bài**. Nhờ nó mọi `posts.author_id` tra được ra `UserCard`, và không màn nào phải có nhánh "tác giả không tên" | Đăng nhập lần đầu → `GET /users/{me}/profile` 404 → chuyển `/onboarding`, **không bỏ qua được** (gõ thẳng URL khác vẫn bị đưa về); trạng thái chưa biết hiện khung chờ, **không nháy nội dung**; `PUT /users/me/profile` 200 → vào app; validation client dùng **đúng câu của server** và **không chặt hơn** (Đ-E5); Vitest phủ ba nhánh 404 / 200 / lỗi mạng |
| **E3** | Avatar (`PUT` + `DELETE /users/me/avatar`) | Chạy thử ba bước presign → `PUT` R2 → gắn key trên một luồng **nhỏ, một ảnh**, trước khi `E4` chạy nó với mười ảnh và tiến trình | Chọn ảnh → 3 bước, **3 trạng thái lỗi riêng** (bước 2 = mạng/CORS, bước 3 = hợp đồng — không gộp thành "tải ảnh thất bại"); ảnh vào bucket `-dev` thật; `avatarUrl` hiện trên header; `DELETE` → 204 và avatar biến mất; sai loại/quá 10 MB bị chặn **ở client** với đúng câu của server, và server vẫn là bên quyết định |
| **E4** | Composer đăng bài + upload thật có tiến trình (SEQ-01) | Đóng bước 1–5 của SEQ-01 trên trình duyệt: đây là **chỗ duy nhất** trong code trình duyệt được gọi ra ngoài origin, và là chỗ ISS-02 lộ ra | `lib/upload/r2.ts` là **module duy nhất** dùng `XMLHttpRequest` (có `eslint-disable` trỏ Đ-E17); presign **một** request cho tối đa 10 ảnh (Đ-2.15); mỗi ảnh một dòng trạng thái + phần trăm; **upload song song tối đa 3**; ảnh lỗi thử lại **riêng ảnh đó**, không hủy cả lô; BR-01 kiểm client **cùng ngưỡng server**; `POST /posts` gửi lại `{mediaKey, contentType, sizeBytes}` đúng giá trị đã khai lúc presign (Q-D1); 400/403/409 hiện đúng theo key `errors` |
| **E5** | Danh sách + chi tiết bài | Chứng minh cursor của Đ-2.11 dùng được từ phía client, và chốt cách cuộn mà GĐ4 (feed) sẽ chép lại | Cuộn/“Xem thêm” nối trang theo `nextCursor`; FE **không tự dựng** cursor, chỉ truyền lại; `nextCursor === null` → hết, ẩn nút; skeleton lúc tải; trạng thái rỗng cho tài khoản mới; không lặp bài khi gọi trùng (khử trùng theo `postId`); ảnh hết hạn presign (>15 phút) **không** thành icon vỡ — xem Mục 6 cạm bẫy 3 |
| **E6** | Sửa / xóa bài của mình | Đóng FR-005 phía người dùng, và giữ đúng luật "FE không tự so id": nút Sửa/Xóa hiện theo `canEdit` của server | Nút hiện **theo `canEdit`**, không so `author.userId` với `me.userId` ở bất kỳ đâu (grep sạch); `PATCH` body `{}` không bao giờ gửi đi (nút Lưu tắt khi chưa đổi gì); xóa có bước xác nhận, 204 → rời khỏi trang và bài biến khỏi danh sách đang cầm; **403 hiện một câu không tiết lộ** bài có tồn tại hay không |
| **E7** | Nới CSP cho host R2 + ghi **Đ-E17** | Không có bước này thì `E3`/`E4` chạy được ở dev (CSP dev lỏng hơn) rồi **chết trên staging** — đúng loại lỗi cổng đóng không còn thời gian để sửa | `connect-src` **và** `img-src` trong `lib/security/csp.ts` có host R2 lấy từ **biến server** (Q-E1), không hằng số gõ tay; Vitest cho từng chỉ thị theo khuôn `csp.test.ts` đã có; **Đ-E17 ghi vào `huong-dan-khoi-e-frontend.md` trong cùng commit**; biến mới có trong `deploy/.env.example` và được nêu tên ở mô tả PR |
| **E8** | Vitest + Playwright cho lát cắt mới | Biến sáu màn trên thành thứ **chặn merge** (Vitest ở CI) và thành bằng chứng chạy tay có ghi lại (Playwright local, Đ-E8) | Vitest phủ đủ Mục 10.5 dòng 1: BR-01 client · 400/403/409 của composer · **một ảnh lỗi không hủy cả lô** · nối trang theo cursor; Playwright `workers: 1` phủ dòng 2: onboarding bắt buộc · đăng bài 2 ảnh thật · sửa · xóa · 403 không lộ tài nguyên · **CSP không chặn `PUT` lên R2**; ảnh thật trong `e2e/fixtures/` (1 JPEG + 1 PNG, commit vào repo); kết quả local + **bản Chrome đã chạy** dán vào PR |

### 0.2 Khối F — Cổng đóng

> **Mục tiêu khối:** chứng minh trên hệ thống thật, không phải trên máy local và không phải trên mock. Đây là chỗ
> "xong" chuyển từ *cảm giác* thành *sự kiện kiểm chứng được*.

| Mã | Đầu việc | Mục tiêu — việc này tồn tại để làm gì | Kết quả mong đợi — thứ kiểm chứng được |
|---|---|---|---|
| **F1** | Deploy staging qua CD tự động | Loại "chạy được trên máy tôi" khỏi định nghĩa xong. Ba module, ba schema, và bốn khóa R2 lần đầu chạy cùng nhau trên server | **Trước khi merge**: `R2__Endpoint`, `R2__Bucket`, `R2__AccessKey`, `R2__SecretKey` và biến host R2 của FE (Q-E1) đã có trong `deploy/.env` **trên server**; sau deploy: service `migrate` xanh cho **cả ba** module (`identity`, `profile`, `content` đều có `__EFMigrationsHistory` riêng); `curl -fsS https://mxh.banhgao.net/health/ready` → 200; `/swagger/profile-v1/swagger.json` và `/swagger/content-v1/swagger.json` → 200 |
| **F2** | Frontend trỏ staging thật | Đóng rủi ro "xanh trên máy local, đỏ trên staging" — và chứng minh bằng **tab Network**, không bằng lời | DevTools trên `https://mxh.banhgao.net`: chỉ thấy `/bff/*` cùng origin và các `PUT` **thẳng tới host R2**; **không** `Authorization` nào ra trình duyệt; Web Storage trống; **không** `storage_key` của người khác trong bất kỳ response nào; `mocks/` chỉ phục vụ Vitest (luật frontend Mục 8 — xem Q-E7, mục này đã đổi nội dung so với B.8) |
| **F3** | E2E lát cắt + **bằng chứng ISS-02** | Đây là lúc ISS-02 đóng lại — hoặc lộ ra. `curl` luôn xanh; chỉ trình duyệt mới nói được sự thật về CORS | Trên domain HTTPS thật: đăng nhập → onboarding → đăng bài **2 ảnh** → xem → sửa → xóa. **Bằng chứng phải giữ**: ảnh tab Network của lượt `PUT` lên R2 (thấy **200** và thấy header đã ký), ảnh dashboard R2 có object, ảnh bài hiển thị ảnh. Đỏ → dùng phương án ứng phó Mục 14 **ngay trong ngày** |
| **F4** | Checklist nghiệm thu (Mục 12) + DoD (Mục 11) | Rà bốn nhóm bằng cách **kiểm tận nơi** (psql, DevTools, dashboard R2), không suy đoán từ CI xanh | Mọi dòng Mục 12 được tick **hoặc** ghi lý do hoãn kèm địa chỉ hoãn — **không xóa dòng**; cả 6 mục Mục 11 tick; có dòng bắt buộc chạy psql (`information_schema.referential_constraints` không có FK chéo schema) và dòng mở URL ảnh **đã hết hạn** → R2 trả 403 |
| **F5** | Đóng băng hợp đồng + bàn giao | GĐ4 khởi động trên nền ổn định, và **nợ có địa chỉ** không thành nợ vô chủ | Thông báo nhóm: `profile-v1.yaml` + `content-v1.yaml` **đóng băng** cho phạm vi GĐ2; liệt kê phần hoãn có địa chỉ (Mục 2) kèm giai đoạn nhận; nhắc **hai bẫy chéo giai đoạn** cho GĐ4 (cache lưu `storage_key` chứ không lưu URL đã ký; `IFriendshipReader` đổi **một dòng DI**); ba điều kiện B.11 xác nhận đủ → GĐ4 được mở |

### 0.3 Thứ tự thực thi

```
 E1 ──┬─→ E2 ──→ E3 ──→ E4 ──┬─→ E5 ──→ E6 ──→ E8
      │         (presign     │
      │          một ảnh)    │
      └─────────────────────→ E7  ← làm SỚM, không đợi E4 xong
                                    (dev CSP lỏng hơn staging — xem dưới)

 [E xong] ──→ F1 ──→ F2 ──→ F3 ──→ F4 ──→ F5
```

Bốn phụ thuộc **thật**, không phải sở thích sắp xếp:

- **`E1` → mọi thứ.** Không có kiểu và api client thì mọi màn phải khai tay payload — đúng thứ luật frontend Mục 4
  cấm, và sửa sau tốn hơn làm đúng.
- **`E3` trước `E4`.** Về mặt kỹ thuật `E4` không cần `E3`. Nhưng `E3` là bản diễn tập **rẻ** của đúng ba bước mà
  `E4` sẽ chạy với mười ảnh: một ảnh, không tiến trình, không song song, không thử lại. Sai chữ ký hay sai header ở
  `E3` thì mất 10 phút để thấy; sai lần đầu ở `E4` thì lẫn vào giữa tiến trình, hàng đợi và retry.
- **`E4` → `E5`/`E6`.** Không có bài nào thì không có gì để liệt kê, sửa hay xóa. Tạo bài bằng `curl` để làm `E5`
  trước là được — nhưng khi đó `E5` không bao giờ thấy ảnh thật.
- **Toàn bộ `E` → `F1`.** Cổng đóng không phải ngày code.

Hai chỗ **không** phải phụ thuộc — đừng xếp hàng cho "gọn":

- **`E7` không cần `E4`.** Và nên làm **sớm**: CSP ở dev có `'unsafe-eval'` + `style-src 'unsafe-inline'` nhưng
  `connect-src` thì **giống hệt production** (`'self'`). Nghĩa là thiếu `E7` thì `E3` đã đỏ ngay trên `localhost` —
  may mắn, vì lỗi này mà để tới staging thì triệu chứng của nó (`PUT` bị chặn, không có response) **trông y hệt**
  CORS sai trên bucket. Làm `E7` trước `E3` là cách rẻ nhất để loại một trong hai nghi phạm.
- **`E8` không phải bước cuối.** Viết test **cùng lúc** với từng màn (Vitest nằm cạnh mã nguồn); `E8` chỉ là chỗ
  gom Playwright và rà xem Mục 10.5 còn thiếu dòng nào.

**Ba cột mốc để đo tiến độ:** cuối buổi sáng Ngày 8 xong `E1`+`E7`+`E2`; cuối Ngày 8 **một ảnh thật đã nằm trong
bucket `-dev` từ trình duyệt** (`E3`); trưa Ngày 9 xong `E4`–`E6` (từ lúc này `F1` chạy được).

### 0.4 Phần cắt được nếu trễ

Cắt thì **ghi rõ vào PR và vào `giai-doan-2.md`**, không lặng lẽ bỏ.

| Ứng viên | Cắt được vì | Cái giá |
|---|---|---|
| Tiến trình phần trăm từng ảnh (`E4`) | Upload vẫn chạy với trạng thái ba mức (đang gửi / xong / lỗi) | Người dùng không biết ảnh 8 MB đang đi tới đâu — với đường lên VPS thì đó là 30 giây im lặng |
| Thử lại **riêng một ảnh** (`E4`) | Thay bằng "gửi lại cả lô" | Một ảnh lỗi trong mười ảnh bắt người dùng chờ lại từ đầu, và sinh 9 object mồ côi mỗi lần |
| Cuộn vô hạn (`E5`) | Thay bằng nút "Xem thêm" | Không mất gì đáng kể ở GĐ2 — **đây là ứng viên cắt tốt nhất** |
| **Không cắt được:** `E7` | Thiếu nó thì upload chết trên staging | — |
| **Không cắt được:** `F3` | Thiếu nó thì ISS-02 **vẫn đang mở**, và GĐ2 chưa xong (B.11 điều kiện 1) | — |

---

## 1. Trước khi gõ dòng đầu tiên

### 1.1 Điều kiện cần

| # | Kiểm | Kỳ vọng |
|---|---|---|
| 1 | `git log --oneline -1` | Khối A–D đã merge; `dotnet test` xanh, ba cổng `CI GATE` xanh |
| 2 | API local chạy | `dotnet run --project src/backend/SocialApp.Api` → `/swagger/profile-v1/swagger.json` và `/swagger/content-v1/swagger.json` trả 200 |
| 3 | Postgres + Redis dev | `docker compose -f deploy/docker-compose.dev.yml up -d` |
| 4 | **Khóa R2 dev** | `dotnet user-secrets list -p src/backend/SocialApp.Api` có `R2:Endpoint`, `R2:Bucket`, `R2:AccessKey`, `R2:SecretKey` trỏ bucket **`-dev`** (Mục 9.0). **Không** nằm trong `deploy/.env` |
| 5 | **CORS bucket `-dev`** | `AllowedOrigins` = `http://localhost:3000` (đúng dạng `scheme://host[:port]`, **không** dấu `/` cuối), `AllowedMethods` = `PUT`, `GET`, `AllowedHeaders` = `content-type`, `ExposeHeaders` = `etag` |
| 6 | Frontend | `cd src/frontend && pnpm install --frozen-lockfile`; `pnpm lint && pnpm typecheck && pnpm test && pnpm build` xanh cả bốn **trước khi** sửa dòng đầu tiên |
| 7 | Chrome đã cài | `pnpm test:e2e` chạy được (`channel: "chrome"`, Đ-E8) |

Điểm 4 và 5 là hai điểm **duy nhất** của khối E chạm hạ tầng ngoài. Thiếu chúng thì `E3`/`E4` không nghiệm thu được,
và không có cách nào biết là do code hay do cấu hình.

### 1.2 Bảy luật áp thẳng vào hai khối

1. **Next 16 và Base UI ở đây không giống bản trong trí nhớ.** `src/frontend/AGENTS.md` bắt đọc
   `node_modules/next/dist/docs/` trước khi viết code; component tra bằng `pnpm exec shadcn docs <tên>`. Mẫu trên
   mạng phần lớn là Radix (`asChild`) và sẽ sai — Base UI ghép bằng prop `render`.
2. **Không thêm route BFF nào.** Mười endpoint của GĐ2 đi qua proxy chung `/bff/api/[...path]` đã có
   (`app/bff/api/[...path]/route.ts` export đủ `GET|POST|PUT|PATCH|DELETE`). Thêm route là vi phạm Đ-E14 và luật
   frontend Mục 1 #7.
3. **Không sửa `src/backend/**` trong hai khối này.** Thấy hợp đồng lệch thực tế thì **dừng lại và báo nhóm** —
   sửa hợp đồng sau `D9` là mở lại thứ vừa chốt, và kéo theo `pnpm gen:api` + hai `ContractTests` + `.yaml` trong
   cùng commit. Ngoại lệ duy nhất: lỗi hợp đồng **blocking** đã thống nhất cả nhóm.
4. **Không có mock trình duyệt** (Đ-E7, đổi 2026-09-17). Dev chạy đủ FE + BE + Postgres + Redis + R2 `-dev`.
   `mocks/` chỉ phục vụ Vitest qua `msw/node`. **Cấm nghiệm thu trên mock** — cổng đóng trỏ API thật.
5. **Trình duyệt không bao giờ cầm token.** Không `localStorage`/`sessionStorage`/`document.cookie`; `fetch` chỉ ở
   `lib/api/http.ts`. `lib/upload/r2.ts` là ngoại lệ **mới** và **duy nhất** — xem Q-E3.
6. **Thêm một luật ESLint hay một cổng CI thì phải thử cho đỏ một lần rồi khôi phục** (luật frontend Mục 9);
   `git status` sạch trước và sau.
7. **Không log token, `uploadUrl`, presigned GET, hay link xác minh** — kể cả `console.log` tạm lúc dò lỗi. Presigned
   URL mang chữ ký; nó là thông tin nhạy cảm có hạn (Mục 11 DoD).

### 1.3 Ba thứ đã đổi mà trí nhớ dễ giữ bản cũ

| Trí nhớ hay giữ | Thực tế hiện tại |
|---|---|
| `lib/api/schema.d.ts` (một file) | Mỗi nhóm một thư mục: `lib/api/identity/`, `lib/api/profile/`, `lib/api/content/` — Identity đã dời 2026-09-19 |
| "Thêm module thì thêm script `gen:api:<module>`" | **Không thêm script nào.** `scripts/gen-api.mjs` suy từ glob `../backend/Modules/*/Presentation/*-v1.yaml`; cổng CI chạy lại `pnpm gen:api` rồi đòi **worktree sạch** |
| "FE có cờ bật mock" | Bỏ từ 2026-09-17. Không `NEXT_PUBLIC_API_MOCKING`, không `public/mockServiceWorker.js` |

### 1.4 Tám câu phải chốt trước khi gõ code

Cùng nếp `Q-D1`–`Q-D9` của khối D: nêu đề xuất kèm lý do, nhóm xác nhận, rồi **ghi ngược** vào tài liệu gốc trong
commit tương ứng. Chưa chốt thì không gõ đầu việc liên quan.

#### Q-E1 — Host R2 vào CSP lấy từ biến nào? ✅ **ĐẢO lại 2026-09-20 (nhóm chốt): dùng `R2__Endpoint`, KHÔNG sinh biến mới**

`connect-src`/`img-src` cần **một host cụ thể**; `proxy.ts` phải biết nó trước khi trang render, nên không suy được
từ `uploadUrl` mà API trả lúc chạy.

- **CHỐT HIỆN HÀNH:** đọc thẳng `R2__Endpoint` — biến API đã có sẵn trong `deploy/.env`, và container frontend đã
  thấy nhờ `env_file: [./.env]`. **Không phải thêm dòng nào vào `deploy/.env` trên server.** Hằng
  `R2_HOST_ENV = "R2__Endpoint"` đặt trong `proxy.ts` (không bao giờ vào bundle trình duyệt), **không** trong
  `lib/security/csp.ts`. Vẫn **không** tiền tố `NEXT_PUBLIC_` (luật frontend Mục 1 #12).
- **Đề xuất ĐẦU BUỔI (đã bỏ):** biến server riêng `R2_PUBLIC_HOST`. Hai lý do khi đó là (a) `R2__*` là không gian
  cấu hình của API, (b) cổng CI bundle grep chuỗi `R2__` nên dùng tên đó trong code FE là "tự đặt mìn dưới chân
  cổng của chính mình".
- **Vì sao đảo — (b) SAI, đã đo, không suy.** Đặt `R2_HOST_ENV = "R2__Endpoint"`, `pnpm build`, rồi chạy **đúng
  lệnh của cổng** (`grep -rlE "…|R2__|X-Amz-Signature" .next/static`) → **không file nào dính**. Chuỗi `R2__` chỉ
  nằm trong `.next/server`, vì `proxy.ts` là middleware và không bao giờ vào bundle trình duyệt. Còn (a) chỉ là lập
  luận đặt tên: với `process.env` của Node, `__` không được diễn giải gì thêm.
- **Cái giá đổi chiều.** Phương án cũ phải trả "hai biến một giá trị, có thể lệch" — chính Q-E1 đã ghi. Phương án
  hiện hành **không còn cái giá đó**, và đổi lấy một ràng buộc rẻ hơn: giữ hằng tên biến trong `proxy.ts`. Ràng
  buộc này **không có test** canh (không có cách viết test cho "đừng import module này từ client"); nó dựa vào cổng
  CI bundle và một chú thích tại chỗ ở `csp.ts`.
- **Dev local:** `pnpm dev` không tự có `R2__Endpoint` — `dotnet user-secrets` là kho của .NET, Node không đọc
  được, và key bên đó tên `R2:Endpoint` chứ không phải `R2__Endpoint` (Mục 1.1 điểm 4). Chép sang
  **`src/frontend/.env`**; FE dùng **một** file env duy nhất, **không** `.env.local` (chốt 2026-09-20).
- **~~Còn mở, phải đo~~ — ĐÃ ĐO xong ở `E7` (2026-09-20): `process.env` trong `proxy.ts` đọc LÚC CHẠY.** Cách đo:
  `pnpm build` với biến **vắng mặt** (build xanh) → chạy `node .next/standalone/server.js` với biến đặt **chỉ lúc
  chạy** → header `Content-Security-Policy` của `/login` có host R2 trong đúng `connect-src` và `img-src`.
  **Không cần** build-arg trong Dockerfile, **không cần** chuyển proxy sang runtime Node. Điều kiện để kết luận còn
  đúng: đọc biến **lười** bên trong `proxy()`, không ở tầng module. Chi tiết và bảng đột biến ở Đ-E17
  (`huong-dan-khoi-e-frontend.md`).
  *Lưu ý dựng lại phép đo:* bản build dùng `output: standalone` — `pnpm start` báo lỗi và không phục vụ; phải chạy
  `node .next/standalone/server.js` (và chép `.next/static` sang). Cổng khởi động của BFF (Đ-E14) cũng đòi
  `API_INTERNAL_URL`, `REDIS_URL`, `APP_ORIGIN`, `SESSION_ENCRYPTION_KEY` có mặt, nếu không nó từ chối phục vụ trước
  khi tới được CSP.

#### Q-E2 — `/onboarding` đặt ở đâu để guard hồ sơ không lặp vô hạn? ✅ **chốt 2026-09-20 theo đề xuất**

Guard hồ sơ nằm ở layout `(app)`; nhưng chính `/onboarding` cũng cần đăng nhập, và nếu nó nằm dưới guard hồ sơ thì
404 → chuyển `/onboarding` → 404 → chuyển `/onboarding` → …

- **Đề xuất:** thêm một route group lồng: `app/(app)/(with-profile)/layout.tsx` bọc `RequireProfile`, mọi trang cần
  hồ sơ nằm dưới nó; `app/(app)/onboarding/page.tsx` nằm **ngoài** group đó, tức là có `RequireAuth` mà không có
  `RequireProfile`. Route group không tạo segment URL nên đường dẫn không đổi.
- **Đã cân nhắc rồi loại:** `if (pathname === "/onboarding") return children` trong `RequireProfile`. Loại vì đó là
  một ngoại lệ bằng chuỗi — đổi tên route là guard hỏng lặng lẽ, và không có test nào bắt được.

#### Q-E3 — `XMLHttpRequest` có bị cấm như `fetch` không? ✅ **chốt 2026-09-20 theo đề xuất**

Luật hiện tại chỉ cấm `fetch`. `E4` dùng `XMLHttpRequest` (để có tiến trình upload — `fetch` không báo được), nên
**lỗ hổng là có thật**: sau `E4`, bất kỳ file nào cũng gọi ra ngoài origin bằng XHR mà không cổng nào kêu.

- **Đề xuất:** thêm `XMLHttpRequest` vào `no-restricted-globals` ở khối chung, và thêm một override **đứng sau**
  cho `lib/upload/**` tắt riêng luật đó. Nhắc lại cạm bẫy flat config (luật frontend Mục 10): override cùng tên rule
  **thay** hẳn options, không cộng dồn — phải spread `KIT` và danh sách gốc lại.
- **Thử cho đỏ:** thêm `new XMLHttpRequest()` vào `features/post/…` → `pnpm lint` đỏ; xóa đi → xanh.

#### Q-E4 — `ErrorContext` tách theo endpoint, không theo module — **lệch B.7** ✅ **chốt 2026-09-20 theo đề xuất, đã ghi vào** `giai-doan-2.md`

B.7 ghi "thêm ngữ cảnh `profile`, `post`, `upload`". Ba ngữ cảnh **không đủ**: `errorMessage` ánh xạ theo
`(ngữ cảnh, status)`, mà 403 mang nghĩa hoàn toàn khác nhau trên các endpoint của cùng module —
`POST /posts` 403 là *"chưa có hồ sơ hoặc thiếu quyền đăng bài"*, `PATCH /posts/{id}` 403 là *"không phải bài của
bạn"*, `PUT /users/me/avatar` 403 là *"khóa ảnh không phải của bạn"*.

- **Đề xuất:** bảy ngữ cảnh — `profile-read`, `profile-write`, `avatar`, `upload`, `post-create`, `post-read`,
  `post-write`. Ghi ngược vào B.7 bằng một mệnh đề "Lệch B.7 (nhóm chốt): …".

#### Q-E5 — Thêm component nào vào kit? ✅ **chốt 2026-09-20 theo đề xuất**

Kit hiện có: `alert`, `button`, `card`, `field`, `input`, `input-group`, `label`, `separator`, `skeleton`, `sonner`,
`spinner`, `textarea`. Thiếu cho GĐ2.

- **Đề xuất:** `pnpm exec shadcn add avatar alert-dialog radio-group progress badge` (chạy trong `src/frontend/`,
  bản **đã ghim** — không `pnpm dlx shadcn@latest`, Đ-E12). `avatar` cho `E3`/`E5`, `alert-dialog` cho xác nhận xóa
  (`E6`), `radio-group` cho ba mức riêng tư (`E4`), `progress` cho tiến trình (`E4`), `badge` cho nhãn "đã chỉnh
  sửa" (`E5`).
- **Ràng buộc:** `components/ui/**` nằm trong `.prettierignore` — **không** chạy Prettier lên chúng, để
  `shadcn add --diff` về sau không bị nhiễu. Không `shadcn eject`, không `shadcn apply` (nó đảo thứ tự dòng trong
  `globals.css`).

#### Q-E6 — Bản đồ route ✅ **chốt 2026-09-20 theo đề xuất**

| Đường | Group | Nội dung | Đầu việc |
|---|---|---|---|
| `/onboarding` | `(app)` | Đặt tên hiển thị + bio lần đầu | `E2` |
| `/me` | `(app)/(with-profile)` | Tài khoản (đã có) + hồ sơ + avatar + bài của mình | `E2`, `E3`, `E5` |
| `/compose` | `(app)/(with-profile)` | Composer đăng bài | `E4` |
| `/users/[userId]` | `(app)/(with-profile)` | Hồ sơ người khác + danh sách bài của họ | `E5` |
| `/posts/[postId]` | `(app)/(with-profile)` | Chi tiết bài, nút Sửa/Xóa theo `canEdit` | `E5`, `E6` |

`app/` chỉ **ráp**; mọi logic nằm ở `features/profile/` và `features/post/` (Đ-E13). **Tên theo màn, không theo
module backend** — `features/content/` là sai tên.

#### Q-E7 — `F2` đã đổi nội dung so với B.8 — **lệch B.8** ✅ **chốt 2026-09-20 theo đề xuất, đã ghi vào** `giai-doan-2.md`

B.8 ghi `F2` là *"Frontend trỏ staging thật, **bỏ mock**"*. Nhưng **mock trình duyệt đã bị bỏ từ GĐ1**
(Đ-E7 đổi 2026-09-17): không có `public/mockServiceWorker.js`, không có cờ, code app không import `@/mocks/*`.

- **Đề xuất:** `F2` giữ nguyên mã việc nhưng nội dung là: (a) **xác nhận** `mocks/` chỉ còn phục vụ Vitest bằng một
  lệnh grep, (b) phần chính là **kiểm tab Network trên staging**. Ghi ngược một mệnh đề vào B.8.
- **Lệnh xác nhận (a):** `grep -rn "@/mocks" app features components lib` → **không dòng nào**.

#### Q-E8 — Playwright có vào CI ở GĐ2 không? ✅ **chốt 2026-09-20 theo đề xuất**

- **Đề xuất: không** — giữ nguyên Đ-E8. E2E của GĐ2 cần API + Postgres + Redis + **khóa R2 thật**, và CI cố ý
  **không có** khóa R2 (Mục 10.2). Chạy local, `workers: 1`, kết quả dán vào PR kèm bản Chrome.
- **Kèm theo:** ảnh fixture (`e2e/fixtures/anh-nho.jpg`, `e2e/fixtures/anh-nho.png`) **commit vào repo**, không
  sinh lúc chạy — ảnh sinh động dễ không phải JPEG hợp lệ, và lỗi khi đó trông hệt lỗi CORS (Mục 10.5).

---

## 2. E1 — Codegen + api client + mở rộng `request()`

### Mục tiêu

Một bề mặt gọi API cho bảy đầu việc sau: kiểu **sinh từ hợp đồng**, một chỗ gọi `fetch`, một bảng thông điệp lỗi.
Hợp đồng đổi thì phải là **lỗi compile**, không phải lỗi runtime ở màn nào đó.

### Các bước

**Bước 1 — xác nhận file sinh đã đúng.** `pnpm gen:api` rồi `git status --porcelain -- .` phải **rỗng**. Hai file
`lib/api/profile/schema.d.ts` và `lib/api/content/schema.d.ts` đã được commit ở cổng mở (`084982b`) — bước này chỉ
để chắc chúng còn khớp hợp đồng sau `D9`.

**Bước 2 — `lib/api/types.ts`.** Thêm hai khối `import type { components } from "./profile/schema"` và
`"./content/schema"`, re-export đúng những kiểu màn cần:

| Từ `profile` | Từ `content` |
|---|---|
| `ProfileResponse`, `UpsertProfileRequest`, `SetAvatarRequest` | `PostResponse`, `PostPage`, `PostAuthor`, `PostMedia`, `PostPrivacy`, `CreatePostRequest`, `UpdatePostRequest`, `CreateUploadsRequest`, `UploadTicket`, `UploadPurpose`, `UploadFileDeclaration`, `MediaKeyDeclaration`, `ImageContentType` |

`ProblemDetails` có ở **cả ba** schema — chỉ re-export **một** bản (bản của `identity`, đã có); ba bản giống hệt
nhau vì cùng sinh từ `SharedKernelProblemDetailsFactory`.

**Bước 3 — `lib/api/http.ts`.** Mở rộng `RequestOptions`:

```ts
export type RequestOptions = {
  method?: "GET" | "POST" | "PUT" | "PATCH" | "DELETE"
  body?: unknown
  signal?: AbortSignal
}
```

Không đổi gì khác. 204 đã được xử đúng (`res.status === 204 ? undefined : await res.json()`), nên
`request<void>(…, { method: "DELETE" })` chạy ngay.

**Bước 4 — hai api client.** `lib/api/profile-api.ts` và `lib/api/content-api.ts`, theo đúng hình dạng
`auth-api.ts`. Mọi đường đi qua `BFF_ROUTES.api`; `userId`/`postId` **phải** `encodeURIComponent`. Ví dụ hình dạng
(không chép nguyên, đọc hợp đồng rồi viết):

```ts
export const profileApi = {
  get: (userId: string, signal?: AbortSignal) =>
    request<T.ProfileResponse>(`${BFF_ROUTES.api}/users/${encodeURIComponent(userId)}/profile`, { signal }),
  upsert: (b: T.UpsertProfileRequest) =>
    request<T.ProfileResponse>(`${BFF_ROUTES.api}/users/me/profile`, { method: "PUT", body: b }),
  setAvatar: (b: T.SetAvatarRequest) => /* PUT  /users/me/avatar  → 200 ProfileResponse */,
  removeAvatar: () => request<void>(`${BFF_ROUTES.api}/users/me/avatar`, { method: "DELETE" }),
}
```

Danh sách bài cần query string: `` `${BFF_ROUTES.api}/users/${encodeURIComponent(userId)}/posts?${params}` `` với
`params = new URLSearchParams()` chỉ chứa `cursor`/`limit` **khi có**. Proxy chung chuyển nguyên
`new URL(request.url).search` sang API, nên không cần làm gì thêm ở BFF.

**Bước 5 — `lib/api/messages.ts`.** Mở rộng `ErrorContext` theo Q-E4 và điền bảng. Câu cho từng
`(ngữ cảnh, status)` — dưới đây là bảng **đề xuất**, đối chiếu lại với `example` trong hai `.yaml` trước khi gõ:

| Ngữ cảnh | Status | Câu |
|---|---|---|
| `profile-read` | 404 | *(không hiện — 404 là **tín hiệu onboarding**, không phải lỗi; xem `E2`)* |
| `avatar` | 403 | "Ảnh này không thuộc về bạn. Hãy chọn lại ảnh." |
| `upload` | 403 | "Tài khoản của bạn chưa được phép đăng bài." |
| `post-create` | 403 | "Bạn cần hoàn tất hồ sơ trước khi đăng bài." |
| `post-create` | 409 | "Ảnh này đã được dùng trong một bài khác. Hãy chọn lại ảnh." |
| `post-read` | 404 | "Không tìm thấy bài viết." |
| `post-write` | 403 | "Không tìm thấy bài viết, hoặc bạn không có quyền với bài này." |

Dòng cuối là **có chủ đích**: hợp đồng trả 403 cho cả "bài của người khác" lẫn "bài đã xóa mềm" — một câu cho cả
hai thì FE không tiết lộ bài có tồn tại hay không (`B.7 E6`, Mục 12 nhóm Bảo mật).

### Test

- `lib/api/messages.test.ts`: mỗi ngữ cảnh mới ít nhất một ca; 500 **có** `traceId` hiện mã tra cứu; mất mạng ra
  câu chung.
- `lib/api/http.test.ts`: `PUT`/`PATCH`/`DELETE` đi đúng method; 204 trả `undefined`; 401 trên `/bff/api/*` gọi
  `onSessionExpired` **một** lần.
- Type: một file `lib/api/content/schema.test-d.ts` theo khuôn `identity/schema.test-d.ts` đã có — pin vài hình
  dạng dễ trôi (`PostPage.nextCursor` là `string | null`, `reactionCounts` là object chứ không `null`).

### Cạm bẫy đã biết

| Cạm bẫy | Triệu chứng | Chặn bằng |
|---|---|---|
| Khai tay `type PostResponse = {...}` cho nhanh | Hợp đồng đổi, không ai biết cho tới lúc chạy | Luật frontend Mục 12; code review |
| Re-export `ProblemDetails` từ cả ba schema | `Duplicate identifier` | Chỉ giữ một bản |
| Quên `encodeURIComponent` cho `userId`/`postId` | Proxy chung **từ chối** segment chứa `/` → 404 khó hiểu | Test một ca id có ký tự lạ |
| Gửi `nextCursor` mà tự sửa/tự dựng | 400 `errors.cursor` | FE chỉ truyền lại nguyên chuỗi (Đ-2.11) |
| Dùng `POST` cho `PUT /users/me/profile` | 405 hoặc 404 | Bảng Mục 8.1, và test |

---

## 3. E2 — Onboarding hồ sơ

### Mục tiêu

Giữ bất biến của Đ-2.4: **không có hồ sơ thì không đăng được bài**. FE là nơi duy nhất biến nó thành đường đi
không cưỡng lại được; server chỉ trả 403.

### Các bước

**Bước 1 — validation client** `lib/validation/profile.ts`, theo Đ-E5 (**nới hơn hoặc bằng** server, thông điệp
dùng **đúng câu của server**):

| Trường | Luật | Câu (chép từ `UpsertProfileRequestValidator`) |
|---|---|---|
| `displayName` | `trim()` rồi đo, `>= 2` và `<= 50` ký tự | "Tên hiển thị phải có từ 2 đến 50 ký tự." |
| `bio` | `<= 500` ký tự, **không** trim khi đo | "Giới thiệu tối đa 500 ký tự." |

Đo `.length` (UTF-16) như `string.Length` của .NET — **không** đếm byte ở đây (khác mật khẩu của GĐ1, nơi server
đếm byte UTF-8).

**Bước 2 — `features/profile/profile-store.ts`.** Store module theo đúng khuôn `lib/auth/token-store.ts`: trạng
thái `unknown | missing | ready | error` + `ProfileResponse | null` + `subscribe`. Lý do có store: header
(`AppHeader`) và composer đều cần tên/avatar, mà gọi lại `GET /users/{me}/profile` ở mỗi chỗ là ba request cho một
dữ liệu.

**Bước 3 — `features/profile/require-profile.tsx`.** Đứng **trong** `RequireAuth`:

```
me() → userId → profileApi.get(userId)
   ├─ 200 → store = ready(profile) → render children
   ├─ 404 → store = missing        → router.replace("/onboarding")
   ├─ 401 → đã có onSessionExpired lo (Đ-E14) — không xử ở đây
   └─ khác → store = error         → khung "Thử lại", KHÔNG đá về /login
```

`unknown` → `PageSkeleton`. **Không** render children ở bất kỳ trạng thái nào khác `ready` (cùng luật với
`RequireAuth`: không nháy nội dung).

**Bước 4 — `app/(app)/(with-profile)/layout.tsx`** bọc `RequireProfile` (Q-E2).

**Bước 5 — `features/profile/onboarding-form.tsx` + `app/(app)/onboarding/page.tsx`.** Form hai trường theo khuôn
`login-form.tsx`: `noValidate`, `FormAlert` cho lỗi cấp form, `TextField`/`Textarea` cho lỗi theo trường, `pending`
chặn gửi đôi. Thành công → `store.set(ready(res))` → `router.replace(safeNext(searchParams.get("next")) ?? "/me")`.

**Bước 6 — form sửa hồ sơ** (dùng chung component với Bước 5, khác nhãn nút). Đặt ở `/me`.

### Test

- `lib/validation/profile.test.ts`: bảng ngưỡng **viết tay số liệu** (1/2/50/51 ký tự; `"  An  "` hợp lệ vì 2 ký tự
  sau trim; `"   "` không hợp lệ), không tính từ hằng số.
- `features/profile/require-profile.test.tsx`: ba nhánh 404 / 200 / 500; ca 404 khẳng định **đã gọi**
  `router.replace("/onboarding")` và **chưa render** children một lần nào.
- `features/profile/onboarding-form.test.tsx`: 400 từ server hiện theo key `errors.displayName` **kể cả khi client
  đã kiểm qua** (Đ-E5); 429 hiện câu chung không đồng hồ đếm ngược.

### Cạm bẫy đã biết

| Cạm bẫy | Triệu chứng | Chặn bằng |
|---|---|---|
| `/onboarding` nằm dưới `RequireProfile` | Vòng lặp chuyển trang vô hạn, tab treo | Q-E2 — route group riêng |
| Coi 404 là lỗi và hiện `FormAlert` | Người dùng mới thấy "Không tìm thấy" rồi mới bị chuyển trang | 404 là **tín hiệu**, xử trước khi vào `errorMessage` |
| Form sửa hồ sơ **không gửi** `bio` khi người dùng để trống | `PUT` là thay thế toàn phần (Q-D3) → bio cũ **bị xóa** mà không ai định thế | Luôn gửi `bio` (chuỗi rỗng → gửi `null`); test một ca "sửa tên, bio giữ nguyên" |
| Client chặt hơn server (vd cấm ký tự đặc biệt trong tên) | Chặn nhầm người dùng hợp lệ, không có đường thoát | Đ-E5 — chỉ hai luật ở bảng Bước 1 |
| Render children lúc `unknown` rồi mới chuyển | Nháy nội dung của người chưa có hồ sơ | Test "chưa render children một lần nào" |

---

## 4. E3 — Avatar

### Mục tiêu

Chạy ba bước presign → `PUT` R2 → gắn key trên **một ảnh**, nơi mọi thứ còn nhỏ và đọc được, trước khi `E4` chạy nó
với mười ảnh, tiến trình và hàng đợi.

### Các bước

```
1. Người dùng chọn file
2. Kiểm client: loại ∈ {image/jpeg, image/png, image/webp} và 1..10 MB
   → sai: hiện "Chỉ nhận ảnh JPEG, PNG hoặc WebP, tối đa 10 MB mỗi ảnh." (đúng câu server), KHÔNG gọi API
3. POST /media/uploads { purpose: "avatar", files: [{ contentType, sizeBytes }] }   ← MỘT file
   → 201 [{ mediaKey, uploadUrl, expiresIn, requiredHeaders }]
4. PUT uploadUrl, body = File, header Content-Type = requiredHeaders["Content-Type"]
   → lỗi ở đây là lỗi MẠNG hoặc CORS hoặc chữ ký — KHÔNG phải lỗi hợp đồng
5. PUT /users/me/avatar { mediaKey }
   → 200 ProfileResponse (avatarUrl là presigned GET 15 phút) → cập nhật store
```

**Ba trạng thái lỗi riêng** (B.7 nói rõ, và đây là lý do duy nhất `E3` tồn tại trước `E4`):

| Bước hỏng | Câu cho người dùng | Người sửa nhìn vào đâu |
|---|---|---|
| 3 | Theo `errorMessage("upload", e)` | Quyền, allowlist, hạn mức |
| 4 | "Không tải được ảnh lên. Kiểm tra kết nối rồi thử lại." | **CORS bucket, chữ ký, CSP** — ISS-02 |
| 5 | Theo `errorMessage("avatar", e)` | Hợp đồng: 400 sai dạng key, 403 key của người khác |

Gộp ba câu thành một "tải ảnh thất bại" là tự bịt đường sửa: bước 4 và bước 5 hỏng vì hai nguyên nhân không liên
quan gì đến nhau.

**Bước `PUT` lên R2** dùng `lib/upload/r2.ts` — viết ở đây, `E4` dùng lại (xem Mục 5 Bước 2).

### Test

- Vitest: ba nhánh lỗi ra **ba câu khác nhau** (mock `msw/node` cho `/bff/api/media/uploads` và cho host R2).
- Kiểm tay trên `localhost:3000` với bucket `-dev`: ảnh lên thật, `avatarUrl` hiện, `DELETE` → 204 và avatar biến
  mất, `DELETE` lần hai vẫn 204.

### Cạm bẫy đã biết

| Cạm bẫy | Triệu chứng | Chặn bằng |
|---|---|---|
| Tự đặt header `Content-Length` trên XHR | Trình duyệt **chặn** header này; cảnh báo ở console, header không được gửi | Chỉ gửi `Content-Type`; `Content-Length` trình duyệt tự đặt từ body. `requiredHeaders["Content-Length"]` là để **đối chiếu**, không để gửi |
| Gửi kèm cookie khi `PUT` lên R2 (`withCredentials = true`) | Preflight/`PUT` đỏ, không có thông điệp rõ | `xhr.withCredentials = false` (mặc định) — R2 không đặt `Access-Control-Allow-Credentials` |
| Thêm header lạ (`x-requested-with`, `authorization`) vào `PUT` | Preflight đỏ: `AllowedHeaders` của bucket chỉ có `content-type` | Danh sách header gửi đi **đúng bằng** `requiredHeaders` |
| `contentType` gửi lúc presign khác `file.type` | R2 trả **403 SignatureDoesNotMatch**, trông hệt CORS sai | Lấy `contentType` từ chính `file.type`, không đoán từ đuôi tên |
| Lưu `avatarUrl` vào store rồi hiện mãi | Sau 15 phút ảnh vỡ, không lỗi nào trong log | Coi `avatarUrl` là **dữ liệu có hạn**: `onError` của `<img>` → gọi lại `profileApi.get` **một** lần |
| Chưa có hồ sơ mà gọi `PUT /users/me/avatar` | 403 khó hiểu | Q-D9 — `E2` đứng trước; `RequireProfile` đã chặn |

---

## 5. E4 — Composer đăng bài + upload thật có tiến trình

### Mục tiêu

Đóng bước 1–5 của SEQ-01 trên trình duyệt. Đây là **chỗ duy nhất** trong code trình duyệt được phép gọi ra ngoài
origin — và vì thế là chỗ ISS-02 lộ ra.

### Các bước

**Bước 1 — BR-01 phía client** trong `lib/validation/post.ts`, **cùng ngưỡng server, không chặt hơn** (Đ-E5). Chép
đúng ba câu của `PostContentPolicy`:

| Luật | Key lỗi | Câu |
|---|---|---|
| `body` ≤ 5000 ký tự | `body` | "Nội dung bài không được vượt quá 5000 ký tự." |
| ≤ 10 ảnh | `mediaKeys` | "Một bài chỉ được đính kèm tối đa 10 ảnh." |
| Rỗng cả chữ lẫn ảnh | `body` | "Bài đăng phải có nội dung hoặc ít nhất một ảnh." |
| Loại/dung lượng một ảnh | *(dưới ô chọn ảnh)* | "Chỉ nhận ảnh JPEG, PNG hoặc WebP, tối đa 10 MB mỗi ảnh." |

Thứ tự kiểm giống server: **ảnh trước, chữ sau** — 11 ảnh không kèm chữ phải ra lỗi `mediaKeys`, không phải `body`.

**Bước 2 — `lib/upload/r2.ts`.** Module **duy nhất** dùng `XMLHttpRequest`:

```ts
// eslint-disable-next-line no-restricted-globals -- Đ-E17: đây LÀ chỗ được phép gọi ra ngoài origin (PUT thẳng lên R2,
// Đ-2.5). fetch không báo được tiến trình upload; mọi nơi khác đi qua lib/api/http.ts.
```

Bề mặt hẹp, đúng một hàm: `putToR2({ url, file, contentType, signal, onProgress })` → `Promise<void>`, phân biệt
**ba** kết cục: 2xx (xong) · status ≠ 2xx (R2 từ chối — kèm status để log, **không** log `url`) · `onerror`/`ontimeout`
(mạng/CORS/CSP — đây là ca ISS-02). Không import gì của `features/`, không biết nghiệp vụ (Đ-E13).

**Bước 3 — hàng đợi** trong `features/post/use-upload-queue.ts`:

- **Một** lời gọi `POST /media/uploads` cho cả lô ≤ 10 ảnh (Đ-2.15) — không gọi 10 lần, hạn mức là 100 req/phút.
- **Song song tối đa 3.** Mỗi ảnh một dòng trạng thái: `chờ | đang gửi (n%) | xong | lỗi`.
- Ảnh lỗi **thử lại riêng nó**: nếu `uploadUrl` còn hạn (< 10 phút kể từ lúc presign) thì `PUT` lại **cùng URL**;
  hết hạn thì presign lại **một** file và nhận `mediaKey` mới.
- Bỏ một ảnh đã upload xong: FE **không làm gì** với R2 — object thành mồ côi, worker dọn sau 24 giờ (Đ-2.13).

**Bước 4 — commit.** Chỉ khi **mọi** ảnh ở trạng thái `xong`:

```
POST /posts { body?, privacy, mediaKeys: [{ mediaKey, contentType, sizeBytes }] }
```

Ba giá trị mỗi phần tử phải **đúng bằng** thứ đã khai lúc presign và đúng bằng file thật — server `HEAD` lên R2 rồi
đối chiếu (Đ-2.8 lớp 2). Thứ tự trong mảng là `position` hiển thị (0..9).

**Bước 5 — `privacy` bắt buộc.** `radio-group` ba lựa chọn, **không có mặc định ngầm** ở phía gửi: nếu UI có giá trị
chọn sẵn thì đó là lựa chọn của UI, còn thiếu trường khi gửi vẫn là 400 `errors.privacy`. Nhãn cho `friends` ở GĐ2
phải nói thật: *"Bạn bè — hiện chỉ mình bạn xem được, tính năng kết bạn sẽ có ở bản sau"* (Đ-2.9,
`AlwaysStrangers`).

### Test

Vitest (`msw/node`) — bốn ca Mục 10.5 dòng 1:

1. BR-01 client: bài rỗng không gọi API; 11 ảnh báo dưới ô ảnh.
2. 400 từ server hiện theo key `errors.body` / `errors.mediaKeys` / `errors.privacy`.
3. 403 (`post-create`) và 409 (`post-create`) ra đúng hai câu khác nhau.
4. **Một ảnh lỗi không hủy cả lô**: 3 ảnh, ảnh thứ 2 trả 403 từ host R2 giả → ảnh 1 và 3 vẫn `xong`, nút Đăng vẫn
   tắt, nút "Thử lại" chỉ hiện ở dòng ảnh 2; sau khi thử lại thành công thì nút Đăng bật.

### Cạm bẫy đã biết

| Cạm bẫy | Triệu chứng | Chặn bằng |
|---|---|---|
| Đẩy byte ảnh qua BFF | **413** từ `handleProxy` (`MAX_PROXY_BODY` = 1 MB) | Đ-2.5 — ảnh **không bao giờ** đi qua `/bff/*`. Con số 1 MB là chỗ nó cố ý vỡ |
| Gửi `POST /posts` khi còn ảnh đang lên | 400 "Ảnh chưa được tải lên xong…" (HEAD không thấy object) | Nút Đăng chỉ bật khi **mọi** ảnh `xong` |
| Bấm Đăng hai lần (mạng chậm) | Lần hai nhận **409** — ảnh đã gắn vào bài vừa tạo | Cờ `pending` chặn gửi đôi; 409 hiện câu "ảnh đã dùng ở bài khác" + buộc chọn lại ảnh (`POST /posts` **không** idempotent) |
| Hai ảnh giống hệt nhau trong một bài | 400 `errors.mediaKeys` "Một ảnh không được đính kèm hai lần." | Khử trùng theo `mediaKey` trước khi gửi — mỗi lần presign sinh key mới nên chỉ xảy ra khi retry sai |
| Đo `sizeBytes` sau khi xử lý ảnh | HEAD lệch → 400, mà thông điệp nói về loại/dung lượng | GĐ2 **không** cắt/nén/strip EXIF ở client (hoãn tới GĐ7) |
| `XMLHttpRequest` lọt ra ngoài `lib/upload/` | Lỗ Đ-E2 mở rộng dần | Q-E3 — luật ESLint, đã thử cho đỏ |
| Log `uploadUrl` khi dò lỗi | Chữ ký vào console, vào Sentry, vào ảnh chụp màn hình dán vào PR | Luật Mục 1.2 #7; grep PR |

---

## 6. E5 — Danh sách + chi tiết bài

### Mục tiêu

Chứng minh cursor của Đ-2.11 dùng được từ phía client — và chốt cách cuộn mà **GĐ4 (feed) sẽ chép lại**. Sai ở đây
thì GĐ4 viết lại phần cuộn vô hạn, đúng thứ Đ-2.11 sinh ra để tránh.

### Các bước

**Bước 1 — `features/post/use-post-page.ts`.** Trạng thái: `items: PostResponse[]`, `nextCursor: string | null`,
`pending`, `error`. Trang đầu gọi không `cursor`; trang sau gọi `?cursor=<nextCursor>&limit=20`.
`nextCursor === null` → hết, ẩn nút/ngắt observer. **FE không bao giờ dựng, sửa, hay diễn giải cursor.**

**Bước 2 — khử trùng.** Nối trang bằng `Map` theo `postId`, không `concat` thẳng: StrictMode gọi effect hai lần, và
người dùng bấm "Xem thêm" hai lần thì lô trùng sẽ hiện hai lần với cùng `key` React.

**Bước 3 — `features/post/post-card.tsx`.** Tác giả (`author.displayName` + `avatarUrl`), thời gian
(`Intl.DateTimeFormat("vi-VN", { timeZone: "Asia/Ho_Chi_Minh" })` như `me-profile.tsx`), nhãn **"đã chỉnh sửa"** khi
`editedAt != null`, lưới ảnh theo `position`, `commentCount`/`reactionCounts` hiện được nhưng **không** có nút nào
(GĐ3 mới có endpoint).

**Bước 4 — skeleton + trạng thái rỗng.** Tài khoản mới: một câu + nút "Đăng bài đầu tiên" trỏ `/compose`. Không để
màn trắng.

**Bước 5 — chi tiết** `/posts/[postId]`: `GET /posts/{postId}`; 404 → trang "không tìm thấy" dùng **đúng một câu**
cho cả "không tồn tại", "đã xóa" và "không được xem" (Mục 7.4 — cùng phản hồi, FE không được nói khác).

### Test

- Vitest: nối hai trang theo `nextCursor`; `nextCursor: null` → không còn nút; gọi trùng không sinh bài lặp;
  `reactionCounts: {}` render được (không `null`).
- Kiểm tay: 25 bài → cuộn hết đúng 25, không lặp, không thiếu.

### Cạm bẫy đã biết

| Cạm bẫy | Triệu chứng | Chặn bằng |
|---|---|---|
| Coi `nextCursor: ""` là "còn trang" | Vòng lặp gọi API | Hợp đồng: hết dữ liệu là **`null`**, không phải chuỗi rỗng (Đ-2.11) |
| Gửi `limit` ngoài `1..50` | 400 `errors.limit` | Hằng số một chỗ, mặc định 20 |
| Giữ `media[].url` trong state quá 15 phút | Ảnh vỡ trên tab mở lâu, **không lỗi nào trong log** | `onError` của `<img>` → gọi lại `GET /posts/{id}` **một** lần rồi mới bỏ cuộc. Đây cũng là bẫy mà GĐ4 phải nhớ ở tầng cache (Đ-2.9) |
| `key={index}` cho danh sách | Bài nhảy chỗ khi nối trang | `key={post.postId}` |
| Hiện hai câu khác nhau cho 404 "không tồn tại" và 404 "không được xem" | Status code tự khai tài nguyên có tồn tại | Một câu duy nhất |

---

## 7. E6 — Sửa / xóa bài của mình

### Mục tiêu

Đóng FR-005 phía người dùng, và giữ đúng luật *"FE không tự so id"*: quyền hiển thị nút đến từ **server**.

### Các bước

**Bước 1 — nút theo `canEdit`.** `{post.canEdit && <PostActions …/>}`. **Không** so `post.author.userId` với
`me.userId` ở bất kỳ đâu — grep `author.userId` trong `features/` phải không ra dòng nào dùng để quyết định quyền.
Lý do (Mục 8.2): FE so id thì mỗi chỗ render lặp một bản logic quyền, và sớm muộn chúng lệch nhau.

**Bước 2 — sửa.** Form `body` + `privacy`, khởi tạo từ bài hiện tại.

- Nút Lưu **tắt khi chưa đổi gì** — `PATCH` body `{}` là 400 "Không có gì để sửa.", một lỗi mà người dùng không
  hiểu vì họ có bấm gì đâu.
- Gửi **chỉ trường đã đổi**. `body: null` nghĩa là *"không gửi"*, không phải *"xóa chữ"*; muốn xóa chữ thì gửi `""`,
  và khi đó BR-01 quyết định (bài có ảnh → được; bài không ảnh → 400 `errors.body`).
- **Không** gửi `mediaKeys` — trường đó không tồn tại trong `UpdatePostRequest`, gửi vào là 400 field lạ. GĐ2 không
  sửa ảnh (Mục 7.3); UI **không có** nút thêm/bớt ảnh ở màn sửa.
- 200 → cập nhật bài trong danh sách đang cầm, hiện nhãn "đã chỉnh sửa" theo `editedAt` mới.

**Bước 3 — xóa.** `AlertDialog` xác nhận (không `window.confirm` — CSP và kit). 204 → rời khỏi `/posts/{id}` và
**gỡ bài khỏi mọi danh sách đang cầm**: sau xóa mềm, `GET /posts/{id}` trả 404 **kể cả với chính tác giả**
(Mục 7.3), nên để lại card cũ trên màn là để lại một liên kết chết.

**Bước 4 — 403.** Một câu **không tiết lộ**: "Không tìm thấy bài viết, hoặc bạn không có quyền với bài này."
Áp cho cả `PATCH` lẫn `DELETE`, cả bài của người khác lẫn bài đã xóa mềm.

### Test

- Vitest: `canEdit: false` → không có nút Sửa/Xóa trong DOM (khẳng định **vắng mặt**, không phải ẩn bằng CSS);
  Lưu tắt khi chưa đổi; `PATCH` gửi **đúng** trường đã đổi; 403 ra đúng một câu; sau 204 bài biến khỏi danh sách.
- Kiểm tay: xóa rồi mở lại URL cũ → trang "không tìm thấy", không phải lỗi 500.

### Cạm bẫy đã biết

| Cạm bẫy | Triệu chứng | Chặn bằng |
|---|---|---|
| Ẩn nút bằng CSS thay vì không render | Người dùng bật DevTools là bấm được (server vẫn chặn, nhưng UI đang nói dối) | Test khẳng định phần tử **không có** trong DOM |
| Gửi cả `body` lẫn `privacy` dù chỉ đổi một | Không sai hợp đồng, nhưng `edited_at` bị đóng dấu cho thay đổi không tồn tại | So với giá trị ban đầu trước khi gửi |
| Hiện "Bài đã bị xóa" cho 403 | Tiết lộ bài có tồn tại | Một câu chung (Bước 4) |
| Sau xóa vẫn giữ card trong danh sách | Bấm vào → 404 | Gỡ khỏi state ngay sau 204 |

---

## 8. E7 — Nới CSP cho R2 + ghi **Đ-E17**

### Mục tiêu

`connect-src` hiện là `'self'`, `img-src` là `'self' blob: data:`. Trình duyệt phải `PUT` thẳng lên R2 (Đ-2.5) và
hiển thị ảnh từ presigned GET (Đ-2.9) — cả hai đều bị CSP hiện tại chặn. **Nới CSP là quyết định mới** (luật
frontend Mục 1 #13), nên nó đi kèm một `Đ-E*` có ngày tháng, ghi **trong cùng commit**.

### Các bước

**Bước 1 — `buildCsp` nhận thêm tham số.** Không hằng số gõ tay trong `csp.ts`:

```ts
export function buildCsp(nonce: string, { dev, r2Host }: { dev: boolean; r2Host: string | null }): string
```

`r2Host` thêm vào **đúng hai** chỉ thị: `connect-src` (lượt `PUT`) và `img-src` (lượt `GET`). **Không** thêm vào
`default-src`, `script-src`, `form-action` — host R2 phục vụ nội dung do người dùng tải lên; cho nó chạy script là
đúng lý do SVG bị loại khỏi allowlist (Mục 2).

**Bước 2 — `proxy.ts`** đọc biến theo Q-E1 và truyền xuống. `r2Host` rỗng/thiếu → truyền `null`; production thiếu
thì ném lỗi **nêu tên biến** (cùng tinh thần "thiếu cấu hình = từ chối chạy").

**Bước 3 — kiểm dạng giá trị.** Hàm thuần nhỏ: phải là `https://host` (hoặc `http://host[:port]` ở dev), **không**
path, **không** `/` cuối. Cùng cạm bẫy đã đốt một lần ở `APP_ORIGIN` và `Cors__AllowedOrigins`: lệch một ký tự thì
trình duyệt chặn **im lặng** và triệu chứng trông hệt ký sai chữ ký.

**Bước 4 — `deploy/.env.example`** thêm biến mới kèm một dòng chú thích (chỉ **tên**, không giá trị).

**Bước 5 — ghi Đ-E17** vào `docs/giai-doan-1/huong-dan-khoi-e-frontend.md`, ngay sau Đ-E16, mở bằng
*"**Đ-E17** — … *(chốt 2026-09-…, nhóm chốt; nới Đ-E15)*"*. Nội dung tối thiểu: **vì sao** phải nới (Đ-2.5 + Đ-2.9)
· **nới đúng hai chỉ thị nào** và vì sao không nới thêm · **host lấy từ biến nào** và kết quả kiểm của Q-E1 (biến
đọc được lúc chạy hay phải có lúc build) · **giá phải trả** (ảnh của người dùng phục vụ từ domain bên thứ ba; SVG
vẫn bị loại) · **bảng đột biến**.

### Test

`lib/security/csp.test.ts` (file đã có khuôn) — mỗi dòng dưới đây thử **một** đột biến, cho đỏ rồi khôi phục:

| Đột biến | Test phải bắt |
|---|---|
| Bỏ host R2 khỏi `connect-src` | Ca "PUT lên R2 được phép" |
| Bỏ host R2 khỏi `img-src` | Ca "ảnh presigned hiện được" |
| Thêm host R2 vào `script-src` | Ca "host R2 KHÔNG được chạy script" |
| `r2Host = null` mà vẫn ghép chuỗi | Ca "thiếu biến thì chỉ thị không có chuỗi rỗng thừa" |
| Host có `/` cuối hoặc có path | Ca kiểm dạng ở Bước 3 |

E2E `e2e/csp.spec.ts` thêm một ca: trên trang `/compose`, thực hiện một `PUT` thật lên R2 và khẳng định **không có
vi phạm CSP nào** (`page.on("console")` + sự kiện `securitypolicyviolation`).

### Cạm bẫy đã biết

| Cạm bẫy | Triệu chứng | Chặn bằng |
|---|---|---|
| Thử ở dev thấy chạy rồi kết luận xong | Dev và production khác nhau ở `script-src`/`style-src`; `connect-src` **giống hệt** — nhưng biến môi trường thì khác | Kiểm trên bản `pnpm build && pnpm start` (Q-E1 Bước kiểm) |
| Nới bằng `*` hoặc `https:` | Mất gần hết giá trị của CSP | Một host cụ thể |
| Quên `img-src`, chỉ thêm `connect-src` | Upload chạy, ảnh không hiện — và không ai nối hai triệu chứng lại | Hai ca test riêng |
| Ghi Đ-E17 ở commit sau | Luật frontend Mục 11 #4 | Cùng commit với code |

---

## 9. E8 — Vitest + Playwright cho lát cắt mới

### Mục tiêu

Biến sáu màn thành thứ **chặn merge** (Vitest ở CI) và thành **bằng chứng chạy tay có ghi lại** (Playwright local).

### Các bước

**Bước 1 — rà Vitest theo Mục 10.5 dòng 1.** Bốn nhóm bắt buộc, phần lớn đã viết cùng `E2`–`E6`; ở đây chỉ kiểm
xem còn thiếu dòng nào. File test nằm **cạnh mã nguồn**; `test/` chỉ chứa `setup.ts`.

**Bước 2 — mở rộng `mocks/`.** `mocks/handlers.ts` thêm bề mặt `/bff/api/users/*`, `/bff/api/posts/*`,
`/bff/api/media/uploads`, và **host R2 giả** cho lượt `PUT`. Fixture chép **giá trị** từ `example` của hai `.yaml`
và gắn kiểu bằng `satisfies` — hợp đồng đổi hình dạng thì mock **đỏ compile**. Không parse yaml lúc chạy.
Kịch bản chọn bằng **dữ liệu nhập**, không bằng cờ ẩn (nếp đã có: `SCENARIO_EMAILS`) — ví dụ `sizeBytes` đặc biệt
kích nhánh 400, `mediaKey` đặc biệt kích 409.

**Bước 3 — ảnh fixture.** `e2e/fixtures/anh-nho.jpg` và `.png`, mỗi file vài KB, **commit vào repo** (Q-E8).

**Bước 4 — Playwright**, `workers: 1`, sáu ca của Mục 10.5 dòng 2:

| Spec | Khẳng định |
|---|---|
| `onboarding.spec.ts` | Tài khoản mới bị đưa về `/onboarding`; gõ thẳng `/compose` vẫn bị đưa về |
| `post-create.spec.ts` | Đăng bài **2 ảnh thật** từ `e2e/fixtures/`; bài hiện với 2 ảnh |
| `post-edit-delete.spec.ts` | Sửa → nhãn "đã chỉnh sửa"; xóa → mở lại URL cũ ra trang không tìm thấy |
| `post-forbidden.spec.ts` | Tài khoản B mở `/posts/{bài private của A}` → **một** câu, không lộ tồn tại |
| `csp.spec.ts` *(mở rộng)* | **CSP không chặn `PUT` lên R2** |
| `login-storage.spec.ts` *(mở rộng)* | Web Storage vẫn trống sau khi đăng bài có ảnh |

**Bước 5 — dán kết quả vào PR** kèm **bản Chrome đã chạy** (Đ-E8: dùng Chrome hệ thống, bản khác nhau giữa các máy).

### Cạm bẫy đã biết

| Cạm bẫy | Triệu chứng | Chặn bằng |
|---|---|---|
| Chạy Playwright song song | 429 hàng loạt (hạn mức theo IP) — test đỏ mà server không sai | `workers: 1`, `fullyParallel: false` (đã cấu hình) |
| Sinh ảnh lúc chạy | Byte không phải JPEG hợp lệ → R2 vẫn nhận, nhưng lỗi trông hệt CORS | Ảnh thật trong `e2e/fixtures/` |
| E2E để lại bài rác trên bucket `-dev` | Bucket phình dần | Spec tự xóa bài ở bước cuối; phần còn lại worker dọn sau 24 giờ |
| `onUnhandledRequest: "error"` của MSW chặn lượt `PUT` tới R2 giả | Test đỏ vì thiếu handler, không phải vì code sai | Thêm handler cho host R2 trong `mocks/handlers.ts` |

---

## 10. F1 — Deploy staging qua CD tự động

### Mục tiêu

Ba module, ba schema và bốn khóa R2 lần đầu chạy cùng nhau trên server thật, qua đường CD chính thức — **không**
SSH sửa tay.

### Trước khi merge — phải có trên `deploy/.env` của server

| Biến | Ghi chú |
|---|---|
| `R2__Endpoint` | `https://<account-id>.r2.cloudflarestorage.com` — **không** dấu `/` cuối |
| `R2__Bucket` | `socialmedia-staging` (**không** phải `-dev`) |
| `R2__AccessKey` / `R2__SecretKey` | Token phạm vi **chỉ** bucket `-staging` |
| Biến host R2 của FE (Q-E1) | Phần host của `R2__Endpoint` |

App **từ chối khởi động khi thiếu cấu hình ngoài Development** — thiếu một dòng là api crash-loop ngay sau khi
merge, và người merge không có cách nào biết trước nếu PR không nói (luật PR Mục 5).

### Các bước

1. Xác nhận CORS bucket **`-staging`**: `AllowedOrigins` = `https://mxh.banhgao.net`, `AllowedMethods` =
   `PUT`, `GET`, `AllowedHeaders` = `content-type`, `ExposeHeaders` = `etag`.
2. Merge → CD build hai image arm64 → GHCR → chạy service `migrate` → `up`.
3. **Kiểm migrate cho cả ba module:**
   ```sql
   select table_schema, count(*) from information_schema.tables
   where table_name = '__EFMigrationsHistory' group by table_schema;
   -- phải thấy: identity, profile, content
   ```
4. `curl -fsS https://mxh.banhgao.net/health/ready` → 200.
5. `curl -fsS https://mxh.banhgao.net/swagger/profile-v1/swagger.json | head -c 200` và bản `content-v1` → JSON, 200.
6. `https://mxh.banhgao.net/login` → 200, header `Content-Security-Policy` **có host R2** (đây là bằng chứng Q-E1
   chạy đúng trên bản build).

### Cạm bẫy đã biết

| Cạm bẫy | Triệu chứng | Chặn bằng |
|---|---|---|
| Khóa bucket `-dev` lọt vào `deploy/.env` | Ảnh của staging ghi vào bucket dev, không ai thấy | Đ-2.14 — dev dùng `user-secrets`; kiểm `R2__Bucket` trên server |
| Quên chạy `migrate` trước `up` | Api 500 ở mọi endpoint của Profile/Content | Bước 2 theo đúng thứ tự CD đã có |
| Biến host R2 của FE chỉ đặt lúc build | CSP trên staging không có host R2 → upload chết | Bước 6 kiểm bằng header thật |
| `AllowedOrigins` có `/` cuối | Trình duyệt chặn im lặng, trông hệt ký sai chữ ký | Kiểm bằng mắt trên dashboard |

---

## 11. F2 — Frontend trỏ staging thật

> **Lệch B.8 (Q-E7):** mục này **không còn** việc "bỏ mock" — mock trình duyệt đã bỏ từ GĐ1 (Đ-E7, 2026-09-17).
> Phần còn lại là **xác nhận** và **kiểm tab Network**.

### Các bước

1. **Xác nhận `mocks/` chỉ phục vụ Vitest:** `grep -rn "@/mocks" app features components lib` → không dòng nào.
   `ls src/frontend/public/` → không có `mockServiceWorker.js`.
2. **Kiểm tab Network trên `https://mxh.banhgao.net`** trong một lượt đăng bài có ảnh. Bốn điều phải đúng:

   | Kiểm | Kỳ vọng |
   |---|---|
   | Danh sách origin được gọi | **Chỉ** `https://mxh.banhgao.net` (`/bff/*`) và host R2 (`PUT`, `GET` ảnh) |
   | Header `Authorization` | **Không** xuất hiện trong bất kỳ request nào của trình duyệt |
   | Web Storage (Local + Session) | Trống; Cookies chỉ có `__Host-sid` |
   | Response của `/bff/api/posts/*` | **Không** chứa `mediaKey`/`storage_key` — chỉ `url` đã ký |
3. **Kiểm `Set-Cookie`:** không response nào từ `/bff/*` mang cookie refresh của API (danh sách trắng header của
   `relay()` đã chặn — đây là xác nhận, không phải sửa).

### Cạm bẫy đã biết

| Cạm bẫy | Triệu chứng | Chặn bằng |
|---|---|---|
| Kiểm bằng `curl` cho nhanh | `curl` không bao giờ thấy CORS, CSP, Web Storage | Mục này **chỉ** nghiệm thu trên trình duyệt |
| Kiểm ở tab đã mở sẵn từ trước khi deploy | Bundle cũ trong cache | Tải lại cứng (Ctrl+Shift+R), hoặc cửa sổ ẩn danh |

---

## 12. F3 — E2E lát cắt + bằng chứng ISS-02

### Mục tiêu

**Đây là lúc ISS-02 được đóng lại — hoặc lộ ra.** `curl` luôn xanh; chỉ trình duyệt thật, trên domain HTTPS thật,
mới nói được sự thật về CORS.

### Kịch bản (một lượt, một tài khoản mới)

```
đăng ký → mail thật → xác minh → đăng nhập
  → onboarding (đặt tên hiển thị)
  → đặt avatar
  → đăng bài kèm 2 ảnh
  → xem lại bài (ảnh hiện)
  → sửa bài (nhãn "đã chỉnh sửa")
  → xóa bài (mở lại URL cũ → không tìm thấy)
```

### Bằng chứng **phải giữ** (dán vào PR)

| # | Bằng chứng | Phải thấy gì |
|---|---|---|
| 1 | Ảnh tab Network lượt `PUT` lên R2 | **Status 200**, và header đã ký trong Request Headers (che phần chữ ký khi chụp) |
| 2 | Ảnh dashboard R2 bucket `-staging` | Object nằm dưới `posts/{userId}/…` và `avatars/{userId}/…` |
| 3 | Ảnh trang bài | Hai ảnh hiển thị thật, không placeholder |
| 4 | Ảnh Network lượt `GET` ảnh | 200 từ host R2 với presigned GET |
| 5 | Một URL ảnh **đã hết hạn** mở lại | R2 trả **403** (Mục 12 nhóm Bảo mật) |

Che chữ ký trước khi chụp: ảnh dán vào PR là công khai với cả tổ chức và **không xóa được** khỏi lịch sử thông báo
(luật PR Mục 7).

### Nếu đỏ — ứng phó **ngay trong ngày**

Thứ tự loại trừ, rẻ trước:

1. **CSP?** Console có dòng `Refused to connect to … because it violates … connect-src` → lỗi `E7`, không phải R2.
2. **CORS?** Request `PUT` không có response, Network hiện `(failed)`, console nói `CORS policy` → `AllowedOrigins`
   của bucket `-staging` lệch (dấu `/` cuối, sai scheme, sai host).
3. **Chữ ký?** R2 trả **403 `SignatureDoesNotMatch`** → header gửi đi lệch với header đã ký: `Content-Type` khác,
   hoặc FE tự thêm header ngoài `requiredHeaders`.
4. **Hết hạn?** 403 sau 10 phút kể từ presign → `uploadUrl` quá hạn, presign lại.

Vẫn đỏ sau bốn bước → **kích hoạt phương án ứng phó ISS-02 của Mục 14**: đổi `IObjectStorage` sang hiện thực lưu
volume VPS, giữ nguyên bảng metadata để chuyển lại R2 sau. Nhờ có interface, đây là thay **một class**, không phải
viết lại luồng. Đừng để sang GĐ4.

---

## 13. F4 — Checklist nghiệm thu + Definition of Done

### Mục tiêu

Rà bằng cách **kiểm tận nơi**, không suy đoán từ CI xanh. Dòng nào không áp dụng thì ghi lý do kèm địa chỉ hoãn —
**không xóa dòng**.

### Bốn dòng bắt buộc chạy lệnh, không tick bằng trí nhớ

| Dòng Mục 12 | Lệnh / thao tác |
|---|---|
| Không FK chéo schema | `select * from information_schema.referential_constraints` — đối chiếu schema hai đầu của từng ràng buộc |
| `--migrate` idempotent | Chạy service `migrate` **lần thứ hai**: exit 0, không đổi gì |
| Bucket không public | Mở URL ảnh **đã hết hạn** → 403 (cũng là bằng chứng #5 của `F3`) |
| Worker dọn rác | Bật một lượt trên staging, đọc log: số object đã xóa + số byte thu hồi. Rồi **tắt Redis** → worker **bỏ lượt**, api vẫn phục vụ |

### Definition of Done (Mục 11) — sáu mục, áp cho **từng** UC

Chú ý hai mục dễ tick ẩu:

- *"Đã chạy thử trên **staging** bằng tài khoản thật, qua domain HTTPS"* — phụ thuộc `F1`–`F3`; không tick trước.
- *"Log **không** chứa presigned URL"* — kiểm bằng `docker compose logs api | grep -c "X-Amz-Signature"` → **0**.
  Cổng CI chỉ grep bundle trình duyệt, không grep log runtime.

---

## 14. F5 — Đóng băng hợp đồng + bàn giao

### Các bước

1. **Thông báo nhóm:** `profile-v1.yaml` và `content-v1.yaml` **đóng băng cho phạm vi GĐ2**. Mọi đổi hình dạng sau
   mốc này thuộc giai đoạn sau + cổng mở của giai đoạn đó.
2. **Liệt kê phần hoãn có địa chỉ** (Mục 2) kèm giai đoạn nhận — để không có nợ vô chủ. Ít nhất: bình luận/cảm xúc
   (GĐ3) · feed + `friends` thật (GĐ4) · media tin nhắn (GĐ5) · ẩn/gỡ bài BR-07 (GĐ6) · cắt/nén ảnh + sửa ảnh của
   bài (GĐ7) · xóa cứng + dọn `profiles`/`posts` khi xóa tài khoản (GĐ8).
3. **Nhắc hai bẫy chéo giai đoạn cho GĐ4**, ghi thẳng vào tài liệu GĐ4 ngay khi mở giai đoạn:
   - Cache feed lưu **`storage_key`**, **không** lưu URL đã ký. Cache TTL 30s mà lưu URL ký 15 phút thì sau 15 phút
     cache trả URL hết hạn → ảnh vỡ mà log không có lỗi nào (Đ-2.9).
   - `IFriendshipReader` đổi **một dòng DI** sang hiện thực thật của SocialGraph; **không** chạm module Content.
4. **Xác nhận ba điều kiện B.11:**

   | # | Điều kiện | Bằng chứng |
   |---|---|---|
   | 1 | Một bài có ảnh thật tồn tại trên staging, upload đi thẳng từ trình duyệt | `F3` bằng chứng #1–#3 |
   | 2 | Sáu dòng AuthZ matrix mới xanh, và **đã từng thấy đỏ** khi cố tình bỏ kiểm ownership | Bảng đột biến của `B3` (`d5b0f49`) |
   | 3 | CI xanh cả năm nhóm | Link CI run của PR |

5. **GĐ4 được phép bắt đầu.**

---

## 15. Kế hoạch commit

Scope `gd2-e` cho khối E, `gd2-f` cho khối F (luật commit Mục 3). Mỗi commit có dòng `Test:` và dòng
`detect-changes:` (chạy `node .gitnexus/run.cjs detect-changes --scope all --repo .` trước **mọi** commit; `partial`
hay `truncated` **không phải** kết quả sạch — chạy lại).

| # | Tiêu đề đề xuất | Ghi chú thân bài |
|---|---|---|
| 1 | `feat(gd2-e): E1 — api client hai module, request() nhận PUT/PATCH/DELETE` | Nêu **Lệch B.7** của Q-E4 (bảy ngữ cảnh lỗi thay vì ba) |
| 2 | `feat(gd2-e): E7 — CSP mở đúng hai chỉ thị cho host R2, chốt Đ-E17` | Đ-E17 ghi vào hướng dẫn khối E GĐ1 **trong chính commit này**; nêu tên biến mới, **không** nêu giá trị |
| 3 | `feat(gd2-e): E2 — onboarding bắt buộc, không có hồ sơ thì không vào được app` | Nêu Q-E2 (route group) |
| 4 | `feat(gd2-e): E3 — avatar ba bước, ba trạng thái lỗi riêng` | Ảnh Network lượt `PUT` lên bucket `-dev` |
| 5 | `feat(gd2-e): E4 — composer đăng bài, upload thẳng lên R2 có tiến trình` | Nêu Q-E3 (luật ESLint cho `XMLHttpRequest`, đã thử cho đỏ) |
| 6 | `feat(gd2-e): E5 — danh sách và chi tiết bài theo cursor keyset` | — |
| 7 | `feat(gd2-e): E6 — sửa và xóa bài theo canEdit của server` | — |
| 8 | `test(gd2-e): E8 — Vitest cho lát cắt GĐ2, sáu spec Playwright chạy local` | Số Vitest trước → sau; kết quả Playwright + **bản Chrome** |
| 9 | `docs(gd2-f): F4 + F5 — tick Mục 11 và Mục 12, đóng băng hai hợp đồng` | `F1`–`F3` không sinh commit code; bằng chứng nằm ở mô tả PR |

**Tách `E7` lên sớm (commit #2)** là có chủ đích — xem Mục 0.3: nó loại một trong hai nghi phạm trước khi `E3` bắt
đầu dò lỗi upload.

---

## 16. Checklist nghiệm thu hai khối

**Khối E**

- [ ] Không `fetch` ngoài `lib/api/http.ts`; không `XMLHttpRequest` ngoài `lib/upload/r2.ts` (ESLint đã thử cho đỏ)
- [ ] Không `localStorage` / `sessionStorage` / `document.cookie`; Web Storage trống sau một lượt đăng bài
- [ ] Không route BFF mới; mọi lời gọi đi qua `/bff/api/...`
- [ ] Kiểu API lấy từ `lib/api/types.ts`; `grep -rn "type .*Response = {" features lib/api/*.ts` không ra kiểu tự khai
- [ ] `grep -rn "author.userId" features` không có chỗ nào dùng để quyết định quyền (`canEdit` là nguồn duy nhất)
- [ ] File mới đặt đúng tầng; `features/profile/` và `features/post/` **không** import chéo nhau
- [ ] UI dùng kit và token; không màu thô; component mới thêm bằng `pnpm exec shadcn add` (bản ghim)
- [ ] `pnpm lint`, `pnpm typecheck`, `pnpm test`, `pnpm build` xanh cả bốn
- [ ] `pnpm gen:api` xong worktree sạch
- [ ] Đ-E17 đã ghi vào hướng dẫn khối E của GĐ1, trong cùng commit với code CSP
- [ ] Không `console.log` token, `uploadUrl`, presigned GET, hay link xác minh
- [ ] Biến mới của BFF là biến server (không `NEXT_PUBLIC_`), có trong `deploy/.env.example`, production thiếu thì báo tên biến

**Khối F**

- [ ] `R2__*` + biến host R2 của FE đã có trên `deploy/.env` của server **trước khi merge**
- [ ] `migrate` xanh cho cả ba schema; chạy lần hai không đổi gì
- [ ] `/health/ready` 200; hai file `swagger.json` mới 200; header CSP trên staging **có host R2**
- [ ] Tab Network staging: chỉ `/bff/*` + host R2; không `Authorization`; không `storage_key` của ai
- [ ] Năm bằng chứng của `F3` đã dán vào PR, chữ ký đã che
- [ ] Mục 12 tick hết hoặc ghi lý do hoãn kèm địa chỉ; Mục 11 tick đủ sáu
- [ ] `docker compose logs api | grep -c "X-Amz-Signature"` = **0**
- [ ] Hai hợp đồng tuyên bố đóng băng; phần hoãn có địa chỉ đã liệt kê; hai bẫy chéo giai đoạn đã ghi cho GĐ4
- [ ] Mô tả PR **sạch bút ký** (luật PR Mục 7); không giá trị secret nào trong mô tả

---

## 17. Hai khối để lại gì

| Di sản | Ai thừa hưởng |
|---|---|
| `lib/upload/r2.ts` — upload thẳng lên object storage có tiến trình, hủy được, thử lại từng file | **GĐ5** (media tin nhắn) — dùng lại, không viết lại |
| Khuôn `use-post-page.ts`: cursor keyset + khử trùng + skeleton + trạng thái rỗng | **GĐ4** (feed), **GĐ5** (lịch sử hội thoại) |
| `RequireProfile` + route group `(with-profile)` | Mọi màn từ GĐ3 trở đi cần hồ sơ |
| `profile-store` (tên + avatar dùng chung cho header/composer/card) | GĐ3, GĐ6 (thông báo) |
| Đ-E17 — khuôn "nới CSP cho một host bên thứ ba": nới đúng chỉ thị cần, host từ biến server, test từng chỉ thị | GĐ5 (nếu có CDN), GĐ7 |
| Bảy ngữ cảnh lỗi trong `messages.ts` | Mọi module sau — thêm module là thêm dòng, không đổi khuôn |
| Bằng chứng ISS-02 đã đóng | Cả dự án: rủi ro đăng ký từ GĐ0 được gạch tên |

---

## 18. Ranh giới — cái gì **không** thuộc hai khối này

| Việc | Thuộc về |
|---|---|
| Sửa bất cứ gì trong `src/backend/**` | Khối D — đã đóng. Hợp đồng lệch thì **dừng và báo nhóm** (Mục 1.2 luật 3) |
| Thêm route dưới `app/bff/**` | Không ai — proxy chung đã đủ cho mười endpoint (Đ-E14) |
| Bình luận, cảm xúc (UI lẫn endpoint) | **GĐ3.** GĐ2 hiện `commentCount`/`reactionCounts` nhưng **không** nút nào |
| News feed, trang chủ có dòng thời gian | **GĐ4** |
| Mức riêng tư `friends` hoạt động thật | **GĐ4** — GĐ2 chỉ ghi nhãn nói thật cho người dùng |
| Sửa danh sách ảnh của bài đã đăng | **GĐ7** (Mục 7.3) |
| Cắt/nén ảnh phía client, thumbnail, strip EXIF | **GĐ7** |
| Xóa object R2 từ phía FE | Không ai — worker dọn (Đ-2.10, Đ-2.13) |
| Playwright vào CI | Không ở GĐ2 (Q-E8, Đ-E8) |
| Đổi `MAX_PROXY_BODY` để đẩy ảnh qua BFF | Không ai — con số đó cố ý là chỗ vỡ (Đ-2.5) |
