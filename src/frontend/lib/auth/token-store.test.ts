import { afterEach, describe, expect, it, vi } from "vitest"

import { INITIAL_SESSION, tokenStore } from "./token-store"

afterEach(() => {
  tokenStore.reset()
})

describe("tokenStore — đăng ký theo dõi", () => {
  it("đổi trạng thái thì báo cho mọi listener", () => {
    const a = vi.fn()
    const b = vi.fn()
    tokenStore.subscribe(a)
    tokenStore.subscribe(b)

    tokenStore.startSession()

    expect(a).toHaveBeenCalledTimes(1)
    expect(b).toHaveBeenCalledTimes(1)
  })

  it("không đổi gì thì không báo, snapshot giữ nguyên object — nếu không useSyncExternalStore render lại vô ích", () => {
    const listener = vi.fn()
    tokenStore.startSession()
    const before = tokenStore.getSession()
    tokenStore.subscribe(listener)

    tokenStore.startSession()

    expect(listener).not.toHaveBeenCalled()
    expect(tokenStore.getSession()).toBe(before)
  })

  it("hủy đăng ký thì thôi báo, và không đụng tới listener còn lại", () => {
    const bo = vi.fn()
    const giu = vi.fn()
    const huy = tokenStore.subscribe(bo)
    tokenStore.subscribe(giu)

    huy()
    tokenStore.startSession()

    expect(bo).not.toHaveBeenCalled()
    expect(giu).toHaveBeenCalledTimes(1)
  })

  it("listener đọc được trạng thái mới ngay trong lúc được báo", () => {
    const thayDuoc: string[] = []
    tokenStore.subscribe(() => {
      thayDuoc.push(tokenStore.getSession().status)
    })

    tokenStore.startSession()
    tokenStore.endSession("expired")

    expect(thayDuoc).toEqual(["authenticated", "anonymous"])
  })
})

describe("tokenStore — trạng thái phiên (E6 bước 1)", () => {
  it("tab mới mở: unknown", () => {
    expect(tokenStore.getSession()).toEqual({
      status: "unknown",
      endedBy: null,
    })
    expect(tokenStore.getSession()).toBe(INITIAL_SESSION)
  })

  it("Đ-E14: store KHÔNG có chỗ nào chứa token", () => {
    tokenStore.startSession()
    expect(Object.keys(tokenStore.getSession()).sort()).toEqual([
      "endedBy",
      "status",
    ])
    expect("get" in tokenStore).toBe(false)
  })

  it("unknown → authenticated → anonymous (đăng xuất) → authenticated (đăng nhập lại) xóa endedBy", () => {
    tokenStore.startSession()
    expect(tokenStore.getSession()).toEqual({
      status: "authenticated",
      endedBy: null,
    })

    tokenStore.endSession("logout")
    expect(tokenStore.getSession()).toEqual({
      status: "anonymous",
      endedBy: "logout",
    })

    tokenStore.startSession()
    expect(tokenStore.getSession().endedBy).toBeNull()
  })

  it("unknown → error → unknown (Thử lại)", () => {
    tokenStore.markError()
    expect(tokenStore.getSession().status).toBe("error")

    tokenStore.markUnknown()
    expect(tokenStore.getSession().status).toBe("unknown")
  })
})
