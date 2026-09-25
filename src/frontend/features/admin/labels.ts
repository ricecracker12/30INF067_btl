import type { AdminUser, AuditAction } from "@/lib/api/types"

// Nhãn tiếng Việt của khu quản trị. `Record<…>` theo kiểu sinh: hợp đồng thêm giá trị là file này đỏ compile.

export const USER_STATUS_LABEL: Record<AdminUser["status"], string> = {
  active: "Đang hoạt động",
  locked: "Tạm khóa",
  disabled: "Đã bị khóa",
  deleted: "Đã xóa",
}

export const AUDIT_ACTION_LABEL: Record<AuditAction, string> = {
  "report.hide": "Ẩn nội dung bị báo cáo",
  "report.dismiss": "Bỏ qua báo cáo",
  "report.resolve": "Đóng báo cáo (đã xử lý)",
  "content.restore": "Khôi phục nội dung",
  "user.lock": "Khóa tài khoản",
  "user.unlock": "Mở khóa tài khoản",
  "role.assign": "Đổi vai trò tài khoản",
  "role.create": "Tạo vai trò",
  "role.rename": "Đổi tên vai trò",
  "role.permissions": "Sửa quyền của vai trò",
  "role.delete": "Xóa vai trò",
  "access.denied": "Bị từ chối truy cập",
}

export const AUDIT_ACTIONS = Object.keys(AUDIT_ACTION_LABEL) as AuditAction[]

export const dateTime = new Intl.DateTimeFormat("vi-VN", {
  dateStyle: "medium",
  timeStyle: "short",
  timeZone: "Asia/Ho_Chi_Minh",
})
