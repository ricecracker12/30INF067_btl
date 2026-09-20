import { describe, expect, it } from "vitest"

import {
  bioError,
  bioToSend,
  displayNameError,
  validateProfile,
} from "./profile"

// Số liệu VIẾT TAY, không tính từ hằng số: đổi hằng số mà quên đổi server thì bảng này phải đỏ.
// Đổi hằng số rồi sửa cả hai nơi cho khớp là việc có ý thức — tính từ hằng số thì test xanh theo và
// không ai biết client vừa lệch server.

const DISPLAY_NAME_MESSAGE = "Tên hiển thị phải có từ 2 đến 50 ký tự."
const BIO_MESSAGE = "Giới thiệu tối đa 500 ký tự."

describe("displayNameError — trim TRƯỚC khi đo, đúng như server", () => {
  it.each([
    ["", "rỗng"],
    [" ", "một khoảng trắng"],
    ["   ", "toàn khoảng trắng → 0 ký tự sau trim"],
    ["A", "1 ký tự"],
    [" A ", "1 ký tự sau trim"],
  ])("từ chối %j (%s)", (value) => {
    expect(displayNameError(value)).toBe(DISPLAY_NAME_MESSAGE)
  })

  it.each([
    ["An", "đúng 2 ký tự — ngưỡng dưới"],
    ["  An  ", "2 ký tự SAU TRIM → hợp lệ, không phải 400"],
    ["An Nguyễn", "tên tiếng Việt có dấu"],
  ])("nhận %j (%s)", (value) => {
    expect(displayNameError(value)).toBeUndefined()
  })

  it("50 ký tự hợp lệ, 51 thì không — ngưỡng trên", () => {
    expect(displayNameError("a".repeat(50))).toBeUndefined()
    expect(displayNameError("a".repeat(51))).toBe(DISPLAY_NAME_MESSAGE)
  })

  it("50 ký tự SAU TRIM vẫn hợp lệ dù chuỗi thô dài hơn", () => {
    expect(displayNameError(`  ${"a".repeat(50)}  `)).toBeUndefined()
  })

  it("đo KÝ TỰ (UTF-16) như string.Length của .NET, KHÔNG đo byte UTF-8", () => {
    // "Nguyễn" = 6 ký tự nhưng 9 byte UTF-8. Lấy byte làm đơn vị là chặn nhầm tên tiếng Việt hợp lệ.
    const ten = "Nguyễn".repeat(8) // 48 ký tự, 72 byte
    expect(ten).toHaveLength(48)
    expect(new TextEncoder().encode(ten).length).toBeGreaterThan(50)
    expect(displayNameError(ten)).toBeUndefined()
  })
})

describe("bioError — KHÔNG trim trước khi đo", () => {
  it("bỏ trống hợp lệ: bio không bắt buộc", () => {
    expect(bioError("")).toBeUndefined()
    expect(bioError("   ")).toBeUndefined()
  })

  it("500 ký tự hợp lệ, 501 thì không", () => {
    expect(bioError("a".repeat(500))).toBeUndefined()
    expect(bioError("a".repeat(501))).toBe(BIO_MESSAGE)
  })

  it("khoảng trắng hai đầu VẪN tính vào độ dài — cột varchar(500) đếm cả chúng", () => {
    // 499 ký tự + 2 khoảng trắng = 501: trim trước khi đo là nới hơn server, và người dùng nhận 400.
    expect(bioError(` ${"a".repeat(499)} `)).toBe(BIO_MESSAGE)
  })
})

describe("validateProfile", () => {
  it("hợp lệ → không lỗi nào", () => {
    expect(validateProfile({ displayName: "An", bio: "" })).toEqual({})
  })

  it("gom lỗi của cả hai trường cùng lúc", () => {
    expect(validateProfile({ displayName: "", bio: "a".repeat(501) })).toEqual({
      displayName: DISPLAY_NAME_MESSAGE,
      bio: BIO_MESSAGE,
    })
  })

  it("KHÔNG có luật nào server không có (Đ-E5): ký tự đặc biệt, emoji, số đều qua", () => {
    for (const ten of ["A<B>", "Trần 'Bo'", "名前です", "An 🌻", "a.b-c_d"]) {
      expect(validateProfile({ displayName: ten, bio: "" })).toEqual({})
    }
  })
})

describe("bioToSend — PUT là thay thế toàn phần (Q-D3)", () => {
  it("ô trống → null (XÓA bio), không phải chuỗi rỗng và không phải bỏ field", () => {
    expect(bioToSend("")).toBeNull()
    expect(bioToSend("   ")).toBeNull()
  })

  it("có nội dung → gửi NGUYÊN chuỗi, giữ khoảng trắng và xuống dòng người dùng gõ", () => {
    expect(bioToSend(" xin chào\nhai dòng ")).toBe(" xin chào\nhai dòng ")
  })
})
