"use client"

import { useEffect, useRef, useState } from "react"

import { FormAlert } from "@/components/form/form-alert"
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
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Skeleton } from "@/components/ui/skeleton"
import { Spinner } from "@/components/ui/spinner"
import {
  errorMessage,
  fieldMessage,
  type ErrorContext,
} from "@/lib/api/messages"
import { ApiError } from "@/lib/api/problem"
import { socialGraphApi } from "@/lib/api/socialgraph-api"
import type { RelationshipResponse } from "@/lib/api/types"

// Nút quan hệ trên hồ sơ NGƯỜI KHÁC (GĐ4 E2, FR-010/011/012). Hai hàng độc lập (Đ-4.5): kết bạn theo `friendship`, theo
// dõi theo `following` — kết bạn KHÔNG tạo dòng theo dõi, nên không suy cái này từ cái kia.
//
// **Không optimistic (Đ-4.16).** Bấm → khóa CẢ cụm → vẽ theo phản hồi. Bốn thao tác 204 (hủy/từ chối lời mời, hủy kết bạn,
// theo dõi, bỏ theo dõi) là idempotent và kết quả đã định sẵn bởi hợp đồng — cập nhật SAU khi 204 về là vẽ theo phản hồi,
// chỉ là phản hồi không có body. Cái bị cấm là cập nhật TRƯỚC khi phản hồi về.
//
// Component không tự hỏi "đây có phải hồ sơ của tôi không": `GET /relationships/{mình}` là 400, và chỉ `app/` biết cả
// người đang đăng nhập lẫn người đang xem (Q-E2) — nó không render component này cho hồ sơ của chính mình.

type Props = {
  userId: string
  /** Tên người kia, cho câu xác nhận Hủy kết bạn. `app/` truyền vào — `features/friend` không đọc hồ sơ (Đ-E13). */
  displayName?: string
}

type Loaded =
  { key: string; rel: RelationshipResponse } | { key: string; error: string }

type Action =
  "send" | "cancel" | "accept" | "decline" | "unfriend" | "follow" | "unfollow"

/** Mỗi thao tác một ngữ cảnh lỗi (Q-E3) — 403/404/409 mang nghĩa khác nhau trên từng endpoint. */
const CONTEXT: Record<Action, ErrorContext> = {
  send: "friend-request",
  cancel: "relationship",
  accept: "friend-respond",
  decline: "relationship",
  unfriend: "relationship",
  follow: "follow",
  unfollow: "relationship",
}

/**
 * Lỗi nào nghĩa là "màn đang vẽ một quan hệ đã cũ" → đọc lại `GET /relationships` để vẽ sự thật. 409 kết bạn: thường là
 * người kia vừa gửi trước, nút đúng bây giờ là "Chấp nhận". 403 chấp nhận: lời mời đã bị hủy trong lúc màn đang mở (FRD-10).
 * 404 (không có hồ sơ) và lỗi khác: giữ trạng thái cũ — không đoán.
 */
function shouldReread(action: Action, e: unknown) {
  if (!(e instanceof ApiError)) return false
  return (
    (action === "send" && e.status === 409) ||
    (action === "accept" && e.status === 403)
  )
}

async function perform(
  action: Action,
  rel: RelationshipResponse
): Promise<RelationshipResponse> {
  const id = rel.userId
  switch (action) {
    case "send":
      return socialGraphApi.sendRequest(id)
    case "accept":
      return socialGraphApi.accept(id)
    // 204: giữa hai người KHÔNG còn lời mời nào, theo cả hai chiều.
    case "cancel":
    case "decline":
      await socialGraphApi.removeRequest(id)
      return { ...rel, friendship: "none" }
    // 204: nút chỉ hiện khi `friends`, tức không có lời mời `pending` nào mà endpoint này bỏ sót.
    case "unfriend":
      await socialGraphApi.unfriend(id)
      return { ...rel, friendship: "none" }
    case "follow":
      await socialGraphApi.follow(id)
      return { ...rel, following: true }
    case "unfollow":
      await socialGraphApi.unfollow(id)
      return { ...rel, following: false }
  }
}

