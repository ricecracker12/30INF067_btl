"use client"

import { MessageCircle } from "lucide-react"
import { useRouter } from "next/navigation"
import { useEffect, useState } from "react"

import { Button } from "@/components/ui/button"
import { messagingApi } from "@/lib/api/messaging-api"
import { errorMessage } from "@/lib/api/messages"
import { socialGraphApi } from "@/lib/api/socialgraph-api"

type Props = { userId: string }

// Nút "Nhắn tin" trên hồ sơ (E6, Mục 7.1). CHỈ hiện khi đang là bạn — tự đọc `GET /relationships/{id}` (bàn giao GĐ4: nút quan hệ
// không xuất trạng thái ra ngoài; chốt ở GĐ5 — tự đọc thay vì nâng trạng thái lên `app/`, một lời gọi rẻ không đáng một tầng
// props). Bấm → get-or-create (idempotent) → sang màn chat. Không optimistic.
export function StartChatButton({ userId }: Props) {
  const router = useRouter()
  const [friends, setFriends] = useState(false)
  const [busy, setBusy] = useState(false)
  const [error, setError] = useState<string | null>(null)

  useEffect(() => {
    const controller = new AbortController()
    socialGraphApi
      .relationship(userId, controller.signal)
      .then((r) => setFriends(r.friendship === "friends"))
      .catch(() => setFriends(false))
    return () => controller.abort()
  }, [userId])

  if (!friends) return null

  const open = async () => {
    setBusy(true)
    setError(null)
    try {
      const conversation = await messagingApi.open(userId)
      router.push(`/messages/${conversation.conversationId}`)
    } catch (e) {
      setError(errorMessage("conversation-open", e))
      setBusy(false)
    }
  }

  return (
    <div className="flex flex-col gap-1">
      <Button variant="outline" onClick={() => void open()} disabled={busy} data-testid="start-chat">
        <MessageCircle data-icon="inline-start" />
        Nhắn tin
      </Button>
      {error && (
        <p role="alert" className="text-xs text-destructive">
          {error}
        </p>
      )}
    </div>
  )
}
