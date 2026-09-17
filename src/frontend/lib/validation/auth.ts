// Validation client cho màn auth (Đ-E5). Client NỚI HƠN HOẶC BẰNG server, không bao giờ chặt hơn;
// thông điệp dùng ĐÚNG câu của validator server (Application/**/…RequestValidator.cs) để một lỗi
// không hiện hai cách nói.

export const MAX_EMAIL_LENGTH = 254
export const MIN_PASSWORD_LENGTH = 8
export const MAX_PASSWORD_BYTES = 72

export type LoginField = "email" | "password"
export type RegisterField = "email" | "password"
export type FieldErrors<K extends string> = Partial<Record<K, string>>

/** Đếm byte UTF-8 như `Encoding.UTF8.GetByteCount` của server — KHÔNG đếm ký tự. */
export function utf8ByteLength(value: string): number {
  return new TextEncoder().encode(value).length
}

/**
 * Mật khẩu theo thứ tự `Cascade(Stop)` của server: bắt buộc → tối thiểu 8 (chỉ đăng ký) → ≤ 72 byte.
 * Không `trim` giá trị: khoảng trắng hai đầu là một phần mật khẩu.
 */
export function passwordError(
  password: string,
  { requireMin }: { requireMin: boolean }
): string | undefined {
  // `NotEmpty()` của FluentValidation coi chuỗi toàn khoảng trắng là rỗng.
  if (password.trim() === "") return "Mật khẩu là bắt buộc."
  // `MinimumLength` đếm `string.Length` (UTF-16) — `String.length` của JS đếm cùng đơn vị.
  if (requireMin && password.length < MIN_PASSWORD_LENGTH)
    return "Mật khẩu phải có ít nhất 8 ký tự."
  if (utf8ByteLength(password) > MAX_PASSWORD_BYTES)
    return "Mật khẩu tối đa 72 byte."
  return undefined
}

/**
 * Email đăng ký — NỚI hơn `MailAddress.TryCreate` của server (Đ-E5): chỉ bắt lỗi chắc chắn sai (không
 * đúng một `@`, một phía rỗng, có khoảng trắng hoặc `<>`); phần còn lại để server nói. Regex email
 * chặt là chặn nhầm địa chỉ hợp lệ có `'` hay tên miền quốc tế mà người dùng không có đường thoát.
 */
export function registerEmailError(raw: string): string | undefined {
  const email = raw.trim()
  if (email === "") return "Email là bắt buộc."
  if (email.length > MAX_EMAIL_LENGTH) return "Email tối đa 254 ký tự."
  const at = email.indexOf("@")
  if (
    at <= 0 ||
    at !== email.lastIndexOf("@") ||
    at === email.length - 1 ||
    /[\s<>]/.test(email)
  )
    return "Email không đúng định dạng."
  return undefined
}

/**
 * Khớp `LoginRequestValidator`: email bắt buộc · ≤ 254, KHÔNG kiểm định dạng; mật khẩu bắt buộc ·
 * ≤ 72 byte, KHÔNG có tối thiểu 8 — tài khoản cũ vẫn phải đăng nhập được khi luật đăng ký đổi.
 */
export function validateLogin(input: {
  email: string
  password: string
}): FieldErrors<LoginField> {
  const errors: FieldErrors<LoginField> = {}

  // `LoginService` trim email trước khi tra — nên đo độ dài sau trim là nới hơn hoặc bằng server.
  const email = input.email.trim()
  if (email === "") errors.email = "Email là bắt buộc."
  else if (email.length > MAX_EMAIL_LENGTH)
    errors.email = "Email tối đa 254 ký tự."

  const password = passwordError(input.password, { requireMin: false })
  if (password) errors.password = password

  return errors
}

/**
 * Khớp `VerifyEmailRequestValidator`: đúng 64 ký tự hex THƯỜNG — thứ `SecureToken.Generate` sinh ra. Sai dạng
 * thì màn hiện trạng thái 400 mà không gọi API. `$` của JS (không cờ `m`) không khớp trước "\n" cuối chuỗi.
 */
export function isVerifyToken(token: string | null): token is string {
  return token !== null && /^[0-9a-f]{64}$/.test(token)
}

/** Khớp `RegisterRequestValidator`: email như `registerEmailError`, mật khẩu 8 ký tự – 72 byte. */
export function validateRegister(input: {
  email: string
  password: string
}): FieldErrors<RegisterField> {
  const errors: FieldErrors<RegisterField> = {}

  const email = registerEmailError(input.email)
  if (email) errors.email = email

  const password = passwordError(input.password, { requireMin: true })
  if (password) errors.password = password

  return errors
}
