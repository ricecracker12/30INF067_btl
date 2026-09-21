// @vitest-environment node
import { randomBytes } from "node:crypto"

import { describe, expect, it, vi } from "vitest"

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
    const key = randomBytes(32)
    const sid = newSessionId()
    const lockKeys = () => redis.keys().filter((k) => k.startsWith("bff:lock:"))

    // HAI store trên CÙNG một Redis, khác nhau đúng `lockMs` — đó là chỗ kịch bản này sống:
    //   chủ cũ  : TTL siêu ngắn, mô phỏng "việc chạy lâu hơn khóa" nên khóa tự hết hạn giữa chừng.
    //   chủ mới : TTL bình thường, KHÔNG được hết hạn giữa lúc đang khẳng định.
    // Bản cũ dùng một store TTL 20ms cho cả hai rồi `setTimeout` cố định — đỏ ~2/8 lượt khi chạy cả
    // bộ, theo hai đường: (1) chủ mới chưa kịp lấy khóa lúc assert, (2) khóa chủ mới hết hạn trước
    // `delIfEquals`, và `live()` trong mocks/redis.ts dọn nó đi. Cả hai đều là đua đồng hồ thật, không
    // phải lỗi của `withLock`. Nay chờ theo ĐIỀU KIỆN, không theo thời gian.
    const storeCu = createRedisSessionStore(redis.port, key, {
      lockMs: 20,
      waitMs: 1_000,
      pollMs: 5,
    })
    const storeMoi = createRedisSessionStore(redis.port, key, {
      lockMs: 5_000,
      waitMs: 1_000,
      pollMs: 5,
    })

    let releaseOld: (() => void) | undefined
    const old = storeCu.withLock(
      sid,
      () => new Promise<void>((r) => (releaseOld = r))
    )
    // `fn` chỉ chạy SAU khi lấy được khóa — nên có `releaseOld` nghĩa là chủ cũ đang giữ khóa.
    await vi.waitFor(() => expect(releaseOld).toBeDefined())

    // Chủ mới chờ khóa cũ hết hạn (TTL 20ms) rồi lấy. Không đoán mất bao lâu — chờ tới khi `fn` chạy.
    let releaseNew: (() => void) | undefined
    const newer = storeMoi.withLock(
      sid,
      () => new Promise<void>((r) => (releaseNew = r))
    )
    await vi.waitFor(() => expect(releaseNew).toBeDefined())

    // Giờ chủ cũ mới xong việc: `delIfEquals` phải thấy owner đã khác và KHÔNG xóa.
    releaseOld!()
    await old

    expect(lockKeys()).toHaveLength(1)

    releaseNew!()
    await newer
    // Chủ mới xong thì khóa của CHÍNH nó được xóa — không còn khóa nào sót lại.
    expect(lockKeys()).toHaveLength(0)
  })
})
