import { describe, expect, it, vi } from "vitest"

import { ApiError, NetworkError } from "@/lib/api/problem"

import {
  createRefreshCoordinator,
  SessionExpiredError,
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
})
