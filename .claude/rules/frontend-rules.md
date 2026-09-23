# Quy tắc frontend

Áp dụng cho mọi thay đổi trong `src/frontend/`. File này là **bản rút gọn để thi hành** — nó nói
*phải làm gì* và *cấm gì*, không giải thích dài.

**Nguồn sự thật, theo thứ tự ưu tiên khi mâu thuẫn:**

1. Hợp đồng API — `src/backend/Modules/<Module>/Presentation/<nhóm>.yaml`
2. Mười sáu quyết định `Đ-E1`–`Đ-E16` trong [`docs/giai-doan-1/huong-dan-khoi-e-frontend.md`](../../docs/giai-doan-1/huong-dan-khoi-e-frontend.md)
3. `src/frontend/AGENTS.md` (luật Next.js của template + mục "UI kit")
4. File này

Thấy file này lệch 1–3 thì **sửa file này**, không sửa ngược lại. Muốn đổi một `Đ-E*` thì đó là
quyết định mới, có ngày tháng, ghi vào tài liệu gốc trong cùng commit — không sửa lặng trong code.

---

## 0. Đọc trước khi gõ dòng đầu tiên

- **Next.js ở đây không giống bản trong trí nhớ.** `src/frontend/AGENTS.md` bắt đọc
  `node_modules/next/dist/docs/` trước khi viết code. Làm thật, đừng suy từ Next 13/14.
- **shadcn/ui ở đây dựng trên Base UI, không phải Radix.** Mẫu trên mạng phần lớn là Radix và sẽ
  sai. Tra bằng `pnpm exec shadcn docs <component>`.
- Đọc mục tương ứng trong hướng dẫn khối E trước khi làm một việc `E*` — mỗi mục có sẵn phần
  "cạm bẫy đã biết".

## 1. Mười bốn điều không bao giờ làm

| # | Cấm | Vì |
|---|---|---|
| 1 | Gọi `fetch` ngoài `lib/api/http.ts` (trình duyệt) và `lib/bff/upstream.ts` (server) | Đ-E2, Đ-E14 — ESLint chặn |
| 2 | `localStorage`, `sessionStorage`, `document.cookie` | Đ-E2 — không có gì của phiên nằm ở đó |
| 3 | Sửa tay `lib/api/**/schema.d.ts` | File sinh; sửa tay là cổng CI codegen đỏ |
| 4 | Thêm component UI bằng `pnpm dlx shadcn@latest add` | Đ-E12 — phải `pnpm exec shadcn add` (bản ghim) |
| 5 | Màu thô (`bg-blue-600`), mã màu tùy ý, radius riêng theo màn | Đ-E12 — token ở `app/globals.css` |
| 6 | Import `@base-ui/react` ngoài `components/ui/` | Đ-E12 |
| 7 | Tạo `app/api/**`, hoặc route FE dưới `/api`, `/health`, `/swagger` — Route Handler chỉ dưới `app/bff/**` | Đ-E11, Đ-E14 — apache đẩy hết `/api` về backend |
| 8 | Guard hay logic đăng nhập trong `proxy.ts` — file đó CHỈ gắn CSP có nonce | Đ-E3, Đ-E15 |
| 9 | `npm`/`yarn`, hoặc thêm `^`/`~` vào `package.json` | Đ-E9 — pnpm, ghim chính xác |
| 10 | `features/` import chéo nhau; `lib/`, `components/` hay `hooks/` import ngược lên `features/`, `app/` | Đ-E13 |
| 11 | Trả access/refresh token (hay `Set-Cookie` của API) ra trình duyệt từ bất kỳ route BFF nào | Đ-E14 — trình duyệt không bao giờ cầm JWT |
| 12 | Module server của BFF thiếu `import "server-only"`, hoặc biến cấu hình server mang tiền tố `NEXT_PUBLIC_` | Đ-E14 — `NEXT_PUBLIC_*` bị nhúng vào bundle |
| 13 | Script inline tự viết không mang nonce, `dangerouslySetInnerHTML` chứa script, thêm `'unsafe-inline'` / `'strict-dynamic'` / domain lạ vào CSP | Đ-E15 — CSP chặn; nới CSP là quyết định mới |
| 14 | `useRef(new Thing())` — controller, subscription, timer, observer khởi tạo ở tham số của `useRef` | StrictMode mount lại trả về **đúng cái vừa bị hủy**; ESLint chặn. Tạo trong effect, ref chỉ là hộp đựng |

## 2. Đặt file ở đâu (Đ-E13)

