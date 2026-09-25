"use client"

import Link from "next/link"
import { useRouter } from "next/navigation"
import { useEffect, useState } from "react"

import { FormAlert } from "@/components/form/form-alert"
import { TextField } from "@/components/form/text-field"
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
import { Button, buttonVariants } from "@/components/ui/button"
import { Skeleton } from "@/components/ui/skeleton"
import { Spinner } from "@/components/ui/spinner"
import { adminApi } from "@/lib/api/admin-api"
import { errorMessage } from "@/lib/api/messages"
import { ApiError, confirmationOf, type ConfirmationRequest } from "@/lib/api/problem"
import type { PermissionInfo, RoleSummary } from "@/lib/api/types"
import { refreshMe } from "@/lib/auth/me-store"
import { roleDisplayNameError } from "@/lib/validation/moderation"

import { PermissionMatrix } from "./permission-matrix"

const count = new Intl.NumberFormat("vi-VN")

type Loaded =
  | { key: number; role: RoleSummary; permissions: PermissionInfo[] }
  | { key: number; notFound: true }
  | { key: number; error: string }

/** Lỗi ghi của màn vai trò: 400 → câu server dưới `errors`; 409 → câu theo `type` (Đ-6.21); 403 → nạp lại `/me`. */
function writeError(e: unknown): string {
  if (e instanceof ApiError && e.status === 403) void refreshMe()
  if (e instanceof ApiError && e.status === 400) {
    const first = Object.values(e.fieldErrors)[0]?.[0]
    if (first) return first
  }
  return errorMessage("role-edit", e)
}

/**
 * Một vai trò (GĐ6 E8, Đ-6.9): đổi tên · ma trận quyền (ADMIN chỉ đọc) · xóa (ẩn với vai trò hệ thống, vẫn xử lý 409). Không có
 * `GET /admin/roles/{id}` — đọc cả danh sách rồi chọn theo id (danh sách nhỏ, một request).
 *
 * Sửa quyền USER/MODERATOR: server trả 409 `confirmation-required` → hộp thoại hiện ĐÚNG `removed`, `added`, `affectedUsers` server
 * gửi (Đ-6.21, không tự tính) → gửi lại với `confirm: true`. Server là chỗ bắt buộc bước này, không phải FE.
 */
