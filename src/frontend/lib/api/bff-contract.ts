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
