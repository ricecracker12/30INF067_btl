"use client"

import { use } from "react"

import { AdminUserDetail } from "@/features/admin/admin-user-detail"
import { ADMIN_SECTIONS } from "@/features/admin/sections"
import { RequirePermission } from "@/features/auth/require-permission"

// Một tài khoản (GĐ6 E7). Chỉ ráp (Đ-E13). `params` là Promise — mở bằng `use()`.
export default function AdminUserPage({
  params,
}: {
  params: Promise<{ userId: string }>
}) {
  const { userId } = use(params)
  return (
    <RequirePermission anyOf={ADMIN_SECTIONS.users.anyOf}>
      <AdminUserDetail userId={userId} />
    </RequirePermission>
  )
}
