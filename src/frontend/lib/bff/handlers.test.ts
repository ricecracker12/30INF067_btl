// @vitest-environment node
import { randomBytes } from "node:crypto"

import { beforeEach, describe, expect, it } from "vitest"

import { createFakeRedis } from "@/mocks/redis"
import { fakeApi, UPSTREAM_API } from "@/mocks/upstream"

import type { BffConfig } from "./config"
import {
  handleLogin,
  handleLogout,
  handleProxy,
  handlePublicAuth,
  handleSession,
  type BffDeps,
} from "./handlers"
import { SESSION_COOKIE } from "./http"
import { createRedisSessionStore } from "./session-store"

// Đ-E14 — BFF gọi thẳng bằng Request/Response chuẩn, API .NET giả qua msw (mocks/upstream.ts), Redis giả trong bộ nhớ.

const APP = "http://localhost:3000"
const JWT_LIKE = /acc-\d+|ref-\d+/

let redis: ReturnType<typeof createFakeRedis>
let deps: BffDeps

function makeDeps(overrides: Partial<BffConfig> = {}): BffDeps {
  const config: BffConfig = {
    apiUrl: UPSTREAM_API,
    redisUrl: "redis://unused",
    appOrigin: APP,
    encryptionKey: randomBytes(32),
    trustedProxyHops: 0,
    ...overrides,
  }
  return {
    config,
    store: createRedisSessionStore(redis.port, config.encryptionKey, {
      lockMs: 15_000,
      waitMs: 5_000,
      pollMs: 5,
    }),
  }
}

beforeEach(() => {
  redis = createFakeRedis()
  deps = makeDeps()
})

type ReqInit = {
  method?: string
  body?: unknown
  sid?: string | null
  origin?: string | null
  headers?: Record<string, string>
}

function req(path: string, init: ReqInit = {}) {
  const headers = new Headers(init.headers)
  const method = init.method ?? "POST"
  if (init.origin !== null && method !== "GET")
    headers.set("origin", init.origin ?? APP)
  if (init.sid)
    headers.set("cookie", `theme=dark; ${SESSION_COOKIE}=${init.sid}`)
  if (init.body !== undefined) headers.set("content-type", "application/json")
  headers.set(
    "x-forwarded-for",
    headers.get("x-forwarded-for") ?? "198.51.100.10"
  )
  return new Request(`${APP}${path}`, {
    method,
    headers,
    body: init.body === undefined ? undefined : JSON.stringify(init.body),
  })
}

const refreshTokenSent = (headers: Headers) =>
  /(?:^|;\s*)refresh_token=([^;]*)/.exec(headers.get("cookie") ?? "")?.[1] ??
  null

function sidFrom(res: Response) {
  const line = res.headers.get("set-cookie") ?? ""
  return new RegExp(`${SESSION_COOKIE}=([^;]*)`).exec(line)?.[1] ?? null
}

async function login(email = "an@example.com") {
  const res = await handleLogin(
    req("/bff/auth/login", { body: { email, password: "MatKhau123" } }),
    deps
  )
  expect(res.status).toBe(204)
  return sidFrom(res)!
}

const proxyMe = (sid: string | null, init: ReqInit = {}) =>
  handleProxy(req("/bff/api/me", { method: "GET", sid, ...init }), deps, ["me"])

