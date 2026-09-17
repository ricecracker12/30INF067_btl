// @vitest-environment node
import { describe, expect, it } from "vitest"

import { readConfig } from "./config"

const key = Buffer.alloc(32, 7).toString("base64")
const production = {
  NODE_ENV: "production",
  API_INTERNAL_URL: "http://api:8080/api/v1/",
  REDIS_URL: "redis://redis:6379",
  APP_ORIGIN: "https://mxh.banhgao.net",
  SESSION_ENCRYPTION_KEY: key,
  TRUSTED_PROXY_HOPS: "1",
} as NodeJS.ProcessEnv

describe("readConfig", () => {
  it("production đủ biến: đọc đúng, bỏ / cuối của API_INTERNAL_URL", () => {
    const c = readConfig(production)
    expect(c.apiUrl).toBe("http://api:8080/api/v1")
    expect(c.appOrigin).toBe("https://mxh.banhgao.net")
    expect(c.encryptionKey.length).toBe(32)
    expect(c.trustedProxyHops).toBe(1)
  })

  it("production thiếu biến → ném, nêu ĐÚNG tên các biến thiếu", () => {
    expect(() =>
      readConfig({ NODE_ENV: "production" } as NodeJS.ProcessEnv)
    ).toThrow(/API_INTERNAL_URL, REDIS_URL, APP_ORIGIN, SESSION_ENCRYPTION_KEY/)
  })

  it("development không biến nào: mặc định local, khóa ngẫu nhiên 32 byte ổn định trong tiến trình", () => {
    const a = readConfig({ NODE_ENV: "development" } as NodeJS.ProcessEnv)
    const b = readConfig({ NODE_ENV: "development" } as NodeJS.ProcessEnv)
    expect(a.apiUrl).toBe("http://localhost:5259/api/v1")
    expect(a.appOrigin).toBe("http://localhost:3000")
    expect(a.encryptionKey.length).toBe(32)
    expect(b.encryptionKey.equals(a.encryptionKey)).toBe(true)
  })

  it.each([
    ["SESSION_ENCRYPTION_KEY", Buffer.alloc(16).toString("base64"), /32 byte/],
    ["APP_ORIGIN", "https://mxh.banhgao.net/", /APP_ORIGIN/],
    ["APP_ORIGIN", "https://mxh.banhgao.net/app", /APP_ORIGIN/],
    ["API_INTERNAL_URL", "api:8080", /API_INTERNAL_URL/],
    ["TRUSTED_PROXY_HOPS", "-1", /TRUSTED_PROXY_HOPS/],
    ["TRUSTED_PROXY_HOPS", "1.5", /TRUSTED_PROXY_HOPS/],
  ])("%s = %j sai dạng → ném", (name, value, message) => {
    expect(() => readConfig({ ...production, [name]: value })).toThrow(message)
  })
})
