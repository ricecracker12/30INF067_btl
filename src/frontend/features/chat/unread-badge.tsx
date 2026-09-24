"use client"

import Link from "next/link"
import { useCallback, useEffect, useRef, useState } from "react"

import { Badge } from "@/components/ui/badge"
import { messagingApi } from "@/lib/api/messaging-api"
import { chatConnection } from "@/lib/realtime/chat"
import { useChatConnection, useOnConnected } from "@/lib/realtime/use-chat-connection"

import { LIST_POLL_MS } from "./conversation-list"

type Props = { meId: string }

// Link "Tin nhắn" + badge chưa đọc ở header (E3, AC-02). Là người dùng THƯỜNG TRỰC của kết nối hub — header có mặt ở mọi trang
// đã đăng nhập, nên cũng là chỗ gửi biên nhận "đã nhận" (Mục 7.2 bước 7) cho tin tới khi người dùng KHÔNG mở hội thoại đó:
// người gửi thấy "Đã nhận" dù người nhận đang ở bảng tin. "Đã xem" thì chỉ màn chat gửi (tab đang hiển thị + hội thoại đang mở).
export function UnreadBadge({ meId }: Props) {
  const status = useChatConnection()
  const [total, setTotal] = useState(0)
  const refreshTimer = useRef<ReturnType<typeof setTimeout> | null>(null)

  const refresh = useCallback(() => {
    messagingApi
      .unreadCount()
      .then((r) => setTotal(r.total))
      .catch(() => undefined) // badge hỏng không chặn gì — lần sau làm mới
  }, [])

  // Gộp nhiều sự kiện liền nhau thành một lần gọi (một tin = MessageReceived + ReceiptUpdated của chính mình).
  const refreshSoon = useCallback(() => {
    if (refreshTimer.current !== null) clearTimeout(refreshTimer.current)
    refreshTimer.current = setTimeout(() => {
      refreshTimer.current = null
      refresh()
    }, 300)
  }, [refresh])

  useEffect(() => {
    refresh()
    const offMessage = chatConnection.onMessage((m) => {
      if (m.senderId !== meId)
        void chatConnection
          .sendReceipt({ conversationId: m.conversationId, kind: "delivered", upToSeq: m.seq })
          .catch(() => undefined)
      refreshSoon()
    })
    const offReceipt = chatConnection.onReceipt(refreshSoon)
    return () => {
      offMessage()
      offReceipt()
      if (refreshTimer.current !== null) clearTimeout(refreshTimer.current)
    }
  }, [meId, refresh, refreshSoon])

  // Kết nối đầu + mỗi lần nối lại: tin tới lúc hub chưa nối không có sự kiện nào báo.
  useOnConnected(status, refresh)

  useEffect(() => {
    if (status !== "fallback") return
    const timer = setInterval(refresh, LIST_POLL_MS)
    return () => clearInterval(timer)
  }, [status, refresh])

  return (
    <Link href="/messages" className="flex items-center gap-1 hover:text-foreground" data-testid="nav-messages">
      Tin nhắn
      {total > 0 && (
        <Badge aria-label={`${total} tin nhắn chưa đọc`} data-testid="unread-badge">
          {total > 99 ? "99+" : total}
        </Badge>
      )}
    </Link>
  )
}
