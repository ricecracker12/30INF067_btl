"use client"

import { useId, type ComponentProps, type ReactNode } from "react"

import {
  Field,
  FieldDescription,
  FieldError,
  FieldLabel,
} from "@/components/ui/field"
import { Input } from "@/components/ui/input"
import {
  InputGroup,
  InputGroupAddon,
  InputGroupInput,
} from "@/components/ui/input-group"

type Props = Omit<ComponentProps<typeof Input>, "id"> & {
  label: string
  description?: string
  /** Chuỗi, hoặc nội dung có link (409 đăng ký: câu lỗi + link "Đăng nhập"). */
  error?: ReactNode
  /** Nút/biểu tượng gắn cuối ô nhập (vd. "Hiện mật khẩu") — có thì ô nhập nằm trong `InputGroup`. */
  inputEnd?: ReactNode
}

// Composite dùng chung cho E3–E5 (Đ-E12 mục 3): mỗi màn KHÔNG tự ráp Field + Input,
// nếu không mỗi màn một cách hiển thị nhãn / lỗi / mô tả.
// `FieldDescription` và `FieldError` của kit không tự sinh id, nên aria-describedby
// phải nối tay ở đây — xem E1 bước 4.
export function TextField({
  label,
  description,
  error,
  inputEnd,
  ...inputProps
}: Props) {
  const id = useId()
  const descriptionId = `${id}-description`
  const errorId = `${id}-error`
  const describedBy =
    [description ? descriptionId : null, error ? errorId : null]
      .filter(Boolean)
      .join(" ") || undefined

  const controlProps = {
    id,
    "aria-invalid": error ? true : undefined,
    ...inputProps,
    "aria-describedby": describedBy,
  }

  return (
    <Field data-invalid={error ? true : undefined}>
      <FieldLabel htmlFor={id}>{label}</FieldLabel>
      {inputEnd ? (
        <InputGroup>
          <InputGroupInput {...controlProps} />
          <InputGroupAddon align="inline-end">{inputEnd}</InputGroupAddon>
        </InputGroup>
      ) : (
        <Input {...controlProps} />
      )}
      {description && (
        <FieldDescription id={descriptionId}>{description}</FieldDescription>
      )}
      {error && <FieldError id={errorId}>{error}</FieldError>}
    </Field>
  )
}
