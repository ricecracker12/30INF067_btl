import { RolesScreen } from "@/features/admin/roles-screen"
import { ADMIN_SECTIONS } from "@/features/admin/sections"
import { RequirePermission } from "@/features/auth/require-permission"

// Danh sách vai trò (GĐ6 E8). Chỉ ráp (Đ-E13); guard mềm theo `role.manage`.
export default function AdminRolesPage() {
  return (
    <RequirePermission anyOf={ADMIN_SECTIONS.roles.anyOf}>
      <RolesScreen />
    </RequirePermission>
  )
}
