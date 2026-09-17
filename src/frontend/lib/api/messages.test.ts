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
