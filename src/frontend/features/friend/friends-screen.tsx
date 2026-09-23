"use client"

import { useRef, useState, type ReactNode } from "react"

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
import { Button } from "@/components/ui/button"
import { Card, CardContent } from "@/components/ui/card"
import { Skeleton } from "@/components/ui/skeleton"
import { Spinner } from "@/components/ui/spinner"
import { useCursorPages, type CursorPagesState } from "@/hooks/use-cursor-pages"
import {
  errorMessage,
  fieldMessage,
  type ErrorContext,
} from "@/lib/api/messages"
import { ApiError } from "@/lib/api/problem"
import { socialGraphApi } from "@/lib/api/socialgraph-api"
import type {
  FriendCard as FriendCardData,
  FriendPage,
  FriendRequestPage,
} from "@/lib/api/types"

import { FriendCard } from "./friend-card"

// Màn `/friends` (GĐ4 E3): chỗ người nhận THẤY lời mời — trước GĐ6 không có thông báo nào.
//
// Ba mục xếp dọc theo thứ tự việc cần làm; không tab (kit không có, và ba mục ngắn đọc một lượt nhanh hơn ba lần bấm).
// Mỗi mục MỘT lượt `useCursorPages` riêng — gộp một danh sách thì người vừa chấp nhận có mặt hai lần cùng `key`. Nút
// "Xem thêm", không observer: Q-E5 (cuộn tự động) chỉ áp cho feed; ba danh sách chồng trên một trang mà tự nạp thì mục
// giữa đẩy mục dưới đi mãi.
//
// Mục "Lời mời đã gửi" đứng CUỐI: nó là ứng viên cắt số 1 (B.9) — cắt là xóa một khối.

/** Mặc định của hợp đồng. KHÔNG dùng để suy "hết" — chỉ `nextCursor === null` (Đ-4.9). */
export const FRIENDS_PAGE_SIZE = 20

type Page = FriendPage | FriendRequestPage
type Section = CursorPagesState<FriendCardData, Page>

const cardId = (c: FriendCardData) => c.user.userId

export function FriendsScreen() {
  const incoming = useCursorPages<FriendCardData, FriendRequestPage>({
    key: "friends:incoming",
    fetchPage: (cursor, signal) =>
      socialGraphApi.listRequests(
        "incoming",
        { cursor, limit: FRIENDS_PAGE_SIZE },
        signal
      ),
    getId: cardId,
  })
  const friends = useCursorPages<FriendCardData, FriendPage>({
    key: "friends:list",
    fetchPage: (cursor, signal) =>
      socialGraphApi.listFriends({ cursor, limit: FRIENDS_PAGE_SIZE }, signal),
    getId: cardId,
  })
  const outgoing = useCursorPages<FriendCardData, FriendRequestPage>({
    key: "friends:outgoing",
    fetchPage: (cursor, signal) =>
      socialGraphApi.listRequests(
        "outgoing",
        { cursor, limit: FRIENDS_PAGE_SIZE },
        signal
      ),
    getId: cardId,
  })

  // 403 Chấp nhận gỡ thẻ (lời mời không còn) — câu lỗi không thể ở "dưới thẻ" nữa, nên lên đầu mục, kèm tên người gửi.
  const [incomingNotice, setIncomingNotice] = useState<string | null>(null)

  return (
    <div className="flex flex-col gap-8" data-testid="friends-screen">
      <FriendSection
        title="Lời mời kết bạn"
        testId="section-incoming"
        page={incoming}
        emptyMessage="Chưa có lời mời nào."
        notice={incomingNotice}
        renderCard={(card) => (
          <IncomingItem
            key={card.user.userId}
            card={card}
            onAccepted={() => {
              setIncomingNotice(null)
              incoming.replaceItem(card.user.userId, null)
              // Bạn mới nằm đầu theo `accepted_at DESC` — NẠP LẠI, không tự dựng `FriendCard` với `since = now()`
              // (lệch giờ server, và lặp khi nạp lại).
              friends.reload()
            }}
            onDeclined={() => {
              setIncomingNotice(null)
              incoming.replaceItem(card.user.userId, null)
            }}
            onGone={(message) => {
              setIncomingNotice(`${card.user.displayName}: ${message}`)
              incoming.replaceItem(card.user.userId, null)
            }}
          />
        )}
      />

      <FriendSection
        title="Bạn bè"
        testId="section-friends"
        page={friends}
        // Trỏ về feed gợi ý — đường gặp người khác duy nhất trước GĐ6 (Đ-4.6).
        emptyMessage="Bạn chưa có bạn bè nào. Mở trang chủ để xem bài công khai và kết bạn với tác giả."
        renderCard={(card) => (
          <FriendItem
            key={card.user.userId}
            card={card}
            onRemoved={() => friends.replaceItem(card.user.userId, null)}
          />
        )}
      />

      <FriendSection
        title="Lời mời đã gửi"
        testId="section-outgoing"
        page={outgoing}
        emptyMessage="Bạn chưa gửi lời mời nào."
        renderCard={(card) => (
          <OutgoingItem
            key={card.user.userId}
            card={card}
            onRemoved={() => outgoing.replaceItem(card.user.userId, null)}
          />
        )}
      />
    </div>
  )
}

