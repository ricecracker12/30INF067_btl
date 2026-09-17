import { deps } from "@/lib/bff/deps"
import { handleSession } from "@/lib/bff/handlers"

// force-dynamic: GET route handler không đọc gì "động" theo cách Next nhận ra được thì bị prerender thành file tĩnh lúc
// build — mọi người nhận CÙNG một câu trả lời "chưa đăng nhập".
export const dynamic = "force-dynamic"

export function GET(request: Request) {
  return handleSession(request, deps())
}
