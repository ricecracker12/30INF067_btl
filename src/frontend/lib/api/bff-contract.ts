// Đ-E14 — hình dạng các endpoint RIÊNG của BFF (trình duyệt ↔ Next server). Không có trong identity-v1.yaml vì API .NET
// không có chúng; payload nghiệp vụ đi qua BFF vẫn lấy kiểu từ `types.ts` (sinh từ hợp đồng).
// File này dùng chung cho code trình duyệt (auth-api.ts) và code server (lib/bff/handlers.ts): KHÔNG import gì server-only.

/** GET /bff/auth/session — trình duyệt chỉ biết có phiên hay không, không bao giờ thấy token. */
export type BffSessionState = { authenticated: boolean }

/** Các đường BFF, để client và server không gõ lệch chuỗi. */
export const BFF_ROUTES = {
  register: "/auth/register",
  verifyEmail: "/auth/verify-email",
  login: "/auth/login",
  logout: "/auth/logout",
  session: "/auth/session",
  /** Proxy chung tới API: /bff/api/<đường của API sau /api/v1>. */
  api: "/api",
} as const

/**
 * `type` riêng của Problem Details do CHÍNH BFF sinh (GĐ4 Q-E4). Tới trình duyệt, 503 có hai nghĩa: API quá tải (feed —
 * `type` khai trong content-v1.yaml) và BFF mất kho phiên (Redis). Trình duyệt phân nhánh theo `type`, không theo `title`.
 * Lỗi còn lại của BFF giữ `https://httpstatuses.io/{status}`.
 */
export const BFF_PROBLEM_TYPES = {
  sessionUnavailable: "urn:socialapp:problem:bff-session-unavailable",
} as const
