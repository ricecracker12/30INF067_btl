import { render, screen, waitFor, within } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { http, HttpResponse } from "msw"
import { beforeEach, describe, expect, it } from "vitest"

import { BFF_URL } from "@/lib/api/config"
import { MEDIA_SCENARIO, mediaKeyDaDung, r2Host } from "@/mocks/fixtures"
import { server } from "@/mocks/node"
import { fakeSession } from "@/mocks/session"

import { PostComposer } from "./post-composer"

// Bốn ca của E4 cộng hai bất biến mà chỉ composer mới chứng minh được: MỘT lời gọi presign cho cả lô
// (Đ-2.15), và ba giá trị gửi ở `POST /posts` đúng bằng thứ đã khai lúc presign (Đ-2.8 lớp 2).
//
// Ca "hủy một ảnh đang lên" KHÔNG có ở đây: XHR giả của msw không cài `abort()`, nên test đó xanh giả
// (cạm bẫy đã ghi ở "Thực tế thi công" của E3). Nhánh hủy nghiệm thu trên trình duyệt thật.

const UPLOADS = `${BFF_URL}/api/media/uploads`
const POSTS = `${BFF_URL}/api/posts`

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

function anh(ten: string, type = "image/jpeg", bytes = 2048) {
  return new File(["x".repeat(bytes)], ten, { type })
}

/** Đếm mọi request rời trình duyệt — ca "client chặn" phải để con số này ở 0. */
function recordRequests() {
  const seen: string[] = []
  server.events.on("request:start", ({ request }) => {
    seen.push(`${request.method} ${new URL(request.url).pathname}`)
  })
  return seen
}

/**
 * `applyAccept: false` — mặc định `userEvent.upload` lọc file theo `accept` và ca "chọn ảnh GIF" sẽ xanh
 * giả vì file bị bỏ trước khi `change` bắn. Đời thật kéo-thả và hộp thoại macOS cũng bỏ qua `accept`.
 */
function nguoiDung() {
  return userEvent.setup({ applyAccept: false })
}

