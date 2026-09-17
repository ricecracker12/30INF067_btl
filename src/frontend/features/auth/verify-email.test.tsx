import { render, screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { http, HttpResponse } from "msw"
import { StrictMode } from "react"
import { beforeEach, describe, expect, it, vi } from "vitest"

import { BFF_URL } from "@/lib/api/config"
import { resetVerifyOnce } from "@/lib/auth/verify-once"
import { verifyEmailResponse } from "@/mocks/fixtures"
import { server } from "@/mocks/node"

import { VerifyEmail } from "./verify-email"

const replace = vi.fn()
// Cùng MỘT object qua mọi lần render, như `useRouter` của Next — `router` nằm trong deps của effect, trả
// object mới mỗi lần là effect chạy lại sau mỗi render (điều không xảy ra ở app thật).
const router = { replace }
let search = new URLSearchParams()

vi.mock("next/navigation", () => ({
  useRouter: () => router,
  useSearchParams: () => search,
}))

const VALID = "0123456789abcdef".repeat(4)
const EXPIRED = "a".repeat(64) // mock → 410
const BAD = "b".repeat(64) // mock → 400

const INVALID_TEXT =
  "Liên kết xác minh không hợp lệ. Hãy mở lại đúng liên kết trong thư, không sao chép thiếu ký tự."
const GONE_TEXT =
  "Liên kết xác minh đã hết hạn hoặc đã được sử dụng. Nếu bạn đã xác minh trước đó, hãy đăng nhập."

function recordRequests() {
  const seen: string[] = []
  server.events.on("request:start", ({ request }) => {
    seen.push(new URL(request.url).pathname)
  })
  return seen
}

/** Render như Next dev: App Router bật StrictMode — effect chạy hai lần. */
function renderAt(token: string | null) {
  search = new URLSearchParams(token === null ? {} : { token })
  return render(
    <StrictMode>
      <VerifyEmail />
    </StrictMode>
  )
}

beforeEach(() => {
  replace.mockReset()
  resetVerifyOnce()
})

describe("VerifyEmail — Đ-E10", () => {
  it("StrictMode: ĐÚNG 1 POST /auth/verify-email; hiện thành công với email của response, không hiện 410", async () => {
    const seen = recordRequests()
    renderAt(VALID)

    expect(screen.getByRole("status")).toHaveTextContent("Đang xác minh email…")
    expect(
      await screen.findByText(verifyEmailResponse.email)
    ).toBeInTheDocument()
    expect(screen.getByRole("status")).toHaveTextContent(
      `Email ${verifyEmailResponse.email} đã được xác minh.`
    )
    expect(screen.getByRole("link", { name: "Đăng nhập" })).toHaveAttribute(
      "href",
      "/login"
    )
    expect(screen.queryByText(/hết hạn/)).not.toBeInTheDocument()
    expect(seen).toEqual(["/bff/auth/verify-email"])
  })

  it("có kết quả: router.replace('/verify-email') đúng một lần — token rời URL", async () => {
    renderAt(VALID)
    await waitFor(() => expect(replace).toHaveBeenCalledWith("/verify-email"))
    expect(replace).toHaveBeenCalledTimes(1)
  })

  it("URL đổi sau replace (không còn token): vẫn giữ trạng thái thành công, không gọi lại", async () => {
    const seen = recordRequests()
    const view = renderAt(VALID)
    await screen.findByText(verifyEmailResponse.email)

    search = new URLSearchParams()
    view.rerender(
      <StrictMode>
        <VerifyEmail />
      </StrictMode>
    )
    expect(screen.getByText(verifyEmailResponse.email)).toBeInTheDocument()
    expect(screen.queryByText(INVALID_TEXT)).not.toBeInTheDocument()
    expect(seen).toEqual(["/bff/auth/verify-email"])
  })
})

describe("VerifyEmail — lỗi cuối (400 / 410)", () => {
  it("410: đúng câu, nút Đăng nhập + link Đăng ký, KHÔNG có Gửi lại", async () => {
    renderAt(EXPIRED)

    expect(await screen.findByRole("alert")).toHaveTextContent(GONE_TEXT)
    expect(screen.getByRole("link", { name: "Đăng nhập" })).toHaveAttribute(
      "href",
      "/login"
    )
    expect(screen.getByRole("link", { name: "Đăng ký" })).toHaveAttribute(
      "href",
      "/register"
    )
    expect(screen.queryByText(/Gửi lại/i)).not.toBeInTheDocument()
    await waitFor(() => expect(replace).toHaveBeenCalledWith("/verify-email"))
  })

  it("400 từ server: đúng câu của bảng, không phải câu của server", async () => {
    renderAt(BAD)

    expect(await screen.findByRole("alert")).toHaveTextContent(INVALID_TEXT)
    expect(screen.getByRole("link", { name: "Đăng ký" })).toBeInTheDocument()
    await waitFor(() => expect(replace).toHaveBeenCalledWith("/verify-email"))
  })

  it.each([
    ["không có token", null],
    ["'XYZ'", "XYZ"],
    ["64 ký tự in hoa", "A".repeat(64)],
    ["63 ký tự", "a".repeat(63)],
    ["64 hex + xuống dòng", `${VALID}\n`],
  ])("%s → 400, 0 request", async (_, token) => {
    const seen = recordRequests()
    renderAt(token)

    expect(screen.getByRole("alert")).toHaveTextContent(INVALID_TEXT)
    // Cho effect và mọi microtask chạy hết rồi mới khẳng định "không có request".
    await new Promise((r) => setTimeout(r, 50))
    expect(seen).toEqual([])
  })
})

describe("VerifyEmail — lỗi tạm thời (bảng E4)", () => {
  it("500: câu chung + traceId, GIỮ token trên URL; Thử lại gửi request mới và thành công", async () => {
    let calls = 0
    server.use(
      http.post(`${BFF_URL}/auth/verify-email`, () => {
        calls += 1
        return calls === 1
          ? HttpResponse.json(
              {
                type: "https://httpstatuses.io/500",
                title: "Đã xảy ra lỗi không mong muốn",
                status: 500,
                traceId: "0af7651916cd43dd8448eb211c80319c",
              },
              {
                status: 500,
                headers: { "Content-Type": "application/problem+json" },
              }
            )
          : HttpResponse.json(verifyEmailResponse)
      })
    )
    const user = userEvent.setup()
    renderAt(VALID)

    const alert = await screen.findByRole("alert")
    expect(alert).toHaveTextContent(
      "Đã xảy ra lỗi không mong muốn. Mã tra cứu: 0af7651916cd43dd8448eb211c80319c"
    )
    expect(alert).toHaveFocus()
    expect(replace).not.toHaveBeenCalled()
    expect(calls).toBe(1)

    await user.click(screen.getByRole("button", { name: "Thử lại" }))
    expect(
      await screen.findByText(verifyEmailResponse.email)
    ).toBeInTheDocument()
    expect(calls).toBe(2)
    expect(replace).toHaveBeenCalledWith("/verify-email")
  })

  it.each([
    [
      "429",
      () =>
        HttpResponse.json(
          { type: "t", title: "t", status: 429 },
          {
            status: 429,
            headers: { "Content-Type": "application/problem+json" },
          }
        ),
      "Bạn thao tác quá nhanh. Vui lòng thử lại sau ít phút.",
    ],
    ["mất mạng", () => HttpResponse.error(), "Không kết nối được máy chủ."],
  ])("%s: đúng câu, có nút Thử lại", async (_, resolver, message) => {
    server.use(http.post(`${BFF_URL}/auth/verify-email`, resolver))
    renderAt(VALID)

    expect(await screen.findByRole("alert")).toHaveTextContent(message)
    expect(screen.getByRole("button", { name: "Thử lại" })).toBeEnabled()
    expect(replace).not.toHaveBeenCalled()
  })
})
