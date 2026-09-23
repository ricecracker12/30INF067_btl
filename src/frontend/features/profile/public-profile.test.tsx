import { render, screen, waitFor } from "@testing-library/react"
import { http, HttpResponse } from "msw"
import { StrictMode } from "react"
import { beforeEach, describe, expect, it } from "vitest"

import { BFF_URL } from "@/lib/api/config"
import { profile, userId, userIdChuaCoHoSo } from "@/mocks/fixtures"
import { server } from "@/mocks/node"
import { fakeSession } from "@/mocks/session"

import { PublicProfile } from "./public-profile"

// File này sinh ra CHỈ để giữ ca StrictMode: `PublicProfile` là màn duy nhất sở hữu một `AbortController`
// mà trước đó không có file test nào. Nhánh 404 và nhánh lỗi của nó đã có spec Playwright canh (E7), nên
// ở đây không chép lại — thêm ca vào đây là hợp lệ, nhưng đừng thêm chỉ để file trông dày hơn.
//
// Ca StrictMode — mỗi màn sở hữu tài nguyên hủy được có ĐÚNG một ca. `render(<X />)` gắn component một
// lần, còn Next dev bọc `<StrictMode>`: mount → unmount → mount lại. Ca này khẳng định TRẠNG THÁI CUỐI
// đạt được, KHÔNG đếm số request — dưới StrictMode số request tăng gấp đôi một cách hợp lệ, trộn hai thứ
// vào một ca là tự làm ca test giòn.

beforeEach(() => {
  fakeSession.start()
})

describe("PublicProfile — sống được dưới StrictMode", () => {
  it("mount hai lần vẫn hiện hồ sơ, không kẹt ở khung chờ", async () => {
    render(
      <StrictMode>
        <PublicProfile userId={userId} />
      </StrictMode>
    )

    expect(await screen.findByTestId("public-profile")).toBeInTheDocument()
    expect(screen.getByTestId("public-display-name")).toHaveTextContent(
      profile.displayName
    )
  })
})

// GĐ4 E5: slot `actions` (nút quan hệ do `app/` truyền). Chỉ ở nhánh ĐÃ NẠP — không có nút Kết bạn nào trỏ vào hư không.
describe("PublicProfile — slot actions (GĐ4 E5)", () => {
  const NUT = (p: { displayName: string }) => (
    <button>Kết bạn với {p.displayName}</button>
  )

  it("hồ sơ nạp được → slot hiện cạnh tên, nhận ĐÚNG hồ sơ đã nạp", async () => {
    render(<PublicProfile userId={userId} actions={NUT} />)

    expect(
      await screen.findByTestId("public-profile-actions")
    ).toContainElement(
      screen.getByRole("button", { name: `Kết bạn với ${profile.displayName}` })
    )
  })

  it("404 (không có hồ sơ) → KHÔNG có slot", async () => {
    render(<PublicProfile userId={userIdChuaCoHoSo} actions={NUT} />)

    expect(await screen.findByTestId("profile-not-found")).toBeInTheDocument()
    expect(screen.queryByRole("button", { name: /Kết bạn/ })).toBeNull()
  })

  it("lỗi nạp → KHÔNG có slot (chỉ Thử lại)", async () => {
    server.use(
      http.get(`${BFF_URL}/api/users/:userId/profile`, () =>
        HttpResponse.json(
          { type: "t", title: "t", status: 500, traceId: "abc" },
          {
            status: 500,
            headers: { "Content-Type": "application/problem+json" },
          }
        )
      )
    )
    render(<PublicProfile userId={userId} actions={NUT} />)

    await waitFor(() =>
      expect(
        screen.getByRole("button", { name: "Thử lại" })
      ).toBeInTheDocument()
    )
    expect(screen.queryByRole("button", { name: /Kết bạn/ })).toBeNull()
  })

  it("không truyền actions (hồ sơ của chính mình) → không có vùng slot rỗng", async () => {
    render(<PublicProfile userId={userId} />)

    await screen.findByTestId("public-profile")
    expect(screen.queryByTestId("public-profile-actions")).toBeNull()
  })
})
