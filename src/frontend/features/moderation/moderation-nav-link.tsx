"use client"

import Link from "next/link"

import { hasPermission } from "@/lib/auth/permissions"
import { useMe } from "@/lib/auth/use-me"

import { MODERATION_PERMISSIONS } from "./permissions"

/**
 * Liên kết "Kiểm duyệt" ở header — hiện khi có `report.resolve` (Đ-6.20), kể cả vai trò tự tạo (`REVIEWER`), không bao giờ theo
 * tên vai trò. Đổi theo `/me` sống: bị hạ quyền rồi focus lại tab là liên kết biến mất, không tải lại trang (Mục 7.3).
 */
export function ModerationNavLink() {
  const { me } = useMe()
  if (!hasPermission(me, MODERATION_PERMISSIONS.queue)) return null
  return (
    <Link
      href="/moderation"
      className="hover:text-foreground"
      data-testid="nav-moderation"
    >
      Kiểm duyệt
    </Link>
  )
}
