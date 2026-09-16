import type { ProblemDetails } from "./types"

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
