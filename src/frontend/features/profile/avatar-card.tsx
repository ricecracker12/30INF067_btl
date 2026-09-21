"use client"

import { useRef, useState, type ChangeEvent } from "react"

import { FormAlert } from "@/components/form/form-alert"
import { Avatar, AvatarFallback, AvatarImage } from "@/components/ui/avatar"
import { Button } from "@/components/ui/button"
import { Field, FieldError, FieldLabel } from "@/components/ui/field"
import { Separator } from "@/components/ui/separator"
import { Spinner } from "@/components/ui/spinner"
import { errorMessage } from "@/lib/api/messages"
import { profileApi } from "@/lib/api/profile-api"
import type { ProfileResponse } from "@/lib/api/types"
import { IMAGE_ACCEPT_ATTRIBUTE, imageFileError } from "@/lib/validation/media"

import { loadProfile } from "./load-profile"
import { profileStore } from "./profile-store"
import { uploadAvatar } from "./upload-avatar"
import { useProfile } from "./use-profile"

/**
 * Ảnh đại diện trên `/me` (E3). Chạy luồng ba bước của `upload-avatar.ts` trên MỘT ảnh — nơi mọi thứ còn
 * nhỏ và đọc được, trước khi E4 chạy nó với mười ảnh, tiến trình và hàng đợi.
 *
 * Hai chỗ hiện lỗi, cố ý tách:
 * - dưới ô chọn ảnh: lỗi của CHÍNH FILE (sai loại, quá nặng) — chặn tại client, không gọi API nào;
 * - `FormAlert`: lỗi của một trong ba BƯỚC, mỗi bước một câu (Mục 4 bảng ba trạng thái).
 */
export function AvatarCard() {
  const { profile } = useProfile()

  // Nằm dưới `(with-profile)` nên tới đây chắc chắn đã có hồ sơ. Tách vỏ này khỏi phần thân KHÔNG phải
  // cho gọn: `profile` narrow ở component ngoài không theo được vào trong các closure async bên dưới, và
  // cách duy nhất để không phải ép kiểu ở đó là nhận nó qua prop đã hết `null`.
  if (!profile) return null
  return <AvatarEditor profile={profile} />
}

