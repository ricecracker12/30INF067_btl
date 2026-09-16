// Nhóm route (auth): đăng nhập, đăng ký, xác minh email — một khung giữa màn, không
// có shell của khu vực đã đăng nhập. Chỉ ráp, không chứa logic nghiệp vụ (Đ-E13).
export default function AuthLayout({
  children,
}: Readonly<{
  children: React.ReactNode
}>) {
  return (
    <div className="flex min-h-svh flex-col items-center justify-center gap-6 bg-muted/30 p-6">
      <div className="w-full max-w-sm">{children}</div>
    </div>
  )
}