export function RelationshipButtons({ userId, displayName }: Props) {
  const [data, setData] = useState<Loaded | null>(null)
  const [attempt, setAttempt] = useState(0)
  const [pending, setPending] = useState<Action | null>(null)
  // Lỗi của thao tác GHI, gắn theo `userId` chứ không theo lượt đọc: đọc lại sau 409/403 đổi `key` mà câu lỗi phải còn.
  const [actionError, setActionError] = useState<{
    userId: string
    message: string
  } | null>(null)
  const [confirmOpen, setConfirmOpen] = useState(false)
  // Chặn bấm đôi ngay cả trước khi `disabled` kịp render.
  const pendingRef = useRef(false)
  // `key` đang hiển thị, để thao tác ghi về muộn biết người dùng đã sang hồ sơ khác chưa. Ghi trong effect, không lúc
  // render (nếp `currentRef` của `use-cursor-pages.ts`).
  const shownKeyRef = useRef<string | null>(null)

  // Khuôn "key + suy ra" của `PublicProfile`: không `setState` đồng bộ trong effect; quan hệ với người này không nháy
  // lên khi điều hướng sang hồ sơ người kia.
  const key = `${userId}:${attempt}`
  const current = data?.key === key ? data : null
  const message = actionError?.userId === userId ? actionError.message : null

  useEffect(() => {
    shownKeyRef.current = key
  })

  useEffect(() => {
    // Tạo TRONG effect (luật frontend Mục 1 #14): StrictMode hủy controller của lần mount đầu, lần mount hai tạo mới.
    const controller = new AbortController()
    const pageKey = `${userId}:${attempt}`
    socialGraphApi.relationship(userId, controller.signal).then(
      (rel) => setData({ key: pageKey, rel }),
      (e: unknown) => {
        if ((e as Error).name === "AbortError") return
        setData({ key: pageKey, error: errorMessage("relationship", e) })
      }
    )
    return () => controller.abort()
  }, [userId, attempt])

  async function run(action: Action) {
    if (pendingRef.current || current === null || !("rel" in current)) return
    pendingRef.current = true
    const clickedKey = key
    setPending(action)
    setActionError(null)
    try {
      const next = await perform(action, current.rel)
      // Cập nhật HÀM, chỉ khi slot vẫn là của lượt đã bấm: phản hồi của người CŨ về sau khi đã sang hồ sơ khác mà ghi đè
      // thẳng thì slot mang `key` cũ, hồ sơ người mới quay lại skeleton mãi.
      setData((prev) =>
        prev?.key === clickedKey ? { key: clickedKey, rel: next } : prev
      )
      setConfirmOpen(false)
    } catch (e) {
      setConfirmOpen(false)
      // `fieldMessage`: 400 `errors.userId` (tự gửi/tự theo dõi) hiện ĐÚNG câu server (Đ-E5), còn lại theo bảng Q-E3.
      setActionError({
        userId,
        message: fieldMessage(e, "userId", CONTEXT[action]),
      })
      // Chỉ đọc lại khi vẫn đang xem đúng lượt đã bấm — sang hồ sơ khác rồi thì hồ sơ đó tự nạp, không cần thêm lượt.
      if (shouldReread(action, e) && shownKeyRef.current === clickedKey)
        setAttempt((n) => n + 1)
    } finally {
      pendingRef.current = false
      setPending(null)
    }
  }

  const alert = <FormAlert message={message} />

  if (current === null) {
    return (
      <div className="flex flex-col gap-3" data-testid="relationship-loading">
        {alert}
        <div className="flex gap-2" aria-hidden>
          <Skeleton className="h-8 w-28" />
          <Skeleton className="h-8 w-24" />
        </div>
      </div>
    )
  }

  if ("error" in current) {
    return (
      <div className="flex flex-col items-start gap-3">
        <FormAlert message={current.error} />
        <Button variant="outline" onClick={() => setAttempt((n) => n + 1)}>
          Thử lại
        </Button>
      </div>
    )
  }

  const { rel } = current
  const busy = pending !== null

  /** Một nút của cụm: MỌI nút khóa khi một thao tác đang bay, nút vừa bấm có spinner. */
  const nut = (
    action: Action,
    label: string,
    testId: string,
    variant: "default" | "outline" | "destructive" = "outline"
  ) => (
    <Button
      variant={variant}
      size="sm"
      data-testid={testId}
      disabled={busy}
      aria-busy={pending === action || undefined}
      onClick={() => void run(action)}
    >
      {pending === action && <Spinner data-icon="inline-start" aria-hidden />}
      {label}
    </Button>
  )

  return (
    <div className="flex flex-col gap-3" data-testid="relationship-buttons">
      {alert}
      <div className="flex flex-wrap items-center gap-2">
        {rel.friendship === "none" &&
          nut("send", "Kết bạn", "nut-ket-ban", "default")}

        {rel.friendship === "outgoing" && (
          <>
            <Badge variant="secondary" data-testid="nhan-da-gui">
              Đã gửi lời mời
            </Badge>
            {nut("cancel", "Hủy lời mời", "nut-huy-loi-moi")}
          </>
        )}

        {rel.friendship === "incoming" && (
          <>
            {nut("accept", "Chấp nhận", "nut-chap-nhan", "default")}
            {nut("decline", "Từ chối", "nut-tu-choi")}
          </>
        )}

        {rel.friendship === "friends" && (
          <>
            <Badge variant="secondary" data-testid="nhan-ban-be">
              Bạn bè
            </Badge>
            {/* `AlertDialog` của kit, không `window.confirm` (nếp `PostActions`). Hủy trong hộp thoại → 0 request. */}
            <AlertDialog open={confirmOpen} onOpenChange={setConfirmOpen}>
              <AlertDialogTrigger
                render={
                  <Button
                    variant="outline"
                    size="sm"
                    data-testid="nut-huy-ket-ban"
                    disabled={busy}
                  >
                    Hủy kết bạn
                  </Button>
                }
              />
              <AlertDialogContent>
                <AlertDialogHeader>
                  <AlertDialogTitle>Hủy kết bạn?</AlertDialogTitle>
                  <AlertDialogDescription>
                    {displayName
                      ? `Bài chỉ dành cho bạn bè của ${displayName} sẽ không còn hiện với bạn.`
                      : "Bài chỉ dành cho bạn bè của người này sẽ không còn hiện với bạn."}
                  </AlertDialogDescription>
                </AlertDialogHeader>
                <AlertDialogFooter>
                  <AlertDialogCancel disabled={busy}>Không</AlertDialogCancel>
                  <AlertDialogAction
                    variant="destructive"
                    disabled={busy}
                    aria-busy={pending === "unfriend" || undefined}
                    onClick={(event) => {
                      // Giữ hộp thoại mở tới khi server trả lời — người dùng thấy thao tác đang chạy.
                      event.preventDefault()
                      void run("unfriend")
                    }}
                  >
                    {pending === "unfriend" && (
                      <Spinner data-icon="inline-start" aria-hidden />
                    )}
                    Hủy kết bạn
                  </AlertDialogAction>
                </AlertDialogFooter>
              </AlertDialogContent>
            </AlertDialog>
          </>
        )}

        {/* Hàng theo dõi — độc lập với kết bạn (Đ-4.5). */}
        {rel.following
          ? nut("unfollow", "Bỏ theo dõi", "nut-bo-theo-doi")
          : nut("follow", "Theo dõi", "nut-theo-doi")}
      </div>
    </div>
  )
}
