import { deps } from "@/lib/bff/deps"
import { handleProxy } from "@/lib/bff/handlers"

// Proxy chung /bff/api/<path> → API /api/v1/<path>, gắn bearer của phiên ở server (Đ-E14). Module GĐ2+ dùng luôn, không
// thêm route. Nhóm auth của API bị chặn ở handleProxy — chúng có route riêng dưới /bff/auth.
export const dynamic = "force-dynamic"

type Context = { params: Promise<{ path: string[] }> }

async function handle(request: Request, { params }: Context) {
  const { path } = await params
  return handleProxy(request, deps(), path)
}

export {
  handle as DELETE,
  handle as GET,
  handle as PATCH,
  handle as POST,
  handle as PUT,
}
