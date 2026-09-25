import { notificationApi } from "@/lib/api/notification-api"

type Listener = () => void

// Số nhóm thông báo chưa đọc — MỘT chỗ cho chuông ở header và màn `/notifications` (khuôn `profile-store.ts`): đánh dấu đã đọc ở
// màn danh sách thì badge trên header đổi ngay, không đợi lượt hỏi lại 30 giây. `null` = chưa hỏi lần nào (không vẽ badge).

let total: number | null = null
const listeners = new Set<Listener>()

function set(next: number | null) {
  if (next === total) return
  total = next
  listeners.forEach((l) => l())
}

export const unreadStore = {
  get: () => total,
  subscribe(l: Listener) {
    listeners.add(l)
    return () => {
      listeners.delete(l)
    }
  },
  /** Đánh dấu đã đọc optimistic (−1) / rollback (+1). Chưa biết số thì bỏ qua — lượt hỏi kế tiếp mang số thật. */
  adjust(delta: number) {
    if (total !== null) set(Math.max(0, total + delta))
  },
  reset() {
    inflight = null
    set(null)
  },
}

let inflight: Promise<void> | null = null

/**
 * Hỏi `GET /notifications/unread-count`. Focus + `visibilitychange` bắn cùng lúc khi quay lại tab, StrictMode mount hai lần —
 * vẫn MỘT request. Lỗi thì giữ số cũ: badge hỏng không chặn gì, lượt sau làm mới (khuôn badge chat GĐ5).
 */
export function refreshUnread(): Promise<void> {
  inflight ??= notificationApi
    .unreadCount()
    .then((r) => set(r.total))
    .catch(() => undefined)
    .finally(() => {
      inflight = null
    })
  return inflight
}
