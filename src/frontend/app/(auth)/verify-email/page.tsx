import { Suspense } from "react"

import { Card, CardContent, CardHeader, CardTitle } from "@/components/ui/card"
import { Skeleton } from "@/components/ui/skeleton"
import { VerifyEmail } from "@/features/auth/verify-email"

// Chỉ ráp (Đ-E13). `VerifyEmail` đọc `?token=` bằng `useSearchParams` nên PHẢI nằm trong <Suspense> (bẫy E4).
// Gọi API chỉ ở client (Đ-E11): Server Component gọi thì token đi qua server Next, và trên staging đường dẫn
// tương đối `/api/v1` không có host.
export default function VerifyEmailPage() {
  return (
    <Card>
      <CardHeader>
        <CardTitle>Xác minh email</CardTitle>
      </CardHeader>
      <CardContent>
        <Suspense fallback={<Skeleton className="h-5" aria-hidden />}>
          <VerifyEmail />
        </Suspense>
      </CardContent>
    </Card>
  )
}
