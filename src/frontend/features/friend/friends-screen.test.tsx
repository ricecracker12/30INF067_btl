import { render, screen, waitFor, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { http, HttpResponse } from "msw"
import { StrictMode } from "react"
import { beforeEach, describe, expect, it } from "vitest"

import { BFF_URL } from "@/lib/api/config"
import type { FriendCard, FriendPage } from "@/lib/api/types"
import { friendCard, SOCIAL_SCENARIO, userIdKhac } from "@/mocks/fixtures"
import { server } from "@/mocks/node"
import { fakeSession } from "@/mocks/session"

import { FriendsScreen } from "./friends-screen"

// E3 — màn `/friends`: ba danh sách tách (mỗi cái một hook), thao tác TẠI CHỖ khóa theo thẻ, hết trang khi và chỉ khi
// `nextCursor === null` (Đ-4.9). Handler mặc định (mocks/handlers.ts): lời mời đến từ `loiMoiDen`, lời mời đi tới
// `loiMoiDi`, bạn bè là `userIdKhac`.

const API = `${BFF_URL}/api`
const REQUESTS = `${API}/friends/requests`
const FRIENDS = `${API}/friends`

/** Chờ tay 5s — ca nối nhiều request; cả bộ chạy song song thì 1s mặc định không đủ (luật frontend Mục 9). */
const CHO = { timeout: 5000 }

const CURSOR_2 = "MjAyNi0wOS0yM1QwODoxNTowMFo-MDE5MmYzYzE_"

const the = (userId: string, name = "Nguyễn Văn An"): FriendCard => ({
  ...friendCard,
  user: { ...friendCard.user, userId, displayName: name },
})

/** Mọi request thật sự đi ra, dạng "METHOD /đường?query" (bỏ tiền tố /bff/api). */
function ghiRequest() {
  const seen: string[] = []
  server.events.on("request:start", ({ request }) => {
    const u = new URL(request.url)
    seen.push(
      `${request.method} ${u.pathname.replace("/bff/api", "")}${u.search}`
    )
  })
  return seen
}

/** Thay trang của MỘT mục (`incoming` | `outgoing` | `friends`) theo cursor; mục khác giữ handler mặc định. */
function phucVu(
  muc: "incoming" | "outgoing" | "friends",
  pages: Record<string, FriendPage>
) {
  const handler = ({ request }: { request: Request }) => {
    const q = new URL(request.url).searchParams
    const page = pages[q.get("cursor") ?? "dau"]
    if (!page) throw new Error(`cursor lạ: ${q.get("cursor")}`)
    return HttpResponse.json(page)
  }
  if (muc === "friends") server.use(http.get(FRIENDS, handler))
  else
    server.use(
      http.get(REQUESTS, ({ request }) => {
        if (new URL(request.url).searchParams.get("direction") !== muc)
          return undefined // rơi xuống handler mặc định
        return handler({ request })
      })
    )
}

const muc = (id: "incoming" | "friends" | "outgoing") =>
  screen.getByTestId(`section-${id}`)
const theTrong = (id: "incoming" | "friends" | "outgoing") =>
  within(muc(id))
    .queryAllByTestId("friend-card")
    .map((c) => c.dataset.userId)

async function moMan() {
  const user = userEvent.setup()
  render(<FriendsScreen />)
  await waitFor(() => {
    expect(theTrong("incoming")).toHaveLength(1)
    expect(theTrong("friends")).toHaveLength(1)
    expect(theTrong("outgoing")).toHaveLength(1)
  }, CHO)
  return user
}

beforeEach(() => {
  fakeSession.start()
})

describe("FriendsScreen — ba mục", () => {
  it("ba mục gọi đúng ba URL, direction LUÔN gửi, thứ tự việc cần làm: đến · bạn bè · đã gửi", async () => {
    const seen = ghiRequest()
    await moMan()

    expect(seen.filter((s) => s.startsWith("GET")).sort()).toEqual(
      [
        "GET /friends/requests?direction=incoming&limit=20",
        "GET /friends?limit=20",
        "GET /friends/requests?direction=outgoing&limit=20",
      ].sort()
    )
    const tieuDe = screen.getAllByRole("heading").map((h) => h.textContent)
    expect(tieuDe).toEqual(["Lời mời kết bạn", "Bạn bè", "Lời mời đã gửi"])
    expect(theTrong("incoming")).toEqual([SOCIAL_SCENARIO.loiMoiDen])
    expect(theTrong("outgoing")).toEqual([SOCIAL_SCENARIO.loiMoiDi])
  })

  it("thẻ dẫn tới /users/{id}", async () => {
    await moMan()
    const link = within(muc("friends")).getByRole("link")
    expect(link).toHaveAttribute("href", `/users/${userIdKhac}`)
  })

  it("trạng thái rỗng riêng từng mục; câu của mục Bạn bè trỏ về trang chủ", async () => {
    phucVu("incoming", { dau: { items: [], nextCursor: null } })
    phucVu("outgoing", { dau: { items: [], nextCursor: null } })
    phucVu("friends", { dau: { items: [], nextCursor: null } })
    render(<FriendsScreen />)

    expect(
      await screen.findByTestId("section-incoming-empty", undefined, CHO)
    ).toHaveTextContent("Chưa có lời mời nào.")
    expect(
      await screen.findByTestId("section-friends-empty", undefined, CHO)
    ).toHaveTextContent("Mở trang chủ để xem bài công khai")
    expect(
      await screen.findByTestId("section-outgoing-empty", undefined, CHO)
    ).toHaveTextContent("Bạn chưa gửi lời mời nào.")
  })

  it("lỗi trang đầu của MỘT mục → câu + Thử lại ở đúng mục đó; hai mục kia vẫn hiện", async () => {
    let lan = 0
    server.use(
      http.get(FRIENDS, () => {
        lan++
        if (lan === 1)
          return HttpResponse.json(
            { type: "t", title: "t", status: 500, traceId: "abc" },
            {
              status: 500,
              headers: { "Content-Type": "application/problem+json" },
            }
          )
        return HttpResponse.json({ items: [friendCard], nextCursor: null })
      })
    )
    const user = userEvent.setup()
    render(<FriendsScreen />)

    const alert = await within(
      await screen.findByTestId("section-friends", undefined, CHO)
    ).findByRole("alert", undefined, CHO)
    expect(alert).toHaveTextContent("Mã tra cứu: abc")
    await waitFor(() => expect(theTrong("incoming")).toHaveLength(1), CHO)

    await user.click(
      within(muc("friends")).getByRole("button", { name: "Thử lại" })
    )
    await waitFor(() => expect(theTrong("friends")).toEqual([userIdKhac]), CHO)
  })
})

describe("FriendsScreen — thao tác tại chỗ", () => {
  it("Chấp nhận → POST accept, thẻ biến khỏi 'Lời mời đến', mục Bạn bè NẠP LẠI (không tự dựng thẻ)", async () => {
    const user = await moMan()
    const seen = ghiRequest()

    await user.click(
      within(muc("incoming")).getByRole("button", { name: "Chấp nhận" })
    )

    await waitFor(() => expect(theTrong("incoming")).toEqual([]), CHO)
    expect(seen).toContain(
      `POST /friends/requests/${SOCIAL_SCENARIO.loiMoiDen}/accept`
    )
    await waitFor(() => expect(seen).toContain("GET /friends?limit=20"), CHO)
  })

  it("403 Chấp nhận → câu Q-E3 (kèm tên) ở đầu mục, thẻ gỡ, mục Bạn bè KHÔNG nạp lại", async () => {
    // Lời mời từ `userIdKhac` — mock coi quan hệ đó là `none` → accept 403 (Đ-4.14: một phản hồi cho mọi lý do).
    phucVu("incoming", {
      dau: { items: [the(userIdKhac, "Trần Bình")], nextCursor: null },
    })
    const user = await moMan()
    const seen = ghiRequest()

    await user.click(
      within(muc("incoming")).getByRole("button", { name: "Chấp nhận" })
    )

    await waitFor(() => expect(theTrong("incoming")).toEqual([]), CHO)
    expect(within(muc("incoming")).getByRole("alert")).toHaveTextContent(
      "Trần Bình: Lời mời này không còn hiệu lực."
    )
    expect(seen.filter((s) => s.startsWith("GET /friends?"))).toEqual([])
  })

  it("Từ chối → DELETE /friends/requests/{id}, thẻ gỡ", async () => {
    const user = await moMan()
    const seen = ghiRequest()

    await user.click(
      within(muc("incoming")).getByRole("button", { name: "Từ chối" })
    )

    await waitFor(() => expect(theTrong("incoming")).toEqual([]), CHO)
    expect(seen).toContain(
      `DELETE /friends/requests/${SOCIAL_SCENARIO.loiMoiDen}`
    )
  })

  it("Hủy lời mời đã gửi → DELETE /friends/requests/{id}, thẻ gỡ", async () => {
    const user = await moMan()
    const seen = ghiRequest()

    await user.click(
      within(muc("outgoing")).getByRole("button", { name: "Hủy lời mời" })
    )

    await waitFor(() => expect(theTrong("outgoing")).toEqual([]), CHO)
    expect(seen).toContain(
      `DELETE /friends/requests/${SOCIAL_SCENARIO.loiMoiDi}`
    )
  })

  it("Hủy kết bạn → hộp thoại nêu tên; 'Không' → 0 DELETE; xác nhận → DELETE /friends/{id}, thẻ gỡ", async () => {
    const user = await moMan()
    const seen = ghiRequest()

    await user.click(
      within(muc("friends")).getByRole("button", { name: "Hủy kết bạn" })
    )
    let hop = await screen.findByRole("alertdialog", undefined, CHO)
    expect(hop).toHaveTextContent("Bài chỉ dành cho bạn bè của Nguyễn Văn An")
    await user.click(within(hop).getByRole("button", { name: "Không" }))
    await waitFor(
      () => expect(screen.queryByRole("alertdialog")).toBeNull(),
      CHO
    )
    expect(seen.filter((s) => s.startsWith("DELETE"))).toEqual([])

    await user.click(
      within(muc("friends")).getByRole("button", { name: "Hủy kết bạn" })
    )
    hop = await screen.findByRole("alertdialog", undefined, CHO)
    await user.click(within(hop).getByRole("button", { name: "Hủy kết bạn" }))

    await waitFor(() => expect(theTrong("friends")).toEqual([]), CHO)
    expect(seen).toContain(`DELETE /friends/${userIdKhac}`)
  })

  it("lỗi khác → câu DƯỚI THẺ, thẻ giữ nguyên", async () => {
    server.use(
      http.delete(`${REQUESTS}/:userId`, () =>
        HttpResponse.json(
          { type: "t", title: "t", status: 500, traceId: "xyz" },
          {
            status: 500,
            headers: { "Content-Type": "application/problem+json" },
          }
        )
      )
    )
    const user = await moMan()

    await user.click(
      within(muc("outgoing")).getByRole("button", { name: "Hủy lời mời" })
    )

    const card = within(muc("outgoing")).getByTestId("friend-card")
    expect(
      await within(card).findByRole("alert", undefined, CHO)
    ).toHaveTextContent("Mã tra cứu: xyz")
    expect(theTrong("outgoing")).toEqual([SOCIAL_SCENARIO.loiMoiDi])
  })

  it("khóa THEO THẺ: thẻ A đang chờ thì nút của A khóa, thẻ B vẫn bấm được", async () => {
    phucVu("incoming", {
      dau: {
        items: [
          the(SOCIAL_SCENARIO.loiMoiDen, "A"),
          the("0192f3c1-8a4e-7c31-9f2a-6b5d4e3c2a40", "B"),
        ],
        nextCursor: null,
      },
    })
    let moChot: () => void = () => {}
    const chot = new Promise<void>((r) => (moChot = r))
    server.use(
      http.post(`${REQUESTS}/:userId/accept`, async ({ params }) => {
        await chot
        return HttpResponse.json({
          userId: String(params.userId),
          friendship: "friends",
          following: false,
        })
      })
    )
    const user = userEvent.setup()
    render(<FriendsScreen />)
    await waitFor(() => expect(theTrong("incoming")).toHaveLength(2), CHO)

    const [theA, theB] = within(muc("incoming")).getAllByTestId("friend-card")
    await user.click(within(theA).getByRole("button", { name: "Chấp nhận" }))

    expect(
      within(theA).getByRole("button", { name: "Chấp nhận" })
    ).toBeDisabled()
    expect(within(theA).getByRole("button", { name: "Từ chối" })).toBeDisabled()
    expect(
      within(theB).getByRole("button", { name: "Chấp nhận" })
    ).toBeEnabled()
    expect(within(theB).getByRole("button", { name: "Từ chối" })).toBeEnabled()

    moChot()
    await waitFor(() => expect(theTrong("incoming")).toHaveLength(1), CHO)
  })
})

describe("FriendsScreen — phân trang (Đ-4.9)", () => {
  it("Xem thêm nối trang theo NGUYÊN nextCursor; bấm hai lần nhanh không lặp thẻ; hết trang thì nút biến mất", async () => {
    phucVu("incoming", {
      dau: {
        items: [the("0192f3c1-8a4e-7c31-9f2a-6b5d4e3c2a41")],
        nextCursor: CURSOR_2,
      },
      [CURSOR_2]: {
        items: [the("0192f3c1-8a4e-7c31-9f2a-6b5d4e3c2a42")],
        nextCursor: null,
      },
    })
    const seen = ghiRequest()
    const user = userEvent.setup()
    render(<FriendsScreen />)
    await waitFor(() => expect(theTrong("incoming")).toHaveLength(1), CHO)

    const nut = within(muc("incoming")).getByRole("button", {
      name: "Xem thêm",
    })
    await user.dblClick(nut)

    await waitFor(() => expect(theTrong("incoming")).toHaveLength(2), CHO)
    expect(new Set(theTrong("incoming")).size).toBe(2)
    expect(seen.filter((s) => s.includes(`cursor=${CURSOR_2}`))).toHaveLength(1)
    expect(
      within(muc("incoming")).queryByRole("button", { name: "Xem thêm" })
    ).toBeNull()
  })

  it("trang RỖNG mà nextCursor ≠ null → nút 'Xem thêm' VẪN hiện, không câu 'chưa có' (không suy hết từ độ dài)", async () => {
    phucVu("friends", {
      dau: { items: [], nextCursor: CURSOR_2 },
      [CURSOR_2]: { items: [friendCard], nextCursor: null },
    })
    const user = userEvent.setup()
    render(<FriendsScreen />)

    const nut = await within(
      await screen.findByTestId("section-friends", undefined, CHO)
    ).findByRole("button", { name: "Xem thêm" }, CHO)
    expect(screen.queryByTestId("section-friends-empty")).toBeNull()

    await user.click(nut)
    await waitFor(() => expect(theTrong("friends")).toEqual([userIdKhac]), CHO)
  })
})

describe("FriendsScreen — StrictMode (luật frontend Mục 9, L5)", () => {
  // Next dev mount → unmount → mount lại: `AbortController` trang đầu của CẢ BA hook bị hủy ở lần mount đầu. Khẳng định
  // TRẠNG THÁI CUỐI, KHÔNG đếm request.
  it("ba mục đều nạp xong, không lặp thẻ, thao tác vẫn chạy", async () => {
    const user = userEvent.setup()
    render(
      <StrictMode>
        <FriendsScreen />
      </StrictMode>
    )

    await waitFor(() => {
      expect(theTrong("incoming")).toEqual([SOCIAL_SCENARIO.loiMoiDen])
      expect(theTrong("friends")).toEqual([userIdKhac])
      expect(theTrong("outgoing")).toEqual([SOCIAL_SCENARIO.loiMoiDi])
    }, CHO)

    await user.click(
      within(muc("outgoing")).getByRole("button", { name: "Hủy lời mời" })
    )
    await waitFor(() => expect(theTrong("outgoing")).toEqual([]), CHO)
  })
})
