"use client"

import { SendHorizontal } from "lucide-react"
import { useState, type KeyboardEvent } from "react"

import { Button } from "@/components/ui/button"
import { Textarea } from "@/components/ui/textarea"

import { countCharacters, MESSAGE_MAX_LENGTH } from "./message-set"

type Props = {
  /** Gửi; trả lỗi client để hiện dưới ô, hoặc `null` khi đã nhận (ô được xóa). */
  onSend: (content: string) => string | null
  disabled: boolean
}

// Ô soạn (E5). Enter gửi, Shift+Enter xuống dòng. Ngưỡng 2000 ký tự CÙNG server, đếm theo code point (Đ-E5). Gửi là optimistic —
// ô xóa ngay, tin hiện "đang gửi" trong danh sách; lỗi mạng hiện Ở TIN, không ở ô.
export function MessageComposer({ onSend, disabled }: Props) {
  const [text, setText] = useState("")
  const [error, setError] = useState<string | null>(null)
  const length = countCharacters(text)

  const submit = () => {
    if (disabled) return
    const problem = onSend(text)
    if (problem) {
      setError(problem)
      return
    }
    setText("")
    setError(null)
  }

  const onKeyDown = (e: KeyboardEvent<HTMLTextAreaElement>) => {
    // Đang gõ tiếng Việt bằng bộ gõ (IME): Enter xác nhận chữ, không phải gửi.
    if (e.key === "Enter" && !e.shiftKey && !e.nativeEvent.isComposing) {
      e.preventDefault()
      submit()
    }
  }

  return (
    <form
      className="flex flex-col gap-1"
      onSubmit={(e) => {
        e.preventDefault()
        submit()
      }}
    >
      <div className="flex items-end gap-2">
        <Textarea
          aria-label="Nội dung tin nhắn"
          placeholder={disabled ? "Không thể gửi tin trong cuộc trò chuyện này" : "Nhập tin nhắn…"}
          value={text}
          onChange={(e) => {
            setText(e.target.value)
            if (error) setError(null)
          }}
          onKeyDown={onKeyDown}
          disabled={disabled}
          rows={2}
          className="min-h-0 resize-none"
          aria-invalid={error !== null}
        />
        <Button type="submit" size="icon" disabled={disabled} aria-label="Gửi">
          <SendHorizontal />
        </Button>
      </div>
      <div className="flex justify-between text-xs">
        <span role="alert" className="text-destructive">
          {error}
        </span>
        {length > MESSAGE_MAX_LENGTH - 200 && (
          <span className={length > MESSAGE_MAX_LENGTH ? "text-destructive" : "text-muted-foreground"}>
            {length}/{MESSAGE_MAX_LENGTH}
          </span>
        )}
      </div>
    </form>
  )
}
