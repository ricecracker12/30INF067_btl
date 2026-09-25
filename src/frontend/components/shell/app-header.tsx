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
      {/* Xuống dòng khi chật (GĐ6: thêm "Kiểm duyệt", "Quản trị", ô tìm, chuông) — chiều cao tối thiểu giữ 14 như cũ. */}
      <div className="mx-auto flex min-h-14 w-full max-w-2xl flex-wrap items-center justify-between gap-x-4 gap-y-2 px-6 py-2">
        <div className="flex min-w-0 flex-wrap items-center gap-4">
          <Link href="/" className="font-medium">
            SocialApp
          </Link>
          {nav && (
            <nav
              aria-label="Điều hướng chính"
              className="flex flex-wrap items-center gap-x-3 gap-y-1 text-sm text-muted-foreground"
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
