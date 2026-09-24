"use client"

import Link from "next/link"
import { useRouter } from "next/navigation"
import { useCallback, useEffect, useState, type ReactNode } from "react"

import { FormAlert } from "@/components/form/form-alert"
import { Button, buttonVariants } from "@/components/ui/button"
import { Card, CardContent } from "@/components/ui/card"
import { Skeleton } from "@/components/ui/skeleton"
import { contentApi } from "@/lib/api/content-api"
import { errorMessage } from "@/lib/api/messages"
import { ApiError } from "@/lib/api/problem"
import type { PostResponse } from "@/lib/api/types"

import { PostItem } from "./post-item"

/**
 * Chi tiết một bài — `GET /posts/{postId}`.
 *
 * **404 có BA nghĩa** và FE chỉ được nói MỘT câu (Mục 7.4): bài không tồn tại · bài đã xóa mềm (kể cả với
 * chính tác giả, Mục 7.3) · bài tồn tại nhưng BR-02 không cho người gọi xem. Server cố ý trả cùng một
 * phản hồi cho cả ba; nói khác đi ở FE là dùng status code để khai tài nguyên nào có thật.
 *
 * `notFound` tách khỏi `error` vì hai thứ khác nhau với người dùng: 404 là câu trả lời cuối cùng (không có
 * nút "Thử lại" — thử lại bao nhiêu lần cũng vậy), còn 429/5xx/mất mạng thì thử lại là việc đúng.
 */

/**
 * Kết quả của MỘT lượt mở bài, gắn `key = "{postId}:{attempt}"`. Gom vào một object có `key` chứ không
 * rải thành ba `useState` là để KHÔNG `setState` đồng bộ trong effect (`react-hooks/set-state-in-effect`):
 * đổi bài hay bấm Thử lại chỉ đổi `key`, và kết quả cũ tự hết hiệu lực vì `key` không khớp nữa. Nhờ vậy
 * bài cũ không nháy lên khi điều hướng sang bài khác.
 */
type Loaded =
  | { key: string; post: PostResponse }
  | { key: string; notFound: true }
  | { key: string; error: string }

type Props = {
  postId: string
  /** Hàng tương tác trong card (GĐ3 — thanh cảm xúc), do `app/` ghép. */
  footer?: (post: PostResponse) => ReactNode
  /** Dưới card (GĐ3 — cây bình luận), do `app/` ghép. Chỉ hiện khi bài đã nạp được. */
  below?: (post: PostResponse) => ReactNode
}

export function PostDetail({ postId, footer, below }: Props) {
  const router = useRouter()
  const [data, setData] = useState<Loaded | null>(null)
  const [attempt, setAttempt] = useState(0)

  const key = `${postId}:${attempt}`
  const current = data?.key === key ? data : null

  useEffect(() => {
    // Rời trang (hoặc bấm Thử lại khi request cũ chưa xong) thì hủy request cũ.
    const controller = new AbortController()
    const pageKey = `${postId}:${attempt}`

    contentApi.getPost(postId, controller.signal).then(
      (res) => setData({ key: pageKey, post: res }),
      (e: unknown) => {
        if ((e as Error).name === "AbortError") return
        if (e instanceof ApiError && e.status === 404)
          setData({ key: pageKey, notFound: true })
        else setData({ key: pageKey, error: errorMessage("post-read", e) })
      }
    )
    return () => controller.abort()
  }, [postId, attempt])

  /**
   * Bài đổi: sửa xong (200 trả `PostResponse` mới), hoặc card nạp lại vì ảnh presigned hết hạn (Đ-2.9).
   * `null` là vừa xóa — **rời trang ngay**: sau xóa mềm, `GET /posts/{id}` trả 404 kể cả với chính tác
   * giả (Mục 7.3), nên ở lại là ở lại trên một URL đã chết.
   */
  const onChanged = useCallback(
    (next: PostResponse | null) => {
      if (next === null) {
        router.replace("/me")
        return
      }
      setData({ key, post: next })
    },
    [key, router]
  )

  if (current === null) {
    return (
      <Card aria-hidden data-testid="post-detail-skeleton">
        <CardContent className="flex flex-col gap-3">
          <Skeleton className="h-10 w-48" />
          <Skeleton className="h-4" />
          <Skeleton className="h-4 w-2/3" />
        </CardContent>
      </Card>
    )
  }

  if ("notFound" in current) return <PostNotFound />

  if ("error" in current) {
    return (
      <div className="flex flex-col gap-4">
        <FormAlert message={current.error} />
        <Button
          variant="outline"
          className="self-start"
          onClick={() => setAttempt((n) => n + 1)}
        >
          Thử lại
        </Button>
      </div>
    )
  }

  return (
    <div className="flex flex-col gap-6">
      <PostItem
        post={current.post}
        standalone
        onChanged={onChanged}
        footer={footer}
      />
      {below?.(current.post)}
    </div>
  )
}

/**
 * ĐÚNG MỘT CÂU cho cả ba nghĩa của 404. Không "Bài đã bị xóa", không "Bạn không có quyền xem bài này" —
 * mỗi câu đó đều xác nhận bài có tồn tại, thứ mà server vừa cố tình không nói.
 */
function PostNotFound() {
  return (
    <Card data-testid="post-not-found">
      <CardContent className="flex flex-col items-start gap-4">
        <p>Không tìm thấy bài viết.</p>
        <p className="text-sm text-muted-foreground">
          Bài có thể đã bị xóa, hoặc bạn không có quyền xem bài này.
        </p>
        {/* LINK thật mang style nút (khuôn `verify-email.tsx`) — không `<Button render={<Link/>}>` (lỗi `nativeButton`). */}
        <Link href="/me" className={buttonVariants({ variant: "outline" })}>
          Về trang của tôi
        </Link>
      </CardContent>
    </Card>
  )
}