describe("POST /bff/auth/login", () => {
  it("204, KHÔNG body, cookie __Host-sid HttpOnly Secure SameSite=Lax Path=/ — token không tới trình duyệt", async () => {
    const res = await handleLogin(
      req("/bff/auth/login", {
        body: { email: "an@example.com", password: "MatKhau123" },
      }),
      deps
    )

    expect(res.status).toBe(204)
    expect(await res.text()).toBe("")
    const cookies = res.headers.getSetCookie()
    expect(cookies).toHaveLength(1)
    expect(cookies[0]).toMatch(
      new RegExp(
        `^${SESSION_COOKIE}=[A-Za-z0-9_-]{43}; Path=/; HttpOnly; Secure; SameSite=Lax; Max-Age=604800$`
      )
    )
    // Refresh token của API KHÔNG bị chuyển ra ngoài; không header nào mang token.
    const allHeaders = [...res.headers.entries()]
      .map(([k, v]) => `${k}: ${v}`)
      .join("\n")
    expect(allHeaders).not.toMatch(JWT_LIKE)
    expect(allHeaders).not.toContain("refresh_token")
    expect(res.headers.get("cache-control")).toBe("no-store")
  })

  it("Redis chỉ chứa bản mã: key là băm của session ID, không có token dạng rõ, không có session ID", async () => {
    const sid = await login()

    const dump = redis.dump()
    expect(dump).toHaveLength(1)
    expect(dump[0].key).toMatch(/^bff:session:[0-9a-f]{64}$/)
    expect(dump[0].key).not.toContain(sid)
    expect(dump[0].value).not.toMatch(JWT_LIKE)
    expect(dump[0].value).not.toContain(sid)
  })

  it("chuyển IP người dùng cho API (rate limit đúng người) và chuyển body nguyên văn", async () => {
    await login("an@example.com")

    const call = fakeApi.callsTo("/auth/login")[0]
    expect(call.headers.get("x-forwarded-for")).toBe("198.51.100.10")
    expect(JSON.parse(call.body)).toEqual({
      email: "an@example.com",
      password: "MatKhau123",
    })
  })

  it("401 của API: chuyển nguyên Problem Details + X-Correlation-ID, không tạo phiên, không cookie", async () => {
    const res = await handleLogin(
      req("/bff/auth/login", {
        body: { email: "sai@example.com", password: "x" },
      }),
      deps
    )

    expect(res.status).toBe(401)
    expect(res.headers.get("content-type")).toContain(
      "application/problem+json"
    )
    expect(res.headers.get("x-correlation-id")).toBe("corr-401")
    expect((await res.json()).detail).toBe("Email hoặc mật khẩu không đúng.")
    expect(res.headers.get("set-cookie")).toBeNull()
    expect(redis.keys()).toEqual([])
  })

  it("chống session fixation: đăng nhập khi đang mang cookie cũ → ID MỚI, phiên cũ bị xóa", async () => {
    const old = await login()
    const res = await handleLogin(
      req("/bff/auth/login", {
        sid: old,
        body: { email: "an@example.com", password: "MatKhau123" },
      }),
      deps
    )
    const fresh = sidFrom(res)

    expect(fresh).not.toBe(old)
    expect(redis.keys()).toHaveLength(1)
    expect((await proxyMe(old)).status).toBe(401)
    expect((await proxyMe(fresh)).status).toBe(200)
  })

  it.each([
    ["thiếu Origin", { origin: null }],
    ["Origin trang lạ", { origin: "https://evil.example" }],
    [
      "Sec-Fetch-Site: cross-site",
      { headers: { "sec-fetch-site": "cross-site" } },
    ],
  ])("CSRF — %s → 403, KHÔNG gọi API", async (_, init) => {
    const res = await handleLogin(
      req("/bff/auth/login", {
        body: { email: "an@example.com", password: "x" },
        ...init,
      }),
      deps
    )
    expect(res.status).toBe(403)
    expect(fakeApi.calls()).toEqual([])
  })

  it("không phải application/json → 415; body quá 16 KB → 413; cả hai không gọi API", async () => {
    const text = await handleLogin(
      new Request(`${APP}/bff/auth/login`, {
        method: "POST",
        headers: { origin: APP, "content-type": "text/plain" },
        body: "x",
      }),
      deps
    )
    const big = await handleLogin(
      req("/bff/auth/login", {
        body: { email: "a".repeat(20_000), password: "x" },
      }),
      deps
    )
    expect(text.status).toBe(415)
    expect(big.status).toBe(413)
    expect(fakeApi.calls()).toEqual([])
  })
})

describe("POST /bff/auth/register, /bff/auth/verify-email", () => {
  it("chuyển tiếp status + body; kiểm Origin như login", async () => {
    const ok = await handlePublicAuth(
      req("/bff/auth/register", {
        body: { email: "an@example.com", password: "MatKhau123" },
      }),
      deps,
      "/auth/register"
    )
    const csrf = await handlePublicAuth(
      req("/bff/auth/verify-email", {
        body: { token: "c".repeat(64) },
        origin: "https://evil.example",
      }),
      deps,
      "/auth/verify-email"
    )
    expect(ok.status).toBe(201)
    expect(csrf.status).toBe(403)
    expect(fakeApi.calls().map((c) => c.path)).toEqual(["/auth/register"])
  })
})

