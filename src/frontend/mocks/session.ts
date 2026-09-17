// Phiên giả của mock BFF cho Vitest: "có cookie phiên hay không". Chỉ sống trong bộ nhớ — `mockControls.reset()` ở
// test/setup.ts xóa sau mỗi test. Từ Đ-E14 không có token nào phía trình duyệt, nên phiên giả chỉ còn là một cờ.
let current: string | null = null
let counter = 0

export const fakeSession = {
  /** ID phiên giả hiện tại, hoặc null nếu chưa "đăng nhập". */
  current(): string | null {
    return current
  },
  /** Đăng nhập thành công: BFF cấp phiên mới. */
  start(): string {
    counter += 1
    current = `mock-session-${counter}`
    return current
  },
  /** Đăng xuất / phiên hết hạn. */
  clear() {
    current = null
  },
}

export const mockControls = {
  reset() {
    fakeSession.clear()
  },
}
