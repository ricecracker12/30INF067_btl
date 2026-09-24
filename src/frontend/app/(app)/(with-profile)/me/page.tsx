"use client"

import { MeProfile } from "@/features/auth/me-profile"
import {
  ComposeButton,
  ComposeFirstPostButton,
} from "@/features/post/post-list"
import { UserPosts } from "@/features/post/user-posts"
import { AvatarCard } from "@/features/profile/avatar-card"
import { ProfileCard } from "@/features/profile/profile-card"
import { useProfile } from "@/features/profile/use-profile"

import { postListFooter } from "../_interactions/post-interactions"

// Chỉ ráp (Đ-E13). Nằm dưới `(with-profile)` (Q-E6): tới được đây nghĩa là đã có hồ sơ, nên `ProfileCard`
// không phải có nhánh "chưa onboarding". Gọi API ở client (Đ-E11).
//
// `"use client"` từ E5: trang cần `userId` của chính mình để hỏi `GET /users/{userId}/posts`, mà id đó
// nằm trong `profileStore` — `(with-profile)` vừa nạp xong, nên đọc nó rẻ hơn một vòng `GET /me` nữa.
// Đọc store rồi truyền id xuống là RÁP, không phải nghiệp vụ; và đây là chỗ duy nhất được biết cả
// `features/profile` lẫn `features/post` (hai feature không import chéo nhau). Trang này vốn toàn
// component client nên không mất gì của RSC.
export default function MePage() {
  const { profile } = useProfile()

  return (
    <div className="flex flex-col gap-8">
      <ProfileCard />
      <AvatarCard />
      <UserPosts
        userId={profile?.userId ?? null}
        title="Bài của tôi"
        emptyMessage="Bạn chưa đăng bài nào."
        emptyAction={<ComposeFirstPostButton />}
        action={<ComposeButton />}
        renderFooter={postListFooter}
      />
      <MeProfile />
    </div>
  )
}
