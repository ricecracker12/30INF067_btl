import { tokenStore } from "../auth/token-store"
import { API_BASE_URL } from "./config"
import { NetworkError, toApiError } from "./problem"

export type RequestOptions = {
  method?: "GET" | "POST"
  /** Giữ dạng object: E7 phải gửi lại được — ReadableStream chỉ đọc được một lần. */
  body?: unknown
  /** Gắn bearer; mặc định true. */
  auth?: boolean
  signal?: AbortSignal
}

/**
 * Chỗ DUY NHẤT trong app được gọi `fetch` (Đ-E2, ESLint canh). Mọi lời gọi có
 * `credentials: 'include'` — thiếu là trình duyệt im lặng không gửi cookie refresh (quyết định 7
 * của hợp đồng) và mọi lần refresh trả 401 mà không có manh mối nào.
 *
 * E7 chèn nhánh 401 → refresh → gọi lại vào đúng chỗ đánh dấu bên dưới; chữ ký hàm không đổi.
 */
export async function request<T>(
  path: string,
  opts: RequestOptions = {}
): Promise<T> {
  const token = opts.auth === false ? null : tokenStore.get()
  let res: Response
  try {
    // eslint-disable-next-line no-restricted-globals -- Đ-E2: đây LÀ chỗ được phép gọi fetch; mọi nơi khác đi qua request().
    res = await fetch(`${API_BASE_URL}${path}`, {
      method: opts.method ?? "GET",
      credentials: "include",
      headers: {
        ...(opts.body !== undefined && {
          "Content-Type": "application/json",
        }),
        ...(token && { Authorization: `Bearer ${token}` }),
      },
      body: opts.body === undefined ? undefined : JSON.stringify(opts.body),
      signal: opts.signal,
    })
  } catch (e) {
    // Abort là do chính mình hủy — ném lại nguyên vẹn, không bọc thành lỗi mạng.
    if ((e as Error).name === "AbortError") throw e
    throw new NetworkError("Không kết nối được máy chủ.")
  }

  // E7: nhánh 401 → refresh → gọi lại, chèn tại đây.

  if (!res.ok) throw await toApiError(res)
  return (res.status === 204 ? undefined : await res.json()) as T
}
