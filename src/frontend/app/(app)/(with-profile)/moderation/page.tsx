"use client"

import { use } from "react"

import { RequirePermission } from "@/features/auth/require-permission"
import { ModerationQueue, type QueueNotice } from "@/features/moderation/moderation-queue"
import { MODERATION_PERMISSIONS } from "@/features/moderation/permissions"

// Hàng đợi kiểm duyệt (GĐ6 E6). Chỉ ráp (Đ-E13): guard mềm theo `report.resolve` (Đ-6.20) — server vẫn chặn thật.
// `?notice=` do màn chi tiết đặt khi quay về (xử lý xong / người khác vừa xử lý); giá trị lạ bị bỏ qua.
export default function ModerationPage({
  searchParams,
}: {
  searchParams: Promise<{ notice?: string | string[] }>
}) {
  const { notice } = use(searchParams)
  const known: QueueNotice | undefined =
    notice === "done" || notice === "decided" ? notice : undefined
  return (
    <RequirePermission anyOf={[MODERATION_PERMISSIONS.queue]}>
      <ModerationQueue notice={known} />
    </RequirePermission>
  )
}
