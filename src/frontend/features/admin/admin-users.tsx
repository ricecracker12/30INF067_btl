"use client"

import Link from "next/link"
import { useCallback, useEffect, useState } from "react"

import { FormAlert } from "@/components/form/form-alert"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Input } from "@/components/ui/input"
import { Skeleton } from "@/components/ui/skeleton"
import { Spinner } from "@/components/ui/spinner"
import { useCursorPages } from "@/hooks/use-cursor-pages"
import { adminApi, type AdminUserFilter } from "@/lib/api/admin-api"
import { errorMessage } from "@/lib/api/messages"
import { ApiError } from "@/lib/api/problem"
import type { AdminUser, AdminUserPage } from "@/lib/api/types"
import { refreshMe } from "@/lib/auth/me-store"

import { USER_STATUS_LABEL } from "./labels"
import { useRoleOptions } from "./use-role-options"

const getId = (u: AdminUser) => u.userId

const STATUS_FILTERS: { value: AdminUserFilter["status"]; label: string }[] = [
  { value: undefined, label: "Mọi trạng thái" },
  { value: "active", label: "Đang hoạt động" },
  { value: "disabled", label: "Đã bị khóa" },
]

/**
 * Danh sách tài khoản (GĐ6 E7): tìm theo TIỀN TỐ email, lọc trạng thái (`active` | `disabled` — hợp đồng chỉ nhận hai giá trị này)
 * và vai trò. Mới tạo trước, "Xem thêm" theo cursor. Mỗi bộ lọc là một lượt xem mới (`key` đổi → trang đầu mới).
 */
export function AdminUsers() {
  const [draft, setDraft] = useState("")
  const [filter, setFilter] = useState<AdminUserFilter>({})
  const roles = useRoleOptions()

  const key = `admin-users:${filter.q ?? ""}:${filter.status ?? ""}:${filter.roleCode ?? ""}`
  const fetchPage = useCallback(
    (cursor: string | null, signal?: AbortSignal) =>
      adminApi.listUsers(filter, { cursor, limit: 20 }, signal),
    [filter]
  )
  const pages = useCursorPages<AdminUser, AdminUserPage>({ key, fetchPage, getId })

  const forbidden = pages.error instanceof ApiError && pages.error.status === 403
  useEffect(() => {
    if (forbidden) void refreshMe()
  }, [forbidden])

  return (
    <section className="flex flex-col gap-4">
      <form
        role="search"
        className="flex gap-2"
        onSubmit={(e) => {
          e.preventDefault()
          setFilter((f) => ({ ...f, q: draft.trim() || undefined }))
        }}
      >
        <Input
          type="search"
          aria-label="Tìm theo email"
          placeholder="Tìm theo email (phần đầu)"
          value={draft}
          onChange={(e) => setDraft(e.target.value)}
        />
        <Button type="submit" variant="outline">
          Tìm
        </Button>
      </form>

      <div className="flex flex-wrap gap-2" role="group" aria-label="Lọc theo trạng thái">
        {STATUS_FILTERS.map((s) => (
          <Button
            key={s.label}
            size="xs"
            variant={filter.status === s.value ? "default" : "outline"}
            aria-pressed={filter.status === s.value}
            onClick={() => setFilter((f) => ({ ...f, status: s.value }))}
          >
            {s.label}
          </Button>
        ))}
      </div>
      <div className="flex flex-wrap gap-2" role="group" aria-label="Lọc theo vai trò">
        <Button
          size="xs"
          variant={filter.roleCode === undefined ? "default" : "outline"}
          aria-pressed={filter.roleCode === undefined}
          onClick={() => setFilter((f) => ({ ...f, roleCode: undefined }))}
        >
          Mọi vai trò
        </Button>
        {roles.map((r) => (
          <Button
            key={r.code}
            size="xs"
            variant={filter.roleCode === r.code ? "default" : "outline"}
            aria-pressed={filter.roleCode === r.code}
            onClick={() => setFilter((f) => ({ ...f, roleCode: r.code }))}
          >
            {r.displayName}
          </Button>
        ))}
      </div>

      {!pages.loaded && (
        <div className="flex flex-col gap-2" aria-hidden>
          <Skeleton className="h-12" />
          <Skeleton className="h-12" />
        </div>
      )}

      {pages.loaded && pages.items.length === 0 && pages.error === null && (
        <p className="text-muted-foreground" data-testid="admin-users-empty">
          Không có tài khoản nào khớp.
        </p>
      )}

      {pages.items.length > 0 && (
        <ul className="flex flex-col divide-y divide-border rounded-lg border border-border">
          {pages.items.map((u) => (
            <li key={u.userId}>
              <Link
                href={`/admin/users/${encodeURIComponent(u.userId)}`}
                data-testid="admin-user-row"
                className="flex flex-wrap items-center gap-x-3 gap-y-1 p-3 text-sm hover:bg-muted/50"
              >
                <span className="font-medium">{u.displayName ?? "(chưa có hồ sơ)"}</span>
                <span className="break-all text-muted-foreground">{u.email}</span>
                <span className="ml-auto flex gap-2">
                  <Badge variant="outline">{u.roleDisplayName}</Badge>
                  <Badge variant={u.status === "active" ? "secondary" : "destructive"}>
                    {USER_STATUS_LABEL[u.status]}
                  </Badge>
                </span>
              </Link>
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
