import { http, HttpResponse } from "msw"
import { afterEach, describe, expect, it, vi } from "vitest"

import { postId, userId } from "@/mocks/fixtures"
import { server } from "@/mocks/node"
import { fakeSession } from "@/mocks/session"

import { authApi } from "./auth-api"
import { BFF_URL } from "./config"
import { contentApi } from "./content-api"
import { configureSessionExpired } from "./http"
import { NetworkError } from "./problem"
import { profileApi } from "./profile-api"

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

describe("request() — bốn method của GĐ2 (E1)", () => {
  it("PUT / PATCH / DELETE đi đúng method, đúng đường, vẫn qua /bff cùng origin", async () => {
    const seen = recordRequests()

    await profileApi.upsert({ displayName: "An Nguyễn", bio: null })
    await profileApi.setAvatar({ mediaKey: `avatars/${userId}/anh.jpg` })
    await profileApi.removeAvatar()
    await contentApi.updatePost(postId, { privacy: "private" })
    await contentApi.deletePost(postId)

    expect(seen.map((r) => `${r.method} ${new URL(r.url).pathname}`)).toEqual([
      "PUT /bff/api/users/me/profile",
      "PUT /bff/api/users/me/avatar",
      "DELETE /bff/api/users/me/avatar",
      `PATCH /bff/api/posts/${postId}`,
      `DELETE /bff/api/posts/${postId}`,
    ])
    for (const r of seen) {
      expect(r.credentials).toBe("same-origin")
      expect(r.headers.get("authorization")).toBeNull()
    }
  })

  it("204 trả undefined — DELETE không cố đọc JSON của body rỗng", async () => {
    await expect(profileApi.removeAvatar()).resolves.toBeUndefined()
    await expect(contentApi.deletePost(postId)).resolves.toBeUndefined()
  })

  it("DELETE không gửi body và không gửi Content-Type", async () => {
    const seen = recordRequests()

    await contentApi.deletePost(postId)

    expect(seen[0].headers.get("content-type")).toBeNull()
    expect(await seen[0].text()).toBe("")
  })

  it("401 trên endpoint GĐ2 vẫn là phiên hết hạn — proxy chung, cùng một nhánh", async () => {
    const expired = vi.fn()
    configureSessionExpired(expired)
    server.use(
      http.get(`${BFF_URL}/api/posts/:postId`, () =>
        HttpResponse.json(
          { type: "t", title: "t", status: 401 },
          {
            status: 401,
            headers: { "Content-Type": "application/problem+json" },
          }
        )
      )
    )

    await expect(contentApi.getPost(postId)).rejects.toMatchObject({
      status: 401,
    })
    expect(expired).toHaveBeenCalledTimes(1)
  })
})

describe("api client — dựng đường đi (E1)", () => {
  it("userId / postId luôn qua encodeURIComponent: segment chứa `/` không tự tách thành đường khác", async () => {
    const seen = recordRequests()

    await contentApi.getPost("a/b").catch(() => {})

    // `%2F` chứ không phải `/`: proxy chung từ chối segment lạ thay vì đọc nhầm thành /posts/a/b.
    expect(new URL(seen[0].url).pathname).toBe("/bff/api/posts/a%2Fb")
  })

  it("cursor và limit chỉ vào query string KHI CÓ — trang đầu không có `?`", async () => {
    const seen = recordRequests()

    await contentApi.listUserPosts(userId)
    await contentApi.listUserPosts(userId, { cursor: null })
    await contentApi.listUserPosts(userId, { cursor: "Y3Vyc29y", limit: 5 })

    expect(seen.map((r) => new URL(r.url).search)).toEqual([
      "",
      "",
      "?cursor=Y3Vyc29y&limit=5",
    ])
  })

  it("nextCursor được truyền lại NGUYÊN VẸN — FE không tự dựng, không tự sửa (Đ-2.11)", async () => {
    const seen = recordRequests()
    const cursor = "MjAyNi0wOS0yMFQwMjoxMDoyMlp8MDE5MmYzYzE="

    await contentApi.listUserPosts(userId, { cursor })

    expect(new URL(seen[0].url).searchParams.get("cursor")).toBe(cursor)
  })

  it("presign là MỘT request cho cả lô, ticket về cùng thứ tự với files (Đ-2.15)", async () => {
    const seen = recordRequests()

    const tickets = await contentApi.createUploads({
      purpose: "post",
      files: [
        { contentType: "image/jpeg", sizeBytes: 1048576 },
        { contentType: "image/png", sizeBytes: 204800 },
      ],
    })

    expect(seen).toHaveLength(1)
    expect(tickets).toHaveLength(2)
    expect(tickets.map((t) => t.requiredHeaders["Content-Type"])).toEqual([
      "image/jpeg",
      "image/png",
    ])
  })
})
