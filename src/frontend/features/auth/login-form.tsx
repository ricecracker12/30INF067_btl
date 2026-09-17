"use client"

import { useRouter, useSearchParams } from "next/navigation"
import { useState, type FormEvent } from "react"

import { FormAlert } from "@/components/form/form-alert"
import { TextField } from "@/components/form/text-field"
import { Button } from "@/components/ui/button"
import { Spinner } from "@/components/ui/spinner"
import { authApi } from "@/lib/api/auth-api"
import { errorMessage, validationErrors } from "@/lib/api/messages"
import { ApiError } from "@/lib/api/problem"
import { safeNext } from "@/lib/auth/safe-next"
import { tokenStore } from "@/lib/auth/token-store"
import {
  validateLogin,
  type FieldErrors,
  type LoginField,
} from "@/lib/validation/auth"

const FIELDS = ["email", "password"] as const satisfies readonly LoginField[]

export function LoginForm() {
  const router = useRouter()
  const searchParams = useSearchParams()

  // Mật khẩu nằm trong state của form và ở lại sau lỗi 401 — sửa một ký tự tiện hơn gõ lại. Rời màn
  // là component bị gỡ, state đi theo. Token thì KHÔNG bao giờ vào state: chỉ `tokenStore.startSession`.
  const [email, setEmail] = useState("")
  const [password, setPassword] = useState("")
  const [fieldErrors, setFieldErrors] = useState<FieldErrors<LoginField>>({})
  const [formError, setFormError] = useState<string | null>(null)
  const [pending, setPending] = useState(false)

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    // Chặn gửi đôi — cũng là chặn tốn hạn mức 10 req/phút của /auth/*.
    if (pending) return

    // Xóa lỗi cũ trước: cùng một lỗi hai lần liên tiếp thì FormAlert vẫn thấy thông điệp đổi
    // (null → câu) và chuyển focus lại.
    setFormError(null)

    const clientErrors = validateLogin({ email, password })
    setFieldErrors(clientErrors)
    if (Object.keys(clientErrors).length > 0) return

    setPending(true)
    try {
      const { accessToken } = await authApi.login({ email, password })
      tokenStore.startSession(accessToken)
      router.replace(safeNext(searchParams.get("next")))
      // Không hạ `pending`: đang rời màn, để nút mở lại là mở đường gửi lần hai.
    } catch (error) {
      if (error instanceof ApiError && error.status === 400) {
        const { fields, formMessage } = validationErrors(error, FIELDS)
        setFieldErrors(fields)
        setFormError(formMessage)
      } else {
        setFormError(errorMessage("login", error))
      }
      setPending(false)
    }
  }

  function clearFieldError(field: LoginField) {
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
      <TextField
        label="Mật khẩu"
        name="password"
        type="password"
        autoComplete="current-password"
        value={password}
        onChange={(e) => {
          setPassword(e.target.value)
          clearFieldError("password")
        }}
        error={fieldErrors.password}
      />
      <Button type="submit" disabled={pending} aria-busy={pending || undefined}>
        {pending && <Spinner data-icon="inline-start" aria-hidden />}
        Đăng nhập
      </Button>
    </form>
  )
}
