import { redirect } from "next/navigation"

// Trang gốc không có nội dung riêng: vào khu vực đã đăng nhập, guard của (app) tự đưa người chưa đăng nhập về
// /login?next=%2Fme. Không kiểm phiên ở đây — server không thấy token lẫn cookie refresh (Đ-E3).
export default function Page() {
  redirect("/me")
}
