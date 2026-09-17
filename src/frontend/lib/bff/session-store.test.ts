// @vitest-environment node
import { randomBytes } from "node:crypto"

import { describe, expect, it } from "vitest"

import { createFakeRedis } from "@/mocks/redis"

import { newSessionId } from "./crypto"
import {
  createRedisSessionStore,
  SessionLockTimeoutError,
} from "./session-store"

const fast = { lockMs: 200, waitMs: 100, pollMs: 5 }

describe("createRedisSessionStore", () => {
  it("set/get/delete; TTL theo refresh cookie; giá trị lưu là bản mã", async () => {
    const redis = createFakeRedis()
    const store = createRedisSessionStore(redis.port, randomBytes(32), fast)
    const sid = newSessionId()

    await store.set(sid, { accessToken: "acc", refreshToken: "ref" }, 60)

    expect(await store.get(sid)).toEqual({
      accessToken: "acc",
      refreshToken: "ref",
    })
    expect(redis.dump()[0].value).not.toMatch(/acc|ref/)
    await store.delete(sid)
    expect(await store.get(sid)).toBeNull()
  })

  it("giá trị bị sửa trong Redis → null và bị dọn", async () => {
    const redis = createFakeRedis()
    const store = createRedisSessionStore(redis.port, randomBytes(32), fast)
    const sid = newSessionId()
    await store.set(sid, { accessToken: "acc", refreshToken: "ref" }, 60)
    const [{ key }] = redis.dump()
    await redis.port.setWithTtl(key, "gia-tri-la", 60)

    expect(await store.get(sid)).toBeNull()
    expect(redis.keys()).toEqual([])
  })

  it("withLock: hai tác vụ cùng phiên chạy LẦN LƯỢT; phiên khác không phải chờ", async () => {
    const store = createRedisSessionStore(
      createFakeRedis().port,
      randomBytes(32),
      {
        ...fast,
        waitMs: 2_000,
      }
    )
    const a = newSessionId()
    const b = newSessionId()
    const log: string[] = []
    const task = (name: string, ms: number) => async () => {
      log.push(`${name}:vào`)
      await new Promise((r) => setTimeout(r, ms))
      log.push(`${name}:ra`)
    }

    await Promise.all([
      store.withLock(a, task("a1", 40)),
      store.withLock(a, task("a2", 1)),
      store.withLock(b, task("b", 1)),
    ])

    expect(log.indexOf("a2:vào")).toBeGreaterThan(log.indexOf("a1:ra"))
    expect(log.indexOf("b:ra")).toBeLessThan(log.indexOf("a1:ra"))
  })

  it("chờ khóa quá hạn → SessionLockTimeoutError; tác vụ ném lỗi vẫn nhả khóa", async () => {
    const redis = createFakeRedis()
    const store = createRedisSessionStore(redis.port, randomBytes(32), fast)
    const sid = newSessionId()

    await expect(
      store.withLock(sid, async () => {
        throw new Error("hỏng")
      })
    ).rejects.toThrow("hỏng")
    expect(redis.keys()).toEqual([])

    let release!: () => void
    const held = store.withLock(
      sid,
      () => new Promise<void>((r) => (release = r))
    )
    await expect(
      store.withLock(sid, async () => "không tới")
    ).rejects.toBeInstanceOf(SessionLockTimeoutError)
    release()
    await held
  })

  it("khóa hết hạn rồi bị tiến trình khác lấy: chủ cũ xong việc KHÔNG xóa khóa của người mới", async () => {
    const redis = createFakeRedis()
    const store = createRedisSessionStore(redis.port, randomBytes(32), {
      lockMs: 20,
      waitMs: 1_000,
      pollMs: 5,
    })
    const sid = newSessionId()

    let releaseOld!: () => void
    const old = store.withLock(
      sid,
      () => new Promise<void>((r) => (releaseOld = r))
    )
    await new Promise((r) => setTimeout(r, 40)) // khóa của chủ cũ đã hết hạn

    let releaseNew!: () => void
    const newer = store.withLock(
      sid,
      () => new Promise<void>((r) => (releaseNew = r))
    )
    await new Promise((r) => setTimeout(r, 10))
    releaseOld()
    await old

    expect(redis.keys().some((k) => k.startsWith("bff:lock:"))).toBe(true)
    releaseNew()
    await newer
  })
})
