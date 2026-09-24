import { expectTypeOf, test } from "vitest"

import type {
  ConversationPage,
  ConversationResponse,
  MessagePage,
  MessageResponse,
  ReceiptKind,
} from "../types"

// Pin vài hình dạng DỄ TRÔI của messaging-v1.yaml (GĐ5 E1). Hợp đồng đổi một trong số này thì file này đỏ compile.

test("MessageResponse KHÔNG có `status` — trạng thái suy từ mốc của người kia (Đ-5.6)", () => {
  expectTypeOf<MessageResponse>().not.toHaveProperty("status")
  expectTypeOf<MessageResponse>().toHaveProperty("seq").toEqualTypeOf<number>()
  expectTypeOf<MessageResponse>().toHaveProperty("clientMsgId").toEqualTypeOf<string>()
})

test("canSend là `boolean | null` và KHÔNG bắt buộc — danh sách để null (Đ-5.3)", () => {
  expectTypeOf<ConversationResponse["canSend"]>().toEqualTypeOf<boolean | null | undefined>()
})

test("lastMessage có thể null — hội thoại vừa mở chưa có tin", () => {
  expectTypeOf<ConversationResponse>().toHaveProperty("lastMessage")
  expectTypeOf<null>().toMatchTypeOf<ConversationResponse["lastMessage"]>()
})

test("hai trang dùng `nextCursor: string | null` — hết dữ liệu là null (Đ-2.11)", () => {
  expectTypeOf<ConversationPage>().toHaveProperty("nextCursor").toEqualTypeOf<string | null>()
  expectTypeOf<MessagePage>().toHaveProperty("nextCursor").toEqualTypeOf<string | null>()
})

test("ReceiptKind là union chữ thường — không PascalCase (Q-D2)", () => {
  expectTypeOf<ReceiptKind>().toEqualTypeOf<"delivered" | "seen">()
})
