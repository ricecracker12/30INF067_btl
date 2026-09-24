import { render, screen, waitFor, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { delay, http, HttpResponse } from "msw"
import { describe, expect, it } from "vitest"

import { BFF_URL } from "@/lib/api/config"
import type { ReactionSummary, ReactionType } from "@/lib/api/types"
import { server } from "@/mocks/node"

import { ReactionBar } from "./reaction-bar"
import { view } from "./reaction-reducer"

// Thanh cảm xúc qua `msw/node` (Đ-3.13): giao diện đổi NGAY khi bấm, chuỗi bấm nhanh sinh ≤ 2 request, lỗi thì quay về số server
// đã xác nhận. Server giả dưới đây NHỚ trạng thái và áp đúng luật Đ-3.8 — số FE hiện được so với số server thật sự sẽ trả.

const REACT = `${BFF_URL}/api/posts/:postId/reactions/me`
const POST_ID = "0192f3c1-8a4e-7c31-9f2a-6b5d4e3c2a10"

/** Server nhớ trạng thái; mỗi request chờ `ms` để test bấm được nhiều lần trong khi request đầu còn bay. */
function statefulServer(start: ReactionSummary, ms = 50) {
  let current = start
  const calls: (ReactionType | null)[] = []
  const apply = (type: ReactionType | null) => {
    calls.push(type)
    current = view({ confirmed: current, desired: type, inFlight: false })
    return HttpResponse.json(current)
  }
  server.use(
    http.put(REACT, async ({ request }) => {
      const { type } = (await request.json()) as { type: ReactionType }
      await delay(ms)
      return apply(type)
    }),
    http.delete(REACT, async () => {
      await delay(ms)
      return apply(null)
    })
  )
  return calls
}

function renderBar(initial: ReactionSummary) {
  return render(
    <ReactionBar target={{ kind: "post", id: POST_ID }} initial={initial} />
  )
}

const count = () => screen.getByTestId("reaction-count")

describe("ReactionBar (Đ-3.13)", () => {
  it("bấm Thích: nút sáng và số tăng NGAY, trước khi server trả lời", async () => {
    const calls = statefulServer(
      { reactionCounts: { like: 2 }, myReaction: null },
      200
    )
    renderBar({ reactionCounts: { like: 2 }, myReaction: null })

    await userEvent.click(screen.getByRole("button", { name: "Thích" }))

    // Chưa đợi request nào về.
    expect(count()).toHaveTextContent("3 cảm xúc")
    expect(
      screen.getByRole("button", { name: "Bỏ cảm xúc Thích" })
    ).toHaveAttribute("aria-pressed", "true")
    await waitFor(() => expect(calls).toEqual(["like"]), { timeout: 3000 })
  })

  it("bấm liên tục 10 lần trong khi request đầu còn bay: ĐÚNG HAI request, trạng thái cuối là lần bấm cuối (FE-02)", async () => {
    // Chốt giữ response ĐẦU tới khi đủ 10 cú bấm — không dựa vào độ trễ giả: khi cả bộ test chạy nặng, một cú bấm của
    // `userEvent` có thể chậm hơn độ trễ đó, request đầu về giữa hai cú bấm và mỗi cú bấm thành một chuỗi riêng (hợp lệ, nhưng
    // không phải kịch bản đang đo). Đo 2026-09-25: bản dùng `delay(150)` ra 4 request ở lượt chạy cả bộ.
    let release!: () => void
    const gate = new Promise<void>((r) => (release = r))
    let current: ReactionSummary = {
      reactionCounts: { like: 5 },
      myReaction: null,
    }
    const calls: (ReactionType | null)[] = []
    const apply = async (type: ReactionType | null) => {
      calls.push(type)
      if (calls.length === 1) await gate
      current = view({ confirmed: current, desired: type, inFlight: false })
      return HttpResponse.json(current)
    }
    server.use(
      http.put(REACT, async ({ request }) =>
        apply(((await request.json()) as { type: ReactionType }).type)
      ),
      http.delete(REACT, () => apply(null))
    )
    renderBar({ reactionCounts: { like: 5 }, myReaction: null })
    const user = userEvent.setup()

    for (let i = 0; i < 10; i++)
      await user.click(screen.getByRole("button", { name: /Thích/ }))
    expect(calls).toEqual(["like"])
    release()

    // 10 lần bật/tắt → chẵn → cuối cùng KHÔNG thả.
    await waitFor(() => expect(calls).toEqual(["like", null]), {
      timeout: 3000,
    })
    await waitFor(
      () =>
        expect(screen.getByRole("button", { name: "Thích" })).toHaveAttribute(
          "aria-pressed",
          "false"
        ),
      { timeout: 3000 }
    )
    expect(count()).toHaveTextContent("5 cảm xúc")
  })

  it("lỗi → quay về số server đã xác nhận, hiện câu của ngữ cảnh `reaction`", async () => {
    server.use(
      http.put(REACT, () =>
        HttpResponse.json(
          { title: "Không tìm thấy tài nguyên", status: 404, traceId: "x" },
          {
            status: 404,
            headers: { "Content-Type": "application/problem+json" },
          }
        )
      )
    )
    renderBar({ reactionCounts: { haha: 1 }, myReaction: "haha" })

    await userEvent.click(screen.getByRole("button", { name: "Chọn cảm xúc" }))
    await userEvent.click(
      within(await screen.findByRole("dialog")).getByRole("button", {
        name: "Buồn",
      })
    )

    expect(await screen.findByRole("alert")).toHaveTextContent(
      "Nội dung này không còn tồn tại."
    )
    expect(count()).toHaveTextContent("1 cảm xúc")
    expect(
      screen.getByRole("button", { name: "Bỏ cảm xúc Haha" })
    ).toHaveAttribute("aria-pressed", "true")
  })

  it("chọn loại trong bảng gửi đúng loại đó; chọn lại chính loại đang có là gỡ", async () => {
    const calls = statefulServer({ reactionCounts: {}, myReaction: null }, 0)
    renderBar({ reactionCounts: {}, myReaction: null })
    const user = userEvent.setup()

    await user.click(screen.getByRole("button", { name: "Chọn cảm xúc" }))
    await user.click(
      within(await screen.findByRole("dialog")).getByRole("button", {
        name: "Yêu thích",
      })
    )
    await waitFor(() => expect(calls).toEqual(["love"]), { timeout: 3000 })
    expect(count()).toHaveTextContent("1 cảm xúc")

    await user.click(screen.getByRole("button", { name: "Chọn cảm xúc" }))
    await user.click(
      within(await screen.findByRole("dialog")).getByRole("button", {
        name: "Yêu thích",
      })
    )
    await waitFor(() => expect(calls).toEqual(["love", null]), {
      timeout: 3000,
    })
    expect(count()).toHaveTextContent("0 cảm xúc")
  })
})
