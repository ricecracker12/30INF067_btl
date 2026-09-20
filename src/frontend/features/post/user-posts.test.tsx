import {
  act,
  fireEvent,
  render,
  screen,
  waitFor,
  within,
} from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { delay, http, HttpResponse } from "msw"
import { beforeEach, describe, expect, it } from "vitest"

import { BFF_URL } from "@/lib/api/config"
import type { PostPage, PostResponse } from "@/lib/api/types"
import { post, userId } from "@/mocks/fixtures"
import { server } from "@/mocks/node"
import { fakeSession } from "@/mocks/session"

import { ComposeFirstPostButton } from "./post-list"
import { useUserPosts } from "./use-post-page"
import { UserPosts } from "./user-posts"

// Cursor keyset của Đ-2.11 nhìn từ phía client. Khuôn này GĐ4 (feed) và GĐ5 (lịch sử hội thoại) sẽ chép
// lại (Mục 17), nên ba luật dưới đây phải có test canh, không phải chỉ có comment:
//
//   1. FE truyền cursor lại NGUYÊN VẸN, không dựng và không diễn giải.
//   2. Hết trang là `nextCursor === null` — và khi đó nút "Xem thêm" BIẾN MẤT, không phải bị `disabled`.
//   3. Lô trùng không sinh card lặp (StrictMode, bấm hai lần).

const POSTS = `${BFF_URL}/api/users/:userId/posts`

/** Cursor thật là `base64url("{created_at:O}|{post_id}")` — chuỗi opaque có `-` và `_`. */
const CURSOR_TRANG_2 = "MjAyNi0wOS0yMFQwMjoxMDoyMlo-MDE5MmYzYzE_"

function bai(id: string, over: Partial<PostResponse> = {}): PostResponse {
  return { ...post, postId: id, ...over }
}

function trang(items: PostResponse[], nextCursor: string | null): PostPage {
  return { items, nextCursor }
}

/** Ghi lại query string của từng lượt gọi — chỗ duy nhất thấy FE gửi `cursor`/`limit` gì. */
function phucVuHaiTrang(
  trang1: PostPage,
  trang2: PostPage,
  seen: string[] = []
) {
  server.use(
    http.get(POSTS, ({ request }) => {
      const params = new URL(request.url).searchParams
      seen.push(request.url.split("?")[1] ?? "")
      return HttpResponse.json(params.get("cursor") === null ? trang1 : trang2)
    })
  )
  return seen
}

function moManHinh(userIdCuaAi: string | null = userId) {
  return render(
    <UserPosts
      userId={userIdCuaAi}
      title="Bài của tôi"
      emptyMessage="Bạn chưa đăng bài nào."
      emptyAction={<ComposeFirstPostButton />}
    />
  )
}

const cards = () => screen.queryAllByTestId("post-card")
const nutXemThem = () => screen.queryByRole("button", { name: "Xem thêm" })

beforeEach(() => {
  fakeSession.start()
})

