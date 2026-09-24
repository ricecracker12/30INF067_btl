"use client"

import { use } from "react"

import { StartChatButton } from "@/features/chat/start-chat-button"
import { RelationshipButtons } from "@/features/friend/relationship-buttons"
import { UserPosts } from "@/features/post/user-posts"
import { PublicProfile } from "@/features/profile/public-profile"
import { useProfile } from "@/features/profile/use-profile"

// Hồ sơ một người + bài của họ + nút quan hệ (GĐ2 Q-E6, GĐ4 E5). Chỉ ráp (Đ-E13) — tầng DUY NHẤT được biết cả
// `features/profile` (ai đang đăng nhập), `features/friend` và `features/post`.
//
// Q-E2 (chốt 2026-09-23): hồ sơ của CHÍNH MÌNH không có nút quan hệ — `GET /relationships/{mình}` là 400. Quyết ở đây,
// không để `RelationshipButtons` tự bắt 400 (dùng mã lỗi để suy "đây là tôi" là đoán luật server từ status).
//
// `"use client"` từ GĐ4: cần `profile` của người đang đăng nhập từ store (khuôn `me/page.tsx`). Nằm dưới
// `(with-profile)` nên `profile` luôn có khi trang này render. `params` vẫn là Promise — mở bằng `use()`.
export default function UserPage({
  params,
}: {
  params: Promise<{ userId: string }>
}) {
  const { userId } = use(params)
  const { profile } = useProfile()
  const laChinhMinh = profile?.userId === userId

  return (
    <div className="flex flex-col gap-8">
      <PublicProfile
        userId={userId}
        actions={
          laChinhMinh
            ? undefined
            : (nguoiKia) => (
                // GĐ5 E6: "Nhắn tin" CẠNH nút quan hệ — chỉ `app/` biết cả hai (Đ-4.16). Nút tự ẩn khi chưa là bạn.
                <div className="flex flex-wrap items-start gap-2">
                  <RelationshipButtons
                    userId={userId}
                    displayName={nguoiKia.displayName}
                  />
                  <StartChatButton userId={userId} />
                </div>
              )
        }
      />
      <UserPosts
        userId={userId}
        title="Bài đã đăng"
        emptyMessage={
          laChinhMinh
            ? "Bạn chưa đăng bài nào."
            : "Người này chưa có bài nào bạn xem được."
        }
      />
    </div>
  )
}
