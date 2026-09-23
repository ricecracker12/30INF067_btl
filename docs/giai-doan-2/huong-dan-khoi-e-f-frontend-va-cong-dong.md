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
- **Đã thêm đủ (chốt lại 2026-09-21, sau `E8`):** sáu component vào kit, **mỗi cái đi cùng đầu việc dùng nó**
  chứ không thêm một lượt — `avatar` (`E3`) · `radio-group`, `progress` (`E4`) · `badge` (`E5`) ·
  `alert-dialog` (`E6`). Thêm sớm cả sáu là mấy file `components/ui/**` không ai import, và `shadcn add --diff`
  về sau không phân biệt được "chưa dùng" với "đã sửa tay". Kit GĐ2 vì thế có **17** component.
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
- **Lệnh xác nhận (a)** *(sửa 2026-09-21 khi thi công `E8`)*: lệnh cũ `grep -rn "@/mocks" app features
  components lib` ra **44 dòng** và tất cả đều là file `.test.tsx` — luật thật là **code app** không import
  mocks, còn file test thì phải import. Lệnh đúng:

  ```
  grep -rn "@/mocks" app features components lib --include="*.ts" --include="*.tsx" | grep -v "\.test\."
  ```

  → **không dòng nào** (đã chạy 2026-09-21). Giữ nguyên lệnh cũ là `F2` dừng vì một cổng sai, và người chạy
  sẽ đi sửa thứ không hỏng.

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

### Thực tế thi công

**Bằng chứng.** Vitest **325 → 361** (+36: `lib/upload/r2.test.ts` 5, `lib/validation/media.test.ts` 18,
`features/profile/avatar-card.test.tsx` 13). `pnpm lint`, `typecheck`, `test`, `build` xanh cả bốn; `pnpm gen:api`
xong `git status --porcelain -- lib/api/*/schema.d.ts` **rỗng** (hợp đồng không đổi ở đầu việc này).
**Kiểm tay trên `localhost:3000` với bucket `socialmedia-dev` — ĐÃ CHẠY 2026-09-21, ba bước đi hết, ảnh lên thật.**
Đây là phần **không mock nào thay được** (Mục 1.2 luật 4): ba nghi phạm của bước 2 (CORS bucket, chữ ký, CSP) chỉ tồn
tại ở trình duyệt thật và cho **cùng một triệu chứng**. Giá trị quan sát, **đã che chữ ký** (luật Mục 1.2 #7 — bản đầy
đủ mang `X-Amz-Signature`, `X-Amz-Credential` chứa access key ID, và account ID nằm trong tên host):

| Bước | Quan sát | Khớp với |
|---|---|---|
| 1 | `POST /bff/api/media/uploads` → 201, `mediaKey` = `avatars/{userId}/{uuid7}.webp`, `expiresIn: 600`, `requiredHeaders` = `{Content-Type: image/webp, Content-Length: "94612"}` | Đ-2.7 tiền tố theo `purpose`; `uploadUrl` có `X-Amz-SignedHeaders=content-length;content-type;host` — **đúng ba header đã ký**, và `Content-Length` do trình duyệt tự đặt |
| 2 | `PUT` thẳng tới `https://<account-id>.r2.cloudflarestorage.com/socialmedia-dev/avatars/…webp?…` — **không** qua `/bff/*`; object có thật trong bucket, **94.61 KB** đúng bằng `sizeBytes` đã khai | Đ-2.5 (byte không đi qua origin của app) và Đ-2.8 lớp 2 (`HEAD` của server đối chiếu được) |
| 3 | `PUT /bff/api/users/me/avatar` → **200**, `avatarUrl` là presigned GET `X-Amz-Expires=900`, `X-Amz-SignedHeaders=host` | Đ-2.9 — 15 phút, và **không bao giờ** là `avatar_key` |
| — | Thẻ `<img>` tải ảnh **200** từ host R2 | Đ-E17: `img-src` **và** `connect-src` đều đã mở đúng host — `connect-src` sai thì bước 2 đã chết trước |
| — | `DELETE /bff/api/users/me/avatar` → **204**, avatar biến mất khỏi hồ sơ, nhưng **object vẫn còn** trong bucket; ảnh `.jpg` của lượt đầu vẫn nằm cạnh `.webp` của lượt sau | Đ-2.10 — chỉ gỡ liên kết, dọn trễ; khớp `AvatarTests.Doi_avatar_lan_hai_khong_xoa_object_cu` |

Hai dòng `X-Amz-Expires` (**600** cho `PUT`, **900** cho GET) và hai danh sách `SignedHeaders` khớp **từng ký tự** với
`example` trong `content-v1.yaml` và `profile-v1.yaml` — hợp đồng tả đúng thứ chạy thật, không phải tả thứ mong muốn.
**ISS-02 đóng cho lát cắt avatar**; `E4` chỉ còn phải chứng minh nó đứng vững với mười ảnh song song.

**Bảng đột biến — chín dòng, tất cả bị bắt** (áp một đột biến, chạy test của nó, khôi phục):

| Đột biến | Test đỏ |
|---|---|
| Gộp câu bước 2 vào `errorMessage("avatar", …)` | `bước 2 … câu RIÊNG về kết nối` |
| Bỏ chốt `refreshed` (nạp lại hồ sơ mỗi lần ảnh vỡ) | `ảnh vỡ … nạp lại hồ sơ ĐÚNG MỘT LẦN` |
| `purpose: "post"` thay vì `"avatar"` lúc presign | `presign khai ĐÚNG purpose=avatar…` |
| Bỏ `if (invalid) return` — cứ gọi API dù file sai | hai ca `client chặn trước khi gọi API` |
| `file.size < 1` thành `< 0` (cho file rỗng qua) | bảng ngưỡng `0 byte` của `media.test.ts` |
| Bỏ nhánh đọc `errors[key]`, lùi hết về bảng chung | hai ca `400 … hiện ĐÚNG CÂU SERVER theo key` |
| Coi mọi phản hồi của R2 là xong (bỏ nhánh status ≠ 2xx) | `status ≠ 2xx là R2 TỪ CHỐI` |
| Gửi thêm `x-requested-with` vào `PUT` | `gửi ĐÚNG Content-Type đã ký…` |
| `contentType` của `PUT` gõ cứng `image/jpeg` | `presign khai ĐÚNG purpose=avatar…` (nhánh webp) |

Hai dòng cuối cùng của bảng này **lúc đầu lọt lưới** và đó là thông tin, không phải thủ tục: test đầu tiên chỉ kiểm
body presign nên một hằng số gõ cứng ở bước 2 vẫn xanh, và ca "nạp lại một lần" xanh **nhờ `loadProfile` single-flight**
chứ không nhờ chốt `refreshed` — xem hai cạm bẫy mới bên dưới.

**Chỗ lệch so với kế hoạch — đã làm như sau:**

- **Lệch Mục 15: luật ESLint của `Q-E3` áp ở `E3`, không đợi `E4`.** Kế hoạch commit ghi `Q-E3` ở commit #5 (`E4`),
  nhưng chính Mục 4 bắt viết `lib/upload/r2.ts` ngay tại `E3` ("viết ở đây, `E4` dùng lại"). Để luật lại cho `E4`
  nghĩa là có một commit mà `XMLHttpRequest` mở toang mà không cổng nào kêu — đúng cái lỗ `Q-E3` được lập ra để bịt.
  **Đã thử cho đỏ hai chiều** (luật FE Mục 9, `git status` sạch trước và sau): một file `features/profile/…` gọi
  `new XMLHttpRequest()` → `pnpm lint` **đỏ** đúng thông điệp `Q-E3`; cùng file đặt dưới `lib/upload/` → **xanh**,
  mà `fetch` trong chính file đó vẫn **đỏ** (override dựng lại `RESTRICTED_GLOBALS` và chỉ bỏ XHR).
- **Lệch mẫu code của `E4` Bước 2: `r2.ts` KHÔNG có dòng `eslint-disable-next-line no-restricted-globals`.** Mẫu ở
  Mục 5 viết sẵn dòng đó, nhưng `Q-E3` đã tắt luật cho cả `lib/upload/**`, nên directive tại chỗ là **directive thừa**
  và ESLint 9 báo lại (`reportUnusedDisableDirectives` mặc định bật). Giữ nguyên lời giải thích dưới dạng comment
  thường; chỗ chặn thật là override, và chuyển `r2.ts` ra khỏi `lib/upload/` là lint đỏ ngay.
- **Lệch `Q-E5`: chỉ thêm `avatar` vào kit ở đầu việc này**, không chạy cả câu lệnh sáu component. Bốn cái còn lại
  (`alert-dialog`, `radio-group`, `progress`, `badge`) đi cùng đầu việc dùng chúng — thêm sớm là bốn file `components/ui/**`
  không ai import, và `shadcn add --diff` về sau không phân biệt được "chưa dùng" với "đã sửa tay".
- **File mới ngoài kế hoạch: `lib/validation/media.ts`.** `E4` Bước 1 xếp BR-01 vào `lib/validation/post.ts`, nhưng
  dòng "loại/dung lượng một ảnh" của bảng đó là luật của **một file**, không phải của một bài — avatar cần đúng nó mà
  không cần ba luật kia. Tách ra để `E4` `import` lại thay vì chép câu chữ lần thứ hai (chép là hai chỗ để lệch với
  `CreateUploadsRequestValidator.FileNotAllowed`).
- **Nới bảng ba trạng thái lỗi của Mục 4 cho 400.** Bảng ghi bước 1 và bước 3 "theo `errorMessage(…)`"; nhưng
  `errorMessage` ánh xạ theo `(ngữ cảnh, status)` nên mọi 400 ra đúng một câu `"Dữ liệu không hợp lệ."`, che mất hai
  câu server duy nhất người dùng dùng được ("Ảnh chưa được tải lên xong…", "Ảnh đại diện chỉ nhận JPEG, PNG hoặc WebP.").
  Đã làm: 400 đọc `errors.files` (bước 1) / `errors.mediaKey` (bước 3) trước, **rồi mới** lùi về bảng — đúng Đ-E5
  ("mọi 400 hiển thị theo key của `errors`"), và hai ca test canh chỗ này.

