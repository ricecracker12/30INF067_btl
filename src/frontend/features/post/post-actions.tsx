"use client"

import { useState } from "react"

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
import { Button } from "@/components/ui/button"
import { Spinner } from "@/components/ui/spinner"
import { contentApi } from "@/lib/api/content-api"
import { errorMessage } from "@/lib/api/messages"
import type { PostResponse } from "@/lib/api/types"

// Nút Sửa/Xóa của một bài (E6 bước 1 và 3).
//
// **Component này không tự hỏi "bài có phải của tôi không".** Chỗ gọi chỉ render nó khi `post.canEdit`,
// và `canEdit` do SERVER tính (`author_id == actorId`). FE không bao giờ so `post.author.userId` với
// `me.userId`: so ở FE nghĩa là mỗi chỗ render lặp một bản logic quyền, và sớm muộn chúng lệch nhau —
// trong khi server vẫn là bên quyết định thật.

type Props = {
  post: PostResponse
  onEdit: () => void
  /** Đã xóa xong (204). Chỗ gọi gỡ bài khỏi danh sách, hoặc rời trang chi tiết. */
  onDeleted: () => void
  /** Lỗi của lượt xóa — hiện ở chỗ gọi, cùng chỗ với lỗi của lượt sửa. */
  onError: (message: string) => void
  /**
   * `false` khi bài bị kiểm duyệt ẩn (GĐ6 Đ-6.14): sửa rồi "tự gỡ ẩn" là lách kiểm duyệt — server trả 409 `post-hidden`, nên nút
   * Sửa không có trong DOM. Xóa vẫn giữ: người dùng luôn xóa được nội dung của mình.
   */
  canEditBody?: boolean
}

export function PostActions({
  post,
  onEdit,
  onDeleted,
  onError,
  canEditBody = true,
}: Props) {
  const [pending, setPending] = useState(false)
  const [open, setOpen] = useState(false)

  async function onConfirmDelete() {
    if (pending) return
    setPending(true)
    try {
      await contentApi.deletePost(post.postId)
      setOpen(false)
      onDeleted()
    } catch (error) {
      setOpen(false)
      // 403 = bài của người khác HOẶC bài đã xóa mềm (gọi lần hai). Một câu cho cả hai: hiện "bài đã bị
      // xóa" là xác nhận bài có tồn tại, thứ server vừa cố tình không nói.
      onError(errorMessage("post-write", error))
    } finally {
      setPending(false)
    }
  }

  return (
    <div className="flex shrink-0 gap-2">
      {canEditBody && (
        <Button variant="outline" size="sm" onClick={onEdit} disabled={pending}>
          Sửa
        </Button>
      )}

      {/* `AlertDialog` của kit, KHÔNG `window.confirm`: hộp thoại của trình duyệt không nhận được style
          của app, không dịch được, và chặn cả luồng JS. */}
      <AlertDialog open={open} onOpenChange={setOpen}>
        <AlertDialogTrigger
          render={
            <Button variant="destructive" size="sm" disabled={pending}>
              Xóa
            </Button>
          }
        />
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Xóa bài này?</AlertDialogTitle>
            <AlertDialogDescription>
              Bài sẽ không còn xem được, kể cả với bạn. Thao tác này không hoàn
              tác được.
            </AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={pending}>Hủy</AlertDialogCancel>
            <AlertDialogAction
              // `nativeButton` mặc định: giữ nút trong DOM cho tới khi request xong, để hai lần bấm
              // không thành hai lượt `DELETE` (lượt hai nhận 403 và trông như lỗi quyền).
              disabled={pending}
              aria-busy={pending || undefined}
              onClick={(event) => {
                // Hộp thoại đóng ngay khi bấm là mặc định của kit; ở đây phải giữ nó mở cho tới khi
                // server trả lời, nếu không người dùng không thấy gì xảy ra trong lúc chờ.
                event.preventDefault()
                void onConfirmDelete()
              }}
            >
              {pending && <Spinner data-icon="inline-start" aria-hidden />}
              Xóa bài
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </div>
  )
}
