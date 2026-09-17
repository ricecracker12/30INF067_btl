import Link from "next/link"

import {
  Card,
  CardContent,
  CardDescription,
  CardFooter,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import { RegisterForm } from "@/features/auth/register-form"

// Chỉ ráp (Đ-E13). Không đọc `useSearchParams` nên không cần <Suspense> như /login.
export default function RegisterPage() {
  return (
    <Card>
      <CardHeader>
        <CardTitle>Đăng ký</CardTitle>
        <CardDescription>
          Tạo tài khoản SocialApp. Bạn cần xác minh email trước khi đăng nhập.
        </CardDescription>
      </CardHeader>
      <CardContent>
        <RegisterForm />
      </CardContent>
      <CardFooter className="justify-center text-sm text-muted-foreground">
        <p>
          Đã có tài khoản?{" "}
          <Link
            href="/login"
            className="font-medium text-foreground underline underline-offset-4"
          >
            Đăng nhập
          </Link>
        </p>
      </CardFooter>
    </Card>
  )
}
