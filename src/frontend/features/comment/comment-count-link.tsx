import { MessageCircle } from "lucide-react"
import Link from "next/link"

// Số bình luận trên card bài TRONG DANH SÁCH (feed, trang cá nhân) — dẫn tới trang chi tiết, nơi cây bình luận thật sự mở. Danh
// sách không mở cây tại chỗ (Mục B.7 E5): hai mươi bài, mỗi bài một cây, là hai mươi lượt tải không ai xem.

export function CommentCountLink({
  postId,
  count,
}: {
  postId: string
  count: number
}) {
  return (
    <Link
      href={`/posts/${postId}`}
      className="flex items-center gap-1 text-sm text-muted-foreground underline-offset-4 hover:underline"
      data-testid="comment-count"
    >
      <MessageCircle className="size-4" aria-hidden />
      {count} bình luận
    </Link>
  )
}
