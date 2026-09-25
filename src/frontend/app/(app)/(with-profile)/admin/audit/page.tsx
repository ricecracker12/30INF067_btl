import { AuditLog } from "@/features/admin/audit-log"
import { ADMIN_SECTIONS } from "@/features/admin/sections"
import { RequirePermission } from "@/features/auth/require-permission"

// Nhật ký kiểm toán (GĐ6 E9). Chỉ ráp (Đ-E13); guard mềm theo `audit.read` — chỉ ADMIN theo ma trận PTTK (Đ-6.15).
export default function AdminAuditPage() {
  return (
    <RequirePermission anyOf={ADMIN_SECTIONS.audit.anyOf}>
      <AuditLog />
    </RequirePermission>
  )
}
