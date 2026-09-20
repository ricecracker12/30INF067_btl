import { render, screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { http, HttpResponse } from "msw"
import { beforeEach, describe, expect, it, vi } from "vitest"

import { BFF_URL } from "@/lib/api/config"
import type { UpsertProfileRequest } from "@/lib/api/types"
import { profile } from "@/mocks/fixtures"
import { server } from "@/mocks/node"

import { ProfileForm } from "./profile-form"
import { profileStore } from "./profile-store"

const replace = vi.fn()
const router = { replace }
const searchParams = new URLSearchParams()
vi.mock("next/navigation", () => ({
  useRouter: () => router,
  useSearchParams: () => searchParams,
}))

const UPSERT = `${BFF_URL}/api/users/me/profile`

function problem(
  status: number,
  title: string,
  errors?: Record<string, string[]>
) {
  return HttpResponse.json(
    { type: "t", title, status, traceId: "x", ...(errors && { errors }) },
    { status, headers: { "Content-Type": "application/problem+json" } }
  )
}

/** Ghi lại body THẬT SỰ đi lên — thứ duy nhất chứng minh `bio` có được gửi hay không. */
function recordUpsert() {
  const bodies: UpsertProfileRequest[] = []
  server.use(
    http.put(UPSERT, async ({ request }) => {
      const body = (await request.json()) as UpsertProfileRequest
      bodies.push(body)
      return HttpResponse.json({ ...profile, ...body })
    })
  )
  return bodies
}

beforeEach(() => {
  profileStore.reset()
  replace.mockReset()
})

describe("ProfileForm — onboarding", () => {
  it("gửi tên đã trim, lưu vào store, rồi rời màn", async () => {
    const bodies = recordUpsert()
    const user = userEvent.setup()
    render(<ProfileForm initial={null} submitLabel="Bắt đầu" />)

    await user.type(screen.getByLabelText("Tên hiển thị"), "  An Nguyễn  ")
    await user.click(screen.getByRole("button", { name: "Bắt đầu" }))

    await waitFor(() => expect(replace).toHaveBeenCalledWith("/me"))
    expect(bodies).toEqual([{ displayName: "An Nguyễn", bio: null }])
    expect(profileStore.getState().status).toBe("ready")
  })

  it("client chặn trước khi gọi API: tên 1 ký tự hiện đúng câu của server, KHÔNG có request nào", async () => {
    const bodies = recordUpsert()
    const user = userEvent.setup()
    render(<ProfileForm initial={null} submitLabel="Bắt đầu" />)

    await user.type(screen.getByLabelText("Tên hiển thị"), "A")
    await user.click(screen.getByRole("button", { name: "Bắt đầu" }))

    expect(
      await screen.findByText("Tên hiển thị phải có từ 2 đến 50 ký tự.")
    ).toBeInTheDocument()
    expect(bodies).toEqual([])
    expect(replace).not.toHaveBeenCalled()
  })

  it("400 của server hiện theo key `errors.displayName` KỂ CẢ khi client đã kiểm qua (Đ-E5)", async () => {
    server.use(
      http.put(UPSERT, () =>
        problem(400, "Dữ liệu không hợp lệ", {
          displayName: ["Tên hiển thị phải có từ 2 đến 50 ký tự."],
        })
      )
    )
    const user = userEvent.setup()
    render(<ProfileForm initial={null} submitLabel="Bắt đầu" />)

    // Client thấy hợp lệ (2 ký tự) nhưng server vẫn từ chối — server là bên quyết định.
    await user.type(screen.getByLabelText("Tên hiển thị"), "An")
    await user.click(screen.getByRole("button", { name: "Bắt đầu" }))

    expect(
      await screen.findByText("Tên hiển thị phải có từ 2 đến 50 ký tự.")
    ).toBeInTheDocument()
    expect(replace).not.toHaveBeenCalled()
    // Nút mở lại để sửa và gửi tiếp.
    expect(screen.getByRole("button", { name: "Bắt đầu" })).toBeEnabled()
  })

  it("400 với key lạ (`body`) không biến mất lặng lẽ — hiện ở cấp form", async () => {
    server.use(
      http.put(UPSERT, () =>
        problem(400, "Dữ liệu không hợp lệ", { body: ["JSON hỏng."] })
      )
    )
    const user = userEvent.setup()
    render(<ProfileForm initial={null} submitLabel="Bắt đầu" />)

    await user.type(screen.getByLabelText("Tên hiển thị"), "An")
    await user.click(screen.getByRole("button", { name: "Bắt đầu" }))

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Dữ liệu không hợp lệ."
    )
  })

  it("429 hiện câu chung, KHÔNG có đồng hồ đếm ngược (server không gửi Retry-After)", async () => {
    server.use(http.put(UPSERT, () => problem(429, "Quá nhiều yêu cầu")))
    const user = userEvent.setup()
    render(<ProfileForm initial={null} submitLabel="Bắt đầu" />)

    await user.type(screen.getByLabelText("Tên hiển thị"), "An")
    await user.click(screen.getByRole("button", { name: "Bắt đầu" }))

    const alert = await screen.findByRole("alert")
    expect(alert).toHaveTextContent(
      "Bạn thao tác quá nhanh. Vui lòng thử lại sau ít phút."
    )
    expect(alert.textContent).not.toMatch(/\d+\s*(giây|phút nữa)/)
  })

  it("500 hiện mã tra cứu để đối chiếu log", async () => {
    server.use(http.put(UPSERT, () => problem(500, "Lỗi")))
    const user = userEvent.setup()
    render(<ProfileForm initial={null} submitLabel="Bắt đầu" />)

    await user.type(screen.getByLabelText("Tên hiển thị"), "An")
    await user.click(screen.getByRole("button", { name: "Bắt đầu" }))

    expect(await screen.findByRole("alert")).toHaveTextContent("Mã tra cứu: x")
  })
})

