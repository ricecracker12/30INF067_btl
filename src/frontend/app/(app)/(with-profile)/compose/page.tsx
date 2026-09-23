import { PostComposer } from "@/features/post/post-composer"

// Chỉ ráp (Đ-E13). Nằm dưới `(with-profile)` (Q-E6): tới được đây nghĩa là đã đăng nhập VÀ đã có hồ sơ,
// nên composer không phải có nhánh "chưa onboarding" — `POST /posts` trả 403 cho ca đó, và guard đã chặn
// trước khi người dùng gõ xong bài.
export default function ComposePage() {
  return (
    <div className="flex flex-col gap-6">
      <h1 className="text-xl font-medium">Đăng bài</h1>
      <PostComposer />
    </div>
  )
}
