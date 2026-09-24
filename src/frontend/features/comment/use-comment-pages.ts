"use client"

import { useCallback, useMemo, useState } from "react"

import { useCursorPages } from "@/hooks/use-cursor-pages"
import type { CommentPage, CommentResponse } from "@/lib/api/types"

/**
 * Một danh sách bình luận (gốc của bài, hoặc phản hồi của một bình luận) theo cursor ASC — `useCursorPages` dùng chung cộng
 * đúng MỘT thứ nó không có: chèn bình luận VỪA TẠO vào cuối mà không nạp lại (Mục 7.2 bước 6).
 *
 * Bình luận vừa tạo nằm ở `added` cho tới khi một trang tải về có chính nó (người dùng bấm "Xem thêm" tới cuối) — lúc đó bản
 * của trang thắng và bản `added` bị lọc, không hiện hai lần.
 */
export function useCommentPages(
  key: string | null,
  fetchPage: (
    cursor: string | null,
    signal?: AbortSignal
  ) => Promise<CommentPage>
) {
  const page = useCursorPages<CommentResponse, CommentPage>({
    key,
    fetchPage,
    getId: (c) => c.commentId,
  })
  const [added, setAdded] = useState<CommentResponse[]>([])
  const { replaceItem } = page

  const items = useMemo(() => {
    const loaded = new Set(page.items.map((c) => c.commentId))
    return [...page.items, ...added.filter((c) => !loaded.has(c.commentId))]
  }, [page.items, added])

  const append = useCallback(
    (comment: CommentResponse) => setAdded((prev) => [...prev, comment]),
    []
  )

  /** Thay tại chỗ (xóa mềm, `replyCount` tăng) — ở trang đã tải VÀ ở phần vừa chèn, bên nào đang giữ nó. */
  const update = useCallback(
    (id: string, next: CommentResponse) => {
      replaceItem(id, next)
      setAdded((prev) => prev.map((c) => (c.commentId === id ? next : c)))
    },
    [replaceItem]
  )

  return { page, items, append, update }
}
