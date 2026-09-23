"use client"

import { useRouter } from "next/navigation"
import { useEffect, type ReactNode } from "react"

import { FormAlert } from "@/components/form/form-alert"
import { PageSkeleton } from "@/components/shell/page-skeleton"
import { Button } from "@/components/ui/button"

import { loadProfile } from "./load-profile"
import { useProfile } from "./use-profile"

/**
 * Bất biến Đ-2.4: KHÔNG CÓ HỒ SƠ THÌ KHÔNG ĐĂNG ĐƯỢC BÀI. Server chỉ trả 403; đây là chỗ biến nó thành
 * đường đi không cưỡng lại được — gõ thẳng URL nào dưới `(with-profile)` cũng bị đưa về `/onboarding`.
 *
 * Đứng TRONG `RequireAuth` (layout `(app)`): tới đây thì chắc chắn đã có phiên. Chính `/onboarding` nằm
 * NGOÀI group này (Q-E2) — để nó bên trong thì 404 → chuyển `/onboarding` → 404 → … lặp vô hạn.
 *
 * Chỉ render `children` khi `ready`; mọi trạng thái khác KHÔNG render, cùng luật với `RequireAuth`
 * (không nháy nội dung của người chưa có hồ sơ).
 */
export function RequireProfile({ children }: { children: ReactNode }) {
  const { status } = useProfile()
  const router = useRouter()

  useEffect(() => {
    // StrictMode chạy effect hai lần → `loadProfile` single-flight, vẫn một cặp request.
    if (status === "unknown") void loadProfile()
  }, [status])

  useEffect(() => {
    if (status === "missing") router.replace("/onboarding")
  }, [status, router])

  if (status === "ready") return <>{children}</>
  if (status === "error") return <ProfileError />
  return <PageSkeleton />
}

/** 429/500/mất mạng: hồ sơ có thể vẫn có — cho thử lại, KHÔNG đá sang onboarding (sẽ ghi đè bio cũ). */
function ProfileError() {
  return (
    <div className="mx-auto flex min-h-svh w-full max-w-sm flex-col justify-center gap-6 p-6">
      <FormAlert message="Không tải được hồ sơ của bạn." />
      <Button variant="outline" onClick={() => void loadProfile()}>
        Thử lại
      </Button>
    </div>
  )
}
