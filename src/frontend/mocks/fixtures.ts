// Giá trị chép từ `example` của identity-v1.yaml. `satisfies` bắt lệch HÌNH DẠNG lúc compile —
// hợp đồng đổi field là file này đỏ, không phải mock im lặng trả sai.
import type * as T from "@/lib/api/types"

export const userId = "0192f3c1-8a4e-7c31-9f2a-6b5d4e3c2a10"

export const me = {
  userId,
  email: "an.nguyen@example.com",
  role: "USER",
  roleDisplayName: "Người dùng",
  emailVerifiedAt: "2026-09-08T03:14:07Z",
  status: "active",
  createdAt: "2026-09-08T03:10:22Z",
} satisfies T.MeResponse

export const registerResponse = {
  userId,
  email: "an.nguyen@example.com",
} satisfies T.RegisterResponse

export const verifyEmailResponse = {
  email: "an.nguyen@example.com",
  verifiedAt: "2026-09-08T03:14:07Z",
} satisfies T.VerifyEmailResponse

function traceId() {
  return crypto.randomUUID().replaceAll("-", "")
}

export const problem = (status: number, title: string, detail?: string) =>
  ({
    type: `https://httpstatuses.io/${status}`,
    title,
    status,
    ...(detail && { detail }),
    traceId: traceId(),
  }) satisfies T.ProblemDetails

/** 400 của hợp đồng: `errors` là map tên trường → danh sách thông điệp. */
export const validationProblem = (errors: Record<string, string[]>) =>
  ({
    ...problem(400, "Dữ liệu không hợp lệ", "Dữ liệu đầu vào không hợp lệ"),
    errors,
  }) satisfies T.ProblemDetails
