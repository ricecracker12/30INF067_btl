import type { MessageResponse } from "@/lib/api/types"

// Luật dữ liệu của màn chat — hàm THUẦN, test không cần render (Đ-5.17, chat-hub-v1.md Mục 4):
//  1. Tập tin nhắn khử trùng theo `messageId`, sắp theo `seq` — KHÔNG nối mảng. Một tin tới qua ACK, `MessageReceived` và REST.
//  2. `seq` là thứ tự duy nhất đáng tin; thấy `seq` nhảy cóc → lấp chỗ hở bằng `afterSeq`.
//  3. Trạng thái tin của MÌNH suy từ mốc của người kia (Đ-5.6) — không có trường `status` trên tin.

/** Gộp tin mới vào tập, khử trùng theo `messageId`, sắp tăng theo `seq`. Trả mảng MỚI (React so tham chiếu). */
export function mergeMessages(
  current: readonly MessageResponse[],
  incoming: readonly MessageResponse[]
): MessageResponse[] {
  if (incoming.length === 0) return current as MessageResponse[]
  const byId = new Map(current.map((m) => [m.messageId, m]))
  let changed = false
  for (const m of incoming) {
    if (!byId.has(m.messageId)) {
      byId.set(m.messageId, m)
      changed = true
    }
  }
  if (!changed) return current as MessageResponse[]
  return [...byId.values()].sort((a, b) => a.seq - b.seq)
}

/** `seq` lớn nhất đang có (0 khi rỗng) — mốc cho `afterSeq` khi nối lại / fallback. */
export function maxSeq(messages: readonly MessageResponse[]): number {
  return messages.length === 0 ? 0 : messages[messages.length - 1].seq
}

/**
 * Tin mới tới có tạo chỗ hở không: có tin trước nó mà mình chưa thấy. Trả `afterSeq` cần lấp, hoặc `null`. Chỉ xét phía TRÊN
 * mốc đang có — tin cũ hơn trang đầu là việc của "Xem tin cũ hơn", không phải chỗ hở.
 */
export function gapBefore(
  messages: readonly MessageResponse[],
  incoming: MessageResponse
): number | null {
  const top = maxSeq(messages)
  if (messages.length === 0) return null
  return incoming.seq > top + 1 ? top : null
}

export type DeliveryState = "sent" | "delivered" | "seen"

/** Trạng thái tin `seq` của MÌNH, nhìn từ mốc của người kia (Đ-5.6). */
export function deliveryState(
  seq: number,
  peerDeliveredSeq: number,
  peerSeenSeq: number
): DeliveryState {
  if (seq <= peerSeenSeq) return "seen"
  if (seq <= peerDeliveredSeq) return "delivered"
  return "sent"
}

export const DELIVERY_LABEL: Record<DeliveryState, string> = {
  sent: "Đã gửi",
  delivered: "Đã nhận",
  seen: "Đã xem",
}

/** Giới hạn độ dài tin — CÙNG ngưỡng server (Đ-E5: client không chặt hơn server). Đếm code point như `char_length` của Postgres. */
export const MESSAGE_MAX_LENGTH = 2000

export function countCharacters(text: string): number {
  return [...text].length
}

/** Lỗi client của nội dung, câu chép NGUYÊN VĂN server (`MessageContentPolicy`), hoặc `null`. */
export function contentError(text: string): string | null {
  if (text.trim().length === 0) return "Tin nhắn không được để trống."
  if (countCharacters(text) > MESSAGE_MAX_LENGTH)
    return `Tin nhắn không được vượt quá ${MESSAGE_MAX_LENGTH} ký tự.`
  return null
}
