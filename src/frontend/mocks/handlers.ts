import { http, HttpResponse } from "msw"

import { BFF_ROUTES, type BffSessionState } from "@/lib/api/bff-contract"
import { BFF_URL } from "@/lib/api/config"
import type * as T from "@/lib/api/types"

import {
  me,
  post,
  postIdKhongDocDuoc,
  postIdKhongSuaDuoc,
  postPage,
  problem,
  profile,
  r2Host,
  registerResponse,
  uploadTicket,
  userId,
  userIdChuaCoHoSo,
  validationProblem,
  verifyEmailResponse,
} from "./fixtures"
import { fakeSession } from "./session"
import { upstreamHandlers } from "./upstream"

// Mock cho test component (Vitest, jsdom): bề mặt BFF mà TRÌNH DUYỆT nhìn thấy (Đ-E14) — /bff/auth/*, /bff/api/*.
// BFF ở server có API giả riêng (upstream.ts) cho test của lib/bff.

const url = (path: string) => `${BFF_URL}${path}`

// Lỗi của server luôn là application/problem+json — mock phải giống, nếu không `toApiError`
// (kiểm content-type) sẽ đi nhánh "không đọc được body" và test nhánh lỗi thành vô nghĩa.
const PROBLEM_HEADERS = { "Content-Type": "application/problem+json" }

const problemResponse = (status: number, title: string, detail?: string) =>
  HttpResponse.json(problem(status, title, detail), {
    status,
    headers: PROBLEM_HEADERS,
  })

const validationResponse = (errors: Record<string, string[]>) =>
  HttpResponse.json(validationProblem(errors), {
    status: 400,
    headers: PROBLEM_HEADERS,
  })

const EXPIRED_TOKEN = "a".repeat(64)
const BAD_TOKEN = "b".repeat(64)

/** Kịch bản chọn bằng DỮ LIỆU NHẬP, không bằng cờ ẩn — test đọc là thấy nhánh nào đang chạy. */
const SCENARIO_EMAILS = {
  daTonTai: "trung@example.com",
  loi400: "loi400@example.com",
  quaNhanh: "quanhanh@example.com",
  loi500: "loi500@example.com",
  saiMatKhau: "sai@example.com",
  chuaXacMinh: "chuaxacminh@example.com",
  biKhoa: "bikhoa@example.com",
} as const

/** Hai kịch bản áp cho MỌI endpoint nhận email. */
function chungChoMoiAuth(email: string) {
  if (email === SCENARIO_EMAILS.quaNhanh) {
    return problemResponse(
      429,
      "Quá nhiều yêu cầu",
      "Bạn đã gửi quá nhiều yêu cầu. Vui lòng thử lại sau ít phút."
    )
  }
  if (email === SCENARIO_EMAILS.loi500) {
    // 500 KHÔNG bao giờ có `detail` — GlobalExceptionHandler giấu chi tiết nội bộ.
    return problemResponse(500, "Đã xảy ra lỗi không mong muốn")
  }
  return null
}

