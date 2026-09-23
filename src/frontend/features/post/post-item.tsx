"use client"

import { useState } from "react"

import { FormAlert } from "@/components/form/form-alert"
import type { PostResponse } from "@/lib/api/types"

import { PostActions } from "./post-actions"
import { PostCard } from "./post-card"
import { PostEditForm } from "./post-edit-form"

// Một bài **có thao tác**: card ở chế độ xem, form ở chế độ sửa, và nút Sửa/Xóa khi `canEdit`.
//
// Tách khỏi `PostCard` vì card phải giữ được tính thuần: nó nhận một `post` và vẽ ra, không giữ trạng
// thái nào của người dùng. Chỗ cần trạng thái ("đang sửa bài nào") là danh sách và trang chi tiết — cả
// hai đi qua đây.

type Props = {
  post: PostResponse
  /** `true` ở trang chi tiết: card không tự dẫn tới chính trang đang mở. */
  standalone?: boolean
  /**
   * Bài đã đổi (`PostResponse` mới sau khi sửa, hoặc sau khi nạp lại vì ảnh hết hạn), hoặc `null` khi
   * bài vừa bị xóa. Chỗ gọi quyết định làm gì: danh sách thay/gỡ phần tử, trang chi tiết rời đi.
   */
  onChanged: (next: PostResponse | null) => void
}

export function PostItem({ post, standalone, onChanged }: Props) {
  const [editing, setEditing] = useState(false)
  const [error, setError] = useState<string | null>(null)

  if (editing) {
    return (
      <PostEditForm
        post={post}
        onSaved={(next) => {
          setEditing(false)
          // 200 trả nguyên `PostResponse` (có `editedAt` mới) nên KHÔNG phải gọi thêm `GET`: nhãn "đã
          // chỉnh sửa" hiện ngay từ dữ liệu vừa nhận.
          onChanged(next)
        }}
        onCancel={() => setEditing(false)}
      />
    )
  }

  return (
    <div className="flex flex-col gap-2">
      <FormAlert message={error} />
      <PostCard
        post={post}
        standalone={standalone}
        // Quyền đến từ SERVER. `canEdit: false` thì hai nút KHÔNG CÓ TRONG DOM — ẩn bằng CSS thì người
        // dùng bật DevTools là bấm được, và tuy server vẫn chặn, UI đang nói dối về thứ họ làm được.
        actions={
          post.canEdit ? (
            <PostActions
              post={post}
              onEdit={() => {
                setError(null)
                setEditing(true)
              }}
              onDeleted={() => onChanged(null)}
              onError={setError}
            />
          ) : undefined
        }
        onRefreshed={onChanged}
      />
    </div>
  )
}
