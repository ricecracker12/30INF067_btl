"use client"

import Link from "next/link"
import { useCallback, useEffect } from "react"

import { Alert, AlertDescription } from "@/components/ui/alert"
import { Avatar, AvatarFallback, AvatarImage } from "@/components/ui/avatar"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardContent } from "@/components/ui/card"
import { Skeleton } from "@/components/ui/skeleton"
import { useCursorPages } from "@/hooks/use-cursor-pages"
import { messagingApi } from "@/lib/api/messaging-api"
import { errorMessage } from "@/lib/api/messages"
import type { ConversationPage, ConversationResponse } from "@/lib/api/types"
import { chatConnection } from "@/lib/realtime/chat"
import { useChatConnection, useOnConnected } from "@/lib/realtime/use-chat-connection"

/** Fallback (Đ-5.12): danh sách + badge làm mới mỗi 30 giây khi không có WebSocket. */
export const LIST_POLL_MS = 30_000

const when = new Intl.DateTimeFormat("vi-VN", {
  dateStyle: "short",
  timeStyle: "short",
  timeZone: "Asia/Ho_Chi_Minh",
})

type Props = { meId: string }

// Danh sách hội thoại (E3) — hook phân trang dùng chung `use-cursor-pages` (GĐ4 Q-E8). Tin mới tới (bất kỳ hội thoại nào) → nạp
// lại trang đầu: hội thoại đó nhảy lên đầu, số chưa đọc cập nhật — không tự xếp lại tại chỗ (nguồn sự thật là server).
export function ConversationList({ meId }: Props) {
  const status = useChatConnection()
  const fetchPage = useCallback(
    (cursor: string | null, signal?: AbortSignal) => messagingApi.list({ cursor }, signal),
    []
  )
  const list = useCursorPages<ConversationResponse, ConversationPage>({
    key: "conversations",
    fetchPage,
    getId: (c) => c.conversationId,
  })
  const { reload } = list

  useEffect(() => {
    const offMessage = chatConnection.onMessage(() => reload())
    return () => offMessage()
  }, [reload])

  // Kết nối đầu + mỗi lần nối lại: tin tới lúc hub chưa nối không có sự kiện nào báo.
  useOnConnected(status, reload)

  useEffect(() => {
    if (status !== "fallback") return
    const timer = setInterval(reload, LIST_POLL_MS)
    return () => clearInterval(timer)
  }, [status, reload])

  if (!list.loaded) return <ListSkeleton />
  if (list.error && list.items.length === 0)
    return (
      <Alert variant="destructive">
        <AlertDescription>{errorMessage("conversation-read", list.error)}</AlertDescription>
      </Alert>
    )
  if (list.items.length === 0)
    return (
      <p className="text-sm text-muted-foreground" data-testid="conversations-empty">
        Chưa có cuộc trò chuyện nào — nhắn cho một người bạn từ trang cá nhân của họ.
      </p>
    )

  return (
    <div className="flex flex-col gap-2">
      <ul className="flex flex-col gap-2" aria-label="Cuộc trò chuyện">
        {list.items.map((c) => (
          <li key={c.conversationId}>
            <ConversationRow conversation={c} meId={meId} />
          </li>
        ))}
      </ul>
      {list.nextCursor !== null && (
        <Button variant="outline" onClick={list.loadMore} disabled={list.pending}>
          {list.pending ? "Đang tải…" : "Xem thêm"}
        </Button>
      )}
    </div>
  )
}

function ConversationRow({ conversation, meId }: { conversation: ConversationResponse; meId: string }) {
  const { peer, lastMessage, unreadCount } = conversation
  const preview = lastMessage
    ? `${lastMessage.senderId === meId ? "Bạn: " : ""}${lastMessage.content}`
    : ""
  return (
    <Link href={`/messages/${conversation.conversationId}`} className="block" data-testid="conversation-row">
      <Card size="sm" className="transition-colors hover:bg-muted/50">
        <CardContent className="flex items-center gap-3">
          <Avatar>
            {peer.avatarUrl && <AvatarImage src={peer.avatarUrl} alt={`Ảnh đại diện của ${peer.displayName}`} />}
            <AvatarFallback>{peer.displayName.trim().charAt(0).toUpperCase()}</AvatarFallback>
          </Avatar>
          <div className="flex min-w-0 flex-1 flex-col">
            <span className={unreadCount > 0 ? "truncate text-sm font-semibold" : "truncate text-sm font-medium"}>
              {peer.displayName}
            </span>
            <span className="truncate text-xs text-muted-foreground">{preview}</span>
          </div>
          <div className="flex flex-col items-end gap-1">
            {lastMessage && (
              <time className="text-xs text-muted-foreground" dateTime={lastMessage.createdAt}>
                {when.format(new Date(lastMessage.createdAt))}
              </time>
            )}
            {unreadCount > 0 && (
              <Badge aria-label={`${unreadCount} tin chưa đọc`} data-testid="conversation-unread">
                {unreadCount}
              </Badge>
            )}
          </div>
        </CardContent>
      </Card>
    </Link>
  )
}

function ListSkeleton() {
  return (
    <div className="flex flex-col gap-2" aria-busy="true">
      {[0, 1, 2].map((i) => (
        <Skeleton key={i} className="h-16 w-full" />
      ))}
    </div>
  )
}
