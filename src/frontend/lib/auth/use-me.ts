"use client"

import { useEffect, useSyncExternalStore } from "react"

import { INITIAL_ME, meStore, refreshMe, type MeState } from "./me-store"
import { tokenStore } from "./token-store"

// Nối store `/me` với trình duyệt và phiên (GĐ6 Đ-6.11): nạp lại khi cửa sổ lấy lại focus hoặc tab hiện lại; xóa khi phiên kết
// thúc. Đăng ký MỘT lần cho mọi người dùng của hook (đếm người dùng — header, guard, nút theo quyền cùng lúc), gỡ khi không còn ai.

let users = 0
let unwire: (() => void) | null = null

function wire(): () => void {
  const onFocus = () => void refreshMe()
  const onVisible = () => {
    if (document.visibilityState === "visible") void refreshMe()
  }
  window.addEventListener("focus", onFocus)
  document.addEventListener("visibilitychange", onVisible)
  const offSession = tokenStore.subscribe(() => {
    if (tokenStore.getSession().status === "anonymous") meStore.reset()
  })
  return () => {
    window.removeEventListener("focus", onFocus)
    document.removeEventListener("visibilitychange", onVisible)
    offSession()
  }
}

/**
 * `me` + trạng thái cho component (`useSyncExternalStore`, khuôn `useSession`). Lần dùng đầu (còn `unknown`) tự nạp. Server
 * render không có `me` → snapshot server là `unknown`, khớp HTML lúc hydrate.
 */
export function useMe(): MeState {
  useEffect(() => {
    users += 1
    if (users === 1) unwire = wire()
    if (meStore.getState().status === "unknown") void refreshMe()
    return () => {
      users -= 1
      if (users === 0) {
        unwire?.()
        unwire = null
      }
    }
  }, [])

  return useSyncExternalStore(
    meStore.subscribe,
    meStore.getState,
    () => INITIAL_ME
  )
}
