import { http, HttpResponse } from "msw"

import { BFF_ROUTES, type BffSessionState } from "@/lib/api/bff-contract"
import { BFF_URL } from "@/lib/api/config"
import type * as T from "@/lib/api/types"

import {
  me,
  problem,
  registerResponse,
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

  ...upstreamHandlers,
]
