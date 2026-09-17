import { setupServer } from "msw/node"

import { handlers } from "./handlers"

// Chỉ dùng cho Vitest — dev không có mock trình duyệt (đổi Đ-E7 ngày 2026-09-17).
export const server = setupServer(...handlers)
