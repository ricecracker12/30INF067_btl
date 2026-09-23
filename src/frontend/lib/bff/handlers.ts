import "server-only"

import { BFF_PROBLEM_TYPES, type BffSessionState } from "@/lib/api/bff-contract"
import type { TokenResponse } from "@/lib/api/types"

import type { BffConfig } from "./config"
import { newSessionId } from "./crypto"
import {
  clearSessionCookie,
  clientIp,
  isSameOriginRequest,
  NO_STORE,
  problem,
  readJsonBody,
  readSessionId,
  readUpstreamRefreshCookie,
  relay,
  sessionCookie,
} from "./http"
import type { SessionStore, StoredSession } from "./session-store"
import { callUpstream, type UpstreamCall } from "./upstream"

// Đ-E14 — Backend-for-Frontend. Token (access + refresh) CHỈ sống ở Next server + Redis; trình duyệt giữ một cookie
// `__Host-sid` HttpOnly chứa session ID ngẫu nhiên. Mọi hàm ở đây nhận `Request` chuẩn và trả `Response` chuẩn — route
// handler trong app/bff/** chỉ gọi chúng, và test gọi thẳng không cần dựng Next.

export type BffDeps = Readonly<{ config: BffConfig; store: SessionStore }>

const forbidden = () =>
  problem(403, "Bị từ chối", "Yêu cầu không đến từ trang của ứng dụng.")
const unavailable = () =>
  problem(
    502,
    "Đã xảy ra lỗi không mong muốn",
    "Không kết nối được máy chủ API."
  )
// `type` riêng (Q-E4): cùng 503 với feed quá tải của API, nhưng người dùng làm việc khác — trình duyệt tách bằng `type`.
const sessionUnavailable = () =>
  problem(
    503,
    "Dịch vụ phiên đăng nhập tạm thời không sẵn sàng",
    undefined,
    {},
    BFF_PROBLEM_TYPES.sessionUnavailable
  )
const unauthenticated = () =>
  problem(401, "Chưa xác thực", "Phiên đăng nhập không còn hiệu lực.", {
    "Set-Cookie": clearSessionCookie(),
  })

/** Lỗi hạ tầng (Redis, API không tới được) → Problem Details; không để exception rơi thành trang lỗi HTML của Next. */
async function guarded(run: () => Promise<Response>): Promise<Response> {
  try {
    return await run()
  } catch (e) {
    if ((e as Error).name === "UpstreamUnavailableError") return unavailable()
    return sessionUnavailable()
  }
}

// ── Đăng ký, xác minh: không có token, chỉ chuyển tiếp ──────────────────────────────────────────────────────────────

export function handlePublicAuth(
  request: Request,
  deps: BffDeps,
  path: "/auth/register" | "/auth/verify-email"
) {
  return guarded(async () => {
    if (!isSameOriginRequest(request, deps.config.appOrigin)) return forbidden()
    const body = await readJsonBody(request)
    if (body instanceof Response) return body
    const upstream = await callUpstream(deps.config.apiUrl, {
      method: "POST",
      path,
      body,
      contentType: "application/json",
      clientIp: clientIp(request, deps.config.trustedProxyHops),
    })
    return relay(upstream)
  })
}

// ── Đăng nhập ────────────────────────────────────────────────────────────────────────────────────────────────────