describe("ProfileForm — sửa hồ sơ (Q-D3: PUT thay thế toàn phần)", () => {
  it("sửa TÊN mà không chạm bio: bio cũ VẪN được gửi lại, không bị xóa", async () => {
    const bodies = recordUpsert()
    const user = userEvent.setup()
    render(
      <ProfileForm initial={profile} submitLabel="Lưu" onSaved={() => {}} />
    )

    const ten = screen.getByLabelText("Tên hiển thị")
    await user.clear(ten)
    await user.type(ten, "Tên mới")
    await user.click(screen.getByRole("button", { name: "Lưu" }))

    await waitFor(() => expect(bodies).toHaveLength(1))
    expect(bodies[0]).toEqual({ displayName: "Tên mới", bio: profile.bio })
  })

  it("xóa trắng ô bio: gửi `bio: null` — nói ra ý muốn xóa, không bỏ field im lặng", async () => {
    const bodies = recordUpsert()
    const user = userEvent.setup()
    render(
      <ProfileForm initial={profile} submitLabel="Lưu" onSaved={() => {}} />
    )

    await user.clear(screen.getByLabelText("Giới thiệu"))
    await user.click(screen.getByRole("button", { name: "Lưu" }))

    await waitFor(() => expect(bodies).toHaveLength(1))
    expect(bodies[0]).toEqual({
      displayName: profile.displayName,
      bio: null,
    })
    expect("bio" in bodies[0]).toBe(true)
  })

  it("sửa xong thì ở lại màn (không điều hướng) và store có hồ sơ mới", async () => {
    recordUpsert()
    const onSaved = vi.fn()
    const user = userEvent.setup()
    render(
      <ProfileForm initial={profile} submitLabel="Lưu" onSaved={onSaved} />
    )

    const ten = screen.getByLabelText("Tên hiển thị")
    await user.clear(ten)
    await user.type(ten, "Tên mới")
    await user.click(screen.getByRole("button", { name: "Lưu" }))

    await waitFor(() => expect(onSaved).toHaveBeenCalledTimes(1))
    expect(replace).not.toHaveBeenCalled()
    expect(profileStore.getState().profile?.displayName).toBe("Tên mới")
  })
})
