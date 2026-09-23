"use client"

import Link from "next/link"
import type { ReactNode } from "react"

import { FormAlert } from "@/components/form/form-alert"
import { Button, buttonVariants } from "@/components/ui/button"
import { Card, CardContent } from "@/components/ui/card"
import { Skeleton } from "@/components/ui/skeleton"
import { Spinner } from "@/components/ui/spinner"

import { PostItem } from "./post-item"
import type { PostPageState } from "./use-post-page"

// Danh sách bài của một người: `/me` (bài của mình) và `/users/{userId}` (bài của người khác). Cùng một
// component vì khác nhau đúng hai chỗ — câu lúc chưa có bài nào, và có nút "Đăng bài đầu tiên" hay không.
//
// **Nút "Xem thêm" chứ không phải IntersectionObserver.** Bước 4 của Mục 6 nói "ẩn nút/ngắt observer";
// GĐ2 chọn nút: cuộn vô hạn tự động làm người dùng bàn phím không tới được cuối trang, và nó nuốt lỗi —
// một trang hỏng giữa chừng trông y hệt "hết bài". GĐ4 đổi sang observer thì đổi ở ĐÚNG file này, phần
// cursor bên dưới không phải chạm.

type Props = {
  page: PostPageState
  /** Hiện khi trang đầu về mà không có bài nào. */
  emptyMessage: string
  /** `/me` có nút "Đăng bài đầu tiên"; hồ sơ người khác thì không. */
  emptyAction?: ReactNode
}

export function PostList({ page, emptyMessage, emptyAction }: Props) {
  // Trang đầu chưa về và chưa có lỗi: skeleton, KHÔNG để màn trắng và cũng không nháy câu "chưa có bài".
  if (!page.loaded && page.items.length === 0 && page.error === null) {
    return <PostListSkeleton />
  }

  return (
    <div className="flex flex-col gap-4" data-testid="post-list">
      <FormAlert message={page.error} />

      {page.items.map((post) => (
        // `key` theo `postId`, KHÔNG theo index: nối trang mà dùng index thì bài nhảy chỗ và React giữ
        // nhầm state của card cũ.
        // `PostItem` chứ không `PostCard`: nút Sửa/Xóa và chế độ sửa là trạng thái của TỪNG dòng, và
        // `onChanged(null)` sau khi xóa gỡ luôn bài khỏi danh sách — để lại card cũ là để lại một liên
        // kết chết (sau xóa mềm, `GET /posts/{id}` trả 404 kể cả với tác giả).
        <PostItem
          key={post.postId}
          post={post}
          onChanged={(next) => page.replaceItem(post.postId, next)}
        />
      ))}

      {/* `error === null` cũng là điều kiện: trang đầu hỏng thì `loaded` vẫn true và `items` vẫn rỗng,
          mà "bạn chưa đăng bài nào" là một câu SAI cho người vừa mất mạng. */}
      {page.loaded && page.items.length === 0 && page.error === null && (
        <Card data-testid="post-list-empty">
          <CardContent className="flex flex-col items-start gap-4">
            <p className="text-sm text-muted-foreground">{emptyMessage}</p>
            {emptyAction}
          </CardContent>
        </Card>
      )}

      {/* Hết trang là `nextCursor === null` — nút biến mất, không phải bị `disabled`. Còn nút mà bấm
          không ra gì là mời người dùng bấm mãi. */}
      {page.nextCursor !== null && (
        <Button
          variant="outline"
          onClick={page.loadMore}
          disabled={page.pending}
          aria-busy={page.pending || undefined}
          className="self-center"
        >
          {page.pending && <Spinner data-icon="inline-start" aria-hidden />}
          Xem thêm
        </Button>
      )}

      {/* Lỗi ở trang SAU: danh sách cũ vẫn còn trên màn, chỉ cần một đường thử lại lô vừa hỏng. */}
      {page.error !== null && page.items.length > 0 && (
        <Button variant="outline" onClick={page.reload} className="self-center">
          Tải lại
        </Button>
      )}
      {/* Lỗi ở trang ĐẦU: không có gì trên màn, nút thử lại là thứ duy nhất. */}
      {page.error !== null && page.items.length === 0 && (
        <Button variant="outline" onClick={page.reload} className="self-center">
          Thử lại
        </Button>
      )}
    </div>
  )
}

/** Ba card mờ — đủ để thấy đây là một danh sách đang tới, không phải một trang hỏng. */
function PostListSkeleton() {
  return (
    <div
      className="flex flex-col gap-4"
      aria-hidden
      data-testid="post-list-skeleton"
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

// Hai nút cùng trỏ `/compose`, khác câu theo chỗ đứng: một lời mời khi chưa có bài nào, một nút thường
// trực khi đã có. Ở đây chứ không ở `app/` để màn chỉ còn việc ráp (Đ-E13).

// Điều hướng thì là LINK thật mang style nút của kit (khuôn `verify-email.tsx`), KHÔNG `<Button render={<Link/>}>`: Base UI
// dán ngữ nghĩa nút lên thẻ <a> và báo lỗi `nativeButton` — lỗi này có từ GĐ2 E5, lộ ra ở trang chủ GĐ4 (Next dev "1 Issue").
export function ComposeFirstPostButton() {
  return (
    <Link href="/compose" className={buttonVariants()}>
      Đăng bài đầu tiên
    </Link>
  )
}

export function ComposeButton() {
  return (
    <Link
      href="/compose"
      className={buttonVariants({ variant: "outline", size: "sm" })}
    >
      Đăng bài
    </Link>
  )
}
