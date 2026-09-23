"use client"

import { RefreshCwIcon, SparklesIcon } from "lucide-react"
import { Fragment, useEffect, useRef, type ReactNode } from "react"

import { FormAlert } from "@/components/form/form-alert"
import { Alert, AlertDescription } from "@/components/ui/alert"
import { Button } from "@/components/ui/button"
import { Card, CardContent } from "@/components/ui/card"
import { Skeleton } from "@/components/ui/skeleton"
import { Spinner } from "@/components/ui/spinner"
import { errorMessage } from "@/lib/api/messages"
import { hasProblemType, PROBLEM_TYPES } from "@/lib/api/problem"
import type { PostResponse } from "@/lib/api/types"

import { useFeedPage, type FeedPageState } from "./use-feed-page"

// Trang chủ là feed (GĐ4 E4). `features/feed/` KHÔNG biết bài được vẽ ra sao: `renderPost` do `app/` truyền vào (Đ-4.16,
// Q-E6) — trang chủ ráp `PostItem`, GĐ3 bọc thêm thanh cảm xúc ở đúng dòng ráp đó mà không chạm file này.
//
// Cuộn (Q-E5, chốt 2026-09-23): CHỈ observer tự nạp, không nút "Xem thêm" lúc bình thường. Trang sau hỏng → ngừng tự nạp,
// hiện dòng lỗi + "Thử lại" ở cuối danh sách; bấm là nạp lại ĐÚNG lô vừa hỏng và observer bật lại. Hết dữ liệu (chỉ khi
// `nextCursor === null`, Đ-4.9) → "Bạn đã xem hết".

export type RenderPost = (
  post: PostResponse,
  /** Bài đã sửa → thay tại chỗ; `null` (đã xóa) → gỡ khỏi feed, không để lại liên kết chết. */
  onChanged: (next: PostResponse | null) => void
) => ReactNode

/**
 * Trần tự nạp: bao nhiêu trang LIÊN TIẾP không thêm được bài nào thì observer thôi tự gọi (Q-E5). Chặn vòng request khi
 * server trả trang rỗng mãi (Đ-4.9 cho phép trang rỗng mà còn trang sau). Tới trần thì nút "Xem tiếp" làm việc thay.
 */
export const MAX_AUTO_EMPTY_PAGES = 5

/** Nạp trước khi chạm đáy — người đọc không phải dừng lại chờ ở cuối trang. */
const PRELOAD_MARGIN = "400px 0px"

type Props = {
  renderPost: RenderPost
  /** Nút cạnh "Làm mới" (trang chủ truyền "Đăng bài") — `features/feed` không import `features/post` (Đ-E13). */
  action?: ReactNode
}

export function FeedList({ renderPost, action }: Props) {
  // Hook gọi TRƯỚC `useAutoLoad`: effect đồng bộ ref của hook phải chạy trước effect tự nạp trong cùng commit.
  const feed = useFeedPage()
  const sentinelRef = useAutoLoad(feed, canAutoLoad(feed))

  // Trang đầu chưa về: skeleton, KHÔNG màn trắng, không nháy câu "chưa có bài".
  if (!feed.loaded) return <FeedSkeleton />

  const firstPageFailed = feed.error !== null && feed.items.length === 0
  const moreFailed = feed.error !== null && feed.items.length > 0
  const empty =
    feed.error === null && feed.items.length === 0 && feed.nextCursor === null

  return (
    <section className="flex flex-col gap-4" data-testid="feed">
      <div className="flex items-center justify-between gap-4">
        <h1 className="text-lg font-medium">Bảng tin</h1>
        <div className="flex items-center gap-2">
          {action}
          {/* Thay cho realtime (ngoài phạm vi): người vừa kết bạn bấm đây để thấy bài của bạn mới. */}
          <Button
            variant="outline"
            size="sm"
            onClick={feed.reload}
            disabled={feed.pending}
          >
            <RefreshCwIcon data-icon="inline-start" aria-hidden />
            Làm mới
          </Button>
        </div>
      </div>

      {/* Đ-4.6: nhãn đọc từ `mode` của server, không suy từ tác giả. Đứng TRÊN danh sách, kể cả khi rỗng. */}
      {feed.mode === "suggested" && (
        <Alert data-testid="feed-suggested">
          <SparklesIcon aria-hidden />
          <AlertDescription>
            Gợi ý cho bạn — kết bạn để thấy bài của bạn bè
          </AlertDescription>
        </Alert>
      )}

      {firstPageFailed && (
        <FirstPageError error={feed.error} onRetry={feed.reload} />
      )}

      {feed.items.map((post) => (
        <Fragment key={post.postId}>
          {renderPost(post, (next) => feed.replaceItem(post.postId, next))}
        </Fragment>
      ))}

      {empty && (
        <Card data-testid="feed-empty">
          <CardContent>
            <p className="text-sm text-muted-foreground">
              {feed.mode === "suggested"
                ? "Chưa có bài công khai nào."
                : "Chưa có bài nào — kết bạn hoặc theo dõi để thấy bài."}
            </p>
          </CardContent>
        </Card>
      )}

      {/* Không có nút nào được bấm khi feed tự nối dài (Q-E5) — trình đọc màn hình cần được báo. Kèm tổng số bài để hai
          lượt cùng thêm 20 bài vẫn là hai câu khác nhau (câu giống hệt thì nhiều trình đọc không đọc lại). */}
      <p role="status" className="sr-only" data-testid="feed-status">
        {feed.lastAdded > 0 &&
          `Đã tải thêm ${feed.lastAdded} bài, đang hiển thị ${feed.items.length} bài.`}
      </p>

      {/* Sentinel có mặt khi và chỉ khi còn trang (`nextCursor !== null`) — observer theo dõi nó. */}
      {feed.nextCursor !== null && (
        <div
          ref={sentinelRef}
          data-testid="feed-sentinel"
          className="flex min-h-8 justify-center"
        >
          {feed.pending && !moreFailed && (
            <Spinner aria-label="Đang tải thêm" />
          )}
        </div>
      )}

      {/* Tới trần trang rỗng liên tiếp: observer đã thôi, người dùng quyết định nạp tiếp. */}
      {feed.nextCursor !== null &&
        feed.error === null &&
        feed.emptyStreak >= MAX_AUTO_EMPTY_PAGES && (
          <Button
            variant="outline"
            onClick={feed.loadMore}
            disabled={feed.pending}
            className="self-center"
          >
            Xem tiếp
          </Button>
        )}

      {moreFailed && (
        <div
          role="alert"
          data-testid="feed-more-error"
          className="flex flex-col items-center gap-2"
        >
          <p className="text-sm text-destructive">
            {errorMessage("feed", feed.error)}
          </p>
          {/* Nạp lại LÔ vừa hỏng (cùng `nextCursor`), không nạp lại từ đầu — danh sách đang đọc giữ nguyên. */}
          <Button
            variant="outline"
            onClick={feed.loadMore}
            disabled={feed.pending}
            aria-busy={feed.pending || undefined}
          >
            {feed.pending && <Spinner data-icon="inline-start" aria-hidden />}
            Thử lại
          </Button>
        </div>
      )}

      {feed.error === null &&
        feed.nextCursor === null &&
        feed.items.length > 0 && (
          <p
            data-testid="feed-end"
            className="text-center text-sm text-muted-foreground"
          >
            Bạn đã xem hết
          </p>
        )}
    </section>
  )
}

