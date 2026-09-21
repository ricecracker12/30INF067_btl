import { render, screen } from "@testing-library/react"
import { StrictMode } from "react"
import { beforeEach, describe, expect, it } from "vitest"

import { profile, userId } from "@/mocks/fixtures"
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
