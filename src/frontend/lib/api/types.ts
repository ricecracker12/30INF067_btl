// Màn KHÔNG import thẳng schema.d.ts — đi qua file này cho gọn, và để chỗ đổi tên/đổi hình
// dạng chỉ nằm một nơi. schema.d.ts là file SINH (`pnpm gen:api`), không sửa tay.
// Mỗi module một thư mục `lib/api/<nhóm>/` (Identity dời vào đây 2026-09-19, đúng kế hoạch GĐ1 khối E Mục 13).
import type { components } from "./identity/schema"

type S = components["schemas"]

export type RoleCode = S["RoleCode"]
export type UserStatus = S["UserStatus"]
export type ProblemDetails = S["ProblemDetails"]

export type RegisterRequest = S["RegisterRequest"]
export type RegisterResponse = S["RegisterResponse"]
export type VerifyEmailRequest = S["VerifyEmailRequest"]
export type VerifyEmailResponse = S["VerifyEmailResponse"]
export type LoginRequest = S["LoginRequest"]
export type TokenResponse = S["TokenResponse"]
export type MeResponse = S["MeResponse"]
