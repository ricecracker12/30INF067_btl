import {
  fireEvent,
  render,
  screen,
  waitFor,
  within,
} from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { http, HttpResponse } from "msw"
import { beforeEach, describe, expect, it, vi } from "vitest"

import { BFF_URL } from "@/lib/api/config"
import type { PostResponse } from "@/lib/api/types"
import { post } from "@/mocks/fixtures"
import { server } from "@/mocks/node"
import { fakeSession } from "@/mocks/session"

import { buildPatch } from "./post-edit-form"
import { PostItem } from "./post-item"

vi.mock("next/navigation", () => ({
  useRouter: () => ({ replace: vi.fn() }),
}))

// E6 giữ đúng một luật vàng: **quyền đến từ server**. `canEdit` do server tính (`author_id == actorId`);
// FE không bao giờ so `post.author.userId` với `me.userId`. File này canh cả hai đầu của luật đó — nút
// KHÔNG CÓ TRONG DOM khi `canEdit: false`, và có khi `canEdit: true`.

const POST = `${BFF_URL}/api/posts/:postId`

function problem(
  status: number,
  title: string,
  errors?: Record<string, string[]>
) {
  return HttpResponse.json(
    { type: "t", title, status, traceId: "x", ...(errors && { errors }) },
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

function moManHinh(p: PostResponse, onChanged = vi.fn()) {
  render(<PostItem post={p} onChanged={onChanged} />)
  return onChanged
}

const nutSua = () => screen.queryByRole("button", { name: "Sửa" })
const nutXoa = () => screen.queryByRole("button", { name: "Xóa" })
const nutLuu = () => screen.getByRole("button", { name: "Lưu" })

beforeEach(() => {
  fakeSession.start()
})

describe("buildPatch — thân PATCH, hàm thuần", () => {
  const goc = (over: Partial<PostResponse> = {}) =>
    bai({ body: "Chào", privacy: "public", ...over })

  it("không đổi gì → object RỖNG, và đó là tín hiệu để tắt nút Lưu", () => {
    expect(buildPatch(goc(), { body: "Chào", privacy: "public" })).toEqual({})
  })

  it("`body: null` của bài chỉ có ảnh quy về `\"\"` khi so, không thành 'đã đổi' giả", () => {
    // `post.body` là `null` còn ô soạn chữ hiển thị `""`: so hai thứ khác kiểu thì lần nào cũng "đã đổi",
    // và nút Lưu bật sẵn cho một thay đổi không tồn tại.
    expect(
      buildPatch(goc({ body: null }), { body: "", privacy: "public" })
    ).toEqual({})
  })

  it('xóa hết chữ → `body: ""`, KHÔNG phải `null`', () => {
    // `null` nghĩa là "không gửi" với `System.Text.Json`; gửi `null` là giữ nguyên chữ cũ.
    const patch = buildPatch(goc(), { body: "", privacy: "public" })
    expect(patch).toEqual({ body: "" })
    expect(patch.body).not.toBeNull()
  })

  it("giữ NGUYÊN khoảng trắng người dùng gõ — server mới là bên `NormalizeBody`", () => {
    expect(buildPatch(goc(), { body: "  Chào  ", privacy: "public" })).toEqual({
      body: "  Chào  ",
    })
  })

  it("đổi cả hai → gửi cả hai; đổi một → chỉ gửi một", () => {
    expect(buildPatch(goc(), { body: "Khác", privacy: "private" })).toEqual({
      body: "Khác",
      privacy: "private",
    })
    expect(buildPatch(goc(), { body: "Chào", privacy: "friends" })).toEqual({
      privacy: "friends",
    })
  })
})

describe("PostItem — quyền đến từ server (canEdit)", () => {
  it("`canEdit: false` → nút Sửa và Xóa KHÔNG CÓ TRONG DOM", () => {
    moManHinh(bai({ canEdit: false }))

    // Khẳng định VẮNG MẶT, không phải "bị ẩn": ẩn bằng CSS thì bật DevTools là bấm được, và tuy server
    // vẫn chặn, UI đang nói dối về thứ người dùng làm được.
    expect(nutSua()).toBeNull()
    expect(nutXoa()).toBeNull()
  })

  it("`canEdit: true` → có cả hai nút", () => {
    moManHinh(bai({ canEdit: true }))

    expect(nutSua()).toBeInTheDocument()
    expect(nutXoa()).toBeInTheDocument()
  })

  it("không đọc `author.userId` để quyết định: bài của người khác mà `canEdit: true` vẫn có nút", () => {
    // Ca nhân tạo, nhưng nó pin đúng chỗ dễ trôi: nếu ai đó thêm một phép so id ở FE, ca này đỏ. Server
    // là bên duy nhất biết `actorId`, và nó đã trả lời trong `canEdit`.
    moManHinh(
      bai({
        canEdit: true,
        author: {
          userId: "0192f3c1-8a4e-7c31-9f2a-000000000099",
          displayName: "Người khác",
          avatarUrl: null,
        },
      })
    )

    expect(nutSua()).toBeInTheDocument()
  })
})

describe("PostItem — sửa bài", () => {
  it("nút Lưu TẮT khi chưa đổi gì, bật ngay khi đổi", async () => {
    const user = userEvent.setup()
    moManHinh(bai({ canEdit: true, body: "Chiều nay ở phố cổ." }))

    await user.click(nutSua()!)
    // `PATCH {}` là 400 "Không có gì để sửa." — một lỗi người dùng không hiểu, vì họ có bấm gì đâu.
    expect(nutLuu()).toBeDisabled()

    await user.type(screen.getByLabelText("Nội dung"), " Mưa.")
    expect(nutLuu()).toBeEnabled()
  })

  it("gõ rồi xóa về đúng giá trị cũ thì Lưu TẮT lại — so với giá trị BAN ĐẦU", async () => {
    const user = userEvent.setup()
    moManHinh(bai({ canEdit: true, body: "Chào" }))

    await user.click(nutSua()!)
    const o = screen.getByLabelText("Nội dung")
    await user.type(o, "!")
    expect(nutLuu()).toBeEnabled()

    await user.clear(o)
    await user.type(o, "Chào")
    expect(nutLuu()).toBeDisabled()
  })

  it("submit form khi chưa đổi gì: KHÔNG gửi request — chốt ở onSubmit, không ở nút", async () => {
    // Nút Lưu `disabled` đã chặn cú bấm, nên test qua nút KHÔNG chứng minh được chốt trong `onSubmit`.
    // Chốt đó vẫn phải có: `PATCH {}` là 400 "Không có gì để sửa.", và một form submit được bằng phím
    // hay bằng script thì `disabled` trên một nút không phải hàng rào.
    const seen: string[] = []
    server.events.on("request:start", ({ request }) => {
      seen.push(request.method)
    })
    const user = userEvent.setup()
    moManHinh(bai({ canEdit: true, body: "Chào" }))

    await user.click(nutSua()!)
    const form = screen.getByTestId("post-edit-form").querySelector("form")!
    fireEvent.submit(form)

    await waitFor(() =>
      expect(screen.getByTestId("post-edit-form")).toBeInTheDocument()
    )
    expect(seen).toEqual([])
  })

  it("chỉ đổi `body` → PATCH gửi ĐÚNG `body`, không kèm `privacy`", async () => {
    const bodies: unknown[] = []
    server.use(
      http.patch(POST, async ({ request }) => {
        bodies.push(await request.json())
        return HttpResponse.json(
          bai({ body: "Chào mới", editedAt: "2026-09-21T03:00:00Z" })
        )
      })
    )
    const user = userEvent.setup()
    moManHinh(bai({ canEdit: true, body: "Chào", privacy: "public" }))

    await user.click(nutSua()!)
    const o = screen.getByLabelText("Nội dung")
    await user.clear(o)
    await user.type(o, "Chào mới")
    await user.click(nutLuu())

    await waitFor(() => expect(bodies).toHaveLength(1))
    // Gửi cả hai trường không sai hợp đồng, nhưng `edited_at` bị đóng dấu cho một thay đổi không tồn tại.
    expect(bodies[0]).toEqual({ body: "Chào mới" })
  })

  it("chỉ đổi `privacy` → PATCH gửi ĐÚNG `privacy`, không kèm `body`", async () => {
    const bodies: unknown[] = []
    server.use(
      http.patch(POST, async ({ request }) => {
        bodies.push(await request.json())
        return HttpResponse.json(bai({ privacy: "private" }))
      })
    )
    const user = userEvent.setup()
    moManHinh(bai({ canEdit: true, body: "Chào", privacy: "public" }))

    await user.click(nutSua()!)
    await user.click(screen.getByRole("radio", { name: /Chỉ mình tôi/ }))
    await user.click(nutLuu())

    await waitFor(() => expect(bodies).toHaveLength(1))
    expect(bodies[0]).toEqual({ privacy: "private" })
  })

  it("xóa hết chữ gửi `\"\"`, KHÔNG gửi `null` — `null` nghĩa là 'không gửi'", async () => {
    const bodies: unknown[] = []
    server.use(
      http.patch(POST, async ({ request }) => {
        bodies.push(await request.json())
        return HttpResponse.json(bai({ body: null }))
      })
    )
    const user = userEvent.setup()
    moManHinh(
      bai({
        canEdit: true,
        body: "Chào",
        media: [anh("https://r2.example.test/a.jpg")],
      })
    )

    await user.click(nutSua()!)
    await user.clear(screen.getByLabelText("Nội dung"))
    await user.click(nutLuu())

    await waitFor(() => expect(bodies).toHaveLength(1))
    // `System.Text.Json` không phân biệt vắng mặt với `null` cho `string?`: gửi `null` là "giữ nguyên",
    // và người dùng vừa xóa chữ sẽ thấy chữ cũ quay lại.
    expect(bodies[0]).toEqual({ body: "" })
  })

  it("KHÔNG bao giờ gửi `mediaKeys` — trường đó không có trong UpdatePostRequest", async () => {
    const bodies: Record<string, unknown>[] = []
    server.use(
      http.patch(POST, async ({ request }) => {
        bodies.push((await request.json()) as Record<string, unknown>)
        return HttpResponse.json(bai())
      })
    )
    const user = userEvent.setup()
    moManHinh(
      bai({
        canEdit: true,
        body: "Chào",
        media: [anh("https://r2.example.test/a.jpg")],
      })
    )

    await user.click(nutSua()!)
    // Màn sửa cũng KHÔNG có nút thêm/bớt ảnh: một nút luôn báo lỗi thì thà đừng có (Mục 7.3).
    expect(screen.queryByRole("button", { name: /ảnh/i })).toBeNull()

    await user.type(screen.getByLabelText("Nội dung"), "!")
    await user.click(nutLuu())

    await waitFor(() => expect(bodies).toHaveLength(1))
    // Gửi `mediaKeys` vào là field lạ → 400 (`UnmappedMemberHandling.Disallow`).
    expect(bodies[0]).not.toHaveProperty("mediaKeys")
  })

  it("200 → thoát chế độ sửa và trả bài MỚI ra ngoài, có `editedAt`", async () => {
    const daSua = bai({
      canEdit: true,
      body: "Đã sửa",
      editedAt: "2026-09-21T03:00:00Z",
    })
    server.use(http.patch(POST, () => HttpResponse.json(daSua)))
    const user = userEvent.setup()
    const onChanged = moManHinh(bai({ canEdit: true, body: "Chào" }))

    await user.click(nutSua()!)
    await user.type(screen.getByLabelText("Nội dung"), "!")
    await user.click(nutLuu())

    await waitFor(() => expect(onChanged).toHaveBeenCalledWith(daSua))
    // 200 trả nguyên `PostResponse` nên KHÔNG phải gọi thêm `GET` để có nhãn "đã chỉnh sửa".
    expect(screen.queryByTestId("post-edit-form")).toBeNull()
  })

  it("400 của server hiện theo key `body`, kể cả khi client đã cho qua", async () => {
    server.use(
      http.patch(POST, () =>
        problem(400, "Dữ liệu không hợp lệ", {
          body: ["Bài đăng phải có nội dung hoặc ít nhất một ảnh."],
        })
      )
    )
    const user = userEvent.setup()
    moManHinh(bai({ canEdit: true, body: "Chào", media: [] }))

    await user.click(nutSua()!)
    await user.clear(screen.getByLabelText("Nội dung"))
    await user.click(nutLuu())

    // BR-01 mệnh đề "rỗng cả chữ lẫn ảnh" cần `media_count` THẬT của bài — chỉ server biết, FE không đoán.
    await waitFor(() =>
      expect(
        screen.getByText("Bài đăng phải có nội dung hoặc ít nhất một ảnh.")
      ).toBeInTheDocument()
    )
    expect(screen.getByTestId("post-edit-form")).toBeInTheDocument()
  })

  it("403 khi sửa → MỘT câu không tiết lộ, vẫn ở lại form", async () => {
    server.use(http.patch(POST, () => problem(403, "Bị từ chối")))
    const user = userEvent.setup()
    moManHinh(bai({ canEdit: true, body: "Chào" }))

    await user.click(nutSua()!)
    await user.type(screen.getByLabelText("Nội dung"), "!")
    await user.click(nutLuu())

    await waitFor(() =>
      expect(
        screen.getByText(
          "Không tìm thấy bài viết, hoặc bạn không có quyền với bài này."
        )
      ).toBeInTheDocument()
    )
  })

  it("bấm Hủy: không gọi API, quay lại card", async () => {
    const seen: string[] = []
    server.events.on("request:start", ({ request }) => {
      seen.push(request.method)
    })
    const user = userEvent.setup()
    moManHinh(bai({ canEdit: true, body: "Chào" }))

    await user.click(nutSua()!)
    await user.type(screen.getByLabelText("Nội dung"), "!")
    await user.click(screen.getByRole("button", { name: "Hủy" }))

    expect(screen.queryByTestId("post-edit-form")).toBeNull()
    expect(screen.getByTestId("post-card")).toBeInTheDocument()
    expect(seen).toEqual([])
  })
})

describe("PostItem — xóa bài", () => {
  it("bấm Xóa mở hộp thoại xác nhận; chưa xác nhận thì KHÔNG gọi API", async () => {
    const seen: string[] = []
    server.events.on("request:start", ({ request }) => {
      seen.push(request.method)
    })
    const user = userEvent.setup()
    moManHinh(bai({ canEdit: true }))

    await user.click(nutXoa()!)

    // `AlertDialog` của kit, KHÔNG `window.confirm`.
    await waitFor(() =>
      expect(screen.getByRole("alertdialog")).toBeInTheDocument()
    )
    expect(seen).toEqual([])

    await user.click(
      within(screen.getByRole("alertdialog")).getByRole("button", {
        name: "Hủy",
      })
    )
    await waitFor(() => expect(screen.queryByRole("alertdialog")).toBeNull())
    expect(seen).toEqual([])
  })

  it("xác nhận → DELETE, 204 thì báo ra ngoài bằng `null`", async () => {
    const seen: string[] = []
    server.use(
      http.delete(POST, () => {
        seen.push("DELETE")
        return new HttpResponse(null, { status: 204 })
      })
    )
    const user = userEvent.setup()
    const onChanged = moManHinh(bai({ canEdit: true }))

    await user.click(nutXoa()!)
    await user.click(screen.getByRole("button", { name: "Xóa bài" }))

    // `null` = đã xóa: danh sách gỡ bài đi, trang chi tiết rời khỏi URL đã chết (Mục 7.3).
    await waitFor(() => expect(onChanged).toHaveBeenCalledWith(null))
    expect(seen).toEqual(["DELETE"])
  })

  it("403 khi xóa: KHÔNG gỡ bài — server vừa nói nó không xóa gì cả", async () => {
    server.use(http.delete(POST, () => problem(403, "Bị từ chối")))
    const user = userEvent.setup()
    const onChanged = moManHinh(bai({ canEdit: true }))

    await user.click(nutXoa()!)
    await user.click(screen.getByRole("button", { name: "Xóa bài" }))

    await waitFor(() => expect(screen.getByRole("alert")).toBeInTheDocument())
    // Gỡ card đi sau một 403 là nói dối theo chiều ngược lại: bài vẫn còn trên server.
    expect(onChanged).not.toHaveBeenCalled()
  })

  it("403 khi xóa → MỘT câu không tiết lộ, bài vẫn còn trên màn", async () => {
    server.use(http.delete(POST, () => problem(403, "Bị từ chối")))
    const user = userEvent.setup()
    const onChanged = moManHinh(bai({ canEdit: true }))

    await user.click(nutXoa()!)
    await user.click(screen.getByRole("button", { name: "Xóa bài" }))

    await waitFor(() =>
      expect(
        screen.getByText(
          "Không tìm thấy bài viết, hoặc bạn không có quyền với bài này."
        )
      ).toBeInTheDocument()
    )
    // Không gỡ bài: server vừa nói nó không xóa gì cả.
    expect(onChanged).not.toHaveBeenCalled()
    expect(screen.getByTestId("post-card")).toBeInTheDocument()
  })

  it("403 KHÔNG nói 'bài đã bị xóa' — câu đó xác nhận bài có tồn tại", async () => {
    server.use(http.delete(POST, () => problem(403, "Bị từ chối")))
    const user = userEvent.setup()
    moManHinh(bai({ canEdit: true }))

    await user.click(nutXoa()!)
    await user.click(screen.getByRole("button", { name: "Xóa bài" }))

    await waitFor(() => expect(screen.getByRole("alert")).toBeInTheDocument())
    expect(screen.getByRole("alert").textContent).not.toMatch(/đã bị xóa/i)
  })
})
