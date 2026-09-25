// Màn KHÔNG import thẳng schema.d.ts — đi qua file này cho gọn, và để chỗ đổi tên/đổi hình
// dạng chỉ nằm một nơi. schema.d.ts là file SINH (`pnpm gen:api`), không sửa tay.
// Mỗi module một thư mục `lib/api/<nhóm>/` (Identity dời vào đây 2026-09-19, đúng kế hoạch GĐ1 khối E Mục 13).
import type { components as AdminComponents } from "./admin/schema"
import type { components as ContentComponents } from "./content/schema"
import type { components } from "./identity/schema"
import type { components as ModerationComponents } from "./moderation/schema"
import type { components as NotificationComponents } from "./notification/schema"
import type { components as ProfileComponents } from "./profile/schema"
import type { components as MessagingComponents } from "./messaging/schema"
import type { components as SocialGraphComponents } from "./socialgraph/schema"

type S = components["schemas"]
type P = ProfileComponents["schemas"]
type C = ContentComponents["schemas"]
type G = SocialGraphComponents["schemas"]
type M = MessagingComponents["schemas"]
type A = AdminComponents["schemas"]
type Mo = ModerationComponents["schemas"]
type N = NotificationComponents["schemas"]

// `ProblemDetails` có ở CẢ BỐN schema — chỉ re-export MỘT bản (bản của identity). Bốn bản giống hệt
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
/** 403 đăng nhập của tài khoản bị Admin khóa (GĐ6 Đ-6.5) — schema thêm 2026-09-25, `type` literal sinh từ hợp đồng. */
export type AccountDisabledProblem = S["AccountDisabledProblem"]

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

// --- Content, thêm ở GĐ4 E1 (`GET /feed`) ---
export type FeedMode = C["FeedMode"]
export type FeedPage = C["FeedPage"]
/** 503 feed quá tải — `type` là literal sinh từ enum của hợp đồng (Q-E4). */
export type FeedOverloadedProblem = C["FeedOverloadedProblem"]

// --- Content, thêm ở GĐ3 (bình luận + cảm xúc) ---
export type ReactionType = C["ReactionType"]
export type CommentStatus = C["CommentStatus"]
export type CreateCommentRequest = C["CreateCommentRequest"]
export type CommentResponse = C["CommentResponse"]
export type CommentPage = C["CommentPage"]
export type SetReactionRequest = C["SetReactionRequest"]
export type ReactionSummary = C["ReactionSummary"]

// --- SocialGraph (socialgraph-v1.yaml) — GĐ4 E1 ---
export type FriendshipState = G["FriendshipState"]
export type FriendRequestDirection = G["FriendRequestDirection"]
export type CreateFriendRequest = G["CreateFriendRequest"]
export type RelationshipResponse = G["RelationshipResponse"]
export type UserCard = G["UserCard"]
export type FriendCard = G["FriendCard"]
export type FriendPage = G["FriendPage"]
export type FriendRequestPage = G["FriendRequestPage"]

// --- Messaging (messaging-v1.yaml) — GĐ5 E1 ---
// `UserCard` của messaging-v1 cùng hình dạng bản socialgraph-v1 — KHÔNG re-export lần hai (Duplicate identifier); màn chat
// dùng `ConversationPeer` (= `ConversationResponse["peer"]`).
export type RealtimeTicket = M["RealtimeTicket"]
export type CreateConversation = M["CreateConversation"]
export type ConversationResponse = M["ConversationResponse"]
export type ConversationPeer = ConversationResponse["peer"]
export type ConversationPage = M["ConversationPage"]
export type UnreadCount = M["UnreadCount"]
export type SendMessageRequest = M["SendMessageRequest"]
export type MessageResponse = M["MessageResponse"]
export type MessagePage = M["MessagePage"]
export type ReceiptKind = M["ReceiptKind"]
export type ReceiptRequest = M["ReceiptRequest"]
/** 403 "không còn là bạn" (BR-09) — `type` literal sinh từ hợp đồng. */
export type NotFriendsProblem = M["NotFriendsProblem"]
/** 503 "kênh realtime không sẵn sàng" (Đ-5.9) — chuyển fallback REST. */
export type RealtimeUnavailableProblem = M["RealtimeUnavailableProblem"]

// --- GĐ6 E1 — ba hợp đồng mới + ba hợp đồng cũ mở lại chỉ-thêm ---
// `UserCard` của moderation-v1, notification-v1 cùng hình dạng bản socialgraph-v1; `RoleCode`, `UserStatus` của admin-v1 cùng bản
// identity-v1; `ReasonCode` của notification-v1 cùng tập moderation-v1 — KHÔNG re-export lần hai (Duplicate identifier).

