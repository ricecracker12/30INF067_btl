"use client"

import { useEffect, useState } from "react"

import { adminApi } from "@/lib/api/admin-api"
import { hasPermission } from "@/lib/auth/permissions"
import { useMe } from "@/lib/auth/use-me"

export type RoleOption = { code: string; displayName: string }

/**
 * Hai vai trò hệ thống gán được mà KHÔNG cần `role.manage` (chạm ADMIN cần thêm `role.manage` — L-D18). Dùng khi người xem không
 * đọc được `GET /admin/roles` (cần `role.manage`): hợp đồng không có danh sách vai trò nào cho người chỉ có `role.assign` — xem
 * Q-E5 của hướng dẫn khối E. Tên hiển thị khớp seed của `IdentitySeeder`.
 */
const FALLBACK: RoleOption[] = [
  { code: "USER", displayName: "Người dùng" },
  { code: "MODERATOR", displayName: "Kiểm duyệt viên" },
]

/**
 * Các vai trò CÓ THẬT để chọn (B.8 E7 "select các vai trò có thật"): có `role.manage` → đọc `GET /admin/roles` (kể cả vai trò tự
 * tạo); không → hai vai trò hệ thống ở trên. Lỗi đọc → cũng lùi về hai vai trò đó: đổi vai trò vẫn làm được, server vẫn kiểm.
 */
export function useRoleOptions(): RoleOption[] {
  const { me } = useMe()
  const canList = hasPermission(me, "role.manage")
  const [roles, setRoles] = useState<RoleOption[] | null>(null)

  useEffect(() => {
    if (!canList) return
    const controller = new AbortController()
    adminApi.listRoles(controller.signal).then(
      (list) => setRoles(list.map((r) => ({ code: r.code, displayName: r.displayName }))),
      () => undefined
    )
    return () => controller.abort()
  }, [canList])

  return canList && roles ? roles : FALLBACK
}
