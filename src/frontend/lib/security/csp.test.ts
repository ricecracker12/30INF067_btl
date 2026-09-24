// @vitest-environment node
import { describe, expect, it } from "vitest"

import { buildCsp, checkR2Host, createNonce } from "./csp"

/** Tên biến thật nằm ở `proxy.ts`; ở đây chỉ cần MỘT tên bất kỳ để kiểm thông điệp có nêu tên biến. */
const TEN_BIEN = "R2__Endpoint"

const R2 = "https://abc123.r2.cloudflarestorage.com"

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
  const csp = buildCsp("abc123", { dev: false, r2Host: null })

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
  const csp = buildCsp("abc123", { dev: true, r2Host: null })

  it("thêm unsafe-eval (React dựng stack lỗi) và style inline (hot reload); không nâng https trên localhost", () => {
    expect(directive(csp, "script-src")).toContain("'unsafe-eval'")
    expect(directive(csp, "style-src")).toEqual(["'self'", "'unsafe-inline'"])
    expect(directive(csp, "upgrade-insecure-requests")).toBeUndefined()
  })

  it("dev vẫn KHÔNG cho script inline không nonce", () => {
    expect(directive(csp, "script-src")).not.toContain("'unsafe-inline'")
  })
})

describe("buildCsp — host R2 (Đ-E17)", () => {
  const csp = buildCsp("abc123", { dev: false, r2Host: R2 })

  it("PUT lên R2 được phép: connect-src có host R2, và vẫn giữ 'self' cho BFF", () => {
    expect(directive(csp, "connect-src")).toEqual(["'self'", R2])
  })

  it("ảnh presigned hiện được: img-src có host R2, và vẫn giữ blob:/data:", () => {
    expect(directive(csp, "img-src")).toEqual(["'self'", "blob:", "data:", R2])
  })

  it("host R2 KHÔNG được chạy script, KHÔNG là default-src, KHÔNG là đích của form", () => {
    // R2 phục vụ nội dung do NGƯỜI DÙNG tải lên. Cho nó chạy script là biến kho ảnh thành kho XSS —
    // cùng lý do SVG bị loại khỏi allowlist (Đ-2.8).
    for (const name of [
      "script-src",
      "default-src",
      "form-action",
      "style-src",
      "font-src",
      "base-uri",
    ]) {
      expect(directive(csp, name)).not.toContain(R2)
    }
  })

  it("thiếu biến thì chỉ thị KHÔNG có chuỗi rỗng thừa (`connect-src 'self' ;`)", () => {
    const khong = buildCsp("abc123", { dev: false, r2Host: null })
    expect(directive(khong, "connect-src")).toEqual(["'self'"])
    expect(directive(khong, "img-src")).toEqual(["'self'", "blob:", "data:"])
    expect(khong).not.toMatch(/\s;|;\s*$/)
  })

  // Sửa 2026-09-24 (Đ-E18, GĐ5): trước đó dev GIỐNG HỆT production; nay dev thêm ĐÚNG hai nguồn hub chat, không gì khác.
  it("dev cũng nới R2 như production — connect-src dev = production + hai nguồn hub dev (Đ-E18)", () => {
    const dev = buildCsp("abc123", { dev: true, r2Host: R2 })
    expect(directive(dev, "connect-src")).toEqual([
      "'self'",
      R2,
      "http://localhost:5259",
      "ws://localhost:5259",
    ])
    expect(directive(dev, "img-src")).toContain(R2)
  })
})

describe("checkR2Host — kiểm dạng (Đ-E17 bước 3)", () => {
  it("nhận https://host và https://host:port", () => {
    expect(checkR2Host(R2, { dev: false, name: TEN_BIEN })).toEqual({
      host: R2,
      problem: null,
    })
    expect(
      checkR2Host("https://r2.example.test:8443", {
        dev: false,
        name: TEN_BIEN,
      }).host
    ).toBe("https://r2.example.test:8443")
  })

  it("cắt khoảng trắng thừa hai đầu", () => {
    expect(checkR2Host(`  ${R2}  `, { dev: false, name: TEN_BIEN }).host).toBe(
      R2
    )
  })

  it.each([
    [`${R2}/`, "dấu / ở cuối"],
    [`${R2}/socialmedia-dev`, "có path"],
    [`${R2}?x=1`, "có query"],
    ["abc123.r2.cloudflarestorage.com", "thiếu scheme"],
    ["ftp://abc123.r2.cloudflarestorage.com", "scheme lạ"],
  ])("từ chối %s (%s) và nêu TÊN BIẾN trong thông điệp", (value) => {
    const { host, problem } = checkR2Host(value, { dev: false, name: TEN_BIEN })
    expect(host).toBeNull()
    expect(problem).toContain(TEN_BIEN)
    expect(problem).toContain(value)
  })

  it("http chỉ chấp nhận ở dev — production dính http là hạ cấp bảo mật lặng lẽ", () => {
    expect(
      checkR2Host("http://localhost:9000", { dev: true, name: TEN_BIEN }).host
    ).toBe("http://localhost:9000")
    expect(
      checkR2Host("http://localhost:9000", { dev: false, name: TEN_BIEN }).host
    ).toBeNull()
  })

  it("thiếu / rỗng / chỉ khoảng trắng → null, KHÔNG phải lỗi (chỗ gọi tự quyết)", () => {
    for (const raw of [undefined, null, "", "   "]) {
      expect(checkR2Host(raw, { dev: false, name: TEN_BIEN })).toEqual({
        host: null,
        problem: null,
      })
    }
  })
})

describe("buildCsp — hub chat (Đ-E18, GĐ5)", () => {
  it("dev: connect-src mở API dev cho WebSocket hub (http + ws của localhost:5259)", () => {
    const csp = buildCsp("abc123", { dev: true, r2Host: null })
    expect(directive(csp, "connect-src")).toEqual([
      "'self'",
      "http://localhost:5259",
      "ws://localhost:5259",
    ])
  })

  it("production: connect-src KHÔNG có localhost — hub đi /hubs/chat cùng origin, 'self' đã phủ wss", () => {
    const csp = buildCsp("abc123", { dev: false, r2Host: null })
    expect(directive(csp, "connect-src")).toEqual(["'self'"])
    expect(csp).not.toContain("localhost")
  })
})
