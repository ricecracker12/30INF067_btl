import { beforeEach, describe, expect, it, vi } from "vitest"

import { profile } from "@/mocks/fixtures"

import { INITIAL_PROFILE, profileStore } from "./profile-store"

beforeEach(() => profileStore.reset())

describe("profileStore", () => {
  it("tab vừa mở là `unknown`, chưa có hồ sơ nào", () => {
    expect(profileStore.getState()).toEqual(INITIAL_PROFILE)
  })

  it("báo subscriber khi đổi, và KHÔNG báo khi đặt lại đúng trạng thái cũ", () => {
    const listener = vi.fn()
    const unsubscribe = profileStore.subscribe(listener)

    profileStore.setReady(profile)
    expect(listener).toHaveBeenCalledTimes(1)

    // Cùng object → snapshot không đổi → `useSyncExternalStore` không phải render lại.
    profileStore.setReady(profile)
    expect(listener).toHaveBeenCalledTimes(1)

    profileStore.setMissing()
    expect(listener).toHaveBeenCalledTimes(2)

    unsubscribe()
    profileStore.setReady(profile)
    expect(listener).toHaveBeenCalledTimes(2)
  })

  it("`missing` và `error` KHÔNG giữ lại hồ sơ cũ — không trạng thái nào hiện dữ liệu đã lỗi thời", () => {
    profileStore.setReady(profile)

    profileStore.setMissing()
    expect(profileStore.getState()).toEqual({
      status: "missing",
      profile: null,
    })

    profileStore.setReady(profile)
    profileStore.markError()
    expect(profileStore.getState()).toEqual({ status: "error", profile: null })
  })

  it("`missing` khác `error`: guard đọc hai trạng thái này ra hai hành động khác nhau", () => {
    profileStore.setMissing()
    expect(profileStore.getState().status).toBe("missing")
    profileStore.markError()
    expect(profileStore.getState().status).toBe("error")
  })

  it("markUnknown đưa `error` về `unknown` để nút Thử lại nạp lại", () => {
    profileStore.markError()
    profileStore.markUnknown()
    expect(profileStore.getState().status).toBe("unknown")
  })

  it("snapshot là object MỚI mỗi lần đổi (useSyncExternalStore so bằng Object.is)", () => {
    const truoc = profileStore.getState()
    profileStore.setReady(profile)
    expect(profileStore.getState()).not.toBe(truoc)
  })
})
