"use client"

import { use } from "react"

import { RequirePermission } from "@/features/auth/require-permission"
import { MODERATION_PERMISSIONS } from "@/features/moderation/permissions"
import { ReportDetailScreen } from "@/features/moderation/report-detail"

// Chi tiết một báo cáo (GĐ6 E6). Chỉ ráp (Đ-E13). `params` là Promise — mở bằng `use()`.
export default function ReportPage({
  params,
}: {
  params: Promise<{ reportId: string }>
}) {
  const { reportId } = use(params)
  return (
    <RequirePermission anyOf={[MODERATION_PERMISSIONS.queue]}>
      <ReportDetailScreen reportId={reportId} />
    </RequirePermission>
  )
}
