import { http, HttpResponse } from "msw"
import { beforeEach, describe, expect, it } from "vitest"

import { SessionExpiredError } from "@/lib/auth/refresh-coordinator"
// Nạp session.ts = `configureRefresh(coordinator.getFreshToken)` như providers.tsx làm trong app.
import "@/lib/auth/session"
import { tokenStore } from "@/lib/auth/token-store"
import { server } from "@/mocks/node"
import { fakeSession, mockControls } from "@/mocks/session"

import { authApi } from "./auth-api"
import { API_BASE_URL } from "./config"
import { ApiError } from "./problem"

const PROBLEM = { "Content-Type": "application/problem+json" }
const problem = (status: number) =>
  HttpResponse.json(
    { type: "t", title: "t", status },
    { status, headers: PROBLEM }
  )

/** Mọi request đi ra, dạng `METHOD /path Bearer…`, để đếm refresh và xem request gọi lại mang token nào. */
function recordRequests() {
  const seen: { path: string; auth: string | null }[] = []
  server.events.on("request:start", ({ request }) => {
    seen.push({
      path: `${request.method} ${new URL(request.url).pathname}`,
      auth: request.headers.get("Authorization"),
    })
  })
  return {
    refreshes: () =>
      seen.filter((r) => r.path === "POST /api/v1/auth/refresh").length,
    all: seen,
  }
}

beforeEach(() => {
  tokenStore.reset()
})

