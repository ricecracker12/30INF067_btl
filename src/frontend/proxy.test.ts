// @vitest-environment node
import { NextRequest } from "next/server"
import { describe, expect, it } from "vitest"

import { config, proxy } from "./proxy"

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
