# Quy tắc frontend

Áp dụng cho mọi thay đổi trong `src/frontend/`. File này là **bản rút gọn để thi hành** — nó nói
*phải làm gì* và *cấm gì*, không giải thích dài.

**Nguồn sự thật, theo thứ tự ưu tiên khi mâu thuẫn:**

1. Hợp đồng API — `src/backend/Modules/<Module>/Presentation/<nhóm>.yaml`
2. Mười ba quyết định `Đ-E1`–`Đ-E13` trong [`docs/giai-doan-1/huong-dan-khoi-e-frontend.md`](../../docs/giai-doan-1/huong-dan-khoi-e-frontend.md)
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

## 1. Mười điều không bao giờ làm

| # | Cấm | Vì |
|---|---|---|
| 1 | Gọi `fetch` ngoài `lib/api/http.ts` | Đ-E2 — ESLint chặn |
| 2 | `localStorage`, `sessionStorage`, `document.cookie` | Đ-E2 — token chỉ ở memory |
| 3 | Sửa tay `lib/api/schema.d.ts` | File sinh; sửa tay là cổng CI codegen đỏ |
| 4 | Thêm component UI bằng `pnpm dlx shadcn@latest add` | Đ-E12 — phải `pnpm exec shadcn add` (bản ghim) |
| 5 | Màu thô (`bg-blue-600`), mã màu tùy ý, radius riêng theo màn | Đ-E12 — token ở `app/globals.css` |
| 6 | Import `@base-ui/react` ngoài `components/ui/` | Đ-E12 |
| 7 | Tạo `app/api/**`, hoặc route FE dưới `/api`, `/health`, `/swagger` | Đ-E11 — apache đẩy hết về backend |
| 8 | Guard bằng `proxy.ts` (tên mới của `middleware.ts`) | Đ-E3 — server không thấy token lẫn cookie |
| 9 | `npm`/`yarn`, hoặc thêm `^`/`~` vào `package.json` | Đ-E9 — pnpm, ghim chính xác |
| 10 | `features/` import chéo nhau; `lib/` hay `components/` import ngược lên `features/`, `app/` | Đ-E13 |

## 2. Đặt file ở đâu (Đ-E13)

Bốn tầng, phụ thuộc **một chiều**: `app/` → `features/` → `components/` + `lib/`.

| Thư mục | Chứa gì | Nhận biết |
|---|---|---|
| `app/` | Route, layout, `page.tsx` mỏng | Chỉ ráp; không có logic nghiệp vụ |
| `features/<màn>/` | Nghiệp vụ: form, card, composer, hook riêng của màn | Biết nghiệp vụ |
| `components/ui/` | Kit shadcn | Sinh bởi CLI, không viết tay |
| `components/form/`, `components/shell/` | UI dùng lại nhiều màn | **Không** biết nghiệp vụ |
| `lib/api/`, `lib/auth/`, `lib/validation/` | Hạ tầng, logic không phải React | Test được bằng Vitest, không cần render |

Đặt file mới thì hỏi hai câu, theo thứ tự: *có biết nghiệp vụ không?* (không → `components/`) ·
*có phải logic không phải React không?* (đúng → `lib/`). Còn lại vào `features/<màn>/`.

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
- Form dùng `components/form/text-field.tsx`, không tự ráp `Field` + `Input` ở từng màn.

## 4. Gọi API (Đ-E1, Đ-E2, Đ-E6)

- Mọi lời gọi đi qua `request()` trong `lib/api/http.ts`. Mọi lời gọi có `credentials: 'include'`.
- Kiểu lấy từ `lib/api/types.ts` (alias của `schema.d.ts`). **Không tự khai lại** kiểu cho payload
  API — hợp đồng đổi thì phải là lỗi compile, không phải lỗi runtime.
- Base URL: dev mặc định `http://localhost:5259/api/v1` (đặt trong code, không bắt buộc `.env`);
  staging là `/api/v1` tương đối. **Cấm FE local trỏ API staging** — khác site, cookie không đi,
  refresh luôn 401 mà không lỗi nào nói lý do.
