import { act, render, screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { http, HttpResponse } from "msw"
import { StrictMode, type ReactNode } from "react"
import { beforeEach, describe, expect, it, vi } from "vitest"

import { BFF_URL } from "@/lib/api/config"
import { tokenStore } from "@/lib/auth/token-store"
import { server } from "@/mocks/node"
import { fakeSession } from "@/mocks/session"

import { LogoutButton } from "./logout-button"
import { RequireAuth } from "./require-auth"

const replace = vi.fn()
// Cùng một object qua mọi render, như Next — `router` nằm trong deps của effect (xem E5).
const router = { replace }
vi.mock("next/navigation", () => ({
  useRouter: () => router,
  usePathname: () => "/me",
}))

function recordSessionChecks() {
  const seen: string[] = []
  server.events.on("request:start", ({ request }) => {
    if (new URL(request.url).pathname.endsWith("/auth/session"))
      seen.push(request.method)
  })
  return seen
}

/** Spy: đếm mọi lần children được RENDER — "không nháy nội dung" nghĩa là con số này là 0. */
const renders = vi.fn()
function Protected({ children }: { children?: ReactNode }) {
  renders()
  return <p>Nội dung bảo vệ{children}</p>
}

function renderGuard(children: ReactNode = <Protected />) {
  return render(
    <StrictMode>
      <RequireAuth>{children}</RequireAuth>
    </StrictMode>
  )
}

beforeEach(() => {
  tokenStore.reset()
  replace.mockReset()
  renders.mockReset()
})

describe("RequireAuth (Đ-E3)", () => {
  it("BFF báo không có phiên: children KHÔNG BAO GIỜ render; về /login?next=%2Fme", async () => {
    renderGuard()

    expect(screen.getByTestId("page-skeleton")).toBeInTheDocument()
    await waitFor(() =>
      expect(replace).toHaveBeenCalledWith("/login?next=%2Fme")
    )
    expect(renders).not.toHaveBeenCalled()
    expect(screen.queryByText("Nội dung bảo vệ")).not.toBeInTheDocument()
  })

  it("StrictMode, còn phiên: ĐÚNG 1 GET /bff/auth/session; children render; không điều hướng", async () => {
    fakeSession.start()
    const seen = recordSessionChecks()
    renderGuard()

    expect(await screen.findByText("Nội dung bảo vệ")).toBeInTheDocument()
    expect(seen).toEqual(["GET"])
    expect(replace).not.toHaveBeenCalled()
  })

  it("BFF 500: 'Không kiểm tra được phiên đăng nhập.', KHÔNG điều hướng; Thử lại hỏi lần 2 và vào được", async () => {
    server.use(
      http.get(
        `${BFF_URL}/auth/session`,
        () =>
          HttpResponse.json(
            { type: "t", title: "t", status: 500 },
            {
              status: 500,
              headers: { "Content-Type": "application/problem+json" },
            }
          ),
        { once: true }
      )
    )
    fakeSession.start()
    const seen = recordSessionChecks()
    const user = userEvent.setup()
    renderGuard()

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Không kiểm tra được phiên đăng nhập."
    )
    expect(replace).not.toHaveBeenCalled()
    expect(renders).not.toHaveBeenCalled()
    expect(seen).toEqual(["GET"])

    await user.click(screen.getByRole("button", { name: "Thử lại" }))
    expect(await screen.findByText("Nội dung bảo vệ")).toBeInTheDocument()
    expect(seen).toEqual(["GET", "GET"])
    expect(replace).not.toHaveBeenCalled()
  })

  it("đang đăng nhập rồi phiên hết hạn (proxy BFF trả 401): kèm next để quay lại", async () => {
    tokenStore.startSession()
    renderGuard()
    expect(screen.getByText("Nội dung bảo vệ")).toBeInTheDocument()

    act(() => tokenStore.endSession("expired"))

    expect(screen.queryByText("Nội dung bảo vệ")).not.toBeInTheDocument()
    await waitFor(() =>
      expect(replace).toHaveBeenCalledWith("/login?next=%2Fme")
    )
  })

  it("bấm Đăng xuất: nội dung biến mất, về /login KHÔNG kèm next, đúng một lần điều hướng", async () => {
    fakeSession.start()
    tokenStore.startSession()
    const user = userEvent.setup()
    renderGuard(
      <>
        <LogoutButton />
        <Protected />
      </>
    )

    await user.click(screen.getByRole("button", { name: "Đăng xuất" }))

    await waitFor(() => expect(replace).toHaveBeenCalledWith("/login"))
    expect(replace).toHaveBeenCalledTimes(1)
    expect(screen.queryByText("Nội dung bảo vệ")).not.toBeInTheDocument()
    expect(fakeSession.current()).toBeNull()
  })
})
