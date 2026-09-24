"use client"

import type { ReactNode } from "react"

import type { PostResponse } from "@/lib/api/types"

import { PostList } from "./post-list"
import { useUserPosts } from "./use-post-page"

// Danh sách bài của MỘT người. Dùng cho cả `/me` (bài của mình) và `/users/{userId}` (bài của người khác)
// — cùng endpoint `GET /users/{userId}/posts`, khác đúng câu lúc chưa có bài nào.
//
// `userId` đến qua PROP, không tự đọc `profileStore`: Đ-E13 cấm `features/post` import `features/profile`,
// và lỗ hổng đó không đáng mở cho một chuỗi id. Tầng `app/` biết cả hai feature nên nó là chỗ ráp đúng.

type Props = {
  /** `null` khi màn chưa biết mình đang xem bài của ai — hook không gọi gì cho tới khi có id. */
  userId: string | null
  title: string
  emptyMessage: string
  /** `/me` có nút "Đăng bài đầu tiên"; hồ sơ người khác thì không có gì để mời họ làm. */
  emptyAction?: ReactNode
  /** Nút cạnh tiêu đề, hiện KỂ CẢ khi đã có bài — `/me` dùng cho "Đăng bài". */
  action?: ReactNode
  /** Hàng tương tác dưới từng bài (GĐ3), do `app/` ghép. */
  renderFooter?: (post: PostResponse) => ReactNode
}

export function UserPosts({
  userId,
  title,
  emptyMessage,
  emptyAction,
  action,
  renderFooter,
}: Props) {
  const page = useUserPosts(userId)

  return (
    <section className="flex flex-col gap-4" data-testid="user-posts">
      <div className="flex items-center justify-between gap-4">
        <h2 className="text-lg font-medium">{title}</h2>
        {action}
      </div>
      <PostList
        page={page}
        emptyMessage={emptyMessage}
        emptyAction={emptyAction}
        renderFooter={renderFooter}
      />
    </section>
  )
}
