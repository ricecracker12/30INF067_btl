// @vitest-environment node
import { readFileSync } from "node:fs"
import { fileURLToPath } from "node:url"

import { NextRequest } from "next/server"
import { afterEach, describe, expect, it, vi } from "vitest"

import { config, proxy, R2_HOST_ENV } from "./proxy"

const R2 = "https://abc123.r2.cloudflarestorage.com"

const nonceOf = (csp: string | null) =>
  /'nonce-([^']+)'/.exec(csp ?? "")?.[1] ?? null

describe("proxy.ts — CSP có nonce theo từng request (Đ-E15)", () => {
  it("gắn CSP vào response; chuyển CÙNG nonce vào header request để Next gắn cho script", () => {
    const res = proxy(new NextRequest("http://localhost:3000/login"))

    const csp = res.headers.get("content-security-policy")
    const nonce = nonceOf(csp)
    expect(nonce).toMatch(/^[A-Za-z0-9+/]{22}==$/)
    // NextResponse.next({ request: { headers } }) chuyển header request qua tiền tố x-middleware-request-*.
    expect(res.headers.get("x-middleware-request-x-nonce")).toBe(nonce)
    expect(
      res.headers.get("x-middleware-request-content-security-policy")
    ).toBe(csp)
  })

  it("mỗi request một nonce khác", () => {
    const a = nonceOf(
      proxy(new NextRequest("http://localhost:3000/me")).headers.get(
        "content-security-policy"
      )
    )
    const b = nonceOf(
      proxy(new NextRequest("http://localhost:3000/me")).headers.get(
        "content-security-policy"
      )
    )
    expect(a).not.toBe(b)
  })

  it("không làm gì ngoài CSP: không redirect, không đọc cookie phiên", () => {
    const res = proxy(
      new NextRequest("http://localhost:3000/me", {
        headers: { cookie: "__Host-sid=x" },
      })
    )
    expect(res.status).toBe(200)
    expect(res.headers.get("location")).toBeNull()
    expect(res.headers.get("x-middleware-next")).toBe("1")
  })

  it.each([
    ["/login", true],
    ["/me", true],
    ["/", true],
    ["/verify-email", true],
    ["/bff/auth/session", false],
    ["/bff/api/me", false],
    ["/_next/static/chunks/app.js", false],
    ["/_next/image", false],
    ["/favicon.ico", false],
  ])("matcher: %s → chạy proxy = %s", (path, runs) => {
    const source = config.matcher[0].source
    expect(new RegExp(`^${source}$`).test(path)).toBe(runs)
  })
})

describe("R2_HOST_ENV — tên biến phải khớp deploy/.env (Đ-E17)", () => {
  // Ghim CHUỖI, không so với chính hằng số: mọi test khác import hằng nên đổi tên là chúng đi theo và
  // vẫn xanh — trong khi `deploy/.env` trên server không đổi theo, và mọi trang trả 500 sau khi merge.
  // Đây là đột biến DUY NHẤT của E7 lọt lưới ở lượt đầu; hai ca dưới là thứ bịt nó lại.
  it("là đúng `R2__Endpoint` — biến API đã có sẵn, không sinh biến thứ hai", () => {
    expect(R2_HOST_ENV).toBe("R2__Endpoint")
  })

  it("có mặt trong deploy/.env.example — người dựng server đọc file đó, không đọc code", () => {
    const example = readFileSync(
      fileURLToPath(new URL("../../deploy/.env.example", import.meta.url)),
      "utf8"
    )
    expect(example).toMatch(new RegExp(`^${R2_HOST_ENV}=`, "m"))
  })
})

describe("proxy.ts — host R2 vào CSP (Đ-E17)", () => {
  const goc = process.env[R2_HOST_ENV]
  afterEach(() => {
    vi.unstubAllEnvs()
    if (goc === undefined) delete process.env[R2_HOST_ENV]
    else process.env[R2_HOST_ENV] = goc
  })

  const cspCua = (path = "/login") =>
    proxy(new NextRequest(`http://localhost:3000${path}`)).headers.get(
      "content-security-policy"
    ) ?? ""

  it("biến có mặt → host vào connect-src và img-src, KHÔNG vào script-src", () => {
    process.env[R2_HOST_ENV] = R2

    const csp = cspCua()

    expect(csp).toContain(`connect-src 'self' ${R2}`)
    expect(csp).toContain(R2)
    expect(/script-src[^;]*/.exec(csp)?.[0]).not.toContain(R2)
  })

  it("ngoài production, thiếu biến → vẫn phục vụ, CSP không có host nào thừa (chưa làm E3/E4 thì chưa cần R2)", () => {
    delete process.env[R2_HOST_ENV]

    const csp = cspCua()

    expect(csp).toContain("connect-src 'self';")
    expect(csp).toContain("img-src 'self' blob: data:;")
  })

  it("production thiếu biến → ném lỗi NÊU TÊN BIẾN, từ chối phục vụ (thiếu cấu hình = không chạy)", () => {
    delete process.env[R2_HOST_ENV]
    vi.stubEnv("NODE_ENV", "production")

    expect(() => cspCua()).toThrow(R2_HOST_ENV)
  })

  it("biến sai dạng → ném lỗi NÊU TÊN BIẾN, không phục vụ trang với CSP sai", () => {
    process.env[R2_HOST_ENV] = `${R2}/socialmedia-dev`

    expect(() => cspCua()).toThrow(R2_HOST_ENV)
  })

  it("đọc env LÚC CHẠY, không chụp một lần: đổi biến giữa hai request thì CSP đổi theo", () => {
    process.env[R2_HOST_ENV] = R2
    expect(cspCua()).toContain(R2)

    process.env[R2_HOST_ENV] = "https://khac.r2.cloudflarestorage.com"
    const sau = cspCua()

    expect(sau).toContain("https://khac.r2.cloudflarestorage.com")
    expect(sau).not.toContain(R2)
  })
})
