"use client"

import { ConversationList } from "@/features/chat/conversation-list"
import { useProfile } from "@/features/profile/use-profile"

// `/messages` (GĐ5 E3). Chỉ ráp (Đ-E13). Dưới `(with-profile)` nên `profile` luôn có khi trang này render.
export default function MessagesPage() {
  const { profile } = useProfile()
  if (!profile) return null
  return (
    <div className="flex flex-col gap-4">
      <h1 className="text-lg font-semibold">Tin nhắn</h1>
      <ConversationList meId={profile.userId} />
    </div>
  )
}
