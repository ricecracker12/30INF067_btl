"use client"

import Link from "next/link"

import { Avatar, AvatarFallback, AvatarImage } from "@/components/ui/avatar"
import type { NotificationResponse } from "@/lib/api/types"
import { cn } from "@/lib/utils"

import { notificationHref, notificationText } from "./notification-text"

const dateTime = new Intl.DateTimeFormat("vi-VN", {
  dateStyle: "short",
  timeStyle: "short",
  timeZone: "Asia/Ho_Chi_Minh",
})

type Props = {
  notification: NotificationResponse
  /** Đã đọc theo lớp phủ optimistic (`useReadMarks`), không theo `notification.isRead` của trang cũ. */
  read: boolean
  /** Bấm → đánh dấu đã đọc (optimistic) — điều hướng là việc của `Link`, không chờ server. */
  onOpen: (n: NotificationResponse) => void
}

/** Một dòng thông báo — dùng chung cho popover chuông và màn `/notifications`. */
export function NotificationItem({ notification: n, read, onOpen }: Props) {
  const name = n.actor?.displayName ?? ""
  return (
    <Link
      href={notificationHref(n)}
      onClick={() => onOpen(n)}
      data-testid="notification-item"
      data-notification-id={n.notificationId}
      data-read={read}
      className={cn(
        "flex items-start gap-3 rounded-lg p-2 text-sm hover:bg-muted",
        !read && "bg-muted/50"
      )}
    >
      <Avatar className="size-8">
        {n.actor?.avatarUrl && <AvatarImage src={n.actor.avatarUrl} alt="" />}
        <AvatarFallback>{name.trim().charAt(0).toUpperCase() || "!"}</AvatarFallback>
      </Avatar>
      <span className="flex flex-1 flex-col gap-0.5">
        <span className={cn(!read && "font-medium")}>{notificationText(n)}</span>
        <time dateTime={n.updatedAt} className="text-xs text-muted-foreground">
          {dateTime.format(new Date(n.updatedAt))}
        </time>
      </span>
      {!read && (
        <span
          aria-label="Chưa đọc"
          className="mt-1.5 size-2 shrink-0 rounded-full bg-primary"
        />
      )}
    </Link>
  )
}
