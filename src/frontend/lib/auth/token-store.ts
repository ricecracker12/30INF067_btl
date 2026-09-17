type Listener = () => void

// Trạng thái PHIÊN phía trình duyệt (E6). Từ Đ-E14 không còn access token nào ở đây — token nằm ở Next server + Redis;
// trình duyệt chỉ cần biết "đang đăng nhập hay không" để guard quyết định render gì. Biến module + subscribe: React đọc
// qua `useSyncExternalStore`, code không phải component (http.ts, session.ts) đổi được trạng thái.

export type SessionStatus = "unknown" | "authenticated" | "anonymous" | "error"
/** Vì sao phiên kết thúc — guard dùng để quyết định có kèm `?next=` khi đưa về `/login` hay không. */
export type SessionEnd = "logout" | "expired"

export type Session = Readonly<{
  status: SessionStatus
  endedBy: SessionEnd | null
}>

/** Tab vừa mở: chưa biết còn phiên hay không — cookie phiên có thể vẫn còn (Đ-E3). */
export const INITIAL_SESSION: Session = {
  status: "unknown",
  endedBy: null,
}

let session: Session = INITIAL_SESSION
const listeners = new Set<Listener>()

function update(next: Session) {
  if (next.status === session.status && next.endedBy === session.endedBy) return
  // Object mới mỗi lần đổi: `useSyncExternalStore` so snapshot bằng `Object.is`.
  session = next
  listeners.forEach((l) => l())
}

// Hàm rời, không dùng `this` — truyền thẳng làm callback được.
export const tokenStore = {
  getSession: () => session,
  subscribe(l: Listener) {
    listeners.add(l)
    return () => {
      listeners.delete(l)
    }
  },

  /** → `authenticated`: đăng nhập 204, hoặc BFF báo còn phiên lúc khởi động. */
  startSession() {
    update({ status: "authenticated", endedBy: null })
  },
  /** → `anonymous`: đăng xuất (`logout`), hoặc phiên hết hạn / không có (`expired`). */
  endSession(endedBy: SessionEnd) {
    update({ status: "anonymous", endedBy })
  },
  /**
   * `unknown` → `error`: không hỏi được BFF lúc khởi động (mất mạng, 5xx). KHÔNG phải `anonymous` — phiên có thể vẫn còn,
   * đẩy về `/login` là bắt đăng nhập lại oan.
   */
  markError() {
    update({ ...session, status: "error" })
  },
  /** `error` → `unknown`: bấm "Thử lại". */
  markUnknown() {
    update({ ...session, status: "unknown" })
  },
  /** Về trạng thái tab vừa mở. Test dùng giữa các ca; app không gọi. */
  reset() {
    update(INITIAL_SESSION)
  },
}
