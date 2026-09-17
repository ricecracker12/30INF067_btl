import { fireEvent, render, screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { http, HttpResponse } from "msw"
import { beforeEach, describe, expect, it, vi } from "vitest"

import { BFF_URL } from "@/lib/api/config"
import type * as T from "@/lib/api/types"
import { server } from "@/mocks/node"

import { pendingEmailStore } from "./pending-email"
import { RegisterForm } from "./register-form"

const push = vi.fn()

vi.mock("next/navigation", () => ({
  useRouter: () => ({ push }),
}))

/** Ghi lại mọi request đi ra, kèm body — để khẳng định "0 request" và email đã trim. */
function recordRequests() {
  const seen: { path: string; body: unknown }[] = []
  server.events.on("request:start", async ({ request }) => {
    seen.push({
      path: new URL(request.url).pathname,
      body: await request.clone().json(),
    })
  })
  return seen
}

/** Giữ response 201 lại tới khi gọi hàm trả về — để thử bấm/gửi thêm trong lúc chờ. */
function holdRegister() {
  let release!: () => void
  const gate = new Promise<void>((r) => (release = r))
  server.use(
    http.post(`${BFF_URL}/auth/register`, async () => {
      await gate
      return HttpResponse.json(
        {
          userId: "0192f3c1-8a4e-7c31-9f2a-6b5d4e3c2a10",
          email: "an@example.com",
        } satisfies T.RegisterResponse,
        { status: 201 }
      )
    })
  )
  return release
}

async function submit(email: string, password = "MatKhau123") {
  const user = userEvent.setup()
  render(<RegisterForm />)
  if (email) await user.type(screen.getByLabelText("Email"), email)
  if (password) await user.type(screen.getByLabelText("Mật khẩu"), password)
  await user.click(screen.getByRole("button", { name: "Đăng ký" }))
  return user
}

beforeEach(() => {
  push.mockReset()
  pendingEmailStore.set(null)
})

describe("RegisterForm — thành công", () => {
  it("201: gửi email đã trim, nhớ email trong bộ nhớ, sang /register/check-email KHÔNG kèm email", async () => {
    const seen = recordRequests()
    // `type="email"` tự bỏ khoảng trắng hai đầu theo chuẩn HTML — `trim()` trong form là lớp dự phòng,
    // bỏ nó thì test này vẫn xanh (đột biến tương đương).
    await submit("  an@example.com  ")

    await waitFor(() =>
      expect(push).toHaveBeenCalledWith("/register/check-email")
    )
    expect(push).toHaveBeenCalledTimes(1)
    expect(pendingEmailStore.get()).toBe("an@example.com")
    expect(seen).toEqual([
      {
        path: "/bff/auth/register",
        body: { email: "an@example.com", password: "MatKhau123" },
      },
    ])
  })

  it("đang chờ: nút disabled, bấm thêm không gửi lần hai (không tự nhận 409)", async () => {
    const release = holdRegister()
    const seen = recordRequests()

    const user = await submit("an@example.com")
    const button = screen.getByRole("button", { name: "Đăng ký" })
    await waitFor(() => expect(button).toBeDisabled())
    await user.click(button)
    release()

    await waitFor(() => expect(push).toHaveBeenCalledTimes(1))
    expect(seen.map((r) => r.path)).toEqual(["/bff/auth/register"])
  })

  it("form bị gửi lần hai KHÔNG qua nút (vd. requestSubmit) trong lúc chờ: vẫn 1 request", async () => {
    const release = holdRegister()
    const seen = recordRequests()

    await submit("an@example.com")
    const button = screen.getByRole("button", { name: "Đăng ký" })
    await waitFor(() => expect(button).toBeDisabled())
    fireEvent.submit(button.closest("form")!)
    release()

    await waitFor(() => expect(push).toHaveBeenCalledTimes(1))
    expect(seen.map((r) => r.path)).toEqual(["/bff/auth/register"])
  })
})

describe("RegisterForm — lỗi theo trường (Đ-E5)", () => {
  it("400 từ server: lỗi dưới đúng trường, không có lỗi cấp form", async () => {
    await submit("loi400@example.com")

    await waitFor(() =>
      expect(screen.getByLabelText("Email")).toHaveAccessibleDescription(
        "Email không đúng định dạng."
      )
    )
    // Mô tả trợ năng = gợi ý của trường + lỗi.
    expect(screen.getByLabelText("Mật khẩu")).toHaveAccessibleDescription(
      /Mật khẩu phải có ít nhất 8 ký tự\.$/
    )
    expect(screen.queryByText(/Dữ liệu không hợp lệ/)).not.toBeInTheDocument()
    expect(push).not.toHaveBeenCalled()
  })

  it("409: dưới trường email, kèm link Đăng nhập; không có lỗi cấp form", async () => {
    await submit("trung@example.com")

    const link = await screen.findByRole("link", { name: "Đăng nhập" })
    expect(link).toHaveAttribute("href", "/login")
    const email = screen.getByLabelText("Email")
    expect(email).toHaveAccessibleDescription(
      "Email này đã được đăng ký. Đăng nhập"
    )
    expect(email).toHaveAttribute("aria-invalid", "true")
    expect(document.querySelector('[data-slot="alert"]')).toBeNull()
    expect(screen.getByRole("button", { name: "Đăng ký" })).toBeEnabled()
    expect(pendingEmailStore.get()).toBeNull()
  })

  it.each([
    ["a".repeat(7), /Mật khẩu phải có ít nhất 8 ký tự\.$/],
    ["ệ".repeat(25), /Mật khẩu tối đa 72 byte\.$/],
  ])("mật khẩu %j: lỗi client, 0 request", async (password, message) => {
    const seen = recordRequests()
    await submit("an@example.com", password)

    expect(screen.getByLabelText("Mật khẩu")).toHaveAccessibleDescription(
      message
    )
    expect(seen).toEqual([])
  })

  it("email chắc chắn sai: lỗi client, 0 request; email lạ mà có thể hợp lệ thì để server nói", async () => {
    const seen = recordRequests()
    const user = await submit("an.example.com")

    expect(screen.getByLabelText("Email")).toHaveAccessibleDescription(
      "Email không đúng định dạng."
    )
    expect(seen).toEqual([])

    await user.clear(screen.getByLabelText("Email"))
    await user.type(screen.getByLabelText("Email"), "o'brien@ví-dụ.vn")
    await user.click(screen.getByRole("button", { name: "Đăng ký" }))
    await waitFor(() => expect(push).toHaveBeenCalledTimes(1))
    expect(seen.map((r) => r.path)).toEqual(["/bff/auth/register"])
  })
})

describe("RegisterForm — lỗi cấp form (bảng E4)", () => {
  it("429: đúng câu, focus vào thông báo", async () => {
    await submit("quanhanh@example.com")
    const alert = await screen.findByRole("alert")
    expect(alert).toHaveTextContent(
      "Bạn thao tác quá nhanh. Vui lòng thử lại sau ít phút."
    )
    expect(alert).toHaveFocus()
  })

  it("500: câu chung kèm traceId của response", async () => {
    let traceId = ""
    server.events.on("response:mocked", async ({ response }) => {
      traceId = ((await response.clone().json()) as { traceId: string }).traceId
    })

    await submit("loi500@example.com")

    const alert = await screen.findByRole("alert")
    expect(traceId).toMatch(/^[0-9a-f]{32}$/)
    expect(alert).toHaveTextContent(
      `Đã xảy ra lỗi không mong muốn. Mã tra cứu: ${traceId}`
    )
  })

  it("mất mạng: không đoán nguyên nhân, không nhớ email", async () => {
    server.use(
      http.post(`${BFF_URL}/auth/register`, () => HttpResponse.error())
    )
    await submit("an@example.com")
    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Không kết nối được máy chủ."
    )
    expect(pendingEmailStore.get()).toBeNull()
    expect(push).not.toHaveBeenCalled()
  })
})
