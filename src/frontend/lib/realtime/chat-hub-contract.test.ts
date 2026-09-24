import { describe, expect, it } from "vitest"

import examples from "../../../backend/Modules/Messaging/Presentation/chat-hub-v1.examples.json"
import {
  HUB_ERROR_CODES,
  HUB_EVENTS,
  HUB_METHODS,
  hubErrorCode,
  type ReceiptUpdated,
  type SendMessageArgs,
  type SendMessageResult,
  type SendReceiptArgs,
} from "./chat-hub-contract"

// Cổng hợp đồng hub phía FE (giai-doan-5.md Mục 8.3). Nguồn chung với backend: `chat-hub-v1.examples.json`.
// Fixture dưới đây GẮN KIỂU bằng `satisfies` (đổi kiểu TS mà không đổi fixture → typecheck đỏ) và được SO với file ví dụ lúc chạy
// (đổi ví dụ mà không đổi fixture → Vitest đỏ). JSON import vào TS bị nới kiểu (`"seen"` thành `string`) nên không `satisfies`
// thẳng file JSON được — hai bước này thay cho một.

const sendMessageArgs = {
  conversationId: "0192f3c1-9b2d-7e40-8a11-3c5d7e9f1a20",
  content: "Chào bạn, tối nay đi cà phê không?",
  clientMsgId: "6f1d2c3b-4a5e-4f60-9b7a-8c9d0e1f2a3b",
} satisfies SendMessageArgs

const message = {
  messageId: "0192f3c2-0a1b-7c2d-9e3f-4a5b6c7d8e9f",
  conversationId: "0192f3c1-9b2d-7e40-8a11-3c5d7e9f1a20",
  senderId: "0192f3c1-8a4e-7c31-9f2a-6b5d4e3c2a10",
  seq: 57,
  content: "Chào bạn, tối nay đi cà phê không?",
  clientMsgId: "6f1d2c3b-4a5e-4f60-9b7a-8c9d0e1f2a3b",
  createdAt: "2026-09-24T10:15:30.123+00:00",
}

const sendMessageResult = { message, replayed: false } satisfies SendMessageResult

const sendReceiptArgs = {
  conversationId: "0192f3c1-9b2d-7e40-8a11-3c5d7e9f1a20",
  kind: "seen",
  upToSeq: 57,
} satisfies SendReceiptArgs

const receiptUpdated = {
  conversationId: "0192f3c1-9b2d-7e40-8a11-3c5d7e9f1a20",
  userId: "0192f3c1-7f3e-7b20-8e19-5a4c3b2a1f09",
  deliveredSeq: 57,
  seenSeq: 57,
} satisfies ReceiptUpdated

describe("hợp đồng hub chat-hub-v1", () => {
  it("fixture gắn kiểu KHỚP từng ví dụ của file dùng chung với backend", () => {
    expect(examples.methods.SendMessage.args).toEqual(sendMessageArgs)
    expect(examples.methods.SendMessage.result).toEqual(sendMessageResult)
    expect(examples.methods.SendReceipt.args).toEqual(sendReceiptArgs)
    expect(examples.methods.SendReceipt.result).toBeNull()
    expect(examples.events.MessageReceived).toEqual(message)
    expect(examples.events.ReceiptUpdated).toEqual(receiptUpdated)
  })

  it("tên phương thức, sự kiện, mã lỗi bằng tập trong file ví dụ", () => {
    expect(Object.keys(examples.methods).sort()).toEqual(Object.values(HUB_METHODS).sort())
    expect(Object.keys(examples.events).sort()).toEqual(Object.values(HUB_EVENTS).sort())
    expect([...examples.errors].sort()).toEqual([...HUB_ERROR_CODES].sort())
  })

  it("hubErrorCode đọc mã sau 'HubException: ' — dạng thật server gửi (EnableDetailedErrors = false)", () => {
    expect(
      hubErrorCode(new Error("An unexpected error occurred invoking 'SendMessage' on the server. HubException: not-friends"))
    ).toBe("not-friends")
    expect(hubErrorCode(new Error("HubException: forbidden"))).toBe("forbidden")
  })

  it("không có mã (mất mạng, kết nối đóng giữa lời gọi, mã lạ) → unavailable: gửi lại cùng clientMsgId", () => {
    expect(hubErrorCode(new Error("Invocation canceled due to the underlying connection being closed."))).toBe("unavailable")
    expect(hubErrorCode(new Error("HubException: gi-do-la"))).toBe("unavailable")
    expect(hubErrorCode("chuỗi")).toBe("unavailable")
  })
})