- Không dùng `rewrites` proxy ở dev — làm vậy là không bao giờ thử thật CORS + `credentials`.
- Thông điệp lỗi lấy từ `errorMessage(context, error)` trong `lib/api/messages.ts`, ánh xạ theo
  `(endpoint, status)`. `detail` của server chỉ là dự phòng. 500 phải hiện `traceId`. Mất mạng
  không đoán nguyên nhân. 429 không hiện đồng hồ đếm ngược (server không gửi `Retry-After`).

## 5. Token, phiên, guard (Đ-E2, Đ-E3, Đ-E4)

- Access token nằm trong `lib/auth/token-store.ts` (biến module + `subscribe`), React đọc qua
  `useSyncExternalStore`. Không để token chỉ trong React state — api client không phải component.
- Guard là component `RequireAuth` trong layout `(app)`. Trạng thái `unknown` hiện khung chờ,
  **không nháy nội dung** rồi mới đá về `/login`.
- **Chỉ `lib/auth/session.ts` được import `authApi.refresh`.** Chỗ khác gọi thẳng là phá single-flight
  và có thể kích hoạt reuse detection của chính mình.
- Refresh là single-flight **hai lớp**: promise chia sẻ trong tab, `navigator.locks` +
  `BroadcastChannel` giữa các tab. Thiếu Web Locks thì rơi về lớp trong tab.
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

- `pnpm gen:api` sinh `lib/api/schema.d.ts`, **commit vào repo**.
- Hợp đồng `.yaml` đổi → chạy lại codegen và sửa chỗ đỏ **trong cùng commit**. Cổng CI so lại bằng
  `git diff --exit-code`.
- Module mới (GĐ2+): thêm script `gen:api:<module>` ra `lib/api/<module>/schema.d.ts`. Không đổi
  version `openapi-typescript` kèm theo việc khác — đổi version là đổi file sinh ra.

## 8. Mock MSW (Đ-E7)

- Mặc định dev dùng **API thật**. Mock chỉ bật khi `NEXT_PUBLIC_API_MOCKING=enabled`, nạp bằng
  `import()` động để bundle production không chứa MSW.
- Fixture chép **giá trị** từ `example` của hợp đồng và gắn kiểu bằng `satisfies` — hợp đồng đổi hình
  dạng thì mock đỏ compile. Không parse yaml lúc chạy.
- Mock tồn tại chủ yếu để **tái hiện nhánh lỗi khó tạo thật** (423, 410, 429, 500) và để dựng màn khi
  không muốn chạy backend. Cùng bộ handler dùng cho Vitest qua `msw/node`.
- **Cấm nghiệm thu trên mock.** Cổng đóng phải trỏ API thật.

## 9. Test (Đ-E8)

| Công cụ | Kiểm | Chạy ở |
|---|---|---|
| Vitest + Testing Library + `msw/node` | validation, `ApiError`, client (`credentials`, bearer), single-flight trong tab, form trên mock, type | local + **CI** |
| Playwright (Chromium) | guard, không token trong Web Storage, 3 tab một refresh, lượt E2E trên dev | local, **`workers: 1`** (rate limit theo IP) |

- Playwright **không vào CI ở GĐ1** (cần API + Postgres + Redis + Mailpit chạy). Kết quả chạy local
  dán vào PR — cùng nếp "kiểm tay ghi bằng chứng" của khối D.
- File test nằm **cạnh mã nguồn** (`lib/validation/auth.test.ts`). `test/` chỉ chứa `setup.ts`;
  `e2e/` chứa spec Playwright.
- Thêm một luật ESLint hay một cổng CI thì phải **thử cho đỏ một lần** rồi khôi phục — `git status`
  sạch trước và sau.

## 10. Phiên bản, lệnh, và bốn chỗ dễ sai của stack

- pnpm, không npm. Chạy từ `src/frontend/`: `pnpm dev | lint | typecheck | test | build`.
- Ghim chính xác: không `^`/`~`; `save-exact=true` trong `.npmrc`; `"packageManager"` trong
  `package.json` (CI đọc trường này); Node 22 LTS trong `.nvmrc`.
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
| `localStorage.setItem('token', t)` | Đ-E2 | `tokenStore.set(t)` |
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
