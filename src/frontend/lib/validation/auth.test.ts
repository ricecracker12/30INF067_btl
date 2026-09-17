import { describe, expect, it } from "vitest"

import {
  passwordError,
  registerEmailError,
  utf8ByteLength,
  validateLogin,
  validateRegister,
} from "./auth"

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

describe("passwordError — bảng ngưỡng E3", () => {
  it.each([
    ["a".repeat(7), true, "Mật khẩu phải có ít nhất 8 ký tự."],
    ["a".repeat(8), true, undefined],
    ["a".repeat(72), true, undefined],
    ["a".repeat(73), true, "Mật khẩu tối đa 72 byte."],
    ["ệ".repeat(24), true, undefined], // 72 byte
    ["ệ".repeat(25), true, "Mật khẩu tối đa 72 byte."], // 75 byte
    ["mậtkhẩu", false, undefined], // 7 ký tự — đăng nhập không có tối thiểu
    ["mậtkhẩu", true, "Mật khẩu phải có ít nhất 8 ký tự."],
    ["", true, "Mật khẩu là bắt buộc."],
    ["        ", true, "Mật khẩu là bắt buộc."], // 8 khoảng trắng: NotEmpty của server coi là rỗng
  ])("%j (requireMin=%s) → %s", (pw, requireMin, expected) => {
    expect(passwordError(pw, { requireMin })).toBe(expected)
  })

  it("chuỗi tiếng Việt 40 ký tự vượt 72 byte → đỏ dù đủ ký tự", () => {
    const pw = "ượ".repeat(20) // ư 2 byte + ợ 3 byte
    expect(pw).toHaveLength(40)
    expect(utf8ByteLength(pw)).toBe(100)
    expect(passwordError(pw, { requireMin: true })).toBe(
      "Mật khẩu tối đa 72 byte."
    )
  })
})

describe("registerEmailError — NỚI hơn MailAddress của server (Đ-E5)", () => {
  it.each([
    "an@example.com",
    " An@Example.com ",
    "o'brien@example.com",
    "an@ví-dụ.vn",
  ])("%j hợp lệ", (email) => {
    expect(registerEmailError(email)).toBeUndefined()
  })

  it.each([
    "an.example.com",
    "@x.com",
    "a@",
    "a@@b.com",
    "Tên <a@b.com>",
    "a b@c.com",
  ])("%j → Email không đúng định dạng.", (email) => {
    expect(registerEmailError(email)).toBe("Email không đúng định dạng.")
  })

  it("bắt buộc, và 254 ký tự sau trim xanh / 255 đỏ", () => {
    expect(registerEmailError("   ")).toBe("Email là bắt buộc.")
    const e254 = "a".repeat(242) + "@example.com"
    expect(registerEmailError(` ${e254} `)).toBeUndefined()
    expect(registerEmailError("a" + e254)).toBe("Email tối đa 254 ký tự.")
  })
})

describe("validateRegister", () => {
  it("gom lỗi của cả hai trường; hợp lệ → rỗng", () => {
    expect(
      validateRegister({ email: "an@example.com", password: "MatKhau123" })
    ).toEqual({})
    expect(validateRegister({ email: "a@", password: "1234567" })).toEqual({
      email: "Email không đúng định dạng.",
      password: "Mật khẩu phải có ít nhất 8 ký tự.",
    })
  })
})
