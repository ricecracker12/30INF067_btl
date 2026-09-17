import { authApi } from "@/lib/api/auth-api"
import { ApiError } from "@/lib/api/problem"
import type { VerifyEmailResponse } from "@/lib/api/types"

// Đ-E10: React StrictMode chạy effect hai lần ở dev. Lần hai gửi token ĐÃ TIÊU THỤ → 410 → màn báo "hết
// hiệu lực" dù vừa xác minh thành công. Gộp theo token ở cấp MODULE — `useRef` không đủ, StrictMode dựng
// lại component nên ref cũng mất.
const inflight = new Map<string, Promise<VerifyEmailResponse>>()

/** 200, 400, 410 là kết quả CUỐI của token — gọi lại cũng không đổi. Còn lại (429, 5xx, mất mạng) thì thử lại được. */
function isFinal(error: unknown) {
  return (
    error instanceof ApiError && (error.status === 400 || error.status === 410)
  )
}

/**
 * Mỗi token chỉ `POST /auth/verify-email` một lần trong đời tab; lần gọi sau nhận lại cùng promise. Lỗi tạm
 * thời thì bỏ khỏi bộ nhớ để nút "Thử lại" gửi được request mới.
 */
export function verifyOnce(token: string): Promise<VerifyEmailResponse> {
  let promise = inflight.get(token)
  if (!promise) {
    promise = authApi.verifyEmail({ token })
    inflight.set(token, promise)
    promise.catch((error: unknown) => {
      if (!isFinal(error)) inflight.delete(token)
    })
  }
  return promise
}

/** Chỉ cho test: mỗi test là một vòng đời tab mới. */
export function resetVerifyOnce() {
  inflight.clear()
}
