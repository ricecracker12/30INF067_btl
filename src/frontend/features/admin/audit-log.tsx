"use client"

import { useCallback, useEffect, useState } from "react"

import { FormAlert } from "@/components/form/form-alert"
import { TextField } from "@/components/form/text-field"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Field, FieldTitle } from "@/components/ui/field"
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from "@/components/ui/select"
import { Skeleton } from "@/components/ui/skeleton"
import { Spinner } from "@/components/ui/spinner"
import { useCursorPages } from "@/hooks/use-cursor-pages"
import { errorMessage } from "@/lib/api/messages"
import { moderationApi, type AuditLogFilter } from "@/lib/api/moderation-api"
import { ApiError } from "@/lib/api/problem"
import type { AuditAction, AuditLogItem, AuditLogPage } from "@/lib/api/types"
import { refreshMe } from "@/lib/auth/me-store"

import { AUDIT_ACTION_LABEL, AUDIT_ACTIONS, dateTime } from "./labels"

const getId = (item: AuditLogItem) => String(item.id)

const ALL = "__all__"
const ACTION_ITEMS = [
  { value: ALL, label: "Mọi hành động" },
  ...AUDIT_ACTIONS.map((a) => ({ value: a, label: AUDIT_ACTION_LABEL[a] })),
]

/** Một giá trị `metadata` thành chữ đọc được: mảng nối bằng dấu phẩy, object thành JSON, null thành "—". */
export function metadataValue(value: unknown): string {
  if (value === null || value === undefined) return "—"
  if (Array.isArray(value)) return value.map(metadataValue).join(", ")
  if (typeof value === "object") return JSON.stringify(value)
  return String(value)
}

/**
 * Nhật ký kiểm toán (GĐ6 E9, Đ-6.15) — chỉ ADMIN (`audit.read`). Lọc theo người thao tác, hành động, đối tượng; mới nhất trước,
 * "Xem thêm" theo cursor. `metadata` hiện dạng khóa–giá trị (không bao giờ chứa nội dung người dùng — server canh, AUD-01).
 * `targetId` chỉ gửi khi có `targetType` (server 400 nếu thiếu cặp).
 */
export function AuditLog() {
  const [actorId, setActorId] = useState("")
  const [targetType, setTargetType] = useState("")
  const [targetId, setTargetId] = useState("")
  const [action, setAction] = useState<AuditAction | undefined>()
  const [filter, setFilter] = useState<AuditLogFilter>({})

  const key = `audit:${JSON.stringify(filter)}`
  const fetchPage = useCallback(
    (cursor: string | null, signal?: AbortSignal) =>
      moderationApi.auditLogs(filter, { cursor, limit: 20 }, signal),
    [filter]
  )
  const pages = useCursorPages<AuditLogItem, AuditLogPage>({ key, fetchPage, getId })

  const forbidden = pages.error instanceof ApiError && pages.error.status === 403
  useEffect(() => {
    if (forbidden) void refreshMe()
  }, [forbidden])

  return (
    <section className="flex flex-col gap-4">
      <h1 className="text-xl font-medium">Nhật ký kiểm toán</h1>

      <form
        className="flex flex-col gap-3"
        onSubmit={(e) => {
          e.preventDefault()
          const type = targetType.trim()
          setFilter({
            actorId: actorId.trim() || undefined,
            action,
            targetType: type || undefined,
            targetId: (type && targetId.trim()) || undefined,
          })
        }}
      >
        <div className="grid gap-3 sm:grid-cols-2">
          <TextField label="Id người thao tác" value={actorId} onChange={(e) => setActorId(e.target.value)} />
          <Field>
            <FieldTitle id="audit-action-label">Hành động</FieldTitle>
            <Select
              items={ACTION_ITEMS}
              value={action ?? ALL}
              onValueChange={(v) => setAction(v === ALL || v === null ? undefined : (v as AuditAction))}
            >
              <SelectTrigger aria-labelledby="audit-action-label" className="w-full">
                <SelectValue />
              </SelectTrigger>
              <SelectContent>
                {ACTION_ITEMS.map((a) => (
                  <SelectItem key={a.value} value={a.value}>
                    {a.label}
                  </SelectItem>
                ))}
              </SelectContent>
            </Select>
          </Field>
          <TextField
            label="Loại đối tượng"
            description="post, comment, user, role, endpoint"
            value={targetType}
            onChange={(e) => setTargetType(e.target.value)}
          />
          <TextField
            label="Id đối tượng"
            description="Cần kèm loại đối tượng."
            value={targetId}
            onChange={(e) => setTargetId(e.target.value)}
          />
        </div>
        <Button type="submit" variant="outline" className="self-start">
          Lọc
        </Button>
      </form>

      {!pages.loaded && (
        <div className="flex flex-col gap-2" aria-hidden>
          <Skeleton className="h-20" />
          <Skeleton className="h-20" />
        </div>
      )}

      {pages.loaded && pages.items.length === 0 && pages.error === null && (
        <p className="text-muted-foreground" data-testid="audit-empty">
          Không có dòng nhật ký nào khớp.
        </p>
      )}

      {pages.items.length > 0 && (
        <ul className="flex flex-col gap-2">
          {pages.items.map((item) => (
            <li key={item.id} className="rounded-lg border border-border p-3 text-sm" data-testid="audit-item">
              <div className="flex flex-wrap items-center gap-2">
                <Badge variant={item.action === "access.denied" ? "destructive" : "secondary"}>
                  {AUDIT_ACTION_LABEL[item.action] ?? item.action}
                </Badge>
                <span className="font-medium">{item.actor?.displayName ?? item.actorId}</span>
                <time dateTime={item.createdAt} className="ml-auto text-muted-foreground">
                  {dateTime.format(new Date(item.createdAt))}
                </time>
              </div>
              <dl className="mt-2 grid grid-cols-[max-content_1fr] gap-x-4 gap-y-1">
                {item.targetType && (
                  <>
                    <dt className="text-muted-foreground">Đối tượng</dt>
                    <dd className="break-all">
                      {item.targetType}
                      {item.targetId && ` · ${item.targetId}`}
                    </dd>
                  </>
                )}
                {item.ip && (
                  <>
                    <dt className="text-muted-foreground">IP</dt>
                    <dd>{item.ip}</dd>
                  </>
                )}
                {Object.entries(item.metadata ?? {}).map(([k, v]) => (
                  <div key={k} className="contents" data-testid="audit-metadata">
                    <dt className="text-muted-foreground">{k}</dt>
                    <dd className="break-all">{metadataValue(v)}</dd>
                  </div>
                ))}
              </dl>
            </li>
          ))}
        </ul>
      )}

      {pages.error !== null && <FormAlert message={errorMessage("admin-read", pages.error)} />}

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
