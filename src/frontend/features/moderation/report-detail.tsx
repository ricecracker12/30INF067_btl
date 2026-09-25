"use client"

import Link from "next/link"
import { useRouter } from "next/navigation"
import { useEffect, useState } from "react"

import { FormAlert } from "@/components/form/form-alert"
import { TextareaField } from "@/components/form/textarea-field"
import { Avatar, AvatarFallback, AvatarImage } from "@/components/ui/avatar"
import { Badge } from "@/components/ui/badge"
import { Button, buttonVariants } from "@/components/ui/button"
import { Card, CardContent } from "@/components/ui/card"
import { FieldLabel, FieldTitle, Field } from "@/components/ui/field"
import { RadioGroup, RadioGroupItem } from "@/components/ui/radio-group"
import { Skeleton } from "@/components/ui/skeleton"
import { Spinner } from "@/components/ui/spinner"
import { errorMessage, validationErrors } from "@/lib/api/messages"
import { moderationApi } from "@/lib/api/moderation-api"
import { ApiError, hasProblemType, PROBLEM_TYPES } from "@/lib/api/problem"
import type {
  ReasonCode,
  ReportDecision,
  ReportDetail,
  TargetSnapshot,
} from "@/lib/api/types"
import { refreshMe } from "@/lib/auth/me-store"
import { hasPermission } from "@/lib/auth/permissions"
import { useMe } from "@/lib/auth/use-me"
import { REASON_CODES, REASON_LABEL, reasonLabel } from "@/lib/moderation/reasons"
import { decisionNoteError, optionalText } from "@/lib/validation/moderation"

import {
  OUTCOME_LABEL,
  TARGET_STATUS_LABEL,
  TARGET_TYPE_LABEL,
} from "./labels"
import { MODERATION_PERMISSIONS } from "./permissions"

const dateTime = new Intl.DateTimeFormat("vi-VN", {
  dateStyle: "medium",
  timeStyle: "short",
  timeZone: "Asia/Ho_Chi_Minh",
})

type Loaded =
  | { key: string; detail: ReportDetail }
  | { key: string; notFound: true }
  | { key: string; error: string }

/**
 * Chi tiết một báo cáo (GĐ6 E6, UC-19 bước 2–5): ảnh chụp THẬT của đối tượng (kể cả bài riêng tư — đường duy nhất Moderator đọc
 * được nội dung không công khai, Mục 8.1), các báo cáo đang mở, lịch sử xử lý. KHÔNG có danh tính người báo (không có trên dây).
 *
 * Không optimistic (Đ-6.21): bấm → chờ server → điều hướng. 409 `already-decided` → về hàng đợi (nạp mới) kèm câu "vừa được người
 * khác xử lý". 403 → nạp lại `/me` (có thể vừa bị hạ quyền, Mục 7.3).
 */
export function ReportDetailScreen({ reportId }: { reportId: string }) {
  const [data, setData] = useState<Loaded | null>(null)
  const [attempt, setAttempt] = useState(0)
  const key = `${reportId}:${attempt}`
  const current = data?.key === key ? data : null

  useEffect(() => {
    const controller = new AbortController()
    const pageKey = `${reportId}:${attempt}`
    moderationApi.detail(reportId, controller.signal).then(
      (detail) => setData({ key: pageKey, detail }),
      (e: unknown) => {
        if ((e as Error).name === "AbortError") return
        if (e instanceof ApiError && e.status === 404)
          setData({ key: pageKey, notFound: true })
        else {
          if (e instanceof ApiError && e.status === 403) void refreshMe()
          setData({ key: pageKey, error: errorMessage("moderation-read", e) })
        }
      }
    )
    return () => controller.abort()
  }, [reportId, attempt])

  const back = (
    <Link href="/moderation" className={buttonVariants({ variant: "outline", size: "sm" })}>
      Về hàng đợi
    </Link>
  )

  if (current === null)
    return (
      <div className="flex flex-col gap-3" aria-hidden data-testid="report-detail-skeleton">
        <Skeleton className="h-32" />
        <Skeleton className="h-16" />
      </div>
    )

  if ("notFound" in current)
    return (
      <div className="flex flex-col items-start gap-4">
        <p>Không tìm thấy báo cáo.</p>
        {back}
      </div>
    )

  if ("error" in current)
    return (
      <div className="flex flex-col items-start gap-4">
        <FormAlert message={current.error} />
        <Button variant="outline" onClick={() => setAttempt((n) => n + 1)}>
          Thử lại
        </Button>
      </div>
    )

  const { detail } = current
  return (
    <div className="flex flex-col gap-6">
      <div className="flex items-center justify-between gap-4">
        <h1 className="text-xl font-medium">
          Báo cáo {TARGET_TYPE_LABEL[detail.target.type].toLowerCase()}
        </h1>
        {back}
      </div>

      <Snapshot target={detail.target} />

      <section className="flex flex-col gap-2">
        <h2 className="font-medium">Báo cáo đang mở ({detail.openReports.length})</h2>
        {detail.openReports.length === 0 ? (
          <p className="text-sm text-muted-foreground">Không còn báo cáo nào đang mở.</p>
        ) : (
          <ul className="flex flex-col gap-2 text-sm" data-testid="open-reports">
            {detail.openReports.map((r) => (
              <li key={r.reportId} className="rounded-lg border border-border p-3">
                <div className="flex flex-wrap items-center gap-2">
                  <Badge variant="secondary">{reasonLabel(r.reasonCode)}</Badge>
                  <time dateTime={r.createdAt} className="text-muted-foreground">
                    {dateTime.format(new Date(r.createdAt))}
                  </time>
                </div>
                {r.detail && <p className="mt-1 whitespace-pre-line">{r.detail}</p>}
              </li>
            ))}
          </ul>
        )}
      </section>

      {detail.openReports.length > 0 && <DecisionPanel detail={detail} />}

      <RestorePanel
        detail={detail}
        onRestored={() => setAttempt((n) => n + 1)}
      />

      <section className="flex flex-col gap-2">
        <h2 className="font-medium">Lịch sử xử lý</h2>
        {detail.history.length === 0 ? (
          <p className="text-sm text-muted-foreground">Chưa có quyết định nào.</p>
        ) : (
          <ul className="flex flex-col gap-2 text-sm" data-testid="report-history">
            {detail.history.map((h, i) => (
              <li key={`${h.resolvedAt}:${i}`} className="rounded-lg border border-border p-3">
                <span className="font-medium">{OUTCOME_LABEL[h.outcome]}</span>
                <span className="text-muted-foreground">
                  {" "}
                  · {dateTime.format(new Date(h.resolvedAt))}
                </span>
                {h.note && <p className="mt-1 whitespace-pre-line">{h.note}</p>}
              </li>
            ))}
          </ul>
        )}
      </section>
    </div>
  )
}

