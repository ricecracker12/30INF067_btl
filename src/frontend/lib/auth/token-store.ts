type Listener = () => void

// Đ-E2 — access token CHỈ ở đây: không Web Storage, không cookie, không log.
// Biến module chứ không phải React state: api client không phải component, và interceptor của E7
// phải đọc được token hiện tại tại đúng thời điểm gọi lại.
//
// E6 mở rộng thành PHIÊN (token + trạng thái) ngay trong file này, không thêm store thứ hai — hai store là
// hai nguồn sự thật cho cùng một câu hỏi "đang đăng nhập chưa".

export type SessionStatus = "unknown" | "authenticated" | "anonymous" | "error"
/** Vì sao phiên kết thúc — guard dùng để quyết định có kèm `?next=` khi đưa về `/login` hay không. */
export type SessionEnd = "logout" | "expired"

export type Session = Readonly<{
  token: string | null
  status: SessionStatus
  endedBy: SessionEnd | null
}>

/** Tab vừa mở: chưa biết còn phiên hay không — cookie refresh có thể vẫn còn (Đ-E3). */
export const INITIAL_SESSION: Session = {
  token: null,
  status: "unknown",
  endedBy: null,
}

let session: Session = INITIAL_SESSION
const listeners = new Set<Listener>()

function update(next: Session) {
  if (
    next.token === session.token &&
    next.status === session.status &&
    next.endedBy === session.endedBy
  )
    return
  // Object mới mỗi lần đổi: `useSyncExternalStore` so snapshot bằng `Object.is`.
  session = next
  listeners.forEach((l) => l())
}

// Hàm rời, không dùng `this` — session.ts truyền thẳng `tokenStore.startSession` vào coordinator.
export const tokenStore = {
  /** Access token hiện tại (api client gắn bearer bằng giá trị này). */
  get: () => session.token,
  getSession: () => session,
  subscribe(l: Listener) {
    listeners.add(l)
    return () => {
      listeners.delete(l)
    }
  },

  /** `unknown`/`anonymous` → `authenticated`: đăng nhập 200 hoặc refresh thành công. */
  startSession(token: string) {
    update({ token, status: "authenticated", endedBy: null })
  },
  /** → `anonymous`: đăng xuất (`logout`), hoặc refresh 401 (`expired`). */
  endSession(endedBy: SessionEnd) {
    update({ token: null, status: "anonymous", endedBy })
  },
  /**
   * `unknown` → `error`: refresh khởi động 429 / 500 / mất mạng. KHÔNG phải `anonymous` — phiên có thể vẫn
   * còn, đẩy về `/login` là bắt đăng nhập lại oan.
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
