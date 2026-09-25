import { act, render, screen, waitFor } from "@testing-library/react"
import { http, HttpResponse } from "msw"
import { describe, expect, it } from "vitest"

import { BFF_URL } from "@/lib/api/config"
import type { MeResponse } from "@/lib/api/types"
import { me } from "@/mocks/fixtures"
import { server } from "@/mocks/node"

import { ModerationNavLink } from "./moderation-nav-link"

// GĐ6 E2 — "Kiểm duyệt" theo `report.resolve`, không theo tên vai trò (Đ-6.11). E2E-06 quay đúng cảnh này trên staging.

const ME = `${BFF_URL}/api/me`
const CHO = { timeout: 5000 }

function meCo(permissions: string[], role = "USER") {
  server.use(
    http.get(ME, () =>
      HttpResponse.json({ ...me, role, permissions } satisfies MeResponse)
    )
  )
}

describe("ModerationNavLink", () => {
  it("được nâng quyền → focus lại tab → liên kết hiện, không tải lại trang", async () => {
    meCo(me.permissions)
    render(<ModerationNavLink />)
    await waitFor(
      () => expect(screen.queryByTestId("nav-moderation")).toBeNull(),
      CHO
    )

    meCo([...me.permissions, "report.resolve"], "MODERATOR")
    act(() => {
      window.dispatchEvent(new Event("focus"))
    })

    expect(
      await screen.findByTestId("nav-moderation", {}, CHO)
    ).toHaveAttribute("href", "/moderation")
  })

  it("vai trò tên MODERATOR mà không có report.resolve → KHÔNG hiện (không suy từ tên vai trò)", async () => {
    meCo(me.permissions, "MODERATOR")
    render(<ModerationNavLink />)
    // Có một nhịp để /me về; sau đó liên kết vẫn không có.
    await new Promise((r) => setTimeout(r, 100))
    expect(screen.queryByTestId("nav-moderation")).toBeNull()
  })

  it("vai trò tự tạo REVIEWER có report.resolve → hiện", async () => {
    meCo(["report.resolve"], "REVIEWER")
    render(<ModerationNavLink />)
    expect(
      await screen.findByTestId("nav-moderation", {}, CHO)
    ).toBeInTheDocument()
  })
})
