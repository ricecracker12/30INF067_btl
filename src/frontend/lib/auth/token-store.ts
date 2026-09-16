type Listener = () => void

// Đ-E2 — access token CHỈ ở đây: không Web Storage, không cookie, không log.
// Biến module chứ không phải React state: api client không phải component, và interceptor của E7
// phải đọc được token hiện tại tại đúng thời điểm gọi lại.
let accessToken: string | null = null
const listeners = new Set<Listener>()

export const tokenStore = {
  get: () => accessToken,
  set(next: string | null) {
    if (next === accessToken) return
    accessToken = next
    listeners.forEach((l) => l())
  },
  subscribe(l: Listener) {
    listeners.add(l)
    return () => {
      listeners.delete(l)
    }
  },
}
