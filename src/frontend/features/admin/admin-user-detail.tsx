"use client"

import { CircleAlertIcon } from "lucide-react"
import Link from "next/link"
import { useEffect, useState } from "react"

import { FormAlert } from "@/components/form/form-alert"
import { TextareaField } from "@/components/form/textarea-field"
import { Alert, AlertDescription } from "@/components/ui/alert"
import { Badge } from "@/components/ui/badge"
import { Button, buttonVariants } from "@/components/ui/button"
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  DialogTrigger,
} from "@/components/ui/dialog"
import { Field, FieldLabel, FieldTitle } from "@/components/ui/field"
import { RadioGroup, RadioGroupItem } from "@/components/ui/radio-group"
import { Skeleton } from "@/components/ui/skeleton"
import { Spinner } from "@/components/ui/spinner"
import { adminApi } from "@/lib/api/admin-api"
import { errorMessage, type ErrorContext } from "@/lib/api/messages"
import { ApiError } from "@/lib/api/problem"
import type { AdminUser, AdminUserChange } from "@/lib/api/types"
import { refreshMe } from "@/lib/auth/me-store"
import { hasPermission } from "@/lib/auth/permissions"
import { useMe } from "@/lib/auth/use-me"
import { lockReasonError } from "@/lib/validation/moderation"

import { dateTime, USER_STATUS_LABEL } from "./labels"
import { useRoleOptions, type RoleOption } from "./use-role-options"

type Loaded =
  | { key: string; user: AdminUser }
  | { key: string; notFound: true }
  | { key: string; error: string }

/** Câu sau một thao tác thành công — theo `revocation` của server (Đ-6.6), không đoán. */
const REVOCATION_TEXT: Record<AdminUserChange["revocation"], string> = {
  applied: "Đã lưu. Thay đổi có hiệu lực ở lần thao tác kế tiếp của người này — họ không phải đăng nhập lại.",
  "not-needed": "Đã lưu. Không có phiên nào cần thu hồi.",
  deferred:
    "Đã lưu, nhưng chưa thu hồi được phiên đang mở của người này (kho phiên tạm lỗi). Thay đổi có hiệu lực chậm nhất sau 15 phút.",
}

/**
 * Lỗi của một thao tác ghi. 409 `last-admin` đi qua `errorMessage` (câu của server, Đ-6.21); 400 có `errors` (tự khóa → `userId`,
 * lý do → `reason`, vai trò lạ → `roleCode`) lấy ĐÚNG câu server. 403 → nạp lại `/me`.
 */
function writeError(e: unknown, context: ErrorContext): string {
  if (e instanceof ApiError && e.status === 403) void refreshMe()
  if (e instanceof ApiError && e.status === 400) {
    const first = Object.values(e.fieldErrors)[0]?.[0]
    if (first) return first
  }
  return errorMessage(context, e)
}

/**
 * Một tài khoản (GĐ6 E7): khóa (hộp thoại lý do) / mở khóa / đổi vai trò. KHÔNG optimistic (Đ-6.21): vẽ theo `AdminUserChange` server
 * trả — một thao tác bị bất biến "≥ 1 Admin" chặn mà UI vẽ "thành công" trước là UI nói dối về quyền.
 */
