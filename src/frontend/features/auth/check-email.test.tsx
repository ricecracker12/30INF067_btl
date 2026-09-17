import { act, render, screen } from "@testing-library/react"
import { renderToString } from "react-dom/server"
import { afterEach, describe, expect, it } from "vitest"

import { CheckEmail } from "./check-email"
import { pendingEmailStore } from "./pending-email"

afterEach(() => {
  pendingEmailStore.set(null)
})

describe("CheckEmail", () => {
  it("có email trong bộ nhớ: nêu đúng địa chỉ, hạn 24 giờ, nhắc thư mục spam", () => {
    pendingEmailStore.set("an@example.com")
    render(<CheckEmail />)

    expect(
      screen.getByText(/Chúng tôi đã gửi liên kết xác minh tới/)
    ).toHaveTextContent("an@example.com")
    expect(screen.getByText(/24 giờ/)).toHaveTextContent(/spam/)
  })

  it("tải lại trang (bộ nhớ trống): câu chung, không có email", () => {
    render(<CheckEmail />)
    expect(
      screen.getByText(
        "Chúng tôi đã gửi liên kết xác minh tới email bạn vừa đăng ký."
      )
    ).toBeInTheDocument()
  })

  it("HTML phía server không chứa email (hydrate không lệch); store đổi thì render lại", () => {
    pendingEmailStore.set("an@example.com")
    expect(renderToString(<CheckEmail />)).not.toContain("an@example.com")

    pendingEmailStore.set(null)
    render(<CheckEmail />)
    act(() => pendingEmailStore.set("binh@example.com"))
    expect(screen.getByText("binh@example.com")).toBeInTheDocument()
  })
})