/** Ảnh chụp đối tượng — nội dung THẬT, kể cả khi bài riêng tư hay đã xóa (Moderator phải thấy mới quyết được). */
function Snapshot({ target }: { target: TargetSnapshot }) {
  const author = target.author
  return (
    <Card data-testid="target-snapshot">
      <CardContent className="flex flex-col gap-3">
        <div className="flex flex-wrap items-center gap-3">
          {author && (
            <div className="flex items-center gap-2">
              <Avatar className="size-8">
                {author.avatarUrl && <AvatarImage src={author.avatarUrl} alt="" />}
                <AvatarFallback>
                  {author.displayName.trim().charAt(0).toUpperCase()}
                </AvatarFallback>
              </Avatar>
              <span className="text-sm font-medium">{author.displayName}</span>
            </div>
          )}
          <Badge variant={target.status === "hidden" ? "destructive" : "secondary"}>
            {TARGET_STATUS_LABEL[target.status]}
          </Badge>
          {target.createdAt && (
            <time dateTime={target.createdAt} className="text-xs text-muted-foreground">
              {dateTime.format(new Date(target.createdAt))}
            </time>
          )}
        </div>
        {target.body && <p className="break-words whitespace-pre-line">{target.body}</p>}
        {!target.body && target.status === "deleted" && (
          <p className="text-sm text-muted-foreground">Nội dung không còn trong hệ thống.</p>
        )}
        {target.media.length > 0 && (
          <div className="grid grid-cols-2 gap-2">
            {target.media.map((m) => (
              // URL presigned 15 phút (Đ-2.9); `next/image` kéo byte ảnh qua origin app — Đ-2.5 cấm (lý do ở `post-card.tsx`).
              // eslint-disable-next-line @next/next/no-img-element -- Đ-2.5: byte ảnh không đi qua origin app.
              <img
                key={m.url}
                src={m.url}
                alt=""
                loading="lazy"
                className="aspect-square w-full rounded-md border border-border object-cover"
              />
            ))}
          </div>
        )}
      </CardContent>
    </Card>
  )
}

/** Cặp `decision × targetType` hợp lệ (Đ-6.13): ẩn cho bài/bình luận, "đã xử lý" CHỈ cho người dùng (bắt buộc ghi chú), bỏ qua mọi loại. */
function decisionsFor(type: TargetSnapshot["type"]): ReportDecision[] {
  return type === "user" ? ["resolve", "dismiss"] : ["hide", "dismiss"]
}

const DECISION_LABEL: Record<ReportDecision, string> = {
  hide: "Ẩn nội dung",
  dismiss: "Bỏ qua",
  resolve: "Đã xử lý",
}

function mostReported(detail: ReportDetail): ReasonCode {
  const counts = new Map<ReasonCode, number>()
  for (const r of detail.openReports) counts.set(r.reasonCode, (counts.get(r.reasonCode) ?? 0) + 1)
  let best: ReasonCode = detail.openReports[0]?.reasonCode ?? "other"
  for (const [code, n] of counts) if (n > (counts.get(best) ?? 0)) best = code
  return best
}

