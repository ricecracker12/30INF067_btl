// Màn KHÔNG import thẳng schema.d.ts — đi qua file này cho gọn, và để chỗ đổi tên/đổi hình
// dạng chỉ nằm một nơi. schema.d.ts là file SINH (`pnpm gen:api`), không sửa tay.
// Mỗi module một thư mục `lib/api/<nhóm>/` (Identity dời vào đây 2026-09-19, đúng kế hoạch GĐ1 khối E Mục 13).
import type { components as ContentComponents } from "./content/schema"
import type { components } from "./identity/schema"
import type { components as ProfileComponents } from "./profile/schema"

type S = components["schemas"]
type P = ProfileComponents["schemas"]
type C = ContentComponents["schemas"]

// `ProblemDetails` có ở CẢ BA schema — chỉ re-export MỘT bản (bản của identity). Ba bản giống hệt
// nhau vì cùng sinh từ `SharedKernelProblemDetailsFactory`; re-export nhiều bản là `Duplicate identifier`.
export type ProblemDetails = S["ProblemDetails"]

// --- Identity (identity-v1.yaml) ---
export type RoleCode = S["RoleCode"]
export type UserStatus = S["UserStatus"]

export type RegisterRequest = S["RegisterRequest"]
export type RegisterResponse = S["RegisterResponse"]
export type VerifyEmailRequest = S["VerifyEmailRequest"]
export type VerifyEmailResponse = S["VerifyEmailResponse"]
export type LoginRequest = S["LoginRequest"]
export type TokenResponse = S["TokenResponse"]
export type MeResponse = S["MeResponse"]

// --- Profile (profile-v1.yaml) — GĐ2 E1 ---
export type ProfileResponse = P["ProfileResponse"]
export type UpsertProfileRequest = P["UpsertProfileRequest"]
export type SetAvatarRequest = P["SetAvatarRequest"]

// --- Content (content-v1.yaml) — GĐ2 E1 ---
export type ImageContentType = C["ImageContentType"]
export type PostPrivacy = C["PostPrivacy"]
export type UploadPurpose = C["UploadPurpose"]

export type UploadFileDeclaration = C["UploadFileDeclaration"]
export type CreateUploadsRequest = C["CreateUploadsRequest"]
export type UploadTicket = C["UploadTicket"]
export type MediaKeyDeclaration = C["MediaKeyDeclaration"]

export type CreatePostRequest = C["CreatePostRequest"]
export type UpdatePostRequest = C["UpdatePostRequest"]
export type PostAuthor = C["PostAuthor"]
export type PostMedia = C["PostMedia"]
export type PostResponse = C["PostResponse"]
export type PostPage = C["PostPage"]
