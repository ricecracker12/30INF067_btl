import { Skeleton } from "@/components/ui/skeleton"

// Khung chờ cả trang khi chưa biết phiên (Đ-E3): KHÔNG có chữ nào của trang thật — người chưa đăng nhập không
// được thấy nháy nội dung. `role="status"` để trình đọc màn hình biết trang đang tải.
export function PageSkeleton() {
  return (
    <div
      role="status"
      aria-label="Đang tải"
      data-testid="page-skeleton"
      className="flex min-h-svh flex-col"
    >
      <div className="h-14 border-b border-border" />
      <div className="mx-auto flex w-full max-w-2xl flex-col gap-4 p-6">
        <Skeleton className="h-8 w-1/3" />
        <Skeleton className="h-24" />
      </div>
    </div>
  )
}
