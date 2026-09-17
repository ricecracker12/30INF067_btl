import { tokenStore } from "../auth/token-store"
import { API_BASE_URL } from "./config"
import { NetworkError, toApiError } from "./problem"

export type RequestOptions = {
  method?: "GET" | "POST"
  /** Giữ dạng object: nhánh 401 phải gửi lại được — ReadableStream chỉ đọc được một lần. */
  body?: unknown
  /** Gắn bearer; mặc định true. */
  auth?: boolean
  signal?: AbortSignal
}

type Internal = RequestOptions & { _retried?: boolean }

/**
 * 401 ở đây là lỗi NGHIỆP VỤ (sai mật khẩu…) hoặc chính refresh hỏng — không bao giờ kích hoạt refresh. Trùng với
 * `auth: false` của `authApi` một cách CỐ Ý: GĐ2 ai đó quên `auth: false` cho endpoint công khai mới thì login 401
 * vẫn không refresh.
 */
const NEVER_REFRESH = new Set([
  "/auth/login",
  "/auth/register",
  "/auth/verify-email",
  "/auth/refresh",
])

let getFreshToken: ((stale: string | null) => Promise<string>) | null = null

/**
 * Nối nhánh 401 với coordinator (E7). Gọi MỘT lần ở `lib/auth/session.ts` — http.ts không import coordinator trực
 * tiếp, tránh vòng `http → coordinator → authApi → http`. Chưa gọi thì 401 trả thẳng lỗi như trước.
 */
export function configureRefresh(
  fn: ((stale: string | null) => Promise<string>) | null
) {
  getFreshToken = fn
}

/**
 * Chỗ DUY NHẤT trong app được gọi `fetch` (Đ-E2, ESLint canh). Mọi lời gọi có
 * `credentials: 'include'` — thiếu là trình duyệt im lặng không gửi cookie refresh (quyết định 7
 * của hợp đồng) và mọi lần refresh trả 401 mà không có manh mối nào.
 */
export async function request<T>(
  path: string,
  opts: RequestOptions = {}
): Promise<T> {
  // Chụp token TRƯỚC fetch — đây là `stale` của nhánh 401 (bẫy 5 của E7: đọc lại sau `await` là luôn bằng token mới).
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

  // E7: 401 → refresh (single-flight) → gọi lại ĐÚNG MỘT lần. Gọi lại vẫn 401 thì trả lỗi — không refresh lần hai
  // (server từ chối cả token mới, vd `revoked:user`, thì vòng lặp không bao giờ dừng). 403 không vào đây: đó là quyết
  // định cuối của tầng 2/3, refresh không đổi được gì. Refresh 401 → coordinator ném `SessionExpiredError`; refresh
  // 429/500/mạng → ném nguyên lỗi đó, không đăng xuất.
  if (
    res.status === 401 &&
    getFreshToken &&
    opts.auth !== false &&
    !NEVER_REFRESH.has(path) &&
    !(opts as Internal)._retried
  ) {
    await getFreshToken(token)
    return request<T>(path, { ...opts, _retried: true } as Internal)
  }

  if (!res.ok) throw await toApiError(res)
  return (res.status === 204 ? undefined : await res.json()) as T
}
