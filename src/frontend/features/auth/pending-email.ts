import { useSyncExternalStore } from "react"

// Email vừa đăng ký, để màn "kiểm tra hộp thư" nêu đúng địa chỉ. Chỉ trong bộ nhớ của tab (E3 bước 3):
// KHÔNG vào query string — nó nằm lại trong lịch sử trình duyệt và log truy cập của apache (PII).
// Tải lại trang là mất → màn hiện câu chung không có email.
let pendingEmail: string | null = null
const listeners = new Set<() => void>()

export const pendingEmailStore = {
  get: () => pendingEmail,
  set(email: string | null) {
    pendingEmail = email
    listeners.forEach((l) => l())
  },
  subscribe(listener: () => void) {
    listeners.add(listener)
    return () => {
      listeners.delete(listener)
    }
  },
}

/**
 * Server không có biến này → snapshot phía server luôn `null`. Lúc hydrate React dùng snapshot server
 * rồi mới render lại với giá trị thật — không lệch HTML (hydration mismatch).
 */
export function usePendingEmail(): string | null {
  return useSyncExternalStore(
    pendingEmailStore.subscribe,
    pendingEmailStore.get,
    () => null
  )
}