export function AdminUserDetail({ userId }: { userId: string }) {
  const { me } = useMe()
  const [data, setData] = useState<Loaded | null>(null)
  const [attempt, setAttempt] = useState(0)
  const [result, setResult] = useState<AdminUserChange["revocation"] | null>(null)
  const key = `${userId}:${attempt}`
  const current = data?.key === key ? data : null

  useEffect(() => {
    const controller = new AbortController()
    const pageKey = `${userId}:${attempt}`
    adminApi.getUser(userId, controller.signal).then(
      (user) => setData({ key: pageKey, user }),
      (e: unknown) => {
        if ((e as Error).name === "AbortError") return
        if (e instanceof ApiError && e.status === 404) setData({ key: pageKey, notFound: true })
        else {
          if (e instanceof ApiError && e.status === 403) void refreshMe()
          setData({ key: pageKey, error: errorMessage("admin-read", e) })
        }
      }
    )
    return () => controller.abort()
  }, [userId, attempt])

  const back = (
    <Link href="/admin/users" className={buttonVariants({ variant: "outline", size: "sm" })}>
      Về danh sách
    </Link>
  )

  if (current === null)
    return (
      <div className="flex flex-col gap-3" aria-hidden>
        <Skeleton className="h-8 w-1/2" />
        <Skeleton className="h-32" />
      </div>
    )
  if ("notFound" in current)
    return (
      <div className="flex flex-col items-start gap-4">
        <p>Không tìm thấy tài khoản.</p>
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

  const user = current.user
  const applied = (change: AdminUserChange) => {
    setData({ key, user: change.user })
    setResult(change.revocation)
  }

  return (
    <section className="flex flex-col gap-6">
      <div className="flex items-center justify-between gap-4">
        <h1 className="text-xl font-medium">{user.displayName ?? "(chưa có hồ sơ)"}</h1>
        {back}
      </div>

      {result === "deferred" ? (
        <Alert variant="warning" data-testid="revocation-deferred">
          <CircleAlertIcon aria-hidden />
          <AlertDescription>{REVOCATION_TEXT.deferred}</AlertDescription>
        </Alert>
      ) : (
        result && (
          <p role="status" className="text-sm text-muted-foreground" data-testid="revocation-result">
            {REVOCATION_TEXT[result]}
          </p>
        )
      )}

      <dl className="grid grid-cols-[max-content_1fr] gap-x-6 gap-y-3 text-sm" data-testid="admin-user">
        <dt className="text-muted-foreground">Email</dt>
        <dd className="break-all">{user.email}</dd>
        <dt className="text-muted-foreground">Vai trò</dt>
        <dd data-testid="admin-user-role">{user.roleDisplayName}</dd>
        <dt className="text-muted-foreground">Trạng thái</dt>
        <dd>
          <Badge variant={user.status === "active" ? "secondary" : "destructive"} data-testid="admin-user-status">
            {USER_STATUS_LABEL[user.status]}
          </Badge>
        </dd>
        <dt className="text-muted-foreground">Xác minh email</dt>
        <dd>{user.emailVerified ? "Đã xác minh" : "Chưa xác minh"}</dd>
        {user.lockedUntil && (
          <>
            <dt className="text-muted-foreground">Tạm khóa tới</dt>
            <dd>{dateTime.format(new Date(user.lockedUntil))}</dd>
          </>
        )}
        <dt className="text-muted-foreground">Tạo lúc</dt>
        <dd>{dateTime.format(new Date(user.createdAt))}</dd>
      </dl>

      <div className="flex flex-wrap gap-2">
        {user.status !== "disabled" && hasPermission(me, "user.lock") && (
          <LockDialog user={user} onDone={applied} />
        )}
        {user.status === "disabled" && hasPermission(me, "user.unlock") && (
          <UnlockButton user={user} onDone={applied} />
        )}
        {hasPermission(me, "role.assign") && <RoleDialog user={user} onDone={applied} />}
      </div>
    </section>
  )
}

type ActionProps = { user: AdminUser; onDone: (change: AdminUserChange) => void }

function LockDialog({ user, onDone }: ActionProps) {
  const [open, setOpen] = useState(false)
  const [reason, setReason] = useState("")
  const [fieldError, setFieldError] = useState<string | undefined>()
  const [error, setError] = useState<string | null>(null)
  const [pending, setPending] = useState(false)

  async function submit() {
    const err = lockReasonError(reason)
    setFieldError(err)
    setError(null)
    if (err || pending) return
    setPending(true)
    try {
      onDone(await adminApi.lock(user.userId, reason.trim()))
      setOpen(false)
    } catch (e) {
      setError(writeError(e, "admin-lock"))
    } finally {
      setPending(false)
    }
  }

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        if (!next && pending) return
        if (next) {
          setReason("")
          setFieldError(undefined)
          setError(null)
        }
        setOpen(next)
      }}
    >
      <DialogTrigger render={<Button variant="destructive" size="sm" />}>Khóa tài khoản</DialogTrigger>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Khóa tài khoản này?</DialogTitle>
          <DialogDescription>
            Người này bị đăng xuất khỏi mọi thiết bị ở lần thao tác kế tiếp và không đăng nhập lại được cho tới khi được mở khóa.
          </DialogDescription>
        </DialogHeader>
        <form
          id="lock-form"
          className="flex flex-col gap-3"
          onSubmit={(e) => {
            e.preventDefault()
            void submit()
          }}
        >
          <FormAlert message={error} />
          <TextareaField
            label="Lý do (ghi vào nhật ký kiểm toán)"
            rows={3}
            value={reason}
            disabled={pending}
            error={fieldError}
            onChange={(e) => {
              setReason(e.target.value)
              setFieldError(undefined)
            }}
          />
        </form>
        <DialogFooter>
          <Button type="submit" form="lock-form" variant="destructive" disabled={pending} aria-busy={pending || undefined}>
            {pending && <Spinner data-icon="inline-start" aria-hidden />}
            Khóa
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}

