"use client"

import { RefreshCwIcon } from "lucide-react"
import { useEffect, useState } from "react"

import { FormAlert } from "@/components/form/form-alert"
import { Button } from "@/components/ui/button"
import { Skeleton } from "@/components/ui/skeleton"
import { Spinner } from "@/components/ui/spinner"
import { authApi } from "@/lib/api/auth-api"
import { errorMessage } from "@/lib/api/messages"
import type { MeResponse } from "@/lib/api/types"

const dateTime = new Intl.DateTimeFormat("vi-VN", {
  dateStyle: "medium",
  timeStyle: "short",
  timeZone: "Asia/Ho_Chi_Minh",
})

export function MeProfile() {
  const [me, setMe] = useState<MeResponse | null>(null)
  const [error, setError] = useState<string | null>(null)
  const [pending, setPending] = useState(true)
  // Tăng để gọi lại /me. Nút "Tải lại" là cách E7 (Playwright) và F4 (bằng tay) tạo request đồng thời.
  const [attempt, setAttempt] = useState(0)

  useEffect(() => {
    // Rời trang (hoặc bấm Tải lại khi request cũ chưa xong) thì hủy request cũ.
    const controller = new AbortController()
    authApi.me(controller.signal).then(
      (res) => {
        setMe(res)
        setError(null)
        setPending(false)
      },
      (e: unknown) => {
        if ((e as Error).name === "AbortError") return
        setError(errorMessage("me", e))
        setPending(false)
      }
    )
    return () => controller.abort()
  }, [attempt])

  return (
    <section className="flex flex-col gap-6">
      <div className="flex items-center justify-between gap-4">
        <h1 className="text-xl font-medium">Tài khoản của tôi</h1>
        <Button
          variant="outline"
          size="sm"
          aria-busy={pending || undefined}
          onClick={() => {
            setPending(true)
            setAttempt((n) => n + 1)
          }}
        >
          {pending ? (
            <Spinner data-icon="inline-start" aria-hidden />
          ) : (
            <RefreshCwIcon data-icon="inline-start" aria-hidden />
          )}
          Tải lại
        </Button>
      </div>

      <FormAlert message={error} />

      {me ? (
        <dl
          data-testid="me-profile"
          className="grid grid-cols-[max-content_1fr] gap-x-6 gap-y-3 text-sm"
        >
          <dt className="text-muted-foreground">Email</dt>
          <dd className="break-all">{me.email}</dd>
          {/* `roleDisplayName` cho người đọc; `role` dành cho so logic — không hiện. */}
          <dt className="text-muted-foreground">Vai trò</dt>
          <dd>{me.roleDisplayName}</dd>
          <dt className="text-muted-foreground">Xác minh lúc</dt>
          <dd>
            {me.emailVerifiedAt
              ? dateTime.format(new Date(me.emailVerifiedAt))
              : "—"}
          </dd>
          <dt className="text-muted-foreground">Tham gia lúc</dt>
          <dd>{dateTime.format(new Date(me.createdAt))}</dd>
        </dl>
      ) : (
        !error && (
          <div className="flex flex-col gap-3" aria-hidden>
            <Skeleton className="h-5" />
            <Skeleton className="h-5" />
            <Skeleton className="h-5" />
            <Skeleton className="h-5" />
          </div>
        )
      )}
    </section>
  )
}
