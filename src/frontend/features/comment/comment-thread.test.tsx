import { render, screen, waitFor, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { http, HttpResponse } from "msw"
import { StrictMode } from "react"
import { describe, expect, it } from "vitest"

import { BFF_URL } from "@/lib/api/config"
import type { CommentPage, CommentResponse } from "@/lib/api/types"
import { comment, deletedComment } from "@/mocks/fixtures"
import { server } from "@/mocks/node"

import { CommentThread } from "./comment-thread"

// Cây bình luận qua `msw/node` (Mục 10.5): tải lười theo cấp, cấp 3 không có nút Trả lời, bình luận đã xóa giữ nhánh, 400
// `errors.parentId` hiện dưới ô trả lời, 404 không lộ bài, viết/xóa đổi số tại chỗ. Đúng MỘT ca `<StrictMode>` (luật FE Mục 9).

const POST_ID = comment.postId
const COMMENTS = `${BFF_URL}/api/posts/:postId/comments`
const REPLIES = `${BFF_URL}/api/comments/:commentId/replies`
const PROBLEM = { "Content-Type": "application/problem+json" }

/** Cursor opaque của server — FE truyền lại NGUYÊN VẸN. */
const CURSOR_2 = "MjAyNi0wOS0yNFQwODoyMDowMFo-MDE5MmYzZDA_"

function binhLuan(
  id: string,
  over: Partial<CommentResponse> = {}
): CommentResponse {
  return { ...comment, commentId: id, replyCount: 0, ...over }
}

function page(
  items: CommentResponse[],
  nextCursor: string | null = null
): CommentPage {
  return { items, nextCursor }
}

function moCay(initialCount = 2) {
  return render(
    <CommentThread
      postId={POST_ID}
      initialCount={initialCount}
      renderReactions={(c) => (
        <span data-testid="reactions-slot">{c.commentId}</span>
      )}
    />
  )
}

const items = () => screen.queryAllByTestId("comment")

describe("CommentThread", () => {
  it("tải bình luận gốc; bình luận đã xóa giữ chỗ, không tác giả/nội dung, vẫn có 'Xem N phản hồi'", async () => {
    moCay()

    await waitFor(() => expect(items()).toHaveLength(2))
    const [first, removed] = items()
    expect(within(first!).getByText("Ảnh đẹp quá!")).toBeInTheDocument()
    expect(within(removed!).getByTestId("comment-removed")).toHaveTextContent(
      "Bình luận đã bị xóa."
    )
    expect(within(removed!).queryByText("Ảnh đẹp quá!")).toBeNull()
    expect(
      within(removed!).getByRole("button", { name: "Xem 1 phản hồi" })
    ).toBeInTheDocument()
    // Slot cảm xúc chỉ cho bình luận còn hiển thị.
    expect(within(first!).getByTestId("reactions-slot")).toBeInTheDocument()
    expect(within(removed!).queryByTestId("reactions-slot")).toBeNull()
  })

  it("bấm 'Xem N phản hồi' mới tải nhánh (Đ-3.6) — với limit 10", async () => {
    const seen: string[] = []
    server.use(
      http.get(REPLIES, ({ request, params }) => {
        seen.push(
          `${String(params.commentId)}?${request.url.split("?")[1] ?? ""}`
        )
        return HttpResponse.json(
          page([
            binhLuan("r1", {
              depth: 2,
              parentId: comment.commentId,
              body: "Phản hồi cấp 2",
            }),
          ])
        )
      })
    )
    moCay()
    await waitFor(() => expect(items()).toHaveLength(2))
    expect(seen).toEqual([])

    await userEvent.click(
      screen.getByRole("button", { name: "Xem 2 phản hồi" })
    )

    expect(await screen.findByText("Phản hồi cấp 2")).toBeInTheDocument()
    expect(seen).toEqual([`${comment.commentId}?limit=10`])
  })

  it("cấp 3 KHÔNG có nút Trả lời (BR-08); cấp 1 và 2 thì có", async () => {
    server.use(
      http.get(COMMENTS, () =>
        HttpResponse.json(
          page([
            binhLuan("c1", { depth: 1 }),
            binhLuan("c2", { depth: 2, parentId: "c1" }),
            binhLuan("c3", { depth: 3, parentId: "c2" }),
          ])
        )
      )
    )
    moCay()
    await waitFor(() => expect(items()).toHaveLength(3))

    const [c1, c2, c3] = items()
    expect(
      within(c1!).getByRole("button", { name: "Trả lời" })
    ).toBeInTheDocument()
    expect(
      within(c2!).getByRole("button", { name: "Trả lời" })
    ).toBeInTheDocument()
    expect(within(c3!).queryByRole("button", { name: "Trả lời" })).toBeNull()
  })

  it("400 errors.parentId hiện DƯỚI ô trả lời, đúng câu server (Đ-E5)", async () => {
    server.use(
      http.post(COMMENTS, () =>
        HttpResponse.json(
          {
            title: "Dữ liệu không hợp lệ",
            status: 400,
            traceId: "x",
            errors: { parentId: ["Bình luận cần trả lời không còn tồn tại."] },
          },
          { status: 400, headers: PROBLEM }
        )
      )
    )
    moCay()
    await waitFor(() => expect(items()).toHaveLength(2))
    const user = userEvent.setup()

    await user.click(
      within(items()[0]!).getByRole("button", { name: "Trả lời" })
    )
    const box = await screen.findByTestId("reply-composer")
    await user.type(within(box).getByRole("textbox"), "Muộn rồi")
    await user.click(within(box).getByRole("button", { name: "Trả lời" }))

    expect(
      await within(box).findByText("Bình luận cần trả lời không còn tồn tại.")
    ).toBeInTheDocument()
  })

  it("viết bình luận: chèn vào CUỐI danh sách, số bình luận tăng tại chỗ; rỗng thì chặn ở client với câu server", async () => {
    moCay(2)
    await waitFor(() => expect(items()).toHaveLength(2))
    const user = userEvent.setup()
    const box = screen.getByTestId("comment-composer")

    await user.click(within(box).getByRole("button", { name: "Bình luận" }))
    expect(
      within(box).getByText("Bình luận không được để trống.")
    ).toBeInTheDocument()

    await user.type(within(box).getByRole("textbox"), "Bình luận mới")
    await user.click(within(box).getByRole("button", { name: "Bình luận" }))

    await waitFor(() => expect(items()).toHaveLength(3))
    expect(within(items()[2]!).getByText("Bình luận mới")).toBeInTheDocument()
    expect(screen.getByTestId("comment-total")).toHaveTextContent("3 bình luận")
  })

  it("xóa bình luận của mình (AlertDialog): thành 'đã bị xóa' tại chỗ, nhánh còn, số giảm", async () => {
    server.use(
      http.get(COMMENTS, () =>
        HttpResponse.json(
          page([binhLuan("mine", { canDelete: true, replyCount: 1 })])
        )
      )
    )
    moCay(2)
    await waitFor(() => expect(items()).toHaveLength(1))
    const user = userEvent.setup()

    await user.click(within(items()[0]!).getByRole("button", { name: "Xóa" }))
    await user.click(
      within(await screen.findByRole("alertdialog")).getByRole("button", {
        name: "Xóa bình luận",
      })
    )

    await waitFor(() =>
      expect(
        within(items()[0]!).getByTestId("comment-removed")
      ).toBeInTheDocument()
    )
    expect(
      within(items()[0]!).getByRole("button", { name: "Xem 1 phản hồi" })
    ).toBeInTheDocument()
    expect(
      within(items()[0]!).queryByRole("button", { name: "Xóa" })
    ).toBeNull()
    expect(screen.getByTestId("comment-total")).toHaveTextContent("1 bình luận")
  })

  it("nút Xóa chỉ hiện theo canDelete của SERVER", async () => {
    moCay()
    await waitFor(() => expect(items()).toHaveLength(2))
    // Fixture `comment` có canDelete: false (bình luận của người khác).
    expect(
      within(items()[0]!).queryByRole("button", { name: "Xóa" })
    ).toBeNull()
  })

  it("404 (bài không còn xem được): MỘT câu, không nói bài có tồn tại hay không", async () => {
    server.use(
      http.get(COMMENTS, () =>
        HttpResponse.json(
          {
            title: "Không tìm thấy tài nguyên",
            status: 404,
            traceId: "x",
            detail: "Không tìm thấy bài viết.",
          },
          { status: 404, headers: PROBLEM }
        )
      )
    )
    moCay()

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Không tìm thấy bài viết hoặc bình luận."
    )
  })

  it("'Xem thêm bình luận' truyền cursor NGUYÊN VẸN, hết trang thì nút biến mất", async () => {
    const seen: (string | null)[] = []
    server.use(
      http.get(COMMENTS, ({ request }) => {
        const cursor = new URL(request.url).searchParams.get("cursor")
        seen.push(cursor)
        return HttpResponse.json(
          cursor === null
            ? page([binhLuan("a")], CURSOR_2)
            : page([binhLuan("b")], null)
        )
      })
    )
    moCay()
    await waitFor(() => expect(items()).toHaveLength(1))

    await userEvent.click(
      screen.getByRole("button", { name: "Xem thêm bình luận" })
    )

    await waitFor(() => expect(items()).toHaveLength(2))
    expect(seen).toEqual([null, CURSOR_2])
    expect(
      screen.queryByRole("button", { name: "Xem thêm bình luận" })
    ).toBeNull()
  })

  it("<StrictMode>: mount → unmount → mount vẫn ra đúng danh sách cuối, không lặp dòng", async () => {
    render(
      <StrictMode>
        <CommentThread postId={POST_ID} initialCount={2} />
      </StrictMode>
    )

    await waitFor(() => expect(items()).toHaveLength(2))
    expect(items().map((li) => li.getAttribute("data-comment-id"))).toEqual([
      comment.commentId,
      deletedComment.commentId,
    ])
  })
})
