// Phiên giả của mock cho Vitest: nhớ access token giả hiện tại để `/me` và `/auth/refresh` cư xử như
// thật. Chỉ sống trong bộ nhớ — `mockControls.reset()` ở test/setup.ts xóa sau mỗi test.
let current: string | null = null

let counter = 0
function mintToken() {
  counter += 1
  return `mock-access-token-${counter}-${Math.random().toString(36).slice(2, 8)}`
}

export const fakeSession = {
  /** Token giả hiện tại, hoặc null nếu chưa "đăng nhập". */
  current(): string | null {
    return current
  },
  /** Đăng nhập / refresh thành công: phát token giả mới. */
  start(): string {
    current = mintToken()
    return current
  },
  /** Đăng xuất. */
  clear() {
    current = null
  },
}

/** Cần cho Vitest của E7: làm access token hiện tại "hết hạn" mà không đụng tới phiên refresh. */
export const mockControls = {
  expireAccessToken() {
    if (fakeSession.current() !== null) fakeSession.start()
  },
  reset() {
    fakeSession.clear()
  },
}
