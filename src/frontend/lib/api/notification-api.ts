import { BFF_ROUTES } from "./bff-contract"
import { request } from "./http"
import { pageQuery } from "./page-query"
import type * as T from "./types"

// Kiểu payload lấy từ hợp đồng `notification-v1.yaml` (GĐ6 E1). Bốn endpoint đi qua proxy chung `/bff/api/*` (Đ-E14).
// Hub `/hubs/notifications` KHÔNG ở đây — FE hỏi lại `unread-count` 30 giây (Đ-6.18); hợp đồng dữ liệu không đổi khi có hub.

type PageOpts = { cursor?: string | null; limit?: number }

const notifications = `${BFF_ROUTES.api}/notifications`

export const notificationApi = {
  /**
   * `updated_at DESC` — nhóm vừa có sự kiện mới nhảy lên đầu, nên có thể hiện lại ở trang trước: màn khử trùng theo
   * `notificationId` (`useCursorPages`). Hết dữ liệu KHI VÀ CHỈ KHI `nextCursor === null`.
   */
  list: (opts: PageOpts = {}, signal?: AbortSignal) =>
    request<T.NotificationPage>(`${notifications}${pageQuery(opts)}`, {
      signal,
    }),

  /** Số NHÓM chưa đọc (không phải số sự kiện) — badge hiện "9+" từ 10. */
  unreadCount: (signal?: AbortSignal) =>
    request<T.NotificationUnreadCount>(`${notifications}/unread-count`, {
      signal,
    }),

  /** 204. Không phải của mình HOẶC không tồn tại → 403 (một phản hồi cho cả hai). */
  markRead: (notificationId: string) =>
    request<void>(
      `${notifications}/${encodeURIComponent(notificationId)}/read`,
      { method: "POST" }
    ),

  /** 204. Chỉ nhóm có `updatedAt ≤ upTo` — thông báo tới sau lúc mở KHÔNG bị nuốt (NOTIF-07). */
  readAll: (upTo: string) =>
    request<void>(`${notifications}/read-all`, {
      method: "POST",
      body: { upTo } satisfies T.ReadAllRequest,
    }),
}