Bốn tầng, phụ thuộc **một chiều**: `app/` → `features/` → `components/` + `hooks/` + `lib/`.

| Thư mục | Chứa gì | Nhận biết |
|---|---|---|
| `app/` | Route, layout, `page.tsx` mỏng | Chỉ ráp; không có logic nghiệp vụ |
| `features/<màn>/` | Nghiệp vụ: form, card, composer, hook riêng của màn | Biết nghiệp vụ |
| `components/ui/` | Kit shadcn | Sinh bởi CLI, không viết tay |
| `components/form/`, `components/shell/` | UI dùng lại nhiều màn | **Không** biết nghiệp vụ |
| `hooks/` | Hook React dùng lại nhiều màn (phân trang theo cursor…) — tầng ngang `components/` | **Không** biết nghiệp vụ; ESLint cấm import `@/features/*`, `@/app/*` (thêm 2026-09-23, GĐ4 Q-E8) |
| `lib/api/`, `lib/auth/`, `lib/validation/` | Hạ tầng, logic không phải React | Test được bằng Vitest, không cần render |
| `lib/bff/` | Server của BFF (Đ-E14): phiên, Redis, gọi API | `import "server-only"`; test môi trường `node` |

Đặt file mới thì hỏi hai câu, theo thứ tự: *có biết nghiệp vụ không?* (không → `components/`, hoặc
`hooks/` nếu là hook) · *có phải logic không phải React không?* (đúng → `lib/`). Còn lại vào `features/<màn>/`.

**Tên `features/` theo màn, không theo module backend.** Chỉ `lib/api/<module>/` mới bám tên module
1-1 (vì sinh từ `<nhóm>.yaml`). Một module đẻ ra nhiều màn: `Content` → `post/`, `comment/`,
`reaction/`, `feed/`.

**Bên trong một feature:** để phẳng cho tới khi một *loại* file chạm 2 cái mới mở thư mục con
(`components/`, `hooks/`). Không tạo sẵn thư mục rỗng cho giai đoạn chưa tới.

## 3. UI theo kit (Đ-E12)

- Component mới: `pnpm exec shadcn add <tên>` chạy trong `src/frontend/`. So với bản gốc bằng `--diff`.
- `components/ui/**` **chỉ** sửa khi thay đổi áp cho **toàn app**; thêm biến thể bằng `cva` ngay
  trong file đó, không tạo bản sao cạnh nó.
- Token màu/radius chỉ định nghĩa ở `app/globals.css` (`:root`, `.dark`, `@theme inline`). Màn dùng
  tên token: `bg-primary`, `text-muted-foreground`, `text-destructive`, `border-border`.
- Base UI ghép bằng prop `render`, **không có `asChild`**.
- Icon chỉ `lucide-react`. Font chỉ Inter, subset `latin` + `vietnamese`, nạp bằng `next/font`.
- Không `shadcn eject`. Không `shadcn apply` tùy tiện — nó đảo thứ tự dòng trong `globals.css`.
- Kit **không chạy Prettier** (`components/ui/` trong `.prettierignore`) — giữ đúng kiểu shadcn sinh ra, để
  `shadcn add --diff` không bị nhiễu bởi khác biệt trình bày.
- Form dùng `components/form/text-field.tsx`, không tự ráp `Field` + `Input` ở từng màn.

## 4. Gọi API (Đ-E14, Đ-E2, Đ-E6)

- **Trình duyệt chỉ gọi BFF cùng origin** (`/bff/*`), qua `request()` trong `lib/api/http.ts`, `credentials: 'same-origin'`.
  Không bearer, không CORS, không biến `NEXT_PUBLIC_API_BASE_URL`. Module GĐ2+ gọi `/bff/api/<đường của API>` — proxy
  chung gắn bearer ở server, không thêm route BFF.
- **Next server gọi API** chỉ ở `lib/bff/upstream.ts`, gốc `API_INTERNAL_URL` (dev mặc định
  `http://localhost:5259/api/v1`). Không trỏ BFF local sang API staging — dữ liệu thật và rate limit của người khác.
- Kiểu lấy từ `lib/api/types.ts` (alias của `schema.d.ts`). **Không tự khai lại** kiểu cho payload
  API — hợp đồng đổi thì phải là lỗi compile, không phải lỗi runtime.
- Không dùng `rewrites` của `next.config` để "proxy" tới API — nó chuyển nguyên header, kể cả `Set-Cookie` có token.
  Proxy là Route Handler `app/bff/api/[...path]` (danh sách trắng header).
