import "server-only"

import { randomBytes } from "node:crypto"

// Đ-E14 — cấu hình BFF, CHỈ ở server. Không biến nào ở đây có tiền tố NEXT_PUBLIC_: tiền tố đó nhúng giá trị vào bundle
// trình duyệt, và khóa mã hóa phiên lọt ra trình duyệt là mất lớp bảo vệ Redis.
//
// Đọc LƯỜI ở request đầu tiên, không lúc nạp module: `next build` nạp route handler mà không có biến nào — kiểm lúc nạp là
// build CI đỏ. Ngoài development thiếu biến thì request đầu tiên ném lỗi nêu tên biến (cùng tinh thần "thiếu cấu hình =
// từ chối chạy" của backend); development có mặc định trỏ hạ tầng local.

export type BffConfig = Readonly<{
  /** Gốc API .NET mà Next server gọi, vd http://localhost:5259/api/v1 (dev), http://api:8080/api/v1 (compose). */
  apiUrl: string
  redisUrl: string
  /** Origin công khai của app — chuẩn để so header Origin (chống CSRF). */
  appOrigin: string
  /** 32 byte AES-256-GCM mã hóa token trong Redis. */
  encryptionKey: Buffer
  /** Số địa chỉ ở CUỐI X-Forwarded-For do hạ tầng tin cậy (apache, Cloudflare…) gắn SAU địa chỉ client. */
  trustedProxyHops: number
}>

const DEV_DEFAULTS = {
  API_INTERNAL_URL: "http://localhost:5259/api/v1",
  REDIS_URL: "redis://localhost:6379",
  APP_ORIGIN: "http://localhost:3000",
} as const

const globalForDev = globalThis as unknown as { __bffDevKey?: Buffer }

let cached: BffConfig | null = null

export function bffConfig(env: NodeJS.ProcessEnv = process.env): BffConfig {
  if (cached && env === process.env) return cached
  const config = readConfig(env)
  if (env === process.env) cached = config
  return config
}

export function readConfig(env: NodeJS.ProcessEnv): BffConfig {
  const production = env.NODE_ENV === "production"
  const missing: string[] = []
  const problems: string[] = []

  const read = (name: keyof typeof DEV_DEFAULTS) => {
    const value = env[name]?.trim()
    if (value) return value
    if (!production) return DEV_DEFAULTS[name]
    missing.push(name)
    return ""
  }

  const apiUrl = read("API_INTERNAL_URL").replace(/\/+$/, "")
  const redisUrl = read("REDIS_URL")
  const appOrigin = read("APP_ORIGIN")

  if (apiUrl && !/^https?:\/\/[^/]+/.test(apiUrl))
    problems.push(`API_INTERNAL_URL '${apiUrl}' phải là URL tuyệt đối http(s)`)
  if (appOrigin) {
    let ok = false
    try {
      const u = new URL(appOrigin)
      ok =
        (u.protocol === "http:" || u.protocol === "https:") &&
        u.origin === appOrigin
    } catch {
      ok = false
    }
    if (!ok)
      problems.push(
        `APP_ORIGIN '${appOrigin}' phải có dạng scheme://host[:port], không path, không dấu / ở cuối`
      )
  }

  let encryptionKey: Buffer
  const rawKey = env.SESSION_ENCRYPTION_KEY?.trim()
  if (rawKey) {
    encryptionKey = Buffer.from(rawKey, "base64")
    if (encryptionKey.length !== 32)
      problems.push(
        `SESSION_ENCRYPTION_KEY phải là 32 byte mã base64 (đang là ${encryptionKey.length} byte) — sinh bằng: openssl rand -base64 32`
      )
  } else if (!production) {
    // Development: khóa ngẫu nhiên theo tiến trình, không có khóa nào trong repo. Giữ qua hot reload (globalThis); khởi
    // động lại `pnpm dev` là phiên cũ không giải mã được → coi như chưa đăng nhập.
    encryptionKey = globalForDev.__bffDevKey ??= randomBytes(32)
  } else {
    missing.push("SESSION_ENCRYPTION_KEY")
    encryptionKey = Buffer.alloc(0)
  }

  const hopsRaw = env.TRUSTED_PROXY_HOPS?.trim() || "0"
  const trustedProxyHops = Number(hopsRaw)
  if (!Number.isInteger(trustedProxyHops) || trustedProxyHops < 0)
    problems.push(`TRUSTED_PROXY_HOPS '${hopsRaw}' phải là số nguyên không âm`)

  if (missing.length > 0 || problems.length > 0) {
    throw new Error(
      [
        missing.length > 0 &&
          `Thiếu biến môi trường BFF khi chạy production: ${missing.join(", ")}.`,
        ...problems,
        "BFF từ chối phục vụ thay vì chạy với cấu hình thiếu (Đ-E14).",
      ]
        .filter(Boolean)
        .join(" ")
    )
  }

  return { apiUrl, redisUrl, appOrigin, encryptionKey, trustedProxyHops }
}