export const handlers = [
  http.post(url(BFF_ROUTES.register), async ({ request }) => {
    const body = (await request.json()) as T.RegisterRequest
    const chung = chungChoMoiAuth(body.email)
    if (chung) return chung

    if (body.email === SCENARIO_EMAILS.loi400) {
      return validationResponse({
        email: ["Email không đúng định dạng."],
        password: ["Mật khẩu phải có ít nhất 8 ký tự."],
      })
    }
    if (body.email === SCENARIO_EMAILS.daTonTai) {
      return problemResponse(
        409,
        "Xung đột dữ liệu",
        "Email này đã được đăng ký."
      )
    }

    return HttpResponse.json(
      { ...registerResponse, email: body.email } satisfies T.RegisterResponse,
      { status: 201 }
    )
  }),

  http.post(url(BFF_ROUTES.verifyEmail), async ({ request }) => {
    const body = (await request.json()) as T.VerifyEmailRequest

    if (body.token === EXPIRED_TOKEN) {
      return problemResponse(
        410,
        "Liên kết không còn hiệu lực",
        "Liên kết xác minh đã hết hạn hoặc đã được sử dụng."
      )
    }
    if (body.token === BAD_TOKEN) {
      // Câu thật của VerifyEmailRequestValidator / IdentityErrors.VerifyTokenInvalid.
      return validationResponse({ token: ["Liên kết xác minh không hợp lệ."] })
    }

    return HttpResponse.json(verifyEmailResponse, { status: 200 })
  }),

  http.post(url(BFF_ROUTES.login), async ({ request }) => {
    const body = (await request.json()) as T.LoginRequest
    const chung = chungChoMoiAuth(body.email)
    if (chung) return chung

    if (body.email === SCENARIO_EMAILS.loi400) {
      return validationResponse({
        email: ["Email không đúng định dạng."],
        password: ["Mật khẩu là bắt buộc."],
      })
    }
    if (body.email === SCENARIO_EMAILS.saiMatKhau) {
      // AC-02: email không tồn tại và sai mật khẩu dùng CÙNG một thông điệp.
      return problemResponse(
        401,
        "Xác thực thất bại",
        "Email hoặc mật khẩu không đúng."
      )
    }
    if (body.email === SCENARIO_EMAILS.chuaXacMinh) {
      return problemResponse(
        403,
        "Bị từ chối",
        "Tài khoản chưa xác minh email. Vui lòng kiểm tra hộp thư."
      )
    }
    if (body.email === SCENARIO_EMAILS.biKhoa) {
      return problemResponse(
        423,
        "Tài khoản tạm khóa",
        "Tài khoản tạm khóa do đăng nhập sai nhiều lần. Vui lòng thử lại sau 15 phút."
      )
    }

    // BFF thật: 204, không body, cookie `__Host-sid` (HttpOnly — test không đọc được, đúng như trình duyệt).
    fakeSession.start()
    return new HttpResponse(null, { status: 204 })
  }),

  http.post(url(BFF_ROUTES.logout), () => {
    fakeSession.clear()
    return new HttpResponse(null, { status: 204 })
  }),

  http.get(url(BFF_ROUTES.session), () =>
    HttpResponse.json({
      authenticated: fakeSession.current() !== null,
    } satisfies BffSessionState)
  ),

  http.get(url(`${BFF_ROUTES.api}/me`), () => {
    if (fakeSession.current() === null) {
      return problemResponse(
        401,
        "Chưa xác thực",
        "Phiên đăng nhập không còn hiệu lực."
      )
    }
    return HttpResponse.json(me)
  }),

  // --- GĐ2 (E1): mười endpoint của Profile + Content, tất cả qua proxy chung `/bff/api/*`.
  // Không route BFF riêng nào — mock giả đúng bề mặt trình duyệt thấy (Mục 1.2 luật 2).

  http.get(url(`${BFF_ROUTES.api}/users/:userId/profile`), ({ params }) => {
    if (params.userId === userIdChuaCoHoSo) {
      return problemResponse(
        404,
        "Không tìm thấy tài nguyên",
        "Người dùng này chưa có hồ sơ."
      )
    }
    return HttpResponse.json(profile)
  }),

  http.put(url(`${BFF_ROUTES.api}/users/me/profile`), async ({ request }) => {
    const body = (await request.json()) as T.UpsertProfileRequest
    if (body.displayName.trim().length < 2) {
      // Câu thật của UpsertProfileRequestValidator (D2).
      return validationResponse({
        displayName: ["Tên hiển thị phải có từ 2 đến 50 ký tự."],
      })
    }
    return HttpResponse.json({
      ...profile,
      displayName: body.displayName,
      bio: body.bio ?? null,
    } satisfies T.ProfileResponse)
  }),

  http.put(url(`${BFF_ROUTES.api}/users/me/avatar`), async ({ request }) => {
    const body = (await request.json()) as T.SetAvatarRequest
    // Đ-2.7: key phải nằm dưới tiền tố của CHÍNH người gọi — của người khác là 403.
    if (!body.mediaKey.startsWith(`avatars/${userId}/`)) {
      return problemResponse(
        403,
        "Bị từ chối",
        "Bạn không có quyền thực hiện thao tác này."
      )
    }
    return HttpResponse.json({
      ...profile,
      // Presigned GET 15 phút (Đ-2.9) — URL mới mỗi lần đặt avatar, không phải `avatar_key`.
      avatarUrl: `${r2Host}/socialmedia-dev/avatars/moi.jpg?X-Amz-Signature=gia`,
    } satisfies T.ProfileResponse)
  }),

  http.delete(
    url(`${BFF_ROUTES.api}/users/me/avatar`),
    () => new HttpResponse(null, { status: 204 })
  ),

  http.post(url(`${BFF_ROUTES.api}/media/uploads`), async ({ request }) => {
    const body = (await request.json()) as T.CreateUploadsRequest
    if (body.files.length === 0 || body.files.length > 10) {
      return validationResponse({
        files: ["Mỗi bài tối đa 10 ảnh."],
      })
    }
    // Tiền tố theo `purpose` (Đ-2.7): `avatars/` cho avatar, `posts/` cho bài. Không phải trang trí —
    // `PUT /users/me/avatar` giả ở trên từ chối 403 mọi key không nằm dưới `avatars/{userId}/`, đúng như
    // server, nên mock nào trả sai tiền tố sẽ làm E3 hỏng ở bước 3 chứ không phải bước 1.
    const prefix = body.purpose === "avatar" ? "avatars" : "posts"
    // Một ticket cho MỖI file, CÙNG THỨ TỰ với `files` gửi lên — E4 ghép ticket với file theo index.
    return HttpResponse.json(
      body.files.map((f, i) => ({
        ...uploadTicket,
        mediaKey: `${prefix}/${userId}/anh-${i}.jpg`,
        uploadUrl: `${r2Host}/socialmedia-dev/${prefix}/anh-${i}.jpg?X-Amz-Signature=gia`,
        requiredHeaders: {
          "Content-Type": f.contentType,
          "Content-Length": String(f.sizeBytes),
        },
      })) satisfies T.UploadTicket[],
      { status: 201 }
    )
  }),

  /**
   * Bước 2 của luồng ba bước: `PUT` THẲNG lên R2, ngoài origin của app (Đ-2.5). Mặc định 200 như R2 thật.
   * Nhánh hỏng dựng bằng `server.use(...)` trong từng test — hai nhánh khác hẳn nhau: `HttpResponse.error()`
   * (mạng/CORS/CSP, ca ISS-02) và một status ≠ 2xx (R2 từ chối chữ ký).
   */
  http.put(`${r2Host}/*`, () => new HttpResponse(null, { status: 200 })),

  http.post(url(`${BFF_ROUTES.api}/posts`), async ({ request }) => {
    const body = (await request.json()) as T.CreatePostRequest
    // BR-01: body rỗng thì phải có ít nhất một ảnh.
    if (!body.body?.trim() && (body.mediaKeys ?? []).length === 0) {
      return validationResponse({
        body: ["Bài đăng phải có nội dung hoặc ít nhất một ảnh."],
      })
    }
    return HttpResponse.json(
      {
        ...post,
        body: body.body ?? null,
        privacy: body.privacy,
      } satisfies T.PostResponse,
      { status: 201 }
    )
  }),

  http.get(url(`${BFF_ROUTES.api}/posts/:postId`), ({ params }) => {
    if (params.postId === postIdKhongDocDuoc) {
      return problemResponse(
        404,
        "Không tìm thấy tài nguyên",
        "Không tìm thấy bài viết."
      )
    }
    return HttpResponse.json(post)
  }),

  http.patch(
    url(`${BFF_ROUTES.api}/posts/:postId`),
    async ({ params, request }) => {
      if (params.postId === postIdKhongSuaDuoc) {
        return problemResponse(
          403,
          "Bị từ chối",
          "Bạn không có quyền thực hiện thao tác này."
        )
      }
      const body = (await request.json()) as T.UpdatePostRequest
      if (Object.keys(body).length === 0) {
        return validationResponse({ body: ["Không có gì để sửa."] })
      }
      return HttpResponse.json({
        ...post,
        ...body,
        editedAt: "2026-09-20T03:00:00Z",
      } satisfies T.PostResponse)
    }
  ),

  http.delete(url(`${BFF_ROUTES.api}/posts/:postId`), ({ params }) => {
    if (params.postId === postIdKhongSuaDuoc) {
      return problemResponse(
        403,
        "Bị từ chối",
        "Bạn không có quyền thực hiện thao tác này."
      )
    }
    return new HttpResponse(null, { status: 204 })
  }),

  http.get(url(`${BFF_ROUTES.api}/users/:userId/posts`), () =>
    HttpResponse.json(postPage)
  ),

  ...upstreamHandlers,
]
