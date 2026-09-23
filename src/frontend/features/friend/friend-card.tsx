"use client"

import Link from "next/link"
import type { ReactNode } from "react"

import { Avatar, AvatarFallback, AvatarImage } from "@/components/ui/avatar"
import { Card, CardContent } from "@/components/ui/card"
import type { FriendCard as FriendCardData } from "@/lib/api/types"

// Một người trong ba danh sách của `/friends` (E3). Chỉ trình bày: nút do màn truyền vào (`actions`), lỗi của thao tác trên
// thẻ này do màn truyền vào (`message`) — thẻ không biết mình đang ở mục nào.

/** Cùng định dạng `PostCard`: giờ Việt Nam, không phụ thuộc múi giờ máy người xem. */
const dateTime = new Intl.DateTimeFormat("vi-VN", {
  dateStyle: "medium",
  timeStyle: "short",
  timeZone: "Asia/Ho_Chi_Minh",
})

type Props = {
  card: FriendCardData
  /** Nhãn trước thời điểm — `since` là `accepted_at` ở danh sách bạn, `created_at` ở danh sách lời mời. */
  sinceLabel: string
  actions?: ReactNode
  /** Lỗi của thao tác trên THẺ NÀY — hiện ngay dưới thẻ, không lên đầu màn. */
  message?: string | null
}

export function FriendCard({ card, sinceLabel, actions, message }: Props) {
  const { user } = card
  return (
    <Card size="sm" data-testid="friend-card" data-user-id={user.userId}>
      <CardContent className="flex flex-col gap-2">
        <div className="flex items-center gap-3">
          <Avatar>
            {user.avatarUrl && (
              <AvatarImage
                src={user.avatarUrl}
                alt={`Ảnh đại diện của ${user.displayName}`}
              />
            )}
            <AvatarFallback>
              {user.displayName.trim().charAt(0).toUpperCase()}
            </AvatarFallback>
          </Avatar>

          <div className="flex min-w-0 flex-1 flex-col gap-0.5">
            <Link
              href={`/users/${user.userId}`}
              className="truncate text-sm font-medium underline-offset-4 hover:underline"
            >
              {user.displayName}
            </Link>
            <span className="text-xs text-muted-foreground">
              {sinceLabel}{" "}
              <time dateTime={card.since}>
                {dateTime.format(new Date(card.since))}
              </time>
            </span>
          </div>

          {actions && <div className="flex shrink-0 gap-2">{actions}</div>}
        </div>

        {message && (
          <p role="alert" className="text-sm text-destructive">
            {message}
          </p>
        )}
      </CardContent>
    </Card>
  )
}
