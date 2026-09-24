import { BFF_PROBLEM_TYPES } from "./bff-contract"
import type {
  FeedOverloadedProblem,
  NotFriendsProblem,
  ProblemDetails,
  RealtimeUnavailableProblem,
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
} as const

export type ProblemType = (typeof PROBLEM_TYPES)[keyof typeof PROBLEM_TYPES]

/** Lỗi từ `request()` mang đúng `type` này. `NetworkError`, lỗi lạ, hay body không phải Problem Details → `false`. */
export function hasProblemType(error: unknown, type: ProblemType): boolean {
  return error instanceof ApiError && error.problem?.type === type
}
