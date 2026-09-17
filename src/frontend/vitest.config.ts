import { fileURLToPath } from "node:url"

import react from "@vitejs/plugin-react"
import { defineConfig } from "vitest/config"

// Thư mục gốc của FE, kết thúc bằng dấu phân cách — dùng để giải alias `@/…` giống
// `paths` trong tsconfig.json.
const rootDir = fileURLToPath(new URL(".", import.meta.url))

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: [
      { find: /^@\//, replacement: rootDir },
      // `server-only` ném lỗi khi nạp ngoài điều kiện "react-server" của Next — Vitest không có điều kiện đó. Test của
      // lib/bff cần nạp module server; thay bằng module rỗng CHỈ trong Vitest (Đ-E14).
      { find: /^server-only$/, replacement: `${rootDir}test/server-only.ts` },
    ],
  },
  test: {
    environment: "jsdom",
    // jsdom mặc định ở http://localhost:3000 — BFF_URL (lib/api/config.ts) lấy origin này để `fetch` của Node có URL
    // tuyệt đối. Test của lib/bff tự chọn môi trường node bằng chú thích `@vitest-environment node`.
    setupFiles: ["test/setup.ts"],
    // e2e/ là Playwright (`pnpm test:e2e`), không phải Vitest. `.next/`: từ E8 (`output: "standalone"`) bản build chép cả
    // `*.test.js` nội bộ của Next vào `.next/standalone/node_modules` — không loại thì `pnpm test` sau `pnpm build` đỏ 6 file.
    exclude: ["e2e/**", "node_modules/**", ".next/**"],
    typecheck: { enabled: true, include: ["**/*.test-d.ts"] },
  },
})
