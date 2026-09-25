"use client"

import { useEffect, useSyncExternalStore } from "react"

import { refreshUnread, unreadStore } from "./unread-store"

/** Chu kỳ hỏi lại (Đ-6.18) — cũng là đường lùi vĩnh viễn khi hub thông báo không nối được. */
export const UNREAD_POLL_MS = 30_000

/**
 * Badge chuông: hỏi `unread-count` ngay khi gắn, rồi mỗi 30 giây; **dừng khi tab ẩn** (không đốt hạn mức 100 req/phút cho tab
 * không ai nhìn) và **nạp ngay khi tab hiện lại / cửa sổ lấy lại focus**.
 *
 * Timer tạo TRONG effect và dọn ở cleanup (luật frontend #14): StrictMode mount → unmount → mount để lại đúng MỘT interval.
 */
export function useUnreadCount(): number | null {
  useEffect(() => {
    let timer: ReturnType<typeof setInterval> | null = null
    const tick = () => void refreshUnread()
    const start = () => {
      timer ??= setInterval(tick, UNREAD_POLL_MS)
    }
    const stop = () => {
      if (timer !== null) clearInterval(timer)
      timer = null
    }
    const onVisibility = () => {
      if (document.visibilityState === "visible") {
        tick()
        start()
      } else stop()
    }

    if (document.visibilityState === "visible") {
      tick()
      start()
    }
    document.addEventListener("visibilitychange", onVisibility)
    window.addEventListener("focus", tick)
    return () => {
      stop()
      document.removeEventListener("visibilitychange", onVisibility)
      window.removeEventListener("focus", tick)
    }
  }, [])

  return useSyncExternalStore(unreadStore.subscribe, unreadStore.get, () => null)
}