// Profile (profile-v1 `1.1.0-gd6`): tìm người — top ≤ 20, không cursor (Đ-6.19).
export type SearchResult = P["SearchResult"]
export type SearchPage = P["SearchPage"]

// Content (content-v1 `1.1.0-gd6`): bài bị ẩn — chỉ tác giả thấy `moderation` (Đ-6.14).
export type PostModeration = C["PostModeration"]
/** 409 tác giả sửa bài bị ẩn — `type` literal sinh từ hợp đồng. */
export type PostHiddenProblem = C["PostHiddenProblem"]
/** 403 `POST /posts` khi người gọi chưa có hồ sơ — tách khỏi 403 thiếu `post.create` (content-v1 `1.3.0-gd6`, 2026-09-25). */
export type ProfileRequiredProblem = C["ProfileRequiredProblem"]

// Moderation (moderation-v1): báo cáo, hàng đợi, quyết định, khôi phục, nhật ký.
export type ReportTargetType = Mo["ReportTargetType"]
export type ReasonCode = Mo["ReasonCode"]
export type CreateReportRequest = Mo["CreateReportRequest"]
export type ReportReceipt = Mo["ReportReceipt"]
export type ReportQueueItem = Mo["ReportQueueItem"]
export type ReportQueuePage = Mo["ReportQueuePage"]
export type TargetSnapshot = Mo["TargetSnapshot"]
export type OpenReport = Mo["OpenReport"]
export type ReportHistoryEntry = Mo["ReportHistoryEntry"]
export type ReportDetail = Mo["ReportDetail"]
export type ReportDecision = Mo["ReportDecision"]
export type DecideReportRequest = Mo["DecideReportRequest"]
export type ReportDecisionResult = Mo["ReportDecisionResult"]
export type RestoreTargetRequest = Mo["RestoreTargetRequest"]
export type ModerationTargetChange = Mo["ModerationTargetChange"]
export type AuditAction = Mo["AuditAction"]
export type AuditLogItem = Mo["AuditLogItem"]
export type AuditLogPage = Mo["AuditLogPage"]
/** 409 `report-already-decided` | `moderation-target-gone` — hai `type`, hai việc khác nhau (Đ-6.21). */
export type ReportDecisionConflictProblem = Mo["ReportDecisionConflictProblem"]
export type TargetNotHiddenProblem = Mo["TargetNotHiddenProblem"]
/** 503 fail-closed của endpoint đặc quyền (Đ-6.8) — cùng `type` ở moderation-v1 và admin-v1. */
export type RevocationUnavailableProblem = A["RevocationUnavailableProblem"]

// Admin (admin-v1 — nhóm thứ hai của Identity, Đ-6.1).
export type AdminUser = A["AdminUser"]
export type AdminUserPage = A["AdminUserPage"]
export type LockRequest = A["LockRequest"]
export type AssignRoleRequest = A["AssignRoleRequest"]
export type AdminUserChange = A["AdminUserChange"]
export type RoleSummary = A["RoleSummary"]
export type CreateRoleRequest = A["CreateRoleRequest"]
export type RenameRoleRequest = A["RenameRoleRequest"]
export type SetRolePermissionsRequest = A["SetRolePermissionsRequest"]
export type PermissionInfo = A["PermissionInfo"]
export type LastAdminProblem = A["LastAdminProblem"]
/** 409 `role-code-taken` | `system-role` | `role-in-use`. */
export type RoleConflictProblem = A["RoleConflictProblem"]
/** 409 hỏi xác nhận — mang `added`, `removed`, `affectedUsers` để hộp thoại hiện ĐÚNG số server tính (Đ-6.9). */
export type ConfirmationRequiredProblem = A["ConfirmationRequiredProblem"]

// Notification (notification-v1).
export type NotificationType = N["NotificationType"]
export type NotificationTargetType = N["NotificationTargetType"]
export type NotificationTarget = N["NotificationTarget"]
export type NotificationResponse = N["NotificationResponse"]
export type NotificationPage = N["NotificationPage"]
export type ReadAllRequest = N["ReadAllRequest"]
/** `{ total }` của notification-v1 — cùng hình dạng `UnreadCount` của messaging-v1 nhưng khác nguồn: đặt tên riêng. */
export type NotificationUnreadCount = N["UnreadCount"]
