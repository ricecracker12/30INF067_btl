import { render, screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { http, HttpResponse } from "msw"
import { StrictMode } from "react"
import { beforeEach, describe, expect, it, vi } from "vitest"

import { BFF_URL } from "@/lib/api/config"
import { profile, userId } from "@/mocks/fixtures"
import { server } from "@/mocks/node"
import { fakeSession } from "@/mocks/session"

import { profileStore } from "./profile-store"
import { RequireProfile } from "./require-profile"

const replace = vi.fn()
// Cùng một object qua mọi render, như Next — `router` nằm trong deps của effect.
const router = { replace }
vi.mock("next/navigation", () => ({
  useRouter: () => router,
  usePathname: () => "/me",
}))

const PROFILE_PATH = `${BFF_URL}/api/users/:userId/profile`

function problem(status: number, title: string) {
  return HttpResponse.json(
    { type: "t", title, status, traceId: "x" },
    { status, headers: { "Content-Type": "application/problem+json" } }
  )
}

/** Đếm mọi lần children được RENDER — "không nháy nội dung" nghĩa là con số này là 0. */
const renders = vi.fn()
function Protected() {
  renders()
  return <p>Nội dung cần hồ sơ</p>
}

function recordProfileCalls() {
  const seen: string[] = []
  server.events.on("request:start", ({ request }) => {
    const { pathname } = new URL(request.url)
    if (pathname.endsWith("/profile") || pathname.endsWith("/api/me"))
      seen.push(pathname)
  })
  return seen
}

function renderGuard() {
  return render(
    <StrictMode>
      <RequireProfile>
        <Protected />
      </RequireProfile>
    </StrictMode>
  )
}

beforeEach(() => {
  profileStore.reset()
  replace.mockReset()
  renders.mockReset()
  fakeSession.start()
})

describe("RequireProfile (Đ-2.4, Q-E2)", () => {
  it("404: về /onboarding, và children KHÔNG render một lần nào", async () => {
    server.use(http.get(PROFILE_PATH, () => problem(404, "Không tìm thấy")))

    renderGuard()

    expect(screen.getByTestId("page-skeleton")).toBeInTheDocument()
    await waitFor(() => expect(replace).toHaveBeenCalledWith("/onboarding"))
    expect(renders).not.toHaveBeenCalled()
    expect(screen.queryByText("Nội dung cần hồ sơ")).not.toBeInTheDocument()
  })

  it("200: render children, KHÔNG điều hướng, và StrictMode vẫn chỉ MỘT cặp request", async () => {
    const seen = recordProfileCalls()

    renderGuard()

    expect(await screen.findByText("Nội dung cần hồ sơ")).toBeInTheDocument()
    expect(replace).not.toHaveBeenCalled()
    // Effect chạy hai lần trong StrictMode → `loadProfile` single-flight gộp lại.
    expect(seen).toEqual(["/bff/api/me", `/bff/api/users/${userId}/profile`])
    expect(profileStore.getState().profile).toEqual(profile)
  })

  it("500: hiện 'Thử lại', KHÔNG đá sang /onboarding (sẽ ghi đè bio cũ), KHÔNG render children", async () => {
    server.use(
      http.get(PROFILE_PATH, () => problem(500, "Lỗi"), { once: true })
    )
    const user = userEvent.setup()

    renderGuard()

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Không tải được hồ sơ của bạn."
    )
    expect(replace).not.toHaveBeenCalled()
    expect(renders).not.toHaveBeenCalled()

    // Thử lại hỏi lần hai và vào được.
    await user.click(screen.getByRole("button", { name: "Thử lại" }))
    expect(await screen.findByText("Nội dung cần hồ sơ")).toBeInTheDocument()
    expect(replace).not.toHaveBeenCalled()
  })

  it("mất mạng cũng là error, không phải missing", async () => {
    server.use(http.get(PROFILE_PATH, () => HttpResponse.error()))

    renderGuard()

    expect(await screen.findByRole("alert")).toBeInTheDocument()
    expect(replace).not.toHaveBeenCalled()
  })

  it("401 KHÔNG thành missing: RequireAuth lo việc đá về /login, onboarding không được nháy lên", async () => {
    server.use(http.get(PROFILE_PATH, () => problem(401, "Chưa xác thực")))

    renderGuard()

    // Ở lại khung chờ; không có lần `replace("/onboarding")` nào.
    await waitFor(() =>
      expect(screen.getByTestId("page-skeleton")).toBeInTheDocument()
    )
    expect(replace).not.toHaveBeenCalled()
    expect(renders).not.toHaveBeenCalled()
    expect(profileStore.getState().status).not.toBe("missing")
  })
})
