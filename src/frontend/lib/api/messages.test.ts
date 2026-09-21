import { describe, expect, it } from "vitest"

import { errorMessage, validationErrors } from "./messages"
import { ApiError, NetworkError } from "./problem"
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
