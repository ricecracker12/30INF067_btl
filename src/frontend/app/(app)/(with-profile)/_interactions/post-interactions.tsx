"use client"

import { CommentCountLink } from "@/features/comment/comment-count-link"
import { CommentThread } from "@/features/comment/comment-thread"
import { ReactionBar } from "@/features/reaction/reaction-bar"
import type { CommentResponse, PostResponse } from "@/lib/api/types"

import { ReportSlot } from "./report-slot"

// Chỗ RÁP DUY NHẤT của bình luận + cảm xúc vào bài (GĐ3 Đ-3.13). `features/post`, `features/comment`, `features/reaction` không
// biết nhau (Đ-E13) — chỉ `app/` biết cả ba, và ba trang (trang chủ, `/me`, `/users/[userId]`) cùng trang chi tiết dùng chung
// file này thay vì mỗi trang tự ráp một kiểu. Thư mục `_interactions` là private folder của Next: không thành route.
//
// GĐ6 E5: nút "Báo cáo" cũng ghép ở đây — trên bài/bình luận KHÔNG phải của mình (`canEdit`/`canDelete` do SERVER tính; FE không
// tự so id tác giả). `features/post`, `features/comment` không import `features/report`.
//
// `key` theo id đối tượng: thanh cảm xúc khởi tạo reducer MỘT lần từ số của bài (Đ-3.13) — đổi bài trong cùng chỗ thì phải là
// một thanh mới, không mang trạng thái của bài cũ.

/** Hàng dưới mỗi bài trong DANH SÁCH: thanh cảm xúc + số bình luận dẫn tới chi tiết. Không mở cây bình luận tại chỗ. */
export function postListFooter(post: PostResponse) {
  return (
    <>
      <ReactionBar
        key={post.postId}
        target={{ kind: "post", id: post.postId }}
        initial={{
          reactionCounts: post.reactionCounts,
          myReaction: post.myReaction,
        }}
      />
      <CommentCountLink postId={post.postId} count={post.commentCount} />
      {!post.canEdit && <ReportSlot targetType="post" targetId={post.postId} />}
    </>
  )
}

/** Hàng trong card ở trang CHI TIẾT: thanh cảm xúc (+ Báo cáo) — số bình luận nằm ở đầu cây ngay bên dưới. */
export function postDetailFooter(post: PostResponse) {
  return (
    <>
      <ReactionBar
        key={post.postId}
        target={{ kind: "post", id: post.postId }}
        initial={{
          reactionCounts: post.reactionCounts,
          myReaction: post.myReaction,
        }}
      />
      {!post.canEdit && <ReportSlot targetType="post" targetId={post.postId} />}
    </>
  )
}

/** Dưới card ở trang CHI TIẾT: cây bình luận 3 cấp, mỗi bình luận có thanh cảm xúc gọn. */
export function postDetailComments(post: PostResponse) {
  return (
    <CommentThread
      key={post.postId}
      postId={post.postId}
      initialCount={post.commentCount}
      renderReactions={commentReactions}
    />
  )
}

function commentReactions(comment: CommentResponse) {
  return (
    <>
      <ReactionBar
        key={comment.commentId}
        compact
        target={{ kind: "comment", id: comment.commentId }}
        initial={{
          reactionCounts: comment.reactionCounts,
          myReaction: comment.myReaction,
        }}
      />
      {!comment.canDelete && (
        <ReportSlot targetType="comment" targetId={comment.commentId} compact />
      )}
    </>
  )
}
