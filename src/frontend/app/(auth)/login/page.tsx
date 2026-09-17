import { Suspense } from "react"

import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import { Skeleton } from "@/components/ui/skeleton"
import { LoginForm } from "@/features/auth/login-form"

// Chỉ ráp (Đ-E13). `LoginForm` đọc `useSearchParams` (lấy `?next=`) nên PHẢI nằm trong <Suspense>:
// thiếu thì `next build` báo lỗi prerender và cả trang bị đẩy về render phía client. Khung Card vẫn
// prerender được; chỉ form chờ tới lúc hydrate.
export default function LoginPage() {
  return (
    <Card>
      <CardHeader>
        <CardTitle>Đăng nhập</CardTitle>
        <CardDescription>
          Nhập email và mật khẩu của bạn để vào SocialApp.
        </CardDescription>
      </CardHeader>
      <CardContent>
        <Suspense fallback={<LoginFormSkeleton />}>
          <LoginForm />
        </Suspense>
      </CardContent>
    </Card>
  )
}

function LoginFormSkeleton() {
  return (
    <div className="flex flex-col gap-6" aria-hidden>
      <Skeleton className="h-14" />
      <Skeleton className="h-14" />
      <Skeleton className="h-9" />
    </div>
  )
}
