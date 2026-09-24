"use client"

import { use } from "react"

import { ChatWindow } from "@/features/chat/chat-window"
import { useProfile } from "@/features/profile/use-profile"

// `/messages/{conversationId}` (GĐ5 E4). Chỉ ráp (Đ-E13) — `meId` từ hồ sơ truyền xuống (features không import chéo).
// `key` theo hội thoại: chuyển sang hội thoại khác là dựng lại toàn bộ trạng thái, không trộn tin của hai hội thoại.
export default function ConversationPage({
  params,
}: {
  params: Promise<{ conversationId: string }>
}) {
  const { conversationId } = use(params)
  const { profile } = useProfile()
  if (!profile) return null
  return <ChatWindow key={conversationId} conversationId={conversationId} meId={profile.userId} />
}
