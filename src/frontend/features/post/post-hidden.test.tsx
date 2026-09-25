import { render, screen } from "@testing-library/react"
import { describe, expect, it, vi } from "vitest"

import type { PostResponse } from "@/lib/api/types"
import { post } from "@/mocks/fixtures"

import { PostItem } from "./post-item"

// GĐ6 E9 — biểu ngữ "bài bị ẩn" (Đ-6.14, BR-07): chỉ tác giả nhận `moderation` (người khác 404 ở server). Nút Sửa không có trong
// DOM (sửa để tự gỡ ẩn là lách kiểm duyệt — server trả 409); nút Xóa giữ.

vi.mock("next/navigation", () => ({ useRouter: () => ({ replace: vi.fn() }) }))

const cuaToi: PostResponse = { ...post, canEdit: true }

describe("PostItem — bài bị kiểm duyệt ẩn", () => {
  it("có moderation → biểu ngữ kèm lý do tiếng Việt; không Sửa, còn Xóa", () => {
    render(
      <PostItem
        post={{
          ...cuaToi,
          moderation: { status: "hidden", reasonCode: "harassment", hiddenAt: "2026-09-25T08:00:00Z" },
        }}
        onChanged={() => undefined}
      />
    )
    expect(screen.getByTestId("post-hidden-banner")).toHaveTextContent(
      "Bài viết này đã bị ẩn vì vi phạm tiêu chuẩn cộng đồng (lý do: Quấy rối hoặc bắt nạt). Chỉ bạn nhìn thấy."
    )
    expect(screen.queryByRole("button", { name: "Sửa" })).toBeNull()
    expect(screen.getByRole("button", { name: "Xóa" })).toBeInTheDocument()
  })

  it("bài thường (moderation null/vắng) → không biểu ngữ, có Sửa", () => {
    render(<PostItem post={{ ...cuaToi, moderation: null }} onChanged={() => undefined} />)
    expect(screen.queryByTestId("post-hidden-banner")).toBeNull()
    expect(screen.getByRole("button", { name: "Sửa" })).toBeInTheDocument()
  })
})