describe("UserPosts — cursor keyset (Đ-2.11)", () => {
  it("trang đầu KHÔNG gửi cursor, gửi đúng limit mặc định 20", async () => {
    const seen = phucVuHaiTrang(trang([bai("p1")], null), trang([], null))
    moManHinh()

    await waitFor(() => expect(cards()).toHaveLength(1))
    // `?cursor=undefined` là 400 `errors.cursor`; `limit` ngoài 1..50 là 400 `errors.limit`.
    expect(seen).toEqual(["limit=20"])
  })

  it("nối hai trang: cursor truyền lại NGUYÊN VẸN, bài không lặp", async () => {
    const seen = phucVuHaiTrang(
      trang([bai("p1"), bai("p2")], CURSOR_TRANG_2),
      trang([bai("p3"), bai("p4")], null)
    )
    const user = userEvent.setup()
    moManHinh()

    await waitFor(() => expect(cards()).toHaveLength(2))
    await user.click(nutXemThem()!)

    await waitFor(() => expect(cards()).toHaveLength(4))
    expect(cards().map((c) => c.dataset.postId)).toEqual([
      "p1",
      "p2",
      "p3",
      "p4",
    ])
    // Chuỗi opaque đi nguyên si — FE không encode lại, không cắt, không dựng.
    expect(seen[1]).toBe(
      `cursor=${encodeURIComponent(CURSOR_TRANG_2)}&limit=20`
    )
    expect(
      decodeURIComponent(seen[1]!.split("&")[0]!.slice("cursor=".length))
    ).toBe(CURSOR_TRANG_2)
  })

  it("`nextCursor: null` → nút Xem thêm BIẾN MẤT, không phải bị tắt", async () => {
    phucVuHaiTrang(trang([bai("p1")], null), trang([], null))
    moManHinh()

    await waitFor(() => expect(cards()).toHaveLength(1))
    // Còn nút mà bấm không ra gì là mời người dùng bấm mãi.
    expect(nutXemThem()).toBeNull()
  })

  it("lô TRÙNG không sinh card lặp — cùng `postId` ở hai trang", async () => {
    // Ca thật: một bài mới chen vào đầu danh sách giữa hai lượt gọi làm lô sau đội lại một phần tử.
    phucVuHaiTrang(
      trang([bai("p1"), bai("p2")], CURSOR_TRANG_2),
      trang([bai("p2"), bai("p3")], null)
    )
    const user = userEvent.setup()
    moManHinh()

    await waitFor(() => expect(cards()).toHaveLength(2))
    await user.click(nutXemThem()!)

    await waitFor(() => expect(cards()).toHaveLength(3))
    expect(cards().map((c) => c.dataset.postId)).toEqual(["p1", "p2", "p3"])
  })

  it("bấm Xem thêm hai lần KHI LƯỢT ĐẦU CÒN BAY chỉ gọi MỘT lượt", async () => {
    const seen: string[] = []
    server.use(
      http.get(POSTS, async ({ request }) => {
        const cursor = new URL(request.url).searchParams.get("cursor")
        seen.push(request.url.split("?")[1] ?? "")
        if (cursor === null)
          return HttpResponse.json(trang([bai("p1")], CURSOR_TRANG_2))
        // Trang sau CHẬM — đó là điều kiện duy nhất để cú bấm thứ hai có cửa chen vào. Không có độ trễ
        // này thì lượt đầu đã xong, `nextCursor` đã là `null`, và nút đã biến mất trước cú bấm thứ hai:
        // test xanh mà không chứng minh gì (đột biến bỏ chốt `pendingRef` vẫn lọt).
        await delay(60)
        return HttpResponse.json(trang([bai("p2")], null))
      })
    )
    moManHinh()

    await waitFor(() => expect(cards()).toHaveLength(1))
    const nut = nutXemThem()!
    // `fireEvent` chứ không `userEvent`: `userEvent.click` await giữa hai cú bấm và lượt đầu kịp xong.
    fireEvent.click(nut)
    fireEvent.click(nut)

    await waitFor(() => expect(cards()).toHaveLength(2))
    // Hai lượt gọi cho cùng một cursor là hai lô giống hệt — và một lần đốt hạn mức vô ích.
    expect(seen).toHaveLength(2)
  })

  it("`loadMore` gọi hai lần liền chỉ bắn MỘT request — chốt ở hook, không ở nút", async () => {
    // Trên màn, `disabled={page.pending}` đã chặn cú bấm thứ hai, nên test qua nút KHÔNG chứng minh được
    // chốt trong hook. Chốt đó vẫn phải có: GĐ4 (feed) thay nút bằng `IntersectionObserver` — không có
    // `disabled` nào để dựa, và observer bắn liên tiếp là chuyện bình thường.
    const seen: string[] = []
    server.use(
      http.get(POSTS, async ({ request }) => {
        const cursor = new URL(request.url).searchParams.get("cursor")
        seen.push(request.url.split("?")[1] ?? "")
        if (cursor === null)
          return HttpResponse.json(trang([bai("p1")], CURSOR_TRANG_2))
        await delay(60)
        return HttpResponse.json(trang([bai("p2")], null))
      })
    )

    let goiThem: () => void = () => {}
    function Tran() {
      const page = useUserPosts(userId)
      goiThem = page.loadMore
      return <span data-testid="so-bai">{page.items.length}</span>
    }
    render(<Tran />)

    await waitFor(() =>
      expect(screen.getByTestId("so-bai")).toHaveTextContent("1")
    )
    act(() => {
      goiThem()
      goiThem()
    })

    await waitFor(() =>
      expect(screen.getByTestId("so-bai")).toHaveTextContent("2")
    )
    expect(seen).toHaveLength(2)
  })

  it("đổi `userId`: KHÔNG nháy bài của người cũ trong lúc bài người mới đang về", async () => {
    server.use(
      http.get(POSTS, ({ params }) =>
        HttpResponse.json(trang([bai(`bai-cua-${params.userId}`)], null))
      )
    )
    const { rerender } = render(
      <UserPosts userId="u1" title="Bài" emptyMessage="Chưa có bài." />
    )

    await waitFor(() =>
      expect(cards().map((c) => c.dataset.postId)).toEqual(["bai-cua-u1"])
    )

    rerender(<UserPosts userId="u2" title="Bài" emptyMessage="Chưa có bài." />)
    // Dữ liệu cũ hết hiệu lực NGAY vì `key` không còn khớp — không ai phải xóa nó bằng tay, và bài của
    // người này không bao giờ hiện dưới tên người kia.
    expect(cards()).toHaveLength(0)
    expect(screen.getByTestId("post-list-skeleton")).toBeInTheDocument()

    await waitFor(() =>
      expect(cards().map((c) => c.dataset.postId)).toEqual(["bai-cua-u2"])
    )
  })

  it("`userId === null`: chưa biết xem bài của ai thì KHÔNG gọi gì", async () => {
    const seen: string[] = []
    server.use(
      http.get(POSTS, ({ request }) => {
        seen.push(request.url)
        return HttpResponse.json(trang([], null))
      })
    )
    moManHinh(null)

    await waitFor(() =>
      expect(screen.getByTestId("post-list-skeleton")).toBeInTheDocument()
    )
    expect(seen).toEqual([])
  })
})

