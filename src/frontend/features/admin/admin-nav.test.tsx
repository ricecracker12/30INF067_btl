import { act, render, screen, waitFor } from "@testing-library/react"
import { http, HttpResponse } from "msw"
import { describe, expect, it, vi } from "vitest"

import { BFF_URL } from "@/lib/api/config"
import type { MeResponse } from "@/lib/api/types"
import { me } from "@/mocks/fixtures"
import { server } from "@/mocks/node"

import { AdminNavLink, AdminSectionNav } from "./admin-nav"

// GĐ6 E2 — liên kết "Quản trị" theo `permissions` (Đ-6.20): đổi quyền giả → hiện/ẩn KHÔNG tải lại trang (focus tab nạp lại /me).

vi.mock("next/navigation", () => ({ usePathname: () => "/admin/audit" }))

const ME = `${BFF_URL}/api/me`
const CHO = { timeout: 5000 }

function meCo(permissions: string[]) {
  server.use(
    http.get(ME, () =>
      HttpResponse.json({ ...me, permissions } satisfies MeResponse)
    )
  )
}

const focusTab = () =>
  act(() => {
    window.dispatchEvent(new Event("focus"))
  })

describe("AdminNavLink", () => {
  it("USER: không có liên kết; được trao audit.read → focus lại tab → liên kết tới ĐÚNG mục được phép", async () => {
    meCo(me.permissions)
    render(<AdminNavLink />)
    // Chờ /me về rồi mới khẳng định "không có" — khẳng định trước lúc nạp là xanh giả.
    await waitFor(() => expect(screen.queryByTestId("nav-admin")).toBeNull(), CHO)

    meCo([...me.permissions, "audit.read"])
    focusTab()

    const link = await screen.findByTestId("nav-admin", {}, CHO)
    expect(link).toHaveAttribute("href", "/admin/audit")
  })

  it("chỉ role.assign → dẫn tới danh sách tài khoản (policy any-of)", async () => {
    meCo(["role.assign"])
    render(<AdminNavLink />)
    expect(await screen.findByTestId("nav-admin", {}, CHO)).toHaveAttribute(
      "href",
      "/admin/users"
    )
  })

  it("bị gỡ hết quyền quản trị → focus lại tab → liên kết biến mất", async () => {
    meCo(["user.lock", "role.manage", "audit.read"])
    render(<AdminNavLink />)
    await screen.findByTestId("nav-admin", {}, CHO)

    meCo(me.permissions)
    focusTab()
    await waitFor(() => expect(screen.queryByTestId("nav-admin")).toBeNull(), CHO)
  })
})

describe("AdminSectionNav", () => {
  it("chỉ hiện mục được vào; mục đang mở có aria-current", async () => {
    meCo(["audit.read", "role.manage"])
    render(<AdminSectionNav />)
    const audit = await screen.findByRole("link", { name: "Nhật ký" }, CHO)
    expect(audit).toHaveAttribute("aria-current", "page")
    expect(screen.getByRole("link", { name: "Vai trò" })).toBeInTheDocument()
    expect(screen.queryByRole("link", { name: "Tài khoản" })).toBeNull()
  })
})
