"use client"

import { UnreadBadge } from "@/features/chat/unread-badge"
import { useProfile } from "@/features/profile/use-profile"

// Link "Tin nhắn" + badge ở header (GĐ5 E3). Chỉ ráp (Đ-E13): `app/` là tầng DUY NHẤT biết cả `features/profile` (ai đang đăng
// nhập) và `features/chat` — khuôn `users/[userId]/page.tsx` của GĐ4. Chưa có hồ sơ (đang onboarding) thì chưa có hội thoại nào:
// không hiện, và cũng không mở kết nối hub.
export function MessagesNav() {
  const { profile } = useProfile()
  if (!profile) return null
  return <UnreadBadge meId={profile.userId} />
}