export function RoleEditor({ roleId }: { roleId: number }) {
  const router = useRouter()
  const [data, setData] = useState<Loaded | null>(null)
  const [attempt, setAttempt] = useState(0)
  const [selected, setSelected] = useState<Set<string> | null>(null)
  const [confirm, setConfirm] = useState<ConfirmationRequest | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [saved, setSaved] = useState(false)
  const [pending, setPending] = useState(false)
  const current = data?.key === attempt ? data : null

  useEffect(() => {
    const controller = new AbortController()
    const key = attempt
    Promise.all([adminApi.listRoles(controller.signal), adminApi.listPermissions(controller.signal)]).then(
      ([roles, permissions]) => {
        const role = roles.find((r) => r.roleId === roleId)
        setData(role ? { key, role, permissions } : { key, notFound: true })
      },
      (e: unknown) => {
        if ((e as Error).name === "AbortError") return
        if (e instanceof ApiError && e.status === 403) void refreshMe()
        setData({ key, error: errorMessage("admin-read", e) })
      }
    )
    return () => controller.abort()
  }, [roleId, attempt])

  const back = (
    <Link href="/admin/roles" className={buttonVariants({ variant: "outline", size: "sm" })}>
      Về danh sách
    </Link>
  )

  if (current === null)
    return (
      <div className="flex flex-col gap-2" aria-hidden>
        <Skeleton className="h-8 w-1/2" />
        <Skeleton className="h-64" />
      </div>
    )
  if ("notFound" in current)
    return (
      <div className="flex flex-col items-start gap-4">
        <p>Không tìm thấy vai trò.</p>
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

  const { role, permissions } = current
  const chosen = selected ?? new Set(role.permissions)
  const byCode = new Map(permissions.map((p) => [p.code, p]))
  const describe = (code: string) => byCode.get(code)?.description ?? code

  async function save(confirmed: boolean) {
    setPending(true)
    setError(null)
    setSaved(false)
    try {
      const next = await adminApi.setPermissions(role.roleId, {
        permissions: [...chosen],
        ...(confirmed && { confirm: true }),
      })
      setData({ key: attempt, role: next, permissions })
      setSelected(null)
      setConfirm(null)
      setSaved(true)
    } catch (e) {
      const ask = confirmationOf(e)
      if (ask) setConfirm(ask)
      else {
        setConfirm(null)
        setError(writeError(e))
      }
    } finally {
      setPending(false)
    }
  }

  return (
    <section className="flex flex-col gap-6">
      <div className="flex flex-wrap items-center justify-between gap-4">
        <div className="flex items-center gap-3">
          <h1 className="text-xl font-medium">{role.displayName}</h1>
          <code className="text-muted-foreground">{role.code}</code>
          {role.isSystem && <Badge variant="secondary">Hệ thống</Badge>}
        </div>
        {back}
      </div>
      <p className="text-sm text-muted-foreground">{count.format(role.userCount)} tài khoản mang vai trò này.</p>

      <RenameForm role={role} onRenamed={(next) => setData({ key: attempt, role: next, permissions })} />

      <section className="flex flex-col gap-3">
        <h2 className="font-medium">Quyền</h2>
        {!role.editable && (
          <p className="text-sm text-muted-foreground" data-testid="role-readonly">
            Quản trị viên luôn có mọi quyền — không sửa được.
          </p>
        )}
        <FormAlert message={error} />
        {saved && (
          <p role="status" className="text-sm text-muted-foreground">
            Đã lưu. Quyền mới có hiệu lực ngay ở lần thao tác kế tiếp.
          </p>
        )}
        <PermissionMatrix
          idPrefix={`role-${role.roleId}`}
          permissions={permissions}
          selected={chosen}
          readOnly={!role.editable}
          disabled={pending}
          onToggle={(code, checked) => {
            const n = new Set(chosen)
            if (checked) n.add(code)
            else n.delete(code)
            setSelected(n)
            setSaved(false)
          }}
        />
        {role.editable && (
          <Button className="self-start" disabled={pending || selected === null} aria-busy={pending || undefined} onClick={() => void save(false)}>
            {pending && <Spinner data-icon="inline-start" aria-hidden />}
            Lưu quyền
          </Button>
        )}
      </section>

      <AlertDialog open={confirm !== null} onOpenChange={(open) => !open && !pending && setConfirm(null)}>
        <AlertDialogContent data-testid="confirm-permissions">
          <AlertDialogHeader>
            <AlertDialogTitle>Xác nhận sửa quyền của “{role.displayName}”</AlertDialogTitle>
            <AlertDialogDescription>
              {count.format(confirm?.affectedUsers ?? 0)} tài khoản bị ảnh hưởng ngay lập tức.
            </AlertDialogDescription>
          </AlertDialogHeader>
          {confirm && confirm.removed.length > 0 && (
            <div className="text-sm">
              <p className="font-medium">Gỡ quyền:</p>
              <ul className="list-disc pl-5" data-testid="confirm-removed">
                {confirm.removed.map((c) => (
                  <li key={c}>
                    {describe(c)} (<code>{c}</code>)
                  </li>
                ))}
              </ul>
            </div>
          )}
          {confirm && confirm.added.length > 0 && (
            <div className="text-sm">
              <p className="font-medium">Thêm quyền:</p>
              <ul className="list-disc pl-5" data-testid="confirm-added">
                {confirm.added.map((c) => (
                  <li key={c}>
                    {describe(c)} (<code>{c}</code>)
                  </li>
                ))}
              </ul>
            </div>
          )}
          <AlertDialogFooter>
            <AlertDialogCancel disabled={pending}>Hủy</AlertDialogCancel>
            <AlertDialogAction
              disabled={pending}
              aria-busy={pending || undefined}
              onClick={(event) => {
                event.preventDefault()
                void save(true)
              }}
            >
              {pending && <Spinner data-icon="inline-start" aria-hidden />}
              Tiếp tục
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>

      {!role.isSystem && <DeleteRole role={role} onDeleted={() => router.push("/admin/roles")} />}
    </section>
  )
}

function RenameForm({ role, onRenamed }: { role: RoleSummary; onRenamed: (next: RoleSummary) => void }) {
  const [name, setName] = useState(role.displayName)
  const [fieldError, setFieldError] = useState<string | undefined>()
  const [error, setError] = useState<string | null>(null)
  const [pending, setPending] = useState(false)

  return (
    <form
      className="flex flex-col gap-2"
      onSubmit={async (e) => {
        e.preventDefault()
        const err = roleDisplayNameError(name)
        setFieldError(err)
        setError(null)
        if (err || pending) return
        setPending(true)
        try {
          onRenamed(await adminApi.renameRole(role.roleId, name.trim()))
        } catch (ex) {
          setError(writeError(ex))
        } finally {
          setPending(false)
        }
      }}
    >
      <FormAlert message={error} />
      <div className="flex items-end gap-2">
        <div className="flex-1">
          <TextField label="Tên hiển thị" value={name} disabled={pending} error={fieldError} onChange={(e) => setName(e.target.value)} />
        </div>
        <Button type="submit" variant="outline" disabled={pending || name.trim() === role.displayName}>
          Đổi tên
        </Button>
      </div>
    </form>
  )
}

function DeleteRole({ role, onDeleted }: { role: RoleSummary; onDeleted: () => void }) {
  const [open, setOpen] = useState(false)
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<string | null>(null)

  return (
    <section className="flex flex-col items-start gap-2">
      <FormAlert message={error} />
      <AlertDialog open={open} onOpenChange={(o) => !pending && setOpen(o)}>
        <AlertDialogTrigger render={<Button variant="destructive" size="sm" />}>Xóa vai trò</AlertDialogTrigger>
        <AlertDialogContent>
          <AlertDialogHeader>
            <AlertDialogTitle>Xóa vai trò “{role.displayName}”?</AlertDialogTitle>
            <AlertDialogDescription>Vai trò còn người mang thì không xóa được.</AlertDialogDescription>
          </AlertDialogHeader>
          <AlertDialogFooter>
            <AlertDialogCancel disabled={pending}>Hủy</AlertDialogCancel>
            <AlertDialogAction
              disabled={pending}
              onClick={async (event) => {
                event.preventDefault()
                setPending(true)
                setError(null)
                try {
                  await adminApi.deleteRole(role.roleId)
                  setOpen(false)
                  onDeleted()
                } catch (e) {
                  setOpen(false)
                  setError(writeError(e)) // 409 role-in-use / system-role → câu theo `type`
                } finally {
                  setPending(false)
                }
              }}
            >
              Xóa
            </AlertDialogAction>
          </AlertDialogFooter>
        </AlertDialogContent>
      </AlertDialog>
    </section>
  )
}
