import type { MessageResponse, ReceiptKind } from "@/lib/api/types"

// Hợp đồng hub `chat-hub-v1.md` viết tay (Đ-5.17) — hub không có Swagger nên không sinh được từ OpenAPI. Payload tin dùng LẠI
// `MessageResponse` sinh từ `messaging-v1.yaml` (sự kiện `MessageReceived` cùng hình dạng). Nguồn chung với backend là
// `chat-hub-v1.examples.json`: `chat-hub-contract.test.ts` so fixture gắn kiểu ở đây với file ví dụ — đổi ví dụ mà không đổi
// kiểu (hoặc ngược lại) thì đỏ.

export const HUB_METHODS = { sendMessage: "SendMessage", sendReceipt: "SendReceipt" } as const
export const HUB_EVENTS = { messageReceived: "MessageReceived", receiptUpdated: "ReceiptUpdated" } as const

export type SendMessageArgs = {
  conversationId: string
  content: string
  clientMsgId: string
}

/** ACK "Đã gửi". `replayed`: clientMsgId này đã lưu trước đó với cùng nội dung — `message` là ĐÚNG tin cũ (Đ-5.5). */
export type SendMessageResult = {
  message: MessageResponse
  replayed: boolean
}

export type SendReceiptArgs = {
  conversationId: string
  kind: ReceiptKind
  upToSeq: number
}

/** Mốc TUYỆT ĐỐI của `userId` — nhận hai lần hay sai thứ tự đều vô hại (lấy max). */
export type ReceiptUpdated = {
  conversationId: string
  userId: string
  deliveredSeq: number
  seenSeq: number
}

export const HUB_ERROR_CODES = [
  "forbidden",
  "not-friends",
  "validation",
  "conflict",
  "rate-limited",
  "unavailable",
] as const

export type HubErrorCode = (typeof HUB_ERROR_CODES)[number]

/**
 * Mã lỗi từ một lời gọi hub hỏng (chat-hub-v1.md Mục 2). Server tắt `EnableDetailedErrors` nên client nhận
 * `"… HubException: forbidden"` — lấy phần sau `HubException: ` CUỐI CÙNG. Không có chuỗi đó (mất mạng, kết nối đóng giữa lời
 * gọi, lỗi lạ) → `unavailable`: KHÔNG biết tin đã lưu chưa → gửi lại cùng `clientMsgId` (quy tắc 5).
 */
export function hubErrorCode(error: unknown): HubErrorCode {
  const message = error instanceof Error ? error.message : String(error)
  const marker = "HubException: "
  const at = message.lastIndexOf(marker)
  if (at < 0) return "unavailable"
  const code = message.slice(at + marker.length).trim()
  return (HUB_ERROR_CODES as readonly string[]).includes(code)
    ? (code as HubErrorCode)
    : "unavailable"
}
