"use client"

import Link from "next/link"
import { useRouter } from "next/navigation"
import { useState, type FormEvent, type ReactNode } from "react"

import { FormAlert } from "@/components/form/form-alert"
import { PasswordField } from "@/components/form/password-field"
import { TextField } from "@/components/form/text-field"
import { Button } from "@/components/ui/button"
import { Spinner } from "@/components/ui/spinner"
import { authApi } from "@/lib/api/auth-api"
import { errorMessage, validationErrors } from "@/lib/api/messages"
import { ApiError } from "@/lib/api/problem"
import { validateRegister, type RegisterField } from "@/lib/validation/auth"

import { pendingEmailStore } from "./pending-email"

const FIELDS = ["email", "password"] as const satisfies readonly RegisterField[]

type RegisterFieldErrors = Partial<Record<RegisterField, ReactNode>>

export function RegisterForm() {
  const router = useRouter()

  const [email, setEmail] = useState("")
  const [password, setPassword] = useState("")
  const [fieldErrors, setFieldErrors] = useState<RegisterFieldErrors>({})
  const [formError, setFormError] = useState<string | null>(null)
  const [pending, setPending] = useState(false)

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    // Chặn gửi đôi: bấm hai lần thì request thứ hai nhận 409 sau khi request thứ nhất đã 201, màn
    // hiện lỗi cho một tài khoản vừa tạo thành công.
    if (pending) return

    // Xóa lỗi cũ trước — cùng một lỗi hai lần liên tiếp thì FormAlert vẫn chuyển focus lại (xem E4).
    setFormError(null)

    const clientErrors = validateRegister({ email, password })
    setFieldErrors(clientErrors)
    if (Object.keys(clientErrors).length > 0) return

    setPending(true)
    const trimmedEmail = email.trim()
    try {
      await authApi.register({ email: trimmedEmail, password })
      pendingEmailStore.set(trimmedEmail)
      router.push("/register/check-email")
      // Không hạ `pending`: đang rời màn, để nút mở lại là mở đường gửi lần hai.
    } catch (error) {
      if (error instanceof ApiError && error.status === 400) {
        const { fields, formMessage } = validationErrors(error, FIELDS)
        setFieldErrors(fields)
        setFormError(formMessage)
      } else if (error instanceof ApiError && error.status === 409) {
        // Lỗi thuộc về trường email — kèm đường đi tiếp, không chỉ báo hỏng.
        setFieldErrors({
          email: (
            <>
              {errorMessage("register", error)}{" "}
              <Link
                href="/login"
                className="font-medium underline underline-offset-4"
              >
                Đăng nhập
              </Link>
            </>
          ),
        })
      } else {
        setFormError(errorMessage("register", error))
      }
      setPending(false)
    }
  }

  function clearFieldError(field: RegisterField) {
    if (fieldErrors[field]) {
      setFieldErrors((prev) => ({ ...prev, [field]: undefined }))
    }
  }

  return (
    // noValidate: thông điệp lấy từ Đ-E5, không để trình duyệt tự hiện câu tiếng Anh.
    <form noValidate onSubmit={onSubmit} className="flex flex-col gap-6">
      <FormAlert message={formError} />
      <TextField
        label="Email"
        name="email"
        type="email"
        autoComplete="email"
        placeholder="ban@vidu.com"
        value={email}
        onChange={(e) => {
          setEmail(e.target.value)
          clearFieldError("email")
        }}
        error={fieldErrors.email}
      />
      <PasswordField
        label="Mật khẩu"
        name="password"
        autoComplete="new-password"
        value={password}
        onChange={(e) => {
          setPassword(e.target.value)
          clearFieldError("password")
        }}
        error={fieldErrors.password}
      />
      <Button type="submit" disabled={pending} aria-busy={pending || undefined}>
        {pending && <Spinner data-icon="inline-start" aria-hidden />}
        Đăng ký
      </Button>
    </form>
  )
}
