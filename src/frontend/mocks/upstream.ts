import { http, HttpResponse } from "msw"

import type * as T from "@/lib/api/types"

import { me, problem, registerResponse, verifyEmailResponse } from "./fixtures"

// API .NET GIẢ cho test của lib/bff (Đ-E14): BFF ở server gọi nó như gọi API thật. Mô phỏng đúng những gì BFF phụ thuộc —
// token trong body, refresh token trong Set-Cookie, xoay vòng refresh (token cũ dùng lại → 401), thu hồi khi logout.
// Không kiểm hợp đồng: phần đó là IdentityContractTests phía backend.

export const UPSTREAM_API = "http://api.test/api/v1"

const PROBLEM_HEADERS = { "Content-Type": "application/problem+json" }
const REFRESH_MAX_AGE = 604_800

type Recorded = { method: string; path: string; headers: Headers; body: string }

const state = {
  seq: 0,
  validAccess: new Set<string>(),
  validRefresh: new Set<string>(),
  calls: [] as Recorded[],
  /** Status ép cho /auth/refresh (429, 500…) — null là chạy bình thường. */
  refreshFailure: null as number | null,
  /** Độ trễ /auth/refresh (ms) — để nhiều request thật sự chồng lên nhau. */
  refreshDelayMs: 0,
}

const unauthorized = () =>
  HttpResponse.json(problem(401, "Chưa xác thực"), {
    status: 401,
    headers: PROBLEM_HEADERS,
  })

function issue() {
  state.seq += 1
  const access = `acc-${state.seq}`
  const refresh = `ref-${state.seq}`
  state.validAccess.add(access)
  state.validRefresh.add(refresh)
  return { access, refresh }
}

function tokenResponse(access: string, refresh: string) {
  return HttpResponse.json(
    { accessToken: access, expiresIn: 900 } satisfies T.TokenResponse,
    {
      headers: {
        "Set-Cookie": `refresh_token=${refresh}; Max-Age=${REFRESH_MAX_AGE}; Path=/api/v1/auth; Secure; HttpOnly; SameSite=Lax`,
        "X-Correlation-ID": "corr-from-api",
      },
    }
  )
}

const bearer = (request: Request) =>
  request.headers.get("authorization")?.replace(/^Bearer /, "") ?? ""
const refreshCookie = (request: Request) =>
  /(?:^|;\s*)refresh_token=([^;]+)/.exec(
    request.headers.get("cookie") ?? ""
  )?.[1] ?? ""

async function record(request: Request) {
  state.calls.push({
    method: request.method,
    path: new URL(request.url).pathname.replace("/api/v1", ""),
    headers: new Headers(request.headers),
    body: await request.clone().text(),
  })
}

const u = (path: string) => `${UPSTREAM_API}${path}`

export const upstreamHandlers = [
  http.post(u("/auth/login"), async ({ request }) => {
    await record(request)
    const body = JSON.parse((await request.text()) || "{}") as T.LoginRequest
    if (body.email === "sai@example.com") {
      return HttpResponse.json(
        problem(401, "Xác thực thất bại", "Email hoặc mật khẩu không đúng."),
        {
          status: 401,
          headers: { ...PROBLEM_HEADERS, "X-Correlation-ID": "corr-401" },
        }
      )
    }
    const { access, refresh } = issue()
    return tokenResponse(access, refresh)
  }),

  http.post(u("/auth/refresh"), async ({ request }) => {
    await record(request)
    if (state.refreshDelayMs > 0)
      await new Promise((r) => setTimeout(r, state.refreshDelayMs))
    if (state.refreshFailure !== null) {
      return HttpResponse.json(problem(state.refreshFailure, "Lỗi"), {
        status: state.refreshFailure,
        headers: PROBLEM_HEADERS,
      })
    }
    const presented = refreshCookie(request)
    if (!state.validRefresh.has(presented)) return unauthorized()
    // Xoay vòng: refresh cũ hết hiệu lực — dùng lại lần hai là 401 (API thật: reuse detection).
    state.validRefresh.delete(presented)
    const { access, refresh } = issue()
    return tokenResponse(access, refresh)
  }),

  http.post(u("/auth/logout"), async ({ request }) => {
    await record(request)
    if (!state.validAccess.has(bearer(request))) return unauthorized()
    state.validAccess.delete(bearer(request))
    state.validRefresh.delete(refreshCookie(request))
    return new HttpResponse(null, {
      status: 204,
      headers: { "Set-Cookie": "refresh_token=; Max-Age=0; Path=/api/v1/auth" },
    })
  }),

  http.post(u("/auth/register"), async ({ request }) => {
    await record(request)
    return HttpResponse.json(registerResponse, { status: 201 })
  }),

  http.post(u("/auth/verify-email"), async ({ request }) => {
    await record(request)
    return HttpResponse.json(verifyEmailResponse)
  }),

  http.get(u("/me"), async ({ request }) => {
    await record(request)
    if (!state.validAccess.has(bearer(request))) return unauthorized()
    return HttpResponse.json(me, {
      headers: {
        "X-Correlation-ID": "corr-me",
        // API không bao giờ đặt cookie ở /me — nếu có, BFF cũng không được chuyển ra trình duyệt.
        "Set-Cookie": "leak=1; Path=/",
      },
    })
  }),

  http.post(u("/posts"), async ({ request }) => {
    await record(request)
    if (!state.validAccess.has(bearer(request))) return unauthorized()
    return HttpResponse.json({ ok: true }, { status: 201 })
  }),
]

export const fakeApi = {
  calls: () => state.calls,
  callsTo: (path: string) => state.calls.filter((c) => c.path === path),
  /** Mọi access token đang phát hết hạn — request kế tiếp 401. */
  expireAccessTokens() {
    state.validAccess.clear()
  },
  /** Refresh token bị thu hồi (reuse detection, đăng xuất nơi khác). */
  revokeRefreshTokens() {
    state.validRefresh.clear()
  },
  failRefreshWith(status: number | null) {
    state.refreshFailure = status
  },
  delayRefresh(ms: number) {
    state.refreshDelayMs = ms
  },
  reset() {
    state.seq = 0
    state.validAccess.clear()
    state.validRefresh.clear()
    state.calls = []
    state.refreshFailure = null
    state.refreshDelayMs = 0
  },
}
