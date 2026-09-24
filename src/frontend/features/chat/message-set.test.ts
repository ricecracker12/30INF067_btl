import { describe, expect, it } from "vitest"

import type { MessageResponse } from "@/lib/api/types"

import {
  contentError,
  countCharacters,
  deliveryState,
  gapBefore,
  maxSeq,
  mergeMessages,
} from "./message-set"

const msg = (seq: number, id = `m-${seq}`): MessageResponse => ({
  messageId: id,
  conversationId: "c",
  senderId: "a",
  seq,
  content: `tin ${seq}`,
  clientMsgId: `cm-${seq}`,
  createdAt: "2026-09-24T10:00:00Z",
})

describe("mergeMessages — khử trùng theo messageId, sắp theo seq (chat-hub-v1 Mục 4)", () => {
  it("cùng một tin tới qua ACK, MessageReceived và REST → hiện MỘT lần", () => {
    const once = mergeMessages([], [msg(1)])
    const twice = mergeMessages(once, [msg(1)])
    const thrice = mergeMessages(twice, [msg(1), msg(2)])
    expect(thrice.map((m) => m.seq)).toEqual([1, 2])
    expect(twice).toBe(once) // không đổi gì → giữ tham chiếu, React không vẽ lại
  })

  it("tới sai thứ tự vẫn sắp theo seq", () => {
    expect(mergeMessages([msg(3)], [msg(1), msg(2)]).map((m) => m.seq)).toEqual([1, 2, 3])
  })
})

describe("gapBefore — seq nhảy cóc thì lấp bằng afterSeq", () => {
  it("có tới 10, nhận 12 → lấp afterSeq=10", () => {
    const have = [msg(9), msg(10)]
    expect(gapBefore(have, msg(12))).toBe(10)
    expect(gapBefore(have, msg(11))).toBeNull()
    expect(maxSeq(have)).toBe(10)
  })

  it("chưa có tin nào → không phải chỗ hở (trang đầu đang nạp)", () => {
    expect(gapBefore([], msg(5))).toBeNull()
  })
})

describe("deliveryState — suy từ mốc người kia (Đ-5.6)", () => {
  it.each([
    [3, "seen"],
    [5, "delivered"],
    [6, "sent"],
  ] as const)("tin %i với mốc (delivered 5, seen 3) → %s", (seq, expected) => {
    expect(deliveryState(seq, 5, 3)).toBe(expected)
  })
})

describe("contentError — cùng ngưỡng và cùng câu server (Đ-E5)", () => {
  it("rỗng / toàn khoảng trắng → câu trống", () => {
    expect(contentError("  \n")).toBe("Tin nhắn không được để trống.")
  })

  it("emoji đếm 1 ký tự — 2000 emoji hợp lệ, 2001 bị chặn", () => {
    expect(countCharacters("😀😀")).toBe(2)
    expect(contentError("😀".repeat(2000))).toBeNull()
    expect(contentError("😀".repeat(2001))).toBe("Tin nhắn không được vượt quá 2000 ký tự.")
  })
})
