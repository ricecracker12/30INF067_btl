import { http, HttpResponse } from "msw"
import { beforeEach, describe, expect, it } from "vitest"

import { BFF_URL } from "@/lib/api/config"
import { ApiError, NetworkError } from "@/lib/api/problem"
import { server } from "@/mocks/node"

import { resetVerifyOnce, verifyOnce } from "./verify-once"

const TOKEN = "c".repeat(64)

function countRequests() {
  const counter = { n: 0 }
  server.events.on("request:start", () => {
    counter.n += 1
  })
  return counter
}

beforeEach(() => {
  resetVerifyOnce()
})

describe("verifyOnce", () => {
  it("gọi hai lần cùng token (StrictMode) → 1 request, cùng một promise", async () => {
    const counter = countRequests()
    const a = verifyOnce(TOKEN)
    const b = verifyOnce(TOKEN)
    expect(b).toBe(a)
    await a
    // Gọi lại SAU khi xong cũng không gửi nữa: token đã tiêu thụ, gửi lại là 410.
    await verifyOnce(TOKEN)
    expect(counter.n).toBe(1)
  })

  it("token khác nhau → request riêng", async () => {
    const counter = countRequests()
    await Promise.all([verifyOnce(TOKEN), verifyOnce("d".repeat(64))])
    expect(counter.n).toBe(2)
  })

  it("410 là kết quả cuối: gọi lại nhận lại đúng lỗi đó, không gửi request mới", async () => {
    const counter = countRequests()
    const expired = "a".repeat(64)
    await expect(verifyOnce(expired)).rejects.toMatchObject({ status: 410 })
    await expect(verifyOnce(expired)).rejects.toBeInstanceOf(ApiError)
    expect(counter.n).toBe(1)
  })

  it("mất mạng là lỗi tạm thời: lần gọi sau gửi request mới", async () => {
    server.use(
      http.post(`${BFF_URL}/auth/verify-email`, () => HttpResponse.error(), {
        once: true,
      })
    )
    const counter = countRequests()
    await expect(verifyOnce(TOKEN)).rejects.toBeInstanceOf(NetworkError)
    await expect(verifyOnce(TOKEN)).resolves.toMatchObject({
      email: expect.any(String),
    })
    expect(counter.n).toBe(2)
  })
})
