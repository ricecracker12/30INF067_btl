import { describe, expect, it, vi } from "vitest"

import { ApiError, NetworkError } from "@/lib/api/problem"

import {
  createRefreshCoordinator,
  SessionExpiredError,
  type AuthMessage,
} from "./refresh-coordinator"

/** Coordinator với store giả trong bộ nhớ và hàm refresh do test điều khiển. */
function setup(refresh: () => Promise<{ accessToken: string }>) {
  let token: string | null = null
  const deps = {
    refresh: vi.fn(refresh),
    getToken: () => token,
    setToken: vi.fn((t: string) => {
      token = t
    }),
    endSession: vi.fn(() => {
      token = null
    }),
  }
  return {
    deps,
    coordinator: createRefreshCoordinator(deps),
    setToken: (t: string | null) => (token = t),
  }
}

describe("createRefreshCoordinator — lớp 1 (trong tab)", () => {
  it("3 lời gọi đồng thời → 1 refresh, cả 3 nhận cùng token", async () => {
    let release!: (v: { accessToken: string }) => void
    const { deps, coordinator } = setup(() => new Promise((r) => (release = r)))

    const calls = [
      coordinator.getFreshToken(null),
      coordinator.getFreshToken(null),
      coordinator.getFreshToken(null),
    ]
    release({ accessToken: "moi" })

    await expect(Promise.all(calls)).resolves.toEqual(["moi", "moi", "moi"])
    expect(deps.refresh).toHaveBeenCalledTimes(1)
    expect(deps.setToken).toHaveBeenCalledWith("moi")
  })

  it("xong một lượt thì lượt sau gọi refresh mới (không nhớ promise cũ mãi)", async () => {
    let n = 0
    const { deps, coordinator, setToken } = setup(async () => ({
      accessToken: `t${++n}`,
    }))

    await expect(coordinator.getFreshToken(null)).resolves.toBe("t1")
    // Token t1 vừa bị server từ chối → đưa t1 làm `stale` → phải refresh lại.
    await expect(coordinator.getFreshToken("t1")).resolves.toBe("t2")
    expect(deps.refresh).toHaveBeenCalledTimes(2)
    setToken(null)
  })

  it("đã có token khác `stale` (khởi động khi đã đăng nhập sẵn) → dùng luôn, KHÔNG gọi refresh", async () => {
    const { deps, coordinator, setToken } = setup(async () => ({
      accessToken: "x",
    }))
    setToken("dang-co")

    await expect(coordinator.getFreshToken(null)).resolves.toBe("dang-co")
    await expect(coordinator.getFreshToken("cu")).resolves.toBe("dang-co")
    expect(deps.refresh).not.toHaveBeenCalled()
  })

  it("refresh 401 → endSession + SessionExpiredError", async () => {
    const { deps, coordinator } = setup(async () => {
      throw new ApiError(401, null)
    })

    await expect(coordinator.getFreshToken(null)).rejects.toBeInstanceOf(
      SessionExpiredError
    )
    expect(deps.endSession).toHaveBeenCalledTimes(1)
  })

  it.each([
    ["429", new ApiError(429, null)],
    ["500", new ApiError(500, null)],
    ["mất mạng", new NetworkError("x")],
  ])("refresh %s → ném nguyên lỗi, KHÔNG kết thúc phiên", async (_, error) => {
    const { deps, coordinator } = setup(async () => {
      throw error
    })

    await expect(coordinator.getFreshToken(null)).rejects.toBe(error)
    expect(deps.endSession).not.toHaveBeenCalled()
  })

  it("request bay đi với token, giờ token đã bị xóa (phiên kết thúc trong lúc chờ) → SessionExpiredError, KHÔNG refresh", async () => {
    const { deps, coordinator } = setup(async () => ({ accessToken: "x" }))

    await expect(coordinator.getFreshToken("token-cu")).rejects.toBeInstanceOf(
      SessionExpiredError
    )
    expect(deps.refresh).not.toHaveBeenCalled()
  })
})

// ── Lớp 2: giữa các tab ──────────────────────────────────────────────────────────────────────────────────────

/** Web Locks giả: một mutex theo tên, cấp lần lượt như `navigator.locks.request`. */
function fakeLocks() {
  let tail: Promise<unknown> = Promise.resolve()
  return {
    request<T>(_name: string, cb: () => Promise<T>): Promise<T> {
      const run = tail.then(cb, cb)
      tail = run.catch(() => undefined)
      return run
    },
  }
}