**Năm cạm bẫy mới, không có trong bảng trên:**

| Cạm bẫy | Triệu chứng | Chặn bằng |
|---|---|---|
| XHR giả của msw **không cài `abort()`** (`grep -c abort` trong `@mswjs/interceptors@0.41.9` `interceptors/XMLHttpRequest/index.mjs` ra **0**) | Test "hủy giữa chừng" **xanh giả**: `xhr.abort()` không làm gì, request vẫn chạy tới `onload` | Không viết ca đó bằng msw. `r2.ts` vẫn có `onabort`; nghiệm thu nhánh hủy chỉ trên trình duyệt thật — **`E4` (hủy một ảnh đang lên) phải nhớ điều này** |
| `userEvent.upload` mặc định **lọc file theo `accept`** (`applyAccept: true`) | Ca "chọn ảnh GIF" xanh giả: file bị bỏ trước khi `change` bắn, chỗ chặn thật không hề chạy | `userEvent.setup({ applyAccept: false })` — đời thật kéo-thả và hộp thoại macOS cũng bỏ qua `accept` |
| Base UI `Avatar.Image` mặc định nạp trước bằng `new window.Image()`, **không** gắn `<img>` vào DOM cho tới khi `loaded` | Không có phần tử nào để nghe `error` → nhánh "URL hết hạn" không chạy được, và trong jsdom ảnh mãi ở `loading` | `keepMounted` + `onLoadingStatusChange` — thẻ `<img>` thật nằm trong DOM và chính `error` của nó là nguồn tin |
| `loadProfile` **single-flight** gộp nhiều lần ảnh vỡ liên tiếp | Ca "nạp lại đúng một lần" xanh cả khi **bỏ** chốt `refreshed` | Chờ lượt nạp đầu **xong** (và trả về một `avatarUrl` khác) rồi mới làm vỡ lần hai |
| `ProfileResponse.avatarUrl` là field **không bắt buộc** của hợp đồng (`avatarUrl?: string \| null`) | `string \| null \| undefined` ở mọi chỗ dùng; và narrow `profile` ở component ngoài **không theo được** vào closure `async` | Prop nhận cả `undefined`; tách `AvatarEditor` nhận `profile: ProfileResponse` đã hết `null` thay vì ép kiểu |

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

### Thực tế thi công

**Bằng chứng.** Vitest **361 → 392** (+31: `lib/validation/post.test.ts` 18,
`features/post/post-composer.test.tsx` 13). `pnpm lint`, `typecheck`, `test`, `build` xanh cả bốn; `/compose` có
trong bảng route của bản build. Hợp đồng không đổi ở đầu việc này nên không chạy lại codegen.

**Kiểm tay trên `localhost:3000` với bucket `socialmedia-dev` — ĐÃ CHẠY 2026-09-21 trên Chrome 153.0.8010.50**,
tài khoản mới, **mười ảnh PNG thật** (43 KB → 692 KB) trong MỘT lượt chọn. Đây là phần không mock nào thay được: E3
đã đóng ISS-02 cho một ảnh dưới tiền tố `avatars/`, còn thứ chỉ hiện ra ở đây là **tiền tố `posts/` và ba lượt `PUT`
chạy song song**.

| Quan sát | Khớp với |
|---|---|
| **10** lượt `PUT` tới `https://<account-id>.r2.cloudflarestorage.com/...`, **0 byte ảnh** qua `/bff/*` | Đ-2.5 |
| **Đúng MỘT** `POST /bff/api/media/uploads` cho cả mười ảnh | Đ-2.15 — mười lời gọi là 10% hạn mức 100 req/phút cho một bài |
| `POST /bff/api/posts` → **201** với mười `mediaKeys` | Đ-2.8 lớp 2 — server `HEAD` từng object và đối chiếu `contentType` + `sizeBytes`; 201 nghĩa là cả mười khớp |
| Không request nào rời hai origin `localhost:3000` và host R2 | Đ-E17 — `connect-src` mở đúng host cần, không hơn |
| **Không** header `Authorization` nào rời trình duyệt; `localStorage` và `sessionStorage` **rỗng** | Đ-E14, Đ-E2 |

`connect-src` sai thì cả mười lượt `PUT` đã chết trước khi có `POST /posts` — nên một dòng 201 ở cuối là bằng chứng
cho cả chuỗi. **ISS-02 đóng cho lát cắt bài đăng.**

**Bảng đột biến — mười lăm dòng, tất cả bị bắt** (áp một đột biến, chạy test, khôi phục; `git status` sạch trước và sau):

| Đột biến | Test đỏ |
|---|---|
| Presign khai `purpose: "avatar"` thay vì `"post"` | `MỘT lời gọi presign cho cả lô ba ảnh` |
| Bỏ lọc `imageFileError` khi thêm ảnh | `ảnh sai loại bị bỏ…` |
| Bỏ chặn quá 10 ảnh | `11 ảnh: báo DƯỚI Ô ẢNH…` |
| `claimNext` không đánh dấu `dang-gui` (ba thợ cùng nhận một ảnh) | `MỘT lời gọi presign…` treo ở `every xong` |
| `PUT` hỏng vẫn coi là `xong` | `MỘT ảnh lỗi KHÔNG hủy cả lô` |
| `sizeBytes` gửi lên không theo file thật | `gửi ba giá trị mỗi ảnh ĐÚNG BẰNG…` |
| `retryItem` không làm gì | `MỘT ảnh lỗi KHÔNG hủy cả lô` |
| Nút Đăng không đợi mọi ảnh `xong` | `MỘT ảnh lỗi KHÔNG hủy cả lô` |
| Nút "Thử lại" hiện ở mọi dòng | `MỘT ảnh lỗi KHÔNG hủy cả lô` |
| `privacy` có mặc định ngầm `public` | `chưa chọn mức riêng tư: chặn ở client` |
| Bài chỉ có ảnh gửi chuỗi rỗng thay vì `null` | `bài chỉ có ảnh: body gửi null` |
| Bỏ `validationErrors`, mọi 400 lùi về bảng chung | `400 của server hiện theo key…` |
| BR-01 kiểm chữ TRƯỚC ảnh | `ẢNH TRƯỚC, CHỮ SAU…` |
| Trim `body` trước khi đo độ dài | `khoảng trắng KHÔNG bị trim trước khi đo` |
| Bỏ kiểm `privacy` ở client | `chưa chọn mức riêng tư: chặn ở client` |

**Chỗ lệch so với kế hoạch — đã làm như sau:**

- **Lệch Mục 5 Bước 1: `lib/validation/post.ts` KHÔNG chứa dòng "loại/dung lượng một ảnh" của bảng.** Dòng đó đã
  nằm ở `lib/validation/media.ts` từ `E3` — nó là luật của **một file**, không phải của một bài. `post.ts` chỉ giữ
  ba mệnh đề BR-01 cộng `privacy`, và `use-upload-queue.ts` `import` luật kia.
- **Lệch Mục 5 Bước 5: `privacy` KHÔNG có giá trị chọn sẵn.** Bước 5 cho phép "UI có giá trị chọn sẵn"; đã chọn
  cách ngược lại (`null` = chưa chọn, chặn ở client bằng đúng câu `PrivacyRequired` của server). Lý do: bài đăng
  nhầm mức riêng tư **không rút lại được**, và một mặc định im lặng là thứ người dùng không bao giờ thấy mình đã
  chọn. Không phải "chặt hơn server" — server cũng bắt buộc trường này, cùng câu.
- **Lệch Q-E5: chỉ thêm `radio-group` và `progress` vào kit ở đầu việc này.** `alert-dialog` và `badge` đi cùng
  `E5`/`E6` — cùng lý do `E3` đã ghi.
- **Không điều hướng sang `/posts/{postId}` sau khi đăng.** Trang chi tiết là việc của `E5`; đưa người dùng tới một
  route chưa tồn tại là đổi một lỗi 400 lấy một trang 404. Composer ở lại màn, báo "Đã đăng bài." kèm liên kết tới
  bài — liên kết đó sống khi `E5` xong.
- **`R2_PUT_FAILED` và `fieldMessage` dời từ `features/profile/upload-avatar.ts` xuống `lib/api/messages.ts`.**
  `features/` không import chéo nhau (Đ-E13), nên `E4` chỉ có hai đường: chép lại hai thứ đó, hoặc đẩy xuống `lib/`.
  Câu của bước `PUT` không phải của màn mà của **đường truyền** — avatar và composer hỏng vì cùng ba nghi phạm
  (mạng, CORS, CSP) và người dùng làm cùng một việc. `upload-avatar.ts` dùng lại, hành vi không đổi.
- **File mới ngoài kế hoạch: `features/post/privacy.ts`.** Bước 5 để nhãn ba mức riêng tư nằm ngay trong composer,
  nhưng `E5` (nhãn trên card) và `E6` (form sửa) cần đúng ba câu đó — ba bản chép là ba chỗ để lệch, và lệch ở đây
  nghĩa là cùng một bài được gọi hai tên trên hai màn. Tách ngay bây giờ vì chỗ dùng thứ hai đã nhìn thấy.
- **Thêm một nút "Đăng bài" trên `/me`.** `/compose` không có đường nào tới từ giao diện cho tới khi `E5` dựng danh
  sách bài trên chính màn đó. Một `Link` trong `app/` (chỉ ráp, Đ-E13), gỡ được khi `E5` thay bằng chỗ tốt hơn.

**Bốn cạm bẫy mới, không có trong bảng trên:**

