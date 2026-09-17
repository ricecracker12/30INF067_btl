"use client"

import { CircleAlertIcon } from "lucide-react"
import { useEffect, useRef } from "react"

import { Alert, AlertDescription } from "@/components/ui/alert"

type Props = {
  /** Thông điệp lỗi cả form; `null` thì không render gì. */
  message: string | null
}

// Lỗi cấp form (401, 423, 500, mất mạng…) — E1 bước 4, dùng chung cho E3–E5. Mỗi lần thông điệp
// đổi thì chuyển focus vào đây: người dùng bàn phím / trình đọc màn hình đang đứng ở nút gửi, không
// chuyển focus thì họ không biết form vừa hỏng.
export function FormAlert({ message }: Props) {
  const ref = useRef<HTMLDivElement>(null)

  useEffect(() => {
    if (message) ref.current?.focus()
  }, [message])

  if (!message) return null

  return (
    <Alert ref={ref} variant="destructive" tabIndex={-1}>
      <CircleAlertIcon aria-hidden />
      <AlertDescription>{message}</AlertDescription>
    </Alert>
  )
}
