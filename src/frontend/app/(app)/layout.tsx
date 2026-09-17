import { AppHeader } from "@/components/shell/app-header"
import { LogoutButton } from "@/features/auth/logout-button"
import { RequireAuth } from "@/features/auth/require-auth"

// Nhóm route (app): mọi trang cần đăng nhập, từ GĐ2 đặt thêm ở đây mà không phải nghĩ lại cách chặn. Chỉ ráp
// (Đ-E13). Header nằm TRONG guard — chưa biết phiên thì không hiện cả nút "Đăng xuất".
export default function AppLayout({
  children,
}: Readonly<{
  children: React.ReactNode
}>) {
  return (
    <RequireAuth>
      <div className="flex min-h-svh flex-col">
        <AppHeader actions={<LogoutButton />} />
        <main className="mx-auto w-full max-w-2xl flex-1 p-6">{children}</main>
      </div>
    </RequireAuth>
  )
}