| Cạm bẫy | Triệu chứng | Chặn bằng |
|---|---|---|
| `react-hooks/immutability` chặn `useCallback` **tự gọi lại chính nó** — khuôn "bơm hàng đợi" quen thuộc (`pump()` trong `finally`) là **lint đỏ**, không phải cảnh báo | `pump accessed before it is declared` | Đổi sang **hồ thợ**: `claimNext()` nhận và đánh dấu một ảnh, `runWorker` tự lấy ảnh kế tiếp khi xong, `pump` chỉ mở thợ cho đủ trần. Không đệ quy, và ba thợ đọc chung `itemsRef` |
| Trạng thái hàng đợi chỉ ở `useState` | Ba lượt `PUT` cùng nhận một ảnh: `setItems` chưa kịp cập nhật giữa hai vòng lặp | `itemsRef` là bản sao **đồng bộ**, mọi thay đổi đi qua `commit`; `claimNext` đánh dấu `dang-gui` ngay trong cùng một lượt |
| `FieldLabel` là `<label>` thật, không nhận prop `render` của Base UI | `Property 'render' does not exist` khi định bọc nhóm radio bằng nhãn | Tên nhóm đi qua `FieldTitle` + `aria-labelledby` — `role="radiogroup"` không gắn được vào một `<label>` |
| Ticket hết hạn giữa chừng khi thử lại | Chữ ký chết giữa lượt `PUT` → đúng câu lỗi của ca CORS/CSP, tức là **nghi phạm sai** | Trừ hao `TICKET_SAFETY_MS` 60 giây trước khi coi `uploadUrl` là còn hạn; sát hạn thì presign lại một file (một request rẻ) |

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

### Thực tế thi công

**Bằng chứng.** Vitest **392 → 418** (+26: `features/post/user-posts.test.tsx` 19,
`features/post/post-detail.test.tsx` 7). `pnpm lint`, `typecheck`, `test`, `build` xanh cả bốn; ba route
`/compose`, `/posts/[postId]`, `/users/[userId]` có trong bảng route của bản build. Hợp đồng không đổi.

**Kiểm tay trên `localhost:3000` với API + Postgres dev — ĐÃ CHẠY 2026-09-21, Chrome 153.0.8010.50**, tài khoản
mới, **25 bài tạo qua API thật**:

| Quan sát | Khớp với |
|---|---|
| Trang đầu **20 card**, bấm "Xem thêm" một lần → **25 card**, **25 `postId` duy nhất**, **0 bài thiếu** | Đ-2.11 — `limit` mặc định 20, cursor nối đúng một lô |
| Thứ tự: bài mới nhất đứng đầu, bài cũ nhất đứng cuối | `sort = (created_at DESC, post_id DESC)` |
| Hết trang → nút "Xem thêm" **biến mất** | `nextCursor === null` |
| Query lượt hai: `?cursor=MjAyNi0wOS0yMFQyMDowNzowMi40MTA3MjEwKzAwOjAwfDAxYTBjMDZkLWU0MGEtNzNjYi04YjRjLTJjZjIwYzBkNzU1Yw&limit=20` | Cursor đi **nguyên vẹn** — FE không dựng, không sửa, không encode lại |
| `/posts/{id}` mở đúng bài; `/posts/{id-không-tồn-tại}` → **đúng một câu**, không nút "Thử lại" | Mục 7.4 |
| `/users/{id}` → hồ sơ + 20 bài đầu | Q-E6 |

**Trang đầu bị gọi HAI lần trên dev** (`limit=20` xuất hiện hai lượt trong Network) — đó là StrictMode của Next chạy
effect hai lần, không phải lỗi: `runRef` hủy lượt đầu và `seenRef` khử trùng, nên **25 id vẫn duy nhất**. Production
chạy effect một lần. Chính lượt chạy này là bằng chứng cho luật 3 của cursor.

**Bảng đột biến — mười sáu dòng, tất cả bị bắt** (áp một đột biến, chạy test, khôi phục; `git status` sạch trước và
sau):

| Đột biến | Test đỏ |
|---|---|
| Coi `nextCursor: ""` là "còn trang" | `nextCursor: null` → nút Xem thêm BIẾN MẤT |
| Bỏ khử trùng theo `postId` | `lô TRÙNG không sinh card lặp` |
| Bỏ chốt `pendingRef` trong `loadMore` | `loadMore gọi hai lần liền chỉ bắn MỘT request` |
| Gửi `limit=100` (ngoài `1..50`) | `trang đầu KHÔNG gửi cursor, gửi đúng limit 20` |
| Trang đầu gửi một cursor bịa ra | `trang đầu KHÔNG gửi cursor…` |
| Dữ liệu cũ không hết hiệu lực khi đổi `userId` | `đổi userId: KHÔNG nháy bài của người cũ` |
| Trang sau hỏng thì xóa sạch danh sách | `trang SAU hỏng: danh sách cũ VẪN còn` |
| Nút "Xem thêm" chỉ bị `disabled` thay vì biến mất | `nextCursor: null` → nút BIẾN MẤT |
| Nói "chưa đăng bài nào" cả khi trang đầu hỏng | `trang đầu hỏng: KHÔNG nói 'chưa đăng bài nào'` |
| Nhãn "đã chỉnh sửa" theo so ngày thay vì `editedAt` | `nhãn 'đã chỉnh sửa' theo editedAt` |
| Bỏ sắp xếp ảnh theo `position` | `ảnh xếp theo position` |
| Ảnh vỡ thì nạp lại **không** giới hạn số lần | `ảnh vỡ → nạp lại ĐÚNG MỘT LẦN` |
| `key` ảnh theo `position` thay vì `url` | `ảnh vỡ → nạp lại ĐÚNG MỘT LẦN` (khẳng định node `<img>` là node MỚI) |
| 404 nói thẳng "Bài này đã bị xóa." | `không nói bài có tồn tại hay không` |
| 404 dùng chung nhánh với 5xx (có nút Thử lại) | `404 → KHÔNG có nút Thử lại` |
| Card trong danh sách không dẫn tới chi tiết | `card trong DANH SÁCH dẫn tới trang chi tiết` |

**Ba đột biến LÚC ĐẦU lọt lưới** — và đó là thông tin, không phải thủ tục:

- **Bỏ chốt `pendingRef`.** Ca "bấm Xem thêm hai lần" xanh giả vì `disabled={page.pending}` đã chặn cú bấm thứ hai
  **ở tầng UI**. Chốt trong hook vẫn phải có: GĐ4 thay nút bằng `IntersectionObserver`, không có `disabled` nào để
  dựa. Đã thêm một ca gọi thẳng `page.loadMore()` hai lần qua một component trần, và một ca bấm nút khi trang sau
  **cố ý chậm** (`delay(60)` của msw) — không có độ trễ đó thì lượt đầu đã xong và nút đã biến mất trước cú bấm thứ hai.
- **Dữ liệu cũ không hết hiệu lực khi đổi `userId`.** Ca tương ứng chỉ có ở `PostDetail`; danh sách thì không. Đã thêm.
- **`key` ảnh theo `position`.** Test cũ chỉ kiểm thuộc tính `src`, mà React cập nhật `src` dù có thay node hay không.
  Đã khẳng định **danh tính node** (`expect(thẻMới).not.toBe(thẻCũ)`) — chính việc thay node mới buộc trình duyệt
  tải lại thay vì dùng lại ảnh hỏng trong cache.

**Chỗ lệch so với kế hoạch — đã làm như sau:**

- **Lệch Bước 4: nút "Xem thêm", KHÔNG `IntersectionObserver`.** Bước 4 viết "ẩn nút/ngắt observer", để ngỏ cả hai.
  GĐ2 chọn nút vì cuộn vô hạn tự động làm người dùng bàn phím không tới được cuối trang, và nó **nuốt lỗi** — một
  trang hỏng giữa chừng trông y hệt "hết bài". GĐ4 đổi sang observer thì đổi trong **đúng `post-list.tsx`**; phần
  cursor ở `use-post-page.ts` không phải chạm.
- **Lệch Bước 1: trạng thái KHÔNG phải bốn `useState` rời** (`items`, `nextCursor`, `pending`, `error`). Luật ESLint
  `react-hooks/immutability`/`set-state-in-effect` của repo **cấm** `setState` đồng bộ trong effect, mà "đổi `userId`
  thì xóa danh sách" đúng là thế. Đã gom thành một object mang `key = "{userId}:{attempt}"`: dữ liệu của lượt xem cũ
  tự hết hiệu lực vì `key` không khớp, không ai phải xóa bằng tay. `PostDetail` và `PublicProfile` dùng cùng khuôn.
  **Bề mặt `PostPageState` vẫn đúng như Bước 1 mô tả** — chỗ đổi là cách giữ, không phải cái giữ.
- **Không có `features/post/my-posts.tsx`.** Đ-E13 cấm `features/post` import `features/profile`, mà `/me` cần
  `userId` từ `profileStore`. Đã để `UserPosts` nhận `userId` qua **prop** và đẩy việc đọc store lên
  `app/(app)/(with-profile)/me/page.tsx` — tầng `app/` là chỗ **duy nhất** được biết cả hai feature. Trang `/me` vì
  thế thành `"use client"`; nó vốn toàn component client nên không mất gì của RSC. **Không mở ngoại lệ ESLint nào.**
- **`<img>` thường, không `next/image`** — kèm `eslint-disable` có lý do tại chỗ. `next/image` **tải ảnh về server
  Next** rồi phục vụ lại, tức byte ảnh đi qua origin của app: đúng thứ Đ-2.5 cấm. Nó còn đòi khai host R2 trong
  `remotePatterns`, mà URL presigned đổi chữ ký mỗi 15 phút nên cache của optimizer là cache của những URL đã chết.
- **Nhãn ba mức riêng tư dùng lại `features/post/privacy.ts`** — file đó tách ra khỏi `post-composer.tsx` và đi
  trong **commit E4**, không phải commit này: composer `import` nó nên hai thứ không tách commit được.
- **`post.author.userId` CÓ xuất hiện trong `features/`** (`post-card.tsx`) — nhưng chỉ làm `href` tới hồ sơ tác giả,
  **không** dùng để quyết định quyền. Luật Mục 16 cấm cái sau; `canEdit` vẫn là nguồn duy nhất cho quyền (E6).

**Ba cạm bẫy mới, không có trong bảng trên:**

