import "server-only"

// Đ-E14 — chỗ DUY NHẤT phía server gọi API .NET. Trình duyệt không bao giờ gọi API trực tiếp.

export class UpstreamUnavailableError extends Error {
  constructor(cause: unknown) {
    super("Không kết nối được API.", { cause })
    this.name = "UpstreamUnavailableError"
  }
}

export type UpstreamCall = {
  method: string
  /** Bắt đầu bằng "/", nối sau apiUrl (đã gồm /api/v1). */
  path: string
  search?: string
  body?: string | ArrayBuffer | null
  contentType?: string | null
  bearer?: string
  refreshToken?: string
  clientIp?: string | null
}

/** Quá thời gian thì bỏ — một API treo không được giữ kết nối của BFF (và khóa phiên) vô thời hạn. */
const TIMEOUT_MS = 10_000

export async function callUpstream(
  apiUrl: string,
  call: UpstreamCall
): Promise<Response> {
  const headers = new Headers({ Accept: "application/json" })
  if (call.contentType) headers.set("Content-Type", call.contentType)
  if (call.bearer) headers.set("Authorization", `Bearer ${call.bearer}`)
  // Cookie gửi server-to-server: API đọc refresh token từ cookie `refresh_token` (quyết định 6 của hợp đồng).
  if (call.refreshToken)
    headers.set("Cookie", `refresh_token=${call.refreshToken}`)
  if (call.clientIp) headers.set("X-Forwarded-For", call.clientIp)

  try {
    // eslint-disable-next-line no-restricted-globals -- Đ-E14: đây LÀ chỗ phía server được gọi API; trình duyệt đi qua lib/api/http.ts.
    return await fetch(`${apiUrl}${call.path}${call.search ?? ""}`, {
      method: call.method,
      headers,
      body: call.body ?? undefined,
      redirect: "manual",
      cache: "no-store",
      signal: AbortSignal.timeout(TIMEOUT_MS),
    })
  } catch (e) {
    throw new UpstreamUnavailableError(e)
  }
}
