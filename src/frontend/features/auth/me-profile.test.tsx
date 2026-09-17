import { render, screen, waitFor, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { http, HttpResponse } from "msw"
import { beforeEach, describe, expect, it, vi } from "vitest"

import { API_BASE_URL } from "@/lib/api/config"
import { tokenStore } from "@/lib/auth/token-store"
import { me } from "@/mocks/fixtures"
import { server } from "@/mocks/node"
import { fakeSession } from "@/mocks/session"

import { MeProfile } from "./me-profile"

// Chạy TRƯỚC mọi import: múi giờ của tiến trình là UTC. Máy nhóm ở UTC+7, nên nếu không ép thì bỏ
// `timeZone: "Asia/Ho_Chi_Minh"` khỏi formatter vẫn ra 10:14 và test giờ xanh vô nghĩa.
vi.hoisted(() => {
  process.env.TZ = "UTC"
})

function countMe() {
  const counter = { n: 0 }
  server.events.on("request:start", ({ request }) => {
    if (new URL(request.url).pathname.endsWith("/me")) counter.n += 1
  })
  return counter
}

beforeEach(() => {
  tokenStore.reset()
  tokenStore.startSession(fakeSession.start())
})

describe("MeProfile (E6 bước 4)", () => {
  it("hiện email, roleDisplayName, giờ Việt Nam; KHÔNG hiện role", async () => {
    render(<MeProfile />)

    const profile = await screen.findByTestId("me-profile")
    const q = within(profile)
    expect(q.getByText(me.email)).toBeInTheDocument()
    expect(q.getByText("Người dùng")).toBeInTheDocument()
    // 2026-09-08T03:14:07Z = 10:14 giờ Việt Nam (UTC+7), không phụ thuộc múi giờ của máy chạy test.
    expect(profile).toHaveTextContent(/10:14/)
    expect(profile).toHaveTextContent(/10:10/)
    expect(profile).not.toHaveTextContent(/\bUSER\b/)
  })

  it("Tải lại gọi /me lần nữa", async () => {
    const counter = countMe()
    const user = userEvent.setup()
    render(<MeProfile />)
    await screen.findByTestId("me-profile")
    expect(counter.n).toBe(1)

    await user.click(screen.getByRole("button", { name: "Tải lại" }))
    await waitFor(() => expect(counter.n).toBe(2))
    await waitFor(() =>
      expect(
        screen.getByRole("button", { name: "Tải lại" })
      ).not.toHaveAttribute("aria-busy")
    )
    expect(screen.getByTestId("me-profile")).toBeInTheDocument()
  })

  it("lỗi 500: câu chung kèm traceId; Tải lại thành công thì lỗi biến mất", async () => {
    server.use(
      http.get(
        `${API_BASE_URL}/me`,
        () =>
          HttpResponse.json(
            {
              type: "t",
              title: "t",
              status: 500,
              traceId: "0af7651916cd43dd8448eb211c80319c",
            },
            {
              status: 500,
              headers: { "Content-Type": "application/problem+json" },
            }
          ),
        { once: true }
      )
    )
    const user = userEvent.setup()
    render(<MeProfile />)

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Đã xảy ra lỗi không mong muốn. Mã tra cứu: 0af7651916cd43dd8448eb211c80319c"
    )
    await user.click(screen.getByRole("button", { name: "Tải lại" }))
    expect(await screen.findByTestId("me-profile")).toBeInTheDocument()
    expect(screen.queryByRole("alert")).not.toBeInTheDocument()
  })

  it("rời trang khi request chưa xong: hủy request (AbortController), không lỗi", async () => {
    let aborted = false
    server.use(
      http.get(`${API_BASE_URL}/me`, async ({ request }) => {
        await new Promise<void>((resolve) => {
          request.signal.addEventListener("abort", () => {
            aborted = true
            resolve()
          })
        })
        return HttpResponse.json(me)
      })
    )
    const view = render(<MeProfile />)
    await new Promise((r) => setTimeout(r, 20))
    view.unmount()
    await waitFor(() => expect(aborted).toBe(true))
  })
})
