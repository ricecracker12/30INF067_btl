"use client"

import { useId, type ComponentProps } from "react"

import {
  Field,
  FieldDescription,
  FieldError,
  FieldLabel,
} from "@/components/ui/field"
import { Textarea } from "@/components/ui/textarea"

type Props = Omit<ComponentProps<typeof Textarea>, "id"> & {
  label: string
  description?: string
  error?: string
}

// Bản nhiều dòng của `TextField` (GĐ6): mô tả báo cáo, ghi chú quyết định, lý do khóa — ba màn, một cách hiện nhãn / lỗi / mô tả
// (Đ-E12 mục 3). `aria-describedby` nối tay như `TextField` — `FieldDescription`, `FieldError` của kit không tự sinh id.
export function TextareaField({
  label,
  description,
  error,
  ...textareaProps
}: Props) {
  const id = useId()
  const descriptionId = `${id}-description`
  const errorId = `${id}-error`
  const describedBy =
    [description ? descriptionId : null, error ? errorId : null]
      .filter(Boolean)
      .join(" ") || undefined

  return (
    <Field data-invalid={error ? true : undefined}>
      <FieldLabel htmlFor={id}>{label}</FieldLabel>
      <Textarea
        id={id}
        aria-invalid={error ? true : undefined}
        {...textareaProps}
        aria-describedby={describedBy}
      />
      {description && (
        <FieldDescription id={descriptionId}>{description}</FieldDescription>
      )}
      {error && <FieldError id={errorId}>{error}</FieldError>}
    </Field>
  )
}
