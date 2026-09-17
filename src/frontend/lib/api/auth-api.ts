import { BFF_ROUTES, type BffSessionState } from "./bff-contract"
import { request } from "./http"
import type * as T from "./types"

// Kiểu payload lấy từ hợp đồng: yaml đổi field là các dòng dưới đỏ compile, không phải đỏ lúc chạy.
// Mọi lời gọi đi tới BFF (Đ-E14); BFF gọi API .NET ở server. Không hàm nào trả token cho trình duyệt.
export const authApi = {
  register: (b: T.RegisterRequest) =>
    request<T.RegisterResponse>(BFF_ROUTES.register, {
      method: "POST",
      body: b,
    }),

  verifyEmail: (b: T.VerifyEmailRequest) =>
    request<T.VerifyEmailResponse>(BFF_ROUTES.verifyEmail, {
      method: "POST",
      body: b,
    }),

  /** 204: BFF giữ token ở server, trình duyệt nhận cookie phiên `__Host-sid` (HttpOnly). */
  login: (b: T.LoginRequest) =>
    request<void>(BFF_ROUTES.login, { method: "POST", body: b }),

  logout: () => request<void>(BFF_ROUTES.logout, { method: "POST" }),

  /** Có phiên hay không — guard khởi động (E6). Refresh token do BFF tự lo, trình duyệt không gọi refresh. */
  session: () => request<BffSessionState>(BFF_ROUTES.session),

  me: (signal?: AbortSignal) =>
    request<T.MeResponse>(`${BFF_ROUTES.api}/me`, { signal }),
}
