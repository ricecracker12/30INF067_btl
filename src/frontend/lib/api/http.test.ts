import { http, HttpResponse } from "msw"
import { afterEach, describe, expect, it, vi } from "vitest"

import {
  CURSOR_QUA_TAI,
  CURSOR_TRANG_RONG,
  postId,
  SOCIAL_SCENARIO,
  userId,
  userIdKhac,
} from "@/mocks/fixtures"
import { server } from "@/mocks/node"
import { fakeSession } from "@/mocks/session"

import { authApi } from "./auth-api"
import { BFF_URL } from "./config"
import { contentApi } from "./content-api"
import { configureSessionExpired } from "./http"
import { ApiError, NetworkError } from "./problem"
import { profileApi } from "./profile-api"
import { socialGraphApi } from "./socialgraph-api"

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

describe("api client — quan hệ và bảng tin (GĐ4 E1)", () => {
  it("chín endpoint quan hệ + feed đi đúng method, đúng đường, qua /bff cùng origin, KHÔNG route BFF mới", async () => {
    const seen = recordRequests()
    const id = userIdKhac

    await socialGraphApi.relationship(id)
    await socialGraphApi.sendRequest(id)
    await socialGraphApi.accept(SOCIAL_SCENARIO.loiMoiDen)
    await socialGraphApi.removeRequest(id)
    await socialGraphApi.unfriend(id)
    await socialGraphApi.follow(id)
    await socialGraphApi.unfollow(id)
    await socialGraphApi.listFriends()
    await socialGraphApi.listRequests("outgoing")
    await contentApi.feed()

    // `follow` là PUT — POST là 405 (cạm bẫy E1).
    expect(seen.map((r) => `${r.method} ${new URL(r.url).pathname}`)).toEqual([
      `GET /bff/api/relationships/${id}`,
      "POST /bff/api/friends/requests",
      `POST /bff/api/friends/requests/${SOCIAL_SCENARIO.loiMoiDen}/accept`,
      `DELETE /bff/api/friends/requests/${id}`,
      `DELETE /bff/api/friends/${id}`,
      `PUT /bff/api/follows/${id}`,
      `DELETE /bff/api/follows/${id}`,
      "GET /bff/api/friends",
      "GET /bff/api/friends/requests",
      "GET /bff/api/feed",
    ])
    for (const r of seen) {
      expect(r.credentials).toBe("same-origin")
      expect(r.headers.get("authorization")).toBeNull()
    }
  })

  it("gửi lời mời: body CHỈ có userId của người kia — người gửi lấy từ token, không từ body", async () => {
    const seen = recordRequests()

    const res = await socialGraphApi.sendRequest(userIdKhac)

    expect(await seen[0].json()).toEqual({ userId: userIdKhac })
    expect(res.friendship).toBe("outgoing")
  })

  it("PUT theo dõi không có body: không gửi Content-Type, 204 trả undefined", async () => {
    const seen = recordRequests()

    await expect(socialGraphApi.follow(userIdKhac)).resolves.toBeUndefined()

    expect(seen[0].headers.get("content-type")).toBeNull()
    expect(await seen[0].text()).toBe("")
  })

  it("userId luôn qua encodeURIComponent ở mọi endpoint có id trên đường", async () => {
    const seen = recordRequests()

    await socialGraphApi.relationship("a/b").catch(() => {})
    await socialGraphApi.accept("a/b").catch(() => {})
    await socialGraphApi.follow("a/b").catch(() => {})

    expect(seen.map((r) => new URL(r.url).pathname)).toEqual([
      "/bff/api/relationships/a%2Fb",
      "/bff/api/friends/requests/a%2Fb/accept",
      "/bff/api/follows/a%2Fb",
    ])
  })

  it("listRequests LUÔN gửi direction — kể cả incoming, không dựa vào mặc định của server", async () => {
    const seen = recordRequests()

    await socialGraphApi.listRequests("incoming")
    await socialGraphApi.listRequests("outgoing", { cursor: "Y3Vy", limit: 5 })

    expect(seen.map((r) => new URL(r.url).search)).toEqual([
      "?direction=incoming",
      "?direction=outgoing&cursor=Y3Vy&limit=5",
    ])
  })

  it("feed và listFriends dùng chung pageQuery: trang đầu không có `?`, cursor chuyển nguyên vẹn", async () => {
    const seen = recordRequests()

    await contentApi.feed()
    await contentApi.feed({ cursor: "MjAy+/=", limit: 20 })
    await socialGraphApi.listFriends({ cursor: null })

    expect(seen.map((r) => new URL(r.url).search)).toEqual([
      "",
      "?cursor=MjAy%2B%2F%3D&limit=20",
      "",
    ])
    expect(new URL(seen[1].url).searchParams.get("cursor")).toBe("MjAy+/=")
  })

  it("trang rỗng mà nextCursor KHÁC null là hợp lệ — mock dựng được ca Đ-4.9 cho E3/E4", async () => {
    const page = await contentApi.feed({ cursor: CURSOR_TRANG_RONG })

    expect(page.items).toEqual([])
    expect(page.nextCursor).not.toBeNull()
  })

  it("feed 503 là ApiError có Problem Details đọc được (problem+json) — không phải nhánh 'không đọc được body'", async () => {
    const err = await contentApi
      .feed({ cursor: CURSOR_QUA_TAI })
      .catch((e: unknown) => e)

    expect(err).toBeInstanceOf(ApiError)
    expect((err as ApiError).status).toBe(503)
    expect((err as ApiError).problem?.title).toBe("Bảng tin đang quá tải")
  })

  it("mock trả 400 errors.userId khi hỏi quan hệ với chính mình — đúng câu SocialGraphErrors", async () => {
    const err = await socialGraphApi
      .relationship(userId)
      .catch((e: unknown) => e)

    expect((err as ApiError).fieldErrors.userId).toEqual([
      "Không thể xem quan hệ với chính mình.",
    ])
  })
})
