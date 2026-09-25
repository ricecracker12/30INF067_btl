// Ba mục của khu quản trị và mã quyền mở từng mục — cùng tầng 2 của server (admin-v1, moderation-v1 Mục 6.1). Một bảng cho ba
// người dùng: liên kết "Quản trị" ở header (tới mục ĐẦU TIÊN được phép), thanh mục trong khu quản trị, và guard mềm của từng trang.

export type AdminSection = {
  href: string
  label: string
  /** Any-of, như server. */
  anyOf: readonly string[]
}

export const ADMIN_SECTIONS = {
  // `GET /admin/users` là policy any-of — người chỉ có `role.assign` cũng phải thấy danh sách để gán vai trò (Mục 6.1).
  users: {
    href: "/admin/users",
    label: "Tài khoản",
    anyOf: ["user.lock", "user.unlock", "role.assign"],
  },
  roles: { href: "/admin/roles", label: "Vai trò", anyOf: ["role.manage"] },
  audit: { href: "/admin/audit", label: "Nhật ký", anyOf: ["audit.read"] },
} as const satisfies Record<string, AdminSection>

export const ADMIN_SECTION_LIST: readonly AdminSection[] = [
  ADMIN_SECTIONS.users,
  ADMIN_SECTIONS.roles,
  ADMIN_SECTIONS.audit,
]

/** Mọi mã mở ít nhất một mục — liên kết "Quản trị" hiện khi có bất kỳ mã nào (Đ-6.20). */
export const ADMIN_ANY_OF: readonly string[] = ADMIN_SECTION_LIST.flatMap(
  (s) => s.anyOf
)
