"use client"

import Link from "next/link"
import { usePathname } from "next/navigation"
import { useEffect, type ReactNode } from "react"

import { FormAlert } from "@/components/form/form-alert"
import { Button, buttonVariants } from "@/components/ui/button"
import { Card, CardContent } from "@/components/ui/card"
import { Skeleton } from "@/components/ui/skeleton"
import { refreshMe } from "@/lib/auth/me-store"
import { hasAnyPermission } from "@/lib/auth/permissions"
import { useMe } from "@/lib/auth/use-me"

type Props = {
  /** Có ÍT NHẤT một mã là được vào — cùng ngữ nghĩa any-of của server. */
  anyOf: readonly string[]
  children: ReactNode
}

/**
 * Guard MỀM của route quản trị/kiểm duyệt (GĐ6 Đ-6.20). Mềm vì server mới là nơi chặn (tầng 2 + `[PrivilegedEndpoint]`); đây chỉ
 * để người thiếu quyền thấy một câu giải thích thay vì một màn toàn lỗi 403.
 *
 * - Vào route → nạp lại `/me` (Đ-6.11): người vừa được nâng quyền vào thẳng được, người vừa bị hạ thấy ngay trang "không có
 *   quyền" — KHÔNG redirect im lặng, người vừa bị hạ quyền cần hiểu vì sao (Mục 7.3).
 * - Thiếu quyền giữa chừng (màn nhận 403 rồi gọi `refreshMe`) → cùng trang đó, không tải lại, không đăng xuất.
 */
export function RequirePermission({ anyOf, children }: Props) {
  const { status, me } = useMe()
  const pathname = usePathname()

  useEffect(() => {
    void refreshMe()
  }, [pathname])

  if (status === "unknown") {
    return (
      <div role="status" aria-label="Đang tải" className="flex flex-col gap-4">
        <Skeleton className="h-8 w-1/3" />
        <Skeleton className="h-24" />
      </div>
    )
  }

  if (status === "error") {
    return (
      <div className="flex flex-col gap-4">
        <FormAlert message="Không kiểm tra được quyền của bạn." />
        <Button
          variant="outline"
          className="self-start"
          onClick={() => void refreshMe()}
        >
          Thử lại
        </Button>
      </div>
    )
  }

  if (!hasAnyPermission(me, anyOf)) return <NoPermission />
  return <>{children}</>
}

/** Trang "không có quyền" — cả khi gõ tay URL lẫn khi vừa bị hạ quyền giữa lúc đang xem. */
export function NoPermission() {
  return (
    <Card data-testid="no-permission">
      <CardContent className="flex flex-col items-start gap-4">
        <p className="font-medium">Bạn không có quyền xem trang này.</p>
        <p className="text-sm text-muted-foreground">
          Quyền của tài khoản có thể vừa được quản trị viên thay đổi. Bạn vẫn
          đăng nhập và dùng được các phần khác của ứng dụng.
        </p>
        <Link href="/" className={buttonVariants({ variant: "outline" })}>
          Về trang chủ
        </Link>
      </CardContent>
    </Card>
  )
}
