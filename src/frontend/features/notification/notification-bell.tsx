"use client"

import { BellIcon } from "lucide-react"
import Link from "next/link"
import { useEffect, useState } from "react"

import { FormAlert } from "@/components/form/form-alert"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import {
  Popover,
  PopoverContent,
  PopoverTrigger,
} from "@/components/ui/popover"
import { Skeleton } from "@/components/ui/skeleton"
import { errorMessage } from "@/lib/api/messages"
import { notificationApi } from "@/lib/api/notification-api"
import type { NotificationResponse } from "@/lib/api/types"

import { NotificationItem } from "./notification-item"
import { badgeText } from "./notification-text"
import { useReadMarks } from "./use-read-marks"
import { useUnreadCount } from "./use-unread-count"

/** Số nhóm hiện trong popover (B.8 E3) — muốn xem thêm thì sang `/notifications`. */
export const BELL_PAGE_SIZE = 10

type Loaded =
  | { key: number; items: NotificationResponse[] }
  | { key: number; error: string }

/**
 * Chuông ở header (GĐ6 E3): badge chưa đọc hỏi lại 30 giây (Đ-6.18), mở ra là 10 nhóm mới nhất — nạp MỖI lần mở, không giữ bản cũ
 * (nhóm vừa có sự kiện mới đã đổi chỗ). Bấm một dòng → đã đọc (optimistic) + điều hướng + đóng popover.
 */
export function NotificationBell() {
  const total = useUnreadCount()
  const [open, setOpen] = useState(false)
  const [opened, setOpened] = useState(0)
  const [data, setData] = useState<Loaded | null>(null)
  const marks = useReadMarks()

  const current = data?.key === opened ? data : null

  useEffect(() => {
    if (!open) return
    const controller = new AbortController()
    const key = opened
    notificationApi.list({ limit: BELL_PAGE_SIZE }, controller.signal).then(
      (page) => setData({ key, items: page.items }),
      (e: unknown) => {
        if ((e as Error).name === "AbortError") return
        setData({ key, error: errorMessage("notification-read", e) })
      }
    )
    return () => controller.abort()
  }, [open, opened])

  const unread = total ?? 0
  const items = current && "items" in current ? current.items : []

  return (
    <Popover
      open={open}
      onOpenChange={(next) => {
        if (next) setOpened((n) => n + 1)
        setOpen(next)
      }}
    >
      <PopoverTrigger
        render={
          <Button
            variant="ghost"
            size="icon"
            className="relative"
            aria-label={
              unread > 0 ? `Thông báo, ${unread} chưa đọc` : "Thông báo"
            }
            data-testid="notification-bell"
          />
        }
      >
        <BellIcon aria-hidden />
        {unread > 0 && (
          <Badge
            className="absolute -top-1 -right-1 h-4 min-w-4 px-1 text-[10px]"
            data-testid="notification-badge"
          >
            {badgeText(unread)}
          </Badge>
        )}
      </PopoverTrigger>
      <PopoverContent align="end" className="w-80 gap-2 p-2">
        <div className="flex items-center justify-between px-2 pt-1">
          <p className="font-medium">Thông báo</p>
          <Button
            variant="link"
            size="xs"
            disabled={items.length === 0}
            onClick={() => void marks.markAll(items)}
          >
            Đánh dấu tất cả đã đọc
          </Button>
        </div>

        <FormAlert message={marks.error} />

        {current === null && (
          <div className="flex flex-col gap-2 p-2" aria-hidden>
            <Skeleton className="h-8" />
            <Skeleton className="h-8" />
          </div>
        )}
        {current && "error" in current && <FormAlert message={current.error} />}
        {current && "items" in current && items.length === 0 && (
          <p className="p-2 text-muted-foreground">Chưa có thông báo nào.</p>
        )}

        {items.length > 0 && (
          <ul className="flex max-h-96 flex-col overflow-y-auto">
            {items.map((n) => (
              <li key={n.notificationId}>
                <NotificationItem
                  notification={n}
                  read={marks.isRead(n)}
                  onOpen={(item) => {
                    void marks.markRead(item)
                    setOpen(false)
                  }}
                />
              </li>
            ))}
          </ul>
        )}

        <Link
          href="/notifications"
          onClick={() => setOpen(false)}
          className="px-2 pb-1 text-center text-sm underline underline-offset-4"
        >
          Xem tất cả
        </Link>
      </PopoverContent>
    </Popover>
  )
}
