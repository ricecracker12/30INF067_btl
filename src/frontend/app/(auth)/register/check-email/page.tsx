import Link from "next/link"

import {
  Card,
  CardContent,
  CardFooter,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import { CheckEmail } from "@/features/auth/check-email"

// Chỉ ráp (Đ-E13). Email hiển thị nằm trong bộ nhớ của tab, KHÔNG trong query string (E3 bước 4).
export default function CheckEmailPage() {
  return (
    <Card>
      <CardHeader>
        <CardTitle>Kiểm tra hộp thư</CardTitle>
      </CardHeader>
      <CardContent>
        <CheckEmail />
      </CardContent>
      <CardFooter className="justify-center text-sm text-muted-foreground">
        <p>
          Đã xác minh xong?{" "}
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
