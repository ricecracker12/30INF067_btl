import { authApi } from "@/lib/api/auth-api"
import { configureRefresh } from "@/lib/api/http"

import {
  createRefreshCoordinator,
  SessionExpiredError,
} from "./refresh-coordinator"
import { tokenStore } from "./token-store"

// Chỉ trong trình duyệt. Next prerender chạy cả module của client component trên server — ở đó Node vẫn có
// `BroadcastChannel` (mở kênh là giữ tiến trình build sống) và có thể có `navigator`; guard bằng `window`.
const inBrowser = typeof window !== "undefined"

const channel =
  inBrowser && typeof BroadcastChannel !== "undefined"
    ? new BroadcastChannel("socialapp:auth")
    : undefined

const locks =
  inBrowser && typeof navigator !== "undefined" && navigator.locks
    ? {
        request: <T>(name: string, cb: () => Promise<T>) =>
          navigator.locks.request(name, () => cb()) as Promise<T>,
      }
    : undefined

// File DUY NHẤT được import `authApi.refresh` (frontend-rules Mục 5): gọi thẳng từ chỗ khác là phá
// single-flight và có thể tự kích hoạt reuse detection của server.
export const coordinator = createRefreshCoordinator({
  refresh: authApi.refresh,
  getToken: tokenStore.get,
  setToken: tokenStore.startSession,
  endSession: () => tokenStore.endSession("expired"),
  locks,
  channel,
})

// Nhánh 401 của `request()` đi qua CÙNG coordinator với khởi động phiên — một đường refresh duy nhất (Đ-E4).
configureRefresh(coordinator.getFreshToken)

/**
 * Khôi phục phiên khi tab vừa mở / tải lại (Đ-E3): một `POST /auth/refresh` bằng cookie. Gọi nhiều lần đồng thời
 * (StrictMode, nút "Thử lại", tab khác) vẫn chỉ một request — gộp bởi coordinator.
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
 * Đăng xuất: server thu hồi refresh token + xóa cookie, rồi xóa token khỏi memory và báo mọi tab. Access token đã
 * phát vẫn sống tới hết hạn phía server (hợp đồng ghi rõ) — xóa khỏi memory là phần của FE. Guard thấy `anonymous`
 * do `logout` sẽ đưa về `/login` (không kèm `next`); các tab khác nhận `logout` → hết hạn → kèm `next`.
 */
export async function logout(): Promise<void> {
  try {
    // 401 (access token hết hạn) → interceptor refresh rồi gọi lại. Refresh cũng hỏng thì thôi.
    await authApi.logout()
  } catch {
    // Server luôn 204 khi bearer hợp lệ (Đ-D6). Lỗi còn lại (phiên đã hết, mất mạng) không được giữ người dùng lại.
  } finally {
    tokenStore.endSession("logout")
    coordinator.announceLogout()
  }
}
