import { fireEvent, render, screen, waitFor } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { http, HttpResponse } from "msw"
import { beforeEach, describe, expect, it, vi } from "vitest"

import { BFF_URL } from "@/lib/api/config"
import { profile, r2Host, userId } from "@/mocks/fixtures"
import { server } from "@/mocks/node"
import { fakeSession } from "@/mocks/session"

import { AvatarCard } from "./avatar-card"
import { profileStore } from "./profile-store"

// E3 tồn tại TRƯỚC E4 vì đúng một lý do: ba bước hỏng ra BA CÂU KHÁC NHAU. File test này là chỗ giữ
// lời hứa đó — gộp hai câu bất kỳ là một ca dưới đây đỏ.

const UPLOADS = `${BFF_URL}/api/media/uploads`
const SET_AVATAR = `${BFF_URL}/api/users/me/avatar`
const PUT_R2 = `${r2Host}/*`

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

function anh(type = "image/jpeg", bytes = 2048) {
  return new File(["x".repeat(bytes)], "anh.jpg", { type })
}

/** Đếm mọi request rời trình duyệt — ca "client chặn" phải để con số này ở 0. */
function recordRequests() {
  const seen: string[] = []
  server.events.on("request:start", ({ request }) => {
    seen.push(`${request.method} ${new URL(request.url).pathname}`)
  })
  return seen
}

/** Ghi lại lượt `PUT` đi thẳng lên R2 — chỗ duy nhất thấy được header thật sự được gửi. */
function recordPutR2() {
  const seen: Request[] = []
  server.use(
    http.put(PUT_R2, ({ request }) => {
      seen.push(request)
      return new HttpResponse(null, { status: 200 })
    })
  )
  return seen
}

function moManHinhCoHoSo(avatarUrl: string | null = null) {
  profileStore.setReady({ ...profile, avatarUrl })
  return render(<AvatarCard />)
}

beforeEach(() => {
  profileStore.reset()
  fakeSession.start()
})

describe("AvatarCard — luồng ba bước", () => {
  it("chọn ảnh hợp lệ: presign → PUT lên R2 → gắn mediaKey, avatarUrl mới vào store", async () => {
    const seen = recordRequests()
    const user = userEvent.setup()
    moManHinhCoHoSo()

    await user.upload(screen.getByLabelText("Chọn ảnh mới"), anh())

    await waitFor(() =>
      expect(profileStore.getState().profile?.avatarUrl).toContain(
        "X-Amz-Signature"
      )
    )
    // Đúng ba lượt, đúng thứ tự — byte ảnh KHÔNG đi qua `/bff/*` (Đ-2.5).
    expect(seen).toEqual([
      "POST /bff/api/media/uploads",
      `PUT /socialmedia-dev/avatars/anh-0.jpg`,
      "PUT /bff/api/users/me/avatar",
    ])
  })

  it("presign khai ĐÚNG purpose=avatar và contentType lấy từ chính file", async () => {
    const bodies: unknown[] = []
    server.use(
      http.post(UPLOADS, async ({ request }) => {
        bodies.push(await request.json())
        return HttpResponse.json(
          [
            {
              mediaKey: `avatars/${userId}/anh-0.webp`,
              uploadUrl: `${r2Host}/bucket/a.webp?X-Amz-Signature=gia`,
              expiresIn: 600,
              requiredHeaders: {
                "Content-Type": "image/webp",
                "Content-Length": "2048",
              },
            },
          ],
          { status: 201 }
        )
      })
    )
    const putR2 = recordPutR2()
    const user = userEvent.setup()
    moManHinhCoHoSo()

    await user.upload(
      screen.getByLabelText("Chọn ảnh mới"),
      anh("image/webp", 2048)
    )

    await waitFor(() => expect(bodies).toHaveLength(1))
    // Header `Content-Type` của lượt `PUT` phải là thứ SERVER ĐÃ KÝ (`requiredHeaders`), không phải
    // hằng số đoán ở client: lệch một ký tự là R2 trả 403 SignatureDoesNotMatch, trông hệt CORS sai.
    await waitFor(() => expect(putR2).toHaveLength(1))
    expect(putR2[0].headers.get("content-type")).toBe("image/webp")
    // Lệch một ký tự giữa `contentType` khai lúc presign và `file.type` là R2 trả 403
    // SignatureDoesNotMatch — trông hệt CORS sai, mất cả buổi để phân biệt.
    expect(bodies[0]).toEqual({
      purpose: "avatar",
      files: [{ contentType: "image/webp", sizeBytes: 2048 }],
    })
  })
})

