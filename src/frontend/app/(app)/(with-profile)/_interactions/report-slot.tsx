"use client"

import { ReportButton } from "@/features/report/report-dialog"
import type { ReportTargetType } from "@/lib/api/types"
import { hasPermission } from "@/lib/auth/permissions"
import { useMe } from "@/lib/auth/use-me"

/**
 * Nút "Báo cáo" ghép vào slot của màn NGƯỜI KHÁC (GĐ6 Đ-6.20) — bài (GĐ2), bình luận (GĐ3), hồ sơ (GĐ4). Chỉ `app/` biết cả
 * `features/report` lẫn feature chủ của slot (Đ-E13). Ẩn khi vai trò thiếu `report.create` (vai trò tự tạo có thể không có) —
 * chỉ để vẽ; server vẫn trả 403. Chỗ gọi tự loại nội dung của chính mình (server trả 400 nếu lọt).
 */
export function ReportSlot({
  targetType,
  targetId,
  compact,
}: {
  targetType: ReportTargetType
  targetId: string
  compact?: boolean
}) {
  const { me } = useMe()
  if (!hasPermission(me, "report.create")) return null
  return (
    <ReportButton targetType={targetType} targetId={targetId} compact={compact} />
  )
}