| Cạm bẫy | Triệu chứng | Chặn bằng |
|---|---|---|
| `react-hooks/set-state-in-effect` cấm khuôn quen "effect đổi param → reset state" | `pnpm lint` **đỏ** ở cả ba màn nạp theo param (`useUserPosts`, `PostDetail`, `PublicProfile`) | Gom state thành một object mang `key`; dữ liệu cũ hết hiệu lực bằng **so key lúc render**, không bằng một lượt `setState` |
| `disabled` trên nút che mất lỗ hổng ở tầng hook | Ca "bấm hai lần" xanh **dù** bỏ chốt gọi đôi trong hook | Test gọi thẳng `page.loadMore()` — đúng cách GĐ4 sẽ gọi nó từ observer |
| `FieldLabel` là `<label>` thật; `role="radiogroup"` không gắn được vào đó | (E4 đã gặp) | — |
| Trang đầu gọi **hai lần** trên dev | Network hiện hai lượt `limit=20` | StrictMode, không phải lỗi — `runRef` + `seenRef` chặn phần hệ quả; production chạy effect một lần |

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

### Thực tế thi công

**Bằng chứng.** Vitest **418 → 441** (+23: `features/post/post-item.test.tsx` 19, cộng 2 ca xóa/sửa trong
`user-posts.test.tsx` và 2 ca trong `post-detail.test.tsx`). `pnpm lint`, `typecheck`, `test`, `build` xanh cả
bốn. Hợp đồng không đổi.

**Kiểm tay trên `localhost:3000` với API + Postgres dev — ĐÃ CHẠY 2026-09-21, Chrome 153.0.8010.50**, tài khoản
mới, ba bài:

| Quan sát | Khớp với |
|---|---|
| Đổi **mỗi** mức riêng tư → PATCH mang đúng `{"privacy":"private"}`, **không** kèm `body` | Bước 2 — `edited_at` chỉ đóng dấu cho thay đổi có thật |
| Nhãn "đã chỉnh sửa" hiện ngay sau 200, **không** gọi thêm `GET` | 200 trả nguyên `PostResponse` |
| Xóa trên `/posts/{id}` → rời trang về `/me`, danh sách còn 2 bài | Bước 3 |
| Mở lại URL cũ → trang "không tìm thấy", **HTTP 200** (trang render bình thường; 404 là của API) | Mục 7.4 — không phải lỗi 500 |
| `GET /posts/{đã xóa}` bằng token của **chính tác giả** → **404** | Mục 7.3 |
| `DELETE` lần hai → **403** | Xóa mềm; FE hiện một câu chung cho cả "của người khác" |

**Bảng đột biến — mười sáu dòng, tất cả bị bắt** (áp một đột biến, chạy test, khôi phục; `git status` sạch
trước và sau):

| Đột biến | Test đỏ |
|---|---|
| Bỏ `canEdit`, luôn hiện nút Sửa/Xóa | `canEdit: false` → nút KHÔNG CÓ TRONG DOM |
| Ẩn nút bằng CSS thay vì không render | như trên |
| Luôn gửi cả `body` lẫn `privacy` | `chỉ đổi body → PATCH gửi ĐÚNG body` |
| Xóa hết chữ gửi `null` thay vì `""` | `xóa hết chữ gửi ""` |
| So `body` với rỗng thay vì với giá trị ban đầu | `gõ rồi xóa về đúng giá trị cũ thì Lưu TẮT lại` |
| Nút Lưu không tắt khi chưa đổi gì | `nút Lưu TẮT khi chưa đổi gì` |
| Bỏ chốt gửi khi patch rỗng | `submit form khi chưa đổi gì: KHÔNG gửi request` |
| Bỏ `validationErrors`, mọi 400 lùi về bảng chung | `400 của server hiện theo key body` |
| 403 khi sửa dùng câu của `post-read` | `403 khi sửa → MỘT câu không tiết lộ` |
| Sửa xong không thoát chế độ sửa | `200 → thoát chế độ sửa` |
| Xóa không hỏi xác nhận, gọi `DELETE` ngay | `bấm Xóa mở hộp thoại; chưa xác nhận thì KHÔNG gọi API` |
| 403 khi xóa vẫn gỡ bài khỏi danh sách | `403 khi xóa: KHÔNG gỡ bài` |
| 403 khi xóa nói thẳng "bài đã bị xóa" | `403 KHÔNG nói 'bài đã bị xóa'` |
| 204 không báo ra ngoài (bài ở lại danh sách) | `xác nhận → DELETE, 204 thì báo bằng null` |
| Danh sách không gỡ bài sau khi xóa | `xóa xong thì bài BIẾN MẤT khỏi danh sách` |
| Trang chi tiết ở lại URL đã chết sau khi xóa | `xóa xong thì RỜI trang` |

**Bốn đột biến LÚC ĐẦU lọt lưới** — ba trong số đó là cùng một bài học đã gặp ở `E5`:

- **Bỏ chốt `if (!daDoi) return` trong `onSubmit`.** Xanh giả vì nút Lưu `disabled` đã chặn cú bấm — **lần thứ
  hai** một chốt ở tầng dưới bị che bởi `disabled` (lần đầu là `pendingRef` của `E5`). Đã thêm ca
  `fireEvent.submit(form)`: một form submit được bằng phím hay bằng script thì `disabled` trên một nút không
  phải hàng rào.
- **Danh sách không gỡ bài sau khi xóa**, và **trang chi tiết ở lại URL đã chết**: hai đột biến ở chỗ *nối*
  `PostItem` vào `PostList`/`PostDetail`, mà test đơn vị của `PostItem` chỉ kiểm tới `onChanged(null)`. Đã thêm
  hai ca **tích hợp** — xóa thật trong danh sách, xóa thật trên trang chi tiết.
- **403 khi xóa vẫn gỡ bài.** Đã thêm ca khẳng định `onChanged` **không** được gọi: gỡ card sau một 403 là nói
  dối theo chiều ngược lại — bài vẫn còn trên server.

**Chỗ lệch so với kế hoạch — đã làm như sau:**

- **File mới ngoài kế hoạch: `features/post/post-item.tsx`.** Bước 1 viết `{post.canEdit && <PostActions …/>}`
  như thể card tự dựng nút. Nhưng "đang sửa bài nào" là **trạng thái**, mà `PostCard` phải giữ được tính thuần
  (nhận một `post`, vẽ ra, không giữ gì của người dùng) — nếu không thì `PostList` và `PostDetail` mỗi bên dựng
  một bản. `PostItem` là chỗ giữ trạng thái đó, dùng chung cho cả hai màn.
- **`PostList` bỏ prop `renderActions`.** E5 để sẵn prop đó cho E6; hóa ra nó không đủ — nút và form phải đi
  cùng nhau trong một component có state. Không ai từng truyền prop đó nên bỏ đi không phá gì.
- **Sửa hai ca test của `E5` mà E6 làm đổi nghĩa**, không phải làm hỏng: ca "GĐ2 không có nút bình luận hay cảm
  xúc" dùng `queryByRole("button")` để nói "không nút nào" — nay bài của chính mình có nút Sửa/Xóa, nên ca đó
  đổi sang `canEdit: false`. Trộn hai chuyện vào một khẳng định là ca test đổi nghĩa mỗi lần thêm thao tác mới.
  `post-detail.test.tsx` phải mock `next/navigation` vì `PostDetail` giờ điều hướng sau khi xóa.
- **Form sửa nói thẳng "Không sửa được ảnh của bài đã đăng."** khi bài có ảnh. Bước 2 chỉ yêu cầu *không có* nút
  thêm/bớt ảnh; thiếu một câu thì người dùng đi tìm nút không tồn tại.

**Hai cạm bẫy mới:**

| Cạm bẫy | Triệu chứng | Chặn bằng |
|---|---|---|
| `AlertDialogAction` của kit **đóng hộp thoại ngay khi bấm**, trước khi request xong | Người dùng bấm "Xóa bài", hộp thoại biến mất, và trong lúc chờ server không có gì trên màn cho biết đang xảy ra chuyện gì | `event.preventDefault()` trong `onClick`, tự đóng sau khi `DELETE` trả lời — thành công thì đóng, lỗi thì đóng và hiện câu lỗi |
| Test đơn vị của một component "có thao tác" **không** phủ được chỗ nối nó vào màn | Ba đột biến ở `PostList`/`PostDetail` lọt lưới dù `PostItem` có 19 ca | Mỗi thao tác đổi dữ liệu cần **một** ca tích hợp ở từng màn nó xuất hiện — không nhiều hơn, nhưng không thiếu |

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

### Thực tế thi công

**Bước 1 — rà Vitest: không thiếu dòng nào.** Bốn nhóm của Mục 10.5 dòng 1 đã viết xong cùng `E2`–`E6`:
BR-01 phía client (`lib/validation/post.test.ts`, 12 ca) · nhánh 400/403/409 của composer · một ảnh lỗi không
hủy cả lô · nối trang theo cursor. File test nằm cạnh mã nguồn. *Lệch nhỏ đã có từ GĐ1:* `test/` chứa cả
`server-only.ts` — shim cho alias của Vitest, không phải test; để nguyên.

**Bước 2 — kịch bản chọn bằng DỮ LIỆU NHẬP.** `mocks/handlers.ts` đã phủ `/bff/api/users/*`,
`/bff/api/posts/*`, `/bff/api/media/uploads` và host R2 giả từ `E1`; `E8` thêm phần còn thiếu là *kịch bản*:
một `sizeBytes` cụ thể (`MEDIA_SCENARIO.sizeBytesKeyDaDung`) lấy về một `mediaKey` mà `POST /posts` coi là đã
gắn vào bài khác → **409** (BR-03). Giá trị thật của nó không phải là "có thêm một nhánh", mà là **mock giống
server hơn**: một `server.use` trả 409 cho mọi request chứng minh được ít hơn hẳn. Ca test dùng nó đi hết
chuỗi ba bước và khẳng định thêm một điều mà ca cũ không nói được — **client gửi lại đúng `mediaKey` nó vừa
nhận lúc presign**. Vitest **441 → 442**.

