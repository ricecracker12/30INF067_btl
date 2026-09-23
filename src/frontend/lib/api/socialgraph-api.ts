import { BFF_ROUTES } from "./bff-contract"
import { request } from "./http"
import { pageQuery } from "./page-query"
import type * as T from "./types"

// Kiểu payload lấy từ hợp đồng `socialgraph-v1.yaml`: yaml đổi field là các dòng dưới đỏ compile, không phải đỏ lúc
// chạy. Chín endpoint đi qua proxy chung `/bff/api/*` (Đ-E14) — GĐ4 KHÔNG thêm route BFF nào.
//
// Không lời gọi nào ở đây đoán trước kết quả (Đ-4.16): thao tác ghi có body trả `RelationshipResponse` để màn vẽ lại
// nút theo sự thật của server; thao tác 204 thì màn tự suy (hoặc đọc lại `relationship`).

type PageOpts = { cursor?: string | null; limit?: number }

const user = (userId: string) => encodeURIComponent(userId)

export const socialGraphApi = {
  /**
   * Hai sự thật độc lập (Đ-4.5): `friendship` nhìn từ phía người gọi, và `following`. `userId` là chính mình → 400
   * `errors.userId` — màn không gọi cho hồ sơ của mình (Q-E2). `userId` không tồn tại → 200 `none`, không 404.
   */
  relationship: (userId: string, signal?: AbortSignal) =>
    request<T.RelationshipResponse>(
      `${BFF_ROUTES.api}/relationships/${user(userId)}`,
      { signal }
    ),

  /** 201, `friendship: "outgoing"`. Đã có lời mời theo bất kỳ chiều nào hoặc đã là bạn → 409 (FRD-06). */
  sendRequest: (userId: string) =>
    request<T.RelationshipResponse>(`${BFF_ROUTES.api}/friends/requests`, {
      method: "POST",
      body: { userId } satisfies T.CreateFriendRequest,
    }),

  /** 200, `friendship: "friends"`. MỘT 403 cho mọi lý do không chấp nhận được (Đ-4.14). */
  accept: (userId: string) =>
    request<T.RelationshipResponse>(
      `${BFF_ROUTES.api}/friends/requests/${user(userId)}/accept`,
      { method: "POST" }
    ),

  /**
   * 204, idempotent. CÙNG một endpoint cho "hủy" (tôi gửi) và "từ chối" (tôi nhận) — xóa lời mời `pending` theo chiều
   * nào cũng được. Quan hệ đã là bạn KHÔNG bị xóa ở đây (đó là `unfriend`).
   */
  removeRequest: (userId: string) =>
    request<void>(`${BFF_ROUTES.api}/friends/requests/${user(userId)}`, {
      method: "DELETE",
    }),

  /** 204, idempotent. Dòng theo dõi (nếu có) KHÔNG bị xóa theo (Đ-4.5). */
  unfriend: (userId: string) =>
    request<void>(`${BFF_ROUTES.api}/friends/${user(userId)}`, {
      method: "DELETE",
    }),

  /** 204, idempotent — vì vậy là `PUT`, không phải `POST` (gọi bằng `POST` là 405). */
  follow: (userId: string) =>
    request<void>(`${BFF_ROUTES.api}/follows/${user(userId)}`, {
      method: "PUT",
    }),

  /** 204, idempotent. Không chạm quan hệ bạn bè (Đ-4.5). */
  unfollow: (userId: string) =>
    request<void>(`${BFF_ROUTES.api}/follows/${user(userId)}`, {
      method: "DELETE",
    }),

  /** Bạn bè của chính người gọi, `accepted_at DESC`. Hết dữ liệu KHI VÀ CHỈ KHI `nextCursor === null` (Đ-4.9). */
  listFriends: (opts: PageOpts = {}, signal?: AbortSignal) =>
    request<T.FriendPage>(`${BFF_ROUTES.api}/friends${pageQuery(opts)}`, {
      signal,
    }),

  /**
   * Lời mời `pending` của chính người gọi. `direction` LUÔN gửi — không dựa vào mặc định `incoming` của server: bỏ
   * nó là mục "lời mời đã gửi" lặng lẽ hiện lời mời ĐẾN.
   */
  listRequests: (
    direction: T.FriendRequestDirection,
    opts: PageOpts = {},
    signal?: AbortSignal
  ) => {
    const page = pageQuery(opts)
    const qs = `?direction=${direction}${page ? `&${page.slice(1)}` : ""}`
    return request<T.FriendRequestPage>(
      `${BFF_ROUTES.api}/friends/requests${qs}`,
      { signal }
    )
  },
}
