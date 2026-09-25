import { AdminUsers } from "@/features/admin/admin-users"
import { ADMIN_SECTIONS } from "@/features/admin/sections"
import { RequirePermission } from "@/features/auth/require-permission"

// Danh sách tài khoản (GĐ6 E7). Chỉ ráp (Đ-E13); guard mềm any-of như policy của server (Mục 6.1).
export default function AdminUsersPage() {
  return (
    <RequirePermission anyOf={ADMIN_SECTIONS.users.anyOf}>
      <AdminUsers />
    </RequirePermission>
  )
}