describe("AvatarCard — ba bước hỏng ra ba câu khác nhau", () => {
  it("bước 1 (presign) 403: thiếu quyền — câu của ngữ cảnh `upload`", async () => {
    server.use(http.post(UPLOADS, () => problem(403, "Bị từ chối")))
    const user = userEvent.setup()
    moManHinhCoHoSo()

    await user.upload(screen.getByLabelText("Chọn ảnh mới"), anh())

    expect(
      await screen.findByText("Tài khoản của bạn chưa được phép đăng bài.")
    ).toBeInTheDocument()
  })

  it("bước 2 (PUT lên R2) hỏng: câu RIÊNG về kết nối — đây là ca ISS-02", async () => {
    server.use(http.put(PUT_R2, () => HttpResponse.error()))
    const user = userEvent.setup()
    moManHinhCoHoSo()

    await user.upload(screen.getByLabelText("Chọn ảnh mới"), anh())

    expect(
      await screen.findByText(
        "Không tải được ảnh lên. Kiểm tra kết nối rồi thử lại."
      )
    ).toBeInTheDocument()
  })

  it("bước 3 (gắn key) 403: câu của ngữ cảnh `avatar` — KHÔNG phải câu của bước 1 hay 2", async () => {
    server.use(http.put(SET_AVATAR, () => problem(403, "Bị từ chối")))
    const user = userEvent.setup()
    moManHinhCoHoSo()

    await user.upload(screen.getByLabelText("Chọn ảnh mới"), anh())

    expect(
      await screen.findByText("Ảnh này không thuộc về bạn. Hãy chọn lại ảnh.")
    ).toBeInTheDocument()
    // Ba câu phải KHÁC nhau: gộp lại là bịt đường sửa, vì ba nguyên nhân không liên quan gì nhau.
    expect(
      screen.queryByText(
        "Không tải được ảnh lên. Kiểm tra kết nối rồi thử lại."
      )
    ).not.toBeInTheDocument()
    expect(
      screen.queryByText("Tài khoản của bạn chưa được phép đăng bài.")
    ).not.toBeInTheDocument()
  })

  it("400 của bước 3 hiện ĐÚNG CÂU SERVER theo key mediaKey (Đ-E5)", async () => {
    server.use(
      http.put(SET_AVATAR, () =>
        problem(400, "Dữ liệu không hợp lệ", {
          mediaKey: [
            "Ảnh chưa được tải lên xong. Hãy chờ tải lên hoàn tất rồi thử lại.",
          ],
        })
      )
    )
    const user = userEvent.setup()
    moManHinhCoHoSo()

    await user.upload(screen.getByLabelText("Chọn ảnh mới"), anh())

    // Câu chung "Dữ liệu không hợp lệ." ở đây là mất thông tin duy nhất người dùng dùng được.
    expect(
      await screen.findByText(
        "Ảnh chưa được tải lên xong. Hãy chờ tải lên hoàn tất rồi thử lại."
      )
    ).toBeInTheDocument()
  })

  it("400 của bước 1 hiện ĐÚNG CÂU SERVER theo key files", async () => {
    server.use(
      http.post(UPLOADS, () =>
        problem(400, "Dữ liệu không hợp lệ", {
          files: ["Chỉ nhận ảnh JPEG, PNG hoặc WebP, tối đa 10 MB mỗi ảnh."],
        })
      )
    )
    const user = userEvent.setup()
    moManHinhCoHoSo()

    await user.upload(screen.getByLabelText("Chọn ảnh mới"), anh())

    expect(
      await screen.findByText(
        "Chỉ nhận ảnh JPEG, PNG hoặc WebP, tối đa 10 MB mỗi ảnh."
      )
    ).toBeInTheDocument()
  })
})

