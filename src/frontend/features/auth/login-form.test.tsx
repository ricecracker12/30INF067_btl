import { render, screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { http, HttpResponse } from "msw"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import { BFF_PROBLEM_TYPES } from "@/lib/api/bff-contract"
import { BFF_URL } from "@/lib/api/config"
import { tokenStore } from "@/lib/auth/token-store"
import { server } from "@/mocks/node"

import { LoginForm } from "./login-form"

const replace = vi.fn()
let search = new URLSearchParams()

vi.mock("next/navigation", () => ({
  useRouter: () => ({ replace }),
  useSearchParams: () => search,
}))

/** Mọi request thật sự đi ra — để khẳng định "0 request" và "không gọi /auth/refresh". */
function recordRequests() {
  const seen: string[] = []
  server.events.on("request:start", ({ request }) => {
    seen.push(new URL(request.url).pathname)
  })
  return seen
}

async function submit(email: string, password = "MatKhau123") {
  const user = userEvent.setup()
  render(<LoginForm />)
  if (email) await user.type(screen.getByLabelText("Email"), email)
  if (password) await user.type(screen.getByLabelText("Mật khẩu"), password)
  await user.click(screen.getByRole("button", { name: "Đăng nhập" }))
  return user
}

beforeEach(() => {
  tokenStore.reset()
  replace.mockReset()
  search = new URLSearchParams()
})
afterEach(() => {
  tokenStore.reset()
})

describe("LoginForm — lỗi cấp form (Đ-E6)", () => {
  it("401: đúng MỘT câu, token vẫn null, KHÔNG gọi /auth/refresh, mật khẩu còn nguyên", async () => {
    const seen = recordRequests()
    await submit("sai@example.com")

    const alert = await screen.findByRole("alert")
    expect(alert).toHaveTextContent(/^Email hoặc mật khẩu không đúng\.$/)
    await waitFor(() => expect(alert).toHaveFocus())
    expect(tokenStore.getSession().status).not.toBe("authenticated")
    expect(seen).toEqual(["/bff/auth/login"])
    expect(screen.getByLabelText("Mật khẩu")).toHaveValue("MatKhau123")
    expect(screen.getByRole("button", { name: "Đăng nhập" })).toBeEnabled()
    expect(replace).not.toHaveBeenCalled()
  })

  it.each([
    [
      "chuaxacminh@example.com",
      "Tài khoản chưa xác minh email. Vui lòng mở liên kết trong thư chúng tôi đã gửi.",
    ],
    [
      "bikhoa@example.com",
      "Tài khoản tạm khóa do đăng nhập sai nhiều lần. Vui lòng thử lại sau 15 phút.",
    ],
    [
      "quanhanh@example.com",
      "Bạn thao tác quá nhanh. Vui lòng thử lại sau ít phút.",
    ],
  ])("%s → %s", async (email, message) => {
    await submit(email)
    expect(await screen.findByRole("alert")).toHaveTextContent(message)
    expect(tokenStore.getSession().status).not.toBe("authenticated")
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

  it("mất mạng: không đoán nguyên nhân", async () => {
    server.use(http.post(`${BFF_URL}/auth/login`, () => HttpResponse.error()))
    await submit("an@example.com")
    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Không kết nối được máy chủ."
    )
  })

  it("503 BFF mất kho phiên (Redis): câu gián đoạn đăng nhập, KHÔNG mã tra cứu — đổi có chủ đích ở GĐ4 Q-E4", async () => {
    // Hình dạng đúng như `sessionUnavailable()` của lib/bff/handlers.ts: `type` riêng, không `traceId` (BFF không có).
    // Trước Q-E4 ca này ra câu chung "Đã xảy ra lỗi không mong muốn." — người dùng tưởng mình nhập sai gì đó.
    server.use(
      http.post(`${BFF_URL}/auth/login`, () =>
        HttpResponse.json(
          {
            type: BFF_PROBLEM_TYPES.sessionUnavailable,
            title: "Dịch vụ phiên đăng nhập tạm thời không sẵn sàng",
            status: 503,
          },
          {
            status: 503,
            headers: { "Content-Type": "application/problem+json" },
          }
        )
      )
    )

    await submit("an@example.com")

    const alert = await screen.findByRole("alert")
    expect(alert).toHaveTextContent(
      "Dịch vụ đăng nhập tạm thời gián đoạn. Vui lòng thử lại sau ít phút."
    )
    expect(alert).not.toHaveTextContent("Mã tra cứu")
    expect(replace).not.toHaveBeenCalled()
  })

  it("cùng một lỗi hai lần liên tiếp: focus quay lại thông báo", async () => {
    const user = await submit("sai@example.com")
    const first = await screen.findByRole("alert")
    expect(first).toHaveFocus()

    await user.click(screen.getByRole("button", { name: "Đăng nhập" }))
    await waitFor(() => expect(screen.getByRole("alert")).toHaveFocus())
  })
})

describe("LoginForm — lỗi theo trường (Đ-E5)", () => {
  it("400 từ server: lỗi hiện dưới đúng trường email và password, không có lỗi cấp form", async () => {
    await submit("loi400@example.com")

    await waitFor(() =>
      expect(screen.getByLabelText("Email")).toHaveAccessibleDescription(
        "Email không đúng định dạng."
      )
    )
    expect(screen.getByLabelText("Mật khẩu")).toHaveAccessibleDescription(
      "Mật khẩu là bắt buộc."
    )
    expect(screen.queryByText(/Dữ liệu không hợp lệ/)).not.toBeInTheDocument()
  })

  it("mật khẩu 25 × 'ệ' (75 byte): lỗi client, 0 request", async () => {
    const seen = recordRequests()
    await submit("an@example.com", "ệ".repeat(25))

    expect(screen.getByLabelText("Mật khẩu")).toHaveAccessibleDescription(
      "Mật khẩu tối đa 72 byte."
    )
    expect(seen).toEqual([])
  })

  it("để trống cả hai: lỗi client, 0 request; mật khẩu 1 ký tự thì KHÔNG bị chặn", async () => {
    const seen = recordRequests()
    const user = await submit("", "")

    expect(screen.getByLabelText("Email")).toHaveAccessibleDescription(
      "Email là bắt buộc."
    )
    expect(screen.getByLabelText("Mật khẩu")).toHaveAccessibleDescription(
      "Mật khẩu là bắt buộc."
    )
    expect(seen).toEqual([])

    // Sửa trường thì lỗi của trường đó biến mất.
    await user.type(screen.getByLabelText("Email"), "sai@example.com")
    expect(screen.getByLabelText("Email")).not.toHaveAttribute("aria-invalid")

    await user.type(screen.getByLabelText("Mật khẩu"), "x")
    await user.click(screen.getByRole("button", { name: "Đăng nhập" }))
    await screen.findByRole("alert")
    expect(seen).toEqual(["/bff/auth/login"])
  })
})

describe("LoginForm — thành công", () => {
  it.each([
    ["/me", "/me"],
    // Chặn open redirect → về trang chủ "/" (GĐ4 Q-E1 đổi mặc định từ "/me").
    ["//evil.example", "/"],
    ["https://evil.example/", "/"],
    ["/posts/1?tab=comments", "/posts/1?tab=comments"],
  ])(
    "?next=%s → router.replace(%s), phiên authenticated",
    async (next, target) => {
      search = new URLSearchParams({ next })
      await submit("an@example.com")

      await waitFor(() => expect(replace).toHaveBeenCalledWith(target))
      expect(replace).toHaveBeenCalledTimes(1)
      expect(tokenStore.getSession().status).toBe("authenticated")
    }
  )

  it("không có ?next → trang chủ \"/\" (feed — GĐ4 Q-E1, trước đó /me)", async () => {
    await submit("an@example.com")
    await waitFor(() => expect(replace).toHaveBeenCalledWith("/"))
  })

  it("đang chờ: nút disabled, bấm thêm không gửi lần hai", async () => {
    let release!: () => void
    const gate = new Promise<void>((r) => (release = r))
    server.use(
      http.post(`${BFF_URL}/auth/login`, async () => {
        await gate
        return new HttpResponse(null, { status: 204 })
      })
    )
    const seen = recordRequests()

    const user = await submit("an@example.com")
    const button = screen.getByRole("button", { name: "Đăng nhập" })
    await waitFor(() => expect(button).toBeDisabled())
    await user.click(button)
    release()

    await waitFor(() => expect(replace).toHaveBeenCalledTimes(1))
    expect(seen).toEqual(["/bff/auth/login"])
  })
})
