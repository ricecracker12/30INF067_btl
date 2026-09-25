import { BFF_PROBLEM_TYPES } from "./bff-contract"
import type {
  AccountDisabledProblem,
  ConfirmationRequiredProblem,
  FeedOverloadedProblem,
  LastAdminProblem,
  NotFriendsProblem,
  PostHiddenProblem,
  ProblemDetails,
  ProfileRequiredProblem,
  RealtimeUnavailableProblem,
  ReportDecisionConflictProblem,
  RevocationUnavailableProblem,
  RoleConflictProblem,
  TargetNotHiddenProblem,
} from "./types"

/**
 * Mọi thứ không phải 2xx. Đọc được cả khi body không phải Problem Details — apache trả trang
 * HTML 502 lúc API đang khởi động lại, parse mù là ném `SyntaxError` giữa luồng.
 */
export class ApiError extends Error {
  constructor(
    readonly status: number,
    readonly problem: ProblemDetails | null
  ) {
    super(problem?.detail ?? problem?.title ?? `HTTP ${status}`)
    this.name = "ApiError"
  }

  get traceId(): string | undefined {
    return this.problem?.traceId
  }

  /** Lỗi theo trường của 400 — key là tên trường client gửi (email, password, token) hoặc "body". */
  get fieldErrors(): Record<string, string[]> {
    return this.problem?.errors ?? {}
  }
}

/** `fetch` ném: mất mạng, CORS sai, API tắt. KHÔNG bao giờ kích hoạt refresh (E7). */
export class NetworkError extends Error {
  constructor(message: string) {
    super(message)
    this.name = "NetworkError"
  }
}

export async function toApiError(res: Response): Promise<ApiError> {
  const isProblem = (res.headers.get("content-type") ?? "").includes("json")
  const body: unknown = isProblem ? await res.json().catch(() => null) : null
  const problem =
    body !== null &&
    typeof body === "object" &&
    typeof (body as ProblemDetails).status === "number"
      ? (body as ProblemDetails)
      : null
  return new ApiError(res.status, problem)
}

/**
 * `type` riêng của Problem Details mà FE phân nhánh theo (GĐ4 Q-E4) — cùng status, khác việc người dùng phải làm. Giá trị
 * API ràng vào kiểu SINH từ hợp đồng: yaml đổi `type` thì dòng dưới đỏ compile. Giá trị BFF lấy từ `bff-contract.ts` —
 * hợp đồng của chính BFF. So `type`, KHÔNG so `title`: `title` là nhãn hiển thị, server đổi chữ được mà không báo ai.
 */
export const PROBLEM_TYPES = {
  feedOverloaded:
    "urn:socialapp:problem:feed-overloaded" satisfies FeedOverloadedProblem["type"],
  bffSessionUnavailable: BFF_PROBLEM_TYPES.sessionUnavailable,
  // GĐ5: 403 "không còn là bạn" (thanh chỉ đọc) khác 403 "không phải thành viên"; 503 vé realtime khác 503 feed/BFF.
  notFriends:
    "urn:socialapp:problem:not-friends" satisfies NotFriendsProblem["type"],
  realtimeUnavailable:
    "urn:socialapp:problem:realtime-unavailable" satisfies RealtimeUnavailableProblem["type"],
  // GĐ6 (E1). Đăng nhập có HAI 403 (chưa xác minh / bị Admin khóa, Đ-6.5). Schema `AccountDisabledProblem` thêm vào
  // identity-v1 ngày 2026-09-25 (trước đó `type` chỉ nằm trong mô tả, dòng này là chuỗi trơn).
  accountDisabled:
    "urn:socialapp:problem:account-disabled" satisfies AccountDisabledProblem["type"],
  // `POST /posts` có HAI 403 (content-v1 `1.3.0-gd6`, 2026-09-25): chưa có hồ sơ mang `type` này; thiếu `post.create` (hay
  // `mediaKey` của người khác) giữ `type` chung.
  profileRequired:
    "urn:socialapp:problem:profile-required" satisfies ProfileRequiredProblem["type"],
  // 503 fail-closed của endpoint quản trị/kiểm duyệt (Đ-6.8) — KHÔNG đăng xuất, phần còn lại của app vẫn chạy.
  revocationUnavailable:
    "urn:socialapp:problem:revocation-unavailable" satisfies RevocationUnavailableProblem["type"],
  // 409 của màn quản trị — ba nghĩa, ba việc khác nhau (Đ-6.21). `confirmation-required` mở hộp thoại, không hiện câu.
  lastAdmin:
    "urn:socialapp:problem:last-admin" satisfies LastAdminProblem["type"],
  confirmationRequired:
    "urn:socialapp:problem:confirmation-required" satisfies ConfirmationRequiredProblem["type"],
  roleInUse:
    "urn:socialapp:problem:role-in-use" satisfies RoleConflictProblem["type"],
  systemRole:
    "urn:socialapp:problem:system-role" satisfies RoleConflictProblem["type"],
  roleCodeTaken:
    "urn:socialapp:problem:role-code-taken" satisfies RoleConflictProblem["type"],
  // 409 của màn kiểm duyệt: người khác vừa quyết (làm mới hàng đợi) ≠ đối tượng đã mất ≠ khôi phục thứ không bị ẩn.
  reportAlreadyDecided:
    "urn:socialapp:problem:report-already-decided" satisfies ReportDecisionConflictProblem["type"],
  moderationTargetGone:
    "urn:socialapp:problem:moderation-target-gone" satisfies ReportDecisionConflictProblem["type"],
  moderationNotHidden:
    "urn:socialapp:problem:moderation-not-hidden" satisfies TargetNotHiddenProblem["type"],
  // 409 tác giả sửa bài bị kiểm duyệt ẩn (Đ-6.14).
  postHidden:
    "urn:socialapp:problem:post-hidden" satisfies PostHiddenProblem["type"],
} as const

export type ProblemType = (typeof PROBLEM_TYPES)[keyof typeof PROBLEM_TYPES]

/** Lỗi từ `request()` mang đúng `type` này. `NetworkError`, lỗi lạ, hay body không phải Problem Details → `false`. */
export function hasProblemType(error: unknown, type: ProblemType): boolean {
  return error instanceof ApiError && error.problem?.type === type
}

/** Số liệu của 409 `confirmation-required` (Đ-6.9) — hộp thoại hiện ĐÚNG các số này, không tự tính (Đ-6.21). */
export type ConfirmationRequest = Pick<
  ConfirmationRequiredProblem,
  "added" | "removed" | "affectedUsers"
>

/**
 * Lỗi là 409 `confirmation-required` → số liệu server gửi; lỗi khác → `null`. Trường thiếu hay sai kiểu cũng `null`: hộp thoại
 * xác nhận với con số đoán là đúng thứ Đ-6.21 cấm — thà hiện lỗi chung còn hơn.
 */
export function confirmationOf(error: unknown): ConfirmationRequest | null {
  if (!hasProblemType(error, PROBLEM_TYPES.confirmationRequired)) return null
  const body = (error as ApiError).problem as Partial<ConfirmationRequest>
  const strings = (v: unknown): v is string[] =>
    Array.isArray(v) && v.every((x) => typeof x === "string")
  if (
    !strings(body.added) ||
    !strings(body.removed) ||
    typeof body.affectedUsers !== "number"
  )
    return null
  return {
    added: body.added,
    removed: body.removed,
    affectedUsers: body.affectedUsers,
  }
}
