"use client"

import { Trash } from "lucide-react"
import Link from "next/link"
import { useCallback, useState, type ReactNode } from "react"

import {
  AlertDialog,
  AlertDialogAction,
  AlertDialogCancel,
  AlertDialogContent,
  AlertDialogDescription,
  AlertDialogFooter,
  AlertDialogHeader,
  AlertDialogTitle,
  AlertDialogTrigger,
} from "@/components/ui/alert-dialog"
import { Avatar, AvatarFallback, AvatarImage } from "@/components/ui/avatar"
import { Button } from "@/components/ui/button"
import { Spinner } from "@/components/ui/spinner"
import { contentApi } from "@/lib/api/content-api"
import { errorMessage } from "@/lib/api/messages"
import type { CommentResponse } from "@/lib/api/types"

import { CommentComposer } from "./comment-composer"
import { useCommentPages } from "./use-comment-pages"

// Một bình luận cùng nhánh con của nó (Đ-3.6: tải lười theo cấp). Đệ quy: phản hồi của một bình luận cũng là `CommentItem`, sâu
// tối đa 3 — cấp 3 KHÔNG có nút Trả lời (BR-08). Server vẫn chặn cấp 4; FE ẩn nút để người dùng thường không bao giờ chạm tới.
//
// Bình luận đã xóa (hay bị ẩn — GĐ6) vẫn giữ chỗ và nhánh: một dòng "đã bị xóa", không tác giả, không nội dung, không cảm xúc,
// và "Xem N phản hồi" vẫn còn (Đ-3.5). Nút Xóa hiện theo `canDelete` của SERVER — FE không tự so id tác giả.

/** Số phản hồi mỗi trang khi mở một nhánh (Đ-3.6: FE dùng 10 cho phản hồi). */
export const REPLY_PAGE_SIZE = 10

/** Tối đa 3 cấp (BR-08) — bằng `CommentDepthPolicy.MaxDepth`. */
const MAX_DEPTH = 3

const dateTime = new Intl.DateTimeFormat("vi-VN", {
  dateStyle: "medium",
  timeStyle: "short",
  timeZone: "Asia/Ho_Chi_Minh",
})

/** Bản FE của mapper server cho bình luận vừa xóa (Đ-3.5) — hiện ngay, không nạp lại cả nhánh. */
function asDeleted(c: CommentResponse): CommentResponse {
  return {
    ...c,
    status: "deleted",
    author: null,
    body: null,
    reactionCounts: {},
    myReaction: null,
    canDelete: false,
  }
}

export type RenderReactions = (comment: CommentResponse) => ReactNode

type Props = {
  comment: CommentResponse
  postId: string
  /** Bình luận này vừa đổi (xóa mềm, có thêm phản hồi) — danh sách đang giữ nó thay tại chỗ. */
  onChange: (next: CommentResponse) => void
  /** `commentCount` của bài đổi (+1 khi tạo, −1 khi xóa) — đếm bình luận ĐANG HIỂN THỊ ở mọi cấp (Đ-3.5). */
  onCountDelta: (delta: number) => void
  /** Slot thanh cảm xúc, do `app/` ghép (Đ-3.13) — feature này không biết `features/reaction` tồn tại. */
  renderReactions?: RenderReactions
}

export function CommentItem({
  comment,
  postId,
  onChange,
  onCountDelta,
  renderReactions,
}: Props) {
  const [replying, setReplying] = useState(false)
  const [open, setOpen] = useState(false)
  const visible = comment.status === "visible"

  const replies = useCommentPages(
    open ? `replies:${comment.commentId}` : null,
    useCallback(
      (cursor: string | null, signal?: AbortSignal) =>
        contentApi.listReplies(
          comment.commentId,
          { cursor, limit: REPLY_PAGE_SIZE },
          signal
        ),
      [comment.commentId]
    )
  )

  function onReplied(reply: CommentResponse) {
    setReplying(false)
    // Mở nhánh nếu chưa mở — lượt tải đầu có luôn phản hồi vừa tạo; đã mở thì chèn vào cuối.
    if (open) replies.append(reply)
    else setOpen(true)
    onChange({ ...comment, replyCount: comment.replyCount + 1 })
    onCountDelta(1)
  }

  return (
    <li
      className="flex flex-col gap-2"
      data-testid="comment"
      data-comment-id={comment.commentId}
      data-depth={comment.depth}
    >
      <div className="flex gap-3">
        <Avatar size="sm">
          {comment.author?.avatarUrl && (
            <AvatarImage
              src={comment.author.avatarUrl}
              alt={`Ảnh đại diện của ${comment.author.displayName}`}
            />
          )}
          <AvatarFallback>
            {comment.author?.displayName.trim().charAt(0).toUpperCase() ?? "·"}
          </AvatarFallback>
        </Avatar>

        <div className="flex min-w-0 flex-1 flex-col gap-1">
          {visible && comment.author ? (
            <>
              <div className="flex flex-wrap items-center gap-2 text-xs text-muted-foreground">
                <Link
                  href={`/users/${comment.author.userId}`}
                  className="text-sm font-medium text-foreground underline-offset-4 hover:underline"
                >
                  {comment.author.displayName}
                </Link>
                <time dateTime={comment.createdAt}>
                  {dateTime.format(new Date(comment.createdAt))}
                </time>
              </div>
              <p className="text-sm break-words whitespace-pre-line">
                {comment.body}
              </p>
            </>
          ) : (
            <p
              className="text-sm text-muted-foreground italic"
              data-testid="comment-removed"
            >
              {comment.status === "hidden"
                ? "Bình luận đã bị ẩn."
                : "Bình luận đã bị xóa."}
            </p>
          )}

          <div className="flex flex-wrap items-center gap-1">
            {visible && renderReactions?.(comment)}
            {visible && comment.depth < MAX_DEPTH && (
              <Button
                variant="ghost"
                size="sm"
                onClick={() => setReplying((v) => !v)}
                aria-expanded={replying}
              >
                Trả lời
              </Button>
            )}
            {comment.canDelete && (
              <DeleteComment
                comment={comment}
                onDeleted={() => {
                  onChange(asDeleted(comment))
                  onCountDelta(-1)
                }}
              />
            )}
          </div>

          {replying && (
            <CommentComposer
              postId={postId}
              parentId={comment.commentId}
              label={`Trả lời ${comment.author?.displayName ?? "bình luận"}`}
              autoFocus
              onCreated={onReplied}
              onCancel={() => setReplying(false)}
            />
          )}

          {comment.replyCount > 0 && !open && (
            <Button
              variant="link"
              size="sm"
              className="self-start px-0"
              onClick={() => setOpen(true)}
            >
              Xem {comment.replyCount} phản hồi
            </Button>
          )}

          {open && (
            <CommentList
              list={replies}
              postId={postId}
              onCountDelta={onCountDelta}
              renderReactions={renderReactions}
              moreLabel="Xem thêm phản hồi"
              testId="replies"
            />
          )}
        </div>
      </div>
    </li>
  )
}

