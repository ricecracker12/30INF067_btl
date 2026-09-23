import { BFF_ROUTES } from "./bff-contract"
import { request } from "./http"
import { pageQuery } from "./page-query"
import type * as T from "./types"

// Kiểu payload lấy từ hợp đồng `content-v1.yaml`. Bảy endpoint (sáu của GĐ2 + `GET /feed` của GĐ4) đi qua
// proxy chung `/bff/api/*` (Đ-E14) — không thêm route BFF nào. Proxy chuyển nguyên
// `new URL(request.url).search` sang API, nên query string dựng ở đây là đủ.

export const contentApi = {
  /**
   * Bước 1 của SEQ-01: MỘT request cho tối đa 10 ảnh (Đ-2.15). Ticket trả về **cùng thứ tự** với `files`.
   * `uploadUrl` mang chữ ký — không log, không lưu (Mục 1.2 luật 7).
   */
  createUploads: (b: T.CreateUploadsRequest) =>
    request<T.UploadTicket[]>(`${BFF_ROUTES.api}/media/uploads`, {
      method: "POST",
      body: b,
    }),

  /** 201. `mediaKeys` gửi lại đúng `{mediaKey, contentType, sizeBytes}` đã khai lúc presign (Q-D1). */
  create: (b: T.CreatePostRequest) =>
    request<T.PostResponse>(`${BFF_ROUTES.api}/posts`, {
      method: "POST",
      body: b,
    }),

  getPost: (postId: string, signal?: AbortSignal) =>
    request<T.PostResponse>(
      `${BFF_ROUTES.api}/posts/${encodeURIComponent(postId)}`,
      { signal }
    ),

  /** `mediaKeys` KHÔNG có trong `UpdatePostRequest` — GĐ2 không sửa ảnh, gửi vào là 400 (Mục 7.3). */
  updatePost: (postId: string, b: T.UpdatePostRequest) =>
    request<T.PostResponse>(
      `${BFF_ROUTES.api}/posts/${encodeURIComponent(postId)}`,
      { method: "PATCH", body: b }
    ),

  /** 204. Xóa mềm; gọi lại là 403 — không phân biệt được "của người khác" với "đã xóa" (E6). */
  deletePost: (postId: string) =>
    request<void>(`${BFF_ROUTES.api}/posts/${encodeURIComponent(postId)}`, {
      method: "DELETE",
    }),

  listUserPosts: (
    userId: string,
    opts: { cursor?: string | null; limit?: number } = {},
    signal?: AbortSignal
  ) =>
    request<T.PostPage>(
      `${BFF_ROUTES.api}/users/${encodeURIComponent(userId)}/posts${pageQuery(opts)}`,
      { signal }
    ),

  /**
   * Bảng tin của người gọi (GĐ4). Hết dữ liệu KHI VÀ CHỈ KHI `nextCursor === null` — trang ngắn hơn `limit`,
   * kể cả trang rỗng, là hợp lệ (Đ-4.9). `mode: "suggested"` = chưa có kết nối nào (Đ-4.6). 503 khi quá tải
   * (Đ-4.10) — màn hiện Thử lại, không đọc `Retry-After`.
   */
  feed: (
    opts: { cursor?: string | null; limit?: number } = {},
    signal?: AbortSignal
  ) =>
    request<T.FeedPage>(`${BFF_ROUTES.api}/feed${pageQuery(opts)}`, {
      signal,
    }),
}