const oNoiDung = () => screen.getByLabelText("Nội dung")
const oAnh = () => screen.getByLabelText(/^Ảnh \(tối đa/)
const nutDang = () => screen.getByRole("button", { name: "Đăng bài" })
const dongAnh = () => screen.getAllByTestId("upload-row")

beforeEach(() => {
  fakeSession.start()
})

describe("PostComposer — BR-01 phía client", () => {
  it("bài rỗng cả chữ lẫn ảnh: KHÔNG gọi API nào, lỗi hiện dưới ô nội dung", async () => {
    const seen = recordRequests()
    const user = nguoiDung()
    render(<PostComposer />)

    await user.click(screen.getByRole("radio", { name: /Công khai/ }))
    await user.click(nutDang())

    expect(
      screen.getByText("Bài đăng phải có nội dung hoặc ít nhất một ảnh.")
    ).toBeInTheDocument()
    // Cả `POST /posts` lẫn `POST /media/uploads` — bài chắc chắn hỏng thì không tốn hạn mức nào.
    expect(seen).toEqual([])
  })

  it("chưa chọn mức riêng tư: chặn ở client, đúng câu của server", async () => {
    const seen = recordRequests()
    const user = nguoiDung()
    render(<PostComposer />)

    await user.type(oNoiDung(), "Chiều nay ở phố cổ.")
    await user.click(nutDang())

    expect(screen.getByText("Mức riêng tư là bắt buộc.")).toBeInTheDocument()
    expect(seen).toEqual([])
  })

  it("11 ảnh: báo DƯỚI Ô ẢNH, không thêm ảnh nào, không gọi presign", async () => {
    const seen = recordRequests()
    const user = nguoiDung()
    render(<PostComposer />)

    await user.upload(
      oAnh(),
      Array.from({ length: 11 }, (_, i) => anh(`anh-${i}.jpg`))
    )

    expect(
      screen.getByText("Một bài chỉ được đính kèm tối đa 10 ảnh.")
    ).toBeInTheDocument()
    expect(screen.queryAllByTestId("upload-row")).toHaveLength(0)
    expect(seen).toEqual([])
  })

  it("ảnh sai loại bị bỏ, ảnh hợp lệ trong cùng lượt chọn vẫn được thêm", async () => {
    const user = nguoiDung()
    render(<PostComposer />)

    await user.upload(oAnh(), [
      anh("tot.jpg"),
      anh("xau.gif", "image/gif"),
      anh("tot-2.png", "image/png"),
    ])

    expect(
      screen.getByText(
        "Chỉ nhận ảnh JPEG, PNG hoặc WebP, tối đa 10 MB mỗi ảnh."
      )
    ).toBeInTheDocument()
    await waitFor(() => expect(dongAnh()).toHaveLength(2))
    // Chọn 10 ảnh mà một cái là GIF thì bắt chọn lại cả 10 là phạt người dùng vì một lần bấm nhầm.
    expect(screen.getByText("tot.jpg")).toBeInTheDocument()
    expect(screen.getByText("tot-2.png")).toBeInTheDocument()
    expect(screen.queryByText("xau.gif")).not.toBeInTheDocument()
  })
})

describe("PostComposer — hàng đợi ảnh", () => {
  it("MỘT lời gọi presign cho cả lô ba ảnh (Đ-2.15), purpose=post", async () => {
    const bodies: unknown[] = []
    server.use(
      http.post(UPLOADS, async ({ request }) => {
        const body = (await request.json()) as {
          files: { contentType: string; sizeBytes: number }[]
        }
        bodies.push(body)
        return HttpResponse.json(ticketsFor(body.files), { status: 201 })
      })
    )
    const user = nguoiDung()
    render(<PostComposer />)

    await user.upload(oAnh(), [
      anh("a.jpg"),
      anh("b.png", "image/png", 4096),
      anh("c.webp", "image/webp", 8192),
    ])

    await waitFor(() =>
      expect(dongAnh().every((row) => row.dataset.status === "xong")).toBe(true)
    )
    // Gọi ba lần là ba phần trăm hạn mức 100 req/phút cho một bài.
    expect(bodies).toHaveLength(1)
    expect(bodies[0]).toEqual({
      purpose: "post",
      files: [
        { contentType: "image/jpeg", sizeBytes: 2048 },
        { contentType: "image/png", sizeBytes: 4096 },
        { contentType: "image/webp", sizeBytes: 8192 },
      ],
    })
  })

  it("MỘT ảnh lỗi KHÔNG hủy cả lô; thử lại xong thì nút Đăng bật", async () => {
    // Ảnh thứ hai bị R2 từ chối ĐÚNG MỘT LẦN — lượt thử lại rơi vào handler mặc định (200).
    server.use(
      http.put(
        `${r2Host}/socialmedia-dev/posts/anh-1.jpg`,
        () => new HttpResponse(null, { status: 403 }),
        { once: true }
      )
    )
    const user = nguoiDung()
    render(<PostComposer />)

    await user.type(oNoiDung(), "Ba ảnh.")
    await user.click(screen.getByRole("radio", { name: /Công khai/ }))
    await user.upload(oAnh(), [anh("a.jpg"), anh("b.jpg"), anh("c.jpg")])

    await waitFor(() =>
      expect(
        dongAnh().filter((row) => row.dataset.status === "loi")
      ).toHaveLength(1)
    )

    // Ảnh 1 và 3 vẫn `xong`: một lượt `PUT` rớt không kéo chín ảnh kia theo.
    const rows = dongAnh()
    expect(rows[0]?.dataset.status).toBe("xong")
    expect(rows[1]?.dataset.status).toBe("loi")
    expect(rows[2]?.dataset.status).toBe("xong")

    // Nút Đăng TẮT khi còn ảnh dở: gửi lúc đó là 400 "Ảnh chưa được tải lên xong…".
    expect(nutDang()).toBeDisabled()

    // Nút "Thử lại" CHỈ ở dòng ảnh hỏng — không có nút nào bắt làm lại cả lô.
    const thuLai = screen.getAllByRole("button", { name: "Thử lại" })
    expect(thuLai).toHaveLength(1)
    expect(within(rows[1]!).getByRole("button", { name: "Thử lại" })).toBe(
      thuLai[0]
    )

    await user.click(thuLai[0]!)

    await waitFor(() =>
      expect(dongAnh().every((row) => row.dataset.status === "xong")).toBe(true)
    )
    expect(nutDang()).toBeEnabled()
    expect(screen.queryByRole("button", { name: "Thử lại" })).toBeNull()
  })

  it("bỏ một ảnh đã tải xong: KHÔNG gọi gì tới R2, chỉ rời khỏi danh sách", async () => {
    const user = nguoiDung()
    render(<PostComposer />)

    await user.upload(oAnh(), [anh("a.jpg"), anh("b.jpg")])
    await waitFor(() => expect(dongAnh()).toHaveLength(2))
    await waitFor(() =>
      expect(dongAnh().every((row) => row.dataset.status === "xong")).toBe(true)
    )

    const seen = recordRequests()
    await user.click(
      within(dongAnh()[0]!).getByRole("button", { name: "Bỏ ảnh" })
    )

    await waitFor(() => expect(dongAnh()).toHaveLength(1))
    // Object thành mồ côi; worker dọn sau 24 giờ (Đ-2.13). FE không có endpoint xóa và không được gọi R2.
    expect(seen).toEqual([])
  })

  it("lỗi presign cả lô: mọi ảnh thành lỗi với ĐÚNG CÂU SERVER dưới key `files`", async () => {
    server.use(
      http.post(UPLOADS, () =>
        problem(400, "Dữ liệu không hợp lệ", {
          files: ["Chỉ nhận ảnh JPEG, PNG hoặc WebP, tối đa 10 MB mỗi ảnh."],
        })
      )
    )
    const user = nguoiDung()
    render(<PostComposer />)

    await user.upload(oAnh(), [anh("a.jpg"), anh("b.jpg")])

    await waitFor(() =>
      expect(dongAnh().every((row) => row.dataset.status === "loi")).toBe(true)
    )
    // Câu server nói được thứ bảng `errorMessage` không nói được; bảng chung chỉ có "Dữ liệu không hợp lệ."
    expect(
      screen.getAllByText(
        "Chỉ nhận ảnh JPEG, PNG hoặc WebP, tối đa 10 MB mỗi ảnh."
      )
    ).toHaveLength(2)
  })
})

describe("PostComposer — POST /posts", () => {
  it("gửi ba giá trị mỗi ảnh ĐÚNG BẰNG thứ đã khai lúc presign, theo thứ tự hiển thị", async () => {
    const bodies: unknown[] = []
    server.use(
      http.post(POSTS, async ({ request }) => {
        bodies.push(await request.json())
        return problem(500, "Dừng ở đây")
      })
    )
    const user = nguoiDung()
    render(<PostComposer />)

    await user.type(oNoiDung(), "Hai ảnh.")
    await user.click(screen.getByRole("radio", { name: /Bạn bè/ }))
    await user.upload(oAnh(), [anh("a.jpg"), anh("b.png", "image/png", 4096)])
    await waitFor(() =>
      expect(dongAnh().every((row) => row.dataset.status === "xong")).toBe(true)
    )

    await user.click(nutDang())

    await waitFor(() => expect(bodies).toHaveLength(1))
    expect(bodies[0]).toEqual({
      body: "Hai ảnh.",
      privacy: "friends",
      // Server `HEAD` lên R2 rồi đối chiếu cả ba giá trị (Đ-2.8 lớp 2) — lệch một con số là 400.
      mediaKeys: [
        {
          mediaKey: expect.stringContaining("posts/"),
          contentType: "image/jpeg",
          sizeBytes: 2048,
        },
        {
          mediaKey: expect.stringContaining("posts/"),
          contentType: "image/png",
          sizeBytes: 4096,
        },
      ],
    })
  })

  it("bài chỉ có ảnh: `body` gửi `null`, không gửi chuỗi rỗng", async () => {
    const bodies: unknown[] = []
    server.use(
      http.post(POSTS, async ({ request }) => {
        bodies.push(await request.json())
        return problem(500, "Dừng ở đây")
      })
    )
    const user = nguoiDung()
    render(<PostComposer />)

    await user.click(screen.getByRole("radio", { name: /Chỉ mình tôi/ }))
    await user.upload(oAnh(), anh("a.jpg"))
    await waitFor(() =>
      expect(dongAnh().every((row) => row.dataset.status === "xong")).toBe(true)
    )

    await user.click(nutDang())

    await waitFor(() => expect(bodies).toHaveLength(1))
    expect((bodies[0] as { body: unknown }).body).toBeNull()
  })

  it("400 của server hiện theo key: `body`, `mediaKeys` và `privacy` cùng lúc", async () => {
    server.use(
      http.post(POSTS, () =>
        problem(400, "Dữ liệu không hợp lệ", {
          body: ["Nội dung bài không được vượt quá 5000 ký tự."],
          mediaKeys: ["Một ảnh không được đính kèm hai lần."],
          privacy: ["Mức riêng tư là bắt buộc."],
        })
      )
    )
    const user = nguoiDung()
    render(<PostComposer />)

    await user.type(oNoiDung(), "Chào.")
    await user.click(screen.getByRole("radio", { name: /Công khai/ }))
    await user.click(nutDang())

    // Mọi 400 hiển thị theo key của `errors`, KỂ CẢ khi client đã kiểm qua (Đ-E5): server là bên quyết định,
    // và ba câu này client không có cách nào tự biết.
    await waitFor(() =>
      expect(
        screen.getByText("Nội dung bài không được vượt quá 5000 ký tự.")
      ).toBeInTheDocument()
    )
    expect(
      screen.getByText("Một ảnh không được đính kèm hai lần.")
    ).toBeInTheDocument()
    expect(screen.getByText("Mức riêng tư là bắt buộc.")).toBeInTheDocument()
  })

  it("409 dựng từ CHUỖI THẬT: ảnh lên xong, key đã dùng ở bài khác (BR-03)", async () => {
    // Khác ca 409 bên dưới: ở đây mock KHÔNG trả 409 cho mọi request, nó chỉ trả 409 khi `mediaKeys` thật
    // sự mang một key đã gắn vào bài khác (kịch bản chọn bằng dữ liệu nhập, E8 bước 2). Nhờ vậy ca này
    // chứng minh thêm một điều mà một `server.use` trả thẳng 409 không chứng minh được: client gửi lại
    // ĐÚNG `mediaKey` nó vừa nhận lúc presign.
    // Ghi thân request qua SỰ KIỆN, không qua `server.use`: override handler ở đây là thay luôn nhánh
    // 409 đang cần kiểm. (Đừng thử "chuyển tiếp" bằng `fetch` cùng URL — chính msw bắt lại và thành đệ
    // quy vô hạn, worker Vitest chết với exit 134.)
    const bodies: { mediaKeys?: { mediaKey: string }[] }[] = []
    server.events.on("request:start", ({ request }) => {
      if (request.method !== "POST" || !request.url.endsWith("/api/posts"))
        return
      void request
        .clone()
        .json()
        .then((b) => bodies.push(b as { mediaKeys?: { mediaKey: string }[] }))
    })
    const user = nguoiDung()
    render(<PostComposer />)

    await user.type(oNoiDung(), "Ảnh này đã dùng rồi.")
    await user.click(screen.getByRole("radio", { name: /Công khai/ }))
    await user.upload(
      oAnh(),
      anh("da-dung.jpg", "image/jpeg", MEDIA_SCENARIO.sizeBytesKeyDaDung)
    )
    await waitFor(() =>
      expect(dongAnh().every((row) => row.dataset.status === "xong")).toBe(true)
    )

    await user.click(nutDang())

    await waitFor(() =>
      expect(
        screen.getByText(
          "Ảnh này đã được dùng trong một bài khác. Hãy chọn lại ảnh."
        )
      ).toBeInTheDocument()
    )
    // Key gửi lên phải đúng key nhận lúc presign — không dựng lại, không sửa.
    expect(bodies[0]?.mediaKeys?.[0]?.mediaKey).toBe(mediaKeyDaDung)
  })

  it("403 và 409 ra HAI câu khác nhau — cùng ngữ cảnh `post-create`", async () => {
    const user = nguoiDung()
    const { unmount } = render(<PostComposer />)

    server.use(http.post(POSTS, () => problem(403, "Bị từ chối")))
    await user.type(oNoiDung(), "Chào.")
    await user.click(screen.getByRole("radio", { name: /Công khai/ }))
    await user.click(nutDang())
    await waitFor(() =>
      expect(
        screen.getByText("Bạn cần hoàn tất hồ sơ trước khi đăng bài.")
      ).toBeInTheDocument()
    )
    unmount()

    server.use(http.post(POSTS, () => problem(409, "Xung đột")))
    render(<PostComposer />)
    await user.type(oNoiDung(), "Chào.")
    await user.click(screen.getByRole("radio", { name: /Công khai/ }))
    await user.click(nutDang())
    // 409 = `mediaKey` đã gắn vào bài khác (BR-03); ảnh đó không dùng lại được, phải chọn lại.
    await waitFor(() =>
      expect(
        screen.getByText(
          "Ảnh này đã được dùng trong một bài khác. Hãy chọn lại ảnh."
        )
      ).toBeInTheDocument()
    )
  })

  it("đăng xong: báo đã đăng, dọn form và dọn hàng đợi ảnh", async () => {
    const user = nguoiDung()
    render(<PostComposer />)

    await user.type(oNoiDung(), "Chiều nay ở phố cổ.")
    await user.click(screen.getByRole("radio", { name: /Công khai/ }))
    await user.upload(oAnh(), anh("a.jpg"))
    await waitFor(() =>
      expect(dongAnh().every((row) => row.dataset.status === "xong")).toBe(true)
    )

    await user.click(nutDang())

    await waitFor(() =>
      expect(screen.getByTestId("post-created")).toBeInTheDocument()
    )
    expect(oNoiDung()).toHaveValue("")
    expect(screen.queryAllByTestId("upload-row")).toHaveLength(0)
    // Không giữ lại `mediaKey` của bài vừa đăng: gửi lại lần nữa là 409 (BR-03).
    expect(screen.getByRole("radio", { name: /Công khai/ })).toHaveAttribute(
      "aria-checked",
      "false"
    )
  })
})

/** Ticket giả cho một lô file: một ticket mỗi file, CÙNG THỨ TỰ — đúng giao kèo của hợp đồng. */
function ticketsFor(files: { contentType: string; sizeBytes: number }[]) {
  return files.map((f, i) => ({
    mediaKey: `posts/nguoi-dung/anh-${i}.jpg`,
    uploadUrl: `${r2Host}/socialmedia-dev/posts/anh-${i}.jpg?X-Amz-Signature=gia`,
    expiresIn: 600,
    requiredHeaders: {
      "Content-Type": f.contentType,
      "Content-Length": String(f.sizeBytes),
    },
  }))
}