**Bước 3 — ảnh fixture thật, commit vào repo.** `e2e/fixtures/anh-nho.jpg` (5.209 byte) và `anh-nho.png`
(16.592 byte), 80×60 nhiễu ngẫu nhiên, sinh một lần bằng `System.Drawing` của Windows rồi **kiểm byte**: JPEG
có `FFD8`/`FFD9` và khối `JFIF`; PNG có chữ ký và chunk `IEND`. Không sinh lúc chạy (Q-E8).

**Bước 4 — sáu spec.** Bốn spec mới (`onboarding`, `post-create`, `post-edit-delete`, `post-forbidden`) và hai
spec mở rộng (`csp`, `login-storage`), cộng `e2e/post-helpers.ts` cho phần dựng tài khoản/bài mà cả bốn đều cần.

**Bằng chứng cuối (Bước 5).** `pnpm test:e2e` cả bộ, **Chrome 153.0.8010.50**, `workers: 1`, API + Postgres +
Redis + Mailpit dev + bucket `socialmedia-dev`:

```
1 skipped
17 passed (7.9m)
```

Ca `skipped` là `single-flight.spec.ts` — skip **có điều kiện** từ GĐ1 (cần access token hết hạn thật), đúng ý đồ,
không phải ca bị tắt. Vitest: **442 passed**, `lint` / `typecheck` / `build` xanh.

**Bước 5 — và đây là chỗ `E8` trả về nhiều nhất: chạy cả bộ lần đầu làm lộ một HỒI QUY đã sống ba commit.**

Bốn spec của **GĐ1** đỏ, và không phải vì `E8`:

| Spec | Ca |
|---|---|
| `csp.spec.ts` | luồng đăng nhập → `/me` → tải lại → đăng xuất |
| `guard.spec.ts` | đăng nhập → `/me` hiện `roleDisplayName`; đăng xuất → `/login` |
| `login-storage.spec.ts` | trình duyệt KHÔNG thấy JWT |
| `login-storage.spec.ts` | CSRF: logout từ Origin khác bị 403 |

Cả bốn dựng tài khoản bằng `taoTaiKhoanDaXacMinh` — **đã xác minh nhưng CHƯA CÓ HỒ SƠ** — rồi chờ `me-profile`
trên `/me`. Từ commit **`0aacb96` (`E2`)**, `RequireProfile` đá mọi tài khoản chưa onboarding sang `/onboarding`,
nên `me-profile` không bao giờ xuất hiện. Bốn ca này đã đỏ suốt `E2` → `E6` mà **không ai biết**: Playwright không
vào CI (Q-E8, Đ-E8), và mỗi đầu việc chỉ chạy spec liên quan tới chính nó.

Đã sửa: bốn ca chuyển sang `taoTaiKhoanCoHoSo` — hồ sơ là **tiền đề** của chúng, không phải thứ chúng kiểm. Mỗi ca
tốn thêm một lượt `/auth/*` (khai lại trong `giuHanMucAuth`).

**Bài học, và nó lớn hơn bốn ca:** một bộ E2E không ai chạy cả lượt thì không phải cổng, nó là bốn file đỏ chờ
người phát hiện. Đề nghị thêm vào checklist khối E (Mục 16) một dòng — *"`pnpm test:e2e` chạy CẢ BỘ, xanh, kèm bản
Chrome"* — và coi nó là cổng của **`F3`**, không phải của từng đầu việc: cả bộ tốn ~7 phút, bắt chạy mỗi commit là
không thực tế, nhưng đóng khối mà chưa chạy lần nào thì con số dán vào PR không có nghĩa gì.

**Chỗ lệch so với kế hoạch — đã làm như sau:**

- **Sửa `e2e/dev-api.ts` của GĐ1: ngân sách hạn mức `/auth/*` chuyển từ BIẾN MODULE sang MỘT FILE.** Đây không
  phải dọn dẹp tùy hứng mà là điều kiện để `Bước 5` có gì để dán: cả bộ **không chạy nổi một lượt**. Playwright
  **khởi động lại worker sau mỗi test thất bại**, nên `used` về 0, lượt khai kế tiếp không chờ, và một ca 429
  kéo cả bộ đổ theo dây chuyền — đo được **11/18 spec đỏ, cả bộ chạy hết 40 giây** vì không chờ lần nào. File
  trong `tmpdir()` làm ngân sách sống qua cả worker restart lẫn hai lượt `playwright test` liền nhau.
- **Thêm `e2e/post-helpers.ts` ngoài danh sách sáu spec.** Bốn spec đều cần đúng một thứ — "một tài khoản đã
  xác minh **và đã có hồ sơ**" — mà `taoTaiKhoanDaXacMinh` của GĐ1 dừng ở bước xác minh. Bốn bản chép là bốn chỗ
  để lệch khi hợp đồng `PUT /users/me/profile` đổi.
- **`post-forbidden.spec.ts` kiểm CẢ HAI VẾ của "một câu".** Bước 4 chỉ yêu cầu "B mở bài private của A → một
  câu, không lộ tồn tại". Nhưng một câu **tự nó** không chứng minh gì: phép thử thật là so nó với câu của một
  `postId` **không tồn tại** và đòi hai câu **bằng nhau từng ký tự**. Hai câu khác nhau ở đây nghĩa là người lạ
  phân biệt được "bài có thật mà tôi không được xem" với "không có bài nào".
- **Mỗi spec tự dọn bài ở bước cuối** (`donBai`) — cạm bẫy "E2E để lại bài rác trên bucket `-dev`". Xóa mềm là
  tất cả FE/API làm được; object do worker dọn sau (Đ-2.13).

**Hai cạm bẫy mới:**

| Cạm bẫy | Triệu chứng | Chặn bằng |
|---|---|---|
| "Chuyển tiếp tới handler mặc định" bằng `fetch` cùng URL trong một `server.use` | **Đệ quy vô hạn** — chính msw bắt lại request đó; worker Vitest chết với **exit 134**, và báo cáo ghi "11 passed (14)" trông như một ca treo | Ghi thân request bằng `server.events.on("request:start")` + `request.clone().json()`, KHÔNG override handler đang cần kiểm |
| Ngân sách hạn mức giữ trong biến module của harness Playwright | Một ca 429 làm worker restart → ngân sách về 0 → 429 dây chuyền; cả bộ "chạy xong" trong 40 giây mà không chờ lần nào | Ngân sách ra file (xem chỗ lệch ở trên). **Vẫn phải nhớ:** hai lượt chạy cách nhau dưới 75 giây thì lượt sau tự chờ, đó là đúng — không phải test treo |
| Ghi `ref.current` trong THÂN RENDER (`use-post-page.ts`, `E5`) | Chưa vỡ hôm nay; vỡ khi GĐ4 dùng transition — một render bị hủy vẫn kịp ghi đè, và lượt đọc sau lấy giá trị của render không bao giờ commit | Đồng bộ trong `useEffect` không deps. `loadMore` luôn chạy trong event handler, tức sau commit, nên vẫn thấy đúng giá trị đang hiển thị |
| Kịch bản mock chọn theo `sizeBytes` áp cho **mọi** `purpose` | Một file đúng 4.242 byte làm test **avatar** đỏ với câu "Ảnh này không thuộc về bạn" — `mediaKeyDaDung` mang tiền tố `posts/`, mà `PUT /users/me/avatar` từ chối key ngoài `avatars/` | Kịch bản chỉ áp khi `purpose === "post"` |

### Dọn nợ sau `E8` (2026-09-21)

Một lượt rà lại toàn khối sau khi `E8` đã commit. Bảy mục, và **một trong số đó là lỗi do chính lượt dọn này
tạo ra** — ghi lại vì nó là bài học lớn hơn bản vá.

| # | Nợ | Đã làm |
|---|---|---|
| D1 | Hàng đợi upload **không hủy khi rời màn**: `putToR2` nhận `signal` từ `E3` nhưng `E4` chưa nối vào | `AbortController` tạo **trong effect**, hủy ở cleanup; `sendOne` bỏ qua `AbortError` thay vì đánh dấu lỗi |
| D2 | `loadMore` không truyền `signal` — trang đầu hủy được, trang sau không | `pageAbortRef` giữ controller của lượt xem hiện tại, cả hai trang hủy cùng một chỗ |
| C1 | `fieldMessage(key: **string**)` — gõ nhầm `"file"` im lặng lùi về bảng chung | Union `FieldErrorKey` cho mọi key của `errors` ở hai hợp đồng |
| C2 | `donBai` ở dòng cuối thân test — **test đỏ giữa chừng là rác ở lại**, đúng lúc cần dọn nhất | `donRacSauTest` trong `afterEach`, dọn theo **tài khoản** chứ không theo danh sách id |
| D3 | `buildPatch` export mà không ai dùng ngoài file | Năm ca unit cho nó — hàm thuần, rẻ, pin đúng luật `null` ≠ `""` |
| C4 | `test/server-only.ts` lệch luật *"`test/` chỉ chứa `setup.ts`"* | Sửa câu luật trong `frontend-rules.md` cho khớp thực tế, ghi ngày và lý do |
| D4 | `Q-E5` rải rác qua bốn đầu việc | Một dòng tổng: sáu component, mỗi cái đi cùng đầu việc dùng nó; kit GĐ2 có **17** |

**C2 làm khác đề xuất ban đầu, và lý do đáng ghi:** dự định là `afterEach` xóa theo danh sách `postId`.
Nhưng bài tạo qua UI chỉ lộ `postId` sau khi màn render xong — test đỏ trước đó thì **không có id nào để xóa**,
tức là đúng ca cần dọn lại là ca dọn không được. Hỏi thẳng *"bài của tài khoản này"* thì không cần biết id, và
tài khoản là mới ở mỗi test nên phạm vi xóa không bao giờ chạm dữ liệu test khác. Mọi lỗi lúc dọn đều nuốt:
dọn rác hỏng **không được** biến một test xanh thành đỏ.

