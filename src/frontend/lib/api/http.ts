import { BFF_ROUTES } from "./bff-contract"
import { BFF_URL } from "./config"
import { NetworkError, toApiError } from "./problem"

export type RequestOptions = {
  method?: "GET" | "POST" | "PUT" | "PATCH" | "DELETE"
  body?: unknown
  signal?: AbortSignal
}

let onSessionExpired: (() => void) | null = null

/**
 * 401 từ proxy `/bff/api/*` nghĩa là BFF đã thử refresh ở server và phiên hết thật (Đ-E14) — không còn gì để trình duyệt
 * thử lại. Gọi MỘT lần ở lib/auth/session.ts để guard đưa người dùng về /login.
 */
export function configureSessionExpired(handler: (() => void) | null) {
  onSessionExpired = handler
}

/**
 * Chỗ DUY NHẤT trong code trình duyệt được gọi `fetch` (Đ-E2, ESLint canh). Mọi lời gọi tới BFF cùng origin
 * (`credentials: 'same-origin'` — cookie `__Host-sid` chỉ đi tới chính origin này). Không có bearer, không có token:
 * trình duyệt không bao giờ cầm token (Đ-E14).
 */
export async function request<T>(
  path: string,
  opts: RequestOptions = {}
): Promise<T> {
  let res: Response
  try {
    // eslint-disable-next-line no-restricted-globals -- Đ-E2: đây LÀ chỗ được phép gọi fetch; mọi nơi khác đi qua request().
    res = await fetch(`${BFF_URL}${path}`, {
      method: opts.method ?? "GET",
      credentials: "same-origin",
      headers:
        opts.body !== undefined
          ? { "Content-Type": "application/json" }
          : undefined,
      body: opts.body === undefined ? undefined : JSON.stringify(opts.body),
      signal: opts.signal,
    })
  } catch (e) {
    // Abort là do chính mình hủy — ném lại nguyên vẹn, không bọc thành lỗi mạng.
    if ((e as Error).name === "AbortError") throw e
    throw new NetworkError("Không kết nối được máy chủ.")
  }

  if (res.status === 401 && path.startsWith(`${BFF_ROUTES.api}/`))
    onSessionExpired?.()

  if (!res.ok) throw await toApiError(res)
  return (res.status === 204 ? undefined : await res.json()) as T
}
