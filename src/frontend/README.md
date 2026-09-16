# SocialApp — frontend

Next.js 16 (App Router, TypeScript, Tailwind v4) + shadcn/ui preset `b50KEhMiu` (Base UI, style
`base-maia`). Quản lý gói bằng **pnpm**, phiên bản **ghim chính xác** — không `npm`, không `yarn`,
không `^`/`~`.

**Đọc trước khi sửa:** [`AGENTS.md`](AGENTS.md) (luật Next 16 của template + mục "UI kit"),
[`.claude/rules/frontend-rules.md`](../../.claude/rules/frontend-rules.md), và
[`docs/giai-doan-1/huong-dan-khoi-e-frontend.md`](../../docs/giai-doan-1/huong-dan-khoi-e-frontend.md).

## Lệnh

Chạy từ chính thư mục này (`src/frontend/`):

```bash
pnpm dev        # http://localhost:3000 — cần API dev ở http://localhost:5259 (Đ-E1)
pnpm lint       # ESLint flat config: luật Đ-E2 / Đ-E12 / Đ-E13
pnpm typecheck  # tsc --noEmit
pnpm gen:api    # sinh lib/api/schema.d.ts từ hợp đồng identity-v1.yaml — file sinh, COMMIT vào repo
pnpm test       # Vitest (jsdom + Testing Library + msw/node), chạy một lượt rồi thoát
pnpm test:e2e   # Playwright trên Chrome đã cài (channel "chrome"), workers: 1 — rate limit /auth/* 10 req/phút/IP
pnpm build      # next build
pnpm format     # prettier --write
```

Bốn cổng phải xanh trước khi commit: `lint`, `typecheck`, `test`, `build`.

Node 24 LTS (`.nvmrc`), pnpm 10.x (trường `packageManager` — CI đọc đúng trường này).

## Thêm component UI

Bằng **bản CLI ghim trong dự án**, không phải `@latest` — bản mới hơn sinh component lệch style các
cái đã có (Đ-E12 mục 2):

```bash
pnpm exec shadcn add dialog        # KHÔNG dùng: pnpm dlx shadcn@latest add dialog
pnpm exec shadcn add dialog --diff # so bản trong repo với bản gốc
pnpm exec shadcn docs dialog       # docs bản Base UI — mẫu Radix trên mạng sẽ sai (không có asChild)
```

Kit rơi vào `components/ui/`, viết bởi CLI — không sửa tay trừ khi thay đổi áp cho **toàn app**.
Màu và radius chỉ khai ở `app/globals.css`.

## Cấu trúc (Đ-E13 — phụ thuộc một chiều `app/` → `features/` → `components/` + `lib/`)

```
app/           route, layout, page.tsx mỏng — chỉ ráp, không có logic nghiệp vụ
features/      nghiệp vụ theo MÀN (GĐ1: auth/); không import chéo nhau
components/    ui/ (kit shadcn) · form/ · shell/ — KHÔNG biết nghiệp vụ
lib/           api/ · auth/ · validation/ — hạ tầng, không import ngược lên features/ hay app/
test/          setup.ts của Vitest (file test nằm CẠNH mã nguồn)
e2e/           spec Playwright
```

## Ba luật hay bị quên

- Không `fetch` ngoài `lib/api/http.ts`; không `localStorage` / `sessionStorage` / `document.cookie`
  — access token chỉ ở memory (Đ-E2). ESLint chặn cả ba.
- Không tạo `app/api/**`, và không route FE nào bắt đầu bằng `/api`, `/health`, `/swagger` (Đ-E11) —
  apache staging đẩy hết những đường đó về backend.
- `.env.example` liệt kê **tên** biến, không bao giờ giá trị. Mọi `NEXT_PUBLIC_*` bị nhúng vào bundle
  gửi cho trình duyệt.
- `lib/api/schema.d.ts` là **file sinh** — không sửa tay. Hợp đồng `.yaml` đổi thì chạy lại
  `pnpm gen:api` và sửa chỗ đỏ **trong cùng commit**; cổng CI so lại bằng `git diff --exit-code`.
- Kiểu cho payload API lấy từ `lib/api/types.ts`, **không tự khai lại** — hợp đồng đổi thì phải là lỗi
  compile, không phải lỗi runtime.
