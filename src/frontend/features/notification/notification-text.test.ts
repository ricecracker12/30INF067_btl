import { describe, expect, it } from "vitest"

import type { NotificationResponse } from "@/lib/api/types"
import { notificationComment, notificationModeration } from "@/mocks/fixtures"

import { badgeText, notificationHref, notificationText } from "./notification-text"

// GĐ6 E3 — câu do FE ghép từ `type` × `actorCount` (Mục 8.3, 10.3 "ghép câu thông báo phía FE"). Hàm thuần.

const n = (over: Partial<NotificationResponse>): NotificationResponse => ({
  ...notificationComment,
  ...over,
})
const an = notificationComment.actor

describe("notificationText — type × actorCount", () => {
  it.each([
    [1, "An đã bình luận về bài viết của bạn."],
    [2, "An và 1 người khác đã bình luận về bài viết của bạn."],
    [4, "An và 3 người khác đã bình luận về bài viết của bạn."],
  ])("comment, %i người", (actorCount, text) => {
    expect(
      notificationText(
        n({ actorCount, actor: { ...an, displayName: "An" } })
      )
    ).toBe(text)
  })

  it("reaction: bài và bình luận là hai câu", () => {
    const actor = { ...an, displayName: "Bình" }
    expect(notificationText(n({ type: "reaction", actor, actorCount: 3 }))).toBe(
      "Bình và 2 người khác đã bày tỏ cảm xúc về bài viết của bạn."
    )
    expect(
      notificationText(
        n({
          type: "reaction",
          actor,
          actorCount: 1,
          target: { type: "comment", id: "c1", postId: "p1" },
        })
      )
    ).toBe("Bình đã bày tỏ cảm xúc về bình luận của bạn.")
  })

  it("các loại còn lại", () => {
    const actor = { ...an, displayName: "An" }
    const one = { actor, actorCount: 1 }
    expect(notificationText(n({ ...one, type: "friend_request" }))).toBe(
      "An đã gửi lời mời kết bạn."
    )
    expect(notificationText(n({ ...one, type: "friend_accepted" }))).toBe(
      "An đã chấp nhận lời mời kết bạn của bạn."
    )
    expect(notificationText(n({ ...one, type: "reply" }))).toBe(
      "An đã trả lời bình luận của bạn."
    )
    expect(notificationText(n({ ...one, type: "message" }))).toBe(
      "An đã nhắn tin cho bạn."
    )
    expect(notificationText(n({ ...one, type: "tag" }))).toBe(
      "An đã nhắc đến bạn trong một bình luận."
    )
  })

  it("moderation: không tên ai, có lý do bằng nhãn tiếng Việt", () => {
    expect(notificationText(notificationModeration)).toBe(
      "Bài viết của bạn đã bị ẩn vì vi phạm tiêu chuẩn cộng đồng (lý do: Spam hoặc quảng cáo)."
    )
  })

  it("actor null (người kia không còn hồ sơ) → 'Một người dùng', không chuỗi rỗng", () => {
    expect(notificationText(n({ actor: null, actorCount: 1 }))).toBe(
      "Một người dùng đã bình luận về bài viết của bạn."
    )
  })
})

describe("notificationHref", () => {
  it("bài → /posts/{id}; bình luận → bài chứa nó; hội thoại → /messages/{id}", () => {
    expect(notificationHref(notificationComment)).toBe(
      `/posts/${notificationComment.target.id}`
    )
    expect(
      notificationHref(n({ target: { type: "comment", id: "c1", postId: "p1" } }))
    ).toBe("/posts/p1")
    expect(
      notificationHref(
        n({ type: "message", target: { type: "conversation", id: "k1", postId: null } })
      )
    ).toBe("/messages/k1")
  })

  it("lời mời kết bạn → /friends (E2E-02); chấp nhận → hồ sơ người kia", () => {
    const target = { type: "user" as const, id: "u1", postId: null }
    expect(notificationHref(n({ type: "friend_request", target }))).toBe("/friends")
    expect(notificationHref(n({ type: "friend_accepted", target }))).toBe("/users/u1")
  })
})

describe("badgeText", () => {
  it("'9+' từ 10", () => {
    expect(badgeText(9)).toBe("9")
    expect(badgeText(10)).toBe("9+")
    expect(badgeText(250)).toBe("9+")
  })
})
