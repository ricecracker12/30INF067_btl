"use client"

import { ArrowLeft } from "lucide-react"
import Link from "next/link"
import { useLayoutEffect, useRef } from "react"

import { Alert, AlertDescription } from "@/components/ui/alert"
import { Avatar, AvatarFallback, AvatarImage } from "@/components/ui/avatar"
import { Button, buttonVariants } from "@/components/ui/button"
import { Skeleton } from "@/components/ui/skeleton"
import { errorMessage } from "@/lib/api/messages"
import type { MessageResponse } from "@/lib/api/types"
import { useChatConnection } from "@/lib/realtime/use-chat-connection"
import { cn } from "@/lib/utils"

import { MessageComposer } from "./message-composer"
import { DELIVERY_LABEL, deliveryState } from "./message-set"
import { useConversation, type PendingMessage } from "./use-conversation"

const time = new Intl.DateTimeFormat("vi-VN", { timeStyle: "short", timeZone: "Asia/Ho_Chi_Minh" })

type Props = {
  conversationId: string
  /** Người đang đăng nhập — `app/` lấy từ hồ sơ và truyền vào (features không import chéo). */
  meId: string
}

// Cửa sổ chat (E4–E7). Chỉ trình bày + nối hook; luật dữ liệu ở `use-conversation.ts` và `message-set.ts`.
export function ChatWindow({ conversationId, meId }: Props) {
  const status = useChatConnection()
  const chat = useConversation(conversationId, { status, meId })
  const listRef = useRef<HTMLDivElement>(null)
  const stickToBottom = useRef(true)
  const prevHeight = useRef(0)
  const prevFirstSeq = useRef<number | null>(null)

  // Cuộn: tin mới tới khi đang ở đáy → xuống đáy; nạp tin CŨ phía trên → giữ nguyên chỗ đang nhìn (bù chênh scrollHeight).
  useLayoutEffect(() => {
    const el = listRef.current
    if (!el) return
    const firstSeq = chat.messages[0]?.seq ?? null
    const prependedOlder = prevFirstSeq.current !== null && firstSeq !== null && firstSeq < prevFirstSeq.current
    if (prependedOlder) el.scrollTop += el.scrollHeight - prevHeight.current
    else if (stickToBottom.current) el.scrollTop = el.scrollHeight
    prevHeight.current = el.scrollHeight
    prevFirstSeq.current = firstSeq
  }, [chat.messages, chat.pending])

  if (chat.loading) return <ChatSkeleton />
  if (chat.loadError || !chat.conversation)
    return (
      <Alert variant="destructive">
        <AlertDescription>{errorMessage("conversation-read", chat.loadError)}</AlertDescription>
      </Alert>
    )

  const { peer, peerDeliveredSeq, peerSeenSeq } = chat.conversation
  const lastMine = [...chat.messages].reverse().find((m) => m.senderId === meId)

  return (
    <section className="flex h-[calc(100svh-10rem)] flex-col gap-3" aria-label={`Trò chuyện với ${peer.displayName}`}>
      <header className="flex items-center gap-3">
        <Link href="/messages" className={buttonVariants({ variant: "ghost", size: "icon" })} aria-label="Về danh sách">
          <ArrowLeft />
        </Link>
        <Avatar>
          {peer.avatarUrl && <AvatarImage src={peer.avatarUrl} alt={`Ảnh đại diện của ${peer.displayName}`} />}
          <AvatarFallback>{peer.displayName.trim().charAt(0).toUpperCase()}</AvatarFallback>
        </Avatar>
        <Link href={`/users/${peer.userId}`} className="truncate font-medium underline-offset-4 hover:underline">
          {peer.displayName}
        </Link>
      </header>

      {(status === "reconnecting" || status === "fallback") && (
        <p role="status" className="rounded-md bg-muted px-3 py-1 text-xs text-muted-foreground" data-testid="chat-banner">
          Đang kết nối lại — tin vẫn gửi được.
        </p>
      )}

      <div
        ref={listRef}
        className="flex flex-1 flex-col gap-1 overflow-y-auto rounded-md border border-border p-3"
        onScroll={(e) => {
          const el = e.currentTarget
          stickToBottom.current = el.scrollHeight - el.scrollTop - el.clientHeight < 40
        }}
        data-testid="chat-messages"
      >
        {chat.hasOlder && (
          <Button variant="ghost" size="sm" className="self-center" onClick={() => void chat.loadOlder()} disabled={chat.loadingOlder}>
            {chat.loadingOlder ? "Đang tải…" : "Xem tin cũ hơn"}
          </Button>
        )}
        {chat.messages.length === 0 && chat.pending.length === 0 && (
          <p className="m-auto text-sm text-muted-foreground">Chưa có tin nhắn nào. Hãy gửi lời chào!</p>
        )}
        {chat.messages.map((m) => (
          <Bubble
            key={m.messageId}
            message={m}
            mine={m.senderId === meId}
            status={
              m.messageId === lastMine?.messageId
                ? DELIVERY_LABEL[deliveryState(m.seq, peerDeliveredSeq, peerSeenSeq)]
                : null
            }
          />
        ))}
        {chat.pending.map((p) => (
          <PendingBubble key={p.clientMsgId} pending={p} onRetry={chat.retry} onDiscard={chat.discard} />
        ))}
      </div>

      {chat.readOnly && (
        <Alert data-testid="chat-read-only">
          <AlertDescription>Hai bạn không còn là bạn bè. Hội thoại chỉ đọc.</AlertDescription>
        </Alert>
      )}
      <MessageComposer onSend={chat.send} disabled={chat.readOnly} />
    </section>
  )
}