function AvatarEditor({ profile }: { profile: ProfileResponse }) {
  const [fileError, setFileError] = useState<string | undefined>(undefined)
  const [stepError, setStepError] = useState<string | null>(null)
  const [pending, setPending] = useState(false)

  async function onPick(event: ChangeEvent<HTMLInputElement>) {
    const file = event.target.files?.[0]
    // Chọn file rồi bấm Hủy trong hộp thoại: `files` rỗng, không phải lỗi.
    if (!file) return

    // Cho chọn LẠI ĐÚNG FILE VỪA CHỌN sau khi hỏng: `input type=file` không bắn `change` khi giá trị
    // không đổi, nên không xóa thì nút "thử lại" duy nhất của người dùng là chọn một file khác.
    event.target.value = ""

    setStepError(null)
    const invalid = imageFileError(file)
    setFileError(invalid)
    // Sai loại hoặc quá nặng thì KHÔNG gọi API nào — ba bước đều chắc chắn hỏng, và bước 1 tốn hạn mức.
    if (invalid) return

    setPending(true)
    try {
      profileStore.setReady(await uploadAvatar(file))
    } catch (error) {
      // Mỗi bước một câu; `error.step` (presign | put | attach) có sẵn trong `AvatarUploadError` cho
      // người sửa đọc log, còn người dùng chỉ cần câu.
      setStepError(
        error instanceof Error ? error.message : errorMessage("avatar", error)
      )
    } finally {
      setPending(false)
    }
  }

  async function onRemove() {
    if (pending) return
    setFileError(undefined)
    setStepError(null)
    setPending(true)
    try {
      await profileApi.removeAvatar()
      // 204 không mang hồ sơ về — server chỉ gỡ liên kết (`avatar_key = NULL`, Đ-2.10), nên cập nhật tại
      // chỗ thay vì gọi lại `GET`. Idempotent: bấm lần hai vẫn 204, và lần hai không có gì để đổi.
      profileStore.setReady({ ...profile, avatarUrl: null })
    } catch (error) {
      setStepError(errorMessage("avatar", error))
    } finally {
      setPending(false)
    }
  }

  return (
    <section className="flex flex-col gap-6" data-testid="avatar-card">
      <h2 className="text-lg font-medium">Ảnh đại diện</h2>
      <FormAlert message={stepError} />

      <div className="flex items-center gap-6">
        <AvatarPreview
          url={profile.avatarUrl}
          displayName={profile.displayName}
        />
        <div className="flex flex-col gap-3">
          <Field data-invalid={fileError ? true : undefined}>
            <FieldLabel htmlFor="avatar-file">Chọn ảnh mới</FieldLabel>
            <input
              id="avatar-file"
              name="avatar"
              type="file"
              // Chỉ là gợi ý cho hộp thoại của hệ điều hành — `imageFileError` mới là chỗ chặn thật.
              accept={IMAGE_ACCEPT_ATTRIBUTE}
              disabled={pending}
              aria-invalid={fileError ? true : undefined}
              aria-describedby={fileError ? "avatar-file-error" : undefined}
              onChange={(e) => void onPick(e)}
              className="text-sm file:mr-4 file:rounded-md file:border file:border-border file:bg-secondary file:px-3 file:py-1.5 file:text-sm file:text-secondary-foreground"
            />
            {fileError && (
              <FieldError id="avatar-file-error">{fileError}</FieldError>
            )}
          </Field>

          <div className="flex items-center gap-3">
            {pending && (
              <p
                role="status"
                className="flex items-center gap-2 text-sm text-muted-foreground"
              >
                <Spinner aria-hidden />
                Đang tải ảnh lên…
              </p>
            )}
            {profile.avatarUrl && !pending && (
              <Button
                variant="outline"
                size="sm"
                onClick={() => void onRemove()}
              >
                Gỡ ảnh đại diện
              </Button>
            )}
          </div>
        </div>
      </div>
      <Separator />
    </section>
  )
}

/**
 * `avatarUrl` là DỮ LIỆU CÓ HẠN: presigned GET 15 phút (Đ-2.9). Tab mở lâu hơn thế thì ảnh vỡ mà không
 * có lỗi nào trong log — trình duyệt chỉ báo `error` trên chính thẻ `<img>`.
 *
 * `keepMounted` để thẻ `<img>` thật nằm trong DOM và chính sự kiện `error` của nó là nguồn tin (mặc định
 * Base UI nạp trước bằng `new Image()` — không có phần tử nào để nghe). Gặp `error` thì nạp lại hồ sơ
 * ĐÚNG MỘT LẦN cho mỗi lần mở màn: lần đầu là URL hết hạn (nạp lại được URL mới), lần sau là R2 hỏng
 * thật — nạp lại nữa chỉ là vòng lặp request.
 */
function AvatarPreview({
  url,
  displayName,
}: {
  /** `avatarUrl` là field KHÔNG bắt buộc của hợp đồng: `undefined` (server không gửi) và `null` (chưa đặt
   * avatar) là cùng một chuyện với màn này. */
  url: string | null | undefined
  displayName: string
}) {
  const refreshed = useRef(false)

  return (
    <Avatar size="lg" className="size-20">
      {url && (
        <AvatarImage
          src={url}
          alt={`Ảnh đại diện của ${displayName}`}
          keepMounted
          data-testid="avatar-image"
          onLoadingStatusChange={(status) => {
            if (status !== "error" || refreshed.current) return
            refreshed.current = true
            void loadProfile()
          }}
        />
      )}
      {/* Chữ cái đầu khi chưa có ảnh, và trong lúc ảnh chưa nạp xong. */}
      <AvatarFallback className="text-xl">
        {displayName.trim().charAt(0).toUpperCase()}
      </AvatarFallback>
    </Avatar>
  )
}
