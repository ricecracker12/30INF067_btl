import { UserPosts } from "@/features/post/user-posts"
import { PublicProfile } from "@/features/profile/public-profile"

// Hồ sơ người khác + bài của họ (Q-E6). Chỉ ráp (Đ-E13) — và đây là LÝ DO tầng `app/` tồn tại: nó là chỗ
// duy nhất được biết cả `features/profile` lẫn `features/post`, hai feature không được import chéo nhau.
//
// `params` là Promise từ Next 15; server component `async` await nó rồi truyền chuỗi xuống hai client
// component bên dưới.
export default async function UserPage({
  params,
}: {
  params: Promise<{ userId: string }>
}) {
  const { userId } = await params

  return (
    <div className="flex flex-col gap-8">
      <PublicProfile userId={userId} />
      <UserPosts
        userId={userId}
        title="Bài đã đăng"
        // Không có nút mời đăng bài: đây là hồ sơ của người khác.
        emptyMessage="Người này chưa có bài nào bạn xem được."
      />
    </div>
  )
}
