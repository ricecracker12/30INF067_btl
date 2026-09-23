import Link from "next/link"
import type { ReactNode } from "react"

type Props = {
  /** Nút bên phải (vd. "Đăng xuất") — shell không biết nghiệp vụ, màn/layout truyền vào (Đ-E13). */
  actions?: ReactNode
  /**
   * Liên kết điều hướng cạnh logo (GĐ4 Q-E7). Chuỗi đường dẫn nằm ở `app/`, shell chỉ đặt chỗ — `components/shell` không
   * biết nghiệp vụ. Màn hẹp: chữ nhỏ cùng hàng, không menu thả (kit không có, không đáng một component mới).
   */
  nav?: ReactNode
}

// Thanh đầu trang của khu vực đã đăng nhập, dùng lại cho mọi trang `(app)/…` từ GĐ2.
export function AppHeader({ actions, nav }: Props) {
  return (
    <header className="border-b border-border bg-background">
      <div className="mx-auto flex h-14 w-full max-w-2xl items-center justify-between gap-4 px-6">
        <div className="flex min-w-0 items-center gap-4">
          <Link href="/" className="font-medium">
            SocialApp
          </Link>
          {nav && (
            <nav
              aria-label="Điều hướng chính"
              className="flex items-center gap-3 text-sm text-muted-foreground"
            >
              {nav}
            </nav>
          )}
        </div>
        {actions}
      </div>
    </header>
  )
}
