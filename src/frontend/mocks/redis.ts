import type { RedisPort } from "@/lib/bff/session-store"

/**
 * Redis giả trong bộ nhớ cho test của lib/bff — đúng ngữ nghĩa mà session-store dựa vào: TTL, SET NX PX, xóa-nếu-đúng-chủ.
 * Không thay được Redis thật: lượt Playwright trên dev (Redis của compose) mới là bằng chứng kết nối thật.
 */
export function createFakeRedis() {
  const data = new Map<string, { value: string; expiresAt: number }>()
  const live = (key: string) => {
    const entry = data.get(key)
    if (!entry) return null
    if (Date.now() >= entry.expiresAt) {
      data.delete(key)
      return null
    }
    return entry
  }

  let reads = 0
  const port: RedisPort = {
    async get(key) {
      reads += 1
      return live(key)?.value ?? null
    },
    async setWithTtl(key, value, ttlSeconds) {
      data.set(key, { value, expiresAt: Date.now() + ttlSeconds * 1000 })
    },
    async setIfAbsent(key, value, ttlMs) {
      if (live(key)) return false
      data.set(key, { value, expiresAt: Date.now() + ttlMs })
      return true
    },
    async del(key) {
      data.delete(key)
    },
    async delIfEquals(key, value) {
      if (live(key)?.value === value) data.delete(key)
    },
  }

  return {
    port,
    /** Toàn bộ giá trị đang lưu — để khẳng định KHÔNG có token dạng rõ trong Redis. */
    dump: () => [...data.entries()].map(([key, { value }]) => ({ key, value })),
    keys: () => [...data.keys()],
    /** Số lần đọc Redis — để khẳng định cookie rác không chạm tới Redis. */
    reads: () => reads,
  }
}
