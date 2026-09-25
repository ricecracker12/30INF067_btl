import { act, render, screen, waitFor } from "@testing-library/react"
import { http, HttpResponse } from "msw"
import { describe, expect, it, vi } from "vitest"

import { BFF_URL } from "@/lib/api/config"
import type { MeResponse } from "@/lib/api/types"
import { me } from "@/mocks/fixtures"
import { server } from "@/mocks/node"

import { RequirePermission } from "./require-permission"

// GĐ6 E2 — guard mềm theo quyền hiệu lực (Đ-6.11, Đ-6.20). Server vẫn là nơi chặn thật: test này chỉ kiểm cái người dùng THẤY.

vi.mock("next/navigation", () => ({ usePathname: () => "/moderation" }))

const ME = `${BFF_URL}/api/me`
const CHO = { timeout: 5000 }

/** `/me` trả tập quyền cho trước — đổi giữa ca là "Admin vừa đổi quyền của bạn". */
function meCo(permissions: string[]) {
  server.use(
    http.get(ME, () =>
      HttpResponse.json({ ...me, permissions } satisfies MeResponse)
    )
  )
}

function moTrang() {
  render(
    <RequirePermission anyOf={["report.resolve"]}>
      <p>Hàng đợi</p>
    </RequirePermission>
  )
}

describe("RequirePermission", () => {
  it("thiếu quyền → trang 'không có quyền', KHÔNG redirect im lặng", async () => {
    meCo(me.permissions)
    moTrang()
    expect(
      await screen.findByText("Bạn không có quyền xem trang này.", {}, CHO)
    ).toBeInTheDocument()
    expect(screen.queryByText("Hàng đợi")).not.toBeInTheDocument()
  })

  it("có quyền (kể cả vai trò tự tạo chỉ có report.resolve) → vào được", async () => {
    meCo(["report.resolve"])
    moTrang()
    expect(await screen.findByText("Hàng đợi", {}, CHO)).toBeInTheDocument()
  })

  it("bị hạ quyền giữa lúc xem: focus lại tab → trang 'không có quyền', không tải lại (Mục 7.3)", async () => {
    meCo([...me.permissions, "report.resolve"])
    moTrang()
    await screen.findByText("Hàng đợi", {}, CHO)

    meCo(me.permissions)
    act(() => {
      window.dispatchEvent(new Event("focus"))
    })

    await waitFor(
      () => expect(screen.getByTestId("no-permission")).toBeInTheDocument(),
      CHO
    )
  })

  it("chưa nạp xong /me → khung chờ, không nháy nội dung", () => {
    meCo(["report.resolve"])
    moTrang()
    expect(screen.getByRole("status", { name: "Đang tải" })).toBeInTheDocument()
    expect(screen.queryByText("Hàng đợi")).not.toBeInTheDocument()
  })
})
