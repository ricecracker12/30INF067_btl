import { fireEvent, render, screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { StrictMode } from "react"
import { http, HttpResponse } from "msw"
import { beforeEach, describe, expect, it, vi } from "vitest"

import { BFF_URL } from "@/lib/api/config"
import type { PostResponse } from "@/lib/api/types"
import { post, r2Host } from "@/mocks/fixtures"
import { server } from "@/mocks/node"
import { fakeSession } from "@/mocks/session"

import { PostDetail } from "./post-detail"

// `PostDetail` rời trang sau khi xóa bài (E6 bước 3) — `useRouter` phải có ở mọi ca, kể cả ca không xóa.
const replace = vi.fn()
vi.mock("next/navigation", () => ({
  useRouter: () => ({ replace }),
}))

// 404 của `GET /posts/{id}` có BA nghĩa (Mục 7.4): không tồn tại · đã xóa mềm (kể cả với chính tác giả,
// Mục 7.3) · BR-02 không cho xem. Server cố ý trả cùng một phản hồi cho cả ba. File này giữ lời hứa rằng
// FE cũng chỉ nói MỘT câu — thêm một câu thứ hai là dùng status code để khai tài nguyên nào có thật.

const POST = `${BFF_URL}/api/posts/:postId`

function problem(status: number, title: string) {
  return HttpResponse.json(
    { type: "t", title, status, traceId: "x" },
    { status, headers: { "Content-Type": "application/problem+json" } }
  )
}

function bai(over: Partial<PostResponse> = {}): PostResponse {
  return { ...post, ...over }
}

const anh = (url: string) => ({
  url,
  contentType: "image/jpeg" as const,
  position: 0,
})

beforeEach(() => {
  replace.mockClear()
  fakeSession.start()
})

describe("PostDetail — 404 một câu duy nhất", () => {
  it("404 → 'Không tìm thấy bài viết.' và KHÔNG có nút Thử lại", async () => {
    server.use(http.get(POST, () => problem(404, "Không tìm thấy tài nguyên")))
    render(<PostDetail postId="p1" />)

    await waitFor(() =>
      expect(screen.getByTestId("post-not-found")).toBeInTheDocument()
    )
    expect(screen.getByText("Không tìm thấy bài viết.")).toBeInTheDocument()
    // Thử lại bao nhiêu lần cũng vậy — 404 là câu trả lời cuối cùng, không phải sự cố.
    expect(screen.queryByRole("button", { name: "Thử lại" })).toBeNull()
  })

  it("không nói bài có tồn tại hay không: không có chữ 'đã bị xóa' đứng một mình", async () => {
    server.use(http.get(POST, () => problem(404, "Không tìm thấy tài nguyên")))
    render(<PostDetail postId="p1" />)

    await waitFor(() =>
      expect(screen.getByTestId("post-not-found")).toBeInTheDocument()
    )
    const text = screen.getByTestId("post-not-found").textContent ?? ""
    // Câu phụ được phép liệt kê CẢ HAI khả năng, nhưng không được khẳng định cái nào.
    expect(text).toContain("hoặc bạn không có quyền xem bài này")
    expect(text).not.toMatch(/Bài (này )?đã bị xóa\./)
  })

  it("5xx thì KHÁC 404: có nút Thử lại, bấm là gọi lại", async () => {
    let lan = 0
    server.use(
      http.get(POST, () => {
        lan += 1
        if (lan === 1) return problem(500, "Lỗi máy chủ")
        return HttpResponse.json(bai())
      })
    )
    const user = userEvent.setup()
    render(<PostDetail postId="p1" />)

    await waitFor(() =>
      expect(
        screen.getByRole("button", { name: "Thử lại" })
      ).toBeInTheDocument()
    )
    await user.click(screen.getByRole("button", { name: "Thử lại" }))

    await waitFor(() =>
      expect(screen.getByTestId("post-card")).toBeInTheDocument()
    )
    expect(lan).toBe(2)
  })
})

describe("PostDetail — ảnh presigned hết hạn (Đ-2.9)", () => {
  it("ảnh vỡ → nạp lại bài ĐÚNG MỘT LẦN, URL mới thay thẻ img", async () => {
    const cu = `${r2Host}/bucket/anh.jpg?X-Amz-Signature=het-han`
    const moi = `${r2Host}/bucket/anh.jpg?X-Amz-Signature=con-han`
    let lan = 0
    server.use(
      http.get(POST, () => {
        lan += 1
        return HttpResponse.json(bai({ media: [anh(lan === 1 ? cu : moi)] }))
      })
    )
    render(<PostDetail postId="p1" />)

    await waitFor(() =>
      expect(screen.getByTestId("post-image")).toHaveAttribute("src", cu)
    )

    const theCu = screen.getByTestId("post-image")
    // jsdom không tải ảnh thật; `error` trên thẻ `<img>` là đúng thứ trình duyệt bắn khi chữ ký hết hạn.
    fireEvent.error(theCu)
    await waitFor(() =>
      expect(screen.getByTestId("post-image")).toHaveAttribute("src", moi)
    )
    expect(lan).toBe(2)
    // Thẻ `<img>` phải là thẻ MỚI, không phải thẻ cũ đổi `src`: `key={m.url}` ép React thay node, và đó
    // là thứ buộc trình duyệt tải lại thay vì dùng lại ảnh hỏng đang nằm trong cache. jsdom không có
    // cache ảnh nên chỉ so được danh tính node — nhưng chính danh tính node là cơ chế.
    expect(screen.getByTestId("post-image")).not.toBe(theCu)

    // Lần vỡ THỨ HAI là R2 hỏng thật — nạp lại nữa chỉ là vòng lặp request.
    fireEvent.error(screen.getByTestId("post-image"))
    await waitFor(() =>
      expect(screen.getByTestId("media-broken")).toBeInTheDocument()
    )
    expect(lan).toBe(2)
  })

  it("nạp lại hỏng (bài vừa bị xóa) thì chỉ báo ảnh vỡ, không dựng thêm lỗi", async () => {
    let lan = 0
    server.use(
      http.get(POST, () => {
        lan += 1
        if (lan === 1)
          return HttpResponse.json(bai({ media: [anh(`${r2Host}/a.jpg`)] }))
        return problem(404, "Không tìm thấy tài nguyên")
      })
    )
    render(<PostDetail postId="p1" />)

    await waitFor(() =>
      expect(screen.getByTestId("post-image")).toBeInTheDocument()
    )
    fireEvent.error(screen.getByTestId("post-image"))

    await waitFor(() =>
      expect(screen.getByTestId("media-broken")).toBeInTheDocument()
    )
    // Bài vẫn hiện — người dùng đang đọc nó; đá họ sang trang "không tìm thấy" vì một tấm ảnh là quá tay.
    expect(screen.getByTestId("post-card")).toBeInTheDocument()
  })
})

describe("PostDetail — xóa bài (E6)", () => {
  it("xóa xong thì RỜI trang — URL cũ đã chết", async () => {
    server.use(
      http.get(POST, () => HttpResponse.json(bai({ canEdit: true }))),
      http.delete(POST, () => new HttpResponse(null, { status: 204 }))
    )
    const user = userEvent.setup()
    render(<PostDetail postId="p1" />)

    await waitFor(() =>
      expect(screen.getByRole("button", { name: "Xóa" })).toBeInTheDocument()
    )
    await user.click(screen.getByRole("button", { name: "Xóa" }))
    await user.click(screen.getByRole("button", { name: "Xóa bài" }))

    // Sau xóa mềm, `GET /posts/{id}` trả 404 kể cả với chính tác giả (Mục 7.3): ở lại là ở lại trên một
    // URL sẽ trả "không tìm thấy" ngay lần tải lại đầu tiên.
    await waitFor(() => expect(replace).toHaveBeenCalledWith("/me"))
  })

  it("sửa xong thì ở LẠI trang, hiện nội dung mới", async () => {
    server.use(
      http.get(POST, () =>
        HttpResponse.json(bai({ canEdit: true, body: "Chào" }))
      ),
      http.patch(POST, () =>
        HttpResponse.json(
          bai({
            canEdit: true,
            body: "Đã sửa",
            editedAt: "2026-09-21T03:00:00Z",
          })
        )
      )
    )
    const user = userEvent.setup()
    render(<PostDetail postId="p1" />)

    await waitFor(() =>
      expect(screen.getByRole("button", { name: "Sửa" })).toBeInTheDocument()
    )
    await user.click(screen.getByRole("button", { name: "Sửa" }))
    const o = screen.getByLabelText("Nội dung")
    await user.clear(o)
    await user.type(o, "Đã sửa")
    await user.click(screen.getByRole("button", { name: "Lưu" }))

    await waitFor(() => expect(screen.getByText("Đã sửa")).toBeInTheDocument())
    expect(replace).not.toHaveBeenCalled()
    expect(screen.getByTestId("edited-badge")).toBeInTheDocument()
  })
})

describe("PostDetail — đổi bài", () => {
  it("đổi `postId`: KHÔNG nháy bài cũ trong lúc bài mới đang về", async () => {
    server.use(
      http.get(POST, ({ params }) =>
        HttpResponse.json(bai({ postId: String(params.postId) }))
      )
    )
    const { rerender } = render(<PostDetail postId="p1" />)

    await waitFor(() =>
      expect(screen.getByTestId("post-card")).toHaveAttribute(
        "data-post-id",
        "p1"
      )
    )

    rerender(<PostDetail postId="p2" />)
    // Ngay sau khi đổi id, dữ liệu cũ đã hết hiệu lực vì `key` không khớp — màn về skeleton, không giữ p1.
    expect(screen.getByTestId("post-detail-skeleton")).toBeInTheDocument()
    expect(screen.queryByTestId("post-card")).toBeNull()

    await waitFor(() =>
      expect(screen.getByTestId("post-card")).toHaveAttribute(
        "data-post-id",
        "p2"
      )
    )
  })

  it("trang chi tiết KHÔNG tự dẫn tới chính nó", async () => {
    server.use(http.get(POST, () => HttpResponse.json(bai({ postId: "p1" }))))
    render(<PostDetail postId="p1" />)

    await waitFor(() =>
      expect(screen.getByTestId("post-card")).toBeInTheDocument()
    )
    expect(screen.queryByRole("link", { name: "Xem bài" })).toBeNull()
  })
})

// Ca StrictMode — mỗi màn sở hữu tài nguyên hủy được có ĐÚNG một ca. `render(<X />)` gắn component một
// lần, còn Next dev bọc `<StrictMode>`: mount → unmount → mount lại. Ca này khẳng định TRẠNG THÁI CUỐI
// đạt được, KHÔNG đếm số request — dưới StrictMode số request tăng gấp đôi một cách hợp lệ, trộn hai thứ
// vào một ca là tự làm ca test giòn.

describe("PostDetail — sống được dưới StrictMode", () => {
  it("mount hai lần vẫn hiện bài, không kẹt ở khung chờ", async () => {
    server.use(http.get(POST, () => HttpResponse.json(bai({ postId: "p1" }))))
    render(
      <StrictMode>
        <PostDetail postId="p1" />
      </StrictMode>
    )

    await waitFor(() =>
      expect(screen.getByTestId("post-card")).toBeInTheDocument()
    )
    expect(screen.queryByRole("alert")).not.toBeInTheDocument()
  })
})
