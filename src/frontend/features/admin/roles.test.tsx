import { render, screen, waitFor, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { http, HttpResponse } from "msw"
import { StrictMode } from "react"
import { beforeEach, describe, expect, it, vi } from "vitest"

import { BFF_URL } from "@/lib/api/config"
import type { RoleSummary, SetRolePermissionsRequest } from "@/lib/api/types"
import { permissionInfos, roleSummaries } from "@/mocks/fixtures"
import { server } from "@/mocks/node"

import { RoleEditor } from "./role-editor"
import { RolesScreen } from "./roles-screen"

// GĐ6 E8 — vai trò + ma trận quyền (Đ-6.9, Đ-6.21): hộp thoại xác nhận hiện ĐÚNG số server trả rồi gửi lại `confirm: true`; ADMIN
// chỉ đọc; nút xóa ẩn với vai trò hệ thống nhưng 409 vẫn được xử lý theo `type`.

const push = vi.fn()
vi.mock("next/navigation", () => ({ useRouter: () => ({ push }) }))

const API = `${BFF_URL}/api/admin`
const CHO = { timeout: 5000 }
const PROBLEM = { "Content-Type": "application/problem+json" }
const [USER_ROLE, , ADMIN_ROLE, REVIEWER] = roleSummaries

function phucVu(roles: RoleSummary[] = roleSummaries) {
  server.use(
    http.get(`${API}/roles`, () => HttpResponse.json(roles)),
    http.get(`${API}/permissions`, () => HttpResponse.json(permissionInfos))
  )
}

beforeEach(() => push.mockReset())

describe("RolesScreen", () => {
  it("mỗi vai trò: số người mang, nhãn 'Hệ thống' chỉ cho vai trò hệ thống", async () => {
    phucVu()
    render(<RolesScreen />)
    const rows = await screen.findAllByTestId("role-row", {}, CHO)
    expect(rows).toHaveLength(4)
    expect(within(rows[0]).getByText(/8\.421 người/)).toBeInTheDocument()
    expect(within(rows[0]).getByText("Hệ thống")).toBeInTheDocument()
    expect(within(rows[3]).queryByText("Hệ thống")).toBeNull()
  })

  it("tạo: mã sai luật → câu của server, KHÔNG gọi API", async () => {
    phucVu()
    let posted = false
    server.use(http.post(`${API}/roles`, () => ((posted = true), HttpResponse.json({}))))
    const user = userEvent.setup()
    render(<RolesScreen />)
    await user.click(await screen.findByRole("button", { name: "Tạo vai trò" }, CHO))
    await user.type(screen.getByLabelText("Mã vai trò"), "reviewer")
    await user.type(screen.getByLabelText("Tên hiển thị"), "Người rà soát")
    await user.click(screen.getByRole("button", { name: "Tạo" }))
    expect(
      await screen.findByText("Mã vai trò gồm 3–30 ký tự A–Z, 0–9, _ và bắt đầu bằng chữ cái in hoa.", {}, CHO)
    ).toBeInTheDocument()
    expect(posted).toBe(false)
  })
})

describe("StrictMode", () => {
  it("danh sách vai trò và ma trận vẫn nạp xong", async () => {
    phucVu()
    render(
      <StrictMode>
        <RolesScreen />
        <RoleEditor roleId={REVIEWER.roleId} />
      </StrictMode>
    )
    expect(await screen.findAllByTestId("role-row", {}, CHO)).toHaveLength(4)
    expect(await screen.findByTestId("permission-matrix", {}, CHO)).toBeInTheDocument()
  })
})

describe("RoleEditor", () => {
  it("gỡ post.create của USER → 409 confirmation-required → hộp thoại hiện ĐÚNG số server trả → gửi lại confirm: true", async () => {
    phucVu()
    const bodies: SetRolePermissionsRequest[] = []
    server.use(
      http.put(`${API}/roles/:id/permissions`, async ({ request }) => {
        const body = (await request.json()) as SetRolePermissionsRequest
        bodies.push(body)
        if (!body.confirm)
          return HttpResponse.json(
            {
              type: "urn:socialapp:problem:confirmation-required",
              title: "Cần xác nhận",
              status: 409,
              traceId: "t",
              added: [],
              removed: ["post.create"],
              affectedUsers: 8421,
            },
            { status: 409, headers: PROBLEM }
          )
        return HttpResponse.json({ ...USER_ROLE, permissions: body.permissions })
      })
    )
    const user = userEvent.setup()
    render(<RoleEditor roleId={USER_ROLE.roleId} />)
    await user.click(await screen.findByRole("checkbox", { name: /post\.create/ }, CHO))
    await user.click(screen.getByRole("button", { name: "Lưu quyền" }))

    const dialog = await screen.findByTestId("confirm-permissions", {}, CHO)
    expect(dialog).toHaveTextContent("8.421 tài khoản bị ảnh hưởng ngay lập tức.")
    expect(within(dialog).getByTestId("confirm-removed")).toHaveTextContent("post.create")
    expect(within(dialog).queryByTestId("confirm-added")).toBeNull()

    await user.click(within(dialog).getByRole("button", { name: "Tiếp tục" }))
    await waitFor(() => expect(bodies).toHaveLength(2), CHO)
    expect(bodies[0].confirm).toBeUndefined()
    expect(bodies[1]).toMatchObject({ confirm: true })
    expect(bodies[1].permissions).not.toContain("post.create")
    expect(await screen.findByText(/Đã lưu\. Quyền mới có hiệu lực/, {}, CHO)).toBeInTheDocument()
  })

  it("ADMIN: chỉ đọc — mọi ô khóa, không nút Lưu, không nút Xóa", async () => {
    phucVu()
    render(<RoleEditor roleId={ADMIN_ROLE.roleId} />)
    expect(await screen.findByTestId("role-readonly", {}, CHO)).toBeInTheDocument()
    for (const box of screen.getAllByRole("checkbox")) expect(box).toHaveAttribute("aria-disabled", "true")
    expect(screen.queryByRole("button", { name: "Lưu quyền" })).toBeNull()
    expect(screen.queryByRole("button", { name: "Xóa vai trò" })).toBeNull()
  })

  it("role.manage không bao giờ tích được (assignable: false)", async () => {
    phucVu()
    render(<RoleEditor roleId={REVIEWER.roleId} />)
    expect(await screen.findByRole("checkbox", { name: /role\.manage/ }, CHO)).toHaveAttribute("aria-disabled", "true")
  })

  it("xóa vai trò tự tạo còn người mang → 409 role-in-use → câu theo type, ở lại trang", async () => {
    phucVu()
    server.use(
      http.delete(`${API}/roles/:id`, () =>
        HttpResponse.json(
          { type: "urn:socialapp:problem:role-in-use", title: "Xung đột dữ liệu", status: 409, traceId: "t" },
          { status: 409, headers: PROBLEM }
        )
      )
    )
    const user = userEvent.setup()
    render(<RoleEditor roleId={REVIEWER.roleId} />)
    await user.click(await screen.findByRole("button", { name: "Xóa vai trò" }, CHO))
    await user.click(await screen.findByRole("button", { name: "Xóa" }, CHO))
    expect(await screen.findByText("Vai trò đang có người dùng, không xóa được.", {}, CHO)).toBeInTheDocument()
    expect(push).not.toHaveBeenCalled()
  })

  it("vai trò hệ thống: không có nút Xóa", async () => {
    phucVu()
    render(<RoleEditor roleId={USER_ROLE.roleId} />)
    await screen.findByRole("button", { name: "Lưu quyền" }, CHO)
    expect(screen.queryByRole("button", { name: "Xóa vai trò" })).toBeNull()
  })
})
