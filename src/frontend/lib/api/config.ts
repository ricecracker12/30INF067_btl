// Đ-E1 — gốc API theo môi trường.
//
// PHẢI viết nguyên văn `process.env.NEXT_PUBLIC_API_BASE_URL`: Next thay chuỗi này bằng giá trị
// lúc build. Đọc động (`process.env[name]`) thì trên trình duyệt là `undefined`.
const fromEnv = process.env.NEXT_PUBLIC_API_BASE_URL

export const API_BASE_URL: string = (() => {
  if (fromEnv) return fromEnv.replace(/\/$/, "")
  // Dev gọi thẳng API local qua CORS. CẤM trỏ sang API staging: khác site → SameSite=Lax chặn
  // cookie refresh, và mọi lần refresh trả 401 mà không lỗi nào nói lý do.
  if (process.env.NODE_ENV !== "production")
    return "http://localhost:5259/api/v1"
  // Cùng tinh thần "thiếu cấu hình = từ chối chạy" của backend: build production mà quên biến
  // thì đỏ ngay, không đẩy một bundle gọi nhầm localhost lên staging.
  throw new Error(
    "Thiếu NEXT_PUBLIC_API_BASE_URL khi build production. Staging: /api/v1 (Đ-E1)."
  )
})()