describe("UserPosts — trạng thái rỗng và lỗi", () => {
  it("chưa có bài nào: một câu + nút Đăng bài đầu tiên, KHÔNG để màn trắng", async () => {
    phucVuHaiTrang(trang([], null), trang([], null))
    moManHinh()

    await waitFor(() =>
      expect(screen.getByTestId("post-list-empty")).toBeInTheDocument()
    )
    expect(screen.getByText("Bạn chưa đăng bài nào.")).toBeInTheDocument()
    expect(
      screen.getByRole("link", { name: "Đăng bài đầu tiên" })
    ).toHaveAttribute("href", "/compose")
  })

  it("trang đầu hỏng: KHÔNG nói 'chưa đăng bài nào', có nút Thử lại", async () => {
    let lan = 0
    server.use(
      http.get(POSTS, () => {
        lan += 1
        if (lan === 1) return HttpResponse.error()
        return HttpResponse.json(trang([bai("p1")], null))
      })
    )
    const user = userEvent.setup()
    moManHinh()

    await waitFor(() =>
      expect(
        screen.getByText("Không kết nối được máy chủ.")
      ).toBeInTheDocument()
    )
    // "Bạn chưa đăng bài nào" là một câu SAI cho người vừa mất mạng — họ có bài, chỉ là chưa tải được.
    expect(screen.queryByTestId("post-list-empty")).toBeNull()

    await user.click(screen.getByRole("button", { name: "Thử lại" }))
    await waitFor(() => expect(cards()).toHaveLength(1))
  })

  it("trang SAU hỏng: danh sách cũ VẪN còn trên màn", async () => {
    let lan = 0
    server.use(
      http.get(POSTS, ({ request }) => {
        lan += 1
        if (
          new URL(request.url).searchParams.get("cursor") !== null &&
          lan === 2
        )
          return HttpResponse.error()
        return HttpResponse.json(trang([bai("p1")], CURSOR_TRANG_2))
      })
    )
    const user = userEvent.setup()
    moManHinh()

    await waitFor(() => expect(cards()).toHaveLength(1))
    await user.click(nutXemThem()!)

    await waitFor(() =>
      expect(
        screen.getByText("Không kết nối được máy chủ.")
      ).toBeInTheDocument()
    )
    // Xóa danh sách đang đúng vì một lô lỗi là phạt người dùng cho việc họ không làm.
    expect(cards()).toHaveLength(1)
  })
})

