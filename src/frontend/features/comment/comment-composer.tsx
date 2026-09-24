"use client"

import { useId, useState, type FormEvent } from "react"

import { FormAlert } from "@/components/form/form-alert"
import { Button } from "@/components/ui/button"
import { Field, FieldError, FieldLabel } from "@/components/ui/field"
import { Spinner } from "@/components/ui/spinner"
import { Textarea } from "@/components/ui/textarea"
import { contentApi } from "@/lib/api/content-api"
import { errorMessage, validationErrors } from "@/lib/api/messages"
import { ApiError } from "@/lib/api/problem"
import type { CommentResponse } from "@/lib/api/types"
import { MAX_COMMENT_LENGTH, commentBodyError } from "@/lib/validation/comment"

// Ô viết bình luận gốc VÀ ô trả lời tại chỗ — một component, khác nhau ở `parentId` và nhãn. Textarea ráp tại chỗ bằng `Field`
// của kit (cùng tiền lệ `profile-form`: `TextField` bọc `Input`, không bọc `Textarea`).
//
// Lỗi 400 hiện DƯỚI Ô theo key của `errors` (Đ-E5): `body` (rỗng/quá dài) và `parentId` (cha vừa bị xóa, hay đã ở cấp 3) —
// `parentId` không có ô riêng nên hiện ở cùng chỗ, vì người dùng đang đứng ở ô này. Không đoán trước lỗi `parentId`: FE ẩn nút
// Trả lời ở cấp 3 nên thường không chạm tới, nhưng server mới là bên quyết định.

const FIELDS = ["body", "parentId"] as const

type Props = {
  postId: string
  /** Có = đang trả lời bình luận này; vắng = bình luận gốc. */
  parentId?: string
  label: string
  /** Bình luận vừa tạo (201) — nơi gọi chèn vào CUỐI danh sách đang mở, không nạp lại cả bài. */
  onCreated: (comment: CommentResponse) => void
  onCancel?: () => void
  autoFocus?: boolean
}

export function CommentComposer({
  postId,
  parentId,
  label,
  onCreated,
  onCancel,
  autoFocus,
}: Props) {
  const id = useId()
  const [body, setBody] = useState("")
  const [fieldError, setFieldError] = useState<string | null>(null)
  const [formError, setFormError] = useState<string | null>(null)
  const [pending, setPending] = useState(false)

  async function onSubmit(event: FormEvent) {
    event.preventDefault()
    if (pending) return

    const local = commentBodyError(body)
    if (local) {
      setFieldError(local)
      return
    }

    setPending(true)
    setFormError(null)
    try {
      const created = await contentApi.createComment(postId, {
        body,
        parentId: parentId ?? null,
      })
      setBody("")
      setFieldError(null)
      onCreated(created)
    } catch (e) {
      if (e instanceof ApiError && e.status === 400) {
        const { fields, formMessage } = validationErrors(e, FIELDS)
        setFieldError(fields.body ?? fields.parentId ?? null)
        setFormError(formMessage)
      } else {
        setFormError(errorMessage("comment-create", e))
      }
    } finally {
      setPending(false)
    }
  }

  return (
    <form
      className="flex flex-col gap-2"
      onSubmit={(e) => void onSubmit(e)}
      data-testid={parentId ? "reply-composer" : "comment-composer"}
    >
      <FormAlert message={formError} />
      <Field data-invalid={fieldError ? true : undefined}>
        <FieldLabel htmlFor={`${id}-body`} className="sr-only">
          {label}
        </FieldLabel>
        <Textarea
          id={`${id}-body`}
          name="body"
          rows={2}
          value={body}
          placeholder={label}
          autoFocus={autoFocus}
          aria-invalid={fieldError ? true : undefined}
          aria-describedby={fieldError ? `${id}-error` : undefined}
          onChange={(e) => {
            setBody(e.target.value)
            if (fieldError) setFieldError(null)
          }}
        />
        {fieldError && <FieldError id={`${id}-error`}>{fieldError}</FieldError>}
      </Field>
      <div className="flex items-center justify-end gap-2">
        {/* Đếm `.length` như server (Đ-3.14) — chỉ hiện khi gần chạm trần, không làm rối ô trả lời ngắn. */}
        {body.length > MAX_COMMENT_LENGTH - 100 && (
          <span className="mr-auto text-xs text-muted-foreground">
            {body.length}/{MAX_COMMENT_LENGTH}
          </span>
        )}
        {onCancel && (
          <Button type="button" variant="ghost" size="sm" onClick={onCancel}>
            Hủy
          </Button>
        )}
        <Button
          type="submit"
          size="sm"
          disabled={pending}
          aria-busy={pending || undefined}
        >
          {pending && <Spinner data-icon="inline-start" aria-hidden />}
          {parentId ? "Trả lời" : "Bình luận"}
        </Button>
      </div>
    </form>
  )
}
