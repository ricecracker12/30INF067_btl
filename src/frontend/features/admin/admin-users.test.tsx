import { render, screen, waitFor, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { http, HttpResponse } from "msw"
import { StrictMode } from "react"
import { describe, expect, it } from "vitest"

import { BFF_URL } from "@/lib/api/config"
import type { AdminUser, AdminUserChange, MeResponse } from "@/lib/api/types"
import { adminUser, me, permissionCodes, roleSummaries } from "@/mocks/fixtures"
import { server } from "@/mocks/node"

import { AdminUserDetail } from "./admin-user-detail"
import { AdminUsers } from "./admin-users"

// GĐ6 E7 — màn quản trị tài khoản: lọc theo trạng thái/vai trò/email; khóa (lý do bắt buộc) / mở khóa / đổi vai trò; KHÔNG
// optimistic; `revocation: deferred` → cảnh báo vàng; 409 `last-admin` → câu của server (Đ-6.21).

const API = `${BFF_URL}/api`
const USER = `${API}/admin/users/${adminUser.userId}`
const CHO = { timeout: 5000 }
const PROBLEM = { "Content-Type": "application/problem+json" }

function laAdmin() {
  server.use(
    http.get(`${API}/me`, () =>
      HttpResponse.json({ ...me, role: "ADMIN", permissions: [...permissionCodes] } satisfies MeResponse)
    ),
    http.get(`${API}/admin/roles`, () => HttpResponse.json(roleSummaries))
  )
}

const change = (user: Partial<AdminUser>, revocation: AdminUserChange["revocation"]) =>
  HttpResponse.json({ user: { ...adminUser, ...user }, revocation } satisfies AdminUserChange)

describe("AdminUsers", () => {
  it("lọc: mỗi bộ lọc là một lượt tải mới với đúng query", async () => {
    laAdmin()
    const queries: string[] = []
    server.use(
      http.get(`${API}/admin/users`, ({ request }) => {
        queries.push(new URL(request.url).search)
        return HttpResponse.json({ items: [adminUser], nextCursor: null })
      })
    )
    const user = userEvent.setup()
    render(<AdminUsers />)
    const row = await screen.findByTestId("admin-user-row", {}, CHO)
    expect(row).toHaveAttribute("href", `/admin/users/${adminUser.userId}`)

    await user.click(screen.getByRole("button", { name: "Đã bị khóa" }))
    await user.click(await screen.findByRole("button", { name: "Kiểm duyệt viên" }, CHO))
    await user.type(screen.getByLabelText("Tìm theo email"), "an.ng{Enter}")

    await waitFor(
      () =>
        expect(Object.fromEntries(new URLSearchParams(queries.at(-1)))).toEqual({
          q: "an.ng",
          status: "disabled",
          roleCode: "MODERATOR",
          limit: "20",
        }),
      CHO
    )
    expect(queries[0]).toBe("?limit=20")
  })
})

describe("StrictMode", () => {
  it("danh sách và chi tiết vẫn nạp xong", async () => {
    laAdmin()
    server.use(
      http.get(`${API}/admin/users`, () => HttpResponse.json({ items: [adminUser], nextCursor: null })),
      http.get(USER, () => HttpResponse.json(adminUser))
    )
    render(
      <StrictMode>
        <AdminUsers />
        <AdminUserDetail userId={adminUser.userId} />
      </StrictMode>
    )
    expect(await screen.findByTestId("admin-user-row", {}, CHO)).toBeInTheDocument()
    expect(await screen.findByTestId("admin-user", {}, CHO)).toBeInTheDocument()
  })
})

describe("AdminUserDetail", () => {
  it("khóa: lý do trống → câu của server, KHÔNG gọi API; có lý do → vẽ theo phản hồi", async () => {
    laAdmin()
    const bodies: unknown[] = []
    server.use(
      http.get(USER, () => HttpResponse.json(adminUser)),
      http.post(`${USER}/lock`, async ({ request }) => {
        bodies.push(await request.json())
        return change({ status: "disabled" }, "applied")
      })
    )
    const user = userEvent.setup()
    render(<AdminUserDetail userId={adminUser.userId} />)
    await user.click(await screen.findByRole("button", { name: "Khóa tài khoản" }, CHO))
    await user.click(screen.getByRole("button", { name: "Khóa" }))
    expect(await screen.findByText("Lý do khóa là bắt buộc.", {}, CHO)).toBeInTheDocument()
    expect(bodies).toHaveLength(0)

    await user.type(screen.getByLabelText("Lý do (ghi vào nhật ký kiểm toán)"), "Spam hàng loạt")
    await user.click(screen.getByRole("button", { name: "Khóa" }))
    await waitFor(() => expect(screen.getByTestId("admin-user-status")).toHaveTextContent("Đã bị khóa"), CHO)
    expect(bodies).toEqual([{ reason: "Spam hàng loạt" }])
    expect(screen.getByTestId("revocation-result")).toBeInTheDocument()
    // Đã khóa → nút đổi thành Mở khóa.
    expect(screen.getByRole("button", { name: "Mở khóa" })).toBeInTheDocument()
  })

  it("revocation: deferred → cảnh báo vàng, không phải thành công trơn", async () => {
    laAdmin()
    server.use(
      http.get(USER, () => HttpResponse.json({ ...adminUser, status: "disabled" })),
      http.post(`${USER}/unlock`, () => change({ status: "active" }, "deferred"))
    )
    const user = userEvent.setup()
    render(<AdminUserDetail userId={adminUser.userId} />)
    await user.click(await screen.findByRole("button", { name: "Mở khóa" }, CHO))
    expect(await screen.findByTestId("revocation-deferred", {}, CHO)).toHaveTextContent("chậm nhất sau 15 phút")
  })

  it("409 last-admin khi đổi vai trò → câu của server, vai trò trên màn KHÔNG đổi", async () => {
    laAdmin()
    server.use(
      http.get(USER, () => HttpResponse.json({ ...adminUser, roleCode: "ADMIN", roleDisplayName: "Quản trị viên" })),
      http.put(`${USER}/role`, () =>
        HttpResponse.json(
          { type: "urn:socialapp:problem:last-admin", title: "Xung đột dữ liệu", status: 409, traceId: "t" },
          { status: 409, headers: PROBLEM }
        )
      )
    )
    const user = userEvent.setup()
    render(<AdminUserDetail userId={adminUser.userId} />)
    await user.click(await screen.findByRole("button", { name: "Đổi vai trò" }, CHO))
    await user.click(await screen.findByRole("radio", { name: "Người dùng" }, CHO))
    await user.click(screen.getByRole("button", { name: "Lưu vai trò" }))

    expect(
      await screen.findByText("Hệ thống phải còn ít nhất một quản trị viên đang hoạt động.", {}, CHO)
    ).toBeInTheDocument()
    expect(screen.getByTestId("admin-user-role")).toHaveTextContent("Quản trị viên")
  })

  it("đổi vai trò: gửi roleCode, vẽ theo AdminUser server trả; vai trò tự tạo có trong danh sách", async () => {
    laAdmin()
    const bodies: unknown[] = []
    server.use(
      http.get(USER, () => HttpResponse.json(adminUser)),
      http.put(`${USER}/role`, async ({ request }) => {
        bodies.push(await request.json())
        return change({ roleCode: "REVIEWER", roleDisplayName: "Người rà soát" }, "applied")
      })
    )
    const user = userEvent.setup()
    render(<AdminUserDetail userId={adminUser.userId} />)
    await user.click(await screen.findByRole("button", { name: "Đổi vai trò" }, CHO))
    const dialog = await screen.findByRole("dialog", {}, CHO)
    await user.click(await within(dialog).findByRole("radio", { name: "Người rà soát" }, CHO))
    await user.click(within(dialog).getByRole("button", { name: "Lưu vai trò" }))

    await waitFor(() => expect(screen.getByTestId("admin-user-role")).toHaveTextContent("Người rà soát"), CHO)
    expect(bodies).toEqual([{ roleCode: "REVIEWER" }])
  })

  it("400 tự khóa → câu server dưới errors.userId", async () => {
    laAdmin()
    server.use(
      http.get(USER, () => HttpResponse.json(adminUser)),
      http.post(`${USER}/lock`, () =>
        HttpResponse.json(
          {
            type: "https://httpstatuses.io/400",
            title: "Dữ liệu không hợp lệ",
            status: 400,
            traceId: "t",
            errors: { userId: ["Không thể tự khóa tài khoản của mình."] },
          },
          { status: 400, headers: PROBLEM }
        )
      )
    )
    const user = userEvent.setup()
    render(<AdminUserDetail userId={adminUser.userId} />)
    await user.click(await screen.findByRole("button", { name: "Khóa tài khoản" }, CHO))
    await user.type(screen.getByLabelText("Lý do (ghi vào nhật ký kiểm toán)"), "thử")
    await user.click(screen.getByRole("button", { name: "Khóa" }))
    expect(await screen.findByText("Không thể tự khóa tài khoản của mình.", {}, CHO)).toBeInTheDocument()
  })

  it("không có user.lock → không có nút Khóa (chỉ để vẽ; server vẫn chặn)", async () => {
    server.use(
      http.get(`${API}/me`, () => HttpResponse.json({ ...me, permissions: ["role.assign"] } satisfies MeResponse)),
      http.get(USER, () => HttpResponse.json(adminUser))
    )
    render(<AdminUserDetail userId={adminUser.userId} />)
    expect(await screen.findByRole("button", { name: "Đổi vai trò" }, CHO)).toBeInTheDocument()
    expect(screen.queryByRole("button", { name: "Khóa tài khoản" })).toBeNull()
  })
})
