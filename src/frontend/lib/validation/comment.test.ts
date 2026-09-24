import { describe, expect, it } from "vitest"

import {
  COMMENT_EMPTY,
  COMMENT_TOO_LONG,
  MAX_COMMENT_LENGTH,
  commentBodyError,
} from "./comment"

describe("commentBodyError — chép CommentPolicy của server (Đ-3.14)", () => {
  it("rỗng và toàn khoảng trắng là cùng một lỗi", () => {
    expect(commentBodyError("")).toBe(COMMENT_EMPTY)
    expect(commentBodyError("   \n\t")).toBe(COMMENT_EMPTY)
  })

  it("đúng 1000 ký tự hợp lệ, 1001 thì không", () => {
    expect(commentBodyError("a".repeat(MAX_COMMENT_LENGTH))).toBeUndefined()
    expect(commentBodyError("a".repeat(MAX_COMMENT_LENGTH + 1))).toBe(
      COMMENT_TOO_LONG
    )
  })

  it("emoji đếm UTF-16 như string.Length của .NET — 500 emoji = 1000 đơn vị", () => {
    const emoji = "😀".repeat(500)
    expect(emoji.length).toBe(1000)
    expect(commentBodyError(emoji)).toBeUndefined()
    expect(commentBodyError(`${emoji}a`)).toBe(COMMENT_TOO_LONG)
  })

  it("không trim trước khi đo — khoảng trắng hai đầu vẫn tính vào độ dài", () => {
    expect(commentBodyError(` ${"a".repeat(MAX_COMMENT_LENGTH - 1)} `)).toBe(
      COMMENT_TOO_LONG
    )
  })

  it("thông điệp chép ĐÚNG câu server — một lỗi không hiện hai cách nói (Đ-E5)", () => {
    expect(COMMENT_EMPTY).toBe("Bình luận không được để trống.")
    expect(COMMENT_TOO_LONG).toBe("Bình luận không được vượt quá 1000 ký tự.")
  })
})