describe("PostCard — nội dung một bài", () => {
  it("`reactionCounts: {}` rỗng render được, KHÔNG phải `null` (Đ-2.12)", async () => {
    phucVuHaiTrang(
      trang([bai("p1", { reactionCounts: {}, commentCount: 0 })], null),
      trang([], null)
    )
    moManHinh()

    await waitFor(() => expect(cards()).toHaveLength(1))
    expect(screen.getByTestId("reaction-count")).toHaveTextContent("0 cảm xúc")
    expect(screen.getByTestId("comment-count")).toHaveTextContent("0 bình luận")
  })

  it("có cảm xúc thì cộng tổng các loại", async () => {
    phucVuHaiTrang(
      trang([bai("p1", { reactionCounts: { like: 3, love: 2 } })], null),
      trang([], null)
    )
    moManHinh()

    await waitFor(() => expect(cards()).toHaveLength(1))
    expect(screen.getByTestId("reaction-count")).toHaveTextContent("5 cảm xúc")
  })

  it("GĐ2 KHÔNG có nút bình luận hay cảm xúc — endpoint là GĐ3", async () => {
    phucVuHaiTrang(trang([bai("p1")], null), trang([], null))
    moManHinh()

    await waitFor(() => expect(cards()).toHaveLength(1))
    const card = within(cards()[0]!)
    expect(card.queryByRole("button")).toBeNull()
  })

  it("nhãn 'đã chỉnh sửa' theo `editedAt`, không theo so ngày", async () => {
    phucVuHaiTrang(
      trang(
        [
          bai("p1", { editedAt: null }),
          bai("p2", { editedAt: "2026-09-20T03:00:00Z" }),
        ],
        null
      ),
      trang([], null)
    )
    moManHinh()

    await waitFor(() => expect(cards()).toHaveLength(2))
    expect(within(cards()[0]!).queryByTestId("edited-badge")).toBeNull()
    expect(within(cards()[1]!).getByTestId("edited-badge")).toHaveTextContent(
      "đã chỉnh sửa"
    )
  })

  it("ảnh xếp theo `position`, không theo thứ tự phần tử trong mảng", async () => {
    phucVuHaiTrang(
      trang(
        [
          bai("p1", {
            media: [
              {
                url: "https://r2.example.test/b.jpg",
                contentType: "image/jpeg",
                position: 1,
              },
              {
                url: "https://r2.example.test/a.jpg",
                contentType: "image/jpeg",
                position: 0,
              },
            ],
          }),
        ],
        null
      ),
      trang([], null)
    )
    moManHinh()

    await waitFor(() => expect(cards()).toHaveLength(1))
    expect(
      screen.getAllByTestId("post-image").map((i) => i.getAttribute("src"))
    ).toEqual([
      "https://r2.example.test/a.jpg",
      "https://r2.example.test/b.jpg",
    ])
  })

  it("card trong DANH SÁCH dẫn tới trang chi tiết của đúng bài đó", async () => {
    phucVuHaiTrang(trang([bai("p1")], null), trang([], null))
    moManHinh()

    await waitFor(() => expect(cards()).toHaveLength(1))
    expect(
      within(cards()[0]!).getByRole("link", { name: "Xem bài" })
    ).toHaveAttribute("href", "/posts/p1")
  })

  it("tên tác giả dẫn tới hồ sơ của họ", async () => {
    phucVuHaiTrang(trang([bai("p1")], null), trang([], null))
    moManHinh()

    await waitFor(() => expect(cards()).toHaveLength(1))
    expect(
      within(cards()[0]!).getByRole("link", { name: post.author.displayName })
    ).toHaveAttribute("href", `/users/${post.author.userId}`)
  })

  it("mức riêng tư hiện đúng nhãn, và `friends` nói thật về GĐ2", async () => {
    phucVuHaiTrang(
      trang([bai("p1", { privacy: "friends" })], null),
      trang([], null)
    )
    moManHinh()

    await waitFor(() => expect(cards()).toHaveLength(1))
    expect(within(cards()[0]!).getByText("Bạn bè")).toBeInTheDocument()
  })
})
