// Validation client cho màn hồ sơ (Đ-E5). Client NỚI HƠN HOẶC BẰNG server, không bao giờ chặt hơn;
// thông điệp dùng ĐÚNG câu của `UpsertProfileRequestValidator` để một lỗi không hiện hai cách nói.
//
// Đo `.length` (UTF-16) như `string.Length` của .NET — KHÁC mật khẩu của GĐ1, nơi server đếm byte UTF-8.
// Tên "Nguyễn" đo bằng ký tự là 6, bằng byte là 9: lẫn hai đơn vị là chặn nhầm tên tiếng Việt hợp lệ.

import type { FieldErrors } from "./auth"

export const DISPLAY_NAME_MIN_LENGTH = 2
export const DISPLAY_NAME_MAX_LENGTH = 50
export const BIO_MAX_LENGTH = 500

export type ProfileField = "displayName" | "bio"

/**
 * MỘT câu cho cả "quá ngắn", "quá dài" lẫn "toàn khoảng trắng" — chép nguyên quyết định của validator
 * server: ba nhánh đó là cùng một điều kiện với người dùng ("tên phải dài chừng này").
 *
 * Trim TRƯỚC khi đo, đúng như server: `"  An  "` là 2 ký tự sau trim nên HỢP LỆ; `"   "` thành 0 ký tự
 * nên rơi vào cùng nhánh quá ngắn.
 */
export function displayNameError(raw: string): string | undefined {
  const length = raw.trim().length
  if (length < DISPLAY_NAME_MIN_LENGTH || length > DISPLAY_NAME_MAX_LENGTH)
    return `Tên hiển thị phải có từ ${DISPLAY_NAME_MIN_LENGTH} đến ${DISPLAY_NAME_MAX_LENGTH} ký tự.`
  return undefined
}

/**
 * KHÔNG trim trước khi đo bio, đúng như server (`MaximumLength` trên giá trị thô): bio là văn bản tự do,
 * khoảng trắng đầu/cuối không đổi nghĩa nhưng vẫn tính vào độ dài cột `varchar(500)`.
 */
export function bioError(raw: string): string | undefined {
  if (raw.length > BIO_MAX_LENGTH)
    return `Giới thiệu tối đa ${BIO_MAX_LENGTH} ký tự.`
  return undefined
}

/** Hai luật ở trên là TẤT CẢ — không thêm luật nào server không có (Đ-E5: chặt hơn là chặn nhầm). */
export function validateProfile(input: {
  displayName: string
  bio: string
}): FieldErrors<ProfileField> {
  const errors: FieldErrors<ProfileField> = {}

  const displayName = displayNameError(input.displayName)
  if (displayName) errors.displayName = displayName

  const bio = bioError(input.bio)
  if (bio) errors.bio = bio

  return errors
}

/**
 * Giá trị `bio` gửi lên. `PUT` là thay thế TOÀN PHẦN (Q-D3): bỏ trống ô thì phải gửi `null` để XÓA bio,
 * KHÔNG được bỏ field ra khỏi body — bỏ field cũng là `null` ở server, nhưng "không gửi" và "gửi null"
 * trông khác nhau ở tầng gọi và dễ dẫn tới nhánh "giữ nguyên bio cũ" không tồn tại.
 */
export function bioToSend(raw: string): string | null {
  return raw.trim() === "" ? null : raw
}
