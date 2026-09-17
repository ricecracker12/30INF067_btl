"use client"

import { EyeIcon, EyeOffIcon } from "lucide-react"
import { useState, type ComponentProps } from "react"

import { InputGroupButton } from "@/components/ui/input-group"

import { TextField } from "./text-field"

type Props = Omit<ComponentProps<typeof TextField>, "type" | "inputEnd">

// Ô mật khẩu có nút "Hiện mật khẩu" — thay cho ô "nhập lại mật khẩu" (hợp đồng không có trường đó,
// E3 bước 1): nhìn được cái mình gõ giải quyết cùng vấn đề gõ nhầm mà rẻ hơn một trường nữa.
export function PasswordField(props: Props) {
  const [visible, setVisible] = useState(false)

  return (
    <TextField
      {...props}
      type={visible ? "text" : "password"}
      inputEnd={
        <InputGroupButton
          size="icon-xs"
          aria-label="Hiện mật khẩu"
          aria-pressed={visible}
          onClick={() => setVisible((v) => !v)}
        >
          {visible ? <EyeOffIcon aria-hidden /> : <EyeIcon aria-hidden />}
        </InputGroupButton>
      }
    />
  )
}
