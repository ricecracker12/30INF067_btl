import { deps } from "@/lib/bff/deps"
import { handleLogin } from "@/lib/bff/handlers"

// Chỉ ráp (Đ-E13, Đ-E14): logic ở lib/bff/handlers.ts. Route BFF nằm dưới /bff, KHÔNG dưới /api — apache đẩy mọi /api
// về backend (Đ-E11).
export const dynamic = "force-dynamic"

export function POST(request: Request) {
  return handleLogin(request, deps())
}
