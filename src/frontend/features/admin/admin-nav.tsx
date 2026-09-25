"use client"

import Link from "next/link"
import { usePathname } from "next/navigation"

import { hasAnyPermission } from "@/lib/auth/permissions"
import { useMe } from "@/lib/auth/use-me"
import { cn } from "@/lib/utils"

import { ADMIN_SECTION_LIST } from "./sections"

/**
 * Liên kết "Quản trị" ở header (Đ-6.20) — hiện khi có BẤT KỲ mã nào mở một mục, dẫn tới mục ĐẦU TIÊN được phép (người chỉ có
 * `audit.read` không bị đưa vào danh sách tài khoản rồi thấy "không có quyền"). Đổi theo `/me` sống: focus lại tab sau khi Admin
 * nâng quyền là liên kết hiện ra, không tải lại trang (mốc 1).
 */
export function AdminNavLink() {
  const { me } = useMe()
  const first = ADMIN_SECTION_LIST.find((s) => hasAnyPermission(me, s.anyOf))
  if (!first) return null
  return (
    <Link
      href={first.href}
      className="hover:text-foreground"
      data-testid="nav-admin"
    >
      Quản trị
    </Link>
  )
}

/** Thanh mục TRONG khu quản trị — chỉ mục người dùng được vào. */
export function AdminSectionNav() {
  const { me } = useMe()
  const pathname = usePathname()
  const sections = ADMIN_SECTION_LIST.filter((s) =>
    hasAnyPermission(me, s.anyOf)
  )
  if (sections.length === 0) return null

  return (
    <nav
      aria-label="Mục quản trị"
      className="flex gap-4 border-b border-border text-sm"
    >
      {sections.map((s) => {
        const active = pathname.startsWith(s.href)
        return (
          <Link
            key={s.href}
            href={s.href}
            aria-current={active ? "page" : undefined}
            className={cn(
              "-mb-px border-b-2 pb-2",
              active
                ? "border-primary font-medium text-foreground"
                : "border-transparent text-muted-foreground hover:text-foreground"
            )}
          >
            {s.label}
          </Link>
        )
      })}
    </nav>
  )
}
