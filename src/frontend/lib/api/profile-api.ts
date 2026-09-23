import { BFF_ROUTES } from "./bff-contract"
import { request } from "./http"
import type * as T from "./types"

// Kiểu payload lấy từ hợp đồng `profile-v1.yaml`: yaml đổi field là các dòng dưới đỏ compile, không
// phải đỏ lúc chạy. Ba endpoint đi qua proxy chung `/bff/api/*` (Đ-E14) — GĐ2 KHÔNG thêm route BFF nào.
export const profileApi = {
  /**
   * Hồ sơ công khai của một người. **404 không phải lỗi** khi `userId` là chính mình: đó là tín hiệu
   * chưa onboarding (Đ-2.4, E2) — màn tự phân nhánh, không đưa qua `errorMessage`.
   */
  get: (userId: string, signal?: AbortSignal) =>
    request<T.ProfileResponse>(
      `${BFF_ROUTES.api}/users/${encodeURIComponent(userId)}/profile`,
      { signal }
    ),

  /** Upsert: lần đầu là tạo (onboarding), các lần sau là sửa — cùng 200, FE không cần biết lần nào. */
  upsert: (b: T.UpsertProfileRequest) =>
    request<T.ProfileResponse>(`${BFF_ROUTES.api}/users/me/profile`, {
      method: "PUT",
      body: b,
    }),

  /** Bước 3 của luồng ba bước avatar: gắn `mediaKey` đã `PUT` xong lên R2. Trả hồ sơ có `avatarUrl` mới. */
  setAvatar: (b: T.SetAvatarRequest) =>
    request<T.ProfileResponse>(`${BFF_ROUTES.api}/users/me/avatar`, {
      method: "PUT",
      body: b,
    }),

  /** 204, idempotent — chỉ gỡ liên kết, object trên R2 dọn trễ (Đ-2.10). */
  removeAvatar: () =>
    request<void>(`${BFF_ROUTES.api}/users/me/avatar`, { method: "DELETE" }),
}
