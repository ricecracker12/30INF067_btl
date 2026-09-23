"use client"

import { useRouter } from "next/navigation"
import { useEffect } from "react"

import { PageSkeleton } from "@/components/shell/page-skeleton"

import { loadProfile } from "./load-profile"
import { ProfileForm } from "./profile-form"
import { useProfile } from "./use-profile"

/**
 * Màn onboarding (FR-013, Đ-2.4). Nằm NGOÀI `(with-profile)` (Q-E2) nên phải tự biết trạng thái hồ sơ:
 *
 * - `unknown` → tự nạp (người dùng gõ thẳng `/onboarding` chứ không bị guard đưa tới).
 * - `ready`   → đã có hồ sơ rồi, không cho khai lại: `PUT` ở đây sẽ GHI ĐÈ bio cũ (Q-D3). Về `/me`.
 * - `missing` → đúng chỗ, hiện form.
 * - `error`   → vẫn hiện form: không tải được hồ sơ không có nghĩa là không được tạo, và server là bên
 *               quyết định cuối (400/403 sẽ hiện dưới trường).
 */
export function Onboarding() {
  const { status } = useProfile()
  const router = useRouter()

  useEffect(() => {
    if (status === "unknown") void loadProfile()
  }, [status])

  useEffect(() => {
    // Onboarding xong → trang chủ: người mới thấy feed GỢI Ý, đường gặp người khác (Đ-4.6, GĐ4 Q-E1; trước đó /me).
    if (status === "ready") router.replace("/")
  }, [status, router])

  if (status === "unknown" || status === "ready") return <PageSkeleton />

  return (
    <section className="flex flex-col gap-6">
      <div className="flex flex-col gap-2">
        <h1 className="text-xl font-medium">Hoàn tất hồ sơ</h1>
        <p className="text-sm text-muted-foreground">
          Đặt tên hiển thị trước khi bắt đầu — mọi bài đăng và bình luận của bạn
          sẽ hiện tên này.
        </p>
      </div>
      <ProfileForm initial={null} submitLabel="Bắt đầu" />
    </section>
  )
}
