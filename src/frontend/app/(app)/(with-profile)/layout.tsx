import { RequireProfile } from "@/features/profile/require-profile"

// Q-E2 — route group lồng: mọi trang CẦN HỒ SƠ nằm dưới đây. Route group không tạo segment URL nên
// đường dẫn không đổi (`/me` vẫn là `/me`). `/onboarding` nằm ngoài group này, tức có `RequireAuth`
// mà không có `RequireProfile` — nếu không thì guard tự đá chính màn onboarding và lặp vô hạn.
//
// Chỉ ráp (Đ-E13).
export default function WithProfileLayout({
  children,
}: Readonly<{
  children: React.ReactNode
}>) {
  return <RequireProfile>{children}</RequireProfile>
}
