import { render, screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { delay, http, HttpResponse } from "msw"
import { StrictMode } from "react"
import { beforeEach, describe, expect, it } from "vitest"

import { BFF_URL } from "@/lib/api/config"
import type { NotificationPage, NotificationResponse } from "@/lib/api/types"
import { notificationComment, notificationModeration } from "@/mocks/fixtures"
import { server } from "@/mocks/node"

import { NotificationList } from "./notification-list"
import { refreshUnread, unreadStore } from "./unread-store"

// GĐ6 E3 — màn `/notifications`: đánh dấu đã đọc OPTIMISTIC có rollback (Đ-6.21), "đánh dấu tất cả" với `upTo` = nhóm mới nhất đang
// hiển thị (notification-v1), nối trang khử trùng theo id.

const API = `${BFF_URL}/api/notifications`
const CHO = { timeout: 5000 }

const unreadComment: NotificationResponse = { ...notificationComment, isRead: false }

function trang(pages: Record<string, NotificationPage>) {
  server.use(
    http.get(API, ({ request }) => {
      const page = pages[new URL(request.url).searchParams.get("cursor") ?? "dau"]
      if (!page) throw new Error("cursor lạ")
      return HttpResponse.json(page)
    }),
    http.get(`${API}/unread-count`, () => HttpResponse.json({ total: 1 }))
  )
}

const dong = (id: string) =>
  screen
    .getAllByTestId("notification-item")
    .find((el) => el.dataset.notificationId === id)!

// Chặn điều hướng thật của jsdom khi bấm `<a>` — ca này kiểm trạng thái đọc, không kiểm router.
beforeEach(() => {
  document.addEventListener("click", (e) => e.preventDefault(), { once: true })
  unreadStore.reset()
})

describe("NotificationList", () => {
  it("bấm: chấm tắt NGAY (trước khi server trả lời), badge −1", async () => {
    trang({ dau: { items: [unreadComment], nextCursor: null } })
    let resolve: () => void = () => undefined
    server.use(
      http.post(`${API}/:id/read`, async () => {
        await new Promise<void>((r) => (resolve = r))
        return new HttpResponse(null, { status: 204 })
      })
    )
    // Badge đã biết số (1) — store chỉ trừ khi đã có số thật.
    await refreshUnread()
    expect(unreadStore.get()).toBe(1)
    const user = userEvent.setup()
    render(<NotificationList />)
    await screen.findAllByTestId("notification-item", {}, CHO)
    await user.click(dong(unreadComment.notificationId))

    // Server CHƯA trả lời (handler còn treo) mà chấm đã tắt và badge đã về 0.
    expect(dong(unreadComment.notificationId)).toHaveAttribute("data-read", "true")
    expect(unreadStore.get()).toBe(0)
    resolve()
  })

  it("server lỗi → rollback: chấm bật lại + câu lỗi", async () => {
    trang({ dau: { items: [unreadComment], nextCursor: null } })
    server.use(
      http.post(`${API}/:id/read`, async () => {
        await delay(50)
        return HttpResponse.json(
          { type: "https://httpstatuses.io/403", title: "Bị từ chối", status: 403, traceId: "t" },
          { status: 403, headers: { "Content-Type": "application/problem+json" } }
        )
      })
    )
    const user = userEvent.setup()
    render(<NotificationList />)
    await screen.findAllByTestId("notification-item", {}, CHO)
    await user.click(dong(unreadComment.notificationId))

    expect(await screen.findByText("Không tìm thấy thông báo này.", {}, CHO)).toBeInTheDocument()
    expect(dong(unreadComment.notificationId)).toHaveAttribute("data-read", "false")
  })

  it("đánh dấu tất cả: upTo = updatedAt MỚI NHẤT đang hiển thị, mọi dòng tắt chấm", async () => {
    const cu = { ...notificationModeration, isRead: false }
    trang({ dau: { items: [cu, unreadComment], nextCursor: null } })
    let upTo: string | null = null
    server.use(
      http.post(`${API}/read-all`, async ({ request }) => {
        upTo = ((await request.json()) as { upTo: string }).upTo
        return new HttpResponse(null, { status: 204 })
      })
    )
    const user = userEvent.setup()
    render(<NotificationList />)
    await screen.findAllByTestId("notification-item", {}, CHO)
    await user.click(screen.getByRole("button", { name: "Đánh dấu tất cả đã đọc" }))

    await waitFor(() => expect(upTo).toBe(unreadComment.updatedAt), CHO)
    for (const el of screen.getAllByTestId("notification-item"))
      expect(el).toHaveAttribute("data-read", "true")
  })

  it("nối trang khử trùng theo id — nhóm nhảy lên đầu không hiện hai lần", async () => {
    trang({
      dau: { items: [unreadComment], nextCursor: "c2" },
      c2: { items: [unreadComment, notificationModeration], nextCursor: null },
    })
    const user = userEvent.setup()
    render(<NotificationList />)
    await screen.findAllByTestId("notification-item", {}, CHO)
    await user.click(screen.getByRole("button", { name: "Xem thêm" }))

    await waitFor(() => expect(screen.getAllByTestId("notification-item")).toHaveLength(2), CHO)
    expect(screen.queryByRole("button", { name: "Xem thêm" })).toBeNull()
  })

  it("StrictMode: mount → unmount → mount vẫn ra trang đầu (controller của lần mount đầu đã hủy không bị dùng lại)", async () => {
    trang({ dau: { items: [unreadComment], nextCursor: null } })
    render(
      <StrictMode>
        <NotificationList />
      </StrictMode>
    )
    expect(await screen.findAllByTestId("notification-item", {}, CHO)).toHaveLength(1)
  })

  it("rỗng → câu trạng thái rỗng", async () => {
    trang({ dau: { items: [], nextCursor: null } })
    render(<NotificationList />)
    expect(await screen.findByTestId("notifications-empty", {}, CHO)).toBeInTheDocument()
  })
})
