import { act, render, screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { http, HttpResponse } from "msw"
import { StrictMode } from "react"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"

import { BFF_URL } from "@/lib/api/config"
import type { NotificationPage } from "@/lib/api/types"
import { notificationComment, notificationModeration } from "@/mocks/fixtures"
import { server } from "@/mocks/node"

import { NotificationBell } from "./notification-bell"
import { unreadStore } from "./unread-store"
import { UNREAD_POLL_MS } from "./use-unread-count"

// GĐ6 E3 — chuông: hỏi lại 30 giây (Đ-6.18), dừng khi tab ẩn, nạp ngay khi tab hiện lại; badge "9+"; popover 10 nhóm mới nhất.
// Chỉ giả `setInterval`/`clearInterval` — msw và Testing Library cần `setTimeout` thật.

const COUNT = `${BFF_URL}/api/notifications/unread-count`
const LIST = `${BFF_URL}/api/notifications`
const CHO = { timeout: 5000 }

let visibility: DocumentVisibilityState = "visible"

function soLanHoi() {
  const seen: string[] = []
  server.events.on("request:start", ({ request }) => {
    if (new URL(request.url).pathname.endsWith("/unread-count"))
      seen.push(request.method)
  })
  return seen
}

function chuaDoc(total: number) {
  server.use(http.get(COUNT, () => HttpResponse.json({ total })))
}

function doiTab(state: DocumentVisibilityState) {
  visibility = state
  act(() => {
    document.dispatchEvent(new Event("visibilitychange"))
  })
}

beforeEach(() => {
  visibility = "visible"
  Object.defineProperty(document, "visibilityState", {
    configurable: true,
    get: () => visibility,
  })
  unreadStore.reset()
})

afterEach(() => {
  vi.useRealTimers()
})

describe("NotificationBell — hỏi lại", () => {
  it("hỏi ngay khi gắn, rồi mỗi 30 giây; tab ẩn thì DỪNG; hiện lại thì hỏi NGAY", async () => {
    vi.useFakeTimers({ toFake: ["setInterval", "clearInterval"] })
    chuaDoc(2)
    const hoi = soLanHoi()
    render(<NotificationBell />)
    await waitFor(() => expect(hoi).toHaveLength(1), CHO)

    await act(async () => vi.advanceTimersByTime(UNREAD_POLL_MS))
    await waitFor(() => expect(hoi).toHaveLength(2), CHO)

    doiTab("hidden")
    await act(async () => vi.advanceTimersByTime(UNREAD_POLL_MS * 3))
    expect(hoi).toHaveLength(2)

    doiTab("visible")
    await waitFor(() => expect(hoi).toHaveLength(3), CHO)
    await act(async () => vi.advanceTimersByTime(UNREAD_POLL_MS))
    await waitFor(() => expect(hoi).toHaveLength(4), CHO)
  })

  it("StrictMode: mount → unmount → mount để lại ĐÚNG một timer hỏi lại", async () => {
    vi.useFakeTimers({ toFake: ["setInterval", "clearInterval"] })
    chuaDoc(3)
    render(
      <StrictMode>
        <NotificationBell />
      </StrictMode>
    )
    expect(
      await screen.findByTestId("notification-badge", {}, CHO)
    ).toHaveTextContent("3")
    expect(vi.getTimerCount()).toBe(1)
  })

  it("badge: '9+' từ 10; 0 thì không có badge", async () => {
    chuaDoc(12)
    const { unmount } = render(<NotificationBell />)
    expect(
      await screen.findByTestId("notification-badge", {}, CHO)
    ).toHaveTextContent("9+")
    unmount()

    unreadStore.reset()
    chuaDoc(0)
    const hoi = soLanHoi()
    render(<NotificationBell />)
    await waitFor(() => expect(hoi).toHaveLength(1), CHO)
    expect(screen.queryByTestId("notification-badge")).toBeNull()
  })
})

describe("NotificationBell — popover", () => {
  it("mở ra: nạp 10 nhóm mới nhất, câu ghép theo type", async () => {
    chuaDoc(1)
    let limit: string | null = null
    server.use(
      http.get(LIST, ({ request }) => {
        limit = new URL(request.url).searchParams.get("limit")
        return HttpResponse.json({
          items: [notificationComment, notificationModeration],
          nextCursor: null,
        } satisfies NotificationPage)
      })
    )
    const user = userEvent.setup()
    render(<NotificationBell />)
    await user.click(screen.getByTestId("notification-bell"))

    expect(
      await screen.findByText(
        "Nguyễn Văn An và 3 người khác đã bình luận về bài viết của bạn.",
        {},
        CHO
      )
    ).toBeInTheDocument()
    expect(limit).toBe("10")
    expect(screen.getAllByTestId("notification-item")).toHaveLength(2)
  })
})
