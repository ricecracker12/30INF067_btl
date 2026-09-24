"use client"

import { use } from "react"

import { PostDetail } from "@/features/post/post-detail"

import {
  postDetailComments,
  postDetailFooter,
} from "../../_interactions/post-interactions"

// Chi tiết một bài (Q-E6) + cây bình luận và cảm xúc (GĐ3 Đ-3.13). Chỉ ráp (Đ-E13); mọi phân nhánh 404 / lỗi / skeleton nằm trong
// `PostDetail`, cây bình luận chỉ hiện khi bài đã nạp được.
//
// `"use client"` từ GĐ3: slot là HÀM, không truyền được qua ranh giới server → client (cùng lý do trang chủ). `params` vẫn là
// Promise — mở bằng `use()`.
export default function PostPage({
  params,
}: {
  params: Promise<{ postId: string }>
}) {
  const { postId } = use(params)
  return (
    <PostDetail
      postId={postId}
      footer={postDetailFooter}
      below={postDetailComments}
    />
  )
}
