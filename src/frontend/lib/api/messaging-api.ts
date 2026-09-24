import { BFF_ROUTES } from "./bff-contract"
import { request } from "./http"
import { pageQuery } from "./page-query"
import type * as T from "./types"

// Kiểu payload lấy từ hợp đồng `messaging-v1.yaml` (GĐ5 E1): yaml đổi field là các dòng dưới đỏ compile. Chín lời gọi đi qua
// proxy chung `/bff/api/*` (Đ-E14) — GĐ5 KHÔNG thêm route BFF nào, kể cả xin vé realtime (Đ-E16).
//
// Hub `/hubs/chat` KHÔNG ở đây: kết nối, gửi qua hub và sự kiện nằm ở `lib/realtime/` (Đ-5.17). File này là đường REST —
// đọc, mở hội thoại, và gửi FALLBACK khi không có WebSocket (Đ-5.12). Cùng service với hub nên cùng idempotency.

type PageOpts = { cursor?: string | null; limit?: number }

const id = (value: string) => encodeURIComponent(value)
const conversations = `${BFF_ROUTES.api}/conversations`

export const messagingApi = {
  /**
   * Vé bắt tay hub — 30 giây, dùng MỘT lần, mỗi lần kết nối xin một vé (Đ-E16, Đ-5.16). 503 `realtime-unavailable` khi Redis
   * chết → màn chuyển fallback REST, không báo lỗi hệ thống.
   */
  ticket: () =>
    request<T.RealtimeTicket>(`${BFF_ROUTES.api}/realtime/tickets`, {
      method: "POST",
    }),

  /**
   * Get-or-create hội thoại với một người bạn (Đ-5.2) — 201 vừa tạo hay 200 đã có đều trả cùng hình dạng, màn không phân biệt.
   * 403 `not-friends` khi không (còn) là bạn; 404 khi người kia chưa có hồ sơ.
   */
  open: (userId: string) =>
    request<T.ConversationResponse>(conversations, {
      method: "POST",
      body: { userId } satisfies T.CreateConversation,
    }),

  /** Hội thoại ĐÃ CÓ TIN, mới nhất trước. Hết dữ liệu KHI VÀ CHỈ KHI `nextCursor === null`. `canSend` là `null` ở đây. */
  list: (opts: PageOpts = {}, signal?: AbortSignal) =>
    request<T.ConversationPage>(`${conversations}${pageQuery(opts)}`, {
      signal,
    }),

  /** Tổng chưa đọc cho badge (Đ-5.14). */
  unreadCount: (signal?: AbortSignal) =>
    request<T.UnreadCount>(`${conversations}/unread-count`, { signal }),

  /** Chi tiết + `canSend` sống (Đ-5.3). Không phải thành viên / không tồn tại → 403 (một phản hồi cho cả hai). */
  get: (conversationId: string, signal?: AbortSignal) =>
    request<T.ConversationResponse>(`${conversations}/${id(conversationId)}`, {
      signal,
    }),

  /**
   * Lịch sử (Mục 7.6). `cursor`: trang CŨ hơn, `seq` giảm dần. `afterSeq`: lấp chỗ hở, `seq` TĂNG dần, `nextCursor` luôn
   * `null`. Không bao giờ gửi cả hai (server 400).
   */
  history: (
    conversationId: string,
    opts: { cursor?: string | null; afterSeq?: number; limit?: number } = {},
    signal?: AbortSignal
  ) => {
    const params = new URLSearchParams()
    if (opts.afterSeq !== undefined) params.set("afterSeq", String(opts.afterSeq))
    else if (opts.cursor) params.set("cursor", opts.cursor)
    if (opts.limit !== undefined) params.set("limit", String(opts.limit))
    const qs = params.size > 0 ? `?${params.toString()}` : ""
    return request<T.MessagePage>(
      `${conversations}/${id(conversationId)}/messages${qs}`,
      { signal }
    )
  },

  /**
   * Gửi bằng REST — đường FALLBACK (Đ-5.12). Gửi lại CÙNG `clientMsgId` trả ĐÚNG tin cũ (200, Đ-5.5) — `request()` không phân
   * biệt 200/201, màn không cần biết. 409 = cùng `clientMsgId` khác nội dung (bug client).
   */
  send: (conversationId: string, body: T.SendMessageRequest) =>
    request<T.MessageResponse>(`${conversations}/${id(conversationId)}/messages`, {
      method: "POST",
      body,
    }),

  /** Biên nhận đã nhận/đã xem (Đ-5.6) — dùng khi không có hub; có hub thì gửi qua hub. 204. */
  receipt: (conversationId: string, body: T.ReceiptRequest) =>
    request<void>(`${conversations}/${id(conversationId)}/receipts`, {
      method: "POST",
      body,
    }),
}
