import { authApi } from "@/lib/api/auth-api"
import { profileApi } from "@/lib/api/profile-api"
import { ApiError } from "@/lib/api/problem"

import { profileStore } from "./profile-store"

// Đối của `profile-store.ts` (trạng thái) — chỗ ĐI HỎI, giống cặp `token-store.ts` / `session.ts` của GĐ1.
// Tách ra vì store phải là state thuần: test store không cần msw, test luồng không cần render.

let inflight: Promise<void> | null = null

/**
 * Nạp hồ sơ của chính mình: `me()` lấy `userId` → `GET /users/{userId}/profile`. Gọi nhiều lần đồng thời
 * (StrictMode chạy effect hai lần, nút "Thử lại") vẫn CHỈ một lượt — nếu không, người dùng mới bắn hai cặp
 * request và đốt hạn mức 100 req/phút nhanh gấp đôi.
 *
 * 200 → `ready` · 404 → `missing` (tín hiệu onboarding, Đ-2.4) · 401 → `onSessionExpired` của `http.ts` đã lo
 * (Đ-E14), ở đây chỉ cần không biến nó thành `missing` · còn lại → `error`.
 */
export function loadProfile(): Promise<void> {
  if (profileStore.getState().status === "error") profileStore.markUnknown()
  inflight ??= (async () => {
    try {
      const { userId } = await authApi.me()
      profileStore.setReady(await profileApi.get(userId))
    } catch (e) {
      // 404 là TÍN HIỆU, không phải lỗi — không đi qua `errorMessage`, không hiện FormAlert.
      if (e instanceof ApiError && e.status === 404) profileStore.setMissing()
      // 401: phiên hết thật; `RequireAuth` đưa về /login. Coi là `missing` thì người dùng thấy màn
      // onboarding nháy lên trước khi bị đá về đăng nhập.
      else if (e instanceof ApiError && e.status === 401)
        profileStore.markUnknown()
      else profileStore.markError()
    }
  })().finally(() => {
    inflight = null
  })
  return inflight
}
