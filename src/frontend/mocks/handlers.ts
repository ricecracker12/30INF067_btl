import { http, HttpResponse } from "msw"

import { BFF_ROUTES, type BffSessionState } from "@/lib/api/bff-contract"
import { BFF_URL } from "@/lib/api/config"
import type * as T from "@/lib/api/types"

import {
  CURSOR_QUA_TAI,
  CURSOR_SAU_TRANG_RONG,
  CURSOR_TRANG_RONG,
  MEDIA_SCENARIO,
  SOCIAL_SCENARIO,
  feedPage,
  friendCard,
  friendPage,
  me,
  mediaKeyDaDung,
  post,
  postIdKhongDocDuoc,
  postIdKhongSuaDuoc,
  postPage,
  problem,
  profile,
  r2Host,
  registerResponse,
  relationship,
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

/**
 * Câu 400 THẬT của `SocialGraphErrors` khi `userId` là chính người gọi — mỗi endpoint một câu. `errors.userId` là
 * thứ FE hiện nguyên văn (Đ-E5), nên mock chép đúng chữ server.
 */
const TU_QUAN_HE = {
  sendRequest: "Không thể gửi lời mời kết bạn cho chính mình.",
  accept: "Không thể chấp nhận lời mời kết bạn với chính mình.",
  removeRequest: "Không thể hủy lời mời kết bạn với chính mình.",
  unfriend: "Không thể hủy kết bạn với chính mình.",
  follow: "Không thể theo dõi chính mình.",
  relationship: "Không thể xem quan hệ với chính mình.",
} as const

/** 400 `errors.userId` khi người kia là chính người gọi, `null` khi không. Server kiểm TRƯỚC mọi thứ khác. */
const tuQuanHe = (otherUserId: unknown, op: keyof typeof TU_QUAN_HE) =>
  otherUserId === userId
    ? validationResponse({ userId: [TU_QUAN_HE[op]] })
    : null

/** Trạng thái đọc được của từng id kịch bản. Id lạ (kể cả `khongTonTai`) → `none`, như server. */
function relationshipOf(otherUserId: string): T.RelationshipResponse {
  switch (otherUserId) {
    case SOCIAL_SCENARIO.banBe:
      return relationship("friends", true, otherUserId)
    case SOCIAL_SCENARIO.loiMoiDen:
      return relationship("incoming", false, otherUserId)
    case SOCIAL_SCENARIO.loiMoiDi:
      return relationship("outgoing", false, otherUserId)
    default:
      return relationship("none", false, otherUserId)
  }
}

/**
 * Trang theo cursor, dùng chung cho `/friends`, `/friends/requests`, `/feed`. `CURSOR_TRANG_RONG` mở ra trang RỖNG mà
 * `nextCursor` vẫn khác null (Đ-4.9) — ca "không suy hết từ độ dài trang"; trang sau nó có dữ liệu và là trang cuối.
 */
function cursorPage<P extends { items: unknown[]; nextCursor: string | null }>(
  cursor: string | null,
  firstPage: P
): P {
  if (cursor === CURSOR_TRANG_RONG)
    return { ...firstPage, items: [], nextCursor: CURSOR_SAU_TRANG_RONG }
  return firstPage
}

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
        // Kịch bản theo dữ liệu nhập (E8 bước 2): một dung lượng cụ thể lấy về `mediaKey` mà `POST /posts`
        // coi là đã gắn vào bài khác. Server thật không nhìn `sizeBytes` để chọn key; ở đây nó chỉ là cái
        // móc để dựng nhánh 409 mà KHÔNG phải cho một `server.use` trả 409 cho mọi request.
        // CHỈ cho `purpose: "post"`: `mediaKeyDaDung` mang tiền tố `posts/`, mà `PUT /users/me/avatar` từ
        // chối mọi key không nằm dưới `avatars/{userId}/` (Đ-2.7). Áp cho cả avatar thì một file đúng 4.242
        // byte sẽ làm test avatar đỏ với câu "Ảnh này không thuộc về bạn" — nghi phạm sai hoàn toàn.
        mediaKey:
          body.purpose === "post" &&
          f.sizeBytes === MEDIA_SCENARIO.sizeBytesKeyDaDung
            ? mediaKeyDaDung
            : `${prefix}/${userId}/anh-${i}.jpg`,
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
    // BR-03: một ảnh chỉ gắn được vào MỘT bài. 409, không phải 400 — ảnh đó không dùng lại được, người
    // dùng phải chọn lại ảnh khác.
    if ((body.mediaKeys ?? []).some((m) => m.mediaKey === mediaKeyDaDung)) {
      return problemResponse(
        409,
        "Xung đột",
        "Ảnh đã được dùng trong một bài khác."
      )
    }
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

  // --- GĐ4 (E1): chín endpoint của socialgraph-v1 + `GET /feed`, tất cả qua proxy chung `/bff/api/*` (Mục 1.2 luật 2).

  http.get(
    url(`${BFF_ROUTES.api}/relationships/:userId`),
    ({ params }) =>
      tuQuanHe(params.userId, "relationship") ??
      HttpResponse.json(relationshipOf(String(params.userId)))
  ),

  http.post(url(`${BFF_ROUTES.api}/friends/requests`), async ({ request }) => {
    const body = (await request.json()) as T.CreateFriendRequest
    const tu = tuQuanHe(body.userId, "sendRequest")
    if (tu) return tu
    if (body.userId === SOCIAL_SCENARIO.khongTonTai) {
      return problemResponse(
        404,
        "Không tìm thấy tài nguyên",
        "Không tìm thấy người dùng."
      )
    }
    // Đã có lời mời theo BẤT KỲ chiều nào, hoặc đã là bạn → đụng PK → 409 (FRD-06).
    if (
      body.userId === SOCIAL_SCENARIO.daCoLoiMoi ||
      relationshipOf(body.userId).friendship !== "none"
    ) {
      return problemResponse(
        409,
        "Xung đột dữ liệu",
        "Đã có lời mời hoặc quan hệ bạn bè giữa hai người."
      )
    }
    return HttpResponse.json(
      { ...relationshipOf(body.userId), friendship: "outgoing" },
      { status: 201 }
    )
  }),

  http.post(
    url(`${BFF_ROUTES.api}/friends/requests/:userId/accept`),
    ({ params }) => {
      const tu = tuQuanHe(params.userId, "accept")
      if (tu) return tu
      const other = String(params.userId)
      // Đ-4.14: MỘT 403 cho mọi lý do — chỉ `incoming` mới chấp nhận được.
      if (relationshipOf(other).friendship !== "incoming") {
        return problemResponse(
          403,
          "Bị từ chối",
          "Bạn không có quyền thực hiện thao tác này."
        )
      }
      return HttpResponse.json({
        ...relationshipOf(other),
        friendship: "friends",
      } satisfies T.RelationshipResponse)
    }
  ),

  // Hủy (tôi gửi) và từ chối (tôi nhận) — cùng endpoint, idempotent.
  http.delete(
    url(`${BFF_ROUTES.api}/friends/requests/:userId`),
    ({ params }) =>
      tuQuanHe(params.userId, "removeRequest") ??
      new HttpResponse(null, { status: 204 })
  ),

  http.get(url(`${BFF_ROUTES.api}/friends/requests`), ({ request }) => {
    const q = new URL(request.url).searchParams
    // Server mặc định `incoming` khi thiếu — mock làm y hệt, để test của FE (không phải mock) bắt lỗi quên `direction`.
    const direction = q.get("direction") ?? "incoming"
    if (direction !== "incoming" && direction !== "outgoing") {
      return validationResponse({
        // Câu thật của `ListFriendRequestsQueryValidator.DirectionInvalid`.
        direction: ["Chiều lời mời phải là incoming hoặc outgoing."],
      })
    }
    const other =
      direction === "incoming"
        ? SOCIAL_SCENARIO.loiMoiDen
        : SOCIAL_SCENARIO.loiMoiDi
    return HttpResponse.json(
      cursorPage(q.get("cursor"), {
        items: [{ ...friendCard, user: { ...friendCard.user, userId: other } }],
        nextCursor: null,
      } satisfies T.FriendRequestPage)
    )
  }),

  http.get(url(`${BFF_ROUTES.api}/friends`), ({ request }) =>
    HttpResponse.json(
      cursorPage(new URL(request.url).searchParams.get("cursor"), friendPage)
    )
  ),

  http.delete(
    url(`${BFF_ROUTES.api}/friends/:userId`),
    ({ params }) =>
      tuQuanHe(params.userId, "unfriend") ??
      new HttpResponse(null, { status: 204 })
  ),

  http.put(url(`${BFF_ROUTES.api}/follows/:userId`), ({ params }) => {
    const tu = tuQuanHe(params.userId, "follow")
    if (tu) return tu
    if (params.userId === SOCIAL_SCENARIO.khongTonTai) {
      return problemResponse(
        404,
        "Không tìm thấy tài nguyên",
        "Không tìm thấy người dùng."
      )
    }
    return new HttpResponse(null, { status: 204 })
  }),

  // Bỏ theo dõi: idempotent. `SocialGraphErrors` không có câu tự-quan-hệ cho endpoint này — mock không bịa thêm.
  http.delete(
    url(`${BFF_ROUTES.api}/follows/:userId`),
    () => new HttpResponse(null, { status: 204 })
  ),

  http.get(url(`${BFF_ROUTES.api}/feed`), ({ request }) => {
    const cursor = new URL(request.url).searchParams.get("cursor")
    if (cursor === CURSOR_QUA_TAI) {
      // Đ-4.10: 503 là problem+json NHƯ MỌI LỖI KHÁC, có `title`/`traceId` như server. Body không phải JSON (trang HTML
      // của apache) thì `toApiError` trả `problem: null` và ca 503 xanh vì lý do sai — `http.test.ts` canh điều đó.
      // `Retry-After` có mặt như server, FE không đọc (Q-E4).
      return HttpResponse.json(
        problem(
          503,
          "Bảng tin đang quá tải",
          "Bảng tin đang có quá nhiều người truy cập. Vui lòng thử lại."
        ),
        { status: 503, headers: { ...PROBLEM_HEADERS, "Retry-After": "5" } }
      )
    }
    return HttpResponse.json(cursorPage(cursor, feedPage("network")))
  }),

  ...upstreamHandlers,
]
