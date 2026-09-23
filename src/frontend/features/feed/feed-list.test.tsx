import { act, render, screen, waitFor, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { delay, http, HttpResponse } from "msw"
import { StrictMode } from "react"
import { beforeEach, describe, expect, it, vi } from "vitest"

import { BFF_URL } from "@/lib/api/config"
import { PROBLEM_TYPES } from "@/lib/api/problem"
import type { FeedMode, FeedPage, PostResponse } from "@/lib/api/types"
import {
  CURSOR_QUA_TAI,
  feedOverloadedProblem,
  feedPost,
  problem,
} from "@/mocks/fixtures"
import { server } from "@/mocks/node"
import { fakeSession } from "@/mocks/session"
import {
  kichHoatGiaoNhau,
  soObserverDangTheoDoi,
} from "@/test/intersection-observer"

import { FeedList, MAX_AUTO_EMPTY_PAGES, type RenderPost } from "./feed-list"

// E4 — feed trang chủ. Ba luật không thương lượng: hết bài KHI VÀ CHỈ KHI `nextCursor === null` (Đ-4.9), nhãn gợi ý đọc
// từ `mode` (Đ-4.6), 503 quá tải thành Thử lại — không màn trắng, không đếm ngược, không tự thử lại (Đ-4.10).
// Observer là stub của harness (`test/intersection-observer.ts`): KHÔNG tự bắn, ca nào cần cuộn thì bắn tay.

const FEED = `${BFF_URL}/api/feed`
const PROBLEM = { "Content-Type": "application/problem+json" }

/** Cursor thật là base64url opaque, có `-` và `_` — FE phải chuyển nguyên vẹn. */
const CURSOR_2 = "MjAyNi0wOS0yM1QwODoxNTowMFo-MDE5MmYzYzE_"

const bai = (id: string, over: Partial<PostResponse> = {}): PostResponse => ({
  ...feedPost,
  postId: id,
  ...over,
})
const nhieuBai = (n: number, prefix = "p") =>
  Array.from({ length: n }, (_, i) => bai(`${prefix}${i + 1}`))

const trang = (
  items: PostResponse[],
  nextCursor: string | null,
  mode: FeedMode = "network"
): FeedPage => ({ items, nextCursor, mode })

/** Phục vụ theo cursor (`null` = trang đầu); ghi lại cursor của từng lượt gọi — thứ duy nhất thấy FE gửi gì. */
function phucVu(pages: Record<string, FeedPage | (() => Response)>) {
  const seen: (string | null)[] = []
  server.use(
    http.get(FEED, ({ request }) => {
      const cursor = new URL(request.url).searchParams.get("cursor")
      seen.push(cursor)
      const p = pages[cursor ?? "dau"]
      if (p === undefined) throw new Error(`cursor lạ: ${cursor}`)
      return typeof p === "function" ? p() : HttpResponse.json(p)
    })
  )
  return seen
}

/** `renderPost` giả: `features/feed/` không biết `PostItem` — test cũng không cần. Có nút gọi `onChanged(null)`. */
const renderPost: RenderPost = (post, onChanged) => (
  <article data-testid="feed-post" data-post-id={post.postId}>
    {post.body}
    <button onClick={() => onChanged(null)}>Gỡ {post.postId}</button>
  </article>
)

const baiTrenMan = () =>
  screen.queryAllByTestId("feed-post").map((a) => a.dataset.postId)

const cuonToiDay = () => act(() => kichHoatGiaoNhau(true))

/**
 * Chờ tay 5s cho MỌI `waitFor`/`findBy` của file: nhiều ca nối 2–7 request và cú bấm; chạy cả bộ (41 file jsdom song
 * song) thì mức mặc định 1s đỏ ngẫu nhiên ~1/8 lượt (đo 2026-09-23, ca "503 ở trang SAU"). Nới thời gian chờ không làm
 * ca yếu đi — khẳng định y nguyên (luật frontend Mục 9).
 */
const CHO = { timeout: 5000 }

beforeEach(() => {
  fakeSession.start()
})

describe("FeedList — trang đầu", () => {
  it("skeleton → 20 bài qua renderPost; limit gửi đúng hợp đồng", async () => {
    const seen: string[] = []
    server.use(
      http.get(FEED, async ({ request }) => {
        seen.push(new URL(request.url).search)
        await delay(20)
        return HttpResponse.json(trang(nhieuBai(20), null))
      })
    )

    render(<FeedList renderPost={renderPost} />)

    expect(screen.getByTestId("feed-skeleton")).toBeInTheDocument()
    await waitFor(() => expect(baiTrenMan()).toHaveLength(20), CHO)
    expect(screen.queryByTestId("feed-skeleton")).toBeNull()
    expect(seen).toEqual(["?limit=20"])
  })

  it("mode = suggested → nhãn gợi ý TRÊN danh sách; mode = network → không nhãn", async () => {
    phucVu({ dau: trang([bai("p1")], null, "suggested") })
    const { unmount } = render(<FeedList renderPost={renderPost} />)

    const nhan = await screen.findByTestId("feed-suggested", undefined, CHO)
    expect(nhan).toHaveTextContent(
      "Gợi ý cho bạn — kết bạn để thấy bài của bạn bè"
    )
    // Nhãn đứng trước bài đầu tiên trong DOM.
    expect(
      nhan.compareDocumentPosition(screen.getByTestId("feed-post")) &
        Node.DOCUMENT_POSITION_FOLLOWING
    ).toBeTruthy()
    unmount()

    phucVu({ dau: trang([bai("p1")], null, "network") })
    render(<FeedList renderPost={renderPost} />)
    await screen.findByTestId("feed-post", undefined, CHO)
    expect(screen.queryByTestId("feed-suggested")).toBeNull()
  })

  it("network rỗng → câu mời kết bạn/theo dõi; suggested rỗng → 'Chưa có bài công khai nào.' (vẫn có nhãn)", async () => {
    phucVu({ dau: trang([], null, "network") })
    const { unmount } = render(<FeedList renderPost={renderPost} />)
    expect(
      await screen.findByTestId("feed-empty", undefined, CHO)
    ).toHaveTextContent("Chưa có bài nào — kết bạn hoặc theo dõi để thấy bài.")
    expect(screen.queryByTestId("feed-end")).toBeNull()
    unmount()

    phucVu({ dau: trang([], null, "suggested") })
    render(<FeedList renderPost={renderPost} />)
    expect(
      await screen.findByTestId("feed-empty", undefined, CHO)
    ).toHaveTextContent("Chưa có bài công khai nào.")
    expect(screen.getByTestId("feed-suggested")).toBeInTheDocument()
  })

  it("nextCursor = null → không sentinel, không nút nạp thêm, hiện 'Bạn đã xem hết'", async () => {
    phucVu({ dau: trang(nhieuBai(3), null) })
    render(<FeedList renderPost={renderPost} />)

    expect(
      await screen.findByTestId("feed-end", undefined, CHO)
    ).toHaveTextContent("Bạn đã xem hết")
    expect(screen.queryByTestId("feed-sentinel")).toBeNull()
    expect(
      screen.queryByRole("button", { name: /Xem thêm|Xem tiếp/ })
    ).toBeNull()
  })

  it("còn trang (nextCursor ≠ null): có sentinel, KHÔNG có nút 'Xem thêm' lúc bình thường (Q-E5)", async () => {
    phucVu({ dau: trang(nhieuBai(20), CURSOR_2) })
    render(<FeedList renderPost={renderPost} />)

    expect(
      await screen.findByTestId("feed-sentinel", undefined, CHO)
    ).toBeInTheDocument()
    expect(
      screen.queryByRole("button", { name: /Xem thêm|Xem tiếp/ })
    ).toBeNull()
    expect(screen.queryByTestId("feed-end")).toBeNull()
  })
})

describe("FeedList — cuộn theo nextCursor (Đ-4.9, Q-E5)", () => {
  it("sentinel giao nhau → GET /feed với NGUYÊN chuỗi nextCursor; bài nối tiếp, không lặp", async () => {
    const seen = phucVu({
      dau: trang(nhieuBai(2), CURSOR_2),
      [CURSOR_2]: trang([bai("p2"), bai("p3")], null),
    })
    render(<FeedList renderPost={renderPost} />)
    await waitFor(() => expect(baiTrenMan()).toHaveLength(2), CHO)

    await cuonToiDay()

    await waitFor(() => expect(baiTrenMan()).toEqual(["p1", "p2", "p3"]), CHO)
    expect(seen).toEqual([null, CURSOR_2])
    expect(screen.getByTestId("feed-end")).toBeInTheDocument()
  })

  it("tự nối trang → báo trình đọc màn hình (vùng role=status) số bài vừa thêm và tổng; trang đầu không báo", async () => {
    phucVu({
      dau: trang(nhieuBai(2), CURSOR_2),
      [CURSOR_2]: trang([bai("p3"), bai("p4")], null),
    })
    render(<FeedList renderPost={renderPost} />)
    await waitFor(() => expect(baiTrenMan()).toHaveLength(2), CHO)
    // Thẻ `p` thường không được trình đọc màn hình báo — phải là vùng `role=status` (aria-live polite ngầm định).
    expect(screen.getByTestId("feed-status")).toHaveAttribute("role", "status")
    expect(screen.getByTestId("feed-status")).toHaveTextContent("")

    await cuonToiDay()

    await waitFor(
      () =>
        expect(screen.getByTestId("feed-status")).toHaveTextContent(
          "Đã tải thêm 2 bài, đang hiển thị 4 bài."
        ),
      CHO
    )
  })

  it("không giao nhau → không gọi gì", async () => {
    const seen = phucVu({ dau: trang(nhieuBai(2), CURSOR_2) })
    render(<FeedList renderPost={renderPost} />)
    await waitFor(() => expect(baiTrenMan()).toHaveLength(2), CHO)

    await act(() => kichHoatGiaoNhau(false))
    await delay(30)

    expect(seen).toEqual([null])
  })

  it("trang RỖNG mà nextCursor ≠ null: sentinel vẫn giao nhau → TỰ nạp trang tiếp, không cần observer bắn lại", async () => {
    const seen = phucVu({
      dau: trang([bai("p1")], "A"),
      A: trang([], "B"),
      B: trang([bai("p2")], null),
    })
    render(<FeedList renderPost={renderPost} />)
    await waitFor(() => expect(baiTrenMan()).toEqual(["p1"]), CHO)

    // MỘT lượt bắn duy nhất. Trang A rỗng → observer thật sẽ không bắn lại vì sentinel chưa rời khung nhìn.
    await cuonToiDay()

    await waitFor(() => expect(baiTrenMan()).toEqual(["p1", "p2"]), CHO)
    expect(seen).toEqual([null, "A", "B"])
  })

  it(`trần ${MAX_AUTO_EMPTY_PAGES} trang rỗng liên tiếp: thôi tự nạp, nút 'Xem tiếp' làm việc thay`, async () => {
    const pages: Record<string, FeedPage> = { dau: trang([bai("p1")], "r1") }
    for (let i = 1; i <= 20; i++) pages[`r${i}`] = trang([], `r${i + 1}`)
    const seen = phucVu(pages)
    render(<FeedList renderPost={renderPost} />)
    await waitFor(() => expect(baiTrenMan()).toEqual(["p1"]), CHO)

    await cuonToiDay()

    const nut = await screen.findByRole("button", { name: "Xem tiếp" }, CHO)
    await delay(50)
    // Trang đầu + đúng 5 trang rỗng — không một request nào nữa dù sentinel vẫn "giao nhau".
    expect(seen).toEqual([null, "r1", "r2", "r3", "r4", "r5"])

    await userEvent.click(nut)
    await waitFor(() => expect(seen).toHaveLength(7), CHO)
    expect(seen[6]).toBe("r6")
  })

  it("mode giữ theo TRANG ĐẦU: trang sau về mode khác thì nhãn không nhảy giữa chừng", async () => {
    phucVu({
      dau: trang([bai("p1")], CURSOR_2, "suggested"),
      [CURSOR_2]: trang([bai("p2")], null, "network"),
    })
    render(<FeedList renderPost={renderPost} />)
    await screen.findByTestId("feed-suggested", undefined, CHO)

    await cuonToiDay()

    await waitFor(() => expect(baiTrenMan()).toEqual(["p1", "p2"]), CHO)
    expect(screen.getByTestId("feed-suggested")).toBeInTheDocument()
  })

  it("Làm mới rồi cuộn: observer được TẠO LẠI cho sentinel mới — không chỉ tạo một lần cho cả đời component", async () => {
    let lan = 0
    const seen: (string | null)[] = []
    server.use(
      http.get(FEED, ({ request }) => {
        const cursor = new URL(request.url).searchParams.get("cursor")
        seen.push(cursor)
        if (cursor === null) {
          lan++
          return HttpResponse.json(trang([bai(`dau-${lan}`)], CURSOR_2))
        }
        return HttpResponse.json(trang([bai("sau")], null))
      })
    )
    render(<FeedList renderPost={renderPost} />)
    await waitFor(() => expect(baiTrenMan()).toEqual(["dau-1"]), CHO)

    await userEvent.click(screen.getByRole("button", { name: "Làm mới" }))
    await waitFor(() => expect(baiTrenMan()).toEqual(["dau-2"]), CHO)
    await cuonToiDay()

    await waitFor(() => expect(baiTrenMan()).toEqual(["dau-2", "sau"]), CHO)
    expect(seen).toEqual([null, null, CURSOR_2])
  })

  it("Làm mới → nạp lại từ đầu, đọc lại mode (vừa kết bạn: gợi ý → mạng lưới)", async () => {
    let lan = 0
    server.use(
      http.get(FEED, () => {
        lan++
        return HttpResponse.json(
          lan === 1
            ? trang([bai("goi-y")], null, "suggested")
            : trang([bai("cua-ban")], null, "network")
        )
      })
    )
    render(<FeedList renderPost={renderPost} />)
    await screen.findByTestId("feed-suggested", undefined, CHO)

    await userEvent.click(screen.getByRole("button", { name: "Làm mới" }))

    await waitFor(() => expect(baiTrenMan()).toEqual(["cua-ban"]), CHO)
    expect(screen.queryByTestId("feed-suggested")).toBeNull()
  })
})

describe("FeedList — lỗi (Đ-4.10, Q-E4)", () => {
  it("503 feed-overloaded ở trang đầu → thẻ quá tải + Thử lại; KHÔNG mã tra cứu; KHÔNG tự thử lại; bấm → gọi lại", async () => {
    let lan = 0
    server.use(
      http.get(FEED, () => {
        lan++
        if (lan === 1)
          return HttpResponse.json(feedOverloadedProblem(), {
            status: 503,
            headers: { ...PROBLEM, "Retry-After": "5" },
          })
        return HttpResponse.json(trang([bai("p1")], null))
      })
    )
    render(<FeedList renderPost={renderPost} />)

    const the = await screen.findByTestId("feed-overloaded", undefined, CHO)
    expect(the).toHaveTextContent(
      "Bảng tin đang quá tải. Vui lòng thử lại sau ít phút."
    )
    expect(the).not.toHaveTextContent("Mã tra cứu")
    expect(the).not.toHaveTextContent(/\d+\s*giây/)
    await delay(50)
    expect(lan).toBe(1)

    await userEvent.click(within(the).getByRole("button", { name: "Thử lại" }))

    await waitFor(() => expect(baiTrenMan()).toEqual(["p1"]), CHO)
    expect(lan).toBe(2)
  })

  it("503 KHÔNG mang type riêng (apache) → lỗi hệ thống thường, không thẻ quá tải", async () => {
    server.use(
      http.get(
        FEED,
        () =>
          new HttpResponse("<html>503</html>", {
            status: 503,
            headers: { "Content-Type": "text/html" },
          })
      )
    )
    render(<FeedList renderPost={renderPost} />)

    expect(await screen.findByRole("alert", undefined, CHO)).toHaveTextContent(
      "Đã xảy ra lỗi không mong muốn."
    )
    expect(screen.queryByTestId("feed-overloaded")).toBeNull()
    expect(screen.getByRole("button", { name: "Thử lại" })).toBeInTheDocument()
  })

  it("503 BFF mất kho phiên → câu gián đoạn đăng nhập, không thẻ quá tải", async () => {
    server.use(
      http.get(FEED, () =>
        HttpResponse.json(
          {
            ...problem(503, "Dịch vụ phiên đăng nhập tạm thời không sẵn sàng"),
            type: PROBLEM_TYPES.bffSessionUnavailable,
          },
          { status: 503, headers: PROBLEM }
        )
      )
    )
    render(<FeedList renderPost={renderPost} />)

    expect(await screen.findByRole("alert", undefined, CHO)).toHaveTextContent(
      "Dịch vụ đăng nhập tạm thời gián đoạn. Vui lòng thử lại sau ít phút."
    )
    expect(screen.queryByTestId("feed-overloaded")).toBeNull()
  })

  it("503 ở trang SAU: bài cũ còn; dòng lỗi + Thử lại cuối danh sách; observer KHÔNG tự gọi; Thử lại nạp ĐÚNG lô hỏng", async () => {
    let hongLanDau = true
    const seen = phucVu({
      dau: trang(nhieuBai(2), CURSOR_QUA_TAI),
      [CURSOR_QUA_TAI]: () => {
        if (hongLanDau) {
          hongLanDau = false
          return HttpResponse.json(feedOverloadedProblem(), {
            status: 503,
            headers: PROBLEM,
          })
        }
        return HttpResponse.json(trang([bai("p3")], null))
      },
    })
    render(<FeedList renderPost={renderPost} />)
    await waitFor(() => expect(baiTrenMan()).toHaveLength(2), CHO)

    await cuonToiDay()

    const loi = await screen.findByTestId("feed-more-error", undefined, CHO)
    expect(loi).toHaveTextContent("Bảng tin đang quá tải")
    expect(baiTrenMan()).toEqual(["p1", "p2"])

    // Observer đã thôi: bắn thêm không sinh request nào.
    await cuonToiDay()
    await delay(30)
    expect(seen).toEqual([null, CURSOR_QUA_TAI])

    await userEvent.click(within(loi).getByRole("button", { name: "Thử lại" }))

    await waitFor(() => expect(baiTrenMan()).toEqual(["p1", "p2", "p3"]), CHO)
    expect(seen).toEqual([null, CURSOR_QUA_TAI, CURSOR_QUA_TAI])
    expect(screen.queryByTestId("feed-more-error")).toBeNull()
  })

  it("500 ở trang đầu → có Mã tra cứu (lỗi hệ thống thật) + Thử lại", async () => {
    server.use(
      http.get(FEED, () =>
        HttpResponse.json(problem(500, "Đã xảy ra lỗi không mong muốn"), {
          status: 500,
          headers: PROBLEM,
        })
      )
    )
    render(<FeedList renderPost={renderPost} />)

    expect(await screen.findByRole("alert", undefined, CHO)).toHaveTextContent(
      "Mã tra cứu"
    )
  })
})

describe("FeedList — ráp với renderPost (Q-E6)", () => {
  it("onChanged(null) → bài biến khỏi feed (đã xóa: không để lại liên kết chết)", async () => {
    phucVu({ dau: trang(nhieuBai(3), null) })
    render(<FeedList renderPost={renderPost} />)
    await waitFor(() => expect(baiTrenMan()).toHaveLength(3), CHO)

    await userEvent.click(screen.getByRole("button", { name: "Gỡ p2" }))

    expect(baiTrenMan()).toEqual(["p1", "p3"])
  })

  it("renderPost nhận đúng PostResponse của server — FeedList không tự dựng lại bài", async () => {
    const spy = vi.fn(renderPost)
    phucVu({
      dau: trang([bai("p1", { body: "Chiều nay trời đẹp quá." })], null),
    })
    render(<FeedList renderPost={spy} />)
    await screen.findByTestId("feed-post", undefined, CHO)

    expect(spy).toHaveBeenLastCalledWith(
      expect.objectContaining({
        postId: "p1",
        body: "Chiều nay trời đẹp quá.",
      }),
      expect.any(Function)
    )
  })
})

describe("FeedList — StrictMode (luật frontend Mục 9, L5)", () => {
  // Next dev mount → unmount → mount lại. Khẳng định TRẠNG THÁI CUỐI, KHÔNG đếm request — dưới StrictMode số request
  // tăng gấp đôi một cách hợp lệ. Tài nguyên StrictMode chạm tới ở màn này là `AbortController` của trang đầu (hook):
  // observer chỉ được tạo SAU khi trang đầu về, lúc đó không còn mount lại. Đột biến "effect trang đầu chỉ chạy một lần
  // cho mỗi key" (controller đã hủy bị dùng lại) chỉ ca này bắt được — skeleton mãi mãi.
  it("đủ bài trang đầu, không lặp; đúng MỘT observer sống; cuộn vẫn nạp được", async () => {
    phucVu({
      dau: trang(nhieuBai(20), CURSOR_2),
      [CURSOR_2]: trang([bai("p21")], null),
    })
    render(
      <StrictMode>
        <FeedList renderPost={renderPost} />
      </StrictMode>
    )

    await waitFor(() => expect(baiTrenMan()).toHaveLength(20), CHO)
    expect(new Set(baiTrenMan()).size).toBe(20)
    // Observer của lần mount đầu đã `disconnect` — chỉ còn cái của lần mount hai.
    expect(soObserverDangTheoDoi()).toBe(1)

    await cuonToiDay()

    await waitFor(() => expect(baiTrenMan()).toHaveLength(21), CHO)
    expect(screen.getByTestId("feed-end")).toBeInTheDocument()
  })
})