function UnlockButton({ user, onDone }: ActionProps) {
  const [pending, setPending] = useState(false)
  const [error, setError] = useState<string | null>(null)
  return (
    <div className="flex flex-col gap-2">
      <FormAlert message={error} />
      <Button
        variant="outline"
        size="sm"
        disabled={pending}
        aria-busy={pending || undefined}
        onClick={async () => {
          setPending(true)
          setError(null)
          try {
            onDone(await adminApi.unlock(user.userId))
          } catch (e) {
            setError(writeError(e, "admin-lock"))
          } finally {
            setPending(false)
          }
        }}
      >
        {pending && <Spinner data-icon="inline-start" aria-hidden />}
        Mở khóa
      </Button>
    </div>
  )
}

function RoleDialog({ user, onDone }: ActionProps) {
  const roles = useRoleOptions()
  const [open, setOpen] = useState(false)
  const [roleCode, setRoleCode] = useState(user.roleCode)
  const [error, setError] = useState<string | null>(null)
  const [pending, setPending] = useState(false)
  // Vai trò hiện tại luôn có trong danh sách — kể cả khi danh sách lùi về hai vai trò hệ thống.
  const options: RoleOption[] = roles.some((r) => r.code === user.roleCode)
    ? roles
    : [...roles, { code: user.roleCode, displayName: user.roleDisplayName }]

  async function submit() {
    if (pending) return
    setPending(true)
    setError(null)
    try {
      onDone(await adminApi.assignRole(user.userId, roleCode))
      setOpen(false)
    } catch (e) {
      setError(writeError(e, "admin-role"))
    } finally {
      setPending(false)
    }
  }

  return (
    <Dialog
      open={open}
      onOpenChange={(next) => {
        if (!next && pending) return
        if (next) {
          setRoleCode(user.roleCode)
          setError(null)
        }
        setOpen(next)
      }}
    >
      <DialogTrigger render={<Button variant="outline" size="sm" />}>Đổi vai trò</DialogTrigger>
      <DialogContent>
        <DialogHeader>
          <DialogTitle>Đổi vai trò</DialogTitle>
          <DialogDescription>
            Quyền mới có hiệu lực ở lần thao tác kế tiếp của người này — họ không phải đăng nhập lại.
          </DialogDescription>
        </DialogHeader>
        <FormAlert message={error} />
        <Field>
          <FieldTitle id="role-pick-label">Vai trò</FieldTitle>
          <RadioGroup value={roleCode} aria-labelledby="role-pick-label" onValueChange={(v) => setRoleCode(v as string)}>
            {options.map((r) => (
              <FieldLabel key={r.code} htmlFor={`role-pick-${r.code}`}>
                <Field orientation="horizontal">
                  <RadioGroupItem id={`role-pick-${r.code}`} value={r.code} disabled={pending} />
                  <FieldTitle>{r.displayName}</FieldTitle>
                </Field>
              </FieldLabel>
            ))}
          </RadioGroup>
        </Field>
        <DialogFooter>
          <Button
            disabled={pending || roleCode === user.roleCode}
            aria-busy={pending || undefined}
            onClick={() => void submit()}
          >
            {pending && <Spinner data-icon="inline-start" aria-hidden />}
            Lưu vai trò
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  )
}