describe("request() — nhánh 401 → refresh → gọi lại (E7)", () => {
  it("3 lời gọi /me đồng thời với token hết hạn → ĐÚNG 1 refresh; cả 3 gọi lại với token mới và resolve", async () => {
    tokenStore.startSession(fakeSession.start())
    const old = tokenStore.get()
    mockControls.expireAccessToken()
    const rec = recordRequests()

    const results = await Promise.all([
      authApi.me(),
      authApi.me(),
      authApi.me(),
    ])

    expect(results).toHaveLength(3)
    expect(rec.refreshes()).toBe(1)
    const fresh = tokenStore.get()
    expect(fresh).not.toBe(old)
    const meCalls = rec.all.filter((r) => r.path === "GET /api/v1/me")
    expect(meCalls).toHaveLength(6)
    expect(meCalls.filter((r) => r.auth === `Bearer ${old}`)).toHaveLength(3)
    expect(meCalls.filter((r) => r.auth === `Bearer ${fresh}`)).toHaveLength(3)
  })

  it("401 về MUỘN, sau khi request khác đã refresh xong → dùng token mới, KHÔNG refresh lần hai (bẫy 5: stale chụp trước fetch)", async () => {
    tokenStore.startSession(fakeSession.start())
    const old = tokenStore.get()
    mockControls.expireAccessToken()

    // Request /me đầu tiên (token cũ) bị giữ lại tới khi request thứ hai đã refresh + gọi lại xong.
    let release!: () => void
    const gate = new Promise<void>((r) => (release = r))
    let first = true
    server.use(
      http.get(`${API_BASE_URL}/me`, async ({ request }) => {
        if (first) {
          first = false
          await gate
        }
        return request.headers.get("Authorization") ===
          `Bearer ${fakeSession.current()}`
          ? HttpResponse.json({})
          : problem(401)
      })
    )
    const rec = recordRequests()

    const slow = authApi.me()
    await authApi.me() // 401 → refresh → gọi lại 200
    expect(rec.refreshes()).toBe(1)
    const fresh = tokenStore.get()
    expect(fresh).not.toBe(old)

    release() // giờ request đầu mới nhận 401 cho token CŨ
    await expect(slow).resolves.toBeDefined()
    expect(rec.refreshes()).toBe(1)
    expect(tokenStore.get()).toBe(fresh)
  })

  it("refresh 401 → cả 3 reject SessionExpiredError; token null, anonymous; ĐÚNG 1 refresh (không thử lại)", async () => {
    // Store còn token cũ nhưng server đã hết phiên (cookie hết hạn / bị thu hồi).
    tokenStore.startSession("token-cu")
    const rec = recordRequests()

    const results = await Promise.allSettled([
      authApi.me(),
      authApi.me(),
      authApi.me(),
    ])

    for (const r of results) {
      expect(r.status).toBe("rejected")
      expect((r as PromiseRejectedResult).reason).toBeInstanceOf(
        SessionExpiredError
      )
    }
    expect(tokenStore.getSession()).toMatchObject({
      token: null,
      status: "anonymous",
      endedBy: "expired",
    })
    expect(rec.refreshes()).toBe(1)
  })

  it("refresh 500 → request gốc reject ApiError(500); token KHÔNG bị xóa", async () => {
    tokenStore.startSession(fakeSession.start())
    const old = tokenStore.get()
    mockControls.expireAccessToken()
    server.use(http.post(`${API_BASE_URL}/auth/refresh`, () => problem(500)))

    const error = await authApi.me().catch((e: unknown) => e)

    expect(error).toBeInstanceOf(ApiError)
    expect((error as ApiError).status).toBe(500)
    expect(tokenStore.getSession()).toMatchObject({
      token: old,
      status: "authenticated",
    })
  })

  it("/me 401, refresh 200, gọi lại VẪN 401 → reject ApiError(401); tổng 1 refresh (không vòng lặp)", async () => {
    tokenStore.startSession(fakeSession.start())
    // Từ chối 5 lần rồi mới cho qua: code đúng dừng ở lần 2; code lặp vô hạn thì tới lần 6 resolve và test ĐỎ gọn —
    // thay vì treo cả tiến trình Vitest (đã gặp khi thử đột biến bỏ `_retried`).
    let meCalls = 0
    server.use(
      http.get(`${API_BASE_URL}/me`, () =>
        ++meCalls <= 5 ? problem(401) : HttpResponse.json({})
      )
    )
    const rec = recordRequests()

    const error = await authApi.me().catch((e: unknown) => e)

    expect(error).toBeInstanceOf(ApiError)
    expect((error as ApiError).status).toBe(401)
    expect(rec.refreshes()).toBe(1)
    expect(rec.all.filter((r) => r.path === "GET /api/v1/me")).toHaveLength(2)
  })

  it("authApi.login 401 → 0 refresh (lỗi nghiệp vụ, không phải hết phiên)", async () => {
    tokenStore.startSession(fakeSession.start())
    const rec = recordRequests()

    await expect(
      authApi.login({ email: "sai@example.com", password: "x" })
    ).rejects.toMatchObject({ status: 401 })
    expect(rec.refreshes()).toBe(0)
  })

  it("403 → 0 refresh; lỗi trả thẳng", async () => {
    tokenStore.startSession(fakeSession.start())
    server.use(http.get(`${API_BASE_URL}/me`, () => problem(403)))
    const rec = recordRequests()

    await expect(authApi.me()).rejects.toMatchObject({ status: 403 })
    expect(rec.refreshes()).toBe(0)
  })

  it("logout với token hết hạn → refresh rồi gọi lại logout bằng token mới (204)", async () => {
    tokenStore.startSession(fakeSession.start())
    mockControls.expireAccessToken()
    let logoutAuth: (string | null)[] = []
    server.use(
      http.post(`${API_BASE_URL}/auth/logout`, ({ request }) => {
        const auth = request.headers.get("Authorization")
        logoutAuth = [...logoutAuth, auth]
        return auth === `Bearer ${fakeSession.current()}`
          ? new HttpResponse(null, { status: 204 })
          : problem(401)
      })
    )
    const rec = recordRequests()

    await expect(authApi.logout()).resolves.toBeUndefined()
    expect(rec.refreshes()).toBe(1)
    expect(logoutAuth).toHaveLength(2)
    expect(logoutAuth[1]).toBe(`Bearer ${tokenStore.get()}`)
  })
})
