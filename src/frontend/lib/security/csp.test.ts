// @vitest-environment node
import { describe, expect, it } from "vitest"

import { buildCsp, createNonce } from "./csp"

/** "script-src 'self' 'nonce-x'" → ["'self'", "'nonce-x'"] */
function directive(csp: string, name: string): string[] | undefined {
  const found = csp
    .split(";")
    .map((d) => d.trim().split(/\s+/))
    .find(([n]) => n === name)
  return found?.slice(1)
}

describe("createNonce", () => {
  it("16 byte base64, không lặp", () => {
    const nonces = new Set(Array.from({ length: 200 }, createNonce))
    expect(nonces.size).toBe(200)
    for (const n of nonces) {
      expect(n).toMatch(/^[A-Za-z0-9+/]{22}==$/)
      expect(Buffer.from(n, "base64")).toHaveLength(16)
    }
  })
})

describe("buildCsp — production", () => {
  const csp = buildCsp("abc123", { dev: false })

  it("script-src: 'self' + nonce; KHÔNG unsafe-inline, KHÔNG unsafe-eval, KHÔNG strict-dynamic", () => {
    const script = directive(csp, "script-src")
    expect(script).toEqual(["'self'", "'nonce-abc123'"])
    expect(csp).not.toContain("unsafe-eval")
    // 'strict-dynamic' làm Chrome cho chạy <script> inline chèn bằng createElement (đã thử trên trình duyệt).
    expect(csp).not.toContain("strict-dynamic")
  })

  it("style-src: nonce, KHÔNG unsafe-inline", () => {
    expect(directive(csp, "style-src")).toEqual(["'self'", "'nonce-abc123'"])
    expect(csp).not.toContain("unsafe-inline")
  })

  it.each([
    ["default-src", ["'self'"]],
    ["connect-src", ["'self'"]],
    ["object-src", ["'none'"]],
    ["base-uri", ["'self'"]],
    ["form-action", ["'self'"]],
    ["frame-ancestors", ["'none'"]],
    ["upgrade-insecure-requests", []],
  ])("%s = %j", (name, expected) => {
    expect(directive(csp, name)).toEqual(expected)
  })
})

describe("buildCsp — development", () => {
  const csp = buildCsp("abc123", { dev: true })

  it("thêm unsafe-eval (React dựng stack lỗi) và style inline (hot reload); không nâng https trên localhost", () => {
    expect(directive(csp, "script-src")).toContain("'unsafe-eval'")
    expect(directive(csp, "style-src")).toEqual(["'self'", "'unsafe-inline'"])
    expect(directive(csp, "upgrade-insecure-requests")).toBeUndefined()
  })

  it("dev vẫn KHÔNG cho script inline không nonce", () => {
    expect(directive(csp, "script-src")).not.toContain("'unsafe-inline'")
  })
})
