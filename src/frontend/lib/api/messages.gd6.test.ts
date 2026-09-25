import { describe, expect, it } from "vitest"

import { hasAnyPermission, hasPermission } from "@/lib/auth/permissions"

import { errorMessage, fieldMessage, type ErrorContext } from "./messages"
import {
  ApiError,
  confirmationOf,
  PROBLEM_TYPES,
  type ProblemType,
} from "./problem"
import type { ProblemDetails } from "./types"

// GĐ6 E1 — mười ngữ cảnh mới, mười hai `type` mới (409/503 phân nhánh theo `type`, Đ-6.21), số liệu hộp thoại xác nhận, và
// `hasPermission`. Tách file khỏi `messages.test.ts` để GĐ6 chỉ THÊM, không sửa ca cũ (nếp GĐ5).

const problem = (
  status: number,
  extra: Partial<ProblemDetails> & Record<string, unknown> = {}
) =>
  new ApiError(status, {
    type: `https://httpstatuses.io/${status}`,
    title: "t",
    status,
    traceId: "0af7651916cd43dd8448eb211c80319c",
    ...extra,
  })

const ofType = (status: number, type: ProblemType, extra = {}) =>
  problem(status, { type, ...extra })

describe("errorMessage — ngữ cảnh GĐ6", () => {
  it.each<[ErrorContext, number, string]>([
    ["report-create", 403, "Tài khoản của bạn chưa được phép báo cáo nội dung."],
    ["report-create", 404, "Nội dung này không còn nữa."],
    ["report-create", 429, "Bạn đã gửi quá nhiều báo cáo. Thử lại sau ít phút."],
    ["moderation-read", 403, "Bạn không còn quyền xem hàng đợi kiểm duyệt."],
    ["moderation-read", 404, "Không tìm thấy báo cáo."],
    ["report-decide", 403, "Bạn không có quyền đưa ra quyết định này."],
    ["report-decide", 404, "Không tìm thấy báo cáo."],
    ["moderation-restore", 403, "Bạn không có quyền khôi phục nội dung."],
    ["moderation-restore", 404, "Không tìm thấy nội dung."],
    ["admin-read", 403, "Bạn không còn quyền xem mục này."],
    ["admin-read", 404, "Không tìm thấy tài khoản."],
    ["admin-lock", 403, "Bạn không có quyền khóa hoặc mở khóa tài khoản."],
    ["admin-lock", 404, "Không tìm thấy tài khoản."],
    ["admin-role", 403, "Bạn không có quyền đổi vai trò này."],
    ["admin-role", 404, "Không tìm thấy tài khoản."],
    ["role-edit", 403, "Bạn không có quyền quản lý vai trò."],
    ["role-edit", 404, "Không tìm thấy vai trò."],
    ["notification-read", 403, "Không tìm thấy thông báo này."],
  ])("%s %i → câu riêng", (ctx, status, text) => {
    expect(errorMessage(ctx, problem(status))).toBe(text)
  })

  it("429 báo cáo KHÁC 429 chung — báo cáo có hạn mức riêng 10/phút (Đ-6.12)", () => {
    expect(errorMessage("search", problem(429))).toBe(
      "Bạn thao tác quá nhanh. Vui lòng thử lại sau ít phút."
    )
  })

  it("search 400: câu CỦA SERVER dưới errors.q (Đ-E5)", () => {
    const e = problem(400, { errors: { q: ["Nhập ít nhất 2 ký tự."] } })
    expect(fieldMessage(e, "q", "search")).toBe("Nhập ít nhất 2 ký tự.")
  })

  it("500 ở mọi ngữ cảnh mới vẫn có Mã tra cứu", () => {
    expect(errorMessage("admin-lock", problem(500))).toContain("Mã tra cứu")
  })
})

