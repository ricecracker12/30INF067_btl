import { http, HttpResponse } from "msw"
import { beforeEach, describe, expect, it } from "vitest"

import { API_BASE_URL } from "@/lib/api/config"
import { fakeSession } from "@/mocks/session"
import { server } from "@/mocks/node"

import { bootstrapSession, logout } from "./session"
import { tokenStore } from "./token-store"

const PROBLEM = { "Content-Type": "application/problem+json" }

function recordRequests() {
  const seen: string[] = []
  server.events.on("request:start", ({ request }) => {
    seen.push(`${request.method} ${new URL(request.url).pathname}`)
  })
  return seen
}

beforeEach(() => {
  tokenStore.reset()
})

describe("bootstrapSession (E6 bước 1)", () => {
  it("còn cookie (refresh 200) → authenticated với token mới; gọi 2 lần đồng thời vẫn 1 request", async () => {
    fakeSession.start()
    const seen = recordRequests()

    await Promise.all([bootstrapSession(), bootstrapSession()])

    expect(tokenStore.getSession()).toMatchObject({
      status: "authenticated",
      token: expect.stringMatching(/^mock-access-token-/),
    })
    expect(seen).toEqual(["POST /api/v1/auth/refresh"])
  })

  it("không còn phiên (refresh 401) → anonymous, do hết hạn (guard sẽ kèm next)", async () => {
    await bootstrapSession()
    expect(tokenStore.getSession()).toEqual({
      token: null,
      status: "anonymous",
      endedBy: "expired",
    })
  })

  it.each([
    [
      "500",
      () =>
        HttpResponse.json(
          { type: "t", title: "t", status: 500 },
          { status: 500, headers: PROBLEM }
        ),
    ],
    [
      "429",
      () =>
        HttpResponse.json(
          { type: "t", title: "t", status: 429 },
          { status: 429, headers: PROBLEM }
        ),
    ],
    ["mất mạng", () => HttpResponse.error()],
  ])(
    "refresh %s → error, KHÔNG anonymous; gọi lại thì qua unknown rồi thử lần nữa",
    async (_, resolver) => {
      server.use(
        http.post(`${API_BASE_URL}/auth/refresh`, resolver, { once: true })
      )
      await bootstrapSession()
      expect(tokenStore.getSession().status).toBe("error")

      fakeSession.start()
      const statuses: string[] = []
      const off = tokenStore.subscribe(() =>
        statuses.push(tokenStore.getSession().status)
      )
      await bootstrapSession()
      off()
      expect(statuses).toEqual(["unknown", "authenticated"])
    }
  )

  it("đã đăng nhập sẵn (vd. vừa login rồi vào /me) → không gọi refresh", async () => {
    tokenStore.startSession("vua-dang-nhap")
    const seen = recordRequests()

    await bootstrapSession()

    expect(seen).toEqual([])
    expect(tokenStore.get()).toBe("vua-dang-nhap")
  })
})

describe("logout (E6 bước 3)", () => {
  it("gọi POST /auth/logout kèm bearer, rồi anonymous do logout", async () => {
    tokenStore.startSession(fakeSession.start())
    const token = tokenStore.get()
    let auth: string | null = null
    server.events.on("request:start", ({ request }) => {
      auth = request.headers.get("Authorization")
    })

    await logout()

    expect(auth).toBe(`Bearer ${token}`)
    expect(tokenStore.getSession()).toEqual({
      token: null,
      status: "anonymous",
      endedBy: "logout",
    })
  })

  it("server lỗi hoặc mất mạng: vẫn xóa phiên khỏi memory, không ném", async () => {
    tokenStore.startSession("t")
    server.use(
      http.post(`${API_BASE_URL}/auth/logout`, () => HttpResponse.error())
    )

    await expect(logout()).resolves.toBeUndefined()
    expect(tokenStore.getSession()).toMatchObject({
      status: "anonymous",
      endedBy: "logout",
    })
  })

  it("báo các tab khác qua BroadcastChannel('socialapp:auth') — E7", async () => {
    tokenStore.startSession(fakeSession.start())
    // Một "tab khác": kênh cùng tên trong cùng tiến trình nhận được tin, như hai tab cùng origin.
    const otherTab = new BroadcastChannel("socialapp:auth")
    const received = new Promise<unknown>((resolve) => {
      otherTab.onmessage = (e) => resolve(e.data)
    })

    await logout()

    await expect(received).resolves.toEqual({ type: "logout" })
    otherTab.close()
  })
})
