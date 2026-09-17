import "server-only"

import { randomUUID } from "node:crypto"

import { hashSessionId, open, seal } from "./crypto"

// Đ-E14 — nơi Next server giữ token của từng phiên. Trình duyệt chỉ giữ session ID.

export type StoredSession = Readonly<{
  accessToken: string
  refreshToken: string
}>

export interface SessionStore {
  get(sid: string): Promise<StoredSession | null>
  set(sid: string, session: StoredSession, ttlSeconds: number): Promise<void>
  delete(sid: string): Promise<void>
  /**
   * Chạy `fn` độc quyền theo phiên — refresh single-flight. Khóa ở Redis nên đúng cả khi chạy nhiều instance Next và khi
   * nhiều tab cùng nhận 401 (lớp giữa tab của Đ-E4 giờ nằm ở server).
   */
  withLock<T>(sid: string, fn: () => Promise<T>): Promise<T>
}

/** Tối thiểu cần từ Redis — test cắm bản giả, production cắm ioredis (deps.ts). */
export interface RedisPort {
  get(key: string): Promise<string | null>
  setWithTtl(key: string, value: string, ttlSeconds: number): Promise<void>
  /** SET key value PX ms NX — true khi lấy được khóa. */
  setIfAbsent(key: string, value: string, ttlMs: number): Promise<boolean>
  del(key: string): Promise<void>
  /** Xóa khóa CHỈ khi còn là của mình — hết hạn rồi bị người khác lấy thì không xóa nhầm. */
  delIfEquals(key: string, value: string): Promise<void>
}

type LockOptions = { lockMs: number; waitMs: number; pollMs: number }

const DEFAULT_LOCK: LockOptions = {
  // Dài hơn timeout gọi API (10 giây, upstream.ts): khóa không tự hết hạn giữa lúc đang refresh.
  lockMs: 15_000,
  waitMs: 20_000,
  pollMs: 50,
}

export class SessionLockTimeoutError extends Error {
  constructor() {
    super("Không lấy được khóa phiên để refresh.")
    this.name = "SessionLockTimeoutError"
  }
}

const isStoredSession = (value: unknown): value is StoredSession =>
  typeof value === "object" &&
  value !== null &&
  typeof (value as StoredSession).accessToken === "string" &&
  typeof (value as StoredSession).refreshToken === "string"

export function createRedisSessionStore(
  redis: RedisPort,
  encryptionKey: Buffer,
  lock: LockOptions = DEFAULT_LOCK
): SessionStore {
  const dataKey = (hash: string) => `bff:session:${hash}`
  const lockKey = (hash: string) => `bff:lock:${hash}`

  return {
    async get(sid) {
      const hash = hashSessionId(sid)
      const sealed = await redis.get(dataKey(hash))
      if (sealed === null) return null
      const plain = open(sealed, encryptionKey, hash)
      let parsed: unknown = null
      try {
        parsed = plain === null ? null : JSON.parse(plain)
      } catch {
        parsed = null
      }
      if (!isStoredSession(parsed)) {
        // Sai khóa (đổi SESSION_ENCRYPTION_KEY, khởi động lại dev) hoặc bị sửa: bỏ luôn, người dùng đăng nhập lại.
        await redis.del(dataKey(hash))
        return null
      }
      return {
        accessToken: parsed.accessToken,
        refreshToken: parsed.refreshToken,
      }
    },

    async set(sid, session, ttlSeconds) {
      const hash = hashSessionId(sid)
      const plain = JSON.stringify({
        accessToken: session.accessToken,
        refreshToken: session.refreshToken,
      })
      await redis.setWithTtl(
        dataKey(hash),
        seal(plain, encryptionKey, hash),
        ttlSeconds
      )
    },

    async delete(sid) {
      await redis.del(dataKey(hashSessionId(sid)))
    },

    async withLock(sid, fn) {
      const key = lockKey(hashSessionId(sid))
      const owner = randomUUID()
      const deadline = Date.now() + lock.waitMs
      while (!(await redis.setIfAbsent(key, owner, lock.lockMs))) {
        if (Date.now() >= deadline) throw new SessionLockTimeoutError()
        await new Promise((r) => setTimeout(r, lock.pollMs))
      }
      try {
        return await fn()
      } finally {
        await redis.delIfEquals(key, owner)
      }
    },
  }
}
