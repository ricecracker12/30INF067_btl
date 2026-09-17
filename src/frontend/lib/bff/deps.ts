import "server-only"

import { Redis } from "ioredis"

import { bffConfig } from "./config"
import type { BffDeps } from "./handlers"
import { createRedisSessionStore, type RedisPort } from "./session-store"

// Đ-E14 — nối BFF với Redis thật. MỘT client cho cả tiến trình; giữ trên globalThis để hot reload của `pnpm dev` không mở
// thêm kết nối mỗi lần sửa file.

const globalForBff = globalThis as unknown as { __bffDeps?: BffDeps }

/** Xóa khóa chỉ khi còn đúng chủ — GET rồi DEL rời nhau là có thể xóa khóa vừa được tiến trình khác lấy. */
const DEL_IF_EQUALS = `if redis.call("get", KEYS[1]) == ARGV[1] then return redis.call("del", KEYS[1]) else return 0 end`

function redisPort(client: Redis): RedisPort {
  return {
    get: (key) => client.get(key),
    async setWithTtl(key, value, ttlSeconds) {
      await client.set(key, value, "EX", ttlSeconds)
    },
    async setIfAbsent(key, value, ttlMs) {
      return (await client.set(key, value, "PX", ttlMs, "NX")) === "OK"
    },
    async del(key) {
      await client.del(key)
    },
    async delIfEquals(key, value) {
      await client.eval(DEL_IF_EQUALS, 1, key, value)
    },
  }
}

export function deps(): BffDeps {
  if (globalForBff.__bffDeps) return globalForBff.__bffDeps
  const config = bffConfig()
  const client = new Redis(config.redisUrl, {
    // Redis chết thì request lỗi sau một lần thử lại (503), không xếp hàng chờ vô hạn giữ kết nối của người dùng.
    // Giữ offline queue mặc định: tắt nó thì request đầu tiên sau khi tiến trình khởi động (lúc kết nối chưa mở xong)
    // cũng lỗi.
    maxRetriesPerRequest: 1,
    connectTimeout: 2_000,
  })
  client.on("error", () => {
    // ioredis tự kết nối lại; lỗi từng request đã thành 503 ở handlers.ts. Không log URL (có thể chứa mật khẩu).
  })
  globalForBff.__bffDeps = {
    config,
    store: createRedisSessionStore(redisPort(client), config.encryptionKey),
  }
  return globalForBff.__bffDeps
}
