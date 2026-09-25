import { AdminSectionNav } from "@/features/admin/admin-nav"

// Khu quản trị (GĐ6 E7–E9). Chỉ ráp (Đ-E13): thanh mục theo quyền; guard mềm nằm ở TỪNG trang (mỗi mục một mã quyền riêng).
export default function AdminLayout({
  children,
}: Readonly<{ children: React.ReactNode }>) {
  return (
    <div className="flex flex-col gap-6">
      <AdminSectionNav />
      {children}
    </div>
  )
}
