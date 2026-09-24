import { describe, expect, it } from "vitest"

import type { ReactionSummary, ReactionType } from "@/lib/api/types"

import {
  failed,
  initialReaction,
  press,
  settled,
  total,
  view,
  type ReactionState,
  type Send,
} from "./reaction-reducer"

const summary = (
  reactionCounts: Record<string, number>,
  myReaction: ReactionType | null
): ReactionSummary => ({ reactionCounts, myReaction })

/**
 * Server giả: áp đúng luật của Đ-3.8 lên một bộ đếm thật, để test chuỗi bấm so với con số server SẼ trả — không tự bịa số.
 */
function fakeServer(start: ReactionSummary) {
  let current = start
  return (send: NonNullable<Send>): ReactionSummary => {
    current = view({ confirmed: current, desired: send.type, inFlight: false })
    return current
  }
}

describe("reaction-reducer (Đ-3.13)", () => {
  it("bấm → giao diện đổi NGAY, gửi đúng một request", () => {
    const s0 = initialReaction(summary({ like: 2 }, null))
    const { state, send } = press(s0, "like")

    expect(send).toEqual({ type: "like" })
    expect(view(state)).toEqual(summary({ like: 3 }, "like"))
    expect(state.inFlight).toBe(true)
  })

  it("chuỗi tim–bỏ–tim–bỏ nhanh (response chưa về) sinh TỐI ĐA HAI request, trạng thái cuối là lần bấm cuối", () => {
    const server = fakeServer(summary({ like: 5 }, null))
    let state: ReactionState = initialReaction(summary({ like: 5 }, null))
    const sent: NonNullable<Send>[] = []

    // Bốn lần bấm trong khi request đầu còn bay.
    for (const next of ["like", null, "like", null] as const) {
      const step = press(state, next)
      state = step.state
      if (step.send) sent.push(step.send)
    }
    expect(sent).toHaveLength(1)
    expect(view(state).myReaction).toBeNull()

    // Request đầu về: server nói "like" nhưng người dùng muốn "không" → gửi tiếp đúng một cái.
    let step = settled(state, server(sent[0]!))
    state = step.state
    if (step.send) sent.push(step.send)
    expect(sent).toHaveLength(2)

    step = settled(state, server(sent[1]!))
    state = step.state
    expect(step.send).toBeNull()
    expect(sent).toHaveLength(2)
    expect(state.inFlight).toBe(false)
    expect(view(state)).toEqual(summary({ like: 5 }, null))
  })

  it("bấm rồi bấm lại đúng trạng thái đang bay về — không gửi request thứ hai", () => {
    let { state } = press(initialReaction(summary({}, null)), "love")
    state = press(state, null).state
    state = press(state, "love").state

    const step = settled(state, summary({ love: 1 }, "love"))
    expect(step.send).toBeNull()
    expect(view(step.state)).toEqual(summary({ love: 1 }, "love"))
  })

  it("lỗi → rollback về con số server đã xác nhận", () => {
    const s0 = initialReaction(summary({ haha: 1 }, "haha"))
    const { state } = press(s0, "sad")
    expect(view(state)).toEqual(summary({ sad: 1 }, "sad"))

    const rolled = failed(state)
    expect(rolled.inFlight).toBe(false)
    expect(view(rolled)).toEqual(summary({ haha: 1 }, "haha"))
  })

  it("response về không theo thứ tự không làm sai trạng thái cuối — server thắng, mong muốn cuối được gửi tiếp", () => {
    let state = press(initialReaction(summary({}, null)), "wow").state
    state = press(state, "angry").state

    // Server trả kết quả của "wow" (request duy nhất đã gửi) kèm lượt của người khác vừa thả cùng lúc.
    const step = settled(state, summary({ wow: 2 }, "wow"))
    expect(step.send).toEqual({ type: "angry" })

    const done = settled(step.state, summary({ wow: 1, angry: 1 }, "angry"))
    expect(done.send).toBeNull()
    expect(view(done.state)).toEqual(summary({ wow: 1, angry: 1 }, "angry"))
  })

  it("đổi loại: số của loại cũ giảm, về 0 thì mất khóa — không bao giờ hiện 0 (Đ-3.8)", () => {
    const s = press(initialReaction(summary({ like: 1 }, "like")), "love").state

    expect(view(s).reactionCounts).toEqual({ love: 1 })
    expect(total(view(s))).toBe(1)
  })

  it("bấm lại đúng loại đang có khi không có gì bay — không gửi gì", () => {
    const step = press(initialReaction(summary({ like: 1 }, "like")), "like")
    expect(step.send).toBeNull()
    expect(step.state.inFlight).toBe(false)
  })
})
