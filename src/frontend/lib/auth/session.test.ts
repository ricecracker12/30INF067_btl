import { http, HttpResponse } from "msw"
import { beforeEach, describe, expect, it } from "vitest"

import { authApi } from "@/lib/api/auth-api"
import { BFF_URL } from "@/lib/api/config"
import { server } from "@/mocks/node"
import { fakeSession } from "@/mocks/session"

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

describe("bootstrapSession (E6 bước 1, qua BFF — Đ-E14)", () => {
  it("BFF còn phiên → authenticated; gọi 2 lần đồng thời vẫn 1 request", async () => {
    fakeSession.start()
    const seen = recordRequests()

    await Promise.all([bootstrapSession(), bootstrapSession()])

    expect(tokenStore.getSession().status).toBe("authenticated")
    expect(seen).toEqual(["GET /bff/auth/session"])
  })

  it("BFF không có phiên → anonymous, do hết hạn (guard sẽ kèm next)", async () => {
    await bootstrapSession()
    expect(tokenStore.getSession()).toEqual({
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
      "503 (Redis chết)",
      () =>
        HttpResponse.json(
          { type: "t", title: "t", status: 503 },
          { status: 503, headers: PROBLEM }
        ),
    ],
    ["mất mạng", () => HttpResponse.error()],
  ])(
    "không hỏi được BFF (%s) → error, KHÔNG anonymous; gọi lại thì qua unknown rồi thử lần nữa",
    async (_, resolver) => {
      server.use(http.get(`${BFF_URL}/auth/session`, resolver, { once: true }))
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
})

describe("401 từ proxy BFF khi đang dùng", () => {
  it("phiên hết hạn ở server → anonymous do hết hạn", async () => {
    fakeSession.start()
    tokenStore.startSession()
    fakeSession.clear()

    await expect(authApi.me()).rejects.toMatchObject({ status: 401 })

    expect(tokenStore.getSession()).toEqual({
      status: "anonymous",
      endedBy: "expired",
    })
  })
})

describe("logout (E6 bước 3)", () => {
  it("gọi POST /bff/auth/logout, rồi anonymous do logout", async () => {
    fakeSession.start()
    tokenStore.startSession()
    const seen = recordRequests()

    await logout()

    expect(seen).toEqual(["POST /bff/auth/logout"])
    expect(fakeSession.current()).toBeNull()
    expect(tokenStore.getSession()).toEqual({
      status: "anonymous",
      endedBy: "logout",
    })
  })

  it("BFF lỗi hoặc mất mạng: vẫn kết thúc phiên phía trình duyệt, không ném", async () => {
    tokenStore.startSession()
    server.use(http.post(`${BFF_URL}/auth/logout`, () => HttpResponse.error()))

    await expect(logout()).resolves.toBeUndefined()
    expect(tokenStore.getSession()).toMatchObject({
      status: "anonymous",
      endedBy: "logout",
    })
  })

  it("báo các tab khác qua BroadcastChannel('socialapp:auth')", async () => {
    tokenStore.startSession()
    // Một "tab khác": kênh cùng tên trong cùng tiến trình nhận được tin, như hai tab cùng origin.
    const otherTab = new BroadcastChannel("socialapp:auth")
    const received = new Promise<unknown>((resolve) => {
      otherTab.onmessage = (e) => resolve(e.data)
    })

    await logout()

    await expect(received).resolves.toEqual({ type: "logout" })
    otherTab.close()
  })

  it("nhận tin logout từ tab khác → phiên ở tab này kết thúc", async () => {
    tokenStore.startSession()
    const otherTab = new BroadcastChannel("socialapp:auth")

    otherTab.postMessage({ type: "logout" })
    await new Promise((r) => setTimeout(r, 20))

    expect(tokenStore.getSession().status).toBe("anonymous")
    otherTab.close()
  })
})
