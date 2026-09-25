import type { MeResponse } from "@/lib/api/types"

// Quyền HIỆU LỰC của người đang đăng nhập (GĐ6 Đ-6.11): `MeResponse.permissions` đọc từ DB, ADMIN = cả 18 mã. Không biết nghiệp vụ
// — không biết mã nào mở màn nào; chỗ đó là của `features/`.
//
// **Chỉ để vẽ** (ẩn/hiện liên kết, guard mềm). Server là nơi chặn thật: một nút FE vẽ nhầm thì server vẫn trả 403, và test FE
// không bao giờ là bằng chứng phân quyền. KHÔNG bao giờ suy quyền từ `me.role` — vai trò tự tạo (`REVIEWER`) có `report.resolve`
// cũng phải thấy hàng đợi (Đ-6.11); không dòng nào ở FE so tên vai trò.

type WithPermissions = Pick<MeResponse, "permissions"> | null | undefined

/** Có đúng mã này không. Chưa biết `me` (đang nạp) → `false`: không vẽ trước thứ có thể không được phép. */
export function hasPermission(me: WithPermissions, code: string): boolean {
  return me?.permissions.includes(code) ?? false
}

/** Có ÍT NHẤT một trong các mã — cùng ngữ nghĩa policy "any-of" của server (`[RequireAnyPermission]`, Mục 6.1). */
export function hasAnyPermission(
  me: WithPermissions,
  codes: readonly string[]
): boolean {
  return codes.some((code) => hasPermission(me, code))
}
