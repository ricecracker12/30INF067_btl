"use client"

import { FeedList } from "@/features/feed/feed-list"
import { ComposeButton } from "@/features/post/post-list"
import { PostItem } from "@/features/post/post-item"

import { postListFooter } from "./_interactions/post-interactions"

// Trang chủ = feed (GĐ4 E5, Q-E1). Nằm dưới `(with-profile)` nên tự có `RequireAuth` + `RequireProfile` + header: người
// chưa có hồ sơ không thấy feed (bấm Kết bạn khi chính mình chưa "tồn tại" là 404 — Đ-2.4).
//
// Chỗ DUY NHẤT biết cả `features/feed` lẫn `features/post` (Đ-4.16, Đ-E13). `PostItem` chứ không `PostCard` (lệch Đ-4.16,
// Q-E6): feed có bài của CHÍNH MÌNH (Đ-4.5) — cần nút Sửa/Xóa theo `canEdit`, và xóa xong phải biến khỏi feed.
// GĐ3 cắm thanh cảm xúc + số bình luận vào ĐÚNG dòng `renderPost` này — không chạm `features/feed/` (Mục 9.1 #3).
//
// `"use client"`: `renderPost` là HÀM, không truyền được qua ranh giới server → client.
export default function HomePage() {
  return (
    <FeedList
      action={<ComposeButton />}
      renderPost={(post, onChanged) => (
        <PostItem post={post} onChanged={onChanged} footer={postListFooter} />
      )}
    />
  )
}
