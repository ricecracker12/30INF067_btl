"use client"

import { useState, type FormEvent } from "react"

import { FormAlert } from "@/components/form/form-alert"
import { Button } from "@/components/ui/button"
import { Card, CardContent } from "@/components/ui/card"
import {
  Field,
  FieldContent,
  FieldError,
  FieldLabel,
  FieldTitle,
  FieldDescription,
} from "@/components/ui/field"
import { RadioGroup, RadioGroupItem } from "@/components/ui/radio-group"
import { Spinner } from "@/components/ui/spinner"
import { Textarea } from "@/components/ui/textarea"
import { contentApi } from "@/lib/api/content-api"
import { errorMessage, validationErrors } from "@/lib/api/messages"
import { ApiError } from "@/lib/api/problem"
import type {
  PostPrivacy,
  PostResponse,
  UpdatePostRequest,
} from "@/lib/api/types"
import type { FieldErrors } from "@/lib/validation/auth"
import { MAX_BODY_LENGTH, type PostField } from "@/lib/validation/post"

import { PRIVACY_DESCRIPTION, PRIVACY_LABEL, PRIVACY_ORDER } from "./privacy"

// Sửa bài (E6 bước 2). Hai trường, và chỉ hai: `UpdatePostRequest` KHÔNG có `mediaKeys` — GĐ2 không sửa
// ảnh (Mục 7.3), và gửi trường đó vào là 400 field lạ (`UnmappedMemberHandling.Disallow`). Vì vậy màn này
// cũng KHÔNG có nút thêm/bớt ảnh: một nút luôn báo lỗi thì thà đừng có.

const FIELDS = ["body", "privacy"] as const satisfies readonly PostField[]

type Props = {
  post: PostResponse
  onSaved: (next: PostResponse) => void
  onCancel: () => void
}

/**
 * Thân request, chỉ chứa **trường đã đổi**.
 *
 * Gửi cả hai trường dù chỉ đổi một thì không sai hợp đồng, nhưng `edited_at` bị đóng dấu cho một thay đổi
 * không tồn tại — và nhãn "đã chỉnh sửa" là thứ người khác đọc được.
 *
 * **`body: null` nghĩa là "không gửi"**, không phải "xóa chữ": `System.Text.Json` không phân biệt vắng
 * mặt với `null` cho `string?`. Muốn xóa chữ thì gửi `""`, và khi đó BR-01 quyết định — bài có ảnh thì
 * được, bài chỉ có chữ thì 400 `errors.body`. Vì vậy hàm này không bao giờ đặt `body: null`: hoặc có
 * chuỗi (kể cả chuỗi rỗng), hoặc không có khóa `body`.
 */
export function buildPatch(
  post: PostResponse,
  next: { body: string; privacy: PostPrivacy }
): UpdatePostRequest {
  const patch: UpdatePostRequest = {}
  // So với giá trị BAN ĐẦU, không so với rỗng. `post.body` có thể là `null` (bài chỉ có ảnh) — quy về `""`
  // vì đó là thứ ô soạn chữ hiển thị, và so hai thứ khác kiểu là luôn "đã đổi".
  if (next.body !== (post.body ?? "")) patch.body = next.body
  if (next.privacy !== post.privacy) patch.privacy = next.privacy
  return patch
}