/** Tự nạp được khi: còn trang, lượt gần nhất không lỗi, chưa chạm trần trang rỗng liên tiếp. */
function canAutoLoad(feed: FeedPageState) {
  return (
    feed.nextCursor !== null &&
    feed.error === null &&
    feed.emptyStreak < MAX_AUTO_EMPTY_PAGES
  )
}

/**
 * Observer TẠO TRONG EFFECT (luật frontend Mục 1 #14 — không `useRef(new IntersectionObserver(…))`): StrictMode mount lại
 * thì lần mount đầu đã `disconnect`, lần mount hai tạo cái mới. Ref chỉ là hộp đựng phần tử và trạng thái giao nhau.
 */
function useAutoLoad(feed: FeedPageState, enabled: boolean) {
  const sentinelRef = useRef<HTMLDivElement | null>(null)
  // `isIntersecting` MỚI NHẤT. Observer chỉ bắn khi trạng thái giao nhau ĐỔI — trang về mà sentinel vẫn trong khung
  // nhìn (trang ngắn, trang RỖNG còn `nextCursor`) thì không có lượt bắn nào nữa và feed đứng im (Q-E5 cạm bẫy).
  const intersectingRef = useRef(false)
  const { loadMore, pending, nextCursor } = feed
  const itemCount = feed.items.length

  useEffect(() => {
    const el = sentinelRef.current
    if (!enabled || el === null) return
    const observer = new IntersectionObserver(
      (entries) => {
        intersectingRef.current = entries.some((e) => e.isIntersecting)
        if (intersectingRef.current) loadMore()
      },
      { rootMargin: PRELOAD_MARGIN }
    )
    observer.observe(el)
    return () => {
      observer.disconnect()
      intersectingRef.current = false
    }
  }, [enabled, loadMore])

  // Sau mỗi lượt nạp XONG: sentinel vẫn giao nhau, còn trang, chưa chạm trần → nạp tiếp, không chờ observer bắn lại.
  // `nextCursor`/`itemCount` trong deps để effect chạy lại cả khi trang về RỖNG (số bài không đổi, cursor đổi).
  useEffect(() => {
    if (enabled && !pending && intersectingRef.current) loadMore()
  }, [enabled, pending, nextCursor, itemCount, loadMore])

  return sentinelRef
}

function FirstPageError({
  error,
  onRetry,
}: {
  error: unknown
  onRetry: () => void
}) {
  // 503 quá tải (Đ-4.10) nhận ra bằng `type` (Q-E4): thẻ riêng, KHÔNG mã tra cứu, KHÔNG đếm ngược, KHÔNG tự thử lại —
  // nghìn người cùng tự thử lại đúng giây thứ 5 là dồn tải đúng lúc server đang quá tải.
  if (hasProblemType(error, PROBLEM_TYPES.feedOverloaded)) {
    return (
      <Card data-testid="feed-overloaded">
        <CardContent className="flex flex-col items-start gap-4">
          <p className="text-sm">{errorMessage("feed", error)}</p>
          <Button variant="outline" onClick={onRetry}>
            Thử lại
          </Button>
        </CardContent>
      </Card>
    )
  }
  return (
    <div className="flex flex-col items-start gap-4">
      <FormAlert message={errorMessage("feed", error)} />
      <Button variant="outline" onClick={onRetry}>
        Thử lại
      </Button>
    </div>
  )
}

/** Ba thẻ mờ — chép khuôn `PostListSkeleton`, không import (Đ-E13: `features/` không import chéo). */
function FeedSkeleton() {
  return (
    <div
      className="flex flex-col gap-4"
      aria-hidden
      data-testid="feed-skeleton"
    >
      {[0, 1, 2].map((i) => (
        <Card key={i}>
          <CardContent className="flex flex-col gap-3">
            <Skeleton className="h-10 w-48" />
            <Skeleton className="h-4" />
            <Skeleton className="h-4 w-2/3" />
          </CardContent>
        </Card>
      ))}
    </div>
  )
}
