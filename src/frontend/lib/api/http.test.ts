import { http, HttpResponse } from "msw"
import { afterEach, beforeEach, describe, expect, it } from "vitest"

import { server } from "@/mocks/node"

import { tokenStore } from "../auth/token-store"
import { authApi } from "./auth-api"
import { API_BASE_URL } from "./config"
import { NetworkError } from "./problem"

/** Ghi lại request thật sự đi ra — thứ duy nhất chứng minh được `credentials` và header. */
function recordRequests() {
  const seen: Request[] = []
  server.events.on("request:start", ({ request }) => {
    seen.push(request.clone())
  })
  return seen
}

beforeEach(() => {
  tokenStore.set(null)
})
afterEach(() => {
  tokenStore.set(null)
})

describe("request()", () => {
  it("gắn credentials: 'include' ở CẢ SÁU lời gọi — thiếu là cookie refresh im lặng không đi", async () => {
    const seen = recordRequests()
    tokenStore.set("token-cua-phien")

    await authApi.register({ email: "a@example.com", password: "MatKhau123" })
    await authApi.verifyEmail({ token: "c".repeat(64) })
    await authApi.login({ email: "a@example.com", password: "MatKhau123" })
    await authApi.refresh().catch(() => null)
    await authApi.me().catch(() => null)
    await authApi.logout()

    expect(seen).toHaveLength(6)
    expect(seen.map((r) => r.credentials)).toEqual(Array(6).fill("include"))
  })

  it("refresh KHÔNG gửi body và KHÔNG gửi Content-Type (hợp đồng: không nhận body)", async () => {
    const seen = recordRequests()

    await authApi.login({ email: "a@example.com", password: "MatKhau123" })
    await authApi.refresh()

    const refreshReq = seen[1]
    expect(refreshReq.method).toBe("POST")
    expect(refreshReq.headers.get("content-type")).toBeNull()
    expect(await refreshReq.text()).toBe("")
  })

  it("logout KHÔNG gửi body, nhưng VẪN gắn bearer", async () => {
    const seen = recordRequests()
    tokenStore.set("token-cua-phien")

    await authApi.logout()

    expect(seen[0].headers.get("content-type")).toBeNull()
    expect(seen[0].headers.get("authorization")).toBe("Bearer token-cua-phien")
    expect(await seen[0].text()).toBe("")
  })

  it("register/login/verifyEmail KHÔNG gắn Authorization dù store đang có token", async () => {
    const seen = recordRequests()
    tokenStore.set("token-cua-phien")

    await authApi.register({ email: "a@example.com", password: "MatKhau123" })
    await authApi.login({ email: "a@example.com", password: "MatKhau123" })
    await authApi.verifyEmail({ token: "c".repeat(64) })

    expect(seen.map((r) => r.headers.get("authorization"))).toEqual([
      null,
      null,
      null,
    ])
  })

  it("me gắn Bearer <token> hiện tại của store", async () => {
    const seen = recordRequests()
    const { accessToken } = await authApi.login({
      email: "a@example.com",
      password: "MatKhau123",
    })
    tokenStore.set(accessToken)

    const me = await authApi.me()

    expect(seen[1].headers.get("authorization")).toBe(`Bearer ${accessToken}`)
    expect(me.roleDisplayName).toBe("Người dùng")
  })

  it("204 trả về undefined, không cố parse JSON", async () => {
    await authApi.login({ email: "a@example.com", password: "MatKhau123" })
    await expect(authApi.logout()).resolves.toBeUndefined()
  })

  it("fetch ném (mất mạng, CORS sai, API tắt) → NetworkError", async () => {
    server.use(http.get(`${API_BASE_URL}/me`, () => HttpResponse.error()))

    await expect(authApi.me()).rejects.toBeInstanceOf(NetworkError)
  })

  it("abort → ném lại AbortError nguyên vẹn, KHÔNG bọc thành NetworkError", async () => {
    const controller = new AbortController()
    server.use(
      http.get(`${API_BASE_URL}/me`, async () => {
        controller.abort()
        return HttpResponse.json({})
      })
    )

    const err = await authApi.me(controller.signal).catch((e: Error) => e)

    expect(err).toBeInstanceOf(Error)
    expect((err as Error).name).toBe("AbortError")
    expect(err).not.toBeInstanceOf(NetworkError)
  })
})
