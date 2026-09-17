import { http, HttpResponse } from "msw"
import { afterEach, describe, expect, it, vi } from "vitest"

import { server } from "@/mocks/node"
import { fakeSession } from "@/mocks/session"

import { authApi } from "./auth-api"
import { BFF_URL } from "./config"
import { configureSessionExpired } from "./http"
import { NetworkError } from "./problem"

/** Ghi lại request thật sự đi ra — thứ duy nhất chứng minh được đích, `credentials` và header. */
function recordRequests() {
  const seen: Request[] = []
  server.events.on("request:start", ({ request }) => {
    seen.push(request.clone())
  })
  return seen
}

afterEach(() => {
  configureSessionExpired(null)
})

describe("request() — trình duyệt chỉ nói chuyện với BFF (Đ-E14)", () => {
  it("mọi lời gọi đi tới /bff cùng origin, credentials: 'same-origin', KHÔNG có Authorization", async () => {
    const seen = recordRequests()

    await authApi.register({ email: "a@example.com", password: "MatKhau123" })
    await authApi.verifyEmail({ token: "c".repeat(64) })
    await authApi.login({ email: "a@example.com", password: "MatKhau123" })
    await authApi.session()
    await authApi.me()
    await authApi.logout()

    expect(seen).toHaveLength(6)
    for (const r of seen) {
      expect(r.url.startsWith(`${window.location.origin}/bff/`)).toBe(true)
      expect(r.credentials).toBe("same-origin")
      expect(r.headers.get("authorization")).toBeNull()
    }
    expect(seen.map((r) => new URL(r.url).pathname)).toEqual([
      "/bff/auth/register",
      "/bff/auth/verify-email",
      "/bff/auth/login",
      "/bff/auth/session",
      "/bff/api/me",
      "/bff/auth/logout",
    ])
  })

  it("login trả 204 — không có token nào để trình duyệt cầm", async () => {
    await expect(
      authApi.login({ email: "a@example.com", password: "MatKhau123" })
    ).resolves.toBeUndefined()
  })

  it("logout không gửi body và không gửi Content-Type", async () => {
    const seen = recordRequests()

    await authApi.logout()

    expect(seen[0].headers.get("content-type")).toBeNull()
    expect(await seen[0].text()).toBe("")
  })

  it("401 từ proxy /bff/api/* → báo phiên hết hạn đúng một lần; 401 của /bff/auth/login thì KHÔNG", async () => {
    const expired = vi.fn()
    configureSessionExpired(expired)

    await expect(
      authApi.login({ email: "sai@example.com", password: "x" })
    ).rejects.toMatchObject({ status: 401 })
    expect(expired).not.toHaveBeenCalled()

    fakeSession.clear()
    await expect(authApi.me()).rejects.toMatchObject({ status: 401 })
    expect(expired).toHaveBeenCalledTimes(1)
  })

  it("403 từ proxy KHÔNG phải hết phiên", async () => {
    const expired = vi.fn()
    configureSessionExpired(expired)
    server.use(
      http.get(`${BFF_URL}/api/me`, () =>
        HttpResponse.json(
          { type: "t", title: "t", status: 403 },
          {
            status: 403,
            headers: { "Content-Type": "application/problem+json" },
          }
        )
      )
    )

    await expect(authApi.me()).rejects.toMatchObject({ status: 403 })
    expect(expired).not.toHaveBeenCalled()
  })

  it("fetch ném (mất mạng, BFF tắt) → NetworkError", async () => {
    server.use(http.get(`${BFF_URL}/api/me`, () => HttpResponse.error()))

    await expect(authApi.me()).rejects.toBeInstanceOf(NetworkError)
  })

  it("abort → ném lại AbortError nguyên vẹn, KHÔNG bọc thành NetworkError", async () => {
    const controller = new AbortController()
    server.use(
      http.get(`${BFF_URL}/api/me`, async () => {
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
