import { authApi } from "@/lib/api/auth-api"

import {
  createRefreshCoordinator,
  SessionExpiredError,
} from "./refresh-coordinator"
import { tokenStore } from "./token-store"

// File DUY NHẤT được import `authApi.refresh` (frontend-rules Mục 5): gọi thẳng từ chỗ khác là phá
// single-flight và có thể tự kích hoạt reuse detection của server.
export const coordinator = createRefreshCoordinator({
  refresh: authApi.refresh,
  getToken: tokenStore.get,
  setToken: tokenStore.startSession,
  endSession: () => tokenStore.endSession("expired"),
})

/**
 * Khôi phục phiên khi tab vừa mở / tải lại (Đ-E3): một `POST /auth/refresh` bằng cookie. Gọi nhiều lần đồng thời
 * (StrictMode, nút "Thử lại") vẫn chỉ một request — gộp bởi coordinator.
 *
 * 200 → `authenticated` · 401 → `anonymous` · còn lại → `error` (không đẩy về `/login`).
 */
export async function bootstrapSession(): Promise<void> {
  if (tokenStore.getSession().status === "error") tokenStore.markUnknown()
  try {
    await coordinator.getFreshToken(null)
  } catch (e) {
    if (!(e instanceof SessionExpiredError)) tokenStore.markError()
  }
}

/**
 * Đăng xuất: server thu hồi refresh token + xóa cookie, rồi xóa token khỏi memory. Access token đã phát vẫn sống
 * tới hết hạn phía server (hợp đồng ghi rõ) — xóa khỏi memory là phần của FE. Guard thấy `anonymous` do `logout`
 * sẽ đưa về `/login` (không kèm `next`).
 */
export async function logout(): Promise<void> {
  try {
    await authApi.logout()
  } catch {
    // Server luôn 204 khi bearer hợp lệ (Đ-D6). Lỗi còn lại (token hết hạn khi chưa có interceptor E7, mất
    // mạng) không được giữ người dùng lại trong phiên.
  } finally {
    tokenStore.endSession("logout")
  }
}