describe("GET /bff/auth/session", () => {
  it("có phiên → authenticated: true, body không chứa token", async () => {
    const sid = await login()
    const res = await handleSession(
      req("/bff/auth/session", { method: "GET", sid }),
      deps
    )
    const text = await res.text()
    expect(JSON.parse(text)).toEqual({ authenticated: true })
    expect(text).not.toMatch(JWT_LIKE)
  })

  it("không cookie → false; cookie mà phiên không còn → false + xóa cookie; cookie sai dạng → không chạm Redis", async () => {
    const none = await handleSession(
      req("/bff/auth/session", { method: "GET" }),
      deps
    )
    expect(await none.json()).toEqual({ authenticated: false })

    const gone = await handleSession(
      req("/bff/auth/session", { method: "GET", sid: "A".repeat(43) }),
      deps
    )
    expect(await gone.json()).toEqual({ authenticated: false })
    expect(gone.headers.get("set-cookie")).toContain("Max-Age=0")

    const readsBefore = redis.reads()
    const forged = await handleSession(
      req("/bff/auth/session", {
        method: "GET",
        headers: { cookie: `${SESSION_COOKIE}=../../x` },
      }),
      deps
    )
    expect(await forged.json()).toEqual({ authenticated: false })
    // Cookie là dữ liệu người dùng gửi: sai dạng thì không bao giờ chạm tới Redis.
    expect(redis.reads()).toBe(readsBefore)
  })

  it("SESSION_ENCRYPTION_KEY đổi (khởi động lại dev) → phiên cũ coi như không có, bị dọn khỏi Redis", async () => {
    const sid = await login()
    deps = makeDeps() // khóa mới, cùng Redis

    const res = await handleSession(
      req("/bff/auth/session", { method: "GET", sid }),
      deps
    )
    expect(await res.json()).toEqual({ authenticated: false })
    expect(redis.keys()).toEqual([])
  })
})

