import { render, screen } from "@testing-library/react"
import { describe, expect, it } from "vitest"

import { TextField } from "./text-field"

describe("TextField", () => {
  it("có lỗi: nhãn nối đúng input, đánh dấu aria-invalid, lỗi vào phần mô tả trợ năng", () => {
    render(
      <TextField
        label="Email"
        type="email"
        error="Email không đúng định dạng."
      />
    )

    const input = screen.getByLabelText("Email")
    expect(input).toHaveAttribute("type", "email")
    expect(input).toHaveAttribute("aria-invalid", "true")
    expect(screen.getByRole("alert")).toHaveTextContent(
      "Email không đúng định dạng."
    )
    expect(input).toHaveAccessibleDescription(/Email không đúng định dạng/)
  })

  it("không lỗi: không aria-invalid, không có phần tử lỗi", () => {
    render(<TextField label="Email" type="email" />)

    const input = screen.getByLabelText("Email")
    expect(input).not.toHaveAttribute("aria-invalid")
    expect(screen.queryByRole("alert")).not.toBeInTheDocument()
  })

  it("mô tả đi vào phần mô tả trợ năng, kèm cả lỗi khi có cả hai", () => {
    render(
      <TextField
        label="Mật khẩu"
        type="password"
        description="Ít nhất 8 ký tự."
        error="Mật khẩu quá ngắn."
      />
    )

    const input = screen.getByLabelText("Mật khẩu")
    expect(input).toHaveAccessibleDescription(
      "Ít nhất 8 ký tự. Mật khẩu quá ngắn."
    )
  })
})
