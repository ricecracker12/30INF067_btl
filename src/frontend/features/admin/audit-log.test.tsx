import { render, screen, waitFor, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { http, HttpResponse } from "msw"
import { StrictMode } from "react"
import { describe, expect, it } from "vitest"

import { BFF_URL } from "@/lib/api/config"
import type { AuditLogPage } from "@/lib/api/types"
import { auditLogItem, reportId, userId } from "@/mocks/fixtures"
import { server } from "@/mocks/node"

import { AuditLog, metadataValue } from "./audit-log"

// GĐ6 E9 — nhật ký kiểm toán: `metadata` dạng khóa–giá trị, lọc theo người thao tác / đối tượng, cursor.

const LOGS = `${BFF_URL}/api/admin/audit-logs`
const CHO = { timeout: 5000 }

describe("AuditLog", () => {
  it("một dòng: nhãn hành động, người thao tác, đối tượng, IP, metadata khóa–giá trị", async () => {
    server.use(
      http.get(LOGS, () => HttpResponse.json({ items: [auditLogItem], nextCursor: null } satisfies AuditLogPage))
    )
    render(<AuditLog />)
    const row = await screen.findByTestId("audit-item", {}, CHO)
    expect(within(row).getByText("Ẩn nội dung bị báo cáo")).toBeInTheDocument()
    expect(within(row).getByText("Kiểm duyệt viên")).toBeInTheDocument()
    expect(within(row).getByText("203.0.113.7")).toBeInTheDocument()
    const meta = within(row).getAllByTestId("audit-metadata").map((m) => m.textContent)
    expect(meta).toEqual([`reportIds${reportId}`, "reasonCodespam", "note—"])
  })

  it("lọc theo người thao tác + đối tượng: targetId chỉ gửi khi có targetType; Xem thêm theo cursor", async () => {
    const queries: string[] = []
    server.use(
      http.get(LOGS, ({ request }) => {
        const q = new URL(request.url).searchParams
        queries.push(q.toString())
        return HttpResponse.json({
          items: [{ ...auditLogItem, id: q.get("cursor") ? 1 : 2 }],
          nextCursor: q.get("cursor") ? null : "c2",
        } satisfies AuditLogPage)
      })
    )
    const user = userEvent.setup()
    render(<AuditLog />)
    await screen.findByTestId("audit-item", {}, CHO)

    await user.type(screen.getByLabelText("Id người thao tác"), userId)
    await user.type(screen.getByLabelText("Id đối tượng"), "x-khong-co-loai")
    await user.click(screen.getByRole("button", { name: "Lọc" }))
    await waitFor(() => expect(queries.at(-1)).toBe(`actorId=${userId}&limit=20`), CHO)

    await user.type(screen.getByLabelText("Loại đối tượng"), "post")
    await user.click(screen.getByRole("button", { name: "Lọc" }))
    await waitFor(
      () => expect(queries.at(-1)).toBe(`actorId=${userId}&targetType=post&targetId=x-khong-co-loai&limit=20`),
      CHO
    )

    await user.click(await screen.findByRole("button", { name: "Xem thêm" }, CHO))
    await waitFor(() => expect(screen.getAllByTestId("audit-item")).toHaveLength(2), CHO)
  })
})

describe("AuditLog — StrictMode", () => {
  it("vẫn nạp xong trang đầu", async () => {
    server.use(
      http.get(LOGS, () => HttpResponse.json({ items: [auditLogItem], nextCursor: null } satisfies AuditLogPage))
    )
    render(
      <StrictMode>
        <AuditLog />
      </StrictMode>
    )
    expect(await screen.findByTestId("audit-item", {}, CHO)).toBeInTheDocument()
  })
})

describe("metadataValue", () => {
  it("mảng, object, null, số", () => {
    expect(metadataValue(["a", "b"])).toBe("a, b")
    expect(metadataValue({ x: 1 })).toBe('{"x":1}')
    expect(metadataValue(null)).toBe("—")
    expect(metadataValue(true)).toBe("true")
  })
})
