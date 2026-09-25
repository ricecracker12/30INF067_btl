"use client"

import { useCallback, useState } from "react"

import { errorMessage } from "@/lib/api/messages"
import { notificationApi } from "@/lib/api/notification-api"
import type { NotificationResponse } from "@/lib/api/types"

import { refreshUnread, unreadStore } from "./unread-store"

/**
 * Đánh dấu đã đọc — OPTIMISTIC có rollback (Đ-6.21): chấm tắt ngay, badge −1 ngay; server lỗi thì bật lại cả hai và nói một câu.
 * Trạng thái đọc giữ ở lớp phủ theo id, không sửa mảng danh sách: danh sách thuộc `useCursorPages` (khử trùng, nối trang), lớp phủ
 * chỉ nói "id này đã đọc dù trang cũ nói chưa".
 */
export function useReadMarks() {
  const [overrides, setOverrides] = useState<Record<string, boolean>>({})
  const [error, setError] = useState<string | null>(null)

  const isRead = useCallback(
    (n: NotificationResponse) => overrides[n.notificationId] ?? n.isRead,
    [overrides]
  )

  const setMany = useCallback(
    (ids: string[], value: boolean | undefined) =>
      setOverrides((prev) => {
        const next = { ...prev }
        for (const id of ids) {
          if (value === undefined) delete next[id]
          else next[id] = value
        }
        return next
      }),
    []
  )

  /** Idempotent phía FE: đã đọc thì không gọi (server cũng idempotent, nhưng không tốn một request vô ích). */
  const markRead = useCallback(
    async (n: NotificationResponse) => {
      if (isRead(n)) return
      setError(null)
      setMany([n.notificationId], true)
      unreadStore.adjust(-1)
      try {
        await notificationApi.markRead(n.notificationId)
      } catch (e) {
        setMany([n.notificationId], undefined)
        unreadStore.adjust(+1)
        setError(errorMessage("notification-read", e))
      }
    },
    [isRead, setMany]
  )

  /**
   * `upTo` = `updatedAt` của thông báo MỚI NHẤT đang hiển thị (notification-v1, luồng chuông bước 4) — nhóm tới sau lúc đó vẫn
   * chưa đọc (NOTIF-07). Sau cùng hỏi lại số thật: nhóm chưa nạp trang cũng vừa được đánh dấu.
   */
  const markAll = useCallback(
    async (items: NotificationResponse[]) => {
      if (items.length === 0) return
      // So bằng thời điểm, không bằng chuỗi: số chữ số phần lẻ giây không cố định, so chuỗi là sai thứ tự.
      const upTo = items.reduce(
        (max, n) => (Date.parse(n.updatedAt) > Date.parse(max) ? n.updatedAt : max),
        items[0].updatedAt
      )
      const ids = items.filter((n) => !isRead(n)).map((n) => n.notificationId)
      setError(null)
      setMany(ids, true)
      try {
        await notificationApi.readAll(upTo)
      } catch (e) {
        setMany(ids, undefined)
        setError(errorMessage("notification-read", e))
      } finally {
        void refreshUnread()
      }
    },
    [isRead, setMany]
  )

  return { isRead, markRead, markAll, error }
}