/** BroadcastChannel giả: tin gửi từ một đầu tới MỌI đầu khác (không tới chính nó), bất đồng bộ như thật. */
function fakeBus() {
  const endpoints: ((m: AuthMessage) => void)[][] = []
  return function endpoint() {
    const listeners: ((m: AuthMessage) => void)[] = []
    endpoints.push(listeners)
    return {
      postMessage(message: AuthMessage) {
        for (const other of endpoints)
          if (other !== listeners)
            other.forEach((l) => queueMicrotask(() => l(message)))
      },
      addEventListener(
        _type: "message",
        listener: (event: MessageEvent<AuthMessage>) => void
      ) {
        listeners.push((data) =>
          listener({ data } as MessageEvent<AuthMessage>)
        )
      },
    }
  }
}

/** Một "tab": store riêng, coordinator riêng, chung khóa + kênh + server. */
function tab(
  shared: {
    locks: ReturnType<typeof fakeLocks>
    bus: ReturnType<typeof fakeBus>
    refresh: () => Promise<{ accessToken: string }>
  },
  initialToken: string | null
) {
  let token = initialToken
  const endSession = vi.fn(() => {
    token = null
  })
  const coordinator = createRefreshCoordinator({
    refresh: shared.refresh,
    getToken: () => token,
    setToken: (t) => {
      token = t
    },
    endSession,
    locks: shared.locks,
    channel: shared.bus(),
  })
  return { coordinator, endSession, token: () => token }
}

describe("createRefreshCoordinator — lớp 2 (giữa các tab, Đ-E4)", () => {
  it("hai tab cùng nhận 401 với CÙNG token → tổng 1 refresh; tab thứ hai trả token do tab thứ nhất phát", async () => {
    let n = 0
    const refresh = vi.fn(async () => ({ accessToken: `T${++n}` }))
    const shared = { locks: fakeLocks(), bus: fakeBus(), refresh }
    const a = tab(shared, "T0")
    const b = tab(shared, "T0")

    const [ta, tb] = await Promise.all([
      a.coordinator.getFreshToken("T0"),
      b.coordinator.getFreshToken("T0"),
    ])

    expect(refresh).toHaveBeenCalledTimes(1)
    expect(ta).toBe("T1")
    expect(tb).toBe("T1")
    expect(b.token()).toBe("T1")
  })

  it("ba tab → vẫn 1 refresh", async () => {
    let n = 0
    const refresh = vi.fn(async () => ({ accessToken: `T${++n}` }))
    const shared = { locks: fakeLocks(), bus: fakeBus(), refresh }
    const tabs = [tab(shared, "T0"), tab(shared, "T0"), tab(shared, "T0")]

    const tokens = await Promise.all(
      tabs.map((t) => t.coordinator.getFreshToken("T0"))
    )

    expect(refresh).toHaveBeenCalledTimes(1)
    expect(tokens).toEqual(["T1", "T1", "T1"])
  })

  it("tab thắng khóa nhận refresh 401 → phát logout; tab đang chờ khóa kết thúc phiên, KHÔNG gọi refresh", async () => {
    const refresh = vi.fn(async (): Promise<{ accessToken: string }> => {
      throw new ApiError(401, null)
    })
    const shared = { locks: fakeLocks(), bus: fakeBus(), refresh }
    const a = tab(shared, "T0")
    const b = tab(shared, "T0")

    const results = await Promise.allSettled([
      a.coordinator.getFreshToken("T0"),
      b.coordinator.getFreshToken("T0"),
    ])

    expect(refresh).toHaveBeenCalledTimes(1)
    for (const r of results)
      expect((r as PromiseRejectedResult).reason).toBeInstanceOf(
        SessionExpiredError
      )
    expect(b.endSession).toHaveBeenCalled()
    expect(b.token()).toBeNull()
  })

  it("kênh nhận { type: 'logout' } → endSession (đăng xuất ở tab khác)", async () => {
    const shared = {
      locks: fakeLocks(),
      bus: fakeBus(),
      refresh: vi.fn(async () => ({ accessToken: "x" })),
    }
    const a = tab(shared, "T0")
    const b = tab(shared, "T0")

    a.coordinator.announceLogout()
    await Promise.resolve()

    expect(b.endSession).toHaveBeenCalledTimes(1)
    expect(b.token()).toBeNull()
    // Không vọng lại chính tab gửi.
    expect(a.endSession).not.toHaveBeenCalled()
  })
})
