import { afterEach, describe, expect, it, vi } from "vitest"

import { tokenStore } from "./token-store"

afterEach(() => {
  tokenStore.set(null)
})

describe("tokenStore", () => {
  it("get trả về giá trị mới nhất đã set", () => {
    expect(tokenStore.get()).toBeNull()

    tokenStore.set("token-1")
    expect(tokenStore.get()).toBe("token-1")

    tokenStore.set("token-2")
    expect(tokenStore.get()).toBe("token-2")

    tokenStore.set(null)
    expect(tokenStore.get()).toBeNull()
  })

  it("set giá trị MỚI thì báo cho mọi listener", () => {
    const a = vi.fn()
    const b = vi.fn()
    tokenStore.subscribe(a)
    tokenStore.subscribe(b)

    tokenStore.set("token-1")

    expect(a).toHaveBeenCalledTimes(1)
    expect(b).toHaveBeenCalledTimes(1)
  })

  it("set CÙNG giá trị thì không báo — nếu không useSyncExternalStore render lại vô ích", () => {
    const listener = vi.fn()
    tokenStore.set("token-1")
    tokenStore.subscribe(listener)

    tokenStore.set("token-1")

    expect(listener).not.toHaveBeenCalled()
  })

  it("hủy đăng ký thì thôi báo, và không đụng tới listener còn lại", () => {
    const bo = vi.fn()
    const giu = vi.fn()
    const huy = tokenStore.subscribe(bo)
    tokenStore.subscribe(giu)

    huy()
    tokenStore.set("token-1")

    expect(bo).not.toHaveBeenCalled()
    expect(giu).toHaveBeenCalledTimes(1)
  })

  it("listener đọc được giá trị mới ngay trong lúc được báo", () => {
    const thayDuoc: (string | null)[] = []
    tokenStore.subscribe(() => {
      thayDuoc.push(tokenStore.get())
    })

    tokenStore.set("token-1")
    tokenStore.set(null)

    expect(thayDuoc).toEqual(["token-1", null])
  })
})
