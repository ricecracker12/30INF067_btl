import { setupServer } from "msw/node"

import { handlers } from "./handlers"

// Dùng cho Vitest — cùng bộ handler với trình duyệt, để mock không trôi khỏi test.
export const server = setupServer(...handlers)