// ── Một mục ──────────────────────────────────────────────────────────────────────────────────────────────────────────

function FriendSection({
  title,
  testId,
  page,
  emptyMessage,
  notice,
  renderCard,
}: {
  title: string
  testId: string
  page: Section
  emptyMessage: string
  notice?: string | null
  renderCard: (card: FriendCardData) => ReactNode
}) {
  const firstPageFailed = page.error !== null && page.items.length === 0
  const moreFailed = page.error !== null && page.items.length > 0
  const empty =
    page.loaded &&
    page.error === null &&
    page.items.length === 0 &&
    page.nextCursor === null

  return (
    <section className="flex flex-col gap-3" data-testid={testId}>
      <h2 className="text-lg font-medium">{title}</h2>

      {notice && (
        <p role="alert" className="text-sm text-destructive">
          {notice}
        </p>
      )}

      {!page.loaded && (
        <div
          className="flex flex-col gap-3"
          aria-hidden
          data-testid={`${testId}-skeleton`}
        >
          {[0, 1].map((i) => (
            <Skeleton key={i} className="h-16 rounded-2xl" />
          ))}
        </div>
      )}

      {firstPageFailed && (
        <div className="flex flex-col items-start gap-3">
          <FormAlert message={errorMessage("relationship", page.error)} />
          <Button variant="outline" onClick={page.reload}>
            Thử lại
          </Button>
        </div>
      )}

      {page.items.map(renderCard)}

      {empty && (
        <Card size="sm" data-testid={`${testId}-empty`}>
          <CardContent>
            <p className="text-sm text-muted-foreground">{emptyMessage}</p>
          </CardContent>
        </Card>
      )}

      {/* Còn trang KHI VÀ CHỈ KHI `nextCursor !== null` (Đ-4.9) — trang ngắn, kể cả rỗng, vẫn có nút. Trang sau hỏng thì
          nút này kiêm "Thử lại" lô vừa hỏng (cùng `nextCursor`), danh sách đang có giữ nguyên. */}
      {page.loaded && !firstPageFailed && page.nextCursor !== null && (
        <div className="flex flex-col items-center gap-2">
          {moreFailed && (
            <p role="alert" className="text-sm text-destructive">
              {errorMessage("relationship", page.error)}
            </p>
          )}
          <Button
            variant="outline"
            onClick={page.loadMore}
            disabled={page.pending}
            aria-busy={page.pending || undefined}
          >
            {page.pending && <Spinner data-icon="inline-start" aria-hidden />}
            {moreFailed ? "Thử lại" : "Xem thêm"}
          </Button>
        </div>
      )}
    </section>
  )
}

// ── Thao tác trên MỘT thẻ: khóa theo thẻ, không khóa cả màn ─────────────────────────────────────────────────────────

/**
 * `pending` + lỗi của RIÊNG một thẻ. Người có 20 lời mời không phải đợi từng cái — các thẻ là các người khác nhau, không
 * giẫm lên nhau (khác E2, nơi cả cụm nói về MỘT người).
 */
function useCardAction() {
  const [pending, setPending] = useState<string | null>(null)
  const [message, setMessage] = useState<string | null>(null)
  const pendingRef = useRef(false)

  async function run(
    name: string,
    context: ErrorContext,
    call: () => Promise<unknown>,
    onDone: () => void,
    onError?: (e: unknown, message: string) => boolean
  ) {
    if (pendingRef.current) return
    pendingRef.current = true
    setPending(name)
    setMessage(null)
    try {
      await call()
      onDone()
    } catch (e) {
      // `fieldMessage`: 400 `errors.userId` hiện đúng câu server (Đ-E5), còn lại theo bảng Q-E3.
      const text = fieldMessage(e, "userId", context)
      // `onError` trả `true` = đã xử (ví dụ gỡ thẻ) — không hiện câu dưới thẻ nữa.
      if (!onError?.(e, text)) setMessage(text)
    } finally {
      pendingRef.current = false
      setPending(null)
    }
  }

  return { pending, message, run }
}

