"use client"

import { FormAlert } from "@/components/form/form-alert"
import { Button } from "@/components/ui/button"
import { Skeleton } from "@/components/ui/skeleton"
import { Spinner } from "@/components/ui/spinner"
import { useCursorPages } from "@/hooks/use-cursor-pages"
import { errorMessage } from "@/lib/api/messages"
import { notificationApi } from "@/lib/api/notification-api"
import type { NotificationPage, NotificationResponse } from "@/lib/api/types"

import { NotificationItem } from "./notification-item"
import { useReadMarks } from "./use-read-marks"

const PAGE_SIZE = 20

const fetchPage = (cursor: string | null, signal?: AbortSignal) =>
  notificationApi.list({ cursor, limit: PAGE_SIZE }, signal)

const getId = (n: NotificationResponse) => n.notificationId

/**
 * Màn `/notifications` (GĐ6 E3): mọi nhóm, cuộn theo cursor bằng nút "Xem thêm" (khuôn `/friends` — không observer), khử trùng
 * theo `notificationId` (`useCursorPages`): nhóm vừa có sự kiện mới nhảy lên trang 1 và có thể trả lại ở trang sau (Mục 8.3).
 */
export function NotificationList() {
  const pages = useCursorPages<NotificationResponse, NotificationPage>({
    key: "notifications",
    fetchPage,
    getId,
  })
  const marks = useReadMarks()

  return (
    <section className="flex flex-col gap-4">
      <div className="flex items-center justify-between gap-4">
        <h1 className="text-xl font-medium">Thông báo</h1>
        <Button
          variant="outline"
          size="sm"
          disabled={pages.items.length === 0}
          onClick={() => void marks.markAll(pages.items)}
        >
          Đánh dấu tất cả đã đọc
        </Button>
      </div>

      <FormAlert message={marks.error} />

      {!pages.loaded && (
        <div className="flex flex-col gap-2" aria-hidden>
          <Skeleton className="h-12" />
          <Skeleton className="h-12" />
          <Skeleton className="h-12" />
        </div>
      )}

      {pages.loaded && pages.items.length === 0 && !pages.error && (
        <p className="text-muted-foreground" data-testid="notifications-empty">
          Chưa có thông báo nào.
        </p>
      )}

      {pages.items.length > 0 && (
        <ul className="flex flex-col gap-1">
          {pages.items.map((n) => (
            <li key={n.notificationId}>
              <NotificationItem
                notification={n}
                read={marks.isRead(n)}
                onOpen={(item) => void marks.markRead(item)}
              />
            </li>
          ))}
        </ul>
      )}

      {pages.error !== null && (
        <FormAlert message={errorMessage("notification-read", pages.error)} />
      )}

      {pages.loaded && (pages.nextCursor !== null || pages.error !== null) && (
        <Button
          variant="outline"
          className="self-center"
          disabled={pages.pending}
          aria-busy={pages.pending || undefined}
          onClick={pages.error !== null && pages.items.length === 0 ? pages.reload : pages.loadMore}
        >
          {pages.pending && <Spinner data-icon="inline-start" aria-hidden />}
          {pages.error !== null ? "Thử lại" : "Xem thêm"}
        </Button>
      )}
    </section>
  )
}
