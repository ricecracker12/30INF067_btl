"use client"

import { use } from "react"

import { RoleEditor } from "@/features/admin/role-editor"
import { ADMIN_SECTIONS } from "@/features/admin/sections"
import { RequirePermission } from "@/features/auth/require-permission"

// Một vai trò + ma trận quyền (GĐ6 E8). Chỉ ráp (Đ-E13). `roleId` là số nguyên (admin-v1 `RoleId`); chuỗi rác → NaN → "không tìm thấy".
export default function AdminRolePage({
  params,
}: {
  params: Promise<{ roleId: string }>
}) {
  const { roleId } = use(params)
  return (
    <RequirePermission anyOf={ADMIN_SECTIONS.roles.anyOf}>
      <RoleEditor roleId={Number(roleId)} />
    </RequirePermission>
  )
}
