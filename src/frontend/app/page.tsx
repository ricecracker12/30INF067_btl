import Link from "next/link"

import { buttonVariants } from "@/components/ui/button"

// GĐ1 tạm thời: trang gốc chỉ dẫn vào màn đăng nhập. E6 đổi thành redirect → /me.
export default function Page() {
  return (
    <div className="flex min-h-svh items-center justify-center p-6">
      <div className="flex max-w-md min-w-0 flex-col gap-4 text-sm leading-loose">
        <h1 className="text-2xl font-medium">SocialApp</h1>
        <p className="text-muted-foreground">
          Mạng xã hội học phần 30INF067. Đăng nhập để tiếp tục.
        </p>
        {/* Điều hướng thì phải là LINK thật, không phải nút. `<Button render={<Link/>}>` của
            Base UI dán ngữ nghĩa nút lên thẻ <a>: trình đọc màn hình đọc là "button", và bỏ
            `nativeButton={false}` thì Base UI cảnh báo ngay trong console. Lấy style của kit
            bằng `buttonVariants` là cách đúng cho trường hợp này. */}
        <Link href="/login" className={buttonVariants()}>
          Đăng nhập
        </Link>
      </div>
    </div>
  )
}
