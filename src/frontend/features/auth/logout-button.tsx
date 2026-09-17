"use client"

import { useState } from "react"

import { Button } from "@/components/ui/button"
import { Spinner } from "@/components/ui/spinner"
import { logout } from "@/lib/auth/session"

// Không tự điều hướng: `logout()` đưa phiên về `anonymous` (do `logout`), và `RequireAuth` là chỗ DUY NHẤT đẩy về
// /login. Hai chỗ cùng `router.replace` thì cái chạy sau thắng — guard chạy sau và gắn `?next=` cho người vừa tự
// đăng xuất.
export function LogoutButton() {
  const [pending, setPending] = useState(false)

  return (
    <Button
      variant="ghost"
      size="sm"
      disabled={pending}
      aria-busy={pending || undefined}
      onClick={() => {
        setPending(true)
        void logout()
      }}
    >
      {pending && <Spinner data-icon="inline-start" aria-hidden />}
      Đăng xuất
    </Button>
  )
}
