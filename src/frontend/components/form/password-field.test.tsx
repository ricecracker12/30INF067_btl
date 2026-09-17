import { render, screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { describe, expect, it } from "vitest"

import { PasswordField } from "./password-field"

describe("PasswordField", () => {
  it("mặc định ẩn; nút Hiện mật khẩu đổi type + aria-pressed và không gửi form", async () => {
    const user = userEvent.setup()
    let submitted = 0
    render(
      <form
        onSubmit={(e) => {
          e.preventDefault()
          submitted += 1
        }}
      >
        <PasswordField label="Mật khẩu" error="Mật khẩu là bắt buộc." />
      </form>
    )

    const input = screen.getByLabelText("Mật khẩu")
    const toggle = screen.getByRole("button", { name: "Hiện mật khẩu" })
    expect(input).toHaveAttribute("type", "password")
    expect(input).toHaveAttribute("aria-invalid", "true")
    expect(input).toHaveAccessibleDescription("Mật khẩu là bắt buộc.")
    expect(toggle).toHaveAttribute("aria-pressed", "false")

    await user.click(toggle)
    expect(input).toHaveAttribute("type", "text")
    expect(toggle).toHaveAttribute("aria-pressed", "true")

    await user.click(toggle)
    expect(input).toHaveAttribute("type", "password")
    expect(submitted).toBe(0)
  })
})
