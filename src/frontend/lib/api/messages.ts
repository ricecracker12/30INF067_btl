import { ApiError, NetworkError } from "./problem"

// Đ-E6: thông điệp lỗi do FE sở hữu, ánh xạ theo (màn/endpoint, status). `detail` của server chỉ là
// dự phòng cho status chưa có trong bảng — hiện `detail` cho 401 đăng nhập là để AC-02 phụ thuộc vào
// việc server không bao giờ sửa một chữ ở nhánh "email không tồn tại", mà không test backend nào canh.

/** Màn gọi API. */
export type ErrorContext = "login" | "register" | "verify-email" | "me"

const COMMON = {
  400: "Dữ liệu không hợp lệ.",
  429: "Bạn thao tác quá nhanh. Vui lòng thử lại sau ít phút.",
  network: "Không kết nối được máy chủ.",
  unexpected: "Đã xảy ra lỗi không mong muốn.",
} as const

const BY_CONTEXT: Record<ErrorContext, Partial<Record<number, string>>> = {
  login: {
    // MỘT câu cho cả "email không tồn tại" lẫn "sai mật khẩu" (AC-02). Không thêm "sai lần thứ N" —
    // FE không biết, đoán là lộ thông tin.
    401: "Email hoặc mật khẩu không đúng.",
    // GĐ1 không có endpoint gửi lại mail → không có nút "Gửi lại".
    403: "Tài khoản chưa xác minh email. Vui lòng mở liên kết trong thư chúng tôi đã gửi.",
    // Không đếm ngược: FE không biết `locked_until`.
    423: "Tài khoản tạm khóa do đăng nhập sai nhiều lần. Vui lòng thử lại sau 15 phút.",
  },
  register: {
    // Ngoại lệ CÓ Ý THỨC của hợp đồng so với AC-02: không báo trùng thì người dùng không biết vì sao
    // đăng ký hỏng. Form hiện câu này dưới trường email, kèm link "Đăng nhập".
    409: "Email này đã được đăng ký.",
  },
  "verify-email": {
    // Cùng câu cho token sai dạng (validator, có `errors.token`) và token không tồn tại (không có
    // `errors`) — người dùng làm cùng một việc: mở lại đúng liên kết.
    400: "Liên kết xác minh không hợp lệ. Hãy mở lại đúng liên kết trong thư, không sao chép thiếu ký tự.",
    // 410 có HAI nghĩa: hết hạn hoặc đã dùng. Câu phải đúng cho cả hai — người bấm link lần hai là ca phổ
    // biến nhất, và họ đăng nhập được. GĐ1 không có endpoint gửi lại mail → không nhắc "gửi lại".
    410: "Liên kết xác minh đã hết hạn hoặc đã được sử dụng. Nếu bạn đã xác minh trước đó, hãy đăng nhập.",
  },
  // `/me` không có mã riêng: 401 là việc của interceptor (E7), còn lại dùng nhánh chung.
  me: {},
}

/** Thông điệp cấp form cho một lỗi bất kỳ ném ra từ `request()`. */
export function errorMessage(context: ErrorContext, error: unknown): string {
  if (error instanceof NetworkError) return COMMON.network
  if (!(error instanceof ApiError)) return COMMON.unexpected

  const known = BY_CONTEXT[context][error.status]
  if (known) return known
  if (error.status === 400) return COMMON[400]
  if (error.status === 429) return COMMON[429]

  // 5xx: không đoán nguyên nhân; traceId là thứ duy nhất backend cần để tìm log. 502 HTML của
  // apache không có traceId → chỉ câu chung.
  if (error.status >= 500) {
    return error.traceId
      ? `${COMMON.unexpected} Mã tra cứu: ${error.traceId}`
      : COMMON.unexpected
  }

  return error.problem?.detail ?? COMMON.unexpected
}

/**
 * Tách lỗi 400 theo trường (Đ-E5: mọi 400 hiển thị theo key của `errors`). Trả về lỗi cho các
 * trường màn đang có, và `formMessage` khi còn lỗi không gắn được vào trường nào (`body`, key lạ,
 * hoặc 400 không có `errors`) — không để lỗi nào biến mất lặng lẽ.
 */
export function validationErrors<K extends string>(
  error: ApiError,
  fields: readonly K[]
): { fields: Partial<Record<K, string>>; formMessage: string | null } {
  const result: Partial<Record<K, string>> = {}
  let unmatched = false

  for (const [key, messages] of Object.entries(error.fieldErrors)) {
    const field = fields.find((f) => f === key)
    const first = messages[0]
    if (field && first) result[field] = first
    else unmatched = true
  }

  const matchedAny = Object.keys(result).length > 0
  return {
    fields: result,
    formMessage: unmatched || !matchedAny ? COMMON[400] : null,
  }
}