**C3 (`globalSetup` gộp tài khoản để giảm 51 lượt `/auth/*`) — CỐ Ý KHÔNG LÀM.** Gộp tài khoản đổi ~5 phút lấy
việc các spec phụ thuộc lẫn nhau: `post-create` đăng bài sẽ làm `post-forbidden` đếm sai số card. Cái giá đó
đắt hơn thời gian tiết kiệm được. Đóng nợ bằng "chấp nhận ~8 phút, chạy một lần ở `F3`".

#### `useRef(new AbortController())` chết dưới StrictMode — lỗi do chính lượt dọn này tạo ra

Bản `D1` đầu tiên viết `const abortRef = useRef(new AbortController())`. Khuôn đó **chết** dưới `<StrictMode>`
của Next dev: React mount → unmount → mount lại, cleanup của lần mount đầu gọi `abort()`, và lần mount thứ hai
`useRef` trả về **đúng controller vừa bị hủy**. Mọi lượt `PUT` sau đó ném `AbortError` ngay, và vì `sendOne` cố
ý bỏ qua `AbortError` nên mọi ảnh **kẹt ở `dang-gui` vĩnh viễn**.

Đo được: **ba spec E2E đỏ** (`post-create`, ca R2 của `csp`, ca ảnh của `login-storage`) trong khi **Vitest vẫn
xanh 447 ca**. Vitest render thẳng, không qua StrictMode — nên cả 447 ca không ca nào chạm tới lớp lỗi này.

Đã sửa: tạo controller **trong effect**. Và quan trọng hơn bản vá, đã thêm **một ca canh**: render
`PostComposer` bọc `<StrictMode>` rồi tải một ảnh, đòi `xong`. Ca đó **đã thử cho đỏ** — áp lại khuôn
`useRef(new ...)` thì nó đỏ, khôi phục thì xanh.

**Luật rút ra:** tài nguyên có vòng đời (controller, subscription, timer) **không bao giờ** khởi tạo bằng
`useRef(new Thing())` — lần mount thứ hai nhận lại cái đã hủy. Khởi tạo trong effect, hủy trong cleanup của
chính effect đó.

#### `retries: 1` cho Playwright

Worker Playwright thỉnh thoảng chết trên Windows với `0xC0000409` (STATUS_STACK_BUFFER_OVERRUN) **trước khi
test chạy dòng đầu tiên** — gặp 2026-09-21 với `register.spec.ts`: đỏ ở **0ms** trong lượt cả bộ, chạy riêng
ngay sau đó **xanh trong 1,9 giây**. Không có `retries` thì một lần Windows hắt hơi là mất cả lượt 8 phút.

`retries` ở đây **không che lỗi**: Playwright in riêng dòng `flaky` khi một ca đỏ rồi xanh lại, và con số đó
phải dán vào PR y như `passed`/`failed` — một ca flaky vẫn là một ca cần nhìn.

#### Bằng chứng sau lượt dọn

Vitest **442 → 448** (+5 `buildPatch`, +1 ca StrictMode). `lint` / `typecheck` / `build` xanh. Playwright cả bộ,
**Chrome 153.0.8010.50**, `workers: 1`:

```
1 skipped
17 passed (7.9m)
```

**0 failed, 0 flaky.** Ca `skipped` vẫn là `single-flight.spec.ts` — skip có điều kiện từ GĐ1.

### Đóng lớp lỗi StrictMode: cổng lint + bốn ca canh (2026-09-21)

Lượt dọn trên để lại **một ca canh cho một màn**. Còn bốn màn nữa cũng sở hữu `AbortController` mà không ca
nào đi qua chu kỳ mount → unmount → mount. Ba tầng dưới đây đóng nốt, và **không tầng nào thay được tầng kia**:
tầng 1 bắt lúc gõ nhưng chỉ biết cú pháp, tầng 2 bắt hành vi nhưng chỉ ở màn có người nhớ viết ca.

**Tầng 1 — cổng ESLint `USE_REF_NEW`.** Một mục mới trong `no-restricted-syntax` của `eslint.config.mjs`, chặn
`useRef(new …)` ở **mọi** file `.ts`/`.tsx`:

```
CallExpression[callee.name='useRef'] > NewExpression,
CallExpression[callee.property.name='useRef'] > NewExpression
```

Chặn **mọi** `new`, **không** liệt kê danh sách loại tài nguyên (`AbortController | WebSocket | …`). Danh sách
thì loại chưa có trong đó lọt qua im lặng — đúng kiểu **cổng xanh giả** mà Mục 7 của `frontend-rules.md` đã bỏ
một lần ở `gen-api.mjs` (đổi 2026-09-19). Cái giá của lựa chọn này là nó cũng bắt những chỗ *đang* đúng; xem
`seenRef` dưới đây.

`components/ui/**` **không** bị rule này, vì khối cuối của config tắt `no-restricted-syntax` cho kit. Có chủ
đích: kit sinh bởi CLI và không sửa tay, nên bắt nó đỏ là chặn chính `pnpm exec shadcn add` mà Đ-E12 bắt dùng.

**Đã thử cho đỏ** (luật FE Mục 9): áp lại `useRef(new AbortController())` vào `use-upload-queue.ts` → rule bắt
đúng dòng đó; khôi phục → xanh; `git status` sạch trước và sau.

**Chỗ duy nhất rule bắt được trong repo:** `use-post-page.ts` có `const seenRef = useRef(new Set<string>())`.

Kiểm rồi mới dám nói: **khuôn này ở ĐÂY chưa hỏng được.** Dự đoán ban đầu là "mount 2 nhận lại set đã đủ
`postId` → lô vừa về bị gạt sạch → 0 card". **Sai.** Chốt `run !== runRef.current` trong `fetchPage` cắt lượt
gọi của mount 1 **trước** dòng khử trùng, nên set không kịp có gì để gạt nhầm. Thử cho đỏ đã chứng minh: bỏ
hẳn dòng `seenRef.current = new Set()` thì ca StrictMode vẫn **xanh**.

Vẫn đổi, vì **cấm là cấm khuôn, không cấm theo từng chỗ có triệu chứng** — chỗ nào đang đúng cũng chỉ đúng nhờ
một chốt khác, và chốt đó có thể đi mất trong lần sửa sau. Đã đổi sang khởi tạo **lười**:

```ts
const seenRef = useRef<Set<string> | null>(null)
const seen = useCallback(() => (seenRef.current ??= new Set<string>()), [])
```

Không đổi hành vi quan sát được.

**Tầng 2 — bốn ca `<StrictMode>` mới**, mỗi màn sở hữu tài nguyên hủy được đúng một ca:

| Màn | Ca khẳng định | Bắt được gì mà ca thường không bắt |
|---|---|---|
| `MeProfile` | hiện `me-profile`, **không** có `role="alert"` | lượt gọi của mount 1 bị hủy phải im lặng, không thành báo lỗi |
| `PostDetail` | hiện `post-card` | không kẹt ở khung chờ |
| `UserPosts` | **đúng 1** card | mount 2 dùng lại controller đã hủy → 0 card |
| `PublicProfile` | hiện `public-profile` | màn này trước đó **không có file test nào** |

Ca StrictMode khẳng định **trạng thái cuối**, **không đếm số request** — dưới StrictMode số request tăng gấp đôi
một cách hợp lệ.

**Thử cho đỏ cả bốn** (áp `useRef(new AbortController())` vào từng màn, chạy, rồi khôi phục):

| Màn | Ca StrictMode | Ca CŨ cũng đỏ theo |
|---|---|---|
| `MeProfile` | đỏ | 2 — `Tải lại` gọi `/me` lần nữa · lỗi 500 rồi `Tải lại` |
| `PostDetail` | đỏ | 2 — 5xx bấm `Thử lại` · đổi `postId` |
| `UserPosts` | đỏ | 2 — đổi `userId` · trang đầu hỏng rồi `Thử lại` |
| `PublicProfile` | đỏ | 0 — **màn này trước đó không có file test nào** |

Cột cuối là thứ đáng đọc kỹ: ba màn đầu **đã** có ca chạy lại effect (bấm `Thử lại`, đổi id), nên lớp lỗi này
không hoàn toàn trần trụi ở đó. Chỗ trần trụi thật là `PublicProfile` — và chính là chỗ không ai nghĩ tới, vì
nó không có file test để mà thấy thiếu.

**CỐ Ý KHÔNG LÀM: bọc `<StrictMode>` toàn cục trong `test/setup.ts`.** Một dòng, phủ hết — nhưng đã đếm: 15 file
dùng `render()` và ít nhất 12 khẳng định đang **đếm số lần** (`expect(seen).toHaveLength(2)`,
`toHaveBeenCalledTimes(1)`). Bọc toàn cục làm đỏ hết chỗ đó, và tệ hơn việc phải sửa: những ca ấy đang canh đúng
bất biến *"chỉ gọi một lần"* — khử trùng, single-flight, chống bấm hai lần. Đổi chúng sang "gọi hai lần cũng
được" là **vứt một lớp canh thật để đổi lấy một lớp canh khác**. Ca StrictMode phải là ca **riêng**, tự khai nó
đang đo chiều nào.

**Lỗ hổng thật mà cổng lint lôi ra: dòng `seenRef.current = new Set()` KHÔNG có ca nào canh.** Bỏ hẳn dòng
đó thì cả 22 ca của `user-posts.test.tsx` vẫn xanh. Nhưng nó **load-bearing**: nút `Tải lại` của
`post-list.tsx` chỉ hiện ở đúng một trạng thái — `error !== null` **và** `items` còn — tức là *trang sau hỏng,
danh sách cũ vẫn trên màn*. Bấm nó thì `attempt` đổi → `pageKey` đổi → `base` rỗng, mà lô trang đầu vừa lấy
lại **nằm sẵn** trong set của lượt trước nên bị gạt hết: **màn trắng sau một cú bấm**.

Ca `Thử lại` đã có không thay được: ở đó trang **đầu** hỏng ngay nên chưa `postId` nào vào set. Đã thêm ca
*"trang sau hỏng rồi bấm Tải lại: danh sách VỀ LẠI"* — bỏ dòng reset thì **đúng một ca** đỏ, khôi phục thì xanh.