function DecisionPanel({ detail }: { detail: ReportDetail }) {
  const router = useRouter()
  const { me } = useMe()
  const canHide = hasPermission(me, MODERATION_PERMISSIONS.hide)
  const choices = decisionsFor(detail.target.type).filter((d) => d !== "hide" || canHide)

  const [decision, setDecision] = useState<ReportDecision | null>(null)
  const [reason, setReason] = useState<ReasonCode>(() => mostReported(detail))
  const [note, setNote] = useState("")
  const [noteError, setNoteError] = useState<string | undefined>()
  const [formError, setFormError] = useState<string | null>(null)
  const [pending, setPending] = useState(false)

  async function submit() {
    if (!decision || pending) return
    const err = decisionNoteError(note, decision === "resolve")
    setNoteError(err)
    setFormError(null)
    if (err) return

    setPending(true)
    try {
      await moderationApi.decide(detail.reportId, {
        decision,
        reasonCode: decision === "hide" ? reason : undefined,
        note: optionalText(note),
      })
      router.replace("/moderation?notice=done")
    } catch (e) {
      setPending(false)
      // Người khác vừa quyết (US-019 AC-04, MOD-C1): về hàng đợi — nạp MỚI, dòng này đã biến mất — kèm câu giải thích.
      if (hasProblemType(e, PROBLEM_TYPES.reportAlreadyDecided)) {
        router.replace("/moderation?notice=decided")
        return
      }
      if (e instanceof ApiError && e.status === 403) void refreshMe()
      if (e instanceof ApiError && e.status === 400) {
        const v = validationErrors(e, ["note"] as const)
        setNoteError(v.fields.note)
        setFormError(v.formMessage)
        return
      }
      setFormError(errorMessage("report-decide", e))
    }
  }

  return (
    <section className="flex flex-col gap-3" data-testid="decision-panel">
      <h2 className="font-medium">Quyết định</h2>
      <p className="text-sm text-muted-foreground">
        Một quyết định đóng mọi báo cáo đang mở của nội dung này.
      </p>
      <div className="flex flex-wrap gap-2">
        {choices.map((d) => (
          <Button
            key={d}
            variant={decision === d ? "default" : "outline"}
            size="sm"
            disabled={pending}
            aria-pressed={decision === d}
            onClick={() => {
              setDecision(d)
              setNoteError(undefined)
              setFormError(null)
            }}
          >
            {DECISION_LABEL[d]}
          </Button>
        ))}
      </div>

      {decision && (
        <form
          className="flex flex-col gap-3"
          onSubmit={(e) => {
            e.preventDefault()
            void submit()
          }}
        >
          <FormAlert message={formError} />
          {decision === "hide" && (
            <Field>
              <FieldTitle id="hide-reason-label">Lý do ẩn</FieldTitle>
              <RadioGroup
                value={reason}
                aria-labelledby="hide-reason-label"
                onValueChange={(v) => setReason(v as ReasonCode)}
              >
                {REASON_CODES.map((code) => (
                  <FieldLabel key={code} htmlFor={`hide-reason-${code}`}>
                    <Field orientation="horizontal">
                      <RadioGroupItem id={`hide-reason-${code}`} value={code} disabled={pending} />
                      <FieldTitle>{REASON_LABEL[code]}</FieldTitle>
                    </Field>
                  </FieldLabel>
                ))}
              </RadioGroup>
            </Field>
          )}
          <TextareaField
            label={decision === "resolve" ? "Ghi chú cách đã xử lý (bắt buộc)" : "Ghi chú (không bắt buộc)"}
            description="Tác giả không thấy ghi chú này."
            rows={2}
            value={note}
            disabled={pending}
            error={noteError}
            onChange={(e) => {
              setNote(e.target.value)
              setNoteError(undefined)
            }}
          />
          <Button type="submit" className="self-start" disabled={pending} aria-busy={pending || undefined}>
            {pending && <Spinner data-icon="inline-start" aria-hidden />}
            Xác nhận: {DECISION_LABEL[decision]}
          </Button>
        </form>
      )}
    </section>
  )
}

/** Khôi phục (FR-020 "ẩn/khôi phục") — chỉ khi đối tượng ĐANG bị ẩn và người xem có `post.hide`. */
function RestorePanel({ detail, onRestored }: { detail: ReportDetail; onRestored: () => void }) {
  const { me } = useMe()
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<string | null>(null)
  const { target } = detail
  if (target.status !== "hidden" || target.type === "user") return null
  if (!hasPermission(me, MODERATION_PERMISSIONS.hide)) return null

  async function restore() {
    setPending(true)
    setError(null)
    try {
      await moderationApi.restore(target.type, target.id)
      onRestored()
    } catch (e) {
      if (e instanceof ApiError && e.status === 403) void refreshMe()
      setError(errorMessage("moderation-restore", e))
      // Người khác vừa khôi phục: vẽ lại theo sự thật của server.
      if (hasProblemType(e, PROBLEM_TYPES.moderationNotHidden)) onRestored()
    } finally {
      setPending(false)
    }
  }

  return (
    <section className="flex flex-col items-start gap-2">
      <FormAlert message={error} />
      <Button variant="outline" size="sm" disabled={pending} aria-busy={pending || undefined} onClick={() => void restore()}>
        {pending && <Spinner data-icon="inline-start" aria-hidden />}
        Khôi phục nội dung
      </Button>
    </section>
  )
}