- Thông điệp lỗi lấy từ `errorMessage(context, error)` trong `lib/api/messages.ts`, ánh xạ theo
  `(endpoint, status)`. `detail` của server chỉ là dự phòng. 500 phải hiện `traceId`. Mất mạng
  không đoán nguyên nhân. 429 không hiện đồng hồ đếm ngược (server không gửi `Retry-After`).

## 5. Token, phiên, guard (Đ-E14, Đ-E3)

- **Token chỉ ở Next server + Redis** (mã hóa AES-256-GCM, key là băm session ID). Trình duyệt giữ cookie `__Host-sid`
  (`HttpOnly`, `Secure`, `SameSite=Lax`, `Path=/`). `lib/auth/token-store.ts` chỉ giữ **trạng thái phiên**
  (`unknown | authenticated | anonymous | error`), React đọc qua `useSyncExternalStore`.
- Mọi route BFF thay đổi dữ liệu kiểm `Origin` (CSRF); đăng nhập luôn cấp session ID mới (session fixation).
- Guard là component `RequireAuth` trong layout `(app)`. Trạng thái `unknown` hiện khung chờ,
  **không nháy nội dung** rồi mới đá về `/login`.
- **Trình duyệt không bao giờ refresh.** Refresh single-flight ở BFF: khóa Redis theo phiên, so token hiện tại với token
  vừa hỏng trước khi gọi `/auth/refresh`. 401 tới được trình duyệt nghĩa là phiên hết thật → `anonymous`.
- Khởi động phiên: `GET /bff/auth/session`. Đăng xuất báo tab khác qua `BroadcastChannel('socialapp:auth')`.
- Điều hướng theo `?next=` phải đi qua `safeNext` — chặn open redirect.

## 6. Validation client (Đ-E5)

- Client **nới hơn hoặc bằng** server, **không bao giờ chặt hơn**. Chặt hơn là chặn nhầm người dùng
  hợp lệ mà không có đường thoát; nới hơn thì server trả 400 và FE hiện đúng lỗi dưới trường.
- Mật khẩu: `>= 8` ký tự và `<= 72` **byte UTF-8** (`new TextEncoder().encode(pw).length`), không
  `trim`. Màn **đăng nhập** không có tối thiểu 8.
- Thông điệp client dùng **đúng câu của server**, để một lỗi không hiện hai cách nói.
- Mọi 400 từ server hiển thị theo key của `errors` (`email`, `password`, `token`, `body`) — kể cả
  khi client đã kiểm qua.

## 7. Codegen từ hợp đồng

- `pnpm gen:api` sinh type cho **mọi** hợp đồng, file sinh **commit vào repo**.
- Hợp đồng `.yaml` đổi → chạy lại codegen và sửa chỗ đỏ **trong cùng commit**. Cổng CI chạy lại
  `pnpm gen:api` rồi đòi **worktree sạch** (`git status --porcelain` rỗng) — không phải
  `git diff --exit-code`, vì `git diff` im lặng với file chưa commit.
- **Module mới: không thêm script nào** (đổi 2026-09-19). `scripts/gen-api.mjs` **suy ra** danh sách
  hợp đồng từ glob `../backend/Modules/*/Presentation/*-v1.yaml` và ghi ra `lib/api/<nhóm>/schema.d.ts`
  (`<nhóm>` = tên file bỏ hậu tố `-v1`; với mọi module đã lên kế hoạch nó trùng tên module — `identity`,
  `profile`, `content`). **Không có ngoại lệ đường dẫn nào**; Identity đã dời vào `lib/api/identity/`
  cùng ngày. Thêm module = thả file `.yaml` vào, không sửa `package.json` lẫn `ci.yml`.
  *Trước đó luật là "thêm script `gen:api:<module>`". Bỏ vì nó tạo ba danh sách phải khớp nhau —
  hợp đồng nào tồn tại, sinh cho cái nào, cổng kiểm cái nào — mà thiếu một dòng ở danh sách 2 hoặc 3
  đều cho **cổng xanh giả**, không phải đỏ.*
- Hai chỗ `gen-api.mjs` cố ý đỏ thay vì im lặng: **glob không khớp hợp đồng nào** (đổi chỗ thư mục)
  và **hai hợp đồng cùng một đích** (hai file cùng tên nhóm, ví dụ `content-v1` cạnh `content-v2`).
  Phần lập kế hoạch là hàm thuần `planJobs`, có unit test ở `scripts/gen-api.test.ts`.
- Không đổi version `openapi-typescript` kèm theo việc khác — đổi version là đổi file sinh ra.