export function handleLogin(request: Request, deps: BffDeps) {
  return guarded(async () => {
    if (!isSameOriginRequest(request, deps.config.appOrigin)) return forbidden()
    const body = await readJsonBody(request)
    if (body instanceof Response) return body

    const upstream = await callUpstream(deps.config.apiUrl, {
      method: "POST",
      path: "/auth/login",
      body,
      contentType: "application/json",
      clientIp: clientIp(request, deps.config.trustedProxyHops),
    })
    // 400/401/403/423/429/500: chuyển nguyên Problem Details — FE vẫn dịch theo status (Đ-E6), AC-02 giữ nguyên.
    if (!upstream.ok) return relay(upstream)

    const tokens = (await upstream.json()) as Partial<TokenResponse>
    const refresh = readUpstreamRefreshCookie(upstream)
    if (typeof tokens.accessToken !== "string" || !refresh) return unavailable()

    // Chống session fixation: luôn cấp ID MỚI, và bỏ phiên cũ nếu trình duyệt còn mang cookie cũ.
    const previous = readSessionId(request)
    if (previous) await deps.store.delete(previous)

    const sid = newSessionId()
    await deps.store.set(
      sid,
      { accessToken: tokens.accessToken, refreshToken: refresh.token },
      refresh.maxAgeSeconds
    )
    // 204 KHÔNG body: access token không bao giờ tới trình duyệt.
    return new Response(null, {
      status: 204,
      headers: {
        ...NO_STORE,
        "Set-Cookie": sessionCookie(sid, refresh.maxAgeSeconds),
      },
    })
  })
}

// ── Trạng thái phiên (guard khởi động) ───────────────────────────────────────────────────────────────────────────

export function handleSession(request: Request, deps: BffDeps) {
  return guarded(async () => {
    const sid = readSessionId(request)
    const session = sid ? await deps.store.get(sid) : null
    const state: BffSessionState = { authenticated: session !== null }
    const headers: Record<string, string> = {
      ...NO_STORE,
      "Content-Type": "application/json",
    }
    // Có cookie mà không còn phiên (hết hạn, đăng xuất ở tab khác, sai khóa): xóa cookie cho gọn.
    if (!session && request.headers.get("cookie")?.includes("__Host-sid="))
      headers["Set-Cookie"] = clearSessionCookie()
    return new Response(JSON.stringify(state), { status: 200, headers })
  })
}

// ── Refresh single-flight ────────────────────────────────────────────────────────────────────────────────────────

type RefreshOutcome =
  | { kind: "ok"; session: StoredSession }
  | { kind: "expired" }
  | { kind: "failed"; response: Response }

/**
 * Một lần refresh cho mọi request đang cầm cùng access token hỏng — trong một tab, nhiều tab, hay nhiều instance Next.
 * `stale` là token request vừa nhận 401 đã dùng: trong lúc chờ khóa mà phiên đã có token KHÁC thì dùng luôn, không gọi
 * refresh lần nữa (không thì tự kích hoạt reuse detection của API — Mục 7.3).
 */
async function refreshSession(
  deps: BffDeps,
  sid: string,
  stale: string,
  ip: string | null
): Promise<RefreshOutcome> {
  return deps.store.withLock(sid, async () => {
    const current = await deps.store.get(sid)
    if (!current) return { kind: "expired" } as const
    if (current.accessToken !== stale)
      return { kind: "ok", session: current } as const

    const upstream = await callUpstream(deps.config.apiUrl, {
      method: "POST",
      path: "/auth/refresh",
      refreshToken: current.refreshToken,
      clientIp: ip,
    })
    if (upstream.status === 401) {
      await deps.store.delete(sid)
      return { kind: "expired" } as const
    }
    if (!upstream.ok) {
      // 429/500: không kết luận là hết phiên — giữ phiên, trả lỗi cho request gốc.
      return { kind: "failed", response: await relay(upstream) } as const
    }
    const tokens = (await upstream.json()) as Partial<TokenResponse>
    const refresh = readUpstreamRefreshCookie(upstream)
    if (typeof tokens.accessToken !== "string" || !refresh)
      return { kind: "failed", response: unavailable() } as const
    const next = {
      accessToken: tokens.accessToken,
      refreshToken: refresh.token,
    }
    await deps.store.set(sid, next, refresh.maxAgeSeconds)
    return { kind: "ok", session: next } as const
  })
}

