"use client"

import { useCursorPages } from "@/hooks/use-cursor-pages"
import { contentApi } from "@/lib/api/content-api"
import type { FeedPage, PostResponse } from "@/lib/api/types"

/** Mặc định của hợp đồng (`1..50`). Hằng một chỗ; KHÔNG dùng để suy "hết bài" (Đ-4.9). */
export const FEED_PAGE_SIZE = 20

const fetchFeed = (cursor: string | null, signal?: AbortSignal) =>
  contentApi.feed({ cursor, limit: FEED_PAGE_SIZE }, signal)

const postId = (p: PostResponse) => p.postId

/**
 * Bảng tin của người đang đăng nhập (SEQ-03 phía client). `mode` đọc từ TRANG ĐẦU của lượt xem: trang sau về `mode`
 * khác (vừa kết bạn ở tab khác) thì nhãn không nhảy giữa chừng — "Làm mới" mới đọc lại. FE không suy "gợi ý" từ tác
 * giả (Đ-4.6): chỉ server biết người đọc có kết nối nào.
 */
export function useFeedPage() {
  const page = useCursorPages<PostResponse, FeedPage>({
    key: "feed",
    fetchPage: fetchFeed,
    getId: postId,
  })
  return { ...page, mode: page.head?.mode ?? null }
}

export type FeedPageState = ReturnType<typeof useFeedPage>
