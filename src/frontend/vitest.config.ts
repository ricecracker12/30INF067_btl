import { fileURLToPath } from "node:url"

import react from "@vitejs/plugin-react"
import { defineConfig } from "vitest/config"

// Thư mục gốc của FE, kết thúc bằng dấu phân cách — dùng để giải alias `@/…` giống
// `paths` trong tsconfig.json.
const rootDir = fileURLToPath(new URL(".", import.meta.url))

export default defineConfig({
  plugins: [react()],
  resolve: {
    alias: [{ find: /^@\//, replacement: rootDir }],
  },
  test: {
    environment: "jsdom",
    // Ghim env của test cho khỏi phụ thuộc shell của từng máy. Xuất
    // NEXT_PUBLIC_API_BASE_URL=/api/v1 rồi chạy `pnpm test` thì 8 test của http.test.ts đỏ:
    // trong Node, `fetch` một đường dẫn TƯƠNG ĐỐI là ném ngay, không có origin để nối vào.
    // Ca riêng cần env khác thì dùng `vi.stubEnv` trong chính test đó (xem lib/api/config.test.ts).
    env: {
      NEXT_PUBLIC_API_BASE_URL: "http://localhost:5259/api/v1",
      NEXT_PUBLIC_API_MOCKING: "",
    },
    setupFiles: ["test/setup.ts"],
    // e2e/ là Playwright (`pnpm test:e2e`), không phải Vitest.
    exclude: ["e2e/**", "node_modules/**"],
    typecheck: { enabled: true, include: ["**/*.test-d.ts"] },
  },
})