export function PostEditForm({ post, onSaved, onCancel }: Props) {
  const [body, setBody] = useState(post.body ?? "")
  const [privacy, setPrivacy] = useState<PostPrivacy>(post.privacy)
  const [fieldErrors, setFieldErrors] = useState<FieldErrors<PostField>>({})
  const [formError, setFormError] = useState<string | null>(null)
  const [pending, setPending] = useState(false)

  const patch = buildPatch(post, { body, privacy })
  const daDoi = Object.keys(patch).length > 0

  async function onSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (pending) return
    // Nút đã tắt khi chưa đổi gì; chặn lần nữa ở đây vì `PATCH {}` là 400 "Không có gì để sửa." — một lỗi
    // người dùng không hiểu, vì họ có bấm gì đâu.
    if (!daDoi) return

    setFormError(null)
    setPending(true)
    try {
      onSaved(await contentApi.updatePost(post.postId, patch))
    } catch (error) {
      // Mọi 400 hiển thị theo key của `errors` (Đ-E5): server có những câu client không có — "Không có gì
      // để sửa.", và BR-01 với `media_count` THẬT của bài (xóa hết chữ của bài không ảnh).
      if (error instanceof ApiError && error.status === 400) {
        const { fields, formMessage } = validationErrors(error, FIELDS)
        setFieldErrors(fields)
        setFormError(formMessage)
      } else {
        // 403 = bài của người khác HOẶC bài đã xóa mềm. Một câu cho cả hai — nói khác đi là tiết lộ bài
        // có tồn tại (Mục 12 nhóm Bảo mật).
        setFormError(errorMessage("post-write", error))
      }
      setPending(false)
    }
  }

  return (
    <Card data-testid="post-edit-form">
      <CardContent>
        <form noValidate onSubmit={onSubmit} className="flex flex-col gap-6">
          <FormAlert message={formError} />

          <Field data-invalid={fieldErrors.body ? true : undefined}>
            <FieldLabel htmlFor={`edit-body-${post.postId}`}>
              Nội dung
            </FieldLabel>
            <Textarea
              id={`edit-body-${post.postId}`}
              name="body"
              rows={5}
              value={body}
              aria-invalid={fieldErrors.body ? true : undefined}
              aria-describedby={
                fieldErrors.body ? `edit-body-error-${post.postId}` : undefined
              }
              onChange={(e) => {
                setBody(e.target.value)
                if (fieldErrors.body)
                  setFieldErrors((prev) => ({ ...prev, body: undefined }))
              }}
            />
            <p className="text-sm text-muted-foreground">
              {body.length}/{MAX_BODY_LENGTH}
            </p>
            {/* Bài có ảnh thì xóa hết chữ là hợp lệ; bài chỉ có chữ thì server trả 400 và câu đó hiện ở
                đây. FE KHÔNG tự đoán: `media_count` thật là của server, và client đoán sai thì chặn nhầm. */}
            {post.media.length > 0 && body.trim() === "" && (
              <FieldDescription>
                Bỏ trống nội dung thì bài chỉ còn ảnh.
              </FieldDescription>
            )}
            {fieldErrors.body && (
              <FieldError id={`edit-body-error-${post.postId}`}>
                {fieldErrors.body}
              </FieldError>
            )}
          </Field>

          <Field data-invalid={fieldErrors.privacy ? true : undefined}>
            <FieldTitle id={`edit-privacy-label-${post.postId}`}>
              Mức riêng tư
            </FieldTitle>
            <RadioGroup
              value={privacy}
              aria-labelledby={`edit-privacy-label-${post.postId}`}
              onValueChange={(value) => setPrivacy(value as PostPrivacy)}
            >
              {PRIVACY_ORDER.map((value) => (
                <FieldLabel
                  key={value}
                  htmlFor={`edit-privacy-${post.postId}-${value}`}
                >
                  <Field orientation="horizontal">
                    <FieldContent>
                      <FieldTitle>{PRIVACY_LABEL[value]}</FieldTitle>
                      <FieldDescription>
                        {PRIVACY_DESCRIPTION[value]}
                      </FieldDescription>
                    </FieldContent>
                    <RadioGroupItem
                      id={`edit-privacy-${post.postId}-${value}`}
                      value={value}
                      disabled={pending}
                    />
                  </Field>
                </FieldLabel>
              ))}
            </RadioGroup>
            {fieldErrors.privacy && (
              <FieldError>{fieldErrors.privacy}</FieldError>
            )}
          </Field>

          {/* GĐ2 không sửa ảnh (Mục 7.3) — nói thẳng ra, để người dùng không đi tìm nút không có. */}
          {post.media.length > 0 && (
            <p className="text-sm text-muted-foreground">
              Không sửa được ảnh của bài đã đăng.
            </p>
          )}

          <div className="flex gap-3">
            <Button
              type="submit"
              // Tắt khi CHƯA ĐỔI GÌ: `PATCH {}` là 400 "Không có gì để sửa.", và đó là lỗi của một thao
              // tác người dùng không hề làm.
              disabled={pending || !daDoi}
              aria-busy={pending || undefined}
            >
              {pending && <Spinner data-icon="inline-start" aria-hidden />}
              Lưu
            </Button>
            <Button
              type="button"
              variant="ghost"
              disabled={pending}
              onClick={onCancel}
            >
              Hủy
            </Button>
          </div>
        </form>
      </CardContent>
    </Card>
  )
}
