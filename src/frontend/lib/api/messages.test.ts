import { describe, expect, it } from "vitest"

import { errorMessage, fieldMessage, validationErrors } from "./messages"
import { ApiError, NetworkError, PROBLEM_TYPES } from "./problem"
import type { ProblemDetails } from "./types"

const problem = (status: number, extra: Partial<ProblemDetails> = {}) =>
  new ApiError(status, {
    type: `https://httpstatuses.io/${status}`,
    title: "t",
    status,
    traceId: "0af7651916cd43dd8448eb211c80319c",
    ...extra,
  })

describe("errorMessage('login', …)", () => {
  it("401 luôn ra ĐÚNG một câu, bất kể server ghi detail gì (AC-02)", () => {
    expect(errorMessage("login", problem(401))).toBe(
      "Email hoặc mật khẩu không đúng."
    )
    expect(
      errorMessage("login", problem(401, { detail: "Không tìm thấy email." }))
    ).toBe("Email hoặc mật khẩu không đúng.")
  })

  it("403 / 423 / 429 theo bảng", () => {
    expect(errorMessage("login", problem(403))).toBe(
      "Tài khoản chưa xác minh email. Vui lòng mở liên kết trong thư chúng tôi đã gửi."
    )
    expect(errorMessage("login", problem(423))).toBe(
      "Tài khoản tạm khóa do đăng nhập sai nhiều lần. Vui lòng thử lại sau 15 phút."
    )
    expect(errorMessage("login", problem(429))).toBe(
      "Bạn thao tác quá nhanh. Vui lòng thử lại sau ít phút."
    )
  })

  it("500 kèm traceId; 502 HTML (problem null) chỉ câu chung", () => {
    expect(errorMessage("login", problem(500))).toBe(
      "Đã xảy ra lỗi không mong muốn. Mã tra cứu: 0af7651916cd43dd8448eb211c80319c"
    )
    expect(errorMessage("login", new ApiError(502, null))).toBe(
      "Đã xảy ra lỗi không mong muốn."
    )
  })

  it("mất mạng và lỗi không phải của request() không đoán nguyên nhân", () => {
    expect(errorMessage("login", new NetworkError("x"))).toBe(
      "Không kết nối được máy chủ."
    )
    expect(errorMessage("login", new TypeError("boom"))).toBe(
      "Đã xảy ra lỗi không mong muốn."
    )
  })
})

describe("errorMessage('register', …)", () => {
  it("409 theo bảng, bất kể detail của server", () => {
    expect(
      errorMessage("register", problem(409, { detail: "Trùng email." }))
    ).toBe("Email này đã được đăng ký.")
  })

  it("401/403/423 của đăng nhập KHÔNG rò sang đăng ký — status lạ dùng detail dự phòng", () => {
    expect(
      errorMessage("register", problem(403, { detail: "Bị từ chối." }))
    ).toBe("Bị từ chối.")
  })

  it("429 / 500 / mất mạng như đăng nhập", () => {
    expect(errorMessage("register", problem(429))).toBe(
      "Bạn thao tác quá nhanh. Vui lòng thử lại sau ít phút."
    )
    expect(errorMessage("register", problem(500))).toBe(
      "Đã xảy ra lỗi không mong muốn. Mã tra cứu: 0af7651916cd43dd8448eb211c80319c"
    )
    expect(errorMessage("register", new NetworkError("x"))).toBe(
      "Không kết nối được máy chủ."
    )
  })
})

describe("errorMessage('verify-email', …)", () => {
  it("400 và 410 theo bảng, bất kể detail của server; 410 không nhắc gửi lại", () => {
    expect(
      errorMessage(
        "verify-email",
        problem(400, { detail: "Liên kết xác minh không hợp lệ." })
      )
    ).toBe(
      "Liên kết xác minh không hợp lệ. Hãy mở lại đúng liên kết trong thư, không sao chép thiếu ký tự."
    )
    const gone = errorMessage("verify-email", problem(410, { detail: "x" }))
    expect(gone).toBe(
      "Liên kết xác minh đã hết hạn hoặc đã được sử dụng. Nếu bạn đã xác minh trước đó, hãy đăng nhập."
    )
    expect(gone).not.toMatch(/gửi lại/i)
  })

  it("400 của màn khác vẫn là câu chung", () => {
    expect(errorMessage("login", problem(400))).toBe("Dữ liệu không hợp lệ.")
  })
})