/**
 * Một danh sách bình luận đã tải — dùng cho bình luận gốc (ở `CommentThread`) và cho phản hồi (lồng trong `CommentItem`). Lỗi
 * của trang SAU không xóa danh sách cũ; "Xem thêm" biến mất khi `nextCursor === null`.
 */
export function CommentList({
  list,
  postId,
  onCountDelta,
  renderReactions,
  moreLabel,
  testId,
}: {
  list: ReturnType<typeof useCommentPages>
  postId: string
  onCountDelta: (delta: number) => void
  renderReactions?: RenderReactions
  moreLabel: string
  testId: string
}) {
  const { page, items, update } = list

  return (
    <div className="flex flex-col gap-3">
      {page.error !== null && (
        <p role="alert" className="text-sm text-destructive">
          {errorMessage("comment-read", page.error)}
        </p>
      )}
      {!page.loaded && page.error === null && (
        <Spinner aria-label="Đang tải bình luận" />
      )}
      {items.length > 0 && (
        <ul
          className={
            testId === "replies"
              ? "flex flex-col gap-3 border-l border-border pl-4"
              : "flex flex-col gap-4"
          }
          data-testid={testId}
        >
          {items.map((c) => (
            <CommentItem
              key={c.commentId}
              comment={c}
              postId={postId}
              onChange={(next) => update(c.commentId, next)}
              onCountDelta={onCountDelta}
              renderReactions={renderReactions}
            />
          ))}
        </ul>
      )}
      {page.nextCursor !== null && (
        <Button
          variant="outline"
          size="sm"
          className="self-start"
          onClick={page.loadMore}
          disabled={page.pending}
          aria-busy={page.pending || undefined}
        >
          {page.pending && <Spinner data-icon="inline-start" aria-hidden />}
          {moreLabel}
        </Button>
      )}
    </div>
  )
}

function DeleteComment({
  comment,
  onDeleted,
}: {
  comment: CommentResponse
  onDeleted: () => void
}) {
  const [open, setOpen] = useState(false)
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<string | null>(null)

  async function onConfirm() {
    if (pending) return
    setPending(true)
    try {
      await contentApi.deleteComment(comment.commentId)
      setOpen(false)
      onDeleted()
    } catch (e) {
      setOpen(false)
      // 403 = của người khác HOẶC đã xóa (tab khác vừa xóa) — một câu cho cả hai.
      setError(errorMessage("comment-delete", e))
    } finally {
      setPending(false)
    }
  }

  return (
    <>
      <AlertDialog open={open} onOpenChange={setOpen}>
        <AlertDialogTrigger
          render={
            <Button variant="ghost" size="sm" disabled={pending}>
              <Trash data-icon="inline-start" aria-hidden />
              Xóa
            </Button>
          }
        />
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Xóa bình luận này?</AlertDialogTitle>
            <AlertDialogDescription>
              Nội dung sẽ không còn hiển thị. Các phản hồi bên dưới vẫn được giữ
              lại.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={pending}>Hủy</AlertDialogCancel>
            <AlertDialogAction
              disabled={pending}
              aria-busy={pending || undefined}
              onClick={(event) => {
                // Giữ hộp thoại mở tới khi server trả lời — cùng lý do `PostActions`: hai lần bấm không thành hai lượt DELETE.
                event.preventDefault()
                void onConfirm()
              }}
            >
              {pending && <Spinner data-icon="inline-start" aria-hidden />}
              Xóa bình luận
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
      {error && (
        <p role="alert" className="w-full text-xs text-destructive">
          {error}
        </p>
      )}
    </>
  )
}