## 8. MSW chỉ trong Vitest (Đ-E7, đổi 2026-09-17)

- **Không có mock trình duyệt.** Dev luôn chạy đủ FE + BE; không cờ `NEXT_PUBLIC_API_MOCKING`, không
  `public/mockServiceWorker.js`, code app (`app/`, `features/`, `components/`, `lib/`) **không** import `@/mocks/*`.
- `mocks/` chỉ phục vụ Vitest qua `msw/node` (`test/setup.ts`) — để **tái hiện nhánh lỗi khó tạo thật**
  (423, 410, 429, 500) và ghi lại request. `mocks/handlers.ts` giả bề mặt `/bff/*` trình duyệt thấy;
  `mocks/upstream.ts` giả API .NET cho test của `lib/bff`; `mocks/redis.ts` giả Redis.
- Fixture chép **giá trị** từ `example` của hợp đồng và gắn kiểu bằng `satisfies` — hợp đồng đổi hình
  dạng thì mock đỏ compile. Không parse yaml lúc chạy.
- **Cấm nghiệm thu trên mock.** Cổng đóng phải trỏ API thật.

## 9. Test (Đ-E8)

| Công cụ | Kiểm | Chạy ở |
|---|---|---|
| Vitest + Testing Library + `msw/node` | validation, `ApiError`, client tới BFF, BFF ở server (phiên, CSRF, không rò token, refresh single-flight), form (nhánh lỗi qua `msw/node`), type | local + **CI** |
| Playwright (Chrome đã cài, `channel: "chrome"`) | guard, không token trong Web Storage, 3 tab một refresh, lượt E2E trên dev | local, **`workers: 1`** (rate limit theo IP) |

- Playwright **không vào CI ở GĐ1** (cần API + Postgres + Redis + Mailpit chạy). Kết quả chạy local
  dán vào PR — cùng nếp "kiểm tay ghi bằng chứng" của khối D — **kèm bản Chrome đã chạy** (lệch Đ-E8:
  dùng Chrome hệ thống, bản khác nhau giữa các máy).
- File test nằm **cạnh mã nguồn** (`lib/validation/auth.test.ts`). `test/` chỉ chứa **harness của Vitest**,
  không chứa ca test nào: `setup.ts` và `server-only.ts` (shim cho alias `server-only`, Đ-E14) — *sửa câu
  này 2026-09-21, trước đó ghi "chỉ chứa `setup.ts`" và đã lệch thực tế từ GĐ1*. `e2e/` chứa spec
  Playwright, `e2e/fixtures/` chứa ảnh thật commit vào repo (Q-E8).
- **Màn nào sở hữu tài nguyên hủy được thì có ĐÚNG một ca `<StrictMode>`** (thêm 2026-09-21). `render(<X />)`
  mount một lần, Next dev mount → unmount → mount lại; lớp lỗi chỉ sống ở lần mount thứ hai nên không ca
  thường nào chạm tới. Ca đó khẳng định **trạng thái cuối đạt được**, **không đếm số request** — dưới
  StrictMode số request tăng gấp đôi một cách hợp lệ, trộn hai thứ vào một ca là tự làm ca test giòn.
  Năm ca hiện có: `post-composer`, `me-profile`, `post-detail`, `user-posts`, `public-profile`.
- **`waitFor` chờ một handler có `delay` thì ghi `timeout` viết tay.** Mặc định 1s đủ khi chạy riêng file
  và KHÔNG đủ khi chạy cả bộ — ca `Xem thêm` của `user-posts` đỏ ~1/3 lượt vì vậy (đo 2026-09-21). Nới
  thời gian chờ không làm ca yếu đi; khẳng định vẫn y nguyên.
- Thêm một luật ESLint hay một cổng CI thì phải **thử cho đỏ một lần** rồi khôi phục — `git status`
  sạch trước và sau.

## 10. Phiên bản, lệnh, và bốn chỗ dễ sai của stack

- pnpm, không npm. Chạy từ `src/frontend/`: `pnpm dev | lint | typecheck | test | build`.
- Ghim chính xác: không `^`/`~`; `save-exact=true` trong `.npmrc`; `"packageManager"` trong
  `package.json` (CI đọc trường này); **Node 24 LTS** trong `.nvmrc` (đổi Đ-E9 ngày 2026-09-17, trước đó là 22) —
  `@types/node` luôn cùng major với `.nvmrc`.
- **Next 16:** `middleware.ts` đổi tên thành `proxy.ts` · `next lint` bị bỏ, script `lint` gọi thẳng
  `eslint` với cấu hình flat · Node `>= 20.9`.
