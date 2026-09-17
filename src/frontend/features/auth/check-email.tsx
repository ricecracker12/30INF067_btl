"use client"

import { usePendingEmail } from "./pending-email"

// Màn sau 201 của đăng ký. Email lấy từ bộ nhớ của tab, không từ URL (E3 bước 4); tải lại trang thì
// không còn → câu chung không có email.
export function CheckEmail() {
  const email = usePendingEmail()

  return (
    <div className="flex flex-col gap-3 text-sm">
      <p>
        {email ? (
          <>
            Chúng tôi đã gửi liên kết xác minh tới{" "}
            <span className="font-medium break-all">{email}</span>.
          </>
        ) : (
          "Chúng tôi đã gửi liên kết xác minh tới email bạn vừa đăng ký."
        )}
      </p>
      <p className="text-muted-foreground">
        Liên kết hết hạn sau 24 giờ. Không thấy thư thì kiểm tra cả thư mục
        spam.
      </p>
    </div>
  )
}
