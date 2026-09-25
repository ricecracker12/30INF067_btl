"use client"

import Link from "next/link"
import { useRouter } from "next/navigation"
import { useEffect, useState } from "react"

import { FormAlert } from "@/components/form/form-alert"
import { TextField } from "@/components/form/text-field"
import { Badge } from "@/components/ui/badge"
import { Button } from "@/components/ui/button"
import { Card, CardContent } from "@/components/ui/card"
import { FieldError } from "@/components/ui/field"
import { Skeleton } from "@/components/ui/skeleton"
import { Spinner } from "@/components/ui/spinner"
import { adminApi } from "@/lib/api/admin-api"
import { errorMessage, validationErrors } from "@/lib/api/messages"
import { ApiError } from "@/lib/api/problem"
import type { PermissionInfo, RoleSummary } from "@/lib/api/types"
import { refreshMe } from "@/lib/auth/me-store"
import { roleCodeError, roleDisplayNameError } from "@/lib/validation/moderation"

import { PermissionMatrix } from "./permission-matrix"

const count = new Intl.NumberFormat("vi-VN")

type Loaded =
  | { key: number; roles: RoleSummary[]; permissions: PermissionInfo[] }
  | { key: number; error: string }

/**
 * Danh sách vai trò (GĐ6 E8): số người mang, nhãn "Hệ thống" (tính ra từ `code`, không phải cột — Đ-6.9), và form tạo vai trò mới.
 * Sửa tên / quyền / xóa ở trang của từng vai trò.
 */
export function RolesScreen() {
  const [data, setData] = useState<Loaded | null>(null)
  const [attempt, setAttempt] = useState(0)
  const [creating, setCreating] = useState(false)
  const current = data?.key === attempt ? data : null

  useEffect(() => {
    const controller = new AbortController()
    const key = attempt
    Promise.all([
      adminApi.listRoles(controller.signal),
      adminApi.listPermissions(controller.signal),
    ]).then(
      ([roles, permissions]) => setData({ key, roles, permissions }),
      (e: unknown) => {
        if ((e as Error).name === "AbortError") return
        if (e instanceof ApiError && e.status === 403) void refreshMe()
        setData({ key, error: errorMessage("admin-read", e) })
      }
    )
    return () => controller.abort()
  }, [attempt])

  if (current === null)
    return (
      <div className="flex flex-col gap-2" aria-hidden>
        <Skeleton className="h-14" />
        <Skeleton className="h-14" />
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

  return (
    <section className="flex flex-col gap-4">
      <div className="flex items-center justify-between gap-4">
        <h1 className="text-xl font-medium">Vai trò</h1>
        {!creating && (
          <Button size="sm" onClick={() => setCreating(true)}>
            Tạo vai trò
          </Button>
        )}
      </div>

      {creating && (
        <CreateRoleForm permissions={current.permissions} onCancel={() => setCreating(false)} />
      )}

      <ul className="flex flex-col gap-2">
        {current.roles.map((r) => (
          <li key={r.roleId}>
            <Link href={`/admin/roles/${r.roleId}`} data-testid="role-row">
              <Card className="hover:bg-muted/50">
                <CardContent className="flex flex-wrap items-center gap-3 text-sm">
                  <span className="font-medium">{r.displayName}</span>
                  <code className="text-muted-foreground">{r.code}</code>
                  {r.isSystem && <Badge variant="secondary">Hệ thống</Badge>}
                  <span className="ml-auto text-muted-foreground">
                    {count.format(r.userCount)} người · {r.permissions.length} quyền
                  </span>
                </CardContent>
              </Card>
            </Link>
          </li>
        ))}
      </ul>
    </section>
  )
}

type CreateFields = { code?: string; displayName?: string; permissions?: string }

function CreateRoleForm({ permissions, onCancel }: { permissions: PermissionInfo[]; onCancel: () => void }) {
  const router = useRouter()
  const [code, setCode] = useState("")
  const [displayName, setDisplayName] = useState("")
  const [selected, setSelected] = useState<Set<string>>(() => new Set())
  const [fields, setFields] = useState<CreateFields>({})
  const [formError, setFormError] = useState<string | null>(null)
  const [pending, setPending] = useState(false)

  async function submit() {
    if (pending) return
    const next: CreateFields = { code: roleCodeError(code), displayName: roleDisplayNameError(displayName) }
    setFields(next)
    setFormError(null)
    if (next.code || next.displayName) return
    setPending(true)
    try {
      const role = await adminApi.createRole({
        code,
        displayName: displayName.trim(),
        permissions: [...selected],
      })
      router.push(`/admin/roles/${role.roleId}`)
    } catch (e) {
      setPending(false)
      if (e instanceof ApiError && e.status === 403) void refreshMe()
      if (e instanceof ApiError && e.status === 400) {
        const v = validationErrors(e, ["code", "displayName", "permissions"] as const)
        setFields(v.fields)
        setFormError(v.formMessage)
      } else setFormError(errorMessage("role-edit", e)) // 409 role-code-taken → câu theo `type`
    }
  }

  return (
    <Card>
      <CardContent>
        <form
          className="flex flex-col gap-4"
          onSubmit={(e) => {
            e.preventDefault()
            void submit()
          }}
        >
          <FormAlert message={formError} />
          <TextField
            label="Mã vai trò"
            description="Không đổi được sau khi tạo. Ví dụ: REVIEWER."
            value={code}
            disabled={pending}
            error={fields.code}
            autoCapitalize="characters"
            onChange={(e) => setCode(e.target.value)}
          />
          <TextField
            label="Tên hiển thị"
            value={displayName}
            disabled={pending}
            error={fields.displayName}
            onChange={(e) => setDisplayName(e.target.value)}
          />
          <PermissionMatrix
            idPrefix="create-perm"
            permissions={permissions}
            selected={selected}
            disabled={pending}
            onToggle={(p, checked) =>
              setSelected((s) => {
                const n = new Set(s)
                if (checked) n.add(p)
                else n.delete(p)
                return n
              })
            }
          />
          {fields.permissions && <FieldError>{fields.permissions}</FieldError>}
          <div className="flex gap-2">
            <Button type="submit" disabled={pending} aria-busy={pending || undefined}>
              {pending && <Spinner data-icon="inline-start" aria-hidden />}
              Tạo
            </Button>
            <Button type="button" variant="outline" disabled={pending} onClick={onCancel}>
              Hủy
            </Button>
          </div>
        </form>
      </CardContent>
    </Card>
  )
}
