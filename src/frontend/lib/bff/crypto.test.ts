// @vitest-environment node
import { randomBytes } from "node:crypto"

import { describe, expect, it } from "vitest"

import { hashSessionId, isSessionId, newSessionId, open, seal } from "./crypto"

const key = randomBytes(32)

describe("seal / open — AES-256-GCM", () => {
  it("mã rồi giải ra đúng nội dung; bản mã không chứa bản rõ; mỗi lần mã ra bản khác (IV ngẫu nhiên)", () => {
    const plain = JSON.stringify({
      accessToken: "eyJ.bi.mat",
      refreshToken: "r",
    })
    const a = seal(plain, key, "aad")
    const b = seal(plain, key, "aad")

    expect(open(a, key, "aad")).toBe(plain)
    expect(a).not.toContain("eyJ")
    expect(a).not.toBe(b)
  })

  it("sai khóa, sai aad (bản mã chép sang key Redis khác), hoặc sửa một byte → null, không ném", () => {
    const sealed = seal("bi-mat", key, "phien-a")
    const raw = Buffer.from(sealed, "base64url")
    raw[raw.length - 1] ^= 1

    expect(open(sealed, randomBytes(32), "phien-a")).toBeNull()
    expect(open(sealed, key, "phien-b")).toBeNull()
    expect(open(raw.toString("base64url"), key, "phien-a")).toBeNull()
    expect(open("rac", key, "phien-a")).toBeNull()
  })
})

describe("session ID", () => {
  it("43 ký tự base64url, không lặp; băm là 64 hex và không chứa ID", () => {
    const ids = new Set(Array.from({ length: 100 }, newSessionId))
    expect(ids.size).toBe(100)
    for (const id of ids) expect(isSessionId(id)).toBe(true)

    const id = newSessionId()
    expect(hashSessionId(id)).toMatch(/^[0-9a-f]{64}$/)
    expect(hashSessionId(id)).not.toContain(id)
  })

  it.each([
    "",
    "ngan",
    "A".repeat(42),
    "A".repeat(44),
    `${"A".repeat(42)}/`,
    `${"A".repeat(42)}=`,
  ])("%j không phải session ID", (value) => {
    expect(isSessionId(value)).toBe(false)
  })
})
