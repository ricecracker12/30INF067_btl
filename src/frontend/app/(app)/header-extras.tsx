"use client"

import { AdminNavLink } from "@/features/admin/admin-nav"
import { ModerationNavLink } from "@/features/moderation/moderation-nav-link"
import { NotificationBell } from "@/features/notification/notification-bell"
import { useProfile } from "@/features/profile/use-profile"
import { SearchBox } from "@/features/search/search-box"

// Phần header của GĐ6 (Đ-6.20). Chỉ ráp (Đ-E13) — khuôn `MessagesNav` của GĐ5: đang onboarding (chưa có hồ sơ) thì chưa ai tìm
// được mình, chưa có thông báo nào, và không hỏi lại `unread-count` mỗi 30 giây cho một người chưa "tồn tại" (Đ-2.4).

/** "Kiểm duyệt" và "Quản trị" — mỗi liên kết tự ẩn/hiện theo `permissions` sống (focus tab là nạp lại `/me`). */
export function PermissionNav() {
  const { profile } = useProfile()
  if (!profile) return null
  return (
    <>
      <ModerationNavLink />
      <AdminNavLink />
    </>
  )
}

/** Ô tìm + chuông, cạnh nút Đăng xuất. */
export function HeaderTools() {
  const { profile } = useProfile()
  if (!profile) return null
  return (
    <>
      <SearchBox />
      <NotificationBell />
    </>
  )
}
