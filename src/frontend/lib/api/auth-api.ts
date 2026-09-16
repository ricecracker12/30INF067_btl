import { request } from "./http"
import type * as T from "./types"

// Kiểu lấy từ hợp đồng: yaml đổi field là các dòng dưới đỏ compile, không phải đỏ lúc chạy.
export const authApi = {
  register: (b: T.RegisterRequest) =>
    request<T.RegisterResponse>("/auth/register", {
      method: "POST",
      body: b,
      auth: false,
    }),

  verifyEmail: (b: T.VerifyEmailRequest) =>
    request<T.VerifyEmailResponse>("/auth/verify-email", {
      method: "POST",
      body: b,
      auth: false,
    }),

  login: (b: T.LoginRequest) =>
    request<T.TokenResponse>("/auth/login", {
      method: "POST",
      body: b,
      auth: false,
    }),

  /**
   * KHÔNG body, KHÔNG Content-Type (quyết định 6: "endpoint này không nhận body") — refresh token
   * đi trong cookie. CHỈ `lib/auth/session.ts` của E7 được gọi hàm này: gọi thẳng từ chỗ khác là
   * phá single-flight và có thể tự kích hoạt reuse detection của server.
   */
  refresh: () =>
    request<T.TokenResponse>("/auth/refresh", { method: "POST", auth: false }),

  logout: () => request<void>("/auth/logout", { method: "POST" }),

  me: (signal?: AbortSignal) => request<T.MeResponse>("/me", { signal }),
}
