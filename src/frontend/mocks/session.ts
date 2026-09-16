// Phiên giả của mock: nhớ access token giả hiện tại để `/me` và `/auth/refresh` cư xử như thật.
// Nhớ trong `sessionStorage` để tải lại trang vẫn "còn phiên" — giống cookie refresh thật.
//
// Đ-E2 cấm Web Storage trong app; `mocks/**` là ngoại lệ có chủ đích và chỉ ở đây: mock KHÔNG vào
// bundle production (nạp bằng import() động, sau cờ NEXT_PUBLIC_API_MOCKING).
const KEY = "msw:fake-access-token"

let current: string | null = null

function readStored(): string | null {
  try {
    // eslint-disable-next-line no-restricted-globals -- Đ-E2: mock cần nhớ phiên qua reload, xem đầu file.
    return sessionStorage.getItem(KEY)
  } catch {
    return null
  }
}

function writeStored(token: string | null) {
  try {
    if (token === null) {
      // eslint-disable-next-line no-restricted-globals -- Đ-E2: mock cần nhớ phiên qua reload, xem đầu file.
      sessionStorage.removeItem(KEY)
    } else {
      // eslint-disable-next-line no-restricted-globals -- Đ-E2: mock cần nhớ phiên qua reload, xem đầu file.
      sessionStorage.setItem(KEY, token)
    }
  } catch {
    // Không có Web Storage (SSR, trình duyệt chặn) thì phiên giả chỉ sống trong tab hiện tại.
  }
}

let counter = 0
function mintToken() {
  counter += 1
  return `mock-access-token-${counter}-${Math.random().toString(36).slice(2, 8)}`
}

export const fakeSession = {
  /** Token giả hiện tại, hoặc null nếu chưa "đăng nhập". */
  current(): string | null {
    current ??= readStored()
    return current
  },
  /** Đăng nhập / refresh thành công: phát token giả mới. */
  start(): string {
    current = mintToken()
    writeStored(current)
    return current
  },
  /** Đăng xuất. */
  clear() {
    current = null
    writeStored(null)
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
