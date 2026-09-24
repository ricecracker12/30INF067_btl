import Link from "next/link"

import { AppHeader } from "@/components/shell/app-header"
import { LogoutButton } from "@/features/auth/logout-button"
import { RequireAuth } from "@/features/auth/require-auth"

import { MessagesNav } from "./messages-nav"

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
        <AppHeader
          // Q-E7: liên kết ở `app/` — shell chỉ đặt chỗ (Đ-E13). Không huy hiệu đếm lời mời: thông báo là GĐ6. Không có
          // "Trang chủ": logo đã dẫn về `/` (bỏ 2026-09-23 theo yêu cầu — hai lối vào cùng một chỗ trên cùng một hàng).
          nav={
            <>
              <Link href="/friends" className="hover:text-foreground">
                Bạn bè
              </Link>
              {/* GĐ5 E3: link "Tin nhắn" + badge chưa đọc — MessagesNav đọc hồ sơ, ẩn khi đang onboarding. */}
              <MessagesNav />
              <Link href="/me" className="hover:text-foreground">
                Trang của tôi
              </Link>
            </>
          }
          actions={<LogoutButton />}
        />
        <main className="mx-auto w-full max-w-2xl flex-1 p-6">{children}</main>
      </div>
    </RequireAuth>
  )
}