/** Gọi API bằng token của phiên; 401 → refresh (single-flight) → gọi lại ĐÚNG MỘT lần. */
async function withSession(
  deps: BffDeps,
  sid: string,
  session: StoredSession,
  ip: string | null,
  call: (session: StoredSession) => Omit<UpstreamCall, "clientIp">
): Promise<Response | "expired"> {
  const send = (s: StoredSession) =>
    callUpstream(deps.config.apiUrl, { ...call(s), clientIp: ip })

  const first = await send(session)
  if (first.status !== 401) return first

  const outcome = await refreshSession(deps, sid, session.accessToken, ip)
  if (outcome.kind === "expired") return "expired"
  if (outcome.kind === "failed") return outcome.response
  // Gọi lại vẫn 401 thì trả 401 — không refresh lần hai (API từ chối cả token mới, vd revoked:user).
  return send(outcome.session)
}

// ── Đăng xuất ────────────────────────────────────────────────────────────────────────────────────────────────────

export function handleLogout(request: Request, deps: BffDeps) {
  return guarded(async () => {
    if (!isSameOriginRequest(request, deps.config.appOrigin)) return forbidden()
    const sid = readSessionId(request)
    if (sid) {
      const session = await deps.store.get(sid).catch(() => null)
      if (session) {
        // API thu hồi refresh family + access token (Đ-D6). Lỗi không giữ người dùng lại trong phiên.
        await withSession(
          deps,
          sid,
          session,
          clientIp(request, deps.config.trustedProxyHops),
          (s) => ({
            method: "POST",
            path: "/auth/logout",
            bearer: s.accessToken,
            refreshToken: s.refreshToken,
          })
        ).catch(() => undefined)
      }
      // Redis lỗi lúc xóa: phiên tự hết theo TTL, refresh token đã bị API thu hồi ở trên — vẫn xóa cookie phía trình duyệt.
      await deps.store.delete(sid).catch(() => undefined)
    }
    return new Response(null, {
      status: 204,
      headers: { ...NO_STORE, "Set-Cookie": clearSessionCookie() },
    })
  })
}

// ── Proxy chung: /bff/api/<path> → API /api/v1/<path> ───────────────────────────────────────────────────────────

const MAX_PROXY_BODY = 1024 * 1024

export function handleProxy(
  request: Request,
  deps: BffDeps,
  segments: string[]
) {
  return guarded(async () => {
    // Nhóm auth của API KHÔNG đi qua proxy chung: /auth/login, /auth/refresh trả token trong body — chuyển nguyên ra
    // trình duyệt là phá toàn bộ BFF. Mỗi endpoint auth có route riêng ở trên.
    if (
      segments.length === 0 ||
      segments[0].toLowerCase() === "auth" ||
      segments.some(
        (s) => s === "" || s === "." || s === ".." || /[/\\]/.test(s)
      )
    )
      return problem(404, "Không tìm thấy")

    if (!isSameOriginRequest(request, deps.config.appOrigin)) return forbidden()

    const sid = readSessionId(request)
    const session = sid ? await deps.store.get(sid) : null
    if (!sid || !session) return unauthenticated()

    let body: ArrayBuffer | null = null
    if (request.method !== "GET" && request.method !== "HEAD") {
      if (Number(request.headers.get("content-length") ?? "0") > MAX_PROXY_BODY)
        return problem(413, "Dữ liệu quá lớn")
      body = await request.arrayBuffer()
      if (body.byteLength > MAX_PROXY_BODY)
        return problem(413, "Dữ liệu quá lớn")
      if (body.byteLength === 0) body = null
    }

    const path = "/" + segments.map(encodeURIComponent).join("/")
    const result = await withSession(
      deps,
      sid,
      session,
      clientIp(request, deps.config.trustedProxyHops),
      (s) => ({
        method: request.method,
        path,
        search: new URL(request.url).search,
        body,
        contentType: body ? request.headers.get("content-type") : null,
        bearer: s.accessToken,
      })
    )
    if (result === "expired") return unauthenticated()
    return relay(result)
  })
}