Đây là lãi ngoài dự tính của tầng 1: rule không tìm ra lỗi ở `seenRef`, nhưng nó **bắt phải đọc lại** dòng đó,
và chỗ thiếu ca lộ ra từ lần đọc ấy.

**Lỗi tìm ra khi rà, đã sửa: flake có sẵn ở `user-posts.test.tsx`.** Ca *"bấm Xem thêm hai lần khi lượt đầu còn
bay"* đỏ **1 trong 3 lượt** khi chạy cả bộ, luôn xanh khi chạy riêng file. Nguyên nhân là `waitFor` mặc định 1s
trong khi handler có `await delay(60)` cộng thời gian jsdom xử lý dưới tải của 39 file test. **Đo trên cây
TRƯỚC thay đổi này** (`git stash` rồi chạy 3 lượt: 1 đỏ) nên là nợ cũ, không phải hệ quả của ca StrictMode. Đã
cho hai `waitFor` phụ thuộc `delay` một `timeout` viết tay 5s — khẳng định y nguyên, chỉ là không còn thua vì
máy bận.

**Bằng chứng:** Vitest **448 → 453** (+4 ca StrictMode, +1 ca `Tải lại`). `lint` / `typecheck` / `build`
xanh. Cả bộ chạy **3 lượt liên tiếp không đỏ lượt nào** — trước khi sửa flake là 1 đỏ trong 3.

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

1. **Xác nhận `mocks/` chỉ phục vụ Vitest** — dùng **lệnh đã sửa ở Q-E7**, không phải lệnh cũ:

   ```
   grep -rn "@/mocks" app features components lib --include="*.ts" --include="*.tsx" | grep -v "\.test\."
   ```

   → không dòng nào. `ls src/frontend/public/` → không có `mockServiceWorker.js`.
   *(Sửa 2026-09-21 khi thi công `F2`: bước này vẫn ghi lệnh cũ `grep -rn "@/mocks" app features components lib`
   dù Q-E7 đã sửa — lệnh cũ ra **46 dòng**, tất cả là file `.test.*`.)*
2. **Kiểm tab Network trên `https://mxh.banhgao.net`** trong một lượt đăng bài có ảnh. Bốn điều phải đúng:

   | Kiểm | Kỳ vọng |
   |---|---|
   | Danh sách origin được gọi | **Chỉ** `https://mxh.banhgao.net` (`/bff/*`) và host R2 (`PUT`, `GET` ảnh) — cộng hai script Cloudflare tự chèn (`static.cloudflareinsights.com`, `/cdn-cgi/*`), đã chấp nhận ở Đ-E15 ngày 2026-09-21 |
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
| Chỉ lọc `/bff/*` + host R2 rồi tick "chỉ hai origin" | Bỏ sót script Cloudflare tự chèn vào trang (xem Thực tế thi công) | Liệt kê **mọi** origin trong tab Network, không lọc trước |

### Thực tế thi công

**Chạy 2026-09-21, Chrome 153.0.8010.50, trên `https://mxh.banhgao.net`**, hai lượt, kết quả trùng nhau. Mỗi lượt
dùng một hồ sơ trình duyệt mới (tương đương cửa sổ ẩn danh, không bundle cũ): đăng nhập bằng tài khoản staging của
nhóm → đăng bài **2 ảnh** (`e2e/fixtures/`, riêng tư "Chỉ mình tôi") → xem trên `/me` và `/posts/{id}` → xóa bài.
Không kiểm bằng mắt từng dòng mà bằng một script Playwright (`channel: "chrome"`) ghi **mọi** request và body
response. Tab Network thấy đúng những thứ đó. Script không commit (Mục 15: `F1`–`F3` không sinh commit code).

| Kiểm | Kết quả |
|---|---|
| Bước 1 — `grep` bản Q-E7 · `public/` | 0 dòng · không có `mockServiceWorker.js` ✅ |
| Origin **app** gọi | `https://mxh.banhgao.net` (`/bff/*` và trang, chunk RSC) + host R2: **2 `PUT` → 200, URL có `X-Amz-Signature`**, 7 `GET` ảnh → 200, cả 2 ảnh hiện (`naturalWidth > 0`) ✅ |
| Header `Authorization` | 0 request ✅ |
| JWT trong body response (fetch/xhr/document) | 0 ✅ |
| `mediaKey`/`storageKey`/`storage_key` trong response | Chỉ `POST /bff/api/media/uploads`: key **của chính người gọi**, vừa được cấp, theo thiết kế (Q-D1: client gửi lại key đó ở `POST /posts`). Response của `/bff/api/posts/*` và `/bff/api/users/*/posts` **không** có key nào ✅ |
| Web Storage | Local 0, Session 0 ✅ |
| `Set-Cookie` từ `/bff/*` | Chỉ `POST /bff/auth/login` → `__Host-sid` (`HttpOnly`, `Secure`, `SameSite=Lax`, `Path=/`). Không cookie refresh của API ✅ |
| CSP | 0 vi phạm trong console — `PUT` và `GET` lên R2 đều qua ✅ |

**Lệch kỳ vọng "chỉ hai origin" — do Cloudflare, không do code app.** ✅ **Nhóm chốt 2026-09-21: chấp nhận (đường b),
đã ghi vào Đ-E15** trong [huong-dan-khoi-e-frontend.md](../giai-doan-1/huong-dan-khoi-e-frontend.md). Với ngoại lệ
đó, F2 **đạt**.

- Cloudflare (proxy trước VPS) **tự chèn** hai script vào HTML: Web Analytics (`static.cloudflareinsights.com/beacon.min.js`,
  gửi `POST /cdn-cgi/rum`) và bộ dò bot JS Detections (`/cdn-cgi/challenge-platform/.../main.js`, gửi `POST`
  tới `/cdn-cgi/challenge-platform/...`), kèm cookie `cf_clearance` (`HttpOnly`, `Secure`). Cả hai **chạy được
  dưới CSP** mà không có vi phạm nào. Lý do: Cloudflare chép nonce từ header CSP sang thẻ `<script>` nó chèn, và
  CSP không có `'strict-dynamic'` nên một script mang nonce được tải từ **bất kỳ host nào**.
- Hệ quả bảo mật: trình duyệt không cầm token nào (ba dòng đầu bảng trên đều sạch), nên script bên thứ ba không
  có gì của phiên để lấy. Tuy vậy, đây là JS lạ chạy trong origin của app, **đi vòng qua** tinh thần của Đ-E15:
  thêm domain vào CSP phải là quyết định mới, còn ở đây domain lọt vào mà không cần sửa CSP.
- Hai đường đã cân nhắc: (a) tắt *Web Analytics automatic setup* và *JS Detections* của zone trên dashboard Cloudflare;
  (b) chấp nhận và ghi vào Đ-E15. Nhóm chọn **(b)**. Dòng "Tab Network staging: chỉ `/bff/*` + host R2" ở Mục 16
  tick được khi đọc kèm ngoại lệ này.

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

### Thực tế thi công

**Chạy 2026-09-21 (06:37–06:39Z), Chrome 153.0.8010.50, trên `https://mxh.banhgao.net`, tài khoản MỚI.** Chạy bằng
script Playwright (`channel: "chrome"`, hồ sơ trình duyệt mới), không commit (Mục 15). **ISS-02 đóng: cả ba lượt
`PUT` từ trình duyệt lên R2 staging đều 200**, không có vi phạm CSP hay lỗi CORS nào trong console.

| Bước | Kết quả |
|---|---|
| Đăng ký qua UI | `POST /bff/auth/register` → **201**. Mail thật qua Resend về hộp thư gmail của nhóm (địa chỉ có `+f3-…`) |
| Xác minh | Link trong mail thật. Lượt mở link của script thấy "đã được sử dụng": link **đã bị mở trước đó** (người nhận bấm, hoặc Gmail quét link). Bằng chứng tài khoản đã xác minh là bước đăng nhập kế tiếp thành công |
| Đăng nhập → onboarding | Bị đưa về `/onboarding` (chưa có hồ sơ). Đặt tên → vào app |
| Avatar | `PUT` → **200** lên `socialmedia-staging/avatars/{userId}/….jpg`, `GET` → 200, ảnh hiện |
| Đăng bài 2 ảnh | Hai `PUT` → **200** lên `socialmedia-staging/posts/{userId}/….jpg` và `….png`. Bài hiện **2/2 ảnh** (`naturalWidth > 0`) |
| Sửa | Đổi riêng tư → nhãn "đã chỉnh sửa" hiện |
| Xóa | Về `/me`. Mở lại URL cũ → "Không tìm thấy bài viết." |
| Web Storage cuối lượt | 0 mục |

**Năm bằng chứng:**

| # | Kết quả |
|---|---|
| 1 | `PUT` avatar: **200**. Query `X-Amz-Algorithm=AWS4-HMAC-SHA256`, `X-Amz-Expires=600`, `X-Amz-SignedHeaders=content-length;content-type;host`, `X-Amz-Signature` (đã che). Request mang `Content-Type: image/jpeg`, `Origin: https://mxh.banhgao.net`. Response có `Access-Control-Allow-Origin: https://mxh.banhgao.net`, `Access-Control-Expose-Headers: etag`, `ETag`. Hai `PUT` của bài: y hệt, cùng 200 |
| 2 | Object nằm đúng tiền tố: `avatars/{userId}/` và `posts/{userId}/` trong bucket `socialmedia-staging` (đọc từ đường dẫn `PUT` và `GET` 200). **Ảnh chụp dashboard R2 do người có quyền Cloudflare bổ sung vào PR** |
| 3 | Ảnh chụp trang `/posts/{id}`: hai ảnh hiển thị thật, không placeholder |
| 4 | `GET` ảnh: **200** từ host R2, `X-Amz-Expires=900` (Đ-2.9: 15 phút), `X-Amz-SignedHeaders=host`, `Content-Type: image/jpeg` |
| 5 | URL presigned GET của avatar, ký lúc 06:38:17Z, hết hạn 06:53:17Z, gọi lại lúc 06:54:17Z → R2 trả **403 `ExpiredRequest`** ("Request has expired"). Avatar **không bị gỡ**, object vẫn còn, nên 403 này là do hết hạn chứ không phải do object bị dọn |

