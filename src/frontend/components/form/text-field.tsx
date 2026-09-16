"use client"

import { useId, type ComponentProps } from "react"

import {
  Field,
  FieldDescription,
  FieldError,
  FieldLabel,
} from "@/components/ui/field"
import { Input } from "@/components/ui/input"

type Props = Omit<ComponentProps<typeof Input>, "id"> & {
  label: string
  description?: string
  error?: string
}

// Composite dùng chung cho E3–E5 (Đ-E12 mục 3): mỗi màn KHÔNG tự ráp Field + Input,
// nếu không mỗi màn một cách hiển thị nhãn / lỗi / mô tả.
// `FieldDescription` và `FieldError` của kit không tự sinh id, nên aria-describedby
// phải nối tay ở đây — xem E1 bước 4.
export function TextField({ label, description, error, ...inputProps }: Props) {
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
      <Input
        id={id}
        aria-invalid={error ? true : undefined}
        {...inputProps}
        aria-describedby={describedBy}
      />
      {description && (
        <FieldDescription id={descriptionId}>{description}</FieldDescription>
      )}
      {error && <FieldError id={errorId} errors={[{ message: error }]} />}
    </Field>
  )
}
