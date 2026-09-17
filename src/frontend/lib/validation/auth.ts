// Validation client cho màn auth (Đ-E5). Client NỚI HƠN HOẶC BẰNG server, không bao giờ chặt hơn;
// thông điệp dùng ĐÚNG câu của validator server (Application/**/…RequestValidator.cs) để một lỗi
// không hiện hai cách nói.

export const MAX_EMAIL_LENGTH = 254
export const MAX_PASSWORD_BYTES = 72

export type LoginField = "email" | "password"
export type FieldErrors<K extends string> = Partial<Record<K, string>>

/** Đếm byte UTF-8 như `Encoding.UTF8.GetByteCount` của server — KHÔNG đếm ký tự. */
export function utf8ByteLength(value: string): number {
  return new TextEncoder().encode(value).length
}

/**
 * Khớp `LoginRequestValidator`: email bắt buộc · ≤ 254, KHÔNG kiểm định dạng; mật khẩu bắt buộc ·
 * ≤ 72 byte, KHÔNG có tối thiểu 8 — tài khoản cũ vẫn phải đăng nhập được khi luật đăng ký đổi.
 * Mật khẩu không `trim`.
 */
export function validateLogin(input: {
  email: string
  password: string
}): FieldErrors<LoginField> {
  const errors: FieldErrors<LoginField> = {}

  // Server `NotEmpty()` coi chuỗi toàn khoảng trắng là rỗng, và `LoginService` trim email trước khi
  // tra — nên đo độ dài sau trim là nới hơn hoặc bằng server.
  const email = input.email.trim()
  if (email === "") errors.email = "Email là bắt buộc."
  else if (email.length > MAX_EMAIL_LENGTH)
    errors.email = "Email tối đa 254 ký tự."

  if (input.password.trim() === "") errors.password = "Mật khẩu là bắt buộc."
  else if (utf8ByteLength(input.password) > MAX_PASSWORD_BYTES)
    errors.password = "Mật khẩu tối đa 72 byte."

  return errors
}
