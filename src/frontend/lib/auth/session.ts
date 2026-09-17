import { authApi } from "@/lib/api/auth-api"
import { configureSessionExpired } from "@/lib/api/http"

import { tokenStore } from "./token-store"

// Đ-E14 — phiên phía trình duyệt. Refresh token, single-flight refresh (Đ-E4) đều đã chuyển về BFF ở server: trình duyệt
// không cầm token nên không có gì để refresh. Ở đây chỉ còn: hỏi BFF có phiên không, đăng xuất, và báo tab khác.

// Chỉ trong trình duyệt: Next prerender chạy module của client component trên server, nơi Node cũng có
// `BroadcastChannel` (mở kênh là giữ tiến trình build sống).
type AuthMessage = { type: "logout" }
const channel =
  typeof window !== "undefined" && typeof BroadcastChannel !== "undefined"
    ? new BroadcastChannel("socialapp:auth")
    : undefined

channel?.addEventListener("message", (event: MessageEvent<AuthMessage>) => {
  // Tab khác vừa đăng xuất: cookie phiên đã bị xóa cho cả origin — kết thúc luôn ở đây, không chờ request sau nhận 401.
  if (event.data?.type === "logout") tokenStore.endSession("expired")
})

// 401 từ proxy /bff/api/* = BFF đã refresh thử ở server và thất bại → phiên hết thật.
configureSessionExpired(() => tokenStore.endSession("expired"))

let inflight: Promise<void> | null = null

/**
 * Khôi phục phiên khi tab vừa mở / tải lại (Đ-E3): hỏi BFF `GET /bff/auth/session`. Gọi nhiều lần đồng thời (StrictMode,
 * nút "Thử lại") vẫn chỉ một request.
 *
 * có phiên → `authenticated` · không → `anonymous` · không hỏi được → `error` (không đẩy về `/login`).
 */
export function bootstrapSession(): Promise<void> {
  if (tokenStore.getSession().status === "error") tokenStore.markUnknown()
  inflight ??= (async () => {
    try {
      const { authenticated } = await authApi.session()
      if (authenticated) tokenStore.startSession()
      else tokenStore.endSession("expired")
    } catch {
      tokenStore.markError()
    }
  })().finally(() => {
    inflight = null
  })
  return inflight
}

/**
 * Đăng xuất: BFF gọi API thu hồi refresh family + access token (Đ-D6), xóa phiên trong Redis, xóa cookie. Lỗi không giữ
 * người dùng lại trong phiên. Guard thấy `anonymous` do `logout` sẽ đưa về `/login` (không kèm `next`).
 */
export async function logout(): Promise<void> {
  try {
    await authApi.logout()
  } catch {
    // Mất mạng / 5xx: vẫn kết thúc phía trình duyệt.
  } finally {
    tokenStore.endSession("logout")
    channel?.postMessage({ type: "logout" } satisfies AuthMessage)
  }
}
