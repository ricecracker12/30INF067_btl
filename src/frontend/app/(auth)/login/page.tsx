import { Button } from "@/components/ui/button"
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from "@/components/ui/card"
import { TextField } from "@/components/form/text-field"

// E1: khung tĩnh — chưa gọi API, chưa validate. E4 thay bằng
// features/auth/login-form.tsx (client component) và giữ nguyên khung này.
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
        <form className="flex flex-col gap-6">
          <TextField
            label="Email"
            name="email"
            type="email"
            autoComplete="email"
            placeholder="ban@vidu.com"
          />
          <TextField
            label="Mật khẩu"
            name="password"
            type="password"
            autoComplete="current-password"
          />
          <Button type="submit">Đăng nhập</Button>
        </form>
      </CardContent>
    </Card>
  )
}
