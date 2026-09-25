"use client"

import { useRouter } from "next/navigation"
import { useEffect } from "react"

import { NoPermission } from "@/features/auth/require-permission"
import { ADMIN_SECTION_LIST } from "@/features/admin/sections"
import { hasAnyPermission } from "@/lib/auth/permissions"
import { useMe } from "@/lib/auth/use-me"

// `/admin` → mục ĐẦU TIÊN người dùng được vào (liên kết "Quản trị" ở header cũng dẫn thẳng tới đó). Không mục nào → trang
// "không có quyền", không redirect im lặng (Đ-6.20).
export default function AdminIndexPage() {
  const { status, me } = useMe()
  const router = useRouter()
  const first = ADMIN_SECTION_LIST.find((s) => hasAnyPermission(me, s.anyOf))

  useEffect(() => {
    if (first) router.replace(first.href)
  }, [first, router])

  if (status === "ready" && !first) return <NoPermission />
  return null
}
