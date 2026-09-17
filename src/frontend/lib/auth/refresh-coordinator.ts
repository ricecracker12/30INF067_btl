import { ApiError } from "@/lib/api/problem"

/** Refresh trả 401 (cookie mất hạn, bị thu hồi, reuse detection), hoặc phiên đã kết thúc trong lúc request đang bay. */
export class SessionExpiredError extends Error {
  constructor() {
    super("Phiên đăng nhập đã hết hạn.")
    this.name = "SessionExpiredError"
  }
}

/** Tin nhắn giữa các tab cùng origin. Chỉ truyền trong bộ nhớ, không ghi xuống đĩa (quyết định 6). */
export type AuthMessage = { type: "token"; token: string } | { type: "logout" }

export const REFRESH_LOCK = "socialapp:auth-refresh"

type Deps = {
  refresh: () => Promise<{ accessToken: string }>
  getToken: () => string | null
  setToken: (token: string) => void
  /** token = null, status = anonymous. */
  endSession: () => void
  /** `navigator.locks` — thiếu (Safari < 15.4, jsdom) thì chỉ còn lớp trong tab; ân hạn 10 giây của server đỡ phần còn lại. */
  locks?: { request<T>(name: string, cb: () => Promise<T>): Promise<T> }
  /** `BroadcastChannel('socialapp:auth')`. */
  channel?: {
    postMessage(message: AuthMessage): void
    addEventListener(
      type: "message",
      listener: (event: MessageEvent<AuthMessage>) => void
    ): void
  }
}

/**
 * Đ-E4 — một đường refresh duy nhất cho cả khởi động phiên (E6) lẫn interceptor 401 (E7): hai đường là hai lời gọi
 * song song cùng một cookie → tự kích hoạt reuse detection của server.
 *
 * - LỚP 1, trong tab: mọi lời gọi đồng thời chờ cùng một promise.
 * - LỚP 2, giữa các tab: refresh chạy trong Web Lock; tab thắng khóa phát token mới qua BroadcastChannel; tab đang chờ
 *   khóa, tới lượt, thấy token hiện tại KHÁC token đã làm request của nó hỏng → dùng luôn, không gọi refresh.
 */
export function createRefreshCoordinator(d: Deps) {
  let inflight: Promise<string> | null = null

  d.channel?.addEventListener("message", ({ data }) => {
    if (data.type === "token") d.setToken(data.token)
    else d.endSession()
  })

  async function refreshUnderLock(stale: string | null): Promise<string> {
    const current = d.getToken()
    // Tab khác (hoặc lượt trước trong tab này) đã có token mới trong lúc chờ khóa → KHÔNG gọi refresh nữa.
    if (current && current !== stale) return current
    // Request bay đi với một token, giờ token đã bị xóa: phiên kết thúc trong lúc chờ (refresh 401 ở tab khác, đăng
    // xuất). Không refresh để "hồi sinh" phiên mà người dùng / server vừa kết thúc.
    if (stale !== null && current === null) throw new SessionExpiredError()
    try {
      const { accessToken } = await d.refresh()
      d.setToken(accessToken)
      d.channel?.postMessage({ type: "token", token: accessToken })
      return accessToken
    } catch (e) {
      if (e instanceof ApiError && e.status === 401) {
        d.endSession()
        d.channel?.postMessage({ type: "logout" })
        throw new SessionExpiredError()
      }
      // 429 / 500 / mất mạng: không kết luận là hết phiên.
      throw e
    }
  }

  return {
    /**
     * @param stale token đã dùng cho request vừa nhận 401 — chụp TRƯỚC `fetch`, không đọc lại sau `await` (bẫy 5).
     * `null` khi khởi động phiên (E6).
     */
    getFreshToken(stale: string | null): Promise<string> {
      inflight ??= (
        d.locks
          ? d.locks.request(REFRESH_LOCK, () => refreshUnderLock(stale))
          : refreshUnderLock(stale)
      ).finally(() => {
        inflight = null
      })
      return inflight
    },

    /** Đăng xuất ở tab này: báo mọi tab khác kết thúc phiên. */
    announceLogout() {
      d.channel?.postMessage({ type: "logout" })
    },
  }
}
