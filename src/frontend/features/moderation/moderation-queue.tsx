"use client"

import Link from "next/link"
import { useEffect } from "react"

import { FormAlert } from "@/components/form/form-alert"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardContent } from "@/components/ui/card"
import { Skeleton } from "@/components/ui/skeleton"
import { Spinner } from "@/components/ui/spinner"
import { useCursorPages } from "@/hooks/use-cursor-pages"
import { errorMessage } from "@/lib/api/messages"
import { moderationApi } from "@/lib/api/moderation-api"
import { ApiError } from "@/lib/api/problem"
import type { ReportQueueItem, ReportQueuePage } from "@/lib/api/types"
import { refreshMe } from "@/lib/auth/me-store"
import { reasonLabel } from "@/lib/moderation/reasons"

import { TARGET_TYPE_LABEL, topReason, waitingLabel } from "./labels"

/** Câu báo sau khi rời màn chi tiết — `?notice=` do màn chi tiết đặt khi điều hướng về. */
export const QUEUE_NOTICE = {
  done: "Đã xử lý báo cáo.",
  decided: "Báo cáo này vừa được người khác xử lý.",
} as const

export type QueueNotice = keyof typeof QUEUE_NOTICE

const fetchPage = (cursor: string | null, signal?: AbortSignal) =>
  moderationApi.queue({ cursor, limit: 20 }, signal)
const getId = (item: ReportQueueItem) => item.reportId

/**
 * Hàng đợi kiểm duyệt (GĐ6 E6, UC-19 bước 1): mỗi ĐỐI TƯỢNG một dòng (Đ-6.13) — loại, số báo cáo, lý do nổi bật, thời gian chờ;
 * cũ nhất trước. Nạp mới mỗi lần mở (không giữ bản cũ): quay về từ màn chi tiết sau 409 `already-decided` là hàng đợi đã mới.
 */
export function ModerationQueue({ notice }: { notice?: QueueNotice }) {
  const pages = useCursorPages<ReportQueueItem, ReportQueuePage>({
    key: "moderation-queue",
    fetchPage,
    getId,
  })

  // 403 = vừa bị hạ quyền (Mục 7.3): nạp lại `/me`, guard mềm bên ngoài đổi sang trang "không có quyền".
  const forbidden = pages.error instanceof ApiError && pages.error.status === 403
  useEffect(() => {
    if (forbidden) void refreshMe()
  }, [forbidden])

  return (
    <section className="flex flex-col gap-4">
      <h1 className="text-xl font-medium">Hàng đợi kiểm duyệt</h1>

      {notice && (
        <p role="status" className="text-sm text-muted-foreground" data-testid="queue-notice">
          {QUEUE_NOTICE[notice]}
        </p>
      )}

      {!pages.loaded && (
        <div className="flex flex-col gap-2" aria-hidden>
          <Skeleton className="h-16" />
          <Skeleton className="h-16" />
        </div>
      )}

      {pages.loaded && pages.items.length === 0 && pages.error === null && (
        <p className="text-muted-foreground" data-testid="queue-empty">
          Không có báo cáo nào đang chờ.
        </p>
      )}

      {pages.items.length > 0 && (
        <ul className="flex flex-col gap-2">
          {pages.items.map((item) => {
            const top = topReason(item.reasons)
            return (
              <li key={item.reportId}>
                <Link
                  href={`/moderation/${encodeURIComponent(item.reportId)}`}
                  data-testid="queue-item"
                  data-report-id={item.reportId}
                >
                  <Card className="hover:bg-muted/50">
                    <CardContent className="flex flex-wrap items-center gap-3 text-sm">
                      <Badge variant="secondary">{TARGET_TYPE_LABEL[item.target.type]}</Badge>
                      <span className="font-medium">{item.reportCount} báo cáo</span>
                      {top && <span>· {reasonLabel(top)}</span>}
                      <time dateTime={item.firstReportedAt} className="ml-auto text-muted-foreground">
                        {waitingLabel(item.firstReportedAt)}
                      </time>
                    </CardContent>
                  </Card>
                </Link>
              </li>
            )
          })}
        </ul>
      )}

      {pages.error !== null && (
        <FormAlert message={errorMessage("moderation-read", pages.error)} />
      )}

      {pages.loaded && (pages.nextCursor !== null || pages.error !== null) && !forbidden && (
        <Button
          variant="outline"
          className="self-center"
          disabled={pages.pending}
          aria-busy={pages.pending || undefined}
          onClick={pages.items.length === 0 ? pages.reload : pages.loadMore}
        >
          {pages.pending && <Spinner data-icon="inline-start" aria-hidden />}
          {pages.error !== null ? "Thử lại" : "Xem thêm"}
        </Button>
      )}
    </section>
  )
}
