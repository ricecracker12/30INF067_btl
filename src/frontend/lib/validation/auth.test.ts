import { describe, expect, it } from "vitest"

import { utf8ByteLength, validateLogin } from "./auth"

// Số liệu viết tay, KHÔNG tính từ hằng số trong auth.ts — tính từ hằng số thì sửa sai hằng số là
// test sai theo, vẫn xanh.
describe("utf8ByteLength", () => {
  it("đếm byte, không đếm ký tự", () => {
    expect(utf8ByteLength("abc")).toBe(3)
    expect(utf8ByteLength("ệ")).toBe(3)
    expect("ệ".repeat(25)).toHaveLength(25)
    expect(utf8ByteLength("ệ".repeat(25))).toBe(75)
  })
})

describe("validateLogin", () => {
  const ok = { email: "an@example.com", password: "x" }

  it("hợp lệ → không lỗi; mật khẩu 1 ký tự vẫn qua (đăng nhập KHÔNG có tối thiểu 8)", () => {
    expect(validateLogin(ok)).toEqual({})
    expect(validateLogin({ ...ok, password: "1234567" })).toEqual({})
  })

  it("email KHÔNG kiểm định dạng — server đưa email lạ về 401 chung", () => {
    expect(validateLogin({ ...ok, email: "khong-phai-email" })).toEqual({})
  })

  it("bắt buộc: rỗng hoặc toàn khoảng trắng", () => {
    expect(validateLogin({ email: "", password: "" })).toEqual({
      email: "Email là bắt buộc.",
      password: "Mật khẩu là bắt buộc.",
    })
    expect(validateLogin({ email: "   ", password: "  " })).toEqual({
      email: "Email là bắt buộc.",
      password: "Mật khẩu là bắt buộc.",
    })
  })

  it("email: 254 ký tự xanh, 255 đỏ; khoảng trắng hai đầu không tính", () => {
    const e254 = "a".repeat(242) + "@example.com"
    expect(e254).toHaveLength(254)
    expect(validateLogin({ ...ok, email: e254 })).toEqual({})
    expect(validateLogin({ ...ok, email: `  ${e254}  ` })).toEqual({})
    expect(validateLogin({ ...ok, email: "a" + e254 })).toEqual({
      email: "Email tối đa 254 ký tự.",
    })
  })

  it("mật khẩu: 72 byte xanh, 73 byte đỏ, 25 × 'ệ' = 75 byte đỏ", () => {
    expect(validateLogin({ ...ok, password: "a".repeat(72) })).toEqual({})
    expect(validateLogin({ ...ok, password: "a".repeat(73) })).toEqual({
      password: "Mật khẩu tối đa 72 byte.",
    })
    // 24 × 3 byte = 72 → xanh; chỉ 24 ký tự nên đếm ký tự cũng xanh — ca dưới mới phân biệt.
    expect(validateLogin({ ...ok, password: "ệ".repeat(24) })).toEqual({})
    expect(validateLogin({ ...ok, password: "ệ".repeat(25) })).toEqual({
      password: "Mật khẩu tối đa 72 byte.",
    })
  })

  it("mật khẩu KHÔNG trim: khoảng trắng hai đầu vẫn tính byte", () => {
    expect(validateLogin({ ...ok, password: ` ${"a".repeat(71)} ` })).toEqual({
      password: "Mật khẩu tối đa 72 byte.",
    })
  })
})