describe("errorMessage — bảy ngữ cảnh GĐ2 (Q-E4)", () => {
  it("403 mang nghĩa KHÁC NHAU trên từng endpoint — đây là lý do tách theo endpoint, không theo module", () => {
    const e = problem(403, {
      detail: "Bạn không có quyền thực hiện thao tác này.",
    })

    expect(errorMessage("avatar", e)).toBe(
      "Ảnh này không thuộc về bạn. Hãy chọn lại ảnh."
    )
    expect(errorMessage("upload", e)).toBe(
      "Tài khoản của bạn chưa được phép đăng bài."
    )
    expect(errorMessage("post-create", e)).toBe(
      "Bạn cần hoàn tất hồ sơ trước khi đăng bài."
    )
    expect(errorMessage("post-write", e)).toBe(
      "Không tìm thấy bài viết, hoặc bạn không có quyền với bài này."
    )
    expect(
      new Set([
        errorMessage("avatar", e),
        errorMessage("upload", e),
        errorMessage("post-create", e),
        errorMessage("post-write", e),
      ]).size
    ).toBe(4)
  })

  it("403 của post-write KHÔNG tiết lộ bài có tồn tại hay không", () => {
    const câu = errorMessage("post-write", problem(403))
    // Hợp đồng trả 403 cho cả "bài của người khác" lẫn "bài đã xóa mềm": một câu phải đúng cho cả hai,
    // và không được khẳng định bài có thật (B.7 E6).
    expect(câu).toMatch(/hoặc/)
    expect(câu).not.toMatch(/của người khác|đã bị xóa/i)
  })

  it("post-create 409: ảnh đã dùng ở bài khác", () => {
    expect(errorMessage("post-create", problem(409))).toBe(
      "Ảnh này đã được dùng trong một bài khác. Hãy chọn lại ảnh."
    )
  })

  it("post-read 404 có câu riêng; profile-read 404 KHÔNG — đó là tín hiệu onboarding, không phải lỗi", () => {
    expect(errorMessage("post-read", problem(404))).toBe(
      "Không tìm thấy bài viết."
    )
    // Rơi về `detail` của server: màn E2 phân nhánh 404 TRƯỚC khi tới đây, nên không có câu nào
    // trong bảng cho ca này — đặt một câu vào đây là mời màn khác hiện "không tìm thấy hồ sơ".
    expect(
      errorMessage(
        "profile-read",
        problem(404, { detail: "Người dùng này chưa có hồ sơ." })
      )
    ).toBe("Người dùng này chưa có hồ sơ.")
  })

  it("400 của mọi ngữ cảnh mới ra câu chung — chi tiết đi theo key của `errors` (Đ-E5)", () => {
    for (const ctx of [
      "profile-write",
      "avatar",
      "upload",
      "post-create",
      "post-write",
    ] as const) {
      expect(errorMessage(ctx, problem(400))).toBe("Dữ liệu không hợp lệ.")
    }
  })

  it("429 và mất mạng dùng chung một câu cho mọi ngữ cảnh mới", () => {
    expect(errorMessage("upload", problem(429))).toBe(
      "Bạn thao tác quá nhanh. Vui lòng thử lại sau ít phút."
    )
    expect(errorMessage("post-create", new NetworkError("x"))).toBe(
      "Không kết nối được máy chủ."
    )
  })

  it("500 hiện mã tra cứu; không đoán nguyên nhân", () => {
    expect(errorMessage("post-create", problem(500))).toBe(
      "Đã xảy ra lỗi không mong muốn. Mã tra cứu: 0af7651916cd43dd8448eb211c80319c"
    )
    expect(errorMessage("avatar", new ApiError(502, null))).toBe(
      "Đã xảy ra lỗi không mong muốn."
    )
  })
})