function Bubble({ message, mine, status }: { message: MessageResponse; mine: boolean; status: string | null }) {
  return (
    <div className={cn("flex max-w-[80%] flex-col gap-0.5", mine ? "self-end items-end" : "self-start items-start")}>
      <p
        className={cn(
          "whitespace-pre-wrap break-words rounded-2xl px-3 py-2 text-sm",
          mine ? "bg-primary text-primary-foreground" : "bg-muted text-foreground"
        )}
        data-testid="chat-message"
        data-seq={message.seq}
        data-client-msg-id={message.clientMsgId}
      >
        {message.content}
      </p>
      <span className="text-[11px] text-muted-foreground">
        <time dateTime={message.createdAt}>{time.format(new Date(message.createdAt))}</time>
        {status && <span data-testid="chat-delivery"> · {status}</span>}
      </span>
    </div>
  )
}

function PendingBubble({
  pending,
  onRetry,
  onDiscard,
}: {
  pending: PendingMessage
  onRetry: (id: string) => void
  onDiscard: (id: string) => void
}) {
  const failed = pending.state === "failed"
  return (
    <div className="flex max-w-[80%] flex-col items-end gap-0.5 self-end" data-testid="chat-pending">
      <p
        className={cn(
          "whitespace-pre-wrap break-words rounded-2xl px-3 py-2 text-sm",
          failed ? "border border-destructive bg-background text-foreground" : "bg-primary/60 text-primary-foreground"
        )}
      >
        {pending.content}
      </p>
      {failed ? (
        <span className="flex items-center gap-2 text-[11px] text-destructive">
          <span role="alert">Thất bại. {pending.error}</span>
          {pending.retryable ? (
            <Button variant="link" size="xs" onClick={() => onRetry(pending.clientMsgId)}>
              Thử lại
            </Button>
          ) : (
            <Button variant="link" size="xs" onClick={() => onDiscard(pending.clientMsgId)}>
              Bỏ
            </Button>
          )}
        </span>
      ) : (
        <span className="text-[11px] text-muted-foreground">Đang gửi…</span>
      )}
    </div>
  )
}

function ChatSkeleton() {
  return (
    <div className="flex flex-col gap-3" aria-busy="true">
      <Skeleton className="h-10 w-48" />
      <Skeleton className="h-64 w-full" />
      <Skeleton className="h-16 w-full" />
    </div>
  )
}
