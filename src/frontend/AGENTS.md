<!-- BEGIN:nextjs-agent-rules -->

# This is NOT the Next.js you know

This version has breaking changes — APIs, conventions, and file structure may all differ from your training data. Read the relevant guide in `node_modules/next/dist/docs/` (resolved from this file's directory; in monorepos the `next` package may not be visible from the repo root) before writing any code. Heed deprecation notices.

This block is written and re-added by `next dev` — verify at `node_modules/next/dist/server/lib/generate-agent-files.js`. Removing it from a diff only re-creates the uncommitted change; committing it with your work keeps the tree clean.

<!-- END:nextjs-agent-rules -->

## UI kit — luật bắt buộc (Đ-E12)

Kit là **shadcn/ui preset `b50KEhMiu`** dựng trên **Base UI**, không phải Radix. Lệch kit là lỗi,
không phải phong cách. Chi tiết và lý do ở
`docs/giai-doan-1/huong-dan-khoi-e-frontend.md` (Đ-E12, Đ-E13).

1. **`components.json` là nguồn sự thật của kit**, commit vào repo. Không sửa tay `style`,
   `baseColor` — shadcn ghi rõ hai trường này không đổi được sau init.
2. **Thêm component chỉ bằng CLI ghim, chạy trong `src/frontend/`:** `pnpm exec shadcn add <tên>`.
   Không `pnpm dlx shadcn@latest add` — CLI/registry mới hơn sinh component lệch style các cái đã
   có. So bản trong repo với bản gốc: `pnpm exec shadcn add <tên> --diff`. Nâng bản CLI là một
   commit riêng, có lý do.
3. **Bốn tầng (Đ-E13), phụ thuộc một chiều `app/` → `features/` → `components/` + `lib/`:**
   `components/ui/` là kit — chỉ sửa khi thay đổi áp cho **toàn app**, thêm biến thể bằng `cva`
   ngay trong file đó · `components/form/`, `components/shell/` ghép từ `ui`, **không biết nghiệp
   vụ** · `features/<màn>/` ghép từ hai tầng trên + `lib/` · `app/**` chỉ ráp, không chứa logic.
4. **Token chỉ ở `app/globals.css`** (`:root`, `.dark`, `@theme inline`). Màn dùng tên token
   (`bg-primary`, `text-muted-foreground`, `text-destructive`, `border-border`); không màu thô,
   không mã màu tùy ý, không đặt radius/bóng riêng theo màn — ESLint chặn.
5. **Base UI, không Radix:** ghép bằng prop `render`, **không có `asChild`**; không import
   `@base-ui/react` ngoài `components/ui/`. Mẫu trên mạng phần lớn là Radix — tra docs bản Base UI
   bằng `pnpm exec shadcn docs <component>`.
6. **Icon chỉ `lucide-react`; font chỉ Inter** (subset `latin` + `vietnamese`) nạp bằng `next/font`
   ở `app/layout.tsx`.
7. **Kit không chạy Prettier** — `components/ui/` nằm trong `.prettierignore`, giữ nguyên kiểu shadcn sinh ra để
   `shadcn add <tên> --diff` chỉ hiện thay đổi thật. Đừng gỡ dòng đó để "cho đồng bộ style".
   **Không `shadcn eject`** (cắt đường cập nhật kit). **Không chạy `shadcn apply` tùy tiện** — nó
   đảo thứ tự dòng trong `globals.css`, sinh diff thừa. Chỉ khi đổi preset thật:
   `pnpm exec shadcn apply --preset <mã mới> --only theme,font`, rồi đọc diff `globals.css` và
   `layout.tsx` trước khi commit.
8. **Luật này nằm ở đây cho người và agent đọc trước khi sửa FE.** `AGENTS.md` gốc Mục 3 trỏ về
   file này.

> `pnpm exec shadcn info` đọc `components.json`, **không** đọc `app/layout.tsx` — nó báo
> `font inter` kể cả khi layout dùng font khác. Kiểm font bằng mắt trong `app/layout.tsx`.
> Mã preset nó in ra là `b50KEhMfo` thay vì `b50KEhMiu` — vô hại, hai mã chỉ khác `radius`
> (`medium`/`default`), cùng ra `--radius: 0.625rem`. Đừng "sửa" bằng `shadcn apply`.

## Ngoài kit — ba luật hay bị quên

- **Không `fetch` ngoài `lib/api/http.ts`; không `localStorage` / `sessionStorage` /
  `document.cookie`** (Đ-E2). ESLint chặn; ngoại lệ mở bằng `// eslint-disable-next-line` kèm lý do
  ngay tại dòng.
- **Không tạo `app/api/**`, và không route FE nào bắt đầu bằng `/api`, `/health`, `/swagger`**
  (Đ-E11) — apache staging đẩy hết những đường đó về backend, Next không bao giờ nhận được.
- **pnpm, ghim chính xác** (Đ-E9): không `npm`/`yarn`, không `^`/`~` trong `package.json`.
- **`@types/node` luôn cùng major với `.nvmrc`.** Đổi bản Node thì đổi cả hai trong một commit — để
  lệch là kiểu của một bản Node khác bản đang chạy. Hiện tại: Node **24** (đổi Đ-E9 ngày 2026-09-17).
- **Nâng `msw` thì chạy lại `pnpm exec msw init public --save`** và commit `public/mockServiceWorker.js`
  cùng lúc. File worker ghim bản riêng; lệch bản thì MSW chỉ cảnh báo trong console lúc chạy, không
  cổng nào bắt (cố ý: test Vitest dùng `msw/node`, không đụng tới file này).
- **`lib/api/schema.d.ts` là file sinh** (`pnpm gen:api` từ `identity-v1.yaml`) — sửa tay là cổng CI
  codegen đỏ. Kiểu cho payload API lấy từ `lib/api/types.ts`, không tự khai lại (Đ-E2, Mục 7 của
  `frontend-rules.md`).
- **Mock MSW chỉ chạy ở `next dev`** và chỉ khi `NEXT_PUBLIC_API_MOCKING=enabled` — hai điều kiện, vì
  một mình cờ mocking không đủ để Turbopack cắt MSW khỏi bundle production. Đổi chỗ gác `import()`
  trong `app/providers.tsx` thì đọc "Thực tế thi công" của E2 trước.