describe("errorMessage — năm ngữ cảnh GĐ4 (Q-E3, lệch B.7)", () => {
  it("friend-request: 409 và 404 là NGUYÊN VĂN detail của socialgraph-v1 — bất kể server ghi gì lúc chạy", () => {
    expect(
      errorMessage("friend-request", problem(409, { detail: "khác" }))
    ).toBe("Đã có lời mời hoặc quan hệ bạn bè giữa hai người.")
    expect(errorMessage("friend-request", problem(404))).toBe(
      "Không tìm thấy người dùng."
    )
    expect(errorMessage("friend-request", problem(403))).toBe(
      "Tài khoản của bạn chưa được phép kết bạn."
    )
  })

  it("friend-respond: MỘT câu 403 cho mọi lý do (Đ-4.14) — detail của server không lọt ra", () => {
    expect(
      errorMessage(
        "friend-respond",
        problem(403, { detail: "Bạn không có quyền thực hiện thao tác này." })
      )
    ).toBe("Lời mời này không còn hiệu lực.")
  })

  it("follow: 403 nói đúng việc đang làm (theo dõi), 404 như friend-request", () => {
    expect(errorMessage("follow", problem(403))).toBe(
      "Tài khoản của bạn chưa được phép theo dõi người khác."
    )
    expect(errorMessage("follow", problem(404))).toBe(
      "Không tìm thấy người dùng."
    )
  })

  it("relationship: KHÔNG mượn câu 409/403 của friend-request — màn đọc không bao giờ nói “đã có lời mời”", () => {
    expect(errorMessage("relationship", problem(409, { detail: "d" }))).toBe(
      "d"
    )
    expect(errorMessage("relationship", problem(403, { detail: "d" }))).toBe(
      "d"
    )
    expect(errorMessage("relationship", problem(400))).toBe(
      "Dữ liệu không hợp lệ."
    )
  })

  it("feed 500 VẪN là lỗi hệ thống: có Mã tra cứu", () => {
    expect(errorMessage("feed", problem(500))).toBe(
      "Đã xảy ra lỗi không mong muốn. Mã tra cứu: 0af7651916cd43dd8448eb211c80319c"
    )
  })

  it("429 và mất mạng dùng chung một câu cho năm ngữ cảnh mới", () => {
    for (const ctx of [
      "relationship",
      "friend-request",
      "friend-respond",
      "follow",
      "feed",
    ] as const) {
      expect(errorMessage(ctx, problem(429))).toBe(
        "Bạn thao tác quá nhanh. Vui lòng thử lại sau ít phút."
      )
      expect(errorMessage(ctx, new NetworkError("x"))).toBe(
        "Không kết nối được máy chủ."
      )
    }
  })

  it("400 tự gửi lời mời: fieldMessage đọc ĐÚNG câu server dưới errors.userId (Đ-E5)", () => {
    const selfRequest = problem(400, {
      errors: { userId: ["Không thể gửi lời mời kết bạn cho chính mình."] },
    })
    expect(fieldMessage(selfRequest, "userId", "friend-request")).toBe(
      "Không thể gửi lời mời kết bạn cho chính mình."
    )
    // Key khác vắng → lùi về bảng, không nuốt lỗi.
    expect(fieldMessage(selfRequest, "direction", "relationship")).toBe(
      "Dữ liệu không hợp lệ."
    )
  })
})

describe("errorMessage — phân nhánh theo `type` của Problem Details (Q-E4)", () => {
  const SYSTEM =
    "Đã xảy ra lỗi không mong muốn. Mã tra cứu: 0af7651916cd43dd8448eb211c80319c"

  it("503 feed-overloaded: câu quá tải, KHÔNG kèm Mã tra cứu (Đ-4.10) — dù Problem Details có traceId", () => {
    const msg = errorMessage(
      "feed",
      problem(503, { type: PROBLEM_TYPES.feedOverloaded })
    )
    expect(msg).toBe("Bảng tin đang quá tải. Vui lòng thử lại sau ít phút.")
    expect(msg).not.toContain("Mã tra cứu")
  })

  it("503 bff-session-unavailable: câu gián đoạn đăng nhập — ở MỌI ngữ cảnh, kể cả feed, không Mã tra cứu", () => {
    for (const ctx of ["feed", "relationship", "post-read", "me"] as const) {
      expect(
        errorMessage(
          ctx,
          problem(503, { type: PROBLEM_TYPES.bffSessionUnavailable })
        )
      ).toBe(
        "Dịch vụ đăng nhập tạm thời gián đoạn. Vui lòng thử lại sau ít phút."
      )
    }
  })

  it("503 KHÔNG mang type riêng là lỗi hệ thống — kể cả ở feed (đổi có chủ đích so với E1: bảng không còn đoán theo status)", () => {
    expect(errorMessage("feed", problem(503))).toBe(SYSTEM)
    expect(errorMessage("relationship", problem(503))).toBe(SYSTEM)
  })

  it("so `type`, KHÔNG so `title`: title 'Bảng tin đang quá tải' mà type mặc định vẫn là lỗi hệ thống", () => {
    expect(
      errorMessage("feed", problem(503, { title: "Bảng tin đang quá tải" }))
    ).toBe(SYSTEM)
  })
})

describe("validationErrors", () => {
  it("gắn lỗi đầu tiên của từng trường màn đang có, không có lỗi cấp form", () => {
    const err = problem(400, {
      errors: { email: ["Email là bắt buộc."], password: ["A", "B"] },
    })
    expect(validationErrors(err, ["email", "password"])).toEqual({
      fields: { email: "Email là bắt buộc.", password: "A" },
      formMessage: null,
    })
  })

  it("key không thuộc màn (body) hoặc 400 không có errors → lỗi cấp form", () => {
    expect(
      validationErrors(problem(400, { errors: { body: ["JSON hỏng."] } }), [
        "email",
        "password",
      ])
    ).toEqual({ fields: {}, formMessage: "Dữ liệu không hợp lệ." })
    expect(validationErrors(problem(400), ["email"])).toEqual({
      fields: {},
      formMessage: "Dữ liệu không hợp lệ.",
    })
  })
})