function ActionButton({
  name,
  label,
  pending,
  onClick,
  variant = "outline",
}: {
  name: string
  label: string
  pending: string | null
  onClick: () => void
  variant?: "default" | "outline"
}) {
  return (
    <Button
      variant={variant}
      size="sm"
      disabled={pending !== null}
      aria-busy={pending === name || undefined}
      onClick={onClick}
    >
      {pending === name && <Spinner data-icon="inline-start" aria-hidden />}
      {label}
    </Button>
  )
}

function IncomingItem({
  card,
  onAccepted,
  onDeclined,
  onGone,
}: {
  card: FriendCardData
  onAccepted: () => void
  onDeclined: () => void
  onGone: (message: string) => void
}) {
  const { pending, message, run } = useCardAction()
  const id = card.user.userId
  return (
    <FriendCard
      card={card}
      sinceLabel="Gửi lúc"
      message={message}
      actions={
        <>
          <ActionButton
            name="accept"
            label="Chấp nhận"
            variant="default"
            pending={pending}
            onClick={() =>
              void run(
                "accept",
                "friend-respond",
                () => socialGraphApi.accept(id),
                onAccepted,
                // 403 = lời mời không còn (đã bị hủy, đã là bạn — Đ-4.14): gỡ thẻ, câu lên đầu mục.
                (e, text) => {
                  if (!(e instanceof ApiError) || e.status !== 403) return false
                  onGone(text)
                  return true
                }
              )
            }
          />
          <ActionButton
            name="decline"
            label="Từ chối"
            pending={pending}
            onClick={() =>
              void run(
                "decline",
                "relationship",
                () => socialGraphApi.removeRequest(id),
                onDeclined
              )
            }
          />
        </>
      }
    />
  )
}

function FriendItem({
  card,
  onRemoved,
}: {
  card: FriendCardData
  onRemoved: () => void
}) {
  const { pending, message, run } = useCardAction()
  const [open, setOpen] = useState(false)
  const name = card.user.displayName
  return (
    <FriendCard
      card={card}
      sinceLabel="Bạn bè từ"
      message={message}
      actions={
        // `AlertDialog` của kit (nếp `PostActions`, E2). "Không" → 0 request.
        <AlertDialog open={open} onOpenChange={setOpen}>
          <AlertDialogTrigger
            render={
              <Button variant="outline" size="sm" disabled={pending !== null}>
                Hủy kết bạn
              </Button>
            }
          />
          <AlertDialogContent>
            <AlertDialogHeader>
              <AlertDialogTitle>Hủy kết bạn với {name}?</AlertDialogTitle>
              <AlertDialogDescription>
                Bài chỉ dành cho bạn bè của {name} sẽ không còn hiện với bạn.
              </AlertDialogDescription>
            </AlertDialogHeader>
            <AlertDialogFooter>
              <AlertDialogCancel disabled={pending !== null}>
                Không
              </AlertDialogCancel>
              <AlertDialogAction
                variant="destructive"
                disabled={pending !== null}
                aria-busy={pending === "unfriend" || undefined}
                onClick={(event) => {
                  // Giữ hộp thoại mở tới khi server trả lời.
                  event.preventDefault()
                  void run(
                    "unfriend",
                    "relationship",
                    () => socialGraphApi.unfriend(card.user.userId),
                    () => {
                      setOpen(false)
                      onRemoved()
                    },
                    () => {
                      setOpen(false)
                      return false
                    }
                  )
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
      }
    />
  )
}

function OutgoingItem({
  card,
  onRemoved,
}: {
  card: FriendCardData
  onRemoved: () => void
}) {
  const { pending, message, run } = useCardAction()
  return (
    <FriendCard
      card={card}
      sinceLabel="Gửi lúc"
      message={message}
      actions={
        <ActionButton
          name="cancel"
          label="Hủy lời mời"
          pending={pending}
          onClick={() =>
            void run(
              "cancel",
              "relationship",
              () => socialGraphApi.removeRequest(card.user.userId),
              onRemoved
            )
          }
        />
      }
    />
  )
}
