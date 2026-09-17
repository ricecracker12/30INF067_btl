"use client"

import { usePathname, useRouter } from "next/navigation"
import { useEffect, type ReactNode } from "react"

import { FormAlert } from "@/components/form/form-alert"
import { PageSkeleton } from "@/components/shell/page-skeleton"
import { Button } from "@/components/ui/button"
import { bootstrapSession } from "@/lib/auth/session"
import { useSession } from "@/lib/auth/use-session"

/**
 * Guard phía client của nhóm route `(app)` (Đ-E3) — không `proxy.ts`: server không thấy access token (memory của
 * tab) lẫn cookie refresh (`Path=/api/v1/auth`). Chỉ render `children` khi `authenticated`; mọi trạng thái khác
 * KHÔNG render, để nội dung trang có bảo vệ không nháy lên và trang con không bắn request thiếu token.
 */
export function RequireAuth({ children }: { children: ReactNode }) {
  const { status, endedBy } = useSession()
  const router = useRouter()
  const pathname = usePathname()

  useEffect(() => {
    // StrictMode chạy effect hai lần → coordinator gộp, vẫn một POST /auth/refresh.
    if (status === "unknown") void bootstrapSession()
  }, [status])

  useEffect(() => {
    if (status !== "anonymous") return
    // Tự đăng xuất thì về /login trơn; phiên hết hạn / chưa đăng nhập thì kèm `next` để quay lại đúng trang.
    router.replace(
      endedBy === "logout"
        ? "/login"
        : `/login?next=${encodeURIComponent(pathname)}`
    )
  }, [status, endedBy, pathname, router])

  if (status === "authenticated") return <>{children}</>
  if (status === "error") return <SessionError />
  return <PageSkeleton />
}

/** Refresh khởi động 429/500/mất mạng: phiên có thể vẫn còn — cho thử lại, không đẩy về /login. */
function SessionError() {
  return (
    <div className="mx-auto flex min-h-svh w-full max-w-sm flex-col justify-center gap-6 p-6">
      <FormAlert message="Không kiểm tra được phiên đăng nhập." />
      <Button variant="outline" onClick={() => void bootstrapSession()}>
        Thử lại
      </Button>
    </div>
  )
}