describe("errorMessage — 409/503/403 theo `type` (Đ-6.21), không theo status", () => {
  it.each<[ProblemType, number, string]>([
    [PROBLEM_TYPES.lastAdmin, 409, "Hệ thống phải còn ít nhất một quản trị viên đang hoạt động."],
    [PROBLEM_TYPES.roleInUse, 409, "Vai trò đang có người dùng, không xóa được."],
    [PROBLEM_TYPES.systemRole, 409, "Không thể thay đổi vai trò hệ thống theo cách này."],
    [PROBLEM_TYPES.roleCodeTaken, 409, "Mã vai trò đã tồn tại."],
    [PROBLEM_TYPES.reportAlreadyDecided, 409, "Báo cáo này vừa được người khác xử lý."],
    [PROBLEM_TYPES.moderationTargetGone, 409, "Nội dung bị báo cáo không còn tồn tại."],
    [PROBLEM_TYPES.moderationNotHidden, 409, "Nội dung này hiện không bị ẩn."],
    [PROBLEM_TYPES.postHidden, 409, "Bài viết đã bị ẩn do vi phạm tiêu chuẩn cộng đồng nên không sửa được."],
    [PROBLEM_TYPES.profileRequired, 403, "Bạn cần tạo hồ sơ trước khi đăng bài."],
  ])("%s", (type, status, text) => {
    expect(errorMessage("role-edit", ofType(status, type))).toBe(text)
  })

  it("ba 409 của màn quản trị ra ba câu khác nhau dù cùng status", () => {
    const texts = new Set(
      [PROBLEM_TYPES.lastAdmin, PROBLEM_TYPES.roleInUse, PROBLEM_TYPES.confirmationRequired].map(
        (t) => errorMessage("admin-role", ofType(409, t))
      )
    )
    expect(texts.size).toBe(3)
  })

  it("503 revocation-unavailable: câu 'tạm thời không khả dụng', KHÔNG Mã tra cứu — không phải lỗi hệ thống", () => {
    const msg = errorMessage(
      "admin-read",
      ofType(503, PROBLEM_TYPES.revocationUnavailable)
    )
    expect(msg).toBe(
      "Chức năng quản trị tạm thời không khả dụng. Vui lòng thử lại sau ít phút."
    )
    expect(msg).not.toContain("Mã tra cứu")
  })

  it("đăng nhập: 403 account-disabled KHÁC 403 chưa xác minh (Đ-6.5)", () => {
    expect(
      errorMessage("login", ofType(403, PROBLEM_TYPES.accountDisabled))
    ).toBe("Tài khoản đã bị khóa. Liên hệ quản trị viên.")
    expect(errorMessage("login", problem(403))).toBe(
      "Tài khoản chưa xác minh email. Vui lòng mở liên kết trong thư chúng tôi đã gửi."
    )
  })
})

describe("POST /posts — hai 403 tách bằng `type` (content-v1 1.3.0-gd6)", () => {
  it("chưa có hồ sơ ≠ thiếu quyền post.create", () => {
    expect(errorMessage("post-create", ofType(403, PROBLEM_TYPES.profileRequired))).toBe(
      "Bạn cần tạo hồ sơ trước khi đăng bài."
    )
    expect(errorMessage("post-create", problem(403))).toBe(
      "Tài khoản của bạn chưa được phép đăng bài."
    )
  })
})

describe("confirmationOf — số liệu hộp thoại xác nhận lấy NGUYÊN từ server", () => {
  it("409 confirmation-required → added/removed/affectedUsers", () => {
    const e = ofType(409, PROBLEM_TYPES.confirmationRequired, {
      added: [],
      removed: ["post.create"],
      affectedUsers: 8421,
    })
    expect(confirmationOf(e)).toEqual({
      added: [],
      removed: ["post.create"],
      affectedUsers: 8421,
    })
  })

  it("409 khác type, hoặc thiếu số liệu → null (không đoán số)", () => {
    expect(confirmationOf(ofType(409, PROBLEM_TYPES.lastAdmin))).toBeNull()
    expect(
      confirmationOf(
        ofType(409, PROBLEM_TYPES.confirmationRequired, { removed: ["x"] })
      )
    ).toBeNull()
    expect(confirmationOf(new Error("x"))).toBeNull()
  })
})

describe("hasPermission — quyền hiệu lực, không suy từ vai trò (Đ-6.11)", () => {
  const me = { permissions: ["report.resolve"] }

  it("có / không có / chưa biết me", () => {
    expect(hasPermission(me, "report.resolve")).toBe(true)
    expect(hasPermission(me, "post.hide")).toBe(false)
    expect(hasPermission(null, "report.resolve")).toBe(false)
  })

  it("any-of: một mã khớp là đủ", () => {
    expect(hasAnyPermission(me, ["user.lock", "report.resolve"])).toBe(true)
    expect(hasAnyPermission(me, ["user.lock", "audit.read"])).toBe(false)
    expect(hasAnyPermission(undefined, ["report.resolve"])).toBe(false)
  })
})
