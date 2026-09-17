"use client"

import Link from "next/link"
import { useRouter, useSearchParams } from "next/navigation"
import { useEffect, useState } from "react"

import { FormAlert } from "@/components/form/form-alert"
import { Button, buttonVariants } from "@/components/ui/button"
import { Spinner } from "@/components/ui/spinner"
import { errorMessage } from "@/lib/api/messages"
import { ApiError } from "@/lib/api/problem"
import { verifyOnce } from "@/lib/auth/verify-once"
import { isVerifyToken } from "@/lib/validation/auth"

type Outcome =
  | { kind: "verified"; email: string }
  | { kind: "invalid" }
  | { kind: "gone" }
  | { kind: "error"; message: string }

function toOutcome(error: unknown): Outcome {
  if (error instanceof ApiError && error.status === 400)
    return { kind: "invalid" }
  if (error instanceof ApiError && error.status === 410) return { kind: "gone" }
  return { kind: "error", message: errorMessage("verify-email", error) }
}

// Câu của 400 và 410 nằm trong bảng `messages.ts` (Đ-E6) — token sai dạng bị chặn ở client cũng phải
// hiện ĐÚNG câu của 400, nên hỏi bảng bằng một ApiError 400 không body.
const INVALID_MESSAGE = errorMessage("verify-email", new ApiError(400, null))
const GONE_MESSAGE = errorMessage("verify-email", new ApiError(410, null))

const LINK = "font-medium text-foreground underline underline-offset-4"

export function VerifyEmail() {
  const router = useRouter()
  const token = useSearchParams().get("token")
  const [outcome, setOutcome] = useState<Outcome | null>(null)
  // Tăng để chạy lại effect khi bấm "Thử lại" sau lỗi tạm thời (verifyOnce đã bỏ promise hỏng).
  const [attempt, setAttempt] = useState(0)

  useEffect(() => {
    // Sai dạng → trạng thái 400 ngay khi render, KHÔNG gọi API (Đ-E5).
    if (!isVerifyToken(token)) return

    // StrictMode: effect lần một bị dọn trước khi promise xong → chỉ lần hai cập nhật state và điều hướng.
    let ignore = false
    verifyOnce(token).then(
      (res) => {
        if (ignore) return
        setOutcome({ kind: "verified", email: res.email })
        // Đ-E10: token không nằm lại trong lịch sử trình duyệt.
        router.replace("/verify-email")
      },
      (error: unknown) => {
        if (ignore) return
        const next = toOutcome(error)
        setOutcome(next)
        // Lỗi tạm thời (429/500/mất mạng) GIỮ token trên URL — xóa đi thì "Thử lại" hay tải lại trang
        // đều không còn gì để gửi.
        if (next.kind !== "error") router.replace("/verify-email")
      }
    )
    return () => {
      ignore = true
    }
  }, [token, attempt, router])

  if (outcome === null && !isVerifyToken(token)) {
    return <Invalid />
  }

  if (outcome === null) {
    return (
      <p
        role="status"
        className="flex items-center gap-2 text-sm text-muted-foreground"
      >
        <Spinner aria-hidden />
        Đang xác minh email…
      </p>
    )
  }

  switch (outcome.kind) {
    case "verified":
      return (
        <div className="flex flex-col gap-6 text-sm">
          <p role="status">
            Email <span className="font-medium break-all">{outcome.email}</span>{" "}
            đã được xác minh.
          </p>
          {/* Điều hướng thì phải là LINK thật, mang style nút của kit. `<Button render={<Link/>}>` của Base UI
              dán ngữ nghĩa nút lên thẻ <a> (trình đọc màn hình đọc là "button") và cảnh báo `nativeButton`. */}
          <Link href="/login" className={buttonVariants()}>
            Đăng nhập
          </Link>
        </div>
      )
    case "invalid":
      return <Invalid />
    case "gone":
      return (
        <div className="flex flex-col gap-6 text-sm">
          <p role="alert">{GONE_MESSAGE}</p>
          <Link href="/login" className={buttonVariants()}>
            Đăng nhập
          </Link>
          <p className="text-center text-muted-foreground">
            Chưa có tài khoản?{" "}
            <Link href="/register" className={LINK}>
              Đăng ký
            </Link>
          </p>
        </div>
      )
    case "error":
      return (
        <div className="flex flex-col gap-6">
          <FormAlert message={outcome.message} />
          <Button
            variant="outline"
            onClick={() => {
              setOutcome(null)
              setAttempt((n) => n + 1)
            }}
          >
            Thử lại
          </Button>
        </div>
      )
  }
}

function Invalid() {
  return (
    <div className="flex flex-col gap-6 text-sm">
      <p role="alert">{INVALID_MESSAGE}</p>
      <p className="text-muted-foreground">
        Chưa có tài khoản?{" "}
        <Link href="/register" className={LINK}>
          Đăng ký
        </Link>
      </p>
    </div>
  )
}