describe("AvatarCard — client chặn trước khi gọi API", () => {
  it("sai loại: hiện đúng câu server, KHÔNG có request nào", async () => {
    const seen = recordRequests()
    // `applyAccept: false` — user-event mặc định LỌC theo `accept` và bỏ file sai loại trước khi bắn
    // `change`, tức là mô phỏng một trình duyệt lý tưởng. Đời thật không thế: kéo-thả bỏ qua `accept`,
    // và hộp thoại của macOS cho gõ thẳng tên file. Bỏ lọc ở đây mới kiểm được chỗ chặn THẬT.
    const user = userEvent.setup({ applyAccept: false })
    moManHinhCoHoSo()

    await user.upload(
      screen.getByLabelText("Chọn ảnh mới"),
      new File(["gif"], "anh.gif", { type: "image/gif" })
    )

    expect(
      await screen.findByText(
        "Chỉ nhận ảnh JPEG, PNG hoặc WebP, tối đa 10 MB mỗi ảnh."
      )
    ).toBeInTheDocument()
    expect(seen).toEqual([])
  })

  it("quá 10 MB: cùng câu đó, vẫn KHÔNG có request nào", async () => {
    const seen = recordRequests()
    const user = userEvent.setup()
    moManHinhCoHoSo()

    // `File` với nội dung 10 MB + 1 byte — dựng bằng `size` thật, không giả lập thuộc tính.
    await user.upload(
      screen.getByLabelText("Chọn ảnh mới"),
      new File(["x".repeat(10485761)], "to.jpg", { type: "image/jpeg" })
    )

    expect(
      await screen.findByText(
        "Chỉ nhận ảnh JPEG, PNG hoặc WebP, tối đa 10 MB mỗi ảnh."
      )
    ).toBeInTheDocument()
    expect(seen).toEqual([])
  })
})

describe("AvatarCard — gỡ ảnh và URL hết hạn", () => {
  it("gỡ: 204 thì avatar biến khỏi store; bấm lần hai không còn nút (idempotent ở server)", async () => {
    const user = userEvent.setup()
    moManHinhCoHoSo(`${r2Host}/bucket/cu.jpg?X-Amz-Signature=gia`)

    await user.click(screen.getByRole("button", { name: "Gỡ ảnh đại diện" }))

    await waitFor(() =>
      expect(profileStore.getState().profile?.avatarUrl).toBeNull()
    )
    expect(
      screen.queryByRole("button", { name: "Gỡ ảnh đại diện" })
    ).not.toBeInTheDocument()
  })

  it("chưa có avatar thì không có nút gỡ", () => {
    moManHinhCoHoSo(null)
    expect(
      screen.queryByRole("button", { name: "Gỡ ảnh đại diện" })
    ).not.toBeInTheDocument()
  })

  it("ảnh vỡ (presigned GET 15 phút đã hết hạn) nạp lại hồ sơ ĐÚNG MỘT LẦN", async () => {
    const seen = recordRequests()
    // Lượt nạp lại phải trả về một URL KHÁC và vẫn có avatar — nếu không thì sau lần nạp đầu thẻ `<img>`
    // biến mất và không còn gì để làm vỡ lần hai, tức là ca này xanh mà không kiểm được cái nó định kiểm.
    server.use(
      http.get(`${BFF_URL}/api/users/:userId/profile`, () =>
        HttpResponse.json({
          ...profile,
          avatarUrl: `${r2Host}/bucket/moi.jpg?X-Amz-Signature=gia`,
        })
      )
    )
    moManHinhCoHoSo(`${r2Host}/bucket/het-han.jpg?X-Amz-Signature=gia`)

    fireEvent.error(screen.getByTestId("avatar-image"))

    // `loadProfile` = `GET /api/me` + `GET /users/{id}/profile`, và nó single-flight: ba lần vỡ LIÊN TIẾP
    // bị gộp thành một dù có chốt hay không. Phải chờ lượt đầu XONG rồi mới làm vỡ lần hai — lúc đó chỉ
    // còn chốt `refreshed` giữ cho không có lượt thứ hai.
    await waitFor(() =>
      expect(profileStore.getState().profile?.avatarUrl).toContain("moi.jpg")
    )
    fireEvent.error(await screen.findByTestId("avatar-image"))
    await new Promise((r) => setTimeout(r, 50))

    expect(seen.filter((r) => r.endsWith("/profile"))).toHaveLength(1)
    expect(seen.filter((r) => r === "GET /bff/api/me")).toHaveLength(1)
  })
})

describe("AvatarCard — không rò chữ ký", () => {
  it("không log uploadUrl hay presigned GET (Mục 1.2 luật 7)", async () => {
    const log = vi.spyOn(console, "log").mockImplementation(() => {})
    const error = vi.spyOn(console, "error").mockImplementation(() => {})
    const user = userEvent.setup()
    moManHinhCoHoSo()

    await user.upload(screen.getByLabelText("Chọn ảnh mới"), anh())
    await waitFor(() =>
      expect(profileStore.getState().profile?.avatarUrl).not.toBeNull()
    )

    const printed = [...log.mock.calls, ...error.mock.calls].flat().join(" ")
    expect(printed).not.toContain("X-Amz-Signature")
    log.mockRestore()
    error.mockRestore()
  })
})
