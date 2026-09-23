import { describe, expect, it } from "vitest"

import {
  IMAGE_ACCEPT_ATTRIBUTE,
  IMAGE_NOT_ALLOWED,
  imageFileError,
  isImageContentType,
} from "./media"

// Ngưỡng viết TAY bằng số, không tính lại từ hằng số của chính module: `MAX_IMAGE_BYTES` gõ nhầm thành
// 10 * 1000 * 1000 thì bảng dưới đây đỏ, còn `file.size <= MAX_IMAGE_BYTES` thì vẫn xanh.
const MUOI_MB = 10485760

describe("imageFileError — loại ảnh", () => {
  it.each(["image/jpeg", "image/png", "image/webp"])(
    "%s nằm trong allowlist Đ-2.8",
    (type) => {
      expect(imageFileError({ type, size: 1024 })).toBeUndefined()
    }
  )

  it.each(["image/gif", "image/heic", "image/svg+xml", "application/pdf", ""])(
    "%s bị chặn",
    (type) => {
      expect(imageFileError({ type, size: 1024 })).toBe(IMAGE_NOT_ALLOWED)
    }
  )

  it("`image/JPEG` viết hoa KHÔNG được nhận — server so chuỗi chính xác", () => {
    expect(imageFileError({ type: "image/JPEG", size: 1024 })).toBe(
      IMAGE_NOT_ALLOWED
    )
  })
})

describe("imageFileError — dung lượng", () => {
  it.each([
    [0, IMAGE_NOT_ALLOWED],
    [1, undefined],
    [MUOI_MB - 1, undefined],
    [MUOI_MB, undefined],
    [MUOI_MB + 1, IMAGE_NOT_ALLOWED],
  ])("%i byte", (size, expected) => {
    expect(imageFileError({ type: "image/jpeg", size })).toBe(expected)
  })
})

describe("giao kèo với hợp đồng và với hộp thoại chọn file", () => {
  it("một câu cho CẢ HAI lý do — client không nói khác server", () => {
    expect(imageFileError({ type: "image/gif", size: 1024 })).toBe(
      imageFileError({ type: "image/jpeg", size: MUOI_MB + 1 })
    )
  })

  it("câu chép nguyên CreateUploadsRequestValidator.FileNotAllowed", () => {
    expect(IMAGE_NOT_ALLOWED).toBe(
      "Chỉ nhận ảnh JPEG, PNG hoặc WebP, tối đa 10 MB mỗi ảnh."
    )
  })

  it("accept liệt kê đúng ba loại của allowlist", () => {
    expect(IMAGE_ACCEPT_ATTRIBUTE).toBe("image/jpeg,image/png,image/webp")
  })

  it("isImageContentType thu hẹp kiểu, không chỉ trả boolean", () => {
    const raw: string = "image/webp"
    expect(isImageContentType(raw)).toBe(true)
    expect(isImageContentType("image/gif")).toBe(false)
  })
})
