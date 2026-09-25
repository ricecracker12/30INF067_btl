import { render, screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { http, HttpResponse } from "msw"
import { describe, expect, it } from "vitest"

import { BFF_URL } from "@/lib/api/config"
import type { CreateReportRequest } from "@/lib/api/types"
import { reportedPostId, reportReceipt } from "@/mocks/fixtures"
import { server } from "@/mocks/node"

import { REPORT_THANKS, ReportButton } from "./report-dialog"

// GĐ6 E5 — hộp thoại báo cáo (Mục 7.1): năm lý do, "Khác" bắt buộc mô tả; 201 và 200 CÙNG câu cảm ơn; 404 → "Nội dung này không
// còn nữa" (một câu cho "không tồn tại" và "không thấy được", Đ-6.12).

const REPORTS = `${BFF_URL}/api/reports`
const CHO = { timeout: 5000 }
const PROBLEM = { "Content-Type": "application/problem+json" }

function phucVu(status: number) {
  const bodies: CreateReportRequest[] = []
  server.use(
    http.post(REPORTS, async ({ request }) => {
      bodies.push((await request.json()) as CreateReportRequest)
      if (status >= 400)
        return HttpResponse.json(
          { type: `https://httpstatuses.io/${status}`, title: "x", status, traceId: "t" },
          { status, headers: PROBLEM }
        )
      return HttpResponse.json(reportReceipt, { status })
    })
  )
  return bodies
}

async function moVaChon(label: string) {
  const user = userEvent.setup()
  render(<ReportButton targetType="post" targetId={reportedPostId} />)
  await user.click(screen.getByTestId("report-button"))
  await user.click(await screen.findByRole("radio", { name: label }, CHO))
  return user
}

describe("ReportButton", () => {
  it.each([201, 200])("%i → câu cảm ơn giống nhau; body đúng hợp đồng, không gửi detail rỗng", async (status) => {
    const bodies = phucVu(status)
    const user = await moVaChon("Spam hoặc quảng cáo")
    await user.click(screen.getByRole("button", { name: "Gửi báo cáo" }))

    expect(await screen.findByTestId("report-thanks", {}, CHO)).toHaveTextContent(REPORT_THANKS)
    expect(bodies).toEqual([
      { targetType: "post", targetId: reportedPostId, reasonCode: "spam" },
    ])
  })

  it("'Khác' không mô tả → câu của server, KHÔNG gọi API", async () => {
    const bodies = phucVu(201)
    const user = await moVaChon("Lý do khác")
    await user.click(screen.getByRole("button", { name: "Gửi báo cáo" }))

    expect(await screen.findByText("Vui lòng mô tả lý do.", {}, CHO)).toBeInTheDocument()
    expect(bodies).toHaveLength(0)

    await user.type(screen.getByLabelText("Mô tả (bắt buộc)"), "  Lừa đảo  ")
    await user.click(screen.getByRole("button", { name: "Gửi báo cáo" }))
    await screen.findByTestId("report-thanks", {}, CHO)
    expect(bodies[0]).toMatchObject({ reasonCode: "other", detail: "Lừa đảo" })
  })

  it("chưa chọn lý do → nhắc, không gọi API", async () => {
    const bodies = phucVu(201)
    const user = userEvent.setup()
    render(<ReportButton targetType="user" targetId={reportedPostId} />)
    await user.click(screen.getByTestId("report-button"))
    await user.click(await screen.findByRole("button", { name: "Gửi báo cáo" }, CHO))
    expect(await screen.findByText("Lý do báo cáo không hợp lệ.", {}, CHO)).toBeInTheDocument()
    expect(bodies).toHaveLength(0)
  })

  it("404 → 'Nội dung này không còn nữa.'", async () => {
    phucVu(404)
    const user = await moVaChon("Bạo lực hoặc đe dọa")
    await user.click(screen.getByRole("button", { name: "Gửi báo cáo" }))
    expect(await screen.findByText("Nội dung này không còn nữa.", {}, CHO)).toBeInTheDocument()
    expect(screen.queryByTestId("report-thanks")).toBeNull()
  })

  it("429 → câu riêng của báo cáo", async () => {
    phucVu(429)
    const user = await moVaChon("Spam hoặc quảng cáo")
    await user.click(screen.getByRole("button", { name: "Gửi báo cáo" }))
    expect(
      await screen.findByText("Bạn đã gửi quá nhiều báo cáo. Thử lại sau ít phút.", {}, CHO)
    ).toBeInTheDocument()
  })
})
