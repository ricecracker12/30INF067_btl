import { describe, expect, it } from "vitest"

import { errorMessage, fieldMessage } from "./messages"
import { ApiError, NetworkError, PROBLEM_TYPES } from "./problem"
import type { ProblemDetails } from "./types"

// GĐ5 E1 — năm ngữ cảnh của Messaging và hai `type` mới. Tách file khỏi `messages.test.ts` để GĐ5 chỉ THÊM, không sửa ca cũ.

const problem = (status: number, extra: Partial<ProblemDetails> = {}) =>
  new ApiError(status, {
    type: `https://httpstatuses.io/${status}`,
    title: "t",
    status,
    traceId: "0af7651916cd43dd8448eb211c80319c",
    ...extra,
  })

describe("errorMessage — ngữ cảnh GĐ5", () => {
  it("403 not-friends: câu 'chỉ đọc' ở MỌI ngữ cảnh gửi/mở — tách bằng `type`, không bằng status", () => {
    for (const ctx of ["conversation-open", "message-send"] as const)
      expect(errorMessage(ctx, problem(403, { type: PROBLEM_TYPES.notFriends }))).toBe(
        "Hai bạn không còn là bạn bè. Hội thoại chỉ đọc."
      )
  })

  it("403 thường (không phải thành viên / thiếu quyền) KHÁC 403 not-friends", () => {
    expect(errorMessage("message-send", problem(403))).toBe(
      "Bạn không thể gửi tin vào cuộc trò chuyện này."
    )
    expect(errorMessage("conversation-open", problem(403))).toBe(
      "Tài khoản của bạn chưa được phép nhắn tin."
    )
  })

  it("403 đọc hội thoại: MỘT câu cho 'không tồn tại' và 'không phải thành viên' (Mục 6.1)", () => {
    expect(errorMessage("conversation-read", problem(403))).toBe(
      "Không tìm thấy cuộc trò chuyện, hoặc bạn không có quyền xem."
    )
  })

  it("503 realtime-unavailable: câu fallback, KHÔNG Mã tra cứu — chat vẫn chạy", () => {
    const msg = errorMessage(
      "realtime-ticket",
      problem(503, { type: PROBLEM_TYPES.realtimeUnavailable })
    )
    expect(msg).toBe("Kênh thời gian thực tạm thời không sẵn sàng. Tin nhắn vẫn gửi được.")
    expect(msg).not.toContain("Mã tra cứu")
  })

  it("409 gửi tin: trùng mã tin phía client", () => {
    expect(errorMessage("message-send", problem(409))).toBe(
      "Tin nhắn bị trùng mã. Hãy gửi lại như một tin mới."
    )
  })

  it("400 nội dung: câu CỦA SERVER dưới errors.content (Đ-E5)", () => {
    const e = problem(400, { errors: { content: ["Tin nhắn không được vượt quá 2000 ký tự."] } })
    expect(fieldMessage(e, "content", "message-send")).toBe(
      "Tin nhắn không được vượt quá 2000 ký tự."
    )
  })

  it("mất mạng khi gửi → câu mạng, không đoán nguyên nhân", () => {
    expect(errorMessage("message-send", new NetworkError("x"))).toBe(
      "Không kết nối được máy chủ."
    )
  })
})
