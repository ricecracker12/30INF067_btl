import "server-only"

import { isIP } from "node:net"

import { isSessionId } from "./crypto"

// Đ-E14 — phần HTTP thuần của BFF: cookie, header, Problem Details. Không gọi mạng, không chạm Redis.

/**
 * `__Host-`: trình duyệt CHỈ nhận cookie khi có `Secure`, `Path=/` và KHÔNG có `Domain` — subdomain khác không ghi đè
 * được. Trình duyệt coi http://localhost là ngữ cảnh an toàn nên dev vẫn nhận (Safari thì không — cùng bẫy E4).
 */
export const SESSION_COOKIE = "__Host-sid"
const UPSTREAM_REFRESH_COOKIE = "refresh_token"

/** Mọi response của BFF: không cache ở bất kỳ tầng nào (trình duyệt, proxy, CDN). */
export const NO_STORE: Readonly<Record<string, string>> = {
  "Cache-Control": "no-store",
  Pragma: "no-cache",
  "X-Content-Type-Options": "nosniff",
}

export function readSessionId(request: Request): string | null {
  const header = request.headers.get("cookie")
  if (!header) return null
  for (const part of header.split(";")) {
    const eq = part.indexOf("=")
    if (eq < 0) continue
    if (part.slice(0, eq).trim() !== SESSION_COOKIE) continue
    const value = part.slice(eq + 1).trim()
    return isSessionId(value) ? value : null
  }
  return null
}

export function sessionCookie(sid: string, maxAgeSeconds: number): string {
  return `${SESSION_COOKIE}=${sid}; Path=/; HttpOnly; Secure; SameSite=Lax; Max-Age=${maxAgeSeconds}`
}

export function clearSessionCookie(): string {
  return `${SESSION_COOKIE}=; Path=/; HttpOnly; Secure; SameSite=Lax; Max-Age=0`
}

/**
 * Refresh token API vừa đặt trong `Set-Cookie`. BFF đọc nó ở server rồi cất vào Redis — header này KHÔNG BAO GIỜ được
 * chuyển tiếp ra trình duyệt.
 */
export function readUpstreamRefreshCookie(
  response: Response
): { token: string; maxAgeSeconds: number } | null {
  for (const line of response.headers.getSetCookie()) {
    const [pair, ...attributes] = line.split(";")
    const eq = pair.indexOf("=")
    if (eq < 0 || pair.slice(0, eq).trim() !== UPSTREAM_REFRESH_COOKIE) continue
    const token = pair.slice(eq + 1).trim()
    let maxAgeSeconds = 7 * 24 * 60 * 60
    for (const attribute of attributes) {
      const [name, value] = attribute.split("=").map((s) => s.trim())
      if (name?.toLowerCase() === "max-age" && value)
        maxAgeSeconds = Number(value)
    }
    if (!token || !Number.isFinite(maxAgeSeconds) || maxAgeSeconds <= 0)
      return null
    return { token, maxAgeSeconds: Math.floor(maxAgeSeconds) }
  }
  return null
}

/**
 * Chống CSRF: cookie phiên tự đi kèm mọi request tới origin này, kể cả request do trang lạ kích hoạt. Mọi method thay
 * đổi dữ liệu phải có `Origin` ĐÚNG origin của app. `SameSite=Lax` đã chặn POST liên site ở trình duyệt hiện đại — đây là
 * lớp thứ hai, không dựa vào trình duyệt. `Sec-Fetch-Site` có mặt mà không phải same-origin thì cũng chặn.
 */
export function isSameOriginRequest(request: Request, appOrigin: string) {
  const fetchSite = request.headers.get("sec-fetch-site")
  if (fetchSite && fetchSite !== "same-origin") return false
  if (request.method === "GET" || request.method === "HEAD") return true
  return request.headers.get("origin") === appOrigin
}

/**
 * IP người dùng để API rate limit đúng người (API tin X-Forwarded-For của BFF — `ForwardedClientIpTests`).
 * Lấy phần tử thứ `trustedProxyHops` tính từ CUỐI: các phần tử sau nó do hạ tầng tin cậy gắn, các phần tử trước nó client
 * tự viết được. Development không có proxy (hops = 0): Next đặt header bằng địa chỉ socket.
 */
export function clientIp(request: Request, trustedProxyHops: number) {
  const parts = (request.headers.get("x-forwarded-for") ?? "")
    .split(",")
    .map((s) => s.trim())
    .filter(Boolean)
  const candidate = parts[parts.length - 1 - trustedProxyHops]
  return candidate && isIP(candidate) ? candidate : null
}

export function problem(
  status: number,
  title: string,
  detail?: string,
  extraHeaders: Record<string, string> = {},
  // Q-E4: chỉ đặt khi trình duyệt phải phân biệt ca này với một lỗi cùng status (BFF_PROBLEM_TYPES).
  type = `https://httpstatuses.io/${status}`
): Response {
  return new Response(
    JSON.stringify({
      type,
      title,
      status,
      ...(detail && { detail }),
    }),
    {
      status,
      headers: {
        ...NO_STORE,
        "Content-Type": "application/problem+json; charset=utf-8",
        ...extraHeaders,
      },
    }
  )
}

/** Header của API được phép ra trình duyệt — danh sách TRẮNG. Set-Cookie, Authorization… không bao giờ đi qua. */
const FORWARDED_RESPONSE_HEADERS = ["content-type", "x-correlation-id"]

export async function relay(
  upstream: Response,
  extraHeaders: Record<string, string> = {}
): Promise<Response> {
  const headers = new Headers(NO_STORE)
  for (const name of FORWARDED_RESPONSE_HEADERS) {
    const value = upstream.headers.get(name)
    if (value) headers.set(name, value)
  }
  for (const [name, value] of Object.entries(extraHeaders))
    headers.append(name, value)
  const body =
    upstream.status === 204 || upstream.status === 304
      ? null
      : await upstream.arrayBuffer()
  return new Response(body, { status: upstream.status, headers })
}

/** Đọc body JSON có giới hạn — BFF không nhận body tùy ý rồi chuyển nguyên cho API. */
export async function readJsonBody(
  request: Request,
  maxBytes = 16 * 1024
): Promise<string | Response> {
  const type = request.headers.get("content-type") ?? ""
  if (!type.toLowerCase().startsWith("application/json"))
    return problem(
      415,
      "Kiểu nội dung không được hỗ trợ",
      "Cần application/json."
    )
  const declared = Number(request.headers.get("content-length") ?? "0")
  if (declared > maxBytes) return problem(413, "Dữ liệu quá lớn")
  const text = await request.text()
  if (Buffer.byteLength(text, "utf8") > maxBytes)
    return problem(413, "Dữ liệu quá lớn")
  return text
}