**Hai tín hiệu nhiễu, đã kiểm và không phải lỗi:** Playwright báo `net::ERR_ABORTED` cho `POST /bff/auth/login` và
`DELETE /bff/api/posts/{id}`. Chạy thêm một lượt dò: cả hai đều **đã nhận response 204** trước khi bị ghi là hủy.
Đây là cách Chrome báo một response 204 trùng lúc trang điều hướng, không phải request bị cắt. Các `GET` bị
`ERR_ABORTED` còn lại là prefetch/RSC bị điều hướng ngắt.

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

### Thực tế thi công

**Rà 2026-09-21: 13 dòng tick, 7 dòng chờ server staging hoặc dashboard R2** (Mục 11 + Mục 12 của `giai-doan-2.md`,
bằng chứng ghi ngay tại từng dòng). Dòng chờ **không** tick bằng suy luận: phiên thi công không có quyền SSH vào VPS, và
bảy dòng đó đều là loại mục này cấm tick từ CI xanh hay từ máy dev.

**Đã chạy:**

| Kiểm | Kết quả |
|---|---|
| `dotnet test` | Unit **218/218**, Architecture **13/13**, Integration **319/320**. Ca đỏ duy nhất là `StartupConfigurationTests.Development_boots_without_r2_config…`: đỏ nền của máy dev vì `user-secrets` có khóa R2; CI xanh |
| CI run 35561152514 (`develop`, `3621c10`) | `API contract` 6/6, `AuthZ matrix` 18/18 |
| CD run 35561152520 | `[migrate] Đã áp dụng migration cho schema "identity", "profile", "content" … Thoát 0`; bốn container `healthy` |
| `pnpm gen:api` | exit 0, `git status --porcelain` rỗng |
| psql trên Postgres **dev** (cùng bộ migration) | Ba `__EFMigrationsHistory`; **0** FK chéo schema (8 FK, đều cùng schema); 77 bài, 0 `author_id` lạ, 0 tác giả không hồ sơ |
| Bucket không public | URL hết hạn → **403 `ExpiredRequest`** (`F3` #5); bỏ hẳn chữ ký → **400 `InvalidArgument`** |

**Máy dev không thay được server ở dòng log.** Container `socialapp-dev-api-1` là bản build 2026-09-18, không chạy lát
cắt GĐ2 (E2E dev chạy API bằng `dotnet run`), nên đếm `X-Amz-Signature` = 0 trong log của nó **không** là bằng chứng.

**Lệnh cho bảy dòng còn chờ** — chạy trong `~/app/deploy` trên VPS, `C="docker compose -f docker-compose.staging.apache.yml"`:

```bash
# Mục 11 dòng 6 — log api: 0 presigned URL, 0 email
$C logs api | grep -c "X-Amz-Signature"
$C logs api | grep -c -E "[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[a-z]{2,}"

# Mục 12 — FK chéo schema (phải ra 0) và posts.author_id (hai số sau phải là 0)
$C exec postgres psql -U socialapp -d socialapp -At -c "
  select count(*) from information_schema.referential_constraints rc
  join information_schema.table_constraints a on a.constraint_name=rc.constraint_name and a.constraint_schema=rc.constraint_schema
  join information_schema.table_constraints b on b.constraint_name=rc.unique_constraint_name and b.constraint_schema=rc.unique_constraint_schema
  where a.table_schema<>b.table_schema;"
$C exec postgres psql -U socialapp -d socialapp -At -c "
  select count(*), count(*) filter (where u.user_id is null), count(*) filter (where pr.user_id is null)
  from content.posts p left join identity.users u on u.user_id=p.author_id left join profile.profiles pr on pr.user_id=p.author_id;"

# Mục 12 — migrate lần hai: exit 0, không áp migration nào mới
$C run --rm migrate; echo "exit=$?"

# Mục 12 — worker: đặt Media__Cleanup__Enabled=true trong .env, khởi động lại api, chờ > 60 phút (lượt đầu SAU một chu kỳ)
$C up -d api && sleep 3700 && $C logs api | grep "Dọn rác media"
# → "Dọn rác media: N object của bài xóa mềm, M object mồ côi, thu hồi B byte"

# Mục 12 — tắt Redis: worker bỏ lượt, api vẫn phục vụ. DỪNG REDIS LÀ ĐĂNG XUẤT MỌI PHIÊN BFF trên staging — báo nhóm trước
$C stop redis && sleep 3700 && $C logs api | grep "bỏ lượt dọn rác"; curl -fsS https://mxh.banhgao.net/health/live
$C start redis
```

Dòng thứ bảy là **ảnh chụp dashboard R2** bucket `socialmedia-staging`. Object của lượt `F3` nằm dưới `avatars/{userId}/`,
dưới `posts/{userId}/` là hai ảnh của bài đã xóa (worker còn chưa dọn: bài xóa mềm được giữ 7 ngày).

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

### Thực tế thi công

**2026-09-21.**

- **Đóng băng:** hai file hợp đồng có thêm khối `ĐÓNG BĂNG (2026-09-21 …)` ở phần chú thích đầu file, theo nếp của
  `identity-v1.yaml`. Chỉ thêm comment, schema không đổi: `pnpm gen:api` → `git status` rỗng; `Category=Contract`
  6/6. README Mục 1 ghi trạng thái. **Thông báo nhóm do người trong đội gửi**, kèm link PR.
- **Hoãn có địa chỉ:** bảng Mục 2 của `giai-doan-2.md` đã có sẵn chín dòng của bước 2. Thêm **hai dòng** mà trước đó
  chỉ nằm trong thân tài liệu, không có trong bảng: sửa danh sách ảnh của bài (GĐ7, từ Mục 7.3) và dọn avatar mồ
  côi (GĐ8, từ Mục 7.5). Nợ chỉ nằm ở thân tài liệu thì người mở GĐ7/GĐ8 không đọc tới, tức là nợ vô chủ.
- **Hai bẫy cho GĐ4:** GĐ4 chưa có tài liệu riêng nên ghi vào mục GĐ4 của `ke-hoach-trien-khai.md`, kèm lời nhắc
  "chép vào tài liệu GĐ4 khi mở giai đoạn". Bẫy `IFriendshipReader` ghi theo code thật: dòng DI nằm **trong**
  `ContentModuleExtensions.cs`, nên "không chạm module Content" nghĩa là không chạm **logic** của Content.
  `AlwaysStrangersTests` **không** đỏ khi đổi DI (nó kiểm chính class null-object), nên cái canh là `READ-01` và
  test của `PostVisibility`.
- **Ba điều kiện B.11 — thỏa cả ba:**

  | # | Bằng chứng |
  |---|---|
  | 1 | `F3`: `PUT` từ Chrome thật lên `socialmedia-staging` → 200; bài hiện 2/2 ảnh |
  | 2 | `AuthZ matrix` 18/18 trên CI run 35561152514; bảng đột biến `B3` (bỏ `post.AuthorId != actorId` → đỏ `TC-A03` / `TC-A03-delete`) |
  | 3 | CI run 35561152514 (`develop`, `3621c10`): `Test (unit + integration)`, `AuthZ matrix`, `API contract`, `API types khop hop dong`, `Bundle production sach…` (grep có `R2__` và `X-Amz-Signature`, rủi ro R2-01) — xanh cả năm |

- **GĐ4 được phép bắt đầu.** Ba điều kiện B.11 là điều kiện **mở GĐ4**. Bảy dòng còn chờ của `F4` là kiểm tận
  nơi trên server, không chặn GĐ4, nhưng phải tick trước PR phát hành `develop` → `main` (luật PR Mục 6: "Migration
  EF đã chạy trên staging … schema khớp").

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
- [ ] **`pnpm test:e2e` chạy CẢ BỘ, xanh, kèm bản Chrome đã chạy** — cổng của `F3`, không phải của từng đầu
      việc (cả bộ tốn ~8 phút). *Thêm 2026-09-21 sau `E8`:* bốn spec của GĐ1 đã đỏ từ commit `0aacb96` (`E2`)
      suốt ba commit mà không ai biết, vì mỗi đầu việc chỉ chạy spec của chính nó và Playwright không vào CI
      (Q-E8). Một bộ E2E không ai chạy cả lượt thì không phải cổng — nó là mấy file đỏ chờ người phát hiện
- [ ] `pnpm gen:api` xong worktree sạch
- [ ] Đ-E17 đã ghi vào hướng dẫn khối E của GĐ1, trong cùng commit với code CSP
- [ ] Không `console.log` token, `uploadUrl`, presigned GET, hay link xác minh
- [ ] Biến mới của BFF là biến server (không `NEXT_PUBLIC_`), có trong `deploy/.env.example`, production thiếu thì báo tên biến

**Khối F**

- [x] `R2__*` + biến host R2 của FE đã có trên `deploy/.env` của server **trước khi merge** (gián tiếp: `F3` ghi vào đúng `socialmedia-staging`, CSP có host R2)
- [ ] `migrate` xanh cho cả ba schema; chạy lần hai không đổi gì
- [x] `/health/ready` 200; hai file `swagger.json` mới 200; header CSP trên staging **có host R2** (2026-09-21)
- [x] Tab Network staging: chỉ `/bff/*` + host R2; không `Authorization`; không `storage_key` của ai (`F2`; cộng script Cloudflare, chấp nhận ở Đ-E15)
- [ ] Năm bằng chứng của `F3` đã dán vào PR, chữ ký đã che
- [ ] Mục 12 tick hết hoặc ghi lý do hoãn kèm địa chỉ; Mục 11 tick đủ sáu
- [ ] `docker compose logs api | grep -c "X-Amz-Signature"` = **0**
- [x] Hai hợp đồng tuyên bố đóng băng; phần hoãn có địa chỉ đã liệt kê; hai bẫy chéo giai đoạn đã ghi cho GĐ4 (`F5`)
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
