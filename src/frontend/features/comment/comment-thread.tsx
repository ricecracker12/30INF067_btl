"use client"

import { RefreshCwIcon } from "lucide-react"
import { useCallback, useState } from "react"

import { Button } from "@/components/ui/button"
import { contentApi } from "@/lib/api/content-api"

import { CommentComposer } from "./comment-composer"
import { CommentList, type RenderReactions } from "./comment-item"
import { useCommentPages } from "./use-comment-pages"

// Cây bình luận của MỘT bài ở trang chi tiết (Đ-3.6, Đ-3.13). Trang bình luận gốc (20, cũ trước) + ô viết; mỗi bình luận tự mở
// nhánh phản hồi khi được bấm. Không realtime (Đ-3.12): bình luận mới của người khác hiện khi bấm "Tải lại".
//
// Sở hữu request hủy được (trang gốc — `AbortController` của `useCursorPages`, tạo trong effect) nên có ĐÚNG một ca `<StrictMode>`
// (luật frontend Mục 9).

/** Số bình luận gốc mỗi trang — mặc định của hợp đồng (Đ-3.6). */
export const COMMENT_PAGE_SIZE = 20

type Props = {
  postId: string
  /** `commentCount` của bài lúc nạp — thread tự cộng/trừ khi người dùng viết hay xóa, không nạp lại cả bài. */
  initialCount: number
  renderReactions?: RenderReactions
}

export function CommentThread({
  postId,
  initialCount,
  renderReactions,
}: Props) {
  const [delta, setDelta] = useState(0)
  const onCountDelta = useCallback((d: number) => setDelta((n) => n + d), [])

  const roots = useCommentPages(
    `comments:${postId}`,
    useCallback(
      (cursor: string | null, signal?: AbortSignal) =>
        contentApi.listComments(
          postId,
          { cursor, limit: COMMENT_PAGE_SIZE },
          signal
        ),
      [postId]
    )
  )

  return (
    <section className="flex flex-col gap-4" data-testid="comment-thread">
      <div className="flex items-center justify-between gap-2">
        <h2 className="text-base font-medium" data-testid="comment-total">
          {Math.max(0, initialCount + delta)} bình luận
        </h2>
        <Button
          variant="ghost"
          size="sm"
          onClick={roots.page.reload}
          disabled={roots.page.pending}
        >
          <RefreshCwIcon data-icon="inline-start" aria-hidden />
          Tải lại
        </Button>
      </div>

      <CommentComposer
        postId={postId}
        label="Viết bình luận"
        onCreated={(c) => {
          roots.append(c)
          onCountDelta(1)
        }}
      />

      {roots.page.loaded &&
        roots.items.length === 0 &&
        roots.page.error === null && (
          <p
            className="text-sm text-muted-foreground"
            data-testid="comments-empty"
          >
            Chưa có bình luận nào.
          </p>
        )}

      <CommentList
        list={roots}
        postId={postId}
        onCountDelta={onCountDelta}
        renderReactions={renderReactions}
        moreLabel="Xem thêm bình luận"
        testId="comments"
      />
    </section>
  )
}