describe("/bff/api/* — proxy có phiên", () => {
  it("gắn bearer của phiên ở server; trả body + X-Correlation-ID; KHÔNG chuyển Set-Cookie của API", async () => {
    const sid = await login()

    const res = await proxyMe(sid)

    expect(res.status).toBe(200)
    expect((await res.json()).roleDisplayName).toBe("Người dùng")
    expect(res.headers.get("x-correlation-id")).toBe("corr-me")
    expect(res.headers.get("set-cookie")).toBeNull()
    expect(fakeApi.callsTo("/me")[0].headers.get("authorization")).toMatch(
      /^Bearer acc-\d+$/
    )
  })

  it("không phiên → 401, không gọi API", async () => {
    expect((await proxyMe(null)).status).toBe(401)
    expect(fakeApi.callsTo("/me")).toEqual([])
  })

  it("access hết hạn → refresh ở server → gọi lại MỘT lần → 200; refresh token mới được cất", async () => {
    const sid = await login()
    fakeApi.expireAccessTokens()

    const res = await proxyMe(sid)

    expect(res.status).toBe(200)
    expect(fakeApi.callsTo("/auth/refresh")).toHaveLength(1)
    // msw trong Node tự giữ cookie của response trước ("leak=1") — fetch của Next server thì không; chỉ đọc refresh_token.
    expect(refreshTokenSent(fakeApi.callsTo("/auth/refresh")[0].headers)).toBe(
      "ref-1"
    )
    expect(fakeApi.callsTo("/me")).toHaveLength(2)
    // Lần sau dùng token mới luôn, không refresh nữa.
    expect((await proxyMe(sid)).status).toBe(200)
    expect(fakeApi.callsTo("/auth/refresh")).toHaveLength(1)
  })

  it("3 request đồng thời (3 tab) cùng nhận 401 → ĐÚNG 1 refresh, cả 3 thành công (Đ-E4 ở server)", async () => {
    const sid = await login()
    fakeApi.expireAccessTokens()
    fakeApi.delayRefresh(30)

    const results = await Promise.all([
      proxyMe(sid),
      proxyMe(sid),
      proxyMe(sid),
    ])

    expect(results.map((r) => r.status)).toEqual([200, 200, 200])
    expect(fakeApi.callsTo("/auth/refresh")).toHaveLength(1)
  })

  it("401 về MUỘN, sau khi request khác đã refresh xong → dùng token mới, KHÔNG refresh lần hai (bẫy 5 ở server)", async () => {
    const { http, HttpResponse } = await import("msw")
    const { server } = await import("@/mocks/node")
    const sid = await login() // acc-1 / ref-1
    fakeApi.expireAccessTokens()

    // Request /me đầu tiên (cầm acc-1) bị giữ lại tới khi request thứ hai đã refresh (acc-2) và gọi lại xong.
    let release!: () => void
    const gate = new Promise<void>((r) => (release = r))
    let first = true
    server.use(
      http.get(`${UPSTREAM_API}/me`, async ({ request }) => {
        if (first) {
          first = false
          await gate
        }
        return request.headers.get("authorization") === "Bearer acc-2"
          ? HttpResponse.json({})
          : HttpResponse.json({ status: 401 }, { status: 401 })
      })
    )

    const slow = proxyMe(sid)
    await new Promise((r) => setTimeout(r, 20))
    expect((await proxyMe(sid)).status).toBe(200)
    expect(fakeApi.callsTo("/auth/refresh")).toHaveLength(1)

    release()
    expect((await slow).status).toBe(200)
    expect(fakeApi.callsTo("/auth/refresh")).toHaveLength(1)
  })

  it("refresh 401 (bị thu hồi) → xóa phiên, 401 + xóa cookie; request sau không gọi refresh nữa", async () => {
    const sid = await login()
    fakeApi.expireAccessTokens()
    fakeApi.revokeRefreshTokens()

    const res = await proxyMe(sid)

    expect(res.status).toBe(401)
    expect(res.headers.get("set-cookie")).toContain("Max-Age=0")
    expect(redis.keys().filter((k) => k.startsWith("bff:session:"))).toEqual([])
    expect((await proxyMe(sid)).status).toBe(401)
    expect(fakeApi.callsTo("/auth/refresh")).toHaveLength(1)
  })

  it("refresh 500 → trả 500, GIỮ phiên (không kết luận là hết phiên)", async () => {
    const sid = await login()
    fakeApi.expireAccessTokens()
    fakeApi.failRefreshWith(500)

    expect((await proxyMe(sid)).status).toBe(500)
    fakeApi.failRefreshWith(null)
    expect((await proxyMe(sid)).status).toBe(200)
  })

  it("gọi lại vẫn 401 → trả 401, tổng 1 refresh (không vòng lặp)", async () => {
    const sid = await login()
    fakeApi.expireAccessTokens()
    // Refresh thành công nhưng API từ chối cả token mới (vd revoked:user): hết hạn ngay sau refresh.
    const { http, HttpResponse } = await import("msw")
    const { server } = await import("@/mocks/node")
    server.use(
      http.get(`${UPSTREAM_API}/me`, () =>
        HttpResponse.json({ status: 401 }, { status: 401 })
      )
    )

    expect((await proxyMe(sid)).status).toBe(401)
    expect(fakeApi.callsTo("/auth/refresh")).toHaveLength(1)
  })

  it.each([
    [["auth", "refresh"]],
    [["AUTH", "login"]],
    [[".."]],
    [["me", "..", "auth", "refresh"]],
    [["a\\b"]],
    [[]],
  ])(
    "đường %j bị chặn 404 — nhóm auth của API và path traversal không đi qua proxy",
    async (segments) => {
      const sid = await login()
      const res = await handleProxy(
        req("/bff/api/x", { method: "GET", sid }),
        deps,
        segments
      )
      expect(res.status).toBe(404)
      expect(fakeApi.calls().map((c) => c.path)).toEqual(["/auth/login"])
    }
  )

  it("mỗi segment được mã hóa lại — '%2F' giải ra '/' không thành thêm một cấp đường dẫn", async () => {
    const sid = await login()
    const res = await handleProxy(
      req("/bff/api/x", { method: "GET", sid }),
      deps,
      ["me/../auth"]
    )
    expect(res.status).toBe(404)
  })

  it("segment chứa '?' hay '#' được mã hóa — không chèn được query string sang API", async () => {
    const { http, HttpResponse } = await import("msw")
    const { server } = await import("@/mocks/node")
    const sid = await login()
    const seen: string[] = []
    server.use(
      http.get(`${UPSTREAM_API}/*`, ({ request }) => {
        const url = new URL(request.url)
        seen.push(url.pathname + url.search)
        return HttpResponse.json({}, { status: 404 })
      })
    )

    await handleProxy(req("/bff/api/x", { method: "GET", sid }), deps, [
      "me?role=ADMIN",
    ])

    expect(seen).toEqual(["/api/v1/me%3Frole%3DADMIN"])
  })

  it("POST qua proxy: kiểm Origin (CSRF) trước khi gọi API", async () => {
    const sid = await login()
    const post = (origin: string) =>
      handleProxy(
        req("/bff/api/posts", { sid, origin, body: { body: "xin chào" } }),
        deps,
        ["posts"]
      )

    expect((await post("https://evil.example")).status).toBe(403)
    expect(fakeApi.callsTo("/posts")).toEqual([])
    expect((await post(APP)).status).toBe(201)
    expect(fakeApi.callsTo("/posts")[0].body).toBe(
      JSON.stringify({ body: "xin chào" })
    )
  })
})

