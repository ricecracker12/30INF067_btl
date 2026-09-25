import { render, screen, waitFor, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { http, HttpResponse } from "msw"
import { StrictMode } from "react"
import { beforeEach, describe, expect, it, vi } from "vitest"

import { BFF_URL } from "@/lib/api/config"
import type {
  DecideReportRequest,
  MeResponse,
  ReportDetail,
  ReportQueuePage,
} from "@/lib/api/types"
import { me, reportDetail, reportId, reportQueueItem } from "@/mocks/fixtures"
import { server } from "@/mocks/node"

import { ModerationQueue } from "./moderation-queue"
import { ReportDetailScreen } from "./report-detail"

// GĐ6 E6 — màn kiểm duyệt: hàng đợi theo đối tượng; quyết định KHÔNG optimistic (Đ-6.21); nút ẩn chỉ khi có `post.hide`
// (Đ-6.13); 409 `already-decided` → về hàng đợi (nạp mới) + câu "vừa được người khác xử lý"; 403 → nạp lại `/me`.

const replace = vi.fn()
const router = { replace }
vi.mock("next/navigation", () => ({ useRouter: () => router }))

const API = `${BFF_URL}/api`
const CHO = { timeout: 5000 }
const PROBLEM = { "Content-Type": "application/problem+json" }

const problem = (status: number, type = `https://httpstatuses.io/${status}`) =>
  HttpResponse.json({ type, title: "x", status, traceId: "t" }, { status, headers: PROBLEM })

function meCo(permissions: string[]) {
  const seen: string[] = []
  server.use(
    http.get(`${API}/me`, () => {
      seen.push("GET")
      return HttpResponse.json({ ...me, permissions } satisfies MeResponse)
    })
  )
  return seen
}

function chiTiet(detail: ReportDetail = reportDetail) {
  server.use(http.get(`${API}/reports/:id`, () => HttpResponse.json(detail)))
}

function quyetDinh(respond: () => Response) {
  const bodies: DecideReportRequest[] = []
  server.use(
    http.patch(`${API}/reports/:id`, async ({ request }) => {
      bodies.push((await request.json()) as DecideReportRequest)
      return respond()
    })
  )
  return bodies
}

const MOD = [...me.permissions, "report.resolve", "post.hide"]

beforeEach(() => replace.mockReset())

describe("ModerationQueue", () => {
  it("mỗi đối tượng một dòng: loại, số báo cáo, lý do nổi bật; dẫn tới chi tiết", async () => {
    server.use(
      http.get(`${API}/reports`, () =>
        HttpResponse.json({ items: [reportQueueItem], nextCursor: null } satisfies ReportQueuePage)
      )
    )
    render(<ModerationQueue />)
    const row = await screen.findByTestId("queue-item", {}, CHO)
    expect(row).toHaveAttribute("href", `/moderation/${reportId}`)
    expect(within(row).getByText("Bài viết")).toBeInTheDocument()
    expect(within(row).getByText("3 báo cáo")).toBeInTheDocument()
    expect(within(row).getByText(/Spam hoặc quảng cáo/)).toBeInTheDocument()
  })

  it("StrictMode: hàng đợi vẫn nạp xong", async () => {
    server.use(
      http.get(`${API}/reports`, () =>
        HttpResponse.json({ items: [reportQueueItem], nextCursor: null } satisfies ReportQueuePage)
      )
    )
    render(
      <StrictMode>
        <ModerationQueue />
      </StrictMode>
    )
    expect(await screen.findByTestId("queue-item", {}, CHO)).toBeInTheDocument()
  })

  it("?notice=decided → câu 'vừa được người khác xử lý'", async () => {
    server.use(http.get(`${API}/reports`, () => HttpResponse.json({ items: [], nextCursor: null })))
    render(<ModerationQueue notice="decided" />)
    expect(screen.getByTestId("queue-notice")).toHaveTextContent(
      "Báo cáo này vừa được người khác xử lý."
    )
    expect(await screen.findByTestId("queue-empty", {}, CHO)).toBeInTheDocument()
  })

  it("403 (vừa bị hạ quyền) → nạp lại /me để guard đổi trang", async () => {
    const hoiMe = meCo(me.permissions)
    server.use(http.get(`${API}/reports`, () => problem(403)))
    render(<ModerationQueue />)
    expect(
      await screen.findByText("Bạn không còn quyền xem hàng đợi kiểm duyệt.", {}, CHO)
    ).toBeInTheDocument()
    await waitFor(() => expect(hoiMe.length).toBeGreaterThan(0), CHO)
  })
})

describe("ReportDetailScreen", () => {
  it("ảnh chụp đối tượng + báo cáo mở; KHÔNG có danh tính người báo", async () => {
    meCo(MOD)
    chiTiet()
    render(<ReportDetailScreen reportId={reportId} />)
    const snap = await screen.findByTestId("target-snapshot", {}, CHO)
    expect(within(snap).getByText(reportDetail.target.body!)).toBeInTheDocument()
    expect(screen.getByTestId("open-reports")).toHaveTextContent("Spam hoặc quảng cáo")
  })

  it("StrictMode: chi tiết vẫn nạp xong", async () => {
    meCo(MOD)
    chiTiet()
    render(
      <StrictMode>
        <ReportDetailScreen reportId={reportId} />
      </StrictMode>
    )
    expect(await screen.findByTestId("target-snapshot", {}, CHO)).toBeInTheDocument()
  })

  it("có post.hide: 'Ẩn nội dung' gửi lý do mặc định = lý do báo nhiều nhất; 200 → về hàng đợi", async () => {
    meCo(MOD)
    chiTiet()
    const bodies = quyetDinh(() =>
      HttpResponse.json({ decision: "hide", closedReportIds: [reportId], targetStatus: "hidden" })
    )
    const user = userEvent.setup()
    render(<ReportDetailScreen reportId={reportId} />)
    await user.click(await screen.findByRole("button", { name: "Ẩn nội dung" }, CHO))
    await user.click(screen.getByRole("button", { name: /Xác nhận/ }))

    await waitFor(() => expect(replace).toHaveBeenCalledWith("/moderation?notice=done"), CHO)
    expect(bodies).toEqual([{ decision: "hide", reasonCode: "spam" }])
  })

  it("vai trò chỉ có report.resolve (REVIEWER): KHÔNG có nút ẩn, vẫn bỏ qua được", async () => {
    meCo(["report.resolve"])
    chiTiet()
    render(<ReportDetailScreen reportId={reportId} />)
    expect(await screen.findByRole("button", { name: "Bỏ qua" }, CHO)).toBeInTheDocument()
    expect(screen.queryByRole("button", { name: "Ẩn nội dung" })).toBeNull()
  })

  it("409 already-decided → về hàng đợi với câu 'vừa được người khác xử lý' — không vẽ thành công trước", async () => {
    meCo(MOD)
    chiTiet()
    quyetDinh(() => problem(409, "urn:socialapp:problem:report-already-decided"))
    const user = userEvent.setup()
    render(<ReportDetailScreen reportId={reportId} />)
    await user.click(await screen.findByRole("button", { name: "Bỏ qua" }, CHO))
    await user.click(screen.getByRole("button", { name: /Xác nhận/ }))
    await waitFor(() => expect(replace).toHaveBeenCalledWith("/moderation?notice=decided"), CHO)
  })

  it("409 target-gone → câu tại chỗ, ở lại (báo cáo còn mở để bỏ qua)", async () => {
    meCo(MOD)
    chiTiet()
    quyetDinh(() => problem(409, "urn:socialapp:problem:moderation-target-gone"))
    const user = userEvent.setup()
    render(<ReportDetailScreen reportId={reportId} />)
    await user.click(await screen.findByRole("button", { name: "Ẩn nội dung" }, CHO))
    await user.click(screen.getByRole("button", { name: /Xác nhận/ }))
    expect(
      await screen.findByText("Nội dung bị báo cáo không còn tồn tại.", {}, CHO)
    ).toBeInTheDocument()
    expect(replace).not.toHaveBeenCalled()
  })

  it("báo cáo người dùng: 'Đã xử lý' bắt buộc ghi chú, không gọi API khi trống", async () => {
    meCo(MOD)
    chiTiet({
      ...reportDetail,
      target: { ...reportDetail.target, type: "user", status: "active", body: null, postId: null },
    })
    const bodies = quyetDinh(() => HttpResponse.json({}))
    const user = userEvent.setup()
    render(<ReportDetailScreen reportId={reportId} />)
    expect(screen.queryByRole("button", { name: "Ẩn nội dung" })).toBeNull()
    await user.click(await screen.findByRole("button", { name: "Đã xử lý" }, CHO))
    await user.click(screen.getByRole("button", { name: /Xác nhận/ }))
    expect(await screen.findByText("Vui lòng ghi chú cách đã xử lý.", {}, CHO)).toBeInTheDocument()
    expect(bodies).toHaveLength(0)
  })

  it("đối tượng đang bị ẩn + có post.hide → nút khôi phục, gọi đúng đường", async () => {
    meCo(MOD)
    chiTiet({
      ...reportDetail,
      openReports: [],
      target: { ...reportDetail.target, status: "hidden" },
    })
    let path = ""
    server.use(
      http.post(`${API}/moderation/targets/:type/:id/restore`, ({ request }) => {
        path = new URL(request.url).pathname
        return HttpResponse.json({ targetType: "post", targetId: reportDetail.target.id, targetStatus: "published" })
      })
    )
    const user = userEvent.setup()
    render(<ReportDetailScreen reportId={reportId} />)
    await user.click(await screen.findByRole("button", { name: "Khôi phục nội dung" }, CHO))
    await waitFor(() => expect(path).toBe(`/bff/api/moderation/targets/post/${reportDetail.target.id}/restore`), CHO)
    expect(screen.queryByTestId("decision-panel")).toBeNull()
  })
})
