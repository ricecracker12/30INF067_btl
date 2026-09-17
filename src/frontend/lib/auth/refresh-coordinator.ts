import { ApiError } from "@/lib/api/problem"

/** Refresh trả 401: phiên đã hết thật (cookie mất hạn, bị thu hồi, reuse detection). */
export class SessionExpiredError extends Error {
  constructor() {
    super("Phiên đăng nhập đã hết hạn.")
    this.name = "SessionExpiredError"
  }
}

type Deps = {
  refresh: () => Promise<{ accessToken: string }>
  getToken: () => string | null
  setToken: (token: string) => void
  /** token = null, status = anonymous. */
  endSession: () => void
}

/**
 * Đ-E4 — một đường refresh duy nhất cho cả khởi động phiên (E6) lẫn interceptor 401 (E7): hai đường là hai
 * lời gọi song song lúc trang vừa mở và có 401 đầu tiên, cùng một cookie → tự kích hoạt reuse detection.
 *
 * E6 dựng LỚP 1 (trong tab: mọi lời gọi đồng thời chờ cùng một promise). LỚP 2 giữa các tab (Web Locks +
 * BroadcastChannel) và nhánh 401 trong `http.ts` là việc của E7 — phụ thuộc tiêm vào để thêm mà không đổi chữ ký.
 */
export function createRefreshCoordinator(d: Deps) {
  let inflight: Promise<string> | null = null

  async function refresh(stale: string | null): Promise<string> {
    // Đã có token khác token vừa hỏng (hoặc khởi động mà đã đăng nhập sẵn) → dùng luôn, không gọi refresh.
    const current = d.getToken()
    if (current && current !== stale) return current
    try {
      const { accessToken } = await d.refresh()
      d.setToken(accessToken)
      return accessToken
    } catch (e) {
      if (e instanceof ApiError && e.status === 401) {
        d.endSession()
        throw new SessionExpiredError()
      }
      // 429 / 500 / mất mạng: không kết luận là hết phiên.
      throw e
    }
  }

  return {
    /** @param stale token đã dùng cho request vừa nhận 401 — `null` khi khởi động phiên (E6). */
    getFreshToken(stale: string | null): Promise<string> {
      inflight ??= refresh(stale).finally(() => {
        inflight = null
      })
      return inflight
    },
  }
}
