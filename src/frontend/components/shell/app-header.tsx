import Link from "next/link"
import type { ReactNode } from "react"

type Props = {
  /** Nút bên phải (vd. "Đăng xuất") — shell không biết nghiệp vụ, màn/layout truyền vào (Đ-E13). */
  actions?: ReactNode
}

// Thanh đầu trang của khu vực đã đăng nhập, dùng lại cho mọi trang `(app)/…` từ GĐ2.
export function AppHeader({ actions }: Props) {
  return (
    <header className="border-b border-border bg-background">
      <div className="mx-auto flex h-14 w-full max-w-2xl items-center justify-between gap-4 px-6">
        <Link href="/" className="font-medium">
          SocialApp
        </Link>
        {actions}
      </div>
    </header>
  )
}
