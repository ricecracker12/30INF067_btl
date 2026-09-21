"use client"

import { useRouter, useSearchParams } from "next/navigation"
import { useState, type FormEvent } from "react"

import { FormAlert } from "@/components/form/form-alert"
import { TextField } from "@/components/form/text-field"
import { Button } from "@/components/ui/button"
import { Field, FieldError, FieldLabel } from "@/components/ui/field"
import { Spinner } from "@/components/ui/spinner"
import { errorMessage, validationErrors } from "@/lib/api/messages"
import { ApiError } from "@/lib/api/problem"
import { profileApi } from "@/lib/api/profile-api"
import type { ProfileResponse } from "@/lib/api/types"
import { Textarea } from "@/components/ui/textarea"
import { safeNext } from "@/lib/auth/safe-next"
import type { FieldErrors } from "@/lib/validation/auth"
import {
  BIO_MAX_LENGTH,
  validateProfile,
  bioToSend,
  type ProfileField,
} from "@/lib/validation/profile"

import { profileStore } from "./profile-store"

const FIELDS = ["displayName", "bio"] as const satisfies readonly ProfileField[]

type Props = {
  /**
   * Hồ sơ đang có (màn sửa ở `/me`), hoặc `null` (onboarding). Khác nhau ở giá trị ban đầu, nhãn nút và
   * chỗ đi sau khi lưu — phần còn lại giống hệt, nên MỘT component (E2 bước 6).
   */
  initial: ProfileResponse | null
  submitLabel: string
  /** Onboarding thì rời màn; sửa hồ sơ thì ở lại `/me` và báo đã lưu. */
  onSaved?: (profile: ProfileResponse) => void
}

export function ProfileForm({ initial, submitLabel, onSaved }: Props) {
  const router = useRouter()
  const searchParams = useSearchParams()

  const [displayName, setDisplayName] = useState(initial?.displayName ?? "")
  const [bio, setBio] = useState(initial?.bio ?? "")
  const [fieldErrors, setFieldErrors] = useState<FieldErrors<ProfileField>>({})
  const [formError, setFormError] = useState<string | null>(null)
  const [pending, setPending] = useState(false)

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    // Chặn gửi đôi — cũng là chặn tốn hạn mức 100 req/phút.
    if (pending) return

    // Xóa lỗi cũ trước: cùng một lỗi hai lần liên tiếp thì FormAlert vẫn thấy thông điệp đổi và chuyển focus lại.
    setFormError(null)

    const clientErrors = validateProfile({ displayName, bio })
    setFieldErrors(clientErrors)
    if (Object.keys(clientErrors).length > 0) return

    setPending(true)
    try {
      // LUÔN gửi `bio`: `PUT` là thay thế toàn phần (Q-D3). Bỏ field ra khi ô trống thì bio cũ bị xóa mà
      // không ai định thế — bỏ trống ô ĐÚNG LÀ ý muốn xóa, nhưng phải nói ra bằng `null`, không bằng im lặng.
      const saved = await profileApi.upsert({
        displayName: displayName.trim(),
        bio: bioToSend(bio),
      })
      profileStore.setReady(saved)
      if (onSaved) {
        onSaved(saved)
        setPending(false)
      } else {
        router.replace(safeNext(searchParams.get("next")))
        // Không hạ `pending`: đang rời màn, mở lại nút là mở đường gửi lần hai.
      }
    } catch (error) {
      // Mọi 400 hiển thị theo key của `errors` — KỂ CẢ khi client đã kiểm qua (Đ-E5): server là bên quyết định.
      if (error instanceof ApiError && error.status === 400) {
        const { fields, formMessage } = validationErrors(error, FIELDS)
        setFieldErrors(fields)
        setFormError(formMessage)
      } else {
        setFormError(errorMessage("profile-write", error))
      }
      setPending(false)
    }
  }

  function clearFieldError(field: ProfileField) {
    if (fieldErrors[field]) {
      setFieldErrors((prev) => ({ ...prev, [field]: undefined }))
    }
  }

  return (
    // noValidate: thông điệp lấy từ Đ-E5, không để trình duyệt tự hiện câu tiếng Anh.
    <form noValidate onSubmit={onSubmit} className="flex flex-col gap-6">
      <FormAlert message={formError} />
      <TextField
        label="Tên hiển thị"
        name="displayName"
        autoComplete="nickname"
        placeholder="An Nguyễn"
        value={displayName}
        onChange={(e) => {
          setDisplayName(e.target.value)
          clearFieldError("displayName")
        }}
        error={fieldErrors.displayName}
      />
      <BioField
        value={bio}
        error={fieldErrors.bio}
        onChange={(next) => {
          setBio(next)
          clearFieldError("bio")
        }}
      />
      <Button type="submit" disabled={pending} aria-busy={pending || undefined}>
        {pending && <Spinner data-icon="inline-start" aria-hidden />}
        {submitLabel}
      </Button>
    </form>
  )
}

/**
 * `TextField` bọc `Input`, không bọc `Textarea` — bio là văn bản nhiều dòng. Ráp tại chỗ thay vì thêm một
 * composite mới vào `components/form/`: tới khi có màn thứ hai cần ô nhiều dòng mới biết khuôn chung là gì.
 */
function BioField({
  value,
  error,
  onChange,
}: {
  value: string
  error?: string
  onChange: (next: string) => void
}) {
  return (
    <Field data-invalid={error ? true : undefined}>
      <FieldLabel htmlFor="bio">Giới thiệu</FieldLabel>
      <Textarea
        id="bio"
        name="bio"
        rows={4}
        placeholder="Vài dòng về bạn (không bắt buộc)"
        value={value}
        aria-invalid={error ? true : undefined}
        aria-describedby={error ? "bio-error" : undefined}
        onChange={(e) => onChange(e.target.value)}
      />
      {/* Đếm ngược để người dùng thấy giới hạn TRƯỚC khi bị chặn — không phải một luật mới, chỉ là hiển thị. */}
      <p className="text-sm text-muted-foreground">
        {value.length}/{BIO_MAX_LENGTH}
      </p>
      {error && <FieldError id="bio-error">{error}</FieldError>}
    </Field>
  )
}