describe("POST /bff/auth/logout", () => {
  it("API thu hồi bằng bearer + refresh cookie; phiên bị xóa; cookie bị xóa; 204", async () => {
    const sid = await login()

    const res = await handleLogout(req("/bff/auth/logout", { sid }), deps)

    expect(res.status).toBe(204)
    expect(res.headers.get("set-cookie")).toContain("Max-Age=0")
    const call = fakeApi.callsTo("/auth/logout")[0]
    expect(call.headers.get("authorization")).toMatch(/^Bearer acc-\d+$/)
    expect(refreshTokenSent(call.headers)).toMatch(/^ref-\d+$/)
    expect(redis.keys()).toEqual([])
    expect((await proxyMe(sid)).status).toBe(401)
  })

  it("access hết hạn lúc đăng xuất → refresh rồi logout bằng token mới (API vẫn thu hồi được)", async () => {
    const sid = await login()
    fakeApi.expireAccessTokens()

    await handleLogout(req("/bff/auth/logout", { sid }), deps)

    expect(fakeApi.calls().map((c) => c.path)).toEqual([
      "/auth/login",
      "/auth/logout",
      "/auth/refresh",
      "/auth/logout",
    ])
  })

  it("không Origin → 403, phiên còn nguyên", async () => {
    const sid = await login()
    expect(
      (await handleLogout(req("/bff/auth/logout", { sid, origin: null }), deps))
        .status
    ).toBe(403)
    expect((await proxyMe(sid)).status).toBe(200)
  })
})

describe("X-Forwarded-For → IP người dùng", () => {
  it.each([
    // [header, hops, IP gửi cho API]
    ["198.51.100.10", 0, "198.51.100.10"],
    ["203.0.113.9, 198.51.100.10", 0, "198.51.100.10"], // client tự viết phần đầu — bỏ
    ["203.0.113.9, 198.51.100.10, 172.68.1.1", 1, "198.51.100.10"], // Cloudflare/apache gắn phần cuối
    ["khong-phai-ip", 0, null],
    ["198.51.100.10", 1, null], // thiếu phần tử — không đoán
  ])("%s (hops=%i) → %s", async (header, hops, expected) => {
    deps = makeDeps({ trustedProxyHops: hops })
    await handlePublicAuth(
      req("/bff/auth/register", {
        body: { email: "an@example.com", password: "MatKhau123" },
        headers: { "x-forwarded-for": header },
      }),
      deps,
      "/auth/register"
    )
    expect(
      fakeApi.callsTo("/auth/register")[0].headers.get("x-forwarded-for")
    ).toBe(expected)
  })
})