- **Tailwind v4:** không có `tailwind.config.ts`, token nằm trong `app/globals.css`.
- **ESLint flat config:** override cùng tên rule **thay** hẳn options, không cộng dồn — tách pattern
  dùng chung ra hằng số rồi spread lại. Khối tắt luật cho `components/ui/**` phải đứng **sau** khối
  `components/**`, nếu không kit bị cấm import chính Base UI.
- **shadcn:** `shadcn info` đọc `components.json`, không đọc `layout.tsx` — nó báo `font inter` kể cả
  khi template sinh ra font khác. Kiểm bằng mắt trong `app/layout.tsx`.

## 11. Cổng phải qua trước khi commit code FE

Cộng thêm vào [`commit-rules.md`](commit-rules.md) Mục 7, không thay thế:

1. `pnpm lint`, `pnpm typecheck`, `pnpm test`, `pnpm build` — xanh cả bốn.
2. Hợp đồng đổi → `pnpm gen:api` đã chạy lại, `schema.d.ts` nằm trong cùng commit.
3. Không `console.log` token, response đăng nhập/refresh, hay link xác minh.
4. Lệch một `Đ-E*` → ghi ngược vào hướng dẫn khối E **trong cùng commit**, mở bằng "Lệch Đ-E…".
5. `git ls-files src/frontend` có file (không phải gitlink — template shadcn tự chạy `git init`).

Scope commit cho lane này: `gd1-e` (xem `commit-rules.md` Mục 3).

## 12. Đạt / Không đạt

| Không đạt | Vì sao | Đạt |
|---|---|---|
| `await fetch('/api/v1/me')` trong `features/profile/` | Đ-E2 | `await authApi.me()` |
| Tự khai `type MeResponse = { id: string; … }` | Hợp đồng đổi không ai biết | `import type { MeResponse } from '@/lib/api/types'` |
| `className="rounded-lg bg-blue-600 p-4"` | Đ-E12 màu thô | `<Card>` của kit, hoặc `bg-primary` |
| `components/post-card.tsx` | Đ-E13 — nó biết nghiệp vụ | `features/post/post-card.tsx` |
| `features/feed/` import `@/features/post/post-card` | Đ-E13 import chéo | Đẩy xuống `components/` nếu thật sự dùng chung |
| `localStorage.setItem('token', t)` | Đ-E2, Đ-E14 | Trình duyệt không có token — BFF giữ ở server |
| Route BFF trả `Response.json(await apiRes.json())` của `/auth/login` | Đ-E14 — token ra trình duyệt | 204 + cookie `__Host-sid`; token vào Redis |
| `headers: apiRes.headers` khi chuyển response API | Đ-E14 — `Set-Cookie` refresh ra trình duyệt | `relay()` — danh sách trắng header |
| `app/api/me/route.ts` | Đ-E11 — apache không bao giờ cho request tới | Gọi thẳng API từ client |
| `pnpm dlx shadcn@latest add dialog` | Đ-E12 — lệch style các cái đã có | `pnpm exec shadcn add dialog` |
| Đặt tối thiểu 8 ký tự cho mật khẩu ở màn đăng nhập | Đ-E5 — chặt hơn server | Chỉ kiểm bắt buộc + 72 byte |

## 13. Checklist trước khi mở PR frontend

Cộng thêm vào [`pull-request-rules.md`](pull-request-rules.md) Mục 9, không thay thế:

- [ ] File mới đặt đúng tầng (Mục 2); không có thư mục `features/` rỗng
- [ ] Không `fetch`, `localStorage`, `sessionStorage`, `document.cookie` ngoài chỗ được phép
- [ ] Kiểu API lấy từ `lib/api/types.ts`, không khai lại bằng tay
- [ ] UI dùng kit và token; không màu thô; component mới thêm bằng CLI ghim
- [ ] `pnpm lint`, `typecheck`, `test`, `build` xanh
- [ ] Hợp đồng đổi thì `schema.d.ts` đã sinh lại trong cùng commit
- [ ] Lệch `Đ-E*` nào thì đã ghi ngược vào hướng dẫn khối E
- [ ] Không log token / link xác minh
- [ ] Không route BFF nào trả token hay `Set-Cookie` của API ra trình duyệt; module `lib/bff/**` có `import "server-only"` (Đ-E14)
- [ ] Biến môi trường mới của BFF là biến server (không `NEXT_PUBLIC_`), có mặc định dev, production thiếu thì báo tên biến
