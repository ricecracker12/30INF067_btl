import { afterEach, describe, expect, it, vi } from "vitest"

import { INITIAL_SESSION, tokenStore } from "./token-store"

afterEach(() => {
  tokenStore.reset()
})

describe("tokenStore — token", () => {
  it("get trả về token mới nhất", () => {
    expect(tokenStore.get()).toBeNull()

    tokenStore.startSession("token-1")
    expect(tokenStore.get()).toBe("token-1")

    tokenStore.startSession("token-2")
    expect(tokenStore.get()).toBe("token-2")

    tokenStore.endSession("logout")
    expect(tokenStore.get()).toBeNull()
  })

  it("đổi giá trị thì báo cho mọi listener", () => {
    const a = vi.fn()
    const b = vi.fn()
    tokenStore.subscribe(a)
    tokenStore.subscribe(b)

    tokenStore.startSession("token-1")

    expect(a).toHaveBeenCalledTimes(1)
    expect(b).toHaveBeenCalledTimes(1)
  })

  it("không đổi gì thì không báo, snapshot giữ nguyên object — nếu không useSyncExternalStore render lại vô ích", () => {
    const listener = vi.fn()
    tokenStore.startSession("token-1")
    const before = tokenStore.getSession()
    tokenStore.subscribe(listener)

    tokenStore.startSession("token-1")

    expect(listener).not.toHaveBeenCalled()
    expect(tokenStore.getSession()).toBe(before)
  })

  it("hủy đăng ký thì thôi báo, và không đụng tới listener còn lại", () => {
    const bo = vi.fn()
    const giu = vi.fn()
    const huy = tokenStore.subscribe(bo)
    tokenStore.subscribe(giu)

    huy()
    tokenStore.startSession("token-1")

    expect(bo).not.toHaveBeenCalled()
    expect(giu).toHaveBeenCalledTimes(1)
  })

  it("listener đọc được giá trị mới ngay trong lúc được báo", () => {
    const thayDuoc: (string | null)[] = []
    tokenStore.subscribe(() => {
      thayDuoc.push(tokenStore.get())
    })

    tokenStore.startSession("token-1")
    tokenStore.endSession("expired")

    expect(thayDuoc).toEqual(["token-1", null])
  })
})

describe("tokenStore — trạng thái phiên (E6 bước 1)", () => {
  it("tab mới mở: unknown, chưa có token", () => {
    expect(tokenStore.getSession()).toEqual({
      token: null,
      status: "unknown",
      endedBy: null,
    })
    expect(tokenStore.getSession()).toBe(INITIAL_SESSION)
  })

  it("unknown → authenticated → anonymous (đăng xuất) → authenticated (đăng nhập lại) xóa endedBy", () => {
    tokenStore.startSession("t1")
    expect(tokenStore.getSession()).toEqual({
      token: "t1",
      status: "authenticated",
      endedBy: null,
    })

    tokenStore.endSession("logout")
    expect(tokenStore.getSession()).toEqual({
      token: null,
      status: "anonymous",
      endedBy: "logout",
    })

    tokenStore.startSession("t2")
    expect(tokenStore.getSession().endedBy).toBeNull()
  })

  it("unknown → error → unknown (Thử lại): không có token nào bị bịa ra", () => {
    tokenStore.markError()
    expect(tokenStore.getSession()).toMatchObject({
      token: null,
      status: "error",
    })

    tokenStore.markUnknown()
    expect(tokenStore.getSession()).toMatchObject({
      token: null,
      status: "unknown",
    })
  })
})
